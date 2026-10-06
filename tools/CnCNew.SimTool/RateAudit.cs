using System.Collections;
using System.Globalization;
using System.Reflection;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Game.Data;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// <c>rate-audit &lt;content-dir&gt; --tick-rate N</c>: what building the catalog for a faster tick rate loses. Two views of the same loss:
/// <para>
/// <b>By conversion</b> – a <see cref="SimulationTimeProbe"/> on the time service while the catalog is built sees the exact decimal value of every
/// conversion to a fixed-point number and counts the ones that come out as zero or more than 1 % / 10 % off. Exact, but it only knows the function.
/// </para>
/// <para>
/// <b>By field</b> – the 30 Hz catalog and the catalog of the rate are walked side by side (same definitions, same shape). A fixed-point value that
/// differs is a value per tick, per tick² or a duration; which one is told from the ratio (the factor nearest to 30 Hz value / value), and the value is
/// compared with the 30 Hz value scaled by exactly that factor. That reference is itself a fixed-point number, so the error is measured against the
/// 30 Hz quantisation, not against the data: it says where the faster rate is coarser than 30 Hz already is. Whole numbers that differ are durations
/// in ticks and must be the 30 Hz count times the factor (the grid rule); the ones that are not are listed.
/// </para>
/// A measurement for phase 22 (fixed-point resolution) and phase 23, never a failing test.
/// </summary>
internal static class RateAudit
{
    public static int Run(string[] args)
    {
        var rate = ToolRate.Current;
        var content = ContentFileSystem.Open(args[1]);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
        var geometry = ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);

        var probes = new Dictionary<SimulationRate, SimulationTimeProbe>();
        var catalogs = new Dictionary<SimulationRate, TemplateCatalog>();
        foreach (var built in new[] { SimulationRate.Hz30, rate }.Distinct())
        {
            var probe = new SimulationTimeProbe();
            built.Time.Probe = probe;
            try { catalogs[built] = TemplateCatalog.Build(graph, ModuleBindingTable.Default, built, geometry); }
            finally { built.Time.Probe = null; }
            probes[built] = probe;
        }

