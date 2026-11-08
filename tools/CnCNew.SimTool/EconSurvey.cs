using System.Diagnostics;
using System.Globalization;
using System.Text;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>econ-survey</c>: does a built-in computer player keep gathering? Plays pairings of the reference manifest (or one map with a list of factions) with the product rules and writes, per
/// match and player, the income in 30-second buckets (supplies delivered by carriers, other income apart), the carriers and what they do, the stock left in reach, and for every window in
/// which a living player with a supply center earned nothing for two game minutes the state around it. Observation only; nothing is written to the world.
/// </summary>
internal static class EconSurvey
{
    private const string UsageText = """
        Usage: CnCNew.SimTool econ-survey <content-dir> --out <dir> [--pairings all|<id>,..] [--map <path> --factions A,B,..] [--seeds N] [--first-seed N] [--minutes M] [--jobs N]
               [--tactics ..] [--strength ..] [--window-seconds S]
          plays the matches (budget M game minutes, default 45) and writes <id>_<seed>.txt per match and a summary line per match on standard output.
        """;

    private sealed class Bucket
    {
        public long Truck, Other;
        public long Money;
        public int Centers, Carriers, Idle, Docking, Moving, Warehouses;
        public long StockAll, StockReach;
        public bool Defeated;
        public int Structures;
        public string Ai = string.Empty;
    }

