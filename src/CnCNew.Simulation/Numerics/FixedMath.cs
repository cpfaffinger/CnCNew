namespace CnCNew.Simulation.Numerics;

/// <summary>
/// The arithmetic of <see cref="Fixed"/> on raw values, with the number of fraction bits as an argument. <see cref="Fixed"/> and <see cref="FixedTrig"/>
/// call it with <see cref="Fixed.FractionBits"/>; the tests call it with other resolutions too, so that a change of the resolution is proven before it is
/// made (phase 22: 16 → 24 bits, <c>docs/FIXED-POINT-Q39-24.md</c>).
/// <para>
/// Every routine is exact integer arithmetic: products and quotients are those of the 128-bit formulas in the comments, rounded to nearest with ties away
/// from zero, the square root is rounded down. The routes through machine words are shortcuts to the same value, not approximations; overflow throws.
/// </para>
/// </summary>
public static class FixedMath
{
    /// <summary>The trigonometric constants carry 61 fraction bits; a resolution uses as many CORDIC steps as it has fraction bits.</summary>
    public const int MaxFractionBits = 32;

    private const int ConstantBits = 61;

    /// <summary>π with 61 fraction bits, rounded to nearest (3.14159265358979323846…).</summary>
    private const long PiQ61 = 7244019458077122842;

    /// <summary>atan(2^-i), i = 0..31, with 61 fraction bits, rounded to nearest.</summary>
    private static readonly long[] AtanQ61 =
    [
        1811004864519280711, 1069098597953152948, 564882337777596249, 286743094836456889, 143927976672616092, 72034151524184357, 36025865417378411, 18014032019027246,
        9007153442175927, 4503593900760542, 2251799097857775, 1125899817364151, 562949942236502, 281474975312555, 140737488180565, 70368744155819,
        35184372086101, 17592186044075, 8796093022165, 4398046511099, 2199023255551, 1099511627776, 549755813888, 274877906944,
        137438953472, 68719476736, 34359738368, 17179869184, 8589934592, 4294967296, 2147483648, 1073741824,
    ];

    private static ulong Magnitude(long raw) => raw < 0 ? unchecked((ulong)(-raw)) : (ulong)raw;

    private static long Signed(ulong magnitude, bool negative)
    {
        if (magnitude <= long.MaxValue) return negative ? -(long)magnitude : (long)magnitude;
        if (negative && magnitude == 1UL << 63) return long.MinValue;
        throw new OverflowException("Fixed-point result out of range.");
    }

    /// <summary>(|a| * |b| + scale / 2) / scale with the sign of the product.</summary>
    public static long Multiply(long a, long b, int fractionBits)
    {
        var high = Math.BigMul(Magnitude(a), Magnitude(b), out var low);
        var rounded = unchecked(low + (1UL << (fractionBits - 1)));
        if (rounded < low) high++;
        if (high >> fractionBits != 0) throw new OverflowException("Fixed-point result out of range.");
        return Signed((high << (64 - fractionBits)) | (rounded >> fractionBits), (a ^ b) < 0);
    }

    /// <summary>(|a| * scale + |b| / 2) / |b| with the sign of the quotient.</summary>
    public static long Divide(long a, long b, int fractionBits)
    {
        if (b == 0) throw new DivideByZeroException();
        var dividend = Magnitude(a);
        var divisor = Magnitude(b);
        var negative = (a ^ b) < 0;
        if (dividend >> (63 - fractionBits) == 0)
        {
            // |a| * scale is below 2^63 and half the divisor at most 2^62: the rounded quotient is one division of machine words.
            return Signed(((dividend << fractionBits) + (divisor >> 1)) / divisor, negative);
        }
        var available = System.Numerics.BitOperations.LeadingZeroCount(divisor);
        if (available == 0)
        {
            // A divisor of 2^63 (the magnitude of long.MinValue) leaves no room to shift a remainder: the 128-bit formula itself.
            var wide = ((UInt128)dividend << fractionBits) + (divisor >> 1);
            var result = wide / divisor;
            if (result > ulong.MaxValue) throw new OverflowException("Fixed-point result out of range.");
            return Signed((ulong)result, negative);
        }
        // Long division of |a| * scale by the divisor, the fraction bits brought down in the largest pieces that keep remainder * 2^piece inside a machine word
        // (the remainder is below the divisor, so the divisor's leading zeros are that room). Every step is exact; the quotient is the floor.
        var quotient = dividend / divisor;
        var remainder = dividend - quotient * divisor;
        var remaining = fractionBits;
        while (remaining > 0)
        {
            var piece = Math.Min(remaining, available);
            if (quotient >> (64 - piece) != 0) throw new OverflowException("Fixed-point result out of range.");
            var shifted = remainder << piece;
            var digit = shifted / divisor;
            remainder = shifted - digit * divisor;
            quotient = (quotient << piece) | digit;
            remaining -= piece;
        }
        // floor((n + floor(d / 2)) / d) is the floor of n / d plus one exactly when the remainder reaches ceil(d / 2).
        if (remainder >= divisor - (divisor >> 1))
        {
            if (quotient == ulong.MaxValue) throw new OverflowException("Fixed-point result out of range.");
            quotient++;
        }
        return Signed(quotient, negative);
    }

