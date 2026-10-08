using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    /// <summary>The skirmish fixture's side with a war factory and a tank that costs more than three soldiers.</summary>
    private static string SkirmishSideWithFactory(string side) => SkirmishSide(side).Replace($"2 = Cmd_{side}Barracks", $"2 = Cmd_{side}Barracks\n  3 = Cmd_{side}Factory") + $$"""

        Object {{side}}Factory
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE FS_WARFACTORY
          BuildCost = 80
          BuildTime = 1
          CommandSet = {{side}}FactorySet
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End
        Object {{side}}Tank
          Side = {{side}}
          KindOf = SELECTABLE VEHICLE
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          BuildCost = 90
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SkirmishGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        CommandButton Cmd_{{side}}Factory
          Command = DOZER_CONSTRUCT
          Object = {{side}}Factory
        End
        CommandButton Cmd_{{side}}Tank
          Command = UNIT_BUILD
          Object = {{side}}Tank
        End
        CommandSet {{side}}FactorySet
          1 = Cmd_{{side}}Tank
        End
        """;

    private static IReadOnlyDictionary<string, string> SkirmishFactoryFiles()
    {
        var files = new Dictionary<string, string>(SkirmishFiles());
        files["Data/INI/Object/SkirmishFixture.ini"] = files["Data/INI/Object/SkirmishFixture.ini"].Replace(SkirmishSide("Alpha"), SkirmishSideWithFactory("Alpha")).Replace(SkirmishSide("Beta"), SkirmishSideWithFactory("Beta"));
        return files;
    }

    /// <summary>
    /// The standing army order (observation B of docs/AI-OBSERVED-2026-10-08.md): with an income that trickles in, the AI saves for the unit it drew instead of spending every
    /// credit at the barracks, so the factory builds vehicles. (That an order which cannot be paid for a minute gives way is counted on the real data: `ai-census`, outcome <c>gave-way</c>.)
    /// <para>The far bases and the short budget keep the armies apart: what is counted is production, not a battle.</para>
    /// </summary>
    public static void SkirmishAiSavesForItsArmyOrder()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFactoryFiles());
        var map = SyntheticMaps.Build(200, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 1900m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish-army");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var frames = fixture.Templates.Rate.TicksPerOriginalFrame;
        // No start money beyond the 500 of the fixture's player template: the base takes most of it.
        SkirmishMatchSession Start(uint seed) => SkirmishMatchSession.Start(new SkirmishMatchOptions(0xC0FFEE06, seed, 6_000L * frames, 60, 500, 0, 0), fixture.Templates, fixture.Fingerprint, terrain, map.Objects,
            [.. participants.Select(participant => new SkirmishSlot(participant))]);
        static int Count(SkirmishAi ai, string text) => ai.Stats.ArmyProduction.Where(pair => pair.Key.Contains(text, StringComparison.Ordinal)).Sum(pair => pair.Value);
        static string Report(SkirmishMatchSession session) => string.Join(" | ", session.Ais.Select((ai, index) =>
            $"{session.World.MoneyOf(session.Players[index].Id)} left: " + string.Join(", ", ai!.Stats.ArmyProduction.Select(pair => pair.Key.Replace('\t', ' ') + "=" + pair.Value))));

        // 40 s without income spend the start money; then five credits a second for 100 s: a soldier (25) is covered every five seconds, a tank (90) only by saving.
        var session = Start(7);
        var world = session.World;
        ulong sequence = 1_000_000;
        while (world.Tick < 1_200L * frames) session.Step();
        var tanksBefore = session.Ais.Select(ai => Count(ai!, "Factory\tqueued:")).ToArray();
        var soldiersBefore = session.Ais.Select(ai => Count(ai!, "Barracks\tqueued:")).ToArray();
        for (var index = 0; index < session.Players.Length; index++)
            Check.That(world.MoneyOf(session.Players[index].Id) < Fixed.FromInt(90), $"Before the income starts no player can pay a tank ({Report(session)}).");
        while (world.Tick < 4_200L * frames && !session.IsOver)
        {
            if (world.Tick % (30L * frames) == 0)
                foreach (var player in session.Players)
                    Check.That(session.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, player.Id, Fixed.FromInt(5))).Accepted, "The trickle is credited.");
            session.Step();
        }
        for (var index = 0; index < session.Ais.Length; index++)
        {
            var ai = session.Ais[index]!;
            Check.That(Count(ai, "Factory\tqueued:") > tanksBefore[index], $"With a trickling income the factory builds a tank ({Report(session)}).");
            Check.That(Count(ai, "Barracks\tqueued:") > soldiersBefore[index], $"The barracks still builds soldiers ({Report(session)}).");
            Check.That(Count(ai, "\tsaving:") >= 1, "The AI saved for an order it could not pay yet.");
        }
    }
}
