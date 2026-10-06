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
        Check.That(steps.Count >= world.Frames(8) && steps.Count <= world.Frames(10), $"Nine repair ticks (300 ms), got {steps.Count}.");
        Check.That(steps.Take(steps.Count - 1).Distinct().Count() == 1, "Every repair tick adds the same amount.");
        Check.That(steps[0] == (Fixed.FromInt(60) / Fixed.FromInt(world.Frames(9))), "The step is the missing health divided by the frames for a full heal.");

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
