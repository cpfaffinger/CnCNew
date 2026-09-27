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
public sealed record GlobalDataDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);
public sealed record CompatibilityDefinition(DefinitionId Id, string OriginalName, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules)
    : GameDefinition(Id, OriginalName, Location, Fields, Modules);

public sealed record DefinitionReferenceEdge(DefinitionId Source, string Field, DefinitionReference Target, IniSourceLocation Location, bool Resolved, string? Fallback);
public sealed record CompatibilityEntry(string Category, string Name, int Count, ImmutableArray<string> OwnerKinds, string PlannedTarget);
public sealed record DefinitionDiagnostic(string Severity, string Code, string Message, IniSourceLocation Location);
public sealed record DefinitionAudit(
    int Files, int Blocks, int Definitions, int Modules, int Keys, int References,
    int ResolvedReferences, int FallbackReferences,
    ImmutableDictionary<string, int> DefinitionKinds,
    ImmutableArray<CompatibilityEntry> Compatibility);

public sealed record CompiledDefinitionGraph(
    ImmutableArray<GameDefinition> Definitions,
    ImmutableArray<DefinitionReferenceEdge> References,
    ImmutableArray<DefinitionDiagnostic> Diagnostics,
    DefinitionAudit Audit);
