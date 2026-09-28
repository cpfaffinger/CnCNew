using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Schema;

namespace CnCNew.Data.Registry;

public enum ModuleOwnerKind { Object, Fx, Global }

/// <summary>The INI keyword that declares a module (<c>Behavior</c>, <c>Body</c>, <c>Draw</c>, <c>ClientUpdate</c>).</summary>
public enum ModuleBlockKind { Behavior, Body, Draw, ClientUpdate }

/// <summary>Whether a module belongs to the deterministic simulation or to the presentation layer.</summary>
public enum ModuleCategory { Simulation, Presentation }

/// <summary>Interface flags of <c>Include/Common/Module.h</c> (<c>MODULEINTERFACE_*</c>).</summary>
[Flags]
public enum ModuleInterfaces
{
    None = 0, Update = 0x1, Die = 0x2, Damage = 0x4, Create = 0x8, Collide = 0x10, Body = 0x20, Contain = 0x40,
    Upgrade = 0x80, SpecialPower = 0x100, Destroy = 0x200, Draw = 0x400, ClientUpdate = 0x800,
}

public sealed record ModuleConfiguration(
    string Name,
    string Tag,
    ModuleOwnerKind OwnerKind,
    ImmutableArray<DefinitionField> Fields,
    IniSourceLocation Location,
    ImmutableArray<DefinitionSection> Sections = default,
    ModuleBlockKind BlockKind = ModuleBlockKind.Behavior);

/// <summary>
/// Engine-independent description of a module type. Descriptors carry no factory: <c>CnCNew.Simulation</c> owns the
/// binding table from module name to typed factory and validates it against these descriptors at start-up.
/// </summary>
public sealed record ModuleDescriptor(
    string Name,
    ModuleBlockKind BlockKind,
    ModuleInterfaces Interfaces,
    ImmutableHashSet<ModuleOwnerKind> AllowedOwners,
    ImmutableArray<FieldSchema> Schema,
    bool CompatibilityOnly,
    string? PlannedTarget,
    ModuleCategory Category)
{
    public bool IsImplementedSimulationModule => !CompatibilityOnly && Category == ModuleCategory.Simulation;
}

public sealed class ModuleRegistry
{
    private readonly ImmutableSortedDictionary<string, ModuleDescriptor> _descriptors;

    public ModuleRegistry(IEnumerable<ModuleDescriptor> descriptors)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, ModuleDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in descriptors.OrderBy(value => value.Name, StringComparer.Ordinal))
            if (!builder.TryAdd(descriptor.Name, descriptor))
                throw new InvalidOperationException($"Duplicate module registry entry '{descriptor.Name}'.");
        _descriptors = builder.ToImmutable();
    }

    public static ModuleRegistry Empty { get; } = new([]);

    /// <summary>The checked-in catalog of every module type of the shipped data.</summary>
    public static ModuleRegistry Default => ModuleCatalog.Registry;

    public IEnumerable<ModuleDescriptor> Descriptors => _descriptors.Values;
    public bool TryGet(string name, out ModuleDescriptor descriptor) => _descriptors.TryGetValue(name, out descriptor!);

    public ModuleDescriptor GetRequired(string name)
        => TryGet(name, out var descriptor) ? descriptor : throw new KeyNotFoundException($"Unknown module '{name}'.");
}
