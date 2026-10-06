using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CnCNew.Simulation.Time;

/// <summary>Thrown when a world, catalog, snapshot or replay asks for a tick rate the simulation does not support.</summary>
public sealed class UnsupportedSimulationRateException(long ticksPerSecond)
    : ArgumentException($"Unsupported simulation rate {ticksPerSecond} Hz; supported are 30, 60 and 120 Hz.")
{
    public long TicksPerSecond { get; } = ticksPerSecond;
}

/// <summary>
/// The tick rate of a world: how many simulation steps make one second of game time. Supported are exactly 30 · 2ᵏ for k = 0, 1, 2 (30, 60 and
/// 120 Hz): frame values of the original (authored for 30 logic frames per second, <c>LOGICFRAMES_PER_SECOND</c>, GameCommon.h:72) then scale by a
/// whole factor. There is one instance per rate, so two rates are equal exactly when they are the same object.
/// <para>
/// A catalog is built for one rate, a world runs at the rate of its configuration and refuses a catalog of another rate, and snapshots and replays
/// carry the rate they were written at. All conversions that depend on the rate live in <see cref="Time"/>.
/// </para>
/// </summary>
public sealed class SimulationRate
{
    /// <summary>The logic frame rate of the original (<c>LOGICFRAMES_PER_SECOND</c>): the grid all authored frame counts and durations stay on.</summary>
    public const int OriginalFramesPerSecond = 30;

    public static SimulationRate Hz30 { get; } = new(OriginalFramesPerSecond);
    public static SimulationRate Hz60 { get; } = new(2 * OriginalFramesPerSecond);
    public static SimulationRate Hz120 { get; } = new(4 * OriginalFramesPerSecond);

    /// <summary>The rate of the original, <see cref="Hz30"/>: one tick is one logic frame of the original.</summary>
    public static SimulationRate Original => Hz30;

    private SimulationRate(int ticksPerSecond)
    {
        TicksPerSecond = ticksPerSecond;
        TicksPerOriginalFrame = ticksPerSecond / OriginalFramesPerSecond;
        Time = new SimulationTime(this);
    }

    public int TicksPerSecond { get; }

    /// <summary>Ticks per logic frame of the original: 1, 2 or 4.</summary>
    public int TicksPerOriginalFrame { get; }

    /// <summary>The conversions of this rate.</summary>
    public SimulationTime Time { get; }

    public bool IsOriginal => TicksPerOriginalFrame == 1;

    public static bool IsSupported(long ticksPerSecond) => ticksPerSecond is 30 or 60 or 120;

    /// <exception cref="UnsupportedSimulationRateException">The rate is not 30, 60 or 120.</exception>
    public static SimulationRate FromTicksPerSecond(long ticksPerSecond) => ticksPerSecond switch
    {
        30 => Hz30,
        60 => Hz60,
        120 => Hz120,
        _ => throw new UnsupportedSimulationRateException(ticksPerSecond),
    };

    /// <summary>
    /// The definition fingerprint a world at this rate carries (what a world, a replay and a save are checked against): the rate is part of a
    /// catalog's identity, because the same definitions yield different tick counts at another rate. At the original rate the fingerprint is
    /// returned unchanged, so that everything recorded at 30 Hz keeps its identity; at 60 and 120 Hz the rate is folded in.
    /// </summary>
    public string Combine(string definitionFingerprint)
    {
        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        return IsOriginal ? definitionFingerprint
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitionFingerprint + "+tick-rate:" + TicksPerSecond.ToString(CultureInfo.InvariantCulture))));
    }

    public override string ToString() => $"{TicksPerSecond.ToString(CultureInfo.InvariantCulture)} Hz";
}
