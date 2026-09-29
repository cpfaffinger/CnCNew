using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

static partial class Tests
{
    public static void NavigationFootprintEscape()
    {
        var (world, feeder, _) = NavSetup();
        var grid = world.Navigation!;
        var ground = LocomotorSurfaces.Ground;
        feeder.Spawn(1, "Wall", x: 100, y: 100);
        world.Step();
        var (wallX, wallY) = (TerrainMap.CellOf(Fixed.FromInt(100)), TerrainMap.CellOf(Fixed.FromInt(100)));
        Check.That(grid.IsBlockedByObstacle(wallX, wallY), "The wall blocks its centre cell.");

        // A unit that stands inside a footprint (built over it, or pushed there) has to be able to walk out.
        var leaving = grid.FindPath(Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(175), ground);
        Check.That(leaving.Status == PathStatus.Found && leaving.Points[^1] == new PathPoint(Fixed.FromInt(100), Fixed.FromInt(175)),
            "A start inside a footprint escapes through the surrounding cells and reaches the goal.");

        // A goal inside a footprint (a click on a building) ends at the goal, not in a refusal.
        var entering = grid.FindPath(Fixed.FromInt(100), Fixed.FromInt(25), Fixed.FromInt(100), Fixed.FromInt(100), ground);
        Check.That(entering.Status != PathStatus.Blocked && entering.Points.Length >= 1, "A goal inside a footprint is approached.");

        // Escape cells are only usable at a penalty: with a free way around, the path avoids the footprint.
        var around = grid.FindPath(Fixed.FromInt(100), Fixed.FromInt(35), Fixed.FromInt(100), Fixed.FromInt(165), ground);
        Check.That(around.Points.All(point => !grid.IsBlockedByObstacle(TerrainMap.CellOf(point.X), TerrainMap.CellOf(point.Y))),
            "Paths between free cells never step on obstacle cells, whatever the escape rule allows for start and goal.");

        // The whole trip, as a unit: it starts in the wall, is ordered out and arrives.
        feeder.Spawn(1, "Walker", x: 100, y: 100);
        world.Step();
        feeder.SetMoveTarget(1, 2, 100, 175);
        var (ticks, arrived) = NavRun(world, 2, 900);
        Check.That(arrived, $"A walker spawned inside a wall footprint walks out and arrives (ticks {ticks}).");
    }
}
