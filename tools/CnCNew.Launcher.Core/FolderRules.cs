namespace CnCNew.Launcher;

public sealed record FolderVerdict(bool Acceptable, string Message, string Folder)
{
    public static FolderVerdict Ok(string folder, string message = "") => new(true, message, folder);
    public static FolderVerdict No(string folder, string message) => new(false, message, folder);
}

/// <summary>
/// Which folder may hold the game. The rules protect the Steam installation (it is only ever read), the user's other files and the install itself:
/// the client writes its shader cache beside itself, so a folder that needs administrator rights does not work.
/// </summary>
public static class FolderRules
{
    public const int MaximumLength = 100;

    public static FolderVerdict Judge(string chosen, IReadOnlyList<string>? steamLibraries = null)
    {
        if (string.IsNullOrWhiteSpace(chosen)) return FolderVerdict.No("", "Choose a folder.");
        string folder;
        try { folder = Path.GetFullPath(chosen.Trim().Trim('"')); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return FolderVerdict.No(chosen, "That is not a folder name Windows accepts."); }
        folder = folder.TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(folder)?.TrimEnd(Path.DirectorySeparatorChar) ?? "";
        if (folder.Equals(root, StringComparison.OrdinalIgnoreCase) || folder.Length <= 3) return FolderVerdict.No(folder, "Choose a folder, not a whole drive. A folder of its own, such as C:\\Games\\CnCNew, keeps your files apart.");
        if (folder.Length > MaximumLength) return FolderVerdict.No(folder, $"The folder name is too long ({folder.Length} characters; at most {MaximumLength}). Windows limits paths, and the game's files lie deep inside.");
        if (File.Exists(folder)) return FolderVerdict.No(folder, "That name is a file, not a folder.");

        var segments = folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) || InsideAny(folder, steamLibraries))
            return FolderVerdict.No(folder, "That folder belongs to Steam. Steam's copy of the game is only ever read, never written. Choose a folder somewhere else.");
        foreach (var protectedRoot in ProtectedRoots())
            if (Inside(folder, protectedRoot))
                return FolderVerdict.No(folder, "Windows protects that folder; the game needs to write files next to itself. Choose a folder in your user folder or on another drive.");

        if (Directory.Exists(folder))
        {
            var layout = new InstallLayout(folder);
            var entries = Directory.EnumerateFileSystemEntries(folder).Any();
            if (entries && layout.ReadRelease() is null)
                return FolderVerdict.No(folder, "That folder already holds other files. Choose an empty folder, or a new one; the launcher will create it.");
        }
        return FolderVerdict.Ok(folder);
    }

    /// <summary>A free subfolder name inside <paramref name="parent"/> for when the chosen folder holds other things.</summary>
    public static string OwnSubfolder(string parent) => Path.Combine(parent, "CnCNew");

    private static IEnumerable<string> ProtectedRoots()
    {
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows })
        {
            var path = Environment.GetFolderPath(special);
            if (!string.IsNullOrEmpty(path)) yield return path;
        }
    }

    private static bool InsideAny(string folder, IReadOnlyList<string>? libraries) => libraries is not null && libraries.Any(library => Inside(folder, library));

    private static bool Inside(string folder, string parent)
    {
        var normal = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return folder.Equals(normal, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(normal + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
