using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>The geometry kinds of <c>GeometryInfo</c> as the partition's shape test reads them.</summary>
internal enum CollideGeometry
{
    Sphere,
    Cylinder,
    Box,
}

/// <summary>
/// One object's shape where the partition looks at it (<c>CollideInfo</c>: the object's position, its geometry and its angle). The position is the object's canonical one: the
/// feet of a box or cylinder, the centre of a sphere (<c>GeometryInfo::getZDeltaToCenterPosition</c>).
/// </summary>
internal readonly record struct CollideShape(CollideGeometry Kind, Fixed X, Fixed Y, Fixed Z, Fixed Major, Fixed Minor, Fixed Height, Fixed Angle)
{
    /// <summary>
    /// The shape of an object of <paramref name="template"/> standing at the given place. A sphere's and a cylinder's minor radius is its major one (<c>GeometryInfo::set</c>). A template
    /// without a <c>Geometry</c> is read as the rest of the port reads it (<see cref="ObjectTemplate.BoundingSphereRadius"/>): a cylinder of the radius and height it names, nothing when it
    /// names none - the original's default is a sphere of radius 1 (<c>ThingTemplate</c>'s <c>m_geometryInfo(GEOMETRY_SPHERE, FALSE, 1, 1, 1)</c>), a difference of the template, not of
    /// this test.
    /// </summary>
    public static CollideShape Of(ObjectTemplate template, Fixed x, Fixed y, Fixed z, Fixed angle)
    {
        var geometry = template.Geometry;
        if (geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase))
            return new(CollideGeometry.Box, x, y, z, template.GeometryMajorRadius, template.GeometryMinorRadius, template.GeometryHeight, angle);
        var radius = template.GeometryMajorRadius;
        return geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase)
            ? new(CollideGeometry.Sphere, x, y, z, radius, radius, radius, angle)
            : new(CollideGeometry.Cylinder, x, y, z, radius, radius, template.GeometryHeight, angle);
    }

    /// <summary><c>GeometryInfo::getMaxHeightAbovePosition</c>.</summary>
    public Fixed Top => Z + (Kind == CollideGeometry.Sphere ? Major : Height);

    /// <summary><c>GeometryInfo::getMaxHeightBelowPosition</c>.</summary>
    public Fixed Bottom => Kind == CollideGeometry.Sphere ? Z - Major : Z;

    /// <summary>How far the shape reaches from its position along either map axis at most, however it is turned: what a cheap test may reject beyond. A circle is tested as a square of its radius, which reaches less than twice the radius.</summary>
    public Fixed Reach => Kind == CollideGeometry.Box ? Major + Minor : Major + Major;
}

/// <summary>
/// <c>PartitionData::collidesWith</c> and the tests it calls (<c>PartitionManager.cpp</c>, <c>theCollideTestProcs</c>): whether two objects' shapes touch. The heights first
/// (a sphere reaches its radius above and below its position, a box or cylinder its height above its feet), then a test per pair of geometries:
/// <list type="bullet">
/// <item>sphere and sphere by the distance of the centres (<c>collideTest_Sphere_Sphere</c>);</item>
/// <item>a sphere and a box or cylinder: within the other's height by the sphere's full radius, above or below it by the circle where the sphere cuts the other's top or bottom
/// plane (<c>z_collideTest_Sphere_Nonsphere</c>);</item>
/// <item>two boxes or cylinders side to side, or when their centres lie closer than the smallest of their four radii (<c>z_collideTest_Nonsphere_Nonsphere</c>);</item>
/// <item>in the plane two circles by their distance (<c>xy_collideTest_Circle_Circle</c>); anything with a box as two rectangles, a circle taken as the square of its radius turned
/// by its object's angle (<c>xy_collideTest_Rect_Circle</c>), touching when a corner of either lies in the other (<c>xy_collideTest_Rect_Rect</c>,
/// <c>testRotatedPointsAgainstRect</c>) - not a full overlap test, as in the original.</item>
/// </list>
/// The contact point and normal of <c>CollideLocAndNormal</c> are not formed: their users here form their own.
/// </summary>
internal static class CollideShapes
{
    /// <summary><c>PartitionData::collidesWith</c> without the <c>KINDOF_NO_COLLIDE</c> check, which needs the objects (<see cref="Collides(Entity, in CollideShape, Entity, in CollideShape)"/>).</summary>
    public static bool Collides(in CollideShape a, in CollideShape b)
    {
        // "invariant for all geometries: first do z collision check."
        if (a.Top < b.Bottom || a.Bottom > b.Top) return false;
        // Nothing the shapes can reach on either axis - not in the original, which has the partition's cells for this.
        var reach = a.Reach + b.Reach;
        if ((a.X - b.X).Abs() > reach || (a.Y - b.Y).Abs() > reach) return false;
        var aSphere = a.Kind == CollideGeometry.Sphere;
        var bSphere = b.Kind == CollideGeometry.Sphere;
        if (aSphere && bSphere) return SpheresTouch(a, b);
        if (aSphere) return SphereTouchesNonsphere(a, b);
        if (bSphere) return SphereTouchesNonsphere(b, a);
        return NonspheresTouch(a, b);
    }

    /// <summary><c>PartitionData::collidesWith</c>: <c>KINDOF_NO_COLLIDE</c> on either object is no contact ("we don't always want to"), else the shapes decide.</summary>
    public static bool Collides(Entity first, in CollideShape a, Entity second, in CollideShape b)
        => Collides(a, b) && !first.Template.IsKindOf("NO_COLLIDE") && !second.Template.IsKindOf("NO_COLLIDE");

