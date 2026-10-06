namespace CnCNew.Data.World;

/// <summary>
/// The one checked way from a single-precision number of a file format into the decimal numbers the simulation's inputs are made of.
/// <para>
/// Map files store coordinates, angles and script parameters as IEEE-754 singles. They used to be converted with the runtime's <c>(decimal)float</c> cast, which
/// keeps seven significant digits. That cast is a routine of the runtime (<c>Decimal.DecCalc.VarDecFromR4</c>): its result for today's .NET is known, its
/// stability across .NET versions and platforms is not promised. <see cref="ToDecimal"/> reproduces it with integer arithmetic only, from the bit pattern, so
/// two machines turn the same file into the same numbers by construction. The steps follow <c>VarDecFromR4</c> one by one, including its double-precision
/// intermediate products (computed here as correctly rounded 53-bit integer mantissas, which is what IEEE-754 prescribes for a single multiplication or
/// division) and its removal of trailing zeros, so the decimal has the same value <em>and</em> the same scale as the cast gave.
/// </para>
/// <para>
/// The identity with the cast is a test, not an assumption: <c>CnCNew.Data.Tests</c> compares both over millions of bit patterns, and
/// <c>CnCNew.Game.Tests</c> (<c>DeterminismTests</c>) over every real of all shipped maps.
/// </para>
/// </summary>
public static class SingleBits
{
    private const int DecimalScaleMax = 28;

    /// <summary>The powers of ten as correctly rounded doubles (exact up to 10^22), as mantissa and binary exponent.</summary>
    private static readonly (ulong Mantissa, int Exponent)[] PowersOfTen = BuildPowersOfTen();

    /// <summary>Whether the bit pattern is a number (not an infinity, not a NaN).</summary>
    public static bool IsFinite(int bits) => (bits & 0x7F800000) != 0x7F800000;

    /// <summary>
    /// The decimal with the seven significant digits of the single with these bits; exactly what <c>(decimal)BitConverter.Int32BitsToSingle(bits)</c> gives.
    /// </summary>
    /// <exception cref="ArgumentException">The bits are an infinity or a NaN.</exception>
    /// <exception cref="OverflowException">The number is too large for a decimal (2^96 or more).</exception>
    public static decimal ToDecimal(int bits)
    {
        if (!IsFinite(bits)) throw new ArgumentException("The single-precision bits are not a finite number.", nameof(bits));
        var biased = (bits >> 23) & 0xFF;
        // The reference biases by 126: the number is below 2^exponent.
        var exponent = biased - 126;
        // 10^28 is just above 2^93: a number below 2^-94 cannot reach 0.5 at the largest scale and rounds to zero (this covers zero and the subnormals).
        if (exponent < -94) return decimal.Zero;
        if (exponent > 96) throw new OverflowException("A single-precision number of 2^96 or more does not fit a decimal.");
        var negative = bits < 0;

        // The single as a double: exact. mantissa * 2^(biased - 150), normalised to 53 bits.
        var value = (Mantissa: ((ulong)(bits & 0x7FFFFF) | 0x800000UL) << 29, Exponent: biased - 150 - 29);

        // The largest power of ten the number could have, from the binary exponent times log10(2) (19728 / 65536); the shift floors, also for negative products.
        var power = 6 - ((exponent * 19728) >> 16);
        if (power >= 0)
        {
            if (power > DecimalScaleMax) power = DecimalScaleMax;
            value = Multiply(value, PowersOfTen[power]);
        }
        else if (power != -1 || !IsBelow(value, 10_000_000))
        {
            value = Divide(value, PowersOfTen[-power]);
        }
        else
        {
            power = 0;
        }

        if (IsBelow(value, 1_000_000) && power < DecimalScaleMax)
        {
            value = Multiply(value, PowersOfTen[1]);
            power++;
        }

        var mantissa = RoundToEven(value);
        if (mantissa == 0) return decimal.Zero;

        if (power < 0)
        {
            // Seven digits times a power of ten of at most 10^22: below 2^96 because of the overflow check above.
            var whole = (UInt128)mantissa * Pow10(-power);
            return new decimal((int)(uint)whole, (int)(uint)(whole >> 32), (int)(uint)(whole >> 64), negative, 0);
        }

        // Trailing zeros leave the scale, at most six of them and never below scale zero (the order 4, 2, 1 of the reference removes as many as possible).
        var removable = Math.Min(power, 6);
        if (removable >= 4 && mantissa % 10_000 == 0)
        {
            mantissa /= 10_000;
            power -= 4;
            removable -= 4;
        }
        if (removable >= 2 && mantissa % 100 == 0)
        {
            mantissa /= 100;
            power -= 2;
            removable -= 2;
        }
        if (removable >= 1 && mantissa % 10 == 0)
        {
            mantissa /= 10;
            power--;
        }
        return new decimal((int)mantissa, 0, 0, negative, (byte)power);
    }

