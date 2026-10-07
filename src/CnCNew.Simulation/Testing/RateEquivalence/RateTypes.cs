using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// What a measured game quantity is, and with it how far its value at 60 and 120 Hz may be from the value at 30 Hz. The tolerances are fixed in
/// <c>docs/SIMULATION-CORE.md</c> §39 and in <see cref="RateTolerance"/>; they were written down before the first run of the suite and are never fitted
/// to a result.
/// </summary>
public enum RateQuantityKind
{
    /// <summary>A duration that comes from the data and lies on the 30 Hz grid of the original (shot spacing, reload, build time, a script timer): exactly equal.</summary>
    ExactTime,
    /// <summary>The time of an event that results from movement or integration (arrival, impact, landing, first shot after a traverse): within one frame of the original.</summary>
    MovementEventTime,
    /// <summary>A number of events in a fixed game time (shots, pulses, flashes): exactly equal.</summary>
    Count,
    /// <summary>A length or a coordinate of an end pose, in world units.</summary>
    Distance,
    /// <summary>An angle, in radians.</summary>
    Angle,
    /// <summary>An amount accumulated over a fixed game time (damage or healing per 60 s, income per 5 min).</summary>
    RateOver60s,
}

/// <summary>
/// One measured quantity of a <see cref="RateCase"/> at one rate. <paramref name="Units"/> is an integer: a time in 1/120 s (ticks times 120 over the
/// tick rate, exact at 30, 60 and 120 Hz), a count, or the raw value of a <see cref="Fixed"/> (world units, radians, an amount).
/// </summary>
/// <param name="Present">False when the event the quantity belongs to never happened within the budget of the case.</param>
public readonly record struct RateQuantity(string Name, RateQuantityKind Kind, long Units, bool Present)
{
    public static RateQuantity Absent(string name, RateQuantityKind kind) => new(name, kind, 0, false);
}

/// <summary>
/// One case of the rate-equivalence suite: a small scene that is staged and measured the same way at 30, 60 and 120 Hz. <paramref name="Measure"/> must be
/// deterministic and must name its quantities the same at every rate.
/// </summary>
public sealed record RateCase(string Id, string Group, Func<RateScene, ImmutableArray<RateQuantity>> Measure);

/// <summary>The tolerances of <c>docs/SIMULATION-CORE.md</c> §39. The reference is always the value at 30 Hz.</summary>
public static class RateTolerance
{
    /// <summary>One frame of the original in units of 1/120 s.</summary>
    public const long OneOriginalFrameUnits = RateScene.TimeUnitsPerSecond / Time.SimulationRate.OriginalFramesPerSecond;

    /// <summary>Half a percent, as a divisor.</summary>
    public const long HalfPercentDivisor = 200;

    /// <summary>One percent, as a divisor.</summary>
    public const long OnePercentDivisor = 100;

    /// <summary>The absolute floor of a distance: half a world unit, a twentieth of a pathfinding cell of ten units.</summary>
    public static readonly long DistanceFloorRaw = Fixed.FromFraction(1, 2).Raw;

    /// <summary>The absolute floor of an angle: 0.01 rad.</summary>
    public static readonly long AngleFloorRaw = Fixed.FromFraction(1, 100).Raw;

    /// <summary>The largest allowed absolute difference to <paramref name="reference30"/> for a quantity of <paramref name="kind"/>.</summary>
    public static long Limit(RateQuantityKind kind, long reference30)
    {
        var magnitude = reference30 == long.MinValue ? long.MaxValue : Math.Abs(reference30);
        return kind switch
        {
            RateQuantityKind.ExactTime or RateQuantityKind.Count => 0,
            RateQuantityKind.MovementEventTime => OneOriginalFrameUnits,
            RateQuantityKind.Distance => Math.Max(magnitude / HalfPercentDivisor, DistanceFloorRaw),
            RateQuantityKind.Angle => Math.Max(magnitude / HalfPercentDivisor, AngleFloorRaw),
            RateQuantityKind.RateOver60s => magnitude / OnePercentDivisor,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public static bool Within(RateQuantityKind kind, long reference30, long value)
    {
        var difference = (Int128)value - reference30;
        if (difference < 0) difference = -difference;
        return difference <= Limit(kind, reference30);
    }
}

/// <summary>Collects the quantities of one measurement; every helper turns a value of the simulation into the integer unit of its kind.</summary>
public sealed class RateQuantities(RateScene scene)
{
    private readonly ImmutableArray<RateQuantity>.Builder _items = ImmutableArray.CreateBuilder<RateQuantity>();

    /// <summary>A duration in ticks of the scene (negative: the event never happened).</summary>
    public RateQuantities Time(string name, RateQuantityKind kind, long ticks)
    {
        if (kind is not (RateQuantityKind.ExactTime or RateQuantityKind.MovementEventTime)) throw new ArgumentException("A time is exact or an event time.", nameof(kind));
        _items.Add(ticks < 0 ? RateQuantity.Absent(name, kind) : new RateQuantity(name, kind, scene.Units(ticks), true));
        return this;
    }

    public RateQuantities Exact(string name, long ticks) => Time(name, RateQuantityKind.ExactTime, ticks);
    public RateQuantities Event(string name, long ticks) => Time(name, RateQuantityKind.MovementEventTime, ticks);

    public RateQuantities Count(string name, long count, bool present = true)
    {
        _items.Add(new RateQuantity(name, RateQuantityKind.Count, count, present));
        return this;
    }

    public RateQuantities Distance(string name, Fixed value, bool present = true)
    {
        _items.Add(new RateQuantity(name, RateQuantityKind.Distance, value.Raw, present));
        return this;
    }

    public RateQuantities Angle(string name, Fixed value, bool present = true)
    {
        _items.Add(new RateQuantity(name, RateQuantityKind.Angle, value.Raw, present));
        return this;
    }

    public RateQuantities Amount(string name, Fixed value, bool present = true)
    {
        _items.Add(new RateQuantity(name, RateQuantityKind.RateOver60s, value.Raw, present));
        return this;
    }

    public ImmutableArray<RateQuantity> Done() => _items.ToImmutable();
}

/// <summary>Geometry helpers of the measurements. Positions are bounded by the map, so the squares below are squares of coordinate differences.</summary>
public static class RateMeasure
{
    public static Fixed Distance(Fixed ax, Fixed ay, Fixed bx, Fixed by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    public static Fixed Distance(World.Entity a, World.Entity b) => Distance(a.X, a.Y, b.X, b.Y);

    /// <summary>A measured value as text for a report: seconds, world units, radians or a plain number, always with the invariant culture.</summary>
    public static string Display(RateQuantityKind kind, long units) => kind switch
    {
        RateQuantityKind.ExactTime or RateQuantityKind.MovementEventTime => ((decimal)units / RateScene.TimeUnitsPerSecond).ToString("0.0000", CultureInfo.InvariantCulture),
        RateQuantityKind.Count => units.ToString(CultureInfo.InvariantCulture),
        RateQuantityKind.Angle => Fixed.FromRaw(units).ToDecimal().ToString("0.00000", CultureInfo.InvariantCulture),
        _ => Fixed.FromRaw(units).ToDecimal().ToString("0.000", CultureInfo.InvariantCulture),
    };

    public static string Unit(RateQuantityKind kind) => kind switch
    {
        RateQuantityKind.ExactTime or RateQuantityKind.MovementEventTime => "s",
        RateQuantityKind.Count => "n",
        RateQuantityKind.Angle => "rad",
        RateQuantityKind.Distance => "units",
        _ => "amount",
    };
}
