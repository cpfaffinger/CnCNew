using System.Security.Cryptography;
using System.Text.Json;
using CnCNew.Content;

namespace CnCNew.Import;

/// <summary>
/// <c>install.json</c> at the root of an installation of ours: what it was made from and whether its content is the known export.
/// Canonical JSON without timestamp or absolute path. A directory without a valid record is never treated as ours.
/// </summary>
public sealed record InstallRecord(
    string Product,
    string ProductVersion,
    string ProductCommit,
    string MasterId,
    string MasterName,
    long SteamAppId,
    string ImporterVersion,
    string ContentManifestSha256,
    long ContentManifestBytes,
    int RuntimeFileCount,
    bool Verified,
    string PatchSource = "")
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "install.json";

    public byte[] Write()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalJson.WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("product", Product);
            writer.WriteString("productVersion", ProductVersion);
            writer.WriteString("productCommit", ProductCommit);
            writer.WriteString("masterId", MasterId);
            writer.WriteString("masterName", MasterName);
            writer.WriteNumber("steamAppId", SteamAppId);
            writer.WriteString("importerVersion", ImporterVersion);
            writer.WriteString("contentManifestSha256", ContentManifestSha256);
            writer.WriteNumber("contentManifestBytes", ContentManifestBytes);
            writer.WriteNumber("runtimeFileCount", RuntimeFileCount);
            writer.WriteBoolean("verified", Verified);
            writer.WriteString("patchSource", PatchSource);
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    /// <summary>The record of <paramref name="directory"/>, or null when the directory carries none that is ours (missing, unreadable, another product).</summary>
    public static InstallRecord? TryRead(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            // Schema 1 had no patch: such a directory is ours, and the next installation brings it up to the patched content.
            var schema = root.TryGetProperty("schemaVersion", out var version) ? version.GetInt32() : CurrentSchemaVersion;
            string[] common = ["schemaVersion", "product", "productVersion", "productCommit", "masterId", "masterName", "steamAppId", "importerVersion",
                "contentManifestSha256", "contentManifestBytes", "runtimeFileCount", "verified"];
            CanonicalJson.RequireProperties(root, "install record", schema == 1 ? common : [.. common, "patchSource"]);
            if (schema is not (1 or CurrentSchemaVersion) || root.GetProperty("product").GetString() != ProductInfo.Product)
                return null;
            return new InstallRecord(
                ProductInfo.Product,
                root.GetProperty("productVersion").GetString()!,
                root.GetProperty("productCommit").GetString()!,
                root.GetProperty("masterId").GetString()!,
                root.GetProperty("masterName").GetString()!,
                root.GetProperty("steamAppId").GetInt64(),
                root.GetProperty("importerVersion").GetString()!,
                root.GetProperty("contentManifestSha256").GetString()!,
                root.GetProperty("contentManifestBytes").GetInt64(),
                root.GetProperty("runtimeFileCount").GetInt32(),
                root.GetProperty("verified").GetBoolean(),
                schema == 1 ? "" : root.GetProperty("patchSource").GetString()!);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

public enum InstallOutcome
{
    /// <summary>The content was exported into an empty or new directory.</summary>
    Installed,
    /// <summary>An existing export was damaged or outdated and has been restored from the game directory.</summary>
    Repaired,
    /// <summary>An existing export was checked file by file and is the known one; nothing was written to it.</summary>
    AlreadyComplete,
    /// <summary>Refused: the game directory is not the master. Nothing was written.</summary>
    InstallationDiffers,
    /// <summary>Refused: the target is inside the game directory, contains it, or holds something that is not ours. Nothing was written.</summary>
    TargetRefused,
}

public sealed record InstallProgress(string Step, int Processed, int Total, string Path);

public sealed record InstallResult(
    InstallOutcome Outcome,
    string Message,
    InstallationCheckResult? Check,
    IReadOnlyList<string> MissingRuntimeFiles,
    IReadOnlyList<string> ChangedRuntimeFiles,
    IReadOnlyList<string> ExtraRuntimeFiles,
    int ReusedFiles,
    int CopiedFiles,
    InstallRecord? Record)
{
    public bool Succeeded => Outcome is InstallOutcome.Installed or InstallOutcome.Repaired or InstallOutcome.AlreadyComplete;
}

public sealed record UninstallResult(bool Removed, string Message, IReadOnlyList<string> Leftovers);

/// <summary>The patch of an installation: its description, and optionally a checkout to take its files from and another way of fetching them.</summary>
public sealed record PatchInstall(PatchMaster Master, string? Checkout = null, Func<Uri, CancellationToken, Task<byte[]>>? Download = null);

/// <summary>
/// Creates a game directory of ours from the user's own installation. The game directory is only read; everything is written below the target.
/// </summary>
public static class GameInstaller
{
    public const string ContentDirectoryName = "content";
    public const string ManifestFileName = "content-manifest.json";

    /// <summary>Where the patch's files lie while an installation is made; it is gone when the installation is verified.</summary>
    public const string PatchDirectoryName = "patch-files";

    /// <summary>The entries an installation of ours consists of; <see cref="Uninstall"/> removes exactly these.</summary>
    public static readonly IReadOnlyList<string> OwnedEntries = [ContentDirectoryName, PatchDirectoryName, "bin", "Start-CnCNew.cmd", InstallRecord.FileName];

    /// <summary>
    /// Checks the game directory against <paramref name="master"/> and exports its runtime content into <c>&lt;target&gt;/content</c>, then writes
    /// <c>&lt;target&gt;/install.json</c>. On an existing installation of ours it verifies every runtime file and restores what is missing or changed.
    /// With <paramref name="patch"/> the layer lies above the game and the export is the one recorded with the patch (docs/PATCH-LAYER.md).
    /// </summary>
    public static async Task<InstallResult> InstallAsync(
        string gameDirectory,
        string targetDirectory,
        InstallationMaster master,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default,
        PatchInstall? patch = null)
    {
        ArgumentNullException.ThrowIfNull(master);
        var game = Path.GetFullPath(gameDirectory);
        var target = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(game)) throw new DirectoryNotFoundException($"The game directory '{gameDirectory}' does not exist.");
        if (Overlap(game, target) is { } overlap) return Refused(InstallOutcome.TargetRefused, overlap);
        if (File.Exists(target)) return Refused(InstallOutcome.TargetRefused, $"The target '{target}' is a file.");
        var previous = Directory.Exists(target) ? InstallRecord.TryRead(target) : null;
        if (previous is null && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            return Refused(InstallOutcome.TargetRefused,
                $"The target '{target}' is not empty and carries no '{InstallRecord.FileName}' of {ProductInfo.Product}: it is not a previous installation. Choose an empty or new directory.");

        var check = await CheckAsync(game, master, progress, cancellationToken);
        if (!check.IsIdentical) return Refused(InstallOutcome.InstallationDiffers, check.FormatReport(), check);
        var expected = patch?.Master.ExpectedExport ?? master.ExpectedExport;
        RequireCurrentImporter(expected);

        // The unverified record marks the directory as ours before anything else is written, so an interrupted installation can be resumed.
        Directory.CreateDirectory(target);
        var (version, commit) = ProductInfo.Read();
        var record = new InstallRecord(ProductInfo.Product, version, commit, master.Id, master.Name, master.SteamAppId,
            expected.ImporterVersion, expected.ContentManifestSha256, expected.ContentManifestBytes,
            expected.RuntimeFileCount, Verified: false, patch?.Master.Source ?? "");
        await File.WriteAllBytesAsync(Path.Combine(target, InstallRecord.FileName), record.Write(), cancellationToken);

        // Fetched only when something is to be exported: an intact installation is verified without the network.
        var patchFiles = Path.Combine(target, PatchDirectoryName);
        var fetchProgress = progress is null ? null : new Progress(progress, "patch", every: 16);
        async Task<ContentLayer?> LayerAsync() => patch is null ? null
            : await new PatchFetcher(patch.Master, patch.Download).FetchAsync(patchFiles, patch.Checkout, fetchProgress, cancellationToken);
        var result = await ExportCoreAsync(game, Path.Combine(target, ContentDirectoryName), expected, check, LayerAsync, progress, cancellationToken);
        if (Directory.Exists(patchFiles)) Directory.Delete(patchFiles, recursive: true);
        record = record with { Verified = true };
        await File.WriteAllBytesAsync(Path.Combine(target, InstallRecord.FileName), record.Write(), cancellationToken);
        return result with { Record = record };
    }

    /// <summary>
    /// Compares a content directory with the export recorded in <paramref name="master"/>: manifest bytes, hash and file count, then every runtime file.
    /// Returns the list of problems; empty means the directory is exactly the known export.
    /// </summary>
    public static Task<IReadOnlyList<string>> VerifyExportAsync(
        string contentDirectory,
        InstallationMaster master,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => VerifyExportAsync(contentDirectory, master.ExpectedExport, progress, cancellationToken);

    /// <inheritdoc cref="VerifyExportAsync(string, InstallationMaster, IProgress{InstallProgress}?, CancellationToken)"/>
    public static async Task<IReadOnlyList<string>> VerifyExportAsync(
        string contentDirectory,
        MasterExport expected,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var content = Path.GetFullPath(contentDirectory);
        var problems = new List<string>();
        var manifestPath = Path.Combine(content, ManifestFileName);
        if (!File.Exists(manifestPath)) return [$"'{ManifestFileName}' is missing."];
        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != expected.ContentManifestSha256 || bytes.LongLength != expected.ContentManifestBytes)
            problems.Add($"'{ManifestFileName}' has {bytes.LongLength} bytes and SHA-256 {hash}; the known export has {expected.ContentManifestBytes} bytes and " +
                         $"{expected.ContentManifestSha256}.");
        var manifest = ReadPublishedManifest(content);
        if (manifest is null) return [.. problems, $"'{ManifestFileName}' is not a runtime manifest of {RuntimeContentImporter.ImporterName}."];
        if (manifest.Assets.Count != expected.RuntimeFileCount)
            problems.Add($"The manifest lists {manifest.Assets.Count} runtime files; the known export has {expected.RuntimeFileCount}.");
        var damage = await ScanContentAsync(content, manifest, progress, cancellationToken);
        problems.AddRange(damage.Missing.Select(path => $"missing runtime file: {path}"));
        problems.AddRange(damage.Changed.Select(path => $"changed runtime file: {path}"));
        problems.AddRange(damage.Extra.Select(path => $"file that does not belong to the export: {path}"));
        return problems;
    }

    /// <summary>
    /// Removes an installation of ours: only a directory that carries our install record, and of it only <see cref="OwnedEntries"/>.
    /// Anything else in the directory stays and is reported; the directory itself goes only when it is empty afterwards.
    /// </summary>
    public static UninstallResult Uninstall(string targetDirectory)
    {
        var target = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(target)) return new UninstallResult(false, $"'{target}' does not exist.", []);
        if (InstallRecord.TryRead(target) is null)
            return new UninstallResult(false, $"'{target}' carries no '{InstallRecord.FileName}' of {ProductInfo.Product}: it is not an installation of ours and is not touched.", []);
        if (File.Exists(Path.Combine(target, InstallationInspector.SteamAppIdFile)))
            return new UninstallResult(false, $"'{target}' contains '{InstallationInspector.SteamAppIdFile}': it looks like a Steam game directory and is not touched.", []);

        foreach (var entry in OwnedEntries)
        {
            var path = Path.Combine(target, entry);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        var leftovers = Directory.EnumerateFileSystemEntries(target).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();
        if (leftovers.Length == 0)
        {
            Directory.Delete(target);
            return new UninstallResult(true, $"Removed the installation '{target}'.", leftovers);
        }
        return new UninstallResult(true, $"Removed the installation from '{target}'; {leftovers.Length} entr(ies) that are not part of it were kept.", leftovers);
    }

    private static async Task<InstallResult> ExportCoreAsync(
        string game,
        string content,
        MasterExport expected,
        InstallationCheckResult check,
        Func<Task<ContentLayer?>> layer,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existed = Directory.Exists(content) && Directory.EnumerateFileSystemEntries(content).Any();
        ContentDamage damage = new([], [], []);
        Predicate<RuntimeArtifact>? trust = null;
        if (existed)
        {
            var manifestPath = Path.Combine(content, ManifestFileName);
            var published = ReadPublishedManifest(content);
            var isKnownManifest = published is not null && File.Exists(manifestPath) &&
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, cancellationToken))).ToLowerInvariant() == expected.ContentManifestSha256;
            if (isKnownManifest)
            {
                damage = await ScanContentAsync(content, published!, progress, cancellationToken);
                if (damage.IsIntact)
                    return new InstallResult(InstallOutcome.AlreadyComplete,
                        $"The content is already the known export ({expected.RuntimeFileCount} runtime files verified against size and SHA-256); nothing was written to it.",
                        check, [], [], [], 0, 0, null);
                var changed = new HashSet<string>(damage.Changed, StringComparer.OrdinalIgnoreCase);
                trust = artifact => !changed.Contains(artifact.Path);
            }
            else
            {
                // A manifest that is not the known one says nothing reliable about the files beside it: every file comes from the game directory again.
                trust = _ => false;
            }
        }

        var importProgress = progress is null ? null : new Progress(progress, "export");
        var import = await RuntimeContentImporter.ImportAsync(game, content, importProgress, cancellationToken, trust, await layer());
        var problems = await VerifyExportAsync(content, expected, progress, cancellationToken);
        if (problems.Count != 0)
            throw new InvalidDataException(
                "The exported content is not the known export although the game directory is the master: " + string.Join(" ", problems.Take(5)) +
                " The importer and the master description do not belong together.");

        var copied = import.WrittenFiles - import.CacheHits;
        return existed
            ? new InstallResult(InstallOutcome.Repaired,
                $"Restored the content from the game directory: {damage.Missing.Count} missing, {damage.Changed.Count} changed, {damage.Extra.Count} foreign file(s); " +
                $"{copied} file(s) copied, {import.CacheHits} kept. {expected.RuntimeFileCount} runtime files verified.",
                check, damage.Missing, damage.Changed, damage.Extra, import.CacheHits, copied, null)
            : new InstallResult(InstallOutcome.Installed,
                $"Exported {expected.RuntimeFileCount} runtime files; verified against size and SHA-256; the content manifest is the known one ({expected.ContentManifestSha256}).",
                check, [], [], [], import.CacheHits, copied, null);
    }

    private sealed record ContentDamage(IReadOnlyList<string> Missing, IReadOnlyList<string> Changed, IReadOnlyList<string> Extra)
    {
        public bool IsIntact => Missing.Count == 0 && Changed.Count == 0 && Extra.Count == 0;
    }

    private static async Task<ContentDamage> ScanContentAsync(string content, RuntimeContentManifest manifest, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        var changed = new List<string>();
        var artifacts = manifest.Assets.Select(asset => asset.Artifacts.Single()).OrderBy(artifact => artifact.Path, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < artifacts.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = artifacts[index];
            if (index % 256 == 0) progress?.Report(new InstallProgress("verify", index, artifacts.Length, artifact.Path));
            var path = InstallationInspector.Physical(content, artifact.Path);
            if (!File.Exists(path)) { missing.Add(artifact.Path); continue; }
            if (new FileInfo(path).Length != artifact.Bytes) { changed.Add(artifact.Path); continue; }
            var (_, hash) = await InstallationInspector.HashAsync(path, cancellationToken);
            if (!hash.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)) changed.Add(artifact.Path);
        }
        var known = new HashSet<string>(artifacts.Select(artifact => artifact.Path), StringComparer.OrdinalIgnoreCase) { ManifestFileName };
        var extra = InstallationInspector.ListFiles(content).Select(file => file.Path).Where(path => !known.Contains(path)).ToArray();
        return new ContentDamage(missing, changed, extra);
    }

    private static RuntimeContentManifest? ReadPublishedManifest(string content)
    {
        var path = Path.Combine(content, ManifestFileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var manifest = RuntimeManifestSerializer.Read(stream);
            return manifest.Importer.Name == RuntimeContentImporter.ImporterName && manifest.Assets.All(asset => asset.Artifacts.Count == 1) ? manifest : null;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static Task<InstallationCheckResult> CheckAsync(string game, InstallationMaster master, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var checkProgress = progress is null
            ? null
            : new SynchronousProgress<InstallationCheckProgress>(value => progress.Report(new InstallProgress("check", value.ProcessedFiles, value.TotalFiles, value.Path)));
        return InstallationInspector.CheckAsync(game, master, checkProgress, cancellationToken);
    }

    private static void RequireCurrentImporter(MasterExport expected)
    {
        if (expected.Importer != RuntimeContentImporter.ImporterName || expected.ImporterVersion != RuntimeContentImporter.ImporterVersion)
            throw new InvalidDataException(
                $"The master description records the export of {expected.Importer} {expected.ImporterVersion}, " +
                $"this build has {RuntimeContentImporter.ImporterName} {RuntimeContentImporter.ImporterVersion}: the master description must be written again (describe-install).");
    }

    private static string? Overlap(string game, string target)
    {
        if (IsSameOrInside(target, game)) return $"The target '{target}' is the game directory or lies inside it. The game directory is never written to; choose a directory elsewhere.";
        if (IsSameOrInside(game, target)) return $"The target '{target}' contains the game directory. Choose a directory elsewhere.";
        return null;
    }

    private static bool IsSameOrInside(string path, string directory)
    {
        var inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var outer = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return inner.StartsWith(outer, StringComparison.OrdinalIgnoreCase);
    }

    private static InstallResult Refused(InstallOutcome outcome, string message, InstallationCheckResult? check = null)
        => new(outcome, message, check, [], [], [], 0, 0, null);

    private sealed class Progress(IProgress<InstallProgress> inner, string step, int every = 256) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value)
        {
            if (value.Processed % every == 0) inner.Report(new InstallProgress(step, value.Processed, value.Total, value.VirtualPath));
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