    /// <summary>floor(sqrt(raw * scale)); throws for a negative operand.</summary>
    public static long Sqrt(long raw, int fractionBits)
    {
        if (raw < 0) throw new ArgumentOutOfRangeException(nameof(raw), "Cannot take the square root of a negative fixed-point value.");
        if (raw == 0) return 0;
        if (raw < 1L << (63 - fractionBits)) return (long)SquareRoot((ulong)raw << fractionBits);
        // raw * scale does not fit a machine word. Take as many of the fraction bits as do fit (an even number less than all of them, so that the rest is a whole
        // number of root bits): a = floor(sqrt(raw * 2^taken)). With rest = (fractionBits - taken) / 2 the root wanted lies in [a * 2^rest, (a + 1) * 2^rest),
        // because sqrt(raw * scale) = sqrt(raw * 2^taken) * 2^rest. Its upper bits are therefore a, and the lower ones follow bit by bit from the comparison of
        // the candidate's square with raw * scale (128 bits, exact).
        var taken = Math.Min(fractionBits, System.Numerics.BitOperations.LeadingZeroCount((ulong)raw));
        if (((fractionBits - taken) & 1) != 0) taken--;
        var rest = (fractionBits - taken) >> 1;
        var root = SquareRoot((ulong)raw << taken) << rest;
        var targetHigh = (ulong)raw >> (64 - fractionBits);
        var targetLow = (ulong)raw << fractionBits;
        for (var bit = rest - 1; bit >= 0; bit--)
        {
            var candidate = root | (1UL << bit);
            var squareHigh = Math.BigMul(candidate, candidate, out var squareLow);
            if (squareHigh < targetHigh || (squareHigh == targetHigh && squareLow <= targetLow)) root = candidate;
        }
        return (long)root;
    }

    /// <summary>
    /// floor(sqrt(value)) of a non-zero machine word by Newton's iteration over the integers: it falls from every start at or above the root to the floor of
    /// the root and stops there, so the start (a power of two just above the root) decides the number of steps, not the result.
    /// </summary>
    private static ulong SquareRoot(ulong value)
    {
        var root = 1UL << ((65 - System.Numerics.BitOperations.LeadingZeroCount(value)) >> 1);
        while (true)
        {
            var next = (root + value / root) >> 1;
            if (next >= root) return root;
            root = next;
        }
    }

    // ------------------------------------------------------------------------------------- squares and products for comparisons

    /// <summary>
    /// The square <see cref="Multiply"/> gives for (raw, raw) where that fits a machine word; where it does not, <see cref="long.MaxValue"/> and
    /// <paramref name="beyond"/>. It never throws: a square that only feeds a comparison with a representable value needs no more than "larger than all of them".
    /// </summary>
    public static long SquareSaturated(long raw, int fractionBits, out bool beyond)
    {
        var magnitude = Magnitude(raw);
        var high = Math.BigMul(magnitude, magnitude, out var low);
        var rounded = unchecked(low + (1UL << (fractionBits - 1)));
        if (rounded < low) high++;
        // (high * 2^64 + rounded) / scale reaches 2^63 exactly when high reaches 2^(fractionBits - 1).
        beyond = high >> (fractionBits - 1) != 0;
        return beyond ? long.MaxValue : (long)((high << (64 - fractionBits)) | (rounded >> fractionBits));
    }

