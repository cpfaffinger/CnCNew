using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CnCNew.Import;

/// <summary>
/// Finds a Steam game's installation directory the way <c>scripts/install.ps1</c> does: the default library first, then every library Steam knows
/// (<c>libraryfolders.vdf</c> of the Steam installations named in the registry), where the app's manifest (<c>appmanifest_&lt;app&gt;.acf</c>) names the directory.
/// It reads only; it never starts Steam.
/// </summary>
public static partial class SteamLocator
{
    public const string DefaultLibrary = @"C:\Program Files (x86)\Steam";

    /// <summary>The installation directory of <paramref name="appId"/>, or null with the places searched in <paramref name="searched"/>.</summary>
    public static string? FindGame(long appId, string defaultDirectoryName, out IReadOnlyList<string> searched)
    {
        var places = new List<string>();
        searched = places;
        var standard = Path.Combine(DefaultLibrary, "steamapps", "common", defaultDirectoryName);
        places.Add(standard);
        if (Directory.Exists(standard)) return standard;
        var libraries = new List<string>();
        foreach (var root in SteamRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libraries.Add(root);
            var folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(folders)) continue;
            foreach (var line in File.ReadLines(folders))
                if (PathLine().Match(line) is { Success: true } match) libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
        }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            places.Add(manifest);
            if (!File.Exists(manifest)) continue;
            foreach (var line in File.ReadLines(manifest))
            {
                if (InstallDirLine().Match(line) is not { Success: true } match) continue;
                var candidate = Path.Combine(library, "steamapps", "common", match.Groups[1].Value);
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var root in WindowsRoots()) yield return root;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> WindowsRoots()
    {
        foreach (var (hive, key, name) in new[]
                 {
                     (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                 })
        {
            using var opened = hive.OpenSubKey(key);
            if (opened?.GetValue(name) is string value && value.Length > 0) yield return value.Replace('/', '\\');
        }
    }

    [GeneratedRegex("^\\s*\"path\"\\s+\"(.+)\"\\s*$")]
    private static partial Regex PathLine();

    [GeneratedRegex("^\\s*\"installdir\"\\s+\"(.+)\"\\s*$")]
    private static partial Regex InstallDirLine();
}
