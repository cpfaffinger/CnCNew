using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// <c>WeaponTemplate::shouldProjectileCollideWith</c>: a projectile meets what its weapon's <c>ProjectileCollidesWith</c> names on its way. Before, the key was read by nobody, and every shell flew
/// through every building to its victim.
/// </summary>
static partial class Tests
{
    private const string CollisionIni = """
        Weapon CollGun
          PrimaryDamage = 50
          PrimaryDamageRadius = 10.0
          AttackRange = 500.0
          WeaponSpeed = 300
          ProjectileObject = CollShell
          DelayBetweenShots = 100000
        End
        Weapon HistGun
          PrimaryDamage = 1
          PrimaryDamageRadius = 5.0
          AttackRange = 500.0
          DelayBetweenShots = 0
          HistoricBonusTime = 3000
          HistoricBonusCount = 3
          HistoricBonusRadius = 20
          HistoricBonusWeapon = HistStorm
        End
        Weapon HistStorm
          PrimaryDamage = 40
          PrimaryDamageRadius = 5.0
          AttackRange = 500.0
          DelayBetweenShots = 0
        End
        Weapon PatternGun
          PrimaryDamage = 7
          PrimaryDamageRadius = 5.0
          AttackRange = 500.0
          DelayBetweenShots = 0
          ClipSize = 3
          ClipReloadTime = 1000
          ScatterTargetScalar = 100
          ScatterTarget = X: 0.000 Y: 1.000
          ScatterTarget = X: 1.000 Y: 0.000
          ScatterTarget = X: -1.000 Y: 0.000
        End
        Object PatternShooter
          KindOf = VEHICLE SELECTABLE
          VisionRange = 800.0
          ShroudClearingRange = 800.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY PatternGun
          End
        End
        Object CollShell
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DumbProjectileBehavior Dumb01
            FirstHeight = 10
            SecondHeight = 10
            FirstPercentIndent = 25%
            SecondPercentIndent = 75%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object CollShooter
          KindOf = VEHICLE SELECTABLE
          VisionRange = 500.0
          ShroudClearingRange = 500.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY CollGun
          End
        End
        Object CollVictim
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object CollWall
          KindOf = STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 40.0
          GeometryHeight = 100.0
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, Entity Shooter, Entity Victim, Entity Wall) CollisionWorld(SimulationRate rate, int wallOwner)
    {
        var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Collision.ini"] = CollisionIni });
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "CollShooter", owner: 1, x: 100, y: 100);
        feeder.Spawn(2, "CollVictim", owner: 2, x: 400, y: 100);
        feeder.Spawn(wallOwner, "CollWall", owner: wallOwner, x: 250, y: 100);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return (world, feeder, world.Entities.Single(value => value.Template.Name == "CollShooter"), world.Entities.Single(value => value.Template.Name == "CollVictim"),
            world.Entities.Single(value => value.Template.Name == "CollWall"));
    }

    public static void ProjectilesMeetWhatStandsInTheirWay()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, shooter, victim, wall) = CollisionWorld(rate, 2);
            Check.That(feeder.FireWeapon(1, shooter.Id.Value, victim.Id.Value).Accepted, $"{rate}: the gun fires.");
            for (var step = 0; step < world.Frames(120); step++) world.Step();
            Check.That(wall.Body!.Health < wall.Body.MaxHealth, $"{rate}: the shell hits the wall of the enemy.");
            Check.That(victim.Body!.Health == victim.Body.MaxHealth, $"{rate}: and not what stands behind it.");

            // The default mask is STRUCTURES: not the player's own, which is CONTROLLED_STRUCTURES.
            (world, feeder, shooter, victim, wall) = CollisionWorld(rate, 1);
            Check.That(feeder.FireWeapon(1, shooter.Id.Value, victim.Id.Value).Accepted, $"{rate}: the gun fires over its own wall.");
            for (var step = 0; step < world.Frames(120); step++) world.Step();
            Check.That(wall.Body!.Health == wall.Body.MaxHealth && victim.Body!.Health < victim.Body.MaxHealth, $"{rate}: its own building lets the shell through.");
        }
    }

    /// <summary><c>dealDamageInternal</c>: the third hit close enough in time and place sets off the bonus weapon, and the list starts anew; a hit elsewhere or too late does not count.</summary>
    public static void ThreeHitsOfNapalmMakeAFirestorm()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var (world, feeder, shooter, victim, _) = CollisionWorld(rate, 2);
            Check.That(world.Templates.Weapons.TryGet("HistGun", out var gun), "The weapon is there.");
            Fixed Hurt() => victim.Body!.MaxHealth - victim.Body.Health;
            void Hit(int dx) => world.DealWeaponDamage(shooter, gun, null, victim.X + Fixed.FromInt(dx), victim.Y, victim.Z);
            Hit(0);
            Hit(0);
            world.Step();
            Check.That(Hurt() == Fixed.FromInt(2), $"{rate}: two hits are two hits ({Hurt()}).");
            Hit(100);
            Hit(0);
            for (var step = 0; step < world.Frames(5); step++) world.Step();
            Check.That(Hurt() == Fixed.FromInt(43), $"{rate}: the third hit at that place sets off the storm ({Hurt()}).");
            Hit(0);
            Hit(0);
            for (var step = 0; step < world.Frames(120); step++) world.Step();
            Hit(0);
            for (var step = 0; step < world.Frames(5); step++) world.Step();
            Check.That(Hurt() == Fixed.FromInt(46), $"{rate}: a hit after the time counts for nothing ({Hurt()}).");
        }
    }

    /// <summary><c>Weapon::privateFireWeapon</c>: every offset of the pattern once in a clip, and the victim itself is not hit.</summary>
    public static void AClipGoesOutInItsPattern()
    {
        foreach (var rate in new[] { SimulationRate.FromTicksPerSecond(30), SimulationRate.FromTicksPerSecond(60), SimulationRate.FromTicksPerSecond(120) })
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Collision.ini"] = CollisionIni });
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "PatternShooter", owner: 1, x: 100, y: 300);
            feeder.Spawn(2, "CollVictim", owner: 2, x: 400, y: 300);
            foreach (var (x, y) in new[] { (400, 400), (500, 300), (300, 300) }) feeder.Spawn(2, "CollVictim", owner: 2, x: x, y: y);
            world.Step();
            var shooter = world.Entities.Single(value => value.Template.Name == "PatternShooter");
            var victims = world.Entities.Where(value => value.Template.Name == "CollVictim").OrderBy(value => value.Id.Value).ToArray();
            for (var shot = 0; shot < 3; shot++)
            {
                while (world.Tick < shooter.ReadyTickOf(0)) world.Step();
                Check.That(feeder.FireWeapon(1, shooter.Id.Value, victims[0].Id.Value).Accepted, $"{rate}: shot {shot} is fired.");
                world.Step();
            }
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            Check.That(victims[0].Body!.Health == victims[0].Body!.MaxHealth, $"{rate}: the victim itself is not hit.");
            Check.That(victims.Skip(1).All(value => value.Body!.MaxHealth - value.Body.Health == Fixed.FromInt(7)), $"{rate}: every place of the pattern once ({string.Join(", ", victims.Select(value => value.Body!.Health))}).");
        }
    }
}
