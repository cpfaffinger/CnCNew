using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>F-83: the abilities that hack and that aim (<c>SpecialAbilityUpdate::triggerAbilityEffect</c>): a disable for <c>EffectDuration</c>, the cash hack and the laser lock.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> AbilityHackFiles() => new Dictionary<string, string>(GroundRateFiles())
    {
        ["Data/INI/Object/AbilityHackFixture.ini"] = """
        Weapon HackRifle
          PrimaryDamage = 1
          AttackRange = 100.0
          DamageType = EXPLOSION
          DelayBetweenShots = 1000
        End
        Weapon HackMissile
          PrimaryDamage = 20
          AttackRange = 200.0
          DamageType = EXPLOSION
          DelayBetweenShots = 500
        End
        SpecialPower SPECIAL_TestHackBuilding
          Enum = SPECIAL_HACKER_DISABLE_BUILDING
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestHackVehicle
          Enum = SPECIAL_BLACKLOTUS_DISABLE_VEHICLE_HACK
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestStealCash
          Enum = SPECIAL_BLACKLOTUS_STEAL_CASH_HACK
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestLaser
          Enum = SPECIAL_MISSILE_DEFENDER_LASER_GUIDED_MISSILES
          ReloadTime = 0
        End
        Object HackBeam
          KindOf = INERT NO_COLLIDE
          Body = ActiveBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
        End
        Object HackHacker
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY HackRifle
            Weapon = SECONDARY HackMissile
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Hack01
            SpecialPowerTemplate = SPECIAL_TestHackBuilding
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate HackUpdate01
            SpecialPowerTemplate = SPECIAL_TestHackBuilding
            StartAbilityRange = 150.0
            PreparationTime = 1000
            EffectDuration = 3000
            SpecialObject = HackBeam
          End
          Behavior = SpecialAbility Vehicle01
            SpecialPowerTemplate = SPECIAL_TestHackVehicle
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate VehicleUpdate01
            SpecialPowerTemplate = SPECIAL_TestHackVehicle
            StartAbilityRange = 150.0
            PreparationTime = 1000
            EffectDuration = 2000
            SpecialObject = HackBeam
          End
          Behavior = SpecialAbility Cash01
            SpecialPowerTemplate = SPECIAL_TestStealCash
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate CashUpdate01
            SpecialPowerTemplate = SPECIAL_TestStealCash
            StartAbilityRange = 150.0
            PreparationTime = 1000
            SpecialObject = HackBeam
          End
          Behavior = SpecialAbility Laser01
            SpecialPowerTemplate = SPECIAL_TestLaser
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate LaserUpdate01
            SpecialPowerTemplate = SPECIAL_TestLaser
            StartAbilityRange = 150.0
            AbilityAbortRange = 250.0
            PreparationTime = 1000
            PersistentPrepTime = 500
            SpecialObject = HackBeam
          End
        End
        Object HackBuilding
          KindOf = SELECTABLE STRUCTURE IMMOBILE CAPTURABLE CASH_GENERATOR
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        Object HackVehicle
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, Entity Hacker, Entity Building, Entity Vehicle) HackScene(SimulationRate rate)
    {
        var (world, feeder, _) = RatedWorld(rate, AbilityHackFiles());
        feeder.GiveMoney(2, 1500);
        feeder.Spawn(1, "HackHacker", owner: 1, x: 100, y: 100);
        feeder.Spawn(2, "HackBuilding", owner: 2, x: 200, y: 100);
        feeder.Spawn(2, "HackVehicle", owner: 2, x: 100, y: 200);
        for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
        return (world, feeder, ChargeFind(world, "HackHacker"), ChargeFind(world, "HackBuilding"), ChargeFind(world, "HackVehicle"));
    }

    /// <summary>
    /// A hacker disables a building for its effect's duration after its preparation, Black Lotus a vehicle; the frame the disable begins and ends in is the same at 30, 60 and 120 Hz. A building that
    /// is no capturable one, or a vehicle for the building's hack, is refused.
    /// </summary>
    public static void AbilityHacksDisableTheirTarget()
    {
        string? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, hacker, building, vehicle) = HackScene(rate);
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestHackBuilding", SpecialPowerTargetKind.Object, vehicle.Id.Value).Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: the building hack takes no vehicle.");
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestHackVehicle", SpecialPowerTargetKind.Object, building.Id.Value).Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: the vehicle hack takes no building.");
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestHackBuilding", SpecialPowerTargetKind.Object, building.Id.Value).Accepted, $"{rate}: the building hack is accepted.");
            var (from, to) = (-1L, -1L);
            for (var step = 0; step < world.Frames(300) && to < 0; step++)
            {
                world.Step();
                var hacked = (world.DisabledFlagsOf(building) & DisabledMask.Hacked) != 0;
                if (hacked && from < 0) from = (world.Tick - 1) / world.Time.TicksPerOriginalFrame;
                if (!hacked && from >= 0) to = (world.Tick - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(from > 0 && to > from, $"{rate}: the building is hacked for a while ({from} to {to}).");
            Check.That(world.Entities.All(value => value.Template.Name != "HackBeam"), $"{rate}: the beam is gone.");
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestHackVehicle", SpecialPowerTargetKind.Object, vehicle.Id.Value).Accepted, $"{rate}: the vehicle hack is accepted.");
            for (var step = 0; step < world.Frames(45); step++) world.Step();
            Check.That((world.DisabledFlagsOf(vehicle) & DisabledMask.Hacked) != 0, $"{rate}: the vehicle is hacked.");
            var line = $"{from}-{to}";
            reference ??= line;
            Check.That(line == reference, $"{rate}: the frames of 30 Hz ({line} against {reference}).");
        }
    }

    /// <summary>The cash hack takes a thousand or what there is.</summary>
    public static void AbilityStealsCash()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, hacker, building, _) = HackScene(rate);
            for (var round = 0; round < 2; round++)
            {
                Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestStealCash", SpecialPowerTargetKind.Object, building.Id.Value).Accepted, $"{rate}: the hack {round + 1} is accepted.");
                for (var step = 0; step < world.Frames(45); step++) world.Step();
            }
            Check.That(world.MoneyOf(new PlayerId(1)).Floor() == 1500 && world.MoneyOf(new PlayerId(2)).Floor() == 0, $"{rate}: the money has moved ({world.MoneyOf(new PlayerId(1))}, {world.MoneyOf(new PlayerId(2))}).");
        }
    }

    /// <summary>The laser lock aims, then the unit fires its secondary weapon at the vehicle until told otherwise; another order frees the weapon.</summary>
    public static void AbilityLocksAndFiresTheLaserMissile()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, hacker, building, vehicle) = HackScene(rate);
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestLaser", SpecialPowerTargetKind.Object, building.Id.Value).Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: the laser takes no building.");
            var health = vehicle.Body!.Health;
            Check.That(feeder.UseSpecialPower(1, hacker.Id.Value, "SPECIAL_TestLaser", SpecialPowerTargetKind.Object, vehicle.Id.Value).Accepted, $"{rate}: the laser is accepted.");
            for (var step = 0; step < world.Frames(120); step++) world.Step();
            Check.That(vehicle.Body!.Health <= health - CnCNew.Simulation.Numerics.Fixed.FromInt(20) * CnCNew.Simulation.Numerics.Fixed.FromInt(3), $"{rate}: the missiles hit ({vehicle.Body!.Health}).");
        }
    }
}
