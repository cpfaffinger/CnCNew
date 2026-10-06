using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// Run-time arithmetic on durations that has to stay on the 30 Hz grid of the original (docs/TICKRATE-MIGRATION.md §1, "W1"): the value is formed in logic
/// frames of the original and only the result is scaled to ticks, so that it is the same game time at every rate. At 30 Hz every function is the plain
/// expression it replaces.
/// </summary>
internal static class TickGrid
{
    /// <summary>
    /// The start value of a counter of the reference that is set to a number of frames, decremented once per update and acted on in the update <i>after</i>
    /// it reached zero (<c>if (m_next > 0) { m_next--; return; } act(); m_next = N;</c>). Such a counter has a period of <c>N + 1</c> frames of the original,
    /// not <c>N</c>. A module that updates every tick keeps that period only when the counter starts at <c>(N + 1) · ticksPerFrame − 1</c> ticks, which is the
    /// scaled value plus <c>ticksPerFrame − 1</c>. <paramref name="ticksOnGrid"/> is the delay already in ticks (frames · ticks per frame). 30 Hz: the value itself.
    /// </summary>
    public static long CountdownActingAfterZero(SimulationTime time, long ticksOnGrid) => ticksOnGrid + (time.TicksPerOriginalFrame - TickOrder.Next);

    /// <summary>
    /// A duration drawn between two bounds that are ticks on the grid (catalog durations): drawn in logic frames of the original
    /// (<c>GameLogicRandomValue(minFrames, maxFrames)</c>) and then scaled. The draw consumes the logic stream exactly as at 30 Hz and the result stays on the
    /// grid. 30 Hz: <c>LogicRandom.Range(minTicks, maxTicks)</c>.
    /// </summary>
    public static long RandomTicks(IWorldServices world, long minTicksOnGrid, long maxTicksOnGrid)
        => world.Time.OriginalFrames(world.LogicRandom.Range(checked((int)world.Time.ToOriginalFrames(minTicksOnGrid)), checked((int)world.Time.ToOriginalFrames(maxTicksOnGrid))));
}
