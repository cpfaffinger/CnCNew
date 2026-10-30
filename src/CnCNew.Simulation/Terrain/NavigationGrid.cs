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
/// <para>
/// The search keeps no state between calls that a result depends on. What it keeps (the per-cell search records, the heap, the buffers)
/// is scratch memory of one search; none of it is part of the world state, hashed or saved (docs/PERF-MOVEMENT.md).
/// </para>
/// </summary>
public sealed class NavigationGrid
{
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;
    private const int ExpansionBudget = 200_000;
    private const int EscapeRadius = 12;
    private const int GoalEscapeRadius = 8;
    private const int EscapePenalty = 4;
    private static readonly Fixed Inflate = Fixed.FromDecimal(TerrainMap.CellSize / 4m);

    private static readonly int[] StepX = [0, 1, 0, -1, 1, 1, -1, -1];
    private static readonly int[] StepY = [-1, 0, 1, 0, -1, 1, 1, -1];

    // One byte per cell: the terrain class in the two low bits (the CellFlags of the terrain, which never change), whether a footprint covers the cell, and two marks that live for one
    // search (the escape cells of that search and the cells under its temporary blockers). Whether a search may enter a cell is one table lookup on this byte.
    private const byte TerrainBits = (byte)(CellFlags.Cliff | CellFlags.Water);
    private const byte ObstacleBit = 4;
    private const byte EscapeBit = 8;
    private const byte TemporaryBit = 16;
    private const int CellStates = 32;

    /// <summary>What one search knows about a cell; valid when <see cref="Stamp"/> is the number of the running search.</summary>
    private struct SearchCell
    {
        public int Stamp;
        public int ClosedStamp;
        public int Cost;
        public int Parent;
    }

    private readonly int _cellsX;
    private readonly int _cellsY;
    private readonly ushort[] _obstacles;
    // For the attack's view (F-55): the sum of the ids of the footprints covering a cell - its owner when exactly one does, independent of the order they came in -
    // and how many of them cannot be seen through.
    private readonly ulong[] _obstacleIds;
    private readonly ushort[] _opaqueObstacles;
    private readonly byte[] _state;
    private SearchCell[] _search;
    private bool[] _open = new bool[CellStates];
    private int _currentStamp;
    private int[] _marked = new int[256];
    private int _markedCount;
    private int _escapeCount;
    private int _temporaryCount;
    private int[] _queue = new int[256];
    private int[] _pathCells = new int[256];
    private PathPoint[] _points = new PathPoint[32];
    // The open list: one heap of cell indices per total cost (see Push).
    private const int RingSize = 128;
    private int[][] _buckets = new int[RingSize][];
    private int[] _bucketCounts = new int[RingSize];
    private int _currentTotal;
    private int _queued;
    // The flood from the goal of a search that only wants a path that arrives (FindPathToGoal).
    private const int ReverseStride = 1024;
    private int[]? _reverseStamp;
    private int _reverseHead;
    private int _reverseTail;
    private int _reverseCells;

    // The test whether the closest cell found so far is the final answer (see ClosestCellIsFinal).
    private const int ClosestCheckFirst = 256;
    private const int ClosestCheckDistance = 160;
    private const int ClosestCheckFlood = 4096;
    private int[]? _closestSeen;
    private int _closestStamp;
    private int[] _closestQueue = new int[256];
    private int _closestChecks;
    private bool _closestFinal;

    // The results of the searches without temporary blockers since the footprints last changed. A search is a function of its start cell, its goal cell, the surfaces, the terrain
    // (which never changes) and the footprints: as long as no footprint was added or removed, the same question has the same answer, whoever asks and in whatever tick. Only the
    // last point of a path that reaches the goal is the asker's exact goal position instead of a cell centre; it is put in when an answer is handed out again.
    private const int MemoCapacity = 4096;
    private readonly record struct MemoKey(int StartCell, int GoalCell, LocomotorSurfaces Surfaces);
    private readonly record struct MemoEntry(PathResult Result, int Expansions, int Pushes, int EscapeCells, int PathCells);
    private readonly Dictionary<MemoKey, MemoEntry> _memo = [];
    private bool _memoStale;

    public NavigationGrid(TerrainMap terrain)
    {
        Terrain = terrain;
        _cellsX = terrain.CellsX;
        _cellsY = terrain.CellsY;
        var cells = checked(_cellsX * _cellsY);
        _obstacles = new ushort[cells];
        _obstacleIds = new ulong[cells];
        _opaqueObstacles = new ushort[cells];
        _state = new byte[cells];
        _search = new SearchCell[cells];
        for (var cy = 0; cy < _cellsY; cy++)
            for (var cx = 0; cx < _cellsX; cx++)
                _state[cy * _cellsX + cx] = (byte)((byte)terrain.CellAt(cx, cy) & TerrainBits);
    }

    public TerrainMap Terrain { get; }

    /// <summary>A tool's measuring hook (<see cref="IMovementProbe"/>); <see langword="null"/> unless a tool installs one. Not part of the state: neither hashed nor saved.</summary>
    public IMovementProbe? Probe { get; set; }

    /// <summary>Order-independent identity of the footprints currently on the grid.</summary>
    public ulong ObstacleSum { get; private set; }

    /// <summary>Which objects block the grid: <c>Pathfinder::classifyObjectFootprint</c> handles only immobile structures without small geometry.</summary>
    public static bool Blocks(ObjectTemplate template)
        => template.IsKindOf("STRUCTURE") && !template.IsKindOf("MOBILE") && !template.GeometryIsSmall;

    public void AddFootprint(ulong entityId, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        Apply(template, x, y, angle, 1, entityId);
        ObstacleSum = unchecked(ObstacleSum + Mix(entityId));
    }

