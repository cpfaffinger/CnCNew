using System.Numerics;
using CnCNew.Simulation.Numerics;

/// <summary>
/// <see cref="SquaredReach"/> and <see cref="Fixed.MultiplyDivide"/> against big-integer formulas (docs/FIXED-POINT-Q39-24.md §5): the comparison
/// "squared distance within the square of a reach" gives the answer of the exact comparison for every reach, also for the ranges of 1,000,000 and
/// 10,000,000 the data use for "everywhere", whose square does not fit a fixed-point number; and it gives the answer of <c>d &lt;= reach * reach</c>
/// wherever that expression can be evaluated at all.
/// </summary>
static partial class Tests
{
    /// <summary>The square <see cref="Fixed"/> forms: (raw² + scale / 2) / scale, as a whole number.</summary>
    private static BigInteger ExactSquare(long raw) => (((BigInteger)raw * raw) + (BigInteger.One << (Fixed.FractionBits - 1))) >> Fixed.FractionBits;

    private static void CheckSquaredReach(long reachRaw, long distanceRaw, ref long evaluable)
    {
        var reach = Fixed.FromRaw(reachRaw);
        var distance = Fixed.FromRaw(distanceRaw);
        var square = ExactSquare(reachRaw);
        var squared = SquaredReach.Of(reach);
        var within = distanceRaw <= square;
        var strictly = distanceRaw < square;
        if (squared.Contains(distance) != within || squared.ContainsStrictly(distance) != strictly)
            throw new InvalidOperationException($"SquaredReach of raw {reachRaw} against raw {distanceRaw}: Contains {squared.Contains(distance)} (exact {within}), ContainsStrictly {squared.ContainsStrictly(distance)} (exact {strictly}).");
        if (squared.IsBeyondRange != (square > long.MaxValue))
            throw new InvalidOperationException($"SquaredReach of raw {reachRaw}: IsBeyondRange {squared.IsBeyondRange}, the exact square is {square}.");
        // Where the plain product can be formed, the answers are those of the expressions the helper replaced.
        if (square > long.MaxValue) return;
        var product = reach * reach;
        evaluable++;
        if (product.Raw != (long)square || squared.Contains(distance) != (distance <= product) || squared.ContainsStrictly(distance) != (distance < product))
            throw new InvalidOperationException($"SquaredReach of raw {reachRaw} against raw {distanceRaw} differs from the comparison with reach * reach (raw {product.Raw}).");
    }

