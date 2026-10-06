using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Presentation.Vision;

public enum FogCellState : byte
{
    /// <summary>Never seen (<c>CELLSHROUD_SHROUDED</c>).</summary>
    Unexplored = 0,
    /// <summary>Seen before, not in sight now (<c>CELLSHROUD_FOGGED</c>).</summary>
    Fogged = 1,
    /// <summary>In sight (<c>CELLSHROUD_CLEAR</c>).</summary>
    Visible = 2,
}

/// <summary>
/// Brightness levels of the three shroud states (<c>W3DShroud</c>: the terrain colour is multiplied by level / 255) and the fade speed of the
/// presentation. The levels come from <c>GameData.ini</c> <c>ShroudAlpha</c>, <c>FogAlpha</c>, <c>ClearAlpha</c>; the fade is this client's own
/// choice (the reference switches instantly), in level units per second.
/// </summary>
public sealed record FogSettings(byte ShroudLevel, byte FogLevel, byte ClearLevel, float FadeLevelsPerSecond)
{
    public static FogSettings FromDefinitions(CompiledDefinitionGraph graph, float fadeLevelsPerSecond)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        byte Level(string key)
        {
            var value = WorldDefinitionReader.Integer(gameData, key);
            return value is >= 0 and <= 255 ? (byte)value : throw new InvalidDataException($"GameData {key} {value} is outside 0..255.");
        }
        return new FogSettings(Level("ShroudAlpha"), Level("FogAlpha"), Level("ClearAlpha"), fadeLevelsPerSecond);
    }

    public byte LevelOf(FogCellState state) => state switch
    {
        FogCellState.Visible => ClearLevel,
        FogCellState.Fogged => FogLevel,
        _ => ShroudLevel,
    };
}

/// <summary>
/// The fog of war of one player for the renderer: per pathfinding cell (10 world units, the simulation's <c>VisionMap</c> grid) the state from
/// the latest <see cref="Update"/> and a brightness level that fades towards the state's level in <see cref="Advance"/>. Cell (cx, cy) covers
/// world [cx*10, cx*10+10) x [cy*10, cy*10+10); texel (cx, cy) of <see cref="CopyLevels"/> is row-major from the south-west, so a renderer maps
/// world (x, y) to texture coordinates (x / (CellsX*10), y / (CellsY*10)) and samples bilinearly (the reference filters its shroud texture the
/// same way). Cells outside the grid count as unexplored.
/// </summary>
public sealed class FogOfWarGrid
{
    private readonly FogCellState[] _states;
    private readonly float[] _levels;
    private readonly FogSettings _settings;
    // The cells whose level has not reached the level of their state yet. Every other cell is at rest: Advance would compute the value it already has, so only
    // these are visited. A cell enters when its state changes to one with another level and leaves when its level arrives.
    private readonly int[] _fading;
    private readonly bool[] _isFading;
    private int _fadingCount;

    public FogOfWarGrid(int cellsX, int cellsY, FogSettings settings)
    {
        if (cellsX < 1 || cellsY < 1) throw new ArgumentOutOfRangeException(nameof(cellsX), "The fog grid needs at least one cell.");
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (!(settings.FadeLevelsPerSecond > 0)) throw new ArgumentOutOfRangeException(nameof(settings), "The fade speed must be positive.");
        CellsX = cellsX;
        CellsY = cellsY;
        _states = new FogCellState[cellsX * cellsY];
        _levels = new float[cellsX * cellsY];
        Array.Fill(_levels, settings.ShroudLevel);
        _fading = new int[cellsX * cellsY];
        _isFading = new bool[cellsX * cellsY];
    }

    public int CellsX { get; }
    public int CellsY { get; }
    public FogSettings Settings => _settings;

    public static FogOfWarGrid ForVision(VisionMap vision, FogSettings settings) => new(vision.CellsX, vision.CellsY, settings);

    /// <summary>Takes the player's current vision (the simulation refreshes it every <c>VisionIntervalTicks</c>). The levels keep fading.</summary>
    public void Update(VisionMap vision, int playerSlot)
    {
        ArgumentNullException.ThrowIfNull(vision);
        if (vision.CellsX != CellsX || vision.CellsY != CellsY) throw new ArgumentException("The vision map does not match the fog grid.", nameof(vision));
        if ((uint)playerSlot >= (uint)vision.PlayerSlots) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        for (var cy = 0; cy < CellsY; cy++)
            for (var cx = 0; cx < CellsX; cx++)
                SetState(cy * CellsX + cx, vision.IsVisible(playerSlot, cx, cy) ? FogCellState.Visible
                    : vision.IsExplored(playerSlot, cx, cy) ? FogCellState.Fogged : FogCellState.Unexplored);
    }