    public void RemoveFootprint(ulong entityId, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        Apply(template, x, y, angle, -1, entityId);
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
    /// The cells of a footprint: a cell counts when its centre lies inside the shape grown by a quarter cell, which makes any
    /// footprint at least one cell wide in every direction (a thinner one would let units slip between two cell centres), plus
    /// the cell that holds the object position itself. Cylinders and spheres use the major radius as a circle.
    /// </summary>
    private readonly struct Footprint
    {
        private readonly Fixed _x, _y, _cos, _sin, _halfLength, _halfWidth;
        private readonly SquaredReach _radiusSquared;
        private readonly bool _box;
        private readonly int _ownX, _ownY;

        public Footprint(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
        {
            var major = template.GeometryMajorRadius;
            var minor = template.GeometryMinorRadius;
            _box = template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase);
            var reach = (_box ? major + minor : major) + Inflate;
            X0 = TerrainMap.CellOf(x - reach);
            X1 = TerrainMap.CellOf(x + reach);
            Y0 = TerrainMap.CellOf(y - reach);
            Y1 = TerrainMap.CellOf(y + reach);
            _x = x;
            _y = y;
            _cos = FixedTrig.Cos(angle);
            _sin = FixedTrig.Sin(angle);
            _halfLength = major + Inflate;
            _halfWidth = minor + Inflate;
            _radiusSquared = SquaredReach.Of(_halfLength);
            _ownX = TerrainMap.CellOf(x);
            _ownY = TerrainMap.CellOf(y);
        }

        public int X0 { get; }
        public int X1 { get; }
        public int Y0 { get; }
        public int Y1 { get; }

        public bool Contains(int cx, int cy)
        {
            if (cx == _ownX && cy == _ownY) return true;
            var dx = TerrainMap.CellCenter(cx) - _x;
            var dy = TerrainMap.CellCenter(cy) - _y;
            if (!_box) return _radiusSquared.Contains(dx * dx + dy * dy);
            var along = dx * _cos + dy * _sin;
            var across = dy * _cos - dx * _sin;
            return along.Abs() <= _halfLength && across.Abs() <= _halfWidth;
        }
    }

    private void Apply(ObjectTemplate template, Fixed x, Fixed y, Fixed angle, int delta, ulong entityId)
    {
        var footprint = new Footprint(template, x, y, angle);
        var opaque = template.IsKindOf("CAN_SEE_THROUGH_STRUCTURE") ? 0 : delta;
        var x0 = Math.Max(0, footprint.X0);
        var x1 = Math.Min(Terrain.CellsX - 1, footprint.X1);
        var y0 = Math.Max(0, footprint.Y0);
        var y1 = Math.Min(Terrain.CellsY - 1, footprint.Y1);
        for (var cy = y0; cy <= y1; cy++)
            for (var cx = x0; cx <= x1; cx++)
            {
                if (!footprint.Contains(cx, cy)) continue;
                var index = cy * Terrain.CellsX + cx;
                var count = checked((ushort)(_obstacles[index] + delta));
                _obstacles[index] = count;
                _obstacleIds[index] = unchecked(_obstacleIds[index] + (ulong)(long)delta * entityId);
                _opaqueObstacles[index] = checked((ushort)(_opaqueObstacles[index] + opaque));
                _state[index] = count > 0 ? (byte)(_state[index] | ObstacleBit) : (byte)(_state[index] & ~ObstacleBit);
            }
        _memoStale = true;
    }

    /// <summary>No footprint of another object under the template's: the terrain under it is a question of <see cref="BuildSiteTerrain"/>.</summary>
    public bool IsFootprintClear(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        var footprint = new Footprint(template, x, y, angle);
        for (var cy = footprint.Y0; cy <= footprint.Y1; cy++)
            for (var cx = footprint.X0; cx <= footprint.X1; cx++)
                if (Terrain.IsInside(cx, cy) && _obstacles[cy * Terrain.CellsX + cx] > 0 && footprint.Contains(cx, cy)) return false;
        return true;
    }

    public bool IsBlockedByObstacle(int cx, int cy) => Terrain.IsInside(cx, cy) && _obstacles[cy * Terrain.CellsX + cx] > 0;

    /// <summary>
    /// The obstacle part of <c>Pathfinder::isAttackViewBlockedByObstacle</c> (<c>iterateCellsAlongLine</c> with <c>attackBlockedByObstacleCallback</c>): a 4-connected
    /// Bresenham walk over the cells from the one under the attacker to the one under the victim, both included, blocked at the first cell a structure covers that
    /// cannot be seen through (<c>CAN_SEE_THROUGH_STRUCTURE</c>) unless the structure is one of <paramref name="excepted"/> (the attacker, the victim, either one's slaver,
    /// the attacker's container) or the one that covers the victim's cell. A cliff or water cell under a structure never blocks (<c>setTypeAsObstacle</c> leaves such a
    /// cell as it is). Where footprints overlap, the cell's owner is the set of them (the original keeps the first that claimed it): a cell of several blocks unless it is
    /// covered by the same set as the victim's cell, and it passes as transparent only when all of them are.
    /// </summary>
    public bool IsAttackLineBlocked(Fixed fromX, Fixed fromY, Fixed toX, Fixed toY, ReadOnlySpan<ulong> excepted)
    {
        var x = Math.Clamp(TerrainMap.CellOf(fromX), 0, Terrain.CellsX - 1);
        var y = Math.Clamp(TerrainMap.CellOf(fromY), 0, Terrain.CellsY - 1);
        var endX = Math.Clamp(TerrainMap.CellOf(toX), 0, Terrain.CellsX - 1);
        var endY = Math.Clamp(TerrainMap.CellOf(toY), 0, Terrain.CellsY - 1);
        var victimCell = endY * Terrain.CellsX + endX;
        var deltaX = Math.Abs(endX - x);
        var deltaY = Math.Abs(endY - y);
        var stepX = endX >= x ? 1 : -1;
        var stepY = endY >= y ? 1 : -1;
        var (majorX, majorY, minorX, minorY, denominator, add) = deltaX >= deltaY
            ? (stepX, 0, 0, stepY, deltaX, deltaY)
            : (0, stepY, stepX, 0, deltaY, deltaX);
        var numerator = denominator / 2;
        for (var step = 0; step <= denominator; step++)
        {
            if (BlocksView(y * Terrain.CellsX + x, victimCell, excepted)) return true;
            numerator += add;
            if (numerator >= denominator)
            {
                // The minor step is a cell of its own: the walk never passes a corner diagonally.
                numerator -= denominator;
                x += minorX;
                y += minorY;
                if (BlocksView(y * Terrain.CellsX + x, victimCell, excepted)) return true;
            }
            x += majorX;
            y += majorY;
        }
        return false;
    }

    private bool BlocksView(int index, int victimCell, ReadOnlySpan<ulong> excepted)
    {
        if (_opaqueObstacles[index] == 0) return false;
        if ((_state[index] & TerrainBits) != 0) return false;
        if (_obstacles[index] == 1 && excepted.Contains(_obstacleIds[index])) return false;
        return _obstacles[victimCell] == 0 || _obstacles[index] != _obstacles[victimCell] || _obstacleIds[index] != _obstacleIds[victimCell];
    }

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

    // ------------------------------------------------------------------------------------------------------- one search

    /// <summary>
    /// Fills the table that says for every cell state whether this search may enter the cell: a passable cell (the surface allows its terrain class and no footprint covers it) that is
    /// not under a temporary blocker, or an escape cell.
    /// </summary>
    private void PrepareOpenTable(LocomotorSurfaces surfaces)
    {
        for (var state = 0; state < CellStates; state++)
        {
            var passable = (state & ObstacleBit) == 0 && SurfaceAllows((CellFlags)(state & TerrainBits), surfaces);
            _open[state] = (passable && (state & TemporaryBit) == 0) || (state & EscapeBit) != 0;
        }
    }

    private bool IsOpen(int cx, int cy) => (uint)cx < (uint)_cellsX && (uint)cy < (uint)_cellsY && _open[_state[cy * _cellsX + cx]];

    private void Mark(int index, byte bit)
    {
        if ((_state[index] & (EscapeBit | TemporaryBit)) == 0)
        {
            if (_markedCount == _marked.Length) Array.Resize(ref _marked, _marked.Length * 2);
            _marked[_markedCount++] = index;
        }
        _state[index] |= bit;
    }

    private void ClearMarks()
    {
        for (var index = 0; index < _markedCount; index++) _state[_marked[index]] &= unchecked((byte)~(EscapeBit | TemporaryBit));
        _markedCount = 0;
        _escapeCount = 0;
        _temporaryCount = 0;
    }

    /// <summary>The cells under the given circles, except those next to the start and the goal cell, become closed for the search that follows.</summary>
    private void MarkTemporary(int startX, int startY, int goalX, int goalY, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> temporaryBlocks)
    {
        for (var block = 0; block < temporaryBlocks.Count; block++)
        {
            var (x, y, radius) = temporaryBlocks[block];
            var centreX = TerrainMap.CellOf(x);
            var centreY = TerrainMap.CellOf(y);
            var reach = TerrainMap.CellOf(radius) + 1;
            var radiusSquared = SquaredReach.Of(radius);
            for (var cy = centreY - reach; cy <= centreY + reach; cy++)
                for (var cx = centreX - reach; cx <= centreX + reach; cx++)
                {
                    if (!Terrain.IsInside(cx, cy) || Math.Abs(cx - startX) <= 1 && Math.Abs(cy - startY) <= 1 || Math.Abs(cx - goalX) <= 1 && Math.Abs(cy - goalY) <= 1) continue;
                    var ddx = TerrainMap.CellCenter(cx) - x;
                    var ddy = TerrainMap.CellCenter(cy) - y;
                    if (!radiusSquared.Contains(ddx * ddx + ddy * ddy)) continue;
                    var index = cy * _cellsX + cx;
                    if ((_state[index] & TemporaryBit) != 0) continue;
                    Mark(index, TemporaryBit);
                    _temporaryCount++;
                }
        }
    }

    /// <summary>
    /// An object that stands inside a footprint (a unit that just left a factory whose approximated footprint covers its exit), or
    /// that is sent to a point inside one (a dock position of a supply center), may pass through the obstacle cells connected to that
    /// cell, within <paramref name="radius"/> cells, at a penalty per cell so that free ground is preferred. The reference lets such
    /// objects in and out because exits and dock points lie on the real geometry's edge, which the footprint approximation overshoots.
    /// </summary>
    private void CollectEscapeCells(int startX, int startY, int radius, LocomotorSurfaces surfaces)
    {
        var cellsX = _cellsX;
        if (!Terrain.IsInside(startX, startY) || _obstacles[startY * cellsX + startX] == 0) return;
        var head = 0;
        var tail = 0;
        var first = startY * cellsX + startX;
        if ((_state[first] & EscapeBit) == 0)
        {
            Mark(first, EscapeBit);
            _escapeCount++;
        }
        Enqueue(ref tail, first);
        while (head < tail)
        {
            var cell = _queue[head++];
            var x = cell % cellsX;
            var y = cell / cellsX;
            for (var direction = 0; direction < 8; direction++)
            {
                var nx = x + StepX[direction];
                var ny = y + StepY[direction];
                if (Math.Abs(nx - startX) > radius || Math.Abs(ny - startY) > radius || !Terrain.IsInside(nx, ny)) continue;
                var index = ny * cellsX + nx;
                if (_obstacles[index] == 0 || !SurfaceAllows((CellFlags)(_state[index] & TerrainBits), surfaces) || (_state[index] & EscapeBit) != 0) continue;
                Mark(index, EscapeBit);
                _escapeCount++;
                Enqueue(ref tail, index);
            }
        }
    }

    private void Enqueue(ref int tail, int cell)
    {
        if (tail == _queue.Length) Array.Resize(ref _queue, _queue.Length * 2);
        _queue[tail++] = cell;
    }

    /// <summary>
    /// <see cref="FindPath(Fixed, Fixed, Fixed, Fixed, LocomotorSurfaces)"/> that also treats the cells under the given circles as blocked for this one search (units that
    /// stand still where a mover's path leads: the static obstacle set does not know them). The cells next to the start and the goal stay open, so a mover that is
    /// touching a blocker, or a goal beside one, is not walled in.
    /// </summary>
    public PathResult FindPath(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> temporaryBlocks)
        => Run(startX, startY, goalX, goalY, surfaces, temporaryBlocks, false)!;

    /// <summary>
    /// The path of <see cref="FindPath(Fixed, Fixed, Fixed, Fixed, LocomotorSurfaces, IReadOnlyList{ValueTuple{Fixed, Fixed, Fixed}})"/> if it reaches the goal
    /// (<see cref="PathStatus.Found"/>), else <see langword="null"/>: for a caller that has no use for a path that ends short.
    /// <para>
    /// The answer is the same as running that search and looking at its status. What differs is the work when the goal cannot be reached at all: the plain search then expands every
    /// cell it can reach, up to its budget, only to report a partial path the caller drops. Here a flood from the goal runs alongside (one cell per expanded cell, in strides); it
    /// walks the same moves backwards, and when it runs dry without having met the start, no path exists and the search stops. A goal cell that is not open ends it before it starts.
    /// </para>
    /// </summary>
    public PathResult? FindPathToGoal(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> temporaryBlocks)
        => Run(startX, startY, goalX, goalY, surfaces, temporaryBlocks, true) is { Status: PathStatus.Found } found ? found : null;

    /// <summary>
    /// A* over the eight-connected cell grid (costs 10 and 14, no corner cutting, ties broken by cell index). The start cell is
    /// exempt from the passability test so an object that a new obstacle covers can still walk out. If the goal cannot be
    /// reached (or is not passable) the path ends at the expanded cell closest to the goal by the octile distance
    /// (ties: lower cost, lower index).
    /// </summary>
    public PathResult FindPath(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces)
        => Run(startX, startY, goalX, goalY, surfaces, null, false)!;

    /// <summary>One search. With <paramref name="arrivingOnly"/> the result is <see langword="null"/> when the goal was proved unreachable before the search ended.</summary>
    private PathResult? Run(Fixed startX, Fixed startY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)>? temporaryBlocks, bool arrivingOnly)
    {
        var start = ClampedCell(startX, startY);
        var goal = ClampedCell(goalX, goalY);
        var startIndex = start.Y * _cellsX + start.X;
        var goalIndex = goal.Y * _cellsX + goal.X;
        var probe = Probe;
        probe?.Started(MovementWork.PathSearch);
        // Remembered answers: not for a search around temporary blockers (their cells are part of the question) and not for the trivial one inside a single cell.
        var remember = (temporaryBlocks is null || temporaryBlocks.Count == 0) && startIndex != goalIndex;
        var key = new MemoKey(startIndex, goalIndex, surfaces);
        if (remember)
        {
            if (_memoStale)
            {
                _memo.Clear();
                _memoStale = false;
            }
            if (_memo.TryGetValue(key, out var known))
            {
                var answer = known.Result;
                if (answer.Status == PathStatus.Found && answer.Points[^1] != new PathPoint(goalX, goalY))
                    answer = new PathResult(PathStatus.Found, answer.Points.SetItem(answer.Points.Length - 1, new PathPoint(goalX, goalY)));
                if (probe is not null)
                {
                    probe.Ended(MovementWork.PathSearch);
                    probe.PathSearched(new PathSearchInfo(startIndex, goalIndex, surfaces, answer.Status, known.Expansions, known.Pushes, known.EscapeCells, 0, known.PathCells, answer.Points.Length, true));
                }
                return answer;
            }
        }

        _probeExpansions = _probePushes = _probePathCells = _probeEndDistance = 0;
        _reverseCells = 0;
        _closestChecks = 0;
        _closestFinal = false;
        PathResult? result;
        var escapeCells = 0;
        var temporaryCells = 0;
        try
        {
            if (temporaryBlocks is not null) MarkTemporary(start.X, start.Y, goal.X, goal.Y, temporaryBlocks);
            temporaryCells = _temporaryCount;
            result = Search(start.X, start.Y, goal.X, goal.Y, goalX, goalY, surfaces, arrivingOnly);
            escapeCells = _escapeCount;
        }
        finally
        {
            ClearMarks();
        }
        if (remember && result is not null)
        {
            if (_memo.Count >= MemoCapacity) _memo.Clear();
            _memo[key] = new MemoEntry(result, _probeExpansions, _probePushes, escapeCells, _probePathCells);
        }
        if (probe is not null)
        {
            probe.Ended(MovementWork.PathSearch);
            probe.PathSearched(new PathSearchInfo(startIndex, goalIndex, surfaces, result?.Status ?? PathStatus.Partial, _probeExpansions, _probePushes, escapeCells, temporaryCells, _probePathCells,
                result?.Points.Length ?? 0, false, result is null, _reverseCells, _probeEndDistance, _closestFinal, _closestChecks));
        }
        return result;
    }

