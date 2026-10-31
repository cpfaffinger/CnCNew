using System.Security.Cryptography;
using System.Text.Json;

namespace CnCNew.Import;

/// <summary>
/// The community patch every installation carries (<c>baselines/patch-master.json</c>, docs/PATCH-LAYER.md): the commit of its repository, the files the layer takes with
/// their sizes and hashes - facts about the files, never their content - and what the import of the game with this layer produces.
/// </summary>
public sealed record PatchMaster(string Repository, string Commit, string Directory, MasterExport ExpectedExport, IReadOnlyList<MasterFile> Files)
{
    public const int CurrentSchemaVersion = 1;
    public const string BuiltInResourceName = "patch-master.json";

    /// <summary>What the content manifest names as the layer's source.</summary>
    public string Source => $"{Repository} {Commit}";

    /// <summary>The address of a file of the layer: the blob of the recorded commit, whatever the line endings of a checkout are.</summary>
    public Uri Address(string path) => new($"https://raw.githubusercontent.com/{Repository}/{Commit}/{Directory}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}");

    /// <summary>The description compiled into this assembly from <c>baselines/patch-master.json</c>.</summary>
    public static PatchMaster LoadBuiltIn()
    {
        using var stream = typeof(PatchMaster).Assembly.GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidDataException($"The built-in patch description '{BuiltInResourceName}' is missing from {typeof(PatchMaster).Assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    public static PatchMaster Load(string path) => Read(File.ReadAllBytes(path));

    public byte[] Write()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("repository", Repository);
            writer.WriteString("commit", Commit);
            writer.WriteString("directory", Directory);
            writer.WriteStartObject("expectedExport");
            writer.WriteString("importer", ExpectedExport.Importer);
            writer.WriteString("importerVersion", ExpectedExport.ImporterVersion);
            writer.WriteNumber("contentManifestBytes", ExpectedExport.ContentManifestBytes);
            writer.WriteString("contentManifestSha256", ExpectedExport.ContentManifestSha256);
            writer.WriteNumber("runtimeFileCount", ExpectedExport.RuntimeFileCount);
            writer.WriteEndObject();
            writer.WriteStartArray("files");
            foreach (var file in Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteNumber("bytes", file.Bytes);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    public static PatchMaster Read(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CanonicalJson.RequireProperties(root, "patch description", "schemaVersion", "repository", "commit", "directory", "expectedExport", "files");
        var schema = root.GetProperty("schemaVersion").GetInt32();
        if (schema != CurrentSchemaVersion) throw new InvalidDataException($"Unsupported patch description schema {schema}; expected {CurrentSchemaVersion}.");
        var export = root.GetProperty("expectedExport");
        CanonicalJson.RequireProperties(export, "expectedExport", "importer", "importerVersion", "contentManifestBytes", "contentManifestSha256", "runtimeFileCount");
        var files = new List<MasterFile>();
        foreach (var item in root.GetProperty("files").EnumerateArray())
        {
            CanonicalJson.RequireProperties(item, "file entry", "path", "bytes", "sha256");
            files.Add(new MasterFile(item.GetProperty("path").GetString()!, item.GetProperty("bytes").GetInt64(), item.GetProperty("sha256").GetString()!));
        }
        return new PatchMaster(
            root.GetProperty("repository").GetString()!,
            root.GetProperty("commit").GetString()!,
            root.GetProperty("directory").GetString()!,
            new MasterExport(
                export.GetProperty("importer").GetString()!,
                export.GetProperty("importerVersion").GetString()!,
                export.GetProperty("contentManifestBytes").GetInt64(),
                export.GetProperty("contentManifestSha256").GetString()!,
                export.GetProperty("runtimeFileCount").GetInt32()),
            files);
    }
}

/// <summary>
/// Puts the sources of a <see cref="PatchMaster"/> into a directory, makes the runtime files of them (<see cref="PatchBuild"/>) and opens those as the layer. A source comes
/// from a directory the caller names (a checkout, whose text files may have CRLF where the blob has LF) or else from the recorded commit; whatever its way, it is taken
/// only with the recorded size and hash.
/// </summary>
public sealed class PatchFetcher(PatchMaster patch, Func<Uri, CancellationToken, Task<byte[]>>? download = null)
{
    private const int Parallelism = 8;
    private const int Attempts = 3;

    private readonly Func<Uri, CancellationToken, Task<byte[]>> _download = download ?? DownloadAsync;

    public async Task<ContentLayer> FetchAsync(string directory, string? checkout = null, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(Path.GetFullPath(directory), "source");
        var built = Path.Combine(Path.GetFullPath(directory), "built");
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;
        await Parallel.ForEachAsync(patch.Files, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken }, async (file, token) =>
        {
            var physical = InstallationInspector.Physical(folder, file.Path);
            if (!Matches(physical, file))
            {
                var bytes = FromCheckout(checkout, file) ?? await FromCommitAsync(file, token);
                Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
                await File.WriteAllBytesAsync(physical, bytes, token);
            }
            foreach (var (path, bytes) in PatchBuild.Outputs(file.Path, await File.ReadAllBytesAsync(physical, token)))
            {
                var target = InstallationInspector.Physical(built, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, token);
                lock (files) files.Add(path, target);
            }
            progress?.Report(new ImportProgress(Interlocked.Increment(ref done), patch.Files.Count, file.Path));
        });
        return new ContentLayer(GeneralsGamePatchLayer.Name, patch.Source, files, new SortedDictionary<string, int>(StringComparer.Ordinal));
    }

    private static bool Is(byte[] bytes, MasterFile file) => bytes.LongLength == file.Bytes && Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == file.Sha256;

    private static bool Matches(string physical, MasterFile file) => File.Exists(physical) && new FileInfo(physical).Length == file.Bytes && Is(File.ReadAllBytes(physical), file);

    private static byte[]? FromCheckout(string? checkout, MasterFile file)
    {
        if (checkout is null) return null;
        var path = InstallationInspector.Physical(Path.GetFullPath(checkout), file.Path);
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        if (Is(bytes, file)) return bytes;
        var unix = ToLf(bytes);
        return Is(unix, file) ? unix : null;
    }

    private async Task<byte[]> FromCommitAsync(MasterFile file, CancellationToken cancellationToken)
    {
        var address = patch.Address(file.Path);
        Exception? last = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                var bytes = await _download(address, cancellationToken);
                if (Is(bytes, file)) return bytes;
                last = new InvalidDataException($"{address} has {bytes.LongLength} bytes and another hash than recorded.");
            }
            catch (HttpRequestException error)
            {
                last = error;
            }
        }
        throw new InvalidDataException($"The patch file '{file.Path}' could not be fetched from {address}: {last?.Message} The installation needs the network once, for the patch.", last);
    }

    private static byte[] ToLf(byte[] bytes)
    {
        var result = new byte[bytes.Length];
        var length = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r' && index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n') continue;
            result[length++] = bytes[index];
        }
        return result[..length];
    }

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static Task<byte[]> DownloadAsync(Uri address, CancellationToken cancellationToken) => Client.GetByteArrayAsync(address, cancellationToken);
}
