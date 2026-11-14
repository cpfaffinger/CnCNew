using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary><c>ringSpacing</c> of <c>PartitionManager.cpp</c>: how fast the ring search of <c>findPositionAround</c> widens.</summary>
    private static readonly Fixed PositionRingSpacing = Fixed.FromInt(5);

    /// <summary>The radius of the sphere <c>tryPosition</c> tests a place with (<c>GeometryInfo geometry( GEOMETRY_SPHERE, TRUE, 5.0f, 5.0f, 5.0f )</c>).</summary>
    private static readonly Fixed PositionProbeRadius = Fixed.FromInt(5);

    /// <inheritdoc cref="Modules.IWorldServices.FindPositionAround"/>
    /// <remarks>
    /// <c>PartitionManager::findPositionAround</c> and <c>tryPosition</c>: from a random angle, on rings five units apart (the innermost is the centre itself), at the angles
    /// start, start + step, start - step, start + 2 step ... a place is taken when it is on no cliff, not under water, and no object overlaps a sphere of radius 5 around it (the
    /// <paramref name="ignore"/> object and <paramref name="forWhom"/> do not count), and, for a ground mover, a path leads there. A centre off the map is returned as it is.
    /// </remarks>
    public bool FindPositionAround(Fixed centerX, Fixed centerY, Fixed minRadius, Fixed maxRadius, Entity forWhom, Entity? ignore, out Fixed x, out Fixed y)
    {
        (x, y) = (centerX, centerY);
        if (Navigation is not { } navigation || !navigation.Terrain.IsOnMap(centerX, centerY)) return true;
        var start = LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
        for (var dist = minRadius; dist <= maxRadius; dist += PositionRingSpacing)
        {
            // The step in angle widens the ring's samples with its length: "larger float = more samples".
            var spacing = dist == minRadius ? FixedTrig.TwoPi : PositionRingSpacing / (dist + Fixed.One) * (FixedTrig.TwoPi / Fixed.FromInt(6));
            var samples = checked((int)(FixedTrig.TwoPi / spacing / Fixed.FromInt(2)).Ceiling());
            for (var i = 0; i < samples; i++)
            {
                if (TryPosition(navigation, centerX, centerY, dist, start + spacing * Fixed.FromInt(i), forWhom, ignore, out x, out y)) return true;
                if (i != 0 && TryPosition(navigation, centerX, centerY, dist, start - spacing * Fixed.FromInt(i), forWhom, ignore, out x, out y)) return true;
            }
        }
        (x, y) = (centerX, centerY);
        return false;
    }

    private bool TryPosition(NavigationGrid navigation, Fixed centerX, Fixed centerY, Fixed dist, Fixed angle, Entity forWhom, Entity? ignore, out Fixed x, out Fixed y)
    {
        x = dist * FixedTrig.Cos(angle) + centerX;
        y = dist * FixedTrig.Sin(angle) + centerY;
        var terrain = navigation.Terrain;
        // "we don't usually find positions on cliffs", nor off the map, nor in the water.
        if (!terrain.IsOnMap(x, y) || (terrain.CellAt(TerrainMap.CellOf(x), TerrainMap.CellOf(y)) & CellFlags.Cliff) != 0 || terrain.IsUnderwater(x, y)) return false;
        var ground = terrain.HeightAt(x, y);
        var probe = SiteShape.Exit(x, y, Fixed.Zero, PositionProbeRadius, PositionProbeRadius, false);
        var reach = PositionProbeRadius * Fixed.FromInt(11) / Fixed.FromInt(10);
        foreach (var them in _entities.Values)
        {
            if (ReferenceEquals(them, ignore) || ReferenceEquals(them, forWhom) || them.ContainedBy.IsValid || them.DestroyRequested) continue;
            var limit = reach + them.Template.BoundingCircleRadius;
            if (FurtherThan(them.X, x, limit) || FurtherThan(them.Y, y, limit)) continue;
            // z_collideTest: the sphere reaches five above the ground, the other object as high as its height (a sphere: its radius).
            var height = them.Template.IsSphere ? them.Template.GeometryMajorRadius : them.Template.GeometryHeight;
            if (ground + PositionProbeRadius < them.Z || ground > them.Z + height) continue;
            if (SiteShape.Collide(probe, new SiteShape(them.Template, them.X, them.Y, them.Angle))) return false;
        }
        // The mover must be able to get there; an aircraft needs no path.
        if (forWhom.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) == 0
            && navigation.FindPath(forWhom.X, forWhom.Y, x, y, locomotor.Surfaces, forWhom.Layer, 0).Status != PathStatus.Found) return false;
        return true;
    }
}
