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

    public bool TryGetReal(string key, out decimal value)
    {
        if (Find(key) is { Type: MapPropertyType.Real } property) { value = property.Real; return true; }
        value = 0m;
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
    /// <summary><c>FLAG_ROAD_CORNER_ANGLED</c>: the road corner at this point is mitred instead of curved.</summary>
    public const int FlagRoadCornerAngled = 0x08;
    public const int FlagBridgePoint1 = 0x10;
    public const int FlagBridgePoint2 = 0x20;
    /// <summary><c>FLAG_ROAD_CORNER_TIGHT</c>: the curve at a road corner uses the tight radius.</summary>
    public const int FlagRoadCornerTight = 0x40;
    /// <summary><c>FLAG_ROAD_JOIN</c>: an open road end does a generic alpha join.</summary>
    public const int FlagRoadJoin = 0x80;

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

/// <summary>A chunk the decoder recognized but deliberately did not decode (lighting, cameras, sounds); counted, never silent.</summary>
public sealed record MapSkippedChunk(string Path, int Version, int Bytes);

/// <summary>Value kind of a <see cref="MapScriptParameter"/> (<c>Parameter::ParameterType</c>, <c>Scripts.h</c>). The raw enum order on
/// disk matches this declaration order exactly (it is the wire value, not just a label).</summary>
public enum MapScriptParameterType
{
    Int = 0, Real, Script, Team, Counter, Flag, Comparison, Waypoint, Boolean, TriggerArea, TextString, Side, Sound,
    ScriptSubroutine, Unit, ObjectType, Coord3D, Angle, TeamState, Relation, AiMood, Dialog, Music, Movie, WaypointPath,
    LocalizedText, Bridge, KindOf, AttackPrioritySet, RadarEventType, SpecialPower, Science, Upgrade, CommandButtonAbility,
    Boundary, Buildable, SurfacesAllowed, ShakeIntensity, CommandButton, FontName, ObjectStatus, CommandButtonAllAbilities,
    SkirmishWaypointPath, Color, Emoticon, ObjectPanelFlag, FactionName, ObjectTypeList, RevealName, ScienceAvailability,
    LeftOrRight, Percent,
}

/// <summary>
/// One parameter of a <see cref="MapCondition"/> or <see cref="MapScriptAction"/> (<c>Parameter::ReadParameter</c>). Every field except
/// <see cref="CoordX"/>/<see cref="CoordY"/>/<see cref="CoordZ"/> is always present on disk regardless of <see cref="Type"/> (the World
/// Builder format stores int+real+string unconditionally for every non-<see cref="MapScriptParameterType.Coord3D"/> parameter, whether or
/// not the type actually uses that field); which field is meaningful is determined solely by <see cref="Type"/>.
/// </summary>
public sealed record MapScriptParameter(MapScriptParameterType Type, int Integer, decimal Real, string Text, decimal CoordX, decimal CoordY, decimal CoordZ);

/// <summary>
/// One leaf test of an AND-chain inside a <see cref="MapOrCondition"/> (<c>Condition</c>). <see cref="TypeId"/> is the raw, version- and
/// build-dependent enum value stored on disk and is kept for diagnostics only; <see cref="TypeName"/> is resolved through the map's own
/// symbol table (the same mechanism every other name-keyed value in this format uses) and is the stable identifier callers should match
/// on, exactly as the original engine's own "rematch by name" recovery does when <see cref="TypeId"/> drifts between builds.
/// </summary>
public sealed record MapCondition(int TypeId, string TypeName, ImmutableArray<MapScriptParameter> Parameters);

/// <summary>One OR-branch of a <see cref="MapScript"/>: all of <see cref="AndConditions"/> must be true for the branch to be true; the
/// script fires when any one branch (any one <c>OrCondition</c>) is true.</summary>
public sealed record MapOrCondition(ImmutableArray<MapCondition> AndConditions);

/// <summary>One action of a <see cref="MapScript"/>'s true- or false-action list (<c>ScriptAction</c>/<c>ScriptActionFalse</c>). See
/// <see cref="MapCondition"/> for the <see cref="TypeId"/>/<see cref="TypeName"/> split.</summary>
public sealed record MapScriptAction(int TypeId, string TypeName, ImmutableArray<MapScriptParameter> Parameters);

/// <summary>One World Builder script (<c>Script</c>): an OR-of-ANDs condition tree, evaluated when the containing list is active, that
/// runs <see cref="Actions"/> when true and <see cref="FalseActions"/> otherwise. <see cref="IsOneShot"/> scripts deactivate themselves
/// after firing once; <see cref="Easy"/>/<see cref="Normal"/>/<see cref="Hard"/> gate a script to specific campaign difficulties.</summary>
public sealed record MapScript(
    string Name, string Comment, string ConditionComment, string ActionComment,
    bool IsActive, bool IsOneShot, bool Easy, bool Normal, bool Hard, bool IsSubroutine, int DelayEvaluationSeconds,
    ImmutableArray<MapOrCondition> OrConditions, ImmutableArray<MapScriptAction> Actions, ImmutableArray<MapScriptAction> FalseActions);

/// <summary>A named, collapsible group of scripts (<c>ScriptGroup</c>); a subroutine group only runs when another script's action calls
/// it by name, never on its own.</summary>
public sealed record MapScriptGroup(string Name, bool IsActive, bool IsSubroutine, ImmutableArray<MapScript> Scripts);

/// <summary>
/// One player's or side's script list (<c>ScriptList</c>): one entry per <c>PlayerScriptsList</c> child, in file order, matching the
/// side/player order of <see cref="MapData.Sides"/>. Top-level <see cref="Scripts"/> and named <see cref="Groups"/> each keep their own
/// file order (World Builder's chunky format lets them interleave arbitrarily; the original engine appends each kind to its own list
/// regardless of interleaving, and this decoder does the same).
/// </summary>
public sealed record MapScriptList(ImmutableArray<MapScript> Scripts, ImmutableArray<MapScriptGroup> Groups);

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
    ImmutableArray<MapScriptList> PlayerScripts,
    ImmutableArray<MapSkippedChunk> SkippedChunks)
{
    public const decimal CellSize = 10m;
    public const decimal HeightScale = 0.625m;
}

/// <summary>One entry of a script library's <c>ScriptsPlayers</c> chunk: the name of the template player a script list belongs to and, from chunk version 2 on, its side dict.</summary>
public sealed record MapScriptLibraryPlayer(string Name, MapProperties Properties);

/// <summary>
/// The decoded content of a script library file (<c>Data/Scripts/SkirmishScripts.scb</c>, <c>MultiplayerScripts.scb</c>): the export format of World Builder's script dialog.
/// <see cref="ScriptLists"/> and <see cref="Players"/> are parallel (list <c>i</c> belongs to player <c>i</c>; <c>SidesList::prepareForMP_or_Skirmish</c> matches them by index and the
/// player by name); <see cref="Teams"/> are the team prototypes of those template players (<c>ScriptTeams</c>). A library without the two optional chunks has only script lists.
/// </summary>
public sealed record MapScriptLibrary(
    string Container,
    ImmutableArray<MapScriptLibraryPlayer> Players,
    ImmutableArray<MapProperties> Teams,
    ImmutableArray<MapScriptList> ScriptLists,
    ImmutableArray<MapSkippedChunk> SkippedChunks);
