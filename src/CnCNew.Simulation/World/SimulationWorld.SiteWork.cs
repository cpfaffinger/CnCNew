using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// The ground plan of a building whose site was cleared and flattened (<c>BuildAssistant::clearRemovableForConstruction</c>'s trees and props, <c>TerrainLogic::flattenTerrain</c>): a box
/// (<see cref="Major"/> and <see cref="Minor"/> half-sizes) or a circle (<see cref="Major"/> the radius), at a place and a heading. <see cref="Small"/> geometry (<c>GeometryIsSmall</c>) clears
/// but does not flatten.
/// </summary>
public readonly record struct ClearedSite(bool Box, Fixed Major, Fixed Minor, Fixed X, Fixed Y, Fixed Angle, bool Small)
{
    /// <summary>
    /// Whether the site touches an upright cylinder of <paramref name="radius"/> at (<paramref name="x"/>, <paramref name="y"/>), as <c>PartitionManager::geomCollidesWithGeom</c> answers it in plan:
    /// a tree is such a cylinder (<c>W3DTreeBuffer::removeTreesForConstruction</c>: twice the tree radius of 7), a prop one of twice its model's radius.
    /// </summary>
    public bool Touches(Fixed x, Fixed y, Fixed radius)
        => SiteShape.Collide(SiteShape.Exit(X, Y, Angle, Major, Minor, Box), SiteShape.Exit(x, y, Fixed.Zero, radius, radius, false));
}

