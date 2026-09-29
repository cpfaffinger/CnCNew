using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Data.World;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Terrain;

/// <summary>Static classification of one pathfinding cell (<c>AIPathfind.cpp classifyMapCell</c>); structures are dynamic and live in <see cref="Navigation.NavigationGrid"/>.</summary>
[Flags]
public enum CellFlags : byte
{
    None = 0,
    Cliff = 1,
    Water = 2,
}

/// <summary>A named map waypoint in world coordinates (<c>Waypoint</c>, <c>TerrainLogic::getWaypointByName</c>).</summary>
public sealed record TerrainWaypoint(string Name, Fixed X, Fixed Y, Fixed Z);

/// <summary>A water polygon (<c>PolygonTrigger::isWaterArea</c>): the level is the z of its first point (<c>TerrainLogic::getWaterHeight</c>).</summary>
public sealed record WaterArea(string Name, ImmutableArray<int> PointX, ImmutableArray<int> PointY, Fixed Level);

/// <summary>
/// The terrain a world plays on, derived once from the decoded <see cref="MapData"/> in fixed point: the height sampler
/// (<c>WorldHeightMap::getHeightMapHeight</c>), the pathfinding cell grid (<see cref="CellSize"/> = <c>PATHFIND_CELL_SIZE</c>), the
/// water areas and the waypoints. Immutable and shared; <see cref="Fingerprint"/> identifies its content in snapshots and replays.
/// World coordinates start at the first playable height vertex; the pathfinding extent is the largest map boundary
/// (<c>W3DTerrainLogic::getMaximumPathfindExtent</c>).
/// </summary>
public sealed class TerrainMap
{
    public const int CellSize = 10;

    private static readonly Fixed CellSizeFixed = Fixed.FromInt(CellSize);
    private static readonly Fixed HeightScale = Fixed.FromDecimal(MapData.HeightScale);
    private readonly ImmutableArray<byte> _heights;
    private readonly int _vertexWidth;
    private readonly int _vertexHeight;
    private readonly int _border;
    private readonly ImmutableArray<CellFlags> _cells;
    private readonly ImmutableArray<WaterArea> _water;

    private TerrainMap(string name, MapData map, int cellsX, int cellsY, ImmutableArray<WaterArea> water, ImmutableArray<TerrainWaypoint> waypoints)
    {
        var field = map.HeightField;
        Name = name;
        _heights = field.Heights;
        _vertexWidth = field.Width;
        _vertexHeight = field.Height;
        _border = field.Border;
        CellsX = cellsX;
        CellsY = cellsY;
        _water = water;
        Waypoints = waypoints;
        var cells = new CellFlags[cellsX * cellsY];
        for (var cy = 0; cy < cellsY; cy++)
            for (var cx = 0; cx < cellsX; cx++) cells[cy * cellsX + cx] = Classify(map.BlendTiles, cx, cy);
        _cells = [..cells];
        Fingerprint = ComputeFingerprint();
    }

    public string Name { get; }
    public string Fingerprint { get; }
    public int CellsX { get; }
    public int CellsY { get; }
    public Fixed ExtentX => CellSizeFixed * CellsX;
    public Fixed ExtentY => CellSizeFixed * CellsY;
    public ImmutableArray<TerrainWaypoint> Waypoints { get; }
    public ImmutableArray<WaterArea> WaterAreas => _water;

    /// <summary>Waypoints named <c>Player_N_Start</c> (N from 1), ordered by N: the skirmish start positions.</summary>
    public ImmutableArray<TerrainWaypoint> StartPositions
    {
        get
        {
            var starts = new SortedDictionary<int, TerrainWaypoint>();
            foreach (var waypoint in Waypoints)
                if (TryParseStart(waypoint.Name, out var index)) starts[index] = waypoint;
            return [..starts.Values];
        }
    }