    private static bool SpheresTouch(in CollideShape a, in CollideShape b)
    {
        var (dx, dy, dz) = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        return SquaredReach.Of(a.Major + b.Major).Contains(dx * dx + dy * dy + dz * dz);
    }

    // z_collideTest_Sphere_Nonsphere
    private static bool SphereTouchesNonsphere(in CollideShape sphere, in CollideShape other)
    {
        // "case 1: center of sphere is within the nonsphere z-space."
        if (sphere.Z >= other.Z && sphere.Z <= other.Top) return InThePlane(sphere.X, sphere.Y, sphere.Major, sphere.Angle, other);
        // "2a: sphere is below the nonsphere" and "2b: sphere is above the nonsphere": the slice of the sphere in the other's bottom or top plane.
        var depth = sphere.Z < other.Z ? other.Z - sphere.Z : sphere.Z - other.Top;
        if (depth > sphere.Major) return false;
        // sqrt(r² - d²) as sqrt(r - d) · sqrt(r + d): the square of a radius never leaves the fixed-point range.
        var slice = Fixed.Sqrt(sphere.Major - depth) * Fixed.Sqrt(sphere.Major + depth);
        return InThePlane(sphere.X, sphere.Y, slice, sphere.Angle, other);
    }

    // The circle (a sphere's slice) against a box or cylinder in the plane: xy_collideTest_Circle_Circle or xy_collideTest_Circle_Rect.
    private static bool InThePlane(Fixed x, Fixed y, Fixed radius, Fixed angle, in CollideShape other)
        => other.Kind == CollideGeometry.Box
            ? RectanglesTouch(other.X, other.Y, other.Major, other.Minor, other.Angle, x, y, radius, radius, angle)
            : CirclesTouch(x, y, radius, other.X, other.Y, other.Major);

    // z_collideTest_Nonsphere_Nonsphere: "we already know that there is a z-intersection (our caller filters that out)".
    private static bool NonspheresTouch(in CollideShape a, in CollideShape b)
    {
        // "Preflight for close objects": centres closer than the smallest radius of either.
        var (dx, dy) = (a.X - b.X, a.Y - b.Y);
        var smallest = Fixed.Min(Fixed.Min(a.Major, a.Minor), Fixed.Min(b.Major, b.Minor));
        if (Strictly(smallest, dx * dx + dy * dy)) return true;
        if (a.Kind == CollideGeometry.Cylinder && b.Kind == CollideGeometry.Cylinder) return CirclesTouch(a.X, a.Y, a.Major, b.X, b.Y, b.Major);
        return RectanglesTouch(a.X, a.Y, a.Major, a.Minor, a.Angle, b.X, b.Y, b.Major, b.Minor, b.Angle);
    }

    // sqr(minRadius) > dSqr
    private static bool Strictly(Fixed radius, Fixed distanceSquared) => SquaredReach.Of(radius).ContainsStrictly(distanceSquared);

    private static bool CirclesTouch(Fixed ax, Fixed ay, Fixed aRadius, Fixed bx, Fixed by, Fixed bRadius)
    {
        var (dx, dy) = (bx - ax, by - ay);
        return SquaredReach.Of(aRadius + bRadius).Contains(dx * dx + dy * dy);
    }

    // xy_collideTest_Rect_Rect: a corner of the first in the second, or one of the second in the first.
    private static bool RectanglesTouch(Fixed ax, Fixed ay, Fixed aMajor, Fixed aMinor, Fixed aAngle, Fixed bx, Fixed by, Fixed bMajor, Fixed bMinor, Fixed bAngle)
    {
        var (aCos, aSin) = (FixedTrig.Cos(aAngle), FixedTrig.Sin(aAngle));
        var (bCos, bSin) = aAngle == bAngle ? (aCos, aSin) : (FixedTrig.Cos(bAngle), FixedTrig.Sin(bAngle));
        return CornerInside(ax, ay, aMajor, aMinor, aCos, aSin, bx, by, bMajor, bMinor, bCos, bSin)
            || CornerInside(bx, by, bMajor, bMinor, bCos, bSin, ax, ay, aMajor, aMinor, aCos, aSin);
    }

    // rectToFourPoints of the first, testRotatedPointsAgainstRect against the second.
    private static bool CornerInside(Fixed x, Fixed y, Fixed major, Fixed minor, Fixed cos, Fixed sin, Fixed rx, Fixed ry, Fixed rMajor, Fixed rMinor, Fixed rCos, Fixed rSin)
    {
        var (exc, eyc, exs, eys) = (major * cos, minor * cos, major * sin, minor * sin);
        return PointInside(x - exc - eys, y + eyc - exs, rx, ry, rMajor, rMinor, rCos, rSin)
            || PointInside(x + exc - eys, y + eyc + exs, rx, ry, rMajor, rMinor, rCos, rSin)
            || PointInside(x - exc + eys, y - eyc - exs, rx, ry, rMajor, rMinor, rCos, rSin)
            || PointInside(x + exc + eys, y - eyc + exs, rx, ry, rMajor, rMinor, rCos, rSin);
    }

    // The point turned back into the rectangle's own axes, within its half-lengths.
    private static bool PointInside(Fixed px, Fixed py, Fixed rx, Fixed ry, Fixed rMajor, Fixed rMinor, Fixed rCos, Fixed rSin)
    {
        var (dx, dy) = (px - rx, py - ry);
        return (dx * rCos + dy * rSin).Abs() <= rMajor && (dy * rCos - dx * rSin).Abs() <= rMinor;
    }
}
