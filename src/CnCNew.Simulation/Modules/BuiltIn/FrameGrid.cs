using CnCNew.Simulation.Random;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// Run-time arithmetic on durations that must stay on the 30 Hz grid of the original (<c>docs/TICKRATE-MIGRATION.md</c> §1, "W1"): where a module
/// rounds, draws or counts in ticks while it runs, it computes in logic frames of the original and scales only the result, so the outcome is the
/// same game time at every rate. Every function is the identity arrangement of the former expression at 30 Hz (the factor is 1 and
/// <see cref="SimulationTime.ToOriginalFrames"/> returns its argument), which is what keeps 30 Hz bit-identical. None of them is for a per-tick path:
/// they run when a duration is decided.
/// </summary>
internal static class FrameGrid
{
    /// <summary>One logic frame of the original: the smallest duration the original can express.</summary>
    public const long OneFrame = 1;

    /// <summary><paramref name="ticks"/> rounded down to whole logic frames of the original, in ticks. 30 Hz: the argument.</summary>
    public static long Floor(this SimulationTime time, long ticks) => time.OriginalFrames(time.ToOriginalFrames(ticks));

    /// <summary><paramref name="ticks"/> rounded up to whole logic frames of the original, in ticks (for a non-negative count). 30 Hz: the argument.</summary>
    public static long Ceiling(this SimulationTime time, long ticks) => time.OriginalFrames(time.ToOriginalFrames(ticks + time.OriginalFrames(OneFrame) - 1));

    /// <summary>
    /// "At least one frame" (<c>if (delay &lt; 1) delay = 1</c> in the reference): the larger of <paramref name="ticks"/> and one logic frame of the
    /// original. 30 Hz: <c>Math.Max(ticks, 1)</c>.
    /// </summary>
    public static long AtLeastOneFrame(this SimulationTime time, long ticks) => Math.Max(ticks, time.OriginalFrames(OneFrame));

    /// <summary>
    /// A whole-number draw over a range of durations (<c>GameLogicRandomValue(minFrames, maxFrames)</c>): drawn over the range in logic frames of the
    /// original (one draw, the same stream at every rate), then scaled. 30 Hz: <c>random.Range(minTicks, maxTicks)</c>.
    /// </summary>
    public static long DrawFrames(this SimulationTime time, SageRandom random, long minTicks, long maxTicks)
        => time.OriginalFrames(random.Range(checked((int)time.ToOriginalFrames(minTicks)), checked((int)time.ToOriginalFrames(maxTicks))));

    /// <summary>
    /// The reload value of a countdown of the reference's shape "if (n &gt; 0) { n--; return; } act; n = N": the action recurs every <c>N + 1</c>
    /// updates. With one update per tick the period must stay <c>N + 1</c> frames of the original, so the counter is reloaded with
    /// <c>(N + 1) · ticksPerFrame − 1</c>. 30 Hz: <paramref name="ticks"/> itself.
    /// </summary>
    public static long CountdownReload(this SimulationTime time, long ticks)
        => time.OriginalFrames(time.ToOriginalFrames(ticks) + OneFrame) - TickOrder.Next;
}
