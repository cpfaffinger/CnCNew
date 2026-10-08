using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Observation C of docs/AI-OBSERVED-2026-10-08.md: several units sent to one point. The first one to arrive stands on it; before the fix the others walked circles around
    /// it for ever (the last waypoint lies inside its bounding circle). Now each takes the nearest free place (<c>SimulationWorld.TryFreeGoalSpot</c>, the reference's
    /// <c>Pathfinder::adjustDestination</c>): everybody arrives, nobody stands in anybody else, and the places are the same at 30, 60 and 120 Hz, frame for frame.
    /// </summary>
    public static void GoalSpotsForUnitsSentToOnePoint()
    {
        (int X, int Y)[] starts = [(30, 30), (30, 150), (160, 40), (150, 160), (40, 90), (100, 20)];
        var goal = (X: Fixed.FromInt(100), Y: Fixed.FromInt(100));
        List<string>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, GroundRateFiles(), NavTerrain());
            foreach (var (x, y) in starts) feeder.Spawn(1, "RateWalker", x: x, y: y);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var units = world.Entities.Where(value => value.Template.Name == "RateWalker").OrderBy(value => value.Id.Value).ToList();
            Check.That(units.Count == starts.Length, $"{rate}: the walkers are there.");
            foreach (var unit in units) Check.That(feeder.SetMoveTarget(1, unit.Id.Value, 100, 100).Accepted, $"{rate}: the move order is accepted.");

            var frames = new List<string>();
            var arrivedBy = -1;
            for (var frame = 0; frame < 600; frame++)
            {
                for (var tick = 0; tick < factor; tick++) world.Step();
                frames.Add(string.Join(';', units.Select(unit => $"{unit.X.Raw},{unit.Y.Raw},{unit.Angle.Raw},{unit.HasMoveTarget}")));
                if (arrivedBy < 0 && units.All(unit => !unit.HasMoveTarget)) arrivedBy = frame;
            }

            Check.That(arrivedBy >= 0, $"{rate}: every walker arrives (still moving after 20 s: {units.Count(unit => unit.HasMoveTarget)} of {units.Count}).");
            Check.That(units.Count(unit => unit.X == goal.X && unit.Y == goal.Y) == 1, $"{rate}: exactly one walker stands on the point itself.");
            var radius = units[0].Template.BoundingCircleRadius;
            for (var first = 0; first < units.Count; first++)
            {
                var dx = units[first].X - goal.X;
                var dy = units[first].Y - goal.Y;
                Check.That(SquaredReach.Of(Fixed.FromInt(30)).Contains(dx * dx + dy * dy), $"{rate}: walker {first} rests within three cells of the point ({units[first].X}, {units[first].Y}).");
                for (var second = first + 1; second < units.Count; second++)
                {
                    var gapX = units[first].X - units[second].X;
                    var gapY = units[first].Y - units[second].Y;
                    Check.That(!SquaredReach.Of(radius + radius).ContainsStrictly(gapX * gapX + gapY * gapY), $"{rate}: walkers {first} and {second} do not stand in each other.");
                }
            }

            if (reference is null)
            {
                reference = frames;
                Console.WriteLine($"       goal spots: {units.Count} walkers sent to one point all arrive after {arrivedBy} frames at {string.Join(" ", units.Select(unit => $"({unit.X - goal.X},{unit.Y - goal.Y})"))} from it");
            }
            else
            {
                var firstDifference = Enumerable.Range(0, frames.Count).FirstOrDefault(index => frames[index] != reference[index], -1);
                Check.That(firstDifference < 0, $"{rate}: positions, headings and order states after every logic frame are those of 30 Hz (first difference in frame {firstDifference}).");
            }
        }
    }
}
