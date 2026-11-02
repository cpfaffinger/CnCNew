using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Terrain;

/// <summary>
/// The bridge layers of the grid (<c>PathfindLayer</c> of <c>AIPathfind.cpp</c>). A layer is a rectangle of cells of its own over the bridge's bounds, appended to the search's cell numbers
/// after the ground's, so that the search of a map without a bridge is the same search. A cell of a layer is either absent (<c>CELL_IMPASSABLE</c>: <c>Pathfinder::getCell</c> then answers with the
/// ground cell, which is how a unit walks off the end of a deck), clear, or impassable (<c>CELL_BRIDGE_IMPASSABLE</c>: the sides of the deck). The ground joins a deck at its entry points, cells
/// half a cell beyond its ends: such a ground cell and the layer cell above it are the same place at no cost (<c>checkChangeLayers</c>). A deck too low to walk under closes the ground below it.
/// </summary>
public sealed partial class NavigationGrid
{
    /// <summary><c>LAYER_GROUND + 1 .. LAYER_WALL - 1</c>: the bridge layers the original's four bits leave. A further bridge has no layer (<c>addBridge</c>: "Ran out of bridge layers").</summary>
    public const int MaxBridgeLayers = 13;

    private static readonly Fixed CellSizeFixed = Fixed.FromInt(TerrainMap.CellSize);

    private sealed class BridgeLayer(TerrainBridge bridge, int number, int x0, int y0, int width, int height, int firstCell)
    {
        public TerrainBridge Bridge { get; } = bridge;
        public int Number { get; } = number;
        public int X0 { get; } = x0;
        public int Y0 { get; } = y0;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int FirstCell { get; } = firstCell;
        public bool[] Present { get; } = new bool[width * height];
        public bool[] Entry { get; } = new bool[width * height];
        public bool Destroyed { get; set; }
        public int CellCount => Width * Height;
    }

    private readonly List<BridgeLayer> _layers = [];
    private readonly int _groundCells;
    private byte[]? _connectLayer;
    private readonly int[] _links = new int[8 + 1 + 8 * MaxBridgeLayers];

    /// <summary>The number of bridge layers.</summary>
    public int LayerCount => _layers.Count;

