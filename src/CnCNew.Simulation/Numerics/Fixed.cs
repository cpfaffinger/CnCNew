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

    /// <summary>
    /// Deterministic integer square root (Newton's method on <see cref="Int128"/>, no floating point): avoids tying phase-5
    /// straight-line movement's distance normalization to platform math-library transcendentals. Rounds down; throws for a
    /// negative operand.
    /// </summary>
    public static Fixed Sqrt(Fixed value)
    {
        if (value._raw < 0) throw new ArgumentOutOfRangeException(nameof(value), "Cannot take the square root of a negative fixed-point value.");
        if (value._raw == 0) return Zero;
        if (value._raw < 1L << (63 - FractionBits))
        {
            // raw * ScaleRaw fits 64 bits: the same Newton iteration on machine words. It ends at floor(sqrt(target)) from every start at or above the root, so the
            // closer start (a power of two just above the root) changes the number of steps, not the result.
            var small = (ulong)value._raw << FractionBits;
            var root = 1UL << ((65 - System.Numerics.BitOperations.LeadingZeroCount(small)) >> 1);
            while (true)
            {
                var next = (root + small / root) >> 1;
                if (next >= root) break;
                root = next;
            }
            return new((long)root);
        }
        // result_raw = floor(sqrt(raw * ScaleRaw)), see ToDecimal()/FromDecimal() round trip reasoning above.
        var target = (Int128)value._raw * ScaleRaw;
        var x = (Int128)value._raw > ScaleRaw ? (Int128)value._raw : ScaleRaw; // initial guess >= true root
        while (true)
        {
            var next = (x + target / x) / 2;
            if (next >= x) break;
            x = next;
        }
        return new(Narrow(x));
    }

    public static Fixed operator +(Fixed a, Fixed b) => new(checked(a._raw + b._raw));
    public static Fixed operator -(Fixed a, Fixed b) => new(checked(a._raw - b._raw));
    public static Fixed operator -(Fixed a) => new(checked(-a._raw));

    // Products and quotients are computed on magnitudes: "to nearest, ties away from zero" is then "add half, truncate", and the sign goes back on at the end.
    // The result is the one of the 128-bit formula (magnitude(a) * magnitude(b) + ScaleRaw / 2) / ScaleRaw in every case, including the overflow.
    public static Fixed operator *(Fixed a, Fixed b)
    {
        var high = Math.BigMul(Magnitude(a._raw), Magnitude(b._raw), out var low);
        var rounded = unchecked(low + (ulong)(ScaleRaw >> 1));
        if (rounded < low) high++;
        if (high >> FractionBits != 0) throw new OverflowException("Fixed-point result out of range.");
        return new(Signed((high << (64 - FractionBits)) | (rounded >> FractionBits), (a._raw ^ b._raw) < 0));
    }

    public static Fixed operator /(Fixed a, Fixed b)
    {
        if (b._raw == 0) throw new DivideByZeroException();
        var dividend = Magnitude(a._raw);
        if (dividend >> (63 - FractionBits) == 0)
        {
            // magnitude(a) * ScaleRaw is below 2^63 and half the divisor at most 2^62: the rounded quotient needs no 128-bit division.
            var divisor = Magnitude(b._raw);
            return new(Signed(((dividend << FractionBits) + (divisor >> 1)) / divisor, (a._raw ^ b._raw) < 0));
        }
        var numerator = (Int128)a._raw << FractionBits;
        return new(Narrow(RoundDivide(numerator, b._raw)));
    }

    private static ulong Magnitude(long raw) => raw < 0 ? unchecked((ulong)(-raw)) : (ulong)raw;

    private static long Signed(ulong magnitude, bool negative)
    {
        if (magnitude <= long.MaxValue) return negative ? -(long)magnitude : (long)magnitude;
        if (negative && magnitude == 1UL << 63) return long.MinValue;
        throw new OverflowException("Fixed-point result out of range.");
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
