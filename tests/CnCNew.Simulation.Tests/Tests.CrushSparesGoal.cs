using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #395, <c>PhysicsBehavior::onCollide</c>: "ignore collisions with our 'ignore' thingie, if any (and vice versa)" - the physics module returns before its crush check
/// (<c>checkForOverlapCollision</c>) when either object's AI ignores the other as its obstacle (<c>getIgnoredObstacleID</c>). A hijacker on its way to a vehicle ignores that vehicle
/// (<c>AIEnterState::onEnter</c>: <c>ignoreObstacle</c>), so the vehicle that runs over it does not crush it; its own collide module takes the vehicle instead.
/// </summary>
static partial class Tests
{
    private const string CrushSparesGoalIni = """
        Locomotor CrushGoalFastLoco
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 1200.0
          Acceleration = 3000000.0
          Braking = 3000000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object CrushGoalFastTank
          KindOf = SELECTABLE VEHICLE
          CrusherLevel = 2
          VisionRange = 200.0
          Geometry = BOX
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL CrushGoalFastLoco
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
        Object CrushGoalHijacker
          KindOf = SELECTABLE INFANTRY
          CrushableLevel = 0
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
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
          End
          Behavior = ConvertToHijackedVehicleCrateCollide Hijack02
            RequiredKindOf = VEHICLE
          End
          Behavior = SquishCollide Squish01
          End
        End
        """;

    /// <summary>
    /// A tank of player 1 at x = <paramref name="startX"/> races east at 40 units a logic frame through (100, 100), where a hijacker of player 2 stands; with <paramref name="hijack"/> the hijacker
    /// is ordered onto the tank in the tick the tank sets off. The tank's first contact with the hijacker is a step that ends with its centre on the hijacker's crush point: the physics module's
    /// crush comes before the hijacker's own collide module. Whether the tank crushed it and whether it took the tank.
    /// </summary>
    private static (bool Crushed, bool Taken) RunOverAtFirstContact(SimulationRate rate, int startX, bool hijack)
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/SquishSpares.ini"] = SquishSparesIni,
            ["Data/INI/Object/CrushSparesGoal.ini"] = CrushSparesGoalIni,
        };
        var (world, feeder, _) = RatedWorld(rate, files);
        feeder.Spawn(1, "CrushGoalFastTank", owner: 1, x: startX, y: 100);
        feeder.Spawn(2, "CrushGoalHijacker", owner: 2, x: 100, y: 100);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        var vehicle = world.Entities.Single(value => value.Template.Name == "CrushGoalFastTank");
        var man = world.Entities.Single(value => value.Template.Name == "CrushGoalHijacker");
        Check.That(feeder.SetMoveTarget(1, vehicle.Id.Value, 400, 100).Accepted, $"{rate}: the tank's move is accepted.");
        if (hijack) Check.That(feeder.MoveAndEnter(2, man.Id.Value, vehicle.Id.Value).Accepted, $"{rate}: the hijack is accepted.");
        var crushed = false;
        for (var step = 0; step < world.Frames(60); step++)
            foreach (var value in world.Step().Events)
                if (value.Kind == SimEventKind.EntityDied && value.Entity == man.Id && value.Other == vehicle.Id) crushed = true;
        return (crushed, vehicle.Owner == new PlayerId(2));
    }

    /// <summary>
    /// #395: a hijacker on its way to the tank that runs over it is not crushed by the tank's physics module, whose collision ignores the hijacker's goal, and takes the tank; the same hijacker
    /// standing idle in the same place is crushed. At 30, 60 and 120 Hz.
    /// </summary>
    public static void CrushSparesTheGoalOfAnApproach()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var idle = RunOverAtFirstContact(rate, CrushGoalStartX, hijack: false);
            Check.That(idle.Crushed && !idle.Taken, $"{rate}: an idle hijacker the tank runs over is crushed at the first contact (crushed {idle.Crushed}, taken {idle.Taken}).");
            var approach = RunOverAtFirstContact(rate, CrushGoalStartX, hijack: true);
            Check.That(!approach.Crushed && approach.Taken, $"{rate}: a hijacker whose goal is the tank is not crushed by it and takes it (crushed {approach.Crushed}, taken {approach.Taken}).");
        }
    }

    private const int CrushGoalStartX = 62;
}
