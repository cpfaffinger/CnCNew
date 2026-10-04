using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Data.World;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Model conditions of buildings, docks, doors and packing equipment (<c>StructureConditions</c>, <c>StructureConditionTracker</c>; docs/MODEL-CONDITIONS-BUILDINGS.md): the production door cycle on the simulation's
/// clock, the airfield's held-open doors, the checkpoint and enemy-near latches, the supply dock (docker and dock side), and, with retail data, the war factory, the strategy center, the nuke silo, the
/// deployable and hacker packing states, the particle cannon and the tech, tree, collapse and train conditions.
/// </summary>
internal static class StructureConditionTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = "GameData\n  ValuePerSupplyBox = 50\nEnd\n",
        ["Data/INI/Object/StructureFixture.ini"] = """
        Locomotor TestLocomotor
          Speed = 90.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 3600.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object DoorFactory
          KindOf = STRUCTURE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
            NumDoorAnimations = 1
            DoorOpeningTime = 1000
            DoorWaitOpenTime = 2000
            DoorCloseTime = 1000
            ConstructionCompleteDuration = 1000
          End
        End

        Object PopFactory
          KindOf = STRUCTURE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
            NumDoorAnimations = 1
            DoorOpeningTime = 1000
            DoorWaitOpenTime = 0
            DoorCloseTime = 2000
          End
        End

        Object PlainFactory
          KindOf = STRUCTURE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
          End
        End

        Object DoorAirfield
          KindOf = STRUCTURE SELECTABLE FS_AIRFIELD
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ParkingPlaceBehavior Park01
            NumRows = 2
            NumCols = 1
            HasRunways = Yes
            ApproachHeight = 20.0
            HealAmountPerSecond = 60.0
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
            NumDoorAnimations = 2
            DoorOpeningTime = 1000
            DoorWaitOpenTime = 1000
            DoorCloseTime = 1000
          End
        End

        Object Grunt
          KindOf = SELECTABLE INFANTRY
          BuildCost = 10
          BuildTime = 2
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Plane
          KindOf = SELECTABLE AIRCRAFT
          BuildCost = 10
          BuildTime = 2
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Gate
          KindOf = STRUCTURE IMMOBILE
          VisionRange = 80.0
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = CheckpointUpdate Check01
            ScanDelayTime = 1000
          End
        End

        Object Watcher
          KindOf = STRUCTURE IMMOBILE
          VisionRange = 80.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = EnemyNearUpdate Near01
            ScanDelayTime = 100
          End
        End

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

        Locomotor PlaneLoco
          Surfaces = AIR
          TurnRate = 90.0
          Speed = 120.0
          Acceleration = 600.0
          Braking = 600.0
          MinSpeed = 0.0
          PreferredHeight = 60.0
          CloseEnoughDist = 1.0
        End
        Locomotor DroneLoco
          Surfaces = AIR
          Speed = 60.0
          Acceleration = 600.0
          Braking = 600.0
          TurnRate = 360.0
          MinSpeed = 0.0
          PreferredHeight = 20.0
          CloseEnoughDist = 1.0
        End
        Locomotor HoverLoco
          Appearance = HOVER
          Surfaces = GROUND WATER
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 3600.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        SpecialPower UplinkPower
          ReloadTime = 3000
        End

        Object Hovercraft
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL HoverLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Bomb
          KindOf = PROJECTILE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 75.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Bomber
          KindOf = SELECTABLE VEHICLE TRANSPORT AIRCRAFT
          Locomotor = SET_NORMAL PlaneLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DeliverPayloadAIUpdate Deliver01
            MaxAttempts = 1
            DoorDelay = 34
            DropDelay = 100
            DeliveryDistance = 80.0
          End
          Behavior = TransportContain Transport01
            Slots = 10
            AllowInsideKindOf = PROJECTILE
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object UplinkBeam
          KindOf = IMMOBILE
          Draw = W3DLaserDraw ModuleTag_01
            OuterBeamWidth = 40.0
            InnerBeamWidth = 4.0
          End
        End

        Object Cannon
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = UplinkPower
            UpdateModuleStartsAttack = Yes
          End
          Behavior = ParticleUplinkCannonUpdate Beam01
            SpecialPowerTemplate = UplinkPower
            BeginChargeTime = 300
            RaiseAntennaTime = 300
            ReadyDelayTime = 300
            WidthGrowTime = 300
            BeamTravelTime = 200
            TotalFiringTime = 600
            TotalDamagePulses = 6
            DamagePerSecond = 100.0
            DamageType = PARTICLE_BEAM
            DeathType = NORMAL
            DamageRadiusScalar = 2.0
            ParticleBeamLaserName = UplinkBeam
            SwathOfDeathDistance = 0.0
            SwathOfDeathAmplitude = 0.0
            ManualDrivingSpeed = 60.0
            ManualFastDrivingSpeed = 600.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object RepairMaster
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:10.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 1
            SpawnTemplateName = BattleDrone
          End
        End

        Object BattleDrone
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Locomotor = SET_NORMAL DroneLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
            GuardMaxRange = 35
            GuardWanderRange = 35
            RepairRange = 8
            RepairMinAltitude = 18.0
            RepairMaxAltitude = 24.0
            RepairRatePerSecond = 30.0
            RepairWhenBelowHealth% = 60
            RepairMinReadyTime = 100
            RepairMaxReadyTime = 200
            RepairMinWeldTime = 100
            RepairMaxWeldTime = 200
          End
          Behavior = DestroyDie Destroy01
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
            SupplyCenterActionDelay = 300
            SupplyWarehouseActionDelay = 300
            SupplyWarehouseScanDistance = 400.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness("structure");
        harness.Run("Production doors follow the simulation's clock: open when the unit leaves, wait, close", ProductionDoorCycle);
        harness.Run("A second unit keeps the door open; a door that is still closing pops to waiting-open", ProductionDoorReuse);
        harness.Run("Producers without door animations show nothing; the construction-complete phase needs no door", ProducersWithoutDoors);
        harness.Run("Airfield doors open per space and stay open while an aircraft holds the space", AirfieldDoors);
        harness.Run("Checkpoint gate and enemy-near alarm", CheckpointAndEnemyNear);
        harness.Run("Supply dock: DOCKING on both sides, DOCKING_ACTIVE only while the dock works", SupplyDock);
        harness.Run("A bomber's doors: DOOR_1_OPENING while it delivers, DOOR_1_CLOSING after, until the next delivery", BomberDoors);
        harness.Run("Particle uplink cannon: UNPACKING while the antenna rises, DEPLOYED until the beam is gone, PACKING while it decays", UplinkCannon);
        harness.Run("Repair drone: packed at creation, then UNPACKING, FIRING_B and FIRING_C as the arm works, packed again when the repair is over", RepairDrone);
        harness.Run("A hover locomotor shows OVER_WATER over a water area and not on land", HoverOverWater);
        harness.Run("The tracker forgets entities that are gone and survives gaps between samples", TrackerHousekeeping);
        var contentDir = ContentDir(args);
        if (contentDir is not null) harness.Run("Real data: war factory doors and construction complete, retail modules and the audit", () => RealData(contentDir));
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--content-dir") return args[i + 1];
        return null;
    }

    // ------------------------------------------------------------------------------------------------ rig

    /// <summary>A synthetic world with a condition tracker and a few command helpers.</summary>
    private sealed class Rig
    {
        private ulong _sequence;

        public Rig(TerrainMap? terrain = null)
        {
            var fixture = new SyntheticFixture(files: Files());
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
        }

        public SimulationWorld World { get; }
        public StructureConditionTracker Tracker { get; } = new();
        public List<SimEvent> Events { get; } = [];

        public Entity Spawn(string template, int owner, int x, int y)
        {
            World.Submit(new SpawnCommand(PlayerId.System, ++_sequence, World.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero));
            World.Step();
            return World.Entities.Where(entity => entity.Template.Name == template).OrderBy(entity => entity.Id.Value).Last();
        }

        public void GiveMoney(int player, int amount)
        {
            World.Submit(new GiveMoneyCommand(PlayerId.System, ++_sequence, World.Tick, new PlayerId(player), Fixed.FromInt(amount)));
            World.Step();
        }

        public void Queue(Entity producer, string template)
        {
            var result = World.Submit(new QueueProductionCommand(producer.Owner, ++_sequence, World.Tick, producer.Id, template));
            TestHarness.Assert(result.Accepted, $"{template} is queued at {producer.Template.Name}.");
        }

        public ulong Next() => ++_sequence;

        /// <summary>Steps one tick and returns the clock after it with the structure conditions of <paramref name="watched"/> at that sample.</summary>
        public (long Tick, ModelConditionSet Set) Step(Entity watched)
        {
            var result = World.Step();
            foreach (var simEvent in result.Events) Events.Add(simEvent);
            return (World.Tick, Tracker.Update(watched, World));
        }

        /// <summary>The sample tick (the clock after the step) at which the first event of <paramref name="kind"/> was reported.</summary>
        public long SampleOfFirst(SimEventKind kind) => Events.First(simEvent => simEvent.Kind == kind).Tick + 1;
    }

    private static string Describe(IEnumerable<(long Tick, ModelConditionSet Set)> timeline)
        => string.Join(" | ", timeline.Where((item, index) => index == 0 || item.Set != timeline.ElementAt(index - 1).Set).Select(item => $"{item.Tick}: {item.Set}"));

    private static (long First, long Last)? Span(IReadOnlyList<(long Tick, ModelConditionSet Set)> timeline, ModelConditionFlag flag)
    {
        var hits = timeline.Where(item => item.Set.Contains(flag)).Select(item => item.Tick).ToArray();
        return hits.Length == 0 ? null : (hits[0], hits[^1]);
    }

    private static void Near(long expected, long actual, long tolerance, string what)
        => TestHarness.Assert(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected} +/- {tolerance}, got {actual}.");

    // ------------------------------------------------------------------------------------------------ tests

    private static void ProductionDoorCycle()
    {
        var rig = new Rig();
        var factory = rig.Spawn("DoorFactory", 1, 100, 100);
        rig.GiveMoney(1, 1000);
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 10; i++) timeline.Add(rig.Step(factory));
        TestHarness.Assert(timeline.All(item => item.Set == ModelConditionSet.Empty), "An idle factory shows no door and no construction-complete phase.");

        rig.Queue(factory, "Grunt");
        for (var i = 0; i < 260; i++) timeline.Add(rig.Step(factory));
        var completed = rig.SampleOfFirst(SimEventKind.ProductionCompleted);
        var opening = Span(timeline, StructureConditions.DoorOpening[0]);
        var waiting = Span(timeline, StructureConditions.DoorWaitingOpen[0]);
        var closing = Span(timeline, StructureConditions.DoorClosing[0]);
        var complete = Span(timeline, StructureConditions.ConstructionComplete);
        var text = Describe(timeline);
        TestHarness.Assert(opening is not null && waiting is not null && closing is not null && complete is not null, $"The door goes through opening, waiting-open and closing ({text}).");

        // DoorOpeningTime = 30 ticks: the door is fully open when the unit appears.
        Near(30, completed - opening!.Value.First, 2, $"The door starts opening DoorOpeningTime before the unit appears ({text})");
        TestHarness.Assert(timeline.First(item => item.Tick == completed).Set.Contains(StructureConditions.DoorWaitingOpen[0]) || timeline.First(item => item.Tick == completed).Set.Contains(StructureConditions.DoorOpening[0]),
            "The unit appears at the end of the opening.");
        Near(30, opening.Value.Last - opening.Value.First + 1, 2, "Opening lasts DoorOpeningTime");
        Near(60, waiting!.Value.Last - waiting.Value.First + 1, 3, "Waiting open lasts DoorWaitOpenTime after the unit");
        Near(30, closing!.Value.Last - closing.Value.First + 1, 2, "Closing lasts DoorCloseTime");
        TestHarness.Assert(opening.Value.Last < waiting.Value.First && waiting.Value.Last < closing.Value.First, "The phases follow one another without overlap.");
        TestHarness.Assert(timeline.All(item => new[] { StructureConditions.DoorOpening[0], StructureConditions.DoorWaitingOpen[0], StructureConditions.DoorClosing[0] }.Count(item.Set.Contains) <= 1),
            "Exactly one door bit at a time.");
        TestHarness.Assert(timeline[^1].Set == ModelConditionSet.Empty, "The door is closed again at the end.");

        // ConstructionCompleteDuration = 30 ticks from the moment the finished unit is handed to the exit.
        TestHarness.Assert(complete!.Value.First == opening.Value.First, "The construction-complete phase starts with the door.");
        Near(30, complete.Value.Last - complete.Value.First + 1, 2, "Construction complete lasts ConstructionCompleteDuration");
    }

    private static void ProductionDoorReuse()
    {
        // Two units 60 ticks apart: the door waits 60 ticks after the first, so it must stay open for the second and close only after it.
        var rig = new Rig();
        var factory = rig.Spawn("DoorFactory", 1, 100, 100);
        rig.GiveMoney(1, 1000);
        rig.Queue(factory, "Grunt");
        rig.Queue(factory, "Grunt");
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 360; i++) timeline.Add(rig.Step(factory));
        var completions = rig.Events.Where(simEvent => simEvent.Kind == SimEventKind.ProductionCompleted).Select(simEvent => simEvent.Tick + 1).ToArray();
        TestHarness.Assert(completions.Length == 2, $"Both units were produced ({completions.Length}).");
        var closing = Span(timeline, StructureConditions.DoorClosing[0]);
        TestHarness.Assert(closing is not null && closing.Value.First > completions[1], $"The door does not close between the units ({Describe(timeline)}).");
        TestHarness.Assert(timeline.Where(item => item.Tick > completions[0] && item.Tick <= completions[1]).All(item => item.Set.Contains(StructureConditions.DoorWaitingOpen[0])),
            "It stays waiting-open until the second unit has left.");
        Near(60, closing!.Value.First - completions[1], 4, "Closing starts DoorWaitOpenTime after the second unit");

        // A door that is still closing when the next unit is ready pops back to waiting-open (ProductionUpdate::update).
        var pop = new Rig();
        var factory2 = pop.Spawn("PopFactory", 1, 100, 100);
        pop.GiveMoney(1, 1000);
        pop.Queue(factory2, "Grunt");
        pop.Queue(factory2, "Grunt");
        var timeline2 = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 300; i++) timeline2.Add(pop.Step(factory2));
        var completions2 = pop.Events.Where(simEvent => simEvent.Kind == SimEventKind.ProductionCompleted).Select(simEvent => simEvent.Tick + 1).ToArray();
        TestHarness.Assert(completions2.Length == 2, "Both units were produced at the pop factory.");
        var between = timeline2.Where(item => item.Tick > completions2[0] + 2 && item.Tick < completions2[1] - 32).ToArray();
        TestHarness.Assert(between.Any(item => item.Set.Contains(StructureConditions.DoorClosing[0])), $"With no wait the door starts closing after the first unit ({Describe(timeline2)}).");
        var atSecond = timeline2.Where(item => item.Tick >= completions2[1] - 25 && item.Tick <= completions2[1] - 2).ToArray();
        TestHarness.Assert(atSecond.All(item => item.Set.Contains(StructureConditions.DoorWaitingOpen[0]) && !item.Set.Contains(StructureConditions.DoorClosing[0])),
            $"A closing door pops to waiting-open when the second unit is ready ({Describe(timeline2)}).");
        TestHarness.Assert(timeline2.All(item => !item.Set.Contains(StructureConditions.ConstructionComplete)), "ConstructionCompleteDuration absent: no construction-complete phase.");
    }

    private static void ProducersWithoutDoors()
    {
        var rig = new Rig();
        var factory = rig.Spawn("PlainFactory", 1, 100, 100);
        rig.GiveMoney(1, 1000);
        rig.Queue(factory, "Grunt");
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 120; i++) timeline.Add(rig.Step(factory));
        TestHarness.Assert(rig.Events.Any(simEvent => simEvent.Kind == SimEventKind.ProductionCompleted), "The unit was produced.");
        TestHarness.Assert(timeline.All(item => item.Set == ModelConditionSet.Empty), "A producer without door animations shows no door condition.");
        TestHarness.Assert(rig.Tracker.Count == 0, "and keeps no tracker entry.");
    }

    private static void AirfieldDoors()
    {
        var rig = new Rig();
        var airfield = rig.Spawn("DoorAirfield", 1, 200, 200);
        var parking = airfield.Modules.OfType<ParkingPlaceBehavior>().Single();
        TestHarness.Assert(parking.SpaceCount == 2, "The fixture airfield has two spaces.");

        // An aircraft takes space 0 (reserveSpace -> setHoldDoorOpen(door, true)): the door of that space opens and stays open.
        var plane = rig.Spawn("Plane", 1, 400, 400);
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 3; i++) timeline.Add(rig.Step(airfield));
        TestHarness.Assert(timeline.All(item => item.Set == ModelConditionSet.Empty), "An empty airfield has closed doors.");
        TestHarness.Assert(parking.ReserveSpace(rig.World, plane, Fixed.Zero, out _) && parking.OccupantOf(0) == plane.Id, "The aircraft holds space 0.");
        for (var i = 0; i < 150; i++) timeline.Add(rig.Step(airfield));
        var opening = Span(timeline, StructureConditions.DoorOpening[0]);
        var waiting = Span(timeline, StructureConditions.DoorWaitingOpen[0]);
        TestHarness.Assert(opening is not null && waiting is not null && waiting.Value.Last == timeline[^1].Tick, $"Door 1 opens and then stays waiting-open while the space is held ({Describe(timeline)}).");
        Near(30, opening!.Value.Last - opening.Value.First + 1, 2, "Door 1 opens for DoorOpeningTime");
        TestHarness.Assert(timeline.All(item => !item.Set.Contains(StructureConditions.DoorOpening[1]) && !item.Set.Contains(StructureConditions.DoorWaitingOpen[1]) && !item.Set.Contains(StructureConditions.DoorClosing[0])),
            "The other door stays shut and door 1 never closes while held.");

        // The aircraft leaves: the door has been waiting open for much longer than DoorWaitOpenTime, so updateDoors closes it at once (for DoorCloseTime, 30 ticks).
        parking.ReleaseSpace(rig.World, plane);
        var release = rig.World.Tick;
        var after = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 100; i++) after.Add(rig.Step(airfield));
        var closing = Span(after, StructureConditions.DoorClosing[0]);
        TestHarness.Assert(closing is not null, $"Door 1 closes after the aircraft left ({Describe(after)}).");
        Near(1, closing!.Value.First - release, 1, "Closing starts right after the release: the wait time has long elapsed");
        Near(30, closing.Value.Last - closing.Value.First + 1, 2, "Closing lasts DoorCloseTime");
        TestHarness.Assert(after[^1].Set == ModelConditionSet.Empty, "and the door is closed.");

        // Production: a unit made at the airfield opens the door of the first free space before it appears and keeps it open afterwards (the space is reserved).
        var second = new Rig();
        var field = second.Spawn("DoorAirfield", 1, 200, 200);
        second.GiveMoney(1, 100);
        second.Queue(field, "Plane");
        var run = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 200; i++) run.Add(second.Step(field));
        var made = second.SampleOfFirst(SimEventKind.ProductionCompleted);
        var doorOne = Span(run, StructureConditions.DoorOpening[0]);
        TestHarness.Assert(doorOne is not null && run[^1].Set.Contains(StructureConditions.DoorWaitingOpen[0]), $"The produced aircraft's door opens and stays open ({Describe(run)}).");
        Near(30, made - doorOne!.Value.First, 2, "Door 1 starts opening DoorOpeningTime before the aircraft appears");
        TestHarness.Assert(run.All(item => !item.Set.Contains(StructureConditions.DoorOpening[1])), "Only the door of the reserved space opens.");
    }

    private static void CheckpointAndEnemyNear()
    {
        var rig = new Rig();
        var gate = rig.Spawn("Gate", 1, 300, 200);
        var watcher = rig.Spawn("Watcher", 1, 100, 100);
        ModelConditionSet GateAfter(int ticks)
        {
            var set = ModelConditionSet.Empty;
            for (var i = 0; i < ticks; i++)
            {
                rig.World.Step();
                set = rig.Tracker.Update(gate, rig.World);
            }
            return set;
        }

        TestHarness.Assert(GateAfter(5) == ModelConditionSet.Empty, "Nobody near: the gate shows neither door bit (CheckpointUpdate sets one only on a change).");
        rig.Spawn("Grunt", 1, 330, 200);
        var open = GateAfter(40);
        TestHarness.Assert(open == ModelConditionSet.Of(StructureConditions.DoorOpening[0]), $"An ally arrives: DOOR_1_OPENING ({open}).");
        var enemy = rig.Spawn("Grunt", 2, 340, 210);
        var closed = GateAfter(40);
        TestHarness.Assert(closed == ModelConditionSet.Of(StructureConditions.DoorClosing[0]), $"An enemy arrives: DOOR_1_CLOSING and nothing else ({closed}).");

        // EnemyNearUpdate -> ENEMYNEAR, through the stateless derivation.
        var visual = ConditionDeriver.Derive(watcher, rig.World, null, default);
        TestHarness.Assert(!visual.Contains(StructureConditions.EnemyNear), "No enemy within the watcher's vision range.");
        rig.Spawn("Grunt", 2, 130, 100);
        for (var i = 0; i < 60; i++) rig.World.Step();
        TestHarness.Assert(ConditionDeriver.Derive(watcher, rig.World, null, default).Contains(StructureConditions.EnemyNear), "An enemy within the vision range raises ENEMYNEAR.");
        TestHarness.Assert(enemy.Id.IsValid, "(the far enemy at the gate is irrelevant to the watcher)");
    }

    private static void SupplyDock()
    {
        var rig = new Rig();
        var depot = rig.Spawn("Depot", 1, 100, 100);
        var warehouse = rig.Spawn("Warehouse", 0, 300, 100);
        var hauler = rig.Spawn("Hauler", 1, 200, 100);
        var samples = new List<(long Tick, ModelConditionSet Truck, ModelConditionSet Warehouse, ModelConditionSet Depot)>();
        rig.World.Submit(new DockCommand(new PlayerId(1), rig.Next(), rig.World.Tick, hauler.Id, warehouse.Id));
        for (var i = 0; i < 1500; i++)
        {
            rig.World.Step();
            samples.Add((rig.World.Tick, ConditionDeriver.Derive(hauler, rig.World, null, default), ConditionDeriver.Derive(warehouse, rig.World, null, default), ConditionDeriver.Derive(depot, rig.World, null, default)));
        }

        long[] Ticks(Func<(long Tick, ModelConditionSet Truck, ModelConditionSet Warehouse, ModelConditionSet Depot), bool> filter) => [..samples.Where(filter).Select(sample => sample.Tick)];
        var truckActive = Ticks(sample => sample.Truck.Contains(StructureConditions.DockingActive));
        var warehouseActive = Ticks(sample => sample.Warehouse.Contains(StructureConditions.DockingActive));
        var depotActive = Ticks(sample => sample.Depot.Contains(StructureConditions.DockingActive));
        TestHarness.Assert(warehouseActive.Length > 0 && depotActive.Length > 0, $"The truck loads at the warehouse and unloads at the depot ({warehouseActive.Length} and {depotActive.Length} ticks of DOCKING_ACTIVE).");
        TestHarness.Assert(truckActive.SequenceEqual(warehouseActive.Concat(depotActive).OrderBy(tick => tick)), "The truck shows DOCKING_ACTIVE exactly while one of the docks does, and each dock exactly while its docker is worked on.");
        var truckInside = Ticks(sample => sample.Truck.Contains(ModelConditions.Docking));
        var dockInside = Ticks(sample => sample.Warehouse.Contains(ModelConditions.Docking) || sample.Depot.Contains(ModelConditions.Docking));
        TestHarness.Assert(truckInside.Length > 0 && truckInside.SequenceEqual(dockInside), "DOCKING is set on the docker and the dock over the same span (onEnterReached .. onExitReached).");
        TestHarness.Assert(truckActive.All(truckInside.Contains), "DOCKING_ACTIVE only inside DOCKING.");
        TestHarness.Assert(warehouseActive.All(tick => samples.First(sample => sample.Tick == tick).Warehouse.Contains(ModelConditions.Docking)), "and on the dock too.");
        TestHarness.Assert(!samples.Any(sample => sample.Truck.Contains(ModelConditions.Parse("DOCKING_BEGINNING")) || sample.Depot.Contains(ModelConditions.Parse("DOCKING_BEGINNING"))),
            "DOCKING_BEGINNING is never set: entry, dock and exit points coincide, so it would last no tick.");
    }

    private static void BomberDoors()
    {
        var rig = new Rig();
        var bomber = rig.Spawn("Bomber", 1, 0, 0);
        var bomb = rig.Spawn("Bomb", 1, 0, 0);
        TestHarness.Assert(rig.World.TryEnterContainer(bomb, bomber), "The bomb is loaded into the plane.");
        bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single().DeliverViaModuleData(rig.World, Fixed.FromInt(400), Fixed.Zero);
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        for (var i = 0; i < 400 && !bomber.DestroyRequested; i++) timeline.Add(rig.Step(bomber));
        var text = Describe(timeline);
        var opening = Span(timeline, StructureConditions.DoorOpening[0]);
        var closing = Span(timeline, StructureConditions.DoorClosing[0]);
        TestHarness.Assert(opening is not null && closing is not null, $"The bomber opens its doors to deliver and closes them afterwards ({text}).");
        TestHarness.Assert(timeline.TakeWhile(item => item.Tick < opening!.Value.First).All(item => item.Set == ModelConditionSet.Empty), "Until it reaches the target its doors show nothing.");
        TestHarness.Assert(opening!.Value.Last < closing!.Value.First, "Opening precedes closing.");
        TestHarness.Assert(timeline.Where(item => item.Tick > closing.Value.First).All(item => !item.Set.Contains(StructureConditions.DoorOpening[0])), "DOOR_1_CLOSING stays until the plane is gone.");
        TestHarness.Assert(closing.Value.First == opening.Value.Last + 1, $"DOOR_1_CLOSING follows the last tick of DOOR_1_OPENING at once ({text}).");
    }

    private static void UplinkCannon()
    {
        var rig = new Rig();
        var cannon = rig.Spawn("Cannon", 1, 100, 100);
        var module = cannon.Modules.OfType<ParticleUplinkCannonUpdate>().Single();
        var timeline = new List<(long Tick, ParticleUplinkCannonUpdate.UplinkStatus Status, ModelConditionSet Set)>();
        void Sample(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                rig.World.Step();
                timeline.Add((rig.World.Tick, module.StatusAt(rig.World), ConditionDeriver.Derive(cannon, rig.World, null, default)));
            }
        }

        Sample(200);
        TestHarness.Assert(module.StatusAt(rig.World) == ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire, $"The cannon became ready ({module.StatusAt(rig.World)}).");
        var statuses = timeline.Select(item => item.Status).Distinct().ToArray();
        TestHarness.Assert(statuses.SequenceEqual([ParticleUplinkCannonUpdate.UplinkStatus.Idle, ParticleUplinkCannonUpdate.UplinkStatus.Charging, ParticleUplinkCannonUpdate.UplinkStatus.Preparing,
            ParticleUplinkCannonUpdate.UplinkStatus.AlmostReady, ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire]), $"The stages follow the clock ({string.Join(" > ", statuses)}).");
        foreach (var item in timeline)
        {
            ModelConditionFlag? expect = item.Status switch
            {
                ParticleUplinkCannonUpdate.UplinkStatus.Preparing => StructureConditions.Unpacking,
                ParticleUplinkCannonUpdate.UplinkStatus.AlmostReady or ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire => ModelConditions.Deployed,
                _ => null,
            };
            var flags = new[] { StructureConditions.Unpacking, StructureConditions.Packing, ModelConditions.Deployed }.Where(item.Set.Contains).ToArray();
            TestHarness.Assert(expect is null ? flags.Length == 0 : flags.Length == 1 && flags[0] == expect.Value, $"Tick {item.Tick} ({item.Status}): {string.Join(' ', flags.Select(flag => flag.Name))}.");
        }
        Near(9, timeline.Count(item => item.Status == ParticleUplinkCannonUpdate.UplinkStatus.Preparing), 1, "The antenna rises for RaiseAntennaTime");

        // Fire: the beam shows DEPLOYED while it burns and decays, PACKING while it dies away, and everything is cleared afterwards.
        TestHarness.Assert(rig.World.Submit(new UseSpecialPowerCommand(new PlayerId(1), rig.Next(), rig.World.Tick, cannon.Id, "UplinkPower", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(300), Fixed.FromInt(300))).Accepted,
            "The cannon accepts the order.");
        timeline.Clear();
        Sample(80);
        var fired = timeline.Select(item => item.Status).Distinct().ToArray();
        TestHarness.Assert(fired.Take(4).SequenceEqual([ParticleUplinkCannonUpdate.UplinkStatus.Firing, ParticleUplinkCannonUpdate.UplinkStatus.PostFire, ParticleUplinkCannonUpdate.UplinkStatus.Packing, ParticleUplinkCannonUpdate.UplinkStatus.Idle]),
            $"Firing, post-fire, packing, idle ({string.Join(" > ", fired)}).");
        TestHarness.Assert(timeline.Where(item => item.Status is ParticleUplinkCannonUpdate.UplinkStatus.Firing or ParticleUplinkCannonUpdate.UplinkStatus.PostFire).All(item => item.Set.Contains(ModelConditions.Deployed) && !item.Set.Contains(StructureConditions.Packing)),
            "DEPLOYED while firing and decaying.");
        TestHarness.Assert(timeline.Where(item => item.Status == ParticleUplinkCannonUpdate.UplinkStatus.Packing).All(item => item.Set.Contains(StructureConditions.Packing) && !item.Set.Contains(ModelConditions.Deployed)),
            "PACKING while the beam dies away.");
        TestHarness.Assert(timeline.Where(item => item.Status == ParticleUplinkCannonUpdate.UplinkStatus.Idle).All(item => !item.Set.Contains(StructureConditions.Packing) && !item.Set.Contains(ModelConditions.Deployed)),
            "Idle clears them.");
    }

    private static void RepairDrone()
    {
        var rig = new Rig();
        var master = rig.Spawn("RepairMaster", 1, 100, 100);
        for (var i = 0; i < 20; i++) rig.World.Step();
        var drone = rig.World.Entities.Single(entity => entity.Template.Name == "BattleDrone");
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        void Sample(int ticks)
        {
            for (var i = 0; i < ticks; i++) timeline.Add(rig.Step(drone));
        }

        Sample(5);
        TestHarness.Assert(timeline.All(item => item.Set == ModelConditionSet.Of(StructureConditions.Packing)), "A drone that can repair is packed up when it is created.");
        rig.World.Submit(new DamageCommand(PlayerId.System, rig.Next(), rig.World.Tick, master.Id, Fixed.FromInt(70), 0, 0));
        Sample(2400);
        var text = Describe(timeline);
        var unpacking = Span(timeline, StructureConditions.Unpacking);
        TestHarness.Assert(unpacking is not null, $"The first repair unpacks the arm ({text}).");
        TestHarness.Assert(timeline.All(item => !(item.Set.Contains(StructureConditions.Packing) && item.Set.Contains(StructureConditions.Unpacking))), "Never both at once.");
        TestHarness.Assert(unpacking!.Value.Last - unpacking.Value.First >= 14, "UNPACKING lasts at least the 15 ticks of the unpack and stays through the ready state.");
        var arm = new[] { StructureConditions.Packing, StructureConditions.Unpacking, ModelConditions.Firing[1], ModelConditions.Firing[2] };
        TestHarness.Assert(timeline.All(item => arm.Count(item.Set.Contains) <= 1), $"One arm bit at a time ({text}).");
        var extending = Span(timeline, ModelConditions.Firing[1]);
        var retracting = Span(timeline, ModelConditions.Firing[2]);
        TestHarness.Assert(extending is not null && retracting is not null && unpacking.Value.First < extending.Value.First && extending.Value.First < retracting.Value.First,
            $"The arm unpacks, extends (FIRING_B, through the weld) and retracts (FIRING_C) in this order ({text}).");
        TestHarness.Assert(master.Body!.Health == master.Body.MaxHealth && timeline[^1].Set == ModelConditionSet.Of(StructureConditions.Packing), $"The repair is over and the arm is packed again ({timeline[^1].Set}).");
    }

    private static void HoverOverWater()
    {
        var lake = new MapPolygonTrigger("Lake", "", 1, true, false, 0, [new(0, 0, 5), new(100, 0, 5), new(100, 100, 5), new(0, 100, 5)]);
        var rig = new Rig(TerrainMap.Build(SyntheticTerrain.Map(22, 22, 1, (_, _) => 0, triggers: [lake]), "lake"));
        var wet = rig.Spawn("Hovercraft", 1, 50, 50);
        var dry = rig.Spawn("Hovercraft", 1, 150, 50);
        var grunt = rig.Spawn("Grunt", 1, 50, 60);
        rig.World.Step();
        TestHarness.Assert(wet.Template.Locomotor is { IsHover: true }, "The hover locomotor is recognised from its Appearance.");
        TestHarness.Assert(ConditionDeriver.Derive(wet, rig.World, null, default).Contains(StructureConditions.OverWater), "A hovercraft on the lake shows OVER_WATER.");
        TestHarness.Assert(!ConditionDeriver.Derive(dry, rig.World, null, default).Contains(StructureConditions.OverWater), "and not on the shore.");
        TestHarness.Assert(!ConditionDeriver.Derive(grunt, rig.World, null, default).Contains(StructureConditions.OverWater), "A foot soldier in the lake does not (only hover locomotors set it).");
        rig.World.Submit(new SetMoveTargetCommand(new PlayerId(1), rig.Next(), rig.World.Tick, wet.Id, Fixed.FromInt(150), Fixed.FromInt(50)));
        for (var i = 0; i < 200; i++) rig.World.Step();
        TestHarness.Assert(wet.X > Fixed.FromInt(120) && !ConditionDeriver.Derive(wet, rig.World, null, default).Contains(StructureConditions.OverWater), "Once it has driven out of the lake the bit is gone.");
    }

    private static void TrackerHousekeeping()
    {
        var rig = new Rig();
        var factory = rig.Spawn("DoorFactory", 1, 100, 100);
        rig.GiveMoney(1, 1000);
        rig.Queue(factory, "Grunt");

        // Samples only every 7th tick (an object out of sight): the transitions still happen at the same ticks, because the stamps are chained.
        var dense = new Rig();
        var denseFactory = dense.Spawn("DoorFactory", 1, 100, 100);
        dense.GiveMoney(1, 1000);
        dense.Queue(denseFactory, "Grunt");
        var denseWaiting = new List<long>();
        var sparseWaiting = new List<long>();
        var sparseLast = ModelConditionSet.Empty;
        for (var i = 0; i < 260; i++)
        {
            var (denseTick, denseSet) = dense.Step(denseFactory);
            if (denseSet.Contains(StructureConditions.DoorWaitingOpen[0])) denseWaiting.Add(denseTick);
            rig.World.Step();
            if (i % 7 == 6)
            {
                sparseLast = rig.Tracker.Update(factory, rig.World);
                if (sparseLast.Contains(StructureConditions.DoorWaitingOpen[0])) sparseWaiting.Add(rig.World.Tick);
            }
        }
        TestHarness.Assert(denseWaiting.Count > 0 && sparseWaiting.Count > 0, "Both rigs saw the waiting-open phase.");
        TestHarness.Assert(sparseWaiting.All(denseWaiting.Contains), "Every sparse sample agrees with the dense timeline.");
        TestHarness.Assert(sparseLast == ModelConditionSet.Empty, "and the door ends closed.");

        rig.Tracker.Prune(_ => false);
        TestHarness.Assert(rig.Tracker.Count == 0, "Prune drops entries of entities that no longer exist.");
        TestHarness.Throws<ArgumentNullException>(() => rig.Tracker.Update(null!, rig.World), "null entity");
    }

    // ------------------------------------------------------------------------------------------------ real data

    private static void RealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", "FactionAmerica"), new PlayerConfiguration(new PlayerId(2), "West", "FactionChina"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, players, "presentation-structures", fingerprint), templates);
        var tracker = new StructureConditionTracker();
        var sequence = 0UL;
        Entity Spawn(string template, int owner, int x, int y)
        {
            Check(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            world.Step();
            return world.Entities.Where(entity => entity.Template.Name == template).OrderBy(entity => entity.Id.Value).Last();
        }

        // The retail war factory: ProductionUpdate NumDoorAnimations = 1 with DoorOpeningTime 3250, DoorWaitOpenTime 3000, DoorCloseTime 4000, ConstructionCompleteDuration 1500 (milliseconds).
        var factory = Spawn("AmericaWarFactory", 1, 1000, 1000);
        var data = factory.Modules.OfType<ProductionUpdate>().Single().Data;
        Check(data is { NumDoorAnimations: 1, DoorOpeningTicks: 98, DoorWaitOpenTicks: 90, DoorCloseTicks: 120, ConstructionCompleteTicks: 45 }, $"The war factory's door timing is typed from the INI ({data}).");
        world.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), Fixed.FromInt(20000)));
        world.Step();
        Check(world.Submit(new QueueProductionCommand(new PlayerId(1), ++sequence, world.Tick, factory.Id, "AmericaTankCrusader")).Accepted, "The Crusader is queued at the war factory.");
        var timeline = new List<(long Tick, ModelConditionSet Set)>();
        var completed = -1L;
        for (var i = 0; i < 1200 && (completed < 0 || timeline[^1].Set != ModelConditionSet.Empty); i++)
        {
            var result = world.Step();
            if (completed < 0 && result.Events.Any(simEvent => simEvent.Kind == SimEventKind.ProductionCompleted)) completed = world.Tick;
            timeline.Add((world.Tick, tracker.Update(factory, world)));
        }
        var text = Describe(timeline);
        var opening = Span(timeline, StructureConditions.DoorOpening[0]);
        var waiting = Span(timeline, StructureConditions.DoorWaitingOpen[0]);
        var closing = Span(timeline, StructureConditions.DoorClosing[0]);
        Check(completed > 0 && opening is not null && waiting is not null && closing is not null, $"The war factory shows DOOR_1_OPENING, DOOR_1_WAITING_OPEN, DOOR_1_CLOSING in turn ({text}).");
        Near(98, completed - opening!.Value.First, 2, "The door starts opening 98 ticks before the Crusader appears");
        Near(90, waiting!.Value.Last - waiting.Value.First + 1, 3, "The door waits open 90 ticks");
        Near(120, closing!.Value.Last - closing.Value.First + 1, 2, "The door closes for 120 ticks");
        Check(Span(timeline, StructureConditions.ConstructionComplete) is { } complete && complete.First == opening.Value.First, "CONSTRUCTION_COMPLETE starts with the door.");
        Console.WriteLine($"       real data: AmericaWarFactory door timeline {text}");

        // The modules of the other retail names answer with their states.
        RealModules(world, tracker, Spawn, ref sequence);
        RealSweep(graph, templates, fingerprint, players);
    }

    private static void RealModules(SimulationWorld world, StructureConditionTracker tracker, Func<string, int, int, int, Entity> spawn, ref ulong sequence)
    {
        // Strategy center: one door per plan, OPENING while it unpacks, WAITING_TO_CLOSE while the plan runs, CLOSING while it packs.
        var center = spawn("AmericaStrategyCenter", 1, 2000, 1000);
        var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
        Check(ConditionDeriver.Derive(center, world, null, default).Flags().All(flag => !flag.Name.StartsWith("DOOR_", StringComparison.Ordinal)), "An idle strategy center has its doors closed.");
        plan.SelectPlan(BattlePlanKind.HoldTheLine);
        var seen = new List<string>();
        for (var i = 0; i < 1200 && plan.Status != BattlePlanUpdate.TransitionStatus.Active; i++)
        {
            world.Step();
            var doors = string.Join(' ', ConditionDeriver.Derive(center, world, null, default).Flags().Where(flag => flag.Name.StartsWith("DOOR_", StringComparison.Ordinal)).Select(flag => flag.Name));
            if (seen.Count == 0 || seen[^1] != doors) seen.Add(doors);
        }
        Check(plan.Status == BattlePlanUpdate.TransitionStatus.Active && seen.Contains("DOOR_2_OPENING"), $"Hold the line opens door 2 ({string.Join(" > ", seen)}).");
        world.Step();
        Check(ConditionDeriver.Derive(center, world, null, default).Contains(StructureConditions.DoorWaitingToClose[1]), "The running plan shows DOOR_2_WAITING_TO_CLOSE.");
        plan.SelectPlan(BattlePlanKind.SearchAndDestroy);
        var packed = false;
        for (var i = 0; i < 1200 && !packed; i++)
        {
            world.Step();
            packed = ConditionDeriver.Derive(center, world, null, default).Contains(StructureConditions.DoorClosing[1]);
        }
        Check(packed, "Choosing another plan packs the first one: DOOR_2_CLOSING.");

        // Nuclear missile silo: the door follows the power's recharge.
        var silo = spawn("ChinaNuclearMissileLauncher", 2, 3000, 1000);
        var door = silo.Modules.OfType<MissileLauncherBuildingUpdate>().Single();
        var states = new SortedSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 12000 && door.Door != MissileLauncherBuildingUpdate.DoorState.Open; i++)
        {
            world.Step();
            var names = ConditionDeriver.Derive(silo, world, null, default).Flags().Where(flag => flag.Name.StartsWith("DOOR_1_", StringComparison.Ordinal)).Select(flag => flag.Name).ToArray();
            Check(names.Length <= 1, $"One door bit at a time ({string.Join(' ', names)}).");
            foreach (var name in names) states.Add(name);
        }
        Check(door.Door == MissileLauncherBuildingUpdate.DoorState.Open && states.Contains("DOOR_1_OPENING"), $"The silo door opens before the missile is ready ({string.Join(' ', states)}).");
        Check(ConditionDeriver.Derive(silo, world, null, default).Contains(StructureConditions.DoorWaitingOpen[0]), "and waits open with DOOR_1_WAITING_OPEN.");

        // Deployable: the Nuke Cannon's unpack and pack states.
        var cannon = spawn("ChinaVehicleNukeLauncher", 2, 3500, 1000);
        var deploy = cannon.Modules.OfType<DeployStyleAIUpdate>().SingleOrDefault();
        Check(deploy is not null, "The Nuke Cannon has a DeployStyleAIUpdate.");
        var packedConditions = ConditionDeriver.Derive(cannon, world, null, default);
        Check(!packedConditions.Contains(StructureConditions.Packing) && !packedConditions.Contains(StructureConditions.Unpacking), "A packed cannon shows neither PACKING nor UNPACKING.");
        var target = spawn("AmericaTankCrusader", 1, 3500 + 200, 1000);
        world.Submit(new FireWeaponCommand(new PlayerId(2), ++sequence, world.Tick, cannon.Id, target.Id));
        var unpacked = false;
        for (var i = 0; i < 400 && !unpacked; i++)
        {
            world.Step();
            unpacked = ConditionDeriver.Derive(cannon, world, null, default).Contains(StructureConditions.Unpacking);
            if (!unpacked && deploy!.IsDeployed) break;
        }
        Check(unpacked, "A fire order unpacks the cannon: UNPACKING while it deploys.");

        // Tech building: CAPTURED while a playable side owns it.
        var derrick = spawn("TechOilDerrick", 0, 4000, 1000);
        Check(!ConditionDeriver.Derive(derrick, world, null, default).Contains(StructureConditions.Captured), "A neutral oil derrick is not captured.");
        world.TransferOwnership(derrick.Id, new PlayerId(1));
        Check(ConditionDeriver.Derive(derrick, world, null, default).Contains(StructureConditions.Captured), "Once a player owns it, it shows CAPTURED.");
        world.TransferOwnership(derrick.Id, PlayerId.System);
        Check(!ConditionDeriver.Derive(derrick, world, null, default).Contains(StructureConditions.Captured), "and not any more when it goes back to the neutral player.");

        // Burned tree: SMOLDERING follows the BURNED status.
        var tree = spawn("GenericTree", 0, 4500, 1000);
        Check(!ConditionDeriver.Derive(tree, world, null, default).Contains(StructureConditions.Smoldering), "An intact tree does not smolder.");
        world.SetStatus(tree.Id, 1UL << ObjectStatusBits.Index("BURNED"));
        Check(ConditionDeriver.Derive(tree, world, null, default).Contains(StructureConditions.Smoldering), "A burned tree smolders.");

        // A civilian highrise collapses: RUBBLE is replaced by POST_COLLAPSE once it is down.
        var bank = spawn("AsianBank", 0, 5000, 1000);
        var collapse = bank.Modules.OfType<StructureCollapseUpdate>().Single();
        Check(!ConditionDeriver.Derive(bank, world, null, default).Contains(StructureConditions.PostCollapse), "A standing building is not collapsed.");
        world.Kill(bank.Id);
        for (var i = 0; i < 3000 && !collapse.IsCollapsed; i++) world.Step();
        var down = ConditionDeriver.Derive(bank, world, null, default);
        Check(collapse.IsCollapsed && down.Contains(StructureConditions.PostCollapse) && !down.Contains(ModelConditions.Rubble), $"The collapsed bank shows POST_COLLAPSE instead of RUBBLE ({down}).");

        // A tower topples: the same replacement once it lies flat.
        var tower = spawn("TallTower02", 0, 5500, 1000);
        var topple = tower.Modules.OfType<StructureToppleUpdate>().Single();
        world.Kill(tower.Id);
        world.Step();
        Check(!ConditionDeriver.Derive(tower, world, null, default).Contains(StructureConditions.PostCollapse), "A tower that is still toppling is not POST_COLLAPSE.");
        for (var i = 0; i < 600 && !topple.IsToppled; i++) world.Step();
        var flat = ConditionDeriver.Derive(tower, world, null, default);
        Check(topple.IsToppled && flat.Contains(StructureConditions.PostCollapse) && !flat.Contains(ModelConditions.Rubble), $"The toppled tower shows POST_COLLAPSE instead of RUBBLE ({flat}).");

        // A hacker: UNPACKING while it sets up, FIRING_A while it hacks, PACKING when it is told to move on.
        var hacker = spawn("ChinaInfantryHacker", 2, 6000, 1000);
        var hack = hacker.Modules.OfType<HackInternetAIUpdate>().Single();
        hack.StartHacking(world);
        var phases = new List<string>();
        void RecordHacker(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                world.Step();
                var set = ConditionDeriver.Derive(hacker, world, null, default);
                var phase = set.Contains(StructureConditions.Unpacking) ? "UNPACKING" : set.Contains(StructureConditions.Packing) ? "PACKING" : set.Contains(ModelConditions.FiringA) ? "FIRING_A" : "-";
                if (phases.Count == 0 || phases[^1] != phase) phases.Add(phase);
            }
        }

        RecordHacker(400);
        world.Submit(new SetMoveTargetCommand(new PlayerId(2), ++sequence, world.Tick, hacker.Id, Fixed.FromInt(6300), Fixed.FromInt(1000)));
        RecordHacker(400);
        Check(string.Join(">", phases).StartsWith("UNPACKING>FIRING_A>PACKING", StringComparison.Ordinal), $"The hacker unpacks, hacks, then packs ({string.Join(">", phases)}).");

        // A shot-down Comanche lies on the ground with its blades off: SPECIAL_DAMAGED.
        var comanche = spawn("AmericaVehicleComanche", 1, 6500, 1000);
        var wreck = comanche.Modules.OfType<HelicopterSlowDeathBehavior>().FirstOrDefault();
        if (wreck is not null)
        {
            world.Kill(comanche.Id);
            for (var i = 0; i < 600 && !wreck.HasHitGround; i++) world.Step();
            Check(wreck.HasHitGround && ConditionDeriver.Derive(comanche, world, null, default).Contains(StructureConditions.SpecialDamaged), "A downed helicopter shows SPECIAL_DAMAGED once it hit the ground.");
        }

        // A train more than 3 below the ground is in a tunnel.
        var engine = spawn("TrainEngine", 0, 7000, 1000);
        Check(!ConditionDeriver.Derive(engine, world, null, default).Contains(StructureConditions.OverWater), "A train on the ground is not in a tunnel.");
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "TrainEngine", PlayerId.System, Fixed.FromInt(7200), Fixed.FromInt(1000), Fixed.FromInt(-5), Fixed.Zero));
        world.Step();
        var buried = world.Entities.Where(entity => entity.Template.Name == "TrainEngine").OrderBy(entity => entity.Id.Value).Last();
        Check(buried.Z == Fixed.FromInt(-5) && ConditionDeriver.Derive(buried, world, null, default).Contains(StructureConditions.OverWater), $"Below the ground (OVER_WATER is the tunnel flag of a train; z {buried.Z}).");

        // Everything that was derived is announced to the audit.
        foreach (var name in new[] { "DOOR_1_OPENING", "DOOR_3_WAITING_TO_CLOSE", "DOOR_4_CLOSING", "CONSTRUCTION_COMPLETE", "DOCKING_ACTIVE", "PACKING", "UNPACKING", "CAPTURED", "SMOLDERING", "POST_COLLAPSE", "SPECIAL_DAMAGED", "ENEMYNEAR", "OVER_WATER" })
            Check(ConditionDeriver.ProducedNames.Contains(name), $"{name} is a produced name.");
        foreach (var name in new[] { "DOCKING_BEGINNING", "PREORDER", "DOOR_4_WAITING_TO_CLOSE" })
            Check(!ConditionDeriver.ProducedNames.Contains(name), $"{name} is documented as not derivable and stays out of the produced names.");
        _ = tracker;
    }

    /// <summary>Every instantiable retail object goes through the frame projector for a few ticks: the derivation and the tracker never throw and show the conditions a fresh object should show.</summary>
    private static void RealSweep(CompiledDefinitionGraph graph, TemplateCatalog templates, string fingerprint, ImmutableArray<PlayerConfiguration> players)
    {
        var catalog = ObjectVisualCatalog.Build(graph);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 3, players, "presentation-structure-sweep", fingerprint), templates);
        var instantiable = templates.Templates.Where(template => template.IsInstantiable).OrderBy(template => template.Name, StringComparer.Ordinal).ToArray();
        var sequence = 0UL;
        for (var index = 0; index < instantiable.Length; index++)
            world.Submit(new SpawnCommand(PlayerId.System, ++sequence, 0, instantiable[index].Name, new PlayerId(1 + index % 2), Fixed.FromInt(index % 60 * 50), Fixed.FromInt(index / 60 * 50), Fixed.Zero, Fixed.Zero));
        var projector = new FrameProjector(catalog, new FrameProjectorOptions { Colors = TeamColors.Resolve(world.Configuration.Players, graph) });
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var tick = 0; tick < 6; tick++)
        {
            world.Step();
            projector.Advance(world);
        }
        var frame = projector.Project(1f);
        foreach (var drawable in frame.Drawables)
            foreach (var flag in drawable.Conditions.Flags())
                if (flag.Name.StartsWith("DOOR_", StringComparison.Ordinal) || flag.Name is "PACKING" or "UNPACKING" or "DOCKING_ACTIVE" or "CONSTRUCTION_COMPLETE" or "CAPTURED" or "SMOLDERING" or "POST_COLLAPSE" or "SPECIAL_DAMAGED" or "ENEMYNEAR" or "OVER_WATER")
                    counts[flag.Name] = counts.GetValueOrDefault(flag.Name) + 1;
        Check(frame.Drawables.Length > 1500, $"The sweep projects the retail objects ({frame.Drawables.Length} of {instantiable.Length}).");
        Console.WriteLine($"       real data: sweep of {instantiable.Length} objects, fresh objects show: {string.Join(", ", counts.Select(pair => $"{pair.Key} x{pair.Value}"))}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

file sealed class RealSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
