using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>group-march</c>: what a person would judge by watching a group march - whether it arrives together and by a sensible way - replaced by measured quantities, under
/// the original movement and under the movement profile <c>movement-budget</c> (docs/PERF-MOVEMENT.md section 9). Groups of one unit type stand at the start positions
/// of a real map (its terrain, no map objects) and are all ordered at once, as one group order each, to the next start position. Per profile: how many arrived, when, the
/// spread of the arrivals within a group, the length each unit walked against the length of the path a search of its own from its start to where it ended would give,
/// the searches and cells, the unit-ticks spent waiting for a path, and the longest tick. Tool code: lengths and times in double precision.
/// </summary>
internal static class GroupMarch
{
    private static double D(Fixed value) => CnCNew.Presentation.FixedConvert.ToDouble(value);

    private const string Usage = "group-march <content-dir> --map <virtual-map-path> [--unit <template>] [--group N] [--groups N] [--seconds S] [--spacing U]";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed class Counter : IMovementProbe
    {
        public long Searches, Cells, MaxCellsInOne;
        public void Started(MovementWork work) { }
        public void Ended(MovementWork work) { }

        public void PathSearched(in PathSearchInfo info)
        {
            Searches++;
            Cells += info.Expansions;
            MaxCellsInOne = Math.Max(MaxCellsInOne, info.Expansions);
        }
    }

