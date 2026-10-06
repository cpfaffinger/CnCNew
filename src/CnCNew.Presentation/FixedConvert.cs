using CnCNew.Simulation.Numerics;

namespace CnCNew.Presentation;

/// <summary>
/// The one place outside the simulation that knows how a <see cref="Fixed"/> is scaled: its conversion to and from floating point. Presentation, game
/// layer, client and tools convert here and nowhere else, so the number of fraction bits is a property of the simulation alone (a source rule forbids the
/// scale as a literal everywhere, <c>Tests.SourceRules</c>). The simulation itself never converts: floating point is forbidden there.
/// </summary>
public static class FixedConvert
{
    /// <summary>The value as a double: exact for every raw value below 2^53.</summary>
    public static double ToDouble(Fixed value) => value.Raw / (double)Fixed.ScaleRaw;

    /// <summary>The value as a single, rounded once (from the exact double).</summary>
    public static float ToSingle(Fixed value) => (float)ToDouble(value);

    /// <summary>A raw fixed-point amount as it travels in <c>SimEvent.Value</c> (damage, money), as a single.</summary>
    public static float RawToSingle(long raw) => ToSingle(Fixed.FromRaw(raw));

    /// <summary>The nearest fixed-point value (ties to even, as <see cref="Math.Round(double)"/>).</summary>
    public static Fixed FromDouble(double value) => Fixed.FromRaw((long)Math.Round(value * Fixed.ScaleRaw));

    /// <summary>The nearest fixed-point value of a single.</summary>
    public static Fixed FromSingle(float value) => FromDouble(value);

    /// <summary>The fixed-point value at or next toward zero: for callers that always cut (the effect layer's ground lookup).</summary>
    public static Fixed FromSingleTruncated(float value) => Fixed.FromRaw((long)((double)value * Fixed.ScaleRaw));
}
