using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// The ground plan of an object for <c>PartitionManager::geomCollidesWithGeom</c>: a rectangle, or a circle. Two circles touch by the sum of their radii. A circle against a rectangle is the
/// square around it (<c>xy_collideTest_Rect_Circle</c>: "still not as accurate as is could be"), and two rectangles collide when a corner of one lies in the other
/// (<c>xy_collideTest_Rect_Rect</c>) - so a cross of two long rectangles does not, as in the original.
/// </summary>
internal readonly struct SiteShape
{
    private readonly Fixed _x, _y, _cos, _sin, _major, _minor;
    private readonly bool _box;

    /// <param name="grow"><c>GeometryInfo::expandFootprint</c>: both radii grow by it.</param>
    public SiteShape(ObjectTemplate template, Fixed x, Fixed y, Fixed angle, Fixed grow = default)
    {
        (_x, _y) = (x, y);
        (_cos, _sin) = (FixedTrig.Cos(angle), FixedTrig.Sin(angle));
        _box = template.IsBox;
        _major = template.GeometryMajorRadius + grow;
        _minor = (_box ? template.GeometryMinorRadius : template.GeometryMajorRadius) + grow;
    }

    public static bool Collide(in SiteShape a, in SiteShape b)
    {
        if (!a._box && !b._box)
        {
            var (dx, dy) = (b._x - a._x, b._y - a._y);
            return SquaredReach.Of(a._major + b._major).Contains(dx * dx + dy * dy);
        }
        return a.HasCornerIn(b) || b.HasCornerIn(a);
    }

    private bool HasCornerIn(in SiteShape other)
    {
        var (alongX, alongY) = (_major * _cos, _major * _sin);
        var (acrossX, acrossY) = (-_minor * _sin, _minor * _cos);
        return other.Contains(_x - alongX + acrossX, _y - alongY + acrossY) || other.Contains(_x + alongX + acrossX, _y + alongY + acrossY)
            || other.Contains(_x - alongX - acrossX, _y - alongY - acrossY) || other.Contains(_x + alongX - acrossX, _y + alongY - acrossY);
    }

    private bool Contains(Fixed pointX, Fixed pointY)
    {
        var (dx, dy) = (pointX - _x, pointY - _y);
        return (dx * _cos + dy * _sin).Abs() <= _major && (dy * _cos - dx * _sin).Abs() <= _minor;
    }
}
