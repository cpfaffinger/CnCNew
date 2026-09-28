using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Time;

/// <summary>
/// Unit conversions from data values to simulation units. Every rule is explicit and integer/decimal based.
/// <list type="bullet">
/// <item><description><b>Tick rate</b>: 30 logic frames per second (<c>LOGICFRAMES_PER_SECOND</c>, GameCommon.h:72).</description></item>
/// <item><description><b>Durations</b>: data durations are milliseconds. Whole-frame durations round up
/// (<c>INI::parseDurationUnsignedInt</c>: <c>ceilf(msec * LOGICFRAMES_PER_MSEC_REAL)</c>). 0 ms is 0 ticks; negative
/// durations are rejected. The reference multiplies in binary32 with 30/1000 rounded to a float; the rewrite computes
/// <c>ceil(ms * 3 / 100)</c> exactly. The simulation tests list every millisecond value below 100 000 where the two
/// differ (binary32 emulation lives in the tests, never in the core).</description></item>
/// <item><description><b>Velocities</b> per second become per tick by dividing by 30 (<c>ConvertVelocityInSecsToFrames</c>),
/// <b>accelerations</b> by dividing by 900, angles in degrees become radians with π to 28 digits. Results are rounded to
/// <see cref="Fixed"/> (nearest, ties away from zero).</description></item>
/// </list>
/// </summary>
public static class SimulationTiming
{
    public const int LogicFramesPerSecond = 30;
    public const long MillisecondsPerSecond = 1000;

    /// <summary>The reference never returns tick 0 as a wake frame from an update; the smallest sleep is one tick.</summary>
    public const long SleepNone = 1;

    /// <summary>Same value as <c>UPDATE_SLEEP_FOREVER</c> (UpdateModule.h): 0x3fffffff ticks, about 414 days at 30 Hz.</summary>
    public const long SleepForever = 0x3fffffff;

    private const decimal Pi = 3.1415926535897932384626433833m;

    /// <summary>Whole ticks, rounded up. Integer arithmetic; overflow throws.</summary>
    public static long DurationMillisecondsToTicks(long milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        return checked(milliseconds * LogicFramesPerSecond + (MillisecondsPerSecond - 1)) / MillisecondsPerSecond;
    }

    /// <summary>Whole ticks, rounded up, for decimal milliseconds (definition values).</summary>
    public static long DurationMillisecondsToTicks(decimal milliseconds)
    {
        if (milliseconds < 0m) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        var ticks = decimal.Ceiling(milliseconds * LogicFramesPerSecond / MillisecondsPerSecond);
        if (ticks > long.MaxValue) throw new OverflowException("Duration does not fit the tick range.");
        return (long)ticks;
    }

    /// <summary>Unrounded duration in ticks (<c>INI::parseDurationReal</c>).</summary>
    public static Fixed DurationMillisecondsToFixedTicks(decimal milliseconds)
    {
        if (milliseconds < 0m) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        return Fixed.FromDecimal(milliseconds * LogicFramesPerSecond / MillisecondsPerSecond);
    }

    /// <summary>Seconds (for example <c>BuildTime</c>) to whole ticks, rounded up.</summary>
    public static long SecondsToTicks(decimal seconds)
    {
        if (seconds < 0m) throw new ArgumentOutOfRangeException(nameof(seconds), "Durations must not be negative.");
        var ticks = decimal.Ceiling(seconds * LogicFramesPerSecond);
        if (ticks > long.MaxValue) throw new OverflowException("Duration does not fit the tick range.");
        return (long)ticks;
    }

    public static Fixed PerSecondToPerTick(decimal unitsPerSecond) => Fixed.FromDecimal(unitsPerSecond / LogicFramesPerSecond);

    public static Fixed PerSecondSquaredToPerTickSquared(decimal unitsPerSecondSquared)
        => Fixed.FromDecimal(unitsPerSecondSquared / (LogicFramesPerSecond * LogicFramesPerSecond));

    public static Fixed DegreesToRadians(decimal degrees) => Fixed.FromDecimal(degrees * Pi / 180m);

    public static Fixed DegreesPerSecondToRadiansPerTick(decimal degreesPerSecond) => Fixed.FromDecimal(degreesPerSecond * Pi / 180m / LogicFramesPerSecond);

    /// <summary>Ratio (1.0 = 100%) from the definition layer's percentage.</summary>
    public static Fixed RatioToFixed(decimal ratio) => Fixed.FromDecimal(ratio);
}
