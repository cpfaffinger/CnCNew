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
    /// <summary>The smallest positive value: one step of the resolution.</summary>
    public static Fixed Epsilon => new(1);
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

    /// <summary>numerator / denominator, rounded to nearest with ties away from zero: a constant that is a fraction, at whatever resolution.</summary>
    public static Fixed FromFraction(long numerator, long denominator)
    {
        if (denominator == 0) throw new DivideByZeroException();
        var scaled = (Int128)numerator << FractionBits;
        var magnitude = Int128.Abs(scaled);
        var divisor = Int128.Abs(denominator);
        var quotient = (magnitude + divisor / 2) / divisor;
        if (quotient > long.MaxValue) throw new OverflowException($"{numerator}/{denominator} does not fit the fixed-point range.");
        return new((numerator < 0) != (denominator < 0) ? -(long)quotient : (long)quotient);
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

    /// <summary>
    /// Deterministic integer square root (no floating point): avoids tying phase-5 straight-line movement's distance normalization to platform
    /// math-library transcendentals. Rounds down; throws for a negative operand.
    /// </summary>
    public static Fixed Sqrt(Fixed value) => new(FixedMath.Sqrt(value._raw, FractionBits));

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a._raw + b._raw));
    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a._raw - b._raw));
    public static Fixed operator -(Fixed a) => new(checked(-a._raw));

    // Products and quotients: to nearest, ties away from zero; the routines and their derivation are in FixedMath.
    public static Fixed operator *(Fixed a, Fixed b) => new(FixedMath.Multiply(a._raw, b._raw, FractionBits));

    public static Fixed operator /(Fixed a, Fixed b) => new(FixedMath.Divide(a._raw, b._raw, FractionBits));

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
}