    private int _probeExpansions;
    private int _probePushes;
    private int _probePathCells;
    private int _probeEndDistance;

    private PathResult? Search(int startCellX, int startCellY, int goalCellX, int goalCellY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, bool arrivingOnly)
    {
        if (BeginSearch(startCellX, startCellY, goalCellX, goalCellY, goalX, goalY, surfaces, arrivingOnly, out var early)) return early;
        ContinueSearch(int.MaxValue);
        return FinishSearch();
    }

    // The state of the running search between its slices (BeginSearch, ContinueSearch, FinishSearch). A search that runs in one piece passes through the same steps with the same
    // values: the slices only decide where it pauses, never what it does.
    private int _sStartIndex;
    private int _sGoalIndex;
    private int _sGoalCellX;
    private int _sGoalCellY;
    private Fixed _sGoalX;
    private Fixed _sGoalY;
    private int _sStamp;
    private bool _sReverse;
    private int _sBest;
    private int _sBestHeuristic;
    private int _sBestCost;
    private bool _sReached;
    private int _sExpansions;
    private int _sNextClosestCheck;
    // How the loop ended: 0 running, 1 ended (queue empty, budget, closest cell final or goal reached), -1 the goal was proved unreachable (no result).
    private int _sOutcome;

    /// <summary>The search's set-up; <see langword="true"/> with <paramref name="early"/> when it is decided before the loop (start in the goal cell, a goal the search may not enter).</summary>
    private bool BeginSearch(int startCellX, int startCellY, int goalCellX, int goalCellY, Fixed goalX, Fixed goalY, LocomotorSurfaces surfaces, bool arrivingOnly, out PathResult? early)
    {
        early = null;
        var cellsX = _cellsX;
        var startIndex = startCellY * cellsX + startCellX;
        var goalIndex = goalCellY * cellsX + goalCellX;
        if (startIndex == goalIndex)
        {
            early = new PathResult(PathStatus.Found, [new PathPoint(goalX, goalY)]);
            return true;
        }

        if (++_currentStamp == int.MaxValue)
        {
            Array.Clear(_search);
            _currentStamp = 1;
        }
        var stamp = _currentStamp;
        Array.Clear(_bucketCounts);
        _queued = 0;
        _currentTotal = Heuristic(startCellX, startCellY, goalCellX, goalCellY);
        CollectEscapeCells(startCellX, startCellY, EscapeRadius, surfaces);
        CollectEscapeCells(goalCellX, goalCellY, GoalEscapeRadius, surfaces);
        PrepareOpenTable(surfaces);
        var search = _search;
        // A search that only counts when it arrives: the goal must be a cell the search may enter, and the flood from the goal must not run dry (see FindPathToGoal).
        var reverse = arrivingOnly;
        if (reverse)
        {
            if (!_open[_state[goalIndex]]) return true;
            _reverseStamp ??= new int[search.Length];
            _reverseStamp[goalIndex] = stamp;
            _reverseHead = _reverseTail = 0;
            Enqueue(ref _reverseTail, goalIndex);
        }
        ref var startCell = ref search[startIndex];
        startCell.Cost = 0;
        startCell.Parent = -1;
        startCell.Stamp = stamp;
        Push(Heuristic(startCellX, startCellY, goalCellX, goalCellY), startIndex);

        _sStartIndex = startIndex;
        _sGoalIndex = goalIndex;
        _sGoalCellX = goalCellX;
        _sGoalCellY = goalCellY;
        _sGoalX = goalX;
        _sGoalY = goalY;
        _sStamp = stamp;
        _sReverse = reverse;
        _sBest = startIndex;
        _sBestHeuristic = Heuristic(startCellX, startCellY, goalCellX, goalCellY);
        _sBestCost = 0;
        _sReached = false;
        _sExpansions = 0;
        _sNextClosestCheck = ClosestCheckFirst;
        _sOutcome = 0;
        return false;
    }

