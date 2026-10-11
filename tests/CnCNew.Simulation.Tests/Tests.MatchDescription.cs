using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

/// <summary>Phase 24: the match description - an immutable value with one encoding from which every machine builds the same initial state.</summary>
static partial class Tests
{
    private static MatchDescription SyntheticDescription(SyntheticFixture fixture, TerrainMap terrain)
    {
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        return SkirmishMatchSession.Describe(new SkirmishMatchOptions(0xC0FFEE06, 7, 3_000, 60, 500, 500, 90), fixture.Templates, fixture.Fingerprint, terrain,
            [..participants.Select(participant => new SkirmishSlot(participant))], "Maps/Synthetic/Synthetic.map");
    }

    /// <summary>The description is a value with one encoding: it survives its bytes, and every field is in its fingerprint.</summary>
    public static void MatchDescriptionIsCanonicalAndEveryFieldCounts()
    {
        var (fixture, terrain, _) = StartSyntheticMatch(100);
        var description = SyntheticDescription(fixture, terrain);
        var bytes = description.Encode();
        var decoded = MatchDescription.Decode(bytes);
        Check.That(decoded.Encode().AsSpan().SequenceEqual(bytes) && decoded.Equals(description) && decoded.Fingerprint == description.Fingerprint, "A description survives its own bytes.");
        var slot = description.Slots[0];
        var variants = new (string Field, MatchDescription Changed)[]
        {
            ("tick rate", description with { TickRate = description.TickRate == 30 ? 60 : 30 }),
            ("map path", description with { MapPath = "Maps/Other/Other.map" }),
            ("terrain fingerprint", description with { TerrainFingerprint = description.TerrainFingerprint + "0" }),
            ("definition fingerprint", description with { DefinitionFingerprint = new string('A', 64) }),
            ("seed", description with { Seed = description.Seed + 1 }),
            ("AI seed", description with { AiSeed = description.AiSeed + 1 }),
            ("max ticks", description with { MaxTicks = description.MaxTicks + 1 }),
            ("start money", description with { StartMoney = description.StartMoney + 1 }),
            ("grace", description with { GraceFrames = description.GraceFrames + 1 }),
            ("science point interval", description with { SciencePointIntervalFrames = description.SciencePointIntervalFrames + 1 }),
            ("command table version", description with { CommandTableVersion = description.CommandTableVersion + 1 }),
            ("world state version", description with { WorldStateVersion = description.WorldStateVersion + 1 }),
            ("simulation identifier", description with { SimulationId = "0123456789abcdef0123456789abcdef01234567" }),
            ("slot count", description with { Slots = description.Slots.Add(new MatchSlotDescription(MatchSlotKind.Closed)) }),
            ("slot kind", description with { Slots = description.Slots.SetItem(0, slot with { Kind = MatchSlotKind.Human }) }),
            ("slot faction", description with { Slots = description.Slots.SetItem(0, slot with { Faction = string.Empty }) }),
            ("slot team", description with { Slots = description.Slots.SetItem(0, slot with { Team = slot.Team + 1 }) }),
            ("slot colour", description with { Slots = description.Slots.SetItem(0, slot with { Color = 3 }) }),
            ("slot start position", description with { Slots = description.Slots.SetItem(0, slot with { StartPosition = -1 }) }),
            ("slot AI strength", description with { Slots = description.Slots.SetItem(0, slot with { AiStrength = 2 }) }),
            ("slot AI composition", description with { Slots = description.Slots.SetItem(0, slot with { AiComposition = 0 }) }),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal) { description.Fingerprint };
        foreach (var (field, changed) in variants)
        {
            Check.That(seen.Add(changed.Fingerprint), $"Changing the {field} changes the fingerprint of the description.");
            Check.That(MatchDescription.Decode(changed.Encode()).Fingerprint == changed.Fingerprint, $"The description with another {field} survives its bytes.");
        }
        Check.Throws<FormatException>(() => MatchDescription.Decode(bytes.AsSpan(0, bytes.Length - 3)), "match description");
        Check.Throws<FormatException>(() => MatchDescription.Decode([..bytes, 0]), "Trailing bytes");
        Check.Throws<ArgumentException>(() => (description with { CommandTableVersion = description.CommandTableVersion + 1 }).Resolve(fixture.Templates, terrain), "simulation version");
        Check.Throws<ArgumentException>(() => (description with { TerrainFingerprint = "other" }).Resolve(fixture.Templates, terrain), "different terrain");
        Check.That(MatchDescription.EncodingVersion == 2 && description.SimulationId.Length == 0, "A description made in the process states no simulation identifier (encoding 2).");
        var stated = description with { SimulationId = "abc" };
        Check.That(stated.Resolve(fixture.Templates, terrain, simulationId: "abc").Slots.Length == 2 && stated.Resolve(fixture.Templates, terrain).Slots.Length == 2
                   && description.Resolve(fixture.Templates, terrain, simulationId: "abc").Slots.Length == 2, "A stated identifier is accepted by the same build; none stated or none asked is not checked.");
        Check.Throws<ArgumentException>(() => stated.Resolve(fixture.Templates, terrain, simulationId: "abd+modified"), "this build is");
    }

