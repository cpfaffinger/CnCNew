using System.Text;
using System.Text.Json;
using CnCNew.Content;

var root = Path.Combine(Path.GetTempPath(), $"CnCNew-content-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(root, "Data", "INI"));
try
{
    var payloadPath = Path.Combine(root, "Data", "INI", "GameData.ini");
    await File.WriteAllTextAsync(payloadPath, "value", Encoding.UTF8);
    var bytes = new FileInfo(payloadPath).Length;
    var manifest = new[]
    {
        new
        {
            VirtualPath = "Data/INI/GameData.ini",
            PhysicalPath = "Data/INI/GameData.ini",
            Bytes = bytes,
            Sha256 = new string('0', 64),
            SelectedSource = "big:INI.big",
            Candidates = Array.Empty<object>(),
        },
    };
    await File.WriteAllTextAsync(
        Path.Combine(root, "content-manifest.json"),
        JsonSerializer.Serialize(manifest));

    var content = ContentFileSystem.Open(root);
    Assert(content.FileCount == 1, "Manifest entry count mismatch.");
    Assert(content.Exists("data\\ini\\gamedata.ini"), "Virtual paths must be case-insensitive.");
    Assert(content.EnumerateFiles("Data/INI").Single() == "Data/INI/GameData.ini", "Prefix enumeration mismatch.");
    await using var stream = content.OpenRead("DATA/INI/GAMEDATA.INI");
    using var reader = new StreamReader(stream, Encoding.UTF8);
    Assert(await reader.ReadToEndAsync() == "value", "Materialized payload mismatch.");

    Console.WriteLine("All direct content file-system tests passed.");
    return 0;
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
