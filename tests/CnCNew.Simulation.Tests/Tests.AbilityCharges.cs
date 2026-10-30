using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-83: the abilities that plant a charge (<c>SpecialAbilityUpdate::triggerAbilityEffect</c>, <c>StickyBombUpdate</c>). Until now a <c>SpecialAbilityUpdate</c> staged the two capture powers only,
/// and these orders were refused as <c>SpecialPowerHandlerMissing</c>.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> AbilityChargeFiles() => new Dictionary<string, string>(GroundRateFiles())
    {
        ["Data/INI/Object/AbilityChargeFixture.ini"] = """
        Weapon ChargeBlast
          PrimaryDamage = 400
          PrimaryDamageRadius = 5.0
          DamageType = EXPLOSION
          AttackRange = 10.0
        End
        SpecialPower SPECIAL_TestRemote
          Enum = SPECIAL_REMOTE_CHARGES
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestTimed
          Enum = SPECIAL_TIMED_CHARGES
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestTnt
          Enum = SPECIAL_TANKHUNTER_TNT_ATTACK
          ReloadTime = 0
        End
        SpecialPower SPECIAL_TestTrap
          Enum = SPECIAL_BOOBY_TRAP
          ReloadTime = 0
        End
        Object ChargeRemote
          KindOf = PROJECTILE MINE NO_COLLIDE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = StickyBombUpdate Sticky01
            GeometryBasedDamageWeapon = ChargeBlast
          End
        End
        Object ChargeTimed
          KindOf = PROJECTILE MINE NO_COLLIDE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 2000
            MaxLifetime = 2000
          End
          Behavior = StickyBombUpdate Sticky01
          End
        End
        Object ChargeTrap
          KindOf = BOOBY_TRAP NO_COLLIDE MINE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = StickyBombUpdate Sticky01
            GeometryBasedDamageWeapon = ChargeBlast
          End
        End
        Object ChargeBurton
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 3000
          End
          Behavior = SpecialAbility Remote01
            SpecialPowerTemplate = SPECIAL_TestRemote
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate RemoteUpdate01
            SpecialPowerTemplate = SPECIAL_TestRemote
            StartAbilityRange = 5.0
            PreparationTime = 1000
            SpecialObject = ChargeRemote
            MaxSpecialObjects = 2
            SpecialObjectsPersistent = Yes
            FleeRangeAfterCompletion = 60.0
          End
          Behavior = SpecialAbility Timed01
            SpecialPowerTemplate = SPECIAL_TestTimed
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate TimedUpdate01
            SpecialPowerTemplate = SPECIAL_TestTimed
            StartAbilityRange = 5.0
            PreparationTime = 500
            SpecialObject = ChargeTimed
            MaxSpecialObjects = 2
            SpecialObjectsPersistent = Yes
            SpecialObjectsPersistWhenOwnerDies = Yes
          End
          Behavior = SpecialAbility Trap01
            SpecialPowerTemplate = SPECIAL_TestTrap
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate TrapUpdate01
            SpecialPowerTemplate = SPECIAL_TestTrap
            StartAbilityRange = 5.0
            PreparationTime = 500
            SpecialObject = ChargeTrap
            MaxSpecialObjects = 4
            SpecialObjectsPersistent = Yes
          End
        End
        Object ChargeVictim
          KindOf = SELECTABLE STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End
        """,
    };

    private static Entity ChargeFind(SimulationWorld world, string template) => world.Entities.First(value => value.Template.Name == template);

    private static int StepUntilExists(SimulationWorld world, string template, int frames)
    {
        var limit = world.Frames(frames);
        for (var step = 0; step < limit; step++)
        {
            world.Step();
            if (world.Entities.Any(value => value.Template.Name == template)) return (int)((world.Tick - 1) / world.Time.TicksPerOriginalFrame);
        }
        return -1;
    }

    /// <summary>
    /// A unit walks to a building, plants a remote charge after its preparation and flees; the charge sits on the building. A second charge on the same building is refused, also of the other kind,
    /// and the order without a target sets the charge off: the building takes the blast, and the charge is gone from the unit's list. The same frames at 30, 60 and 120 Hz.
    /// </summary>
    public static void AbilityPlantsARemoteChargeAndSetsItOff()
    {
        string? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, AbilityChargeFiles());
            feeder.Spawn(1, "ChargeBurton", owner: 1, x: 100, y: 100);
            feeder.Spawn(2, "ChargeVictim", owner: 2, x: 200, y: 100);
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
            var burton = ChargeFind(world, "ChargeBurton");
            var victim = ChargeFind(world, "ChargeVictim");
            var ability = burton.Modules.OfType<SpecialAbilityUpdate>().First();
            Check.That(feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestRemote", SpecialPowerTargetKind.Object, victim.Id.Value).Accepted, $"{rate}: the order is accepted.");
            var planted = StepUntilExists(world, "ChargeRemote", 300);
            Check.That(planted > 0, $"{rate}: the charge is planted.");
            world.Step();
            var charge = ChargeFind(world, "ChargeRemote");
            Check.That(charge.Modules.OfType<StickyBombUpdate>().Single().Target == victim.Id, $"{rate}: the charge sits on the building.");
            Check.That(ability.SpecialObjectCount(world) == 1, $"{rate}: the unit has one charge out.");
            for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
            Check.That(burton.X < CnCNew.Simulation.Numerics.Fixed.FromInt(170), $"{rate}: the unit flees from what it planted ({burton.X}).");
            var refused = feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestRemote", SpecialPowerTargetKind.Object, victim.Id.Value);
            Check.That(refused.Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: a second charge on the same building is refused ({refused.Reason}).");
            refused = feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestTimed", SpecialPowerTargetKind.Object, victim.Id.Value);
            Check.That(refused.Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: and one of the other kind ({refused.Reason}).");
            var health = victim.Body!.Health;
            Check.That(feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestRemote").Accepted, $"{rate}: the detonation is accepted.");
            for (var tick = 0; tick < world.Frames(40); tick++) world.Step();
            Check.That(victim.Body!.Health < health, $"{rate}: the building takes the blast.");
            Check.That(world.Entities.All(value => value.Template.Name != "ChargeRemote"), $"{rate}: the charge is gone.");
            Check.That(ability.SpecialObjectCount(world) == 0, $"{rate}: and off the unit's list.");
            var line = $"{planted}:{victim.Body!.Health.Raw}";
            reference ??= line;
            Check.That(line == reference, $"{rate}: the same frame and blast as at 30 Hz ({line} against {reference}).");
        }
    }

    /// <summary>A timed charge goes off by itself, outlives a unit whose data says so; a booby trap takes a friendly building only, and an enemy that touches the building sets it off.</summary>
    public static void AbilityPlantsATimedChargeAndABoobyTrap()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, AbilityChargeFiles());
            feeder.Spawn(1, "ChargeBurton", owner: 1, x: 100, y: 100);
            feeder.Spawn(2, "ChargeVictim", owner: 2, x: 200, y: 100);
            feeder.Spawn(1, "ChargeVictim", owner: 1, x: 100, y: 200);
            world.Step();
            var burton = ChargeFind(world, "ChargeBurton");
            var enemy = world.Entities.First(value => value.Template.Name == "ChargeVictim" && value.Owner.Value == 2);
            var own = world.Entities.First(value => value.Template.Name == "ChargeVictim" && value.Owner.Value == 1);
            Check.That(feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestTrap", SpecialPowerTargetKind.Object, enemy.Id.Value).Reason == CommandRejection.InvalidSpecialPowerTarget, $"{rate}: no booby trap on an enemy.");
            Check.That(feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestTrap", SpecialPowerTargetKind.Object, own.Id.Value).Accepted, $"{rate}: a booby trap on the own building.");
            Check.That(StepUntilExists(world, "ChargeTrap", 300) > 0, $"{rate}: the trap is laid.");
            world.Step();
            Check.That(own.HasStatus(ObjectStatusBitsIndexOfBoobyTrapped), $"{rate}: the building is trapped.");
            Check.That(feeder.UseSpecialPower(1, burton.Id.Value, "SPECIAL_TestTimed", SpecialPowerTargetKind.Object, enemy.Id.Value).Accepted, $"{rate}: a timed charge on the enemy.");
            Check.That(StepUntilExists(world, "ChargeTimed", 300) > 0, $"{rate}: the timed charge is planted.");
            feeder.Kill(burton.Id.Value);
            for (var tick = 0; tick < world.Frames(20); tick++) world.Step();
            Check.That(world.Entities.Any(value => value.Template.Name == "ChargeTimed"), $"{rate}: it outlives the unit (SpecialObjectsPersistWhenOwnerDies).");
            Check.That(world.Entities.All(value => value.Template.Name != "ChargeTrap"), $"{rate}: the trap does not.");
            for (var tick = 0; tick < world.Frames(90); tick++) world.Step();
            Check.That(world.Entities.All(value => value.Template.Name != "ChargeTimed"), $"{rate}: the timed charge has gone off.");
        }
    }

    private static readonly int ObjectStatusBitsIndexOfBoobyTrapped = CnCNew.Simulation.Modules.ObjectStatusBits.Index("BOOBY_TRAPPED");
}
