using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;

const string Usage = """
    Usage: CnCNew.IniTool <command> <content-directory> [arguments]
      audit <dir>                                       parse every INI file (syntax only)
      compile <dir> [snapshot.json] [--maps include|exclude]
                                                        compile with the module catalog in strict mode and print reference numbers
      compat-baseline <dir> <baseline.json>             write the compatibility baseline from a fresh full-inventory compilation
      compat-check <dir> <baseline.json>                fail (exit 1) on any new, missing or changed compatibility entry
      load-order-report <dir> [report.txt]              multi-file definitions, load order, winners (fails on unsourced order)
      debt-report <dir>                                 value-kind histogram per definition kind (typing debt for phase 5)
    """;

if (args.Length < 2)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var command = args[0];
    var content = ContentFileSystem.Open(args[1]);
    var source = new ContentDefinitionSource(content);
    switch (command)
    {
        case "audit":
            return Audit(content);
        case "compile":
        {
            var maps = ParseMaps(args);
            var graph = CompileStrict(source, maps);
            var bytes = DefinitionSnapshotWriter.WriteCanonical(graph);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            PrintSummary(graph, bytes.Length, hash);
            var output = args.Skip(2).FirstOrDefault(value => !value.StartsWith("--") && value != "include" && value != "exclude");
            if (output is not null)
            {
                var path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
                Console.WriteLine($"Wrote {path}");
            }
            return 0;
        }
        case "compat-baseline" when args.Length == 3:
        {
            var graph = CompileStrict(source, MapOverlayMode.Include);
            var json = CompatibilityBaseline.Serialize(graph.Audit.Compatibility, "full inventory (all map overlays), strict module catalog, retail load plan");
            var path = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            Console.WriteLine($"Wrote {graph.Audit.Compatibility.Length} compatibility entries to {path}");
            return 0;
        }
        case "compat-check" when args.Length == 3:
        {
            var graph = CompileStrict(source, MapOverlayMode.Include);
            var differences = CompatibilityBaseline.Compare(File.ReadAllText(args[2]), graph.Audit.Compatibility);
            foreach (var difference in differences) Console.Error.WriteLine(difference);
            Console.WriteLine(differences.Count == 0
                ? $"Compatibility baseline matches: {graph.Audit.Compatibility.Length} entries."
                : $"Compatibility baseline deviates in {differences.Count} entries.");
            return differences.Count == 0 ? 0 : 1;
        }
        case "load-order-report":
            return LoadOrderReport(source, args.Length > 2 ? args[2] : null);
        case "debt-report":
            return DebtReport(source);
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

static MapOverlayMode ParseMaps(string[] args)
{
    var index = Array.IndexOf(args, "--maps");
    if (index < 0) return MapOverlayMode.Include;
    return args.ElementAtOrDefault(index + 1)?.ToLowerInvariant() switch
    {
        "include" => MapOverlayMode.Include,
        "exclude" => MapOverlayMode.Exclude,
        _ => throw new ArgumentException("--maps expects include or exclude."),
    };
}

static CompiledDefinitionGraph CompileStrict(IDefinitionSource source, MapOverlayMode maps)
    => DefinitionCompiler.Compile(source, ModuleRegistry.Default, DefinitionCompilerOptions.Audit with
    {
        StrictUnknownModules = true,
        StrictLoadPlan = true,
        MapOverlays = maps,
    });

static void PrintSummary(CompiledDefinitionGraph graph, int snapshotLength, string hash)
{
    var audit = graph.Audit;
    Console.WriteLine($"Compiled {audit.Files} INI files ({graph.ExcludedFiles.Length} excluded by the retail load plan), {audit.Blocks} blocks, {audit.Definitions} definitions, {audit.Modules} modules and {audit.Keys} keys.");
    Console.WriteLine($"References: {audit.ResolvedReferences}/{audit.References} resolved; {audit.FallbackReferences} explicit compatibility fallbacks.");
    foreach (var group in audit.Compatibility.GroupBy(value => value.Category).OrderBy(value => value.Key, StringComparer.Ordinal))
        Console.WriteLine($"Compatibility {group.Key}: {group.Count()} entries, {group.Sum(value => value.Count)} uses.");
    Console.WriteLine($"Canonical snapshot: {snapshotLength} bytes, SHA-256 {hash}.");
    foreach (var pair in audit.DefinitionKinds) Console.WriteLine($"  {pair.Key}: {pair.Value}");
}

static int Audit(ContentFileSystem content)
{
    var iniPaths = content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToArray();
    var failures = new List<string>();
    var blocks = 0;
    foreach (var path in iniPaths)
    {
        try
        {
            using var stream = content.OpenRead(path);
            blocks += SageIniParser.Parse(stream, path).DescendantBlocks().Count();
        }
        catch (Exception exception) { failures.Add($"{path}: {exception.Message}"); }
    }
    Console.WriteLine($"Parsed {iniPaths.Length - failures.Count}/{iniPaths.Length} INI files and {blocks} blocks from direct content.");
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return failures.Count == 0 ? 0 : 1;
}

static int LoadOrderReport(IDefinitionSource source, string? reportPath)
{
    var graph = CompileStrict(source, MapOverlayMode.Include);
    var lines = new List<string>
    {
        "# Load order report (retail load plan of the original engine)",
        $"loaded files: {graph.LoadedFiles.Length}, excluded files: {graph.ExcludedFiles.Length}",
        "",
        "## Excluded files",
    };
    foreach (var file in graph.ExcludedFiles) lines.Add($"{file.Class}\t{file.Path}\t{file.Reason}");
    lines.Add("");
    lines.Add("## Load sequence");
    var index = 0;
    foreach (var file in graph.LoadedFiles) lines.Add($"{++index:D3}\t{file.Class}\tstage {file.Stage}\t{file.Subsystem}\t{file.Path}");
    lines.Add("");
    lines.Add("## Definitions present in more than one occurrence (winner = last)");

    static string LegacyKey(string path)
        => (path.StartsWith("Data/INI/Default/", StringComparison.OrdinalIgnoreCase) ? "0"
            : path.StartsWith("Data/INI/", StringComparison.OrdinalIgnoreCase) ? "1"
            : path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? "2" : "3") + path.ToLowerInvariant();

    var changedWinner = 0;
    var unsourced = 0;
    var crossFile = 0;
    foreach (var definition in graph.MultiFileDefinitions)
    {
        var distinct = definition.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length > 1) crossFile++;
        var legacyWinner = definition.Files.OrderBy(LegacyKey, StringComparer.Ordinal).Last();
        var differs = !legacyWinner.Equals(definition.Winner, StringComparison.OrdinalIgnoreCase);
        if (differs) changedWinner++;
        if (!definition.OrderSourced) unsourced++;
        lines.Add($"{definition.Id}\toccurrences={definition.Files.Length}\tfiles={distinct.Length}\twinner={definition.Winner}\t"
            + $"{(definition.OrderSourced ? "order-sourced" : "ORDER-NOT-SOURCED")}\t{(differs ? "legacy-winner=" + legacyWinner : "same-as-legacy")}");
    }
    lines.Add("");
    lines.Add($"multi-occurrence definitions: {graph.MultiFileDefinitions.Length}, across more than one file: {crossFile}");
    lines.Add($"definitions whose winner differs from the phase-3 order: {changedWinner}");
    lines.Add($"definitions whose relative file order is not sourced from the reference: {unsourced}");
    foreach (var line in lines.Take(6).Concat(lines.Skip(lines.Count - 3))) Console.WriteLine(line);
    if (reportPath is not null)
    {
        File.WriteAllLines(Path.GetFullPath(reportPath), lines);
        Console.WriteLine($"Wrote {Path.GetFullPath(reportPath)}");
    }
    return unsourced == 0 ? 0 : 1;
}