    private void SetState(int index, FogCellState state)
    {
        if (_states[index] == state) return;
        _states[index] = state;
        if (_isFading[index] || _levels[index] == _settings.LevelOf(state)) return;
        _isFading[index] = true;
        _fading[_fadingCount++] = index;
    }

    /// <summary>Sets every cell state directly (tests, replays of saved fog); levels keep fading.</summary>
    public void SetStates(ReadOnlySpan<FogCellState> states)
    {
        if (states.Length != _states.Length) throw new ArgumentException("State count does not match the grid.", nameof(states));
        for (var index = 0; index < states.Length; index++) SetState(index, states[index]);
    }

    /// <summary>Cells whose level is still on its way to the level of their state.</summary>
    public int FadingCells => _fadingCount;

    /// <summary>Moves every level towards its state's level by at most <c>FadeLevelsPerSecond * dt</c>.</summary>
    public void Advance(float dtSeconds)
    {
        if (dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        var step = _settings.FadeLevelsPerSecond * dtSeconds;
        for (var slot = 0; slot < _fadingCount;)
        {
            var index = _fading[slot];
            float target = _settings.LevelOf(_states[index]);
            var current = _levels[index];
            var next = current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
            _levels[index] = next;
            if (next == target)
            {
                // Arrived: the last fading cell takes this slot and is visited next.
                _isFading[index] = false;
                _fading[slot] = _fading[--_fadingCount];
            }
            else slot++;
        }
    }

    /// <summary>Jumps every level to its state's level (after loading or a camera cut).</summary>
    public void Snap()
    {
        for (var index = 0; index < _levels.Length; index++) _levels[index] = _settings.LevelOf(_states[index]);
        Array.Clear(_isFading);
        _fadingCount = 0;
    }

    public bool InBounds(int cx, int cy) => (uint)cx < (uint)CellsX && (uint)cy < (uint)CellsY;

    public FogCellState StateAt(int cx, int cy) => InBounds(cx, cy) ? _states[cy * CellsX + cx] : FogCellState.Unexplored;

    public byte LevelAt(int cx, int cy) => InBounds(cx, cy) ? (byte)MathF.Round(_levels[cy * CellsX + cx]) : _settings.ShroudLevel;

    /// <summary>Entities of other players are drawn only in visible cells.</summary>
    public bool IsVisible(int cx, int cy) => StateAt(cx, cy) == FogCellState.Visible;

    public bool IsExplored(int cx, int cy) => StateAt(cx, cy) != FogCellState.Unexplored;

    public bool IsVisibleAt(Vector2 world) => IsVisible(CellOf(world.X), CellOf(world.Y));

    public bool IsExploredAt(Vector2 world) => IsExplored(CellOf(world.X), CellOf(world.Y));

    public static int CellOf(float coordinate) => (int)MathF.Floor(coordinate / TerrainMap.CellSize);

    public static Vector2 CellCenter(int cx, int cy) => new((cx + 0.5f) * TerrainMap.CellSize, (cy + 0.5f) * TerrainMap.CellSize);

    /// <summary>One byte per cell, row-major from cell (0, 0): the current faded levels.</summary>
    public void CopyLevels(Span<byte> destination)
    {
        if (destination.Length < _levels.Length) throw new ArgumentException("Destination is too small.", nameof(destination));
        for (var index = 0; index < _levels.Length; index++) destination[index] = (byte)MathF.Round(_levels[index]);
    }

    /// <summary>
    /// The levels upscaled by <paramref name="factor"/> texels per cell with bilinear filtering between cell centres (edges clamp): what the
    /// GPU would sample from the per-cell texture, for renderers that want a pre-smoothed texture. Row-major from the south-west.
    /// </summary>
    public byte[] BuildSmoothedLevels(int factor)
    {
        if (factor < 1 || factor > 16) throw new ArgumentOutOfRangeException(nameof(factor));
        var width = CellsX * factor;
        var height = CellsY * factor;
        var result = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var gy = (y + 0.5f) / factor - 0.5f;
            var y0 = Math.Clamp((int)MathF.Floor(gy), 0, CellsY - 1);
            var y1 = Math.Min(y0 + 1, CellsY - 1);
            var fy = Math.Clamp(gy - y0, 0f, 1f);
            for (var x = 0; x < width; x++)
            {
                var gx = (x + 0.5f) / factor - 0.5f;
                var x0 = Math.Clamp((int)MathF.Floor(gx), 0, CellsX - 1);
                var x1 = Math.Min(x0 + 1, CellsX - 1);
                var fx = Math.Clamp(gx - x0, 0f, 1f);
                var top = _levels[y0 * CellsX + x0] * (1 - fx) + _levels[y0 * CellsX + x1] * fx;
                var bottom = _levels[y1 * CellsX + x0] * (1 - fx) + _levels[y1 * CellsX + x1] * fx;
                result[y * width + x] = (byte)MathF.Round(top * (1 - fy) + bottom * fy);
            }
        }
        return result;
    }
}
