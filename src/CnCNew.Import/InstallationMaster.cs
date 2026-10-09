using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CnCNew.Import;

/// <summary>One file of a game installation: relative path with forward slashes, size and lower-case SHA-256. A fact about the file, never its content.</summary>
public sealed record MasterFile(string Path, long Bytes, string Sha256);

/// <summary>What <see cref="RuntimeContentImporter"/> produces from the master: one comparison decides whether an export is exactly the known one.</summary>
public sealed record MasterExport(string Importer, string ImporterVersion, long ContentManifestBytes, string ContentManifestSha256, int RuntimeFileCount);

/// <summary>
/// The description of the one game installation the project accepts as its source (<c>baselines/steam-master.json</c>).
/// <see cref="HarmlessExtraFiles"/> is the explicit list of additional files that do not make an installation different; everything else does.
/// </summary>
public sealed record InstallationMaster(
    string Id,
    string Name,
    long SteamAppId,
    MasterExport ExpectedExport,
    IReadOnlyList<string> HarmlessExtraFiles,
    IReadOnlyList<MasterFile> Files)
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultName = "Command & Conquer Generals Zero Hour, Steam, English";
    public const string BuiltInResourceName = "steam-master.json";

    public int FileCount => Files.Count;
    public long TotalBytes => Files.Sum(file => file.Bytes);

    /// <summary>The master description compiled into this assembly from <c>baselines/steam-master.json</c>.</summary>
    public static InstallationMaster LoadBuiltIn()
    {
        using var stream = typeof(InstallationMaster).Assembly.GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidDataException($"The built-in master description '{BuiltInResourceName}' is missing from {typeof(InstallationMaster).Assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return InstallationMasterSerializer.Read(buffer.ToArray());
    }

    public static InstallationMaster Load(string path) => InstallationMasterSerializer.Read(File.ReadAllBytes(path));
}

