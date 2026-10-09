using System.Diagnostics;
using System.Globalization;
using System.Text;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>ai-census</c>: the pairings of <c>ai-survey</c>, played once per seed and observed instead of only scored. Per match and player it writes what the built-in AI
/// built and produced (by producing structure), what every look of its army production ended with, its money over time, the damage its units dealt by attacker type and
/// victim kind, its losses, and the units that walked a lot and got nowhere (<see cref="OrbitDetector"/>). Observation only: nothing is written to the world, the matches are
/// the ones <c>ai-match --seed N</c> plays.
/// </summary>
internal static class AiCensus
{
    private const string UsageText = """
        Usage: CnCNew.SimTool ai-census <content-dir> --out <tsv> [--orbits <txt>] [--seeds N] [--first-seed N] [--tick-rate 30|60|120] [--minutes M] [--pairings all|<id>,<id>] [--jobs N]
               [--composition uniform|exchange|default,uniform|exchange|default] [--watch <entity id>]
               [--ai original [--difficulty easy|medium|hard] [--ai-b ..] [--difficulty-b ..]]
          plays every AI-vs-AI pairing of the reference manifest once per seed (default 5 seeds from 1, 25 game minutes) and writes one line per observed quantity:
          pairing, seed, slot, faction, section, key, value. Sections: match, built (first completion tick per structure), produced (producer|unit), army (outcome of every
          look of the army production, producer|outcome), rejections, money (every 50 s), damage (attacker|victim kind), lost (unit), circling (orbit|shuffle per unit type and place: rally, goal-near, goal-far, no-order).
          --orbits writes one line per circling episode with the unit's state and its neighbours.
          With --ai/--difficulty the matches are played by the original skirmish AI and the sections are the ones of OriginalAiCensus (times in game seconds):
          match (first barracks, war factory and attack, attack waves, teams, units, money, powers), built, produced10, power, money.
        """;

    /// <summary>Money is sampled every this many logic frames of the original (50 s).</summary>
    private const long MoneySampleFrames = 1500;

