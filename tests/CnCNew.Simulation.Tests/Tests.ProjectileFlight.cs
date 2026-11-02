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
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Where a projectile starts and how it flies (docs/SIMULATION-CORE.md §76): the launch bone turned with the turret and the launcher, the barrels in turn.</summary>
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
        Object IdleAimer
          KindOf = INFANTRY SELECTABLE
          VisionRange = 400.0
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
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 100
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

        Locomotor CruiseLoco
          Surfaces = AIR
          Speed = 200
          MinSpeed = 100
          Acceleration = 675
          Braking = 0
          TurnRate = 540
          MaxThrustAngle = 45
          PreferredHeight = 120
          PreferredHeightDamping = 0.7
        End
        Locomotor LimitedLoco
          Surfaces = AIR
          Speed = 225
          MinSpeed = 120
          Acceleration = 675
          Braking = 0
          TurnRate = 100
          MaxThrustAngle = 45
        End
        Weapon CruiseGun
          PrimaryDamage = 150
          PrimaryDamageRadius = 10.0
          SecondaryDamage = 50
          SecondaryDamageRadius = 25.0
          AttackRange = 400.0
          ProjectileObject = CruiseMissile
          DelayBetweenShots = 0
        End
        Weapon LimitedGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 400.0
          ProjectileObject = LimitedMissile
          DelayBetweenShots = 0
        End
        Object CruiseMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Geometry = CYLINDER
          GeometryMajorRadius = 8.0
          GeometryHeight = 4.0
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL CruiseLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 8000
            IgnitionDelay = 0
            InitialVelocity = 50
            DistanceToTravelBeforeTurning = 80
            DistanceToTargetBeforeDiving = 100
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LimitedMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL LimitedLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 8000
            IgnitionDelay = 0
            InitialVelocity = 150
            DistanceToTravelBeforeTurning = 3
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object RollingLauncher
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL AimerLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY LimitedGun
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 50.0
          End
        End
        Object CruiseLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CruiseGun
          End
        End
        Object LimitedLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY LimitedGun
          End
        End
        Object WideBuilding
          KindOf = STRUCTURE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 40.0
          GeometryHeight = 30.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
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
        // The rack of a cruise missile launcher: 16 units up, 70 degrees to the sky.
        var rack = new LaunchSlot([new LaunchBarrel(F(0), F(0), F(16), F(0.34202m), F(0), F(0.93969m))], []);
        var tube = new LaunchSlot([new LaunchBarrel(F(0), F(0), F(10), F(1), F(0), F(0))], []);
        return new ModelGeometry([], [], [], null,
        [
            new KeyValuePair<string, WeaponLaunchPoints>("TurretLauncher", new WeaponLaunchPoints([turret, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("TwinLauncher", new WeaponLaunchPoints([twin, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("HullLauncher", new WeaponLaunchPoints([hull, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("CruiseLauncher", new WeaponLaunchPoints([rack, NoLaunch, NoLaunch])),
            new KeyValuePair<string, WeaponLaunchPoints>("LimitedLauncher", new WeaponLaunchPoints([tube, NoLaunch, NoLaunch])),
        ]);
    }

    private static SimulationWorld LaunchWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: LaunchFiles(), geometry: LaunchGeometry());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static void Near(Fixed value, decimal expected, string what, decimal within = 0.01m)
        => Check.That((value - Fixed.FromDecimal(expected)).Abs() < Fixed.FromDecimal(within), $"{what}: expected {expected}, got {value}.");

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

    private static readonly (SimulationRate Rate, int Factor)[] MissileRates = [(SimulationRate.Hz30, 1), (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4)];

    /// <summary>One flight, read where each logic frame of the original ends: the missile's place and its nose, and the place and frame of its impact.</summary>
    private sealed record MissileFlight(List<(Fixed X, Fixed Y, Fixed Z, Fixed Pitch, Fixed Angle)> Frames, (Fixed X, Fixed Y, Fixed Z) Impact, long ImpactFrame, Fixed Health);

    private static MissileFlight FlyMissile(SimulationRate rate, int factor, string launcher, string missile, string victim, int x, int y)
    {
        var fixture = new SyntheticFixture(rate, files: LaunchFiles(), geometry: LaunchGeometry());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, launcher, x: 0, y: 0);
        feeder.Spawn(2, victim, x: x, y: y);
        world.Step();
        var target = Get(world, victim);
        var fire = world.Tick;
        Check.That(feeder.FireWeapon(1, Get(world, launcher).Id.Value, target.Id.Value).Accepted, "The launcher fires.");
        var frames = new List<(Fixed, Fixed, Fixed, Fixed, Fixed)>();
        (Fixed, Fixed, Fixed) impact = default;
        long impactFrame = -1;
        for (var step = 1; step <= 200 * factor && impactFrame < 0; step++)
        {
            var result = world.Step();
            if (result.Events.FirstOrDefault(value => value.Kind == SimEventKind.WeaponImpact) is { HasPosition: true } hit)
            {
                impact = (hit.X, hit.Y, hit.Z);
                impactFrame = (result.Tick - fire) / factor;
            }
            if (step % factor == 0 && world.Entities.FirstOrDefault(value => value.Template.Name == missile) is { } flying)
                frames.Add((flying.X, flying.Y, flying.Z, flying.Modules.OfType<MissileAIUpdate>().Single().FlightPitch, flying.Angle));
        }
        return new MissileFlight(frames, impact, impactFrame, target.Body!.Health);
    }

    private static decimal Degrees(Fixed radians) => radians.Raw * 180m / FixedTrig.Pi.Raw;

    /// <summary>
    /// A cruise missile (<c>Locomotor::moveTowardsPositionThrust</c>, <c>MissileAIUpdate::doAttackState</c>, the numbers of the Tomahawk): it leaves its rack 70 degrees up and keeps that nose for
    /// the 80 units of <c>DistanceToTravelBeforeTurning</c>, climbs to about its <c>PreferredHeight</c> of 120 and holds it, and within <c>DistanceToTargetBeforeDiving</c> of its victim it
    /// noses down and comes down on it. The port's kinematic missile turned at will after 80 units and flew a straight line at the target's feet. Bit for bit the same flight at 30, 60 and 120 Hz.
    /// </summary>
    public static void MissileCruisesAtItsPreferredHeightAndDives()
    {
        MissileFlight? reference = null;
        foreach (var (rate, factor) in MissileRates)
        {
            var flight = FlyMissile(rate, factor, "CruiseLauncher", "CruiseMissile", "Dummy", 330, 0);
            Check.That(flight.ImpactFrame > 0, $"{rate}: the missile arrives.");
            reference ??= flight;
            // The loop ends with the impact, which above 30 Hz falls on a frame's first tick: that frame has not ended there.
            Check.That(Math.Abs(flight.Frames.Count - reference.Frames.Count) <= 1, $"{rate}: followed for as many frames as at 30 Hz ({flight.Frames.Count} against {reference.Frames.Count}).");
            for (var frame = 0; frame < Math.Min(flight.Frames.Count, reference.Frames.Count); frame++)
                Check.That(flight.Frames[frame] == reference.Frames[frame], $"{rate}: frame {frame} is that of 30 Hz bit for bit ({flight.Frames[frame]} against {reference.Frames[frame]}).");
            Check.That(flight.ImpactFrame == reference.ImpactFrame && flight.Health == reference.Health, $"{rate}: the same impact as at 30 Hz.");
        }
        var path = reference!.Frames;
        for (var frame = 0; frame < 12; frame++) Near(Fixed.FromDecimal(Degrees(path[frame].Pitch)), 70m, $"The nose stays on the launch line in frame {frame}", 0.5m);
        var highest = path.Max(value => value.Z);
        Check.That(highest > Fixed.FromInt(115) && highest < Fixed.FromInt(150), $"The missile climbs to about its preferred height (highest {highest}).");
        var midway = path.First(value => value.X >= Fixed.FromInt(165));
        Check.That(midway.Z > Fixed.FromInt(110), $"Halfway it cruises high above the ground ({midway.Z}).");
        Check.That(Math.Abs(Degrees(midway.Pitch)) < 10m, $"Level ({Degrees(midway.Pitch)} degrees).");
        var last = path[^1];
        Check.That(Degrees(last.Pitch) < -40m, $"At the end it comes down on its victim, nose first ({Degrees(last.Pitch)} degrees).");
        Check.That((reference.Impact.X - Fixed.FromInt(330)).Abs() < Fixed.One && reference.Health < Fixed.FromInt(1000), $"It hits the victim at {reference.Impact}.");
    }

    /// <summary>The locomotor's <c>TurnRate</c> limits the nose (100 degrees a second are 3.33 a frame; the port's missile turned at will), so a victim behind the launcher takes a turn of a wide radius.</summary>
    public static void MissileTurnsAtItsLocomotorsRate()
    {
        var flight = FlyMissile(SimulationRate.Hz30, 1, "LimitedLauncher", "LimitedMissile", "Dummy", -250, 40);
        var path = flight.Frames;
        Check.That(flight.ImpactFrame > 0, "The missile comes round and arrives.");
        var fastest = 0m;
        for (var frame = 1; frame < path.Count; frame++) fastest = Math.Max(fastest, Math.Abs(Degrees(FixedTrig.AngleDifference(path[frame - 1].Angle, path[frame].Angle))));
        Check.That(fastest <= 3.34m, $"The nose never turns faster than the locomotor ({fastest} degrees a frame).");
        Check.That(path.Max(value => value.X) > Fixed.FromInt(50), $"It overshoots away from its victim before it comes round ({path.Max(value => value.X)}).");
    }

    /// <summary>
    /// <c>transferVelocityTo</c>: a missile takes the speed of a launcher on the move, the same after a save: the launcher's speed per frame of the original comes from the frame's
    /// own value, not from the speed per tick, which has lost the division's rest. A world restored in the middle of the launcher's walk stays the continuous world.
    /// </summary>
    public static void MissileTakesTheLaunchersVelocityAcrossASave()
    {
        foreach (var (rate, _) in MissileRates)
        {
            var fixture = new SyntheticFixture(rate, files: LaunchFiles(), geometry: LaunchGeometry());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "RollingLauncher", x: 0, y: 0, angle: FixedTrig.PiFraction(1, 2));
            feeder.Spawn(2, "Dummy", x: 0, y: 300);
            world.Step();
            var (launcher, dummy) = (Get(world, "RollingLauncher"), Get(world, "Dummy"));
            feeder.SetMoveTarget(1, launcher.Id.Value, 0, 250);
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            Check.That(launcher.Speed > Fixed.Zero, $"{rate}: the launcher is on its way.");
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
            var other = feeder.ForkTo(restored);
            feeder.FireWeapon(1, launcher.Id.Value, dummy.Id.Value);
            other.FireWeapon(1, launcher.Id.Value, dummy.Id.Value);
            for (var step = 0; step < world.Frames(10); step++)
                Check.That(world.Step().StateHash == restored.Step().StateHash, $"{rate}: tick {step} after the save is the continuous one.");
            Check.That(world.Entities.Any(value => value.Template.Name == "LimitedMissile"), $"{rate}: the shot went out.");
        }
    }

    /// <summary>The stand-in for the collision: a missile blows up where it meets the box of the object it was fired at, not at the object's feet in the middle of it.</summary>
    public static void MissileDetonatesOnTheVictimsGeometry()
    {
        var flight = FlyMissile(SimulationRate.Hz30, 1, "CruiseLauncher", "CruiseMissile", "WideBuilding", 330, 0);
        Check.That(flight.ImpactFrame > 0, "The missile hits.");
        Check.That(flight.Impact.Z > Fixed.Zero && flight.Impact.Z <= Fixed.FromInt(30), $"It meets the building above the ground and below its top ({flight.Impact}).");
        Check.That((flight.Impact.X - Fixed.FromInt(330)).Abs() <= Fixed.FromInt(48) && (flight.Impact.X - Fixed.FromInt(330)).Abs() > Fixed.FromInt(10), $"At its wall or roof, not at its middle ({flight.Impact}).");
        Check.That(flight.Health < Fixed.FromInt(1000), "And hurts it.");
    }

    /// <summary><c>m_orientToFlightPath</c>: a shell's nose follows its arc, up at the start, down at the end.</summary>
    public static void ShellPitchFollowsItsArc()
    {
        var world = LaunchWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "ArcLauncher", 120, out _);
        CsRun(world, 2);
        var shell = world.Entities.Single(value => value.Template.Name == "ArcShell");
        var dumb = shell.Modules.OfType<DumbProjectileBehavior>().Single();
        Check.That(Degrees(dumb.FlightPitch) > 20m, $"Up at the start ({Degrees(dumb.FlightPitch)}).");
        while (world.Tick < fire + world.Frames(13)) world.Step();
        Check.That(Degrees(dumb.FlightPitch) < -20m, $"Down at the end ({Degrees(dumb.FlightPitch)}).");
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

    /// <summary>A unit that finds its victim by itself turns to it too, in the first tick of its own frame, in the same frames at 30, 60 and 120 Hz.</summary>
    public static void BodyAimOfASelfAcquiredVictim()
    {
        long? reference = null;
        foreach (var (rate, factor) in MissileRates)
        {
            var fixture = new SyntheticFixture(rate, files: LaunchFiles(), geometry: LaunchGeometry());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "IdleAimer", x: 0, y: 0);
            feeder.Spawn(2, "Dummy", x: 0, y: 150);
            world.Step();
            var aimer = Get(world, "IdleAimer");
            var start = world.Tick;
            long frame = -1;
            for (var step = 0; step < world.Frames(60) && frame < 0; step++)
                if (world.Step().Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == aimer.Id)) frame = (world.Tick - 1 - start) / factor;
            Check.That(frame >= 15, $"{rate}: the unit turns before it fires (first shot in frame {frame}).");
            Check.That(FixedTrig.AngleDifference(aimer.Angle, FixedTrig.PiFraction(1, 2)).Abs() <= Fixed.FromDecimal(0.035m), $"{rate}: it looks at its victim ({aimer.Angle}).");
            reference ??= frame;
            Check.That(frame == reference, $"{rate}: the same frame as at 30 Hz ({frame} against {reference}).");
        }
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
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    feeder = feeder.ForkTo(restored);
                    world = restored;
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
