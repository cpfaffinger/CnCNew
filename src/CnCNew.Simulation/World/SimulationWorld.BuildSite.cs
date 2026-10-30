using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
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
    /// <summary>The width the skirmish AI keeps around what it builds, "so it doesn't build itself into a corner" (<c>isLocationClearOfObjects</c>, three pathfind cells).</summary>
    private static readonly Fixed ComputerBibWidth = Fixed.FromInt(3 * TerrainMap.CellSize);

    private static readonly int UsingAbilityBit = ObjectStatusBits.Index("USING_ABILITY");

    /// <summary>
    /// <c>BuildAssistant::isLocationLegalToBuild</c> for a builder's order (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP | SHROUD_REVEALED</c>): the shroud comes first, "since returning other
    /// errors for shrouded areas could be used to game the system". A world without terrain has no site rules.
    /// </summary>
    public LegalBuildCode CheckBuildSite(Entity builder, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is not { } navigation) return LegalBuildCode.Ok;
        if (!navigation.Terrain.IsOnMap(x, y)) return LegalBuildCode.RestrictedTerrain;
        return !IsPositionExploredBy(builder.Owner, x, y) ? LegalBuildCode.Shroud : CheckGround(navigation, builder, template, x, y, angle, false);
    }

    /// <summary>The same for a computer player's search (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP</c> without a builder, <c>AIPlayer::buildStructureNow</c>).</summary>
    public LegalBuildCode CheckBuildGround(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is not { } navigation) return LegalBuildCode.Ok;
        return navigation.Terrain.IsOnMap(x, y) ? CheckGround(navigation, null, template, x, y, angle, true) : LegalBuildCode.RestrictedTerrain;
    }

    private LegalBuildCode CheckGround(NavigationGrid navigation, Entity? builder, ObjectTemplate template, Fixed x, Fixed y, Fixed angle, bool computer)
    {
        if (ObjectsInTheWay(navigation, builder, template, x, y, angle) || BlocksFactoryExit(template, x, y, angle, computer)) return LegalBuildCode.ObjectsInTheWay;
        if (TooCloseToSupplies(template, x, y, angle)) return LegalBuildCode.TooCloseToSupplies;
        return BuildSiteTerrain.Check(navigation.Terrain, template, x, y, angle, _templates.BuildSite);
    }

    /// <summary>
    /// The objects under the footprint by <c>isLocationClearOfObjects</c>: what will be removed, mines and inert objects do not count, an immobile, disabled or enemy object does, and so does an
    /// ally at an ability, which may not be moved aside. Without a builder nobody is an enemy (<c>rel = NEUTRAL</c>).
    /// </summary>
    private bool ObjectsInTheWay(NavigationGrid navigation, Entity? builder, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        var site = new SiteShape(template, x, y, angle);
        var ground = navigation.Terrain.HeightAt(x, y);
        var reach = template.BoundingCircleRadius;
        foreach (var them in _entities.Values)
        {
            if (FurtherThan(them.X, x, reach + them.Template.BoundingCircleRadius) || FurtherThan(them.Y, y, reach + them.Template.BoundingCircleRadius)) continue;
            if (!Overlaps(site, ground, template, them)) continue;
            var relation = builder is null ? Relationship.Neutral : RelationshipOf(builder.Owner, them.Owner);
            if (relation == Relationship.Allies && them.HasStatus(UsingAbilityBit)) return true;
            if (IsRemovableForConstruction(them) || them.Template.IsKindOf("MINE") || them.Template.IsKindOf("INERT")) continue;
            if (them.Template.IsKindOf("IMMOBILE") || DisabledFlagsOf(them) != DisabledMask.None || (builder is not null && relation == Relationship.Enemies)) return true;
        }
        return false;
    }

    /// <summary><c>iteratePotentialCollisions</c>: an object in a container is in no cell, and the height of the two has to meet (<c>geomCollidesWithGeom</c> tests z first).</summary>
    private static bool Overlaps(in SiteShape site, Fixed ground, ObjectTemplate template, Entity them)
        => !them.ContainedBy.IsValid && !them.DestroyRequested && ground + template.GeometryHeight >= them.Z && ground <= them.Z + them.Template.GeometryHeight
           && SiteShape.Collide(site, new SiteShape(them.Template, them.X, them.Y, them.Angle));

    /// <summary><c>isRemovableForConstruction</c>: shrubbery, what a building clears, and rubble and the dead.</summary>
    private static bool IsRemovableForConstruction(Entity entity)
        => entity.Template.IsKindOf("SHRUBBERY") || entity.Template.IsKindOf("CLEARED_BY_BUILD") || entity.EffectivelyDead;

    /// <summary>
    /// The second half of <c>isLocationClearOfObjects</c>: a building's bib and the way out of a factory are kept free - the site with its <c>FactoryExtraBibWidth</c> against every structure with
    /// its own, and against an immobile one the exit rectangles (<c>FactoryExitWidth</c>, in front of the building along its heading) of both. The original grows the other building's minor radius by
    /// the site's width, not its own.
    /// </summary>
    private bool BlocksFactoryExit(ObjectTemplate template, Fixed x, Fixed y, Fixed angle, bool computer)
    {
        var (exitWidth, extraWidth) = (template.FactoryExitWidth, template.FactoryExtraBibWidth);
        if (computer && extraWidth < ComputerBibWidth)
        {
            extraWidth = ComputerBibWidth;
            exitWidth = Fixed.Max(Fixed.Zero, exitWidth - extraWidth);
        }

        var range = Fixed.FromInt(2) * (template.GeometryMajorRadius + (template.IsBox ? template.GeometryMinorRadius : template.GeometryMajorRadius));
        var bounds = SiteShape.Bounds(x, y, angle, template.GeometryMajorRadius + extraWidth, (template.IsBox ? template.GeometryMinorRadius : template.GeometryMajorRadius) + extraWidth);
        var hasExit = exitWidth > Fixed.Zero;
        var exit = hasExit ? ExitShape(template, x, y, angle, exitWidth) : default;
        foreach (var them in EntitiesOfKind("STRUCTURE").AsSpan())
        {
            // iterateObjectsInRange, FROM_BOUNDINGSPHERE_2D: the circle of the structure reaches the site's range.
            var (dx, dy) = (them.X - x, them.Y - y);
            if (!SquaredReach.Of(range + them.Template.BoundingCircleRadius).Contains(dx * dx + dy * dy)) continue;
            if (them.DestroyRequested || them.ContainedBy.IsValid || IsRemovableForConstruction(them)) continue;
            var (hisExit, hisExtra) = (them.Template.FactoryExitWidth, them.Template.FactoryExtraBibWidth);
            var hisBounds = SiteShape.Bounds(them.X, them.Y, them.Angle, them.Template.GeometryMajorRadius + hisExtra,
                them.Template.IsBox ? them.Template.GeometryMinorRadius + extraWidth : them.Template.GeometryMajorRadius + hisExtra);
            if (SiteShape.Collide(hisBounds, bounds)) return true;
            var hasHisExit = hisExit > Fixed.Zero;
            if ((!hasExit && !hasHisExit && hisExtra == Fixed.Zero && extraWidth == Fixed.Zero) || !them.Template.IsKindOf("IMMOBILE")) continue;
            var hisShape = hasHisExit ? ExitShape(them.Template, them.X, them.Y, them.Angle, hisExit) : default;
            if (hasExit && SiteShape.Collide(hisBounds, exit)) return true;
            if (hasHisExit && SiteShape.Collide(hisShape, bounds)) return true;
            if (hasExit && hasHisExit && SiteShape.Collide(hisShape, exit)) return true;
        }
        return false;
    }

    /// <summary>The exit rectangle: half its width as the major radius, the building's minor radius, directly in front of the building; a building that is no box keeps its circle.</summary>
    private static SiteShape ExitShape(ObjectTemplate template, Fixed x, Fixed y, Fixed angle, Fixed width)
    {
        var half = width / Fixed.FromInt(2);
        var offset = template.GeometryMajorRadius + half;
        return SiteShape.Exit(x + FixedTrig.Cos(angle) * offset, y + FixedTrig.Sin(angle) * offset, angle, half, template.IsBox ? template.GeometryMinorRadius : template.GeometryMajorRadius, template.IsBox);
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

    /// <summary>
    /// <c>clearRemovableForConstruction</c> and <c>moveObjectsForConstruction</c> before a building is begun: what can be removed is destroyed, and the player's and neutral units under the footprint
    /// go to a place drawn around it (two draws of the logic stream per unit, in the order of their ids). False when something cannot be moved; the original lets only a computer player build then.
    /// </summary>
    private bool ClearSite(PlayerId owner, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is not { } navigation) return true;
        var ground = navigation.Terrain.HeightAt(x, y);
        var reach = template.BoundingCircleRadius;
        var site = new SiteShape(template, x, y, angle);
        foreach (var them in _entities.Values)
        {
            if (FurtherThan(them.X, x, reach + them.Template.BoundingCircleRadius) || FurtherThan(them.Y, y, reach + them.Template.BoundingCircleRadius)) continue;
            if (Overlaps(site, ground, template, them) && IsRemovableForConstruction(them) && !them.Template.IsKindOf("ALWAYS_SELECTABLE")) RequestDestroy(them.Id);
        }

        var minor = template.IsBox ? template.GeometryMinorRadius : template.GeometryMajorRadius;
        var moving = SiteShape.Bounds(x, y, angle, template.GeometryMajorRadius, minor);
        var radius = Fixed.Sqrt(template.GeometryMajorRadius * template.GeometryMajorRadius + minor * minor) * Fixed.FromDecimal(1.4m);
        var free = true;
        foreach (var them in _entities.Values)
        {
            if (FurtherThan(them.X, x, reach + them.Template.BoundingCircleRadius) || FurtherThan(them.Y, y, reach + them.Template.BoundingCircleRadius)) continue;
            if (them.ContainedBy.IsValid || them.DestroyRequested || them.Template.IsKindOf("MINE") || them.Template.IsKindOf("INERT") || them.Template.IsKindOf("ALWAYS_SELECTABLE")) continue;
            if (IsRemovableForConstruction(them) || ground + template.GeometryHeight < them.Z || ground > them.Z + them.Template.GeometryHeight) continue;
            if (!SiteShape.Collide(moving, new SiteShape(them.Template, them.X, them.Y, them.Angle))) continue;
            if (RelationshipOf(owner, them.Owner) == Relationship.Enemies || AiOf(them) is not { } ai)
            {
                free = false;
                continue;
            }
            var distance = LogicRandom.Range(Fixed.FromDecimal(0.5m), Fixed.FromDecimal(1.5m)) * radius;
            var direction = LogicRandom.Range(-FixedTrig.Pi, FixedTrig.Pi);
            ai.OrderMove(this, x + FixedTrig.Cos(direction) * distance, y + FixedTrig.Sin(direction) * distance, false);
        }
        return free;
    }
}
