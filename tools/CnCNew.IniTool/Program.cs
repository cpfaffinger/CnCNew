using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Ini;
using CnCNew.Data.Snapshots;

if (args is not ["audit", var contentDirectory] && args is not ["compile", var contentDirectory2] && args is not ["compile", var contentDirectory3, var snapshotPath])
{
    Console.Error.WriteLine("Usage: CnCNew.IniTool audit <content-directory> | compile <content-directory> [snapshot.json]");
    return 2;
}

try
{
    var directory = args[1];
    var content = ContentFileSystem.Open(directory);
    if (args[0].Equals("audit", StringComparison.OrdinalIgnoreCase))
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

    var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content));
    var bytes = DefinitionSnapshotWriter.WriteCanonical(graph);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    var audit = graph.Audit;
    Console.WriteLine($"Compiled {audit.Files} INI files, {audit.Blocks} blocks, {audit.Definitions} definitions, {audit.Modules} modules and {audit.Keys} keys.");
    Console.WriteLine($"References: {audit.ResolvedReferences}/{audit.References} resolved; {audit.FallbackReferences} explicit compatibility fallbacks.");
    Console.WriteLine($"Compatibility: {audit.Compatibility.Count(value => value.Category == "definition")} definition types, {audit.Compatibility.Count(value => value.Category == "module")} modules.");
    Console.WriteLine($"Canonical snapshot: {bytes.Length} bytes, SHA-256 {hash}.");
    foreach (var pair in audit.DefinitionKinds) Console.WriteLine($"  {pair.Key}: {pair.Value}");
    if (args.Length == 3)
    {
        var output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        Console.WriteLine($"Wrote {output}");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

file sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
