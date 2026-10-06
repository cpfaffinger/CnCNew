using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Time;

/// <summary>
/// The rate-independent part of the time contract: sentinels of the update scheduler and conversions that do not involve time. Everything that depends
/// on the tick rate (durations, values per second, logic frames of the original) lives in <see cref="SimulationTime"/>, reached through the world
/// (<c>world.Time</c>) at run time and through the catalog's rate while a catalog is built.
/// </summary>
public static class SimulationTiming
{
    public const long MillisecondsPerSecond = 1000;

    /// <summary>The reference never returns tick 0 as a wake frame from an update; the smallest sleep is one tick.</summary>
    public const long SleepNone = 1;

    /// <summary>Same value as <c>UPDATE_SLEEP_FOREVER</c> (UpdateModule.h): 0x3fffffff ticks (about 414 days at 30 Hz); a sentinel, never scaled with the rate.</summary>
    public const long SleepForever = 0x3fffffff;

    private const decimal Pi = 3.1415926535897932384626433833m;

    /// <summary>Degrees to radians with π to 28 digits, rounded to <see cref="Fixed"/> (nearest, ties away from zero).</summary>
    public static Fixed DegreesToRadians(decimal degrees) => Fixed.FromDecimal(degrees * Pi / 180m);

    /// <summary>Ratio (1.0 = 100%) from the definition layer's percentage.</summary>
    public static Fixed RatioToFixed(decimal ratio) => Fixed.FromDecimal(ratio);
}
