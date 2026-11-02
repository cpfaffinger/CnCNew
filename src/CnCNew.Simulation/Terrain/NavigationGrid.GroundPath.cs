using System.Collections.Immutable;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Terrain;

/// <summary>
/// <c>Pathfinder::findGroundPath</c>: the wide path a group's columns follow. Clear ground only, cells narrower than the path's diameter at a price, a straight run towards the goal
/// wherever the full width is clear (<c>groundCellsCallback</c>), and <c>Path::optimizeGroundPath</c> on the result. The original also asks whether a unit stands fixed in a cell
/// (<c>UNIT_PRESENT_FIXED</c>) and whether a cell is pinched; the port's grid knows neither.
/// </summary>
public sealed partial class NavigationGrid
{
    private const byte NotClearBits = TerrainBits | ObstacleBit | ImpassableBit | RubbleBit;

    /// <summary><c>findGroundPath</c>: <c>MAX_OFFSET</c>, how far a goal that is not clear moves.</summary>
    private const int GroundGoalMaxOffset = 8;

    /// <summary><c>0.6f * COST_ORTHOGONAL</c> for every cell of width a cell lacks.</summary>
    private const int GroundNarrowCost = 6;

    /// <summary><c>0.5f * COST_ORTHOGONAL</c>, a step of the straight run.</summary>
    private const int GroundRunCost = 5;

    /// <summary><c>optimizeGroundPath</c>: <c>ALLOWED_STEPS</c>, how far a straight line may reach across a change of layer.</summary>
    private const int GroundLayerSteps = 3;

    /// <summary><c>sqr(PATHFIND_CELL_SIZE_F)*3.9f</c>: a jog of the path is shorter than this.</summary>
    private const decimal GroundJogSquared = 390m;

    private int[]? _groundCost;
    private int[]? _groundTotal;
    private int[]? _groundParent;
    private int[]? _groundSeen;
    private int[]? _groundClosed;
    private int[]? _groundClearSeen;
    private byte[]? _groundClear;
    private int _groundStamp;
    private readonly PriorityQueue<int, long> _groundOpen = new();

    /// <summary><c>Pathfinder::clearCellForDiameter</c>: the widest diameter up to <paramref name="diameter"/> whose cells around this one are all clear; off the map none.</summary>
    private int ClearDiameter(int cellX, int cellY, int layer, int diameter)
    {
        var radius = diameter / 2;
        var above = radius == 0 ? 1 : radius;
        var cutCorners = radius > 1;
        var clear = true;
        for (var x = cellX - radius; x < cellX + above; x++)
        {
            var xEdge = x == cellX - radius || x == cellX + above - 1;
            for (var y = cellY - radius; y < cellY + above; y++)
            {
                if (xEdge && cutCorners && (y == cellY - radius || y == cellY + above - 1)) continue;
                var index = StepTarget(layer, x, y);
                if (index < 0) return 0;
                if ((_state[index] & NotClearBits) != 0) clear = false;
                if (!clear) break;
            }
        }
        if (clear) return radius == 0 ? 1 : 2 * radius;
        return diameter < 2 ? 0 : ClearDiameter(cellX, cellY, layer, diameter - 2);
    }

    /// <summary>The clearance of a cell, once per search: a search over the whole map asks for each cell up to eight times.</summary>
    private int ClearDiameterOf(int index, int cellX, int cellY, int layer, int diameter)
    {
        if (_groundClearSeen![index] == _groundStamp) return _groundClear![index];
        var clear = ClearDiameter(cellX, cellY, layer, diameter);
        _groundClearSeen[index] = _groundStamp;
        _groundClear![index] = checked((byte)clear);
        return clear;
    }

    private void GroundCellOf(int index, out int layer, out int x, out int y)
    {
        if (index >= _groundCells)
        {
            DeckCellOf(index, out layer, out x, out y);
            return;
        }
        layer = 0;
        y = index / _cellsX;
        x = index - y * _cellsX;
    }

