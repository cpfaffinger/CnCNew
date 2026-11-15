using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Objects;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Hud;

/// <summary>
/// The objects the radar shows to its viewer (<c>W3DRadar::renderObjectList</c> over <c>Radar::addObject</c>'s lists). The original puts an object on the radar only when its
/// <c>Object::getRadarPriority</c> is visible (<c>Radar::isPriorityVisible</c>: not <c>INVALID</c>, not <c>NOT_ON_RADAR</c>) and draws it only while its shrouded status for the
/// local player is clear or partly clear: a building remembered in the fog (a ghost), a unit out of sight and an enemy's stealthed object that is not detected are never a blip,
/// and a <c>LOCAL_UNIT_ONLY</c> object (a civilian car, a drone) is one only to the player who controls it. So the radar tells a player nothing his view does not.
/// The frame the blips are taken from already holds only what the viewer may see; the rule here leaves out the remembered and what the radar does not show at all.
/// </summary>
public sealed class RadarBlips
{
    private static readonly int CarBombBit = ObjectStatusBits.Index("IS_CARBOMB");
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private static readonly int DisguisedBit = ObjectStatusBits.Index("DISGUISED");

    // ThingTemplate's RadarPriority by object template name, where it is set (unset is INVALID).
    private readonly Dictionary<string, RadarPriority> _priorities = new(StringComparer.OrdinalIgnoreCase);

    public RadarBlips(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        foreach (var definition in graph.Definitions)
        {
            if (definition is not ObjectDefinition) continue;
            if (definition.Fields.LastOrDefault(field => field.Name.Equals("RadarPriority", StringComparison.OrdinalIgnoreCase)) is not { } field) continue;
            _priorities[definition.OriginalName] = field.Value.OriginalText.Trim().ToUpperInvariant() switch
            {
                "NOT_ON_RADAR" => RadarPriority.NotOnRadar,
                "STRUCTURE" => RadarPriority.Structure,
                "UNIT" => RadarPriority.Unit,
                "LOCAL_UNIT_ONLY" => RadarPriority.LocalUnitOnly,
                _ => RadarPriority.Invalid,
            };
        }
    }

    /// <summary>
    /// <c>Object::getRadarPriority</c>: the template's priority; one that sets none (<c>INVALID</c>) is a structure when its container is garrisonable or it is <c>CAPTURABLE</c>;
    /// a car bomb is a unit whatever its template says.
    /// </summary>
    public RadarPriority PriorityOf(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var priority = _priorities.GetValueOrDefault(entity.Template.Name, RadarPriority.Invalid);
        if (priority == RadarPriority.Invalid)
        {
            if (entity.Modules.OfType<IContainerModule>().FirstOrDefault() is { IsGarrisonable: true }) priority = RadarPriority.Structure;
            if (entity.Template.IsKindOf("CAPTURABLE")) priority = RadarPriority.Structure;
        }
        if (entity.HasStatus(CarBombBit)) priority = RadarPriority.Unit;
        return priority;
    }

    /// <summary><c>Radar::isPriorityVisible</c>.</summary>
    public static bool IsOnRadar(RadarPriority priority) => priority is not (RadarPriority.Invalid or RadarPriority.NotOnRadar);

    /// <summary>
    /// The blips of <paramref name="frame"/> for <paramref name="viewer"/> (<see langword="null"/>: a spectator who sees the whole world, the original's observer, who is
    /// not an active player and so sees every <c>LOCAL_UNIT_ONLY</c> object). Each in the colour the frame draws its object in (the house colour, a disguise's or a stealth
    /// garrison's apparent owner for those it deceives - <c>Radar::addObject</c>).
    /// </summary>
    public List<MinimapDot> Dots(RenderFrame frame, SimulationWorld world, PlayerId? viewer)
    {
        var dots = new List<MinimapDot>();
        foreach (var drawable in Shown(frame, world, viewer))
            dots.Add(new MinimapDot(new Vector2(drawable.Position.X, drawable.Position.Y), new Rgba32(drawable.TeamColor.Red, drawable.TeamColor.Green, drawable.TeamColor.Blue, 255)));
        return dots;
    }

    /// <summary>The drawables of <paramref name="frame"/> that are blips, in the frame's order (see <see cref="Dots"/>).</summary>
    public List<DrawableInstance> Shown(RenderFrame frame, SimulationWorld world, PlayerId? viewer)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(world);
        var shown = new List<DrawableInstance>();
        foreach (var drawable in frame.Drawables)
        {
            // Scenery and the placement preview are no objects of the logic; a ghost is a building in the fog (OBJECTSHROUD_FOGGED), which the radar skips.
            if (!drawable.Entity.IsValid || drawable.IsGhost) continue;
            if (world.FindEntity(drawable.Entity) is not { } entity) continue;
            var priority = PriorityOf(entity);
            if (!IsOnRadar(priority)) continue;
            if (viewer is { } player)
            {
                if (priority == RadarPriority.LocalUnitOnly && entity.Owner != player) continue;
                // An enemy's stealthed object shows only while it is detected or in disguise (the frame leaves such objects out already; the radar asks again).
                if (entity.HasStatus(StealthedBit) && world.RelationshipOf(player, entity.Owner) == Relationship.Enemies && !entity.HasStatus(DetectedBit) && !entity.HasStatus(DisguisedBit)) continue;
            }
            shown.Add(drawable);
        }
        return shown;
    }
}

/// <summary><c>RadarPriorityType</c> (<c>Radar.h</c>).</summary>
public enum RadarPriority
{
    Invalid,
    NotOnRadar,
    Structure,
    Unit,
    LocalUnitOnly,
}
