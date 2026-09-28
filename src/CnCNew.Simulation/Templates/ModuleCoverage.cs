using System.Collections.Immutable;

namespace CnCNew.Simulation.Templates;

/// <summary>How many objects a not-yet-implemented simulation module blocks.</summary>
public sealed record ModuleBlocker(string Module, int ObjectsBlocked, int ObjectsBlockedOnlyByThisModule);

public sealed record ModuleCoverageReport(
    int Objects, int Instantiable, int WithoutSimulationModules, ImmutableArray<ModuleBlocker> Blockers,
    ImmutableArray<(string Module, int Objects)> PresentationOnly)
{
    public int Blocked => Objects - Instantiable;
}

/// <summary>Module coverage audit: which objects can be instantiated today and what blocks the others (most frequent blocker first).</summary>
public static class ModuleCoverage
{
    public static ModuleCoverageReport Compute(TemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var blocked = new Dictionary<string, int>(StringComparer.Ordinal);
        var only = new Dictionary<string, int>(StringComparer.Ordinal);
        var presentation = new Dictionary<string, int>(StringComparer.Ordinal);
        var instantiable = 0;
        var withoutSimulation = 0;
        foreach (var template in catalog.Templates)
        {
            foreach (var name in template.PresentationModules.Distinct(StringComparer.Ordinal))
                presentation[name] = presentation.GetValueOrDefault(name) + 1;
            if (template.Modules.IsEmpty && template.MissingModules.IsEmpty) withoutSimulation++;
            if (template.IsInstantiable)
            {
                instantiable++;
                continue;
            }
            foreach (var module in template.MissingModules) blocked[module] = blocked.GetValueOrDefault(module) + 1;
            if (template.MissingModules.Length == 1)
                only[template.MissingModules[0]] = only.GetValueOrDefault(template.MissingModules[0]) + 1;
        }
        return new ModuleCoverageReport(catalog.Templates.Length, instantiable, withoutSimulation,
            [..blocked.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ModuleBlocker(pair.Key, pair.Value, only.GetValueOrDefault(pair.Key)))],
            [..presentation.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value))]);
    }
}
