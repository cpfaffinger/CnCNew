using System.Runtime.CompilerServices;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Time;

/// <summary>
/// The time service of a world and of the catalog it runs on: every conversion between data units (milliseconds, seconds, values per second, logic
/// frames of the original) and simulation units (ticks, values per tick) for one <see cref="SimulationRate"/>. Integer and decimal arithmetic only.
/// <para>
/// <b>Rule of the grid.</b> Authored durations stay on the 30 Hz grid of the original: a duration is first rounded to whole logic frames of the
/// original exactly as the reference does (<c>INI::parseDurationUnsignedInt</c>: <c>ceilf(msec * LOGICFRAMES_PER_MSEC_REAL)</c>) and only then
/// multiplied by <see cref="SimulationRate.TicksPerOriginalFrame"/>. A weapon with a 50 ms reload therefore takes two frames of the original
/// (66.7 ms) at every rate. The reference multiplies in binary32 with 30/1000 rounded to a float; the rewrite computes <c>ceil(ms * 3 / 100)</c>
/// exactly (the simulation tests list every millisecond value below 100 000 where the two differ).
/// </para>
/// <para>
/// <b>30 Hz is unchanged.</b> At <see cref="SimulationRate.Original"/> every function evaluates the very expression the former static conversions
/// used (same operands, same order), so every result is bit-identical; each function states its rounding and why.
/// </para>
/// </summary>
public sealed class SimulationTime
{
    private const decimal Pi = 3.1415926535897932384626433833m;
    private const int OriginalFramesPerSecond = SimulationRate.OriginalFramesPerSecond;
    private readonly int _ticksPerSecond;
    private readonly int _ticksPerOriginalFrame;

    internal SimulationTime(SimulationRate rate)
    {
        Rate = rate;
        _ticksPerSecond = rate.TicksPerSecond;
        _ticksPerOriginalFrame = rate.TicksPerOriginalFrame;
        TicksPerSecondFixed = Fixed.FromInt(_ticksPerSecond);
    }

    public SimulationRate Rate { get; }
    public int TicksPerSecond => _ticksPerSecond;
    public int TicksPerOriginalFrame => _ticksPerOriginalFrame;
    public long TicksPerMinute => 60L * _ticksPerSecond;

    /// <summary>
    /// Diagnostic counters, null unless a tool attaches them (<see cref="SimulationTimeProbe"/>). Not state: nothing in the simulation reads them. The
    /// caller information some functions take only names the place a counter belongs to and is never passed by hand.
    /// </summary>
    public SimulationTimeProbe? Probe { get; set; }

    /// <summary>The rate as a <see cref="Fixed"/>, for run-time divisions "per second → per tick" of values that only exist at run time (a ratio of the current maximum health).</summary>
    public Fixed TicksPerSecondFixed { get; }

    // ------------------------------------------------------------------------------------ frames of the original

    /// <summary>
    /// <paramref name="frames"/> logic frames of the original as ticks: <c>frames · rate / 30</c>, exact for every supported rate (a whole factor 1, 2
    /// or 4; overflow throws). The one way to write an original constant in frames, a rhythm, or a duration that the code states in frames. 30 Hz: the factor is 1.
    /// </summary>
    public long OriginalFrames(long frames) => checked(frames * _ticksPerOriginalFrame);

    /// <summary>
    /// Whole logic frames of the original that have fully elapsed in <paramref name="ticks"/> (<c>floor(ticks · 30 / rate)</c>). The inverse of
    /// <see cref="OriginalFrames"/> for values on the grid. 30 Hz: the tick count itself.
    /// </summary>
    public long ToOriginalFrames(long ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (Probe is { } probe)
        {
            probe.Call(TimeProbeKind.OffGrid, nameof(ToOriginalFrames));
            if (ticks % _ticksPerOriginalFrame != 0) probe.Hit(TimeProbeKind.OffGrid, nameof(ToOriginalFrames), file, line);
        }
        return FramesIn(ticks);
    }

    /// <summary><see cref="ToOriginalFrames"/> without the probe, for <see cref="FrameGrid"/> (which counts under its own names).</summary>
    internal long FramesIn(long ticks)
    {
        var frames = ticks / _ticksPerOriginalFrame;
        return ticks < 0 && frames * _ticksPerOriginalFrame != ticks ? frames - 1 : frames;
    }

    /// <summary>Whether <paramref name="tick"/> starts a logic frame of the original (every tick at 30 Hz, every second at 60 Hz, every fourth at 120 Hz).</summary>
    public bool IsOriginalFrameBoundary(long tick) => tick % _ticksPerOriginalFrame == 0;

    // ------------------------------------------------------------------------------------ durations

