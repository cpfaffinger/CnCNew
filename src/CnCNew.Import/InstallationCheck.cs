using System.Security.Cryptography;
using System.Text;
using CnCNew.Content;

namespace CnCNew.Import;

public sealed record InstallationCheckProgress(int ProcessedFiles, int TotalFiles, string Path);

/// <summary>A file of the master that exists with another size or, at the same size, another content. <see cref="ActualSha256"/> is null when the size already differs.</summary>
public sealed record ChangedFile(string Path, long ExpectedBytes, long ActualBytes, string ExpectedSha256, string? ActualSha256);

/// <summary>A file of the master that is missing under its name but lies, byte for byte, under <see cref="ActualPath"/>.</summary>
public sealed record RenamedFile(string ExpectedPath, string ActualPath);

public sealed record ExtraFile(string Path, long Bytes);

public sealed record InstallationCheckResult(
    InstallationMaster Master,
    int CheckedFiles,
    IReadOnlyList<MasterFile> Missing,
    IReadOnlyList<ChangedFile> Changed,
    IReadOnlyList<RenamedFile> Renamed,
    IReadOnlyList<ExtraFile> Extra,
    IReadOnlyList<ExtraFile> HarmlessExtra)
{
    public int DifferenceCount => Missing.Count + Changed.Count + Renamed.Count + Extra.Count;
    public bool IsIdentical => DifferenceCount == 0;

    /// <summary>The message for the user: every difference by name and what to do about it.</summary>
    public string FormatReport()
    {
        var text = new StringBuilder();
        if (IsIdentical)
        {
            text.Append($"The game directory is the required master \"{Master.Name}\" (Steam app {Master.SteamAppId}): ")
                .Append($"{Master.FileCount} files, {Master.TotalBytes} bytes, every size and SHA-256 matches.\n");
            if (HarmlessExtra.Count != 0)
            {
                text.Append($"{HarmlessExtra.Count} additional file(s) are on the master's list of harmless files:\n");
                foreach (var file in HarmlessExtra) text.Append($"  harmless: {file.Path} ({file.Bytes} bytes)\n");
            }
            return text.ToString();
        }

        text.Append($"The game directory is NOT the required master \"{Master.Name}\" (Steam app {Master.SteamAppId}): {DifferenceCount} difference(s).\n");
        foreach (var file in Missing) text.Append($"  missing: {file.Path} ({file.Bytes} bytes expected)\n");
        foreach (var file in Renamed) text.Append($"  renamed: {file.ExpectedPath} is now {file.ActualPath}\n");
        foreach (var file in Changed)
            text.Append(file.ActualSha256 is null
                ? $"  changed: {file.Path} ({file.ActualBytes} bytes, expected {file.ExpectedBytes})\n"
                : $"  changed: {file.Path} (same size, SHA-256 {file.ActualSha256}, expected {file.ExpectedSha256})\n");
        foreach (var file in Extra) text.Append($"  extra:   {file.Path} ({file.Bytes} bytes)\n");
        foreach (var file in HarmlessExtra) text.Append($"  harmless (not a difference): {file.Path} ({file.Bytes} bytes)\n");
        text.Append("What to do:\n");
        if (Extra.Count != 0 || Renamed.Count != 0)
            text.Append("  - Remove mods and every added file (the \"extra\" entries, the new names of the \"renamed\" entries) from the game directory.\n");
        text.Append("  - In Steam: Library -> right-click the game -> Properties -> Installed Files -> \"Verify integrity of game files\".\n");
        text.Append("    Steam restores missing and changed files; it does not delete files it does not know.\n");
        text.Append("  - The required version is a fresh ENGLISH Steam installation; switch the language under Properties -> General if needed.\n");
        text.Append("  - Run the check again. Only this exact version is accepted; nothing was written or changed.\n");
        return text.ToString();
    }
}

/// <summary>Reads a game directory (never writes to it) and describes it or compares it with an <see cref="InstallationMaster"/>.</summary>
public static class InstallationInspector
{
    public const string SteamAppIdFile = "steam_appid.txt";

