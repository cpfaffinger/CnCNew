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
/// <remarks><see cref="Id"/> is the map's <c>waypointID</c> and <see cref="NextId"/> the target of its first link (<c>Waypoint::getLink(0)</c>), both -1 when absent (a waypoint built by hand).</remarks>
public sealed record TerrainWaypoint(string Name, Fixed X, Fixed Y, Fixed Z, int Id = -1, int NextId = -1);

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
    private readonly Fixed _maxHeight;
    private readonly ImmutableArray<CellFlags> _cells;
    private readonly ImmutableArray<WaterArea> _water;
    private readonly Dictionary<int, TerrainWaypoint> _waypointsById;

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
        _waypointsById = waypoints.Where(value => value.Id >= 0).GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var cells = new CellFlags[cellsX * cellsY];
        for (var cy = 0; cy < cellsY; cy++)
            for (var cx = 0; cx < cellsX; cx++) cells[cy * cellsX + cx] = Classify(map.BlendTiles, cx, cy);
        _cells = [..cells];
        // BaseHeightMapRenderObjClass::getMaxHeight: the highest vertex of the whole field.
        var highest = 0;
        foreach (var height in _heights) highest = Math.Max(highest, height);
        _maxHeight = Fixed.FromInt(highest) * HeightScale;
        Fingerprint = ComputeFingerprint(map);
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

    /// <summary><c>Waypoint::getLink(0)</c>: the waypoint a link of <paramref name="waypoint"/> leads to, or <see langword="null"/> at the end of a path.</summary>
    public TerrainWaypoint? NextWaypoint(TerrainWaypoint waypoint)
        => waypoint.NextId >= 0 && _waypointsById.TryGetValue(waypoint.NextId, out var next) ? next : null;

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

        var links = new Dictionary<int, int>();
        foreach (var link in map.WaypointLinks) links.TryAdd(link.From, link.To);
        var waypoints = ImmutableArray.CreateBuilder<TerrainWaypoint>();
        foreach (var obj in map.Objects)
        {
            if (!obj.IsWaypoint) continue;
            var waypointName = obj.Properties.GetText("waypointName");
            if (string.IsNullOrEmpty(waypointName)) continue;
            var id = obj.Properties.TryGetInt("waypointID", out var waypointId) ? waypointId : -1;
            waypoints.Add(new TerrainWaypoint(waypointName, Fixed.FromDecimal(obj.X), Fixed.FromDecimal(obj.Y), Fixed.FromDecimal(obj.Z), id, id >= 0 && links.TryGetValue(id, out var next) ? next : -1));
        }
        waypoints.Sort(CompareWaypoints);

        return new TerrainMap(name, map, cellsX, cellsY, water.ToImmutable(), waypoints.ToImmutable());
    }

    /// <summary><c>classifyMapCell</c>: CLIFF when the height cell is a cliff, WATER when any of its four corners lies below the water level at that corner.</summary>
    /// <summary>
    /// The canonical waypoint order. It compares every field, so two waypoints that tie are equal in everything and their order cannot matter: the sort is not
    /// stable, and a comparison that left a field out would make the order of such waypoints a property of the file order and of the runtime's sort routine.
    /// </summary>
    public static int CompareWaypoints(TerrainWaypoint a, TerrainWaypoint b)
    {
        var order = string.CompareOrdinal(a.Name, b.Name);
        if (order == 0) order = a.X.Raw.CompareTo(b.X.Raw);
        if (order == 0) order = a.Y.Raw.CompareTo(b.Y.Raw);
        if (order == 0) order = a.Z.Raw.CompareTo(b.Z.Raw);
        if (order == 0) order = a.Id.CompareTo(b.Id);
        if (order == 0) order = a.NextId.CompareTo(b.NextId);
        return order;
    }

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

    /// <summary>
    /// <c>BaseHeightMapRenderObjClass::isClearLineOfSight</c> (the <c>DO_BRESENHAM</c> branch, reached through <c>TerrainLogic::isClearLineOfSight</c>): a Bresenham walk
    /// over the vertex squares of the height field from the square under (<paramref name="x0"/>, <paramref name="y0"/>) toward the one under (<paramref name="x1"/>,
    /// <paramref name="y1"/>). A square is as high as the highest of its four corners; the sight line starts at <paramref name="z0"/> and climbs by an equal share per
    /// step. The start square is tested, the end square never; the line is blocked where a square stands more than 0.5 above it, and clear once the walk leaves the
    /// field or the line, still climbing, is above the highest point of the map. Used by the attack's view test (F-55).
    /// </summary>
    public bool IsClearLineOfSight(Fixed x0, Fixed y0, Fixed z0, Fixed x1, Fixed y1, Fixed z1)
    {
        var x = FloorToCell(x0) + _border;
        var y = FloorToCell(y0) + _border;
        var endX = FloorToCell(x1) + _border;
        var endY = FloorToCell(y1) + _border;
        var deltaX = Math.Abs(endX - x);
        var deltaY = Math.Abs(endY - y);
        var stepX = endX >= x ? 1 : -1;
        var stepY = endY >= y ? 1 : -1;
        // The major axis advances every step, the minor one when the numerator reaches the denominator.
        var (majorX, majorY, minorX, minorY, denominator, add) = deltaX >= deltaY
            ? (stepX, 0, 0, stepY, deltaX, deltaY)
            : (0, stepY, stepX, 0, deltaY, deltaX);
        var steps = denominator;
        if (steps == 0) return true;
        var numerator = denominator / 2;
        var z = z0;
        var climb = (z1 - z0) / Fixed.FromInt(steps);
        for (var step = 0; step < steps; step++)
        {
            if (x < 0 || y < 0 || x >= _vertexWidth - 1 || y >= _vertexHeight - 1) break;
            var index = y * _vertexWidth + x;
            var corners = Math.Max(Math.Max(_heights[index], _heights[index + 1]), Math.Max(_heights[index + _vertexWidth], _heights[index + _vertexWidth + 1]));
            if (Fixed.FromInt(corners) * HeightScale > z + LineOfSightFudge) return false;
            if (z >= _maxHeight && climb > Fixed.Zero) break;
            z += climb;
            numerator += add;
            if (numerator >= denominator)
            {
                numerator -= denominator;
                x += minorX;
                y += minorY;
            }
            x += majorX;
            y += majorY;
        }
        return true;
    }

    /// <summary><c>LOS_FUDGE</c>.</summary>
    private static readonly Fixed LineOfSightFudge = Fixed.FromRaw(Fixed.ScaleRaw / 2);

    /// <summary><c>REAL_TO_INT_FLOOR(pos / MAP_XY_FACTOR)</c>, exact on the raw value.</summary>
    private static int FloorToCell(Fixed value)
    {
        var cell = value.Raw / CellSizeFixed.Raw;
        if (value.Raw < 0 && cell * CellSizeFixed.Raw != value.Raw) cell--;
        return checked((int)cell);
    }

    /// <summary>Upper bound on the samples <see cref="HasHeightLineOfSight"/> takes along one line; see its doc comment for what this limits.</summary>
    public const int MaxLineOfSightSamples = 64;

    /// <summary>
    /// Height-based terrain occlusion between two eye points (<paramref name="z0"/>/<paramref name="z1"/> already include whatever eye-height
    /// offset the caller wants — this model has no per-template eye height, a named simplification). There is no single reference function this
    /// ports (the original resolves line of sight through <c>PartitionManager</c>'s object-to-object sightline tests, which this project does not
    /// have); this is a straightforward, self-contained terrain-horizon check: walk the straight segment from (x0,y0,z0) to (x1,y1,z1) at
    /// <see cref="CellSize"/> intervals (capped at <see cref="MaxLineOfSightSamples"/> samples — a line longer than that undersamples, a named
    /// limit; no vision or detection range in the shipped game is anywhere near that long in cells) and compare, at each sample, the terrain
    /// height (<see cref="HeightAt"/>) against the height the straight sight line would have there. The line is blocked the first time terrain
    /// strictly exceeds it — so a viewer standing high is never blocked by terrain below the line to a distant low target ("sees over" it), while
    /// any ridge the line must climb over blocks it. This only ever narrows an existing circular range check; it never extends one (a tall
    /// observer gets no bonus range, only fewer false blocks). Fixed-point only, no trigonometry, deterministic for a given terrain and inputs.
    /// </summary>
    public bool HasHeightLineOfSight(Fixed x0, Fixed y0, Fixed z0, Fixed x1, Fixed y1, Fixed z1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        if (distance <= CellSizeFixed) return true;
        var cells = (int)(distance / CellSizeFixed).Floor() + 1;
        var steps = Math.Min(MaxLineOfSightSamples, cells);
        for (var i = 1; i < steps; i++)
        {
            var t = Fixed.FromInt(i) / Fixed.FromInt(steps);
            var sightZ = z0 + (z1 - z0) * t;
            if (HeightAt(x0 + dx * t, y0 + dy * t) > sightZ) return false;
        }
        return true;
    }

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

    /// <summary>
    /// What a replay, a snapshot and a second machine compare to know they stand on the same terrain: heights, cells, water, the waypoints with their ids and
    /// links (the paths units and scripts follow), and every object placed on the map (start bases, supply sources, scripted units, props) with its properties,
    /// in file order. Not covered: sides, teams, scripts and trigger areas - a whole map is identified by the hash of its file (content manifest).
    /// </summary>
    private string ComputeFingerprint(MapData map)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            // Version of this layout: 2 adds waypoint ids and links and the map objects.
            writer.Write("terrain-fingerprint-2");
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
                writer.Write(waypoint.Id);
                writer.Write(waypoint.NextId);
            }
            writer.Write(map.Objects.Length);
            foreach (var placed in map.Objects)
            {
                writer.Write(placed.Name);
                writer.Write(Fixed.FromDecimal(placed.X).Raw);
                writer.Write(Fixed.FromDecimal(placed.Y).Raw);
                writer.Write(Fixed.FromDecimal(placed.Z).Raw);
                writer.Write(Fixed.FromDecimal(placed.Angle).Raw);
                writer.Write(placed.Flags);
                writer.Write(placed.Properties.Items.Length);
                foreach (var property in placed.Properties.Items)
                {
                    writer.Write(property.Key);
                    writer.Write((int)property.Type);
                    writer.Write(property.Integer);
                    writer.Write(property.Real);
                    writer.Write(property.Text);
                }
            }
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
