using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.SimTool;

/// <summary>
/// <c>path-bench</c>: the cost of the path search alone, on the terrain of a real map without structures. A fixed list of searches (drawn from a seed) is run several times; every
/// search counts with its fastest run, which is the figure least disturbed by whatever else the machine does. The cells expanded are deterministic and identify the work; the checksum over every
/// returned path must be the same for every build that claims to search the same way.
/// </summary>
internal static class PathBench
{
    private const string Usage = "path-bench <content-dir> --map <virtual-map-path> [--searches N] [--rounds N] [--seed N]";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed class Counter : IMovementProbe
    {
        public long Expansions, Pushes, Searches, AtBudget;
        public void Started(MovementWork work) { }
        public void Ended(MovementWork work) { }

        public void PathSearched(in PathSearchInfo info)
        {
            Searches++;
            Expansions += info.Expansions;
            Pushes += info.Pushes;
            if (info.Expansions >= 200_000) AtBudget++;
        }
    }

    public static int Run(string[] args)
    {
        string? Arg(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var mapPath = Arg("--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.\n" + Usage);
        var searches = int.Parse(Arg("--searches") ?? "400", Invariant);
        var rounds = int.Parse(Arg("--rounds") ?? "7", Invariant);
        var seed = ulong.Parse(Arg("--seed") ?? "19", Invariant);
        var content = ContentFileSystem.Open(args[1]);
        using var stream = content.OpenRead(mapPath);
        var terrain = TerrainMap.Build(SageMapDecoder.Decode(stream), mapPath);
        var grid = new NavigationGrid(terrain);

        // SplitMix64, so that the list depends on the seed alone.
        ulong Next()
        {
            var value = seed += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        var queries = new List<(Fixed StartX, Fixed StartY, Fixed GoalX, Fixed GoalY)>();
        while (queries.Count < searches)
        {
            Fixed Coordinate(int cells) => Fixed.FromRaw((long)(Next() % (ulong)((long)cells * TerrainMap.CellSize << 16)));
            var query = (Coordinate(terrain.CellsX), Coordinate(terrain.CellsY), Coordinate(terrain.CellsX), Coordinate(terrain.CellsY));
            // A start a ground unit can stand on; the goal is whatever the draw gives (some cannot be reached: those searches run to the budget, as in a match).
            if (grid.IsPassable(TerrainMap.CellOf(query.Item1), TerrainMap.CellOf(query.Item2), LocomotorSurfaces.Ground)) queries.Add(query);
        }

        // One blocker of radius zero far outside the map keeps the grid from answering a repeated question from its memory: the search itself is what is measured.
        (Fixed X, Fixed Y, Fixed Radius)[] offMap = [(Fixed.FromInt(-10_000), Fixed.FromInt(-10_000), Fixed.Zero)];
        var counter = new Counter();
        // The fastest run of every single search over the rounds, summed: a search that was interrupted in one round is not interrupted in all of them.
        var fastest = new long[queries.Count];
        Array.Fill(fastest, long.MaxValue);
        ulong checksum = 0;
        for (var round = 0; round < rounds; round++)
        {
            grid.Probe = round == 0 ? counter : null;
            ulong sum = 14695981039346656037UL;
            for (var index = 0; index < queries.Count; index++)
            {
                var (startX, startY, goalX, goalY) = queries[index];
                var start = Stopwatch.GetTimestamp();
                var path = grid.FindPath(startX, startY, goalX, goalY, LocomotorSurfaces.Ground, offMap);
                var elapsed = Stopwatch.GetTimestamp() - start;
                if (round > 0 && elapsed < fastest[index]) fastest[index] = elapsed;
                sum = (sum ^ (ulong)path.Status) * 1099511628211UL;
                foreach (var point in path.Points) sum = ((sum ^ (ulong)point.X.Raw) * 1099511628211UL ^ (ulong)point.Y.Raw) * 1099511628211UL;
            }
            if (round == 0) checksum = sum;
            else if (sum != checksum) throw new InvalidOperationException("The same searches returned different paths in two rounds.");
        }

        var nanoseconds = fastest.Sum() * 1e9 / Stopwatch.Frequency;
        Console.WriteLine($"Path bench on {mapPath} ({terrain.CellsX}x{terrain.CellsY} cells, terrain only): {counter.Searches} searches, {counter.Expansions} cells expanded, {counter.Pushes} heap pushes, {counter.AtBudget} at the budget; paths {checksum:X16}.");
        Console.WriteLine($"Every search at its fastest of {rounds - 1} rounds: {nanoseconds / 1e6:F1} ms = {nanoseconds / Math.Max(1, counter.Expansions):F1} ns per expanded cell, {nanoseconds / 1e3 / Math.Max(1, counter.Searches):F1} us per search.");
        Console.WriteLine($"summary: bench_cells={counter.Expansions};bench_ns_per_cell={(nanoseconds / Math.Max(1, counter.Expansions)).ToString("F1", Invariant)};bench_paths={checksum:X16};bench_searches={counter.Searches}");
        return 0;
    }
}