    /// <summary>The relative paths (forward slashes) and sizes of every file below <paramref name="gameDirectory"/>, in ordinal path order.</summary>
    public static IReadOnlyList<ExtraFile> ListFiles(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The game directory '{gameDirectory}' does not exist.");
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new ExtraFile(Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The Steam app id as the installation states it in <c>steam_appid.txt</c> (digits, then optional line end and NUL padding).</summary>
    public static long ReadSteamAppId(string gameDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(gameDirectory), SteamAppIdFile);
        if (!File.Exists(path)) throw new InvalidDataException($"'{SteamAppIdFile}' was not found in '{gameDirectory}': this is not a Steam installation of the game.");
        byte[] bytes;
        using (var stream = OpenSource(path))
        {
            bytes = new byte[checked((int)Math.Min(stream.Length, 64))];
            stream.ReadExactly(bytes);
        }
        var text = Encoding.ASCII.GetString(bytes).TrimEnd('\0', '\r', '\n', ' ');
        return text.Length != 0 && text.All(char.IsAsciiDigit) && long.TryParse(text, out var id) && id > 0
            ? id
            : throw new InvalidDataException($"'{SteamAppIdFile}' does not contain a Steam app id.");
    }

    /// <summary>
    /// Describes <paramref name="gameDirectory"/> as a master: every file with size and hash, and the manifest the runtime import produces from it
    /// (computed without writing anything). Two runs over the same directory give byte-identical output of <see cref="InstallationMasterSerializer.Write"/>.
    /// </summary>
    public static async Task<InstallationMaster> DescribeAsync(
        string gameDirectory,
        string? name = null,
        string? id = null,
        IProgress<InstallationCheckProgress>? progress = null,
        IProgress<ImportProgress>? importProgress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(gameDirectory);
        var appId = ReadSteamAppId(root);
        var listed = ListFiles(root);
        var files = new List<MasterFile>(listed.Count);
        for (var index = 0; index < listed.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallationCheckProgress(index, listed.Count, listed[index].Path));
            var (bytes, hash) = await HashAsync(Physical(root, listed[index].Path), cancellationToken);
            files.Add(new MasterFile(listed[index].Path, bytes, hash));
        }
        var described = await RuntimeContentImporter.DescribeAsync(root, importProgress, cancellationToken);
        var manifest = RuntimeManifestSerializer.WriteCanonical(described);
        var export = new MasterExport(
            RuntimeContentImporter.ImporterName,
            RuntimeContentImporter.ImporterVersion,
            manifest.LongLength,
            Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(),
            described.Assets.Count);
        return new InstallationMaster(id ?? $"steam-{appId}-english", name ?? InstallationMaster.DefaultName, appId, export, [], files);
    }

    public static async Task<InstallationCheckResult> CheckAsync(
        string gameDirectory,
        InstallationMaster master,
        IProgress<InstallationCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        var root = Path.GetFullPath(gameDirectory);
        var actual = ListFiles(root).ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var harmless = new HashSet<string>(master.HarmlessExtraFiles, StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(master.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
        var missing = new List<MasterFile>();
        var changed = new List<ChangedFile>();

        for (var index = 0; index < master.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = master.Files[index];
            progress?.Report(new InstallationCheckProgress(index, master.Files.Count, expected.Path));
            if (!actual.TryGetValue(expected.Path, out var found)) { missing.Add(expected); continue; }
            if (found.Bytes != expected.Bytes) { changed.Add(new ChangedFile(expected.Path, expected.Bytes, found.Bytes, expected.Sha256, null)); continue; }
            var (bytes, hash) = await HashAsync(Physical(root, found.Path), cancellationToken);
            if (bytes != expected.Bytes || hash != expected.Sha256)
                changed.Add(new ChangedFile(expected.Path, expected.Bytes, bytes, expected.Sha256, bytes == expected.Bytes ? hash : null));
        }

        var extra = new List<ExtraFile>();
        var harmlessFound = new List<ExtraFile>();
        foreach (var file in actual.Values.OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            if (known.Contains(file.Path)) continue;
            (harmless.Contains(file.Path) ? harmlessFound : extra).Add(file);
        }

        // A missing file whose exact bytes lie under another name was renamed (a deactivated archive, for example); only candidates of equal size are hashed.
        var renamed = new List<RenamedFile>();
        foreach (var expected in missing.ToArray())
        {
            foreach (var candidate in extra.Where(file => file.Bytes == expected.Bytes).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (_, hash) = await HashAsync(Physical(root, candidate.Path), cancellationToken);
                if (hash != expected.Sha256) continue;
                renamed.Add(new RenamedFile(expected.Path, candidate.Path));
                missing.Remove(expected);
                extra.Remove(candidate);
                break;
            }
        }

        return new InstallationCheckResult(master, master.Files.Count, missing, changed, renamed, extra, harmlessFound);
    }

    public static string Physical(string root, string relativePath)
        => Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    // The source is only ever opened for reading, shared for reading.
    internal static FileStream OpenSource(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.SequentialScan);

    internal static async Task<(long Bytes, string Sha256)> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = OpenSource(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
