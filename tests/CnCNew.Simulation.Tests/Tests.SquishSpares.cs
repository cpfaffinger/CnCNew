using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #370, <c>SquishCollide::onCollide</c>: what spares a victim a crusher runs over. A victim whose goal object is the crusher is not squished when it has a <c>HijackerUpdate</c> (it runs
/// at the vehicle to take it) or its <c>SPECIAL_TANKHUNTER_TNT_ATTACK</c> ability is active (it places a charge on it); a crusher without a physics module squishes nothing; and the
/// relationship asked is the crusher's of the victim ("we want to know if IT considers ME to be an ally").
/// </summary>
static partial class Tests
{
    private const string SquishSparesIni = """
        Locomotor SquishTankLoco
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 120.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor SquishFootLoco
          Surfaces = GROUND
          TurnRate = 30.0
          Speed = 6.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        SpecialPower SPECIAL_TestSquishTnt
          Enum = SPECIAL_TANKHUNTER_TNT_ATTACK
          ReloadTime = 0
        End
        Object SquishTnt
          KindOf = PROJECTILE MINE NO_COLLIDE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = StickyBombUpdate Sticky01
          End
        End
        Object SquishTank
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          VisionRange = 200.0
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL SquishTankLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 50.0
          End
        End
        Object SquishTankWithoutPhysics
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          VisionRange = 200.0
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL SquishTankLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object SquishHijacker
          KindOf = SELECTABLE INFANTRY
          VisionRange = 200.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL SquishFootLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HijackerUpdate Hijack01
          End
          Behavior = ConvertToHijackedVehicleCrateCollide Hijack02
            RequiredKindOf = VEHICLE
          End
          Behavior = SquishCollide Squish01
          End
        End
        Object SquishTankHunter
          KindOf = SELECTABLE INFANTRY
          VisionRange = 200.0
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL SquishFootLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpecialAbility Tnt01
            SpecialPowerTemplate = SPECIAL_TestSquishTnt
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate Tnt02
            SpecialPowerTemplate = SPECIAL_TestSquishTnt
            StartAbilityRange = 5.0
            PreparationTime = 0
            SpecialObject = SquishTnt
            MaxSpecialObjects = 8
            SpecialObjectsPersistWhenOwnerDies = Yes
            SpecialObjectsPersistent = Yes
          End
          Behavior = SquishCollide Squish01
          End
        End
        Object SquishSoldier
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SquishCollide Squish01
          End
        End
        """;

    private readonly record struct SquishOutcome(bool Crushed, bool TankTaken, bool ChargePlaced, bool ChargeBeforeDeath);

    /// <summary>
    /// A tank of player 1 at x = 78 drives east through (100, 100), where a <paramref name="victim"/> of player 2 stands facing away from it; the victim is given <paramref name="order"/>
    /// ("hijack" in the tick the tank is, "tnt" a logic frame earlier, or "none"). The tank covers 4 units a logic frame: its box first meets the victim in its second frame, deep enough to squish it.
    /// </summary>
    private static SquishOutcome RunOver(SimulationRate rate, string tank, string victim, string order, (int From, int To, Relationship Relationship)[]? relations = null)
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/SquishSpares.ini"] = SquishSparesIni };
        var (world, feeder, _) = RatedWorld(rate, files);
        foreach (var (from, to, relationship) in relations ?? []) Check.That(feeder.SetRelationship(from, to, relationship).Accepted, $"{rate}: the relationship is set.");
        feeder.Spawn(1, tank, owner: 1, x: 78, y: 100);
        feeder.Spawn(2, victim, owner: 2, x: 100, y: 100);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        var vehicle = world.Entities.Single(value => value.Template.Name == tank);
        var man = world.Entities.Single(value => value.Template.Name == victim);
        if (order == "tnt")
        {
            // The ability takes up its target in the logic frame after the order (initiateIntentToDoSpecialPower: setWakeFrame); the tank sets off then.
            Check.That(feeder.UseSpecialPower(2, man.Id.Value, "SPECIAL_TestSquishTnt", SpecialPowerTargetKind.Object, vehicle.Id.Value).Accepted, $"{rate}: the TNT order is accepted.");
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
        }
        Check.That(feeder.SetMoveTarget(1, vehicle.Id.Value, 220, 100).Accepted, $"{rate}: the tank's move is accepted.");
        if (order == "hijack") Check.That(feeder.MoveAndEnter(2, man.Id.Value, vehicle.Id.Value).Accepted, $"{rate}: the hijack is accepted.");
        var (crushed, charge, chargeFirst) = (false, false, false);
        for (var step = 0; step < world.Frames(600); step++)
        {
            var result = world.Step();
            foreach (var value in result.Events)
                if (value.Kind == SimEventKind.EntityDied && value.Entity == man.Id && value.Other == vehicle.Id) crushed = true;
            if (!charge && world.Entities.Any(value => value.Template.Name == "SquishTnt"))
            {
                charge = true;
                chargeFirst = !crushed;
            }
        }
        return new SquishOutcome(crushed, vehicle.Owner == new PlayerId(2), charge, chargeFirst);
    }

    /// <summary>#370: <c>PhysicsBehavior *otherPhysics = other->getPhysics(); if (otherPhysics == NULL) return;</c> - a crusher without a physics module squishes nobody. At 30, 60 and 120 Hz.</summary>
    public static void SquishNeedsTheCrushersPhysics()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            Check.That(RunOver(rate, "SquishTank", "SquishSoldier", "none").Crushed, $"{rate}: a tank with a physics module squishes the soldier it runs over.");
            Check.That(!RunOver(rate, "SquishTankWithoutPhysics", "SquishSoldier", "none").Crushed, $"{rate}: a tank without a physics module squishes nobody.");
        }
    }
}
