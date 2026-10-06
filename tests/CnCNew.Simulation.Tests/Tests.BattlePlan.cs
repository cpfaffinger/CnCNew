using System.Collections.Generic;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>The strategy center's battle plans: the unpack/active/pack cycle, the army bonuses, the center's own bonus and the paralysis of a plan change.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> BattlePlanFiles() => new Dictionary<string, string>(CleanupAndSpyFiles())
    {
        ["Data/INI/GameData.ini"] = """
        GameData
          WeaponBonus = BATTLEPLAN_BOMBARDMENT DAMAGE 150%
          WeaponBonus = BATTLEPLAN_SEARCHANDDESTROY RANGE 120%
        End
        """,
        ["Data/INI/Object/BattlePlanFixture.ini"] = """
        Weapon PlanGun
          PrimaryDamage = 20
          AttackRange = 100.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End
        SpecialPower SPECIAL_BattlePlanFixture
          Enum = SPECIAL_CHANGE_BATTLE_PLANS
          ReloadTime = 0
        End
        Object PlanCenter
          KindOf = STRUCTURE
          VisionRange = 10.0
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = SPECIAL_BattlePlanFixture
            UpdateModuleStartsAttack = Yes
          End
          Behavior = StealthDetectorUpdate Detect01
            DetectionRate = 100
            DetectionRange = 100.0
            InitiallyDisabled = Yes
          End
          Behavior = BattlePlanUpdate Plan01
            SpecialPowerTemplate = SPECIAL_BattlePlanFixture
            BombardmentPlanAnimationTime = 1000
            HoldTheLinePlanAnimationTime = 1000
            SearchAndDestroyPlanAnimationTime = 1000
            TransitionIdleTime = 0
            ValidMemberKindOf = INFANTRY CAN_ATTACK VEHICLE
            InvalidMemberKindOf = DOZER STRUCTURE AIRCRAFT DRONE
            BattlePlanChangeParalyzeTime = 1000
            HoldTheLinePlanArmorDamageScalar = 0.5
            SearchAndDestroyPlanSightRangeScalar = 2.0
            StrategyCenterSearchAndDestroySightRangeScalar = 2.0
            StrategyCenterSearchAndDestroyDetectsStealth = Yes
            StrategyCenterHoldTheLineMaxHealthScalar = 2.0
            StrategyCenterHoldTheLineMaxHealthChangeType = PRESERVE_RATIO
          End
        End
        Object PlanSoldier
          KindOf = SELECTABLE INFANTRY CAN_ATTACK
          VisionRange = 20.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY PlanGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object PlanDozer
          KindOf = SELECTABLE VEHICLE DOZER
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PlanBunker
          KindOf = STRUCTURE CAN_ATTACK
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PlanSneak
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 0
            InnateStealth = Yes
          End
        End
        Object PlanDummy
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) BattlePlanWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: BattlePlanFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    private static void ChooseBattlePlan(SimulationWorld world, Feeder feeder, Entity center, int option)
    {
        Check.That(feeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: option).Accepted, $"The strategy center accepts battle plan option {option}.");
        world.Step();
    }

    private static uint BattlePlanBit(string name) => 1u << (NameTablesPhase5.WeaponBonusCondition.TryIndexOf(name, out var bit) ? bit : throw new InvalidOperationException(name));

    public static void SpecialsBattlePlanCycle()
    {
        var (world, feeder, fixture) = BattlePlanWorld();
        var alpha = new PlayerId(1);
        var center = CsSpawn(world, feeder, "PlanCenter", 1, 100, 100);
        var soldier = CsSpawn(world, feeder, "PlanSoldier", 1, 20, 100);
        var dozer = CsSpawn(world, feeder, "PlanDozer", 1, 30, 120);
        var bunker = CsSpawn(world, feeder, "PlanBunker", 1, 40, 120);
        var sneak = CsSpawn(world, feeder, "PlanSneak", 2, 150, 100);
        var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
        var detector = center.Modules.OfType<StealthDetectorUpdate>().Single();
        var vision = world.Vision!;
        var slot = world.VisionSlotOf(alpha);
        var searchBit = BattlePlanBit("BATTLEPLAN_SEARCHANDDESTROY");
        var holdBit = BattlePlanBit("BATTLEPLAN_HOLDTHELINE");
        var bombardBit = BattlePlanBit("BATTLEPLAN_BOMBARDMENT");
        CsRun(world, 10);
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Idle && plan.ActivePlan == BattlePlanKind.None, "A strategy center starts idle with no plan.");
        Check.That(!world.IsPositionVisibleTo(alpha, Fixed.FromInt(50), Fixed.FromInt(100)), "The soldier cannot see 30 units away.");
        Check.That(!sneak.HasStatus(ObjectStatusBits.Index("DETECTED")) && sneak.HasStatus(ObjectStatusBits.Index("STEALTHED")), "The enemy sneak is stealthed and nobody detects it.");

        // Search and destroy: unpack for 1 s, then the army gets the weapon bonus and a wider sight, the center a wider shroud and active detection.
        ChooseBattlePlan(world, feeder, center, 3);
        CsRun(world, 5);
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Unpacking && world.BattlePlansActive(alpha, BattlePlanKind.SearchAndDestroy) == 0, "The plan is unpacking and not yet in force.");
        CsRun(world, 40, () => plan.Status == BattlePlanUpdate.TransitionStatus.Active);
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Active && plan.ActivePlan == BattlePlanKind.SearchAndDestroy, "The plan became active after the unpack animation.");
        Check.That(world.BattlePlansActive(alpha, BattlePlanKind.SearchAndDestroy) == 1, "The player has one search-and-destroy plan in force.");
        Check.That((soldier.WeaponBonusConditions & searchBit) != 0 && (dozer.WeaponBonusConditions & searchBit) == 0 && (bunker.WeaponBonusConditions & searchBit) == 0 && (center.WeaponBonusConditions & searchBit) == 0,
            "A qualifying soldier got the bonus; a dozer, a structure and the center itself did not.");
        CsRun(world, 12);
        Check.That(world.IsPositionVisibleTo(alpha, Fixed.FromInt(50), Fixed.FromInt(100)), "The soldier now sees twice as far (20 -> 40 units).");
        Check.That(center.ShroudScalar == Fixed.FromInt(2), "The center's shroud is doubled.");
        Check.That(sneak.HasStatus(ObjectStatusBits.Index("DETECTED")), "The center detects the stealthed enemy within its range.");
        var late = CsSpawn(world, feeder, "PlanSoldier", 1, 20, 140);
        Check.That((late.WeaponBonusConditions & searchBit) != 0, "A soldier created while the plan is active gets its bonus at once.");

        // Changing the plan packs the current one up: the bonuses go, the troops are paralyzed for BattlePlanChangeParalyzeTime (1 s).
        ChooseBattlePlan(world, feeder, center, 2);
        CsRun(world, 2);
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Packing && world.BattlePlansActive(alpha, BattlePlanKind.SearchAndDestroy) == 0, "The plan is packing and its bonus is gone.");
        Check.That((soldier.WeaponBonusConditions & searchBit) == 0 && center.ShroudScalar == Fixed.One, "The soldier lost the weapon bonus and the center its shroud bonus.");
        feeder.SetMoveTarget(1, soldier.Id.Value, 60, 100);
        CsRun(world, 12);
        Check.That(soldier.X == Fixed.FromInt(20), $"A paralyzed soldier does not move ({soldier.X}).");
        CsRun(world, 40);
        Check.That(soldier.X > Fixed.FromInt(21), "It carries on with its order once the paralysis is over.");
        CsRun(world, 60, () => plan.Status == BattlePlanUpdate.TransitionStatus.Active);

        // Hold the line: damage to qualifying objects is halved, the center's maximum health doubles (health keeps its ratio).
        Check.That(plan.ActivePlan == BattlePlanKind.HoldTheLine && world.BattlePlansActive(alpha, BattlePlanKind.HoldTheLine) == 1, "Hold the line is in force.");
        Check.That(center.Body!.MaxHealth == Fixed.FromInt(2000) && center.Body.Health == Fixed.FromInt(2000), $"The center's maximum health doubled with its health ratio ({center.Body!.Health}/{center.Body.MaxHealth}).");
        Check.That((soldier.WeaponBonusConditions & holdBit) != 0, "The soldier carries the hold-the-line bonus switch.");
        var enemyShooter = CsSpawn(world, feeder, "PlanSoldier", 2, 20, 160);
        world.Damage(soldier.Id, new DamageInfo(enemyShooter.Id, Fixed.FromInt(20), DamageTypes.Explosion, DeathTypes.Normal, false));
        world.Damage(dozer.Id, new DamageInfo(enemyShooter.Id, Fixed.FromInt(20), DamageTypes.Explosion, DeathTypes.Normal, false));
        world.Damage(bunker.Id, new DamageInfo(enemyShooter.Id, Fixed.FromInt(20), DamageTypes.Explosion, DeathTypes.Normal, false));
        world.Damage(soldier.Id, new DamageInfo(enemyShooter.Id, Fixed.FromInt(20), DamageTypes.Unresistable, DeathTypes.Normal, false));
        Check.That(soldier.Body!.Health == Fixed.FromInt(70), $"A covered soldier took half of a normal hit and the full unresistable one ({soldier.Body!.Health}).");
        Check.That(dozer.Body!.Health == Fixed.FromInt(80) && bunker.Body!.Health == Fixed.FromInt(80), "A dozer and a structure took the full damage.");

        // A snapshot taken in hold the line continues identically, and a plan change after the restore ends in the same world.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, NavTerrain());
        Check.That(restored.BattlePlansActive(alpha, BattlePlanKind.HoldTheLine) == 1, "The plan in force survives the snapshot.");
        var restoredFeeder = feeder.ForkTo(restored);
        var restoredCenter = restored.FindEntity(center.Id)!;
        Check.That(restoredFeeder.UseSpecialPower(1, restoredCenter.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 1, tick: restored.Tick).Accepted && feeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 1).Accepted,
            "Both worlds are told to switch to bombardment.");
        for (var tick = 0; tick < world.Frames(150); tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The plan change continues identically after a restore (tick {tick}).");

        // Bombardment: the weapon bonus raises the damage by 50 %.
        Check.That(plan.ActivePlan == BattlePlanKind.Bombardment, "Bombardment is in force.");
        var dummy = CsSpawn(world, feeder, "PlanDummy", 2, 60, 100);
        var before = dummy.Body!.Health;
        CsAi(soldier).OrderMove(world, Fixed.FromInt(55), Fixed.FromInt(100), false);
        CsRun(world, 40);
        Check.That(feeder.FireWeapon(1, soldier.Id.Value, dummy.Id.Value).Accepted, "The soldier fires.");
        world.Step();
        Check.That(before - dummy.Body!.Health == Fixed.FromInt(30), $"Bombardment's DAMAGE 150% turned the 20 into 30 ({before - dummy.Body!.Health}).");
        Check.That((soldier.WeaponBonusConditions & bombardBit) != 0 && (soldier.WeaponBonusConditions & holdBit) == 0, "Applying the bombardment bonus cleared the hold-the-line switch.");

        // Losing the center removes its plan.
        world.Kill(center.Id);
        CsRun(world, 4);
        Check.That(world.BattlePlansActive(alpha, BattlePlanKind.Bombardment) == 0 && (soldier.WeaponBonusConditions & bombardBit) == 0, "Destroying the center takes its plan and the bonus away again.");
    }

    public static void SpecialsBattlePlanShrouds()
    {
        var (world, feeder, fixture) = BattlePlanWorld();
        var center = CsSpawn(world, feeder, "PlanCenter", 1, 100, 100);
        CsSpawn(world, feeder, "PlanSoldier", 1, 20, 100);
        var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
        var ready = new SyntheticFixture(TestRate.Default, files: BattlePlanFiles());
        ChooseBattlePlan(world, feeder, center, 3);
        CsRun(world, 40, () => plan.Status == BattlePlanUpdate.TransitionStatus.Active);

        // A snapshot taken while the plan is active restores the player's bookkeeping and the center's scalar: it continues identically and a pack-up works in both.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), ready.Templates, ready.Fingerprint, NavTerrain());
        var restoredFeeder = feeder.ForkTo(restored);
        Check.That(restoredFeeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 2, tick: restored.Tick).Accepted && feeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 2).Accepted,
            "Both worlds switch to hold the line.");
        for (var tick = 0; tick < world.Frames(130); tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The switch continues identically after a restore (tick {tick}).");
        Check.That(restored.FindEntity(center.Id)!.Modules.OfType<BattlePlanUpdate>().Single().ActivePlan == BattlePlanKind.HoldTheLine, "The restored center reached hold the line.");

        // An unsupported option does nothing.
        var other = CsSpawn(world, feeder, "PlanCenter", 1, 150, 150);
        ChooseBattlePlan(world, feeder, other, 7);
        CsRun(world, 10);
        Check.That(other.Modules.OfType<BattlePlanUpdate>().Single().DesiredPlan == BattlePlanKind.None, "An option outside 1..3 chooses no plan.");
    }
}
