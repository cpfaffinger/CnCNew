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
}
