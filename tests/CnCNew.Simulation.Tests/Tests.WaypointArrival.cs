using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;

/// <summary>
/// F-78: a unit that follows a waypoint path goes on to the next waypoint when its move to the current one has ended, whether it arrived or not (<c>AIFollowWaypointPathState::update</c>:
/// "if move to has finished, move to next point on waypoint path", for every status but "continue"); the VM went on only within 40 units of the waypoint and ordered the unit there again otherwise.
/// </summary>
static partial class Tests
{
    // A block that covers the path's corner: the nearest place a soldier can stand is farther than 40 units from it.
    private const string WaypointBlockIni = """
        Object WaypointBlock
          KindOf = STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 60.0
          GeometryMinorRadius = 60.0
          GeometryHeight = 20.0
          GeometryIsSmall = No
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """;

    /// <summary>
    /// A soldier follows a path whose corner lies inside a block: its move to the corner ends at the block's edge, and it goes on to the path's end, at 30, 60 and 120 Hz. Before, it was
    /// ordered to the corner again and again and never finished the path.
    /// </summary>
    public static void FollowerGoesOnWhenItsMoveEnds()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Start", [Scr.And(Scr.If("NAMED_CREATED", Scr.Unit("Walker")))], [Scr.Do("NAMED_FOLLOW_WAYPOINTS", Scr.Unit("Walker"), Scr.Named(MapScriptParameterType.WaypointPath, "Corner"))], oneShot: true),
                Scr.Script("Arrived", [Scr.And(Scr.If("NAMED_REACHED_WAYPOINTS_END", Scr.Unit("Walker"), Scr.Named(MapScriptParameterType.WaypointPath, "Corner")))], [Scr.Do("SET_FLAG", Scr.Flag("arrived"), Scr.Bool(true))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Walker"), Placed("WaypointBlock", 280m, 100m, "teamA", "Block")],
                paths: [("Corner", [("Start", 100m, 100m), ("Corner", 280m, 100m), ("End", 160m, 300m)])], rate: rate,
                extraFiles: new Dictionary<string, string> { ["Data/INI/Object/WaypointBlock.ini"] = WaypointBlockIni });
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(1200));
            var walker = NamedIn(session, "Walker")!;
            Check.That(session.Engine.FlagValue("arrived"), $"{rate}: the soldier finished the path (it stands at {walker.X}, {walker.Y}).");
            var dx = walker.X - Fixed.FromInt(160);
            var dy = walker.Y - Fixed.FromInt(300);
            Check.That(dx * dx + dy * dy <= Fixed.FromInt(40 * 40), $"{rate}: it stands at the path's end ({walker.X}, {walker.Y}).");
        }
    }
}
