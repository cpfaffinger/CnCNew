using System.Buffers.Binary;
using System.Text;
using CnCNew.Archives;

var temporaryRoot = Path.Combine(Path.GetTempPath(), $"CnCNew-archive-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);
try
{
    var archivePath = Path.Combine(temporaryRoot, "fixture.big");
    CreateArchive(archivePath, "Data/INI/Test.ini", "answer = 42\n"u8.ToArray());

    using var archive = BigArchive.Open(archivePath);
    Assert(archive.Entries.Count == 1, "Expected one archive entry.");
    Assert(archive.Entries[0].Path == "Data/INI/Test.ini", "Expected normalized entry path.");

    var output = Path.Combine(temporaryRoot, "output");
    await archive.ExtractToAsync(output);
    var extracted = await File.ReadAllTextAsync(Path.Combine(output, "Data", "INI", "Test.ini"));
    Assert(extracted == "answer = 42\n", "Extracted content differs.");

    var legacyArchivePath = Path.Combine(temporaryRoot, "legacy-name.big");
    CreateArchive(legacyArchivePath, "Data/*", []);
    using var legacyArchive = BigArchive.Open(legacyArchivePath);
    await legacyArchive.ExtractToAsync(output);
    Assert(File.Exists(Path.Combine(output, "Data", "%2A")), "Invalid Windows characters must be reversibly escaped.");
    Console.WriteLine("All archive tests passed.");
    return 0;
}
finally
{
    Directory.Delete(temporaryRoot, recursive: true);
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

