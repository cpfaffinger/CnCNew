using System.Text.Json;
using System.Security.Cryptography;

namespace CnCNew.Content;

public sealed class ContentFileSystem
{
    private readonly Dictionary<string, ContentFileEntry> _files;

    private ContentFileSystem(string rootDirectory, Dictionary<string, ContentFileEntry> files)
    {
        RootDirectory = rootDirectory;
        _files = files;
    }

    public string RootDirectory { get; }
    public int FileCount => _files.Count;

    public static ContentFileSystem Open(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        var manifestPath = Path.Combine(root, "content-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The materialized content manifest was not found.", manifestPath);
        }

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        if (manifest.RootElement.ValueKind == JsonValueKind.Object)
        {
            using var stream = File.OpenRead(manifestPath);
            return OpenVersioned(root, RuntimeManifestSerializer.Read(stream));
        }

        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The content manifest root must be an array.");
        }

        var files = new Dictionary<string, ContentFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var virtualPath = ContentPath.Normalize(GetRequiredString(item, "VirtualPath"));
            var physicalPath = GetRequiredString(item, "PhysicalPath").Replace('/', Path.DirectorySeparatorChar);
            var bytes = item.GetProperty("Bytes").GetInt64();
            var sha256 = GetRequiredString(item, "Sha256");
            var selectedSource = GetRequiredString(item, "SelectedSource");
            if (bytes < 0 || sha256.Length != 64)
            {
                throw new InvalidDataException($"Invalid metadata for '{virtualPath}'.");
            }

            var resolvedPath = ResolveContainedPath(root, physicalPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException($"Materialized content file '{virtualPath}' is missing.", resolvedPath);
            }

            if (!files.TryAdd(virtualPath, new ContentFileEntry(
                    ContentPath.ToAssetId(virtualPath),
                    virtualPath,
                    Path.GetRelativePath(root, resolvedPath).Replace('\\', '/'),
                    "legacy/unknown",
                    bytes,
                    sha256,
                    selectedSource,
                    [],
                    [new RuntimeDiagnostic("warning", "legacy-manifest", "Loaded schema-1 array manifest; re-import to migrate to schema 2.")])))
            {
                throw new InvalidDataException($"Duplicate virtual content path '{virtualPath}'.");
            }
        }

        return new ContentFileSystem(root, files);
    }

    public bool Exists(string virtualPath)
        => _files.ContainsKey(ContentPath.Normalize(virtualPath));

    public ContentFileEntry GetEntry(string virtualPath)
    {
        var normalized = ContentPath.Normalize(virtualPath);
        return _files.TryGetValue(normalized, out var entry)
            ? entry
            : throw new FileNotFoundException($"Content file '{virtualPath}' was not found.", virtualPath);
    }

    public Stream OpenRead(string virtualPath)
    {
        var entry = GetEntry(virtualPath);
        var physicalPath = ResolveContainedPath(RootDirectory, entry.PhysicalPath);
        var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != entry.Bytes)
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"Content file '{entry.VirtualPath}' has {new FileInfo(physicalPath).Length} bytes; expected {entry.Bytes}.");
        }

        return stream;
    }

    public async Task VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in _files.Values.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            await using var stream = OpenRead(entry.VirtualPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Content file '{entry.VirtualPath}' SHA-256 mismatch.");
            }
        }
    }

    public IEnumerable<string> EnumerateFiles(string prefix = "")
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : ContentPath.Normalize(prefix).TrimEnd('/') + '/';
        return _files.Keys
            .Where(path => path.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRequiredString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new InvalidDataException($"Content manifest property '{propertyName}' is missing or invalid.");

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Content path '{relativePath}' must be relative.");
        }

        var resolved = Path.GetFullPath(relativePath, root);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Content path '{relativePath}' escapes the content directory.");
        }

        return resolved;
    }

    private static ContentFileSystem OpenVersioned(string root, RuntimeContentManifest manifest)
    {
        var files = new Dictionary<string, ContentFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            var artifact = asset.Artifacts.Count == 1
                ? asset.Artifacts[0]
                : throw new InvalidDataException($"Asset '{asset.Id}' must have exactly one primary runtime artifact in schema 2.");
            var resolved = ResolveContainedPath(root, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Runtime artifact for '{asset.Id}' is missing.", resolved);
            }

            files.Add(asset.VirtualPath, new ContentFileEntry(
                asset.Id, asset.VirtualPath, artifact.Path, asset.Type, artifact.Bytes, artifact.Sha256,
                asset.Origin.SelectedSource, asset.Dependencies, asset.Diagnostics));
        }

        return new ContentFileSystem(root, files);
    }
}
