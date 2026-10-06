using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Terrain;

/// <summary>
/// Work counters of a <see cref="VisionMap"/> for measuring tools: deterministic (they count cells, not time), not part of the state, neither
/// hashed nor saved.
/// </summary>
public sealed class VisionCounters
{
    /// <summary>Calls of <see cref="VisionMap.BeginRefresh"/>.</summary>
    public long Refreshes;
    /// <summary>Cell bytes <see cref="VisionMap.BeginRefresh"/> went over (the whole table in the reference mode).</summary>
    public long CellsCleared;
    /// <summary>Calls of <see cref="VisionMap.Reveal"/> with a positive radius.</summary>
    public long Circles;
    /// <summary>Cell bytes <see cref="VisionMap.Reveal"/> wrote (the cells inside the circles; a cell under several circles counts once per circle).</summary>
    public long CellsRevealed;
    /// <summary>Tests of a cell against the radius in <see cref="VisionMap.Reveal"/>: the reference mode tests every cell of the bounding box, the run walk about five per row plus the steps its two ends move.</summary>
    public long CellsTested;
    /// <summary>Calls of <see cref="VisionMap.CurrentHash"/>.</summary>
    public long HashCalls;
    /// <summary>Calls of <see cref="VisionMap.CurrentHash"/> that found no changed byte and returned the kept value.</summary>
    public long HashUnchanged;
    /// <summary>Cell bytes compared with the copy of the last hashed state to find the first changed byte.</summary>
    public long BytesCompared;
    /// <summary>Cell bytes mixed into the hash chain (the reference mode mixes the whole table on every call).</summary>
    public long BytesHashed;
}

/// <summary>
/// Per-player fog of war on the pathfinding cell grid (<c>PartitionManager</c> shroud in the reference): a cell is <em>visible</em> while some
/// looking object of the player or its allies covers its centre, and <em>explored</em> once it has been visible. Circular, no height
/// line of sight — <c>Object::look</c> only adds a radius. One byte per cell and player (bit 0 visible, bit 1 explored, bit 2 permanently revealed by a script; a snapshot that never used a map-reveal script contains only bits 0 and 1).
/// <para>
/// The cell bytes and <see cref="Hash"/> define the result. Everything else in this class is bookkeeping for <see cref="CurrentHash"/>: the rows
/// touched since the last hash, a copy of the cells as they were hashed and the chain values at fixed distances, so the chain resumes at the
/// first byte that really changed. None of it is state: it is rebuilt from the cells, never hashed, never saved.
/// </para>
/// </summary>
public sealed class VisionMap
{
    private const byte VisibleBit = 1;
    private const byte ExploredBit = 2;

    /// <summary>The cell is revealed for good (<c>revealMapForPlayerPermanently</c>): <see cref="BeginRefresh"/> keeps it visible and explored. Always set together with both other bits.</summary>
    private const byte PermanentBit = 4;


    /// <summary>A chain value is kept before every this many hash words (64 words = 512 cell bytes).</summary>
    private const int CheckpointShift = 6;
    private const int CheckpointMask = (1 << CheckpointShift) - 1;

    private readonly byte[][] _cells;

    // Bookkeeping, not state (see the class remarks).
    private readonly byte[][] _hashed;        // the cells as CurrentHash last mixed them
    private readonly ulong[][] _checkpoints;  // chain value before hash word (index << CheckpointShift) of the slot
    private readonly int[] _touchedLow;       // rows written since the last CurrentHash, per slot; low > high when none
    private readonly int[] _touchedHigh;
    private ulong _hash;

    public VisionMap(int cellsX, int cellsY, int playerSlots)
    {
        CellsX = cellsX;
        CellsY = cellsY;
        var length = cellsX * cellsY;
        _cells = new byte[playerSlots][];
        _hashed = new byte[playerSlots][];
        _checkpoints = new ulong[playerSlots][];
        _touchedLow = new int[playerSlots];
        _touchedHigh = new int[playerSlots];
        var hash = StateHash.Seed;
        for (var slot = 0; slot < playerSlots; slot++)
        {
            _cells[slot] = new byte[length];
            _hashed[slot] = new byte[length];
            _checkpoints[slot] = new ulong[((length >> 3) >> CheckpointShift) + 1];
            _touchedLow[slot] = int.MaxValue;
            _touchedHigh[slot] = -1;
            hash = MixSlot(slot, 0, hash);
        }
        _hash = hash;
    }

    public int CellsX { get; }
    public int CellsY { get; }
    public int PlayerSlots => _cells.Length;

    /// <summary>Work counters for measuring tools; not state.</summary>
    public VisionCounters Counters { get; } = new();

    /// <summary>
    /// A hook for tests and measuring tools, not state: while set, <see cref="CurrentHash"/> runs the full pass (<see cref="Hash"/>) on every
    /// call. The value is the same in both modes; only the work differs. May be switched at any time.
    /// </summary>
    public bool ReferenceMode { get; set; }

