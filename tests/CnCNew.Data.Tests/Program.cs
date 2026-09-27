using System.Collections.Immutable;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;

ParserPreservesStructureAndLocations();
IncludesAndSearchPriority();
IncludeCyclesAreRejected();
InheritanceOverridesAndModuleMutations();
ConditionsSelectDeterministically();
ValuesAreTypedAndValidated();
ReferencesResolveAndRejectWrongTypes();
RegistryValidatesDuplicatesOwnersAndSchemas();
CompatibilityIsExplicit();
CorruptInputIsRejected();
SnapshotsAreDeterministic();
Console.WriteLine("All typed definition compiler tests passed.");
return 0;

static void ParserPreservesStructureAndLocations()
{
    const string text = """
    ; comment
    Object DemoTank
      Side = America
      Behavior = ActiveBody ModuleTag_02
        MaxHealth = 100.0 ; inline comment
      End
    End
    """;
    var document = SageIniParser.Parse(text, "fixture.ini");
    Assert(document.Blocks.Single().Type == "Object", "Top-level type mismatch.");
    Assert(document.DescendantBlocks().Count() == 2, "Nested block count mismatch.");
    Assert(document.Blocks.Single().Location == new IniSourceLocation("fixture.ini", 2, 1), "Source location mismatch.");
}

static void IncludesAndSearchPriority()
{
    var source = new MemorySource(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Data/INI/root.ini"] = "#include \"shared.ini\"\nObject Tank\n  Cost = 20\nEnd\n",
        ["Data/INI/shared.ini"] = "Object Tank\n  Cost = 10\nEnd\n",
        ["shared.ini"] = "Object Wrong\nEnd\n",
    }, ["Data/INI/root.ini"]);
    var graph = DefinitionCompiler.Compile(source);
    var tank = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Tank");
    Assert(((Money)tank.Fields.Single().Value.Value).Value == 20, "Owner-relative include or override order failed.");
    Assert(!graph.Definitions.Any(value => value.OriginalName == "Wrong"), "Include search priority was not owner-relative.");
}

static void IncludeCyclesAreRejected()
{
    var source = new MemorySource(new Dictionary<string, string> { ["a.ini"] = "#include b.ini\n", ["b.ini"] = "#include a.ini\n" }, ["a.ini"]);
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(source), "Include cycle");
}

static void InheritanceOverridesAndModuleMutations()
{
    var source = One("""
    Object Base
      Cost = 10
      Behavior = ActiveBody Keep
        MaxHealth = 100
      End
      Behavior = SlowDeath RemoveMe
      End
    End
    Object Child : Base
      Cost = 20
      RemoveModule RemoveMe
      ReplaceModule Keep
        Behavior = ActiveBody Replaced
          MaxHealth = 200
        End
      End
      AddModule
        Behavior = AutoHeal Added
          HealingAmount = 5
        End
      End
    End
    """);
    var graph = DefinitionCompiler.Compile(source);
    var child = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Child");
    Assert(((Money)child.Fields.Single().Value.Value).Value == 20, "Inherited field override failed.");
    Assert(child.Modules.Select(value => value.Tag).SequenceEqual(["Replaced", "Added"]), "Module mutation order failed.");
}

static void ConditionsSelectDeterministically()
{
    var source = One("""
    #if DEBUG
    Object DebugOnly
    End
    #else
    Object RetailOnly
    End
    #endif
    """);
    var options = DefinitionCompilerOptions.Audit with { Conditions = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "DEBUG") };
    var graph = DefinitionCompiler.Compile(source, options: options);
    Assert(graph.Definitions.Single().OriginalName == "DebugOnly", "Condition selection failed.");
}