static int DebtReport(IDefinitionSource source)
{
    var graph = CompileStrict(source, MapOverlayMode.Include);
    var total = 0;
    foreach (var group in graph.Definitions.GroupBy(value => value.Id.Kind).OrderBy(value => value.Key.ToString(), StringComparer.Ordinal))
    {
        var histogram = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var definition in group)
            foreach (var field in definition.Fields)
                histogram[field.Value.Kind.ToString()] = histogram.GetValueOrDefault(field.Value.Kind.ToString()) + 1;
        var fieldCount = histogram.Values.Sum();
        total += fieldCount;
        var flat = histogram.Where(pair => Enum.Parse<DefinitionValueKind>(pair.Key) is DefinitionValueKind.Text or DefinitionValueKind.List).Sum(pair => pair.Value);
        Console.WriteLine($"{group.Key}: {group.Count()} definitions, {fieldCount} header fields, {flat} untyped (Text/List): " + string.Join(", ", histogram.Select(pair => $"{pair.Key}={pair.Value}")));
    }
    Console.WriteLine($"Total header fields: {total}");

    // Every typed value of the graph (header fields, module fields, nested sections at any depth).
    var all = new SortedDictionary<string, int>(StringComparer.Ordinal);
    void Count(DefinitionValue value) => all[value.Kind.ToString()] = all.GetValueOrDefault(value.Kind.ToString()) + 1;
    void CountSection(DefinitionSection section)
    {
        foreach (var field in section.Fields) Count(field.Value);
        foreach (var child in section.Children) CountSection(child);
    }
    foreach (var definition in graph.Definitions)
    {
        foreach (var field in definition.Fields) Count(field.Value);
        foreach (var section in definition.Sections) CountSection(section);
        foreach (var module in definition.Modules)
        {
            foreach (var field in module.Fields) Count(field.Value);
            if (!module.Sections.IsDefault) foreach (var section in module.Sections) CountSection(section);
        }
    }
    Console.WriteLine($"All typed values: {all.Values.Sum()}; Text={all.GetValueOrDefault("Text")}, List={all.GetValueOrDefault("List")}: " + string.Join(", ", all.Select(pair => $"{pair.Key}={pair.Value}")));
    return 0;
}

file sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
