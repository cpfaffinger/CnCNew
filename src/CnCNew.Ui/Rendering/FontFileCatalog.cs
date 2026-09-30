using System.Collections.Immutable;

namespace CnCNew.Ui.Rendering;

/// <summary>A font file of an installed family. <see cref="Italic"/> faces are never chosen (the UI has no italic).</summary>
public sealed record FontFileEntry(string Family, bool Bold, bool Italic, string Path);

/// <summary>
/// Maps a family name and weight to a font file. The entries come from the Windows font registry list
/// (<c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts</c>): value names such as <c>Arial Bold (TrueType)</c> or
/// <c>Microsoft YaHei &amp; Microsoft YaHei UI (TrueType)</c>; reading the registry is the host's job, this class only
/// interprets the names. Lookup is case-insensitive and deterministic (first entry in path order wins on duplicates).
/// </summary>
public sealed class FontFileCatalog
{
    private readonly ImmutableArray<FontFileEntry> _entries;

    private FontFileCatalog(ImmutableArray<FontFileEntry> entries) => _entries = entries;

    public ImmutableArray<FontFileEntry> Entries => _entries;

    /// <param name="registryEntries">Value name and file path (absolute) pairs.</param>
    public static FontFileCatalog FromRegistryNames(IEnumerable<(string Name, string Path)> registryEntries)
    {
        ArgumentNullException.ThrowIfNull(registryEntries);
        var entries = new List<FontFileEntry>();
        foreach (var (name, path) in registryEntries.OrderBy(pair => pair.Path, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Name, StringComparer.Ordinal))
        {
            var extension = System.IO.Path.GetExtension(path);
            if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            var trimmed = name;
            var parenthesis = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
            if (parenthesis > 0) trimmed = trimmed[..parenthesis];
            foreach (var part in trimmed.Split(" & ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (family, bold, italic) = Split(part);
                entries.Add(new FontFileEntry(family, bold, italic, path));
            }
        }
        return new FontFileCatalog([..entries]);
    }

    /// <summary>
    /// Splits <c>Arial Bold Italic</c> into family <c>Arial</c>, bold and italic. Only the trailing words Bold, Italic,
    /// Oblique and Regular are styles; other names (<c>Arial Narrow</c>, <c>Segoe UI Semibold</c>) are families of their own.
    /// </summary>
    public static (string Family, bool Bold, bool Italic) Split(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var bold = false;
        var italic = false;
        var changed = true;
        while (changed && words.Count > 1)
        {
            changed = false;
            var last = words[^1];
            if (last.Equals("Bold", StringComparison.OrdinalIgnoreCase)) { bold = true; words.RemoveAt(words.Count - 1); changed = true; }
            else if (last.Equals("Italic", StringComparison.OrdinalIgnoreCase) || last.Equals("Oblique", StringComparison.OrdinalIgnoreCase)) { italic = true; words.RemoveAt(words.Count - 1); changed = true; }
            else if (last.Equals("Regular", StringComparison.OrdinalIgnoreCase)) { words.RemoveAt(words.Count - 1); changed = true; }
        }
        return (string.Join(' ', words), bold, italic);
    }

    /// <summary>
    /// The file of <paramref name="family"/>. A bold request prefers a bold face and otherwise takes the regular one
    /// (<paramref name="isExactWeight"/> is false then); a regular request never takes a bold face.
    /// </summary>
    public FontFileEntry? Find(string family, bool bold, out bool isExactWeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        FontFileEntry? regular = null;
        FontFileEntry? boldEntry = null;
        foreach (var entry in _entries)
        {
            if (entry.Italic || !entry.Family.Equals(family, StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Bold) boldEntry ??= entry;
            else regular ??= entry;
        }
        if (bold && boldEntry is not null)
        {
            isExactWeight = true;
            return boldEntry;
        }
        isExactWeight = !bold;
        return regular ?? (bold ? null : boldEntry);
    }

    public bool HasFamily(string family) => _entries.Any(entry => entry.Family.Equals(family, StringComparison.OrdinalIgnoreCase));
}