    /// <summary>
    /// Runs the loop of the search until it ends or until it has counted <paramref name="stopAt"/> expansions (a cell taken from the open list, the budget's unit); returns whether it
    /// ended. A search paused at a count and continued later does exactly what a search run in one piece does from that count on.
    /// </summary>
    private bool ContinueSearch(int stopAt)
    {
        var cellsX = _cellsX;
        var cellsY = _cellsY;
        var open = _open;
        var state = _state;
        var search = _search;
        var startIndex = _sStartIndex;
        var goalIndex = _sGoalIndex;
        var goalCellX = _sGoalCellX;
        var goalCellY = _sGoalCellY;
        var stamp = _sStamp;
        var reverse = _sReverse;
        var best = _sBest;
        var bestHeuristic = _sBestHeuristic;
        var bestCost = _sBestCost;
        var reached = false;
        var expansions = _sExpansions;
        var nextClosestCheck = _sNextClosestCheck;
        var ended = true;
        // A cell taken from the heap a second time (it was pushed again with a lower cost before it was expanded) counts toward the budget like an expansion; it always did.
        for (; _queued > 0 && expansions < ExpansionBudget; expansions++)
        {
            if (expansions >= stopAt) { ended = false; break; }
            if (expansions == nextClosestCheck)
            {
                // The search has not arrived and its closest cell lies near the goal: if nothing it could still expand can be closer, that cell is the answer already. Every cell
                // still to come has at least the total of the next one; with the same distance to the goal as the closest cell it then has a higher cost and loses the tie.
                nextClosestCheck <<= 1;
                if (bestHeuristic <= ClosestCheckDistance)
                {
                    while (_bucketCounts[_currentTotal & (RingSize - 1)] == 0) _currentTotal++;
                    if (_currentTotal > bestCost + bestHeuristic && ClosestCellIsFinal(goalCellX, goalCellY, bestHeuristic, stamp))
                    {
                        _closestFinal = true;
                        break;
                    }
                }
            }
            if (reverse && (expansions & (ReverseStride - 1)) == ReverseStride - 1)
            {
                var verdict = AdvanceReverse(startIndex, stamp, ReverseStride);
                if (verdict < 0)
                {
                    _probeExpansions = expansions;
                    _sExpansions = expansions;
                    _sOutcome = -1;
                    return true;
                }
                if (verdict > 0) reverse = false;
            }
            var current = Pop();
            ref var cell = ref search[current];
            if (cell.ClosedStamp == stamp) continue;
            cell.ClosedStamp = stamp;
            if (current == goalIndex) { reached = true; break; }
            var cy = current / cellsX;
            var cx = current - cy * cellsX;
            var currentCost = cell.Cost;
            var heuristic = Heuristic(cx, cy, goalCellX, goalCellY);
            if (heuristic < bestHeuristic || (heuristic == bestHeuristic && (currentCost < bestCost || (currentCost == bestCost && current < best))))
            {
                best = current;
                bestHeuristic = heuristic;
                bestCost = currentCost;
            }

            // The four straight neighbours first (north, east, south, west), then the diagonals, each of which needs both straight neighbours it passes between.
            var north = cy > 0 && open[state[current - cellsX]];
            var east = cx < cellsX - 1 && open[state[current + 1]];
            var south = cy < cellsY - 1 && open[state[current + cellsX]];
            var west = cx > 0 && open[state[current - 1]];
            if (north) Relax(search, state, stamp, current, current - cellsX, currentCost, StraightCost, cx, cy - 1, goalCellX, goalCellY);
            if (east) Relax(search, state, stamp, current, current + 1, currentCost, StraightCost, cx + 1, cy, goalCellX, goalCellY);
            if (south) Relax(search, state, stamp, current, current + cellsX, currentCost, StraightCost, cx, cy + 1, goalCellX, goalCellY);
            if (west) Relax(search, state, stamp, current, current - 1, currentCost, StraightCost, cx - 1, cy, goalCellX, goalCellY);
            if (east && north && open[state[current - cellsX + 1]]) Relax(search, state, stamp, current, current - cellsX + 1, currentCost, DiagonalCost, cx + 1, cy - 1, goalCellX, goalCellY);
            if (east && south && open[state[current + cellsX + 1]]) Relax(search, state, stamp, current, current + cellsX + 1, currentCost, DiagonalCost, cx + 1, cy + 1, goalCellX, goalCellY);
            if (west && south && open[state[current + cellsX - 1]]) Relax(search, state, stamp, current, current + cellsX - 1, currentCost, DiagonalCost, cx - 1, cy + 1, goalCellX, goalCellY);
            if (west && north && open[state[current - cellsX - 1]]) Relax(search, state, stamp, current, current - cellsX - 1, currentCost, DiagonalCost, cx - 1, cy - 1, goalCellX, goalCellY);
        }

        _sReverse = reverse;
        _sBest = best;
        _sBestHeuristic = bestHeuristic;
        _sBestCost = bestCost;
        _sReached = reached;
        _sExpansions = expansions;
        _sNextClosestCheck = nextClosestCheck;
        if (ended) _sOutcome = 1;
        return ended;
    }

