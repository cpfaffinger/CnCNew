using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The skin a suite's screen walk draws in, and the file its hashes are pinned in (docs/UI-MODERN.md section 9, R2): every walk that pins pictures of the original skin
/// walks a second time in the modern skin, against a file of its own beside the original's (<c>NAME-modern.txt</c>). The original's files are never written by the
/// modern walk.
/// </summary>
internal static class SkinPins
{
    /// <summary>The skin of the walk under way; the original unless <see cref="With"/> sets another.</summary>
    public static UiSkin Current { get; private set; } = UiSkin.Original;

    /// <summary>The settings a test's HUD is built with: the defaults, in the skin of the walk.</summary>
    public static GameSettings Settings => GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = Current.Name } };

    /// <summary>The file of the modern skin's hashes beside the original's: <c>screens.txt</c> gives <c>screens-modern.txt</c>.</summary>
    public static string Modern(string path) => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-modern" + Path.GetExtension(path)).Replace('\\', '/');

    /// <summary>Runs <paramref name="walk"/> in <paramref name="skin"/> and goes back to the original afterwards, also when it throws.</summary>
    public static void With(UiSkin skin, Action walk)
    {
        var before = Current;
        Current = skin;
        try { walk(); }
        finally { Current = before; }
    }

    public static Dictionary<string, string> Load(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var equals = line.IndexOf('=');
            if (equals > 0) result[line[..equals]] = line[(equals + 1)..];
        }
        return result;
    }

    /// <summary>
    /// Writes the hashes sorted by name, one <c>NAME=HASH</c> line each, with "\n" line ends on every system. <paramref name="merge"/>: the file's other hashes stay (a
    /// suite that pins per rate records only the rate it runs at).
    /// </summary>
    public static void Save(string path, Dictionary<string, string> hashes, bool merge = false)
    {
        if (merge)
        {
            var merged = Load(path);
            foreach (var (key, value) in hashes) merged[key] = value;
            hashes = merged;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}\n")));
        Console.WriteLine($"Wrote {hashes.Count} baseline hash(es) to {path}.");
    }
}