static void ValuesAreTypedAndValidated()
{
    Assert(DefinitionValueParser.Parse("DelayTime", "1500", new("x", 1, 1)).Kind == DefinitionValueKind.Time, "Time typing failed.");
    Assert(DefinitionValueParser.Parse("AttackRange", "300", new("x", 1, 1)).Kind == DefinitionValueKind.Distance, "Distance typing failed.");
    Assert(DefinitionValueParser.Parse("Speed", "2.5", new("x", 1, 1)).Kind == DefinitionValueKind.Speed, "Speed typing failed.");
    Assert(DefinitionValueParser.Parse("Angle", "90", new("x", 1, 1)).Kind == DefinitionValueKind.Angle, "Angle typing failed.");
    Assert(DefinitionValueParser.Parse("Bonus", "125%", new("x", 1, 1)).Kind == DefinitionValueKind.Percentage, "Percentage typing failed.");
    Assert(DefinitionValueParser.Parse("BuildCost", "1200", new("x", 1, 1)).Kind == DefinitionValueKind.Money, "Money typing failed.");
    Assert(DefinitionValueParser.Parse("Color", "1 2 3", new("x", 1, 1)).Kind == DefinitionValueKind.Color, "Color typing failed.");
    Assert(DefinitionValueParser.Parse("Offset", "1 2 3", new("x", 1, 1)).Kind == DefinitionValueKind.Vector3, "Vector typing failed.");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Value", "NaN", new("x", 1, 1)), "Non-finite");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Value", "1e1000", new("x", 1, 1)), "outside the supported range");
}

static void ReferencesResolveAndRejectWrongTypes()
{
    var valid = DefinitionCompiler.Compile(One("Weapon Gun\nEnd\nObject Tank\n  Weapon = Gun\nEnd\n"),
        options: DefinitionCompilerOptions.Audit with { StrictReferences = true });
    Assert(valid.References.Single().Resolved, "Reference did not resolve.");
    var wrong = One("Armor Gun\nEnd\nObject Tank\n  Weapon = Gun\nEnd\n");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(wrong, options: DefinitionCompilerOptions.Audit with { StrictReferences = true }), "Missing Weapon reference");
}

static void RegistryValidatesDuplicatesOwnersAndSchemas()
{
    var descriptor = new ModuleDescriptor("ActiveBody", ImmutableHashSet.Create(ModuleOwnerKind.Object),
        [new ModuleFieldSchema("MaxHealth", DefinitionValueKind.Number, true)], configuration => configuration);
    Throws<InvalidOperationException>(() => new ModuleRegistry([descriptor, descriptor]), "Duplicate module");
    var registry = new ModuleRegistry([descriptor]);
    var graph = DefinitionCompiler.Compile(One("Object Tank\n  Behavior = ActiveBody Body\n    MaxHealth = 100.0\n  End\nEnd\n"), registry,
        DefinitionCompilerOptions.Audit with { StrictUnknownModules = true });
    Assert(graph.Definitions.Single().Modules.Length == 1, "Registered module was not compiled.");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Behavior = ActiveBody Body\n    Unknown = 1\n  End\nEnd\n"), registry), "requires field");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Behavior = Mystery Tag\n  End\nEnd\n"), registry,
        DefinitionCompilerOptions.Audit with { StrictUnknownModules = true }), "Unknown module");
}

static void CompatibilityIsExplicit()
{
    var graph = DefinitionCompiler.Compile(One("MysteryDefinition X\nEnd\nObject Tank\n  Behavior = MysteryModule Tag\n  End\nEnd\n"));
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "definition" && value.Name == "MysteryDefinition"), "Unknown definition was not inventoried.");
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "module" && value.Name == "MysteryModule"), "Unknown module was not inventoried.");
}

static void CorruptInputIsRejected()
{
    Throws<IniParseException>(() => SageIniParser.Parse("Object Broken\n  Cost = 1\n", "broken.ini"), "missing its End");
    Throws<IniParseException>(() => SageIniParser.Parse("End\n", "broken.ini"), "Unexpected End");
}

static void SnapshotsAreDeterministic()
{
    var source = One("Weapon Gun\n  Damage = 10\nEnd\nObject Tank\n  Weapon = Gun\nEnd\n");
    var first = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(source));
    var second = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(source));
    Assert(first.AsSpan().SequenceEqual(second), "Canonical snapshots differ.");
}

static MemorySource One(string text) => new(new Dictionary<string, string> { ["fixture.ini"] = text }, ["fixture.ini"]);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<T>(Action action, string messagePart) where T : Exception
{
    try { action(); }
    catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}'.");
}

file sealed class MemorySource(IReadOnlyDictionary<string, string> files, IReadOnlyList<string> roots) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => roots;
    public Stream OpenRead(string virtualPath) => new MemoryStream(System.Text.Encoding.Latin1.GetBytes(files[virtualPath]));
    public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
}
