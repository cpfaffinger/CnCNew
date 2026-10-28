using System.Linq;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;

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
            Mass = 50.0
            ShockResistance = 0.5
            ShockMaxRoll = 0.1
            ForwardFriction = 30.0
            AerodynamicFriction = 3.0
            CenterOfMassOffset = -2.0
            MinFallHeightForDamage = 20.0
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
                   && tank.AerodynamicFriction == Fixed.FromDecimal(0.1m) && tank.CenterOfMassOffset == Fixed.FromInt(-2) && tank.MinFallHeightForDamage == Fixed.FromInt(20),
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
}
