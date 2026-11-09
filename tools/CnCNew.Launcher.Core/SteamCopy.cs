using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace CnCNew.Launcher;

/// <summary>The Steam copy of Command &amp; Conquer Generals - Zero Hour on this machine: where it is and what Steam says about its language. Found by reading, never by starting Steam.</summary>
public sealed record SteamCopy(string? Directory, IReadOnlyList<string> Libraries, string? Language)
{
    public const long AppId = 2732960;
    public const string DefaultDirectoryName = "Command & Conquer Generals - Zero Hour";

    public bool Found => Directory is not null;
    public bool IsEnglishOrUnknown => Language is null || Language.Equals("english", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Looks in the places Steam keeps its libraries: the registry, <c>libraryfolders.vdf</c>, the app manifest. The patcher does the same and then checks every file; this is for the launcher's first page.</summary>
public static partial class SteamLocation
{
    public const string DefaultLibrary = @"C:\Program Files (x86)\Steam";

    public static SteamCopy Find(IEnumerable<string>? steamRoots = null, string? defaultLibrary = DefaultLibrary)
    {
        var libraries = new List<string>();
        foreach (var root in (steamRoots ?? RegistryRoots()).Concat(defaultLibrary is null ? [] : [defaultLibrary]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!System.IO.Directory.Exists(root)) continue;
            libraries.Add(root);
            var folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(folders)) continue;
            foreach (var line in File.ReadLines(folders))
                if (LibraryPath().Match(line) is { Success: true } match) libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
        }
        libraries = libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var library in libraries)
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamCopy.AppId}.acf");
            if (!File.Exists(manifest)) continue;
            string? directory = null;
            string? language = null;
            foreach (var line in File.ReadLines(manifest))
            {
                if (InstallDir().Match(line) is { Success: true } dir) directory = Path.Combine(library, "steamapps", "common", dir.Groups[1].Value);
                else if (Language().Match(line) is { Success: true } lang) language = lang.Groups[1].Value;
            }
            if (directory is not null && System.IO.Directory.Exists(directory)) return new SteamCopy(directory, libraries, language);
        }
        var standard = defaultLibrary is null ? null : Path.Combine(defaultLibrary, "steamapps", "common", SteamCopy.DefaultDirectoryName);
        return new SteamCopy(standard is not null && System.IO.Directory.Exists(standard) ? standard : null, libraries, null);
    }

    private static IEnumerable<string> RegistryRoots()
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
                     (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            string? value = null;
            try { using var opened = hive.OpenSubKey(key); value = opened?.GetValue(name) as string; }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            if (!string.IsNullOrWhiteSpace(value)) yield return value.Replace('/', '\\');
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPath();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDir();

    [GeneratedRegex("\"language\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex Language();
}