        Console.WriteLine($"Rate audit of the catalog at {rate} against 30 Hz ({catalogs[rate].Templates.Length} templates, retail base data).");
        Console.WriteLine();
        Console.WriteLine($"By conversion (exact decimal value against its fixed-point result, Q{63 - Fixed.FractionBits}.{Fixed.FractionBits}), while the catalog is built:");
        Console.WriteLine($"  {"function",-42} {"rate",7} {"calls",8} {"non-zero",9} {"-> zero",8} {"> 1 %",8} {"> 10 %",8}  worst relative error (exact value)");
        foreach (var built in probes.Keys.OrderBy(value => value.TicksPerSecond))
            foreach (var (function, entry) in probes[built].Quantisation.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {function,-42} {built,7} {entry.Calls,8} {entry.NonZero,9} {entry.RoundedToZero,8} {entry.AboveOnePercent,8} {entry.AboveTenPercent,8}  {entry.WorstRelativeError * 100m:F3} % ({entry.WorstExact:G12})"));
        foreach (var built in probes.Keys.OrderBy(value => value.TicksPerSecond))
        {
            var sites = probes[built].Sites;
            Console.WriteLine($"  other counters at {built} during the build: {(sites.Count == 0 ? "none" : string.Join(", ", sites.GroupBy(pair => (pair.Key.Kind, pair.Key.Function)).OrderBy(group => group.Key.Function, StringComparer.Ordinal).Select(group => $"{group.Key.Kind} in {group.Key.Function} x{group.Sum(pair => pair.Value)}")))}.");
        }
        var walk = new Walk(rate.TicksPerOriginalFrame);
        walk.Compare(catalogs[SimulationRate.Hz30], catalogs[rate], "catalog", string.Empty);
        // The range: a value that is squared (a radius, a range) must stay below the square root of the largest fixed-point value.
        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Range of the catalog at {rate} (largest fixed-point value {Fixed.MaxValue.ToDecimal():F0}, its square root {Walk.SquareLimit.ToDecimal():F0}): the largest magnitude is {walk.LargestText}."));
        Print($"magnitude above the square root of the largest value (a square of it overflows)", walk.Large);
        if (rate == SimulationRate.Hz30) return 0;

        Console.WriteLine();
        Console.WriteLine($"By field (30 Hz catalog against the {rate} catalog): {walk.FixedValues} fixed-point values and {walk.WholeValues} whole numbers compared; {walk.FixedChanged} fixed-point values and {walk.WholeChanged} whole numbers differ.");
        Print($"non-zero at 30 Hz, zero at {rate}", walk.Zero);
        Print($"relative error above 10 % against the exactly scaled 30 Hz value (zeros not repeated)", walk.AboveTen);
        Print($"relative error above 1 % up to 10 %", walk.AboveOne);
        Print($"below eight significant bits at {rate} (raw value under 256, non-zero)", walk.Coarse);
        Print($"whole numbers that differ but are not {rate.TicksPerOriginalFrame} x the 30 Hz value (a duration off the grid, or not a duration)", walk.OffGrid);
        Console.WriteLine($"  changed fixed-point values by the factor they follow: {string.Join(", ", walk.ByFactor.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} x{pair.Value}"))}.");
        return 0;
    }

    private static void Print(string title, SortedDictionary<string, Bucket> buckets)
    {
        Console.WriteLine($"  {title}: {buckets.Values.Sum(value => value.Count)} values in {buckets.Count} fields{(buckets.Count == 0 ? "." : ":")}");
        foreach (var (field, bucket) in buckets.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {field,-52} x{bucket.Count,-5} worst: {bucket.WorstText}");
    }

    private sealed class Bucket
    {
        public int Count;
        public decimal Worst = -1m;
        public string WorstText = string.Empty;

        public void Add(decimal badness, string text)
        {
            Count++;
            if (badness > Worst) (Worst, WorstText) = (badness, text);
        }
    }

    /// <summary>Walks two object graphs of the same shape in step (as <c>catalog-cost</c> does) and keeps the name of the nearest named owner for the examples.</summary>
    private sealed class Walk(int ticksPerFrame)
    {
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Type, FieldInfo[]> _fields = [];
        public long FixedValues, FixedChanged, WholeValues, WholeChanged;
        public SortedDictionary<string, Bucket> Zero { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, Bucket> AboveTen { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, Bucket> AboveOne { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, Bucket> Coarse { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, Bucket> OffGrid { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, long> ByFactor { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, Bucket> Large { get; } = new(StringComparer.Ordinal);
        public static readonly Fixed SquareLimit = Fixed.Sqrt(Fixed.MaxValue);
        public string LargestText { get; private set; } = "none";
        private Fixed _largest;

        private static Bucket Of(SortedDictionary<string, Bucket> table, string path) => table.TryGetValue(path, out var bucket) ? bucket : table[path] = new Bucket();

        public void Compare(object? left, object? right, string path, string owner)
        {
            if (left is null || right is null) return;
            switch (left)
            {
                case Fixed a when right is Fixed b:
                    FixedValues++;
                    if (a != b) Changed(a, b, path, owner);
                    Range(b, path, owner);
                    return;
                case long a when right is long b:
                    Whole(a, b, path, owner);
                    return;
                case int a when right is int b:
                    Whole(a, b, path, owner);
                    return;
                case string or bool or decimal or Enum or Delegate or Type or SimulationRate or SimulationTime:
                    return;
            }
            var type = left.GetType();
            if (type != right.GetType() || type.IsPrimitive) return;
            if (!type.IsValueType && !_seen.Add(left)) return;
            if (left is IDictionary leftTable && right is IDictionary rightTable)
            {
                foreach (var key in leftTable.Keys.OfType<string>().OrderBy(value => value, StringComparer.Ordinal))
                    if (rightTable.Contains(key)) Compare(leftTable[key], rightTable[key], path + "[]", owner);
                return;
            }
            if (left is IList leftList && right is IList rightList)
            {
                for (var index = 0; index < Math.Min(leftList.Count, rightList.Count); index++) Compare(leftList[index], rightList[index], path.EndsWith("[]", StringComparison.Ordinal) ? path : path + "[]", owner);
                return;
            }
            if (left is IEnumerable) return;
            var fields = FieldsOf(type);
            // The nearest object with a name (a template, a weapon, a locomotor) is what an example is reported under.
            var named = fields.FirstOrDefault(field => field.FieldType == typeof(string) && NameOf(field) == "Name")?.GetValue(left) as string;
            var inner = string.IsNullOrEmpty(named) ? owner : owner.Length == 0 ? named : owner + "/" + named;
            foreach (var field in fields) Compare(field.GetValue(left), field.GetValue(right), type.Name + "." + NameOf(field), inner);
        }

        private void Range(Fixed value, string path, string owner)
        {
            var magnitude = value == Fixed.MinValue ? Fixed.MaxValue : value.Abs();
            var text = string.Create(CultureInfo.InvariantCulture, $"{owner}: {value.ToDecimal():G12}");
            if (magnitude > _largest) (_largest, LargestText) = (magnitude, $"{text} ({path})");
            if (magnitude > SquareLimit) Of(Large, path).Add(magnitude.ToDecimal(), text);
        }

        private void Whole(long a, long b, string path, string owner)
        {
            WholeValues++;
            if (a == b) return;
            WholeChanged++;
            if (b != a * ticksPerFrame) Of(OffGrid, path).Add(Math.Abs(b - a * ticksPerFrame), $"{owner}: {a} at 30 Hz, {b}");
        }

        private void Changed(Fixed a, Fixed b, string path, string owner)
        {
            FixedChanged++;
            var text = string.Create(CultureInfo.InvariantCulture, $"{owner}: {a.ToDecimal():G10} at 30 Hz (raw {a.Raw}), {b.ToDecimal():G10} (raw {b.Raw})");
            if (b == Fixed.Zero)
            {
                Of(Zero, path).Add(Math.Abs(a.ToDecimal()), text);
                return;
            }
            // Per tick (÷ f), per tick² (÷ f²), a duration (× f) or a duration² (× f²): the factor whose exactly scaled value is nearest.
            decimal factor = ticksPerFrame;
            (string Name, decimal Exact)[] candidates = [("per tick (/" + ticksPerFrame + ")", a.ToDecimal() / factor), ("per tick squared (/" + ticksPerFrame * ticksPerFrame + ")", a.ToDecimal() / (factor * factor)),
                ("duration (x" + ticksPerFrame + ")", a.ToDecimal() * factor), ("duration squared (x" + ticksPerFrame * ticksPerFrame + ")", a.ToDecimal() * factor * factor)];
            var best = candidates.MinBy(candidate => Math.Abs(b.ToDecimal() - candidate.Exact));
            ByFactor[best.Name] = ByFactor.GetValueOrDefault(best.Name) + 1;
            var error = best.Exact == 0m ? 0m : Math.Abs((b.ToDecimal() - best.Exact) / best.Exact);
            var withError = string.Create(CultureInfo.InvariantCulture, $"{text}, {best.Name}, {error * 100m:F2} % off");
            if (error > 0.1m) Of(AboveTen, path).Add(error, withError);
            else if (error > 0.01m) Of(AboveOne, path).Add(error, withError);
            if (Math.Abs(b.Raw) < 256) Of(Coarse, path).Add(256 - Math.Abs(b.Raw), withError);
        }

        private static string NameOf(FieldInfo field) => field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;

        private FieldInfo[] FieldsOf(Type type)
        {
            if (_fields.TryGetValue(type, out var known)) return known;
            var all = new List<FieldInfo>();
            for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
                all.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            return _fields[type] = [..all.OrderBy(field => field.Name, StringComparer.Ordinal)];
        }
    }
}

/// <summary>
/// <c>--time-probe</c> on the commands that play a match or a mission: a <see cref="SimulationTimeProbe"/> is attached to the time service of the run's
/// rate for the first (recording) run only and reported after it. The later runs of the command (fresh runs, continuation) run without it, so the
/// command's own determinism checks also show that the probe changed nothing.
/// </summary>
internal static class TimeProbeReport
{
    public const string Option = "--time-probe";

    public static SimulationTimeProbe? Attach(string[] args, SimulationRate rate)
    {
        if (!args.Contains(Option)) return null;
        var probe = new SimulationTimeProbe();
        rate.Time.Probe = probe;
        return probe;
    }

    /// <summary>Detaches the probe and prints its counters. <paramref name="ticks"/> is the length of the probed run.</summary>
    public static void Detach(SimulationTimeProbe? probe, SimulationRate rate, long ticks)
    {
        if (probe is null) return;
        rate.Time.Probe = null;
        Console.WriteLine($"Time probe at {rate} over the first run ({ticks} ticks, {ToolRate.Minutes(ticks, rate):F1} game minutes). Counters are diagnostics, not state.");
        Console.WriteLine($"  {"counter",-22} {"function",-42} {"calls",12} {"hits",12} {"places",7}");
        foreach (var group in probe.Calls.OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Function, StringComparer.Ordinal))
        {
            var sites = probe.Sites.Where(pair => pair.Key.Kind == group.Key.Kind && pair.Key.Function == group.Key.Function).ToArray();
            Console.WriteLine($"  {group.Key.Kind,-22} {group.Key.Function,-42} {group.Value,12} {sites.Sum(pair => pair.Value),12} {sites.Length,7}");
        }
        // AtLeastOneFrame also reports durations off the grid under its own name; they have no call counter of their own.
        foreach (var extra in probe.Sites.Where(pair => !probe.Calls.ContainsKey((pair.Key.Kind, pair.Key.Function))).GroupBy(pair => (pair.Key.Kind, pair.Key.Function)).OrderBy(group => group.Key.Function, StringComparer.Ordinal))
            Console.WriteLine($"  {extra.Key.Kind,-22} {extra.Key.Function,-42} {"",12} {extra.Sum(pair => pair.Value),12} {extra.Count(),7}");
        var top = probe.Sites.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key.File, StringComparer.Ordinal).ThenBy(pair => pair.Key.Line).Take(40).ToArray();
        if (top.Length == 0) { Console.WriteLine("  no hits."); return; }
        Console.WriteLine($"  places, by hits (top {top.Length} of {probe.Sites.Count}):");
        foreach (var (site, hits) in top)
            Console.WriteLine($"    {hits,12}  {site.Kind,-22} {site.Function,-28} {Path.GetFileName(site.File.Replace('\\', '/'))}:{site.Line}");
        var catalog = probe.Quantisation.Where(pair => pair.Value.Calls > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        if (catalog.Length > 0)
            Console.WriteLine($"  decimal conversions at run time: {string.Join(", ", catalog.Select(pair => $"{pair.Key} x{pair.Value.Calls} ({pair.Value.RoundedToZero} to zero, {pair.Value.AboveOnePercent} above 1 %)"))}.");
    }
}
