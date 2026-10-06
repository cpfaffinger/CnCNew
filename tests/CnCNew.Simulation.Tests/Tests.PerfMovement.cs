using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
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

    // --------------------------------------------------------------------------------------------------------- path search

    /// <summary>
    /// The search of <see cref="NavigationGrid"/> as it was before phase 19 (hash sets for the escape and temporary cells, four arrays per cell, lists for the result), reading
    /// passability through the grid's public test. Every path the grid returns must be this one, point for point.
    /// </summary>
    private sealed class ReferencePathfinder(NavigationGrid grid)
    {
        private const int StraightCost = 10, DiagonalCost = 14, ExpansionBudget = 200_000, EscapeRadius = 12, GoalEscapeRadius = 8, EscapePenalty = 4;
        private static readonly int[] StepX = [0, 1, 0, -1, 1, 1, -1, -1];
        private static readonly int[] StepY = [-1, 0, 1, 0, -1, 1, 1, -1];
        private readonly TerrainMap _terrain = grid.Terrain;
        private readonly int[] _cost = new int[grid.Terrain.CellsX * grid.Terrain.CellsY];
        private readonly int[] _parent = new int[grid.Terrain.CellsX * grid.Terrain.CellsY];
        private readonly int[] _stamp = new int[grid.Terrain.CellsX * grid.Terrain.CellsY];
        private readonly int[] _closedStamp = new int[grid.Terrain.CellsX * grid.Terrain.CellsY];
        private int _currentStamp;
        private readonly HashSet<int> _escape = [];
        private readonly HashSet<int> _temporary = [];
        private long[] _heap = new long[256];
        private int _heapCount;

        public int Expansions { get; private set; }

        private bool IsOpen(int cx, int cy, LocomotorSurfaces surfaces)
            => (grid.IsPassable(cx, cy, surfaces) && (_temporary.Count == 0 || !_temporary.Contains(cy * _terrain.CellsX + cx)))
               || (_escape.Count > 0 && _terrain.IsInside(cx, cy) && _escape.Contains(cy * _terrain.CellsX + cx));

        public PathResult FindPath(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> temporaryBlocks)
        {
            _temporary.Clear();
            var start = ClampedCell(startX, startY);
            var goal = ClampedCell(goalX, goalY);
            foreach (var (x, y, radius) in temporaryBlocks)
            {
                var centreX = TerrainMap.CellOf(x);
                var centreY = TerrainMap.CellOf(y);
                var reach = TerrainMap.CellOf(radius) + 1;
                for (var cy = centreY - reach; cy <= centreY + reach; cy++)
                    for (var cx = centreX - reach; cx <= centreX + reach; cx++)
                    {
                        if (!_terrain.IsInside(cx, cy) || Math.Abs(cx - start.X) <= 1 && Math.Abs(cy - start.Y) <= 1 || Math.Abs(cx - goal.X) <= 1 && Math.Abs(cy - goal.Y) <= 1) continue;
                        var ddx = TerrainMap.CellCenter(cx) - x;
                        var ddy = TerrainMap.CellCenter(cy) - y;
                        if (ddx * ddx + ddy * ddy <= radius * radius) _temporary.Add(cy * _terrain.CellsX + cx);
                    }
            }

            try { return FindPath(startX, startY, goalX, goalY, surfaces); }
            finally { _temporary.Clear(); }
        }

        private void CollectEscapeCells(int startX, int startY, int radius, LocomotorSurfaces surfaces)
        {
            var cellsX = _terrain.CellsX;
            if (!_terrain.IsInside(startX, startY) || !grid.IsBlockedByObstacle(startX, startY)) return;
            var queue = new Queue<(int X, int Y)>();
            _escape.Add(startY * cellsX + startX);
            queue.Enqueue((startX, startY));
            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (var direction = 0; direction < 8; direction++)
                {
                    var nx = x + StepX[direction];
                    var ny = y + StepY[direction];
                    if (Math.Abs(nx - startX) > radius || Math.Abs(ny - startY) > radius || !_terrain.IsInside(nx, ny)) continue;
                    var index = ny * cellsX + nx;
                    if (!grid.IsBlockedByObstacle(nx, ny) || !NavigationGrid.SurfaceAllows(_terrain.CellAt(nx, ny), surfaces) || !_escape.Add(index)) continue;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        public PathResult FindPath(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces)
        {
            Expansions = 0;
            var start = ClampedCell(startX, startY);
            var goal = ClampedCell(goalX, goalY);
            var cellsX = _terrain.CellsX;
            var startIndex = start.Y * cellsX + start.X;
            var goalIndex = goal.Y * cellsX + goal.X;
            if (startIndex == goalIndex)
                return new PathResult(PathStatus.Found, [new PathPoint(goalX, goalY)]);

            ++_currentStamp;
            _heapCount = 0;
            _escape.Clear();
            CollectEscapeCells(start.X, start.Y, EscapeRadius, surfaces);
            CollectEscapeCells(goal.X, goal.Y, GoalEscapeRadius, surfaces);
            _cost[startIndex] = 0;
            _parent[startIndex] = -1;
            _stamp[startIndex] = _currentStamp;
            Push(Heuristic(start.X, start.Y, goal.X, goal.Y), startIndex);

            var best = startIndex;
            var bestHeuristic = Heuristic(start.X, start.Y, goal.X, goal.Y);
            var reached = false;
            var expansions = 0;
            for (; _heapCount > 0 && expansions < ExpansionBudget; expansions++)
            {
                var current = (int)(Pop() & 0xFFFFFFFFL);
                if (_closedStamp[current] == _currentStamp) continue;
                _closedStamp[current] = _currentStamp;
                var cx = current % cellsX;
                var cy = current / cellsX;
                if (current == goalIndex) { reached = true; break; }
                var heuristic = Heuristic(cx, cy, goal.X, goal.Y);
                if (heuristic < bestHeuristic || (heuristic == bestHeuristic && (_cost[current] < _cost[best] || (_cost[current] == _cost[best] && current < best))))
                {
                    best = current;
                    bestHeuristic = heuristic;
                }
                for (var direction = 0; direction < 8; direction++)
                {
                    var nx = cx + StepX[direction];
                    var ny = cy + StepY[direction];
                    if (!IsOpen(nx, ny, surfaces)) continue;
                    var diagonal = direction >= 4;
                    if (diagonal && (!IsOpen(cx + StepX[direction], cy, surfaces) || !IsOpen(cx, cy + StepY[direction], surfaces))) continue;
                    var next = ny * cellsX + nx;
                    if (_closedStamp[next] == _currentStamp) continue;
                    var cost = _cost[current] + (diagonal ? DiagonalCost : StraightCost) * (_escape.Contains(next) ? EscapePenalty : 1);
                    if (_stamp[next] == _currentStamp && _cost[next] <= cost) continue;
                    _stamp[next] = _currentStamp;
                    _cost[next] = cost;
                    _parent[next] = current;
                    Push(cost + Heuristic(nx, ny, goal.X, goal.Y), next);
                }
            }

            Expansions = expansions;
            var end = reached ? goalIndex : best;
            if (end == startIndex) return new PathResult(PathStatus.Blocked, []);

            var cells = new List<int>();
            for (var cell = end; cell >= 0; cell = _parent[cell]) cells.Add(cell);
            cells.Reverse();

            var points = System.Collections.Immutable.ImmutableArray.CreateBuilder<PathPoint>();
            var from = 0;
            while (from < cells.Count - 1)
            {
                var to = from + 1;
                while (to + 1 < cells.Count && LineClear(cells[from], cells[to + 1], surfaces)) to++;
                points.Add(new PathPoint(TerrainMap.CellCenter(cells[to] % cellsX), TerrainMap.CellCenter(cells[to] / cellsX)));
                from = to;
            }
            if (reached) points[^1] = new PathPoint(goalX, goalY);
            return new PathResult(reached ? PathStatus.Found : PathStatus.Partial, points.ToImmutable());
        }

        private (int X, int Y) ClampedCell(Fixed x, Fixed y)
            => (Math.Clamp(TerrainMap.CellOf(x), 0, _terrain.CellsX - 1), Math.Clamp(TerrainMap.CellOf(y), 0, _terrain.CellsY - 1));

        private static int Heuristic(int x, int y, int goalX, int goalY)
        {
            var dx = Math.Abs(x - goalX);
            var dy = Math.Abs(y - goalY);
            return StraightCost * (dx + dy) - (2 * StraightCost - DiagonalCost) * Math.Min(dx, dy);
        }

        private bool LineClear(int fromIndex, int toIndex, LocomotorSurfaces surfaces)
        {
            var cellsX = _terrain.CellsX;
            int x = fromIndex % cellsX, y = fromIndex / cellsX;
            int x1 = toIndex % cellsX, y1 = toIndex / cellsX;
            var dx = Math.Abs(x1 - x);
            var dy = Math.Abs(y1 - y);
            var sx = x < x1 ? 1 : -1;
            var sy = y < y1 ? 1 : -1;
            var error = dx - dy;
            while (x != x1 || y != y1)
            {
                var doubled = 2 * error;
                var stepX = doubled > -dy;
                var stepY = doubled < dx;
                if (stepX && stepY)
                {
                    if (!IsOpen(x + sx, y, surfaces) || !IsOpen(x, y + sy, surfaces)) return false;
                    error += dx - dy;
                    x += sx;
                    y += sy;
                }
                else if (stepX) { error -= dy; x += sx; }
                else { error += dx; y += sy; }
                if (!IsOpen(x, y, surfaces)) return false;
            }
            return true;
        }

        private void Push(int cost, int index)
        {
            if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
            var key = ((long)cost << 32) | (uint)index;
            var child = _heapCount++;
            while (child > 0)
            {
                var parent = (child - 1) >> 1;
                if (_heap[parent] <= key) break;
                _heap[child] = _heap[parent];
                child = parent;
            }
            _heap[child] = key;
        }

        private long Pop()
        {
            var top = _heap[0];
            var last = _heap[--_heapCount];
            if (_heapCount > 0)
            {
                var parent = 0;
                while (true)
                {
                    var child = 2 * parent + 1;
                    if (child >= _heapCount) break;
                    if (child + 1 < _heapCount && _heap[child + 1] < _heap[child]) child++;
                    if (_heap[child] >= last) break;
                    _heap[parent] = _heap[child];
                    parent = child;
                }
                _heap[parent] = last;
            }
            return top;
        }
    }

    private static bool SamePath(PathResult a, PathResult b) => a.Status == b.Status && a.Points.AsSpan().SequenceEqual(b.Points.AsSpan());

    private static string PathText(PathResult path) => $"{path.Status} [{string.Join(" ", path.Points.Select(point => $"({point.X},{point.Y})"))}]";

    private static readonly LocomotorSurfaces[] PerfSurfaces =
    [
        LocomotorSurfaces.Ground, LocomotorSurfaces.Ground, LocomotorSurfaces.Ground | LocomotorSurfaces.Rubble, LocomotorSurfaces.Ground | LocomotorSurfaces.Water,
        LocomotorSurfaces.Ground | LocomotorSurfaces.Cliff, LocomotorSurfaces.Water, LocomotorSurfaces.Ground | LocomotorSurfaces.Water | LocomotorSurfaces.Cliff, LocomotorSurfaces.None,
    ];

    /// <summary>A map of the given size with scattered cliff cells, cliff walls with gaps, a sealed cliff ring and a lake.</summary>
    private static TerrainMap PerfTerrain(PerfRandom random, int cellsX, int cellsY, int cliffPercent)
    {
        var cliffs = new HashSet<(int X, int Y)>();
        for (var y = 0; y < cellsY; y++)
            for (var x = 0; x < cellsX; x++)
                if (random.Below(100) < cliffPercent) cliffs.Add((x, y));
        for (var wall = 0; wall < 3; wall++)
        {
            var at = 3 + random.Below(Math.Max(1, cellsX - 6));
            var gap = random.Below(cellsY);
            for (var y = 0; y < cellsY; y++)
                if (Math.Abs(y - gap) > 1) cliffs.Add((at, y));
        }
        // A closed ring: whatever lies inside cannot be reached on the ground, whatever starts inside cannot leave.
        var ringX = 2 + random.Below(Math.Max(1, cellsX - 12));
        var ringY = 2 + random.Below(Math.Max(1, cellsY - 12));
        for (var offset = 0; offset < 8; offset++)
        {
            cliffs.Add((ringX + offset, ringY));
            cliffs.Add((ringX + offset, ringY + 7));
            cliffs.Add((ringX, ringY + offset));
            cliffs.Add((ringX + 7, ringY + offset));
        }
        var lakeX = TerrainMap.CellSize * (2 + random.Below(Math.Max(1, cellsX - 10)));
        var lakeY = TerrainMap.CellSize * (2 + random.Below(Math.Max(1, cellsY - 10)));
        var lake = SyntheticMaps.Water("Lake", 10, (lakeX, lakeY), (lakeX + 70, lakeY), (lakeX + 70, lakeY + 50), (lakeX, lakeY + 50));
        return TerrainMap.Build(SyntheticMaps.Build(cellsX, cellsY, 1, (_, _) => 0, cliffs.Select(cell => (cell.X + 1, cell.Y + 1)), [lake]), $"perf-{cellsX}x{cellsY}-{cliffPercent}");
    }

    private static (ObjectTemplate Wall, ObjectTemplate Post) PerfFootprintTemplates()
    {
        var (world, feeder, _) = NavSetup();
        feeder.Spawn(1, "Wall", x: 100, y: 100);
        feeder.Spawn(1, "Post", x: 30, y: 150);
        world.Step();
        return (world.FindEntity(new EntityId(1))!.Template, world.FindEntity(new EntityId(2))!.Template);
    }

    private readonly record struct PerfQuery(Fixed StartX, Fixed StartY, Fixed GoalX, Fixed GoalY, LocomotorSurfaces Surfaces, (Fixed X, Fixed Y, Fixed Radius)[] Blocks);

    private static PerfQuery PerfRandomQuery(PerfRandom random, TerrainMap terrain, List<(ulong Id, ObjectTemplate Template, Fixed X, Fixed Y, Fixed Angle)> footprints)
    {
        var extentX = terrain.CellsX * TerrainMap.CellSize;
        var extentY = terrain.CellsY * TerrainMap.CellSize;
        Fixed Coordinate(int extent) => Fixed.FromRaw((long)(random.Next() % (ulong)((extent + 40L) << 16)) - (20L << 16));
        var startX = Coordinate(extentX);
        var startY = Coordinate(extentY);
        var goalX = Coordinate(extentX);
        var goalY = Coordinate(extentY);
        // Starts and goals inside a footprint exercise the escape cells; goals a few cells from the start the short searches.
        if (footprints.Count > 0 && random.Below(4) == 0) (startX, startY) = (footprints[random.Below(footprints.Count)].X + Fixed.FromInt(random.Below(11) - 5), footprints[random.Below(footprints.Count)].Y);
        if (footprints.Count > 0 && random.Below(4) == 0) (goalX, goalY) = (footprints[random.Below(footprints.Count)].X, footprints[random.Below(footprints.Count)].Y + Fixed.FromInt(random.Below(11) - 5));
        if (random.Below(5) == 0) (goalX, goalY) = (startX + Fixed.FromInt(random.Below(61) - 30), startY + Fixed.FromInt(random.Below(61) - 30));
        var blocks = new (Fixed X, Fixed Y, Fixed Radius)[random.Below(3) == 0 ? 1 + random.Below(6) : 0];
        for (var index = 0; index < blocks.Length; index++)
        {
            // Mostly near the straight line, where a blocker matters; sometimes on the goal or the start itself.
            var along = Fixed.FromRaw((long)(random.Next() % 65537));
            var where = random.Below(6);
            var x = where == 0 ? goalX : where == 1 ? startX : startX + (goalX - startX) * along + Fixed.FromInt(random.Below(21) - 10);
            var y = where == 0 ? goalY : where == 1 ? startY : startY + (goalY - startY) * along + Fixed.FromInt(random.Below(21) - 10);
            blocks[index] = (x, y, Fixed.FromRaw((long)(random.Next() % (40L << 16))));
        }
        return new PerfQuery(startX, startY, goalX, goalY, PerfSurfaces[random.Below(PerfSurfaces.Length)], blocks);
    }

    private static void PerfChangeFootprints(PerfRandom random, NavigationGrid grid, List<(ulong Id, ObjectTemplate Template, Fixed X, Fixed Y, Fixed Angle)> footprints, (ObjectTemplate Wall, ObjectTemplate Post) templates, ref ulong nextId)
    {
        if (footprints.Count > 0 && random.Below(3) == 0)
        {
            var index = random.Below(footprints.Count);
            var (id, template, x, y, angle) = footprints[index];
            grid.RemoveFootprint(id, template, x, y, angle);
            footprints.RemoveAt(index);
            return;
        }
        var added = (nextId++, random.Below(2) == 0 ? templates.Wall : templates.Post,
            Fixed.FromInt(random.Below(grid.Terrain.CellsX * TerrainMap.CellSize)), Fixed.FromInt(random.Below(grid.Terrain.CellsY * TerrainMap.CellSize)),
            Fixed.FromRaw((long)(random.Next() % (ulong)FixedTrig.TwoPi.Raw)));
        grid.AddFootprint(added.Item1, added.Item2, added.Item3, added.Item4, added.Item5);
        footprints.Add(added);
    }

    private static PathResult PerfAsk(NavigationGrid grid, in PerfQuery query)
        => query.Blocks.Length == 0 ? grid.FindPath(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces) : grid.FindPath(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces, query.Blocks);

    private static PathResult PerfAsk(ReferencePathfinder reference, in PerfQuery query)
        => query.Blocks.Length == 0 ? reference.FindPath(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces) : reference.FindPath(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces, query.Blocks);

    public static void PathSearchMatchesTheOriginalSearch()
    {
        var random = new PerfRandom(0x19_0003);
        var templates = PerfFootprintTemplates();
        var statuses = new int[3];
        long compared = 0, withBlockers = 0, fromObstacle = 0, repeated = 0;
        for (var map = 0; map < 10; map++)
        {
            var terrain = PerfTerrain(random, 24 + 9 * map, 30 + 7 * map, map % 4 * 6);
            var grid = new NavigationGrid(terrain);
            var reference = new ReferencePathfinder(grid);
            var footprints = new List<(ulong Id, ObjectTemplate Template, Fixed X, Fixed Y, Fixed Angle)>();
            ulong nextId = 1;
            var asked = new List<PerfQuery>();
            for (var round = 0; round < 40; round++)
            {
                // The footprints change between the rounds: every answer the grid remembered from the round before must be forgotten.
                for (var change = 0; change < (round == 0 ? 6 : 1 + random.Below(2)); change++) PerfChangeFootprints(random, grid, footprints, templates, ref nextId);
                for (var index = 0; index < 60; index++)
                {
                    PerfQuery query;
                    if (asked.Count > 0 && random.Below(3) == 0)
                    {
                        // The same cells again, in this round or after footprints changed; half of the time another exact goal inside the same goal cell.
                        query = asked[random.Below(asked.Count)];
                        if (random.Below(2) == 0)
                        {
                            var cellX = Fixed.FromInt(TerrainMap.CellOf(query.GoalX) * TerrainMap.CellSize);
                            var cellY = Fixed.FromInt(TerrainMap.CellOf(query.GoalY) * TerrainMap.CellSize);
                            query = query with { GoalX = cellX + Fixed.FromRaw((long)(random.Next() % (10L << 16))), GoalY = cellY + Fixed.FromRaw((long)(random.Next() % (10L << 16))) };
                        }
                        repeated++;
                    }
                    else
                    {
                        query = PerfRandomQuery(random, terrain, footprints);
                        asked.Add(query);
                    }
                    var expected = PerfAsk(reference, query);
                    var actual = PerfAsk(grid, query);
                    Check.That(SamePath(actual, expected), $"Map {map}, round {round}, query {index} ({query.StartX},{query.StartY}) -> ({query.GoalX},{query.GoalY}) {query.Surfaces}, {query.Blocks.Length} blockers: {PathText(actual)} instead of {PathText(expected)}.");
                    statuses[(int)expected.Status]++;
                    if (query.Blocks.Length > 0) withBlockers++;
                    if (grid.IsBlockedByObstacle(TerrainMap.CellOf(query.StartX), TerrainMap.CellOf(query.StartY)) || grid.IsBlockedByObstacle(TerrainMap.CellOf(query.GoalX), TerrainMap.CellOf(query.GoalY))) fromObstacle++;
                    compared++;
                }
            }
        }
        Check.That(compared == 24_000 && statuses[(int)PathStatus.Found] > 4_000 && statuses[(int)PathStatus.Partial] > 4_000 && statuses[(int)PathStatus.Blocked] > 200 && withBlockers > 4_000 && fromObstacle > 2_000 && repeated > 4_000,
            $"The comparison saw every kind of search ({compared}: found {statuses[0]}, partial {statuses[1]}, blocked {statuses[2]}, around blockers {withBlockers}, in a footprint {fromObstacle}, repeated {repeated}).");
    }

    /// <summary>A map with more cells than the search budget: a goal behind a closed wall makes the search stop at the budget, and the path it then returns depends on the exact order of the search.</summary>
    public static void PathSearchAtTheBudgetMatchesTheOriginalSearch()
    {
        var random = new PerfRandom(0x19_0004);
        var cliffs = new List<(int X, int Y)>();
        const int cellsX = 520, cellsY = 440;
        for (var y = 0; y < cellsY; y++) cliffs.Add((400, y));
        for (var scatter = 0; scatter < 6_000; scatter++) cliffs.Add((random.Below(cellsX), random.Below(cellsY)));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(cellsX, cellsY, 1, (_, _) => 0, cliffs.Distinct().Select(cell => (cell.X + 1, cell.Y + 1))), "perf-budget");
        var grid = new NavigationGrid(terrain);
        var reference = new ReferencePathfinder(grid);
        var atBudget = 0;
        for (var index = 0; index < 8; index++)
        {
            var startX = Fixed.FromInt(20 + random.Below(3_700));
            var startY = Fixed.FromInt(20 + random.Below(4_300));
            var goalX = Fixed.FromInt(4_100 + random.Below(1_000));
            var goalY = Fixed.FromInt(20 + random.Below(4_300));
            (Fixed X, Fixed Y, Fixed Radius)[] blocks = index % 2 == 0 ? [] : [(startX + Fixed.FromInt(40), startY, Fixed.FromInt(15))];
            var query = new PerfQuery(startX, startY, goalX, goalY, LocomotorSurfaces.Ground, blocks);
            var expected = PerfAsk(reference, query);
            if (reference.Expansions == 200_000) atBudget++;
            var actual = PerfAsk(grid, query);
            Check.That(SamePath(actual, expected), $"Budget query {index}: {PathText(actual)} instead of {PathText(expected)}.");
            Check.That(SamePath(PerfAsk(grid, query), expected), $"Budget query {index}, asked again: the same path.");
        }
        Check.That(atBudget >= 6, $"The searches ran into the budget ({atBudget} of 8).");
    }
}
