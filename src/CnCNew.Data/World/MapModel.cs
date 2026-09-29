using System.Collections.Immutable;

namespace CnCNew.Data.World;

/// <summary>The value kind of a <see cref="MapProperty"/> (<c>Dict::DataType</c>).</summary>
public enum MapPropertyType
{
    Bool = 0,
    Int = 1,
    Real = 2,
    Ascii = 3,
    Unicode = 4,
}

/// <summary>One key of a map <c>Dict</c>. Reals are stored as <see cref="decimal"/> (the reference stores 32-bit floats).</summary>
public sealed record MapProperty(string Key, MapPropertyType Type, long Integer, decimal Real, string Text)
{
    public bool Bool => Integer != 0;
}

/// <summary>An ordered <c>Dict</c>; keys are looked up case-sensitively, as the name-key generator does not fold case for map keys.</summary>
public sealed record MapProperties(ImmutableArray<MapProperty> Items)
{
    public static MapProperties Empty { get; } = new(ImmutableArray<MapProperty>.Empty);

    public MapProperty? Find(string key)
    {
        foreach (var item in Items)
            if (item.Key == key) return item;
        return null;
    }

    public bool TryGetInt(string key, out int value)
    {
        if (Find(key) is { Type: MapPropertyType.Int } property) { value = checked((int)property.Integer); return true; }
        value = 0;
        return false;
    }

    public string? GetText(string key) => Find(key) is { Type: MapPropertyType.Ascii or MapPropertyType.Unicode } property ? property.Text : null;

    public bool GetBool(string key) => Find(key) is { Type: MapPropertyType.Bool } property && property.Bool;
}

/// <summary>An entry of the <c>ObjectsList</c> chunk (<c>MapObject</c>): terrain props, buildings, units and waypoints alike.</summary>
public sealed record MapObject(string Name, decimal X, decimal Y, decimal Z, decimal Angle, int Flags, MapProperties Properties)
{
    public const int FlagRoadPoint1 = 0x02;
    public const int FlagRoadPoint2 = 0x04;
    public const int FlagBridgePoint1 = 0x10;
    public const int FlagBridgePoint2 = 0x20;

    /// <summary><c>MO_WAYPOINT</c>: the object carries an integer <c>waypointID</c> key.</summary>
    public bool IsWaypoint => Properties.Find("waypointID") is { Type: MapPropertyType.Int };

    public bool IsRoadPoint => (Flags & (FlagRoadPoint1 | FlagRoadPoint2)) != 0;
    public bool IsBridgePoint => (Flags & (FlagBridgePoint1 | FlagBridgePoint2)) != 0;
}

public sealed record MapPoint(int X, int Y, int Z);

/// <summary>A <c>PolygonTrigger</c>: trigger areas, water areas and river paths share this structure.</summary>
public sealed record MapPolygonTrigger(string Name, string Layer, int Id, bool IsWater, bool IsRiver, int RiverStart, ImmutableArray<MapPoint> Points);

public sealed record MapWaypointLink(int From, int To);

public sealed record MapBoundary(int X, int Y);

/// <summary><c>HeightMapData</c>: one byte per grid vertex; the playable area excludes <see cref="Border"/> vertices on every side.</summary>
public sealed record MapHeightField(int Width, int Height, int Border, ImmutableArray<MapBoundary> Boundaries, ImmutableArray<byte> Heights)
{
    public byte HeightAt(int x, int y) => Heights[y * Width + x];
}

public sealed record MapTextureClass(int FirstTile, int TileCount, int Width, string Name);

public sealed record MapBlendedTile(int BlendIndex, bool Horizontal, bool Vertical, bool RightDiagonal, bool LeftDiagonal, byte Inverted, bool LongDiagonal, int CustomEdgeClass);

public sealed record MapCliffInfo(int TileIndex, ImmutableArray<decimal> Uv, bool Flip, bool Mutant);

/// <summary>
/// <c>BlendTileData</c>: per-cell tile, blend and cliff indices plus the cliff bit mask that the pathfinder classifies cliff cells
/// with. <see cref="CliffFlags"/> packs one bit per cell, <see cref="CliffStride"/> bytes per row (<c>m_flipStateWidth</c>).
/// </summary>
public sealed record MapBlendTiles(
    int Version,
    ImmutableArray<short> Tiles,
    ImmutableArray<short> Blends,
    ImmutableArray<short> ExtraBlends,
    ImmutableArray<short> CliffInfoIndices,
    ImmutableArray<byte> CliffFlags,
    int CliffStride,
    int BitmapTileCount,
    int EdgeTileCount,
    ImmutableArray<MapTextureClass> TextureClasses,
    ImmutableArray<MapTextureClass> EdgeTextureClasses,
    ImmutableArray<MapBlendedTile> BlendedTiles,
    ImmutableArray<MapCliffInfo> CliffInfos)
{
    public bool IsCliff(int cellX, int cellY) => (CliffFlags[cellY * CliffStride + (cellX >> 3)] & (1 << (cellX & 7))) != 0;
}

/// <summary>An entry of a side's build list (<c>BuildListInfo</c>): a structure the side's AI rebuilds at this place.</summary>
public sealed record MapBuildListEntry(
    string BuildingName, string Template, decimal X, decimal Y, decimal Angle, bool InitiallyBuilt, int Rebuilds, string Script,
    int Health, bool Whiner, bool Unsellable, bool Repairable);

public sealed record MapSide(MapProperties Properties, ImmutableArray<MapBuildListEntry> BuildList);

/// <summary>A chunk the decoder recognized but deliberately did not decode (scripts, lighting, cameras, sounds); counted, never silent.</summary>
public sealed record MapSkippedChunk(string Path, int Version, int Bytes);

/// <summary>
/// The decoded static content of one map file (<c>TerrainLogic::loadMap</c>, <c>WorldHeightMap</c>, <c>SidesList</c>, <c>PolygonTrigger</c>).
/// Immutable, renderer- and filesystem-independent; the simulation derives its terrain grid and waypoints from it.
/// </summary>
public sealed record MapData(
    string Container,
    MapHeightField HeightField,
    MapBlendTiles BlendTiles,
    MapProperties WorldInfo,
    ImmutableArray<MapSide> Sides,
    ImmutableArray<MapProperties> Teams,
    ImmutableArray<MapObject> Objects,
    ImmutableArray<MapPolygonTrigger> Triggers,
    ImmutableArray<MapWaypointLink> WaypointLinks,
    ImmutableArray<MapSkippedChunk> SkippedChunks)
{
    public const decimal CellSize = 10m;
    public const decimal HeightScale = 0.625m;
}
