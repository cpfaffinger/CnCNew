using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Templates;

/// <summary>A simulation module of a template: its binding, its typed data, and its position in declaration order.</summary>
public sealed record ModuleSpec(string Name, string Tag, IModuleBinding Binding, ModuleData Data);

/// <summary>
/// The runtime view of an <c>Object</c> definition. Presentation modules (Draw, ClientUpdate) are listed but never
/// instantiated; a simulation module without implementation makes the template not instantiable and is named in
/// <see cref="MissingModules"/> (never dropped silently).
/// </summary>
public sealed class ObjectTemplate
{
    internal ObjectTemplate(int index, ObjectDefinition definition, ImmutableArray<ModuleSpec> modules,
        ImmutableArray<string> presentationModules, ImmutableArray<string> missingModules)
    {
        Index = index;
        Id = definition.Id;
        Name = definition.OriginalName;
        Modules = modules;
        PresentationModules = presentationModules;
        MissingModules = missingModules;
        Side = Text(definition, "Side");
        KindOf = definition.Fields.LastOrDefault(field => field.Name.Equals("KindOf", StringComparison.OrdinalIgnoreCase))?.Value.Value is FlagsValue flags ? flags.Names : [];
        Geometry = definition.Fields.LastOrDefault(field => field.Name.Equals("Geometry", StringComparison.OrdinalIgnoreCase))?.Value.Value is EnumValue geometry ? geometry.Name : string.Empty;
        GeometryMajorRadius = Real(definition, "GeometryMajorRadius");
        GeometryMinorRadius = Real(definition, "GeometryMinorRadius");
        GeometryHeight = Real(definition, "GeometryHeight");
    }

    /// <summary>Position in the ordinally sorted list of all object definitions (stable for one definition graph).</summary>
    public int Index { get; }
    public DefinitionId Id { get; }
    public string Name { get; }
    public string Side { get; }
    public ImmutableArray<string> KindOf { get; }
    public string Geometry { get; }
    public Fixed GeometryMajorRadius { get; }
    public Fixed GeometryMinorRadius { get; }
    public Fixed GeometryHeight { get; }
    public ImmutableArray<ModuleSpec> Modules { get; }
    public ImmutableArray<string> PresentationModules { get; }
    public ImmutableArray<string> MissingModules { get; }
    public bool IsInstantiable => MissingModules.IsEmpty;

    public bool IsKindOf(string kind) => KindOf.Contains(kind, StringComparer.OrdinalIgnoreCase);

    private static string Text(ObjectDefinition definition, string name)
        => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty;

    private static Fixed Real(ObjectDefinition definition, string name)
    {
        var field = definition.Fields.LastOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return field?.Value.Value switch
        {
            Distance distance => Fixed.FromDecimal(distance.Value),
            CompatibilityValue compatibility => Fixed.FromDecimal(decimal.Parse(compatibility.NormalizedValue, CultureInfo.InvariantCulture)),
            _ => Fixed.Zero,
        };
    }
}

public sealed class TemplateBuildException(string message) : FormatException(message);

/// <summary>All object templates of one compiled definition graph.</summary>
public sealed class TemplateCatalog
{
    private readonly ImmutableDictionary<string, ObjectTemplate> _byName;

    private TemplateCatalog(ImmutableArray<ObjectTemplate> templates)
    {
        Templates = templates;
        var builder = ImmutableDictionary.CreateBuilder<string, ObjectTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates) builder[template.Name] = template;
        _byName = builder.ToImmutable();
    }

    public ImmutableArray<ObjectTemplate> Templates { get; }
    public ObjectTemplate this[int index] => Templates[index];
    public bool TryGet(string name, out ObjectTemplate template) => _byName.TryGetValue(name, out template!);
    public int InstantiableCount => Templates.Count(value => value.IsInstantiable);

    public static TemplateCatalog Build(CompiledDefinitionGraph graph, ModuleBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(bindings);
        var templates = ImmutableArray.CreateBuilder<ObjectTemplate>();
        var index = 0;
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            var specs = ImmutableArray.CreateBuilder<ModuleSpec>();
            var presentation = ImmutableArray.CreateBuilder<string>();
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var module in definition.Modules)
            {
                if (!bindings.Registry.TryGet(module.Name, out var descriptor))
                {
                    missing.Add(module.Name);
                    continue;
                }
                if (descriptor.Category == ModuleCategory.Presentation)
                {
                    presentation.Add(module.Name);
                    continue;
                }
                if (!bindings.TryGet(module.Name, out var binding))
                {
                    missing.Add(module.Name);
                    continue;
                }
                try
                {
                    specs.Add(new ModuleSpec(module.Name, module.Tag, binding, binding.ParseData(module)));
                }
                catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException)
                {
                    throw new TemplateBuildException($"Object '{definition.OriginalName}', module '{module.Name}' ({module.Tag}): {exception.Message}");
                }
            }
            templates.Add(new ObjectTemplate(index++, definition, specs.ToImmutable(), presentation.ToImmutable(), [..missing]));
        }
        return new TemplateCatalog(templates.ToImmutable());
    }
}
