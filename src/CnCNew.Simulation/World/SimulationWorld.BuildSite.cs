using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary><c>LegalBuildCode</c> (<c>BuildAssistant.h</c>): why a place is no building site.</summary>
public enum LegalBuildCode : byte
{
    Ok,
    RestrictedTerrain,
    NotFlatEnough,
    ObjectsInTheWay,
    Shroud,
    TooCloseToSupplies,
}

public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>BuildAssistant::isLocationLegalToBuild</c> for a player's order (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP | SHROUD_REVEALED</c>): the shroud comes first, "since returning other
    /// errors for shrouded areas could be used to game the system". A world without terrain has no site rules.
    /// </summary>
    public LegalBuildCode CheckBuildSite(PlayerId player, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is null) return LegalBuildCode.Ok;
        if (!Navigation.Terrain.IsOnMap(x, y)) return LegalBuildCode.RestrictedTerrain;
        return !IsPositionExploredBy(player, x, y) ? LegalBuildCode.Shroud : CheckBuildGround(template, x, y, angle);
    }

    /// <summary>The same without the shroud, for a computer player's search (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP</c>, <c>AIPlayer::buildStructureNow</c>).</summary>
    public LegalBuildCode CheckBuildGround(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is not { } navigation) return LegalBuildCode.Ok;
        if (!navigation.Terrain.IsOnMap(x, y)) return LegalBuildCode.RestrictedTerrain;
        if (!navigation.IsFootprintClear(template, x, y, angle)) return LegalBuildCode.ObjectsInTheWay;
        if (TooCloseToSupplies(template, x, y, angle)) return LegalBuildCode.TooCloseToSupplies;
        return BuildSiteTerrain.Check(navigation.Terrain, template, x, y, angle, _templates.BuildSite);
    }

    /// <summary>
    /// "Special case for supply centers: can't build too close to supply sources": only the closest source counts (<c>getClosestObject</c> within the site's bounding circle and twice the border,
    /// <c>FROM_BOUNDINGSPHERE_2D</c>, so from the source's circle), with its footprint grown by <c>SupplyBuildBorder</c>.
    /// </summary>
    private bool TooCloseToSupplies(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        var border = _templates.BuildSite.SupplyBorder;
        if (border <= Fixed.Zero || !template.IsKindOf("CANNOT_BUILD_NEAR_SUPPLIES")) return false;
        var range = template.BoundingCircleRadius + border * Fixed.FromInt(2);
        Entity? closest = null;
        var nearest = Fixed.MaxValue;
        foreach (var source in EntitiesOfKind("SUPPLY_SOURCE").AsSpan())
        {
            if (source.DestroyRequested) continue;
            var (dx, dy) = (source.X - x, source.Y - y);
            var distance = Fixed.Max(Fixed.Zero, Fixed.Sqrt(dx * dx + dy * dy) - source.Template.BoundingCircleRadius);
            if (distance > range || distance >= nearest) continue;
            nearest = distance;
            closest = source;
        }
        return closest is not null && SiteShape.Collide(new SiteShape(template, x, y, angle), new SiteShape(closest.Template, closest.X, closest.Y, closest.Angle, border));
    }
}
