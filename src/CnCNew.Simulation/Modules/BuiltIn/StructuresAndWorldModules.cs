using System.Collections.Immutable;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>The bindings of the structures-and-world module group, gathered so the shared binding list needs a single entry (<c>docs/SIMULATION-CORE.md</c> §29).</summary>
internal static class StructuresAndWorldModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } = [..StructureModules.Bindings, ..RailroadModules.Bindings, ..MineModules.Bindings, ..BridgeModules.Bindings, ..WorldEffectModules.Bindings];
}