    public static void SquaredReachGivesTheExactComparison()
    {
        var dice = new ResolutionDice(0x5EED2201);
        long cases = 0, evaluable = 0;

        // The largest raw value whose rounded square still fits: found from the exact formula, then confirmed against the operator.
        var limit = (long)Math.Sqrt((double)long.MaxValue * Fixed.ScaleRaw);
        while (ExactSquare(limit) <= long.MaxValue) limit++;
        while (ExactSquare(limit) > long.MaxValue) limit--;
        Check.That(Fixed.FromRaw(limit).Floor() == 741455, $"The largest value that can be squared is just above 741,455 units (raw {limit}).");
        _ = Fixed.FromRaw(limit) * Fixed.FromRaw(limit);
        Check.Throws<OverflowException>(() => _ = Fixed.FromRaw(limit + 1) * Fixed.FromRaw(limit + 1), "out of range");
        Check.That(!SquaredReach.Of(Fixed.FromRaw(limit)).IsBeyondRange && SquaredReach.Of(Fixed.FromRaw(limit + 1)).IsBeyondRange, "SquaredReach changes to 'beyond the range' exactly there.");

        void Against(long reachRaw)
        {
            var square = ExactSquare(reachRaw);
            // The distances that decide: around the exact square where it is representable, the ends of the range, zero, negative values, random ones.
            if (square <= long.MaxValue)
            {
                var exact = (long)square;
                for (var offset = -2L; offset <= 2; offset++)
                {
                    if (offset > 0 && exact > long.MaxValue - offset) continue;
                    CheckSquaredReach(reachRaw, exact + offset, ref evaluable);
                    cases++;
                }
            }
            foreach (var distance in new[] { 0L, 1L, -1L, long.MaxValue, long.MaxValue - 1, long.MinValue, dice.Wide(), dice.Wide(), Math.Abs(dice.Wide() % long.MaxValue) })
            {
                CheckSquaredReach(reachRaw, distance, ref evaluable);
                cases++;
            }
        }

        // 1. A dense sweep of raw values around the old overflow limit, both signs.
        for (var offset = -60000L; offset <= 60000; offset++)
        {
            Against(limit + offset);
            if (offset % 16 == 0) Against(-(limit + offset));
        }
        // 2. The sentinels of the data and of the code, each with its neighbours, and whole units up to the largest sentinel in coarse steps.
        foreach (var units in new long[] { 0, 1, 15, 100, 300, 9999, 46000, 46341, 99999, 100000, 524288, 741455, 741456, 999999, 1000000, 1048576, 2200000, 9999999, 10000000, 99999999, 549755813887 })
            for (var offset = -3L; offset <= 3; offset++)
            {
                Against(Fixed.FromInt(units).Raw + offset);
                Against(-(Fixed.FromInt(units).Raw + offset));
            }
        for (var units = 0L; units <= 10000000; units += 997) Against(Fixed.FromInt(units).Raw + units % 7919);
        // 3. Values of every magnitude, and the ends of the range.
        foreach (var raw in new[] { long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1 }) Against(raw);
        for (var index = 0; index < 300000; index++) Against(dice.Wide());

        // 4. The comparison with a margin (the minimum attack range: "distSqr < minAttackRangeSqr - 0.5") and the comparison of two squares.
        long margins = 0;
        for (var index = 0; index < 400000; index++)
        {
            var reachRaw = index % 3 == 0 ? Fixed.FromInt(index % 2000000).Raw + index % 5 : dice.Wide();
            var square = ExactSquare(reachRaw);
            var marginRaw = index % 4 == 0 ? Fixed.FromDecimal(0.5m).Raw : dice.Wide();
            var distanceRaw = index % 2 == 0 && square <= long.MaxValue ? (long)square - Math.Min(marginRaw, (long)square) + index % 5 - 2 : dice.Wide();
            var expected = (BigInteger)distanceRaw + marginRaw < square;
            if (SquaredReach.ContainsStrictly(Fixed.FromRaw(reachRaw), Fixed.FromRaw(distanceRaw), Fixed.FromRaw(marginRaw)) != expected)
                throw new InvalidOperationException($"ContainsStrictly with a margin: reach raw {reachRaw}, distance raw {distanceRaw}, margin raw {marginRaw}, exact {expected}.");
            // The expression it replaced, where that can be evaluated: distance >= reach * reach - margin is the negation.
            if (square <= long.MaxValue && (BigInteger)square - marginRaw <= long.MaxValue && (BigInteger)square - marginRaw >= long.MinValue)
            {
                var old = Fixed.FromRaw(distanceRaw) >= Fixed.FromRaw(reachRaw) * Fixed.FromRaw(reachRaw) - Fixed.FromRaw(marginRaw);
                if (old == expected) throw new InvalidOperationException($"ContainsStrictly with a margin differs from 'distance >= reach * reach - margin': reach raw {reachRaw}, distance raw {distanceRaw}, margin raw {marginRaw}.");
                margins++;
            }

            var otherRaw = index % 2 == 0 ? reachRaw + index % 3 - 1 : dice.Wide();
            if (index % 2 == 0 && (reachRaw == long.MaxValue || reachRaw == long.MinValue)) otherRaw = reachRaw;
            var order = square.CompareTo(ExactSquare(otherRaw));
            if (Math.Sign(SquaredReach.Compare(Fixed.FromRaw(reachRaw), Fixed.FromRaw(otherRaw))) != order)
                throw new InvalidOperationException($"SquaredReach.Compare of raw {reachRaw} and raw {otherRaw}: exact {order}.");
        }

        // 5. A value that already is a square keeps its plain comparisons.
        for (var index = 0; index < 100000; index++)
        {
            var bound = Fixed.FromRaw(Math.Abs(dice.Wide() % long.MaxValue));
            var distance = Fixed.FromRaw(index % 2 == 0 ? bound.Raw + index % 3 - 1 : dice.Wide());
            var squared = SquaredReach.OfSquare(bound);
            if (squared.Contains(distance) != (distance <= bound) || squared.ContainsStrictly(distance) != (distance < bound) || squared.IsBeyondRange)
                throw new InvalidOperationException($"SquaredReach.OfSquare of raw {bound.Raw} against raw {distance.Raw}.");
        }
        Check.That(SquaredReach.OfSquare(Fixed.MaxValue).Contains(Fixed.MaxValue) && !SquaredReach.OfSquare(Fixed.MaxValue).ContainsStrictly(Fixed.MaxValue), "OfSquare(MaxValue) is a bound like any other.");
        Check.That(SquaredReach.Of(Fixed.FromInt(1000000)).ContainsStrictly(Fixed.MaxValue), "The square of 1,000,000 lies strictly beyond the largest fixed-point value.");

        Check.That(cases > 2000000 && evaluable > 500000 && margins > 100000, $"The sweep ran ({cases} comparisons, {evaluable} of them also against reach * reach, {margins} margin comparisons against the old expression).");
        Console.WriteLine($"       {cases} comparisons against the exact square, {evaluable} of them also against the plain product; largest raw value that can be squared: {limit} ({Fixed.FromRaw(limit)} units)");
    }

