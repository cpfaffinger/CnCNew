using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 19, movement and path search (docs/PERF-MOVEMENT.md): every faster routine is compared with the implementation it replaced, which is kept here in its
/// original form, on inputs drawn from a fixed seed.
/// </summary>
static partial class Tests
{
    /// <summary>SplitMix64: the test's own generator, so that the inputs do not depend on the simulation's random streams.</summary>
    private sealed class PerfRandom(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            var value = _state += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public int Below(int bound) => (int)(Next() % (ulong)bound);

        /// <summary>A signed value of a random bit width (0 to 63 bits), so that small, medium and huge magnitudes are equally frequent.</summary>
        public long Wide()
        {
            var bits = Below(64);
            var magnitude = bits == 0 ? 0L : (long)(Next() >> (64 - bits));
            return (Next() & 1) == 0 ? magnitude : -magnitude;
        }
    }

    // The 128-bit formulas Fixed used until phase 19, unchanged.
    private static Int128 ReferenceRoundDivide(Int128 numerator, Int128 denominator)
    {
        var absDenominator = denominator < 0 ? -denominator : denominator;
        var negative = (numerator < 0) != (denominator < 0);
        var absNumerator = numerator < 0 ? -numerator : numerator;
        var quotient = (absNumerator + absDenominator / 2) / absDenominator;
        return negative ? -quotient : quotient;
    }

    private static long? ReferenceNarrow(Int128 value) => value > long.MaxValue || value < long.MinValue ? null : (long)value;

    private static long? ReferenceMultiply(long a, long b) => ReferenceNarrow(ReferenceRoundDivide((Int128)a * b, Fixed.ScaleRaw));

    private static long? ReferenceDivide(long a, long b) => ReferenceNarrow(ReferenceRoundDivide((Int128)a << Fixed.FractionBits, b));

    private static long ReferenceSqrt(long raw)
    {
        if (raw == 0) return 0;
        var target = (Int128)raw * Fixed.ScaleRaw;
        var x = (Int128)raw > Fixed.ScaleRaw ? (Int128)raw : Fixed.ScaleRaw;
        while (true)
        {
            var next = (x + target / x) / 2;
            if (next >= x) break;
            x = next;
        }
        return (long)x;
    }

    private static long? Outcome(Func<Fixed> operation)
    {
        try { return operation().Raw; }
        catch (OverflowException) { return null; }
    }

    public static void FixedFastPathsMatchWideArithmetic()
    {
        long[] edges =
        [
            0, 1, -1, 2, -2, 3, 32767, 32768, 32769, 65535, 65536, 65537, -32768, -65536,
            (1L << 31) - 1, 1L << 31, (1L << 31) + 1, -(1L << 31), (1L << 32) - 1, 1L << 32,
            (1L << 47) - 1, 1L << 47, (1L << 47) + 1, -(1L << 47), -(1L << 47) + 1, (1L << 48) - 1, 1L << 48,
            (1L << 62) - 1, 1L << 62, long.MaxValue - 1, long.MaxValue, long.MinValue + 1, long.MinValue,
            3037000499, 3037000500, 46341L << 16, 2147483647L << 16,
        ];
        var checkedPairs = 0;
        void Pair(long a, long b)
        {
            checkedPairs++;
            var product = Outcome(() => Fixed.FromRaw(a) * Fixed.FromRaw(b));
            Check.That(product == ReferenceMultiply(a, b), $"{a} * {b}: {product} instead of {ReferenceMultiply(a, b)}.");
            if (b == 0) return;
            var quotient = Outcome(() => Fixed.FromRaw(a) / Fixed.FromRaw(b));
            Check.That(quotient == ReferenceDivide(a, b), $"{a} / {b}: {quotient} instead of {ReferenceDivide(a, b)}.");
        }

        foreach (var a in edges)
            foreach (var b in edges) Pair(a, b);
        var random = new PerfRandom(0x19_0001);
        for (var round = 0; round < 400_000; round++) Pair(random.Wide(), random.Wide());
        // What the simulation mostly multiplies: coordinates, radii and their squares (up to a few thousand units) and values around one.
        for (var round = 0; round < 400_000; round++)
        {
            var a = (long)(random.Next() % (8000UL << 16)) - (4000L << 16);
            var b = round % 3 == 0 ? (long)(random.Next() % (2UL << 16)) - (1L << 16) : (long)(random.Next() % (8000UL << 16)) - (4000L << 16);
            Pair(a, b);
            Pair(a, edges[round % edges.Length]);
            Pair(edges[round % edges.Length], b);
        }
        Check.Throws<DivideByZeroException>(() => _ = Fixed.One / Fixed.Zero, "");
        Check.That(checkedPairs > 1_500_000, "The comparison ran.");

        foreach (var raw in edges)
            if (raw >= 0) Check.That(Fixed.Sqrt(Fixed.FromRaw(raw)).Raw == ReferenceSqrt(raw), $"sqrt of raw {raw}: {Fixed.Sqrt(Fixed.FromRaw(raw)).Raw} instead of {ReferenceSqrt(raw)}.");
        for (var raw = 0L; raw < 300_000; raw++)
            Check.That(Fixed.Sqrt(Fixed.FromRaw(raw)).Raw == ReferenceSqrt(raw), $"sqrt of raw {raw}.");
        for (var round = 0; round < 300_000; round++)
        {
            var raw = Math.Abs(random.Wide() + 1);
            if (raw < 0) raw = long.MaxValue;
            Check.That(Fixed.Sqrt(Fixed.FromRaw(raw)).Raw == ReferenceSqrt(raw), $"sqrt of raw {raw}: {Fixed.Sqrt(Fixed.FromRaw(raw)).Raw} instead of {ReferenceSqrt(raw)}.");
            // Perfect squares and their neighbours: where a rounding slip would show.
            var root = (long)(random.Next() % (1UL << 31));
            foreach (var near in new[] { root * root - 1, root * root, root * root + 1 })
            {
                if (near < 0 || near % Fixed.ScaleRaw != 0 && near >= 1L << 47) continue;
                var value = near >> (near >= 1L << 47 ? Fixed.FractionBits : 0);
                Check.That(Fixed.Sqrt(Fixed.FromRaw(value)).Raw == ReferenceSqrt(value), $"sqrt of raw {value}.");
            }
        }
        Check.Throws<ArgumentOutOfRangeException>(() => _ = Fixed.Sqrt(Fixed.FromRaw(-1)), "negative");
    }