/// <summary>Canonical JSON of a master description: UTF-8 without BOM, LF, two-space indentation, files in ordinal path order.</summary>
public static class InstallationMasterSerializer
{
    public static byte[] Write(InstallationMaster master)
    {
        master = Canonicalize(master);
        Validate(master);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", InstallationMaster.CurrentSchemaVersion);
            writer.WriteString("id", master.Id);
            writer.WriteString("name", master.Name);
            writer.WriteNumber("steamAppId", master.SteamAppId);
            writer.WriteNumber("fileCount", master.FileCount);
            writer.WriteNumber("totalBytes", master.TotalBytes);
            writer.WriteStartObject("expectedExport");
            writer.WriteString("importer", master.ExpectedExport.Importer);
            writer.WriteString("importerVersion", master.ExpectedExport.ImporterVersion);
            writer.WriteNumber("contentManifestBytes", master.ExpectedExport.ContentManifestBytes);
            writer.WriteString("contentManifestSha256", master.ExpectedExport.ContentManifestSha256);
            writer.WriteNumber("runtimeFileCount", master.ExpectedExport.RuntimeFileCount);
            writer.WriteEndObject();
            writer.WriteStartArray("harmlessExtraFiles");
            foreach (var path in master.HarmlessExtraFiles) writer.WriteStringValue(path);
            writer.WriteEndArray();
            writer.WriteStartArray("files");
            foreach (var file in master.Files)
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

    public static InstallationMaster Read(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CanonicalJson.RequireProperties(root, "master description",
            "schemaVersion", "id", "name", "steamAppId", "fileCount", "totalBytes", "expectedExport", "harmlessExtraFiles", "files");
        var schema = root.GetProperty("schemaVersion").GetInt32();
        if (schema != InstallationMaster.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported master description schema {schema}; expected {InstallationMaster.CurrentSchemaVersion}.");
        var export = root.GetProperty("expectedExport");
        CanonicalJson.RequireProperties(export, "expectedExport",
            "importer", "importerVersion", "contentManifestBytes", "contentManifestSha256", "runtimeFileCount");
        var files = new List<MasterFile>();
        foreach (var item in root.GetProperty("files").EnumerateArray())
        {
            CanonicalJson.RequireProperties(item, "file entry", "path", "bytes", "sha256");
            files.Add(new MasterFile(item.GetProperty("path").GetString()!, item.GetProperty("bytes").GetInt64(), item.GetProperty("sha256").GetString()!));
        }
        var master = new InstallationMaster(
            root.GetProperty("id").GetString()!,
            root.GetProperty("name").GetString()!,
            root.GetProperty("steamAppId").GetInt64(),
            new MasterExport(
                export.GetProperty("importer").GetString()!,
                export.GetProperty("importerVersion").GetString()!,
                export.GetProperty("contentManifestBytes").GetInt64(),
                export.GetProperty("contentManifestSha256").GetString()!,
                export.GetProperty("runtimeFileCount").GetInt32()),
            root.GetProperty("harmlessExtraFiles").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            files);
        Validate(master);
        if (!master.Files.Select(file => file.Path).SequenceEqual(Canonicalize(master).Files.Select(file => file.Path), StringComparer.Ordinal))
            throw new InvalidDataException("The files of the master description are not in canonical (ordinal) order.");
        if (root.GetProperty("fileCount").GetInt32() != master.FileCount)
            throw new InvalidDataException($"The master description states {root.GetProperty("fileCount").GetInt32()} files but lists {master.FileCount}.");
        if (root.GetProperty("totalBytes").GetInt64() != master.TotalBytes)
            throw new InvalidDataException($"The master description states {root.GetProperty("totalBytes").GetInt64()} bytes but its files sum to {master.TotalBytes}.");
        return master;
    }

    private static InstallationMaster Canonicalize(InstallationMaster master) => master with
    {
        Files = master.Files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
        HarmlessExtraFiles = master.HarmlessExtraFiles.Order(StringComparer.Ordinal).ToArray(),
    };

    private static void Validate(InstallationMaster master)
    {
        if (string.IsNullOrWhiteSpace(master.Id) || string.IsNullOrWhiteSpace(master.Name))
            throw new InvalidDataException("A master description needs an id and a name.");
        if (master.SteamAppId <= 0) throw new InvalidDataException("A master description needs the Steam app id of the installation.");
        if (!IsSha256(master.ExpectedExport.ContentManifestSha256))
            throw new InvalidDataException("The expected content manifest hash must be 64 lower-case hex digits.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in master.Files)
        {
            ValidatePath(file.Path);
            if (file.Bytes < 0 || !IsSha256(file.Sha256)) throw new InvalidDataException($"Invalid size or hash for '{file.Path}' in the master description.");
            if (!seen.Add(file.Path)) throw new InvalidDataException($"Duplicate path '{file.Path}' in the master description.");
        }
        foreach (var path in master.HarmlessExtraFiles)
        {
            ValidatePath(path);
            if (!seen.Add(path)) throw new InvalidDataException($"Harmless extra file '{path}' is listed twice or is a file of the master.");
        }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.StartsWith('/') || path.Contains(':') ||
            path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"'{path}' is not a relative path with forward slashes.");
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class CanonicalJson
{
    public static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Nothing falls back silently: a missing or an unknown property makes the document invalid.
    public static void RequireProperties(JsonElement element, string what, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"The {what} must be a JSON object.");
        var present = element.EnumerateObject().Select(property => property.Name).ToArray();
        foreach (var name in names)
            if (!present.Contains(name, StringComparer.Ordinal)) throw new InvalidDataException($"The {what} lacks the property '{name}'.");
        foreach (var name in present)
            if (!names.Contains(name, StringComparer.Ordinal)) throw new InvalidDataException($"The {what} has the unknown property '{name}'.");
    }
}

internal static class ProductInfo
{
    public const string Product = "CnCNew";

    // "1.0.0+<commit>" when the build knows its source revision.
    public static (string Version, string Commit) Read()
    {
        var informational = typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational)) return ("unknown", "unknown");
        var plus = informational.IndexOf('+');
        return plus < 0 ? (informational, "unknown") : (informational[..plus], informational[(plus + 1)..]);
    }
}
