using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Shells, missiles, a delayed rifle, a scattering gun and a point-defense laser. Every object keeps the default sphere of radius 1, so the
    /// launch point and the aim point are the object positions (centre offset 0) and all flights run along the x axis at height 0.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ProjectileFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\nFXList FX_Boom\nEnd\nFXList FX_Ignite\nEnd\n",
        ["Data/INI/Object/ProjectileFixture.ini"] = """
        Locomotor FastMissileLoco
          Surfaces = AIR
          Speed = 300
          Acceleration = 9000
          TurnRate = 360
        End
        Locomotor SlowMissileLoco
          Surfaces = AIR
          Speed = 300
          Acceleration = 900
          TurnRate = 360
        End

        Weapon ShellGun
          PrimaryDamage = 50
          PrimaryDamageRadius = 10.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ProjectileObject = TestShell
          ProjectileDetonationFX = FX_Boom
          DelayBetweenShots = 0
        End
        Weapon ArcGun
          PrimaryDamage = 50
          PrimaryDamageRadius = 10.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ProjectileObject = ArcShell
          DelayBetweenShots = 0
        End
        Weapon ScatterGun
          PrimaryDamage = 50
          PrimaryDamageRadius = 5.0
          AttackRange = 400.0
          WeaponSpeed = 300
          ScatterRadius = 40.0
          ProjectileObject = TestShell
          DelayBetweenShots = 0
        End
        Weapon MissileGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 400.0
          ProjectileObject = FastMissile
          ProjectileDetonationFX = FX_Boom
          DelayBetweenShots = 0
        End
        Weapon SlowMissileGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 400.0
          ProjectileObject = SlowMissile
          DelayBetweenShots = 0
        End
        Weapon Rifle
          PrimaryDamage = 25
          AttackRange = 400.0
          WeaponSpeed = 600
          DelayBetweenShots = 0
        End
        Weapon LaserWeapon
          PrimaryDamage = 100
          AttackRange = 200.0
          AntiGround = Yes
          DelayBetweenShots = 0
        End

        Object TestShell
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DumbProjectileBehavior Dumb01
            FirstHeight = 0
            SecondHeight = 0
            FirstPercentIndent = 30%
            SecondPercentIndent = 70%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object ArcShell
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DumbProjectileBehavior Dumb01
            FirstHeight = 30
            SecondHeight = 30
            FirstPercentIndent = 25%
            SecondPercentIndent = 75%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object FastMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL FastMissileLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 0
            IgnitionDelay = 0
            InitialVelocity = 0
            DistanceToTravelBeforeTurning = 0
            DistanceToTargetForLock = 0
            IgnitionFX = FX_Ignite
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SlowMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL SlowMissileLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            IgnitionDelay = 100
            InitialVelocity = 60
            DistanceToTravelBeforeTurning = 0
            DistanceToTargetForLock = 0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object ShellLauncher
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
        Object ArcLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ArcGun
          End
        End
        Object ScatterLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ScatterGun
          End
        End
        Object MissileLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY MissileGun
          End
        End
        Object SlowMissileLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SlowMissileGun
          End
        End
        Object Rifleman
          KindOf = INFANTRY SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY Rifle
          End
        End
        Object Dummy
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object LaserGuard
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PointDefenseLaserUpdate Laser01
            WeaponTemplate = LaserWeapon
            PrimaryTargetTypes = BALLISTIC_MISSILE SMALL_MISSILE
            ScanRate = 0
            ScanRange = 300.0
          End
        End
        """,
    };

    private static SimulationWorld ProjectileWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: ProjectileFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    /// <summary>Spawns a launcher of player 1 at the origin and a 1000-health dummy of player 2 at (<paramref name="distance"/>, 0), fires once, returns the fire tick.</summary>
    private static long FireAtDummy(SimulationWorld world, Feeder feeder, string launcher, int distance, out Entity dummy)
    {
        feeder.Spawn(1, launcher, x: 0, y: 0);
        feeder.Spawn(2, "Dummy", x: distance, y: 0);
        world.Step();
        dummy = Get(world, "Dummy");
        var fireTick = world.Tick;
        Check.That(feeder.FireWeapon(1, Get(world, launcher).Id.Value, dummy.Id.Value).Accepted, $"{launcher} may fire.");
        return fireTick;
    }

    /// <summary>
    /// Straight shell, hand-computed: 200 units at WeaponSpeed 300/s = 10 per tick. Flat control points (heights 0) make the Bézier length exactly the
    /// chord, 200, so the path has ceil(200 / 10) = 20 points; the shell spawns at the end of the fire tick F, sits on point 0..19 in ticks F+1..F+20
    /// and detonates in tick F+21 at the aim point (200, 0, 0), where the damage, the WeaponImpact event and the detonation effect appear.
    /// </summary>
    public static void ProjectileStraightShellFlightTime()
    {
        var world = ProjectileWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "ShellLauncher", 200, out var dummy);
        var launched = world.Step().Events.Single(value => value.Kind == SimEventKind.ProjectileLaunched);
        Check.That(launched.HasPosition && launched.X == Fixed.FromInt(200) && launched.Y == Fixed.Zero, "The launch event carries the aim point.");
        Check.That(world.Step().Events.All(value => value.Kind != SimEventKind.WeaponImpact), "Nothing lands in the first flight tick.");
        var shell = world.Entities.Single(value => value.Template.Name == "TestShell");
        var dumb = shell.Modules.OfType<DumbProjectileBehavior>().Single();
        Check.That(dumb.Segments == 20, $"ceil(200 / 10) = 20 path points (got {dumb.Segments}).");
        Check.That(shell.X == Fixed.Zero, "Tick F+1 puts the shell on the first point, the launch point.");
        while (world.Tick < fire + 21) Check.That(world.Step().Events.All(value => value.Kind != SimEventKind.WeaponImpact), $"No impact before tick F+21 (tick {world.Tick - 1}).");
        Check.That(shell.X == Fixed.FromInt(200) && dummy.Body!.Health == Fixed.FromInt(1000), "Tick F+20 reaches the last point without damage yet.");
        var impact = world.Step();
        Check.That(impact.Tick == fire + 21, "The detonation update is tick F+21.");
        var hit = impact.Events.Single(value => value.Kind == SimEventKind.WeaponImpact);
        Check.That(hit.HasPosition && hit.X == Fixed.FromInt(200) && hit.Entity == shell.Id && hit.Text == "ShellGun", "The impact is reported at the aim point by the shell.");
        var fx = impact.Events.Single(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Boom");
        Check.That(fx.HasPosition && fx.X == Fixed.FromInt(200) && fx.Tick == fire + 21, "ProjectileDetonationFX is requested at the impact point and tick.");
        Check.That(dummy.Body!.Health == Fixed.FromInt(950), "The radius damage hits the dummy at the aim point.");
        Check.That(!world.Entities.Any(value => value.Template.Name == "TestShell"), "The shell is destroyed after detonating.");
    }

    /// <summary>
    /// Ballistic arc: heights 30/30 at 25 %/75 % over 120 units. The reference length (BezierSegment::getApproximateLength, tolerance 1, recomputed
    /// independently in binary64: 131.43) gives ceil(131.43 / 10) = 14 path points, so the shell lands in tick F+15; the path peaks at t = 1/2 with
    /// 3 · 1/4 · 1/2 · 30 · 2 = 22.5 and point 5 (t = 5/13) lies at x = 44.515, z = 21.302.
    /// </summary>
    public static void ProjectileBallisticArc()
    {
        var world = ProjectileWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "ArcLauncher", 120, out var dummy);
        world.Step();
        var shell = world.Entities.Single(value => value.Template.Name == "ArcShell");
        var dumb = shell.Modules.OfType<DumbProjectileBehavior>().Single();
        Check.That(dumb.Segments == 14, $"The arc has 14 path points (got {dumb.Segments}).");
        var peak = Fixed.Zero;
        long impactTick = -1;
        while (world.Tick < fire + 30 && impactTick < 0)
        {
            var result = world.Step();
            if (result.Tick == fire + 6)
            {
                Check.That((shell.X - Fixed.FromDecimal(44.515m)).Abs() < Fixed.FromDecimal(0.01m) && (shell.Z - Fixed.FromDecimal(21.302m)).Abs() < Fixed.FromDecimal(0.01m),
                    $"Point 5 of the arc is (44.515, 21.302), got ({shell.X}, {shell.Z}).");
            }
            if (shell.Z > peak) peak = shell.Z;
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponImpact)) impactTick = result.Tick;
        }
        Check.That(impactTick == fire + 15, $"The arc lands in tick F+15 (got F+{impactTick - fire}).");
        Check.That(peak > Fixed.FromInt(21) && peak <= Fixed.FromDecimal(22.5m), $"The arc stays below its analytic peak of 22.5 (got {peak}).");
        Check.That(dummy.Body!.Health == Fixed.FromInt(950), "The shell lands on the dummy.");
    }

    /// <summary>
    /// Missile, hand-computed: ignition at once, InitialVelocity 0, acceleration 9000/s² (10 per tick²) up to 300/s (10 per tick), no lock distance.
    /// Tick F+1 ignites and flies to x = 10; F+2 (attack without turning) to 20; from F+3 it steers at the dummy; at the start of F+10 it is 10 away,
    /// no more than one step, so it lands exactly on the dummy and detonates in tick F+10; tick F+13 (KillSelfDelay 3) removes it.
    /// </summary>
    public static void ProjectileMissileFlightTime()
    {
        var world = ProjectileWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "MissileLauncher", 100, out var dummy);
        world.Step();
        var missile = world.Entities.Single(value => value.Template.Name == "FastMissile");
        var ignition = world.Step();
        Check.That(missile.X == Fixed.FromInt(10), $"Tick F+1 ignites and moves one full step (x = {missile.X}).");
        Check.That(ignition.Events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Ignite"), "IgnitionFX is requested at ignition.");
        long impactTick = -1;
        while (world.Tick <= fire + 20 && impactTick < 0)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponImpact)) impactTick = result.Tick;
        }
        Check.That(impactTick == fire + 10, $"The missile detonates in tick F+10 (got F+{impactTick - fire}).");
        Check.That(missile.X == Fixed.FromInt(100) && dummy.Body!.Health == Fixed.FromInt(940), "It detonates on the dummy.");
        for (var tick = 0; tick < 2; tick++) world.Step();
        Check.That(world.Entities.Any(value => value.Template.Name == "FastMissile"), "The missile waits KillSelfDelay before it removes itself.");
        world.Step();
        Check.That(!world.Entities.Any(value => value.Template.Name == "FastMissile"), "Tick F+13, KillSelfDelay (3) after the detonation, removes the missile.");
    }

    /// <summary>
    /// Missile with ignition delay and acceleration: IgnitionDelay 100 ms = 3 ticks, InitialVelocity 60/s = 2 per tick, acceleration 900/s² = 1 per tick².
    /// Coast: x = 2, 4 (F+1, F+2); ignition at F+3 (speed 3, x = 7); then speed 4..10: x = 11, 16, 22, 29, 37, 46, 56, 66, 76, 86, 96 (F+4..F+14);
    /// at F+15 the dummy (x = 100) is 4 away and the missile lands.
    /// </summary>
    public static void ProjectileMissileAcceleration()
    {
        var world = ProjectileWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "SlowMissileLauncher", 100, out _);
        world.Step();
        var missile = world.Entities.Single(value => value.Template.Name == "SlowMissile");
        int[] expected = [2, 4, 7, 11, 16, 22, 29, 37, 46, 56, 66, 76, 86, 96];
        foreach (var x in expected)
        {
            world.Step();
            Check.That(missile.X == Fixed.FromInt(x), $"Tick F+{world.Tick - 1 - fire}: expected x = {x}, got {missile.X}.");
        }
        var landing = world.Step();
        Check.That(landing.Tick == fire + 15 && landing.Events.Any(value => value.Kind == SimEventKind.WeaponImpact), "The missile lands in tick F+15.");
    }

    /// <summary>A weapon without a projectile: 150 units at 600/s (20 per tick) land ceil(7.5) = 8 ticks later; within one tick's travel the hit is immediate.</summary>
    public static void ProjectileDelayedHit()
    {
        var world = ProjectileWorld(out _, out var feeder);
        var fire = FireAtDummy(world, feeder, "Rifleman", 150, out var dummy);
        var shot = world.Step();
        Check.That(shot.Events.Single(value => value.Kind == SimEventKind.WeaponFired).Value == 0, "A delayed shot reports no damage at firing.");
        var launched = shot.Events.Single(value => value.Kind == SimEventKind.ProjectileLaunched);
        Check.That(launched.Value == fire + world.Frames(8) && !launched.Entity.IsValid, "The launch event names the landing tick F+8 and no projectile object.");
        Check.That(world.PendingDelayedDamage == 1, "One delayed hit is pending.");
        while (world.Tick < fire + world.Frames(8)) world.Step();
        Check.That(dummy.Body!.Health == Fixed.FromInt(1000), "No damage before tick F+8.");
        var landing = world.Step();
        Check.That(landing.Events.Single(value => value.Kind == SimEventKind.WeaponImpact).Value == Fixed.FromInt(25).Raw, "The impact reports the damage.");
        Check.That(dummy.Body.Health == Fixed.FromInt(975) && world.PendingDelayedDamage == 0, "The hit lands in tick F+8.");

        var near = ProjectileWorld(out _, out var nearFeeder);
        FireAtDummy(near, nearFeeder, "Rifleman", 15, out var close);
        var instant = near.Step();
        Check.That(instant.Events.Single(value => value.Kind == SimEventKind.WeaponFired).Value == Fixed.FromInt(25).Raw && close.Body!.Health == Fixed.FromInt(975),
            "Below one tick of travel the damage is dealt at once.");
    }

    /// <summary>ScatterRadius: the shell flies to a random ground point within 40 of the dummy (two logic draws), the same one in every run.</summary>
    public static void ProjectileScatter()
    {
        (Fixed X, Fixed Y) Aim()
        {
            var world = ProjectileWorld(out _, out var feeder);
            FireAtDummy(world, feeder, "ScatterLauncher", 200, out _);
            var launched = world.Step().Events.Single(value => value.Kind == SimEventKind.ProjectileLaunched);
            return (launched.X, launched.Y);
        }
        var first = Aim();
        var second = Aim();
        Check.That(first == second, "Scatter is deterministic.");
        var dx = first.X - Fixed.FromInt(200);
        var dy = first.Y;
        Check.That(dx * dx + dy * dy <= Fixed.FromInt(1600), "The aim point lies within the scatter radius.");
        Check.That(first != (Fixed.FromInt(200), Fixed.Zero), "The aim point moved.");
    }

    /// <summary>PointDefenseLaserUpdate shoots the missile down before it reaches the dummy.</summary>
    public static void ProjectilePointDefenseLaser()
    {
        var world = ProjectileWorld(out _, out var feeder);
        feeder.Spawn(2, "LaserGuard", x: 150, y: 0);
        var fire = FireAtDummy(world, feeder, "MissileLauncher", 300, out var dummy);
        var shotDown = false;
        while (world.Tick < fire + world.Frames(40))
        {
            var result = world.Step();
            shotDown |= result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Text == "LaserWeapon");
        }
        Check.That(shotDown, "The laser fires at the missile.");
        Check.That(dummy.Body!.Health == Fixed.FromInt(1000), "The missile never reaches the dummy.");
        Check.That(!world.Entities.Any(value => value.Template.Name == "FastMissile"), "The missile is gone.");
    }

    /// <summary>Shells, missiles and delayed hits in flight survive a snapshot (v7) and two fresh runs agree tick by tick.</summary>
    public static void ProjectileSnapshotAndDeterminism()
    {
        List<ulong> Run(int snapshotAt)
        {
            var world = ProjectileWorld(out var fixture, out var feeder);
            feeder.Spawn(1, "ShellLauncher", x: 0, y: 0);
            feeder.Spawn(1, "MissileLauncher", x: 0, y: 20);
            feeder.Spawn(1, "Rifleman", x: 0, y: 40);
            feeder.Spawn(1, "ScatterLauncher", x: 0, y: 60);
            feeder.Spawn(2, "Dummy", x: 250, y: 30);
            world.Step();
            var dummy = Get(world, "Dummy").Id.Value;
            foreach (var name in new[] { "ShellLauncher", "MissileLauncher", "Rifleman", "ScatterLauncher" })
                feeder.FireWeapon(1, Get(world, name).Id.Value, dummy);
            var hashes = new List<ulong>();
            for (var tick = 0; tick < world.Frames(40); tick++)
            {
                if (tick == snapshotAt)
                {
                    var bytes = WorldSnapshot.Write(world);
                    world = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
                    Check.That(world.PendingDelayedDamage == 1 && world.Entities.Any(value => value.Template.Name == "FastMissile"), "The snapshot carries the flights.");
                }
                hashes.Add(world.Step().StateHash);
            }
            return hashes;
        }
        var first = Run(-1);
        Check.That(first.SequenceEqual(Run(-1)), "Two fresh runs are identical.");
        Check.That(first.SequenceEqual(Run(5)), "A snapshot taken mid-flight continues identically.");
    }
}
