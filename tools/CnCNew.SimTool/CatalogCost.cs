using System.Diagnostics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Game.Data;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// <c>catalog-cost</c>: what building the catalog costs now that it is built per tick rate. The definitions are compiled and the bone table is read once
/// (neither depends on the rate); the catalog is then built several times for each supported rate. A measurement, never a failing test.
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
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var times = new List<double>();
            var templates = 0;
            for (var round = 0; round < rounds; round++)
            {
                watch.Restart();
                var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, geometry);
                times.Add(watch.Elapsed.TotalMilliseconds);
                templates = catalog.Templates.Length;
            }
            Console.WriteLine($"TemplateCatalog.Build at {rate}: first {times[0]:F0} ms, fastest of {rounds} {times.Min():F0} ms, median {times.Order().ElementAt(times.Count / 2):F0} ms ({templates} templates).");
        }
        return 0;
    }
}
