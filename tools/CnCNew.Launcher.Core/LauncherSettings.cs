using System.Text.Json;

namespace CnCNew.Launcher;

/// <summary>What the launcher remembers between starts (<c>%LOCALAPPDATA%\CnCNew\launcher.json</c>). A missing or damaged file is the defaults, never an error.</summary>
public sealed record LauncherSettings
{
    /// <summary>The install folder; empty until the user has chosen one (the default is <see cref="DefaultInstallFolder"/>).</summary>
    public string InstallFolder { get; init; } = "";

    /// <summary>Whether, after the game is made, the simulation is checked against the reference values. Off unless the user asks for it.</summary>
    public bool VerifySimulation { get; init; }

    /// <summary>A release the user chose to skip; the banner does not offer it again.</summary>
    public string SkippedVersion { get; init; } = "";

    public bool CheckForUpdates { get; init; } = true;

    public static string DefaultInstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CnCNew");

    public static string DefaultDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CnCNew");

    public string EffectiveInstallFolder => InstallFolder.Length > 0 ? InstallFolder : DefaultInstallFolder;

    public static LauncherSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), LauncherJson.Options) ?? new LauncherSettings() : new LauncherSettings();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    /// <summary>Writes the file through a temporary one, so a crash never leaves half a settings file. Returns false when it cannot be written.</summary>
    public bool Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, LauncherJson.Options));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
