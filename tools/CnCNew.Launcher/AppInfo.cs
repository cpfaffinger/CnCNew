using System.Reflection;

namespace CnCNew.Launcher.Shell;

/// <summary>Facts about this running launcher: its version (from version.txt at build time) and where it keeps its files.</summary>
public static class AppInfo
{
    public static string DataDirectory { get; set; } = LauncherSettings.DefaultDataDirectory;

    public static string SettingsPath => Path.Combine(DataDirectory, "launcher.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static SemanticVersion Version { get; } =
        SemanticVersion.TryParse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new SemanticVersion(0, 0, 0);
}

/// <summary>A plain text log of what the launcher did, in the user's data folder. Never anything but file names, versions and messages; nothing is sent anywhere.</summary>
public static class LauncherLog
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(AppInfo.LogDirectory, "launcher.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppInfo.LogDirectory);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 1_000_000) File.Move(Path, Path + ".old", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
