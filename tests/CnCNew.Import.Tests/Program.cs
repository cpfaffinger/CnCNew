using System.Buffers.Binary;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Import;

var root = Path.Combine(Path.GetTempPath(), $"CnCNew-import-tests-{Guid.NewGuid():N}");
var source = Path.Combine(root, "source"); var first = Path.Combine(root, "first"); var second = Path.Combine(root, "second");
Directory.CreateDirectory(Path.Combine(source, "Art", "Textures")); Directory.CreateDirectory(Path.Combine(source, "Window"));
try
{
    await File.WriteAllBytesAsync(Path.Combine(source, "Art", "Textures", "Test.dds"), CreateDds(1));
    await File.WriteAllTextAsync(Path.Combine(source, "Window", "Test.wnd"), "FILE_VERSION = 2;\nWINDOW { IMAGE = Art/Textures/Test.dds; }");
    var firstResult = await RuntimeContentImporter.ImportAsync(source, first);
    var firstManifest = await File.ReadAllBytesAsync(Path.Combine(first, "content-manifest.json"));
    Assert(firstResult.Manifest.SchemaVersion == 2, "Schema version mismatch.");
    Assert(firstResult.Manifest.Assets.Single(asset => asset.Type == "sage/window-layout").Dependencies.Single().Status == "resolved", "Dependency not resolved.");

    var repeat = await RuntimeContentImporter.ImportAsync(source, first);
    Assert(repeat.CacheHits == 2, "Incremental cache misses.");
    var repeatManifest = await File.ReadAllBytesAsync(Path.Combine(first, "content-manifest.json"));
    Assert(firstManifest.SequenceEqual(repeatManifest), "Incremental manifest differs.");

    await RuntimeContentImporter.ImportAsync(source, second);
    var secondManifest = await File.ReadAllBytesAsync(Path.Combine(second, "content-manifest.json"));
    Assert(firstManifest.SequenceEqual(secondManifest), "Fresh manifests differ.");
    var content = ContentFileSystem.Open(second); await content.VerifyIntegrityAsync();

    await File.WriteAllBytesAsync(Path.Combine(source, "Art", "Textures", "Test.dds"), new byte[128]);
    byte[] beforeFailure;
    await using (var beforeStream = File.OpenRead(Path.Combine(first, "content-manifest.json")))
        beforeFailure = await SHA256.HashDataAsync(beforeStream);
    try { await RuntimeContentImporter.ImportAsync(source, first); throw new InvalidOperationException("Broken DDS import succeeded."); }
    catch (InvalidDataException) { }
    byte[] afterFailure;
    await using (var afterStream = File.OpenRead(Path.Combine(first, "content-manifest.json")))
        afterFailure = await SHA256.HashDataAsync(afterStream);
    Assert(beforeFailure.SequenceEqual(afterFailure), "Failed import changed published content.");

    // A content layer (docs/PATCH-LAYER.md): the patch's file replaces the installation's, the manifest records both candidates and names the layer.
    await File.WriteAllBytesAsync(Path.Combine(source, "Art", "Textures", "Test.dds"), CreateDds(1));
    var patchRoot = Path.Combine(root, "patch"); var patched = Path.Combine(root, "patched");
    Directory.CreateDirectory(Path.Combine(patchRoot, GeneralsGamePatchLayer.EditedFolder, "Window"));
    Directory.CreateDirectory(Path.Combine(patchRoot, GeneralsGamePatchLayer.EditedFolder, "Art", "Textures", "GenerateMip"));
    await File.WriteAllTextAsync(Path.Combine(patchRoot, GeneralsGamePatchLayer.EditedFolder, "Window", "Test.wnd"), "FILE_VERSION = 2;\nWINDOW { IMAGE = Art/Textures/Test.dds; }\n");
    await File.WriteAllBytesAsync(Path.Combine(patchRoot, GeneralsGamePatchLayer.EditedFolder, "Art", "Textures", "GenerateMip", "Source.psd"), [1, 2, 3]);
    var layer = GeneralsGamePatchLayer.Open(patchRoot, "test patch");
    Assert(layer.Files.Count == 1 && layer.Excluded.Values.Sum() == 1 && layer.Excluded.Keys.Single().StartsWith("texture source", StringComparison.Ordinal),
        "The layer takes the window layout and counts the texture source it cannot take.");
    var layered = await RuntimeContentImporter.ImportAsync(source, patched, layer: layer);
    var window = layered.Manifest.Assets.Single(asset => asset.Type == "sage/window-layout");
    Assert(window.Origin.SelectedSource == "overlay:generals-game-patch-core:Window/Test.wnd" && window.Origin.Candidates.Count == 2, "The patch's layout is selected over the installation's.");
    Assert(layered.Manifest.Importer.Options["layer"] == GeneralsGamePatchLayer.Name && layered.Manifest.Importer.Options["layerSha256"] == layer.Fingerprint
        && layered.Manifest.Importer.Options["layerFiles"] == "1", "The manifest names the layer and its fingerprint.");
    Assert(!firstResult.Manifest.Importer.Options.ContainsKey("layer"), "A manifest without a layer is unchanged.");
    Assert(GeneralsGamePatchLayer.Classify("Data/INI/Object/Test.ini") is null && GeneralsGamePatchLayer.Classify("Maps/A/map.str") is null
        && GeneralsGamePatchLayer.Classify("Data/generals.str") is not null && GeneralsGamePatchLayer.Classify("Art/Models/Mesh/a.blend") is not null,
        "The layer takes INI and map strings, not string sources and Blender scenes.");
    Console.WriteLine("All runtime content importer tests passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

// Options: --game-dir <directory> additionally checks the real master installation (reads only).
await InstallTests.RunAsync(args);

static byte[] CreateDds(byte fill)
{
    var data = Enumerable.Repeat(fill, 136).Select(value => (byte)value).ToArray(); "DDS "u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 4); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32); "DXT1"u8.CopyTo(data.AsSpan(84)); return data;
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
