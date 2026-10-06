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
    /// <summary>Tests of a cell against the radius in <see cref="VisionMap.Reveal"/>: the reference mode tests every cell of the bounding box, the run walk two per row plus the steps its two ends move.</summary>
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
/// The cell bytes and <see cref="Hash"/> define the result. Everything else in this class is bookkeeping that makes a refresh cost what is in
/// sight instead of the map area: the row extents <see cref="Reveal"/> wrote (so <see cref="BeginRefresh"/> clears only those), the rows touched
/// since the last hash, a copy of the cells as they were hashed and the chain values at fixed distances (so <see cref="CurrentHash"/> resumes the
/// chain at the first byte that really changed). None of it is state: it is rebuilt from the cells, never hashed, never saved.
/// </para>
/// </summary>
public sealed class VisionMap
{
    private const byte VisibleBit = 1;
    private const byte ExploredBit = 2;

    /// <summary>The cell is revealed for good (<c>revealMapForPlayerPermanently</c>): <see cref="BeginRefresh"/> keeps it visible and explored. Always set together with both other bits.</summary>
    private const byte PermanentBit = 4;

    private const ulong Ones = 0x0101010101010101UL;
    private const ulong VisibleAndExploredWord = (VisibleBit | ExploredBit) * Ones;

    /// <summary>A chain value is kept before every this many hash words (64 words = 512 cell bytes).</summary>
    private const int CheckpointShift = 6;
    private const int CheckpointMask = (1 << CheckpointShift) - 1;

    private readonly byte[][] _cells;