    /// <summary>
    /// Milliseconds to whole ticks on the grid of the original: <c>ceil(ms · 30 / 1000)</c> frames in integer arithmetic (overflow throws), then
    /// × ticks per frame. 0 ms is 0 ticks; negative durations are rejected. 30 Hz: the former integer expression times 1.
    /// </summary>
    public long DurationMillisecondsToTicks(long milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        var frames = checked(milliseconds * OriginalFramesPerSecond + (SimulationTiming.MillisecondsPerSecond - 1)) / SimulationTiming.MillisecondsPerSecond;
        return checked(frames * _ticksPerOriginalFrame);
    }

    /// <summary>
    /// Decimal milliseconds (definition values) to whole ticks on the grid of the original: <c>decimal.Ceiling(ms · 30 / 1000)</c> frames, then
    /// × ticks per frame. 30 Hz: the former decimal expression times 1.
    /// </summary>
    public long DurationMillisecondsToTicks(decimal milliseconds)
    {
        if (milliseconds < 0m) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        var frames = decimal.Ceiling(milliseconds * OriginalFramesPerSecond / SimulationTiming.MillisecondsPerSecond);
        if (frames > long.MaxValue / _ticksPerOriginalFrame) throw new OverflowException("Duration does not fit the tick range.");
        return (long)frames * _ticksPerOriginalFrame;
    }

    /// <summary>
    /// Unrounded duration in ticks (<c>INI::parseDurationReal</c>): <c>ms · rate / 1000</c> in decimal, rounded once to <see cref="Fixed"/> (nearest,
    /// ties away from zero). Not on the grid, because the reference does not round it either. 30 Hz: the former expression <c>ms · 30 / 1000</c>.
    /// </summary>
    public Fixed DurationMillisecondsToFixedTicks(decimal milliseconds)
    {
        if (milliseconds < 0m) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Durations must not be negative.");
        return Quantised(nameof(DurationMillisecondsToFixedTicks), milliseconds * _ticksPerSecond / SimulationTiming.MillisecondsPerSecond);
    }

    /// <summary>
    /// Seconds (for example <c>BuildTime</c>) to whole ticks on the grid of the original: <c>decimal.Ceiling(seconds · 30)</c> frames, then × ticks
    /// per frame. 30 Hz: the former expression times 1.
    /// </summary>
    public long SecondsToTicks(decimal seconds)
    {
        if (seconds < 0m) throw new ArgumentOutOfRangeException(nameof(seconds), "Durations must not be negative.");
        var frames = decimal.Ceiling(seconds * OriginalFramesPerSecond);
        if (frames > long.MaxValue / _ticksPerOriginalFrame) throw new OverflowException("Duration does not fit the tick range.");
        return (long)frames * _ticksPerOriginalFrame;
    }

    /// <summary>Whole seconds to ticks: <c>seconds · rate</c>, exact (overflow throws). 30 Hz: <c>seconds · 30</c>.</summary>
    public long WholeSecondsToTicks(long seconds) => checked(seconds * _ticksPerSecond);

    // ------------------------------------------------------------------------------------ rates of change

    /// <summary>
    /// Units per second to units per tick (<c>ConvertVelocityInSecsToFrames</c>): <c>value / rate</c> in decimal, rounded once to <see cref="Fixed"/>
    /// (nearest, ties away from zero). 30 Hz: the former expression <c>value / 30</c>.
    /// </summary>
    public Fixed PerSecondToPerTick(decimal unitsPerSecond) => Quantised(nameof(PerSecondToPerTick), unitsPerSecond / _ticksPerSecond);

    /// <summary>
    /// Units per second² to units per tick²: <c>value / (rate · rate)</c> in decimal (the divisor is formed as an integer first, as before), rounded
    /// once to <see cref="Fixed"/>. 30 Hz: the former expression <c>value / 900</c>.
    /// </summary>
    public Fixed PerSecondSquaredToPerTickSquared(decimal unitsPerSecondSquared) => Quantised(nameof(PerSecondSquaredToPerTickSquared), unitsPerSecondSquared / (_ticksPerSecond * _ticksPerSecond));

    /// <summary>
    /// Degrees per second to radians per tick: <c>degrees · π / 180 / rate</c> in decimal, in this order, rounded once to <see cref="Fixed"/>.
    /// 30 Hz: the former expression with 30 as the last divisor.
    /// </summary>
    public Fixed DegreesPerSecondToRadiansPerTick(decimal degreesPerSecond) => Quantised(nameof(DegreesPerSecondToRadiansPerTick), degreesPerSecond * Pi / 180m / _ticksPerSecond);

    /// <summary>
    /// A run-time value per second as a value per tick: <c>value / rate</c> in <see cref="Fixed"/> (one fixed-point division, rounded as
    /// <see cref="Fixed"/> divides). For amounts that depend on run-time state; catalog values use the decimal overload. 30 Hz: the former
    /// <c>value / Fixed.FromInt(30)</c>.
    /// </summary>
    public Fixed PerSecondToPerTick(Fixed unitsPerSecond, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var perTick = unitsPerSecond / TicksPerSecondFixed;
        Probe?.RunTime(nameof(PerSecondToPerTick), unitsPerSecond, perTick, file, line);
        return perTick;
    }

