using System.Buffers.Binary;
using System.Text;
using CnCNew.IO;

var root = Path.Combine(Path.GetTempPath(), $"CnCNew-io-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(root, "Data", "INI"));
try
{
    await File.WriteAllTextAsync(Path.Combine(root, "Data", "INI", "Test.ini"), "loose");
    CreateArchive(Path.Combine(root, "A.big"), "Data/INI/Test.ini", "archive-a"u8.ToArray());
    CreateArchive(Path.Combine(root, "Z.big"), "Data/INI/Test.ini", "archive-z"u8.ToArray());

    using var fileSystem = GameFileSystem.OpenInstallation(root);
    Assert(await ReadAsync(fileSystem, "data\\ini\\test.ini", 0) == "loose", "Loose files must win.");
    Assert(await ReadAsync(fileSystem, "Data/INI/Test.ini", 1) == "archive-a", "First BIG name must win.");
    Assert(await ReadAsync(fileSystem, "Data/INI/Test.ini", 2) == "archive-z", "Later BIG must remain available.");
    Assert(!fileSystem.Exists("Data/INI/Test.ini", 3), "Unexpected fourth instance.");

    var candidates = fileSystem.GetCandidates("DATA/INI/TEST.INI");
    Assert(candidates.Count == 3, "Every overlay candidate must be visible.");
    Assert(candidates[1].Source == "big:A.big", "Candidate source mismatch.");

    // A layer above the installation (a community patch) is instance zero of its paths; the installation's own candidates stay behind it.
    var layerFile = Path.Combine(root, "layer-test.ini");
    await File.WriteAllTextAsync(layerFile, "layer");
    var addedFile = Path.Combine(root, "layer-added.ini");
    await File.WriteAllTextAsync(addedFile, "added");
    var overlay = new GameFileOverlay("patch", new Dictionary<string, string> { ["Data/INI/Test.ini"] = layerFile, ["Data/INI/New.ini"] = addedFile });
    using (var layered = GameFileSystem.OpenInstallation(root, overlay))
    {
        Assert(await ReadAsync(layered, "Data/INI/Test.ini", 0) == "layer", "The layer must win over loose files and archives.");
        Assert(await ReadAsync(layered, "Data/INI/Test.ini", 1) == "loose", "The installation's files stay behind the layer.");
        Assert(layered.GetCandidates("Data/INI/Test.ini")[0].Source == "overlay:patch:Data/INI/Test.ini" && layered.GetCandidates("Data/INI/Test.ini").Count == 4,
            "The layer's candidate names the layer and the path.");
        Assert(await ReadAsync(layered, "Data/INI/New.ini", 0) == "added" && layered.GetCandidates("Data/INI/New.ini").Count == 1, "A layer can add a file.");
    }
    var twice = new GameFileOverlay("patch", new Dictionary<string, string>(StringComparer.Ordinal) { ["Data/INI/Twice.ini"] = layerFile, ["data/ini/twice.ini"] = addedFile });
    try { using var _ = GameFileSystem.OpenInstallation(root, twice); throw new InvalidOperationException("A layer naming one path twice was accepted."); }
    catch (InvalidDataException) { }
    Console.WriteLine("All virtual file-system tests passed.");
    return 0;
}
finally
{
    Directory.Delete(root, recursive: true);
}

static async Task<string> ReadAsync(GameFileSystem fileSystem, string path, int instance)
{
    await using var stream = fileSystem.OpenRead(path, instance);
    using var reader = new StreamReader(stream, Encoding.Latin1);
    return await reader.ReadToEndAsync();
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void CreateArchive(string path, string entryPath, byte[] payload)
{
    var encodedPath = Encoding.Latin1.GetBytes(entryPath);
    var directoryEnd = 16 + 8 + encodedPath.Length + 1;
    var totalSize = directoryEnd + payload.Length;
    using var stream = File.Create(path);
    stream.Write("BIGF"u8);

    Span<byte> number = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(number, checked((uint)totalSize));
    stream.Write(number);
    BinaryPrimitives.WriteUInt32BigEndian(number, 1);
    stream.Write(number);
    BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)directoryEnd));
    stream.Write(number);
    BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)directoryEnd));
    stream.Write(number);
    BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)payload.Length));
    stream.Write(number);
    stream.Write(encodedPath);
    stream.WriteByte(0);
    stream.Write(payload);
}