    /// <summary>
    /// Two sessions built from the same bytes are the same at tick 0 and stay the same; "random" is decided by the seed of the description, not by who resolves it; and the
    /// description decides who is who: a closed slot and a spectator take no player number, a named start position is obeyed.
    /// </summary>
    public static void MatchDescriptionBuildsTheSameMatchEverywhere()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m), ("Player_3_Start", 300m, 60m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var names = ReferenceMatchScenario.Participants(fixture.Templates, out _).Select(participant => participant.Faction.Name).ToArray();
        var description = new MatchDescription(TestRate.Default.TicksPerSecond, "Maps/Synthetic/Synthetic.map", terrain.Fingerprint, fixture.Fingerprint.ToUpperInvariant(), 0xC0FFEE06, 7, 1_200, 500, 60, 0,
        [
            new MatchSlotDescription(MatchSlotKind.Closed),
            new MatchSlotDescription(MatchSlotKind.Computer, Faction: string.Empty, StartPosition: -1, AiStrength: 2),
            new MatchSlotDescription(MatchSlotKind.Spectator),
            new MatchSlotDescription(MatchSlotKind.Computer, Faction: names[0], StartPosition: 1, Color: 0),
        ]);
        var bytes = description.Encode();
        var first = SkirmishMatchSession.Start(MatchDescription.Decode(bytes), fixture.Templates, terrain, map.Objects);
        var second = SkirmishMatchSession.Start(MatchDescription.Decode(bytes), new SyntheticFixture(TestRate.Default, files: SkirmishFiles()).Templates, TerrainMap.Build(map, "skirmish"), map.Objects);
        Check.That(first.StateHash() == second.StateHash() && first.World.StateHash == second.World.StateHash, "Two sessions built from the same bytes have the same tick-0 hash.");
        for (var index = 0; index < 600; index++)
        {
            first.Step();
            second.Step();
            if (first.StateHash() != second.StateHash()) throw new InvalidOperationException($"The two sessions differ after tick {index}.");
        }
        var match = first.Match;
        Check.That(match.Slots.Length == 2 && match.Slots[0].Slot == 1 && match.Slots[0].Player.Value == 1 && match.Slots[1].Slot == 3 && match.Slots[1].Player.Value == 2,
            "Closed and spectator slots take no player number; the others are numbered in slot order.");
        Check.That(match.Slots[1].StartPosition == 1 && match.Slots[0].StartPosition != 1 && match.Slots[0].StartPosition >= 0, "A named start position is obeyed, a random one takes a free one.");
        Check.That(match.Slots[1].Color == 0 && match.Slots[0].Color == 1, "A random colour is the lowest free one.");
        Check.That(match.AiStrengthsIgnored == 1 && first.Notes.Any(note => note.Contains("strength", StringComparison.Ordinal)), "A strength the built-in AI does not have is counted and noted, not obeyed.");
        Check.That(second.Match.Slots[0].Participant.Faction.Name == match.Slots[0].Participant.Faction.Name && second.Match.Slots[0].StartPosition == match.Slots[0].StartPosition,
            "The random faction and the random start position are the same in both sessions.");
        var picks = new HashSet<string>(StringComparer.Ordinal);
        for (uint seed = 1; seed <= 40; seed++)
        {
            var resolved = (description with { Seed = seed }).Resolve(fixture.Templates, terrain);
            picks.Add(resolved.Slots[0].Participant.Faction.Name + "@" + resolved.Slots[0].StartPosition.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Check.That(picks.Count > 1, $"The seed decides the random choices ({picks.Count} different outcomes over 40 seeds).");
        Check.Throws<ArgumentException>(() => (description with { Slots = description.Slots.SetItem(1, description.Slots[1] with { StartPosition = 1 }) }).Resolve(fixture.Templates, terrain), "named by two slots");
        Check.Throws<ArgumentException>(() => (description with { Slots = description.Slots.SetItem(3, description.Slots[3] with { Faction = "FactionNobody" }) }).Resolve(fixture.Templates, terrain), "Unknown or unplayable");
    }
}