    /// <summary>
    /// <c>Pathfinder::addBridge</c> with <c>PathfindLayer::allocateCells</c> and <c>classifyCells</c>: the bridge gets the next free layer and its cells; 0 when the layers are used up. Called while
    /// the world is built, before anybody searched.
    /// </summary>
    public int AddBridge(TerrainBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        if (_layers.Count == MaxBridgeLayers) return 0;
        var number = _layers.Count + 1;
        var (x0, y0, x1, y1) = bridge.CellBounds();
        (x0, y0) = (Math.Max(x0, 0), Math.Max(y0, 0));
        (x1, y1) = (Math.Min(x1, _cellsX - 1), Math.Min(y1, _cellsY - 1));
        var (width, height) = (Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
        if (width == 0 || height == 0) (width, height) = (0, 0);
        var layer = new BridgeLayer(bridge, number, x0, y0, width, height, _state.Length);
        _layers.Add(layer);
        var total = _state.Length + layer.CellCount;
        Array.Resize(ref _state, total);
        _search = new SearchCell[total];
        _reverseStamp = null;
        _closestSeen = null;
        _sliceMemory = null;
        _connectLayer ??= new byte[_groundCells];
        Classify(layer);
        return number;
    }

    /// <summary><c>Pathfinder::changeBridgeState</c>: a destroyed layer (or one under scaffolding) has no way on or over it. Paths made before the change are looked at again.</summary>
    public void SetBridgeUsable(int number, bool usable)
    {
        if (number <= 0 || number > _layers.Count) return;
        var layer = _layers[number - 1];
        if (layer.Destroyed == !usable) return;
        layer.Destroyed = !usable;
        ObstacleSum = unchecked(usable ? ObstacleSum - LayerKey(number) : ObstacleSum + LayerKey(number));
        Classify(layer);
    }

    private static ulong LayerKey(int number) => Mix(0xB51D6E00UL + (ulong)number);

    /// <summary><c>PathfindLayer::isDestroyed</c>.</summary>
    public bool IsBridgeUsable(int number) => number > 0 && number <= _layers.Count && !_layers[number - 1].Destroyed;

    /// <summary>Whether the layer has a walkable cell at the cell (<c>getCell(layer, x, y)</c> is one of the layer's and clear).</summary>
    public bool IsDeckCellClear(int number, int cx, int cy) => CellIndex(number, cx, cy) is var index && index >= _groundCells && (_state[index] & ImpassableBit) == 0;

    /// <summary>Whether the ground cell is a place where the ground joins the layer (<c>PathfindCell::getConnectLayer</c>).</summary>
    public int ConnectLayerAt(int cx, int cy) => _connectLayer is { } connect && Terrain.IsInside(cx, cy) ? connect[cy * _cellsX + cx] : 0;

    /// <summary>
    /// The first broken bridge, in layer order, whose entry points touch both reached sets: <c>PathfindLayer::connectsZones</c> with the two floods standing for the zones. The tables come from
    /// <see cref="ReachableCells"/>.
    /// </summary>
    public int BrokenLayerJoining(bool[] near, bool[] far)
    {
        foreach (var layer in _layers)
        {
            if (!layer.Destroyed) continue;
            var (one, other) = (false, false);
            for (var index = 0; index < layer.CellCount; index++)
            {
                if (!layer.Entry[index]) continue;
                var ground = (layer.Y0 + index / layer.Width) * _cellsX + layer.X0 + index % layer.Width;
                one |= near[ground];
                other |= far[ground];
            }
            if (one && other) return layer.Number;
        }
        return 0;
    }

    /// <summary><c>PathfindLayer::classifyCells</c>.</summary>
    private void Classify(BridgeLayer layer)
    {
        var connect = _connectLayer!;
        for (var row = 0; row < layer.Height; row++)
            for (var column = 0; column < layer.Width; column++)
                ClassifyCell(layer, column, row, connect);
        if (layer.Destroyed)
            for (var index = 0; index < layer.CellCount; index++)
            {
                if (layer.Entry[index]) connect[(layer.Y0 + index / layer.Width) * _cellsX + layer.X0 + index % layer.Width] = 0;
                layer.Present[index] = true;
                _state[layer.FirstCell + index] = ImpassableBit;
            }
        _memoStale = true;
    }

    /// <summary><c>PathfindLayer::classifyLayerMapCell</c>, the branch of Zero Hour.</summary>
    private void ClassifyCell(BridgeLayer layer, int column, int row, byte[] connect)
    {
        var (i, j) = (layer.X0 + column, layer.Y0 + row);
        var (bridge, index) = (layer.Bridge, row * layer.Width + column);
        var (loX, loY) = (CellSizeFixed * i, CellSizeFixed * j);
        var (hiX, hiY) = (loX + CellSizeFixed, loY + CellSizeFixed);
        var corners = 0;
        if (bridge.IsPointOnBridge(loX, loY)) corners++;
        if (bridge.IsPointOnBridge(loX, hiY)) corners++;
        if (bridge.IsPointOnBridge(hiX, hiY)) corners++;
        if (bridge.IsPointOnBridge(hiX, loY)) corners++;

        var present = false;
        var clear = false;
        var entry = false;
        if (corners == 4) present = clear = true;
        else
        {
            present = corners != 0;
            if (bridge.IsCellOnSide(loX, loY, hiX, hiY)) present = true;
            else
            {
                if (bridge.IsCellOnEnd(loX, loY, hiX, hiY)) present = clear = true;
                if (bridge.IsCellEntryPoint(loX, loY, hiX, hiY))
                {
                    present = clear = entry = true;
                    connect[j * _cellsX + i] = checked((byte)layer.Number);
                }
            }
        }
        layer.Present[index] = present;
        layer.Entry[index] = entry;
        _state[layer.FirstCell + index] = clear ? (byte)0 : ImpassableBit;
        if (!present || entry) return;
        // "Check for bridge clearance. If the ground isn't 1 pathfind cells below, mark impassable."
        var half = CellSizeFixed / Fixed.FromInt(2);
        var (centerX, centerY) = (loX + half, loY + half);
        if (Terrain.HeightAt(centerX, centerY) + TerrainBridge.LayerCloseEnough > bridge.HeightAt(centerX, centerY)) _state[j * _cellsX + i] |= ImpassableBit;
    }

    /// <summary><c>Pathfinder::getCell</c>: the cell of the layer when it has one there, else the ground's.</summary>
    private int CellIndex(int number, int x, int y)
    {
        if (number > 0 && number <= _layers.Count)
        {
            var layer = _layers[number - 1];
            var (column, row) = (x - layer.X0, y - layer.Y0);
            if (column >= 0 && column < layer.Width && row >= 0 && row < layer.Height && layer.Present[row * layer.Width + column]) return layer.FirstCell + row * layer.Width + column;
        }
        return y * _cellsX + x;
    }

    private BridgeLayer LayerHolding(int index)
    {
        foreach (var layer in _layers)
            if (index >= layer.FirstCell && index < layer.FirstCell + layer.CellCount) return layer;
        throw new InvalidOperationException("A cell number beyond the layers.");
    }

    private void DeckCellOf(int index, out int number, out int x, out int y)
    {
        var layer = LayerHolding(index);
        var cell = index - layer.FirstCell;
        number = layer.Number;
        y = layer.Y0 + cell / layer.Width;
        x = layer.X0 + cell % layer.Width;
    }

    private int LayerOf(int index) => index < _groundCells ? 0 : LayerHolding(index).Number;

    private PathPoint PointOf(int index)
    {
        if (index < _groundCells) return new PathPoint(TerrainMap.CellCenter(index % _cellsX), TerrainMap.CellCenter(index / _cellsX));
        DeckCellOf(index, out var number, out var x, out var y);
        return new PathPoint(TerrainMap.CellCenter(x), TerrainMap.CellCenter(y), number);
    }

    private bool IsOpenOn(int number, int x, int y)
    {
        if (number == 0) return IsOpen(x, y);
        if ((uint)x >= (uint)_cellsX || (uint)y >= (uint)_cellsY) return false;
        var index = CellIndex(number, x, y);
        return index >= _groundCells && _open[_state[index]];
    }

    /// <summary>A step of a unit on a layer to (x, y): the cell it lands on, -1 beyond the map.</summary>
    private int StepTarget(int number, int x, int y) => (uint)x >= (uint)_cellsX || (uint)y >= (uint)_cellsY ? -1 : CellIndex(number, x, y);

    /// <summary>The neighbours of a cell of a layer, in the order of the ground's; the same moves, then the way down at an entry point.</summary>
    private void ExpandDeckCell(SearchCell[] search, byte[] state, bool[] open, int stamp, int current, int cx, int cy, int currentCost, int goalCellX, int goalCellY)
    {
        var layer = LayerHolding(current);
        var number = layer.Number;
        var (northCell, eastCell, southCell, westCell) = (StepTarget(number, cx, cy - 1), StepTarget(number, cx + 1, cy), StepTarget(number, cx, cy + 1), StepTarget(number, cx - 1, cy));
        var north = northCell >= 0 && open[state[northCell]];
        var east = eastCell >= 0 && open[state[eastCell]];
        var south = southCell >= 0 && open[state[southCell]];
        var west = westCell >= 0 && open[state[westCell]];
        if (north) Relax(search, state, stamp, current, northCell, currentCost, StraightCost, cx, cy - 1, goalCellX, goalCellY);
        if (east) Relax(search, state, stamp, current, eastCell, currentCost, StraightCost, cx + 1, cy, goalCellX, goalCellY);
        if (south) Relax(search, state, stamp, current, southCell, currentCost, StraightCost, cx, cy + 1, goalCellX, goalCellY);
        if (west) Relax(search, state, stamp, current, westCell, currentCost, StraightCost, cx - 1, cy, goalCellX, goalCellY);
        void Diagonal(bool first, bool second, int dx, int dy)
        {
            if (!first || !second) return;
            var next = StepTarget(number, cx + dx, cy + dy);
            if (next >= 0 && open[state[next]]) Relax(search, state, stamp, current, next, currentCost, DiagonalCost, cx + dx, cy + dy, goalCellX, goalCellY);
        }
        Diagonal(east, north, 1, -1);
        Diagonal(east, south, 1, 1);
        Diagonal(west, south, -1, 1);
        Diagonal(west, north, -1, -1);
        if (layer.Entry[current - layer.FirstCell] && !layer.Destroyed && open[state[cy * _cellsX + cx]])
            Relax(search, state, stamp, current, cy * _cellsX + cx, currentCost, 0, cx, cy, goalCellX, goalCellY);
    }

    /// <summary>
    /// Every cell a move of the search joins to <paramref name="cell"/>, in either direction: a table for the floods from the goal, which may only ever know more ways than there are, never fewer
    /// (a flood that runs dry proves that no way exists). Returns the count.
    /// </summary>
    private int LinkedCells(int cell)
    {
        var links = _links;
        var count = 0;
        var open = _open;
        var state = _state;
        if (cell >= _groundCells)
        {
            var layer = LayerHolding(cell);
            DeckCellOf(cell, out var number, out var x, out var y);
            for (var direction = 0; direction < 8; direction++)
                if (StepTarget(number, x + StepX[direction], y + StepY[direction]) is var next and >= 0) links[count++] = next;
            if (layer.Entry[cell - layer.FirstCell]) links[count++] = y * _cellsX + x;
            return count;
        }

        var (cx, cy) = (cell % _cellsX, cell / _cellsX);
        for (var direction = 0; direction < 8; direction++)
        {
            var (nx, ny) = (cx + StepX[direction], cy + StepY[direction]);
            if ((uint)nx >= (uint)_cellsX || (uint)ny >= (uint)_cellsY) continue;
            // A diagonal move passes between the two cells that share a side with both ends.
            if (direction >= 4 && (!open[state[cy * _cellsX + nx]] || !open[state[ny * _cellsX + cx]])) continue;
            links[count++] = ny * _cellsX + nx;
        }
        if (_connectLayer![cell] != 0 && CellIndex(_connectLayer[cell], cx, cy) is var deck && deck >= _groundCells) links[count++] = deck;
        foreach (var layer in _layers)
        {
            // A step off a deck lands on the ground cell where the layer has none.
            if (cx < layer.X0 - 1 || cy < layer.Y0 - 1 || cx > layer.X0 + layer.Width || cy > layer.Y0 + layer.Height) continue;
            if (CellIndex(layer.Number, cx, cy) >= _groundCells) continue;
            for (var direction = 0; direction < 8; direction++)
                if (CellIndex(layer.Number, Math.Clamp(cx + StepX[direction], 0, _cellsX - 1), Math.Clamp(cy + StepY[direction], 0, _cellsY - 1)) is var above && above >= _groundCells) links[count++] = above;
        }
        return count;
    }

    private int AdvanceReverseOverLayers(int startIndex, int stamp, int budget)
    {
        var open = _open;
        var state = _state;
        var seen = _reverseStamp!;
        while (_reverseHead < _reverseTail && budget-- > 0)
        {
            var cell = _queue[_reverseHead++];
            _reverseCells++;
            var count = LinkedCells(cell);
            for (var link = 0; link < count; link++)
            {
                var from = _links[link];
                if (from == startIndex) return 1;
                if (seen[from] == stamp || !open[state[from]]) continue;
                seen[from] = stamp;
                Enqueue(ref _reverseTail, from);
            }
        }
        return _reverseHead == _reverseTail ? -1 : 0;
    }

    private bool ClosestCellIsFinalOverLayers(int goalCellX, int goalCellY, int closest, int stamp)
    {
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
        bool Seed(int index)
        {
            if (!open[state[index]]) return true;
            if (search[index].Stamp == stamp) return false;
            if (seen[index] == mark) return true;
            seen[index] = mark;
            if (tail == _closestQueue.Length) Array.Resize(ref _closestQueue, _closestQueue.Length * 2);
            _closestQueue[tail++] = index;
            return true;
        }
        var radius = (closest - 1) / 10;
        for (var y = Math.Max(0, goalCellY - radius); y <= Math.Min(_cellsY - 1, goalCellY + radius); y++)
            for (var x = Math.Max(0, goalCellX - radius); x <= Math.Min(_cellsX - 1, goalCellX + radius); x++)
            {
                if (Heuristic(x, y, goalCellX, goalCellY) >= closest) continue;
                if (!Seed(y * _cellsX + x)) return false;
                foreach (var layer in _layers)
                    if (CellIndex(layer.Number, x, y) is var deck && deck >= _groundCells && !Seed(deck)) return false;
            }
        for (var head = 0; head < tail; head++)
        {
            if (head >= ClosestCheckFlood) return false;
            var count = LinkedCells(_closestQueue[head]);
            for (var link = 0; link < count; link++)
            {
                var from = _links[link];
                if (search[from].Stamp == stamp) return false;
                if (seen[from] == mark || !open[state[from]]) continue;
                seen[from] = mark;
                if (tail == _closestQueue.Length) Array.Resize(ref _closestQueue, _closestQueue.Length * 2);
                _closestQueue[tail++] = from;
            }
        }
        return true;
    }

    /// <summary><see cref="ReachableCells"/> on a map with bridges: the table holds the decks' cells behind the ground's.</summary>
    private bool[] ReachableCellsOverLayers(Fixed startX, Fixed startY, LocomotorSurfaces surfaces, int startLayer)
    {
        PrepareOpenTable(surfaces);
        var reached = new bool[_state.Length];
        var (startCellX, startCellY) = ClampedCell(startX, startY);
        var pending = new Stack<int>();
        var start = CellIndex(startLayer, startCellX, startCellY);
        reached[start] = true;
        pending.Push(start);
        while (pending.TryPop(out var cell))
        {
            // Walking out of a footprint, as a path search does.
            var leaving = cell < _groundCells && IsCovered(cell);
            var count = LinkedCells(cell);
            for (var link = 0; link < count; link++)
            {
                var next = _links[link];
                if (reached[next]) continue;
                var enters = _open[_state[next]] || (leaving && next < _groundCells && (_state[next] & ImpassableBit) == 0 && SurfaceAllows((CellFlags)(_state[next] & TerrainBits), surfaces));
                if (!enters) continue;
                reached[next] = true;
                pending.Push(next);
            }
        }
        return reached;
    }

    private bool ReachesOnADeck(bool[] reached, int cx, int cy)
    {
        foreach (var layer in _layers)
            if (CellIndex(layer.Number, cx, cy) is var deck && deck >= _groundCells && deck < reached.Length && reached[deck]) return true;
        return false;
    }
}
