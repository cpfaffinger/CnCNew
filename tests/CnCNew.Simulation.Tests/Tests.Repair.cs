using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>Player 1 and 3 are computer players, player 2 is a human.</summary>
    private static readonly ImmutableArray<PlayerConfiguration> RepairPlayers =
    [
        new(new PlayerId(1), "Alpha", IsComputer: true), new(new PlayerId(2), "Beta"), new(new PlayerId(3), "Gamma", IsComputer: true),
    ];

    private static IReadOnlyDictionary<string, string> RepairFiles() => new Dictionary<string, string>(RetailModuleFiles())
    {
        ["Data/INI/Object/RepairFixture.ini"] = """
        Object RepairPad
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
          Behavior = RepairDockUpdate Dock01
            NumberApproachPositions = 1
            TimeForFullHeal = 300
          End
        End

        Object Truck
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        Object Clinic
          KindOf = STRUCTURE HEAL_PAD
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HealContain Heal01
            ContainMax = 2
            TimeForFullHeal = 300
            AllowInsideKindOf = INFANTRY
            AllowAlliesInside = Yes
            AllowEnemiesInside = No
            AllowNeutralInside = No
          End
        End

        Object Patient
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = AutoFindHealingUpdate Find01
            ScanRate = 100
            ScanRange = 300.0
            NeverHeal = 0.85
            AlwaysHeal = 0.25
          End
        End

        Object DyingPatient
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 60000
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = AutoFindHealingUpdate Find01
            ScanRate = 100
            ScanRange = 300.0
            NeverHeal = 0.85
            AlwaysHeal = 0.25
          End
        End
        """,
    };

    private static SimulationWorld RepairWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: RepairFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, RepairPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity Get(SimulationWorld world, string template, int nth = 0) => world.FindEntity(new EntityId(IdOf(world, template, nth)))!;

    /// <summary>The dock heals the docked object at a constant rate over TimeForFullHeal, then lets it go; a healthy docker does not hold the queue.</summary>
    public static void RepairDockHealsOverTime()
    {
        var world = RepairWorld(out _, out var feeder);
        feeder.Spawn(1, "RepairPad", x: 100);
        feeder.Spawn(1, "Truck", x: 100, y: 60);
        world.Step();
        var pad = Get(world, "RepairPad");
        var truck = Get(world, "Truck");
        feeder.Damage(truck.Id.Value, 60);
        world.Step();
        Check.That(truck.Body!.Health == Fixed.FromInt(40), "The truck starts at 40 of 100.");
        Check.That(feeder.Dock(1, truck.Id.Value, pad.Id.Value).Accepted, "The dock order is accepted.");

        var history = new List<Fixed>();
        for (var i = 0; i < world.Frames(400) && truck.Body.Health < truck.Body.MaxHealth; i++)
        {
            world.Step();
            history.Add(truck.Body.Health);
        }
        Check.That(truck.Body.Health == truck.Body.MaxHealth, "The truck is repaired to full health.");
        var steps = new List<Fixed>();
        for (var i = 1; i < history.Count; i++)
            if (history[i] > history[i - 1]) steps.Add(history[i] - history[i - 1]);
        // The dock heals once per logic frame of the original at every rate (phase 23): nine steps of 60 / 9, not nine frames' worth of smaller steps.
        Check.That(steps.Count is >= 8 and <= 10, $"Nine repair steps (300 ms are nine frames of the original), got {steps.Count}.");
        Check.That(steps.Take(steps.Count - 1).Distinct().Count() == 1, "Every repair step adds the same amount.");
        Check.That(steps[0] == (Fixed.FromInt(60) / Fixed.FromInt(9)), "The step is the missing health divided by the frames of the original for a full heal.");

        for (var i = 0; i < world.Frames(20); i++) world.Step();
        Check.That(!truck.HasMoveTarget && truck.Body.Health == truck.Body.MaxHealth, "The docking is over and the truck is idle.");

        // A healthy docker finishes at once; the damaged one behind it is still repaired.
        feeder.Spawn(1, "Truck", x: 100, y: 60);
        feeder.Spawn(1, "Truck", x: 100, y: 80);
        world.Step();
        var healthy = Get(world, "Truck", 1);
        var hurt = Get(world, "Truck", 2);
        feeder.Damage(hurt.Id.Value, 30);
        world.Step();
        feeder.Dock(1, healthy.Id.Value, pad.Id.Value);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(healthy.Body!.Health == healthy.Body.MaxHealth && !healthy.HasMoveTarget, "A healthy docker has left the dock free after 100 ticks.");
        feeder.Dock(1, hurt.Id.Value, pad.Id.Value);
        for (var i = 0; i < world.Frames(300) && hurt.Body!.Health < hurt.Body.MaxHealth; i++) world.Step();
        Check.That(hurt.Body!.Health == hurt.Body.MaxHealth, "The damaged docker behind it is repaired.");
    }

    /// <summary>A mid-repair snapshot continues hash-identically to the live run.</summary>
    public static void RepairDockSnapshot()
    {
        var world = RepairWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "RepairPad", x: 100);
        feeder.Spawn(1, "Truck", x: 100, y: 60);
        world.Step();
        var truck = Get(world, "Truck");
        feeder.Damage(truck.Id.Value, 60);
        world.Step();
        feeder.Dock(1, truck.Id.Value, Get(world, "RepairPad").Id.Value);
        var midRepair = false;
        for (var i = 0; i < world.Frames(100) && !midRepair; i++)
        {
            world.Step();
            midRepair = truck.Body!.Health > Fixed.FromInt(50) && truck.Body.Health < Fixed.FromInt(90);
        }
        Check.That(midRepair, "The truck is caught mid-repair.");
        SnapshotContinues(world, fixture);
    }

    private static Entity? RunUntilContained(SimulationWorld world, Entity patient, int maxFrames)
    {
        for (var i = 0; i < world.Frames(maxFrames) && !patient.ContainedBy.IsValid; i++) world.Step();
        return patient.ContainedBy.IsValid ? world.FindEntity(patient.ContainedBy) : null;
    }

    /// <summary>A hurt computer-player infantry object walks into an allied heal pad, is healed and released.</summary>
    public static void AutoFindHealingWalksToPad()
    {
        var world = RepairWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Clinic", x: 100);
        feeder.Spawn(1, "Patient");
        world.Step();
        var patient = Get(world, "Patient");
        var clinic = Get(world, "Clinic");
        feeder.Damage(patient.Id.Value, 20);
        world.Step();
        Check.That(patient.Body!.Health == Fixed.FromInt(30), "The patient is at 30 of 50 (60 %).");

        var walking = false;
        for (var i = 0; i < world.Frames(40) && !walking; i++)
        {
            world.Step();
            walking = patient.HasMoveTarget;
        }
        Check.That(walking, "The scan found the pad and the patient started to walk.");
        SnapshotContinues(world, fixture);

        var host = RunUntilContained(world, patient, 400);
        Check.That(host == clinic, "The patient entered the pad.");
        Check.That(patient.X > Fixed.FromInt(50), "It walked there first; the entry is not instant.");
        for (var i = 0; i < world.Frames(100) && patient.ContainedBy.IsValid; i++) world.Step();
        Check.That(!patient.ContainedBy.IsValid && patient.Body.Health == patient.Body.MaxHealth, "The pad healed the patient to full health and released it.");
        for (var i = 0; i < world.Frames(60); i++) world.Step();
        Check.That(!patient.HasMoveTarget && !patient.ContainedBy.IsValid, "A healthy patient is not sent in again.");
    }

    /// <summary>
    /// F-87: the dying are not idle (<c>AI_DEAD</c>), so a unit in its slow death looks for no pad. Before, a ranger that died in the frame of its scan was given the pad as its enter target above 30 Hz and
    /// kept it through its death.
    /// </summary>
    public static void AutoFindHealingLeavesTheDyingAlone()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: RepairFiles());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, RepairPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Clinic", x: 100);
            feeder.Spawn(1, "DyingPatient");
            for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
            var patient = Get(world, "DyingPatient");
            var ai = patient.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.AIUpdateInterface>().Single();
            feeder.Damage(patient.Id.Value, 100);
            var aimed = false;
            for (var tick = 0; tick < world.Frames(300); tick++)
            {
                world.Step();
                aimed |= ai.EnterTarget.IsValid;
            }
            Check.That(patient.EffectivelyDead && !patient.DestroyRequested, $"{rate}: the unit is in its slow death.");
            Check.That(!aimed, $"{rate}: the dying look for no pad.");
        }
    }

    /// <summary>The scan does nothing for human players, healthy objects, busy objects, enemy pads, and pads out of range.</summary>
    public static void AutoFindHealingRefusals()
    {
        // Human player.
        var world = RepairWorld(out _, out var feeder);
        feeder.Spawn(2, "Clinic", x: 100, owner: 2);
        feeder.Spawn(2, "Patient", owner: 2);
        world.Step();
        var patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 30);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(!patient.HasMoveTarget && !patient.ContainedBy.IsValid && patient.X == Fixed.Zero, "A human player's infantry never looks for healing by itself.");

        // Above NeverHeal (88 %).
        world = RepairWorld(out _, out feeder);
        feeder.Spawn(1, "Clinic", x: 100);
        feeder.Spawn(1, "Patient");
        world.Step();
        patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 6);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(!patient.HasMoveTarget && !patient.ContainedBy.IsValid && patient.X == Fixed.Zero, "An object above NeverHeal is not sent to the pad.");
        feeder.Damage(patient.Id.Value, 2);
        Check.That(RunUntilContained(world, patient, 400) is not null, "Below NeverHeal (84 %) it does go.");

        // Busy: a move order away from the pad.
        world = RepairWorld(out _, out feeder);
        feeder.Spawn(1, "Clinic", x: 100);
        feeder.Spawn(1, "Patient");
        world.Step();
        patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 20);
        feeder.SetMoveTarget(1, patient.Id.Value, -500, 0);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(patient.X < Fixed.Zero && !patient.ContainedBy.IsValid && patient.Body!.Health == Fixed.FromInt(30), "A busy object keeps its order.");

        // Enemy pad.
        world = RepairWorld(out _, out feeder);
        feeder.Spawn(3, "Clinic", x: 100, owner: 3);
        feeder.Spawn(1, "Patient");
        world.Step();
        patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 20);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(!patient.HasMoveTarget && !patient.ContainedBy.IsValid && patient.X == Fixed.Zero, "An enemy's pad is not entered.");

        // Out of range (ScanRange 300).
        world = RepairWorld(out _, out feeder);
        feeder.Spawn(1, "Clinic", x: 400);
        feeder.Spawn(1, "Patient");
        world.Step();
        patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 20);
        for (var i = 0; i < world.Frames(100); i++) world.Step();
        Check.That(!patient.HasMoveTarget && !patient.ContainedBy.IsValid && patient.X == Fixed.Zero, "A pad beyond ScanRange is not seen.");
    }

    /// <summary>The closest pad wins; equal distances go to the lower entity id.</summary>
    public static void AutoFindHealingPicksClosestPad()
    {
        var world = RepairWorld(out _, out var feeder);
        feeder.Spawn(1, "Clinic", x: 200);
        feeder.Spawn(1, "Clinic", x: 50);
        feeder.Spawn(1, "Patient");
        world.Step();
        var far = Get(world, "Clinic", 0);
        var near = Get(world, "Clinic", 1);
        var patient = Get(world, "Patient");
        Check.That(far.X == Fixed.FromInt(200) && near.X == Fixed.FromInt(50), "The clinics stand where they were placed.");
        feeder.Damage(patient.Id.Value, 20);
        Check.That(RunUntilContained(world, patient, 400) == near, "The nearer pad is chosen although the other has the lower id.");

        world = RepairWorld(out _, out feeder);
        feeder.Spawn(1, "Clinic", x: 100);
        feeder.Spawn(1, "Clinic", x: -100);
        feeder.Spawn(1, "Patient");
        world.Step();
        var first = world.Entities.Where(value => value.Template.Name == "Clinic").OrderBy(value => value.Id.Value).First();
        patient = Get(world, "Patient");
        feeder.Damage(patient.Id.Value, 20);
        Check.That(RunUntilContained(world, patient, 400) == first, "Equal distances are decided by the lower entity id.");
    }

    /// <summary>The computer flag survives the replay header.</summary>
    public static void RepairPlayerTypeReplayHeader()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RepairFiles());
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), RepairPlayers, 20, 5);
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, [], [])));
        Check.That(read.Header.Players.SequenceEqual(RepairPlayers), "The replay header keeps every player's computer flag.");
        Check.That(read.Header.Players.Select(value => value.IsComputer).SequenceEqual([true, false, true]), "The flags are read back in order.");
    }
}

