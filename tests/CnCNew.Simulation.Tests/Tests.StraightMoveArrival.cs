using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #457: a straight move on a world without terrain ends where the 30 Hz move ends, at every rate. Above 30 Hz the move ends in the first tick of its last frame (as the 30 Hz step
/// does) and the object covers the rest of that step in the frame's shares (<c>BeginArrival</c>): read in that tick it stands short of its target by the uncovered shares - half
/// a frame's step at 60 Hz, three quarters at 120 Hz -, and where the frame ends is the target.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> StraightMoveFiles() => new Dictionary<string, string>(GroupMoveFiles())
    {
        ["Data/INI/Object/StraightMoveFixture.ini"] = """
        Locomotor StraightTankLoco
          Surfaces = GROUND
          Speed = 40
          Acceleration = 60
          Braking = 50
          TurnRate = 180
        End
        Locomotor StraightRotorLoco
          Surfaces = AIR
          AirborneTargetingHeight = 30
          Speed = 120.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 360.0
          PreferredHeight = 40.0
          CloseEnoughDist = 4.0
        End
        Object StraightTank
          KindOf = VEHICLE SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL StraightTankLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object StraightRotor
          KindOf = AIRCRAFT VEHICLE SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL StraightRotorLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>A ground unit and an aircraft moved straight on a world without terrain: the same place at the end of every logic frame at 30, 60 and 120 Hz, and on the target.</summary>
    public static void AStraightMoveEndsWhere30HzEnds()
    {
        foreach (var template in new[] { "Marcher", "StraightTank", "StraightRotor" })
        foreach (var (targetX, targetY) in new[] { (437, 100), (100, 351), (391, 287) })
        {
            List<string> Run(SimulationRate rate)
            {
                var (world, feeder, _) = RatedWorld(rate, StraightMoveFiles());
                feeder.Spawn(1, template, x: 100, y: 100);
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                var unit = world.Entities.Single(value => value.Template.Name == template);
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), 1000, world.Tick, unit.Id, Fixed.FromInt(targetX), Fixed.FromInt(targetY))).Accepted, $"{rate}: the move is accepted.");
                var trace = new List<string>();
                for (var frame = 0; frame < 900; frame++)
                {
                    for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++)
                    {
                        var moving = unit.HasMoveTarget;
                        world.Step();
                        if (!moving || unit.HasMoveTarget) continue;
                        // The tick the move ends in: above 30 Hz the object still covers the rest of that frame's step in shares, and where the frame ends is its target.
                        var (endX, endY, _) = unit.FrameEnd(world.Tick);
                        Check.That(endX == Fixed.FromInt(targetX) && endY == Fixed.FromInt(targetY), $"{rate}, {template}: where the frame of the arrival ends is the target ({endX}, {endY}).");
                    }
                    trace.Add($"{unit.X.ToDecimal():0.####},{unit.Y.ToDecimal():0.####},{(unit.HasMoveTarget ? 1 : 0)}");
                }
                Check.That(!unit.HasMoveTarget, $"{rate}, {template}: the move has ended ({trace[^1]}).");
                var gap = ((unit.X - Fixed.FromInt(targetX)).Abs() + (unit.Y - Fixed.FromInt(targetY)).Abs()).ToDecimal();
                Check.That(gap < 0.01m, $"{rate}, {template} to ({targetX}, {targetY}): it stands on its target ({trace[^1]}).");
                return trace;
            }

            var reference = Run(SimulationRate.Hz30);
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var run = Run(rate);
                var first = Enumerable.Range(0, reference.Count).FirstOrDefault(frame => run[frame] != reference[frame], -1);
                Check.That(first < 0, $"{rate}, {template} to ({targetX}, {targetY}): where 30 Hz has it at the end of every logic frame (first difference in frame {first + 1}: {(first < 0 ? "" : run[first] + " against " + reference[first])}).");
            }
        }
    }
}
