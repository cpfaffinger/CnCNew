using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Terrain;

/// <summary>
/// Per-player fog of war on the pathfinding cell grid (<c>PartitionManager</c> shroud in the reference): a cell is <em>visible</em> while some
/// looking object of the player or its allies covers its centre, and <em>explored</em> once it has been visible. Circular, no height
/// line of sight — <c>Object::look</c> only adds a radius. One byte per cell and player (bit 0 visible, bit 1 explored, bit 2 permanently revealed by a script; a snapshot that never used a map-reveal script contains only bits 0 and 1).
/// </summary>
public sealed class VisionMap
{
    private const byte VisibleBit = 1;
    private const byte ExploredBit = 2;

    /// <summary>The cell is revealed for good (<c>revealMapForPlayerPermanently</c>): <see cref="BeginRefresh"/> keeps it visible and explored. Always set together with both other bits.</summary>
    private const byte PermanentBit = 4;
    private readonly byte[][] _cells;

    public VisionMap(int cellsX, int cellsY, int playerSlots)
    {
        CellsX = cellsX;
        CellsY = cellsY;
        _cells = new byte[playerSlots][];
        for (var slot = 0; slot < playerSlots; slot++) _cells[slot] = new byte[cellsX * cellsY];
    }

    public int CellsX { get; }
    public int CellsY { get; }
    public int PlayerSlots => _cells.Length;

    public static int CellOf(Fixed coordinate) => (int)(coordinate / Fixed.FromInt(TerrainMap.CellSize)).Floor();

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)CellsX && (uint)cy < (uint)CellsY;

    public bool IsVisible(int slot, int cx, int cy) => InBounds(cx, cy) && (_cells[slot][cy * CellsX + cx] & VisibleBit) != 0;
    public bool IsExplored(int slot, int cx, int cy) => InBounds(cx, cy) && (_cells[slot][cy * CellsX + cx] & ExploredBit) != 0;

    public bool IsVisibleAt(int slot, Fixed x, Fixed y) => IsVisible(slot, CellOf(x), CellOf(y));
    public bool IsExploredAt(int slot, Fixed x, Fixed y) => IsExplored(slot, CellOf(x), CellOf(y));

    /// <summary>Forgets what is currently visible; exploration stays.</summary>
    public void BeginRefresh()
    {
        foreach (var cells in _cells)
            for (var index = 0; index < cells.Length; index++)
                cells[index] = (cells[index] & PermanentBit) != 0 ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~VisibleBit);
    }

    /// <summary><c>PartitionManager::revealMapForPlayerPermanently</c> / <c>undoRevealMapForPlayerPermanently</c>: every cell of the slot is visible for good, or the permanent mark is dropped again (what was seen stays explored).</summary>
    public void SetPermanentRevealAll(int slot, bool revealed)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++)
            cells[index] = revealed ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~(PermanentBit | VisibleBit));
    }

    /// <summary>The "quick look" of <c>MAP_REVEAL_ALL</c>: every cell becomes explored (fogged), nothing becomes visible.</summary>
    public void ExploreAll(int slot)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++) cells[index] |= ExploredBit;
    }

    /// <summary><c>shroudMapForPlayer</c>: every cell of the slot forgets what it knew (visible, explored and permanent marks cleared).</summary>
    public void ShroudAll(int slot) => Array.Clear(_cells[slot]);

    /// <summary>The "quick look" of <c>MAP_REVEAL_AT_WAYPOINT</c> (reveal then undo in the same frame): the cells within the radius become explored, not visible.</summary>
    public void ExploreCircle(int slot, Fixed x, Fixed y, Fixed radius) => ForCircle(slot, x, y, radius, (ref byte cell) => cell |= ExploredBit);

    /// <summary>The "dollop of shroud" of <c>MAP_SHROUD_AT_WAYPOINT</c>: the cells within the radius forget being explored (and, unless permanent, visible).</summary>
    public void ShroudCircle(int slot, Fixed x, Fixed y, Fixed radius)
        => ForCircle(slot, x, y, radius, (ref byte cell) => { if ((cell & PermanentBit) == 0) cell = 0; });

    private delegate void CellAction(ref byte cell);

    private void ForCircle(int slot, Fixed x, Fixed y, Fixed radius, CellAction action)
    {
        if (radius <= Fixed.Zero) return;
        var cells = _cells[slot];
        var cellSize = Fixed.FromInt(TerrainMap.CellSize);
        var minY = Math.Max(0, CellOf(y - radius));
        var maxY = Math.Min(CellsY - 1, CellOf(y + radius));
        var minX = Math.Max(0, CellOf(x - radius));
        var maxX = Math.Min(CellsX - 1, CellOf(x + radius));
        var limit = (long)radius.Raw * radius.Raw;
        var half = TerrainMap.CellSize * 65536L / 2;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var dy = (long)cy * cellSize.Raw + half - y.Raw;
            var dy2 = dy * dy;
            if (dy2 > limit) continue;
            for (var cx = minX; cx <= maxX; cx++)
            {
                var dx = (long)cx * cellSize.Raw + half - x.Raw;
                if (dx * dx + dy2 <= limit) action(ref cells[cy * CellsX + cx]);
            }
        }
    }

    /// <summary>Marks every cell whose centre lies within <paramref name="radius"/> of the point as visible and explored for the slot.</summary>
    public void Reveal(int slot, Fixed x, Fixed y, Fixed radius)
    {
        if (radius <= Fixed.Zero) return;
        var cells = _cells[slot];
        var cellSize = Fixed.FromInt(TerrainMap.CellSize);
        var minY = Math.Max(0, CellOf(y - radius));
        var maxY = Math.Min(CellsY - 1, CellOf(y + radius));
        var minX = Math.Max(0, CellOf(x - radius));
        var maxX = Math.Min(CellsX - 1, CellOf(x + radius));
        var limit = (long)radius.Raw * radius.Raw;
        var half = TerrainMap.CellSize * 65536L / 2;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var dy = (long)cy * cellSize.Raw + half - y.Raw;
            var dy2 = dy * dy;
            if (dy2 > limit) continue;
            for (var cx = minX; cx <= maxX; cx++)
            {
                var dx = (long)cx * cellSize.Raw + half - x.Raw;
                if (dx * dx + dy2 <= limit) cells[cy * CellsX + cx] |= VisibleBit | ExploredBit;
            }
        }
    }

    public ulong Hash()
    {
        var hash = StateHash.Seed;
        foreach (var cells in _cells)
        {
            ulong word = 0;
            var filled = 0;
            foreach (var cell in cells)
            {
                word |= (ulong)cell << (filled * 8);
                if (++filled != 8) continue;
                hash = StateHash.Mix(hash, word);
                word = 0;
                filled = 0;
            }
            hash = StateHash.Mix(hash, word);
        }
        return hash;
    }

    public string Save(int slot) => Convert.ToBase64String(_cells[slot]);

    public void Load(int slot, string text)
    {
        var bytes = Convert.FromBase64String(text);
        if (bytes.Length != _cells[slot].Length) throw new InvalidDataException("Vision map size does not match the terrain.");
        foreach (var value in bytes)
            if ((value & ~(VisibleBit | ExploredBit | PermanentBit)) != 0 || ((value & VisibleBit) != 0 && (value & ExploredBit) == 0)
                || ((value & PermanentBit) != 0 && (value & (VisibleBit | ExploredBit)) != (VisibleBit | ExploredBit)))
                throw new InvalidDataException("Vision map holds an invalid cell state.");
        bytes.CopyTo(_cells[slot], 0);
    }
}
