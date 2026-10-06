namespace CnCNew.Simulation.Numerics;

/// <summary>
/// The square of a reach (a range, a radius, a limit) for comparisons with squared distances: <c>distanceSquared &lt;= reach * reach</c> without the
/// product ever leaving the fixed-point range.
/// <para>
/// Why: the data use ranges as large as 1,000,000 and 10,000,000 for "everywhere" (<c>AttackRange</c> of the Scud Storm, <c>StartAbilityRange</c> of the
/// bomb truck). Since <see cref="Fixed"/> has 24 fraction bits, the square of anything above 741,455 no longer fits and <c>reach * reach</c> throws
/// (docs/FIXED-POINT-Q39-24.md §5). A comparison does not need the square itself, only its order against a representable value.
/// </para>
/// <para>
/// The answer is the one the exact comparison gives: the square is the product <see cref="Fixed"/> itself forms (rounded to nearest, ties away from
/// zero), taken in 128 bits. Where <c>reach * reach</c> fits, <see cref="Contains"/> and <see cref="ContainsStrictly"/> are bit for bit
/// <c>d &lt;= reach * reach</c> and <c>d &lt; reach * reach</c>; where it does not, the square is larger than every representable value and both are true.
/// Nothing is clamped: the reach stays what the data say, and the saturated square never leaves this type.
/// </para>
/// </summary>
public readonly struct SquaredReach
{
    // The rounded square, or long.MaxValue when it lies beyond the range; _beyond tells the two apart for the strict comparison only.
    private readonly long _raw;
    private readonly bool _beyond;

    private SquaredReach(long raw, bool beyond)
    {
        _raw = raw;
        _beyond = beyond;
    }

    /// <summary>The square of <paramref name="reach"/> (of its magnitude, as <c>reach * reach</c>).</summary>
    public static SquaredReach Of(Fixed reach)
    {
        var raw = FixedMath.SquareSaturated(reach.Raw, Fixed.FractionBits, out var beyond);
        return new(raw, beyond);
    }

    /// <summary>A value that already is a square (the best squared distance of a nearest-first search, a constant such as 15 · 15).</summary>
    public static SquaredReach OfSquare(Fixed squared) => new(squared.Raw, false);

    /// <summary>True when the square of the reach lies beyond the fixed-point range: every representable distance is within it.</summary>
    public bool IsBeyondRange => _beyond;

    /// <summary><c>distanceSquared &lt;= reach²</c>.</summary>
    public bool Contains(Fixed distanceSquared) => distanceSquared.Raw <= _raw;

    /// <summary><c>distanceSquared &lt; reach²</c>.</summary>
    public bool ContainsStrictly(Fixed distanceSquared) => distanceSquared.Raw < _raw || _beyond;

    /// <summary><c>distanceSquared + margin &lt; reach²</c> with the sum and the square exact (128 bits): neither can overflow.</summary>
    public static bool ContainsStrictly(Fixed reach, Fixed distanceSquared, Fixed margin)
        => (Int128)distanceSquared.Raw + margin.Raw < (Int128)FixedMath.SquareWide(reach.Raw, Fixed.FractionBits);

    /// <summary>The sign of <c>a² - b²</c> (the rounded squares, exact): which of two lengths is the longer one where the code compares their squares.</summary>
    public static int Compare(Fixed a, Fixed b)
        => FixedMath.SquareWide(a.Raw, Fixed.FractionBits).CompareTo(FixedMath.SquareWide(b.Raw, Fixed.FractionBits));
}
