using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;

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
    bool StrictReferences = false)
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

public static class DefinitionCompiler
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static CompiledDefinitionGraph Compile(
        IDefinitionSource source,
        ModuleRegistry? modules = null,
        DefinitionCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        modules ??= ModuleRegistry.Empty;
        options ??= DefinitionCompilerOptions.Audit;

        var roots = source.EnumerateIniFiles().Select(NormalizePath).Distinct(Names)
            .OrderBy(LoadPriority).ThenBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var documents = new List<IniDocument>();
        var included = new HashSet<string>(Names);
        foreach (var path in roots)
            LoadDocument(source, path, options, documents, included, new Stack<string>());

        var diagnostics = new List<DefinitionDiagnostic>();
        var states = new Dictionary<DefinitionId, DefinitionState>();
        var blockCount = 0;
        var keyCount = 0;
        var moduleCount = 0;
        var unknownDefinitions = new Dictionary<string, CompatibilityCounter>(Names);
        var unknownModules = new Dictionary<string, CompatibilityCounter>(Names);

        foreach (var document in documents)
        {
            foreach (var block in SelectActive(document.Nodes, options.Conditions, document.Source).OfType<IniBlock>())
            {
                blockCount += DescendantCount(block);
                var declaration = ParseDeclaration(block.Declaration, block.Location);
                if (!TryMapKind(declaration.Type, out var kind))
                {
                    CountCompatibility(unknownDefinitions, declaration.Type, "top-level", "Phase-specific typed definition compiler");
                    if (options.StrictUnknownDefinitions)
                        throw new DefinitionCompileException($"Unknown top-level definition type '{declaration.Type}'.", block.Location);
                    kind = DefinitionKind.Compatibility;
                }

                var name = declaration.Name ?? (kind is DefinitionKind.GameData or DefinitionKind.GlobalData or DefinitionKind.Compatibility ? declaration.Type : null);
                if (string.IsNullOrWhiteSpace(name))
                    throw new DefinitionCompileException($"Definition '{declaration.Type}' requires a name.", block.Location);
                var id = new DefinitionId(kind, name);
                var inherited = ResolveBase(states, kind, declaration, block.Location);
                var fields = inherited?.Fields.ToList() ?? [];
                var configs = inherited?.Modules.ToList() ?? [];
                var children = SelectActive(block.Children, options.Conditions, document.Source).ToArray();

                foreach (var property in children.OfType<IniProperty>())
                {
                    keyCount++;
                    var parsed = kind == DefinitionKind.CommandSet && int.TryParse(property.Name, out _)
                        ? new DefinitionValue(DefinitionValueKind.Reference, new DefinitionReference(DefinitionKind.CommandButton, property.Value.Trim()), property.Value)
                        : DefinitionValueParser.Parse(property.Name, property.Value, property.Location);
                    fields.RemoveAll(field => field.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
                    fields.Add(new DefinitionField(property.Name, parsed, property.Location));
                }

                ApplyModules(children, kind, configs, modules, options, unknownModules, diagnostics, ref keyCount, ref moduleCount);
                states[id] = new DefinitionState(kind, name, block.Location, fields.ToImmutableArray(), configs.ToImmutableArray());
            }
        }

        var definitions = states.Values.Select(CreateDefinition)
            .OrderBy(value => value.Id.ToString(), StringComparer.Ordinal).ToImmutableArray();
        var byId = definitions.ToDictionary(value => value.Id);
        var references = new List<DefinitionReferenceEdge>();
        foreach (var definition in definitions)
        foreach (var field in definition.Fields)
        {
            if (field.Value.Kind != DefinitionValueKind.Reference) continue;
            var fieldReferences = field.Value.Value switch
            {
                DefinitionReference single => [single],
                ImmutableArray<DefinitionReference> multiple => multiple,
                _ => throw new InvalidDataException($"Invalid reference payload for '{definition.Id}.{field.Name}'."),
            };
            foreach (var reference in fieldReferences)
            {
                if (reference.IsNone) continue;
                var target = new DefinitionId(reference.Kind, reference.Name);
                var resolved = byId.ContainsKey(target);
                var fallback = resolved ? null : "compatibility-unresolved-reference";
                if (!resolved && options.StrictReferences)
                    throw new DefinitionCompileException($"Missing {reference.Kind} reference '{reference.Name}' in '{definition.Id}'.", field.Location);
                references.Add(new DefinitionReferenceEdge(definition.Id, field.Name, reference, field.Location, resolved, fallback));
            }
        }

        var compatibility = unknownDefinitions.Select(pair => ToEntry("definition", pair))
            .Concat(unknownModules.Select(pair => ToEntry("module", pair)))
            .OrderBy(value => value.Category, StringComparer.Ordinal)
            .ThenBy(value => value.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var kindCounts = definitions.GroupBy(value => value.Id.Kind.ToString())
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var edges = references.OrderBy(value => value.Source.ToString(), StringComparer.Ordinal)
            .ThenBy(value => value.Field, StringComparer.Ordinal).ThenBy(value => value.Target.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        var audit = new DefinitionAudit(documents.Select(value => value.Source).Distinct(Names).Count(), blockCount,
            definitions.Length, moduleCount, keyCount, edges.Length, edges.Count(value => value.Resolved),
            edges.Count(value => !value.Resolved), kindCounts, compatibility);
        return new CompiledDefinitionGraph(definitions, edges, diagnostics.ToImmutableArray(), audit);
    }

    private static void LoadDocument(IDefinitionSource source, string path, DefinitionCompilerOptions options,
        List<IniDocument> output, HashSet<string> included, Stack<string> stack)
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
            LoadDocument(source, resolved, options, output, included, stack);
        }
        output.Add(document with { Nodes = document.Nodes.Where(node => node is not IniStatement statement || !IsInclude(statement.Text)).ToArray() });
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

    private static void ApplyModules(IniNode[] children, DefinitionKind ownerKind, List<ModuleConfiguration> target,
        ModuleRegistry registry, DefinitionCompilerOptions options, Dictionary<string, CompatibilityCounter> compatibility,
        List<DefinitionDiagnostic> diagnostics, ref int keyCount, ref int moduleCount)
    {
        foreach (var statement in children.OfType<IniStatement>())
        {
            var declaration = ParseDeclaration(statement.Text, statement.Location);
            if (!declaration.Type.Equals("RemoveModule", StringComparison.OrdinalIgnoreCase)) continue;
            if (declaration.Name is null) throw new DefinitionCompileException("RemoveModule requires a tag.", statement.Location);
            target.RemoveAll(module => module.Tag.Equals(declaration.Name, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var child in children.OfType<IniBlock>())
        {
            var declaration = ParseDeclaration(child.Declaration, child.Location);
            if (declaration.Type.Equals("RemoveModule", StringComparison.OrdinalIgnoreCase))
            {
                if (declaration.Name is null) throw new DefinitionCompileException("RemoveModule requires a tag.", child.Location);
                target.RemoveAll(module => module.Tag.Equals(declaration.Name, StringComparison.OrdinalIgnoreCase));
                continue;
            }
            if (declaration.Type.Equals("AddModule", StringComparison.OrdinalIgnoreCase) || declaration.Type.Equals("ReplaceModule", StringComparison.OrdinalIgnoreCase))
            {
                var nested = child.Children.OfType<IniBlock>().ToArray();
                if (nested.Length == 0 && declaration.Type.Equals("AddModule", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-empty-add-module", "AddModule contains no module block and is retained only as a diagnosed shipped-data compatibility case.", child.Location));
                    continue;
                }
                if (nested.Length == 0 || (declaration.Type.Equals("ReplaceModule", StringComparison.OrdinalIgnoreCase) && nested.Length != 1))
                    throw new DefinitionCompileException($"{declaration.Type} requires {(declaration.Type.Equals("ReplaceModule", StringComparison.OrdinalIgnoreCase) ? "exactly one" : "at least one")} module block.", child.Location);
                if (declaration.Type.Equals("ReplaceModule", StringComparison.OrdinalIgnoreCase))
                {
                    var compiled = CompileModule(nested[0], ownerKind, registry, options, compatibility, diagnostics, ref keyCount);
                    if (declaration.Name is null) throw new DefinitionCompileException("ReplaceModule requires a tag.", child.Location);
                    var index = target.FindIndex(module => module.Tag.Equals(declaration.Name, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                    {
                        diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-missing-replace-target", $"ReplaceModule target '{declaration.Name}' was not present in this aggregate map overlay; replacement was retained.", child.Location));
                        target.Add(compiled);
                    }
                    else target[index] = compiled;
                    moduleCount++;
                }
                else
                {
                    foreach (var nestedModule in nested)
                    {
                        target.Add(CompileModule(nestedModule, ownerKind, registry, options, compatibility, diagnostics, ref keyCount));
                        moduleCount++;
                    }
                }
                continue;
            }
            if (declaration.Type.Equals("InheritableModule", StringComparison.OrdinalIgnoreCase))
            {
                var nested = child.Children.OfType<IniBlock>().ToArray();
                if (nested.Length != 1) throw new DefinitionCompileException("InheritableModule requires exactly one module block.", child.Location);
                var compiled = CompileModule(nested[0], ownerKind, registry, options, compatibility, diagnostics, ref keyCount);
                var existing = target.FindIndex(value => value.Tag.Equals(compiled.Tag, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0) target[existing] = compiled; else target.Add(compiled);
                moduleCount++;
                continue;
            }
            if (!IsModuleBlock(declaration.Type)) continue;
            var module = CompileModule(child, ownerKind, registry, options, compatibility, diagnostics, ref keyCount);
            var duplicate = target.FindIndex(value => value.Tag.Equals(module.Tag, StringComparison.OrdinalIgnoreCase));
            if (duplicate >= 0) target[duplicate] = module; else target.Add(module);
            moduleCount++;
        }
    }

    private static ModuleConfiguration CompileModule(IniBlock block, DefinitionKind ownerKind, ModuleRegistry registry,
        DefinitionCompilerOptions options, Dictionary<string, CompatibilityCounter> compatibility,
        List<DefinitionDiagnostic> diagnostics, ref int keyCount)
    {
        var declaration = ParseModuleDeclaration(block.Declaration, block.Location);
        var owner = ownerKind == DefinitionKind.Object ? ModuleOwnerKind.Object : ownerKind is DefinitionKind.Fx or DefinitionKind.FxList ? ModuleOwnerKind.Fx : ModuleOwnerKind.Global;
        var fieldBuilder = ImmutableArray.CreateBuilder<DefinitionField>();
        foreach (var property in block.Children.OfType<IniProperty>())
        {
            keyCount++;
            fieldBuilder.Add(new DefinitionField(property.Name, DefinitionValueParser.Parse(property.Name, property.Value, property.Location), property.Location));
        }
        var fields = fieldBuilder.ToImmutable();
        if (!registry.TryGet(declaration.Name, out var descriptor))
        {
            CountCompatibility(compatibility, declaration.Name, owner.ToString(), "Phase 4/5 simulation module implementation");
            diagnostics.Add(new DefinitionDiagnostic("warning", "compatibility-module", $"Module '{declaration.Name}' is inventoried but has no runtime factory.", block.Location));
            if (options.StrictUnknownModules) throw new DefinitionCompileException($"Unknown module '{declaration.Name}'.", block.Location);
        }
        else
        {
            if (!descriptor.AllowedOwners.Contains(owner)) throw new DefinitionCompileException($"Module '{declaration.Name}' is not allowed on {owner}.", block.Location);
            foreach (var required in descriptor.Schema.Where(value => value.Required))
                if (!fields.Any(field => field.Name.Equals(required.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new DefinitionCompileException($"Module '{declaration.Name}' requires field '{required.Name}'.", block.Location);
            foreach (var field in fields)
            {
                var schema = descriptor.Schema.FirstOrDefault(value => value.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
                if (schema is null)
                    throw new DefinitionCompileException($"Unknown field '{field.Name}' for module '{declaration.Name}'.", field.Location);
                if (schema.Kind != field.Value.Kind)
                    throw new DefinitionCompileException($"Module field '{field.Name}' expects {schema.Kind}, got {field.Value.Kind}.", field.Location);
            }
        }
        return new ModuleConfiguration(declaration.Name, declaration.Tag, owner, fields, block.Location);
    }

    private static DefinitionState? ResolveBase(Dictionary<DefinitionId, DefinitionState> states, DefinitionKind kind, Declaration declaration, IniSourceLocation location)
    {
        if (declaration.Parent is not null)
        {
            var parentId = new DefinitionId(kind, declaration.Parent);
            if (!states.TryGetValue(parentId, out var parent)) throw new DefinitionCompileException($"Base definition '{parentId}' was not found.", location);
            return parent;
        }
        var id = new DefinitionId(kind, declaration.Name ?? "Default");
        return states.GetValueOrDefault(id);
    }

    private static GameDefinition CreateDefinition(DefinitionState state)
    {
        var id = new DefinitionId(state.Kind, state.Name);
        return state.Kind switch
        {
            DefinitionKind.Object => new ObjectDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Weapon => new WeaponDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.WeaponSet => new WeaponSetDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Armor => new ArmorDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Damage => new DamageDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Upgrade => new UpgradeDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Science => new ScienceDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Player => new PlayerDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Faction => new FactionDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.CommandButton => new CommandButtonDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.CommandSet => new CommandSetDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.FxList => new FxListDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Fx => new FxDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.AudioEvent => new AudioEventDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.DialogEvent => new DialogEventDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.MusicTrack => new MusicTrackDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.AmbientStream => new AmbientStreamDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.Locomotor => new LocomotorDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.ExperienceLevel => new ExperienceLevelDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.SpecialPower => new SpecialPowerDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            DefinitionKind.GameData or DefinitionKind.GlobalData => new GlobalDataDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
            _ => new CompatibilityDefinition(id, state.Name, state.Location, state.Fields, state.Modules),
        };
    }

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

    private static (string Name, string Tag) ParseModuleDeclaration(string text, IniSourceLocation location)
    {
        var equals = text.IndexOf('=');
        var payload = equals >= 0 ? text[(equals + 1)..] : text;
        var tokens = payload.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (equals < 0 && tokens.Length > 0 && IsModuleBlock(tokens[0])) tokens = tokens[1..];
        if (tokens.Length == 0) throw new DefinitionCompileException("Module declaration requires a type.", location);
        return (tokens[0], tokens.ElementAtOrDefault(1) ?? $"anonymous@{location.Line}");
    }

    private static bool IsModuleBlock(string type) => type.Equals("Behavior", StringComparison.OrdinalIgnoreCase)
        || type.Equals("Draw", StringComparison.OrdinalIgnoreCase) || type.Equals("Body", StringComparison.OrdinalIgnoreCase)
        || type.Equals("ClientUpdate", StringComparison.OrdinalIgnoreCase) || type.Equals("InheritableModule", StringComparison.OrdinalIgnoreCase);
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
    private static string NormalizePath(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    private static int LoadPriority(string path) => path.StartsWith("Data/INI/Default/", StringComparison.OrdinalIgnoreCase) ? 0 : path.StartsWith("Data/INI/", StringComparison.OrdinalIgnoreCase) ? 1 : path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    private static int DescendantCount(IniBlock block) => 1 + block.Children.OfType<IniBlock>().Sum(DescendantCount);
    private static void CountCompatibility(Dictionary<string, CompatibilityCounter> values, string name, string owner, string target)
    {
        if (!values.TryGetValue(name, out var counter)) values[name] = counter = new CompatibilityCounter(target);
        counter.Count++; counter.Owners.Add(owner);
    }
    private static CompatibilityEntry ToEntry(string category, KeyValuePair<string, CompatibilityCounter> pair)
        => new(category, pair.Key, pair.Value.Count, pair.Value.Owners.Order(StringComparer.Ordinal).ToImmutableArray(), pair.Value.Target);
    private sealed record Declaration(string Type, string? Name, string? Parent);
    private sealed record DefinitionState(DefinitionKind Kind, string Name, IniSourceLocation Location, ImmutableArray<DefinitionField> Fields, ImmutableArray<ModuleConfiguration> Modules);
    private sealed class CompatibilityCounter(string target) { public int Count { get; set; } public HashSet<string> Owners { get; } = new(StringComparer.Ordinal); public string Target { get; } = target; }
}
