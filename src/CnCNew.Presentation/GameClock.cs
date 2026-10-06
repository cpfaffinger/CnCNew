using CnCNew.Data;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation;

/// <summary>
/// The clock of the presentation: game time, derived from the simulation's tick, the rate of its world and how far the rendered frame is into the next tick. It stands
/// while the game is paused, runs with the game speed and never jumps, because it is carried as whole numbers (ticks and rate) and never summed up from frame times.
/// Wall time stays the clock of everything that does not depend on the game (shell, mouse cursor, camera).
/// </summary>
/// <param name="Tick">The simulation's current tick (ticks of <paramref name="TicksPerSecond"/>).</param>
/// <param name="TicksPerSecond">The rate of the world the tick belongs to.</param>
/// <param name="Fraction">How far the rendered frame is into the next tick, 0 to 1 (the interpolation factor of the frame).</param>
public readonly record struct GameClock(long Tick, int TicksPerSecond, double Fraction)
{
    public static GameClock Of(SimulationWorld world, double fraction = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new GameClock(world.Tick, world.Time.TicksPerSecond, fraction);
    }

    /// <summary>Ticks of this clock's world in one original frame (1 at 30 Hz, 2 at 60 Hz).</summary>
    public int TicksPerOriginalFrame => TicksPerSecond / OriginalFrame.PerSecond;

    /// <summary>Whole original frames (1/30 s) of game time that have passed: at 30 Hz the tick itself, at 60 Hz every second tick begins a new one.</summary>
    public long OriginalFrames => OriginalFrameOf(Tick, TicksPerOriginalFrame);

    /// <summary>This tick begins an original frame (always at 30 Hz).</summary>
    public bool IsOriginalFrameBoundary => BeginsOriginalFrame(Tick, TicksPerOriginalFrame);

    /// <summary>
    /// The original frame a tick lies in, rounded down also for the tick before the first one (-1 is frame -1 at every rate), so a clock that trails the world by one
    /// tick keeps counting frames evenly.
    /// </summary>
    public static long OriginalFrameOf(long tick, int ticksPerOriginalFrame)
        => tick >= 0 ? tick / ticksPerOriginalFrame : -((-tick + ticksPerOriginalFrame - 1) / ticksPerOriginalFrame);

    /// <summary>The tick is the first one of its original frame: what runs once per original frame runs on these ticks. Every tick at 30 Hz.</summary>
    public static bool BeginsOriginalFrame(long tick, int ticksPerOriginalFrame)
        => tick - OriginalFrameOf(tick, ticksPerOriginalFrame) * ticksPerOriginalFrame == 0;

    /// <summary>Whole milliseconds of game time at the tick, without the fraction.</summary>
    public long Milliseconds => Tick * 1000 / TicksPerSecond;

    /// <summary>Game time in seconds including the fraction, for continuous motion; durations and deadlines are compared in ticks or original frames instead.</summary>
    public double Seconds => (Tick + Fraction) / TicksPerSecond;
}
