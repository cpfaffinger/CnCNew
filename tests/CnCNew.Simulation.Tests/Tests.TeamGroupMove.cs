using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;

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
    public static void TeamMoveIsAGroupOrder()
    {
        (decimal X, decimal Y, string Name)[] square = [(110m, 190m, "S1"), (110m, 210m, "S2"), (130m, 190m, "S3"), (130m, 210m, "S4")];
        var session = StartMission(
            [Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("MOVE_TEAM_TO", Scr.Team("Squad"), Scr.Waypoint("Mid"))], oneShot: true)],
            objects: [..square.Select(spot => Placed("AlphaSoldier", spot.X, spot.Y, "Squad", spot.Name))]);
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
}
