using System.Collections.Immutable;

namespace CnCNew.Ui.Text;

/// <summary>One label of a string table as the game displays it: the first value, whitespace-normalized.</summary>
/// <param name="Speech">Speech (wave) file name of the first value, or <c>null</c> when there is none.</param>
public sealed record StringEntry(string Label, string Text, string? Speech);

/// <summary>A label that occurs more than once (case-insensitively); <see cref="Occurrences"/> counts every occurrence.</summary>
public sealed record DuplicateLabel(string Label, int Occurrences);

public enum StringTableFormat
{
    /// <summary>Binary compiled string file (<c>Data/&lt;Language&gt;/Generals.csf</c>).</summary>
    Csf,
    /// <summary>Textual string file (<c>Data/Generals.str</c>, <c>map.str</c>).</summary>
    Str,
}

/// <summary>
/// Immutable label table with the lookup rules of <c>GameTextManager::fetch</c> (GameText.cpp): labels compare
/// case-insensitively (<c>compareLUT</c> uses <c>stricmp</c>). Entries keep file order; when a label occurs more than
/// once the first occurrence wins and the collision is listed in <see cref="Duplicates"/> (the original's
/// <c>qsort</c>/<c>bsearch</c> pick an unspecified one).
/// </summary>
public sealed class StringTable
{
    private readonly Dictionary<string, StringEntry> _byLabel;

    public StringTable(string source, StringTableFormat format, IEnumerable<StringEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(entries);
        Source = source;
        Format = format;
        Entries = [..entries];
        _byLabel = new Dictionary<string, StringEntry>(Entries.Length, StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            _byLabel.TryAdd(entry.Label, entry);
            counts[entry.Label] = counts.GetValueOrDefault(entry.Label) + 1;
        }
        Duplicates = [..counts.Where(pair => pair.Value > 1)
            .Select(pair => new DuplicateLabel(_byLabel[pair.Key].Label, pair.Value))
            .OrderBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Label, StringComparer.Ordinal)];
    }

    public string Source { get; }
    public StringTableFormat Format { get; }
    public ImmutableArray<StringEntry> Entries { get; }
    public ImmutableArray<DuplicateLabel> Duplicates { get; }
    public int Count => _byLabel.Count;

    public bool TryGet(string label, out StringEntry entry)
    {
        ArgumentNullException.ThrowIfNull(label);
        return _byLabel.TryGetValue(label, out entry!);
    }

    public bool Contains(string label) => _byLabel.ContainsKey(label);

    /// <summary><c>GameTextManager::getStringsWithLabelPrefix</c>: labels that start with <paramref name="prefix"/>
    /// (the original compares case-sensitively with <c>strstr</c>), in canonical order.</summary>
    public ImmutableArray<string> LabelsWithPrefix(string prefix)
        => [.._byLabel.Values.Select(entry => entry.Label)
            .Where(label => label.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(label => label, StringComparer.Ordinal)];

    public static StringTable FromCsf(CsfFile file, string source)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new StringTable(source, StringTableFormat.Csf, file.Labels.Select(label => label.ToEntry()));
    }
}

/// <summary>Whitespace normalization shared by both string formats.</summary>
public static class GameTextWhitespace
{
    /// <summary>
    /// <c>GameTextManager::stripSpaces</c>: drops leading blanks and blanks after <c>\n</c>/<c>\t</c>, collapses runs of
    /// blanks to one, removes a blank directly before <c>\n</c>/<c>\t</c> and a trailing blank. Only U+0020 counts as blank.
    /// </summary>
    public static string StripSpaces(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new System.Text.StringBuilder(text.Length);
        var skipAll = true;
        var last = '\0';
        foreach (var ch in text)
        {
            if (ch == ' ' && (last == ' ' || skipAll)) continue;
            if (ch is '\n' or '\t')
            {
                if (last == ' ') output.Length--;
                skipAll = true;
                output.Append(ch);
                last = ch;
                continue;
            }
            output.Append(ch);
            last = ch;
            skipAll = false;
        }
        if (last == ' ') output.Length--;
        return output.ToString();
    }
}
