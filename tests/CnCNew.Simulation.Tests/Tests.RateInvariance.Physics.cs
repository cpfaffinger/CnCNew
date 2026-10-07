using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package "physics": recursions the original runs once per logic frame (a fall, a slide, a train, healing in slices) are the same game at 30, 60
/// and 120 Hz. Each test runs one scene at the three rates on the synthetic fixture and compares the state at every frame boundary of the original – the
/// values of 60 and 120 Hz must be the values of 30 Hz, not merely close to them – and, where something moves, that the ticks in between lie on the way.
/// The tests name their rates and do not follow <c>--tick-rate</c>.
/// </summary>
static partial class Tests
{
    /// <summary>Runs <paramref name="scene"/> at 30, 60 and 120 Hz and hands the three results to <paramref name="compare"/> (reference first).</summary>
    private static void AtThreeRates<T>(Func<SimulationRate, T> scene, Action<T, T, string> compare)
    {
        var reference = scene(SimulationRate.Hz30);
        compare(reference, scene(SimulationRate.Hz60), "60 Hz");
        compare(reference, scene(SimulationRate.Hz120), "120 Hz");
    }

    private static void SameSeries(IReadOnlyList<long> reference, IReadOnlyList<long> other, string what)
    {
        Check.That(reference.Count == other.Count, what + ": " + other.Count + " frames instead of " + reference.Count + ".");
        for (var index = 0; index < reference.Count; index++)
            Check.That(reference[index] == other[index], what + ": frame " + index + " is " + other[index] + " instead of " + reference[index] + ".");
    }

    /// <summary>
    /// <c>ToppleUpdate</c>: the angular velocity changes once per frame of the original, the angle moves every tick. The angle at every frame boundary, the
    /// number of frames of the fall and the frame of the death are those of 30 Hz; above 30 Hz the angle of a tick inside a frame lies between its neighbours.
    /// </summary>
    public static void RateInvarianceToppleFall()
    {
        AtThreeRates(rate =>
        {
            var (world, feeder, fixture) = RatedWorld(rate, CollisionFiles(), CollisionTerrain());
            feeder.Spawn(2, "Tree", x: 40, y: 0);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var tree = world.FindEntity(new EntityId(1)) ?? throw new InvalidOperationException("No tree.");
            var topple = tree.Modules.OfType<ToppleUpdate>().First();
            // A push of 0.3 units per frame of the original, whatever the rate.
            topple.ApplyTopplingSpeed(world, Fixed.One, Fixed.Zero, Fixed.FromDecimal(0.3m));
            var angles = new List<long>();
            var perFrame = world.Time.TicksPerOriginalFrame;
            var monotone = true;
            long died = -1;
            byte[]? midFrame = null;
            for (var tick = 1; tick <= world.Frames(300) && died < 0; tick++)
            {
                var before = topple.ToppleAngle;
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 1)) died = tick;
                // The first update comes one tick after the push; from there on every frame of the tree is perFrame ticks.
                if (tick >= 2 && (tick - 1) % perFrame == 0) angles.Add(topple.ToppleAngle.Raw);
                if (tick == 2 + perFrame && perFrame > 1) midFrame = WorldSnapshot.Write(world);
                if (tick > 1 && tick <= perFrame && topple.ToppleAngle <= before) monotone = false;
            }
            if (midFrame is not null)
            {
                // A snapshot taken inside a frame of the tree carries the phase and the step of that frame.
                var live = WorldSnapshot.Restore(midFrame, fixture.Templates, fixture.Fingerprint, CollisionTerrain());
                var again = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint, CollisionTerrain());
                for (var tick = 0; tick < world.Frames(40); tick++)
                    Check.That(live.Step().StateHash == again.Step().StateHash, "A tree restored inside a frame keeps falling identically (" + rate.TicksPerSecond + " Hz, step " + tick + ").");
            }
            return (Angles: angles, DiedFrame: died < 0 ? -1 : world.Time.ToOriginalFrames(died - 1), Exact: died < 0 || (died - 1) % perFrame == 0, Monotone: monotone);
        }, (reference, other, rate) =>
        {
            Check.That(reference.DiedFrame > 3, "Setup: the tree falls for several frames at 30 Hz (" + reference.DiedFrame + ").");
            Check.That(other.DiedFrame == reference.DiedFrame && other.Exact, "The tree dies in the same frame of the original at " + rate + ": " + other.DiedFrame + " instead of " + reference.DiedFrame + ".");
            SameSeries(reference.Angles, other.Angles, "Topple angle at " + rate);
            Check.That(other.Monotone, "Inside its first frame the angle advances with every tick at " + rate + ".");
        });
    }
}