    private static UInt128 Pow10(int power)
    {
        var result = UInt128.One;
        for (var index = 0; index < power; index++) result *= 10;
        return result;
    }

    private static (ulong Mantissa, int Exponent)[] BuildPowersOfTen()
    {
        var table = new (ulong, int)[DecimalScaleMax + 1];
        for (var power = 0; power <= DecimalScaleMax; power++) table[power] = Normalize(Pow10(power), 0, false);
        return table;
    }

    /// <summary>Rounds <paramref name="magnitude"/> * 2^<paramref name="exponent"/> to a 53-bit mantissa, ties to even (<paramref name="sticky"/>: bits were already lost below).</summary>
    private static (ulong Mantissa, int Exponent) Normalize(UInt128 magnitude, int exponent, bool sticky)
    {
        var length = 128 - (int)UInt128.LeadingZeroCount(magnitude);
        if (length <= 53) return ((ulong)(magnitude << (53 - length)), exponent - (53 - length));
        var shift = length - 53;
        var kept = (ulong)(magnitude >> shift);
        var dropped = magnitude & ((UInt128.One << shift) - 1);
        var half = UInt128.One << (shift - 1);
        if (dropped > half || (dropped == half && (sticky || (kept & 1) != 0))) kept++;
        if (kept == 1UL << 53)
        {
            kept >>= 1;
            shift++;
        }
        return (kept, exponent + shift);
    }

    private static (ulong Mantissa, int Exponent) Multiply((ulong Mantissa, int Exponent) a, (ulong Mantissa, int Exponent) b)
        => Normalize((UInt128)a.Mantissa * b.Mantissa, a.Exponent + b.Exponent, false);

    private static (ulong Mantissa, int Exponent) Divide((ulong Mantissa, int Exponent) a, (ulong Mantissa, int Exponent) b)
    {
        // 55 extra bits: the quotient has 55 or 56 bits, so at least two bits lie below the 53 kept ones and the remainder only breaks a tie.
        var numerator = (UInt128)a.Mantissa << 55;
        var quotient = numerator / b.Mantissa;
        var remainder = numerator - quotient * b.Mantissa;
        return Normalize(quotient, a.Exponent - b.Exponent - 55, remainder != UInt128.Zero);
    }

    /// <summary>Whether the positive number is below the whole number <paramref name="limit"/>.</summary>
    private static bool IsBelow((ulong Mantissa, int Exponent) value, ulong limit)
    {
        if (value.Exponent >= 0) return false;
        var shift = -value.Exponent;
        return shift >= 64 || (value.Mantissa >> shift) < limit;
    }

    /// <summary>The nearest whole number, ties to even (the reference: <c>Math.Round</c>, or truncation and a comparison of the rest with one half).</summary>
    private static uint RoundToEven((ulong Mantissa, int Exponent) value)
    {
        var shift = -value.Exponent;
        // The scaled number is below 10^7 < 2^24 and its mantissa has 53 bits: the shift is at least 29; from 54 on the number is below one half.
        if (shift is < 29 or > 63) return shift > 63 ? 0u : throw new InvalidOperationException("The scaled number left the range the conversion works in.");
        var whole = value.Mantissa >> shift;
        var rest = value.Mantissa & ((1UL << shift) - 1);
        var half = 1UL << (shift - 1);
        if (rest > half || (rest == half && (whole & 1) != 0)) whole++;
        return checked((uint)whole);
    }
}