    /// <summary>The result of an ended search: the path to the goal or to the closest cell, or <see langword="null"/> when the goal was proved unreachable.</summary>
    private PathResult? FinishSearch()
    {
        if (_sOutcome < 0) return null;
        var cellsX = _cellsX;
        var search = _search;
        var reached = _sReached;
        var startIndex = _sStartIndex;
        _probeExpansions = _sExpansions;
        _probeEndDistance = reached ? 0 : _sBestHeuristic;
        var end = reached ? _sGoalIndex : _sBest;
        if (end == startIndex) return new PathResult(PathStatus.Blocked, []);

        // The cells from the end back to the start, then walked forward: a waypoint is the last cell of the chain that a straight grid walk from the previous waypoint still reaches.
        var count = 0;
        for (var cell = end; cell >= 0; cell = search[cell].Parent)
        {
            if (count == _pathCells.Length) Array.Resize(ref _pathCells, _pathCells.Length * 2);
            _pathCells[count++] = cell;
        }
        _probePathCells = count;
        var cells = _pathCells;
        var points = 0;
        var from = count - 1;
        while (from > 0)
        {
            var to = from - 1;
            while (to > 0 && LineClear(cells[from], cells[to - 1])) to--;
            if (points == _points.Length) Array.Resize(ref _points, _points.Length * 2);
            _points[points++] = new PathPoint(TerrainMap.CellCenter(cells[to] % cellsX), TerrainMap.CellCenter(cells[to] / cellsX));
            from = to;
        }
        if (reached) _points[points - 1] = new PathPoint(_sGoalX, _sGoalY);
        return new PathResult(reached ? PathStatus.Found : PathStatus.Partial, ImmutableArray.Create(_points, 0, points));
    }

