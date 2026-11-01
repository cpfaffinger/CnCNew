using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>Where a projectile starts and how it flies (docs/SIMULATION-CORE.md §75): the launch bone turned with the turret and the launcher, the barrels in turn.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> LaunchFiles() => new Dictionary<string, string>(ProjectileFiles())
    {
        ["Data/INI/Object/LaunchFixture.ini"] = """
        Weapon TwinGun
          PrimaryDamage = 1
          PrimaryDamageRadius = 1.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ProjectileObject = TestShell
          DelayBetweenShots = 0
          ShotsPerBarrel = 2
        End

        Weapon LazyGun
          PrimaryDamage = 1
          PrimaryDamageRadius = 1.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ProjectileObject = TestShell
          DelayBetweenShots = 0
          AcceptableAimDelta = 180
        End
        Weapon NarrowGun
          PrimaryDamage = 1
          PrimaryDamageRadius = 1.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ProjectileObject = TestShell
          DelayBetweenShots = 0
          AcceptableAimDelta = 33
        End
        Locomotor AimerLoco
          Speed = 30
          Acceleration = 300
          Braking = 300
          TurnRate = 180
        End

        Object BodyAimer
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL AimerLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ShellGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object LazyAimer
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL AimerLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY LazyGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object NarrowAimer
          KindOf = INFANTRY SELECTABLE
          Locomotor = SET_NORMAL AimerLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY NarrowGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        Object TurretLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ShellGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              TurretTurnRate = 3600
              TurretPitchRate = 3600
              FirePitch = 60
              AllowsPitch = Yes
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object TwinLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TwinGun
          End
        End
        Object HullLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ShellGun
          End
        End
        """,
    };

    private static LaunchSlot NoLaunch => new([], []);

    private static ModelGeometry LaunchGeometry()
    {
        static Fixed F(decimal value) => Fixed.FromDecimal(value);
        var turret = new LaunchSlot([new LaunchBarrel(F(10), F(0), F(4), F(1), F(0), F(0))], [new LaunchTurret(F(2), F(0), F(3), F(4), F(0), F(3), F(0), F(0)), default]);
        var twin = new LaunchSlot([new LaunchBarrel(F(5), F(3), F(2), F(1), F(0), F(0)), new LaunchBarrel(F(5), F(-3), F(2), F(1), F(0), F(0))], []);
        var hull = new LaunchSlot([new LaunchBarrel(F(5), F(1), F(2), F(1), F(0), F(0))], []);
        return new ModelGeometry([], [], [], null,
        [
            new KeyValuePair<string, WeaponLaunchPoints>("TurretLauncher", new WeaponLaunchPoints([turret, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("TwinLauncher", new WeaponLaunchPoints([twin, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("HullLauncher", new WeaponLaunchPoints([hull, NoLaunch, NoLaunch])),
        ]);
    }

    private static SimulationWorld LaunchWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: LaunchFiles(), geometry: LaunchGeometry());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static void Near(Fixed value, decimal expected, string what)
        => Check.That((value - Fixed.FromDecimal(expected)).Abs() < Fixed.FromDecimal(0.01m), $"{what}: expected {expected}, got {value}.");

    /// <summary>
    /// A turret turned a quarter round and pitched 60 degrees: the bone (10, 0, 4) goes about the pitch bone (4, 0, 3) to (6.134, 0, 8.696), then about the angle bone (2, 0, 3) to
    /// (2, 4.134, 8.696), and the projectile takes the heading of the turret, not of the hull that still looks along x (<c>Weapon::calcProjectileLaunchPosition</c>).
    /// </summary>
    public static void ProjectileLaunchTurnsWithTheTurret()
    {
        var world = LaunchWorld(out _, out var feeder);
        feeder.Spawn(1, "TurretLauncher", x: 0, y: 0);
        feeder.Spawn(2, "Dummy", x: 0, y: 300);
        world.Step();
        var launcher = Get(world, "TurretLauncher");
        var dummy = Get(world, "Dummy");
        Entity? shell = null;
        for (var tick = 0; tick < world.Frames(60) && shell is null; tick++)
        {
            feeder.FireWeapon(1, launcher.Id.Value, dummy.Id.Value);
            world.Step();
            shell = world.Entities.FirstOrDefault(value => value.Template.Name == "TestShell");
        }
        Check.That(shell is not null, "The turret turns to the side and fires.");
        Near(shell!.X, 2m, "x of the launch");
        Near(shell.Y, 4.134m, "y of the launch");
        Near(shell.Z, 8.696m, "z of the launch");
        Check.That(launcher.Angle == Fixed.Zero, "The hull has not turned.");
        Check.That(FixedTrig.AngleDifference(shell.Angle, FixedTrig.PiFraction(1, 2)).Abs() < Fixed.FromDecimal(0.01m), $"The shell has the turret's heading, got {shell.Angle}.");
    }

    /// <summary>The launch bone turns with the object: (5, 1, 2) of a launcher heading along y is (-1, 5, 2) from it.</summary>
    public static void ProjectileLaunchTurnsWithTheHull()
    {
        var world = LaunchWorld(out _, out var feeder);
        feeder.Spawn(1, "HullLauncher", x: 100, y: 100, angle: FixedTrig.PiFraction(1, 2));
        feeder.Spawn(2, "Dummy", x: 100, y: 300);
        world.Step();
        feeder.FireWeapon(1, Get(world, "HullLauncher").Id.Value, Get(world, "Dummy").Id.Value);
        world.Step();
        var shell = Get(world, "TestShell");
        Near(shell.X, 99m, "x of the launch");
        Near(shell.Y, 105m, "y of the launch");
        Near(shell.Z, 2m, "z of the launch");
    }

    /// <summary>The orders of an attack, one per tick, until the first shot; the frame of the shot after the first order.</summary>
    private static long FramesToFirstShot(SimulationWorld world, Feeder feeder, Entity source, Entity victim)
    {
        var start = world.Tick;
        for (var tick = 0; tick < world.Frames(120); tick++)
        {
            feeder.FireWeapon(1, source.Id.Value, victim.Id.Value);
            if (world.Step().Events.Any(value => value.Kind == SimEventKind.WeaponFired)) return world.InFrames(world.Tick - 1 - start);
        }
        return -1;
    }

    /// <summary>
    /// <c>AIAttackAimAtTargetState::update</c>: a unit without a turret turns to its victim at its turn rate (180 degrees a second are 6 a frame) and fires when it looks at it: a quarter
    /// turn takes 15 frames, within 2 degrees of the bearing. At every rate the same frames.
    /// </summary>
    public static void BodyAimTurnsTheUnitToItsVictim()
    {
        var world = LaunchWorld(out _, out var feeder);
        feeder.Spawn(1, "BodyAimer", x: 0, y: 0);
        feeder.Spawn(2, "Dummy", x: 0, y: 300);
        world.Step();
        var aimer = Get(world, "BodyAimer");
        var frames = FramesToFirstShot(world, feeder, aimer, Get(world, "Dummy"));
        Check.That(frames == 15, $"A quarter turn at 6 degrees a frame is 15 frames, got {frames}.");
        Check.That(FixedTrig.AngleDifference(aimer.Angle, FixedTrig.PiFraction(1, 2)).Abs() <= Fixed.FromDecimal(0.035m), $"The unit looks at its victim, got {aimer.Angle}.");
        Check.That(FixedTrig.AngleDifference(Get(world, "TestShell").Angle, FixedTrig.PiFraction(1, 2)).Abs() < Fixed.FromDecimal(0.04m), "The shot leaves that way.");
    }

    /// <summary><c>AcceptableAimDelta</c>: 180 degrees ask for no turn at all, 33 degrees let a unit stop within 33 degrees of its victim.</summary>
    public static void BodyAimAcceptsTheWeaponsDelta()
    {
        var world = LaunchWorld(out _, out var feeder);
        feeder.Spawn(1, "LazyAimer", x: 0, y: 0);
        feeder.Spawn(1, "NarrowAimer", x: 0, y: 50);
        feeder.Spawn(2, "Dummy", x: 0, y: 300);
        world.Step();
        var (lazy, narrow, dummy) = (Get(world, "LazyAimer"), Get(world, "NarrowAimer"), Get(world, "Dummy"));
        Check.That(FramesToFirstShot(world, feeder, lazy, dummy) == 0, "A weapon that does not care fires at once.");
        Check.That(lazy.Angle == Fixed.Zero, "And the unit has not turned.");
        var frames = FramesToFirstShot(world, feeder, narrow, dummy);
        Check.That(frames == 10, $"60 degrees of the quarter turn are 10 frames, got {frames}.");
    }

    /// <summary><c>Weapon::privateFireWeapon</c>: <c>ShotsPerBarrel</c> shots from a barrel, then the next one, and round again - across a snapshot.</summary>
    public static void ProjectileLaunchTakesTheBarrelsInTurn()
    {
        List<decimal> Run(int snapshotAfter)
        {
            var world = LaunchWorld(out var fixture, out var feeder);
            feeder.Spawn(1, "TwinLauncher", x: 0, y: 0);
            feeder.Spawn(2, "Dummy", x: 300, y: 0);
            world.Step();
            var (launcher, dummy) = (Get(world, "TwinLauncher").Id.Value, Get(world, "Dummy").Id.Value);
            var sides = new List<decimal>();
            var seen = new HashSet<uint>();
            for (var tick = 0; tick < world.Frames(40) && sides.Count < 6; tick++)
            {
                feeder.FireWeapon(1, launcher, dummy);
                world.Step();
                foreach (var shell in world.Entities.Where(value => value.Template.Name == "TestShell" && seen.Add(value.Id.Value)))
                    sides.Add(shell.Y > Fixed.Zero ? 3m : -3m);
                if (sides.Count == snapshotAfter)
                {
                    world = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    feeder = new Feeder(world);
                    snapshotAfter = -1;
                }
            }
            return sides;
        }
        var sides = Run(-1);
        Check.That(sides.SequenceEqual([3m, 3m, -3m, -3m, 3m, 3m]), $"Two shots of each barrel in turn, got {string.Join(' ', sides)}.");
        Check.That(sides.SequenceEqual(Run(3)), "A snapshot keeps the barrel and its shots.");
    }
}
