using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;

namespace CnCNew.Data.Compilation;

public interface IDefinitionSource
{
    IEnumerable<string> EnumerateIniFiles();
    Stream OpenRead(string virtualPath);
    bool Exists(string virtualPath);
}

public sealed record DefinitionCompilerOptions(
    ImmutableArray<string> IncludeSearchPaths,
    ImmutableHashSet<string> Conditions,
    bool StrictUnknownDefinitions = false,
    bool StrictUnknownModules = false,
    bool StrictReferences = false,
    bool StrictObjectKeys = true,
    bool StrictLoadPlan = false,
    string Language = "English",
    MapOverlayMode MapOverlays = MapOverlayMode.Include)
{
    public static DefinitionCompilerOptions Audit { get; } = new(
        ["Data/INI"],
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase));
}

public sealed class DefinitionCompileException(string message, IniSourceLocation location)
    : FormatException($"{location.Source}({location.Line},{location.Column}): {message}")
{
    public IniSourceLocation Location { get; } = location;
}

/// <summary>
/// Compiles SAGE INI into the immutable definition graph. Semantics follow the retail loader of the reference sources:
/// <see cref="OriginalLoadPlan"/> for file order, <c>ThingFactory::newTemplate</c> (copy of <c>DefaultThingTemplate</c>),
/// <c>ThingTemplate::parseModuleName</c>/<c>clearCopiedFromDefaultEntries</c> for default module inheritance and
/// <c>parseAdd/Remove/ReplaceModule</c> for module edits.
/// </summary>
public static class DefinitionCompiler
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly DefinitionId DefaultThingTemplate = new(DefinitionKind.Object, "DefaultThingTemplate");

    public static CompiledDefinitionGraph Compile(
        IDefinitionSource source,
        ModuleRegistry? modules = null,
        DefinitionCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        modules ??= ModuleRegistry.Empty;
        options ??= DefinitionCompilerOptions.Audit;

        var plan = OriginalLoadPlan.Plan(source.EnumerateIniFiles(), options.Language, options.MapOverlays, options.StrictLoadPlan);
        var documents = new List<LoadedDocument>();
        var included = new HashSet<string>(Names);
        foreach (var file in plan.Loaded)
            LoadDocument(source, file.Path, file, options, documents, included, new Stack<string>());

        var context = new Context(options, modules);
        foreach (var loaded in documents)
            foreach (var block in SelectActive(loaded.Document.Nodes, options.Conditions, loaded.Document.Source).OfType<IniBlock>())
                CompileDefinition(context, block, loaded);
        return Finish(context, documents, plan);
    }

    // ------------------------------------------------------------------------------------------------ definitions

    private static void CompileDefinition(Context c, IniBlock block, LoadedDocument doc)
    {
        c.Blocks += DescendantCount(block);
        var declaration = ParseDeclaration(block.Declaration, block.Location);
        var declType = declaration.Type;
        var isReskin = declType.Equals("ObjectReskin", StringComparison.OrdinalIgnoreCase);
        if (!TryMapKind(declType, out var kind))
        {
            CountCompat(c.UnknownDefinitions, declType, "top-level", "phase-specific typed definition compiler");
            if (c.Options.StrictUnknownDefinitions)
                throw new DefinitionCompileException($"Unknown top-level definition type '{declType}'.", block.Location);
            kind = DefinitionKind.Compatibility;
        }

        var name = declaration.Name ?? (kind is DefinitionKind.GameData or DefinitionKind.GlobalData or DefinitionKind.Compatibility ? declType : null);
        if (string.IsNullOrWhiteSpace(name))
            throw new DefinitionCompileException($"Definition '{declType}' requires a name.", block.Location);
        var id = IdFor(kind, declType, name);

        DefinitionState state;
        if (isReskin)
        {
            var parentId = IdFor(kind, declType, declaration.Parent!);
            if (!c.States.TryGetValue(parentId, out var parent))
                throw new DefinitionCompileException($"ObjectReskin must come after the original Object '{declaration.Parent}'.", block.Location);
            state = parent.Clone(markCopiedFromDefault: true);
        }
        else if (declaration.Parent is not null)
        {
            c.InheritanceDeclarations++;
            var parentId = IdFor(kind, declType, declaration.Parent);
            if (!c.States.TryGetValue(parentId, out var parent))
                throw new DefinitionCompileException($"Base definition '{parentId}' was not found.", block.Location);
            state = parent.Clone(markCopiedFromDefault: false);
        }
        else if (c.States.TryGetValue(id, out var existing))
            state = existing;
        else if (kind == DefinitionKind.Object && c.States.TryGetValue(DefaultThingTemplate, out var template))
            state = template.Clone(markCopiedFromDefault: true);
        else
            state = new DefinitionState();

        state.Kind = kind;
        state.Name = name;
        state.DeclarationType = declType;
        state.Location = block.Location;
        state.IsReskin = isReskin;
        c.States[id] = state;
        if (!c.FileTrace.TryGetValue(id, out var trace)) c.FileTrace[id] = trace = [];
        trace.Add(doc.File);

        ProcessChildren(c, state, block.Children, doc, ModuleParseMode.Normal, new BlockScope(), null);
    }

    private static DefinitionId IdFor(DefinitionKind kind, string declarationType, string name)
        => kind == DefinitionKind.Compatibility
            ? new DefinitionId(kind, $"{declarationType.ToLowerInvariant()}/{name}")
            : new DefinitionId(kind, name);

    private enum ModuleParseMode { Normal, Add, Replace, Inheritable, OverrideableByLikeKind }

    private sealed class BlockScope
    {
        public HashSet<string> SeenKeys { get; } = new(Names);
    }

    private sealed record ReplaceTarget(string Tag, string ModuleName);

    private static void ProcessChildren(Context c, DefinitionState state, IEnumerable<IniNode> nodes, LoadedDocument doc,
        ModuleParseMode mode, BlockScope scope, ReplaceTarget? replace)
    {
        foreach (var node in SelectActive(nodes, c.Options.Conditions, doc.Document.Source))
        {
            switch (node)
            {
                case IniProperty property:
                    ApplyProperty(c, state, property, scope);
                    break;
                case IniStatement statement:
                    ApplyStatement(c, state, statement);
                    break;
                case IniBlock child:
                    ApplyBlock(c, state, child, doc, mode, scope, replace);
                    break;
            }
        }
    }

    private static void ApplyStatement(Context c, DefinitionState state, IniStatement statement)
    {
        var declaration = ParseDeclaration(statement.Text, statement.Location);
        if (!declaration.Type.Equals("RemoveModule", StringComparison.OrdinalIgnoreCase)) return;
        if (declaration.Name is null) throw new DefinitionCompileException("RemoveModule requires a tag.", statement.Location);
        RemoveModule(c, state, declaration.Name, statement.Location);
    }

    private static void RemoveModule(Context c, DefinitionState state, string tag, IniSourceLocation location)
    {
        var removed = state.Modules.RemoveAll(slot => slot.Config.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-missing-remove-target",
                $"RemoveModule target '{tag}' was not present in this aggregate; nothing was removed.", location));
    }

    private static void ApplyBlock(Context c, DefinitionState state, IniBlock block, LoadedDocument doc,
        ModuleParseMode mode, BlockScope scope, ReplaceTarget? replace)
    {
        var declaration = ParseDeclaration(block.Declaration, block.Location);
        var type = declaration.Type;
        if (IsModuleBlock(type))
        {
            AddModule(c, state, block, doc, mode, replace);
            return;
        }
        if (type.Equals("RemoveModule", StringComparison.OrdinalIgnoreCase))
        {
            if (declaration.Name is null) throw new DefinitionCompileException("RemoveModule requires a tag.", block.Location);
            RemoveModule(c, state, declaration.Name, block.Location);
            return;
        }
        if (type.Equals("AddModule", StringComparison.OrdinalIgnoreCase))
        {
            if (!ContainsModuleBlock(c, block, doc))
                c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-add-module-without-module",
                    "AddModule contains no module block; its fields are applied to the definition as the reference parser does.", block.Location));
            ProcessChildren(c, state, block.Children, doc, ModuleParseMode.Add, scope, null);
            return;
        }
        if (type.Equals("ReplaceModule", StringComparison.OrdinalIgnoreCase))
        {
            if (declaration.Name is null) throw new DefinitionCompileException("ReplaceModule requires a tag.", block.Location);
            var index = state.Modules.FindIndex(slot => slot.Config.Tag.Equals(declaration.Name, StringComparison.OrdinalIgnoreCase));
            ReplaceTarget? target = null;
            if (index < 0)
                c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-missing-replace-target",
                    $"ReplaceModule target '{declaration.Name}' was not present in this aggregate map overlay; the replacement is added.", block.Location));
            else
            {
                target = new ReplaceTarget(declaration.Name, state.Modules[index].Config.Name);
                state.Modules.RemoveAt(index);
            }
            ProcessChildren(c, state, block.Children, doc, ModuleParseMode.Replace, scope, target);
            return;
        }
        if (type.Equals("InheritableModule", StringComparison.OrdinalIgnoreCase))
        {
            ProcessChildren(c, state, block.Children, doc, ModuleParseMode.Inheritable, scope, null);
            return;
        }
        if (type.Equals("OverrideableByLikeKind", StringComparison.OrdinalIgnoreCase))
        {
            ProcessChildren(c, state, block.Children, doc, ModuleParseMode.OverrideableByLikeKind, scope, null);
            return;
        }

        var section = CompileSection(c, block, doc);
        if (state.Kind == DefinitionKind.Object)
        {
            if (type.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase) && state.WeaponSetsFromDefault)
            {
                state.WeaponSetsFromDefault = false;
                state.Sections.RemoveAll(value => SectionType(value).Equals("WeaponSet", StringComparison.OrdinalIgnoreCase));
            }
            else if (type.Equals("ArmorSet", StringComparison.OrdinalIgnoreCase) && state.ArmorSetsFromDefault)
            {
                state.ArmorSetsFromDefault = false;
                state.Sections.RemoveAll(value => SectionType(value).Equals("ArmorSet", StringComparison.OrdinalIgnoreCase));
            }
            else if (type.Equals("UnitSpecificSounds", StringComparison.OrdinalIgnoreCase) || type.Equals("UnitSpecificFX", StringComparison.OrdinalIgnoreCase))
                state.Sections.RemoveAll(value => SectionType(value).Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        state.Sections.Add(section);
    }

    private static string SectionType(DefinitionSection section)
        => section.Declaration.Split([' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static bool ContainsModuleBlock(Context c, IniBlock block, LoadedDocument doc)
        => SelectActive(block.Children, c.Options.Conditions, doc.Document.Source).OfType<IniBlock>()
            .Any(child => IsModuleBlock(ParseDeclaration(child.Declaration, child.Location).Type));

    // ---------------------------------------------------------------------------------------------- properties

    private static void ApplyProperty(Context c, DefinitionState state, IniProperty property, BlockScope scope)
    {
        c.Keys++;
        var key = property.Name;
        var repeat = RepeatPolicy.Overwrite;
        DefinitionValue value;
        if (state.Kind == DefinitionKind.Object && (state.DeclarationType.Equals("Object", StringComparison.OrdinalIgnoreCase) || state.IsReskin))
        {
            if (state.IsReskin && !ObjectSchema.ReskinKeys.Contains(key))
                throw new DefinitionCompileException($"ObjectReskin may only set appearance keys; '{key}' is not one of them.", property.Location);
            if (ObjectSchema.Object.TryGet(key, out var schema))
            {
                if (schema.Type == FieldType.Opaque)
                    CountCompat(c.DeferredFields, $"Object/{schema.Name}", "Object", "phase-5-object-field-typing");
                value = schema.Parse(property.Value, property.Location);
                repeat = schema.Repeat;
                if (schema.IgnoredTokenCount(property.Value) is > 0 and var ignored)
                    c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-ignored-tokens",
                        $"'{key}' = '{property.Value.Trim()}' has {ignored} trailing token(s) that the reference parser reads past and ignores.", property.Location));
            }
            else if (c.Options.StrictObjectKeys)
                throw new DefinitionCompileException($"Unknown Object key '{key}'; it is not part of ThingTemplate::s_objectFieldParseTable.", property.Location);
            else
            {
                CountCompat(c.DeferredFields, $"Object/{key}", "Object", "unknown-object-key");
                value = DefinitionValueParser.Parse(key, property.Value, property.Location);
            }
        }
        else if (DomainSchemas.For(state.Kind) is { } domainSchema && domainSchema.TryGet(key, out var domainField))
        {
            if (domainField.Type == FieldType.Opaque)
                CountCompat(c.DeferredFields, $"{domainSchema.Name}/{domainField.Name}", domainSchema.Name, "phase-5-domain-field-typing");
            value = domainField.Parse(property.Value, property.Location);
            repeat = domainField.Repeat;
            if (domainField.IgnoredTokenCount(property.Value) is > 0 and var ignoredDomainTokens)
                c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-ignored-tokens",
                    $"'{key}' = '{property.Value.Trim()}' has {ignoredDomainTokens} trailing token(s) that the reference parser reads past and ignores.", property.Location));
        }
        else
        {
            repeat = RepeatableKeys.Lookup(state.DeclarationType, key);
            value = state.Kind == DefinitionKind.CommandSet && int.TryParse(key, out _)
                ? new DefinitionValue(DefinitionValueKind.Reference, new DefinitionReference(DefinitionKind.CommandButton, property.Value.Trim()), property.Value)
                : DefinitionValueParser.Parse(key, property.Value, property.Location);
            // A domain that has a (partial) typed schema but doesn't know this particular key: visible, not silent.
            if (DomainSchemas.For(state.Kind) is not null)
                CountCompat(c.DeferredFields, $"{state.DeclarationType}/{key}", state.DeclarationType, "phase-5-domain-field-typing");
        }

        if (value.Value is CompatibilityValue compatibility)
            c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-value",
                $"'{key}' = '{property.Value.Trim()}' is accepted as {compatibility.NormalizedValue} ({compatibility.Code}).", property.Location));
        if (value.Value is FlagsValue { IsRelative: true } relative)
        {
            var previous = state.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.Value as FlagsValue;
            value = value with { Value = relative.ResolveAgainst(previous) };
        }

        var repeated = !scope.SeenKeys.Add(key);
        var field = new DefinitionField(key, value, property.Location);
        switch (repeat)
        {
            case RepeatPolicy.Accumulate:
                state.Fields.Add(field);
                break;
            case RepeatPolicy.IndexedByFirstToken:
            case RepeatPolicy.IndexedByFirstTwoTokens:
                var width = repeat == RepeatPolicy.IndexedByFirstToken ? 1 : 2;
                var index = IndexKey(property.Value, width);
                var slot = state.Fields.FindIndex(existing => existing.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                    && IndexKey(existing.Value.OriginalText, width).Equals(index, StringComparison.OrdinalIgnoreCase));
                if (slot >= 0) state.Fields[slot] = field; else state.Fields.Add(field);
                break;
            default:
                if (repeated)
                {
                    CountCompat(c.DuplicateKeys, $"{state.DeclarationType}/{key}", state.DeclarationType, "last-wins");
                    c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-duplicate-key",
                        $"Key '{key}' occurs more than once in one '{state.DeclarationType}' block; the last value wins.", property.Location));
                }
                state.Fields.RemoveAll(existing => existing.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                state.Fields.Add(field);
                break;
        }
    }

    private static string IndexKey(string text, int width)
        => string.Join(' ', text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Take(width));

    // -------------------------------------------------------------------------------------------------- modules

    private static void AddModule(Context c, DefinitionState state, IniBlock block, LoadedDocument doc, ModuleParseMode mode, ReplaceTarget? replace)
    {
        var declaration = ParseModuleDeclaration(block.Declaration, block.Location);
        c.Registry.TryGet(declaration.Name, out var descriptor);
        var owner = state.Kind == DefinitionKind.Object ? ModuleOwnerKind.Object
            : state.Kind is DefinitionKind.Fx or DefinitionKind.FxList ? ModuleOwnerKind.Fx : ModuleOwnerKind.Global;
        var config = CompileModule(c, block, declaration, owner, descriptor, doc);

        if (replace is not null)
        {
            if (!replace.ModuleName.Equals(declaration.Name, StringComparison.OrdinalIgnoreCase))
                throw new DefinitionCompileException($"ReplaceModule must replace a '{replace.ModuleName}' with a module of the same type, not '{declaration.Name}'.", block.Location);
            if (replace.Tag.Equals(declaration.Tag, StringComparison.OrdinalIgnoreCase))
                throw new DefinitionCompileException($"ReplaceModule must give the replacement a new unique tag; '{declaration.Tag}' is reused.", block.Location);
        }

        // ThingTemplate::parseModuleName: load types other than CREATE_OVERRIDES first drop default modules that
        // share an interface with the new module.
        if (!doc.IsOverride && descriptor is not null)
            ClearCopiedFromDefault(state, descriptor.Interfaces, declaration.Name);

        var slot = new ModuleSlot(config)
        {
            Interfaces = descriptor?.Interfaces ?? ModuleInterfaces.None,
            Inheritable = mode == ModuleParseMode.Inheritable,
            OverrideableByLikeKind = mode == ModuleParseMode.OverrideableByLikeKind,
        };
        // The retail build keeps modules with a repeated tag (only the debug build asserts and throws), so both stay; the
        // repeat is diagnosed and counted because Remove/ReplaceModule by tag becomes ambiguous.
        if (state.Modules.Any(value => value.Config.Tag.Equals(declaration.Tag, StringComparison.OrdinalIgnoreCase)))
            c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-duplicate-module-tag",
                $"Module tag '{declaration.Tag}' is used twice in one object; both modules are kept like the retail build does.", block.Location));
        state.Modules.Add(slot);
        c.ModuleCount++;
    }

    private static readonly ImmutableArray<string> ImmuneToScramblerKinds =
    [
        "AIRCRAFT", "SHRUBBERY", "OPTIMIZED_TREE", "STRUCTURE", "DRAWABLE_ONLY", "MOB_NEXUS", "IGNORED_IN_GUI", "CLEARED_BY_BUILD",
        "DEFENSIVE_WALL", "BALLISTIC_MISSILE", "SUPPLY_SOURCE", "BOAT", "INERT", "BRIDGE", "LANDMARK_BRIDGE", "BRIDGE_TOWER",
    ];
    private static readonly ImmutableArray<string> ScramblerCandidateKinds = ["SCORE", "VEHICLE", "INFANTRY", "PORTABLE_STRUCTURE"];

    // ModuleInfo::clearCopiedFromDefaultEntries (ThingTemplate.cpp).
    private static void ClearCopiedFromDefault(DefinitionState state, ModuleInterfaces newInterfaces, string newName)
    {
        var kinds = state.Fields.LastOrDefault(field => field.Name.Equals("KindOf", StringComparison.OrdinalIgnoreCase))?.Value.Value as FlagsValue;
        var disallowed = kinds is not null && ImmuneToScramblerKinds.Any(kinds.Contains);
        var candidate = kinds is not null && ScramblerCandidateKinds.Any(kinds.Contains);
        var trainable = state.Fields.LastOrDefault(field => field.Name.Equals("IsTrainable", StringComparison.OrdinalIgnoreCase))?.Value.Value is true;
        for (var index = 0; index < state.Modules.Count;)
        {
            var slot = state.Modules[index];
            if (!slot.CopiedFromDefault || (slot.Interfaces & newInterfaces) == 0) { index++; continue; }
            bool remove;
            if (slot.Inheritable)
                remove = slot.Config.Tag == "ModuleTag_DefaultAutoHealBehavior" && !trainable;
            else if (slot.OverrideableByLikeKind)
                remove = slot.Config.Name.Equals(newName, StringComparison.Ordinal) || disallowed || !candidate;
            else
                remove = true;
            if (remove) state.Modules.RemoveAt(index); else index++;
        }
    }

    private static ModuleConfiguration CompileModule(Context c, IniBlock block, ModuleDeclaration declaration, ModuleOwnerKind owner,
        ModuleDescriptor? descriptor, LoadedDocument doc)
    {
        var implemented = descriptor is { CompatibilityOnly: false };
        var fieldBuilder = ImmutableArray.CreateBuilder<DefinitionField>();
        var seen = new HashSet<string>(Names);
        var active = SelectActive(block.Children, c.Options.Conditions, doc.Document.Source).ToArray();
        foreach (var property in active.OfType<IniProperty>())
        {
            c.Keys++;
            if (implemented)
            {
                var schema = descriptor!.Schema.FirstOrDefault(value => value.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new DefinitionCompileException($"Unknown field '{property.Name}' for module '{declaration.Name}'.", property.Location);
                if (!seen.Add(property.Name) && schema.Repeat == RepeatPolicy.Overwrite)
                    throw new DefinitionCompileException($"Field '{property.Name}' of module '{declaration.Name}' is set twice.", property.Location);
                var parsed = schema.Parse(property.Value, property.Location);
                if (schema.Type == FieldType.Opaque)
                    CountCompat(c.DeferredFields, $"{declaration.Name}/{schema.Name}", declaration.Name, "phase-5-module-field-typing");
                if (schema.Type == FieldType.DurationMs && property.Value.TrimStart().StartsWith('-'))
                    c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-unsigned-wrap",
                        $"'{property.Name}' = '{property.Value.Trim()}' wraps modulo 2^32 like sscanf(\"%u\") in the reference parser.", property.Location));
                fieldBuilder.Add(new DefinitionField(schema.Name, parsed, property.Location));
            }
            else
                fieldBuilder.Add(new DefinitionField(property.Name, DefinitionValueParser.Parse(property.Name, property.Value, property.Location), property.Location));
        }
        var fields = fieldBuilder.ToImmutable();
        var sectionBuilder = ImmutableArray.CreateBuilder<DefinitionSection>();
        foreach (var child in active.OfType<IniBlock>())
            sectionBuilder.Add(CompileSection(c, child, doc));
        var sections = sectionBuilder.ToImmutable();

        if (descriptor is null)
        {
            CountCompat(c.UnknownModules, declaration.Name, owner.ToString(), "unknown-module-not-in-catalog");
            c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-unknown-module", $"Module '{declaration.Name}' is not part of the module catalog.", block.Location));
            if (c.Options.StrictUnknownModules) throw new DefinitionCompileException($"Unknown module '{declaration.Name}'.", block.Location);
        }
        else
        {
            if (!descriptor.AllowedOwners.Contains(owner))
                throw new DefinitionCompileException($"Module '{declaration.Name}' is not allowed on {owner}.", block.Location);
            if (descriptor.BlockKind != declaration.BlockKind)
                throw new DefinitionCompileException($"Module '{declaration.Name}' must be declared with '{descriptor.BlockKind}', not '{declaration.BlockKind}'.", block.Location);
            if (descriptor.CompatibilityOnly)
            {
                CountCompat(c.UnknownModules, declaration.Name, owner.ToString(), descriptor.PlannedTarget ?? "unplanned");
                c.Diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-module", $"Module '{declaration.Name}' is catalogued but has no runtime implementation.", block.Location));
            }
            else
            {
                foreach (var required in descriptor.Schema.Where(value => value.Required))
                    if (!fields.Any(field => field.Name.Equals(required.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new DefinitionCompileException($"Module '{declaration.Name}' requires field '{required.Name}'.", block.Location);
                if (sections.Length > 0 && !descriptor.AllowsNestedSections)
                    throw new DefinitionCompileException($"Module '{declaration.Name}' does not allow nested blocks.", sections[0].Location);
            }
        }
        return new ModuleConfiguration(declaration.Name, declaration.Tag, owner, fields, block.Location, sections, declaration.BlockKind);
    }

    private static DefinitionSection CompileSection(Context c, IniBlock block, LoadedDocument doc)
    {
        var active = SelectActive(block.Children, c.Options.Conditions, doc.Document.Source).ToArray();
        var fields = ImmutableArray.CreateBuilder<DefinitionField>();
        foreach (var property in active.OfType<IniProperty>())
        {
            c.Keys++;
            fields.Add(new DefinitionField(property.Name, DefinitionValueParser.Parse(property.Name, property.Value, property.Location), property.Location));
        }
        var children = ImmutableArray.CreateBuilder<DefinitionSection>();
        foreach (var nested in active.OfType<IniBlock>()) children.Add(CompileSection(c, nested, doc));
        return new DefinitionSection(block.Declaration, fields.ToImmutable(), children.ToImmutable(), block.Location);
    }

    // ---------------------------------------------------------------------------------------------- finishing

    private static CompiledDefinitionGraph Finish(Context c, List<LoadedDocument> documents, LoadPlanResult plan)
    {
        var definitions = c.States.Select(pair => CreateDefinition(pair.Key, pair.Value))
            .OrderBy(value => value.Id.ToString(), StringComparer.Ordinal).ToImmutableArray();
        var byId = definitions.ToDictionary(value => value.Id);
        var references = new List<DefinitionReferenceEdge>();
        foreach (var definition in definitions)
        foreach (var (fieldPath, field) in EnumerateFields(definition))
        {
            if (field.Value.Kind != DefinitionValueKind.Reference) continue;
            var fieldReferences = field.Value.Value switch
            {
                DefinitionReference single => [single],
                ImmutableArray<DefinitionReference> multiple => multiple,
                TaggedReferenceList tagged => tagged.References,
                _ => throw new InvalidDataException($"Invalid reference payload for '{definition.Id}.{field.Name}'."),
            };
            foreach (var reference in fieldReferences)
            {
                if (reference.IsNone) continue;
                var target = new DefinitionId(reference.Kind, reference.Name);
                var resolved = byId.ContainsKey(target);
                var fallback = resolved ? null
                    : IsEngineVeterancyUpgrade(reference) ? "compatibility-engine-veterancy-upgrade"
                    : "compatibility-unresolved-reference";
                if (!resolved && c.Options.StrictReferences)
                    throw new DefinitionCompileException($"Missing {reference.Kind} reference '{reference.Name}' in '{definition.Id}'.", field.Location);
                references.Add(new DefinitionReferenceEdge(definition.Id, fieldPath, reference, field.Location, resolved, fallback));
            }
        }

        var compatibility = new List<CompatibilityEntry>();
        compatibility.AddRange(c.UnknownDefinitions.Select(pair => ToEntry("definition", pair)));
        compatibility.AddRange(c.UnknownModules.Select(pair => ToEntry("module", pair)));
        compatibility.AddRange(c.DeferredFields.Select(pair => ToEntry("deferred-field", pair)));
        compatibility.AddRange(c.DuplicateKeys.Select(pair => ToEntry("duplicate-key", pair)));
        foreach (var group in c.Diagnostics.GroupBy(value => value.Code))
            compatibility.Add(new CompatibilityEntry("diagnostic", group.Key, group.Count(), [], "explained-in-docs"));
        foreach (var file in plan.Excluded)
            compatibility.Add(new CompatibilityEntry("excluded-file", file.Path, 1, [file.Class.ToString()], file.Reason));
        if (c.InheritanceDeclarations > 0)
            compatibility.Add(new CompatibilityEntry("diagnostic", "non-original-inheritance-syntax", c.InheritanceDeclarations, [], "extension-of-the-reference-syntax"));
        var sortedCompatibility = compatibility
            .OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();

        var kindCounts = definitions.GroupBy(value => value.Id.Kind.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var edges = references.OrderBy(value => value.Source.ToString(), StringComparer.Ordinal)
            .ThenBy(value => value.Field, StringComparer.Ordinal).ThenBy(value => value.Target.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var audit = new DefinitionAudit(documents.Select(value => value.Document.Source).Distinct(Names).Count(), c.Blocks,
            definitions.Length, c.ModuleCount, c.Keys, edges.Length, edges.Count(value => value.Resolved),
            edges.Count(value => !value.Resolved), kindCounts, sortedCompatibility);

        var multi = c.FileTrace.Where(pair => pair.Value.Count > 1)
            .Select(pair => new MultiFileDefinition(pair.Key, [..pair.Value.Select(file => file.Path)], pair.Value[^1].Path, OrderIsSourced(pair.Value)))
            .OrderBy(value => value.Id.ToString(), StringComparer.Ordinal).ToImmutableArray();
        return new CompiledDefinitionGraph(definitions, edges, c.Diagnostics.ToImmutableArray(), audit)
        {
            LoadedFiles = plan.Loaded,
            ExcludedFiles = plan.Excluded,
            MultiFileDefinitions = multi,
        };
    }

    // Upgrade_Veterancy_ELITE/HEROIC are engine-internal upgrade names (UpgradeMux triggers of the default AutoHeal
    // module and a few objects); they have no INI definition. Kept visible as their own compatibility case.
    private static bool IsEngineVeterancyUpgrade(DefinitionReference reference)
        => reference.Kind == DefinitionKind.Upgrade
            && (reference.Name.Equals("Upgrade_Veterancy_ELITE", StringComparison.OrdinalIgnoreCase)
                || reference.Name.Equals("Upgrade_Veterancy_HEROIC", StringComparison.OrdinalIgnoreCase));

    private static bool OrderIsSourced(List<PlannedFile> files)
    {
        for (var index = 1; index < files.Count; index++)
        {
            var previous = files[index - 1];
            var current = files[index];
            if (previous.Path.Equals(current.Path, StringComparison.OrdinalIgnoreCase)) continue;
            if (current.Class == IniFileClass.MapOverlay || previous.Class == IniFileClass.MapOverlay) continue;
            if (previous.Subsystem == current.Subsystem || (previous.OrderSourced && current.OrderSourced)) continue;
            return false;
        }
        return true;
    }

    private static GameDefinition CreateDefinition(DefinitionId id, DefinitionState state)
    {
        var fields = state.Fields.ToImmutableArray();
        var modules = state.Modules.Select(slot => slot.Config).ToImmutableArray();
        GameDefinition definition = state.Kind switch
        {
            DefinitionKind.Object => new ObjectDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Weapon => new WeaponDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.WeaponSet => new WeaponSetDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Armor => new ArmorDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Damage => new DamageDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Upgrade => new UpgradeDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Science => new ScienceDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Player => new PlayerDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Faction => new FactionDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.CommandButton => new CommandButtonDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.CommandSet => new CommandSetDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.FxList => new FxListDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Fx => new FxDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.AudioEvent => new AudioEventDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.DialogEvent => new DialogEventDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.MusicTrack => new MusicTrackDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.AmbientStream => new AmbientStreamDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.Locomotor => new LocomotorDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.ExperienceLevel => new ExperienceLevelDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.SpecialPower => new SpecialPowerDefinition(id, state.Name, state.Location, fields, modules),
            DefinitionKind.GameData or DefinitionKind.GlobalData => new GlobalDataDefinition(id, state.Name, state.Location, fields, modules),
            _ => new CompatibilityDefinition(id, state.Name, state.Location, fields, modules),
        };
        return definition with { Sections = state.Sections.ToImmutableArray() };
    }

    private static IEnumerable<(string Path, DefinitionField Field)> EnumerateFields(GameDefinition definition)
    {
        foreach (var field in definition.Fields) yield return (field.Name, field);
        for (var index = 0; index < definition.Sections.Length; index++)
            foreach (var result in EnumerateSectionFields(definition.Sections[index], $"section[{index}]")) yield return result;
        for (var moduleIndex = 0; moduleIndex < definition.Modules.Length; moduleIndex++)
        {
            var module = definition.Modules[moduleIndex];
            var prefix = $"module[{moduleIndex}]:{module.Tag}";
            foreach (var field in module.Fields) yield return ($"{prefix}/{field.Name}", field);
            if (module.Sections.IsDefault) continue;
            for (var sectionIndex = 0; sectionIndex < module.Sections.Length; sectionIndex++)
                foreach (var result in EnumerateSectionFields(module.Sections[sectionIndex], $"{prefix}/section[{sectionIndex}]")) yield return result;
        }
    }

    private static IEnumerable<(string Path, DefinitionField Field)> EnumerateSectionFields(DefinitionSection section, string prefix)
    {
        var path = $"{prefix}:{section.Declaration}";
        foreach (var field in section.Fields) yield return ($"{path}/{field.Name}", field);
        for (var index = 0; index < section.Children.Length; index++)
            foreach (var result in EnumerateSectionFields(section.Children[index], $"{path}/section[{index}]")) yield return result;
    }

    // -------------------------------------------------------------------------------------------- file loading

    private static void LoadDocument(IDefinitionSource source, string path, PlannedFile file, DefinitionCompilerOptions options,
        List<LoadedDocument> output, HashSet<string> included, Stack<string> stack)
    {
        if (stack.Contains(path, Names))
            throw new DefinitionCompileException($"Include cycle: {string.Join(" -> ", stack.Reverse())} -> {path}.", new(path, 1, 1));
        if (!included.Add(path)) return;
        if (!source.Exists(path)) throw new FileNotFoundException($"INI source '{path}' does not exist.", path);
        stack.Push(path);
        using var stream = source.OpenRead(path);
        var document = SageIniParser.Parse(stream, path);
        foreach (var statement in document.Nodes.OfType<IniStatement>().Where(value => IsInclude(value.Text)))
        {
            var target = ParseInclude(statement.Text, statement.Location);
            var resolved = ResolveInclude(source, path, target, options.IncludeSearchPaths);
            LoadDocument(source, resolved, file, options, output, included, stack);
        }
        output.Add(new LoadedDocument(document with { Nodes = document.Nodes.Where(node => node is not IniStatement statement || !IsInclude(statement.Text)).ToArray() },
            file, file.Class == IniFileClass.MapOverlay));
        stack.Pop();
    }

    private static IEnumerable<IniNode> SelectActive(IEnumerable<IniNode> nodes, ImmutableHashSet<string> symbols, string source)
    {
        var active = true;
        var stack = new Stack<(bool Parent, bool Matched)>();
        foreach (var node in nodes)
        {
            if (node is IniStatement statement && statement.Text.StartsWith('#'))
            {
                var tokens = statement.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens[0].Equals("#if", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("#ifdef", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length != 2) throw new DefinitionCompileException("Condition directive requires one symbol.", statement.Location);
                    var matched = symbols.Contains(tokens[1]);
                    stack.Push((active, matched)); active &= matched; continue;
                }
                if (tokens[0].Equals("#else", StringComparison.OrdinalIgnoreCase))
                {
                    if (stack.Count == 0) throw new DefinitionCompileException("Unexpected #else.", statement.Location);
                    var frame = stack.Peek(); active = frame.Parent && !frame.Matched; continue;
                }
                if (tokens[0].Equals("#endif", StringComparison.OrdinalIgnoreCase))
                {
                    if (stack.Count == 0) throw new DefinitionCompileException("Unexpected #endif.", statement.Location);
                    active = stack.Pop().Parent; continue;
                }
            }
            if (active) yield return node;
        }
        if (stack.Count != 0) throw new DefinitionCompileException("Unterminated condition directive.", new(source, 1, 1));
    }

    // -------------------------------------------------------------------------------------------- declarations

    private static bool TryMapKind(string type, out DefinitionKind kind)
    {
        kind = type.ToLowerInvariant() switch
        {
            "object" or "objectreskin" => DefinitionKind.Object,
            "weapon" => DefinitionKind.Weapon, "weaponset" => DefinitionKind.WeaponSet,
            "armor" or "armortemplate" => DefinitionKind.Armor, "damagefx" or "damagetype" => DefinitionKind.Damage,
            "upgrade" => DefinitionKind.Upgrade, "science" => DefinitionKind.Science,
            "player" => DefinitionKind.Player, "playertemplate" or "faction" => DefinitionKind.Faction,
            "commandbutton" => DefinitionKind.CommandButton, "commandset" => DefinitionKind.CommandSet,
            "fxlist" => DefinitionKind.FxList, "fx" or "particlesystem" => DefinitionKind.Fx,
            "audioevent" => DefinitionKind.AudioEvent, "dialogevent" => DefinitionKind.DialogEvent,
            "musictrack" => DefinitionKind.MusicTrack, "ambientstream" => DefinitionKind.AmbientStream,
            "locomotor" => DefinitionKind.Locomotor, "experiencelevel" => DefinitionKind.ExperienceLevel,
            "specialpower" or "specialpowertemplate" => DefinitionKind.SpecialPower,
            "gamedata" => DefinitionKind.GameData, "globaldata" => DefinitionKind.GlobalData,
            _ => DefinitionKind.Compatibility,
        };
        return kind != DefinitionKind.Compatibility;
    }

    private static Declaration ParseDeclaration(string text, IniSourceLocation location)
    {
        var tokens = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new DefinitionCompileException("Empty block declaration.", location);
        if (tokens[0].Equals("ObjectReskin", StringComparison.OrdinalIgnoreCase))
            return tokens.Length == 3 ? new(tokens[0], tokens[1], tokens[2]) : throw new DefinitionCompileException("ObjectReskin requires child and parent names.", location);
        string? parent = null;
        if (tokens.Length >= 4 && (tokens[2] == ":" || tokens[2].Equals("Inherits", StringComparison.OrdinalIgnoreCase))) parent = tokens[3];
        return new(tokens[0], tokens.ElementAtOrDefault(1), parent);
    }

    private static ModuleDeclaration ParseModuleDeclaration(string text, IniSourceLocation location)
    {
        var equals = text.IndexOf('=');
        var keyword = (equals >= 0 ? text[..equals] : text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty).Trim();
        var payload = equals >= 0 ? text[(equals + 1)..] : text;
        var tokens = payload.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (equals < 0 && tokens.Length > 0 && IsModuleBlock(tokens[0])) tokens = tokens[1..];
        if (tokens.Length == 0) throw new DefinitionCompileException("Module declaration requires a type.", location);
        var blockKind = keyword.ToLowerInvariant() switch
        {
            "behavior" => ModuleBlockKind.Behavior,
            "body" => ModuleBlockKind.Body,
            "draw" => ModuleBlockKind.Draw,
            "clientupdate" => ModuleBlockKind.ClientUpdate,
            _ => throw new DefinitionCompileException($"Unknown module keyword '{keyword}'.", location),
        };
        return new ModuleDeclaration(blockKind, tokens[0], tokens.ElementAtOrDefault(1) ?? $"anonymous@{location.Line}");
    }

    private static bool IsModuleBlock(string type) => type.Equals("Behavior", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Draw", StringComparison.OrdinalIgnoreCase) || type.Equals("Body", StringComparison.OrdinalIgnoreCase)
        || type.Equals("ClientUpdate", StringComparison.OrdinalIgnoreCase);
    private static bool IsInclude(string text) => text.StartsWith("#include ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("include ", StringComparison.OrdinalIgnoreCase);
    private static string ParseInclude(string text, IniSourceLocation location)
    {
        var separator = text.IndexOf(' ');
        var value = separator < 0 ? string.Empty : text[(separator + 1)..].Trim().Trim('"');
        if (value.Length == 0) throw new DefinitionCompileException("Include requires a path.", location);
        return NormalizePath(value);
    }
    private static string ResolveInclude(IDefinitionSource source, string owner, string include, ImmutableArray<string> searchPaths)
    {
        var candidates = new List<string>();
        var directory = Path.GetDirectoryName(owner)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(directory)) candidates.Add(NormalizePath(directory + "/" + include));
        candidates.Add(include);
        candidates.AddRange(searchPaths.Select(path => NormalizePath(path.TrimEnd('/', '\\') + "/" + include)));
        var matches = candidates.Distinct(Names).Where(source.Exists).ToArray();
        if (matches.Length == 0) throw new FileNotFoundException($"Include '{include}' referenced by '{owner}' was not found.", include);
        return matches[0];
    }
    private static string NormalizePath(string path) => OriginalLoadPlan.Normalize(path);
    private static int DescendantCount(IniBlock block) => 1 + block.Children.OfType<IniBlock>().Sum(DescendantCount);
    private static void CountCompat(Dictionary<string, CompatibilityCounter> values, string name, string owner, string target)
    {
        if (!values.TryGetValue(name, out var counter)) values[name] = counter = new CompatibilityCounter(target);
        counter.Count++; counter.Owners.Add(owner);
    }
    private static CompatibilityEntry ToEntry(string category, KeyValuePair<string, CompatibilityCounter> pair)
        => new(category, pair.Key, pair.Value.Count, pair.Value.Owners.Order(StringComparer.Ordinal).ToImmutableArray(), pair.Value.Target);

    // ------------------------------------------------------------------------------------------------ types

    private sealed record Declaration(string Type, string? Name, string? Parent);
    private sealed record ModuleDeclaration(ModuleBlockKind BlockKind, string Name, string Tag);
    private sealed record LoadedDocument(IniDocument Document, PlannedFile File, bool IsOverride);

    private sealed class ModuleSlot(ModuleConfiguration config)
    {
        public ModuleConfiguration Config { get; } = config;
        public ModuleInterfaces Interfaces { get; init; }
        public bool CopiedFromDefault { get; init; }
        public bool Inheritable { get; init; }
        public bool OverrideableByLikeKind { get; init; }
    }

    private sealed class DefinitionState
    {
        public DefinitionKind Kind { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DeclarationType { get; set; } = string.Empty;
        public IniSourceLocation Location { get; set; }
        public bool IsReskin { get; set; }
        public bool WeaponSetsFromDefault { get; set; }
        public bool ArmorSetsFromDefault { get; set; }
        public List<DefinitionField> Fields { get; init; } = [];
        public List<ModuleSlot> Modules { get; init; } = [];
        public List<DefinitionSection> Sections { get; init; } = [];

        public DefinitionState Clone(bool markCopiedFromDefault) => new()
        {
            Kind = Kind,
            Name = Name,
            DeclarationType = DeclarationType,
            Location = Location,
            WeaponSetsFromDefault = markCopiedFromDefault || WeaponSetsFromDefault,
            ArmorSetsFromDefault = markCopiedFromDefault || ArmorSetsFromDefault,
            Fields = [..Fields],
            Modules = [..Modules.Select(slot => new ModuleSlot(slot.Config)
            {
                Interfaces = slot.Interfaces,
                Inheritable = slot.Inheritable,
                OverrideableByLikeKind = slot.OverrideableByLikeKind,
                CopiedFromDefault = markCopiedFromDefault,
            })],
            Sections = [..Sections],
        };
    }

    private sealed class CompatibilityCounter(string target)
    {
        public int Count { get; set; }
        public HashSet<string> Owners { get; } = new(StringComparer.Ordinal);
        public string Target { get; } = target;
    }

    private sealed class Context(DefinitionCompilerOptions options, ModuleRegistry registry)
    {
        public DefinitionCompilerOptions Options { get; } = options;
        public ModuleRegistry Registry { get; } = registry;
        public Dictionary<DefinitionId, DefinitionState> States { get; } = [];
        public Dictionary<DefinitionId, List<PlannedFile>> FileTrace { get; } = [];
        public List<DefinitionDiagnostic> Diagnostics { get; } = [];
        public Dictionary<string, CompatibilityCounter> UnknownDefinitions { get; } = new(Names);
        public Dictionary<string, CompatibilityCounter> UnknownModules { get; } = new(Names);
        public Dictionary<string, CompatibilityCounter> DeferredFields { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CompatibilityCounter> DuplicateKeys { get; } = new(StringComparer.Ordinal);
        public int Blocks { get; set; }
        public int Keys { get; set; }
        public int ModuleCount { get; set; }
        public int InheritanceDeclarations { get; set; }
    }
}
