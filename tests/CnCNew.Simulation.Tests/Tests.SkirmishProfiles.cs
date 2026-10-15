using System.Collections.Immutable;
using CnCNew.Data.World;
using System.Linq;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    /// <summary>The skirmish fixture's side with a factory, a tank and a base defence a dozer can raise (a tower with the soldier's gun).</summary>
    private static string SkirmishSideWithDefence(string side) => SkirmishSideWithFactory(side).Replace($"3 = Cmd_{side}Factory", $"3 = Cmd_{side}Factory\n  4 = Cmd_{side}Tower") + $$"""

        Object {{side}}Tower
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE FS_BASE_DEFENSE
          BuildCost = 30
          BuildTime = 1
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          Geometry = BOX
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SkirmishGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        CommandButton Cmd_{{side}}Tower
          Command = DOZER_CONSTRUCT
          Object = {{side}}Tower
        End
        """;

    private static IReadOnlyDictionary<string, string> SkirmishDefenceFiles()
    {
        var files = new Dictionary<string, string>(SkirmishFiles());
        files["Data/INI/Object/SkirmishFixture.ini"] = files["Data/INI/Object/SkirmishFixture.ini"].Replace(SkirmishSide("Alpha"), SkirmishSideWithDefence("Alpha")).Replace(SkirmishSide("Beta"), SkirmishSideWithDefence("Beta"));
        return files;
    }

    /// <summary>A match of the two slots' choices, <paramref name="originalFrames"/> long, with <paramref name="startMoney"/> credits (0: the fixture's 500).</summary>
    private static SkirmishMatchSession ProfileMatch(SyntheticFixture fixture, TerrainMap terrain, ImmutableArray<MapObject> objects, AiSlotChoice first, AiSlotChoice second, long originalFrames, int startMoney = 0)
    {
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var frames = fixture.Templates.Rate.TicksPerOriginalFrame;
        return SkirmishMatchSession.Start(new SkirmishMatchOptions(0xC0FFEE06, 7, originalFrames * frames, 60, 500, startMoney, 0), fixture.Templates, fixture.Fingerprint, terrain, objects,
            [first.Slot(participants[0]), second.Slot(participants[1])]);
    }

    /// <summary>
    /// The tactics profiles of the built-in AI (docs/SKIRMISH-AI.md): with the same money and the same seed on a long map (the bases do not meet), the rusher marches first (four
    /// units), the balanced AI next (eight), the eco AI later (twelve) and the turtle last (twenty); the turtle raises base defences before it has an army, the rusher none.
    /// </summary>
    public static void SkirmishTacticsProfilesBehave()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishDefenceFiles());
        var map = SyntheticMaps.Build(200, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 1900m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish-profiles");
        var frames = fixture.Templates.Rate.TicksPerOriginalFrame;
        var firstWave = new Dictionary<AiTactics, long>();
        // The most base defences a profile has raised while its army was smaller than six (before the balanced AI's rule of one per six units starts).
        var towers = new Dictionary<AiTactics, int>();
        foreach (var tactics in new[] { AiTactics.Balanced, AiTactics.Rush, AiTactics.Turtle, AiTactics.Eco })
        {
            var choice = new AiSlotChoice(Tactics: tactics);
            // 4 min of game time with 1,200 credits: enough for the base, a few towers and some thirty soldiers.
            var session = ProfileMatch(fixture, terrain, map.Objects, choice, choice, 7_200, 1_200);
            var wave = -1L;
            var early = 0;
            var owner = session.Players[0].Id;
            while (!session.IsOver)
            {
                session.Step();
                if (wave < 0 && session.Ais[0]!.Stats.Waves > 0) wave = session.World.Tick;
                if (session.World.Tick % (15L * frames) != 0) continue;
                var own = session.World.Entities.Where(entity => entity.Owner == owner && !entity.EffectivelyDead).ToList();
                if (own.Count(entity => entity.Template.Name is "AlphaSoldier" or "AlphaTank") < 6)
                    early = Math.Max(early, own.Count(entity => entity.Template.Name == "AlphaTower" && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction)));
            }
            firstWave[tactics] = wave < 0 ? long.MaxValue : wave / frames;
            towers[tactics] = early;
            Check.That(session.Ais[0]!.Tactics == tactics, "The AI plays the profile its slot names.");
        }
        string Report() => string.Join(", ", firstWave.Select(pair => $"{pair.Key}: wave at frame {(pair.Value == long.MaxValue ? "never" : pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}, {towers[pair.Key]} towers"));
        Check.That(firstWave[AiTactics.Rush] < firstWave[AiTactics.Balanced] && firstWave[AiTactics.Balanced] < firstWave[AiTactics.Eco] && firstWave[AiTactics.Eco] < firstWave[AiTactics.Turtle],
            $"The rusher marches first, then the balanced AI, the eco AI and the turtle ({Report()}).");
        Check.That(towers[AiTactics.Turtle] >= 1 && towers[AiTactics.Rush] == 0 && towers[AiTactics.Balanced] == 0, $"The turtle raises defences before it has an army, the others do not ({Report()}).");
    }

    /// <summary>
    /// The strengths of the built-in AI: they differ by how often the AI decides (easy every second, medium every half second, hard every third of a second - the same game time at
    /// every rate) and by what it may know (easy never weighs its army against the force it has seen); the same match is otherwise the same at every strength.
    /// </summary>
    public static void SkirmishStrengthsDecideAtTheirPace()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFiles());
        // A long map: nobody is defeated in the minute, so every AI thinks for all of it.
        var map = SyntheticMaps.Build(200, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 1900m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish-long");
        var thinks = new Dictionary<AiStrength, int>();
        foreach (var strength in new[] { AiStrength.Easy, AiStrength.Medium, AiStrength.Hard })
        {
            // 20 s of game time: before any wave can reach a base, so nobody wins and every AI thinks for all of it.
            var session = ProfileMatch(fixture, terrain, map.Objects, new AiSlotChoice(Strength: strength), AiSlotChoice.Default, 600);
            while (!session.IsOver) session.Step();
            var ai = session.Ais[0]!;
            Check.That(ai.Strength == strength, "The AI plays the strength its slot names.");
            thinks[strength] = ai.Stats.Thinks;
            if (strength == AiStrength.Easy) Check.That(ai.Stats.InformedDraws == 0, "An easy AI never weighs its army against what it has seen.");
        }
        var report = string.Join(", ", thinks.Select(pair => $"{pair.Key} {pair.Value}"));
        Check.That(Math.Abs(thinks[AiStrength.Easy] - 20) <= 1 && Math.Abs(thinks[AiStrength.Medium] - 40) <= 1 && Math.Abs(thinks[AiStrength.Hard] - 60) <= 1,
            $"Easy decides once a second, medium twice, hard three times in 20 s of game time ({report}).");
    }

    /// <summary>
    /// A slot that names no profile at medium strength is the AI as it was, and naming the balanced profile plays the same match (the profile is only the name of the old values); a
    /// profile and a strength travel with the description and the savegame: a restored session continues hash-identically, and the state of an AI without a profile has no field for one.
    /// </summary>
    public static void SkirmishProfilesTravelWithTheMatch()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var none = ProfileMatch(fixture, terrain, map.Objects, AiSlotChoice.Default, AiSlotChoice.Default, 3_600);
        var balanced = ProfileMatch(fixture, terrain, map.Objects, new AiSlotChoice(Tactics: AiTactics.Balanced), new AiSlotChoice(Tactics: AiTactics.Balanced), 3_600);
        while (!none.IsOver) none.Step();
        while (!balanced.IsOver) balanced.Step();
        Check.That(none.World.StateHash == balanced.World.StateHash && none.VictoryTick == balanced.VictoryTick, "The balanced profile plays the match of the AI without a profile.");
        Check.That(none.Description.Slots.All(slot => slot.AiTactics == -1) && balanced.Description.Slots.All(slot => slot.AiTactics == (int)AiTactics.Balanced), "The description names the profile only when one was chosen.");

        foreach (var (first, second) in new[] { (new AiSlotChoice(Tactics: AiTactics.Rush, Strength: AiStrength.Hard), new AiSlotChoice(Tactics: AiTactics.Eco, Strength: AiStrength.Easy)),
                     (new AiSlotChoice(Tactics: AiTactics.Turtle), AiSlotChoice.Default) })
        {
            var live = ProfileMatch(fixture, terrain, map.Objects, first, second, 3_600);
            var frames = fixture.Templates.Rate.TicksPerOriginalFrame;
            while (live.World.Tick < 900L * frames && !live.IsOver) live.Step();
            var restored = SkirmishMatchSession.Restore(live.Save(), fixture.Templates, fixture.Fingerprint, terrain);
            Check.That(restored.Ais[0]!.Tactics == first.Tactics && restored.Ais[0]!.Strength == first.Strength && restored.Ais[1]!.Strength == second.Strength,
                "The restored session plays the profiles and strengths of the saved one.");
            Check.That(restored.StateHash() == live.StateHash(), $"The restored session has the session hash of the saved one ({first} against {second}).");
            while (!live.IsOver)
            {
                live.Step();
                restored.Step();
                if (restored.StateHash() != live.StateHash()) throw new InvalidOperationException($"The restored session ({first} against {second}) differs after tick {live.World.Tick}.");
            }
        }

        static string Json(SkirmishAi ai)
        {
            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                ai.Describe(new CnCNew.Simulation.State.JsonStateWriter(writer));
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        var plain = Json(none.Ais[0]!);
        var named = Json(ProfileMatch(fixture, terrain, map.Objects, new AiSlotChoice(Tactics: AiTactics.Rush, Strength: AiStrength.Easy), AiSlotChoice.Default, 3_600).Ais[0]!);
        Check.That(!plain.Contains("\"tactics\"", StringComparison.Ordinal) && !plain.Contains("\"strength\"", StringComparison.Ordinal)
                   && named.Contains("\"tactics\":1", StringComparison.Ordinal) && named.Contains("\"strength\":0", StringComparison.Ordinal),
            "The state names a profile and a strength only when they are set, so an AI without them has the state it had before the profiles.");
    }
}
