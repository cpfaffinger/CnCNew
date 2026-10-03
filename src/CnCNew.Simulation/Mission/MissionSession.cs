using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Mission;

/// <param name="WorldSeed">Seed of the logic random stream (script timers draw from it).</param>
/// <param name="MaxTicks">Hard stop for headless runs; 0 means no limit (a live mission ends with its script outcome).</param>
/// <param name="MissionAttempts">How many times the player has started this mission (the campaign progress knows; <c>MISSION_ATTEMPTS</c> conditions compare against it).</param>
/// <param name="CombatIntervalTicks">How often units look for a target in range (the stand-in for the reference's idle target acquisition).</param>
/// <param name="HumanSideName">
/// Which map side the host wants as the human viewer (<c>&lt;Local Player&gt;</c>, the multiplayer victory/defeat conditions), by side name —
/// deliberately not required to be marked <c>playerIsHuman</c> on the map, since the Generals' Challenge maps model the chosen general as an
/// ordinary side and leave naming the human one to the host. Empty (the default) keeps <see cref="MissionSession.LocalPlayerOf"/>'s own fallback:
/// the first side the map itself marks <c>playerIsHuman</c>; without this set, a map with no such side (e.g. <c>GC_LaserGeneral</c>) silently
/// defaults to player 1 — see <c>docs/SCRIPT-ENGINE.md</c> §7. A name that does not exist on the map falls back the same way, which
/// <see cref="MissionSession.Notes"/> records, never silently.
/// </param>
/// <param name="HumanFaction">
/// The faction of the human side when the host chose a persona (the Generals' Challenge general): it replaces the faction the map gives that side and, on a map without any human side (<c>GC_LaserGeneral</c>), adds one named <paramref name="HumanSideName"/> (default <c>ThePlayer</c>) — <see cref="ScriptProgram.WithHumanSide"/>. Empty keeps what the map says.
/// </param>
public sealed record MissionOptions(uint WorldSeed, ScriptDifficulty Difficulty, long MaxTicks = 0, int PresentationLatencyTicks = 0, int CombatIntervalTicks = 3,
    int MissionAttempts = 1, string HumanSideName = "", string HumanFaction = "")
{
    public static MissionOptions Standard { get; } = new(0xC0FFEE09, ScriptDifficulty.Normal);
}

/// <summary>Counters of the units' automatic combat driver.</summary>
public sealed record MissionCombatStats(long Shots, long Rejected);

/// <summary>
/// One running campaign (or scripted) mission: the world built from a map's own data, the script engine that runs the map's scripts, and the
/// combat driver that stands in for idle target acquisition (<c>AIUpdateInterface</c> in the reference; this simulation has none, the skirmish AI fires
/// explicitly and so does this driver). <see cref="Step"/> advances one logic tick: scripts, combat, world. The whole state is
/// <see cref="MissionSnapshot"/>-saveable: a restored session continues hash-identically to the original.
/// <para>
/// What is built from the map: one player per named side (relations from <c>playerAllies</c>/<c>playerEnemies</c>, everything unlisted neutral, as in the
/// reference), every pre-placed instantiable object owned by the player of its <c>originalOwner</c> team (unowned ones are neutral), named objects and
/// teams for the script engine. Objects whose template is not instantiable (<see cref="ObjectTemplate.MissingModules"/>) are counted in
/// <see cref="SkippedObjects"/>, never dropped silently.
/// </para>
/// </summary>
public sealed class MissionSession
{
    public const string FixtureId = "mission";

    private readonly Dictionary<int, ulong> _sequences = [];
    private long _shots;
    private long _rejectedShots;

    internal MissionSession(MissionOptions options, SimulationWorld world, ScriptProgram program, MapData map, ImmutableArray<string> notes)
    {
        Options = options;
        World = world;
        Program = program;
        Map = map;
        Notes = notes;
        var localPlayer = LocalPlayerOf(program, options.HumanSideName);
        if (options.HumanSideName.Length > 0 && !program.Players.Any(player => !player.Id.IsSystem && string.Equals(player.Name, options.HumanSideName, StringComparison.Ordinal)))
        {
            var fallbackName = program.Players.FirstOrDefault(player => player.Id == localPlayer)?.Name ?? localPlayer.ToString();
            Notes = [..Notes, $"requested human side '{options.HumanSideName}' is not a human side of this map; falling back to '{fallbackName}'"];
        }
        Engine = new ScriptEngine(program, world, new ScriptEngineOptions(options.Difficulty, localPlayer, options.PresentationLatencyTicks, options.MissionAttempts), Issue);
    }