    /// <summary>
    /// A run-time amount spread evenly over a duration: <c>total / ticks</c> in <see cref="Fixed"/> (one fixed-point division) – the health a full heal
    /// adds per tick, the distance a pull covers per tick. The plain division, written through the time service so that a share that rounds to zero can
    /// be counted (<see cref="Probe"/>). The same expression at every rate.
    /// </summary>
    public Fixed SharePerTick(Fixed total, Fixed ticks, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var perTick = total / ticks;
        Probe?.RunTime(nameof(SharePerTick), total, perTick, file, line);
        return perTick;
    }

    // ------------------------------------------------------------------------------------ raw values per frame of the original

    /// <summary>
    /// An additive amount per logic frame of the original (a speed per frame, health per frame) as an amount per tick: <c>value · 30 / rate</c>,
    /// computed as one decimal division by the whole factor 1, 2 or 4 and rounded once to <see cref="Fixed"/>. 30 Hz: the divisor is 1 and a decimal
    /// divided by one is itself, so the result is <c>Fixed.FromDecimal(value)</c> – what reading the raw value gave.
    /// <para>Not for multiplicative factors per frame (damping, friction): those need a root, not a division.</para>
    /// </summary>
    public Fixed PerOriginalFrameToPerTick(decimal valuePerFrame) => Quantised(nameof(PerOriginalFrameToPerTick), valuePerFrame / _ticksPerOriginalFrame);

    /// <summary>
    /// The same for a value that already is a <see cref="Fixed"/>: divided by the whole factor (one fixed-point division; the factors 2 and 4 lose at
    /// most the last bit or two). 30 Hz: the value is returned untouched, without any arithmetic.
    /// </summary>
    public Fixed PerOriginalFrameToPerTick(Fixed valuePerFrame, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_ticksPerOriginalFrame == 1) return valuePerFrame;
        var perTick = valuePerFrame / Fixed.FromInt(_ticksPerOriginalFrame);
        Probe?.RunTime(nameof(PerOriginalFrameToPerTick), valuePerFrame, perTick, file, line);
        return perTick;
    }

    /// <summary>
    /// An additive amount per logic frame² of the original (an acceleration per frame) as an amount per tick²: <c>value · 900 / rate²</c>, one decimal
    /// division by 1, 4 or 16, rounded once to <see cref="Fixed"/>. 30 Hz: the divisor is 1.
    /// </summary>
    public Fixed PerOriginalFrameSquaredToPerTickSquared(decimal valuePerFrameSquared) => Quantised(nameof(PerOriginalFrameSquaredToPerTickSquared), valuePerFrameSquared / (_ticksPerOriginalFrame * _ticksPerOriginalFrame));

    /// <summary>The same for a <see cref="Fixed"/>: one fixed-point division by 4 or 16. 30 Hz: the value is returned untouched.</summary>
    public Fixed PerOriginalFrameSquaredToPerTickSquared(Fixed valuePerFrameSquared, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_ticksPerOriginalFrame == 1) return valuePerFrameSquared;
        var perTick = valuePerFrameSquared / Fixed.FromInt(_ticksPerOriginalFrame * _ticksPerOriginalFrame);
        Probe?.RunTime(nameof(PerOriginalFrameSquaredToPerTickSquared), valuePerFrameSquared, perTick, file, line);
        return perTick;
    }

    // The one rounding of a catalog value to Fixed (nearest, ties away from zero); with a probe attached the loss against the exact value is counted.
    private Fixed Quantised(string function, decimal exact)
    {
        var value = Fixed.FromDecimal(exact);
        Probe?.Quantised(function, exact, value);
        return value;
    }

    // ------------------------------------------------------------------------------------ ticks back to game time

    /// <summary>Whole seconds of game time that have fully elapsed in <paramref name="ticks"/> (integer division, toward zero).</summary>
    public long ToWholeSeconds(long ticks) => ticks / _ticksPerSecond;

    /// <summary>Whole minutes of game time that have fully elapsed in <paramref name="ticks"/> (integer division, toward zero).</summary>
    public long ToWholeMinutes(long ticks) => ticks / TicksPerMinute;

    /// <summary>Whole milliseconds of game time in <paramref name="ticks"/>: <c>ticks · 1000 / rate</c> (integer division, toward zero; overflow throws).</summary>
    public long ToMilliseconds(long ticks) => checked(ticks * SimulationTiming.MillisecondsPerSecond) / _ticksPerSecond;

    /// <summary>Game time in seconds as an exact rational: <paramref name="ticks"/> over <see cref="TicksPerSecond"/>, reduced by nothing (for tools and reports).</summary>
    public (long Numerator, long Denominator) ToSecondsRational(long ticks) => (ticks, _ticksPerSecond);

    /// <summary>Game time in seconds as a <see cref="Fixed"/>: <c>ticks / rate</c>, one fixed-point division.</summary>
    public Fixed ToSeconds(long ticks) => Fixed.FromInt(ticks) / TicksPerSecondFixed;
}
