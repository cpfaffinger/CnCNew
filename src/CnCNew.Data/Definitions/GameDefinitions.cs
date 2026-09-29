using System.Collections.Immutable;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;

namespace CnCNew.Data.Definitions;

public abstract record GameDefinition(
    DefinitionId Id,
    string OriginalName,
    IniSourceLocation Location,
    ImmutableArray<DefinitionField> Fields,
    ImmutableArray<ModuleConfiguration> Modules)
{
    public ImmutableArray<DefinitionSection> Sections { get; init; } = [];
}

public sealed record DefinitionSection(
    string Declaration,
    ImmutableArray<DefinitionField> Fields,
    ImmutableArray<DefinitionSection> Children,
    IniSourceLocation Location);

public sealed record ObjectDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record WeaponDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record WeaponSetDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record ArmorDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record DamageDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record UpgradeDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record ScienceDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record PlayerDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record FactionDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record CommandButtonDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record CommandSetDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record FxListDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record FxDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record AudioEventDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record DialogEventDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record MusicTrackDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record AmbientStreamDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record LocomotorDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record ExperienceLevelDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record SpecialPowerDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record CrateDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record GlobalDataDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record CompatibilityDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);

public sealed record DefinitionReferenceEdge(DefinitionId Source, string Field, DefinitionReference Target, IniSourceLocation Location, bool Resolved, string? Fallback);

/// <summary>
/// One inventoried compatibility case. Categories: <c>definition</c> (top-level type without typed contract),
/// <c>module</c> (catalogued but not implemented module type), <c>deferred-field</c> (known key that keeps heuristic typing),
/// <c>duplicate-key</c> (scalar key repeated in one block, last wins), <c>diagnostic</c> (compiler diagnostics by code) and
/// <c>excluded-file</c> (INI file that the retail load plan does not load).
/// </summary>
public sealed record CompatibilityEntry(string Category, string Name, int Count, ImmutableArray<string> OwnerKinds, string PlannedTarget);
public sealed record DefinitionDiagnostic(string Severity, string Code, string Message, IniSourceLocation Location);
public sealed record DefinitionAudit(
    int Files, int Blocks, int Definitions, int Modules, int Keys, int References,
    int ResolvedReferences, int FallbackReferences,
    ImmutableDictionary<string, int> DefinitionKinds,
    ImmutableArray<CompatibilityEntry> Compatibility);

/// <summary>A definition that occurs in more than one INI file; <see cref="Files"/> is in load order, the last one wins.</summary>
public sealed record MultiFileDefinition(DefinitionId Id, ImmutableArray<string> Files, string Winner, bool OrderSourced);

public sealed record CompiledDefinitionGraph(
    ImmutableArray<GameDefinition> Definitions,
    ImmutableArray<DefinitionReferenceEdge> References,
    ImmutableArray<DefinitionDiagnostic> Diagnostics,
    DefinitionAudit Audit)
{
    public ImmutableArray<Compilation.PlannedFile> LoadedFiles { get; init; } = [];
    public ImmutableArray<Compilation.PlannedFile> ExcludedFiles { get; init; } = [];
    public ImmutableArray<MultiFileDefinition> MultiFileDefinitions { get; init; } = [];
}