    public static void MultiplyDivideEqualsTheTwoOperatorsWithoutTheirOverflow()
    {
        var dice = new ResolutionDice(0x5EED2202);
        long cases = 0, withOperators = 0, beyond = 0;

        void Against(long a, long b, long c)
        {
            if (c == 0) return;
            cases++;
            // Both roundings of the operators, on whole numbers: the product rounded to the resolution, then the quotient rounded.
            var product = RoundedQuotient((BigInteger)a * b, BigInteger.One << Fixed.FractionBits);
            var expected = Narrowed(RoundedQuotient(product << Fixed.FractionBits, c));
            long? actual;
            try { actual = Fixed.MultiplyDivide(Fixed.FromRaw(a), Fixed.FromRaw(b), Fixed.FromRaw(c)).Raw; }
            catch (OverflowException) { actual = null; }
            if (actual != expected) throw new InvalidOperationException($"MultiplyDivide({a}, {b}, {c}) = {actual}, the wide formula gives {expected}.");
            if (Narrowed(product) is null) { if (expected is not null) beyond++; return; }
            long? plain;
            try { plain = (Fixed.FromRaw(a) * Fixed.FromRaw(b) / Fixed.FromRaw(c)).Raw; }
            catch (OverflowException) { plain = null; }
            if (plain != actual) throw new InvalidOperationException($"MultiplyDivide({a}, {b}, {c}) = {actual}, a * b / c = {plain}.");
            withOperators++;
        }

        for (var index = 0; index < 600000; index++) Against(dice.Wide(), dice.Wide(), dice.Wide());
        // Health ratios: health * newMaximum / oldMaximum with the health values of the data (up to 99,999,999) and bonuses around them.
        foreach (var health in new long[] { 1, 50, 1000, 741455, 741456, 999999, 1000000, 99999999 })
            foreach (var oldMaximum in new long[] { 1, 100, 741456, 999999, 1000000, 99999999 })
                foreach (var percent in new long[] { 50, 100, 110, 125, 150, 300 })
                    for (var offset = -2L; offset <= 2; offset++)
                    {
                        var newMaximum = Fixed.FromInt(oldMaximum).Raw / 100 * percent + offset;
                        Against(Fixed.FromInt(health).Raw + offset, newMaximum, Fixed.FromInt(oldMaximum).Raw);
                        Against(-(Fixed.FromInt(health).Raw + offset), newMaximum, Fixed.FromInt(oldMaximum).Raw - offset);
                    }
        for (var index = 0; index < 200000; index++)
        {
            var maximum = 1 + dice.Below(Fixed.FromInt(100000000).Raw);
            Against(dice.Below(maximum + 1), 1 + dice.Below(Fixed.FromInt(200000000).Raw), maximum);
        }
        Check.Throws<DivideByZeroException>(() => Fixed.MultiplyDivide(Fixed.One, Fixed.One, Fixed.Zero), string.Empty);
        Check.Throws<DivideByZeroException>(() => Fixed.MultiplyDivide(Fixed.FromInt(99999999), Fixed.FromInt(99999999), Fixed.Zero), string.Empty);
        var full = Fixed.MultiplyDivide(Fixed.FromInt(99999999), Fixed.FromInt(109999999), Fixed.FromInt(99999999));
        Check.That(full == Fixed.FromInt(109999999), $"Full health stays full health: 99,999,999 * 109,999,999 / 99,999,999 = {full}.");
        Check.That(cases > 700000 && withOperators > 100000 && beyond > 50000, $"The sweep ran ({cases} cases, {withOperators} against the operators, {beyond} with a product beyond the range and a quotient inside it).");
        Console.WriteLine($"       {cases} quotients of products against the wide formula, {withOperators} also against a * b / c, {beyond} with a product only 128 bits hold");
    }
}
