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

    var runtimeAsset = new RuntimeAsset(
        ContentPath.ToAssetId("Data/INI/GameData.ini"), "Data/INI/GameData.ini", "sage/ini", new string('a', 64), bytes,
        new RuntimeOrigin("big:INI.big", 0, [new RuntimeCandidate(0, "big:INI.big", bytes)]),
        [new RuntimeDependency("asset://art/textures/test.dds", "direct", "fallback", "visible-magenta-texture")],
        [new RuntimeArtifact("Data/INI/GameData.ini", new string('a', 64), bytes)], []);
    var versioned = new RuntimeContentManifest(2, new RuntimeImporter("test", "1", new Dictionary<string, string> { ["z"] = "2", ["a"] = "1" }), [runtimeAsset]);
    var canonical1 = RuntimeManifestSerializer.WriteCanonical(versioned);
    var canonical2 = RuntimeManifestSerializer.WriteCanonical(versioned with
    {
        Importer = versioned.Importer with { Options = new Dictionary<string, string> { ["a"] = "1", ["z"] = "2" } },
    });
    Assert(canonical1.SequenceEqual(canonical2), "Canonical serialization depends on dictionary insertion order.");
    ExpectInvalid(() => RuntimeManifestSerializer.WriteCanonical(versioned with { SchemaVersion = 99 }), "Unknown schema accepted.");
    ExpectInvalid(() => RuntimeManifestSerializer.WriteCanonical(versioned with
    {
        Assets = [runtimeAsset, runtimeAsset with { VirtualPath = "data/ini/gamedata.ini", Id = ContentPath.ToAssetId("data/ini/gamedata.ini") }],
    }), "Case collision accepted.");

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

static void ExpectInvalid(Action action, string message)
{
    try { action(); throw new InvalidOperationException(message); }
    catch (InvalidDataException) { }
}
