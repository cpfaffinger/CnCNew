using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Terrain;
using CnCNew.SimTool.Survey;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// Commands around the original skirmish AI (docs/SKIRMISH-AI-ORIGINAL.md): its typed data and matches played with it. <c>ai-match</c> and <c>ai-survey</c> come here as soon as one of
/// <c>--ai</c>, <c>--ai-b</c>, <c>--difficulty</c>, <c>--difficulty-b</c> is given; without them both commands are exactly what they were (the built-in AI in its own session).
/// </summary>
internal static class OriginalAi
{
    public const string LibraryPath = "Data/Scripts/SkirmishScripts.scb";

    public static bool Requested(string[] args)
        => ManifestCommands.Option(args, "--ai") is not null || ManifestCommands.Option(args, "--ai-b") is not null
           || ManifestCommands.Option(args, "--difficulty") is not null || ManifestCommands.Option(args, "--difficulty-b") is not null;

    /// <summary><c>ai-data &lt;content&gt;</c>: what <see cref="AiData"/> reads from <c>AIData.ini</c>.</summary>
    public static int Data(string[] args, Func<string, CompiledDefinitionGraph> compile)
    {
        var data = AiData.TryBuild(compile(args[1]));
        if (data is null)
        {
            Console.Error.WriteLine("The content has no AIData block.");
            return 1;
        }
        string F(CnCNew.Simulation.Numerics.Fixed value) => value.ToDecimal().ToString("0.###", CultureInfo.InvariantCulture);
        Console.WriteLine($"AIData: StructureSeconds {F(data.StructureSeconds)}, TeamSeconds {F(data.TeamSeconds)}, Wealthy {data.Wealthy}, Poor {data.Poor}, structure rates wealthy/poor {F(data.StructuresWealthyRate)}/{F(data.StructuresPoorRate)}, " +
                          $"team rates wealthy/poor {F(data.TeamsWealthyRate)}/{F(data.TeamsPoorRate)}, TeamResourcesToStart {F(data.TeamResourcesToStart)}, MaxRecruitRadius {F(data.MaxRecruitRadius)}, " +
                          $"SkirmishBaseDefenseExtraDistance {F(data.SkirmishBaseDefenseExtraDistance)}, RotateSkirmishBases {data.RotateSkirmishBases}, SupplyCenterSafeRadius {F(data.SupplyCenterSafeRadius)}, RebuildDelayTimeSeconds {data.RebuildDelaySeconds}");
        Console.WriteLine($"valid keys not read by the AI catalogue ({data.UnreadKeys.Length}): {string.Join(", ", data.UnreadKeys)}");
        Console.WriteLine($"side infos: {data.Sides.Length}");
        foreach (var side in data.Sides)
            Console.WriteLine($"  {side.Side}: gatherers easy/normal/hard {side.GatherersEasy}/{side.GatherersNormal}/{side.GatherersHard}, base defence {side.BaseDefenseStructure}, skill sets {string.Join(" | ", side.SkillSets.Select(set => set.Length.ToString(CultureInfo.InvariantCulture)))} sciences");
        Console.WriteLine($"skirmish build lists: {data.BuildLists.Count}, {data.BuildLists.Values.Sum(list => list.Length)} structures");
        foreach (var (side, list) in data.BuildLists)
        {
            Console.WriteLine($"  {side}: {list.Length} structures, {list.Count(entry => entry.AutomaticallyBuild)} built automatically, {list.Count(entry => entry.InitiallyBuilt)} initially built, rebuilds {list.Sum(entry => entry.Rebuilds)}; " +
                              string.Join(", ", list.GroupBy(entry => entry.Template).Select(group => $"{group.Key} x{group.Count()}")));
        }
        return 0;
    }

    private static SkirmishAiKind Kind(string? text) => text switch
    {
        null or "original" => SkirmishAiKind.Original,
        "builtin" => SkirmishAiKind.Builtin,
        _ => throw new ArgumentException($"--ai takes builtin or original, not '{text}'."),
    };

    private static ScriptDifficulty Difficulty(string? text) => text switch
    {
        "easy" => ScriptDifficulty.Easy,
        null or "medium" or "normal" => ScriptDifficulty.Normal,
        "hard" or "brutal" => ScriptDifficulty.Hard,
        _ => throw new ArgumentException($"--difficulty takes easy, medium or hard, not '{text}'."),
    };