    // Bookkeeping, not state (see the class remarks).
    private readonly byte[][] _hashed;        // the cells as CurrentHash last mixed them
    private readonly ulong[][] _checkpoints;  // chain value before hash word (index << CheckpointShift) of the slot
    private readonly int[] _touchedLow;       // rows written since the last CurrentHash, per slot; low > high when none
    private readonly int[] _touchedHigh;
    private readonly int[][] _revealMinX;     // per slot and row: the columns Reveal (or Load) may have left visible without the permanent mark
    private readonly int[][] _revealMaxX;
    private readonly int[] _revealLow;        // rows with a non-empty extent, per slot; low > high when none
    private readonly int[] _revealHigh;
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
        _revealMinX = new int[playerSlots][];
        _revealMaxX = new int[playerSlots][];
        _revealLow = new int[playerSlots];
        _revealHigh = new int[playerSlots];
        var hash = StateHash.Seed;
        for (var slot = 0; slot < playerSlots; slot++)
        {
            _cells[slot] = new byte[length];
            _hashed[slot] = new byte[length];
            _checkpoints[slot] = new ulong[((length >> 3) >> CheckpointShift) + 1];
            _revealMinX[slot] = new int[cellsY];
            _revealMaxX[slot] = new int[cellsY];
            Array.Fill(_revealMinX[slot], int.MaxValue);
            Array.Fill(_revealMaxX[slot], -1);
            _touchedLow[slot] = int.MaxValue;
            _touchedHigh[slot] = -1;
            _revealLow[slot] = int.MaxValue;
            _revealHigh[slot] = -1;
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
    /// A hook for tests and measuring tools, not state: while set, <see cref="BeginRefresh"/>, <see cref="Reveal"/> and <see cref="CurrentHash"/> run
    /// the original computation over every cell (<see cref="BeginRefreshReference"/>, <see cref="RevealReference"/>, <see cref="Hash"/>). The cell
    /// bytes and the hash value are the same in both modes; only the work differs. May be switched at any time.
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

    /// <summary>Any cell of the slot may be visible without the permanent mark from now on (after a load or a reference reveal).</summary>
    private void ExtendRevealToAll(int slot)
    {
        if (CellsX == 0 || CellsY == 0) return;
        Array.Fill(_revealMinX[slot], 0);
        Array.Fill(_revealMaxX[slot], CellsX - 1);
        _revealLow[slot] = 0;
        _revealHigh[slot] = CellsY - 1;
    }

    /// <summary>
    /// Forgets what is currently visible; exploration stays. The same bytes as <see cref="BeginRefreshReference"/>, which goes over every cell:
    /// that pass changes a cell only if it is visible without the permanent mark (a permanent cell always holds all three bits), and such cells
    /// exist only where <see cref="Reveal"/> wrote since the last refresh or where <see cref="Load"/> put them — the recorded row extents.
    /// </summary>
    public void BeginRefresh()
    {
        if (ReferenceMode)
        {
            BeginRefreshReference();
            return;
        }
        Counters.Refreshes++;
        for (var slot = 0; slot < _cells.Length; slot++)
        {
            var low = _revealLow[slot];
            var high = _revealHigh[slot];
            if (low > high) continue;
            var cells = _cells[slot];
            var minX = _revealMinX[slot];
            var maxX = _revealMaxX[slot];
            for (var row = low; row <= high; row++)
            {
                var from = minX[row];
                var to = maxX[row];
                if (from > to) continue;
                ForgetVisible(cells.AsSpan(row * CellsX + from, to - from + 1));
                Counters.CellsCleared += to - from + 1;
                minX[row] = int.MaxValue;
                maxX[row] = -1;
            }
            Touch(slot, low, high);
            _revealLow[slot] = int.MaxValue;
            _revealHigh[slot] = -1;
        }
    }

    /// <summary>The cell rule of <see cref="BeginRefreshReference"/> for every byte value, eight cells at a time.</summary>
    private static void ForgetVisible(Span<byte> cells)
    {
        var words = MemoryMarshal.Cast<byte, ulong>(cells);
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            // Per byte: 0xFF where the permanent bit is set, else 0 (a byte-wise rule, so the byte order of the word does not matter).
            var permanent = (word >> 2) & Ones;
            permanent = (permanent << 8) - permanent;
            words[index] = (word & ~Ones & ~permanent) | (permanent & ((VisibleBit | ExploredBit | PermanentBit) * Ones));
        }
        for (var index = words.Length * 8; index < cells.Length; index++)
            cells[index] = (cells[index] & PermanentBit) != 0 ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~VisibleBit);
    }

    /// <summary>The original refresh start over every cell of every slot: the oracle for <see cref="BeginRefresh"/>.</summary>
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

    /// <summary>
    /// Marks every cell whose centre lies within <paramref name="radius"/> of the point as visible and explored for the slot. The same cells as
    /// <see cref="RevealReference"/>, which tests every cell of the bounding box: within a row the squared distance is convex in the column, so
    /// the cells inside the circle are one run of columns around the cell nearest to the point. A row nearer to the point's row than the one
    /// before holds a run that contains the previous one, a row farther away a run inside it, so the two ends only ever walk outwards or
    /// inwards from the previous row's; the run is then written at once.
    /// </summary>
    public void Reveal(int slot, Fixed x, Fixed y, Fixed radius)
    {
        if (ReferenceMode)
        {
            RevealReference(slot, x, y, radius);
            return;
        }
        if (radius <= Fixed.Zero) return;
        Counters.Circles++;
        var cellRaw = Fixed.FromInt(TerrainMap.CellSize).Raw;
        var minY = Math.Max(0, CellOf(y - radius));
        var maxY = Math.Min(CellsY - 1, CellOf(y + radius));
        var minX = Math.Max(0, CellOf(x - radius));
        var maxX = Math.Min(CellsX - 1, CellOf(x + radius));
        if (minY > maxY || minX > maxX) return;
        var cells = _cells[slot];
        var rowMinX = _revealMinX[slot];
        var rowMaxX = _revealMaxX[slot];
        var limit = (long)radius.Raw * radius.Raw;
        var half = TerrainMap.CellSize * 65536L / 2;
        // The in-bounds column whose centre is nearest to the point: if it is outside the circle in a row, every column of that row is.
        var nearest = Math.Clamp(CellOf(x), minX, maxX);
        var nearestDx = (long)nearest * cellRaw + half - x.Raw;
        var nearestDx2 = nearestDx * nearestDx;
        var left = nearest;
        var right = nearest;
        var firstRow = -1;
        var lastRow = -1;
        long previousRest = -1;
        long tested = 0, written = 0;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var dy = (long)cy * cellRaw + half - y.Raw;
            var rest = limit - dy * dy;
            if (rest < 0 || nearestDx2 > rest) continue;
            if (rest >= previousRest)
            {
                // At least as wide as the last run (on the first row that is the nearest column alone, which is inside): the ends move out while the next cell is inside.
                while (left > minX && Inside(left - 1, cellRaw, half, x.Raw, rest)) { left--; tested++; }
                while (right < maxX && Inside(right + 1, cellRaw, half, x.Raw, rest)) { right++; tested++; }
            }
            else
            {
                // Inside the last run and not empty (the nearest column is inside): the ends move in while they are outside.
                while (!Inside(left, cellRaw, half, x.Raw, rest)) { left++; tested++; }
                while (!Inside(right, cellRaw, half, x.Raw, rest)) { right--; tested++; }
            }
            previousRest = rest;
            tested += 2;
            MarkVisible(cells.AsSpan(cy * CellsX + left, right - left + 1));
            written += right - left + 1;
            if (left < rowMinX[cy]) rowMinX[cy] = left;
            if (right > rowMaxX[cy]) rowMaxX[cy] = right;
            if (firstRow < 0) firstRow = cy;
            lastRow = cy;
        }
        Counters.CellsTested += tested;
        Counters.CellsRevealed += written;
        if (firstRow < 0) return;
        Touch(slot, firstRow, lastRow);
        if (firstRow < _revealLow[slot]) _revealLow[slot] = firstRow;
        if (lastRow > _revealHigh[slot]) _revealHigh[slot] = lastRow;
    }

    private static bool Inside(int cx, long cellRaw, long half, long xRaw, long rest)
    {
        var dx = cx * cellRaw + half - xRaw;
        return dx * dx <= rest;
    }

    private static void MarkVisible(Span<byte> cells)
    {
        if (cells.Length < 8)
        {
            for (var index = 0; index < cells.Length; index++) cells[index] |= VisibleBit | ExploredBit;
            return;
        }
        var words = MemoryMarshal.Cast<byte, ulong>(cells);
        for (var index = 0; index < words.Length; index++) words[index] |= VisibleAndExploredWord;
        // The last up to seven cells as one word that overlaps the cells before: setting the bits twice changes nothing.
        if ((cells.Length & 7) != 0) MemoryMarshal.Cast<byte, ulong>(cells[^8..])[0] |= VisibleAndExploredWord;
    }

    /// <summary>The original reveal, one test per cell of the bounding box: the oracle for <see cref="Reveal"/>.</summary>
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
        // The reference keeps no extents: a later BeginRefresh has to go over the whole slot once.
        TouchAll(slot);
        ExtendRevealToAll(slot);
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
        // A loaded slot may hold visible cells anywhere: the next refresh goes over all of it once.
        TouchAll(slot);
        ExtendRevealToAll(slot);
    }
}
