using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// F-146: ToppleUpdate::update runs once per logic frame of the tree, and a push lands in the frame before its first one. Above 30 Hz the first step began in the tick after the
// push - the second tick of a frame when a vehicle pushes in its first - and the tree was half a frame ahead of the 30 Hz tree for the whole fall.
static partial class Tests
{
    /// <summary>A crusher drives at a tree from <paramref name="startX"/>; returns the tree's angle at the frame ends while it falls (the frame of the push itself differs between the rates by the crusher's own timing).</summary>
    private static (List<long> Series, long FirstFrame) TreeToppledFrom(SimulationRate rate, int startX)
    {
        var (world, feeder, _) = RatedWorld(rate, CollisionFiles(), CollisionTerrain());
        feeder.Spawn(1, "Crusher", x: startX, y: 0);
        feeder.Spawn(2, "Tree", x: 40, y: 0);
        world.Step();
        feeder.SetMoveTarget(1, 1, 100, 0);
        var tree = world.FindEntity(new EntityId(2)) ?? throw new InvalidOperationException("No tree.");
        var topple = tree.Modules.OfType<ToppleUpdate>().First();
        var series = new List<long>();
        long firstFrame = -1;
        for (var step = 0; step < world.Frames(40) && !tree.EffectivelyDead; step++)
        {
            world.Step();
            if (!world.Time.IsOriginalFrameBoundary(world.Tick) || topple.ToppleAngle <= Fixed.Zero) continue;
            if (firstFrame < 0) firstFrame = world.Time.ToOriginalFrames(world.Tick - 1);
            series.Add(topple.ToppleAngle.Raw);
        }
        return (series, firstFrame);
    }

    public static void ToppleBeginsOnTheTreesNextFrame()
    {
        // The crusher's start moves the tick of the push through the frame; the tree's fall is the same at every rate wherever the push lands in a frame.
        for (var startX = -6; startX <= 0; startX++)
        {
            var reference = TreeToppledFrom(SimulationRate.Hz30, startX);
            Check.That(reference.Series.Count > 1, "Setup: the tree falls at 30 Hz (start " + startX + ").");
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var other = TreeToppledFrom(rate, startX);
                SameSeries(reference.Series, other.Series, "The angle of the toppling tree at the frame ends at " + rate + " (start " + startX + ")");
            }
        }
    }
}
