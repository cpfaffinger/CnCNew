using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// A <c>ModelConditionUpgrade</c> of the template: while the upgrade condition holds, <see cref="Flag"/> is set on the drawable
/// (<c>ModelConditionUpgrade.cpp upgradeImplementation</c>). Trigger and conflict names are resolved against the world's upgrades at use.
/// </summary>
public sealed record ConditionUpgradeSpec(ModelConditionFlag Flag, ImmutableArray<string> TriggeredBy, ImmutableArray<string> ConflictsWith, bool RequiresAllTriggers);

/// <summary>Everything the presentation needs about one object template, shared by all its drawables.</summary>
public sealed record ObjectVisual(
    string TemplateName,
    ImmutableArray<string> KindOf,
    float Scale,
    ImmutableArray<string> Shadow,
    float SelectionRadius,
    string? SelectPortrait,
    string? ButtonImage,
    ImmutableArray<DrawSpec> Draws,
    ImmutableArray<ConditionUpgradeSpec> ConditionUpgrades,
    ImmutableArray<TurretMotion?> Turrets = default)
{
    public bool IsKindOf(string kind) => KindOf.Contains(kind, StringComparer.OrdinalIgnoreCase);
    public bool IsStructure => IsKindOf("STRUCTURE");
    public bool IsUnit => IsKindOf("INFANTRY") || IsKindOf("VEHICLE") || IsKindOf("AIRCRAFT") || IsKindOf("HUGE_VEHICLE");

    /// <summary>At least one draw module selects a model file (what scenery and the renderer can actually show).</summary>
    public bool HasModel => Draws.Any(draw => draw.States.Any(state => state.Model is not null));
}

/// <summary>
/// The <see cref="ObjectVisual"/> of every object of a <see cref="CompiledDefinitionGraph"/>, compiled once, with all draw diagnostics.
/// Lookups are case-insensitive like template names.
/// </summary>
public sealed class ObjectVisualCatalog
{
    private readonly ImmutableDictionary<string, ObjectVisual> _byName;

    private ObjectVisualCatalog(ImmutableArray<ObjectVisual> visuals, ImmutableArray<DrawDiagnostic> diagnostics)
    {
        Visuals = visuals;
        Diagnostics = diagnostics;
        _byName = visuals.ToImmutableDictionary(visual => visual.TemplateName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>In ordinal template-name order.</summary>
    public ImmutableArray<ObjectVisual> Visuals { get; }

    public ImmutableArray<DrawDiagnostic> Diagnostics { get; }

    public bool TryGet(string templateName, out ObjectVisual visual) => _byName.TryGetValue(templateName, out visual!);

    public ObjectVisual this[string templateName]
        => TryGet(templateName, out var visual) ? visual : throw new KeyNotFoundException($"No object template '{templateName}'.");

    public static ObjectVisualCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var diagnostics = new List<DrawDiagnostic>();
        var visuals = graph.Definitions.OfType<ObjectDefinition>()
            .OrderBy(definition => definition.OriginalName, StringComparer.Ordinal)
            .Select(definition => Compile(definition, diagnostics))
            .ToImmutableArray();
        return new ObjectVisualCatalog(visuals, [..diagnostics]);
    }

    public static ObjectVisual Compile(ObjectDefinition definition, List<DrawDiagnostic> diagnostics)
    {
        var draws = definition.Modules.Where(module => module.BlockKind == ModuleBlockKind.Draw)
            .Select(module => DrawSpecCompiler.Compile(module, diagnostics)).ToImmutableArray();
        var upgrades = ImmutableArray.CreateBuilder<ConditionUpgradeSpec>();
        foreach (var module in definition.Modules.Where(module => module.Name.Equals("ModelConditionUpgrade", StringComparison.OrdinalIgnoreCase)))
        {
            var flagText = Text(module.Fields, "ConditionFlag");
            if (flagText is null) continue;
            var token = Data.Definitions.ValueGrammar.Tokenize(flagText).FirstOrDefault() ?? string.Empty;
            if (!ModelConditions.TryParse(token, out var flag))
            {
                diagnostics.Add(new DrawDiagnostic("error", "unknown-condition", module.Name, token, "ModelConditionUpgrade ConditionFlag is not a model condition.", module.Location));
                continue;
            }
            upgrades.Add(new ConditionUpgradeSpec(flag, Names(module.Fields, "TriggeredBy"), Names(module.Fields, "ConflictsWith"),
                Text(module.Fields, "RequiresAllTriggers") is { } all && all.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase)));
        }

        var kindOf = Field(definition.Fields, "KindOf")?.Value.Value is FlagsValue flags ? flags.Names : [];
        var shadow = Field(definition.Fields, "Shadow")?.Value.Value is FlagsValue shadows ? shadows.Names : [];
        var major = Number(definition.Fields, "GeometryMajorRadius") ?? 0m;
        var minor = Number(definition.Fields, "GeometryMinorRadius") ?? 0m;
        var isBox = Field(definition.Fields, "Geometry")?.Value.Value is EnumValue { Name: "BOX" };
        var radius = isBox ? Math.Sqrt((double)(major * major + minor * minor)) : (double)major;
        return new ObjectVisual(definition.OriginalName, kindOf, (float)(Number(definition.Fields, "Scale") ?? 1m), shadow, (float)radius,
            Text(definition.Fields, "SelectPortrait"), Text(definition.Fields, "ButtonImage"), draws, upgrades.ToImmutable(), TurretMotion.FromModules(definition.Modules));
    }

    private static DefinitionField? Field(ImmutableArray<DefinitionField> fields, string name)
    {
        DefinitionField? result = null;
        foreach (var field in fields)
            if (field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) result = field;
        return result;
    }

    private static string? Text(ImmutableArray<DefinitionField> fields, string name) => Field(fields, name)?.Value.OriginalText;

    private static ImmutableArray<string> Names(ImmutableArray<DefinitionField> fields, string name)
        => [..fields.Where(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(field => Data.Definitions.ValueGrammar.Tokenize(field.Value.OriginalText))];

    private static decimal? Number(ImmutableArray<DefinitionField> fields, string name) => Field(fields, name)?.Value.Value switch
    {
        decimal value => value,
        long value => value,
        Distance value => value.Value,
        CompatibilityValue value when decimal.TryParse(value.NormalizedValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