    /// <summary><c>Pathfinder::iterateCellsAlongLine</c>; <paramref name="step"/> gets the cell before, the cell, and its place, and stops the walk with <see langword="false"/>. Returns whether the walk reached the end.</summary>
    private bool WalkCells(int x0, int y0, int x1, int y1, int layer, Func<int, int, int, int, bool> step)
    {
        var (deltaX, deltaY) = (Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        var (x, y) = (x0, y0);
        var incX = x1 >= x0 ? 1 : -1;
        var incY = y1 >= y0 ? 1 : -1;
        var (minorX, minorY, majorX, majorY) = (incX, incY, incX, incY);
        int denominator, numerator, add, count;
        if (deltaX >= deltaY)
        {
            (minorX, majorY) = (0, 0);
            (denominator, numerator, add, count) = (deltaX, deltaX / 2, deltaY, deltaX);
        }
        else
        {
            (majorX, minorY) = (0, 0);
            (denominator, numerator, add, count) = (deltaY, deltaY / 2, deltaX, deltaY);
        }
        var from = -1;
        for (var pixel = 0; pixel <= count; pixel++)
        {
            var to = StepTarget(layer, x, y);
            if (to < 0) return true;
            if (!step(from, to, x, y)) return false;
            numerator += add;
            if (numerator >= denominator)
            {
                numerator -= denominator;
                x += minorX;
                y += minorY;
                from = to;
                to = StepTarget(layer, x, y);
                if (to < 0) return true;
                if (!step(from, to, x, y)) return false;
            }
            x += majorX;
            y += majorY;
            from = to;
        }
        return true;
    }

    /// <summary><c>PathfindCell::costToGoal</c>.</summary>
    private static int GroundToGoal(int x, int y, int goalX, int goalY)
    {
        var (dx, dy) = (Math.Abs(x - goalX), Math.Abs(y - goalY));
        return dx > dy ? StraightCost * dx + StraightCost * dy / 2 : StraightCost * dy + StraightCost * dx / 2;
    }

    /// <summary><c>PathfindCell::costSoFar</c>: the step, and what a turn costs.</summary>
    private int GroundStep(int parent, int parentX, int parentY, int x, int y)
    {
        var (backX, backY) = (parentX - x, parentY - y);
        var cost = _groundCost![parent] + (backX == 0 || backY == 0 ? StraightCost : DiagonalCost);
        if (_groundParent![parent] is var before and >= 0)
        {
            GroundCellOf(before, out _, out var beforeX, out var beforeY);
            var (dirX, dirY) = (beforeX - parentX, beforeY - parentY);
            if (dirX != backX || dirY != backY)
            {
                var dot = dirX * backX + dirY * backY;
                cost += dot > 0 ? 4 : dot == 0 ? 8 : 16;
            }
        }
        return cost;
    }

    private void GroundPush(int cell, int parent, int cost, int total, ref long sequence)
    {
        _groundCost![cell] = cost;
        _groundTotal![cell] = total;
        _groundParent![cell] = parent;
        _groundSeen![cell] = _groundStamp;
        _groundClosed![cell] = 0;
        _groundOpen.Enqueue(cell, ((long)total << 32) | sequence++);
    }

    /// <summary>
    /// The nodes of the ground path of the original (<c>getNextOptimized</c>), the first the start itself; empty when the goal has no clear place within eight cells or cannot be reached
    /// (<c>findGroundPath</c> returns <c>NULL</c>, and the group moves as single units).
    /// </summary>
    public ImmutableArray<PathPoint> FindGroundPath(Fixed fromX, Fixed fromY, int fromLayer, Fixed toX, Fixed toY, int goalLayer, int diameter)
    {
        var size = _state.Length;
        if (_groundCost is null || _groundCost.Length != size)
        {
            (_groundCost, _groundTotal, _groundParent, _groundSeen, _groundClosed, _groundClearSeen) = (new int[size], new int[size], new int[size], new int[size], new int[size], new int[size]);
            _groundClear = new byte[size];
            _groundStamp = 0;
        }
        var (goalX, goalY) = ClampedCell(toX, toY);
        if (ClearDiameter(goalX, goalY, goalLayer, diameter) != diameter && !MoveGoalToClear(ref goalX, ref goalY, goalLayer, diameter)) return [];
        var goal = CellIndex(goalLayer, goalX, goalY);
        var (startX, startY) = ClampedCell(fromX, fromY);
        var start = CellIndex(fromLayer, startX, startY);
        if (start == goal) return [];

        _groundStamp++;
        _groundOpen.Clear();
        var sequence = 0L;
        GroundPush(start, -1, 0, GroundToGoal(startX, startY, goalX, goalY), ref sequence);
        for (var expansions = 0; _groundOpen.TryDequeue(out var current, out var key) && expansions < ExpansionBudget;)
        {
            if ((int)(key >> 32) != _groundTotal![current] || _groundClosed![current] == _groundStamp) continue;
            if (current == goal) return GroundNodes(goal, fromX, fromY, fromLayer, diameter);
            expansions++;
            _groundClosed[current] = _groundStamp;
            GroundCellOf(current, out var layer, out var cx, out var cy);
            GroundChangeLayer(current, layer, cx, cy, goalX, goalY, ref sequence);
            var run = sequence;
            WalkCells(cx, cy, goalX, goalY, layer, (from, to, x, y) =>
            {
                if (from < 0) return true;
                if (_groundSeen![to] == _groundStamp) return false;
                if (ClearDiameterOf(to, x, y, layer, diameter) != diameter) return false;
                var cost = _groundCost![from] + GroundRunCost;
                GroundPush(to, from, cost, cost + GroundToGoal(x, y, goalX, goalY), ref run);
                return true;
            });
            sequence = run;
            GroundNeighbours(current, layer, cx, cy, goal, goalX, goalY, diameter, ref sequence);
        }
        return [];
    }

    /// <summary>
    /// <c>findGroundPath</c>: a goal without the full width looks at the eight places around it, a little further each time, in the original's order.
    /// </summary>
    private bool MoveGoalToClear(ref int cellX, ref int cellY, int layer, int diameter)
    {
        for (var offset = 1; offset < GroundGoalMaxOffset; offset++)
        {
            var (x, y) = (cellX, cellY);
            ReadOnlySpan<(int X, int Y)> walk = [(1, 0), (0, 1), (-1, 0), (-1, 0), (0, -1), (0, -1), (1, 0), (1, 0)];
            foreach (var (stepX, stepY) in walk)
            {
                x += stepX * offset;
                y += stepY * offset;
                if (ClearDiameter(x, y, layer, diameter) != diameter) continue;
                (cellX, cellY) = (x, y);
                return true;
            }
        }
        return false;
    }

    /// <summary><c>Pathfinder::checkChangeLayers</c>: at an entry point the deck and the ground are the same place.</summary>
    private void GroundChangeLayer(int current, int layer, int cx, int cy, int goalX, int goalY, ref long sequence)
    {
        var other = -1;
        if (layer == 0)
        {
            if (_connectLayer is { } connect && connect[current] != 0 && CellIndex(connect[current], cx, cy) is var deck && deck >= _groundCells) other = deck;
        }
        else
        {
            var holding = LayerHolding(current);
            if (holding.Entry[current - holding.FirstCell] && !holding.Destroyed) other = cy * _cellsX + cx;
        }
        if (other < 0 || (_state[other] & NotClearBits) != 0) return;
        var cost = _groundCost![current];
        if (_groundSeen![other] == _groundStamp && _groundCost[other] <= cost) return;
        GroundPush(other, current, cost, cost + GroundToGoal(cx, cy, goalX, goalY), ref sequence);
    }

    private void GroundNeighbours(int current, int layer, int cx, int cy, int goal, int goalX, int goalY, int diameter, ref long sequence)
    {
        Span<bool> open = stackalloc bool[8];
        ReadOnlySpan<int> adjacent = [0, 1, 2, 3, 0];
        for (var direction = 0; direction < 8; direction++)
        {
            var (x, y) = (cx + GroundStepX[direction], cy + GroundStepY[direction]);
            var cell = StepTarget(layer, x, y);
            if (cell < 0) continue;
            var narrow = 0;
            if (cell != goal)
            {
                if (direction >= 4 && !open[adjacent[direction - 4]] && !open[adjacent[direction - 3]]) continue;
                var clear = ClearDiameterOf(cell, x, y, layer, diameter);
                if ((_state[cell] & NotClearBits) != 0) continue;
                open[direction] = true;
                if (clear < diameter) narrow = GroundNarrowCost * (diameter - clear);
            }
            var cost = GroundStep(current, cx, cy, x, y) + narrow;
            if (_groundSeen![cell] == _groundStamp && _groundCost![cell] <= cost) continue;
            GroundPush(cell, current, cost, cost + GroundToGoal(x, y, goalX, goalY), ref sequence);
        }
    }

    // findGroundPath's delta: the four straight neighbours, then the diagonals.
    private static readonly int[] GroundStepX = [1, 0, -1, 0, 1, -1, -1, 1];
    private static readonly int[] GroundStepY = [0, 1, 0, -1, 1, 1, -1, -1];

    /// <summary><c>prependCells</c> (the corner of a cell: a path of even width has no centre cell) and <c>optimizeGroundPath</c>.</summary>
    private ImmutableArray<PathPoint> GroundNodes(int goal, Fixed fromX, Fixed fromY, int fromLayer, int diameter)
    {
        var nodes = new List<PathPoint>();
        for (var cell = goal; _groundParent![cell] >= 0; cell = _groundParent[cell])
        {
            GroundCellOf(cell, out var layer, out var x, out var y);
            var before = _groundParent[cell];
            GroundCellOf(before, out _, out var beforeX, out var beforeY);
            if (beforeX == x && beforeY == y) continue;
            nodes.Add(new PathPoint(Fixed.FromInt(x * TerrainMap.CellSize), Fixed.FromInt(y * TerrainMap.CellSize), layer));
        }
        nodes.Add(new PathPoint(fromX, fromY, fromLayer));
        nodes.Reverse();
        return OptimizeGroundPath(nodes, diameter);
    }

    private ImmutableArray<PathPoint> OptimizeGroundPath(List<PathPoint> nodes, int diameter)
    {
        var last = nodes.Count - 1;
        var kept = new List<int> { 0 };
        for (var anchor = 0; anchor != last;)
        {
            var layer = nodes[anchor].Layer;
            var current = layer;
            var count = 0;
            var node = anchor + 1;
            for (; node < last; node++)
            {
                count++;
                if (current == 0)
                {
                    if (nodes[node].Layer != current)
                    {
                        layer = nodes[node].Layer;
                        current = layer;
                        if (count > GroundLayerSteps) break;
                    }
                }
                else if (nodes[node + 1].Layer != current && count > GroundLayerSteps) break;
                current = nodes[node].Layer;
            }
            var next = -1;
            for (; node != anchor; node--)
                if (IsGroundPathPassable(nodes[anchor], layer, nodes[node], diameter) || IsStraightRun(nodes, anchor, node))
                {
                    next = node;
                    break;
                }
            anchor = next < 0 ? anchor + 1 : next;
            kept.Add(anchor);
        }
        // "Remove jig/jogs": a step of less than two cells, unless it is the last.
        var limit = Fixed.FromDecimal(GroundJogSquared);
        for (var index = 0; index + 2 < kept.Count; index++)
        {
            var (dx, dy) = (nodes[kept[index + 1]].X - nodes[kept[index]].X, nodes[kept[index + 1]].Y - nodes[kept[index]].Y);
            if (dx * dx + dy * dy < limit) kept.RemoveAt(index + 1);
        }
        var result = ImmutableArray.CreateBuilder<PathPoint>(kept.Count);
        foreach (var index in kept) result.Add(nodes[index]);
        return result.MoveToImmutable();
    }

    /// <summary><c>Pathfinder::isGroundPathPassable</c>: the full width all the way; on a deck a step along the deck passes.</summary>
    private bool IsGroundPathPassable(PathPoint from, int layer, PathPoint to, int diameter)
    {
        var (x0, y0) = ClampedCell(from.X, from.Y);
        var (x1, y1) = ClampedCell(to.X, to.Y);
        return WalkCells(x0, y0, x1, y1, layer, (before, cell, x, y) =>
        {
            if (ClearDiameterOf(cell, x, y, cell >= _groundCells ? layer : 0, diameter) == diameter) return true;
            return before >= 0 && cell >= _groundCells && before >= _groundCells;
        });
    }

    /// <summary><c>optimizeGroundPath</c>: "horizontal, diagonal, and vertical steps are passable".</summary>
    private static bool IsStraightRun(List<PathPoint> nodes, int anchor, int node)
    {
        var (dx, dy) = (TruncatedStep(nodes[node].X - nodes[anchor].X), TruncatedStep(nodes[node].Y - nodes[anchor].Y));
        bool Steps(Func<int, int, bool> keeps)
        {
            for (var index = node - 1; index > anchor; index--)
                if (!keeps(TruncatedStep(nodes[index + 1].X - nodes[index].X), TruncatedStep(nodes[index + 1].Y - nodes[index].Y))) return false;
            return true;
        }
        // The original reassigns dx and dy in its loops, so that a later test reads the step last looked at; the runs of whole cells it was written for are the same either way.
        return (dx == 0 && Steps((stepX, _) => stepX == 0)) || (dy == 0 && Steps((_, stepY) => stepY == 0)) || (dx == dy && Steps((stepX, stepY) => stepX == stepY)) || (dx == -dy && Steps((stepX, stepY) => stepX == -stepY));
    }

    private static int TruncatedStep(Fixed value) => value >= Fixed.Zero ? checked((int)value.Floor()) : -checked((int)(-value).Floor());
}
