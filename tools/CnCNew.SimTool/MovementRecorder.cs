using System.Diagnostics;
using System.Globalization;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>perf --movement</c>: reads the clock where the simulation reports the pieces of ground movement (<see cref="IMovementProbe"/>) and keeps the deterministic counters of every path
/// search (cells expanded, heap pushes, result) together with the unit that asked. The counters are the same in every run of the same inputs and are what two builds are compared by on a
/// machine that is not quiet; the times are not.
/// </summary>
internal sealed class MovementRecorder(SimulationWorld world, TickRecorder ticks) : IMovementProbe
{
    private static readonly MovementWork[] Kinds = Enum.GetValues<MovementWork>();
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly record struct Search(long Tick, uint Entity, string Template, string Module, PathSearchInfo Info, long Elapsed);

    private readonly long[] _calls = new long[Kinds.Length];
    private readonly long[] _elapsed = new long[Kinds.Length];
    private readonly long[] _max = new long[Kinds.Length];
    private readonly long[] _allocated = new long[Kinds.Length];
    private readonly long[] _started = new long[Kinds.Length];
    private readonly long[] _startAllocated = new long[Kinds.Length];
    private readonly List<Search> _searches = [];
    private long _lastElapsed;

    public long DumpTick { get; init; } = -1;

    public void Started(MovementWork work)
    {
        _startAllocated[(int)work] = GC.GetAllocatedBytesForCurrentThread();
        _started[(int)work] = Stopwatch.GetTimestamp();
    }

    public void Ended(MovementWork work)
    {
        var elapsed = Stopwatch.GetTimestamp() - _started[(int)work];
        _allocated[(int)work] += GC.GetAllocatedBytesForCurrentThread() - _startAllocated[(int)work];
        _calls[(int)work]++;
        _elapsed[(int)work] += elapsed;
        if (elapsed > _max[(int)work]) _max[(int)work] = elapsed;
        _lastElapsed = elapsed;
    }

    public void PathSearched(in PathSearchInfo info)
    {
        var module = ticks.Current;
        _searches.Add(new Search(world.Tick, module?.Entity.Id.Value ?? 0, module?.Entity.Template.Name ?? "(outside updates)", module?.ModuleName ?? "(outside updates)", info, _lastElapsed));
    }

    private static double Us(double stopwatchTicks) => stopwatchTicks * 1e6 / Stopwatch.Frequency;

