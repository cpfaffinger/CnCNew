namespace CnCNew.Simulation.Numerics;

/// <summary>
/// Sine and cosine on <see cref="Fixed"/> radians without floating point: range reduction to [-π/2, π/2] followed by a nested
/// Taylor polynomial (absolute error below 2e-5, i.e. about one raw unit). The reference uses <c>Sin</c>/<c>Cos</c> of binary32
/// values, which are not reproducible across platforms; this replacement is deliberate and stays deterministic.
/// </summary>
public static class FixedTrig
{
    public static readonly Fixed Pi = Fixed.FromRaw(205887);
    public static readonly Fixed TwoPi = Fixed.FromRaw(411775);
    public static readonly Fixed HalfPi = Fixed.FromRaw(102944);

    public static Fixed Sin(Fixed radians)
    {
        var raw = radians.Raw % TwoPi.Raw;
        if (raw > Pi.Raw) raw -= TwoPi.Raw;
        else if (raw < -Pi.Raw) raw += TwoPi.Raw;
        if (raw > HalfPi.Raw) raw = Pi.Raw - raw;
        else if (raw < -HalfPi.Raw) raw = -Pi.Raw - raw;
        var x = Fixed.FromRaw(raw);
        var x2 = x * x;
        // x(1 - x²/6(1 - x²/20(1 - x²/42(1 - x²/72(1 - x²/110(1 - x²/156))))))
        var inner = Fixed.One - x2 / Fixed.FromInt(156);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(110);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(72);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(42);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(20);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(6);
        return x * inner;
    }

    public static Fixed Cos(Fixed radians)
    {
        var raw = radians.Raw % TwoPi.Raw;
        if (raw > Pi.Raw) raw -= TwoPi.Raw;
        else if (raw < -Pi.Raw) raw += TwoPi.Raw;
        var negate = false;
        if (raw > HalfPi.Raw) { raw = Pi.Raw - raw; negate = true; }
        else if (raw < -HalfPi.Raw) { raw = -Pi.Raw - raw; negate = true; }
        var x = Fixed.FromRaw(raw);
        var x2 = x * x;
        // 1 - x^2/2(1 - x^2/12(1 - x^2/30(1 - x^2/56(1 - x^2/90(1 - x^2/132)))))
        var inner = Fixed.One - x2 / Fixed.FromInt(132);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(90);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(56);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(30);
        inner = Fixed.One - x2 * inner / Fixed.FromInt(12);
        var result = Fixed.One - x2 * inner / Fixed.FromInt(2);
        return negate ? -result : result;
    }
}
