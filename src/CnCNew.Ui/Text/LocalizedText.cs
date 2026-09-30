using System.Collections.Immutable;

namespace CnCNew.Ui.Text;

/// <summary>
/// Label lookup facade for the UI, following <c>GameTextManager::fetch</c> (GameText.cpp): the main table is searched
/// first, then the table of the current map (<c>initMapStringFile</c>, <c>map.str</c>). A missing label never becomes
/// an empty string: <see cref="Get"/> returns the visible marker <c>??label??</c> (the original shows
/// <c>MISSING: 'label'</c>) and records the label in <see cref="MissingLabels"/> (the original's <c>m_noStringList</c>).
/// </summary>
public sealed class LocalizedText
{
    private readonly object _gate = new();
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);
    private int _missCount;

    public LocalizedText(StringTable table, StringTable? mapTable = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        Table = table;
        MapTable = mapTable;
    }

    public StringTable Table { get; }
    public StringTable? MapTable { get; }

    /// <summary>Distinct missing labels in ordinal order.</summary>
    public ImmutableArray<string> MissingLabels
    {
        get { lock (_gate) return [.._missing]; }
    }

    /// <summary>Number of failed lookups including repeats.</summary>
    public int MissCount
    {
        get { lock (_gate) return _missCount; }
    }

    public static string MissingMarker(string label) => $"??{label}??";

    /// <summary>A facade over the same main table with another map table (the original resets it per map).</summary>
    public LocalizedText WithMapStrings(StringTable? mapTable) => new(Table, mapTable);

    public bool TryGetEntry(string label, out StringEntry entry)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (Table.TryGet(label, out entry)) return true;
        return MapTable is not null && MapTable.TryGet(label, out entry);
    }

    public bool TryGet(string label, out string text)
    {
        var found = TryGetEntry(label, out var entry);
        text = found ? entry.Text : string.Empty;
        return found;
    }

    public string Get(string label)
    {
        if (TryGet(label, out var text)) return text;
        RecordMiss(label);
        return MissingMarker(label);
    }

    /// <summary><c>fetchFormat</c>: formats the text with printf-style arguments; a missing label yields the marker unformatted.</summary>
    public string Format(string label, params object?[] args)
    {
        if (!TryGet(label, out var text))
        {
            RecordMiss(label);
            return MissingMarker(label);
        }
        return GameTextFormatter.Format(text, args);
    }

    private void RecordMiss(string label)
    {
        lock (_gate)
        {
            _missing.Add(label);
            _missCount++;
        }
    }
}
