using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const string FlightCrashIni = """
        Weapon VehicleCrashesIntoBuildingWeapon
          PrimaryDamage = 50.0
          PrimaryDamageRadius = 40.0
          AttackRange = 1.0
          DamageType = EXPLOSION
        End
        Weapon VehicleCrashesIntoNonBuildingWeapon
          PrimaryDamage = 5.0
          PrimaryDamageRadius = 15.0
          AttackRange = 1.0
          DamageType = EXPLOSION
        End
        Object CrashHouse
          KindOf = STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 20.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object CrashRock
          KindOf = IMMOBILE
          Geometry = CYLINDER
          GeometryMajorRadius = 8.0
          GeometryHeight = 8.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object CrashHulk
          KindOf = VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 5.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 10.0
          End
        End
        """;

    /// <summary>
    /// <c>PhysicsBehavior::onCollide</c>: a hulk that falls into a building blows up (<c>VehicleCrashesIntoBuildingWeapon</c>) and is gone; one that falls onto a rock does a little damage
    /// (<c>VehicleCrashesIntoNonBuildingWeapon</c>) and stays; both in the same frame at 30, 60 and 120 Hz.
    /// </summary>
    public static void AFallingHulkCrashesIntoABuilding()
    {
        (long Frame, Fixed Damage) Fall(SimulationRate rate, string target)
        {
            var files = new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Crash.ini"] = FlightCrashIni };
            var (world, feeder, _) = RatedWorld(rate, files, NavTerrain());
            feeder.Spawn(2, target, owner: 2, x: 100, y: 100);
            feeder.Spawn(1, "CrashHulk", owner: 1, x: 100, y: 100, z: 80);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var hulk = world.Entities.Single(value => value.Template.Name == "CrashHulk");
            var standing = world.Entities.Single(value => value.Template.Name == target);
            hulk.Modules.OfType<PhysicsBehavior>().First().Launch(world, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, false, false, Fixed.Zero);
            for (var frame = 1; frame <= 120; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                if (world.FindEntity(hulk.Id) is null || standing.Body!.Health < standing.Body.MaxHealth)
                    return (frame, standing.Body!.MaxHealth - standing.Body.Health);
            }
            return (-1, Fixed.Zero);
        }

        var house = new List<long>();
        var rock = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (houseFrame, houseDamage) = Fall(rate, "CrashHouse");
            Check.That(houseFrame > 0 && houseDamage == Fixed.FromInt(50), $"{rate}: the hulk blows up on the building ({houseDamage} damage in frame {houseFrame}).");
            house.Add(houseFrame);
            var (rockFrame, rockDamage) = Fall(rate, "CrashRock");
            Check.That(rockFrame > 0 && rockDamage == Fixed.FromInt(5), $"{rate}: the hulk does a little damage to the rock ({rockDamage} in frame {rockFrame}).");
            rock.Add(rockFrame);
        }
        Check.That(house.Distinct().Count() == 1 && rock.Distinct().Count() == 1, $"The crash falls into the same frame at every rate ({string.Join(", ", house)}; {string.Join(", ", rock)}).");

        // A hulk that meets the building is gone, not dead: no death of it is told.
        var (fallWorld, fallFeeder, _) = RatedWorld(SimulationRate.Hz30, new Dictionary<string, string>(GroupMoveFiles()) { ["Data/INI/Object/Crash.ini"] = FlightCrashIni }, NavTerrain());
        fallFeeder.Spawn(2, "CrashHouse", owner: 2, x: 100, y: 100);
        fallFeeder.Spawn(1, "CrashHulk", owner: 1, x: 100, y: 100, z: 80);
        fallWorld.Step();
        var falling = fallWorld.Entities.Single(value => value.Template.Name == "CrashHulk");
        falling.Modules.OfType<PhysicsBehavior>().First().Launch(fallWorld, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, false, false, Fixed.Zero);
        var died = false;
        for (var step = 0; step < 120 && fallWorld.FindEntity(falling.Id) is not null; step++)
            died |= fallWorld.Step().Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity == falling.Id);
        Check.That(fallWorld.FindEntity(falling.Id) is null && !died, "The hulk is destroyed, not killed.");
    }
}
