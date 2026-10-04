using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// All triangles of one road template on one map, in world space (X east, Y north, Z up). <see cref="Stacking"/> is the draw order among road
/// templates (0 first, higher on top). <see cref="Colors"/> is the static terrain lighting at the nearest height vertex (alpha 255); the road texture's own alpha
/// blends the road into the terrain (<c>SRCALPHA</c>/<c>INVSRCALPHA</c>, no depth write). Triangles are counter-clockwise seen from above.
/// </summary>
public sealed record RoadMesh(
    string Template,
    string Texture,
    int Stacking,
    ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector2> Uv,
    ImmutableArray<Rgba32> Colors,
    ImmutableArray<int> Indices,
    Aabb Bounds)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>Counted outcome of building the roads of one map; nothing the reference would skip or alter is skipped silently.</summary>
public sealed record RoadStats(
    int RoadPointPairs,
    int Segments,
    int Pieces,
    int Curves,
    int Tees,
    int FourWays,
    int YJunctions,
    int SlantedTees,
    int AlphaJoins,
    int CrossTypeJoins,
    int Mitres,
    int CurvesAborted,
    int DuplicateSegments,
    int DroppedBySegmentBudget,
    int UnpairedRoadPoints,
    int UnknownTemplateSegments,
    int Triangles,
    ImmutableSortedDictionary<string, int> UnknownTemplates,
    ImmutableSortedDictionary<string, int> SegmentsByTemplate,
    ImmutableSortedDictionary<string, int> PiecesByType,
    ImmutableArray<RoadJunction> Junctions);

/// <summary>Where a junction piece (tee, crossing, Y, H, alpha join) sits, for audits and captures.</summary>
public sealed record RoadJunction(RoadPiece Piece, string Template, Vector2 Location);

/// <summary>
/// The roads of one map as renderer-neutral triangles (<c>docs/CLIENT-ROADS.md</c>): the <c>FLAG_ROAD_POINT1</c>/<c>POINT2</c> map objects, laid out like
/// <c>W3DRoadBuffer</c> (<see cref="RoadLayout"/>) and turned into terrain-hugging textured strips (<see cref="RoadGeometry"/>). Bridge points are not roads and
/// are ignored here. Pure and deterministic: equal input gives equal meshes.
/// </summary>
public sealed record RoadModel(ImmutableArray<RoadMesh> Meshes, RoadStats Stats)
{
    public static RoadModel Empty { get; } = new([], new RoadStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        ImmutableSortedDictionary<string, int>.Empty, ImmutableSortedDictionary<string, int>.Empty, ImmutableSortedDictionary<string, int>.Empty, []));

    public static RoadModel Build(ImmutableArray<MapObject> objects, TerrainHeightSampler heights, RoadCatalog catalog, TerrainLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lighting);
        var layout = new RoadLayout(catalog);
        layout.Build(objects);
        foreach (var road in layout.Roads) RoadGeometry.Load(road, heights);

        // Static lighting per height vertex, computed on demand (a map has a few thousand road vertices, not the whole field).
        var lit = new Dictionary<(int, int), Rgba32>();
        Rgba32 LightAt(float x, float y)
        {
            var vx = Math.Clamp((int)(x / TerrainHeightSampler.CellSize + 0.5f) + heights.Border, 0, heights.VertexWidth - 1);
            var vy = Math.Clamp((int)(y / TerrainHeightSampler.CellSize + 0.5f) + heights.Border, 0, heights.VertexHeight - 1);
            if (!lit.TryGetValue((vx, vy), out var color))
            {
                color = Rgba32.FromUnit(lighting.Shade(heights.VertexNormal(vx, vy)), 255);
                lit.Add((vx, vy), color);
            }
            return color;
        }

        var meshes = ImmutableArray.CreateBuilder<RoadMesh>();
        foreach (var template in catalog.Templates)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Rgba32>();
            var indices = new List<int>();
            foreach (var piece in Enum.GetValues<RoadPiece>())
                foreach (var road in layout.Roads)
                {
                    if (road.UniqueId != template.Id || road.Type != piece || road.Indices.Length == 0) continue;
                    var offset = positions.Count;
                    foreach (var position in road.Positions)
                    {
                        positions.Add(position);
                        colors.Add(LightAt(position.X, position.Y));
                    }
                    uvs.AddRange(road.Uvs);
                    for (var t = 0; t < road.Indices.Length; t += 3)
                    {
                        var a = road.Indices[t] + offset;
                        var b = road.Indices[t + 1] + offset;
                        var c = road.Indices[t + 2] + offset;
                        // Counter-clockwise seen from above (the reference leaves the winding to the segment's direction).
                        var area = (positions[b].X - positions[a].X) * (positions[c].Y - positions[a].Y) - (positions[b].Y - positions[a].Y) * (positions[c].X - positions[a].X);
                        if (area >= 0f) { indices.Add(a); indices.Add(b); indices.Add(c); }
                        else { indices.Add(a); indices.Add(c); indices.Add(b); }
                    }
                }
            if (indices.Count == 0) continue;
            meshes.Add(new RoadMesh(template.Name, template.Texture, layout.StackingOf(template.Id), [..positions], [..uvs], [..colors], [..indices],
                Aabb.FromPoints(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(positions))));
        }

        var counters = layout.Counters;
        var byTemplate = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var road in layout.Roads)
            if (road.Type == RoadPiece.Segment && catalog.ById(road.UniqueId) is { } found)
                byTemplate[found.Name] = byTemplate.GetValueOrDefault(found.Name) + 1;
        var unknown = layout.UnknownTemplates.Values.Sum();
        var stats = new RoadStats(
            counters.MapSegments + unknown,
            layout.Roads.Count(road => road.Type == RoadPiece.Segment),
            layout.Roads.Count,
            counters.Curves,
            counters.Tees,
            counters.FourWays,
            counters.YJunctions,
            counters.SlantedTees,
            counters.AlphaJoins,
            counters.CrossTypeJoins,
            counters.Mitres,
            counters.CurvesAborted,
            counters.DuplicateSegments,
            counters.DroppedBySegmentBudget,
            counters.UnmatchedPoint1,
            unknown,
            meshes.Sum(mesh => mesh.TriangleCount),
            layout.UnknownTemplates.ToImmutableSortedDictionary(StringComparer.Ordinal),
            byTemplate.ToImmutableSortedDictionary(StringComparer.Ordinal),
            layout.Roads.GroupBy(road => road.Type).ToImmutableSortedDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.Ordinal),
            [..layout.Roads.Where(road => road.Type is not (RoadPiece.Segment or RoadPiece.Curve))
                .Select(road => new RoadJunction(road.Type, catalog.ById(road.UniqueId)!.Name, road.P1.Loc))]);
        return new RoadModel(meshes.ToImmutable(), stats);
    }
}
