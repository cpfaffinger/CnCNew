using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

/// <summary>
/// Phase 24: the state of the computer players is part of the session state. The source tests keep <see cref="SkirmishAi"/> honest when it grows (model: "every
/// IUpdateModule stores nextWake"); the behaviour tests save a running match and compare the continuation with the uninterrupted session tick by tick.
/// </summary>
static partial class Tests
{
    private static string SkirmishAiSource(string file) => File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "CnCNew.Simulation", "Skirmish", file));

    /// <summary>The instance fields of <see cref="SkirmishAi"/> with their declared type, from all parts of the class.</summary>
    private static List<(string Name, string Type)> SkirmishAiFields()
    {
        var fields = new List<(string, string)>();
        var declaration = new Regex(@"^    private (?:readonly )?(?<type>[^=;]+?) (?<name>_\w+)(?: = [^;]*)?;\s*$", RegexOptions.Multiline);
        foreach (var file in new[] { "SkirmishAi.cs", "SkirmishAi.Composition.cs", "SkirmishAi.Profiles.cs", "SkirmishAi.State.cs" })
            foreach (Match match in declaration.Matches(SkirmishAiSource(file).Replace("\r", string.Empty)))
                if (!match.Groups["type"].Value.StartsWith("static ", StringComparison.Ordinal) && !match.Groups["type"].Value.StartsWith("const ", StringComparison.Ordinal))
                    fields.Add((match.Groups["name"].Value, match.Groups["type"].Value));
        return fields;
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Check.That(start >= 0, $"SkirmishAi.State.cs declares '{signature}'.");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[open..index];
        }
        throw new InvalidOperationException("Unbalanced braces.");
    }

    public static void SkirmishAiStateNamesEveryField()
    {
        var fields = SkirmishAiFields();
        Check.That(fields.Count >= 50, $"The scan finds the fields of SkirmishAi ({fields.Count}).");
        var state = SkirmishAiSource("SkirmishAi.State.cs");
        var describe = MethodBody(state, "public void Describe(IStateWriter writer)");
        var load = MethodBody(state, "public void Load(IStateReader reader)");
        var exempt = SkirmishAi.NotState.Select(entry => entry.Field).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (name, _) in fields)
        {
            var word = new Regex($@"(?<!\w){Regex.Escape(name)}(?!\w)");
            if (exempt.Contains(name))
            {
                if (word.IsMatch(describe) || word.IsMatch(load)) problems.Add($"{name} is listed as no state but is written or read");
                continue;
            }
            if (name == "_random") { if (!describe.Contains("_random.Describe(", StringComparison.Ordinal) || !load.Contains("_random.Load(", StringComparison.Ordinal)) problems.Add("_random is not written and read"); continue; }
            if (!word.IsMatch(describe)) problems.Add($"{name} is not written by Describe");
            if (!word.IsMatch(load)) problems.Add($"{name} is not read by Load");
        }
        foreach (var entry in SkirmishAi.NotState)
        {
            if (fields.All(field => field.Name != entry.Field)) problems.Add($"NotState names {entry.Field}, which is no field of SkirmishAi");
            if (entry.Reason.Length < 10) problems.Add($"NotState gives no reason for {entry.Field}");
        }
        Check.That(problems.Count == 0, "Every field of SkirmishAi is state (written and read) or a named exception: " + string.Join("; ", problems));
    }

    /// <summary>
    /// Nothing may act in the order of a hash table: that order is not part of the state (a restored table enumerates differently). Allowed on a <c>Dictionary</c> or <c>HashSet</c>
    /// field are the keyed members and <c>Count</c>; the state file sorts before it writes.
    /// </summary>
    public static void SkirmishAiNeverActsInTheOrderOfAHashTable()
    {
        var unordered = SkirmishAiFields().Where(field => field.Type.StartsWith("Dictionary<", StringComparison.Ordinal) || field.Type.StartsWith("HashSet<", StringComparison.Ordinal)).Select(field => field.Name).ToList();
        Check.That(unordered.Count >= 10, $"The scan finds the hash tables of SkirmishAi ({unordered.Count}).");
        var allowed = new Regex(@"^(\.(TryGetValue|GetValueOrDefault|ContainsKey|Contains|Add|Remove|Clear|Count)\b|\[)");
        var problems = new List<string>();
        foreach (var file in new[] { "SkirmishAi.cs", "SkirmishAi.Composition.cs" })
        {
            var lines = SkirmishAiSource(file).Replace("\r", string.Empty).Split('\n');
            for (var number = 0; number < lines.Length; number++)
            {
                var line = lines[number];
                if (line.TrimStart().StartsWith("private ", StringComparison.Ordinal) || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (var name in unordered)
                    foreach (Match match in new Regex($@"(?<!\w){Regex.Escape(name)}(?!\w)").Matches(line))
                        if (!allowed.IsMatch(line[(match.Index + match.Length)..])) problems.Add($"{file}:{number + 1} {name}");
            }
        }
        Check.That(problems.Count == 0, "SkirmishAi uses its hash tables only by key: " + string.Join(", ", problems));
    }

    private static (SyntheticFixture Fixture, TerrainMap Terrain, SkirmishMatchSession Session) StartSyntheticMatch(long maxTicks, uint aiSeed = 7)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, aiSeed, maxTicks, 60, 500, 500, 0);
        var session = SkirmishMatchSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map.Objects, [..participants.Select(participant => new SkirmishSlot(participant))]);
        return (fixture, terrain, session);
    }

    /// <summary>
    /// The acceptance of step 2 on the synthetic match: saved at arbitrary ticks (frame boundaries, ticks inside a frame, a tick the AI thinks in and the tick before it), loaded
    /// into a fresh session and continued, the session hash - world plus every AI - is the one of the uninterrupted session in every tick, and so is the recording at the end.
    /// </summary>
    public static void SkirmishSessionSavedAtAnyTickContinuesIdentically()
    {
        var (fixture, terrain, session) = StartSyntheticMatch(TestRate.Default.Time.OriginalFrames(4_500));
        // Save points in game time (logic frames of the original), so that the match is as long for them at every tick rate; every second one is shifted by a tick, which
        // puts it inside a frame above 30 Hz.
        var frames = new long[] { 1, 19, 300, 301, 600, 750, 900, 1_100, 1_500, 2_000, 3_000, 4_400 };
        var candidates = frames.Select((frame, index) => session.World.Time.OriginalFrames(frame) + index % 2).ToArray();
        var saves = new Dictionary<long, byte[]>();
        var hashes = new List<ulong>();
        var aiHashes = new HashSet<ulong>();
        while (!session.IsOver)
        {
            if (candidates.Contains(session.World.Tick)) saves[session.World.Tick] = session.Save();
            session.Step();
            hashes.Add(session.StateHash());
            aiHashes.Add(session.Ais[0]!.StateHash());
        }
        var saveTicks = saves.Keys.Order().ToArray();
        Check.That(saveTicks.Length >= 5 && saveTicks[^1] > session.EndTick / 2, $"The match is long enough for the save points ({saveTicks.Length} of them, the match ends at tick {session.EndTick}).");
        Check.That(aiHashes.Count > 100, $"The AI's own hash moves while it plays ({aiHashes.Count} different values).");
        Check.That(session.Ais.All(ai => ai!.Stats.Commands > 50), "Both AIs gave orders (the state that is saved is not empty).");
        var reference = CnCNew.Simulation.Replay.ReplayFormat.Write(session.ToReplay());

        foreach (var tick in saveTicks)
        {
            var restored = SkirmishMatchSession.Restore(saves[tick], fixture.Templates, fixture.Fingerprint, terrain);
            Check.That(restored.World.Tick == tick, $"The session saved at tick {tick} is restored at that tick.");
            Check.That(restored.Save().AsSpan().SequenceEqual(saves[tick]), $"The session restored at tick {tick} writes the bytes it was read from.");
            while (!restored.IsOver)
            {
                var at = restored.World.Tick;
                restored.Step();
                if (restored.StateHash() != hashes[(int)at])
                    throw new InvalidOperationException($"The session saved at tick {tick} differs from the uninterrupted one after tick {at}.");
            }
            Check.That(restored.Winner == session.Winner && restored.VictoryTick == session.VictoryTick && restored.EndTick == session.EndTick, $"The session saved at tick {tick} ends like the uninterrupted one.");
            Check.That(CnCNew.Simulation.Replay.ReplayFormat.Write(restored.ToReplay()).AsSpan().SequenceEqual(reference), $"The session saved at tick {tick} yields the whole replay of the match, byte for byte.");
            for (var index = 0; index < session.Ais.Length; index++)
                Check.That(restored.Ais[index]!.Stats.Commands == session.Ais[index]!.Stats.Commands, $"The AI of slot {index} counts its orders across the save at tick {tick}.");
        }
    }

    /// <summary>A hash that leaves the AI out would not notice a deviation in it; the session hash does: one remembered value changed in one AI changes it, the world hash stays.</summary>
    public static void SkirmishSessionHashCoversTheAi()
    {
        var (fixture, terrain, session) = StartSyntheticMatch(4_000);
        for (var index = 0; index < 2_000; index++) session.Step();
        var save = session.Save();
        var text = System.Text.Encoding.UTF8.GetString(save);
        var marker = "\"threshold\":";
        var at = text.IndexOf(marker, StringComparison.Ordinal);
        Check.That(at > 0, "The save holds the AI's attack threshold.");
        var end = text.IndexOf(',', at);
        var value = long.Parse(text[(at + marker.Length)..end], System.Globalization.CultureInfo.InvariantCulture);
        var tampered = System.Text.Encoding.UTF8.GetBytes(text[..(at + marker.Length)] + (value + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + text[end..]);
        Check.Throws<SnapshotFormatException>(() => SkirmishMatchSession.Restore(tampered, fixture.Templates, fixture.Fingerprint, terrain), "integrity check failed");
        var restored = SkirmishMatchSession.Restore(save, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.StateHash() == session.StateHash() && restored.World.StateHash == session.World.StateHash, "The untouched save restores the session hash and the world hash.");
        Check.Throws<SnapshotFormatException>(() => SkirmishMatchSession.Restore(save, fixture.Templates, new string('0', 64), terrain), "different definitions");
    }
}
