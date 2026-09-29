using System.Collections.Immutable;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Terrain;

public readonly record struct PathPoint(Fixed X, Fixed Y);

public enum PathStatus
{
    /// <summary>The path ends at the requested point.</summary>
    Found,
    /// <summary>The goal cannot be reached (blocked, walled off or beyond the search budget): the path ends at the closest reachable cell.</summary>
    Partial,
    /// <summary>No move is possible from the start cell.</summary>
    Blocked,
}

public sealed record PathResult(PathStatus Status, ImmutableArray<PathPoint> Points);

/// <summary>
/// Passability of the terrain cells for the ground locomotors plus the deterministic A* over them. One grid cell is a terrain
/// cell (<see cref="TerrainMap.CellSize"/> metres). Structure footprints are added on top of the static terrain classes when a
/// structure enters the world and removed when it leaves; <see cref="ObstacleSum"/> identifies the current obstacle set so a
/// path computed earlier can tell that it may be stale, independent of the order the changes happened in (a restored snapshot
/// rebuilds the same set and gets the same value).
/// <para>
/// The class priority per cell follows <c>AIPathfind.cpp</c> (<c>PathfindCell::setType</c>): obstacle, then cliff, then water, then
/// clear. Search, no corner cutting and the smoothing pass are rewritten, not copied: the reference searches a zone hierarchy with
/// per-frame cell budgets, this search is a flat A* with one fixed budget and no stochastic tie-break.
/// </para>
/// </summary>
public sealed class NavigationGrid
{
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;
    private const int ExpansionBudget = 200_000;
    private static readonly Fixed Inflate = Fixed.FromDecimal(TerrainMap.CellSize / 4m);

