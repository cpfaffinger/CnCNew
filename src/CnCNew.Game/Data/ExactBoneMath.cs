using BigInteger = System.Numerics.BigInteger;

namespace CnCNew.Game.Data;

/// <summary>
/// Exact arithmetic for the bone table (<see cref="ModelGeometryLoader"/>). A single-precision number is a whole number times a power of two; sums and products
/// of such numbers are again of that kind. A bone's rest pose is nothing but sums and products of the numbers in the model file, so it can be computed without
/// any rounding and therefore without any dependence on the processor, on fused multiply-add or on the vector instructions the runtime picks for
/// <c>Matrix4x4</c>. The only step that is not exact, the arc tangent of the heading, is computed in whole numbers to 2^-<see cref="AngleBits"/> and then rounded
/// to the table's resolution; a result too close to a rounding boundary to be decided is an error, never a guess.
/// </summary>
internal readonly record struct Dyadic(BigInteger Mantissa, int Exponent)
{
    public static Dyadic Zero => new(BigInteger.Zero, 0);
    public static Dyadic One => new(BigInteger.One, 0);

    public bool IsZero => Mantissa.IsZero;
    public int Sign => Mantissa.Sign;

    /// <summary>The exact value of the single with these bits (<c>BitConverter.SingleToInt32Bits</c>): the way a file's number enters the exact domain.</summary>
    public static Dyadic FromSingleBits(int bits)
    {
        var biased = (bits >> 23) & 0xFF;
        if (biased == 0xFF) throw new InvalidDataException("A bone transform contains a number that is not finite.");
        var fraction = bits & 0x7FFFFF;
        // Subnormals have no implicit leading one and the exponent of the smallest normal number.
        var (mantissa, exponent) = biased == 0 ? (fraction, -149) : (fraction | 0x800000, biased - 150);
        return new Dyadic(bits < 0 ? -mantissa : mantissa, exponent);
    }

    public static Dyadic operator +(Dyadic a, Dyadic b)
    {
        if (a.Exponent < b.Exponent) (a, b) = (b, a);
        return new Dyadic((a.Mantissa << (a.Exponent - b.Exponent)) + b.Mantissa, b.Exponent);
    }

    public static Dyadic operator -(Dyadic a) => new(-a.Mantissa, a.Exponent);
    public static Dyadic operator -(Dyadic a, Dyadic b) => a + -b;
    public static Dyadic operator *(Dyadic a, Dyadic b) => new(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);

    public Dyadic Twice() => new(Mantissa, Exponent + 1);

    /// <summary>The nearest whole multiple of 2^-<paramref name="fractionBits"/>, ties to the even multiple; returned as that multiple's count.</summary>
    public long RoundToStep(int fractionBits)
    {
        var shift = Exponent + fractionBits;
        if (shift >= 0) return checked((long)(Mantissa << shift));
        return checked((long)ExactBoneMath.RoundShiftToEven(Mantissa, -shift));
    }
}

/// <summary>A rotation (rows as in <c>System.Numerics</c>: a row vector is multiplied from the left) and a translation, exact.</summary>
internal readonly record struct ExactTransform(
    Dyadic M11, Dyadic M12, Dyadic M13, Dyadic M21, Dyadic M22, Dyadic M23, Dyadic M31, Dyadic M32, Dyadic M33, Dyadic X, Dyadic Y, Dyadic Z)
{
    public static ExactTransform Identity => new(Dyadic.One, Dyadic.Zero, Dyadic.Zero, Dyadic.Zero, Dyadic.One, Dyadic.Zero, Dyadic.Zero, Dyadic.Zero, Dyadic.One, Dyadic.Zero, Dyadic.Zero, Dyadic.Zero);

    /// <summary>
    /// <c>Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t)</c> with the same formulas, without rounding (the quaternion is used as the file
    /// states it, not normalised - exactly what the single-precision path does).
    /// </summary>
    public static ExactTransform FromPivot(Dyadic qx, Dyadic qy, Dyadic qz, Dyadic qw, Dyadic tx, Dyadic ty, Dyadic tz)
    {
        var (xx, yy, zz) = (qx * qx, qy * qy, qz * qz);
        var (xy, wz, xz, wy, yz, wx) = (qx * qy, qz * qw, qz * qx, qy * qw, qy * qz, qx * qw);
        return new ExactTransform(
            Dyadic.One - (yy + zz).Twice(), (xy + wz).Twice(), (xz - wy).Twice(),
            (xy - wz).Twice(), Dyadic.One - (zz + xx).Twice(), (yz + wx).Twice(),
            (xz + wy).Twice(), (yz - wx).Twice(), Dyadic.One - (yy + xx).Twice(),
            tx, ty, tz);
    }

    /// <summary><c>local * parent</c>: the local transform first, then the parent's.</summary>
    public ExactTransform Then(ExactTransform parent) => new(
        M11 * parent.M11 + M12 * parent.M21 + M13 * parent.M31, M11 * parent.M12 + M12 * parent.M22 + M13 * parent.M32, M11 * parent.M13 + M12 * parent.M23 + M13 * parent.M33,
        M21 * parent.M11 + M22 * parent.M21 + M23 * parent.M31, M21 * parent.M12 + M22 * parent.M22 + M23 * parent.M32, M21 * parent.M13 + M22 * parent.M23 + M23 * parent.M33,
        M31 * parent.M11 + M32 * parent.M21 + M33 * parent.M31, M31 * parent.M12 + M32 * parent.M22 + M33 * parent.M32, M31 * parent.M13 + M32 * parent.M23 + M33 * parent.M33,
        X * parent.M11 + Y * parent.M21 + Z * parent.M31 + parent.X, X * parent.M12 + Y * parent.M22 + Z * parent.M32 + parent.Y, X * parent.M13 + Y * parent.M23 + Z * parent.M33 + parent.Z);
}

