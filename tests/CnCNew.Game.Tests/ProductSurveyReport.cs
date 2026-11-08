using System.Text;

namespace CnCNew.Game.Tests;

/// <summary>
/// What the product survey (<see cref="ProductSurveyTests"/>) found broken. A finding has a stable key (the same defect found in many configurations is one finding with
/// the list of configurations), a text and the steps that reproduce it. <c>baselines/product-survey-known.txt</c> lists findings that are known and explained
/// (<c>KEY | reason</c>); a known finding is reported but does not fail the survey, any other does.
/// </summary>
internal sealed class ProductSurveyReport
{
    private const string KnownPath = "tests/CnCNew.Game.Tests/baselines/product-survey-known.txt";

    private sealed class Entry(string key, string area, string text, string repro)
    {
        public string Key { get; } = key;
        public string Area { get; } = area;
        public string Text { get; } = text;
        public string Repro { get; } = repro;
        public SortedSet<string> Configs { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Subjects { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _notes = [];
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);

    public static ProductSurveyReport Instance { get; } = new();

    private ProductSurveyReport()
    {
        if (!File.Exists(KnownPath)) return;
        foreach (var line in File.ReadAllLines(KnownPath))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var bar = line.IndexOf(" | ", StringComparison.Ordinal);
            if (bar > 0) _known[line[..bar].Trim()] = line[(bar + 3)..].Trim();
        }
    }

    /// <summary>A known finding: its key is in the file, or a key written <c>KEY&lt;=N</c> allows a finding that names at most N subjects (a rollup may not grow).</summary>
    private bool IsKnown(Entry entry, out string reason)
    {
        if (_known.TryGetValue(entry.Key, out reason!)) return true;
        foreach (var (key, value) in _known)
        {
            var marker = key.LastIndexOf("<=", StringComparison.Ordinal);
            if (marker > 0 && key[..marker] == entry.Key && int.TryParse(key[(marker + 2)..], out var limit) && entry.Subjects.Count <= limit)
            {
                reason = value;
                return true;
            }
        }
        reason = string.Empty;
        return false;
    }

    public int Unknown => _entries.Values.Count(entry => !IsKnown(entry, out _));
    public int Known => _entries.Values.Count(entry => IsKnown(entry, out _));

    /// <summary>A finding of its own; <paramref name="subject"/> makes it a rollup that names every window, button or screen it concerns.</summary>
    public void Add(string key, string area, string text, string repro, string config = "", string? subject = null)
    {
        if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry(key, area, text, repro);
        if (config.Length > 0) entry.Configs.Add(config);
        if (subject is not null) entry.Subjects.Add(subject);
    }

    public void Note(string line) => _notes.Add(line);

    public string Write(string path)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Product survey: {Unknown} finding(s), {Known} known");
        foreach (var note in _notes) text.AppendLine(note);
        foreach (var group in _entries.Values.GroupBy(entry => entry.Area).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.AppendLine();
            text.AppendLine($"## {group.Key}");
            foreach (var entry in group.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var known = IsKnown(entry, out var reason);
                text.AppendLine($"- {(known ? "KNOWN" : "FOUND")} {entry.Key}: {entry.Text}{(entry.Subjects.Count > 0 ? $" - {entry.Subjects.Count} in all" : string.Empty)}");
                if (known) text.AppendLine($"    reason: {reason}");
                text.AppendLine($"    reproduce: {entry.Repro}");
                foreach (var subject in entry.Subjects) text.AppendLine($"      * {subject}");
                if (entry.Configs.Count > 0) text.AppendLine($"    configurations ({entry.Configs.Count}): {string.Join("; ", entry.Configs.Take(12))}{(entry.Configs.Count > 12 ? "; ..." : string.Empty)}");
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text.ToString());
        return text.ToString();
    }

    public IEnumerable<string> UnknownLines() => _entries.Values.Where(entry => !IsKnown(entry, out _)).OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => $"{entry.Key}: {entry.Text}{(entry.Subjects.Count > 0 ? $" - {entry.Subjects.Count} in all" : string.Empty)}");
}
