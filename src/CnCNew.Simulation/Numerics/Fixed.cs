using System.Globalization;

namespace CnCNew.Simulation.Numerics;

/// <summary>
/// Signed fixed-point number with 16 fractional bits stored in a <see cref="long"/> (Q47.16).
/// <para>
/// Why fixed point: the reference engine keeps <c>Real</c> (binary32) values in its state, which ties determinism to
/// compiler flags and FPU modes (<c>setFPMode()</c> in <c>INI::load</c>). Integer arithmetic gives bit-identical results
/// on every platform. 16 fractional bits give a resolution of 1/65536 world units, and a 64-bit raw value keeps
/// ±1.4·10^14 units, far beyond any map. Products and quotients use 128-bit intermediates.
/// </para>
/// <para>
/// Rounding rule (all operations): to nearest, ties away from zero. Overflow throws <see cref="OverflowException"/>;
/// no operation wraps or saturates silently.
/// </para>
/// </summary>
public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
{
    public const int FractionBits = 16;
    public const long ScaleRaw = 1L << FractionBits;

    private readonly long _raw;

    private Fixed(long raw) => _raw = raw;

    public long Raw => _raw;
    public static Fixed Zero => default;
    public static Fixed One => new(ScaleRaw);
    public static Fixed MaxValue => new(long.MaxValue);
    public static Fixed MinValue => new(long.MinValue);

    public static Fixed FromRaw(long raw) => new(raw);

    public static Fixed FromInt(long value)
    {
        var raw = checked(value * ScaleRaw);
        return new(raw);
    }

    /// <summary>Converts a decimal (as produced by the definition compiler) with the documented rounding rule.</summary>
    public static Fixed FromDecimal(decimal value)
    {
        var scaled = decimal.Round(value * ScaleRaw, 0, MidpointRounding.AwayFromZero);
        if (scaled > long.MaxValue || scaled < long.MinValue) throw new OverflowException($"{value} does not fit the fixed-point range.");
        return new((long)scaled);
    }

    /// <summary>Exact conversion (every raw value is representable as a decimal).</summary>
    public decimal ToDecimal() => (decimal)_raw / ScaleRaw;

    /// <summary>Whole units, rounded toward negative infinity.</summary>
    public long Floor() => _raw >> FractionBits;

    /// <summary>Whole units, rounded toward positive infinity.</summary>
    public long Ceiling() => -((-_raw) >> FractionBits);

    /// <summary>Whole units, rounded to nearest, ties away from zero.</summary>
    public long Round() => _raw >= 0 ? (_raw + (ScaleRaw >> 1)) >> FractionBits : -((-_raw + (ScaleRaw >> 1)) >> FractionBits);

    public Fixed Abs() => _raw < 0 ? new(checked(-_raw)) : this;
    public bool IsNegative => _raw < 0;

    public static Fixed Min(Fixed a, Fixed b) => a._raw <= b._raw ? a : b;
    public static Fixed Max(Fixed a, Fixed b) => a._raw >= b._raw ? a : b;

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a._raw + b._raw));
    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a._raw - b._raw));
    public static Fixed operator -(Fixed a) => new(checked(-a._raw));

    public static Fixed operator *(Fixed a, Fixed b)
    {
        var product = (Int128)a._raw * b._raw;
        return new(Narrow(RoundDivide(product, ScaleRaw)));
    }

    public static Fixed operator /(Fixed a, Fixed b)
    {
        if (b._raw == 0) throw new DivideByZeroException();
        var numerator = (Int128)a._raw << FractionBits;
        return new(Narrow(RoundDivide(numerator, b._raw)));
    }

    /// <summary>Multiplies by an integer without intermediate rounding.</summary>
    public static Fixed operator *(Fixed a, long factor) => new(checked(a._raw * factor));

    public static bool operator ==(Fixed a, Fixed b) => a._raw == b._raw;
    public static bool operator !=(Fixed a, Fixed b) => a._raw != b._raw;
    public static bool operator <(Fixed a, Fixed b) => a._raw < b._raw;
    public static bool operator >(Fixed a, Fixed b) => a._raw > b._raw;
    public static bool operator <=(Fixed a, Fixed b) => a._raw <= b._raw;
    public static bool operator >=(Fixed a, Fixed b) => a._raw >= b._raw;

    public bool Equals(Fixed other) => _raw == other._raw;
    public override bool Equals(object? obj) => obj is Fixed other && Equals(other);
    public override int GetHashCode() => _raw.GetHashCode();
    public int CompareTo(Fixed other) => _raw.CompareTo(other._raw);
    public override string ToString() => ToDecimal().ToString(CultureInfo.InvariantCulture);

    private static Int128 RoundDivide(Int128 numerator, Int128 denominator)
    {
        var absDenominator = denominator < 0 ? -denominator : denominator;
        var negative = (numerator < 0) != (denominator < 0);
        var absNumerator = numerator < 0 ? -numerator : numerator;
        var quotient = (absNumerator + absDenominator / 2) / absDenominator;
        return negative ? -quotient : quotient;
    }

    private static long Narrow(Int128 value)
    {
        if (value > long.MaxValue || value < long.MinValue) throw new OverflowException("Fixed-point result out of range.");
        return (long)value;
    }
}