static partial class Tests
{
    /// <summary>
    /// F-34 (<c>HealContain::update</c>, <c>OpenContain::exitObjectViaDoor</c>): a healed occupant leaves the pad through its door and walks to the end of the exit path,
    /// instead of being put on the pad's centre.
    /// </summary>
    public static void HealContainReleasesThroughTheDoor()
    {
        var files = new Dictionary<string, string>(RepairFiles());
        files["Data/INI/Object/HealDoorFixture.ini"] = """
        Object WidePad
          KindOf = STRUCTURE HEAL_PAD
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
          Behavior = HealContain Heal01
            ContainMax = 2
            TimeForFullHeal = 300
            AllowInsideKindOf = INFANTRY
            AllowAlliesInside = Yes
          End
        End
        """;
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        // The pad's model has one exit path: from its centre to 40 units in front of it.
        var geometry = new CnCNew.Simulation.Templates.ModelGeometry([], [new KeyValuePair<string, ImmutableArray<CnCNew.Simulation.Templates.ExitPath>>("WidePad",
            [new CnCNew.Simulation.Templates.ExitPath(new CnCNew.Simulation.Templates.BonePose(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero),
                new CnCNew.Simulation.Templates.BonePose(Fixed.FromInt(40), Fixed.Zero, Fixed.Zero, Fixed.Zero))])]);
        var templates = CnCNew.Simulation.Templates.TemplateCatalog.Build(fixture.Graph, fixture.Bindings, TestRate.Default, geometry);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, RepairPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "WidePad", x: 200, y: 200);
        feeder.Spawn(1, "Patient", x: 150, y: 200);
        world.Step();
        var patient = Get(world, "Patient");
        var pad = Get(world, "WidePad");
        feeder.Damage(patient.Id.Value, 20);
        world.Step();
        Check.That(feeder.EnterContainer(1, patient.Id.Value, pad.Id.Value).Accepted, "The patient is sent into the pad.");
        RunUntilContained(world, patient, 400);
        Check.That(patient.ContainedBy == pad.Id, "The patient is inside the pad.");
        for (var i = 0; i < world.Frames(100) && patient.ContainedBy.IsValid; i++) world.Step();
        Check.That(!patient.ContainedBy.IsValid && patient.Body!.Health == patient.Body.MaxHealth, "The pad healed the patient and released it.");
        for (var i = 0; i < world.Frames(60); i++) world.Step();
        var dx = (patient.X - pad.X).Abs();
        var dy = (patient.Y - pad.Y).Abs();
        Check.That(dx > Fixed.FromInt(20) || dy > Fixed.FromInt(20), $"The healed patient walked off the pad through its door ({patient.X}, {patient.Y}; pad at {pad.X}, {pad.Y}).");
    }

    /// <summary>
    /// F-75: the scan counts the frames of the object, also after it was held. A computer player's hurt patient rides in a bus: held, it does not scan. The bus lets it out in its update,
    /// after the patient's update of that tick, and the patient goes to the pad in the same frame at 30, 60 and 120 Hz, counted from the frame it was let out in - at 30 Hz its next update
    /// is the next frame's. Before, above 30 Hz the scan ran in the next tick, inside the frame it was let out in, a frame early.
    /// </summary>
    public static void AutoFindHealingCountsFramesAfterAHold()
    {
        long? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(RepairFiles())
            {
                ["Data/INI/Object/HoldFixture.ini"] = """
                Object RpBus
                  KindOf = SELECTABLE VEHICLE TRANSPORT
                  Locomotor = SET_NORMAL TestLocomotor
                  Body = ActiveBody Body01
                    MaxHealth = 100.0
                    InitialHealth = 100.0
                  End
                  Behavior = TransportAIUpdate AI01
                  End
                  Behavior = TransportContain Transport01
                    Slots = 2
                    ExitDelay = 250
                    NumberOfExitPaths = 0
                  End
                End
                Object RpRider
                  KindOf = SELECTABLE INFANTRY
                  TransportSlotCount = 1
                  Locomotor = SET_NORMAL TestLocomotor
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = AIUpdateInterface AI01
                  End
                  Behavior = AutoFindHealingUpdate Find01
                    ScanRate = 100
                    ScanRange = 300.0
                    NeverHeal = 0.85
                    AlwaysHeal = 0.25
                  End
                End
                """,
            };
            var fixture = new SyntheticFixture(rate, files: files);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, RepairPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            // Two riders before the bus: they have their turn in a tick before the bus has. The first leaves when the bus is told to unload; the second when its door is free again,
            // in the bus's update - after the patient's update of that tick.
            feeder.Spawn(1, "RpRider");
            feeder.Spawn(1, "RpRider");
            feeder.Spawn(1, "Clinic", x: 200);
            feeder.Spawn(1, "RpBus");
            world.Step();
            var first = Get(world, "RpRider");
            var patient = Get(world, "RpRider", 1);
            var bus = Get(world, "RpBus");
            Check.That(world.TryEnterContainer(first, bus) && world.TryEnterContainer(patient, bus), $"{rate}: the riders board the bus.");
            feeder.Damage(patient.Id.Value, 20);
            for (var i = 0; i < world.Frames(250); i++) world.Step();
            Check.That(patient.ContainedBy == bus.Id && !patient.HasMoveTarget, $"{rate}: held in the bus, the hurt patient does not go to the pad.");
            Check.That(feeder.Evacuate(1, bus.Id.Value).Accepted, $"{rate}: the bus lets its riders out.");
            long outTick = -1, walks = -1;
            for (var i = 0; i < world.Frames(400) && walks < 0; i++)
            {
                world.Step();
                if (outTick < 0 && !patient.ContainedBy.IsValid) outTick = world.Tick;
                if (outTick >= 0 && (patient.HasMoveTarget || patient.ContainedBy.IsValid))
                {
                    walks = (world.Tick - outTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
                    // The scan is a decision of the patient's frame: it is taken in the first tick of one.
                    Check.That(world.Time.BeginsFrameOf(patient, world.Tick - 1), $"{rate}: the scan sends the patient in a tick that begins one of its frames (tick {world.Tick - 1}).");
                }
            }
            Check.That(walks >= 0, $"{rate}: let out, the patient goes to the pad.");
            if (reference is null) reference = walks;
            else Check.That(walks == reference, $"{rate}: it goes to the pad in the frame 30 Hz has ({walks} against {reference}).");
        }
    }
}
