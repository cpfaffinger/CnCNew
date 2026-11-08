using System;
using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// F-164: the riders of a transport that dies before or among the updates of a tick (a timer, a command, a missile's update) are scattered with their own update still to
// come in that tick; at 30 Hz they decide in that frame. Above 30 Hz they were held to the next frame, as if the transport had died after the updates, and walked a frame
// behind - on Alpine Assault a Red Guard asked the path queue a frame late and the random stream parted a few frames later.
internal static partial class Tests
{
    /// <summary>A transport with one rider is killed by a timer in a frame's first tick (the timers run before the updates); the rider's place at the frame ends.</summary>
    private static List<long> RiderAfterATimerKill(SimulationRate rate)
    {
        var (world, feeder, _, _) = ContainerExitWorld(rate, ModelGeometry.None, "PlainTruck", 1);
        var truck = world.Entities.Single(value => value.Template.Name == "PlainTruck");
        var rider = Grunts(world)[0];
        while (!world.Time.BeginsFrameOf(rider, world.Tick)) world.Step();
        Check.That(feeder.Timer(truck.Id.Value, 2 * world.Time.TicksPerOriginalFrame, 0, TimerActionKind.Kill).Accepted, $"{rate}: the timer is set.");
        var series = new List<long>();
        for (var step = 0; step < world.Frames(40); step++)
        {
            world.Step();
            if (!world.Time.IsOriginalFrameBoundary(world.Tick)) continue;
            var (x, y, _) = rider.FrameEnd(world.Tick);
            series.Add(x.Raw);
            series.Add(y.Raw);
        }
        return series;
    }

    public static void ARiderOfATransportKilledByATimerDecidesInThatFrame()
    {
        var reference = RiderAfterATimerKill(SimulationRate.Hz30);
        Check.That(reference.Count > 20, "Setup: the series has frames.");
        // The rider walks: its place is not the same in every frame.
        Check.That(reference.Distinct().Count() > 4, "Setup: the rider walks away from the truck: " + string.Join(",", reference.Take(40)));
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = RiderAfterATimerKill(rate);
            var first = Enumerable.Range(0, Math.Min(reference.Count, other.Count)).FirstOrDefault(index => reference[index] != other[index], -1);
            Check.That(first < 0 && other.Count == reference.Count, $"{rate}: the rider walks as at 30 Hz (first difference at value {first} of {other.Count}).");
        }
    }
}
