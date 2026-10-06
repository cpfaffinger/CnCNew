using System.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;

/// <summary>
/// Phase 22: the fixed-point routines are derived for a number of fraction bits, not for sixteen. Proven here before the resolution changes: at 16 and at
/// 24 bits (and at two more) products, quotients and roots equal the plain big-integer formulas; at 16 bits the angle functions return bit for bit what the
/// implementation before phase 22 returned (kept below as the oracle); and their error against the exact functions is measured per resolution.
/// </summary>
static partial class Tests
{
    private sealed class ResolutionDice(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 7;
            _state ^= _state << 17;
            return _state;
        }

        /// <summary>A signed value with a random number of significant bits (0..63): small and huge magnitudes are equally likely.</summary>
        public long Wide()
        {
            var bits = (int)(Next() % 64);
            var magnitude = bits == 0 ? 0L : (long)(Next() >> (64 - bits));
            return (Next() & 1) == 0 ? magnitude : -magnitude;
        }

        public long Below(long bound) => (long)(Next() % (ulong)bound);
    }

    private static long? Narrowed(BigInteger value) => value > long.MaxValue || value < long.MinValue ? null : (long)value;

    private static BigInteger RoundedQuotient(BigInteger numerator, BigInteger denominator)
    {
        var quotient = (BigInteger.Abs(numerator) + BigInteger.Abs(denominator) / 2) / BigInteger.Abs(denominator);
        return (numerator.Sign < 0) != (denominator.Sign < 0) ? -quotient : quotient;
    }

    private static long? WideMultiply(long a, long b, int bits) => Narrowed(RoundedQuotient((BigInteger)a * b, BigInteger.One << bits));

    private static long? WideDivide(long a, long b, int bits) => Narrowed(RoundedQuotient((BigInteger)a << bits, b));

    /// <summary>floor(sqrt(raw * scale)) by bisection on big integers: no relation to the routine under test.</summary>
    private static long WideSqrt(long raw, int bits)
    {
        var target = (BigInteger)raw << bits;
        BigInteger low = 0, high = BigInteger.One << 48;
        while (high - low > 1)
        {
            var middle = (low + high) >> 1;
            if (middle * middle <= target) low = middle;
            else high = middle;
        }
        return (long)low;
    }

    private static long? RawOutcome(Func<long> operation)
    {
        try { return operation(); }
        catch (OverflowException) { return null; }
    }

    public static void FixedMathMatchesWideArithmeticAtEveryResolution()
    {
        foreach (var bits in new[] { 16, 24, 12, 32 })
        {
            var scale = 1L << bits;
            var edges = new List<long> { 0, 1, -1, 2, -2, 3, scale - 1, scale, scale + 1, -scale, scale / 2, scale / 2 + 1, scale / 2 - 1, long.MaxValue, long.MaxValue - 1, long.MinValue, long.MinValue + 1, 3037000499, 3037000500 };
            foreach (var bit in new[] { 31, 32, 63 - bits - 1, 63 - bits, 63 - bits + 1, 64 - bits, 47, 48, 61, 62 })
                edges.AddRange([(1L << bit) - 1, 1L << bit, (1L << bit) + 1, -(1L << bit), -(1L << bit) + 1]);
            var pairs = 0;
            void Pair(long a, long b)
            {
                pairs++;
                var product = RawOutcome(() => FixedMath.Multiply(a, b, bits));
                if (product != WideMultiply(a, b, bits)) Check.That(false, $"{bits} bits: {a} * {b} gives {product}, the wide formula {WideMultiply(a, b, bits)}.");
                if (b == 0) return;
                var quotient = RawOutcome(() => FixedMath.Divide(a, b, bits));
                if (quotient != WideDivide(a, b, bits)) Check.That(false, $"{bits} bits: {a} / {b} gives {quotient}, the wide formula {WideDivide(a, b, bits)}.");
            }

            foreach (var a in edges)
                foreach (var b in edges) Pair(a, b);
            var dice = new ResolutionDice(0x22_0001UL + (ulong)bits);
            for (var round = 0; round < 150_000; round++) Pair(dice.Wide(), dice.Wide());
            // What the simulation computes: coordinates and radii (some thousand units), their squares (up to 10^8 units), values around one, and a large
            // dividend over a large divisor (a projection: dot product over squared length), which is the case the long division is for.
            for (var round = 0; round < 150_000; round++)
            {
                var length = dice.Below(8000 * scale) - 4000 * scale;
                var fraction = dice.Below(2 * scale) - scale;
                var area = dice.Below(100_000_000) * scale + dice.Below(scale);
                var otherArea = dice.Below(100_000_000) * scale + dice.Below(scale);
                Pair(length, fraction);
                Pair(length, dice.Below(8000 * scale) - 4000 * scale);
                Pair(area, otherArea);
                Pair(-area, length);
                Pair(area, edges[round % edges.Count]);
                Pair(edges[round % edges.Count], otherArea);
            }
            Check.Throws<DivideByZeroException>(() => _ = FixedMath.Divide(scale, 0, bits), "");
            Check.That(pairs > 1_000_000, "The comparison ran.");

            var roots = 0;
            void Root(long raw)
            {
                if (raw < 0) return;
                roots++;
                var root = FixedMath.Sqrt(raw, bits);
                if (root != WideSqrt(raw, bits)) Check.That(false, $"{bits} bits: the root of raw {raw} is {root}, the wide formula gives {WideSqrt(raw, bits)}.");
            }

            foreach (var raw in edges) Root(raw);
            for (var raw = 0L; raw < 20_000; raw++) Root(raw);
            for (var bit = 0; bit < 63; bit++)
                foreach (var raw in new[] { (1L << bit) - 1, 1L << bit, (1L << bit) + 1 }) Root(raw);
            for (var round = 0; round < 60_000; round++)
            {
                Root(Math.Abs(dice.Wide() + 1));
                Root(dice.Below(100_000_000) * scale + dice.Below(scale));
                // Squares of representable values and their neighbours: where a rounding slip would show.
                var value = dice.Below(1L << (20 + bits / 2));
                var square = Narrowed(((BigInteger)value * value) >> bits);
                if (square is { } exact)
                    foreach (var near in new[] { exact - 1, exact, exact + 1 }) Root(near);
            }
            Check.Throws<ArgumentOutOfRangeException>(() => _ = FixedMath.Sqrt(-1, bits), "negative");
            Check.That(roots > 150_000, "The roots were compared.");
        }

        // Fixed is these routines at its own resolution.
        var own = new ResolutionDice(0x22_00FF);
        for (var round = 0; round < 50_000; round++)
        {
            long a = own.Wide() >> 8, b = own.Wide() >> 8;
            Check.That(RawOutcome(() => (Fixed.FromRaw(a) * Fixed.FromRaw(b)).Raw) == WideMultiply(a, b, Fixed.FractionBits), $"Fixed product of raw {a} and {b}.");
            if (b != 0) Check.That(RawOutcome(() => (Fixed.FromRaw(a) / Fixed.FromRaw(b)).Raw) == WideDivide(a, b, Fixed.FractionBits), $"Fixed quotient of raw {a} and {b}.");
            if (a >= 0) Check.That(Fixed.Sqrt(Fixed.FromRaw(a)).Raw == WideSqrt(a, Fixed.FractionBits), $"Fixed root of raw {a}.");
        }
        Check.That(Fixed.FromFraction(1, 64) * Fixed.FromInt(64) == Fixed.One && Fixed.FromFraction(-1, 2) == -Fixed.FromFraction(1, 2) && Fixed.FromFraction(3, 1) == Fixed.FromInt(3), "FromFraction is exact for dyadic fractions.");
        Check.That(Fixed.FromFraction(1, 3) == Fixed.One / Fixed.FromInt(3) && Fixed.FromFraction(-2, 3) == Fixed.FromInt(-2) / Fixed.FromInt(3), "FromFraction rounds like the quotient.");
        Check.That(Fixed.Epsilon.Raw == 1, "Epsilon is one step.");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------
    // FixedTrig as it was before phase 22, word for word on raw values with 16 fraction bits: the oracle that the 16-bit path still returns the same bits.

    private static class SixteenBitTrig
    {
        private const long One = 65536;
        private const long Pi = 205887;
        private const long TwoPi = 411775;
        private const long HalfPi = 102944;
        private static readonly long[] AtanTable = [51472, 30386, 16055, 8150, 4091, 2047, 1024, 512, 256, 128, 64, 32, 16, 8, 4, 2];

        private static long Mul(long a, long b) => (long)RoundedQuotient((BigInteger)a * b, One);
        private static long Div(long a, long b) => (long)RoundedQuotient((BigInteger)a << 16, b);

        public static long Atan2(long y, long x)
        {
            var vx = x;
            var vy = y;
            if (vx == 0 && vy == 0) return 0;
            long offset = 0;
            if (vx < 0)
            {
                offset = vy >= 0 ? Pi : -Pi;
                vx = -vx;
                vy = -vy;
            }
            vx <<= 16;
            vy <<= 16;
            long angle = 0;
            for (var i = 0; i < AtanTable.Length; i++)
            {
                var nx = vy > 0 ? vx + (vy >> i) : vx - (vy >> i);
                var ny = vy > 0 ? vy - (vx >> i) : vy + (vx >> i);
                angle += vy > 0 ? AtanTable[i] : -AtanTable[i];
                vx = nx;
                vy = ny;
            }
            return angle + offset;
        }

        public static long AngleDifference(long from, long to)
        {
            var raw = (to - from) % TwoPi;
            if (raw > Pi) raw -= TwoPi;
            else if (raw <= -Pi) raw += TwoPi;
            return raw;
        }

        public static long Sin(long radians)
        {
            var raw = radians % TwoPi;
            if (raw > Pi) raw -= TwoPi;
            else if (raw < -Pi) raw += TwoPi;
            if (raw > HalfPi) raw = Pi - raw;
            else if (raw < -HalfPi) raw = -Pi - raw;
            var x = raw;
            var x2 = Mul(x, x);
            var inner = One - Div(x2, 156 * One);
            inner = One - Div(Mul(x2, inner), 110 * One);
            inner = One - Div(Mul(x2, inner), 72 * One);
            inner = One - Div(Mul(x2, inner), 42 * One);
            inner = One - Div(Mul(x2, inner), 20 * One);
            inner = One - Div(Mul(x2, inner), 6 * One);
            return Mul(x, inner);
        }

        public static long Cos(long radians)
        {
            var raw = radians % TwoPi;
            if (raw > Pi) raw -= TwoPi;
            else if (raw < -Pi) raw += TwoPi;
            var negate = false;
            if (raw > HalfPi) { raw = Pi - raw; negate = true; }
            else if (raw < -HalfPi) { raw = -Pi - raw; negate = true; }
            var x = raw;
            var x2 = Mul(x, x);
            var inner = One - Div(x2, 132 * One);
            inner = One - Div(Mul(x2, inner), 90 * One);
            inner = One - Div(Mul(x2, inner), 56 * One);
            inner = One - Div(Mul(x2, inner), 30 * One);
            inner = One - Div(Mul(x2, inner), 12 * One);
            var result = One - Div(Mul(x2, inner), 2 * One);
            return negate ? -result : result;
        }
    }

    public static void FixedTrigReproducesTheSixteenBitResults()
    {
        const int bits = 16;
        Check.That(FixedMath.Pi(bits) == 205887 && FixedMath.TwoPi(bits) == 411775 && FixedMath.HalfPi(bits) == 102944, "π, 2π and π/2 at 16 bits are the constants that were written out.");
        Check.That(FixedMath.PiFraction(1, 6, bits) == 34315 && FixedMath.PiFraction(1, 1, bits) == 205887 && FixedMath.PiFraction(2, 1, bits) == 411775 && FixedMath.PiFraction(1, 2, bits) == 102944, "Fractions of π at 16 bits.");
        var table = FixedMath.AtanTable(bits);
        Check.That(table.SequenceEqual(new long[] { 51472, 30386, 16055, 8150, 4091, 2047, 1024, 512, 256, 128, 64, 32, 16, 8, 4, 2 }), "The arctangent table at 16 bits is the table that was written out.");

        // Sine and cosine: every raw angle of three turns either way, then angles of any size.
        long compared = 0;
        for (var raw = -3 * 411775L; raw <= 3 * 411775L; raw++)
        {
            compared++;
            if (FixedMath.Sin(raw, bits) != SixteenBitTrig.Sin(raw)) Check.That(false, $"Sin of raw {raw}: {FixedMath.Sin(raw, bits)} instead of {SixteenBitTrig.Sin(raw)}.");
            if (FixedMath.Cos(raw, bits) != SixteenBitTrig.Cos(raw)) Check.That(false, $"Cos of raw {raw}: {FixedMath.Cos(raw, bits)} instead of {SixteenBitTrig.Cos(raw)}.");
        }
        var dice = new ResolutionDice(0x22_0002);
        for (var round = 0; round < 300_000; round++)
        {
            var raw = dice.Wide();
            compared++;
            if (FixedMath.Sin(raw, bits) != SixteenBitTrig.Sin(raw) || FixedMath.Cos(raw, bits) != SixteenBitTrig.Cos(raw)) Check.That(false, $"Sin or Cos of raw {raw} differs.");
            var other = dice.Wide() >> 2;
            raw >>= 2;
            if (FixedMath.AngleDifference(raw, other, bits) != SixteenBitTrig.AngleDifference(raw, other)) Check.That(false, $"The turn from raw {raw} to {other} differs.");
        }

        // The arctangent: every vector of a small raw grid (where the scaled shifts matter most), every vector of a grid in whole units, and vectors of
        // every length up to 2^44 raw (268 million units); beyond 2^45 raw the old routine left the machine word and returned noise.
        void Vector(long y, long x)
        {
            compared++;
            var angle = FixedMath.Atan2(y, x, bits, table);
            if (angle != SixteenBitTrig.Atan2(y, x)) Check.That(false, $"Atan2({y}, {x}) raw: {angle} instead of {SixteenBitTrig.Atan2(y, x)}.");
        }

        for (var y = -260L; y <= 260; y++)
            for (var x = -260L; x <= 260; x++) Vector(y, x);
        for (var y = -180L; y <= 180; y++)
            for (var x = -180L; x <= 180; x++) Vector(y * 65536 * 7, x * 65536 * 7);
        for (var round = 0; round < 3_000_000; round++)
        {
            var shift = 20 + (int)(dice.Next() % 44);
            long y = dice.Wide() >> shift, x = dice.Wide() >> shift;
            Vector(y, x);
            Vector(y, dice.Wide() >> (20 + (int)(dice.Next() % 44)));
        }
        Check.That(compared > 8_000_000, $"The 16-bit comparison ran ({compared}).");
    }

    /// <summary>The largest error of the angle functions against the exact ones at a resolution, in radians (arctangent) and as a value (sine, cosine).</summary>
    private static (double Sin, double Cos, double Atan) TrigErrors(int bits)
    {
        var scale = (double)(1L << bits);
        var twoPi = FixedMath.TwoPi(bits);
        double worstSin = 0, worstCos = 0, worstAtan = 0;
        var step = Math.Max(1, twoPi / 400_000);
        for (var raw = -twoPi; raw <= twoPi; raw += step)
        {
            // The argument the routine sees is the raw angle; the exact function is taken of that same number.
            worstSin = Math.Max(worstSin, Math.Abs(FixedMath.Sin(raw, bits) / scale - Math.Sin(raw / scale)));
            worstCos = Math.Max(worstCos, Math.Abs(FixedMath.Cos(raw, bits) / scale - Math.Cos(raw / scale)));
        }
        var table = FixedMath.AtanTable(bits);
        var dice = new ResolutionDice(0x22_0003UL + (ulong)bits);
        for (var round = 0; round < 600_000; round++)
        {
            // Vectors from a hundredth of a unit to some thousand units, every direction.
            var magnitude = (round % 3) switch { 0 => 4000L << bits, 1 => 40L << bits, _ => Math.Max(64, (1L << bits) / 64) };
            long y = dice.Below(2 * magnitude) - magnitude, x = dice.Below(2 * magnitude) - magnitude;
            if (Math.Abs(x) + Math.Abs(y) < magnitude / 8) continue;
            var difference = Math.Abs(FixedMath.Atan2(y, x, bits, table) / scale - Math.Atan2(y, x));
            if (difference > Math.PI) difference = 2 * Math.PI - difference;
            worstAtan = Math.Max(worstAtan, difference);
        }
        return (worstSin, worstCos, worstAtan);
    }

    public static void FixedTrigPrecisionFollowsTheResolution()
    {
        var coarse = TrigErrors(16);
        var fine = TrigErrors(24);
        Console.WriteLine($"       trig error against the exact functions: 16 bits sin {coarse.Sin:E2} cos {coarse.Cos:E2} atan2 {coarse.Atan:E2} rad; 24 bits sin {fine.Sin:E2} cos {fine.Cos:E2} atan2 {fine.Atan:E2} rad");
        Check.That(coarse.Sin < 1e-4 && coarse.Cos < 1e-4 && coarse.Atan < 2e-3, "At 16 bits the errors are within the bounds the functions were documented with.");
        // 24 bits: one step is 6e-8. Sine and cosine stay within a few steps (seven rounded operations), the arctangent within its 24 rounded table entries.
        Check.That(fine.Sin < 5e-7 && fine.Cos < 5e-7 && fine.Atan < 2e-6, $"At 24 bits the errors shrink with the resolution (sin {fine.Sin:E2}, cos {fine.Cos:E2}, atan2 {fine.Atan:E2}).");
        Check.That(fine.Sin < coarse.Sin / 100 && fine.Cos < coarse.Cos / 100 && fine.Atan < coarse.Atan / 50, "Eight more bits make every angle function at least fifty times more exact.");
        Check.That(FixedMath.Pi(24) == 52707179 && FixedMath.TwoPi(24) == 105414357 && FixedMath.HalfPi(24) == 26353589 && FixedMath.PiFraction(1, 6, 24) == 8784530, "π and its fractions at 24 bits are the rounded exact values.");
        // A vector too long for the full scale of the CORDIC still gets its angle (the scale shrinks instead of the word overflowing).
        foreach (var bits in new[] { 16, 24 })
        {
            var table = FixedMath.AtanTable(bits);
            var scale = (double)(1L << bits);
            foreach (var (y, x) in new[] { (1L << 61, 1L << 61), (-(1L << 60), 1L << 62), (long.MaxValue, -long.MaxValue), (long.MinValue + 1, 3L), (1L << 50, -(1L << 49)) })
            {
                var difference = Math.Abs(FixedMath.Atan2(y, x, bits, table) / scale - Math.Atan2(y, x));
                Check.That(difference < 1e-3, $"{bits} bits: the angle of the long vector ({x}, {y}) is off by {difference:E2}.");
            }
        }
    }

    /// <summary>
    /// The formats carry raw values, so their versions changed with the resolution: world snapshot 9 → 10, replay 11 → 12, command table 14 → 15 (mission
    /// state 2 → 3: <c>Tests.Scripting</c>; savegame 1 → 2: the game tests). A state of 16 fraction bits is refused by its version with a message that
    /// names it; nothing reads it with the new scale.
    /// </summary>
    public static void StatesOfSixteenFractionBitsAreRejected()
    {
        Check.That(Fixed.FractionBits == 24 && WorldSnapshot.Version == 10 && ReplayFormat.CurrentVersion == 12 && CommandTypes.Version == 15, "The formats of 24 fraction bits: snapshot 10, replay 12, command table 15.");
        var (fixture, replay, _) = ShortReplay();
        var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = 300 });
        var text = System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(world));
        var sixteen = text.Replace($"\"version\":{WorldSnapshot.Version}", "\"version\":9", StringComparison.Ordinal);
        Check.That(sixteen != text, "The snapshot names its version.");
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(System.Text.Encoding.UTF8.GetBytes(sixteen), fixture.Templates, fixture.Fingerprint), "Unsupported snapshot version 9");

        // The replay container: the version is the word after the magic, checked before anything else is read.
        var bytes = ReplayFormat.Write(replay);
        var oldContainer = (byte[])bytes.Clone();
        BitConverter.TryWriteBytes(oldContainer.AsSpan(8, 4), 11u);
        Check.That(BitConverter.ToUInt32(bytes, 8) == (uint)ReplayFormat.CurrentVersion, "The version word follows the eight bytes of the magic.");
        Check.Throws<ReplayFormatException>(() => ReplayFormat.Read(oldContainer), "Unsupported replay format version 11");

        // The command table: a replay that names table 14 is not run, whatever its container says.
        var oldTable = replay with { Header = replay.Header with { CommandTableVersion = 14 } };
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(oldTable, fixture.Templates, fixture.Fingerprint), "command table version 14");
    }
}
