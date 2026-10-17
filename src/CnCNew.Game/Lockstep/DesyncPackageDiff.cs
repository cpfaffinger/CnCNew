using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CnCNew.Data.World;
using CnCNew.Game.Diagnostics;
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

    /// <summary>Where the state of a package differs from the match played again from the package's own recording, at the tick the state stands at.</summary>
    public static ImmutableArray<string> FieldsAgainstReplay(string directory, TemplateCatalog catalog, string definitionFingerprint, Func<string, MatchMap> loadMap, int limit = 200)
    {
        var replay = ReplayFormat.Read(File.ReadAllBytes(Path.Combine(directory, "replay.bin")));
        var description = SkirmishReplay.DescriptionOf(replay.Header) ?? throw new InvalidDataException("The package's recording has no match description.");
        var map = loadMap(description.MapPath);
        var played = SkirmishReplay.Play(replay, catalog, definitionFingerprint, map.Terrain, map.Objects, stopAtFirstDeviation: false, audit: false);
        using var expected = JsonDocument.Parse(played.Session.Save());
        using var actual = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "state.bin")));
        // The shared field comparison of the diagnosis tools (docs/MATCH-ANALYSIS.md section 5); the embedded recording is not compared.
        static bool Recording(string name) => name == "recording";
        return [..StateFields.Compare(StateFields.OfJson(expected.RootElement, Recording), StateFields.OfJson(actual.RootElement, Recording), limit: limit).Select(field => field.ToString())];
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