    public MissionOptions Options { get; }
    public SimulationWorld World { get; }
    public ScriptProgram Program { get; }
    public MapData Map { get; }
    public ScriptEngine Engine { get; }

    /// <summary>Placed objects that were not created because their template cannot be instantiated yet, by template name (ordinal order).</summary>
    public ImmutableArray<(string Template, int Count)> SkippedObjects { get; private set; } = [];

    public ImmutableArray<string> Notes { get; private set; }
    public MissionCombatStats CombatStats => new(_shots, _rejectedShots);
    public MissionOutcome Outcome => Engine.Outcome;

    /// <summary>The mission is over once its script ended it (victory or defeat plus the end-game delay) or the tick limit passed.</summary>
    public bool IsOver => Engine.IsEnded || (Options.MaxTicks > 0 && World.Tick >= Options.MaxTicks);

    /// <summary>
    /// The human viewer: <paramref name="humanSideName"/> when it names any non-system side of the map (the host's explicit choice — deliberately
    /// not required to be <c>playerIsHuman</c>, since the Generals' Challenge maps model the chosen general as an ordinary side and leave which one
    /// is the human to the host), else the first side the map itself marks <c>playerIsHuman</c>, else player 1 (a map with no human side at all,
    /// which does not happen in practice but must still resolve to something).
    /// </summary>
    public static PlayerId LocalPlayerOf(ScriptProgram program, string humanSideName = "")
    {
        if (humanSideName.Length > 0)
            foreach (var player in program.Players)
                if (!player.Id.IsSystem && string.Equals(player.Name, humanSideName, StringComparison.Ordinal)) return player.Id;
        foreach (var player in program.Players)
            if (player.IsHuman && !player.Id.IsSystem) return player.Id;
        return new PlayerId(1);
    }

