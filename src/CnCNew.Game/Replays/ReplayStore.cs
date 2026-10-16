using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Game.Replays;

/// <summary>One replay file of a store: its name (the file name without the extension), its size, its header or the reason it cannot be read, and whether this game plays it.</summary>
public sealed record ReplayEntry(string Name, long Bytes, MatchRecordingInfo? Info, ReplayCompatibility Compatibility);

/// <summary>
/// The replay files in one directory (<c>&lt;name&gt;.cncreplay</c>), beside the savegames: what the client writes after every skirmish and what the replay list shows.
/// Writing is atomic (temporary file, then move); listing never throws for a bad file, it reports its state; renaming and deleting refuse what does not fit with a message
/// fit to show. The store knows a file only by its header.
/// </summary>
public sealed class ReplayStore
{
    private readonly string _directory;

    public ReplayStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    public string Directory => _directory;

    /// <summary>A name a person may give a replay: not empty, at most 64 characters, no character a file name cannot hold, no leading or trailing blank, not a dot name.</summary>
    public static string? ProblemOfName(string name)
    {
        if (name.Length == 0 || name.Trim().Length == 0) return "A replay needs a name.";
        if (name.Length > 64) return "A replay name has at most 64 characters.";
        if (name != name.Trim()) return "A replay name does not begin or end with a blank.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0) return "A replay name cannot hold / \\ : * ? \" < > |.";
        if (name.StartsWith('.') || name.EndsWith('.')) return "A replay name does not begin or end with a dot.";
        return null;
    }

    public string PathOf(string name)
    {
        if (ProblemOfName(name) is { } problem) throw new ArgumentException(problem, nameof(name));
        return Path.Combine(_directory, name + MatchRecordingFormat.Extension);
    }

    public bool Exists(string name) => ProblemOfName(name) is null && File.Exists(PathOf(name));

    public void Save(string name, ReadOnlySpan<byte> bytes)
    {
        var path = PathOf(name);
        System.IO.Directory.CreateDirectory(_directory);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes.ToArray());
        File.Move(temporary, path, overwrite: true);
    }

    public byte[] Read(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) throw new ReplayRefusedException($"There is no replay '{name}'.");
        return File.ReadAllBytes(path);
    }

    public bool Delete(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Gives a replay another name; refuses a name that is not one (<see cref="ProblemOfName"/>) or is taken by another file, with a message fit to show.</summary>
    public void Rename(string name, string newName)
    {
        if (ProblemOfName(newName) is { } problem) throw new ReplayRefusedException(problem);
        var from = PathOf(name);
        var to = PathOf(newName);
        if (!File.Exists(from)) throw new ReplayRefusedException($"There is no replay '{name}'.");
        if (string.Equals(name, newName, StringComparison.Ordinal)) return;
        // A name that differs only in case is the same file on Windows: renaming to it is renaming, not a collision.
        if (File.Exists(to) && !string.Equals(name, newName, StringComparison.OrdinalIgnoreCase)) throw new ReplayRefusedException($"There is already a replay '{newName}'.");
        File.Move(from, to, overwrite: string.Equals(name, newName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The name of a new recording: the date and time it was recorded and the map (<c>2026-10-16 1423 Alpine Assault</c>), with <c>(2)</c>, <c>(3)</c> ... when that is taken.
    /// </summary>
    public string NewName(DateTimeOffset recorded, string mapPath)
    {
        var map = Path.GetFileNameWithoutExtension(mapPath.Replace('\\', '/'));
        var clean = new string([.. map.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)]).Trim();
        var stem = string.Create(CultureInfo.InvariantCulture, $"{recorded.UtcDateTime:yyyy-MM-dd HHmm} {clean}").Trim();
        if (stem.Length > 56) stem = stem[..56].TrimEnd();
        var name = stem;
        for (var number = 2; Exists(name); number++) name = string.Create(CultureInfo.InvariantCulture, $"{stem} ({number})");
        return name;
    }

    /// <summary>
    /// Every replay of the directory: the newest recording first (by the date in its header, then by name), files that cannot be read last by name. Every file is judged against
    /// <paramref name="machine"/>.
    /// </summary>
    public ImmutableArray<ReplayEntry> List(ReplayMachine machine)
    {
        if (!System.IO.Directory.Exists(_directory)) return [];
        var entries = new List<ReplayEntry>();
        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*" + MatchRecordingFormat.Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (ProblemOfName(name) is not null) continue;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (IOException exception) { entries.Add(new ReplayEntry(name, 0, null, new ReplayCompatibility(ReplayCompatibilityKind.Damaged, "Damaged", $"The file cannot be read: {exception.Message}"))); continue; }
            try
            {
                var info = MatchRecordingFormat.Read(bytes).Info;
                entries.Add(new ReplayEntry(name, bytes.Length, info, ReplayCompatibility.Judge(info, machine)));
            }
            catch (ReplayRefusedException refusal)
            {
                entries.Add(new ReplayEntry(name, bytes.Length, null, ReplayCompatibility.Unreadable(refusal)));
            }
        }
        return [.. entries
            .OrderBy(entry => entry.Info is null ? 1 : 0)
            .ThenByDescending(entry => entry.Info?.Recorded ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)];
    }
}