/// <summary>
/// What a building does to the ground it is begun on. <c>buildObjectNow</c> removes the trees and props of the client (<c>TheTerrainVisual->removeTreesAndPropsForConstruction</c>), and the
/// dozer's <c>construct</c> flattens the terrain under the structure (<c>TerrainLogic::flattenTerrain</c>) before the structure is put on the ground. The world keeps a list of the sites it did
/// that for: a viewer takes the trees and props they touch off the map, and the heights - which only ever go down - follow from the list and the map, so a snapshot stores the list and a
/// restored world works the sites again in their order. The state hash holds the list's length and a running sum; a world that never builds writes nothing.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly Fixed MapCell = Fixed.FromInt(TerrainMap.CellSize);
    private static readonly Fixed MapHeightScale = Fixed.FromDecimal(CnCNew.Data.World.MapData.HeightScale);
    private readonly List<ClearedSite> _clearedSites = [];
    private ulong _clearedSum;

    /// <summary>The sites of the buildings begun so far, in the order they were begun.</summary>
    public IReadOnlyList<ClearedSite> ClearedSites => _clearedSites;

    /// <summary>The site of a building of <paramref name="template"/> at the place: remembered, and the terrain beneath it flattened.</summary>
    private void ClearAndFlatten(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Terrain is null) return;
        var box = template.IsBox;
        AddClearedSite(new ClearedSite(box, template.GeometryMajorRadius, box ? template.GeometryMinorRadius : template.GeometryMajorRadius, x, y, angle, template.GeometryIsSmall));
    }

    private void AddClearedSite(in ClearedSite site)
    {
        _clearedSites.Add(site);
        var sum = _clearedSum;
        foreach (var value in new[] { site.Box ? 1L : 0L, site.Small ? 1L : 0L, site.Major.Raw, site.Minor.Raw, site.X.Raw, site.Y.Raw, site.Angle.Raw })
            sum = State.StateHash.Mix(sum, unchecked((ulong)value));
        _clearedSum = sum;
        FlattenTerrain(site);
    }

    /// <summary>
    /// <c>TerrainLogic::flattenTerrain</c>: the vertices under the footprint (a box's rotated rectangle, a circle's disc) are averaged, the average is limited to the height at the
    /// building's own place, and every vertex under the footprint and the eight around it is lowered to it (<c>setRawMapHeight</c> never raises). A small geometry is left alone.
    /// </summary>
    private void FlattenTerrain(in ClearedSite site)
    {
        if (site.Small || Terrain is not { IsFork: true } terrain) return;
        var matched = new List<(int X, int Y)>();
        var total = Fixed.Zero;
        if (site.Box)
        {
            var (cos, sin) = (FixedTrig.Cos(site.Angle), FixedTrig.Sin(site.Angle));
            var (halfX, halfY) = (site.Major, site.Minor);
            (Fixed X, Fixed Y) topLeft = (site.X - halfX * cos - halfY * sin, site.Y + halfY * cos - halfX * sin);
            (Fixed X, Fixed Y) topRight = (site.X + halfX * cos - halfY * sin, site.Y + halfY * cos + halfX * sin);
            (Fixed X, Fixed Y) bottomRight = (site.X + halfX * cos + halfY * sin, site.Y - halfY * cos + halfX * sin);
            (Fixed X, Fixed Y) bottomLeft = (site.X - halfX * cos + halfY * sin, site.Y - halfY * cos - halfX * sin);
            var minX = Fixed.Min(Fixed.Min(topLeft.X, topRight.X), Fixed.Min(bottomRight.X, bottomLeft.X));
            var maxX = Fixed.Max(Fixed.Max(topLeft.X, topRight.X), Fixed.Max(bottomRight.X, bottomLeft.X));
            var minY = Fixed.Min(Fixed.Min(topLeft.Y, topRight.Y), Fixed.Min(bottomRight.Y, bottomLeft.Y));
            var maxY = Fixed.Max(Fixed.Max(topLeft.Y, topRight.Y), Fixed.Max(bottomRight.Y, bottomLeft.Y));
            var (firstX, lastX, firstY, lastY) = (CellFloor(minX), CellFloor(maxX), Math.Max(0, CellFloor(minY)), CellFloor(maxY));
            for (var i = firstX; i <= lastX; i++)
                for (var j = firstY; j <= lastY; j++)
                {
                    var point = (X: Fixed.FromInt(i * TerrainMap.CellSize), Y: Fixed.FromInt(j * TerrainMap.CellSize));
                    if (!InTriangle(topLeft, topRight, bottomLeft, point) && !InTriangle(topRight, bottomRight, bottomLeft, point)) continue;
                    total += terrain.HeightAt(point.X, point.Y);
                    matched.Add((i, j));
                }
        }
        else
        {
            var radius = site.Major;
            var reach = SquaredReach.Of(radius);
            var (firstX, lastX, firstY, lastY) = (CellFloor(site.X - radius), CellFloor(site.X + radius), Math.Max(0, CellFloor(site.Y - radius)), CellFloor(site.Y + radius));
            for (var i = firstX; i <= lastX; i++)
                for (var j = firstY; j <= lastY; j++)
                {
                    var (px, py) = (Fixed.FromInt(i * TerrainMap.CellSize), Fixed.FromInt(j * TerrainMap.CellSize));
                    var (dx, dy) = (px - site.X, py - site.Y);
                    if (!reach.ContainsStrictly(dx * dx + dy * dy)) continue;
                    total += terrain.HeightAt(px, py);
                    matched.Add((i, j));
                }
        }
        if (matched.Count == 0) return;
        var average = total / Fixed.FromInt(matched.Count);
        var raw = (int)(Fixed.FromDecimal(0.5m) + average / MapHeightScale).Floor();
        // "Compare to the height at the building's origin, because setRawMapHeight will only lower, not raise" - a box only; the circle takes the average as it is.
        if (site.Box) raw = Math.Min(raw, (int)(terrain.HeightAt(site.X, site.Y) / MapHeightScale).Floor());
        foreach (var (i, j) in matched)
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    terrain.LowerVertex(i + dx, j + dy, raw);
    }

    private static int CellFloor(Fixed value) => (int)(value / MapCell).Floor();

    /// <summary><c>Point_In_Triangle_2D</c>: the point is on the inner side of all three edges or on one of them; the winding of the corners does not matter, and a flat triangle holds nothing.</summary>
    private static bool InTriangle((Fixed X, Fixed Y) p0, (Fixed X, Fixed Y) p1, (Fixed X, Fixed Y) p2, (Fixed X, Fixed Y) point)
    {
        static Fixed Cross(Fixed ax, Fixed ay, Fixed bx, Fixed by) => ax * by - ay * bx;
        var area = Cross(p1.X - p0.X, p1.Y - p0.Y, p2.X - p0.X, p2.Y - p0.Y);
        if (area == Fixed.Zero) return false;
        var positive = area > Fixed.Zero;
        bool Inside(Fixed value) => positive ? value >= Fixed.Zero : value <= Fixed.Zero;
        return Inside(Cross(p1.X - p0.X, p1.Y - p0.Y, point.X - p0.X, point.Y - p0.Y))
            && Inside(Cross(p2.X - p1.X, p2.Y - p1.Y, point.X - p1.X, point.Y - p1.Y))
            && Inside(Cross(p0.X - p2.X, p0.Y - p2.Y, point.X - p2.X, point.Y - p2.Y));
    }

    private void DescribeClearedSites(IStateWriter writer)
    {
        if (_clearedSites.Count == 0) return;
        if (!writer.KeepsNames)
        {
            writer.Int("clearedSites", _clearedSites.Count);
            writer.UInt("clearedSum", _clearedSum);
            return;
        }
        writer.BeginArray("clearedSites");
        foreach (var site in _clearedSites)
        {
            writer.BeginObject(null);
            writer.Bool("box", site.Box);
            writer.Bool("small", site.Small);
            writer.Fixed("major", site.Major);
            writer.Fixed("minor", site.Minor);
            writer.Fixed("x", site.X);
            writer.Fixed("y", site.Y);
            writer.Fixed("angle", site.Angle);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadClearedSites(IStateReader reader)
    {
        if (!reader.Has("clearedSites")) return;
        foreach (var entry in reader.Array("clearedSites"))
            AddClearedSite(new ClearedSite(entry.Bool("box"), entry.Fixed("major"), entry.Fixed("minor"), entry.Fixed("x"), entry.Fixed("y"), entry.Fixed("angle"), entry.Bool("small")));
    }
}
