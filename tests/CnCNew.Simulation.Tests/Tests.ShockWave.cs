using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Fidelity finding F-63, the weapon's shock wave (docs/SHOCK-WAVE.md).</summary>
static partial class Tests
{
    private static readonly IReadOnlyDictionary<string, string> ShockFiles = new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ShockFixture.ini"] = """
        Weapon ShockBomb
          PrimaryDamage = 10
          PrimaryDamageRadius = 100.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          ShockWaveAmount = 50.0
          ShockWaveRadius = 100.0
          ShockWaveTaperOff = 0.33
        End
        Object ShockTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
            ShockResistance = 0.5
            ShockMaxRoll = 0.1
            ForwardFriction = 30.0
            AerodynamicFriction = 3.0
            CenterOfMassOffset = -2.0
            MinFallHeightForDamage = 1.0
          End
        End
        Object ShockWall
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
            ShockResistance = 1.0
          End
        End
        Object ShockLauncher
          KindOf = STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Locomotor ShockFeet
          Surfaces = GROUND
          Speed = 20
          StickToGround = Yes
        End
        Object ShockSoldier
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL ShockFeet
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>
    /// Step 1: the three shock wave keys of a weapon and the physics keys the flight reads are read - the frictions per logic frame of the original (<c>parseFrictionPerSec</c>),
    /// everything else as written - and a module that names none of them has the defaults of <c>PhysicsBehaviorModuleData</c>.
    /// </summary>
    public static void ShockWaveFieldsAreRead()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: ShockFiles);
        Check.That(fixture.Templates.Weapons.TryGet("ShockBomb", out var bomb), "Setup: the weapon is in the catalog.");
        Check.That(bomb!.ShockWaveAmount == Fixed.FromInt(50) && bomb.ShockWaveRadius == Fixed.FromInt(100) && bomb.ShockWaveTaperOff == Fixed.FromDecimal(0.33m),
            $"The shock wave keys are read ({bomb.ShockWaveAmount}, {bomb.ShockWaveRadius}, {bomb.ShockWaveTaperOff}).");
        PhysicsData Physics(string name) => fixture.Templates.Templates.Single(value => value.Name == name).Modules.Select(module => module.Data).OfType<PhysicsData>().Single();
        var tank = Physics("ShockTank");
        Check.That(tank.ShockResistance == Fixed.FromDecimal(0.5m) && tank.ShockMaxRoll == Fixed.FromDecimal(0.1m) && tank.ForwardFriction == Fixed.One
                   && tank.AerodynamicFriction == Fixed.FromDecimal(0.1m) && tank.CenterOfMassOffset == Fixed.FromInt(-2) && tank.MinFallHeightForDamage == Fixed.One,
            $"The physics keys are read, the frictions per frame ({tank.ForwardFriction}, {tank.AerodynamicFriction}).");
        var soldier = Physics("ShockSoldier");
        Check.That(soldier.ShockResistance == Fixed.Zero && soldier.ShockMaxYaw == Fixed.FromDecimal(0.05m) && soldier.ShockMaxPitch == Fixed.FromDecimal(0.025m)
                   && soldier.ShockMaxRoll == Fixed.FromDecimal(0.025m) && soldier.ForwardFriction == Fixed.FromDecimal(0.15m) && soldier.LateralFriction == Fixed.FromDecimal(0.15m)
                   && soldier.ZFriction == Fixed.FromDecimal(0.8m) && soldier.AerodynamicFriction == Fixed.Zero && soldier.MinFallHeightForDamage == Fixed.FromInt(40)
                   && soldier.FallHeightDamageFactor == Fixed.One && soldier.PitchRollYawFactor == Fixed.FromInt(2),
            "Without the keys, the defaults of PhysicsBehaviorModuleData.");
        Check.That(fixture.Templates.Templates.Single(value => value.Name == "ShockSoldier").Locomotor is { StickToGround: true }, "A locomotor's StickToGround is read.");
        Check.That(fixture.Templates.GroundStiffness == Fixed.FromDecimal(0.5m), $"Without GroundStiffness in GameData the original's default 0.5 ({fixture.Templates.GroundStiffness}).");
    }

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) ShockWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: ShockFiles);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>One blast of the shock bomb from a launcher at the origin, then the flights to their end: where each object rests, how high it flew, its facing and health.</summary>
    private static Dictionary<string, (Fixed X, Fixed Y, Fixed Top, Fixed Angle, Fixed Health, bool Flew)> ShockRun(SimulationRate rate, bool saveMidway = false)
    {
        var (world, feeder, fixture) = ShockWorld(rate);
        feeder.Spawn(1, "ShockLauncher", owner: 1, x: 0, y: 0);
        feeder.Spawn(2, "ShockTank", owner: 2, x: 50, y: 0);
        feeder.Spawn(2, "ShockWall", owner: 2, x: 0, y: 50);
        feeder.Spawn(2, "ShockSoldier", owner: 2, x: -50, y: 0);
        world.Step();
        var launcher = world.Entities.Single(value => value.Template.Name == "ShockLauncher");
        Check.That(world.Weapons.TryGet("ShockBomb", out var bomb), "Setup: the bomb is in the catalog.");
        var names = new[] { "ShockTank", "ShockWall", "ShockSoldier" };
        var top = names.ToDictionary(name => name, _ => Fixed.Zero);
        var flew = names.ToDictionary(name => name, _ => false);
        world.DealWeaponDamage(launcher, bomb!, null, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        for (var step = 0; step < world.Frames(600); step++)
        {
            if (saveMidway && step == world.Frames(10))
            {
                var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
                Check.That(restored.StateHash == world.StateHash, $"{rate}: a world saved in mid-flight restores to the same state.");
                world = restored;
            }
            world.Step();
            foreach (var name in names)
            {
                var entity = world.Entities.Single(value => value.Template.Name == name);
                if (entity.Z > top[name]) top[name] = entity.Z;
                if (entity.InPhysicsFlight) flew[name] = true;
            }
            if (step > world.Frames(5) && world.Entities.All(value => !value.InPhysicsFlight)) break;
        }
        return names.ToDictionary(name => name, name =>
        {
            var entity = world.Entities.Single(value => value.Template.Name == name);
            return (entity.X, entity.Y, top[name], entity.Angle, entity.Body!.Health, flew[name]);
        });
    }

    /// <summary>
    /// Step 2, the flight: a light tank 50 east of the blast flies away from it, rises and lands, turned by the random rates, and takes the blast's damage and the falling damage;
    /// an object with <c>ShockResistance</c> 1 does not move; a soldier whose locomotor sticks to the ground slides along it without rising or turning. The same at 30, 60 and
    /// 120 Hz, and the same after a save in mid-flight.
    /// </summary>
    public static void ShockWaveFlight()
    {
        var reference = ShockRun(SimulationRate.Hz30);
        var tank = reference["ShockTank"];
        Check.That(tank.Flew && tank.X > Fixed.FromInt(60) && tank.Y.Abs() < Fixed.One && tank.Top > Fixed.FromInt(20), $"The tank flew away from the blast and up ({tank.X}, {tank.Y}, top {tank.Top}).");
        Check.That(tank.Angle != Fixed.Zero, "The random yaw turned it.");
        Check.That(tank.Health < Fixed.FromInt(90), $"It took the blast and a fall ({tank.Health} of 100).");
        var wall = reference["ShockWall"];
        Check.That(wall.X == Fixed.Zero && wall.Y == Fixed.FromInt(50) && wall.Top == Fixed.Zero, $"Shock resistance 1: it does not move ({wall.X}, {wall.Y}, top {wall.Top}).");
        var soldier = reference["ShockSoldier"];
        Check.That(soldier.X < Fixed.FromInt(-60) && soldier.Top == Fixed.Zero && soldier.Angle == Fixed.Zero, $"The soldier slid away without rising or turning ({soldier.X}, top {soldier.Top}, {soldier.Angle}).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = ShockRun(rate);
            foreach (var (name, values) in reference)
                Check.That(other[name] == values, $"{rate}: {name} ends where it ends at 30 Hz ({other[name]} against {values}).");
        }
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60 })
        {
            var saved = ShockRun(rate, saveMidway: true);
            foreach (var (name, values) in reference)
                Check.That(saved[name] == values, $"{rate}: after a save in mid-flight {name} ends the same ({saved[name]} against {values}).");
        }
    }
}
