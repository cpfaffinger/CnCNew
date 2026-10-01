using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    private static string SkirmishSide(string side) => $$"""
        Object {{side}}Center
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE COMMANDCENTER
          VisionRange = 150.0
          ShroudClearingRange = 150.0
          CommandSet = {{side}}CenterSet
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 600.0
            InitialHealth = 600.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = {{side}}Shared
          End
        End
        Object {{side}}Dozer
          Side = {{side}}
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          BuildCost = 20
          BuildTime = 1
          CommandSet = {{side}}DozerSet
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End
        Object {{side}}Soldier
          Side = {{side}}
          KindOf = SELECTABLE INFANTRY
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          BuildCost = 25
          BuildTime = 1
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 80.0
            InitialHealth = 80.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SkirmishGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object {{side}}Plant
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 60
          BuildTime = 1
          EnergyProduction = 5
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object {{side}}Barracks
          Side = {{side}}
          KindOf = SELECTABLE STRUCTURE FS_BARRACKS
          BuildCost = 80
          BuildTime = 1
          CommandSet = {{side}}BarracksSet
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
        CommandButton Cmd_{{side}}Dozer
          Command = UNIT_BUILD
          Object = {{side}}Dozer
        End
        CommandButton Cmd_{{side}}Soldier
          Command = UNIT_BUILD
          Object = {{side}}Soldier
        End
        CommandButton Cmd_{{side}}Plant
          Command = DOZER_CONSTRUCT
          Object = {{side}}Plant
        End
        CommandButton Cmd_{{side}}Barracks
          Command = DOZER_CONSTRUCT
          Object = {{side}}Barracks
        End
        CommandButton Cmd_{{side}}Sell
          Command = SELL
        End
        CommandSet {{side}}CenterSet
          1 = Cmd_{{side}}Dozer
          2 = Cmd_{{side}}Soldier
          3 = Cmd_{{side}}Sell
          4 = Cmd_{{side}}Soldier
        End
        CommandSet {{side}}DozerSet
          1 = Cmd_{{side}}Plant
          2 = Cmd_{{side}}Barracks
        End
        CommandSet {{side}}BarracksSet
          1 = Cmd_{{side}}Soldier
        End
        PlayerTemplate Faction{{side}}
          Side = {{side}}
          PlayableSide = Yes
          StartMoney = 500
          StartingBuilding = {{side}}Center
          StartingUnit0 = {{side}}Dozer
          StartingUnit1 = {{side}}Soldier
        End
        """;

    private static IReadOnlyDictionary<string, string> SkirmishFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/SkirmishFixture.ini"] = $$"""
        Weapon SkirmishGun
          PrimaryDamage = 20
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 400
        End
        Locomotor SkirmishLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        SpecialPower AlphaShared
          ReloadTime = 10000
          SharedSyncedTimer = Yes
        End
        SpecialPower BetaShared
          ReloadTime = 10000
          SharedSyncedTimer = Yes
        End
        {{SkirmishSide("Alpha")}}
        {{SkirmishSide("Beta")}}
        """,
        ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\nEnd\n",
    };

    public static void CommandCatalogBuilds()
    {
        var catalog = new SyntheticFixture(files: SkirmishFiles()).Templates;
        var commands = catalog.Commands;
        Check.That(commands.BuildsOf("AlphaCenterSet").SequenceEqual(["AlphaDozer", "AlphaSoldier"]),
            $"A producer's set lists each UNIT_BUILD object once, in button order, and skips other commands (got {string.Join(", ", commands.BuildsOf("AlphaCenterSet"))}).");
        Check.That(commands.BuildsOf("AlphaDozerSet").SequenceEqual(["AlphaPlant", "AlphaBarracks"]), "DOZER_CONSTRUCT buttons list the structures a builder offers.");
        Check.That(commands.BuildsOf("ALPHABARRACKSSET").Length == 1 && commands.BuildsOf("NoSuchSet").IsEmpty, "Set names are case-insensitive and an unknown set offers nothing.");
        var found = catalog.TryGet("AlphaDozer", out var dozer) & catalog.TryGet("AlphaCenter", out var center);
        Check.That(found, "The fixture defines the templates.");
        Check.That(catalog.OffersBuild(dozer, "AlphaPlant") && catalog.OffersBuild(dozer, "alphabarracks"), "A builder offers what its set constructs.");
        Check.That(!catalog.OffersBuild(dozer, "BetaPlant") && !catalog.OffersBuild(dozer, "AlphaSoldier"), "A builder does not offer foreign structures or units of another set.");
        Check.That(catalog.OffersBuild(center, "AlphaSoldier") && !catalog.OffersBuild(center, "AlphaPlant"), "A producer offers units, not structures.");
    }

    private static (ReplayFile Replay, SkirmishMatchScenario Scenario, TemplateCatalogHolder Holder) SkirmishRecord(long maxTicks, uint aiSeed = 7)
    {
        var fixture = new SyntheticFixture(files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, aiSeed, maxTicks, 60, 500, 500, 0);
        var scenario = new SkirmishMatchScenario(options);
        var replay = scenario.Record(fixture.Templates, fixture.Fingerprint, terrain, map.Objects, participants);
        return (replay, scenario, new TemplateCatalogHolder(fixture, terrain));
    }

    private sealed record TemplateCatalogHolder(SyntheticFixture Fixture, TerrainMap Terrain);

    public static void SkirmishAiPlaysAMatch()
    {
        var (replay, scenario, holder) = SkirmishRecord(12_000);
        var (fixture, terrain) = (holder.Fixture, holder.Terrain);
        Check.That(scenario.Players.Length == 2, "Both playable factions take part.");
        Check.That(scenario.Winner is not null && scenario.VictoryTick > 0,
            $"Two identical-strength AIs on an open map end the match (winner {scenario.Winner}, finals {string.Join("; ", scenario.Finals)}).");
        foreach (var ai in scenario.Ais)
        {
            Check.That(ai.Stats.Constructions >= 2, $"The AI builds structures ({ai.Stats.Constructions}).");
            Check.That(ai.Stats.Productions >= 3, $"The AI produces units ({ai.Stats.Productions}).");
            Check.That(ai.Stats.Commands > 20, $"The AI issues commands ({ai.Stats.Commands}).");
        }

        var withCheckpoints = replay with { Checkpoints = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordCheckpointInterval = 500 }, terrain).Checkpoints };
        var bytes = ReplayFormat.Write(withCheckpoints);
        var read = ReplayFormat.Read(bytes);
        Check.That(ReplayFormat.Write(read).AsSpan().SequenceEqual(bytes), "The recorded match survives the replay file byte for byte.");
        Check.That(read.Commands.Any(value => value is ConstructCommand) && read.Commands.Any(value => value is QueueProductionCommand) && read.Commands.Any(value => value is SetMoveTargetCommand),
            "The replay carries construction, production and movement commands.");

        var first = ReplayRunner.Run(read, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 500 }, terrain);
        Check.That(first.VerifiedCheckpoints == read.Checkpoints.Length, "Every checkpoint of the AI match verifies.");
        var second = ReplayRunner.Run(read, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true }, terrain);
        Check.That(first.TickHashes.AsSpan().SequenceEqual(second.TickHashes) && first.FinalHash == second.FinalHash, "Two fresh runs of the recorded match are hash identical on every tick.");

        var half = read.Header.EndTick / 2;
        var head = ReplayRunner.CreateWorld(read.Header, fixture.Templates, terrain);
        var headRun = ReplayRunner.Run(read, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), fixture.Templates, fixture.Fingerprint, terrain);
        var tail = ReplayRunner.Run(read, restored, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(second.TickHashes), "A snapshot taken mid-match continues hash identically.");

        var again = SkirmishRecord(12_000).Replay;
        Check.That(ReplayFormat.Write(again with { Checkpoints = [] }).AsSpan().SequenceEqual(ReplayFormat.Write(replay with { Checkpoints = [] })), "Recording the same match twice yields the same replay.");
        var other = SkirmishRecord(12_000, aiSeed: 99).Replay;
        Check.That(!ReplayFormat.Write(other with { Checkpoints = [] }).AsSpan().SequenceEqual(ReplayFormat.Write(replay with { Checkpoints = [] })), "The AI seed is an input of the recording.");
    }

    public static void SkirmishAiOnlyUsesWhatItSees()
    {
        // 2 s of game time: the bases are 400 units apart and even a scouting soldier (60 units/s, sight 120) cannot see across that yet.
        var (_, scenario, holder) = SkirmishRecord(60);
        foreach (var ai in scenario.Ais)
            Check.That(ai.KnownEnemyStructures == 0, "Before the armies meet, no enemy structure is known: start positions are hints, not knowledge.");
        Check.That(scenario.Winner is null, "Nobody wins in the first two seconds.");
        Check.That(holder.Terrain.StartPositions.Length == 2, "The synthetic map has two start positions.");
    }

    /// <summary>
    /// A live, incrementally stepped <see cref="SkirmishMatchSession"/> must hash-match a fresh <see cref="ReplayRunner"/> run of its own
    /// <see cref="SkirmishMatchSession.ToReplay"/> recording (docs/SIMULATION-CORE.md §12/§12a): the existing replay tests only ever compared
    /// two fresh <see cref="ReplayRunner"/> runs against each other, never a live session's own <see cref="SimulationWorld.StateHash"/>. Each
    /// starting centre carries a <c>SharedSyncedTimer</c> power (<c>{{side}}Shared</c>) the AI's <c>UsePowers()</c> queries every live tick,
    /// which used to seed the shared ready frame from <see cref="SimulationWorld.Tick"/> at query time and diverge from a replay that never
    /// runs AI code.
    /// </summary>
    public static void SkirmishLiveSessionMatchesItsOwnReplay()
    {
        var fixture = new SyntheticFixture(files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, 7, 12_000, 60, 500, 500, 0);
        var session = SkirmishMatchSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map.Objects, [.. participants.Select(participant => new SkirmishSlot(participant))]);
        while (!session.IsOver) session.Step();
        Check.That(session.Ais.Any(ai => ai!.Stats.PowerUses > 0), "The AI actually used the shared power at least once (otherwise the scenario does not exercise the gap).");

        var liveHash = session.World.StateHash;
        var replay = session.ToReplay();
        var replayResult = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, terrain: terrain);
        Check.That(replayResult.FinalHash == liveHash,
            $"A live session's own StateHash ({liveHash:X16}) matches a fresh ReplayRunner.Run of its own ToReplay() recording ({replayResult.FinalHash:X16}).");
    }

    /// <summary>
    /// Phase 10 decision (docs/SIMULATION-CORE.md §18, see <see cref="SkirmishMatchSession.CreateSave"/>'s doc comment for the full
    /// reasoning): a live skirmish session cannot be saved and resumed as a continuing live game, because <see cref="SkirmishAi"/> holds
    /// state no snapshot carries. The guard fails loudly instead of a routine silently writing a world-only save that would resume with
    /// every AI's memory erased. A finished/partial match's actual save path, replay plus a snapshot checkpoint, needs no AI state and is
    /// already covered by every ai-match/reference-match "snapshot continuation" check.
    /// </summary>
    public static void SkirmishSessionRefusesToSave()
    {
        var fixture = new SyntheticFixture(files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, 7, 500, 60, 500, 500, 0);
        var session = SkirmishMatchSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map.Objects, [.. participants.Select(participant => new SkirmishSlot(participant))]);
        for (var i = 0; i < 5; i++) session.Step();
        Check.Throws<NotSupportedException>(() => session.CreateSave(), "cannot be saved");
    }
}