    public static MissionSession Start(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(map);
        var program = ScriptProgram.Build(map).WithHumanSide(options.HumanSideName, options.HumanFaction);
        var notes = ImmutableArray.CreateBuilder<string>();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), options.WorldSeed, ConfiguredPlayers(program, catalog, notes), FixtureId, fingerprint, terrain.Fingerprint), catalog, terrain);
        ApplyRelations(world, program);
        var session = new MissionSession(options, world, program, map, [..notes]);
        session.PlaceObjects(catalog, terrain);
        // The reference creates the map's objects synchronously while loading, so the first script evaluation already sees them.
        world.MaterializePendingSpawns();
        return session;
    }

    private static ImmutableArray<PlayerConfiguration> ConfiguredPlayers(ScriptProgram program, TemplateCatalog catalog, ImmutableArray<string>.Builder notes)
    {
        var players = ImmutableArray.CreateBuilder<PlayerConfiguration>();
        foreach (var player in program.Players)
        {
            if (player.Id.IsSystem || string.IsNullOrEmpty(player.Name)) continue;
            string? faction = null;
            if (player.Faction.Length > 0)
            {
                if (catalog.Factions.TryGet(player.Faction, out _)) faction = player.Faction;
                else notes.Add($"{player.Name}: faction '{player.Faction}' is not a playable faction definition; the player starts without faction money and sciences");
            }
            players.Add(new PlayerConfiguration(player.Id, player.Name, faction, 0, !player.IsHuman));
        }
        return players.ToImmutable();
    }

    /// <summary>
    /// <c>SidesList</c> relations: <c>playerAllies</c>/<c>playerEnemies</c> name each side's view of the others; every pair the lists do not mention is neutral
    /// (<c>Player::getRelationship</c> defaults to NEUTRAL; the world's own default would make unlisted pairs enemies).
    /// </summary>
    internal static void ApplyRelations(SimulationWorld world, ScriptProgram program)
    {
        foreach (var from in program.Players)
        {
            if (from.Id.IsSystem || !world.HasPlayer(from.Id)) continue;
            foreach (var to in program.Players)
            {
                if (to.Id.IsSystem || to.Id == from.Id || !world.HasPlayer(to.Id)) continue;
                var relationship = from.Allies.Contains(to.Name) ? Relationship.Allies : from.Enemies.Contains(to.Name) ? Relationship.Enemies : Relationship.Neutral;
                world.SetRelationship(from.Id, to.Id, relationship);
            }
        }
    }

    private void PlaceObjects(TemplateCatalog catalog, TerrainMap terrain)
    {
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var placed in Program.Objects)
        {
            if (!catalog.TryGet(placed.Template, out var template) || !template.IsInstantiable)
            {
                skipped[placed.Template] = skipped.GetValueOrDefault(placed.Template) + 1;
                continue;
            }
            var prototype = placed.TeamName.Length > 0 ? Program.FindTeam(placed.TeamName) : null;
            var owner = prototype is not null && Program.FindPlayer(prototype.Owner) is { } side && World.HasPlayer(side.Id) ? side.Id : PlayerId.System;
            var id = World.RequestSpawn(template.Index, owner, placed.X, placed.Y, placed.Z, placed.Angle, placed.HealthPercent);
            if (placed.ScriptName.Length > 0) Engine.RegisterNamed(placed.ScriptName, id);
            if (prototype is not null) Engine.AssignToTeam(prototype.Name, id);
        }
        PlaceBuildLists(catalog, skipped);
        PlaceHumanStarts(catalog, terrain, skipped);
        SkippedObjects = [..skipped.Select(pair => (pair.Key, pair.Value))];
    }

    /// <summary>
    /// <c>AIPlayer::newMap</c>: a computer player's build list entries flagged <c>InitiallyBuilt</c> stand finished at mission start (<c>buildStructureNow</c>),
    /// named by the entry's <c>buildingName</c> and at its initial health.
    /// </summary>
    private void PlaceBuildLists(TemplateCatalog catalog, SortedDictionary<string, int> skipped)
    {
        for (var side = 0; side < Map.Sides.Length && side < Program.Players.Length; side++)
        {
            var player = Program.Players[side];
            if (player.IsHuman || player.Id.IsSystem || !World.HasPlayer(player.Id)) continue;
            foreach (var entry in Map.Sides[side].BuildList)
            {
                if (!entry.InitiallyBuilt || entry.Template.Length == 0) continue;
                if (!catalog.TryGet(entry.Template, out var template) || !template.IsInstantiable)
                {
                    skipped[entry.Template] = skipped.GetValueOrDefault(entry.Template) + 1;
                    continue;
                }
                var id = World.RequestSpawn(template.Index, player.Id, Fixed.FromDecimal(entry.X), Fixed.FromDecimal(entry.Y), Fixed.Zero, Fixed.FromDecimal(entry.Angle), entry.Health);
                if (entry.BuildingName.Length > 0) Engine.RegisterNamed(entry.BuildingName, id);
            }
        }
    }

    /// <summary>
    /// A human side that owns nothing on the map but has a playable faction and a start position (the Generals' Challenge maps) starts like a skirmish player:
    /// the faction's base building on its <c>Player_N_Start</c> waypoint and the faction's start units in a ring around it. Sides that own placed objects
    /// (campaign maps) keep exactly what the map gives them.
    /// </summary>
    private void PlaceHumanStarts(TemplateCatalog catalog, TerrainMap terrain, SortedDictionary<string, int> skipped)
    {
        var starts = terrain.StartPositions;
        var humans = 0;
        foreach (var player in Program.Players)
        {
            if (!player.IsHuman || player.Id.IsSystem || !World.HasPlayer(player.Id)) continue;
            var startIndex = humans++;
            if (startIndex >= starts.Length || Program.Objects.Any(item => item.TeamName.Length > 0 && Program.FindTeam(item.TeamName)?.Owner == player.Name)) continue;
            if (!catalog.Factions.TryGet(player.Faction, out var faction) || faction.StartingBuilding.Length == 0 || !catalog.TryGet(faction.StartingBuilding, out var building) || !building.IsInstantiable)
            {
                Notes = [..Notes, $"{player.Name}: no base to start with (faction '{player.Faction}')"];
                continue;
            }
            var (bx, by) = (starts[startIndex].X, starts[startIndex].Y);
            var center = World.RequestSpawn(building.Index, player.Id, bx, by, Fixed.Zero, Fixed.Zero);
            if (Program.FindTeam("team" + player.Name) is { } startTeam) Engine.AssignToTeam(startTeam.Name, center);
            var ring = building.BoundingCircleRadius + Fixed.FromInt(30);
            for (var index = 0; index < faction.StartingUnits.Length; index++)
            {
                if (!catalog.TryGet(faction.StartingUnits[index], out var unit) || !unit.IsInstantiable)
                {
                    skipped[faction.StartingUnits[index]] = skipped.GetValueOrDefault(faction.StartingUnits[index]) + 1;
                    continue;
                }
                var angle = FixedTrig.TwoPi * index / Fixed.FromInt(Math.Max(1, faction.StartingUnits.Length));
                World.RequestSpawn(unit.Index, player.Id, bx + FixedTrig.Cos(angle) * ring, by + FixedTrig.Sin(angle) * ring, Fixed.Zero, angle);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------ stepping

    /// <summary>Advances one logic tick: scripts (may order units and change the world), the automatic combat, then the world step.</summary>
    public TickResult Step()
    {
        var tick = World.Tick;
        Engine.Update(tick);
        if (Options.CombatIntervalTicks > 0) DriveCombat(tick);
        var result = World.Step();
        Engine.Observe(result.Events);
        return result;
    }

    /// <summary>Submits a command of <paramref name="player"/> at the current tick; the per-player sequence is assigned here, so every source of commands (scripts, the human, the combat driver) shares one counter.</summary>
    public SubmitResult Issue(PlayerId player, Func<ulong, long, SimCommand> make)
    {
        var sequence = _sequences.GetValueOrDefault(player.Value) + 1;
        _sequences[player.Value] = sequence;
        return World.Submit(make(sequence, World.Tick));
    }

    /// <summary>
    /// Every armed object fires at the nearest enemy in weapon range (the idle <c>AutoAcquireEnemiesWhenIdle</c> behaviour, which the simulation's AI modules
    /// do not model). Only players that are enemies of each other shoot; neutral objects never do. Runs every <see cref="MissionOptions.CombatIntervalTicks"/>
    /// ticks, staggered by object id.
    /// </summary>
    private void DriveCombat(long tick)
    {
        var interval = Options.CombatIntervalTicks;
        List<Entity>? targets = null;
        foreach (var entity in World.Entities.OrderBy(value => value.Id.Value).ToArray())
        {
            if ((tick + entity.Id.Value) % interval != 0) continue;
            if (entity.Owner.IsSystem || !ScriptEngine.CountsAsObject(entity) || !World.IsAnyWeaponReady(entity)) continue;
            var range = World.MaxWeaponRange(entity);
            if (range <= Fixed.Zero) continue;
            targets ??= [..World.Entities.Where(value => !value.Owner.IsSystem && value.Body is not null && ScriptEngine.CountsAsObject(value)).OrderBy(value => value.Id.Value)];
            var limit = range * range;
            Entity? best = null;
            var bestPriority = int.MinValue;
            var bestDistance = Fixed.MaxValue;
            foreach (var target in targets)
            {
                if (!World.AreEnemies(entity, target)) continue;
                var dx = entity.X - target.X;
                var dy = entity.Y - target.Y;
                var distance = dx * dx + dy * dy;
                if (distance > limit) continue;
                // Highest attack priority first (scripts may give an object a priority set), nearest among equals.
                var priority = Engine.AttackPriority(entity, target);
                if (priority < bestPriority || (priority == bestPriority && distance >= bestDistance)) continue;
                // An idle unit picks its own weapon (CMD_FROM_AI); a target none of its weapons can hit and fire this tick is not worth an order.
                if (!World.ChooseWeapon(entity, target, FireOrigin.Ai).CanFire) continue;
                best = target;
                bestPriority = priority;
                bestDistance = distance;
            }
            if (best is null) continue;
            var owner = entity.Owner;
            var source = entity.Id;
            var victim = best.Id;
            if (Issue(owner, (sequence, at) => new FireWeaponCommand(owner, sequence, at, source, victim, WeaponSlot.Best, FireOrigin.Ai)).Accepted) _shots++;
            else _rejectedShots++;
        }
    }

    // ---------------------------------------------------------------------------------------------------------- state

    internal void DescribeSession(IStateWriter writer)
    {
        writer.Int("shots", _shots);
        writer.Int("rejectedShots", _rejectedShots);
        writer.BeginArray("sequences");
        foreach (var (player, sequence) in _sequences.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.Int("player", player);
            writer.UInt("sequence", sequence);
            writer.EndObject();
        }
        writer.EndArray();
    }

    internal void LoadSession(IStateReader reader)
    {
        _shots = reader.Int("shots");
        _rejectedShots = reader.Int("rejectedShots");
        _sequences.Clear();
        foreach (var item in reader.Array("sequences")) _sequences[checked((int)item.Int("player"))] = item.UInt("sequence");
    }

    /// <summary>Hash over the world and the script engine: what a saved-and-restored session must reproduce tick for tick.</summary>
    public ulong StateHash()
        => StateHashes.Combine(World.StateHash, Engine.StateHash());
}

internal static class StateHashes
{
    public static ulong Combine(ulong first, ulong second) => StateHash.Mix(StateHash.Mix(StateHash.Seed, first), second);
}
