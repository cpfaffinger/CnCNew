using System.Runtime.CompilerServices;
using CnCNew.Simulation.Random;

namespace CnCNew.Simulation.Time;

/// <summary>
/// Run-time arithmetic on durations that must stay on the 30 Hz grid of the original (<c>docs/TICKRATE-MIGRATION.md</c> §1, "W1"): where the simulation
/// rounds, draws or counts in ticks while it runs, it computes in logic frames of the original and scales only the result, so the outcome is the same
/// game time at every rate. Every function is the former expression at 30 Hz (the factor is 1 and <see cref="SimulationTime.ToOriginalFrames"/> returns
/// its argument), which is what keeps 30 Hz bit-identical. None of them is for a per-tick path: they run when a duration is decided.
/// <para>
/// One name per concept; this is the only helper of its kind (phase 20 merged two that were written in parallel, <c>Tests.TickRate.Grid.cs</c> holds the
/// proof that the merged forms are the same functions). The caller information only feeds <see cref="SimulationTime.Probe"/> and is never passed by hand.
/// </para>
/// </summary>
public static class FrameGrid
{
    /// <summary><paramref name="ticks"/> rounded down to whole logic frames of the original, in ticks. 30 Hz: the argument.</summary>
    public static long FloorToFrame(this SimulationTime time, long ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var floored = time.OriginalFrames(time.FramesIn(ticks));
        if (time.Probe is { } probe)
        {
            probe.Call(TimeProbeKind.RoundedToFrame, nameof(FloorToFrame));
            if (floored != ticks) probe.Hit(TimeProbeKind.RoundedToFrame, nameof(FloorToFrame), file, line);
        }
        return floored;
    }

    /// <summary><paramref name="ticks"/> rounded up to whole logic frames of the original, in ticks. 30 Hz: the argument.</summary>
    public static long CeilingToFrame(this SimulationTime time, long ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var raised = time.OriginalFrames(time.FramesIn(ticks + time.TicksPerOriginalFrame - TickOrder.Next));
        if (time.Probe is { } probe)
        {
            probe.Call(TimeProbeKind.RoundedToFrame, nameof(CeilingToFrame));
            if (raised != ticks) probe.Hit(TimeProbeKind.RoundedToFrame, nameof(CeilingToFrame), file, line);
        }
        return raised;
    }

    /// <summary>
    /// "At least one frame" (<c>if (delay &lt; 1) delay = 1</c> in the reference, and every lower bound the rewrite adds so that nothing recurs faster than
    /// the original could): the larger of <paramref name="ticks"/> and one logic frame of the original. 30 Hz: <c>Math.Max(ticks, 1)</c>.
    /// </summary>
    public static long AtLeastOneFrame(this SimulationTime time, long ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var frame = time.TicksPerOriginalFrame;
        if (time.Probe is { } probe)
        {
            probe.Call(TimeProbeKind.MinimumOneFrame, nameof(AtLeastOneFrame));
            if (ticks < frame) probe.Hit(TimeProbeKind.MinimumOneFrame, nameof(AtLeastOneFrame), file, line);
            if (ticks > 0 && ticks % frame != 0) probe.Hit(TimeProbeKind.OffGrid, nameof(AtLeastOneFrame), file, line);
        }
        return Math.Max(ticks, frame);
    }

    /// <summary>
    /// A whole-number draw over a range of durations (<c>GameLogicRandomValue(minFrames, maxFrames)</c>): drawn over the range in logic frames of the
    /// original (one draw, the same stream at every rate), then scaled, so the result stays on the grid. 30 Hz: <c>random.Range(minTicks, maxTicks)</c>.
    /// </summary>
    public static long DrawFrames(this SimulationTime time, SageRandom random, long minTicks, long maxTicks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (time.Probe is { } probe)
        {
            probe.Call(TimeProbeKind.OffGrid, nameof(DrawFrames));
            if (minTicks % time.TicksPerOriginalFrame != 0 || maxTicks % time.TicksPerOriginalFrame != 0) probe.Hit(TimeProbeKind.OffGrid, nameof(DrawFrames), file, line);
        }
        return time.OriginalFrames(random.Range(checked((int)time.FramesIn(minTicks)), checked((int)time.FramesIn(maxTicks))));
    }

    /// <summary>
    /// The reload value of a countdown of the reference's shape <c>if (n &gt; 0) { n--; return; } act(); n = N;</c>: the action recurs every <c>N + 1</c>
    /// updates, not every <c>N</c>. A module that updates every tick keeps the period of <c>N + 1</c> frames of the original only when the counter is
    /// reloaded with <c>(N + 1) · ticksPerFrame − 1</c> ticks. <paramref name="ticks"/> is the delay in ticks on the grid (frames · ticks per frame); a
    /// remainder below one frame is cut off. 30 Hz: <paramref name="ticks"/> itself.
    /// </summary>
    public static long CountdownReload(this SimulationTime time, long ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (time.Probe is { } probe)
        {
            probe.Call(TimeProbeKind.OffGrid, nameof(CountdownReload));
            if (ticks % time.TicksPerOriginalFrame != 0) probe.Hit(TimeProbeKind.OffGrid, nameof(CountdownReload), file, line);
        }
        return time.OriginalFrames(time.FramesIn(ticks) + 1) - TickOrder.Next;
    }
}