    public static int CellOf(Fixed coordinate) => (int)(coordinate / Fixed.FromInt(TerrainMap.CellSize)).Floor();

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)CellsX && (uint)cy < (uint)CellsY;

    public bool IsVisible(int slot, int cx, int cy) => InBounds(cx, cy) && (_cells[slot][cy * CellsX + cx] & VisibleBit) != 0;
    public bool IsExplored(int slot, int cx, int cy) => InBounds(cx, cy) && (_cells[slot][cy * CellsX + cx] & ExploredBit) != 0;

    public bool IsVisibleAt(int slot, Fixed x, Fixed y) => IsVisible(slot, CellOf(x), CellOf(y));
    public bool IsExploredAt(int slot, Fixed x, Fixed y) => IsExplored(slot, CellOf(x), CellOf(y));

    /// <summary>The raw cell bytes of a slot (row-major, <see cref="CellsX"/> per row), for measuring tools and tests.</summary>
    public ReadOnlySpan<byte> CellBytes(int slot) => _cells[slot];

    private void Touch(int slot, int lowRow, int highRow)
    {
        if (lowRow < _touchedLow[slot]) _touchedLow[slot] = lowRow;
        if (highRow > _touchedHigh[slot]) _touchedHigh[slot] = highRow;
    }

    private void TouchAll(int slot) => Touch(slot, 0, CellsY - 1);

    /// <summary>Forgets what is currently visible; exploration stays.</summary>
    public void BeginRefresh() => BeginRefreshReference();

    /// <summary>The refresh start over every cell of every slot.</summary>
    public void BeginRefreshReference()
    {
        Counters.Refreshes++;
        for (var slot = 0; slot < _cells.Length; slot++)
        {
            var cells = _cells[slot];
            for (var index = 0; index < cells.Length; index++)
                cells[index] = (cells[index] & PermanentBit) != 0 ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~VisibleBit);
            Counters.CellsCleared += cells.Length;
            TouchAll(slot);
        }
    }

    /// <summary><c>PartitionManager::revealMapForPlayerPermanently</c> / <c>undoRevealMapForPlayerPermanently</c>: every cell of the slot is visible for good, or the permanent mark is dropped again (what was seen stays explored).</summary>
    public void SetPermanentRevealAll(int slot, bool revealed)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++)
            cells[index] = revealed ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~(PermanentBit | VisibleBit));
        TouchAll(slot);
    }

    /// <summary>The "quick look" of <c>MAP_REVEAL_ALL</c>: every cell becomes explored (fogged), nothing becomes visible.</summary>
    public void ExploreAll(int slot)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++) cells[index] |= ExploredBit;
        TouchAll(slot);
    }

    /// <summary><c>shroudMapForPlayer</c>: every cell of the slot forgets what it knew (visible, explored and permanent marks cleared).</summary>
    public void ShroudAll(int slot)
    {
        Array.Clear(_cells[slot]);
        TouchAll(slot);
    }

    /// <summary>The "quick look" of <c>MAP_REVEAL_AT_WAYPOINT</c> (reveal then undo in the same frame): the cells within the radius become explored, not visible.</summary>
    public void ExploreCircle(int slot, Fixed x, Fixed y, Fixed radius) => ForCircle(slot, x, y, radius, (ref byte cell) => cell |= ExploredBit);

    /// <summary>The "dollop of shroud" of <c>MAP_SHROUD_AT_WAYPOINT</c>: the cells within the radius forget being explored (and, unless permanent, visible).</summary>
    public void ShroudCircle(int slot, Fixed x, Fixed y, Fixed radius)
        => ForCircle(slot, x, y, radius, (ref byte cell) => { if ((cell & PermanentBit) == 0) cell = 0; });

    /// <summary><c>MAP_REVEAL_PERMANENTLY_AT_WAYPOINT</c> and its undo: the cells within the radius are marked permanently visible, or lose the mark again (what was seen stays explored).</summary>
    public void SetPermanentRevealCircle(int slot, Fixed x, Fixed y, Fixed radius, bool revealed)
        => ForCircle(slot, x, y, radius, revealed
            ? (ref byte cell) => cell = unchecked((byte)(VisibleBit | ExploredBit | PermanentBit))
            : (ref byte cell) => cell = (byte)(cell & ~(PermanentBit | VisibleBit)));

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
        if (minY <= maxY) Touch(slot, minY, maxY);
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
    public void Reveal(int slot, Fixed x, Fixed y, Fixed radius) => RevealReference(slot, x, y, radius);

    /// <summary>The reveal, one test per cell of the bounding box.</summary>
    public void RevealReference(int slot, Fixed x, Fixed y, Fixed radius)
    {
        if (radius <= Fixed.Zero) return;
        Counters.Circles++;
        var cells = _cells[slot];
        var cellSize = Fixed.FromInt(TerrainMap.CellSize);
        var minY = Math.Max(0, CellOf(y - radius));
        var maxY = Math.Min(CellsY - 1, CellOf(y + radius));
        var minX = Math.Max(0, CellOf(x - radius));
        var maxX = Math.Min(CellsX - 1, CellOf(x + radius));
        var limit = (long)radius.Raw * radius.Raw;
        var half = TerrainMap.CellSize * 65536L / 2;
        long tested = 0, written = 0;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var dy = (long)cy * cellSize.Raw + half - y.Raw;
            var dy2 = dy * dy;
            if (dy2 > limit) continue;
            if (maxX >= minX) tested += maxX - minX + 1;
            for (var cx = minX; cx <= maxX; cx++)
            {
                var dx = (long)cx * cellSize.Raw + half - x.Raw;
                if (dx * dx + dy2 > limit) continue;
                cells[cy * CellsX + cx] |= VisibleBit | ExploredBit;
                written++;
            }
        }
        Counters.CellsTested += tested;
        Counters.CellsRevealed += written;
        if (minY <= maxY) Touch(slot, minY, maxY);
    }

    /// <summary>
    /// The vision hash by definition: one chain of <see cref="StateHash.Mix"/> over every cell byte of every slot, eight bytes to a word (first
    /// byte lowest), each slot closed by one more word of the remaining bytes (zero when there are none). This is the oracle for
    /// <see cref="CurrentHash"/>; it reads nothing but the cells.
    /// </summary>
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

    /// <summary>
    /// The value of <see cref="Hash"/> for the cells as they are now. Every step of the chain depends on the one before, so a changed byte forces
    /// every later word of every later slot to be mixed again; what can be saved exactly is the part of the chain <em>before</em> the first
    /// changed byte. The rows written since the last call are compared with the copy of the cells as they were hashed; with no difference the
    /// kept value stands, otherwise the chain resumes at the last kept chain value at or before the first difference.
    /// </summary>
    public ulong CurrentHash()
    {
        Counters.HashCalls++;
        if (ReferenceMode)
        {
            Counters.BytesHashed += (long)_cells.Length * CellsX * CellsY;
            // The kept chain values no longer describe the cells; the next non-reference call starts over.
            for (var slot = 0; slot < _cells.Length; slot++) TouchAll(slot);
            return Hash();
        }
        var firstSlot = -1;
        var firstByte = 0;
        for (var slot = 0; slot < _cells.Length && firstSlot < 0; slot++)
        {
            var low = _touchedLow[slot];
            var high = _touchedHigh[slot];
            if (low > high) continue;
            var start = low * CellsX;
            var length = (high - low + 1) * CellsX;
            var same = _cells[slot].AsSpan(start, length).CommonPrefixLength(_hashed[slot].AsSpan(start, length));
            Counters.BytesCompared += Math.Min(length, same + 1);
            if (same == length)
            {
                _touchedLow[slot] = int.MaxValue;
                _touchedHigh[slot] = -1;
                continue;
            }
            firstSlot = slot;
            firstByte = start + same;
        }
        if (firstSlot < 0)
        {
            Counters.HashUnchanged++;
            return _hash;
        }

        // Bring the copy up to date: in the first changed slot from the first difference on, in later slots every touched row.
        for (var slot = firstSlot; slot < _cells.Length; slot++)
        {
            var low = _touchedLow[slot];
            var high = _touchedHigh[slot];
            if (low > high) continue;
            var start = slot == firstSlot ? firstByte : low * CellsX;
            var end = (high + 1) * CellsX;
            _cells[slot].AsSpan(start, end - start).CopyTo(_hashed[slot].AsSpan(start));
            _touchedLow[slot] = int.MaxValue;
            _touchedHigh[slot] = -1;
        }

        var firstWord = ((firstByte >> 3) >> CheckpointShift) << CheckpointShift;
        var hash = _checkpoints[firstSlot][firstWord >> CheckpointShift];
        for (var slot = firstSlot; slot < _cells.Length; slot++) hash = MixSlot(slot, slot == firstSlot ? firstWord : 0, hash);
        _hash = hash;
        return hash;
    }

    /// <summary>
    /// Continues the chain of <see cref="Hash"/> through one slot from hash word <paramref name="firstWord"/> (a multiple of the checkpoint
    /// distance) and records the chain value before every checkpoint word. The words are read whole; on a little-endian machine that is the
    /// word <see cref="Hash"/> assembles byte by byte, elsewhere the bytes are swapped to it.
    /// </summary>
    private ulong MixSlot(int slot, int firstWord, ulong hash)
    {
        var cells = _cells[slot];
        var checkpoints = _checkpoints[slot];
        var words = MemoryMarshal.Cast<byte, ulong>(cells.AsSpan());
        for (var index = firstWord; index < words.Length; index++)
        {
            if ((index & CheckpointMask) == 0) checkpoints[index >> CheckpointShift] = hash;
            var word = words[index];
            if (!BitConverter.IsLittleEndian) word = BinaryPrimitives.ReverseEndianness(word);
            hash = StateHash.Mix(hash, word);
        }
        if ((words.Length & CheckpointMask) == 0) checkpoints[words.Length >> CheckpointShift] = hash;
        ulong rest = 0;
        for (int index = words.Length * 8, filled = 0; index < cells.Length; index++, filled++) rest |= (ulong)cells[index] << (filled * 8);
        Counters.BytesHashed += cells.Length - (long)firstWord * 8;
        return StateHash.Mix(hash, rest);
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
        TouchAll(slot);
    }
}