    /// <summary>(raw² + scale / 2) / scale as the whole 128-bit number: the square of any raw value, rounded as <see cref="Multiply"/> rounds.</summary>
    public static UInt128 SquareWide(long raw, int fractionBits)
    {
        var magnitude = Magnitude(raw);
        var high = Math.BigMul(magnitude, magnitude, out var low);
        var rounded = unchecked(low + (1UL << (fractionBits - 1)));
        if (rounded < low) high++;
        return new UInt128(high, rounded) >> fractionBits;
    }

    /// <summary>
    /// <see cref="Divide"/>(<see cref="Multiply"/>(a, b), c) – both roundings as they are – for every a and b, also where the product alone leaves the range
    /// (a ratio of two large values, <c>health * newMaximum / oldMaximum</c>). Throws only when the quotient does.
    /// </summary>
    public static long MultiplyDivide(long a, long b, long c, int fractionBits)
    {
        var high = Math.BigMul(Magnitude(a), Magnitude(b), out var low);
        var rounded = unchecked(low + (1UL << (fractionBits - 1)));
        if (rounded < low) high++;
        var negativeProduct = (a ^ b) < 0;
        if (high >> (fractionBits - 1) == 0)
        {
            var product = (long)((high << (64 - fractionBits)) | (rounded >> fractionBits));
            return Divide(negativeProduct ? -product : product, c, fractionBits);
        }
        if (c == 0) throw new DivideByZeroException();
        // The product is below 2^(126 - fractionBits), so product * scale is below 2^126: the quotient formula of Divide in 128 bits.
        var wide = new UInt128(high, rounded) >> fractionBits;
        var divisor = (UInt128)Magnitude(c);
        var quotient = ((wide << fractionBits) + (divisor >> 1)) / divisor;
        if (quotient > ulong.MaxValue) throw new OverflowException("Fixed-point result out of range.");
        return Signed((ulong)quotient, negativeProduct != (c < 0));
    }

    // ------------------------------------------------------------------------------------------------------------- angles

    private static long Rounded(long valueQ61, int shift) => (valueQ61 + (1L << (shift - 1))) >> shift;

    /// <summary>π at the resolution, rounded to nearest.</summary>
    public static long Pi(int fractionBits) => Rounded(PiQ61, ConstantBits - fractionBits);

    /// <summary>2π at the resolution, rounded to nearest (not always twice <see cref="Pi"/>).</summary>
    public static long TwoPi(int fractionBits) => Rounded(PiQ61, ConstantBits - 1 - fractionBits);

    /// <summary>π/2 at the resolution, rounded to nearest.</summary>
    public static long HalfPi(int fractionBits) => Rounded(PiQ61, ConstantBits + 1 - fractionBits);

