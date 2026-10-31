using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const int BoxValue = 50;

    private static IReadOnlyDictionary<string, string> SupplyFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/GameData.ini"] = $"GameData\n  ValuePerSupplyBox = {BoxValue}\nEnd\n",
        ["Data/INI/Object/SupplyFixture.ini"] = """
        Object Depot
          KindOf = STRUCTURE COMMANDCENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyCenterDockUpdate Dock01
            NumberApproachPositions = 2
          End
          Behavior = SupplyCenterCreate Create01
          End
        End

        Object Warehouse
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = 1
            StartingBoxes = 5
          End
          Behavior = SupplyWarehouseCreate Create01
          End
        End

        Object Cache
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = 1
            StartingBoxes = 2
            DeleteWhenEmpty = Yes
          End
        End

        Object Hauler
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SupplyTruckAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 100
            SupplyWarehouseActionDelay = 100
            SupplyWarehouseScanDistance = 400.0
          End
        End

        Object Bench
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object PayPost
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoDepositUpdate Pay01
            DepositTiming = 1000
            DepositAmount = 40
            UpgradedBoost = UpgradeType:Upgrade_PlayerWide Boost:25
          End
        End

        Object Drone
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 20.0
            InitialHealth = 20.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object BrokenDrone
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 20.0
            InitialHealth = 20.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpawnBehavior Rail01
            CanReclaimOrphans = Yes
          End
        End

        Object Hive
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnReplaceDelay = 2000
            SpawnTemplateName = Drone
            SpawnedRequireSpawner = Yes
          End
        End

        Object Mint
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 3
            OneShot = Yes
            SpawnTemplateName = Drone
          End
        End

        Object Nest
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnTemplateName = Drone
          End
        End

        Object Swarm
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 4
            OneShot = Yes
            SpawnTemplateName = Drone
            SpawnTemplateName = BrokenDrone
          End
        End

        Object Colony
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnTemplateName = Drone
            CanReclaimOrphans = Yes
          End
        End
        """,
    };

    private static SimulationWorld SupplyWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: SupplyFiles());
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        return world;
    }

    private static List<SimEvent> RunCollecting(SimulationWorld world, long untilTick, SimEventKind kind)
    {
        var events = new List<SimEvent>();
        while (world.Tick < untilTick) events.AddRange(world.Step().Events.Where(value => value.Kind == kind));
        return events;
    }

    /// <summary>
    /// The supply economy end to end: a hauler docks at a five-box warehouse, unloads at the depot and repeats until the warehouse is dry.
    /// The expected payouts are recomputed independently from the rules (capacity 2 per trip, 50 per box): 100, 100, 50; the money ledger
    /// and the deposit events must agree with that and with box conservation, and two runs must be hash-identical.
    /// </summary>
    public static void SupplyEconomyGoldenMaster()
    {
        var expectedTrips = new List<long>();
        for (var remaining = 5; remaining > 0;)
        {
            var load = System.Math.Min(2, remaining);
            expectedTrips.Add(load * BoxValue);
            remaining -= load;
        }
        Check.That(expectedTrips.SequenceEqual([100, 100, 50]), "The independent trip model expects 100, 100, 50.");

        ulong hashA = 0, hashB = 0;
        for (var run = 0; run < 2; run++)
        {
            var world = SupplyWorld(out _, out var feeder);
            feeder.Spawn(1, "Depot", x: -100);
            feeder.Spawn(1, "Warehouse", x: 100);
            feeder.Spawn(1, "Hauler", x: 0);
            world.Step();
            var warehouse = IdOf(world, "Warehouse");
            var hauler = IdOf(world, "Hauler");
            Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.Zero, "No money before the first delivery.");

            Check.That(feeder.SetMoveTarget(1, hauler, 0, 0).Accepted, "A plain move order is accepted (control run precondition).");
            world.Step();
            Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, new EntityId(hauler), new EntityId(warehouse))).Accepted, "The dock order is accepted.");
            var deposits = RunCollecting(world, world.Frames(4000), SimEventKind.SupplyDeposited);

            Check.That(deposits.Select(value => value.Value).SequenceEqual(expectedTrips), "Deposits: " + string.Join(",", deposits.Select(value => value.Value)));
            Check.That(deposits.All(value => value.Entity == new EntityId(hauler) && value.Other == new EntityId(IdOf(world, "Depot"))), "Every deposit names the hauler and the depot.");
            Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(expectedTrips.Sum()), "The ledger equals the sum of the independent trips (250).");
            Check.That(deposits.Zip(deposits.Skip(1)).All(pair => pair.Second.Tick > pair.First.Tick + world.Frames(6)), "Trips are separated by travel and action delays.");

            if (run == 0) hashA = world.StateHash;
            else hashB = world.StateHash;
        }
        Check.That(hashA == hashB, "Two independent runs end in the same state hash.");
    }

    /// <summary>Without any order the hauler stays idle and earns nothing; a fresh hauler forced by the exit module would start at once (covered by the reference match).</summary>
    public static void SupplyIdleHaulerEarnsNothing()
    {
        var world = SupplyWorld(out _, out var feeder);
        feeder.Spawn(1, "Depot", x: -100);
        feeder.Spawn(1, "Warehouse", x: 100);
        feeder.Spawn(1, "Hauler");
        for (var i = 0; i < world.Frames(600); i++) world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.Zero, "An unordered hauler never gathers on its own.");
    }

    /// <summary>The warehouse queue: two haulers with one approach slot take turns; nothing is lost or duplicated; a DeleteWhenEmpty warehouse disappears with its last box.</summary>
    public static void SupplyQueueAndDeleteWhenEmpty()
    {
        var world = SupplyWorld(out _, out var feeder);
        feeder.Spawn(1, "Depot", x: -100);
        feeder.Spawn(1, "Cache", x: 100);
        feeder.Spawn(1, "Hauler", x: 0, y: 10);
        feeder.Spawn(1, "Hauler", x: 0, y: -10);
        world.Step();
        var cache = IdOf(world, "Cache");
        var haulers = world.Entities.Where(value => value.Template.Name == "Hauler").OrderBy(value => value.Id.Value).Select(value => value.Id.Value).ToArray();
        foreach (var id in haulers) Check.That(world.Submit(new DockCommand(new PlayerId(1), 800 + id, world.Tick, new EntityId(id), new EntityId(cache))).Accepted, "Dock order accepted.");
        var deposits = RunCollecting(world, world.Frames(3000), SimEventKind.SupplyDeposited);
        Check.That(deposits.Sum(value => value.Value) == 2 * BoxValue, "The two boxes of the cache pay exactly 2 x 50 in total: " + string.Join(",", deposits.Select(value => value.Value)));
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(2 * BoxValue), "The ledger agrees.");
        Check.That(CountOf(world, "Cache") == 0, "The empty DeleteWhenEmpty warehouse was removed.");
        Check.That(CountOf(world, "Hauler") == 2, "Both haulers survive and end idle.");
    }

    /// <summary>DockCommand validation: unknown entity, foreign owner, non-mover, no dock module.</summary>
    public static void SupplyDockCommandValidation()
    {
        var world = SupplyWorld(out _, out var feeder);
        feeder.Spawn(1, "Depot", x: -100);
        feeder.Spawn(1, "Hauler");
        feeder.Spawn(1, "Bench");
        feeder.Spawn(2, "Hauler");
        world.Step();
        var depot = IdOf(world, "Depot");
        var mine = IdOf(world, "Hauler");
        var theirs = IdOf(world, "Hauler", 1);
        var bench = IdOf(world, "Bench");
        ulong sequence = 700;
        SubmitResult Order(int player, uint actor, uint dock) => world.Submit(new DockCommand(new PlayerId(player), ++sequence, world.Tick, new EntityId(actor), new EntityId(dock)));
        Check.That(!Order(1, 999, depot).Accepted && Order(1, 999, depot).Reason == CommandRejection.UnknownEntity, "An unknown actor is rejected.");
        Check.That(Order(1, mine, 999).Reason == CommandRejection.UnknownEntity, "An unknown dock is rejected.");
        Check.That(Order(1, theirs, depot).Reason == CommandRejection.NotPermitted, "Ordering another player's unit is rejected.");
        Check.That(Order(1, bench, depot).Reason == CommandRejection.CannotMove, "A structure cannot dock.");
        Check.That(Order(1, mine, bench).Reason == CommandRejection.CannotDock, "An object without a dock module is not a dock.");
        Check.That(Order(1, mine, depot).Accepted, "A valid order is accepted.");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotDock] == 1 && world.RejectionCounts[(int)CommandRejection.CannotMove] == 1, "Submission rejections are counted per reason.");
    }

    /// <summary>AutoDepositUpdate pays on its rhythm, adds the first owned upgrade boost, and pays nobody for a neutral object.</summary>
    public static void SupplyAutoDeposit()
    {
        var world = SupplyWorld(out _, out var feeder);
        feeder.Spawn(1, "PayPost");
        world.Step();
        var deposits = RunCollecting(world, world.Frames(30), SimEventKind.SupplyDeposited);
        Check.That(deposits.Count == 0 && world.MoneyOf(new PlayerId(1)) == Fixed.Zero, "Nothing is paid before the first period ends.");
        deposits = RunCollecting(world, world.Frames(30) + 1, SimEventKind.SupplyDeposited);
        Check.That(deposits.Count == 1 && deposits[0].Value == 40 && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(40), "The first period pays 40.");
        deposits = RunCollecting(world, world.Frames(60) + 1, SimEventKind.SupplyDeposited);
        Check.That(deposits.Count == 1 && deposits[0].Value == 40, "The second period pays 40 again.");
        feeder.GiveMoney(1, 500);
        world.Step();
        feeder.GrantUpgrade(1, "Upgrade_PlayerWide");
        world.Step();
        var before = world.MoneyOf(new PlayerId(1));
        deposits = RunCollecting(world, world.Frames(90) + 1, SimEventKind.SupplyDeposited);
        Check.That(deposits.Count == 1 && deposits[0].Value == 65 && world.MoneyOf(new PlayerId(1)) == before + Fixed.FromInt(65), "With the owned upgrade the period pays 40 + 25.");

        var neutralWorld = SupplyWorld(out _, out var neutralFeeder);
        neutralFeeder.Spawn(0, "PayPost", owner: 0);
        for (var i = 0; i < neutralWorld.Frames(130); i++) neutralWorld.Step();
        Check.That(neutralWorld.MoneyOf(new PlayerId(1)) == Fixed.Zero && neutralWorld.MoneyOf(new PlayerId(2)) == Fixed.Zero, "A neutral deposit post pays nobody.");
    }

    /// <summary>A mid-trip snapshot continues hash-identically to the live run, and the DockCommand survives the replay file.</summary>
    public static void SupplySnapshotAndReplay()
    {
        var live = SupplyWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Depot", x: -100);
        feeder.Spawn(1, "Warehouse", x: 100);
        feeder.Spawn(1, "Hauler");
        live.Step();
        var hauler = IdOf(live, "Hauler");
        live.Submit(new DockCommand(new PlayerId(1), 950, live.Tick, new EntityId(hauler), new EntityId(IdOf(live, "Warehouse"))));
        var deposited = false;
        for (var i = 0; i < live.Frames(400) && !deposited; i++)
        {
            deposited = live.Step().Events.Any(value => value.Kind == SimEventKind.SupplyDeposited);
            if (i == 8 || i == 25) SnapshotContinues(live, fixture);
        }
        Check.That(deposited, "The first delivery happened within 400 ticks.");
        SnapshotContinues(live, fixture);

        var command = new DockCommand(new PlayerId(1), 5, 5, new EntityId(3), new EntityId(4));
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 10, 5);
        ImmutableArray<SimCommand> commands = [command];
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, commands, [])));
        Check.That(read.Commands.SequenceEqual(commands), "DockCommand survives the replay file.");
    }

    private static void SnapshotContinues(SimulationWorld live, SyntheticFixture fixture)
    {
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The dock, supply machine and carrier state round-trip at tick " + live.Tick + ".");
        var copy = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        for (var i = 0; i < copy.Frames(300); i++)
            Check.That(copy.Step().StateHash == restored.Step().StateHash, "Two restored copies stay identical (tick " + i + ").");
    }

    /// <summary>SpawnBehavior: initial creation through the exit, replacement after the delay, SpawnedRequireSpawner and the round trip.</summary>
    public static void SpawnBehaviorReplaceAndRequireSpawner()
    {
        var world = SupplyWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Hive", x: 50, y: 50);
        world.Step();
        for (var i = 0; i < world.Frames(40); i++)
        {
            world.Step();
            if (CountOf(world, "Drone") == 2) break;
        }
        Check.That(CountOf(world, "Drone") == 2, "The hive creates its two drones.");
        var drones = world.Entities.Where(value => value.Template.Name == "Drone").OrderBy(value => value.Id.Value).ToArray();
        Check.That(drones.All(value => value.Owner.Value == 1 && value.X == Fixed.FromInt(60) && value.Y == Fixed.FromInt(50)), "Drones leave at the exit's create point with the hive's owner.");

        var doomed = drones[0].Id.Value;
        feeder.Kill(doomed);
        world.Step();
        Check.That(CountOf(world, "Drone") == 1, "Killing a drone leaves one.");
        var deathTick = world.Tick;
        while (world.Tick < deathTick + world.Frames(70) && CountOf(world, "Drone") < 2) world.Step();
        Check.That(CountOf(world, "Drone") == 1, "The replacement is not due before SpawnReplaceDelay (2000 ms = 60 ticks) has passed.");
        while (world.Tick < deathTick + world.Frames(140) && CountOf(world, "Drone") < 2) world.Step();
        Check.That(CountOf(world, "Drone") == 2, "The replacement arrives after the delay (polled at the 15-tick rhythm).");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "Spawner state round-trips.");
        for (var i = 0; i < world.Frames(200); i++) Check.That(world.Step().StateHash == restored.Step().StateHash, "Live and restored spawner agree at step " + i + ".");

        feeder.Kill(IdOf(world, "Hive"));
        for (var i = 0; i < 4; i++) world.Step();
        Check.That(CountOf(world, "Drone") == 0 && CountOf(world, "Hive") == 0, "SpawnedRequireSpawner: the drones die with their hive.");
    }

    /// <summary>OneShot spawners stop after SpawnNumber creations; a missing exit module and a non-instantiable template are reported, never guessed.</summary>
    public static void SpawnBehaviorOneShotAndSkips()
    {
        var world = SupplyWorld(out _, out var feeder);
        feeder.Spawn(1, "Mint", x: 10);
        for (var i = 0; i < world.Frames(200); i++) world.Step();
        Check.That(CountOf(world, "Drone") == 3, "A OneShot spawner creates exactly SpawnNumber objects.");
        foreach (var drone in world.Entities.Where(value => value.Template.Name == "Drone").ToArray()) feeder.Kill(drone.Id.Value);
        for (var i = 0; i < world.Frames(300); i++) world.Step();
        Check.That(CountOf(world, "Drone") == 0, "A OneShot spawner never replaces a dead spawn.");

        var noExit = SupplyWorld(out _, out var noExitFeeder);
        noExitFeeder.Spawn(1, "Nest");
        var skipped = new List<SimEvent>();
        for (var i = 0; i < noExit.Frames(200); i++) skipped.AddRange(noExit.Step().Events.Where(value => value.Kind == SimEventKind.CreationSkipped));
        Check.That(skipped.Count == 1 && skipped[0].Value == CreationSkipReasons.NoExitInterface && skipped[0].Text == "Drone", "Without an exit module the spawner reports NoExitInterface exactly once and goes dormant.");
        Check.That(CountOf(noExit, "Drone") == 0, "Nothing is created without an exit.");

        var broken = SupplyWorld(out _, out var brokenFeeder);
        brokenFeeder.Spawn(1, "Swarm");
        var reasons = new List<long>();
        for (var i = 0; i < broken.Frames(300); i++) reasons.AddRange(broken.Step().Events.Where(value => value.Kind == SimEventKind.CreationSkipped).Select(value => value.Value));
        Check.That(CountOf(broken, "Drone") == 2 && reasons.Count == 2 && reasons.All(value => value == CreationSkipReasons.TemplateNotInstantiable),
            "Two of four slots go to the non-instantiable template: they are reported, consume their slot and are not retried.");
    }

    /// <summary>Unsupported SpawnBehavior configurations keep the object out of the simulation with a named missing module.</summary>
    public static void SpawnBehaviorUnsupportedConfiguration()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SupplyFiles());
        Check.That(fixture.Templates.TryGet("Colony", out var colony) && !colony.IsInstantiable && colony.MissingModules.Contains("SpawnBehavior[CanReclaimOrphans]"), "CanReclaimOrphans is reported as a missing feature.");
        Check.That(fixture.Templates.TryGet("Hive", out var hive) && hive.IsInstantiable && fixture.Templates.TryGet("Hauler", out var hauler) && hauler.IsInstantiable, "Supported spawners and haulers are instantiable.");
    }

    /// <summary>
    /// F-79: a hauler at work in a warehouse's dock whose warehouse is destroyed after the updates of a frame's first tick leaves the dock in the next frame at 30, 60 and 120 Hz. Before,
    /// above 30 Hz its dock machine, which runs every tick while the dock works on it, saw the warehouse gone in the frame's second tick and left a frame early.
    /// </summary>
    public static void HaulerSeesItsDockGoneInTheNextFrame()
    {
        long? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: SupplyFiles());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Warehouse", x: 0);
            feeder.Spawn(1, "Hauler", x: 80);
            world.Step();
            var warehouse = IdOf(world, "Warehouse");
            var hauler = world.FindEntity(new EntityId(IdOf(world, "Hauler")))!;
            var ai = hauler.Modules.OfType<AIUpdateInterface>().Single();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, hauler.Id, new EntityId(warehouse))).Accepted, $"{rate}: the hauler is sent to the warehouse.");
            // Into the dock, then to the end of a tick that began a frame of the original: the warehouse is destroyed after that tick's updates.
            for (var step = 0; step < world.Frames(600) && !ai.IsInsideDock; step++) world.Step();
            Check.That(ai.IsInsideDock, $"{rate}: the hauler works in the dock.");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            world.Step();
            var killed = (world.Tick - 1) / world.Time.TicksPerOriginalFrame;
            world.Kill(new EntityId(warehouse));
            var left = -1L;
            for (var step = 0; step < world.Frames(10) && left < 0; step++)
            {
                world.Step();
                if (!ai.IsDocking) left = (world.Tick - 1) / world.Time.TicksPerOriginalFrame - killed;
            }
            Check.That(left > 0, $"{rate}: the hauler left the dock after the warehouse was destroyed ({left}).");
            if (reference is null) reference = left;
            else Check.That(left == reference, $"{rate}: it leaves in the frame 30 Hz has (frame {left} after the destruction against {reference}).");
        }
    }

    /// <summary>
    /// F-86: a unit on its way to a goal object that dies after the updates of a frame's first tick stops in the next frame at 30, 60 and 120 Hz, at the same place. Before, above 30 Hz it saw the goal dead
    /// in the frame's second tick and stood a step short.
    /// </summary>
    public static void WalkerSeesItsGoalGoneInTheNextFrame()
    {
        Fixed? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: SupplyFiles());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Warehouse", x: 900);
            feeder.Spawn(1, "Hauler", x: 0);
            for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
            var goal = world.FindEntity(new EntityId(IdOf(world, "Warehouse")))!;
            var walker = world.FindEntity(new EntityId(IdOf(world, "Hauler")))!;
            walker.Modules.OfType<AIUpdateInterface>().Single().OrderApproachGoal(world, goal, false);
            for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
            Check.That(walker.HasMoveTarget, $"{rate}: the unit is on its way.");
            // To the end of the tick that begins a frame of the walker: the goal is destroyed after that tick's updates.
            while ((world.Tick - walker.SpawnTick) % world.Time.TicksPerOriginalFrame != 0) world.Step();
            world.Step();
            world.Kill(goal.Id);
            for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
            Check.That(!walker.HasMoveTarget, $"{rate}: the unit has stopped.");
            if (reference is null) reference = walker.X;
            else Check.That(walker.X == reference, $"{rate}: it stands where it stands at 30 Hz ({walker.X} against {reference}).");
        }
    }
}