    public void Report(int cellsX, int steps, SortedDictionary<string, string> summary)
    {
        Console.WriteLine();
        Console.WriteLine($"Movement probe (--movement) over {steps} steps; counters are deterministic, times are this machine's:");
        Console.WriteLine($"  {"work",-22} {"calls",11} {"sum ms",11} {"us/tick",9} {"mean us",9} {"max us",11} {"bytes/call",11}");
        foreach (var kind in Kinds)
        {
            var index = (int)kind;
            Console.WriteLine($"  {kind,-22} {_calls[index],11} {Us(_elapsed[index]) / 1000,11:F1} {Us(_elapsed[index]) / Math.Max(1, steps),9:F1} {Us(_elapsed[index]) / Math.Max(1, _calls[index]),9:F2} {Us(_max[index]),11:F1} {_allocated[index] / Math.Max(1, _calls[index]),11}");
            summary[$"move_{kind}_calls"] = _calls[index].ToString(Invariant);
            summary[$"move_{kind}_us_per_tick"] = (Us(_elapsed[index]) / Math.Max(1, steps)).ToString("F1", Invariant);
        }

        long expansions = 0, pushes = 0, searchTicks = 0, cached = 0, cachedExpansions = 0, cachedTicks = 0, proved = 0, provedExpansions = 0, reverseCells = 0, provedTicks = 0;
        var byStatus = new SortedDictionary<string, (long Count, long Expansions, long Elapsed)>(StringComparer.Ordinal);
        var byCaller = new SortedDictionary<string, (long Count, long Expansions, long Elapsed)>(StringComparer.Ordinal);
        var byTick = new Dictionary<long, (int Count, long Expansions, long Elapsed)>();
        var byEntity = new Dictionary<uint, (int Count, long Expansions, string Template)>();
        // A search with the same start cell, goal cell and surfaces as the one the same unit ran before: what a per-unit memo could answer if the grid had not changed in between.
        var lastOfEntity = new Dictionary<uint, PathSearchInfo>();
        long sameAsLast = 0, sameAsLastExpansions = 0;
        int[] bounds = [0, 10, 100, 1_000, 10_000, 50_000, 199_999, int.MaxValue];
        var histogram = new (long Count, long Expansions, long Elapsed)[bounds.Length];
        foreach (var search in _searches)
        {
            var info = search.Info;
            if (info.Cached)
            {
                // Answered from the grid's memory: the counters are those of the search that was saved, not work done.
                cached++;
                cachedExpansions += info.Expansions;
                cachedTicks += search.Elapsed;
                var cachedTick = byTick.GetValueOrDefault(search.Tick);
                byTick[search.Tick] = (cachedTick.Count + 1, cachedTick.Expansions, cachedTick.Elapsed + search.Elapsed);
                continue;
            }
            expansions += info.Expansions;
            pushes += info.Pushes;
            searchTicks += search.Elapsed;
            reverseCells += info.ReverseCells;
            if (info.ProvedUnreachable)
            {
                proved++;
                provedExpansions += info.Expansions;
                provedTicks += search.Elapsed;
            }
            var status = (info.ProvedUnreachable ? "No path (proved)" : info.Status.ToString()) + (info.TemporaryCells > 0 ? " (around blockers)" : string.Empty) + (info.Expansions >= 200_000 ? " budget exhausted" : string.Empty);
            Add(byStatus, status, info.Expansions, search.Elapsed);
            Add(byCaller, search.Module, info.Expansions, search.Elapsed);
            var tick = byTick.GetValueOrDefault(search.Tick);
            byTick[search.Tick] = (tick.Count + 1, tick.Expansions + info.Expansions, tick.Elapsed + search.Elapsed);
            var entity = byEntity.GetValueOrDefault(search.Entity);
            byEntity[search.Entity] = (entity.Count + 1, entity.Expansions + info.Expansions, search.Template);
            if (lastOfEntity.TryGetValue(search.Entity, out var last) && last.StartCell == info.StartCell && last.GoalCell == info.GoalCell && last.Surfaces == info.Surfaces && last.TemporaryCells == 0 && info.TemporaryCells == 0)
            {
                sameAsLast++;
                sameAsLastExpansions += info.Expansions;
            }
            lastOfEntity[search.Entity] = info;
            var bucket = 0;
            while (info.Expansions > bounds[bucket]) bucket++;
            histogram[bucket] = (histogram[bucket].Count + 1, histogram[bucket].Expansions + info.Expansions, histogram[bucket].Elapsed + search.Elapsed);
        }

        Console.WriteLine($"  Path searches: {_searches.Count} ({(double)_searches.Count / Math.Max(1, steps):F3} per tick), cells expanded {expansions} ({(double)expansions / Math.Max(1, _searches.Count):F0} per search, {(double)expansions / Math.Max(1, steps):F0} per tick), heap pushes {pushes}; "
                          + $"{Us(searchTicks) * 1000 / Math.Max(1, expansions):F0} ns per expanded cell.");
        Console.WriteLine($"  Answered from the grid's memory (same start cell, goal cell and surfaces, footprints unchanged): {cached} of {_searches.Count} searches, {cachedExpansions} cells not expanded again, {Us(cachedTicks) / 1000:F1} ms spent on them.");
        Console.WriteLine($"  Stopped because the goal was proved unreachable (searches that only count when they arrive): {proved} searches after {provedExpansions} expanded cells, {Us(provedTicks) / 1000:F1} ms; cells of the floods from the goal, all such searches: {reverseCells}.");
        summary["move_proved_unreachable"] = proved.ToString(Invariant);
        summary["move_reverse_cells"] = reverseCells.ToString(Invariant);
        summary["move_cached"] = cached.ToString(Invariant);
        summary["move_cached_expansions"] = cachedExpansions.ToString(Invariant);
        summary["move_searches"] = _searches.Count.ToString(Invariant);
        summary["move_expansions"] = expansions.ToString(Invariant);
        summary["move_pushes"] = pushes.ToString(Invariant);
        Console.WriteLine("  by result:");
        foreach (var (name, value) in byStatus) Console.WriteLine($"    {name,-40} {value.Count,8} searches {value.Expansions,12} cells {Us(value.Elapsed) / 1000,10:F1} ms");
        Console.WriteLine("  by calling module:");
        foreach (var (name, value) in byCaller) Console.WriteLine($"    {name,-40} {value.Count,8} searches {value.Expansions,12} cells {Us(value.Elapsed) / 1000,10:F1} ms");
        Console.WriteLine("  by cells expanded:");
        for (var bucket = 0; bucket < bounds.Length; bucket++)
            Console.WriteLine($"    {(bucket == 0 ? "0" : bounds[bucket] == int.MaxValue ? "200000 (budget)" : $"<= {bounds[bucket]}"),-40} {histogram[bucket].Count,8} searches {histogram[bucket].Expansions,12} cells {Us(histogram[bucket].Elapsed) / 1000,10:F1} ms");
        Console.WriteLine($"  Same start cell, goal cell and surfaces as the unit's previous search: {sameAsLast} searches, {sameAsLastExpansions} cells.");
        Console.WriteLine($"  Ticks with at least one search: {byTick.Count}; with at least 10: {byTick.Values.Count(value => value.Count >= 10)}.");
        Console.WriteLine("  The five ticks with the most expanded cells:");
        foreach (var (tick, value) in byTick.OrderByDescending(pair => pair.Value.Expansions).ThenBy(pair => pair.Key).Take(5))
            Console.WriteLine($"    tick {tick,7}: {value.Count,4} searches, {value.Expansions,9} cells, {Us(value.Elapsed) / 1000,8:F1} ms");
        Console.WriteLine("  The five units with the most expanded cells:");
        foreach (var (entity, value) in byEntity.OrderByDescending(pair => pair.Value.Expansions).ThenBy(pair => pair.Key).Take(5))
            Console.WriteLine($"    #{entity,-6} {value.Template,-36} {value.Count,6} searches, {value.Expansions,10} cells");
        if (byTick.Count > 0)
        {
            var worst = byTick.OrderByDescending(pair => pair.Value.Expansions).ThenBy(pair => pair.Key).First();
            summary["move_worst_tick"] = worst.Key.ToString(Invariant);
            summary["move_worst_tick_expansions"] = worst.Value.Expansions.ToString(Invariant);
            summary["move_worst_tick_searches"] = worst.Value.Count.ToString(Invariant);
        }

        var dump = DumpTick >= 0 ? DumpTick : byTick.Count > 0 ? byTick.OrderByDescending(pair => pair.Value.Expansions).ThenBy(pair => pair.Key).First().Key : -1;
        if (dump < 0) return;
        Console.WriteLine($"  Searches of tick {dump} (cells as x,y):");
        var shown = 0;
        foreach (var search in _searches)
        {
            if (search.Tick != dump) continue;
            if (++shown > 60) { Console.WriteLine("    ..."); break; }
            var info = search.Info;
            Console.WriteLine($"    #{search.Entity,-6} {search.Template,-32} {search.Module,-22} {info.StartCell % cellsX},{info.StartCell / cellsX} -> {info.GoalCell % cellsX},{info.GoalCell / cellsX} {info.Surfaces} {info.Status}: "
                              + $"{info.Expansions} cells, {info.Pushes} pushes, escape {info.EscapeCells}, temporary {info.TemporaryCells}, path {info.PathCells} cells / {info.Waypoints} points, {Us(search.Elapsed) / 1000:F2} ms");
        }
    }

    private static void Add(SortedDictionary<string, (long Count, long Expansions, long Elapsed)> table, string key, long expansions, long elapsed)
    {
        var value = table.GetValueOrDefault(key);
        table[key] = (value.Count + 1, value.Expansions + expansions, value.Elapsed + elapsed);
    }
}