    /// <summary>
    /// The slots of a match: <c>--ai</c>/<c>--difficulty</c> describe the first player (and every other one unless <c>--ai-b</c>/<c>--difficulty-b</c> say otherwise), <c>--ai-b</c>/<c>--difficulty-b</c>
    /// every player after the first. <c>--ai</c> defaults to <c>original</c> here: the command only arrives when one of the four options is present.
    /// </summary>
    public static ImmutableArray<ScriptedSkirmishSlot> Slots(SimContext context, IReadOnlyList<string> factions, string[] args)
    {
        var first = (Kind: Kind(ManifestCommands.Option(args, "--ai")), Difficulty: Difficulty(ManifestCommands.Option(args, "--difficulty")));
        var others = (Kind: ManifestCommands.Option(args, "--ai-b") is { } kind ? Kind(kind) : first.Kind,
            Difficulty: ManifestCommands.Option(args, "--difficulty-b") is { } difficulty ? Difficulty(difficulty) : first.Difficulty);
        var participants = ReferenceManifest.Participants(context, factions);
        return [..participants.Select((participant, index) => new ScriptedSkirmishSlot(participant, index == 0 ? first.Difficulty : others.Difficulty, Kind: index == 0 ? first.Kind : others.Kind))];
    }

    public static (MapData Map, TerrainMap Terrain, MapScriptLibrary Library) Load(SimContext context, string mapPath)
    {
        MapData map;
        using (var stream = context.Content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
        MapScriptLibrary library;
        using (var stream = context.Content.OpenRead(LibraryPath)) library = SageMapDecoder.DecodeScriptLibrary(stream);
        return (map, TerrainMap.Build(map, mapPath), library);
    }

    /// <summary><c>--library-for-missing-sides</c>: the named option that is not the reference (<see cref="ScriptedSkirmishOptions.LibraryScriptsForMissingSides"/>).</summary>
    public static bool LibraryForMissingSides(string[] args) => args.Contains("--library-for-missing-sides");

    public static ScriptedSkirmishSession Start(SimContext context, MapData map, TerrainMap terrain, MapScriptLibrary library, ImmutableArray<ScriptedSkirmishSlot> slots, uint seed, long maxTicks,
        bool allMapObjects = false, bool libraryForMissingSides = false)
    {
        var data = context.AiData ?? throw new InvalidOperationException("The content has no AIData block.");
        var options = ScriptedSkirmishOptions.Standard with { Seed = seed, MaxTicks = maxTicks, AllMapObjects = allMapObjects, LibraryScriptsForMissingSides = libraryForMissingSides };
        return ScriptedSkirmishSession.Start(options, context.Catalog, context.Fingerprint, terrain, map, library, data, slots);
    }

    internal static string Describe(ScriptedSkirmishSlot slot)
        => slot.Kind == SkirmishAiKind.Builtin ? "builtin" : "original/" + slot.Difficulty.ToString().ToLowerInvariant();

    /// <summary>The line the surveys write for one match played in a <see cref="ScriptedSkirmishSession"/>.</summary>
    public static SurveyRow Survey(SimContext context, string id, string mapPath, string[] factions, uint seed, long maxTicks, string[] args)
    {
        var (map, terrain, library) = Load(context, mapPath);
        var slots = Slots(context, factions, args);
        var session = Start(context, map, terrain, library, slots, seed, maxTicks, libraryForMissingSides: LibraryForMissingSides(args));
        long[] barracks = [-1, -1], attack = [-1, -1];
        var world = session.World;
        var probe = world.Time.OriginalFrames(15);
        while (!session.IsOver)
        {
            session.Step();
            if (world.Tick % probe != 0) continue;
            for (var side = 0; side < 2; side++)
            {
                var owner = ScriptedSkirmishSession.PlayerOf(side);
                var enemy = ScriptedSkirmishSession.PlayerOf(1 - side);
                if (barracks[side] < 0)
                    foreach (var entity in world.EntitiesOwnedBy(owner).AsSpan())
                        if (!entity.EffectivelyDead && entity.Template.IsKindOf("FS_BARRACKS") && !entity.HasStatus(ObjectStatusBits.UnderConstruction)) { barracks[side] = world.Tick; break; }
                // First attack: an own combat unit within 400 of an enemy structure (the built-in AI's survey column counts its own attack waves; this is the comparable observation).
                if (attack[side] >= 0) continue;
                foreach (var unit in world.EntitiesOwnedBy(owner).AsSpan())
                {
                    if (unit.EffectivelyDead || unit.Template.IsKindOf("STRUCTURE") || unit.Template.IsKindOf("DOZER") || unit.Template.IsKindOf("HARVESTER")) continue;
                    foreach (var structure in world.EntitiesOwnedBy(enemy).AsSpan())
                    {
                        if (structure.EffectivelyDead || !structure.Template.IsKindOf("STRUCTURE")) continue;
                        var dx = (structure.X - unit.X).Floor();
                        var dy = (structure.Y - unit.Y).Floor();
                        if (dx * dx + dy * dy > 400L * 400L) continue;
                        attack[side] = world.Tick;
                        break;
                    }
                    if (attack[side] >= 0) break;
                }
            }
        }
        var rate = context.Catalog.Rate.TicksPerSecond;
        var slot = session.Winner is { } winner ? winner.Value : 0;
        var endTick = slot == 0 ? session.EndTick : session.VictoryTick;
        var missing = session.Engine.MissingActionHits.Values.Sum() + session.Engine.MissingConditionHits.Values.Sum();
        if (missing > 0) throw new InvalidOperationException($"{missing.ToString(CultureInfo.InvariantCulture)} executions of script types without implementation: " +
                                                             string.Join(", ", session.Engine.MissingActionHits.Concat(session.Engine.MissingConditionHits).Select(pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}")));
        return new SurveyRow(id, mapPath, factions[0], factions[1], seed, rate, slot == 0 ? SurveyRow.Stalemate : SurveyRow.Winner, slot == 0 ? "-" : factions[slot - 1], slot,
            endTick, endTick * 1000 / rate, session.EndTick, session.Engine.ExecutedActions, session.StateHash().ToString("X16", CultureInfo.InvariantCulture),
            barracks[0], barracks[1], attack[0], attack[1]);
    }

    /// <summary>
    /// <c>ai-match &lt;content&gt; --map M --factions A,B[,..] --ai original|builtin --difficulty easy|medium|hard [--ai-b ..] [--difficulty-b ..] [--seed N] [--max-ticks N] [--trace N] [--no-verify]</c>:
    /// a match in a <see cref="ScriptedSkirmishSession"/>, then a second fresh run and a save/restore continuation that must give the same hashes.
    /// </summary>
    public static int Match(string[] args)
    {
        var mapPath = ManifestCommands.Option(args, "--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.");
        var factions = (ManifestCommands.Option(args, "--factions") ?? throw new ArgumentException("--factions A,B is required with --ai/--difficulty."))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seed = ManifestCommands.Option(args, "--seed") is { } text ? uint.Parse(text, CultureInfo.InvariantCulture) : ScriptedSkirmishOptions.Standard.Seed;
        var maxTicks = ToolRate.Budget(args, "--max-ticks", ScriptedSkirmishOptions.Standard.MaxTicks);
        var trace = ManifestCommands.Option(args, "--trace") is { } every ? long.Parse(every, CultureInfo.InvariantCulture) : 0;
        var watch = Stopwatch.StartNew();
        var context = SimContext.Load(args[1], ToolRate.Current);
        var (map, terrain, library) = Load(context, mapPath);
        var slots = Slots(context, factions, args);
        Console.WriteLine($"Original-AI match on {mapPath} ({terrain.StartPositions.Length} start positions), seed {seed}, definitions {context.Fingerprint[..16]}, loaded in {watch.Elapsed.TotalSeconds:F1} s.");
        for (var index = 0; index < slots.Length; index++) Console.WriteLine($"  player {index + 1}: {slots[index].Participant.Faction.Name} ({Describe(slots[index])})");

        watch.Restart();
        var allObjects = args.Contains("--all-map-objects");
        var session = Start(context, map, terrain, library, slots, seed, maxTicks, allObjects, LibraryForMissingSides(args));
        foreach (var note in session.Notes.Take(12)) Console.WriteLine($"  note: {note}");
        if (session.Notes.Length > 12) Console.WriteLine($"  ... {session.Notes.Length - 12} more notes");
        // The session hash covers the whole script engine state; it is taken every HashEvery ticks (and at the end), which is what the two check runs compare.
        const int HashEvery = 30;
        var hashes = new List<ulong>();
        byte[]? saved = null;
        long savedAt = -1;
        var verify = !args.Contains("--no-verify");
        var saveTick = maxTicks / 4;
        var world = session.World;
        var profile = args.Contains("--profile") ? new OriginalAiProfile() : null;
        if (profile is not null)
        {
            world.PhaseObserver = profile;
            session.Engine.Observer = profile;
        }
        while (!session.IsOver)
        {
            if (verify && world.Tick == saveTick && slots.All(slot => slot.Kind == SkirmishAiKind.Original))
            {
                saved = session.Save();
                savedAt = hashes.Count;
            }
            session.Step();
            if (verify && (world.Tick % HashEvery == 0 || session.IsOver)) hashes.Add(session.StateHash());
            if (trace > 0 && world.Tick % trace == 0) Trace(session);
        }
        var seconds = watch.Elapsed.TotalSeconds;
        Console.WriteLine($"Played {world.Tick} ticks in {seconds:F1} s ({seconds * 1_000_000 / Math.Max(1, world.Tick):F0} us per tick); {world.Entities.Count()} entities at the end.");
        profile?.Print(world.Tick);
        if (profile is not null)
            Console.WriteLine("  map objects (not owned by a slot): " + string.Join(", ", world.Entities.Where(entity => entity.Owner.Value < 1 || entity.Owner.Value > slots.Length)
                .GroupBy(entity => entity.Template.Name).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(40).Select(group => $"{group.Key} x{group.Count()}")));
        Report(session, factions);

        var ok = true;
        if (verify)
        {
            watch.Restart();
            var second = Start(context, map, terrain, library, slots, seed, maxTicks, allObjects, LibraryForMissingSides(args));
            var index = 0;
            var identical = true;
            while (!second.IsOver)
            {
                second.Step();
                if (second.World.Tick % HashEvery != 0 && !second.IsOver) continue;
                if (index >= hashes.Count || hashes[index] != second.StateHash()) { identical = false; break; }
                index++;
            }
            identical &= index == hashes.Count;
            Console.WriteLine($"Run 2 (fresh): state hashes identical at {index} of {hashes.Count} sample points (every {HashEvery} ticks): {identical} ({watch.Elapsed.TotalSeconds:F1} s).");
            ok &= identical;
            if (saved is not null)
            {
                var restored = ScriptedSkirmishSession.Restore(saved, context.Catalog, context.Fingerprint, terrain, map, library, context.AiData!);
                var at = (int)savedAt;
                var continued = true;
                while (!restored.IsOver)
                {
                    restored.Step();
                    if (restored.World.Tick % HashEvery != 0 && !restored.IsOver) continue;
                    if (at >= hashes.Count || hashes[at] != restored.StateHash()) { continued = false; break; }
                    at++;
                }
                continued &= at == hashes.Count;
                Console.WriteLine($"Save at tick {saveTick} ({saved.Length} bytes), restore and continue: identical at {at - savedAt} sample points: {continued}.");
                ok &= continued;
            }
            else Console.WriteLine("Save/restore not checked (a slot is played by the built-in AI, whose state cannot be saved, or the match ended before the save tick).");
        }
        return ok ? 0 : 1;
    }

    private static void Report(ScriptedSkirmishSession session, string[] factions)
    {
        var world = session.World;
        var engine = session.Engine;
        Console.WriteLine(session.Winner is { } winner
            ? $"Winner: player {winner.Value} ({factions[winner.Value - 1]}) at tick {session.VictoryTick} ({ToolRate.Minutes(session.VictoryTick)})."
            : $"No winner within {session.EndTick} ticks (stalemate).");
        Console.WriteLine($"Final state hash {session.StateHash():X16}, world hash {world.StateHash:X16}; scripts executed {engine.ExecutedScripts}, actions executed {engine.ExecutedActions}, forwarded {engine.ForwardedActions}.");
        for (var index = 0; index < session.Slots.Length; index++)
        {
            var player = ScriptedSkirmishSession.PlayerOf(index);
            var own = world.EntitiesOwnedBy(player).AsSpan().ToArray().Where(entity => !entity.EffectivelyDead).ToArray();
            var structures = own.Where(entity => entity.Template.IsKindOf("STRUCTURE")).ToArray();
            var list = engine.AiBuildListOf(player);
            Console.WriteLine($"  player {index + 1} {factions[index]} ({Describe(session.Slots[index])}): {(world.IsPlayerDefeated(player) ? "defeated" : "alive")}, {own.Length} objects ({structures.Length} structures), " +
                              $"${(long)world.MoneyOf(player).Floor()}, build list {list.Count(entry => entry.ObjectId != 0)}/{list.Length} standing, enemy {(engine.AiEnemyOf(player) is { } enemy ? "player " + enemy.Value : "-")}");
            // Where the teams are: name, members, centre, how many move, distance to the nearest enemy structure.
            var enemyStructures = world.Entities.Where(entity => world.AreEnemies(player, entity.Owner) && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE")).ToArray();
            foreach (var team in engine.TeamInstances.Where(team => team.Owner == player && team.Members.Count > 0).OrderBy(team => team.Prototype, StringComparer.Ordinal))
            {
                var members = team.Members.Select(id => world.FindEntity(new CnCNew.Simulation.Ids.EntityId(id))).Where(entity => entity is { EffectivelyDead: false }).Select(entity => entity!).ToArray();
                if (members.Length == 0 || members.All(member => member.Template.IsKindOf("STRUCTURE"))) continue;
                var cx = (long)members.Average(member => (double)member.X.Floor());
                var cy = (long)members.Average(member => (double)member.Y.Floor());
                var nearest = enemyStructures.Length == 0 ? -1 : (long)enemyStructures.Min(structure => Math.Sqrt(Math.Pow((double)(structure.X.Floor() - cx), 2) + Math.Pow((double)(structure.Y.Floor() - cy), 2)));
                Console.WriteLine($"    team {team.Prototype}{(team.Created ? "" : " (inactive)")}: {members.Length} at ({cx},{cy}), moving {members.Count(member => member.HasMoveTarget)}, attack-moving {members.Count(world.IsAttackMoving)}, nearest enemy structure {nearest}");
            }
            var pending = engine.AiPendingBuilds(player);
            if (pending.Length > 0) Console.WriteLine($"    asked for and not standing: {string.Join("; ", pending)}");
            Console.WriteLine($"    {string.Join(", ", own.GroupBy(entity => entity.Template.Name).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} x{group.Count()}"))}");
        }
        var boxes = world.Entities.Sum(entity => (long)CnCNew.Simulation.Views.EntityPresentationView.WarehouseSupplyBoxes(entity));
        var disbanded = engine.Diagnostics.Where(pair => pair.Key.Contains("disbanded: build time expired", StringComparison.Ordinal)).Sum(pair => pair.Value);
        Console.WriteLine($"Supply boxes left in the map's warehouses: {boxes} (worth {boxes * world.ValuePerSupplyBox}); teams disbanded because their build time ran out: {disbanded}.");
        var missing = engine.MissingActionHits.Concat(engine.MissingConditionHits).ToArray();
        Console.WriteLine(missing.Length == 0
            ? "Script types without implementation hit at run time: 0."
            : $"Script types without implementation hit at run time: {string.Join(", ", missing.Select(pair => $"{pair.Key}={pair.Value}"))}.");
        Console.WriteLine($"Diagnostics ({engine.Diagnostics.Count} kinds, {engine.Diagnostics.Values.Sum()} in total), most frequent first:");
        foreach (var (message, count) in engine.Diagnostics.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(25)) Console.WriteLine($"    {count,8} {message}");
        Console.WriteLine($"Rejections by reason: {string.Join(", ", Enum.GetValues<CommandRejection>().Where(reason => world.RejectionCounts[(int)reason] > 0).Select(reason => $"{reason}={world.RejectionCounts[(int)reason]}"))}");
    }

    private static void Trace(ScriptedSkirmishSession session)
    {
        var world = session.World;
        var line = new System.Text.StringBuilder($"  trace {world.Tick,6}:");
        for (var index = 0; index < session.Slots.Length; index++)
        {
            var player = ScriptedSkirmishSession.PlayerOf(index);
            int structures = 0, harvesters = 0, dozers = 0, army = 0;
            foreach (var entity in world.EntitiesOwnedBy(player).AsSpan())
            {
                if (entity.EffectivelyDead) continue;
                if (entity.Template.IsKindOf("STRUCTURE")) structures++;
                else if (entity.Template.IsKindOf("DOZER")) dozers++;
                else if (entity.Template.IsKindOf("HARVESTER")) harvesters++;
                else army++;
            }
            var teams = session.Engine.TeamInstances.Count(team => team.Owner == player && team.Created && team.Members.Count > 0);
            line.Append(CultureInfo.InvariantCulture, $" | P{index + 1} ${(long)world.MoneyOf(player).Floor(),6} bld {structures,2} army {army,3} harv {harvesters,2} doz {dozers} teams {teams,2}");
        }
        Console.WriteLine(line.ToString());
    }
}
