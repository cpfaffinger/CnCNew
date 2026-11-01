using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;

namespace CnCNew.Content;

public sealed class ContentFileSystem
{
    /// <summary>Where the user's own maps lie in the content: nothing of the game's export is ever under this name.</summary>
    public const string UserMapPrefix = "UserMaps/";

    private readonly Dictionary<string, ContentFileEntry> _files;
    private readonly Dictionary<string, (ContentFileEntry Entry, string Path)> _userFiles = new(StringComparer.OrdinalIgnoreCase);

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
    {
        var normalized = ContentPath.Normalize(virtualPath);
        return _files.ContainsKey(normalized) || _userFiles.ContainsKey(normalized);
    }

    /// <summary>
    /// Reads the user's own maps from disk without touching the content, so that it can run beside the game: every directory below <paramref name="directory"/> that holds a <c>.map</c> of its
    /// name, as <c>getPath_UserData</c> + <c>Maps</c> has them (<c>populateMapListbox</c>), the files beside it (a <c>map.ini</c>) with it. A directory that cannot be read is named in the result.
    /// A file of <paramref name="known"/> with its size and time unchanged is taken as it is, so looking at the folder again costs a listing.
    /// </summary>
    public static UserMapScan ScanUserMaps(string directory, UserMapScan? known = null)
    {
        var unchanged = known?.Files.ToDictionary(file => file.DiskPath, StringComparer.OrdinalIgnoreCase);
        var files = ImmutableArray.CreateBuilder<UserMapFile>();
        var problems = ImmutableArray.CreateBuilder<string>();
        if (!Directory.Exists(directory)) return new UserMapScan(files.ToImmutable(), problems.ToImmutable());
        foreach (var folder in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(folder);
            if (!File.Exists(Path.Combine(folder, name + ".map"))) continue;
            try
            {
                var read = new List<UserMapFile>();
                foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var written = File.GetLastWriteTimeUtc(file);
                    if (unchanged is not null && unchanged.TryGetValue(file, out var same) && same.Written == written && same.Entry.Bytes == new FileInfo(file).Length)
                    {
                        read.Add(same);
                        continue;
                    }

                    var virtualPath = ContentPath.Normalize($"{UserMapPrefix}{name}/{Path.GetFileName(file)}");
                    var bytes = File.ReadAllBytes(file);
                    read.Add(new UserMapFile(new ContentFileEntry(ContentPath.ToAssetId(virtualPath), virtualPath, file, "user", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), "user", [], []), file) { Written = written });
                }

                files.AddRange(read);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {exception.Message}");
            }
        }

        return new UserMapScan(files.ToImmutable(), problems.ToImmutable());
    }

    /// <summary>Lays a scan over the content: a map is a content file like any other from here on, with its hash. Returns the maps' virtual paths in ordinal order.</summary>
    public IReadOnlyList<string> AddUserMaps(UserMapScan scan)
    {
        foreach (var file in scan.Files) _userFiles[file.Entry.VirtualPath] = (file.Entry, file.DiskPath);
        return scan.Maps;
    }

    public IReadOnlyList<string> AddUserMaps(string directory) => AddUserMaps(ScanUserMaps(directory));

    /// <summary>A newer look at the folder in place of <paramref name="before"/>: what is gone is gone from the content too.</summary>
    public IReadOnlyList<string> ReplaceUserMaps(UserMapScan? before, UserMapScan scan)
    {
        if (before is not null) foreach (var file in before.Files) _userFiles.Remove(file.Entry.VirtualPath);
        return AddUserMaps(scan);
    }

    public ContentFileEntry GetEntry(string virtualPath)
    {
        var normalized = ContentPath.Normalize(virtualPath);
        if (_userFiles.TryGetValue(normalized, out var user)) return user.Entry;
        return _files.TryGetValue(normalized, out var entry)
            ? entry
            : throw new FileNotFoundException($"Content file '{virtualPath}' was not found.", virtualPath);
    }

    /// <summary>
    /// The SHA-256 of a content file as the manifest records it, 64 upper-case hex digits: the identity of a file (a map, for instance) that two machines can
    /// compare without reading it. <see cref="VerifyIntegrityAsync"/> checks that the files on disk still have these hashes.
    /// </summary>
    public string FileHash(string virtualPath) => GetEntry(virtualPath).Sha256.ToUpperInvariant();

    public Stream OpenRead(string virtualPath)
    {
        if (_userFiles.TryGetValue(ContentPath.Normalize(virtualPath), out var user)) return new FileStream(user.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
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

/// <summary>One file of a user's map directory, read and hashed.</summary>
public sealed record UserMapFile(ContentFileEntry Entry, string DiskPath)
{
    public DateTime Written { get; init; }
}

/// <summary>What <see cref="ContentFileSystem.ScanUserMaps"/> found.</summary>
public sealed record UserMapScan(ImmutableArray<UserMapFile> Files, ImmutableArray<string> Problems)
{
    public ImmutableArray<string> Maps => [.. Files.Where(file => file.DiskPath.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Select(file => file.Entry.VirtualPath)];
}
