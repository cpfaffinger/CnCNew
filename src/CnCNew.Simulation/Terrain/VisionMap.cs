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
    /// <summary>Refreshes started (<see cref="VisionMap.BeginRefresh"/> or <see cref="VisionMap.BeginCollectedRefresh"/>).</summary>
    public long Refreshes;
    /// <summary>Cell bytes a refresh went over to forget what was visible (the whole table in the reference mode).</summary>
    public long CellsCleared;
    /// <summary>Calls of <see cref="VisionMap.Reveal"/> with a positive radius (in the reference mode also those beside the map).</summary>
    public long Circles;
    /// <summary>Slots a refresh left alone because their circles were the same as in the refresh before.</summary>
    public long SlotsUnchanged;
    /// <summary>Slots a refresh patched: only the circles that went were forgotten and the surroundings drawn again.</summary>
    public long SlotsPatched;
    /// <summary>Slots a refresh forgot and drew completely.</summary>
    public long SlotsRedrawn;
    /// <summary>Circles that were not drawn in full because they were there in the refresh before (in a slot left alone or patched).</summary>
    public long CirclesSkipped;
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
/// sight instead of the map area: the circles of the last refresh per slot (a slot whose circles did not change is left alone, one with few
/// changes is patched around them), the row extents the circles wrote (so a full redraw clears only those), the rows touched
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
    private readonly List<Circle>[] _pending; // per slot: the circles of the refresh in progress
    private readonly List<Circle>[] _drawn;   // per slot: the circles the cells currently show, sorted
    private readonly List<Circle> _gone = [];  // work lists of EndRefresh: circles of the last refresh that are no longer there, ...
    private readonly List<Circle> _stayed = []; // ... that are there again, ...
    private readonly List<Circle> _came = [];   // ... and new ones
    private readonly bool[] _redraw;          // per slot: the cells were written by something else than a collected refresh since it last drew them
    private bool _collecting;                 // between BeginCollectedRefresh and EndCollectedRefresh
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
        _pending = new List<Circle>[playerSlots];
        _drawn = new List<Circle>[playerSlots];
        _redraw = new bool[playerSlots];
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
            _pending[slot] = [];
            _drawn[slot] = [];
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
    /// A hook for tests and measuring tools, not state: while set, a refresh (plain or collected, with its <see cref="Reveal"/> calls) and <see cref="CurrentHash"/> run
    /// the original computation over every cell (<see cref="BeginRefreshReference"/>, <see cref="RevealReference"/>, <see cref="Hash"/>). The cell
    /// bytes and the hash value are the same in both modes; only the work differs. May be switched between two refreshes.
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

    /// <summary>Something other than the refresh wrote cells of the slot: rows to compare for the hash, and the next refresh must draw the slot again.</summary>
    private void Written(int slot, int lowRow, int highRow)
    {
        Touch(slot, lowRow, highRow);
        _redraw[slot] = true;
    }

    private void WrittenAll(int slot) => Written(slot, 0, CellsY - 1);

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
    /// Forgets what is currently visible, at once; exploration stays. The <see cref="Reveal"/> calls that follow write their cells at once too.
    /// This is the plain form for callers that draw a few circles by hand; the world's refresh uses <see cref="BeginCollectedRefresh"/>.
    /// </summary>
    public void BeginRefresh()
    {
        if (_collecting) throw new InvalidOperationException("A collected vision refresh is in progress.");
        if (ReferenceMode)
        {
            BeginRefreshReference();
            return;
        }
        Counters.Refreshes++;
        for (var slot = 0; slot < _cells.Length; slot++)
        {
            ForgetVisible(slot);
            // The circles kept from the last collected refresh no longer describe the cells.
            _redraw[slot] = true;
        }
    }

    /// <summary>
    /// Starts a refresh whose circles are collected: what is visible is forgotten, exploration stays, and the <see cref="Reveal"/> calls up to
    /// <see cref="EndCollectedRefresh"/> say what is visible now. The cells are written in <see cref="EndCollectedRefresh"/>; nothing may read
    /// them in between. Collecting is what lets a refresh leave alone what did not change.
    /// </summary>
    public void BeginCollectedRefresh()
    {
        if (_collecting) throw new InvalidOperationException("A collected vision refresh is already in progress.");
        _collecting = true;
        if (ReferenceMode)
        {
            BeginRefreshReference();
            return;
        }
        Counters.Refreshes++;
        foreach (var circles in _pending) circles.Clear();
    }

    /// <summary>
    /// Ends a collected refresh. The cells afterwards are those <see cref="BeginRefreshReference"/> followed by one <see cref="RevealReference"/> per circle
    /// leave behind (visible exactly where a circle of this refresh or a permanent mark is; exploration only grows). Per slot, cheapest first:
    /// <list type="bullet">
    /// <item>The circles are those of the refresh before and nothing else wrote the cells since: the slot is left alone. Forgetting the visible
    /// cells and drawing the same circles again would end with the bytes it started from.</item>
    /// <item>Some circles went, some came: only the cells of the circles that went are forgotten; then every circle that stayed is drawn again
    /// inside the bounding box of each circle that went, and the new circles are drawn. Outside the circles that went nothing was forgotten, so
    /// the circles that stayed still show there.</item>
    /// <item>Something else wrote the cells (a script, a load, a reference pass), or patching would go over at least as many cells as drawing
    /// everything (many circles moved, or the ones that moved overlap many that stayed): everything visible in the slot is forgotten and every
    /// circle drawn.</item>
    /// </list>
    /// </summary>
    public void EndCollectedRefresh()
    {
        if (!_collecting) throw new InvalidOperationException("No collected vision refresh is in progress.");
        _collecting = false;
        if (ReferenceMode) return;
        for (var slot = 0; slot < _cells.Length; slot++)
        {
            var pending = _pending[slot];
            var drawn = _drawn[slot];
            // The order of the circles does not matter to the cells (bits are only set); sorted, two refreshes compare circle by circle.
            pending.Sort();
            if (_redraw[slot])
            {
                Redraw(slot, pending);
            }
            else if (CollectionsMarshal.AsSpan(pending).SequenceEqual(CollectionsMarshal.AsSpan(drawn)))
            {
                Counters.SlotsUnchanged++;
                Counters.CirclesSkipped += pending.Count;
                continue;
            }
            else
            {
                _gone.Clear();
                _stayed.Clear();
                _came.Clear();
                int old = 0, now = 0;
                while (old < drawn.Count && now < pending.Count)
                {
                    var order = drawn[old].CompareTo(pending[now]);
                    if (order == 0)
                    {
                        _stayed.Add(pending[now]);
                        old++;
                        now++;
                    }
                    else if (order < 0) _gone.Add(drawn[old++]);
                    else _came.Add(pending[now++]);
                }
                while (old < drawn.Count) _gone.Add(drawn[old++]);
                while (now < pending.Count) _came.Add(pending[now++]);
                // Both ways end with the same cells; the choice is by work, counted in cells of the boxes each way goes over.
                long redrawCells = 0, patchCells = 0;
                foreach (var circle in pending) redrawCells += circle.BoxCells;
                foreach (var circle in _gone) patchCells += circle.BoxCells;
                foreach (var circle in _came) patchCells += circle.BoxCells;
                foreach (var circle in _stayed)
                {
                    if (patchCells >= redrawCells) break;
                    foreach (var hole in _gone)
                    {
                        var width = Math.Min(circle.MaxX, hole.MaxX) - Math.Max(circle.MinX, hole.MinX) + 1;
                        var height = Math.Min(circle.MaxY, hole.MaxY) - Math.Max(circle.MinY, hole.MinY) + 1;
                        if (width > 0 && height > 0) patchCells += (long)width * height;
                    }
                }
                if (patchCells >= redrawCells)
                {
                    Redraw(slot, pending);
                }
                else
                {
                    Counters.SlotsPatched++;
                    Counters.CirclesSkipped += _stayed.Count;
                    foreach (var circle in _gone) Trace(slot, circle, circle, forget: true);
                    foreach (var circle in _stayed)
                        foreach (var hole in _gone)
                            if (circle.MinX <= hole.MaxX && hole.MinX <= circle.MaxX && circle.MinY <= hole.MaxY && hole.MinY <= circle.MaxY)
                                Trace(slot, circle, hole, forget: false);
                    foreach (var circle in _came) Trace(slot, circle, circle, forget: false);
                }
            }
            _drawn[slot] = pending;
            _pending[slot] = drawn;
            _redraw[slot] = false;
        }
    }

    private void Redraw(int slot, List<Circle> circles)
    {
        Counters.SlotsRedrawn++;
        ForgetVisible(slot);
        foreach (var circle in circles) Trace(slot, circle, circle, forget: false);
    }

    /// <summary>
    /// The refresh start for one slot. The same bytes as <see cref="BeginRefreshReference"/>, which goes over every cell: that pass changes a
    /// cell only if it is visible without the permanent mark (a permanent cell always holds all three bits), and such cells exist only where
    /// a circle was drawn since the slot was last cleared or where <see cref="Load"/> put them — the recorded row extents.
    /// </summary>
    private void ForgetVisible(int slot)
    {
        var low = _revealLow[slot];
        var high = _revealHigh[slot];
        if (low > high) return;
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

    /// <summary>The original refresh start over every cell of every slot: the oracle for a refresh.</summary>
    public void BeginRefreshReference()
    {
        Counters.Refreshes++;
        for (var slot = 0; slot < _cells.Length; slot++)
        {
            var cells = _cells[slot];
            for (var index = 0; index < cells.Length; index++)
                cells[index] = (cells[index] & PermanentBit) != 0 ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~VisibleBit);
            Counters.CellsCleared += cells.Length;
            WrittenAll(slot);
        }
    }

    /// <summary><c>PartitionManager::revealMapForPlayerPermanently</c> / <c>undoRevealMapForPlayerPermanently</c>: every cell of the slot is visible for good, or the permanent mark is dropped again (what was seen stays explored).</summary>
    public void SetPermanentRevealAll(int slot, bool revealed)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++)
            cells[index] = revealed ? unchecked((byte)(VisibleBit | ExploredBit | PermanentBit)) : (byte)(cells[index] & ~(PermanentBit | VisibleBit));
        WrittenAll(slot);
    }

    /// <summary>The "quick look" of <c>MAP_REVEAL_ALL</c>: every cell becomes explored (fogged), nothing becomes visible.</summary>
    public void ExploreAll(int slot)
    {
        var cells = _cells[slot];
        for (var index = 0; index < cells.Length; index++) cells[index] |= ExploredBit;
        WrittenAll(slot);
    }

    /// <summary><c>shroudMapForPlayer</c>: every cell of the slot forgets what it knew (visible, explored and permanent marks cleared).</summary>
    public void ShroudAll(int slot)
    {
        Array.Clear(_cells[slot]);
        WrittenAll(slot);
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
        if (minY <= maxY) Written(slot, minY, maxY);
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
    /// Every cell whose centre lies within <paramref name="radius"/> of the point is visible and explored for the slot: at once, or, inside a
    /// collected refresh, when it ends.
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
        var minY = Math.Max(0, CellOf(y - radius));
        var maxY = Math.Min(CellsY - 1, CellOf(y + radius));
        var minX = Math.Max(0, CellOf(x - radius));
        var maxX = Math.Min(CellsX - 1, CellOf(x + radius));
        // A circle beside the map covers no cell: it is not part of what the slot shows.
        if (minY > maxY || minX > maxX) return;
        var circle = new Circle(x.Raw, y.Raw, radius.Raw, minX, maxX, minY, maxY, CellOf(x));
        if (_collecting)
        {
            _pending[slot].Add(circle);
            return;
        }
        Trace(slot, circle, circle, forget: false);
        // Drawn outside a collected refresh: the circles kept from the last one no longer describe the cells.
        _redraw[slot] = true;
    }

    /// <summary>
    /// A circle of a refresh with the cell box <see cref="RevealReference"/> would go over (clamped to the map) and the column under its
    /// centre. Two circles are the same when centre and radius are; the rest follows from them.
    /// </summary>
    private readonly record struct Circle(long X, long Y, long Radius, int MinX, int MaxX, int MinY, int MaxY, int CentreColumn) : IComparable<Circle>
    {
        public int CompareTo(Circle other)
            => X != other.X ? X.CompareTo(other.X) : Y != other.Y ? Y.CompareTo(other.Y) : Radius.CompareTo(other.Radius);

        public long BoxCells => (long)(MaxX - MinX + 1) * (MaxY - MinY + 1);

        public bool Equals(Circle other) => X == other.X && Y == other.Y && Radius == other.Radius;

        // Never used as a key; defined with Equals so that the two cannot disagree.
        public override int GetHashCode() => 0;
    }

    /// <summary>
    /// Marks every cell whose centre lies within the circle as visible and explored for the slot, or (<paramref name="forget"/>) applies the
    /// refresh start to those cells; only inside the cell box of <paramref name="clip"/>. The same cells as <see cref="RevealReference"/>, which
    /// tests every cell of the bounding box: within a row the squared distance is convex in the column, so the cells inside the circle are one
    /// run of columns around the column nearest to the centre. A row nearer to the centre's row than the one before holds a run that contains
    /// the previous one, a row farther away a run inside it, so the two ends only ever walk outwards or inwards from the previous row's; the
    /// run is then written at once. Clipping keeps all of that: the nearest column inside the clipped columns takes the place of the nearest.
    /// </summary>
    private void Trace(int slot, in Circle circle, in Circle clip, bool forget)
    {
        var cellRaw = Fixed.FromInt(TerrainMap.CellSize).Raw;
        var minY = Math.Max(circle.MinY, clip.MinY);
        var maxY = Math.Min(circle.MaxY, clip.MaxY);
        var minX = Math.Max(circle.MinX, clip.MinX);
        var maxX = Math.Min(circle.MaxX, clip.MaxX);
        if (minY > maxY || minX > maxX) return;
        var cells = _cells[slot];
        var rowMinX = _revealMinX[slot];
        var rowMaxX = _revealMaxX[slot];
        var limit = circle.Radius * circle.Radius;
        var half = TerrainMap.CellSize * 65536L / 2;
        // The column in range whose centre is nearest to the point: if it is outside the circle in a row, every column of that row is.
        var nearest = Math.Clamp(circle.CentreColumn, minX, maxX);
        var nearestDx = (long)nearest * cellRaw + half - circle.X;
        var nearestDx2 = nearestDx * nearestDx;
        var left = nearest;
        var right = nearest;
        var firstRow = -1;
        var lastRow = -1;
        long previousRest = -1;
        long tested = 0, written = 0;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var dy = (long)cy * cellRaw + half - circle.Y;
            var rest = limit - dy * dy;
            if (rest < 0 || nearestDx2 > rest) continue;
            if (rest >= previousRest)
            {
                // At least as wide as the last run (on the first row that is the nearest column alone, which is inside): the ends move out while the next cell is inside.
                while (left > minX && Inside(left - 1, cellRaw, half, circle.X, rest)) { left--; tested++; }
                while (right < maxX && Inside(right + 1, cellRaw, half, circle.X, rest)) { right++; tested++; }
            }
            else
            {
                // Inside the last run and not empty (the nearest column is inside): the ends move in while they are outside.
                while (!Inside(left, cellRaw, half, circle.X, rest)) { left++; tested++; }
                while (!Inside(right, cellRaw, half, circle.X, rest)) { right--; tested++; }
            }
            previousRest = rest;
            tested += 2;
            var run = cells.AsSpan(cy * CellsX + left, right - left + 1);
            written += run.Length;
            if (forget)
            {
                ForgetVisible(run);
            }
            else
            {
                MarkVisible(run);
                if (left < rowMinX[cy]) rowMinX[cy] = left;
                if (right > rowMaxX[cy]) rowMaxX[cy] = right;
            }
            if (firstRow < 0) firstRow = cy;
            lastRow = cy;
        }
        Counters.CellsTested += tested;
        if (forget) Counters.CellsCleared += written;
        else Counters.CellsRevealed += written;
        if (firstRow < 0) return;
        Touch(slot, firstRow, lastRow);
        if (forget) return;
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

    /// <summary>The original reveal, one test per cell of the bounding box: the oracle for a refresh's circles.</summary>
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
        // The reference keeps no extents: a later refresh has to go over the whole slot once.
        WrittenAll(slot);
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
        WrittenAll(slot);
        ExtendRevealToAll(slot);
    }
}
