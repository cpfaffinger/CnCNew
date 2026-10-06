using CnCNew.Data;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Tests;

/// <summary>
/// Time in tests, so that no test writes a rate of its own. Three different things used to be the literal <c>1.0 / 30</c> and a bare tick count:
/// <list type="bullet">
/// <item><description><see cref="FrameSeconds"/>: one original frame (1/30 s) of wall time - the step of a display that draws thirty frames per second and of everything that does not
/// depend on a world (shell, camera, cursor). A loop of n such steps lasts n/30 s at every tick rate (two ticks per step at 60 Hz).</description></item>
/// <item><description><see cref="TickSeconds"/>: the step that advances one world by exactly one tick, from the rate of that world.</description></item>
/// <item><description><see cref="Ticks"/> / <see cref="Frames"/>: a duration that means game time, in ticks of the test's world. An expectation that really means ticks
/// (scheduler order, sequence numbers, "one tick later") stays a plain number.</description></item>
/// </list>
/// </summary>
internal static class TestTime
{
    /// <summary>One original frame of wall time, in seconds.</summary>
    public const double FrameSeconds = 1.0 / OriginalFrame.PerSecond;

    /// <summary><see cref="FrameSeconds"/> in single precision, for the camera and input functions that take a <see cref="float"/>.</summary>
    public const float FrameSecondsF = 1f / OriginalFrame.PerSecond;

    /// <summary>Original frames per second, for loops that run "n seconds of display frames".</summary>
    public const int FramesPerSecond = OriginalFrame.PerSecond;

    /// <summary>The time service of the original's rate, for the fixtures that have no world (an FX sweep, a cue filter).</summary>
    public static SimulationTime Original => SimulationRate.Original.Time;

    /// <summary>The fixed step that advances <paramref name="world"/> by exactly one tick.</summary>
    public static double TickSeconds(SimulationWorld world) => 1.0 / world.Time.TicksPerSecond;

    /// <summary>Whole seconds of game time in ticks of <paramref name="world"/>.</summary>
    public static long Ticks(SimulationWorld world, long seconds) => world.Time.WholeSecondsToTicks(seconds);

    /// <summary>Original frames (1/30 s of game time each) in ticks of <paramref name="world"/>.</summary>
    public static long Frames(SimulationWorld world, long originalFrames) => world.Time.OriginalFrames(originalFrames);

    /// <summary>The presentation clock a fake view reports for a tick counted at the original's rate.</summary>
    public static CnCNew.Presentation.GameClock ClockAt(long tick) => new(tick, SimulationRate.Original.TicksPerSecond, 0);
}