    public static int Run(string[] args)
    {
        var outDir = ManifestCommands.Option(args, "--out");
        if (args.Length < 2 || outDir is null) return ManifestCommands.Fail(UsageText);
        var directory = args[1];
        Directory.CreateDirectory(outDir);
        var seeds = int.Parse(ManifestCommands.Option(args, "--seeds") ?? "1", CultureInfo.InvariantCulture);
        var firstSeed = uint.Parse(ManifestCommands.Option(args, "--first-seed") ?? "1", CultureInfo.InvariantCulture);
        var minutes = long.Parse(ManifestCommands.Option(args, "--minutes") ?? "45", CultureInfo.InvariantCulture);
        var windowSeconds = int.Parse(ManifestCommands.Option(args, "--window-seconds") ?? "120", CultureInfo.InvariantCulture);
        var jobs = ManifestCommands.Jobs(args);
        var rate = ToolRate.Current;
        var maxTicks = checked(minutes * 60 * rate.TicksPerSecond);

        var runs = new List<(string Id, string Map, string[] Factions)>();
        if (ManifestCommands.Option(args, "--map") is { } map)
        {
            var factions = (ManifestCommands.Option(args, "--factions") ?? throw new ArgumentException("--factions is required with --map.")).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            runs.Add((Path.GetFileNameWithoutExtension(map).Replace(" ", string.Empty) + "_" + string.Join("-", factions.Select(name => name.Replace("Faction", string.Empty))), map, factions));
        }
        else
        {
            var all = ReferenceManifest.AiPairingRuns().OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
            var only = ManifestCommands.Option(args, "--pairings") ?? "all";
            runs.AddRange(only == "all" ? all : all.Where(value => only.Split(',', StringSplitOptions.TrimEntries).Contains(value.Id, StringComparer.Ordinal)));
        }
        var queue = runs.SelectMany(run => Enumerable.Range(0, seeds).Select(index => (Run: run, Seed: checked(firstSeed + (uint)index)))).ToArray();
        var next = -1;
        var gate = new object();
        var threads = Enumerable.Range(0, Math.Min(jobs, queue.Length)).Select(worker => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= queue.Length) return;
                var ((id, mapPath, factions), seed) = queue[index];
                try
                {
                    context ??= SimContext.Load(directory, rate);
                    var watch = Stopwatch.StartNew();
                    var (report, summary) = Play(context, id, mapPath, factions, seed, maxTicks, windowSeconds, AiSurvey.Choices(args));
                    File.WriteAllText(Path.Combine(outDir, $"{id}_{seed.ToString(CultureInfo.InvariantCulture)}.txt"), report, new UTF8Encoding(false));
                    lock (gate) Console.WriteLine($"{summary} ({watch.Elapsed.TotalSeconds:F0} s)");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    lock (gate) Console.WriteLine($"FAILED {id} seed {seed}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }, 64 * 1024 * 1024) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        return 0;
    }

    private static (string Report, string Summary) Play(SimContext context, string id, string mapPath, string[] factions, uint seed, long maxTicks, int windowSeconds, AiSlotChoice[]? choices)
    {
        context = context.ForMap(mapPath);
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var options = SkirmishMatchOptions.Standard with { MaxTicks = maxTicks, AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 };
        var participants = ReferenceManifest.Participants(context, factions);
        var rate = context.Catalog.Rate.TicksPerSecond;
        var bucketTicks = 30L * rate;
        var session = SkirmishMatchSession.Start(options, context.Catalog, context.Fingerprint, terrain, map.Objects,
            [..participants.Select((participant, index) => AiSlotChoice.At(choices, index).Slot(participant))]);
        var players = session.Players;
        var buckets = players.Select(_ => new List<Bucket>()).ToArray();
        var current = players.Select(_ => new Bucket()).ToArray();
        var ownerIndex = players.Select((player, index) => (player.Id.Value, index)).ToDictionary(pair => pair.Value, pair => pair.index);
        while (!session.IsOver)
        {
            var result = session.Step();
            var world = session.World;
            if (result.Events is { } events)
                foreach (var ev in events)
                {
                    if (ev.Kind != SimEventKind.SupplyDeposited) continue;
                    var owner = (world.FindEntity(ev.Entity) ?? world.FindEntity(ev.Other))?.Owner;
                    if (owner is not { } who || !ownerIndex.TryGetValue(who.Value, out var player)) continue;
                    if (ev.Entity != ev.Other) current[player].Truck += ev.Value; else current[player].Other += ev.Value;
                }
            if (world.Tick % bucketTicks != 0) continue;
            for (var player = 0; player < players.Length; player++)
            {
                Snapshot(session, player, current[player]);
                buckets[player].Add(current[player]);
                current[player] = new Bucket();
            }
        }
        var report = new StringBuilder();
        var summary = new StringBuilder($"{id} seed {seed}: {(session.Winner is { } w ? $"winner {session.Players.First(p => p.Id == w).Faction} at {session.VictoryTick / rate / 60.0:F1} min" : "stalemate")}");
        report.AppendLine($"{id} seed {seed} {rate} Hz, {maxTicks / rate / 60} game minutes, end tick {session.EndTick}, {summary}");
        var windowBuckets = windowSeconds / 30;
        for (var player = 0; player < players.Length; player++)
        {
            var name = players[player].Faction?.Replace("Faction", string.Empty) ?? "?";
            var rows = buckets[player];
            report.AppendLine();
            report.AppendLine($"== player {player} {name}: collected by carriers {rows.Sum(row => row.Truck)}, other income {rows.Sum(row => row.Other)}");
            report.AppendLine("  min  truck other  money ctr car idle dock move whs  stockAll stockReach struct");
            for (var row = 1; row < rows.Count; row += 2)
            {
                var a = rows[row - 1];
                var b = rows[row];
                report.AppendLine($"  {(row + 1) / 2,3} {a.Truck + b.Truck,6} {a.Other + b.Other,5} {b.Money,6} {b.Centers,3} {b.Carriers,3} {b.Idle,4} {b.Docking,4} {b.Moving,4} {b.Warehouses,3} {b.StockAll,9} {b.StockReach,10} {b.Structures,6}{(b.Defeated ? " DEFEATED" : string.Empty)}");
            }
            // Windows: consecutive buckets without any carrier income while the player lives and owns a finished supply center.
            var start = -1;
            var zeroWindows = 0;
            long zeroSeconds = 0;
            for (var index = 0; index <= rows.Count; index++)
            {
                var zero = index < rows.Count && rows[index].Truck == 0 && !rows[index].Defeated && rows[index].Centers > 0;
                if (zero && start < 0) start = index;
                if (zero || start < 0) continue;
                if (index - start >= windowBuckets)
                {
                    zeroWindows++;
                    zeroSeconds += (index - start) * 30L;
                    report.AppendLine($"  ZERO WINDOW {name}: {(start * 30L)}..{(index * 30L)} s ({(index - start) * 30} s)");
                    foreach (var at in new[] { start, (start + index - 1) / 2, index - 1 }.Distinct())
                        report.AppendLine($"    at {at * 30 + 30} s: {Describe(rows[at])}\n      AI: {rows[at].Ai}");
                }
                start = -1;
            }
            // Phase 64 threshold: a living player with a finished supply center and no carrier at three samples in a row (90 game seconds).
            var noCarrier = 0;
            var run = 0;
            foreach (var row in rows)
            {
                if (!row.Defeated && row.Centers > 0 && row.Carriers == 0) { run++; if (run == 3) noCarrier++; }
                else run = 0;
            }
            if (noCarrier > 0) report.AppendLine($"  NO CARRIER {name}: {noCarrier} stretch(es) of 90 s or more with a finished supply center and no carrier");
            summary.Append($" | {name}: {rows.Sum(row => row.Truck)} gathered, zero windows {zeroWindows} ({zeroSeconds} s), no-carrier stretches {noCarrier}");
        }
        return (report.ToString(), summary.ToString());
    }

    private static string Describe(Bucket bucket) => $"money {bucket.Money}, centers {bucket.Centers}, carriers {bucket.Carriers} (idle {bucket.Idle}, docking {bucket.Docking}, moving {bucket.Moving}), warehouses with stock {bucket.Warehouses}, stock on map {bucket.StockAll}, in reach of own centers {bucket.StockReach}";

    private static void Snapshot(SkirmishMatchSession session, int index, Bucket bucket)
    {
        var world = session.World;
        var player = session.Players[index];
        bucket.Defeated = world.IsPlayerDefeated(player.Id);
        bucket.Money = (long)world.MoneyOf(player.Id).Floor();
        var centers = new List<Entity>();
        foreach (var entity in world.Entities)
        {
            if (entity.EffectivelyDead) continue;
            if (entity.Owner == player.Id)
            {
                if (entity.Template.IsKindOf("STRUCTURE"))
                {
                    bucket.Structures++;
                    if (entity.Template.IsKindOf("CASH_GENERATOR") && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && HasModule(entity, "SupplyCenterDockUpdate")) centers.Add(entity);
                }
                else if (entity.Template.IsKindOf("HARVESTER"))
                {
                    bucket.Carriers++;
                    foreach (var module in entity.Modules)
                        if (module is AIUpdateInterface ai)
                        {
                            if (ai.IsIdle) bucket.Idle++;
                            else if (ai.IsDocking) bucket.Docking++;
                            else if (entity.HasMoveTarget) bucket.Moving++;
                            break;
                        }
                }
            }
        }
        bucket.Centers = centers.Count;
        foreach (var entity in world.Entities)
        {
            if (entity.EffectivelyDead || !HasModule(entity, "SupplyWarehouseDockUpdate")) continue;
            var boxes = EntityPresentationView.WarehouseSupplyBoxes(entity);
            if (boxes <= 0) continue;
            var value = (long)boxes * world.ValuePerSupplyBox;
            bucket.Warehouses++;
            bucket.StockAll += value;
            foreach (var center in centers)
            {
                var dx = (entity.X - center.X).ToDecimal();
                var dy = (entity.Y - center.Y).ToDecimal();
                if (Math.Sqrt((double)(dx * dx + dy * dy)) <= 20 * TerrainMap.CellSize + 100) { bucket.StockReach += value; break; }
            }
        }
        bucket.Ai = session.Ais[index]?.DescribeState() ?? string.Empty;
    }

    private static bool HasModule(Entity entity, string name)
    {
        foreach (var module in entity.Modules)
            if (module.GetType().Name == name) return true;
        return false;
    }
}
