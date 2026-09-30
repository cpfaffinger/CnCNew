using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;

namespace CnCNew.Game.Shell;

/// <param name="VirtualPath">Decoded virtual path (<c>Maps/Alpine Assault/Alpine Assault.map</c>), matching <see cref="Data.GameData.Maps"/>.</param>
/// <param name="NameLookupTag">The CSF label of the map's display name (<c>MAP:AlpineAssault</c>), or null when the entry has none.</param>
/// <param name="IsMultiplayer">Whether the map is flagged for skirmish/multiplayer.</param>
/// <param name="StartPositionCount">Number of <c>Player_N_Start</c> entries (playable slots).</param>
public sealed record ShellMapEntry(string VirtualPath, string? NameLookupTag, bool IsMultiplayer, int StartPositionCount);

/// <summary>
/// A minimal reader of <c>Maps/MapCache.ini</c> (<c>WorldBuilder</c>'s auto-generated map cache), used only to pick
/// display names and multiplayer/start-position metadata for the skirmish map list. This is deliberately not a general
/// INI parser: the shell owns exactly the handful of keys it needs and the one path-escaping rule the file uses
/// (<c>_XX</c> hex-escapes non-alphanumeric bytes of the lowercased virtual path, e.g. <c>_5C</c> = backslash, <c>_20</c> =
/// space, <c>_2E</c> = dot), so a block name can be matched back to a real map path.
/// </summary>
public static class ShellMapCatalog
{
    public const string VirtualPath = "Maps/MapCache.ini";

    public static ImmutableDictionary<string, ShellMapEntry> Load(ContentFileSystem content, IEnumerable<string> knownMapPaths)
    {
        var byEscapedPath = knownMapPaths.ToDictionary(Escape, path => path, StringComparer.Ordinal);
        var result = ImmutableDictionary.CreateBuilder<string, ShellMapEntry>(StringComparer.Ordinal);
        if (!content.Exists(VirtualPath)) return result.ToImmutable();

        string text;
        using (var stream = content.OpenRead(VirtualPath))
        using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();

        string? blockKey = null;
        string? nameLookupTag = null;
        bool isMultiplayer = false;
        var startPositions = 0;

        void Flush()
        {
            if (blockKey is null) return;
            if (byEscapedPath.TryGetValue(blockKey, out var realPath))
                result[realPath] = new ShellMapEntry(realPath, string.IsNullOrEmpty(nameLookupTag) ? null : nameLookupTag, isMultiplayer, startPositions);
            blockKey = null;
            nameLookupTag = null;
            isMultiplayer = false;
            startPositions = 0;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith("MapCache ", StringComparison.Ordinal))
            {
                Flush();
                blockKey = line["MapCache ".Length..].Trim();
                continue;
            }
            if (line.Equals("END", StringComparison.OrdinalIgnoreCase)) { Flush(); continue; }
            if (blockKey is null) continue;
            var equals = line.IndexOf('=');
            if (equals < 0) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("nameLookupTag", StringComparison.Ordinal)) nameLookupTag = value;
            else if (key.Equals("isMultiplayer", StringComparison.Ordinal)) isMultiplayer = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            else if (key.StartsWith("Player_", StringComparison.Ordinal) && key.EndsWith("_Start", StringComparison.Ordinal)) startPositions++;
        }
        Flush();
        return result.ToImmutable();
    }

    /// <summary>
    /// Encodes a virtual map path the way <c>WorldBuilder</c> names its cache blocks: the whole path (backslash
    /// separators, lowercase) with every non-alphanumeric byte replaced by <c>_XX</c> (uppercase hex), e.g.
    /// <c>Maps/Alpine Assault/Alpine Assault.map</c> becomes <c>maps_5Calpine_20assault_5Calpine_20assault_2Emap</c>.
    /// </summary>
    private static string Escape(string virtualPath)
    {
        var lower = virtualPath.Replace('/', '\\').ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
            else builder.Append('_').Append(((byte)c).ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