    /// <summary>
    /// Whether no cell closer to the goal than the closest cell found so far (octile distance <paramref name="closest"/>) can be reached at all. Then the search can only confirm what it
    /// has: it would expand the rest of what it can reach, up to its budget, without arriving and without finding a closer cell, and return the path to that cell. That is the usual
    /// fate of an order to a place nobody can stand on (a cliff, water, a blocked cell): the path ends next to it, and the remaining search is a flood of the whole map.
    /// <para>
    /// The cells closer than <paramref name="closest"/> form a small block around the goal. The test floods from the open ones among them, backwards along the moves of the search
    /// (see <see cref="AdvanceReverse"/>). If it meets a cell this search has queued or expanded, a closer cell can be reached and the search has to go on; the same if the flood
    /// grows beyond <see cref="ClosestCheckFlood"/> cells. If it runs dry, every path from the start to a closer cell would have to pass a cell of the flood, and none does.
    /// </para>
    /// </summary>
    private bool ClosestCellIsFinal(int goalCellX, int goalCellY, int closest, int stamp)
    {
        _closestChecks++;
        var cellsX = _cellsX;
        var cellsY = _cellsY;
        var open = _open;
        var state = _state;
        var search = _search;
        var seen = _closestSeen ??= new int[search.Length];
        if (++_closestStamp == int.MaxValue)
        {
            Array.Clear(seen);
            _closestStamp = 1;
        }
        var mark = _closestStamp;
        var tail = 0;
        // 10 * max(dx, dy) + 4 * min(dx, dy) < closest needs max(dx, dy) <= (closest - 1) / 10.
        var radius = (closest - 1) / 10;
        for (var y = Math.Max(0, goalCellY - radius); y <= Math.Min(cellsY - 1, goalCellY + radius); y++)
            for (var x = Math.Max(0, goalCellX - radius); x <= Math.Min(cellsX - 1, goalCellX + radius); x++)
            {
                if (Heuristic(x, y, goalCellX, goalCellY) >= closest) continue;
                var index = y * cellsX + x;
                if (!open[state[index]]) continue;
                if (search[index].Stamp == stamp) return false;
                seen[index] = mark;
                if (tail == _closestQueue.Length) Array.Resize(ref _closestQueue, _closestQueue.Length * 2);
                _closestQueue[tail++] = index;
            }
        for (var head = 0; head < tail; head++)
        {
            if (head >= ClosestCheckFlood) return false;
            var cell = _closestQueue[head];
            var y = cell / cellsX;
            var x = cell - y * cellsX;
            for (var direction = 0; direction < 8; direction++)
            {
                var nx = x + StepX[direction];
                var ny = y + StepY[direction];
                if ((uint)nx >= (uint)cellsX || (uint)ny >= (uint)cellsY) continue;
                if (direction >= 4 && (!open[state[y * cellsX + nx]] || !open[state[ny * cellsX + x]])) continue;
                var from = ny * cellsX + nx;
                // A cell of this search (the start included, open or not) that can step into the flood: a closer cell can be reached.
                if (search[from].Stamp == stamp) return false;
                if (seen[from] == mark || !open[state[from]]) continue;
                seen[from] = mark;
                if (tail == _closestQueue.Length) Array.Resize(ref _closestQueue, _closestQueue.Length * 2);
                _closestQueue[tail++] = from;
            }
        }
        return true;
    }

    /// <summary>
    /// Continues the flood from the goal by up to <paramref name="budget"/> cells: every cell from which the goal can be reached by the moves of the search (into an open cell; a
    /// diagonal move only between two open cells). Those moves are the same in both directions, except that the start cell need not be open itself. Returns 1 when the start is among
    /// those cells (a path exists), -1 when the flood is complete without it (none exists), 0 when it is not done yet.
    /// </summary>
    private int AdvanceReverse(int startIndex, int stamp, int budget)
    {
        var cellsX = _cellsX;
        var cellsY = _cellsY;
        var open = _open;
        var state = _state;
        var seen = _reverseStamp!;
        while (_reverseHead < _reverseTail && budget-- > 0)
        {
            var cell = _queue[_reverseHead++];
            _reverseCells++;
            var y = cell / cellsX;
            var x = cell - y * cellsX;
            for (var direction = 0; direction < 8; direction++)
            {
                var nx = x + StepX[direction];
                var ny = y + StepY[direction];
                if ((uint)nx >= (uint)cellsX || (uint)ny >= (uint)cellsY) continue;
                // A diagonal move passes between the two cells that share a side with both ends.
                if (direction >= 4 && (!open[state[y * cellsX + nx]] || !open[state[ny * cellsX + x]])) continue;
                var from = ny * cellsX + nx;
                if (from == startIndex) return 1;
                if (seen[from] == stamp || !open[state[from]]) continue;
                seen[from] = stamp;
                Enqueue(ref _reverseTail, from);
            }
        }
        return _reverseHead == _reverseTail ? -1 : 0;
    }

    /// <summary>One step of the search from <paramref name="current"/> to its open neighbour <paramref name="next"/>: records the cheaper way and queues the neighbour.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void Relax(SearchCell[] search, byte[] state, int stamp, int current, int next, int currentCost, int stepCost, int nx, int ny, int goalCellX, int goalCellY)
    {
        ref var cell = ref search[next];
        if (cell.ClosedStamp == stamp) return;
        var cost = currentCost + ((state[next] & EscapeBit) != 0 ? stepCost * EscapePenalty : stepCost);
        if (cell.Stamp == stamp && cell.Cost <= cost) return;
        cell.Stamp = stamp;
        cell.Cost = cost;
        cell.Parent = current;
        Push(cost + Heuristic(nx, ny, goalCellX, goalCellY), next);
    }

    private (int X, int Y) ClampedCell(Fixed x, Fixed y)
        => (Math.Clamp(TerrainMap.CellOf(x), 0, Terrain.CellsX - 1), Math.Clamp(TerrainMap.CellOf(y), 0, Terrain.CellsY - 1));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int Heuristic(int x, int y, int goalX, int goalY)
    {
        var dx = Math.Abs(x - goalX);
        var dy = Math.Abs(y - goalY);
        return StraightCost * (dx + dy) - (2 * StraightCost - DiagonalCost) * Math.Min(dx, dy);
    }

