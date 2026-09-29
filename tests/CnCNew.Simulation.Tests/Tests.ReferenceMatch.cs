using System.Collections.Generic;
using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using System.Linq;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ReferenceMatchFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ReferenceMatchFixture.ini"] = """
        Weapon MatchGun
          PrimaryDamage = 25
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 500
        End

        Locomotor MatchLocomotor
          Speed = 60.0
          Acceleration = 30.0
          Braking = 30.0
          TurnRate = 180.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object AlphaBase
          Side = Alpha
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End

        Object AlphaSoldier
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          BuildCost = 100
          BuildTime = 2
          Locomotor = SET_NORMAL MatchLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MatchGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        Object BetaBase
          Side = Beta
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End

        Object BetaTank
          Side = Beta
          KindOf = SELECTABLE VEHICLE
          BuildCost = 200
          BuildTime = 3
          Locomotor = SET_NORMAL MatchLocomotor
          Body = ActiveBody Body01
            MaxHealth = 220.0
            InitialHealth = 220.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MatchGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        Object GammaBase
          Side = Alpha
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = RadarUpdate Radar01
          End
        End

        Object NobodyBase
          Side = Nobody
          KindOf = STRUCTURE
        End

        PlayerTemplate FactionAlpha
          Side = Alpha
          PlayableSide = Yes
          StartMoney = 600
          StartingBuilding = AlphaBase
          StartingUnit0 = AlphaSoldier
          StartingUnit1 = AlphaSoldier
        End
        PlayerTemplate FactionBeta
          Side = Beta
          PlayableSide = Yes
          StartMoney = 600
          StartingBuilding = BetaBase
          StartingUnit0 = BetaTank
        End
        PlayerTemplate FactionGamma
          Side = Alpha
          PlayableSide = Yes
          StartMoney = 600
          StartingBuilding = GammaBase
          StartingUnit0 = AlphaSoldier
        End
        PlayerTemplate FactionNobody
          Side = Nobody
          PlayableSide = Yes
          StartingBuilding = NobodyBase
        End
        PlayerTemplate FactionHidden
          Side = Alpha
          StartingBuilding = AlphaBase
        End
        """,
    };

    /// <summary>Scenario participants, stand-in bases, and the match itself: a deterministic replay whose runs, snapshot continuation and file round trip all agree.</summary>
    public static void ReferenceMatch()
    {
        var fixture = new SyntheticFixture(files: ReferenceMatchFiles());
        var catalog = fixture.Templates;
        var participants = ReferenceMatchScenario.Participants(catalog, out var excluded);
        Check.That(participants.Select(value => value.Faction.Name).SequenceEqual(["FactionAlpha", "FactionBeta", "FactionGamma"]),
            $"Only playable factions with a base and an armed unit take part, in ordinal order (got {string.Join(", ", participants.Select(value => value.Faction.Name))}).");
        Check.That(participants[0].Substitution is null && participants[0].BaseBuilding == "AlphaBase", "A faction with an instantiable starting building keeps it.");
        Check.That(participants[2].BaseBuilding == "AlphaBase" && participants[2].Substitution is { } why && why.Contains("RadarUpdate"),
            "A faction whose starting building is not instantiable gets a same-side producer as a recorded stand-in.");
        Check.That(excluded.Length == 1 && excluded[0].Contains("FactionNobody") && excluded[0].Contains("no instantiable armed unit"), "A faction whose side cannot field a unit is listed as excluded, never dropped silently.");
        Check.That(ReferenceMatchScenario.DescribeCoverage(catalog).Any(line => line.Contains("side Alpha: 1/1 armed units")), "The coverage report counts instantiable armed units per side.");

        var options = ReferenceMatchOptions.Standard with { MaxTicks = 60_000, MinTicks = 62_000, CheckpointInterval = 500, ArenaRadius = 400, GraceTicks = 100 };
        var scenario = new ReferenceMatchScenario(options);
        var recorded = scenario.Record(catalog, fixture.Fingerprint);
        Check.That(scenario.Winner is not null && scenario.VictoryTick > 0, $"A three-way match on an obstacle-free map ends with a winner (orders {scenario.Orders}, shots {scenario.Shots}, production {scenario.ProductionOrders}, peak {scenario.PeakAlive}, skipped {scenario.StartObjectsSkipped}, victory {scenario.VictoryTick}).");
        Check.That(recorded.Header.EndTick == 62_000, "The replay is padded with idle ticks up to MinTicks.");
        Check.That(scenario.ProductionOrders > 10 && scenario.Orders > 10 && scenario.Shots > 10, $"The generator plays: {scenario.ProductionOrders} production orders, {scenario.Orders} moves, {scenario.Shots} shots.");
        Check.That(scenario.PeakAlive > participants.Length * 3, $"Production adds units beyond the starting objects (peak {scenario.PeakAlive}).");
        Check.That(recorded.Header.Players.Select(value => value.Faction).SequenceEqual(["FactionAlpha", "FactionBeta", "FactionGamma"]), "The replay header carries the factions.");

        var extra = ImmutableArray.Create<SimCommand>(
            new GiveMoneyCommand(PlayerId.System, 1, 5, new PlayerId(2), Fixed.FromInt(7)),
            new PurchaseUpgradeCommand(new PlayerId(1), 2, 5, "Upgrade_X", null),
            new PurchaseUpgradeCommand(new PlayerId(1), 3, 5, "Upgrade_Y", new EntityId(9)),
            new QueueProductionCommand(new PlayerId(1), 4, 6, new EntityId(3), "AlphaSoldier"),
            new CancelProductionCommand(new PlayerId(1), 5, 6, new EntityId(3), 2),
            new SetMoveTargetCommand(new PlayerId(1), 6, 7, new EntityId(4), Fixed.FromInt(-3), Fixed.FromInt(8)),
            new FireWeaponCommand(new PlayerId(1), 7, 8, new EntityId(4), new EntityId(5)),
            new GiveSciencePointsCommand(PlayerId.System, 8, 9, new PlayerId(1), 3),
            new PurchaseScienceCommand(new PlayerId(1), 9, 9, "SCIENCE_X"),
            new EnterContainerCommand(new PlayerId(1), 10, 10, new EntityId(4), new EntityId(6)),
            new ExitContainerCommand(new PlayerId(1), 11, 11, new EntityId(4)));
        var everyCommand = ReplayFormat.Read(ReplayFormat.Write(recorded with { Commands = extra, Checkpoints = [] }));
        Check.That(everyCommand.Commands.SequenceEqual(extra), "Every economy, production, movement, combat, science and container command survives the replay file.");
        var withCheckpoints = recorded with { Checkpoints = ReplayRunner.Run(recorded, catalog, fixture.Fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval }).Checkpoints };
        var bytes = ReplayFormat.Write(withCheckpoints);
        var replay = ReplayFormat.Read(bytes);
        Check.That(replay.Header.Players.SequenceEqual(recorded.Header.Players) && replay.Commands.Length == recorded.Commands.Length, "The replay file round-trips players, factions and commands.");
        Check.That(ReplayFormat.Write(replay).AsSpan().SequenceEqual(bytes), "Writing a read replay reproduces the file byte for byte.");

        var first = ReplayRunner.Run(replay, catalog, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 1_000 });
        Check.That(first.VerifiedCheckpoints == replay.Checkpoints.Length && first.RejectedCommands == 0, $"Every checkpoint verifies and no recorded command is rejected ({first.RejectedCommands} rejected).");
        var second = ReplayRunner.Run(replay, catalog, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(first.TickHashes.AsSpan().SequenceEqual(second.TickHashes) && first.FinalHash == second.FinalHash, "Two fresh runs are hash identical on every tick.");

        var quarter = replay.Header.EndTick / 4;
        var head = ReplayRunner.CreateWorld(replay.Header, catalog);
        var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = quarter, RecordTickHashes = true });
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), catalog, fixture.Fingerprint);
        var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(second.TickHashes), "A snapshot taken mid-match continues hash identically.");

        var end = ReplayRunner.CreateWorld(replay.Header, catalog);
        ReplayRunner.Run(replay, end);
        Check.That(end.TryGetWinner() == scenario.Winner, "Replaying the recorded commands reaches the same winner.");
        Check.That(scenario.WinnerFaction.Length > 0, "The winner is reported with its faction.");

        var again = new ReferenceMatchScenario(options).Record(catalog, fixture.Fingerprint);
        Check.That(ReplayFormat.Write(again).AsSpan().SequenceEqual(ReplayFormat.Write(recorded)), "Recording the scenario twice yields the same replay.");
    }
}