    private static readonly int[] StepX = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] StepY = [-1, 0, 1, 0, -1, 1, 1, -1];

    private readonly ushort[] _obstacles;
    private readonly int[] _cost;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private readonly int[] _closedStamp;
    private int _currentStamp;
    private long[] _heap = new long[256];
    private int _heapCount;

    public NavigationGrid(TerrainMap terrain)
    {
        Terrain = terrain;
        var cells = checked(terrain.CellsX * terrain.CellsY);
        _obstacles = new ushort[cells];
        _cost = new int[cells];
        _parent = new int[cells];
        _stamp = new int[cells];
        _closedStamp = new int[cells];
    }

    public TerrainMap Terrain { get; }

    /// <summary>Order-independent identity of the footprints currently on the grid.</summary>
    public ulong ObstacleSum { get; private set; }

    /// <summary>Which objects block the grid: <c>Pathfinder::classifyObjectFootprint</c> handles only immobile structures without small geometry.</summary>
    public static bool Blocks(ObjectTemplate template)
        => template.IsKindOf("STRUCTURE") && !template.IsKindOf("MOBILE") && !template.GeometryIsSmall;

    public void AddFootprint(ulong entityId, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        Apply(template, x, y, angle, 1);
        ObstacleSum = unchecked(ObstacleSum + Mix(entityId));
    }

    public void RemoveFootprint(ulong entityId, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        Apply(template, x, y, angle, -1);
        ObstacleSum = unchecked(ObstacleSum - Mix(entityId));
    }

    private static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    /// <summary>
    /// Marks the cells of the footprint: a cell counts when its centre lies inside the shape grown by a quarter cell, which makes any
    /// footprint at least one cell wide in every direction (a thinner one would let units slip between two cell centres), plus
    /// the cell that holds the object position itself. Cylinders and spheres use the major radius as a circle.
    /// </summary>
    private void Apply(ObjectTemplate template, Fixed x, Fixed y, Fixed angle, int delta)
    {
        var major = template.GeometryMajorRadius;
        var minor = template.GeometryMinorRadius;
        var box = template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase);
        var reach = (box ? major + minor : major) + Inflate;
        var x0 = Math.Max(0, TerrainMap.CellOf(x - reach));
        var x1 = Math.Min(Terrain.CellsX - 1, TerrainMap.CellOf(x + reach));
        var y0 = Math.Max(0, TerrainMap.CellOf(y - reach));
        var y1 = Math.Min(Terrain.CellsY - 1, TerrainMap.CellOf(y + reach));
        var cos = FixedTrig.Cos(angle);
        var sin = FixedTrig.Sin(angle);
        var halfLength = major + Inflate;
        var halfWidth = minor + Inflate;
        var radiusSquared = halfLength * halfLength;
        var own = (X: TerrainMap.CellOf(x), Y: TerrainMap.CellOf(y));
        for (var cy = y0; cy <= y1; cy++)
            for (var cx = x0; cx <= x1; cx++)
            {
                var dx = TerrainMap.CellCenter(cx) - x;
                var dy = TerrainMap.CellCenter(cy) - y;
                bool inside;
                if (box)
                {
                    var along = dx * cos + dy * sin;
                    var across = dy * cos - dx * sin;
                    inside = along.Abs() <= halfLength && across.Abs() <= halfWidth;
                }
                else inside = dx * dx + dy * dy <= radiusSquared;
                if (!inside && !(cx == own.X && cy == own.Y)) continue;
                var index = cy * Terrain.CellsX + cx;
                _obstacles[index] = checked((ushort)(_obstacles[index] + delta));
            }
    }

    public bool IsBlockedByObstacle(int cx, int cy) => Terrain.IsInside(cx, cy) && _obstacles[cy * Terrain.CellsX + cx] > 0;

    public bool IsPassable(int cx, int cy, LocomotorSurfaces surfaces)
    {
        if (!Terrain.IsInside(cx, cy)) return false;
        if (_obstacles[cy * Terrain.CellsX + cx] > 0) return false;
        return SurfaceAllows(Terrain.CellAt(cx, cy), surfaces);
    }

    /// <summary>Priority cliff, then water, then clear: a cliff cell that is also under water is still a cliff.</summary>
    public static bool SurfaceAllows(CellFlags flags, LocomotorSurfaces surfaces)
    {
        if ((flags & CellFlags.Cliff) != 0) return (surfaces & LocomotorSurfaces.Cliff) != 0;
        if ((flags & CellFlags.Water) != 0) return (surfaces & LocomotorSurfaces.Water) != 0;
        return (surfaces & LocomotorSurfaces.Ground) != 0;
    }

    /// <summary>
    /// A* over the eight-connected cell grid (costs 10 and 14, no corner cutting, ties broken by cell index). The start cell is
    /// exempt from the passability test so an object that a new obstacle covers can still walk out. If the goal cannot be
    /// reached (or is not passable) the path ends at the expanded cell closest to the goal by the octile distance
    /// (ties: lower cost, lower index).
    /// </summary>
    public PathResult FindPath(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces)
    {
        var start = ClampedCell(startX, startY);
        var goal = ClampedCell(goalX, goalY);
        var cellsX = Terrain.CellsX;
        var startIndex = start.Y * cellsX + start.X;
        var goalIndex = goal.Y * cellsX + goal.X;
        if (startIndex == goalIndex)
            return new PathResult(PathStatus.Found, [new PathPoint(goalX, goalY)]);

        if (++_currentStamp == int.MaxValue)
        {
            Array.Clear(_stamp);
            Array.Clear(_closedStamp);
            _currentStamp = 1;
        }
        _heapCount = 0;
        _cost[startIndex] = 0;
        _parent[startIndex] = -1;
        _stamp[startIndex] = _currentStamp;
        Push(Heuristic(start.X, start.Y, goal.X, goal.Y), startIndex);

        var best = startIndex;
        var bestHeuristic = Heuristic(start.X, start.Y, goal.X, goal.Y);
        var reached = false;
        for (var expansions = 0; _heapCount > 0 && expansions < ExpansionBudget; expansions++)
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
                if (!IsPassable(nx, ny, surfaces)) continue;
                var diagonal = direction >= 4;
                if (diagonal && (!IsPassable(cx + StepX[direction], cy, surfaces) || !IsPassable(cx, cy + StepY[direction], surfaces))) continue;
                var next = ny * cellsX + nx;
                if (_closedStamp[next] == _currentStamp) continue;
                var cost = _cost[current] + (diagonal ? DiagonalCost : StraightCost);
                if (_stamp[next] == _currentStamp && _cost[next] <= cost) continue;
                _stamp[next] = _currentStamp;
                _cost[next] = cost;
                _parent[next] = current;
                Push(cost + Heuristic(nx, ny, goal.X, goal.Y), next);
            }
        }

        var end = reached ? goalIndex : best;
        if (end == startIndex) return new PathResult(PathStatus.Blocked, []);

        var cells = new List<int>();
        for (var cell = end; cell >= 0; cell = _parent[cell]) cells.Add(cell);
        cells.Reverse();

        var points = ImmutableArray.CreateBuilder<PathPoint>();
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
        => (Math.Clamp(TerrainMap.CellOf(x), 0, Terrain.CellsX - 1), Math.Clamp(TerrainMap.CellOf(y), 0, Terrain.CellsY - 1));

    private static int Heuristic(int x, int y, int goalX, int goalY)
    {
        var dx = Math.Abs(x - goalX);
        var dy = Math.Abs(y - goalY);
        return StraightCost * (dx + dy) - (2 * StraightCost - DiagonalCost) * Math.Min(dx, dy);
    }

    /// <summary>Grid walk between two cells: every visited cell must be passable and a diagonal step needs both orthogonal neighbours.</summary>
    private bool LineClear(int fromIndex, int toIndex, LocomotorSurfaces surfaces)
    {
        var cellsX = Terrain.CellsX;
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
                if (!IsPassable(x + sx, y, surfaces) || !IsPassable(x, y + sy, surfaces)) return false;
                error += dx - dy;
                x += sx;
                y += sy;
            }
            else if (stepX) { error -= dy; x += sx; }
            else { error += dx; y += sy; }
            if (!IsPassable(x, y, surfaces)) return false;
        }
        return true;
    }

    // Min-heap of (cost << 32 | cell index): the index in the low word makes equal costs pop in ascending cell order.
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
