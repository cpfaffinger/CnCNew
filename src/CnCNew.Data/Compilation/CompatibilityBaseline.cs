using System.Collections.Immutable;
using System.Text.Json;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Compilation;

/// <summary>
/// The reviewed compatibility baseline: names, owner kinds, frequencies and planned targets of every case the compiler
/// accepts without an implementation (never asset contents). Any new, missing or changed entry is a difference.
/// </summary>
public static class CompatibilityBaseline
{
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Entry(string Category, string Name, int Count, string[] Owners, string PlannedTarget);
    private sealed record Document(int SchemaVersion, string Mode, Entry[] Entries);

    public static string Serialize(ImmutableArray<CompatibilityEntry> entries, string mode)
        => JsonSerializer.Serialize(new Document(SchemaVersion, mode,
            [..entries.OrderBy(value => value.Category, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal)
                .Select(value => new Entry(value.Category, value.Name, value.Count, [..value.OwnerKinds], value.PlannedTarget))]), Options) + "\n";

    public static List<string> Compare(string baselineJson, ImmutableArray<CompatibilityEntry> current)
    {
        var baseline = JsonSerializer.Deserialize<Document>(baselineJson, Options) ?? throw new InvalidDataException("Empty compatibility baseline.");
        if (baseline.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Unsupported compatibility baseline schema {baseline.SchemaVersion}.");
        var expected = baseline.Entries.ToDictionary(value => (value.Category, value.Name));
        var actual = current.ToDictionary(value => (value.Category, value.Name));
        var differences = new List<string>();
        foreach (var (key, entry) in actual.OrderBy(pair => pair.Key.Category, StringComparer.Ordinal).ThenBy(pair => pair.Key.Name, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(key, out var previous))
                differences.Add($"NEW      {key.Category} '{key.Name}' x{entry.Count} ({entry.PlannedTarget})");
            else if (previous.Count != entry.Count || previous.PlannedTarget != entry.PlannedTarget || !previous.Owners.SequenceEqual(entry.OwnerKinds))
                differences.Add($"CHANGED  {key.Category} '{key.Name}': baseline x{previous.Count} [{previous.PlannedTarget}] now x{entry.Count} [{entry.PlannedTarget}]");
        }
        foreach (var (key, entry) in expected.OrderBy(pair => pair.Key.Category, StringComparer.Ordinal).ThenBy(pair => pair.Key.Name, StringComparer.Ordinal))
            if (!actual.ContainsKey(key))
                differences.Add($"MISSING  {key.Category} '{key.Name}' (baseline x{entry.Count})");
        return differences;
    }
}
