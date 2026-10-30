using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;

/// <summary>
/// F-35: the script VM's team moves are group orders (<c>ScriptActions::doMoveToWaypoint</c> -> <c>AIGroup::groupMoveToPosition</c>): every member walks to a destination of its own
/// around the point, keeping its place in the group, instead of every member heading for the point itself.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A square of four soldiers is sent to a waypoint with <c>MOVE_TEAM_TO</c>; where they stand afterwards, the square is kept - each soldier is on the same side of the group's
    /// middle as before. Before, each was ordered onto the waypoint and the four stood around it in the order they took free places.
    /// </summary>
    public static void TeamMoveIsAGroupOrder() => SquareKeepsItsSides(Scr.Do("MOVE_TEAM_TO", Scr.Team("Squad"), Scr.Waypoint("Mid")));

    /// <summary>F-35 step 3 (<c>doTeamMoveToSkirmishApproachPath</c>): the move to the closest waypoint of a skirmish approach path is the same group order.</summary>
    public static void ApproachPathMoveIsAGroupOrder() => SquareKeepsItsSides(Scr.Do("SKIRMISH_MOVE_TO_APPROACH_PATH", Scr.Team("Squad"), Scr.SkirmishPath("Approach")),
        [("Approach1", [("A", 250m, 200m)])]);

    private static void SquareKeepsItsSides(MapScriptAction move, (string Label, (string Name, decimal X, decimal Y)[] Points)[]? paths = null)
    {
        (decimal X, decimal Y, string Name)[] square = [(110m, 190m, "S1"), (110m, 210m, "S2"), (130m, 190m, "S3"), (130m, 210m, "S4")];
        var session = StartMission(
            [Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [move], oneShot: true)],
            objects: [..square.Select(spot => Placed("AlphaSoldier", spot.X, spot.Y, "Squad", spot.Name))], paths: paths);
        Run(session, session.Frames(600));
        var soldiers = square.Select(spot => NamedIn(session, spot.Name)!).ToArray();
        var middleX = soldiers.Aggregate(Fixed.Zero, (sum, soldier) => sum + soldier.X) / Fixed.FromInt(soldiers.Length);
        var middleY = soldiers.Aggregate(Fixed.Zero, (sum, soldier) => sum + soldier.Y) / Fixed.FromInt(soldiers.Length);
        Check.That(soldiers.All(soldier => (soldier.X - Fixed.FromInt(250)).Abs() < Fixed.FromInt(60) && (soldier.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(60)),
            $"The team walked to the waypoint ({string.Join("; ", soldiers.Select(soldier => $"{soldier.X}, {soldier.Y}"))}).");
        for (var index = 0; index < square.Length; index++)
        {
            var (x, y, name) = square[index];
            var soldier = soldiers[index];
            Check.That((soldier.X > middleX) == (x > 120m) && (soldier.Y > middleY) == (y > 200m),
                $"{name} keeps its side of the square ({soldier.X}, {soldier.Y} around {middleX}, {middleY}).");
        }
        Check.That(session.Engine.Diagnostics.Count == 0, "No diagnostic: " + string.Join("; ", session.Engine.Diagnostics.Keys));
    }

    /// <summary>
    /// F-35 step 2 (<c>ScriptActions::doCreateReinforcements</c>, "if (needToMoveToDestination)"): a reinforcement team without a transport that appears at its origin walks to the
    /// destination as one group order. Its four vehicles appear in a row along x and keep that order at the destination, at 30, 60 and 120 Hz. Before, every vehicle followed the
    /// destination point by itself and they stood around it in another order.
    /// </summary>
    // A unit wide enough that the row of a reinforcement (2.25 radii apart) is several cells of the navigation grid apart.
    private const string WideWalkerIni = """
        Object WideWalker
          Side = Alpha
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL SkirmishLoco
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    public static void ReinforcementsWalkAsAGroup()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (map, fixture, terrain) = MissionMap(
                [Scr.Script("Call", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team("Walkers"), Scr.Waypoint("Mid"))], oneShot: true)], rate: rate,
                extraFiles: new Dictionary<string, string> { ["Data/INI/Object/WideWalker.ini"] = WideWalkerIni });
            MapProperties Walkers() => new([Scr.Text("teamName", "Walkers"), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true), Scr.Text("teamReinforcementOrigin", "Spawn"),
                Scr.Text("teamUnitType1", "WideWalker"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 4, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 4, 0m, string.Empty)]);
            map = map with { Teams = [..map.Teams, Walkers()] };
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(2));
            var walkers = session.Engine.TeamInstances.First(team => team.Prototype == "Walkers").Members.Select(id => session.World.FindEntity(new CnCNew.Simulation.Ids.EntityId(id))!).ToArray();
            Check.That(walkers.Length == 4, $"{rate}: the four vehicles appeared ({walkers.Length}).");
            var row = walkers.OrderBy(walker => walker.X).Select(walker => walker.Id.Value).ToArray();
            Run(session, session.Frames(600));
            // Each keeps its offset from the nearest member, at most six of its radii (72): the row stands within 80 of the destination.
            Check.That(walkers.All(walker => (walker.X - Fixed.FromInt(250)).Abs() < Fixed.FromInt(80) && (walker.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(40)),
                $"{rate}: the team walked to the destination ({string.Join("; ", walkers.Select(walker => $"{walker.X}, {walker.Y}"))}).");
            var arrived = walkers.OrderBy(walker => walker.X).Select(walker => walker.Id.Value).ToArray();
            Check.That(arrived.SequenceEqual(row), $"{rate}: the row keeps its order ({string.Join(",", row)} against {string.Join(",", arrived)}).");
        }
    }
}
