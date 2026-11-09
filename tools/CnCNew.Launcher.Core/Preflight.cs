using System.Runtime.Versioning;

namespace CnCNew.Launcher;

public enum CheckStatus { Ok, Warning, Failed }

public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Detail);

/// <summary>What the preflight asks the machine. A seam so tests do not depend on the machine they run on.</summary>
public interface ISystemProbe
{
    bool Is64BitWindows { get; }
    int WindowsBuild { get; }
    long FreeBytes(string path);
    SteamCopy FindSteamCopy();
    IReadOnlyList<string> GraphicsAdapters();
}

public sealed class SystemProbe : ISystemProbe
{
    public bool Is64BitWindows => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;
    public int WindowsBuild => OperatingSystem.IsWindows() ? Environment.OSVersion.Version.Build : 0;

    public long FreeBytes(string path)
    {
        try
        {
            // The drive of the first existing parent: the folder itself may not exist yet.
            var probe = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe)) probe = Path.GetDirectoryName(probe) ?? "";
            return string.IsNullOrEmpty(probe) ? 0 : new DriveInfo(Path.GetPathRoot(probe)!).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException) { return 0; }
    }

    public SteamCopy FindSteamCopy() => SteamLocation.Find();

    public IReadOnlyList<string> GraphicsAdapters() => OperatingSystem.IsWindows() ? WindowsAdapters() : [];

    [SupportedOSPlatform("windows")]
    private static List<string> WindowsAdapters()
    {
        var names = new List<string>();
        try
        {
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return names;
            foreach (var sub in classKey.GetSubKeyNames())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var adapter = classKey.OpenSubKey(sub);
                if (adapter?.GetValue("DriverDesc") is string name && name.Length > 0) names.Add(name);
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return names;
    }
}

/// <summary>The checks of the first page. Each result says in a sentence what was found and, when it is not fine, what to do.</summary>
public static class Preflight
{
    /// <summary>The disk the whole install needs: the package (about 0.4 GB), the patch download (0.1 GB), the game content (about 3 GB) and room to work.</summary>
    public const long RequiredBytes = 8L * 1024 * 1024 * 1024;

    public static IReadOnlyList<CheckResult> Run(ISystemProbe probe, string installFolder)
    {
        var results = new List<CheckResult>();

        if (!probe.Is64BitWindows) results.Add(new("windows", "Windows", CheckStatus.Failed, "The game needs 64-bit Windows 10 or 11."));
        else if (probe.WindowsBuild < 17763) results.Add(new("windows", "Windows", CheckStatus.Failed, "This Windows is too old. The game needs Windows 10 (version 1809) or newer."));
        else results.Add(new("windows", "Windows", CheckStatus.Ok, $"64-bit Windows, build {probe.WindowsBuild}."));

        var free = probe.FreeBytes(installFolder);
        results.Add(free >= RequiredBytes
            ? new("disk", "Disk space", CheckStatus.Ok, $"{free / (1024.0 * 1024 * 1024):F0} GB free where the game goes.")
            : new("disk", "Disk space", CheckStatus.Failed, $"Only {free / (1024.0 * 1024 * 1024):F1} GB free where the game goes; about {RequiredBytes / (1024 * 1024 * 1024)} GB are needed. Free some space or choose another folder."));

        var adapters = probe.GraphicsAdapters();
        if (adapters.Count == 0) results.Add(new("graphics", "Graphics", CheckStatus.Warning, "Windows reports no graphics card. The game needs one that supports Direct3D 11."));
        else if (adapters.All(name => name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)))
            results.Add(new("graphics", "Graphics", CheckStatus.Warning, "Windows uses its basic display driver. Install the driver of your graphics card, or the game will be very slow."));
        else results.Add(new("graphics", "Graphics", CheckStatus.Ok, adapters[0]));

        var steam = probe.FindSteamCopy();
        if (!steam.Found)
            results.Add(new("steam", "Your copy of the game", CheckStatus.Failed, "Command & Conquer Generals - Zero Hour was not found in your Steam libraries. The game itself is not part of this download: install your own copy in Steam (English), let it finish, and press Check again."));
        else if (!steam.IsEnglishOrUnknown)
            results.Add(new("steam", "Your copy of the game", CheckStatus.Failed, $"Your Steam copy is set to {steam.Language}. Only the English version is supported: in Steam, open the game's Properties > Language, choose English, let Steam finish, and press Check again."));
        else results.Add(new("steam", "Your copy of the game", CheckStatus.Ok, $"Found in {steam.Directory}. Steam's files are only read, never changed."));

        var folder = FolderRules.Judge(installFolder, steam.Libraries);
        results.Add(folder.Acceptable
            ? new("folder", "Install folder", CheckStatus.Ok, folder.Folder)
            : new("folder", "Install folder", CheckStatus.Failed, folder.Message));
        return results;
    }

    public static bool CanInstall(IEnumerable<CheckResult> results) => results.All(result => result.Status != CheckStatus.Failed);
}
