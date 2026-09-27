using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;

namespace CnCNew.Data.Registry;

public enum ModuleOwnerKind { Object, Fx, Global }
public sealed record ModuleFieldSchema(string Name, DefinitionValueKind Kind, bool Required = false);
public sealed record ModuleConfiguration(string Name, string Tag, ModuleOwnerKind OwnerKind, ImmutableArray<DefinitionField> Fields, IniSourceLocation Location);
public sealed record ModuleDescriptor(
    string Name,
    ImmutableHashSet<ModuleOwnerKind> AllowedOwners,
    ImmutableArray<ModuleFieldSchema> Schema,
    Func<ModuleConfiguration, object>? Factory,
    bool CompatibilityOnly = false,
    string? PlannedTarget = null);

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
    public IEnumerable<ModuleDescriptor> Descriptors => _descriptors.Values;
    public bool TryGet(string name, out ModuleDescriptor descriptor) => _descriptors.TryGetValue(name, out descriptor!);

    public ModuleDescriptor GetRequired(string name)
        => TryGet(name, out var descriptor) ? descriptor : throw new KeyNotFoundException($"Unknown module '{name}'.");
}