internal static class ExactBoneMath
{
    /// <summary>Fraction bits of the whole-number arc tangent. Every step below loses at most a few units of the last bit; 2^-<see cref="SafetyBits"/> bounds the sum generously.</summary>
    private const int AngleBits = 192;
    private const int SafetyBits = 160;

    private static readonly BigInteger One = BigInteger.One << AngleBits;

    /// <summary>π from Machin's formula, π = 16·atan(1/5) − 4·atan(1/239), each arc tangent by its alternating series.</summary>
    private static readonly BigInteger Pi = 16 * AtanOfReciprocal(5) - 4 * AtanOfReciprocal(239);

    /// <summary>tan(π/8) = √2 − 1 lies between 0.41 and 0.42; any fixed split point in that region keeps the series argument below 0.42.</summary>
    private static readonly BigInteger Split = One * 53 / 128;

    /// <summary><paramref name="value"/> / 2^<paramref name="shift"/> rounded to the nearest whole number, ties to even.</summary>
    public static BigInteger RoundShiftToEven(BigInteger value, int shift)
    {
        var negative = value.Sign < 0;
        var magnitude = BigInteger.Abs(value);
        var whole = magnitude >> shift;
        var rest = magnitude - (whole << shift);
        var half = BigInteger.One << (shift - 1);
        if (rest > half || (rest == half && !whole.IsEven)) whole += BigInteger.One;
        return negative ? -whole : whole;
    }

    private static BigInteger AtanOfReciprocal(int n)
    {
        // atan(1/n) = 1/n − 1/(3 n^3) + 1/(5 n^5) − …
        var power = One / n;
        var sum = power;
        var square = (BigInteger)n * n;
        for (var k = 1; !power.IsZero; k++)
        {
            power /= square;
            var term = power / (2 * k + 1);
            sum += (k & 1) == 1 ? -term : term;
        }
        return sum;
    }

    /// <summary>atan of a number in [0, 1], both scaled by 2^<see cref="AngleBits"/>.</summary>
    private static BigInteger AtanUnit(BigInteger t)
    {
        // Above the split: atan(t) = π/4 − atan((1 − t) / (1 + t)); the new argument is at most 0.42 as well.
        if (t > Split) return Pi / 4 - AtanSeries((One - t) * One / (One + t));
        return AtanSeries(t);
    }

    /// <summary>The series t − t^3/3 + t^5/5 − … for 0 ≤ t ≤ 0.42 (each term at most 0.18 times the one before).</summary>
    private static BigInteger AtanSeries(BigInteger t)
    {
        var square = t * t >> AngleBits;
        var power = t;
        var sum = t;
        for (var k = 1; !power.IsZero; k++)
        {
            power = power * square >> AngleBits;
            var term = power / (2 * k + 1);
            sum += (k & 1) == 1 ? -term : term;
        }
        return sum;
    }

    /// <summary>
    /// The angle of the vector (<paramref name="x"/>, <paramref name="y"/>) in (−π, π], rounded to the nearest multiple of 2^-<paramref name="fractionBits"/>
    /// radians (ties cannot occur except for the angle zero, which is exact); returned as that multiple's count. The zero vector has the angle zero.
    /// </summary>
    public static long Atan2ToStep(Dyadic y, Dyadic x, int fractionBits)
    {
        if (y.IsZero) return x.Sign >= 0 ? 0 : RoundAngle(Pi, fractionBits);
        // |y| / |x| or its reciprocal as a quotient of whole numbers: bring both to one exponent.
        var exponent = Math.Min(x.Exponent, y.Exponent);
        var ax = BigInteger.Abs(x.Mantissa) << (x.Exponent - exponent);
        var ay = BigInteger.Abs(y.Mantissa) << (y.Exponent - exponent);
        var angle = ay <= ax ? AtanUnit(ay * One / ax) : Pi / 2 - AtanUnit(ax * One / ay);
        if (x.Sign < 0) angle = Pi - angle;
        if (y.Sign < 0) angle = -angle;
        return RoundAngle(angle, fractionBits);
    }

    private static long RoundAngle(BigInteger angle, int fractionBits)
    {
        var shift = AngleBits - fractionBits;
        var rounded = RoundShiftToEven(angle, shift);
        // The computed angle is only known to 2^-SafetyBits. If that could move it across the rounding boundary, the table entry is undecided: refuse.
        var distance = BigInteger.Abs(BigInteger.Abs(angle - (rounded << shift)) - (BigInteger.One << (shift - 1)));
        if (distance < BigInteger.One << (AngleBits - SafetyBits))
            throw new InvalidDataException("A bone heading lies on a rounding boundary of the bone table and cannot be rounded reliably.");
        return checked((long)rounded);
    }
}
