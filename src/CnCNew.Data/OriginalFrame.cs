namespace CnCNew.Data;

/// <summary>
/// The original frame: 1/30 s, the unit the original authored its client content in (particle systems, FX durations, shell transitions, <c>InGameUI</c> times, camera
/// values, <c>LOGICFRAMES_PER_SECOND</c> in <c>GameCommon.h</c>). It is a unit of the data, not the rate a simulation runs at: the simulation's tick rate is a value of
/// its world and never read from here. Everything outside the simulation that needs the 30 names this constant; nothing else outside the simulation carries a rate.
/// </summary>
public static class OriginalFrame
{
    /// <summary>Original frames per second.</summary>
    public const int PerSecond = 30;
}
