using System.Collections.Immutable;

namespace CnCNew.Simulation.Templates;

/// <summary>How many objects a not-yet-implemented simulation module blocks.</summary>
public sealed record ModuleBlocker(string Module, int ObjectsBlocked, int ObjectsBlockedOnlyByThisModule);

public sealed record ModuleCoverageReport(
    int Objects, int Instantiable, int WithoutSimulationModules, ImmutableArray<ModuleBlocker> Blockers,
    ImmutableArray<(string Module, int Objects)> PresentationOnly, ImmutableArray<GreedyStep> Plan)
{
    public int Blocked => Objects - Instantiable;
}

/// <summary>One step of the greedy implementation plan: implementing <paramref name="Module"/> next frees <paramref name="Gain"/> more objects.</summary>
public sealed record GreedyStep(string Module, int Gain, int Cumulative);

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
        var plan = Plan(catalog, instantiable);
        return new ModuleCoverageReport(catalog.Templates.Length, instantiable, withoutSimulation,
            [..blocked.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ModuleBlocker(pair.Key, pair.Value, only.GetValueOrDefault(pair.Key)))],
            [..presentation.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value))], plan);
    }

    // lcm(1..20): an exact integer share of one object for every missing-set size up to 20 (integer arithmetic keeps the simulation float-free).
    private const long CreditScale = 232_792_560;

    // Fractional credit (1/|missing|) ranks modules that only unlock together; the reported gain is exact.
    private static ImmutableArray<GreedyStep> Plan(TemplateCatalog catalog, int instantiable)
    {
        var remaining = catalog.Templates.Where(value => !value.IsInstantiable)
            .Select(value => new HashSet<string>(value.MissingModules, StringComparer.Ordinal)).ToList();
        var steps = ImmutableArray.CreateBuilder<GreedyStep>();
        var cumulative = instantiable;
        while (remaining.Count > 0 && steps.Count < 40)
        {
            var score = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var missing in remaining)
                foreach (var module in missing) score[module] = score.GetValueOrDefault(module) + CreditScale / missing.Count;
            var best = score.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
            var gain = 0;
            foreach (var missing in remaining)
            {
                missing.Remove(best);
                if (missing.Count == 0) gain++;
            }
            remaining.RemoveAll(missing => missing.Count == 0);
            cumulative += gain;
            steps.Add(new GreedyStep(best, gain, cumulative));
        }
        return steps.ToImmutable();
    }
}
