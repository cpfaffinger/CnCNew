using System.Text.Json;

namespace CnCNew.Launcher;

public enum InstallState
{
    /// <summary>No client of ours in the folder.</summary>
    NotInstalled,
    /// <summary>The client is there but the game content has not been made (or was removed).</summary>
    ClientOnly,
    /// <summary>The client and the content are there.</summary>
    Ready,
}

/// <summary>What the launcher writes next to the client package it unpacked: which release it is, so a later start knows what is installed.</summary>
public sealed record ReleaseRecord(string Version, string Tag, DateTimeOffset InstalledAt);

/// <summary>
/// The install folder: the unpacked client package (<c>bin</c>, <c>patcher</c>, the two scripts, the licences) with the game content the patcher made beside it
/// in <c>game</c>. The names are those of <c>scripts/package-client.ps1</c>. The content is the player's own, made from their Steam copy; it is never part of a download.
/// </summary>
public sealed class InstallLayout(string root)
{
    public const string ReleaseRecordName = "cncnew-release.json";
    public const string FileListName = "cncnew-files.json";

    public string Root { get; } = Path.GetFullPath(root);
    public string ClientExe => Path.Combine(Root, "bin", "CnCNew.Client.Stride.exe");
    public string PatcherExe => Path.Combine(Root, "patcher", "CnCNew.AssetTool.exe");
    public string StartScript => Path.Combine(Root, "Start-CnCNew.cmd");
    public string GameDirectory => Path.Combine(Root, "game");
    public string ContentDirectory => Path.Combine(GameDirectory, "content");
    public string ContentManifest => Path.Combine(ContentDirectory, "content-manifest.json");
    public string InstallRecord => Path.Combine(GameDirectory, "install.json");
    public string ReleaseRecordPath => Path.Combine(Root, ReleaseRecordName);
    public string FileListPath => Path.Combine(Root, FileListName);

    /// <summary>The optional reference check: present only when a package ships the simulation tool with the reference values.</summary>
    public string SimulationCheckExe => Path.Combine(Root, "simcheck", "CnCNew.SimTool.exe");
    public string SimulationCheckBaselines => Path.Combine(Root, "simcheck", "baselines");
    public bool HasSimulationCheck => File.Exists(SimulationCheckExe) && Directory.Exists(SimulationCheckBaselines);

    /// <summary>The top-level entries of an install folder that belong to the user and are never replaced by an update.</summary>
    public static readonly IReadOnlySet<string> UserEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "game" };

    public InstallState Inspect()
    {
        if (!File.Exists(ClientExe) || !File.Exists(PatcherExe) || !File.Exists(ReleaseRecordPath)) return InstallState.NotInstalled;
        return File.Exists(ContentManifest) && File.Exists(InstallRecord) ? InstallState.Ready : InstallState.ClientOnly;
    }

    public ReleaseRecord? ReadRelease()
    {
        try { return File.Exists(ReleaseRecordPath) ? JsonSerializer.Deserialize<ReleaseRecord>(File.ReadAllText(ReleaseRecordPath), LauncherJson.Options) : null; }
        catch (Exception exception) when (exception is JsonException or IOException) { return null; }
    }

    public SemanticVersion? InstalledVersion => SemanticVersion.TryParse(ReadRelease()?.Version);

    public void WriteRelease(ReleaseRecord record)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(ReleaseRecordPath, JsonSerializer.Serialize(record, LauncherJson.Options));
    }
}

internal static class LauncherJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
}
