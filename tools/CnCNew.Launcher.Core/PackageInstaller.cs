using System.IO.Compression;
using System.Text.Json;

namespace CnCNew.Launcher;

public sealed class PackageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Unpacks the client package and puts it into the install folder without touching what is the user's: the <c>game</c> folder (the content made from the Steam copy)
/// stays, everything the package brings is replaced. A package is first unpacked into a staging folder inside the install folder and checked; only then is the
/// old client replaced, so a broken download never leaves a half-updated client.
/// </summary>
public static class PackageInstaller
{
    /// <summary>What a package must hold to be a client package at all.</summary>
    public static readonly string[] RequiredFiles = [Path.Combine("bin", "CnCNew.Client.Stride.exe"), Path.Combine("patcher", "CnCNew.AssetTool.exe"), "Start-CnCNew.cmd"];

    public static void Unpack(string zipPath, string stagingDirectory, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        Directory.CreateDirectory(stagingDirectory);
        var staging = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var prefix = CommonRoot(archive);
            var total = Math.Max(1, archive.Entries.Count);
            var done = 0;
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                done++;
                var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (prefix.Length > 0 && name.StartsWith(prefix, StringComparison.Ordinal)) name = name[prefix.Length..];
                if (name.Length == 0) continue;
                var destination = Path.GetFullPath(Path.Combine(staging, name));
                // A name that climbs out of the staging folder is an attack, not a package.
                if (!destination.StartsWith(staging, StringComparison.OrdinalIgnoreCase)) throw new PackageException("The download holds a file with an unsafe name. It was not unpacked.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                if (done % 50 == 0) progress?.Report((double)done / total);
            }
            progress?.Report(1);
        }
        catch (InvalidDataException exception)
        {
            throw new PackageException("The downloaded file is not a valid package. Try the download again.", exception);
        }
        foreach (var required in RequiredFiles)
            if (!File.Exists(Path.Combine(stagingDirectory, required))) throw new PackageException($"The package does not hold {required.Replace('\\', '/')}. It is not a client package of this launcher's kind.");
    }

    /// <summary>Replaces the client in <paramref name="layout"/> with the unpacked package in <paramref name="stagingDirectory"/>. The folder <c>game</c> and anything unknown stay.</summary>
    public static void Apply(string stagingDirectory, InstallLayout layout, ReleaseRecord record)
    {
        Directory.CreateDirectory(layout.Root);
        try
        {
            foreach (var directory in Directory.GetDirectories(stagingDirectory))
            {
                var name = Path.GetFileName(directory);
                if (InstallLayout.UserEntries.Contains(name)) continue;
                var target = Path.Combine(layout.Root, name);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                Directory.Move(directory, target);
            }
            foreach (var file in Directory.GetFiles(stagingDirectory))
            {
                var target = Path.Combine(layout.Root, Path.GetFileName(file));
                File.Move(file, target, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PackageException("The old files could not be replaced. If the game or its window is open, close it and try again.", exception);
        }
        WriteFileList(layout);
        layout.WriteRelease(record);
        try { Directory.Delete(stagingDirectory, recursive: true); } catch (IOException) { }
    }

    /// <summary>The path and SHA-256 of every file of the client (everything but <c>game</c>), written after an install so a repair can tell what is damaged.</summary>
    public static void WriteFileList(InstallLayout layout)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in ClientFiles(layout))
            files[Relative(layout, path)] = Hashing.Sha256FileAsync(path).GetAwaiter().GetResult();
        File.WriteAllText(layout.FileListPath, JsonSerializer.Serialize(files, LauncherJson.Options));
    }

    /// <summary>The files that differ from the list written at install time: missing or changed. Empty when the client is whole; null when there is no list.</summary>
    public static async Task<IReadOnlyList<string>?> FindDamageAsync(InstallLayout layout, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (!File.Exists(layout.FileListPath)) return null;
        Dictionary<string, string>? expected;
        try { expected = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(layout.FileListPath, cancellation).ConfigureAwait(false), LauncherJson.Options); }
        catch (JsonException) { return null; }
        if (expected is null) return null;
        var damaged = new List<string>();
        var done = 0;
        foreach (var (relative, hash) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(layout.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || await Hashing.Sha256FileAsync(path, cancellation).ConfigureAwait(false) != hash) damaged.Add(relative);
            progress?.Report((double)++done / expected.Count);
        }
        return damaged;
    }

    private static IEnumerable<string> ClientFiles(InstallLayout layout)
    {
        foreach (var path in Directory.EnumerateFiles(layout.Root, "*", SearchOption.AllDirectories))
        {
            var relative = Relative(layout, path);
            var top = relative.Split('/')[0];
            if (top.StartsWith(".", StringComparison.Ordinal) || InstallLayout.UserEntries.Contains(top) || relative is InstallLayout.ReleaseRecordName or InstallLayout.FileListName) continue;
            yield return path;
        }
    }

    private static string Relative(InstallLayout layout, string path) => Path.GetRelativePath(layout.Root, path).Replace('\\', '/');

    /// <summary>If every entry lies under one top-level folder that is not one of the package's own, that folder is the package root.</summary>
    private static string CommonRoot(ZipArchive archive)
    {
        var firsts = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/').Split('/')[0]).Distinct(StringComparer.Ordinal).ToArray();
        if (firsts.Length != 1) return "";
        var only = firsts[0];
        if (only is "bin" or "patcher" || archive.Entries.Any(entry => entry.FullName == only)) return "";
        return only + Path.DirectorySeparatorChar;
    }
}
