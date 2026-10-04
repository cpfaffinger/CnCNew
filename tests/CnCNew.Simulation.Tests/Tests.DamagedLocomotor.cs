using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary><c>SpeedDamaged</c>/<c>TurnRateDamaged</c>/<c>AccelerationDamaged</c> (<c>Locomotor::getMaxSpeedForCondition</c>): a really damaged object moves with its locomotor's damaged constants.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> DamagedLocomotorFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/DamagedLocomotorFixture.ini"] = """
        Locomotor LimpLoco
          Speed = 90.0
          SpeedDamaged = 30.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor SturdyLoco
          Speed = 90.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object LimpTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL LimpLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object SturdyTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL SturdyLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    public static void DamagedLocomotorSlowsAReallyDamagedObject()
    {
        var fixture = new SyntheticFixture(files: DamagedLocomotorFiles());
        fixture.Templates.TryGet("LimpTank", out var limp);
        fixture.Templates.TryGet("SturdyTank", out var sturdy);
        var loco = limp!.Locomotor!;
        Check.That(loco.Damaged is { } damaged && damaged.Speed * Fixed.FromInt(3) == loco.Speed && damaged.TurnRatePerTick == loco.TurnRatePerTick && damaged.Acceleration == loco.Acceleration,
            "SpeedDamaged is read; the keys that are absent keep the undamaged value.");
        Check.That(sturdy!.Locomotor!.Damaged is null, "A locomotor without damaged keys has no damaged form.");
        Check.That(loco.PenaltyDamageState == BodyDamageStates.ReallyDamaged && ReferenceEquals(loco.ForCondition(BodyDamageStates.Damaged), loco) && ReferenceEquals(loco.ForCondition(BodyDamageStates.ReallyDamaged), loco.Damaged),
            "The penalty starts at MovementPenaltyDamageState (REALLYDAMAGED when GameData does not say otherwise).");

        decimal Travel(string template, decimal damage)
        {
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, template, owner: 1, x: 0, y: 0);
            world.Step();
            if (damage > 0) feeder.Damage(1, damage);
            world.Step();
            feeder.SetMoveTarget(1, 1, 2000, 0);
            for (var i = 0; i < 60; i++) world.Step();
            return world.FindEntity(new CnCNew.Simulation.Ids.EntityId(1))!.X.ToDecimal();
        }

        var pristine = Travel("LimpTank", 0);
        var damagedOnly = Travel("LimpTank", 500);   // 50 % health: DAMAGED, above the penalty state
        var reallyDamaged = Travel("LimpTank", 800); // 20 % health: REALLYDAMAGED
        var sturdyHurt = Travel("SturdyTank", 800);
        Check.That(pristine > 170 && damagedOnly == pristine, $"A merely damaged object keeps its speed ({pristine} vs {damagedOnly}).");
        Check.That(reallyDamaged > pristine / 3 - 5 && reallyDamaged < pristine / 3 + 5, $"A really damaged object moves at SpeedDamaged, a third of the speed ({reallyDamaged} vs {pristine}).");
        Check.That(sturdyHurt == pristine, $"An object whose locomotor has no damaged keys is not slowed ({sturdyHurt}).");
    }
}
