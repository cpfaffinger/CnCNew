using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Time;

/// <summary>
/// The frame of an object (phase 23, <c>docs/RATE-INVARIANCE-CONSOLIDATION.md</c> §1.3): the phase of a new object inside a logic frame of the original is the tick it appeared
/// in. Modules of one object that decide "once per frame from the object's appearance" share these ticks and run in them in the order of their module index, as they do in
/// every tick at 30 Hz. At 30 Hz every tick begins a frame and nothing here computes anything.
/// </summary>
public static class EntityFrames
{
    /// <summary>True in the ticks a logic frame of the original of <paramref name="entity"/> begins with: whole frames after the tick it appeared in. Always true at 30 Hz.</summary>
    public static bool BeginsFrameOf(this SimulationTime time, Entity entity, long tick)
        => time.TicksPerOriginalFrame == TickOrder.Next || (tick - entity.SpawnTick) % time.TicksPerOriginalFrame == 0;
}
