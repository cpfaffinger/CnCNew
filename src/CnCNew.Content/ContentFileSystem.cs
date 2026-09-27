using System.Text.Json;

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
        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The content manifest root must be an array.");
        }

        var files = new Dictionary<string, ContentFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var virtualPath = NormalizeVirtualPath(GetRequiredString(item, "VirtualPath"));
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
                    virtualPath,
                    Path.GetRelativePath(root, resolvedPath).Replace('\\', '/'),
                    bytes,
                    sha256,
                    selectedSource)))
            {
                throw new InvalidDataException($"Duplicate virtual content path '{virtualPath}'.");
            }
        }

        return new ContentFileSystem(root, files);
    }

    public bool Exists(string virtualPath)
        => _files.ContainsKey(NormalizeVirtualPath(virtualPath));

    public ContentFileEntry GetEntry(string virtualPath)
    {
        var normalized = NormalizeVirtualPath(virtualPath);
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

    public IEnumerable<string> EnumerateFiles(string prefix = "")
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : NormalizeVirtualPath(prefix).TrimEnd('/') + '/';
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

    private static string NormalizeVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var segments = path.Replace('\\', '/').TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid virtual content path '{path}'.");
        }

        return string.Join('/', segments);
    }
}
