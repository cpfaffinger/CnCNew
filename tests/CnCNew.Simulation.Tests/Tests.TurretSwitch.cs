using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// F-57 (docs/REFORGED-FIXES.md R-02): a turret is switched on and off by the module that owns it (<c>TurretAI::setTurretEnabled</c>). A switched-off turret runs no state
/// machine, so its weapon does not fire, and an object without <c>CAN_ATTACK</c> whose weapons all sit on such a turret cannot attack (<c>Object::isAbleToAttack</c>).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> TurretSwitchFiles() => new Dictionary<string, string>(BattlePlanFiles())
    {
        ["Data/INI/Object/TurretSwitchFixture.ini"] = """
        Object GunCenter
          KindOf = STRUCTURE
          VisionRange = 200.0
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY PlanGun
          End
          Behavior = AIUpdateInterface AI01
            Turret
              ControlledWeaponSlots = PRIMARY
              TurretTurnRate = 90
              NaturalTurretAngle = -90
            End
            AutoAcquireEnemiesWhenIdle = Yes
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = SPECIAL_BattlePlanFixture
            UpdateModuleStartsAttack = Yes
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
          End
        End
        Object SwitchCannon
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 200.0
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
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
          Behavior = DeployStyleAIUpdate AI01
            Turret
              ControlledWeaponSlots = PRIMARY
              TurretTurnRate = 90
              NaturalTurretAngle = 0
              InitiallyDisabled = Yes
            End
            AutoAcquireEnemiesWhenIdle = No
            PackTime = 1000
            UnpackTime = 1000
            TurretsFunctionOnlyWhenDeployed = Yes
            TurretsMustCenterBeforePacking = Yes
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) TurretSwitchWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: TurretSwitchFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>
    /// F-57: <c>BattlePlanUpdate::onObjectCreated</c> switches the center's turret off, so its gun attacks nothing and an order to fire is refused; the bombardment plan switches it on
    /// once unpacked; leaving bombardment, the turret is told to recentre, the center packs only once it stands at its natural angle, and the turret is off again. The port never
    /// switched the turret: the gun fired under every plan and without one, and the pack-up never waited.
    /// </summary>
    public static void TurretSwitchStrategyCenter()
    {
        var (world, feeder, fixture) = TurretSwitchWorld();
        var center = CsSpawn(world, feeder, "GunCenter", 1, 100, 100);
        var dummy = CsSpawn(world, feeder, "PlanDummy", 2, 160, 100);
        var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
        var full = dummy.Body!.Health;
        CsRun(world, 90);
        Check.That(dummy.Body!.Health == full, "Without a plan the center's gun attacks nothing.");
        var refused = feeder.FireWeapon(1, center.Id.Value, dummy.Id.Value);
        Check.That(!refused.Accepted && refused.Reason == CommandRejection.NoWeapon, $"An order to fire is refused while the turret is off ({refused.Reason}).");

        // Hold the line: the gun stays off.
        ChooseBattlePlan(world, feeder, center, 2);
        CsRun(world, 40, () => plan.Status == BattlePlanUpdate.TransitionStatus.Active);
        CsRun(world, 90);
        Check.That(plan.ActivePlan == BattlePlanKind.HoldTheLine && dummy.Body!.Health == full, "Under hold the line the gun attacks nothing.");

        // Bombardment: packs hold the line, unpacks, and then the turret is on - it turns from its natural angle toward the dummy and the gun fires.
        ChooseBattlePlan(world, feeder, center, 1);
        CsRun(world, 120, () => plan.ActivePlan == BattlePlanKind.Bombardment);
        Check.That(plan.ActivePlan == BattlePlanKind.Bombardment && dummy.Body!.Health == full, "Bombardment is active; nothing was fired before.");
        CsRun(world, 90, () => dummy.Body!.Health < full);
        Check.That(dummy.Body!.Health < full, "Under bombardment the center attacks the dummy by itself.");
        Check.That(!world.IsTurretInNaturalPosition(center), "The turret points away from its natural angle at the dummy.");

        // A snapshot taken in bombardment continues identically through the change of plan.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, NavTerrain());
        var restoredFeeder = feeder.ForkTo(restored);
        Check.That(restoredFeeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 2, tick: restored.Tick).Accepted
                   && feeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 2).Accepted, "Both worlds are told to switch to hold the line.");
        // The center goes idle and its turret is told to recentre once (m_centeringTurret); the idle center acquires the dummy again, which puts the turret back on it
        // (TurretAI::setTurretTargetObject), so while an enemy stays in range bombardment does not pack - the original's code, not a choice of the port.
        for (var tick = 0; tick < world.Frames(60); tick++)
            Check.That(world.Step().StateHash == restored.Step().StateHash, $"The change of plan continues identically after a restore (tick {tick}).");
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Active && !world.IsTurretInNaturalPosition(center), "With the dummy in range the turret is not centred and bombardment stays.");

        // Without a victim the turret holds, recentres, and then the center packs and switches it off.
        world.Kill(dummy.Id);
        restored.Kill(dummy.Id);
        var packedAt = -1L;
        for (var tick = 0; tick < world.Frames(200); tick++)
        {
            Check.That(world.Step().StateHash == restored.Step().StateHash, $"The pack-up continues identically after a restore (tick {tick}).");
            if (packedAt < 0 && plan.Status == BattlePlanUpdate.TransitionStatus.Packing)
            {
                packedAt = world.Tick;
                Check.That(world.IsTurretInNaturalPosition(center), "The center packs only with its turret at its natural angle.");
            }
        }
        Check.That(packedAt > 0, "The center packed once its turret had recentred.");
        var second = CsSpawn(world, feeder, "PlanDummy", 2, 160, 120);
        CsRun(world, 120);
        Check.That(second.Body!.Health == full, "With bombardment packed the turret is off and the gun is silent again.");
    }

    /// <summary>
    /// F-57: a deploy-style unit with <c>TurretsFunctionOnlyWhenDeployed</c> switches the turret of its weapon on when it is ready to attack, so it turns to the victim before the
    /// first shot; with <c>TurretsMustCenterBeforePacking</c> a move order first recentres the turret (<c>ALIGNING_TURRETS</c>, standing still) and packing begins once it is centred,
    /// which switches it off. The port fired at once after deploying, never aimed a turret that starts disabled, and packed at once.
    /// </summary>
    public static void TurretSwitchDeployStyle()
    {
        var (world, feeder, _) = TurretSwitchWorld();
        var cannon = CsSpawn(world, feeder, "SwitchCannon", 1, 100, 100);
        var dummy = CsSpawn(world, feeder, "PlanDummy", 2, 100, 170);
        var deploy = cannon.Modules.OfType<DeployStyleAIUpdate>().Single();
        var full = dummy.Body!.Health;
        long deployedAt = -1, hitAt = -1;
        for (var tick = 0; tick < world.Frames(150) && hitAt < 0; tick++)
        {
            feeder.FireWeapon(1, cannon.Id.Value, dummy.Id.Value);
            world.Step();
            if (deployedAt < 0 && deploy.IsDeployed) deployedAt = world.Tick;
            if (dummy.Body!.Health < full) hitAt = world.Tick;
        }
        Check.That(deployedAt > 0 && hitAt > 0, $"The cannon deploys ({deployedAt}) and hits ({hitAt}).");
        // A quarter turn at 90 degrees per second: the first shot comes about a second after the unit is set up.
        Check.That(hitAt - deployedAt >= world.Frames(25), $"The turret turns to the victim before the first shot ({world.Time.ToOriginalFrames(hitAt - deployedAt)} frames after deploying).");

        feeder.SetMoveTarget(1, cannon.Id.Value, 100, 40);
        world.Step();
        Check.That(!deploy.IsPacking && !deploy.IsDeployed && !deploy.IsPacked, "The move order first aligns the turret.");
        Check.That(!world.IsTurretInNaturalPosition(cannon), "The turret still points at the dummy.");
        var packingAt = -1L;
        CsRun(world, 90, () =>
        {
            if (packingAt < 0 && deploy.IsPacking) packingAt = world.Tick;
            return packingAt > 0;
        });
        Check.That(packingAt > 0 && world.IsTurretInNaturalPosition(cannon), "Packing begins once the turret is centred.");
        Check.That(cannon.Y == Fixed100, "The cannon stood still while it aligned its turret.");
        CsRun(world, 120, () => cannon.Y < Fixed100);
        Check.That(deploy.IsPacked && cannon.Y < Fixed100, "Packed, it moves.");
        var hits = dummy.Body!.Health;
        for (var tick = 0; tick < world.Frames(30); tick++)
        {
            feeder.FireWeapon(1, cannon.Id.Value, dummy.Id.Value);
            world.Step();
        }
        Check.That(dummy.Body!.Health == hits, "Packing switched the turret off; the next order deploys again before anything is fired.");
    }

    private static readonly CnCNew.Simulation.Numerics.Fixed Fixed100 = CnCNew.Simulation.Numerics.Fixed.FromInt(100);
}