    /// <summary>Grid walk between two cells: every visited cell must be passable and a diagonal step needs both orthogonal neighbours.</summary>
    private bool LineClear(int fromIndex, int toIndex)
    {
        var cellsX = _cellsX;
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
                if (!IsOpen(x + sx, y) || !IsOpen(x, y + sy)) return false;
                error += dx - dy;
                x += sx;
                y += sy;
            }
            else if (stepX) { error -= dy; x += sx; }
            else { error += dx; y += sy; }
            if (!IsOpen(x, y)) return false;
        }
        return true;
    }

    // The open list hands out the queued cell with the lowest total cost (cost so far plus the octile distance to the goal) and, among equal totals, the lowest cell index: the order
    // of a min-heap over (total << 32 | cell index), which is what this was. That order depends on the queued values alone, not on how they are stored, so they are stored by total:
    // the totals of the queued cells never lie below the total of the cell being expanded (the octile distance never drops by more than the cost of the step) and never more than 70
    // above it (the dearest step, a diagonal onto an escape cell, costs 56, and the distance grows by at most 14). A ring of 128 heaps of cell indices, indexed by the total modulo
    // 128, therefore holds one total per heap; the search works through them in ascending order. A total outside that window would be a broken search and stops it.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void Push(int total, int index)
    {
        _probePushes++;
        if ((uint)(total - _currentTotal) >= RingSize) throw new InvalidOperationException("Path search: a total cost outside the window of the open list.");
        var slot = total & (RingSize - 1);
        var heap = _buckets[slot] ??= new int[64];
        var child = _bucketCounts[slot];
        if (child == heap.Length)
        {
            Array.Resize(ref heap, heap.Length * 2);
            _buckets[slot] = heap;
        }
        _bucketCounts[slot] = child + 1;
        _queued++;
        while (child > 0)
        {
            var parent = (child - 1) >> 1;
            if (heap[parent] <= index) break;
            heap[child] = heap[parent];
            child = parent;
        }
        heap[child] = index;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int Pop()
    {
        var counts = _bucketCounts;
        while (counts[_currentTotal & (RingSize - 1)] == 0) _currentTotal++;
        var slot = _currentTotal & (RingSize - 1);
        var heap = _buckets[slot];
        var top = heap[0];
        var count = --counts[slot];
        _queued--;
        var last = heap[count];
        if (count > 0)
        {
            var parent = 0;
            while (true)
            {
                var child = 2 * parent + 1;
                if (child >= count) break;
                if (child + 1 < count && heap[child + 1] < heap[child]) child++;
                if (heap[child] >= last) break;
                heap[parent] = heap[child];
                parent = child;
            }
            heap[parent] = last;
        }
        return top;
    }

    // ------------------------------------------------------------------------------------------------------- a search in slices (the movement profile)

    /// <summary>
    /// The working memory of a search that pauses between ticks (<see cref="Slice"/>). The grid's own fields are the memory of whatever search runs now; a sliced search swaps
    /// its memory in for its slice and out again, so a search run in one piece in between (another unit's, the AI's reachability test) finds its usual memory untouched.
    /// </summary>
    private sealed class SearchMemory
    {
        public SearchCell[] Search = [];
        public int CurrentStamp;
        public bool[] Open = new bool[CellStates];
        public int[] Marked = new int[256];
        public int MarkedCount;
        public int EscapeCount;
        public int TemporaryCount;
        public byte[] MarkedBits = new byte[256];
        public int[] Queue = new int[256];
        public int[] PathCells = new int[256];
        public PathPoint[] Points = new PathPoint[32];
        public int[][] Buckets = new int[RingSize][];
        public int[] BucketCounts = new int[RingSize];
        public int CurrentTotal;
        public int Queued;
        public int[]? ReverseStamp;
        public int ReverseHead;
        public int ReverseTail;
        public int ReverseCells;
        public int[]? ClosestSeen;
        public int ClosestStamp;
        public int[] ClosestQueue = new int[256];
        public int ClosestChecks;
        public bool ClosestFinal;
        public int ProbeExpansions;
        public int ProbePushes;
        public int ProbePathCells;
        public int ProbeEndDistance;
        public int SStartIndex, SGoalIndex, SGoalCellX, SGoalCellY, SStamp, SBest, SBestHeuristic, SBestCost, SExpansions, SNextClosestCheck, SOutcome;
        public Fixed SGoalX, SGoalY;
        public bool SReverse, SReached;

        // Which query the memory holds, and how far it got.
        public PathQuery? Query;
        public bool Running;
        public int EscapeAtBegin;
        public int TemporaryAtBegin;
    }

    private SearchMemory? _sliceMemory;

    /// <summary>Exchanges the grid's search memory with <paramref name="memory"/>: called once to enter a slice and once to leave it.</summary>
    private void Swap(SearchMemory memory)
    {
        (_search, memory.Search) = (memory.Search, _search);
        (_currentStamp, memory.CurrentStamp) = (memory.CurrentStamp, _currentStamp);
        (_open, memory.Open) = (memory.Open, _open);
        (_marked, memory.Marked) = (memory.Marked, _marked);
        (_markedCount, memory.MarkedCount) = (memory.MarkedCount, _markedCount);
        (_escapeCount, memory.EscapeCount) = (memory.EscapeCount, _escapeCount);
        (_temporaryCount, memory.TemporaryCount) = (memory.TemporaryCount, _temporaryCount);
        (_queue, memory.Queue) = (memory.Queue, _queue);
        (_pathCells, memory.PathCells) = (memory.PathCells, _pathCells);
        (_points, memory.Points) = (memory.Points, _points);
        (_buckets, memory.Buckets) = (memory.Buckets, _buckets);
        (_bucketCounts, memory.BucketCounts) = (memory.BucketCounts, _bucketCounts);
        (_currentTotal, memory.CurrentTotal) = (memory.CurrentTotal, _currentTotal);
        (_queued, memory.Queued) = (memory.Queued, _queued);
        (_reverseStamp, memory.ReverseStamp) = (memory.ReverseStamp, _reverseStamp);
        (_reverseHead, memory.ReverseHead) = (memory.ReverseHead, _reverseHead);
        (_reverseTail, memory.ReverseTail) = (memory.ReverseTail, _reverseTail);
        (_reverseCells, memory.ReverseCells) = (memory.ReverseCells, _reverseCells);
        (_closestSeen, memory.ClosestSeen) = (memory.ClosestSeen, _closestSeen);
        (_closestStamp, memory.ClosestStamp) = (memory.ClosestStamp, _closestStamp);
        (_closestQueue, memory.ClosestQueue) = (memory.ClosestQueue, _closestQueue);
        (_closestChecks, memory.ClosestChecks) = (memory.ClosestChecks, _closestChecks);
        (_closestFinal, memory.ClosestFinal) = (memory.ClosestFinal, _closestFinal);
        (_probeExpansions, memory.ProbeExpansions) = (memory.ProbeExpansions, _probeExpansions);
        (_probePushes, memory.ProbePushes) = (memory.ProbePushes, _probePushes);
        (_probePathCells, memory.ProbePathCells) = (memory.ProbePathCells, _probePathCells);
        (_probeEndDistance, memory.ProbeEndDistance) = (memory.ProbeEndDistance, _probeEndDistance);
        (_sStartIndex, memory.SStartIndex) = (memory.SStartIndex, _sStartIndex);
        (_sGoalIndex, memory.SGoalIndex) = (memory.SGoalIndex, _sGoalIndex);
        (_sGoalCellX, memory.SGoalCellX) = (memory.SGoalCellX, _sGoalCellX);
        (_sGoalCellY, memory.SGoalCellY) = (memory.SGoalCellY, _sGoalCellY);
        (_sGoalX, memory.SGoalX) = (memory.SGoalX, _sGoalX);
        (_sGoalY, memory.SGoalY) = (memory.SGoalY, _sGoalY);
        (_sStamp, memory.SStamp) = (memory.SStamp, _sStamp);
        (_sReverse, memory.SReverse) = (memory.SReverse, _sReverse);
        (_sBest, memory.SBest) = (memory.SBest, _sBest);
        (_sBestHeuristic, memory.SBestHeuristic) = (memory.SBestHeuristic, _sBestHeuristic);
        (_sBestCost, memory.SBestCost) = (memory.SBestCost, _sBestCost);
        (_sReached, memory.SReached) = (memory.SReached, _sReached);
        (_sExpansions, memory.SExpansions) = (memory.SExpansions, _sExpansions);
        (_sNextClosestCheck, memory.SNextClosestCheck) = (memory.SNextClosestCheck, _sNextClosestCheck);
        (_sOutcome, memory.SOutcome) = (memory.SOutcome, _sOutcome);
    }

    /// <summary>The marks of a paused search leave the cell bytes (another search may run before it continues) and come back when it continues.</summary>
    private void LiftMarks(SearchMemory memory)
    {
        if (memory.MarkedBits.Length < _markedCount) Array.Resize(ref memory.MarkedBits, _marked.Length);
        for (var index = 0; index < _markedCount; index++)
        {
            var cell = _marked[index];
            memory.MarkedBits[index] = (byte)(_state[cell] & (EscapeBit | TemporaryBit));
            _state[cell] &= unchecked((byte)~(EscapeBit | TemporaryBit));
        }
    }

    private void RestoreMarks(SearchMemory memory)
    {
        for (var index = 0; index < _markedCount; index++) _state[_marked[index]] |= memory.MarkedBits[index];
    }

    /// <summary>
    /// One slice of the search for <paramref name="query"/>: the search begins (or begins again, with <paramref name="restart"/>), silently runs up to <paramref name="resumeAt"/>
    /// expansions when its memory was lost (a restored world), and then runs until it ends or has counted <paramref name="stopAt"/> expansions. Returns whether it ended; then
    /// <paramref name="result"/> is what <see cref="FindPath(Fixed, Fixed, Fixed, Fixed, LocomotorSurfaces, IReadOnlyList{ValueTuple{Fixed, Fixed, Fixed}})"/> would have
    /// returned (or, for an arriving-only query, <see cref="FindPathToGoal"/>). <paramref name="done"/> is the count of expansions so far and <paramref name="spent"/> the cells this
    /// slice worked on (expansions and the cells of the flood from the goal).
    /// </summary>
    public bool Slice(PathQuery query, bool restart, int resumeAt, int stopAt, out PathResult? result, out int done, out int spent)
    {
        result = null;
        var memory = _sliceMemory ??= new SearchMemory { Search = new SearchCell[_search.Length] };
        var probe = Probe;
        probe?.Started(MovementWork.PathSearch);
        Swap(memory);
        var reverseBefore = 0;
        try
        {
            if (restart || !memory.Running || !ReferenceEquals(memory.Query, query))
            {
                memory.Running = false;
                memory.Query = query;
                _probeExpansions = _probePushes = _probePathCells = _probeEndDistance = 0;
                _reverseCells = 0;
                _closestChecks = 0;
                _closestFinal = false;
                ClearMarks();
                var start = ClampedCell(query.StartX, query.StartY);
                var goal = ClampedCell(query.GoalX, query.GoalY);
                if (!query.Blocks.IsDefaultOrEmpty) MarkTemporary(start.X, start.Y, goal.X, goal.Y, query.Blocks);
                memory.TemporaryAtBegin = _temporaryCount;
                if (BeginSearch(start.X, start.Y, goal.X, goal.Y, query.GoalX, query.GoalY, query.Surfaces, query.ArrivingOnly, out var early))
                {
                    memory.EscapeAtBegin = _escapeCount;
                    ClearMarks();
                    result = early;
                    done = 0;
                    spent = 0;
                    Report(probe, query, result, memory);
                    return true;
                }
                memory.EscapeAtBegin = _escapeCount;
                memory.Running = true;
                if (resumeAt > 0) ContinueSearch(resumeAt);
            }
            else RestoreMarks(memory);

            var before = _sExpansions;
            reverseBefore = _reverseCells;
            var ended = ContinueSearch(stopAt);
            done = _sExpansions;
            spent = _sExpansions - before + (_reverseCells - reverseBefore);
            if (!ended)
            {
                LiftMarks(memory);
                return false;
            }
            result = FinishSearch();
            // An arriving-only question has no use for a path that ends short (FindPathToGoal).
            if (query.ArrivingOnly && result is not { Status: PathStatus.Found }) result = null;
            ClearMarks();
            memory.Running = false;
            Report(probe, query, result, memory);
            return true;
        }
        finally
        {
            Swap(memory);
            probe?.Ended(MovementWork.PathSearch);
        }
    }

    /// <summary>Forgets a paused search (its requester is gone, or another search takes the slices).</summary>
    public void DropSlice()
    {
        if (_sliceMemory is { Running: true } memory)
        {
            memory.Running = false;
            memory.Query = null;
        }
    }

    private void Report(IMovementProbe? probe, PathQuery query, PathResult? result, SearchMemory memory)
    {
        if (probe is null) return;
        var start = ClampedCell(query.StartX, query.StartY);
        var goal = ClampedCell(query.GoalX, query.GoalY);
        probe.PathSearched(new PathSearchInfo(start.Y * _cellsX + start.X, goal.Y * _cellsX + goal.X, query.Surfaces, result?.Status ?? PathStatus.Partial, _probeExpansions, _probePushes,
            memory.EscapeAtBegin, memory.TemporaryAtBegin, _probePathCells, result?.Points.Length ?? 0, false, result is null, _reverseCells, _probeEndDistance, _closestFinal, _closestChecks));
    }

    /// <summary>
    /// Whether a straight grid walk between the cells of two points stays on cells <paramref name="surfaces"/> may enter (no footprint, no marks of a search): what a member of a
    /// group needs to join the group's path and to leave it for its own goal.
    /// </summary>
    public bool StraightWalkClear(Fixed fromX, Fixed fromY, Fixed toX, Fixed toY, LocomotorSurfaces surfaces)
    {
        PrepareOpenTable(surfaces);
        var from = ClampedCell(fromX, fromY);
        var to = ClampedCell(toX, toY);
        return LineClear(from.Y * _cellsX + from.X, to.Y * _cellsX + to.X);
    }
}

/// <summary>A path question as the world's queue keeps it (the movement profile): start, goal, surfaces, the temporary blockers, and whether only a path that arrives counts.</summary>
public sealed record PathQuery(Fixed StartX, Fixed StartY, Fixed GoalX, Fixed GoalY, LocomotorSurfaces Surfaces, ImmutableArray<(Fixed X, Fixed Y, Fixed Radius)> Blocks, bool ArrivingOnly, ulong Group = 0);
