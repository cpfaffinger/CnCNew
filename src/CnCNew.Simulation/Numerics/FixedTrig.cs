namespace CnCNew.Simulation.Numerics;

/// <summary>
/// Angles on <see cref="Fixed"/> radians without floating point. The reference uses <c>Sin</c>/<c>Cos</c>/<c>atan2</c> of binary32 values, which are not
/// reproducible across platforms; this replacement is deliberate and stays deterministic. Constants, tables and routines are those of
/// <see cref="FixedMath"/> at <see cref="Fixed.FractionBits"/>: they follow the resolution, nothing here assumes a number of fraction bits.
/// </summary>
public static class FixedTrig
{
    public static readonly Fixed Pi = Fixed.FromRaw(FixedMath.Pi(Fixed.FractionBits));
    public static readonly Fixed TwoPi = Fixed.FromRaw(FixedMath.TwoPi(Fixed.FractionBits));
    public static readonly Fixed HalfPi = Fixed.FromRaw(FixedMath.HalfPi(Fixed.FractionBits));

    private static readonly long[] AtanTable = FixedMath.AtanTable(Fixed.FractionBits);

    /// <summary>π * numerator / denominator, rounded to nearest: an angle constant that is a fraction of the half turn.</summary>
    public static Fixed PiFraction(long numerator, long denominator) => Fixed.FromRaw(FixedMath.PiFraction(numerator, denominator, Fixed.FractionBits));

    /// <summary>The angle of (x, y) in (-π, π], 0 for the zero vector (integer CORDIC, <see cref="FixedMath.Atan2"/>).</summary>
    public static Fixed Atan2(Fixed y, Fixed x) => Fixed.FromRaw(FixedMath.Atan2(y.Raw, x.Raw, Fixed.FractionBits, AtanTable));

    /// <summary>The signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, in (-π, π].</summary>
    public static Fixed AngleDifference(Fixed from, Fixed to) => Fixed.FromRaw(FixedMath.AngleDifference(from.Raw, to.Raw, Fixed.FractionBits));

    /// <summary>An angle wrapped into (-π, π].</summary>
    public static Fixed Normalize(Fixed radians) => AngleDifference(Fixed.Zero, radians);

    public static Fixed Sin(Fixed radians) => Fixed.FromRaw(FixedMath.Sin(radians.Raw, Fixed.FractionBits));

    public static Fixed Cos(Fixed radians) => Fixed.FromRaw(FixedMath.Cos(radians.Raw, Fixed.FractionBits));
}
