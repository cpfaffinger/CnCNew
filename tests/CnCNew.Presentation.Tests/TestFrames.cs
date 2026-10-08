using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The frame convention of the simulation above 30 Hz, for tests that read the simulation (docs/RATE-INVARIANCE-CONSOLIDATION.md §1.3): an object decides in the first tick of
/// its own logic frame of the original and stands where the 30 Hz step puts it after the last; a new object begins its first frame one frame after it appeared; the shroud is
/// recomputed after the last tick of its frame. At 30 Hz a frame is a tick and none of these helpers steps more than the test did before.
/// </summary>
internal static class TestFrames
{
    /// <summary>Ticks of <paramref name="world"/> in one logic frame of the original (1, 2 or 4).</summary>
    public static int TicksPerFrame(SimulationWorld world) => world.Time.TicksPerOriginalFrame;

    /// <summary>
    /// Steps until the next step is the first tick of a frame of <paramref name="entity"/> other than the one it appeared in - from there the object acts on an order the way
    /// the 30 Hz object does one tick after its spawn. No step at 30 Hz once the spawn's own step is done.
    /// </summary>
    public static void StepToFrameOf(SimulationWorld world, EntityId entity)
    {
        var spawned = world.FindEntity(entity)!.SpawnTick;
        while (world.Tick - spawned < TicksPerFrame(world) || (world.Tick - spawned) % TicksPerFrame(world) != 0) world.Step();
    }

    /// <summary>Steps until the next step begins a logic frame of the original of the world (the grid of the shroud and of scripts). No step at 30 Hz.</summary>
    public static void StepToFrameOfWorld(SimulationWorld world)
    {
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
    }

    /// <summary>
    /// One logic frame of the original: as many steps as it has ticks (one at 30 Hz). After it an object in phase stands where the 30 Hz step has put it.
    /// <paramref name="afterEachTick"/> sees every tick, for trackers that are fed per tick.
    /// </summary>
    public static void StepFrame(SimulationWorld world, Action? afterEachTick = null)
    {
        for (var tick = 0; tick < TicksPerFrame(world); tick++)
        {
            world.Step();
            afterEachTick?.Invoke();
        }
    }
}
