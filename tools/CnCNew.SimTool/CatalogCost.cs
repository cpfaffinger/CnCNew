using System.Collections;
using System.Diagnostics;
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
/// <c>catalog-cost</c>: what building the catalog costs now that it is built per tick rate, and what the conversion to a faster rate does to its values.
/// The definitions are compiled and the bone table is read once (neither depends on the rate); the catalog is then built several times for each
/// supported rate. The comparison walks the 30 Hz catalog and the catalog of another rate side by side (same definitions, same shape) and counts the
/// fixed-point values that are non-zero at 30 Hz and round to zero at the faster rate, and the ones that end up with fewer than eight significant bits.
/// A measurement, never a failing test.
/// </summary>
internal static class CatalogCost
{
    public static int Run(string[] args)
    {
        var rounds = Array.IndexOf(args, "--rounds") is var index and >= 0 && index + 1 < args.Length ? int.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture) : 5;
        var watch = Stopwatch.StartNew();
        var content = ContentFileSystem.Open(args[1]);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
        Console.WriteLine($"Definitions compiled once (independent of the rate): {watch.Elapsed.TotalMilliseconds:F0} ms.");
        watch.Restart();
        var geometry = ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);
        Console.WriteLine($"Model geometry read once (independent of the rate): {watch.Elapsed.TotalMilliseconds:F0} ms.");
        var catalogs = new Dictionary<SimulationRate, TemplateCatalog>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var times = new List<double>();
            for (var round = 0; round < rounds; round++)
            {
                watch.Restart();
                catalogs[rate] = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, geometry, ToolRate.Rules);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"TemplateCatalog.Build at {rate}: first {times[0]:F0} ms, fastest of {rounds} {times.Min():F0} ms, median {times.Order().ElementAt(times.Count / 2):F0} ms ({catalogs[rate].Templates.Length} templates).");
        }

        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var walk = new Comparison();
            walk.Compare(catalogs[SimulationRate.Hz30], catalogs[rate], "catalog");
            Console.WriteLine($"30 Hz against {rate}: {walk.FixedValues} fixed-point values and {walk.WholeValues} whole numbers compared; {walk.FixedChanged} fixed-point values and {walk.WholeChanged} whole numbers differ.");
            Console.WriteLine($"  non-zero at 30 Hz, zero at {rate}: {walk.RoundedToZero.Values.Sum()} values in {walk.RoundedToZero.Count} fields{Top(walk.RoundedToZero)}");
            Console.WriteLine($"  changed and below eight significant bits at {rate} (raw value under 256): {walk.Coarse.Values.Sum()} values in {walk.Coarse.Count} fields{Top(walk.Coarse)}");
        }
        return 0;
    }

    private static string Top(SortedDictionary<string, int> counts)
        => counts.Count == 0 ? "." : ": " + string.Join(", ", counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(12).Select(pair => $"{pair.Key} x{pair.Value}")) + (counts.Count > 12 ? ", ..." : ".");

    /// <summary>Walks two object graphs of the same shape in step. Ordered collections are walked in order; dictionaries and sets by their sorted keys where the keys are texts, otherwise they are skipped.</summary>
    private sealed class Comparison
    {
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Type, FieldInfo[]> _fields = [];
        public long FixedValues, FixedChanged, WholeValues, WholeChanged;
        public SortedDictionary<string, int> RoundedToZero { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, int> Coarse { get; } = new(StringComparer.Ordinal);

        public void Compare(object? left, object? right, string path)
        {
            if (left is null || right is null) return;
            switch (left)
            {
                case Fixed a when right is Fixed b:
                    FixedValues++;
                    if (a == b) return;
                    FixedChanged++;
                    if (a != Fixed.Zero && b == Fixed.Zero) RoundedToZero[path] = RoundedToZero.GetValueOrDefault(path) + 1;
                    else if (Math.Abs(b.Raw) < 256) Coarse[path] = Coarse.GetValueOrDefault(path) + 1;
                    return;
                case long a when right is long b:
                    WholeValues++;
                    if (a != b) WholeChanged++;
                    return;
                case int a when right is int b:
                    WholeValues++;
                    if (a != b) WholeChanged++;
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
                    if (rightTable.Contains(key)) Compare(leftTable[key], rightTable[key], path + "[]");
                return;
            }
            if (left is IList leftList && right is IList rightList)
            {
                for (var index = 0; index < Math.Min(leftList.Count, rightList.Count); index++) Compare(leftList[index], rightList[index], path.EndsWith("[]", StringComparison.Ordinal) ? path : path + "[]");
                return;
            }
            if (left is IEnumerable) return;
            foreach (var field in FieldsOf(type))
            {
                var name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;
                // The path is the declaring type and the field: where a value lives, not how it was reached.
                Compare(field.GetValue(left), field.GetValue(right), type.Name + "." + name);
            }
        }

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
