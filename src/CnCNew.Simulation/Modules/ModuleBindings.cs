using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Modules;

public interface IModuleBinding
{
    string Name { get; }
    ModuleOwnerKind Owner { get; }

    /// <summary><paramref name="upgrades"/> lets upgrade-activated modules resolve <c>TriggeredBy</c>/<c>ConflictsWith</c> names to mask bits once, at template-build time.</summary>
    ModuleData ParseData(ModuleConfiguration configuration, UpgradeCatalog upgrades);
    SimModule Create(ModuleData data);
}

/// <summary>Typed binding of one module name: parses the compiled configuration into <typeparamref name="TData"/> and creates the module.</summary>
public sealed class ModuleBinding<TData>(string name, ModuleOwnerKind owner, Func<ModuleConfiguration, UpgradeCatalog, TData> parse, Func<TData, SimModule> create) : IModuleBinding
    where TData : ModuleData
{
    public string Name { get; } = name;
    public ModuleOwnerKind Owner { get; } = owner;
    public ModuleData ParseData(ModuleConfiguration configuration, UpgradeCatalog upgrades) => parse(configuration, upgrades);
    public SimModule Create(ModuleData data) => create((TData)data);
}

public sealed class ModuleBindingException(string message) : InvalidOperationException(message);

/// <summary>
/// Thrown by a module's data parser when the configuration asks for a feature that is not simulated yet. The template builder keeps the
/// object instantiable-checked as missing (<c>Module[Feature]</c> in <c>MissingModules</c>) instead of failing the whole catalog.
/// </summary>
public sealed class UnsupportedModuleConfigurationException(string feature) : Exception($"unsupported module configuration '{feature}'")
{
    public string Feature { get; } = feature;
}

/// <summary>
/// Explicit, ordinally sorted, reflection-free table from module name to typed factory. It lives in the simulation, not in
/// the data layer, and is validated against the data descriptors when it is constructed: unknown name, wrong owner,
/// duplicate, unsorted order, a binding for a module the catalog marks as not implemented (or presentation), and an
/// implemented simulation descriptor without binding are all errors.
/// </summary>
public sealed class ModuleBindingTable
{
    private readonly ImmutableDictionary<string, IModuleBinding> _byName;

    public ModuleBindingTable(IEnumerable<IModuleBinding> bindings, ModuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(registry);
        var list = bindings.ToArray();
        var builder = ImmutableDictionary.CreateBuilder<string, IModuleBinding>(StringComparer.OrdinalIgnoreCase);
        string? previous = null;
        foreach (var binding in list)
        {
            if (previous is not null && StringComparer.Ordinal.Compare(previous, binding.Name) > 0)
                throw new ModuleBindingException($"The binding table must be sorted ordinally: '{binding.Name}' follows '{previous}'.");
            previous = binding.Name;
            if (!builder.TryAdd(binding.Name, binding))
                throw new ModuleBindingException($"Duplicate module binding '{binding.Name}'.");
            if (!registry.TryGet(binding.Name, out var descriptor))
                throw new ModuleBindingException($"Module binding '{binding.Name}' has no data descriptor (unknown module name).");
            if (descriptor.CompatibilityOnly)
                throw new ModuleBindingException($"Module '{binding.Name}' is bound but the catalog marks it as not implemented.");
            if (descriptor.Category != ModuleCategory.Simulation)
                throw new ModuleBindingException($"Module '{binding.Name}' is a presentation module and must not be bound in the simulation.");
            if (!descriptor.AllowedOwners.Contains(binding.Owner))
                throw new ModuleBindingException($"Module '{binding.Name}' is bound for owner {binding.Owner}, which its descriptor does not allow.");
        }
        foreach (var descriptor in registry.Descriptors.Where(value => value.IsImplementedSimulationModule))
            if (!builder.ContainsKey(descriptor.Name))
                throw new ModuleBindingException($"Implemented simulation module '{descriptor.Name}' has no binding.");
        Bindings = [..list];
        _byName = builder.ToImmutable();
        Registry = registry;
    }

    public ImmutableArray<IModuleBinding> Bindings { get; }
    public ModuleRegistry Registry { get; }

    public bool TryGet(string name, out IModuleBinding binding) => _byName.TryGetValue(name, out binding!);

    /// <summary>The production table: the phase-4 lifecycle modules against <see cref="ModuleRegistry.Default"/>.</summary>
    public static ModuleBindingTable Default { get; } = new(BuiltInModules.Bindings, ModuleRegistry.Default);
}