    public static bool TryParseStart(string name, out int index)
    {
        index = 0;
        const string prefix = "Player_";
        const string suffix = "_Start";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > prefix.Length + suffix.Length
            && int.TryParse(name.AsSpan(prefix.Length, name.Length - prefix.Length - suffix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out index) && index > 0;
    }

    public TerrainWaypoint? FindWaypoint(string name)
    {
        foreach (var waypoint in Waypoints)
            if (waypoint.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return waypoint;
        return null;
    }

    public static TerrainMap Build(MapData map, string name)
    {
        ArgumentNullException.ThrowIfNull(map);
        var field = map.HeightField;
        if (field.Width < 2 || field.Height < 2) throw new InvalidDataException("A map needs at least 2x2 height vertices.");
        if (field.Border < 0 || 2 * field.Border >= Math.Min(field.Width, field.Height)) throw new InvalidDataException($"Map border {field.Border} leaves no playable area.");
        if (field.Boundaries.IsDefaultOrEmpty) throw new InvalidDataException("The map has no boundary.");
        var cellsX = 0;
        var cellsY = 0;
        foreach (var boundary in field.Boundaries)
        {
            cellsX = Math.Max(cellsX, boundary.X);
            cellsY = Math.Max(cellsY, boundary.Y);
        }
        if (cellsX <= 0 || cellsY <= 0) throw new InvalidDataException($"Map boundary {cellsX}x{cellsY} is empty.");
        // Two shipped maps declare a boundary larger than their height field; the reference then reads the clamped edge height for
        // the cells beyond it (getExtent uses the boundary, WorldHeightMap clamps the sample position), and so does this model.

        var water = ImmutableArray.CreateBuilder<WaterArea>();
        foreach (var trigger in map.Triggers)
        {
            if (!trigger.IsWater || trigger.Points.IsDefaultOrEmpty) continue;
            water.Add(new WaterArea(trigger.Name, [..trigger.Points.Select(point => point.X)], [..trigger.Points.Select(point => point.Y)],
                Fixed.FromInt(trigger.Points[0].Z)));
        }

        var waypoints = ImmutableArray.CreateBuilder<TerrainWaypoint>();
        foreach (var obj in map.Objects)
        {
            if (!obj.IsWaypoint) continue;
            var waypointName = obj.Properties.GetText("waypointName");
            if (string.IsNullOrEmpty(waypointName)) continue;
            waypoints.Add(new TerrainWaypoint(waypointName, Fixed.FromDecimal(obj.X), Fixed.FromDecimal(obj.Y), Fixed.FromDecimal(obj.Z)));
        }
        waypoints.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name) is var order and not 0 ? order : a.X.Raw.CompareTo(b.X.Raw) is var xo and not 0 ? xo : a.Y.Raw.CompareTo(b.Y.Raw));

        return new TerrainMap(name, map, cellsX, cellsY, water.ToImmutable(), waypoints.ToImmutable());
    }

    /// <summary><c>classifyMapCell</c>: CLIFF when the height cell is a cliff, WATER when any of its four corners lies below the water level at that corner.</summary>
    private CellFlags Classify(MapBlendTiles blend, int cx, int cy)
    {
        var flags = CellFlags.None;
        var hx = Math.Min(cx + _border, _vertexWidth - 2);
        var hy = Math.Min(cy + _border, _vertexHeight - 2);
        if (blend.IsCliff(hx, hy)) flags |= CellFlags.Cliff;
        if (_water.Length > 0)
            for (var corner = 0; corner < 4; corner++)
            {
                var x = (cx + (corner & 1)) * CellSize;
                var y = (cy + (corner >> 1)) * CellSize;
                if (TryWaterLevel(Fixed.FromInt(x), Fixed.FromInt(y), out var level) && HeightAt(Fixed.FromInt(x), Fixed.FromInt(y)) < level)
                {
                    flags |= CellFlags.Water;
                    break;
                }
            }
        return flags;
    }

    public bool IsInside(int cx, int cy) => (uint)cx < (uint)CellsX && (uint)cy < (uint)CellsY;

    public CellFlags CellAt(int cx, int cy) => _cells[cy * CellsX + cx];

    public static int CellOf(Fixed coordinate) => checked((int)(coordinate.Floor() / CellSize));

    /// <summary>Center of a pathfinding cell in world coordinates.</summary>
    public static Fixed CellCenter(int cell) => Fixed.FromInt(cell * CellSize) + Fixed.FromInt(CellSize / 2);

