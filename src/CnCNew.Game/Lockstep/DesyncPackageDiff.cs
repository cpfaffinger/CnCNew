using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CnCNew.Data.World;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Lockstep;

/// <param name="FirstDifferingTick">The first tick both packages hold whose session hashes differ; -1 when none does.</param>
/// <param name="Parts">The parts of the session hash that differ at that tick.</param>
/// <param name="Objects">The objects whose hashes differ at that tick (or that only one side has), when both packages still hold the tick's object table.</param>
/// <param name="FieldsA">Where the state of package A differs from the match played again from its own recording, at the tick A's state stands at; <paramref name="FieldsB"/> likewise.</param>
/// <param name="Summary">The same as text, for a person.</param>
public sealed record DesyncDiffResult(long FirstDifferingTick, ImmutableArray<string> Parts, ImmutableArray<uint> Objects, ImmutableArray<string> FieldsA, ImmutableArray<string> FieldsB,
    long StateTickA, long StateTickB, string Summary);

/// <summary>
/// Compares the packages two clients wrote at a desync (docs/NETWORK-DIAGNOSTICS.md): the first tick their hash chains differ at, the parts of the hash, the objects - and,
/// by playing each package's own recording again to the tick its state stands at, the fields in which that client's state differs from what the match should be there.
/// The client that is in step shows no field; the one that diverged shows what diverged.
/// </summary>
public static class DesyncPackageDiff
{
    private static Dictionary<long, (string Hash, Dictionary<string, string> Parts)> ReadHashes(string directory)
    {
        var result = new Dictionary<long, (string, Dictionary<string, string>)>();
        foreach (var line in File.ReadLines(Path.Combine(directory, "hashes.tsv")))
        {
            var fields = line.Split('\t');
            if (fields.Length < 2) continue;
            var parts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields.Skip(2))
            {
                var at = field.IndexOf('=', StringComparison.Ordinal);
                if (at > 0) parts[field[..at]] = field[(at + 1)..];
            }
            result[long.Parse(fields[0], CultureInfo.InvariantCulture)] = (fields[1], parts);
        }
        return result;
    }

    private static Dictionary<uint, string> ReadObjects(string directory, long tick)
    {
        var result = new Dictionary<uint, string>();
        var prefix = tick.ToString(CultureInfo.InvariantCulture) + "\t";
        foreach (var line in File.ReadLines(Path.Combine(directory, "objects.tsv")))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var fields = line.Split('\t');
            result[uint.Parse(fields[1], CultureInfo.InvariantCulture)] = fields[2];
        }
        return result;
    }

    private static long StateTick(string directory)
    {
        using var info = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "info.json")));
        return info.RootElement.GetProperty("stateTick").GetInt64();
    }

    /// <summary>The paths at which two JSON values differ. Arrays of objects with an "id" are matched by it, others by position; the embedded recording is not compared.</summary>
    private static void Diff(JsonElement expected, JsonElement actual, string path, List<string> into, int limit)
    {
        if (into.Count >= limit) return;
        if (expected.ValueKind != actual.ValueKind)
        {
            into.Add($"{path}: {Short(expected)} -> {Short(actual)}");
            return;
        }
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in expected.EnumerateObject())
                {
                    seen.Add(property.Name);
                    if (property.Name == "recording") continue;
                    if (actual.TryGetProperty(property.Name, out var other)) Diff(property.Value, other, path.Length == 0 ? property.Name : path + "." + property.Name, into, limit);
                    else into.Add($"{path}.{property.Name}: {Short(property.Value)} -> (missing)");
                }
                foreach (var property in actual.EnumerateObject())
                    if (!seen.Contains(property.Name) && property.Name != "recording") into.Add($"{path}.{property.Name}: (missing) -> {Short(property.Value)}");
                break;
            case JsonValueKind.Array:
                var keyed = expected.GetArrayLength() > 0 && expected[0].ValueKind == JsonValueKind.Object && expected[0].TryGetProperty("id", out _);
                if (keyed)
                {
                    var others = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var item in actual.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id)) others[id.GetRawText()] = item;
                    foreach (var item in expected.EnumerateArray())
                    {
                        var id = item.GetProperty("id").GetRawText();
                        if (others.Remove(id, out var other)) Diff(item, other, $"{path}[id={id}]", into, limit);
                        else into.Add($"{path}[id={id}]: (present) -> (missing)");
                    }
                    foreach (var id in others.Keys) into.Add($"{path}[id={id}]: (missing) -> (present)");
                }
                else
                {
                    if (expected.GetArrayLength() != actual.GetArrayLength()) into.Add($"{path}: {expected.GetArrayLength()} entries -> {actual.GetArrayLength()} entries");
                    var index = 0;
                    using var right = actual.EnumerateArray();
                    foreach (var item in expected.EnumerateArray())
                    {
                        if (!right.MoveNext()) break;
                        Diff(item, right.Current, $"{path}[{index++}]", into, limit);
                    }
                }
                break;
            default:
                if (expected.GetRawText() != actual.GetRawText()) into.Add($"{path}: {Short(expected)} -> {Short(actual)}");
                break;
        }
    }

    private static string Short(JsonElement value)
    {
        var text = value.GetRawText();
        return text.Length <= 60 ? text : text[..57] + "...";
    }

    /// <summary>Where the state of a package differs from the match played again from the package's own recording, at the tick the state stands at.</summary>
    public static ImmutableArray<string> FieldsAgainstReplay(string directory, TemplateCatalog catalog, string definitionFingerprint, Func<string, MatchMap> loadMap, int limit = 200)
    {
        var replay = ReplayFormat.Read(File.ReadAllBytes(Path.Combine(directory, "replay.bin")));
        var description = SkirmishReplay.DescriptionOf(replay.Header) ?? throw new InvalidDataException("The package's recording has no match description.");
        var map = loadMap(description.MapPath);
        var played = SkirmishReplay.Play(replay, catalog, definitionFingerprint, map.Terrain, map.Objects, stopAtFirstDeviation: false, audit: false);
        using var expected = JsonDocument.Parse(played.Session.Save());
        using var actual = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "state.bin")));
        var fields = new List<string>();
        Diff(expected.RootElement, actual.RootElement, string.Empty, fields, limit);
        return [..fields];
    }

    public static DesyncDiffResult Compare(string directoryA, string directoryB, TemplateCatalog catalog, string definitionFingerprint, Func<string, MatchMap> loadMap)
    {
        var a = ReadHashes(directoryA);
        var b = ReadHashes(directoryB);
        var first = a.Keys.Where(b.ContainsKey).Order().Where(tick => a[tick].Hash != b[tick].Hash).DefaultIfEmpty(-1).First();
        var parts = ImmutableArray<string>.Empty;
        var objects = ImmutableArray<uint>.Empty;
        if (first >= 0)
        {
            parts = [..a[first].Parts.Keys.Union(b[first].Parts.Keys).Order(StringComparer.Ordinal).Where(name => a[first].Parts.GetValueOrDefault(name) != b[first].Parts.GetValueOrDefault(name))];
            var objectsA = ReadObjects(directoryA, first);
            var objectsB = ReadObjects(directoryB, first);
            if (objectsA.Count > 0 && objectsB.Count > 0)
                objects = [..objectsA.Keys.Union(objectsB.Keys).Order().Where(id => objectsA.GetValueOrDefault(id) != objectsB.GetValueOrDefault(id))];
        }
        var fieldsA = FieldsAgainstReplay(directoryA, catalog, definitionFingerprint, loadMap);
        var fieldsB = FieldsAgainstReplay(directoryB, catalog, definitionFingerprint, loadMap);
        var (tickA, tickB) = (StateTick(directoryA), StateTick(directoryB));
        var text = new System.Text.StringBuilder();
        text.AppendLine(first < 0 ? "The hash chains of the two packages do not differ in any tick both hold." : string.Create(CultureInfo.InvariantCulture, $"First differing tick: {first}"));
        if (first >= 0) text.AppendLine("Parts of the session hash that differ: " + (parts.IsEmpty ? "(none recorded)" : string.Join(", ", parts)));
        if (first >= 0) text.AppendLine("Objects that differ at that tick: " + (objects.IsEmpty ? "(none, or the tick's object table is no longer held)" : string.Join(", ", objects)));
        foreach (var (name, tick, fields) in new[] { ("A", tickA, fieldsA), ("B", tickB, fieldsB) })
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Package {name} against its own recording played again, at tick {tick}: {(fields.IsEmpty ? "no difference - this client is in step" : $"{fields.Length} difference(s)")}"));
            foreach (var field in fields.Take(40)) text.AppendLine("  " + field);
        }
        return new DesyncDiffResult(first, parts, objects, fieldsA, fieldsB, tickA, tickB, text.ToString());
    }
}