    /// <summary>π * numerator / denominator at the resolution, rounded to nearest.</summary>
    public static long PiFraction(long numerator, long denominator, int fractionBits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(numerator, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(denominator, 0);
        var divisor = (Int128)denominator << (ConstantBits - fractionBits);
        return checked((long)(((Int128)PiQ61 * numerator + divisor / 2) / divisor));
    }

    /// <summary>atan(2^-i) for i = 0 .. fractionBits - 1 at the resolution, rounded to nearest: the table of <see cref="Atan2"/>.</summary>
    public static long[] AtanTable(int fractionBits)
    {
        if (fractionBits is < 1 or > MaxFractionBits) throw new ArgumentOutOfRangeException(nameof(fractionBits));
        var table = new long[fractionBits];
        for (var index = 0; index < table.Length; index++) table[index] = Rounded(AtanQ61[index], ConstantBits - fractionBits);
        return table;
    }

    /// <summary>
    /// Integer CORDIC in vectoring mode: the angle of (x, y) in (-π, π], 0 for the zero vector. One step per entry of <paramref name="table"/>
    /// (<see cref="AtanTable"/> of the resolution), so the error shrinks with the resolution: a few raw units at every one.
    /// </summary>
    public static long Atan2(long y, long x, int fractionBits, ReadOnlySpan<long> table)
    {
        var vx = x;
        var vy = y;
        if (vx == 0 && vy == 0) return 0;
        // The vector is scaled up so that the shifted terms do not vanish for small vectors: by the fraction bits, or by less when the vector is so long that
        // it would leave the word (the vector grows by the CORDIC gain of 1.65 and by the square root of two when both components are equal; three bits
        // cover both). A vector that long loses nothing by the smaller scale. Below 2^(61 - fractionBits) raw the scale is always the full one.
        var headroom = System.Numerics.BitOperations.LeadingZeroCount(Math.Max(Magnitude(vx), Magnitude(vy))) - 3;
        if (headroom < 0)
        {
            vx >>= -headroom;
            vy >>= -headroom;
            headroom = 0;
        }
        long offset = 0;
        if (vx < 0)
        {
            offset = vy >= 0 ? Pi(fractionBits) : -Pi(fractionBits);
            vx = -vx;
            vy = -vy;
        }
        var scale = Math.Min(fractionBits, headroom);
        vx <<= scale;
        vy <<= scale;
        long angle = 0;
        for (var i = 0; i < table.Length; i++)
        {
            var nx = vy > 0 ? vx + (vy >> i) : vx - (vy >> i);
            var ny = vy > 0 ? vy - (vx >> i) : vy + (vx >> i);
            angle += vy > 0 ? table[i] : -table[i];
            vx = nx;
            vy = ny;
        }
        return angle + offset;
    }

    /// <summary>The signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, in (-π, π].</summary>
    public static long AngleDifference(long from, long to, int fractionBits)
    {
        var pi = Pi(fractionBits);
        var twoPi = TwoPi(fractionBits);
        var raw = (to - from) % twoPi;
        if (raw > pi) raw -= twoPi;
        else if (raw <= -pi) raw += twoPi;
        return raw;
    }

    /// <summary>value / divisor rounded to nearest, ties away from zero; the divisor is a small positive even number (a term of a series).</summary>
    private static long RoundedQuotient(long value, long divisor) => value >= 0 ? (value + (divisor >> 1)) / divisor : -((-value + (divisor >> 1)) / divisor);

    /// <summary>
    /// The sine: range reduction to [-π/2, π/2], then the nested Taylor polynomial up to x^13 (truncation error below 7e-10, under the resolution of every
    /// supported number of fraction bits; what remains is the rounding of the seven steps, a few raw units).
    /// </summary>
    public static long Sin(long radians, int fractionBits)
    {
        var pi = Pi(fractionBits);
        var twoPi = TwoPi(fractionBits);
        var halfPi = HalfPi(fractionBits);
        var x = radians % twoPi;
        if (x > pi) x -= twoPi;
        else if (x < -pi) x += twoPi;
        if (x > halfPi) x = pi - x;
        else if (x < -halfPi) x = -pi - x;
        var one = 1L << fractionBits;
        var x2 = Multiply(x, x, fractionBits);
        // x(1 - x²/6(1 - x²/20(1 - x²/42(1 - x²/72(1 - x²/110(1 - x²/156)))))); a quotient by a whole even number is the fixed-point quotient, bit for bit.
        var inner = one - RoundedQuotient(x2, 156);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 110);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 72);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 42);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 20);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 6);
        return Multiply(x, inner, fractionBits);
    }

    /// <summary>The cosine: as <see cref="Sin"/>, the polynomial up to x^12 (truncation error below 7e-9).</summary>
    public static long Cos(long radians, int fractionBits)
    {
        var pi = Pi(fractionBits);
        var twoPi = TwoPi(fractionBits);
        var halfPi = HalfPi(fractionBits);
        var x = radians % twoPi;
        if (x > pi) x -= twoPi;
        else if (x < -pi) x += twoPi;
        var negate = false;
        if (x > halfPi) { x = pi - x; negate = true; }
        else if (x < -halfPi) { x = -pi - x; negate = true; }
        var one = 1L << fractionBits;
        var x2 = Multiply(x, x, fractionBits);
        // 1 - x²/2(1 - x²/12(1 - x²/30(1 - x²/56(1 - x²/90(1 - x²/132)))))
        var inner = one - RoundedQuotient(x2, 132);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 90);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 56);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 30);
        inner = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 12);
        var result = one - RoundedQuotient(Multiply(x2, inner, fractionBits), 2);
        return negate ? -result : result;
    }
}
