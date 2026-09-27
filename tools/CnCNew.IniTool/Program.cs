using CnCNew.Content;
using CnCNew.Data.Ini;

if (args is not ["audit", var contentDirectory])
{
    Console.Error.WriteLine("Usage: CnCNew.IniTool audit <content-directory>");
    return 2;
}

try
{
    var content = ContentFileSystem.Open(contentDirectory);
    var iniPaths = content.EnumerateFiles()
        .Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var failures = new List<string>();
    var blocks = 0;

    foreach (var path in iniPaths)
    {
        try
        {
            using var stream = content.OpenRead(path);
            var document = SageIniParser.Parse(stream, path);
            blocks += document.DescendantBlocks().Count();
        }
        catch (Exception exception)
        {
            failures.Add($"{path}: {exception.Message}");
        }
    }

    Console.WriteLine($"Parsed {iniPaths.Length - failures.Count}/{iniPaths.Length} INI files and {blocks} blocks from direct content.");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return failures.Count == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}