    // ------------------------------------------------------------------------------------------- entity scans of a movement step

    // SimulationWorld.Collision.cs as it was before phase 19, on the public surface of the world: every entity, the exact circle test, no shortcut.
    private static Fixed ReferenceBoundingCircle(Entity entity)
    {
        var template = entity.Template;
        return template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase)
            ? Fixed.Sqrt(template.GeometryMajorRadius * template.GeometryMajorRadius + template.GeometryMinorRadius * template.GeometryMinorRadius)
            : template.GeometryMajorRadius;
    }

    private static EntityId ReferenceGoal(Entity mover) => mover.Modules.OfType<AIUpdateInterface>().FirstOrDefault()?.GoalObject ?? EntityId.Invalid;

    private static bool ReferenceStepBlocked(SimulationWorld world, Entity mover, Fixed dx, Fixed dy, EntityId goal)
    {
        var x = mover.X + dx;
        var y = mover.Y + dy;
        var moverRadius = ReferenceBoundingCircle(mover);
        foreach (var other in world.Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid) continue;
            var limit = moverRadius + ReferenceBoundingCircle(other);
            var ddx = other.X - x;
            var ddy = other.Y - y;
            if (ddx * ddx + ddy * ddy < limit * limit) return true;
        }
        return false;
    }

    private static (Fixed Dx, Fixed Dy) ReferenceAvoid(SimulationWorld world, Entity mover, Fixed dx, Fixed dy)
    {
        if (dx == Fixed.Zero && dy == Fixed.Zero) return (dx, dy);
        var goal = ReferenceGoal(mover);
        if (!ReferenceStepBlocked(world, mover, dx, dy, goal)) return (dx, dy);
        Fixed[] angles = [-FixedTrig.HalfPi / Fixed.FromInt(2), FixedTrig.HalfPi / Fixed.FromInt(2), -FixedTrig.HalfPi, FixedTrig.HalfPi];
        foreach (var angle in angles)
        {
            var cos = FixedTrig.Cos(angle);
            var sin = FixedTrig.Sin(angle);
            var rx = dx * cos - dy * sin;
            var ry = dx * sin + dy * cos;
            if (!ReferenceStepBlocked(world, mover, rx, ry, goal)) return (rx, ry);
        }
        return (dx, dy);
    }

    private static List<(Fixed X, Fixed Y, Fixed Radius)> ReferenceStationary(SimulationWorld world, Entity mover, Fixed dx, Fixed dy)
    {
        if (dx == Fixed.Zero && dy == Fixed.Zero) return [];
        var goal = ReferenceGoal(mover);
        var margin = Fixed.FromInt(5);
        var moverRadius = ReferenceBoundingCircle(mover);
        var blocked = false;
        foreach (var other in world.Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid || other.HasMoveTarget || other.Speed != Fixed.Zero) continue;
            var limit = moverRadius + ReferenceBoundingCircle(other) + margin;
            for (var sample = 1; sample <= 4 && !blocked; sample++)
            {
                var ddx = other.X - (mover.X + dx * Fixed.FromInt(sample) / Fixed.FromInt(4));
                var ddy = other.Y - (mover.Y + dy * Fixed.FromInt(sample) / Fixed.FromInt(4));
                if (ddx * ddx + ddy * ddy < limit * limit) blocked = true;
            }
            if (blocked) break;
        }
        if (!blocked) return [];
        var circles = new List<(Fixed X, Fixed Y, Fixed Radius)>();
        var around = Fixed.FromInt(150);
        foreach (var other in world.Entities)
        {
            if (other.Id == mover.Id || other.Id == goal || other.EffectivelyDead || other.Template.Locomotor is null || other.ContainedBy.IsValid || other.HasMoveTarget || other.Speed != Fixed.Zero) continue;
            var ddx = other.X - mover.X;
            var ddy = other.Y - mover.Y;
            if (ddx * ddx + ddy * ddy > around * around) continue;
            circles.Add((other.X, other.Y, ReferenceBoundingCircle(other) + moverRadius + margin));
        }
        return circles;
    }

    public static void MovementScansMatchTheFullScan()
    {
        var random = new PerfRandom(0x19_0002);
        long blockedSteps = 0, turnedSteps = 0, detours = 0, compared = 0;
        for (var scene = 0; scene < 6; scene++)
        {
            var (world, feeder, _) = NavSetup();
            var count = 20 + 12 * scene;
            for (var index = 0; index < count; index++)
            {
                var template = index % 9 == 0 ? "Wall" : index % 7 == 0 ? "Post" : index % 5 == 0 ? "Climber" : index % 11 == 0 ? "Flyer" : "Walker";
                feeder.Spawn(1, template, owner: 1 + index % 2, x: 5 + random.Below(190), y: 5 + random.Below(190));
            }
            world.Step();
            foreach (var entity in world.Entities) Check.That(entity.Template.BoundingCircleRadius == ReferenceBoundingCircle(entity), $"The stored bounding circle of {entity.Template.Name} is the computed one.");
            // Half of the mobile objects walk (fractional positions, moving objects are not standing blockers), the others stand.
            foreach (var entity in world.Entities.ToArray())
                if (entity.Template.Locomotor is not null && entity.Id.Value % 2 == 0) feeder.SetMoveTarget(entity.Owner.Value, entity.Id.Value, 5 + random.Below(190), 5 + random.Below(190));
            for (var round = 0; round < 12; round++)
            {
                for (var step = 0; step < 4; step++) world.Step();
                var entities = world.Entities.ToArray();
                foreach (var mover in entities)
                    for (var probe = 0; probe < 24; probe++)
                    {
                        // Steps from a fraction of a unit to a long look-ahead, in every direction; every fourth one aims exactly at another object.
                        var length = Fixed.FromRaw((long)(random.Next() % (ulong)(probe % 3 == 0 ? 2L << 16 : 220L << 16)));
                        var angle = Fixed.FromRaw((long)(random.Next() % (ulong)FixedTrig.TwoPi.Raw));
                        var dx = FixedTrig.Cos(angle) * length;
                        var dy = FixedTrig.Sin(angle) * length;
                        if (probe % 4 == 0)
                        {
                            var aim = entities[random.Below(entities.Length)];
                            dx = aim.X - mover.X;
                            dy = aim.Y - mover.Y;
                        }
                        var expectedStep = ReferenceAvoid(world, mover, dx, dy);
                        var actualStep = world.AvoidLocalBlockers(mover, dx, dy);
                        Check.That(actualStep == expectedStep, $"Scene {scene}, object {mover.Id.Value}, step ({dx}, {dy}): avoidance {actualStep} instead of {expectedStep}.");
                        if (ReferenceStepBlocked(world, mover, dx, dy, ReferenceGoal(mover))) blockedSteps++;
                        if (expectedStep != (dx, dy)) turnedSteps++;
                        var expected = ReferenceStationary(world, mover, dx, dy);
                        var actual = world.StationaryBlockersAhead(mover, dx, dy);
                        Check.That(actual.SequenceEqual(expected), $"Scene {scene}, object {mover.Id.Value}, step ({dx}, {dy}): {actual.Count} standing blockers instead of {expected.Count}.");
                        if (expected.Count > 0) detours++;
                        compared++;
                    }
            }
        }
        Check.That(compared > 30_000 && blockedSteps > 1_000 && turnedSteps > 500 && detours > 1_000, $"The comparison saw blocked steps, turned steps and detours ({compared}, {blockedSteps}, {turnedSteps}, {detours}).");
    }
}