    public static int Run(string[] args, Func<string, CompiledDefinitionGraph> compile)
    {
        string? Arg(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var mapPath = Arg("--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.\n" + Usage);
        var unit = Arg("--unit") ?? "AmericaTankCrusader";
        var groupSize = int.Parse(Arg("--group") ?? "12", Invariant);
        var groupCount = int.Parse(Arg("--groups") ?? "8", Invariant);
        var seconds = int.Parse(Arg("--seconds") ?? "240", Invariant);
        var spacing = int.Parse(Arg("--spacing") ?? "25", Invariant);

        var content = ContentFileSystem.Open(args[1]);
        var graph = compile(args[1]);
        var geometry = CnCNew.Game.Data.ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);
        TerrainMap terrain;
        using (var stream = content.OpenRead(mapPath)) terrain = TerrainMap.Build(SageMapDecoder.Decode(stream), mapPath);
        if (terrain.StartPositions.Length < 2) throw new ArgumentException($"{mapPath} has fewer than two start positions.");

        Console.WriteLine($"Group march on {mapPath} ({terrain.CellsX}x{terrain.CellsY} cells, {terrain.StartPositions.Length} start positions), {groupCount} groups of {groupSize} {unit}, "
            + $"{ToolRate.Current.TicksPerSecond} Hz, {seconds} s of game time; terrain only (no map objects).");
        var results = new List<(string Profile, Result Result)>();
        foreach (var rules in new[] { RuleProfile.Original, RuleProfile.Parse(RuleProfile.BudgetedMovementName) })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, ToolRate.Current, geometry, rules);
            var fingerprint = catalog.Rules.Combine(catalog.Rate.Combine(geometry.Combine(DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256)));
            var result = March(catalog, fingerprint, terrain, unit, groupSize, groupCount, seconds, spacing);
            results.Add((rules.IsOriginal ? "original" : rules.Name, result));
        }

        Console.WriteLine();
        Console.WriteLine("profile          arrived  arrival median  spread median/max  walked/own search median/p95/max  searches   cells       waiting unit-ticks  longest tick");
        foreach (var (profile, r) in results)
            Console.WriteLine($"{profile,-16} {r.Arrived,3}/{r.Units,-4} {r.ArrivalMedian,10:F1} s   {r.SpreadMedian,6:F1} / {r.SpreadMax,6:F1} s   "
                + $"{r.RatioMedian,6:F3} / {r.RatioP95,6:F3} / {r.RatioMax,6:F3}       {r.Searches,8:N0}  {r.Cells,11:N0}  {r.WaitingUnitTicks,10:N0}         {r.LongestTickMs,7:F1} ms");
        Console.WriteLine();
        Console.WriteLine("Arrival: the tick a unit's move ended after the order; spread: last minus first arrival of a group; walked: the sum of the steps of a unit; own search:");
        Console.WriteLine("the path a search of the unit's own from its start to where it ended gives on the same terrain. What a person would judge by watching is not measured.");
        return results.All(value => value.Result.Arrived > 0) ? 0 : 1;
    }

    private sealed record Result(int Units, int Arrived, double ArrivalMedian, double SpreadMedian, double SpreadMax, double RatioMedian, double RatioP95, double RatioMax,
        long Searches, long Cells, long WaitingUnitTicks, double LongestTickMs);

    private static Result March(TemplateCatalog catalog, string fingerprint, TerrainMap terrain, string unit, int groupSize, int groupCount, int seconds, int spacing)
    {
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "P1"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), catalog.Rate, 7, players, "group-march", fingerprint, terrain.Fingerprint), catalog, terrain);
        var counter = new Counter();
        world.Navigation!.Probe = counter;
        ulong sequence = 1000;
        var starts = terrain.StartPositions;
        var columns = (int)Math.Ceiling(Math.Sqrt(groupSize));
        for (var group = 0; group < groupCount; group++)
        {
            var (sx, sy) = (starts[group % starts.Length].X, starts[group % starts.Length].Y);
            // Groups that share a start stand apart: each further one a few rows aside.
            var lane = group / starts.Length;
            for (var index = 0; index < groupSize; index++)
            {
                var x = sx + Fixed.FromInt((index % columns - columns / 2) * spacing + lane * spacing * (columns + 1));
                var y = sy + Fixed.FromInt((index / columns - columns / 2) * spacing);
                var spawned = world.Submit(new SpawnCommand(new PlayerId(1), sequence++, world.Tick, unit, new PlayerId(1), x, y, Fixed.Zero, Fixed.Zero));
                if (!spawned.Accepted) throw new InvalidOperationException($"Spawning {unit} was refused ({spawned.Reason}).");
            }
        }
        world.Step();
        var units = world.Entities.Where(value => value.Template.Name == unit).OrderBy(value => value.Id.Value).ToArray();
        if (units.Length != groupSize * groupCount) throw new InvalidOperationException($"{units.Length} of {groupSize * groupCount} units exist.");
        var startAt = units.ToDictionary(value => value.Id.Value, value => (value.X, value.Y));
        for (var group = 0; group < groupCount; group++)
        {
            var target = starts[(group + 1) % starts.Length];
            var members = units.Skip(group * groupSize).Take(groupSize).Select(value => value.Id).ToImmutableArray();
            var ordered = world.Submit(new GroupMoveCommand(new PlayerId(1), sequence++, world.Tick, members, target.X, target.Y, false));
            if (!ordered.Accepted) throw new InvalidOperationException($"The group order was refused ({ordered.Reason}).");
        }
        var orderTick = world.Tick;
        var arrival = new Dictionary<uint, long>();
        var walked = units.ToDictionary(value => value.Id.Value, _ => 0.0);
        var last = units.ToDictionary(value => value.Id.Value, value => (X: D(value.X), Y: D(value.Y)));
        long waiting = 0;
        double longest = 0;
        var watch = new Stopwatch();
        var ais = units.ToDictionary(value => value.Id.Value, value => value.Modules.OfType<AIUpdateInterface>().First());
        for (var tick = 0; tick < seconds * catalog.Rate.TicksPerSecond && arrival.Count < units.Length; tick++)
        {
            watch.Restart();
            var step = world.Step();
            watch.Stop();
            if (tick > 0) longest = Math.Max(longest, watch.Elapsed.TotalMilliseconds);
            foreach (var simEvent in step.Events)
                if (simEvent.Kind == SimEventKind.MoveArrived && !arrival.ContainsKey(simEvent.Entity.Value) && walked.ContainsKey(simEvent.Entity.Value)) arrival[simEvent.Entity.Value] = world.Tick - orderTick;
            foreach (var entity in units)
            {
                var id = entity.Id.Value;
                var (x, y) = (D(entity.X), D(entity.Y));
                var (px, py) = last[id];
                walked[id] += Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                last[id] = (x, y);
                if (ais[id].WaitingForPath) waiting++;
                if (!arrival.ContainsKey(id) && tick > 0 && !entity.HasMoveTarget) arrival[id] = world.Tick - orderTick;
            }
        }

        // A search of the unit's own on a fresh grid of the same terrain (no units on it): from where it stood to where it ended.
        var reference = new NavigationGrid(terrain);
        var ratios = new List<double>();
        foreach (var entity in units)
        {
            if (!arrival.ContainsKey(entity.Id.Value)) continue;
            var (fromX, fromY) = startAt[entity.Id.Value];
            var surfaces = entity.Template.Locomotor?.Surfaces ?? LocomotorSurfaces.Ground;
            var own = reference.FindPath(fromX, fromY, entity.X, entity.Y, surfaces);
            if (own is null || own.Points.IsDefaultOrEmpty) continue;
            double length = 0, cx = D(fromX), cy = D(fromY);
            foreach (var point in own.Points)
            {
                var (nx, ny) = (D(point.X), D(point.Y));
                length += Math.Sqrt((nx - cx) * (nx - cx) + (ny - cy) * (ny - cy));
                (cx, cy) = (nx, ny);
            }
            if (length > 1) ratios.Add(walked[entity.Id.Value] / length);
        }
        ratios.Sort();
        var spreads = new List<double>();
        for (var group = 0; group < groupCount; group++)
        {
            var times = units.Skip(group * groupSize).Take(groupSize).Where(value => arrival.ContainsKey(value.Id.Value)).Select(value => arrival[value.Id.Value]).ToArray();
            if (times.Length == groupSize) spreads.Add((times.Max() - times.Min()) / (double)catalog.Rate.TicksPerSecond);
        }
        spreads.Sort();
        var arrivals = arrival.Values.Select(value => value / (double)catalog.Rate.TicksPerSecond).OrderBy(value => value).ToArray();
        static double Quantile(IReadOnlyList<double> sorted, double q) => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * (sorted.Count - 1) + 0.5))];
        return new Result(units.Length, arrival.Count, Quantile(arrivals, 0.5), Quantile(spreads, 0.5), spreads.Count == 0 ? double.NaN : spreads[^1],
            Quantile(ratios, 0.5), Quantile(ratios, 0.95), ratios.Count == 0 ? double.NaN : ratios[^1], counter.Searches, counter.Cells, waiting, longest);
    }
}
