using CnCNew.Archives;

namespace CnCNew.IO;

public sealed class GameFileSystem : IDisposable
{
    private readonly Dictionary<string, List<FileRecord>> _files;
    private readonly List<BigArchive> _archives;

    private GameFileSystem(
        string rootDirectory,
        Dictionary<string, List<FileRecord>> files,
        List<BigArchive> archives)
    {
        RootDirectory = rootDirectory;
        _files = files;
        _archives = archives;
    }

    public string RootDirectory { get; }
    public int UniqueFileCount => _files.Count;
    public int ArchiveCount => _archives.Count;

    /// <summary>
    /// Opens an installation as the original's file system sees it: loose files before every archive instance, archives in ascending name order.
    /// <paramref name="overlay"/> (optional) is a layer above everything - a community patch's files, by virtual path - that becomes instance zero of each of its
    /// paths; the installation's own candidates stay behind it, so the content manifest records what the layer replaced (<c>overlay:NAME:PATH</c>).
    /// </summary>
    public static GameFileSystem OpenInstallation(string rootDirectory, GameFileOverlay? overlay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var files = new Dictionary<string, List<FileRecord>>(StringComparer.OrdinalIgnoreCase);
        var archives = new List<BigArchive>();
        try
        {
            if (overlay is not null)
                foreach (var (virtualPath, physicalPath) in overlay.Files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var normalized = NormalizeVirtualPath(virtualPath);
                    if (files.ContainsKey(normalized)) throw new InvalidDataException($"The overlay '{overlay.Name}' names '{normalized}' twice.");
                    Add(files, normalized, FileRecord.ForOverlayFile(normalized, overlay.Name, Path.GetFullPath(physicalPath)));
                }

            // The original FileSystem checks loose files before every archive instance.
            foreach (var physicalPath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                var virtualPath = NormalizeVirtualPath(Path.GetRelativePath(root, physicalPath));
                Add(files, virtualPath, FileRecord.ForLooseFile(virtualPath, physicalPath));
            }

            // SAGE collects BIG names in a case-insensitive std::set, then loads them in ascending order.
            // With overwrite=false the first archive containing a path remains instance zero.
            var archivePaths = Directory.EnumerateFiles(root, "*.big", SearchOption.AllDirectories)
                .Where(path => !NormalizeVirtualPath(Path.GetRelativePath(root, path))
                    .EndsWith("Data/INI/INIZH.big", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase);

            foreach (var archivePath in archivePaths)
            {
                var archive = BigArchive.Open(archivePath);
                archives.Add(archive);
                var archiveName = NormalizeVirtualPath(Path.GetRelativePath(root, archivePath));
                foreach (var entry in archive.Entries)
                {
                    var virtualPath = NormalizeVirtualPath(entry.Path);
                    Add(files, virtualPath, FileRecord.ForArchiveEntry(virtualPath, archiveName, archive, entry));
                }
            }

            return new GameFileSystem(root, files, archives);
        }
        catch
        {
            foreach (var archive in archives)
            {
                archive.Dispose();
            }

            throw;
        }
    }

    public bool Exists(string virtualPath, int instance = 0)
        => TryGetRecord(virtualPath, instance, out _);

    public Stream OpenRead(string virtualPath, int instance = 0)
    {
        if (!TryGetRecord(virtualPath, instance, out var record))
        {
            throw new FileNotFoundException(
                $"Game file '{virtualPath}' instance {instance} was not found.",
                virtualPath);
        }

        return record.OpenRead();
    }

    public IReadOnlyList<GameFileCandidate> GetCandidates(string virtualPath)
    {
        var normalizedPath = NormalizeVirtualPath(virtualPath);
        if (!_files.TryGetValue(normalizedPath, out var records))
        {
            return [];
        }

        return records
            .Select((record, index) => new GameFileCandidate(record.VirtualPath, record.Source, record.Length, index))
            .ToArray();
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

    public void Dispose()
    {
        foreach (var archive in _archives)
        {
            archive.Dispose();
        }
    }

    private bool TryGetRecord(string virtualPath, int instance, out FileRecord record)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instance);
        var normalizedPath = NormalizeVirtualPath(virtualPath);
        if (_files.TryGetValue(normalizedPath, out var records) && instance < records.Count)
        {
            record = records[instance];
            return true;
        }

        record = null!;
        return false;
    }

    private static void Add(Dictionary<string, List<FileRecord>> files, string path, FileRecord record)
    {
        if (!files.TryGetValue(path, out var records))
        {
            records = [];
            files.Add(path, records);
        }

        records.Add(record);
    }

    private static string NormalizeVirtualPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid virtual game path '{path}'.");
        }

        return string.Join('/', segments);
    }

    private sealed class FileRecord
    {
        private readonly string? _physicalPath;
        private readonly BigArchive? _archive;
        private readonly BigArchiveEntry? _entry;

        private FileRecord(
            string virtualPath,
            string source,
            long length,
            string? physicalPath,
            BigArchive? archive,
            BigArchiveEntry? entry)
        {
            VirtualPath = virtualPath;
            Source = source;
            Length = length;
            _physicalPath = physicalPath;
            _archive = archive;
            _entry = entry;
        }

        public string VirtualPath { get; }
        public string Source { get; }
        public long Length { get; }

        public static FileRecord ForLooseFile(string virtualPath, string physicalPath)
        {
            var info = new FileInfo(physicalPath);
            return new FileRecord(virtualPath, $"loose:{virtualPath}", info.Length, physicalPath, null, null);
        }

        public static FileRecord ForOverlayFile(string virtualPath, string overlayName, string physicalPath)
            => new(virtualPath, $"overlay:{overlayName}:{virtualPath}", new FileInfo(physicalPath).Length, physicalPath, null, null);

        public static FileRecord ForArchiveEntry(
            string virtualPath,
            string archiveName,
            BigArchive archive,
            BigArchiveEntry entry)
            => new(virtualPath, $"big:{archiveName}", entry.Length, null, archive, entry);

        public Stream OpenRead()
            => _physicalPath is not null
                ? new FileStream(_physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                : _archive!.OpenEntry(_entry!);
    }
}

/// <summary>
/// A layer of files above an installation (<see cref="GameFileSystem.OpenInstallation"/>): a name for the provenance and, per virtual path, the physical file that
/// replaces or adds it.
/// </summary>
public sealed record GameFileOverlay(string Name, IReadOnlyDictionary<string, string> Files);