    /// <summary><c>WorldHeightMap::getHeightMapHeight</c>: two triangles per height cell, split along the (ix,iy)-(ix+1,iy+1) diagonal; positions outside the height field clamp to its edge.</summary>
    public Fixed HeightAt(Fixed x, Fixed y)
    {
        var gx = x / CellSizeFixed + Fixed.FromInt(_border);
        var gy = y / CellSizeFixed + Fixed.FromInt(_border);
        var maxX = Fixed.FromInt(_vertexWidth - 1);
        var maxY = Fixed.FromInt(_vertexHeight - 1);
        gx = Fixed.Min(Fixed.Max(gx, Fixed.Zero), maxX);
        gy = Fixed.Min(Fixed.Max(gy, Fixed.Zero), maxY);
        var ix = (int)Math.Min(gx.Floor(), _vertexWidth - 2);
        var iy = (int)Math.Min(gy.Floor(), _vertexHeight - 2);
        var fx = gx - Fixed.FromInt(ix);
        var fy = gy - Fixed.FromInt(iy);
        Fixed P(int px, int py) => Fixed.FromInt(_heights[py * _vertexWidth + px]);
        var p0 = P(ix, iy);
        var p1 = P(ix + 1, iy);
        var p2 = P(ix + 1, iy + 1);
        var p3 = P(ix, iy + 1);
        var raw = fy > fx
            ? p3 + (Fixed.One - fy) * (p0 - p3) + fx * (p2 - p3)
            : p1 + fy * (p2 - p1) + (Fixed.One - fx) * (p0 - p1);
        return raw * HeightScale;
    }

    /// <summary><c>TerrainLogic::getWaterHandle</c> restricted to polygon water: the highest level among the areas containing the point (rounded to a whole coordinate first).</summary>
    public bool TryWaterLevel(Fixed x, Fixed y, out Fixed level)
    {
        level = Fixed.Zero;
        var found = false;
        var px = (int)(x + Fixed.FromRaw(Fixed.ScaleRaw / 2)).Floor();
        var py = (int)(y + Fixed.FromRaw(Fixed.ScaleRaw / 2)).Floor();
        foreach (var area in _water)
        {
            if (!PointInPolygon(area, px, py)) continue;
            if (!found || area.Level >= level)
            {
                level = area.Level;
                found = true;
            }
        }
        return found;
    }

    /// <summary><c>TerrainLogic::isUnderwater</c>: water at the point and the terrain below its level.</summary>
    public bool IsUnderwater(Fixed x, Fixed y) => TryWaterLevel(x, y, out var level) && HeightAt(x, y) < level;

    /// <summary>Even-odd ray cast (<c>PolygonTrigger::pointInTrigger</c>).</summary>
    private static bool PointInPolygon(WaterArea area, int x, int y)
    {
        var count = area.PointX.Length;
        var inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var xi = (long)area.PointX[i];
            var yi = (long)area.PointY[i];
            var xj = (long)area.PointX[j];
            var yj = (long)area.PointY[j];
            if ((yi > y) == (yj > y)) continue;
            // x < xi + (y - yi) * (xj - xi) / (yj - yi), compared without division (the sign of the divisor flips the inequality).
            var lhs = (x - xi) * (yj - yi);
            var rhs = (y - yi) * (xj - xi);
            if (yj > yi ? lhs < rhs : lhs > rhs) inside = !inside;
        }
        return inside;
    }

    private string ComputeFingerprint()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(_vertexWidth);
            writer.Write(_vertexHeight);
            writer.Write(_border);
            writer.Write(CellsX);
            writer.Write(CellsY);
            writer.Write(_heights.AsSpan());
            foreach (var flags in _cells) writer.Write((byte)flags);
            writer.Write(_water.Length);
            foreach (var area in _water)
            {
                writer.Write(area.Name);
                writer.Write(area.Level.Raw);
                writer.Write(area.PointX.Length);
                for (var i = 0; i < area.PointX.Length; i++)
                {
                    writer.Write(area.PointX[i]);
                    writer.Write(area.PointY[i]);
                }
            }
            writer.Write(Waypoints.Length);
            foreach (var waypoint in Waypoints)
            {
                writer.Write(waypoint.Name);
                writer.Write(waypoint.X.Raw);
                writer.Write(waypoint.Y.Raw);
                writer.Write(waypoint.Z.Raw);
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