    public static int Run(string[] args)
    {
        var outPath = ManifestCommands.Option(args, "--out");
        if (args.Length < 2 || outPath is null) return ManifestCommands.Fail(UsageText);
        var directory = args[1];
        var seeds = int.Parse(ManifestCommands.Option(args, "--seeds") ?? "5", CultureInfo.InvariantCulture);
        var firstSeed = uint.Parse(ManifestCommands.Option(args, "--first-seed") ?? "1", CultureInfo.InvariantCulture);
        var minutes = long.Parse(ManifestCommands.Option(args, "--minutes") ?? "25", CultureInfo.InvariantCulture);
        if (seeds < 1 || minutes < 1) return ManifestCommands.Fail("--seeds and --minutes must be at least 1.");
        var jobs = ManifestCommands.Jobs(args);
        var rate = ToolRate.Current;
        var maxTicks = checked(minutes * 60 * rate.TicksPerSecond);
        var pairings = ReferenceManifest.AiPairingRuns().OrderBy(pairing => pairing.Id, StringComparer.Ordinal).ToArray();
        if (ManifestCommands.Option(args, "--pairings") is { } only && only != "all")
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = wanted.Where(id => pairings.All(pairing => pairing.Id != id)).ToArray();
            if (unknown.Length > 0 || wanted.Length == 0) return ManifestCommands.Fail($"Unknown pairing id(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", pairings.Select(pairing => pairing.Id))}.");
            pairings = [..pairings.Where(pairing => wanted.Contains(pairing.Id, StringComparer.Ordinal))];
        }
        var content = ContentFileSystem.Open(directory);
        foreach (var pairing in pairings)
            if (!content.Exists(pairing.Map)) throw new FileNotFoundException($"The map {pairing.Map} is not in the content.");

        var queue = pairings.SelectMany(pairing => Enumerable.Range(0, seeds).Select(index => (Pairing: pairing, Seed: checked(firstSeed + (uint)index)))).ToArray();
        var results = new MatchCensus?[queue.Length];
        var next = -1;
        var done = 0;
        var failed = 0;
        var gate = new object();
        var total = Stopwatch.StartNew();
        Console.Error.WriteLine($"{queue.Length} matches ({pairings.Length} pairings x {seeds} seeds from {firstSeed.ToString(CultureInfo.InvariantCulture)}) at {rate}, budget {maxTicks.ToString(CultureInfo.InvariantCulture)} ticks, {jobs} side by side.");
        var threads = Enumerable.Range(0, Math.Min(jobs, queue.Length)).Select(worker => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= queue.Length) return;
                var (pairing, seed) = queue[index];
                var watch = Stopwatch.StartNew();
                MatchCensus? census = null;
                try
                {
                    context ??= SimContext.Load(directory, rate);
                    if (OriginalAi.Requested(args))
                    {
                        // The original AI in its own session: the same line format, its own sections (OriginalAiCensus).
                        var (outcome, lines) = OriginalAiCensus.Play(context, pairing.Id, pairing.Map, pairing.Factions, seed, maxTicks, args);
                        census = new MatchCensus(outcome, lines, []);
                    }
                    else census = Play(context, pairing.Id, pairing.Map, pairing.Factions, seed, maxTicks, args);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    lock (gate)
                    {
                        failed++;
                        Console.Error.WriteLine($"FAILED {pairing.Id} seed {seed.ToString(CultureInfo.InvariantCulture)}: {exception.GetType().Name}: {exception.Message}");
                    }
                }
                lock (gate)
                {
                    results[index] = census;
                    Console.Error.WriteLine($"[{++done,4}/{queue.Length}] {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s\t{pairing.Id}\t{seed.ToString(CultureInfo.InvariantCulture)}\t{census?.Outcome ?? "failed"}");
                }
            }
        }, 64 * 1024 * 1024) { IsBackground = true, Name = $"census-worker-{worker}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        var text = new StringBuilder("pairing\tseed\tslot\tfaction\tsection\tkey\tvalue\n");
        var orbits = new StringBuilder();
        foreach (var census in results)
        {
            if (census is null) continue;
            foreach (var line in census.Lines) text.Append(line).Append('\n');
            foreach (var line in census.Episodes) orbits.Append(line).Append('\n');
        }
        File.WriteAllText(Path.GetFullPath(outPath), text.ToString(), new UTF8Encoding(false));
        if (ManifestCommands.Option(args, "--orbits") is { } orbitPath) File.WriteAllText(Path.GetFullPath(orbitPath), orbits.ToString(), new UTF8Encoding(false));
        Console.Error.WriteLine($"Computed in {total.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
        Console.WriteLine($"Census written: {queue.Length} matches at {rate}, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private sealed record MatchCensus(string Outcome, List<string> Lines, List<string> Episodes);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string VictimKind(CnCNew.Simulation.Templates.ObjectTemplate template)
        => template.IsKindOf("STRUCTURE") ? "structure" : template.IsKindOf("AIRCRAFT") ? "aircraft" : template.IsKindOf("INFANTRY") ? "infantry" : template.IsKindOf("VEHICLE") ? "vehicle" : "other";

    private static MatchCensus Play(SimContext context, string id, string mapPath, string[] factions, uint seed, long maxTicks, string[] args)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var options = SkirmishMatchOptions.Standard with { MaxTicks = maxTicks, AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 };
        var participants = ReferenceManifest.Participants(context, factions);
        var compositions = AiSurvey.Compositions(args);
        var session = SkirmishMatchSession.Start(options, context.Catalog, context.Fingerprint, terrain, map.Objects,
            [..participants.Select((participant, index) => new SkirmishSlot(participant, Composition: compositions is not null && index < compositions.Length ? compositions[index] : null))]);
        var world = session.World;
        var slotOf = new Dictionary<int, int>();
        for (var slot = 0; slot < session.Players.Length; slot++) slotOf[session.Players[slot].Id.Value] = slot;
        var counts = new SortedDictionary<(int Slot, string Section, string Key), long>();
        void Add(int slot, string section, string key, long value) => counts[(slot, section, key)] = counts.GetValueOrDefault((slot, section, key)) + value;
        void First(int slot, string section, string key, long value) { if (!counts.ContainsKey((slot, section, key))) counts[(slot, section, key)] = value; }

        // What an id was, for events that arrive after the object is gone (a projectile's launcher, a dead attacker).
        var known = new Dictionary<uint, (CnCNew.Simulation.Templates.ObjectTemplate Template, int Owner)>();
        var launcherOf = new Dictionary<uint, uint>();
        var damage = new Dictionary<(int Slot, string Key), decimal>();
        var detector = new OrbitDetector(world, session, factions, id, seed) { Watch = uint.Parse(ManifestCommands.Option(args, "--watch") ?? "0", CultureInfo.InvariantCulture) };
        var moneyEvery = world.Time.OriginalFrames(MoneySampleFrames);

        (CnCNew.Simulation.Templates.ObjectTemplate Template, int Owner)? Identify(EntityId entityId)
        {
            if (!entityId.IsValid) return null;
            if (world.FindEntity(entityId) is { } entity)
            {
                var entry = (entity.Template, entity.Owner.Value);
                known[entityId.Value] = entry;
                return entry;
            }
            return known.TryGetValue(entityId.Value, out var remembered) ? remembered : null;
        }

        while (!session.IsOver)
        {
            var result = session.Step();
            foreach (var item in result.Events)
            {
                switch (item.Kind)
                {
                    case SimEventKind.EntitySpawned:
                        Identify(item.Entity);
                        break;
                    case SimEventKind.ProjectileLaunched:
                        if (item.Entity.IsValid && item.Other.IsValid) launcherOf[item.Entity.Value] = item.Other.Value;
                        Identify(item.Other);
                        break;
                    case SimEventKind.ConstructionCompleted:
                        if (Identify(item.Entity) is { } built && slotOf.TryGetValue(built.Owner, out var builtSlot)) First(builtSlot, "built", built.Template.Name, item.Tick);
                        break;
                    case SimEventKind.ProductionCompleted:
                        if (Identify(item.Entity) is { } producer && slotOf.TryGetValue(producer.Owner, out var producerSlot))
                            Add(producerSlot, "produced", producer.Template.Name + "|" + context.Catalog[(int)item.Value].Name, 1);
                        break;
                    case SimEventKind.EntityDamaged:
                    {
                        var sourceId = item.Other;
                        if (sourceId.IsValid && launcherOf.TryGetValue(sourceId.Value, out var launcher)) sourceId = new EntityId(launcher);
                        if (Identify(sourceId) is not { } attacker || Identify(item.Entity) is not { } victim || !slotOf.TryGetValue(attacker.Owner, out var attackerSlot) || attacker.Owner == victim.Owner) break;
                        var key = attacker.Template.Name + "|" + VictimKind(victim.Template);
                        damage[(attackerSlot, key)] = damage.GetValueOrDefault((attackerSlot, key)) + CnCNew.Simulation.Numerics.Fixed.FromRaw(item.Value).ToDecimal();
                        break;
                    }
                    case SimEventKind.EntityDied:
                        if (Identify(item.Entity) is { } dead && slotOf.TryGetValue(dead.Owner, out var deadSlot) && !dead.Template.IsKindOf("PROJECTILE")) Add(deadSlot, "lost", dead.Template.Name, 1);
                        break;
                }
            }
            detector.AfterTick();
            if (world.Tick % moneyEvery == 0)
                for (var slot = 0; slot < session.Players.Length; slot++)
                    counts[(slot, "money", "t" + world.Tick.ToString("D6", CultureInfo.InvariantCulture))] = (long)world.MoneyOf(session.Players[slot].Id).Floor();
        }

        foreach (var ((slot, key), value) in damage) counts[(slot, "damage", key)] = (long)value;
        foreach (var ((slot, key), value) in detector.Counts) counts[(slot, "circling", key)] = value;
        var winner = session.Winner is { } winnerId ? slotOf[winnerId.Value] + 1 : 0;
        var lines = new List<string>();
        for (var slot = 0; slot < session.Players.Length; slot++)
        {
            counts[(slot, "match", "winnerSlot")] = winner;
            counts[(slot, "match", "endTick")] = winner == 0 ? world.Tick : session.VictoryTick;
            counts[(slot, "match", "unitTicks")] = detector.UnitTicks[slot];
            if (session.Ais[slot] is not { } ai) continue;
            counts[(slot, "match", "waves")] = ai.Stats.Waves;
            counts[(slot, "match", "productions")] = ai.Stats.Productions;
            counts[(slot, "match", "informedDraws")] = ai.Stats.InformedDraws;
            counts[(slot, "match", "uninformedDraws")] = ai.Stats.UninformedDraws;
            foreach (var (key, value) in ai.Stats.ArmyProduction) counts[(slot, "army", key.Replace('\t', '|'))] = value;
            foreach (var (reason, value) in ai.Stats.RejectionsByReason) counts[(slot, "rejections", reason.ToString())] = value;
        }
        foreach (var ((slot, section, key), value) in counts)
            lines.Add(string.Join('\t', id, seed.ToString(CultureInfo.InvariantCulture), Text(slot + 1), factions[slot], section, key, Text(value)));
        return new MatchCensus(winner == 0 ? "stalemate" : factions[winner - 1], lines, detector.Episodes);
    }
}

/// <summary>
/// Finds ground units that travel a lot and get nowhere. Every logic frame of the original the position of every mobile ground unit of a player is sampled; once a second a
/// unit's last <see cref="WindowFrames"/> frames are judged: the length of the path it walked, its net displacement, and the full turns of its direction of travel. A unit with
/// more than <see cref="MinPath"/> of path and less than <see cref="MaxDisplacement"/> of displacement is an episode: an <c>orbit</c> with at least <see cref="MinTurns"/> full
/// turns, otherwise a <c>shuffle</c> (back and forth). An episode is counted once and again only after the unit stopped qualifying. Reads the world only.
/// </summary>
internal sealed class OrbitDetector
{
    public const int WindowFrames = 300;
    public const double MinPath = 150;
    public const double MaxDisplacement = 20;
    public const double MinTurns = 2;
    private const double NeighbourRadius = 30;
    /// <summary>"Close to its goal" and "at the rally point" for the place of an episode: 40 units.</summary>
    private const double GoalNear = 40;
    private const int MaxEpisodesPerMatch = 40;

    private sealed class Track
    {
        public readonly double[] X = new double[WindowFrames + 1];
        public readonly double[] Y = new double[WindowFrames + 1];
        public int Count;
        public int Head;
        public bool InEpisode;
        public long LastSeenFrame;
    }

    private readonly SimulationWorld _world;
    private readonly SkirmishMatchSession _session;
    private readonly string[] _factions;
    private readonly string _pairing;
    private readonly uint _seed;
    private readonly long _frameTicks;
    private readonly Dictionary<uint, Track> _tracks = [];
    private long _frame;

    public OrbitDetector(SimulationWorld world, SkirmishMatchSession session, string[] factions, string pairing, uint seed)
    {
        _world = world;
        _session = session;
        _factions = factions;
        _pairing = pairing;
        _seed = seed;
        _frameTicks = world.Time.OriginalFrames(1);
        UnitTicks = new long[session.Players.Length];
    }

    public SortedDictionary<(int Slot, string Key), long> Counts { get; } = [];
    public List<string> Episodes { get; } = [];
    /// <summary>Sampled unit frames per slot: the denominator of an episode rate.</summary>
    public long[] UnitTicks { get; }

    private static bool Watched(Entity entity)
        => entity.Template.Locomotor is not null && !entity.EffectivelyDead && !entity.ContainedBy.IsValid && !entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("PROJECTILE")
           && !entity.Template.IsKindOf("AIRCRAFT");

    private static double Number(CnCNew.Simulation.Numerics.Fixed value) => (double)value.ToDecimal();

    /// <summary><c>--watch &lt;entity id&gt;</c>: this unit's position, heading, speed and move target every ten frames, to the error stream (one match only makes sense).</summary>
    public uint Watch { get; init; }

    public void AfterTick()
    {
        if (_world.Tick % _frameTicks != 0) return;
        _frame++;
        if (Watch != 0 && _frame % 10 == 0 && _world.FindEntity(new CnCNew.Simulation.Ids.EntityId(Watch)) is { } watched)
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"watch {_world.Tick}: {watched.Template.Name} at {Number(watched.X):F1},{Number(watched.Y):F1} angle {Number(watched.Angle):F2} speed {Number(watched.Speed):F2} moving {watched.HasMoveTarget} target {Number(watched.MoveTargetX):F1},{Number(watched.MoveTargetY):F1} attack-move {_world.IsAttackMoving(watched)} contained {watched.ContainedBy.Value}"));
        var judge = _frame % 30 == 0;
        for (var slot = 0; slot < _session.Players.Length; slot++)
        {
            var owned = _world.EntitiesOwnedBy(_session.Players[slot].Id);
            for (var index = 0; index < owned.Count; index++)
            {
                var entity = owned[index];
                if (!Watched(entity)) continue;
                UnitTicks[slot]++;
                if (!_tracks.TryGetValue(entity.Id.Value, out var track)) _tracks[entity.Id.Value] = track = new Track();
                // A gap (the unit rode in a transport) starts the window again.
                if (track.LastSeenFrame != _frame - 1) track.Count = 0;
                track.LastSeenFrame = _frame;
                track.X[track.Head] = Number(entity.X);
                track.Y[track.Head] = Number(entity.Y);
                track.Head = (track.Head + 1) % track.X.Length;
                if (track.Count < track.X.Length) track.Count++;
                if (judge && track.Count == track.X.Length) Judge(slot, entity, track);
            }
        }
        if (judge && _frame % 900 == 0)
            foreach (var id in _tracks.Where(pair => pair.Value.LastSeenFrame < _frame - WindowFrames).Select(pair => pair.Key).ToList()) _tracks.Remove(id);
    }

    private void Judge(int slot, Entity entity, Track track)
    {
        var size = track.X.Length;
        double path = 0, turned = 0, lastDirection = double.NaN;
        for (var step = 1; step < size; step++)
        {
            var from = (track.Head + step - 1) % size;
            var to = (track.Head + step) % size;
            var dx = track.X[to] - track.X[from];
            var dy = track.Y[to] - track.Y[from];
            var length = Math.Sqrt(dx * dx + dy * dy);
            path += length;
            if (length < 0.05) continue;
            var direction = Math.Atan2(dy, dx);
            if (!double.IsNaN(lastDirection))
            {
                var change = direction - lastDirection;
                while (change > Math.PI) change -= 2 * Math.PI;
                while (change < -Math.PI) change += 2 * Math.PI;
                turned += change;
            }
            lastDirection = direction;
        }
        var first = track.Head;
        var last = (track.Head + size - 1) % size;
        var netX = track.X[last] - track.X[first];
        var netY = track.Y[last] - track.Y[first];
        var displacement = Math.Sqrt(netX * netX + netY * netY);
        var turns = Math.Abs(turned) / (2 * Math.PI);
        var qualifies = path > MinPath && displacement < MaxDisplacement;
        if (!qualifies)
        {
            track.InEpisode = false;
            return;
        }
        if (track.InEpisode) return;
        track.InEpisode = true;
        var kind = turns >= MinTurns ? "orbit" : "shuffle";
        // Where it happens: at the AI's rally point, close to the unit's own goal elsewhere, on the way to a far goal, or without a move order (a dock's own motion).
        var where = "no-order";
        if (entity.HasMoveTarget)
        {
            var toGoalX = Number(entity.MoveTargetX) - track.X[last];
            var toGoalY = Number(entity.MoveTargetY) - track.Y[last];
            where = toGoalX * toGoalX + toGoalY * toGoalY > GoalNear * GoalNear ? "goal-far" : "goal-near";
            if (where == "goal-near" && _session.Ais[slot]?.RallyPoint is { } gather)
            {
                var toRallyX = Number(gather.X) - track.X[last];
                var toRallyY = Number(gather.Y) - track.Y[last];
                if (toRallyX * toRallyX + toRallyY * toRallyY <= GoalNear * GoalNear) where = "rally";
            }
        }
        var countKey = kind + "|" + entity.Template.Name + "|" + where;
        Counts[(slot, countKey)] = Counts.GetValueOrDefault((slot, countKey)) + 1;
        if (Episodes.Count >= MaxEpisodesPerMatch) return;

        var text = new StringBuilder();
        var invariant = CultureInfo.InvariantCulture;
        text.Append(invariant, $"{_pairing}\t{_seed}\ttick {_world.Tick}\t{kind}\t{_factions[slot]}\t{entity.Template.Name}#{entity.Id.Value}\tat {track.X[last]:F0},{track.Y[last]:F0}\tpath {path:F0} net {displacement:F1} turns {turns:F1}");
        text.Append(invariant, $"\tmoving {entity.HasMoveTarget}");
        if (entity.HasMoveTarget)
        {
            var goalX = Number(entity.MoveTargetX) - track.X[last];
            var goalY = Number(entity.MoveTargetY) - track.Y[last];
            text.Append(invariant, $" goal {Number(entity.MoveTargetX):F0},{Number(entity.MoveTargetY):F0} ({Math.Sqrt(goalX * goalX + goalY * goalY):F1} away)");
        }
        text.Append(invariant, $" attack-move {_world.IsAttackMoving(entity)} victim {_world.AcquiredVictimOf(entity).Value}");
        foreach (var module in entity.Modules)
            if (module is AIUpdateInterface ai) text.Append(invariant, $" idle {ai.IsIdle} goal-object {ai.GoalObject.Value}");
        if (_session.Ais[slot]?.RallyPoint is { } rally)
        {
            var rallyX = Number(rally.X) - track.X[last];
            var rallyY = Number(rally.Y) - track.Y[last];
            text.Append(invariant, $"\trally {Math.Sqrt(rallyX * rallyX + rallyY * rallyY):F1} away");
        }
        text.Append("\tneighbours:");
        foreach (var other in _world.Entities)
        {
            if (other.Id == entity.Id || other.EffectivelyDead || other.ContainedBy.IsValid || other.Template.IsKindOf("PROJECTILE")) continue;
            var dx = Number(other.X) - track.X[last];
            var dy = Number(other.Y) - track.Y[last];
            var reach = NeighbourRadius + (other.Template.IsKindOf("STRUCTURE") ? Number(other.Template.BoundingCircleRadius) : 0);
            if (dx * dx + dy * dy > reach * reach) continue;
            var state = other.Template.IsKindOf("STRUCTURE") ? "structure" : other.HasMoveTarget ? "moving" : "standing";
            text.Append(invariant, $" {other.Template.Name}#{other.Id.Value}({dx:F0},{dy:F0} {state})");
        }
        Episodes.Add(text.ToString());
    }
}
