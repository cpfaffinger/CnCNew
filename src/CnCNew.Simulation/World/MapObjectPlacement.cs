using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// What <c>GameLogic::startNewGame</c> makes of one object of a map's <c>ObjectsList</c> in a match of several players, and the properties
/// <c>Object::updateObjValuesFromMapProperties</c> gives it.
/// </summary>
public static class MapObjectPlacement
{
    /// <summary>How an object of the map is treated.</summary>
    public enum Kind : byte
    {
        /// <summary>A logic object of the world.</summary>
        Placed,
        /// <summary>A waypoint, a road or bridge end point: the terrain's.</summary>
        Marker,
        /// <summary>No template of that name.</summary>
        Unknown,
        /// <summary>A bridge: the terrain builds it before the objects.</summary>
        Bridge,
        /// <summary><c>KINDOF_OPTIMIZED_TREE</c>, <c>KINDOF_PROP</c>, and fluff (<c>KINDOF_CLEARED_BY_BUILD</c> without a fence width, <c>forceFluffToProp</c> being always on in a match of
        /// several players): drawn by the client only, never a logic object.</summary>
        DrawnOnly,
        /// <summary>A template the port cannot make (<see cref="ObjectTemplate.MissingModules"/>): counted, never dropped silently.</summary>
        NotInstantiable,
    }

    public static Kind Classify(MapObject placed, TemplateCatalog catalog, out ObjectTemplate? template)
    {
        template = null;
        if (placed.IsWaypoint || placed.IsRoadPoint || placed.IsBridgePoint) return Kind.Marker;
        if (!catalog.TryGet(placed.Name, out var found)) return Kind.Unknown;
        template = found;
        if (found.IsBridge) return Kind.Bridge;
        if (found.IsKindOf("OPTIMIZED_TREE") || found.IsKindOf("PROP") || (found.IsKindOf("CLEARED_BY_BUILD") && found.FenceWidth == Numerics.Fixed.Zero)) return Kind.DrawnOnly;
        return found.IsInstantiable ? Kind.Placed : Kind.NotInstantiable;
    }

    /// <summary><c>objectInitialHealth</c> (100 when the map names none).</summary>
    public static int HealthPercent(MapProperties properties) => properties.TryGetInt("objectInitialHealth", out var value) ? value : 100;

    /// <summary><c>objectAggressiveness</c> (0 when the map names none: the team's attitude).</summary>
    public static int Attitude(MapProperties properties) => properties.TryGetInt("objectAggressiveness", out var value) ? value : 0;

    /// <summary>The yes-or-no properties that differ from an object made any other way.</summary>
    public static MapObjectFlags Flags(MapProperties properties)
    {
        var flags = MapObjectFlags.None;
        if (properties.Find("objectIndestructible") is { Type: MapPropertyType.Bool, Bool: true }) flags |= MapObjectFlags.Indestructible;
        if (properties.Find("objectEnabled") is { Type: MapPropertyType.Bool, Bool: false }) flags |= MapObjectFlags.Disabled;
        if (properties.Find("objectPowered") is { Type: MapPropertyType.Bool, Bool: false }) flags |= MapObjectFlags.Unpowered;
        if (properties.Find("objectUnsellable") is { Type: MapPropertyType.Bool, Bool: true }) flags |= MapObjectFlags.Unsellable;
        if (properties.Find("objectTargetable") is { Type: MapPropertyType.Bool, Bool: true }) flags |= MapObjectFlags.Targetable;
        return flags;
    }
}
