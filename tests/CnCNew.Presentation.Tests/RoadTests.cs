using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Presentation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>Road layout and geometry on small synthetic maps (<c>docs/CLIENT-ROADS.md</c>); the real-data counterpart is <see cref="RoadRealDataTests"/>.</summary>
internal static class RoadTests
{
    /// <summary><c>FLOAT_AMOUNT</c>: roads float one eighth of a height step above the highest cell corner.</summary>
    private const float Float = TerrainHeightSampler.HeightScale / 8f;

    private static readonly RoadCatalog Catalog = new(
        [("Wide", "wide.tga", 20f, 0.9f), ("Narrow", "narrow.tga", 10f, 0.9f)], 4000);

    public static int Run(string[] args)
    {
        var t = new TestHarness("Road");
        t.Run("catalog ids, lookup and validation", CatalogBasics);
        t.Run("straight segment geometry, uv and lighting", StraightSegment);
        t.Run("columns follow a hill, flat across the width", Hill);
        t.Run("90 degree corner becomes curve pieces", CurvedCorner);
        t.Run("angled corner is mitred", MitredCorner);
        t.Run("tee, slanted tee, Y and crossing", Junctions);
        t.Run("alpha join and stacking against another road type", CrossTypeJoin);
        t.Run("unknown templates, duplicates, unpaired points and the segment budget are counted", Counting);
        t.Run("builds are deterministic and counter-clockwise", Deterministic);
        t.Run("sampler: max cell height and vertex normals", Sampler);
        return t.Failures;
    }

    private static TerrainHeightSampler Flat(int size = 60, byte height = 16)
        => new(SyntheticTerrain.Map(size, size, 0, (_, _) => height).HeightField);

    private static IEnumerable<MapObject> Pair(string name, float x1, float y1, float x2, float y2, int flags1 = 0, int flags2 = 0)
    {
        yield return new MapObject(name, (decimal)x1, (decimal)y1, 0m, 0m, MapObject.FlagRoadPoint1 | flags1, MapProperties.Empty);
        yield return new MapObject(name, (decimal)x2, (decimal)y2, 0m, 0m, MapObject.FlagRoadPoint2 | flags2, MapProperties.Empty);
    }

    private static RoadModel Build(IEnumerable<MapObject> objects, TerrainHeightSampler? heights = null, RoadCatalog? catalog = null)
        => RoadModel.Build([..objects], heights ?? Flat(), catalog ?? Catalog, SyntheticTerrain.Lighting);

    private static void CatalogBasics()
    {
        Assert(Catalog.Count == 2 && Catalog.MaxSegments == 4000, "two templates and the budget");
        Assert(Catalog.Templates[0].Name == "Wide" && Catalog.Templates[0].Id == 1 && Catalog.Templates[1].Id == 2, "ids follow the given (INI) order, not the name order");
        Assert(Catalog.ById(2)!.Name == "Narrow" && Catalog.ById(3) is null && Catalog.ById(0) is null, "lookup by id");
        Assert(Catalog.TryGet("Wide", out var wide) && wide is { Texture: "wide.tga", Width: 20f, WidthInTexture: 0.9f }, "lookup by name");
        Assert(!Catalog.TryGet("wide", out _), "names are case sensitive like AsciiString::operator==");
        Throws<ArgumentException>(() => _ = new RoadCatalog([("A", "a.tga", 1f, 1f), ("A", "b.tga", 1f, 1f)], 10), "duplicate template");
        Throws<ArgumentException>(() => _ = new RoadCatalog([("A", "a.tga", 0f, 1f)], 10), "zero width");
        Throws<ArgumentOutOfRangeException>(() => _ = new RoadCatalog([("A", "a.tga", 1f, 1f)], 0), "empty budget");
    }

    private static void StraightSegment()
    {
        var model = Build(Pair("Wide", 100, 100, 200, 100));
        Assert(model.Meshes.Length == 1 && model.Meshes[0].Template == "Wide" && model.Meshes[0].Texture == "wide.tga", "one mesh for the one template");
        var mesh = model.Meshes[0];
        // Flat terrain: every intermediate column lies on the line between its neighbours and is dropped, leaving the two end columns.
        Assert(mesh.Positions.Length == 4 && mesh.TriangleCount == 2, $"two columns, {mesh.Positions.Length} vertices, {mesh.TriangleCount} triangles");
        Assert(model.Stats is { RoadPointPairs: 1, Segments: 1, Pieces: 1, Triangles: 2 }, $"stats {model.Stats}");
        foreach (var position in mesh.Positions)
        {
            Assert(position.X is 100f or 200f, $"x {position.X}");
            Assert(MathF.Abs(MathF.Abs(position.Y - 100f) - 9f) < 1e-4f, $"half width 20 * 0.9 / 2 = 9, y {position.Y}");
            Assert(MathF.Abs(position.Z - (10f + Float)) < 1e-4f, $"height 16 * 0.625 plus the float amount, z {position.Z}");
        }
        // One texture width spans four road widths along the road; the strip sprite is centred at v = 85/512, 0.9 wide in a quarter of the texture.
        var topEnd = mesh.Positions.Select((position, i) => (position, uv: mesh.Uv[i])).Single(v => v.position.X == 200f && v.position.Y > 100f);
        Near(1.25, topEnd.uv.X, 1e-4, "u at 100 units = 100 / (20 * 4)");
        Near(85.0 / 512 - 9.0 / 80, topEnd.uv.Y, 1e-4, "v = vOffset - V / (scale * 4)");
        Assert(mesh.Colors.All(color => color == new Rgba32(204, 204, 204, 255)), "up-facing vertices get 0.2 + 0.6 = 0.8 of the synthetic light");
        Assert(mesh.Bounds.Min.X == 100f && mesh.Bounds.Max.X == 200f, "bounds");
    }

    private static void Hill()
    {
        // A hill across x: height 0 .. 100 .. 0 over 40 vertices, constant along y.
        var map = SyntheticTerrain.Map(40, 40, 0, (x, _) => (byte)Math.Max(0, 100 - Math.Abs(x - 20) * 10));
        var heights = new TerrainHeightSampler(map.HeightField);
        var model = Build(Pair("Wide", 50, 200, 350, 200), heights);
        var mesh = model.Meshes.Single();
        Assert(mesh.Positions.Length > 4 && mesh.Positions.Length % 2 == 0, $"a hill keeps columns, {mesh.Positions.Length}");
        for (var column = 0; column < mesh.Positions.Length; column += 2)
        {
            var low = mesh.Positions[column];
            var high = mesh.Positions[column + 1];
            Assert(low.Z == high.Z && low.X == high.X, "both edge points of a column share x and the flattened height");
            var expected = Enumerable.Range(0, 3).Max(j => heights.MaxCellHeight(low.X, low.Y + (j - 1) * 9f)) + Float;
            Assert(MathF.Abs(low.Z - expected) < 1e-3f, $"column z {low.Z} is the largest cell height under the column plus the float amount ({expected})");
        }
        Assert(mesh.Positions.Max(position => position.Z) > 60f, "the road climbs the hill");
    }

    private static void CurvedCorner()
    {
        var model = Build([..Pair("Wide", 100, 100, 200, 100), ..Pair("Wide", 200, 100, 200, 200)]);
        Assert(model.Stats is { Segments: 2, Curves: 1, Mitres: 0, CurvesAborted: 0 }, $"one curved corner {model.Stats}");
        Assert(model.Stats.PiecesByType["Curve"] == 3 && model.Stats.PiecesByType["Segment"] == 2, "90 degrees = three 30 degree steps");
        // The curve pieces fill the inside of the old corner: no vertex goes beyond the square the two segments span plus half a road width.
        Assert(model.Meshes.Single().Positions.All(position => position.X is >= 80f and <= 220f && position.Y is >= 80f and <= 220f), "curve stays near the corner");
    }

    private static void MitredCorner()
    {
        var model = Build([..Pair("Wide", 100, 100, 200, 100, flags2: MapObject.FlagRoadCornerAngled), ..Pair("Wide", 200, 100, 200, 200, flags1: MapObject.FlagRoadCornerAngled)]);
        Assert(model.Stats is { Segments: 2, Curves: 0, Mitres: 1 }, $"angled flag mitres {model.Stats}");
        Assert(model.Stats.PiecesByType.Keys.SequenceEqual(["Segment"]), "only the two segments remain");
        var positions = model.Meshes.Single().Positions;
        Assert(positions.Any(p => MathF.Abs(p.X - 191f) < 1e-3f && MathF.Abs(p.Y - 109f) < 1e-3f), "inner mitre point (191, 109)");
        Assert(positions.Any(p => MathF.Abs(p.X - 209f) < 1e-3f && MathF.Abs(p.Y - 91f) < 1e-3f), "outer mitre point (209, 91)");
    }

    private static void Junctions()
    {
        var tee = Build([..Pair("Wide", 100, 200, 200, 200), ..Pair("Wide", 200, 200, 300, 200), ..Pair("Wide", 200, 200, 200, 300)]);
        Assert(tee.Stats is { Tees: 1, YJunctions: 0, SlantedTees: 0, FourWays: 0, Segments: 3 } && tee.Stats.PiecesByType["Tee"] == 1, $"straight tee {tee.Stats}");
        Assert(tee.Stats.Junctions.Length == 1 && tee.Stats.Junctions[0] is { Piece: RoadPiece.Tee, Template: "Wide" }, "the junction is reported");

        var slanted = Build([..Pair("Wide", 200, 300, 300, 300), ..Pair("Wide", 300, 300, 400, 300), ..Pair("Wide", 300, 300, 360, 360)]);
        Assert(slanted.Stats is { SlantedTees: 1, Tees: 0, YJunctions: 0 }, $"45 degree branch is an H piece {slanted.Stats}");
        Assert(slanted.Stats.PiecesByType.ContainsKey("ThreeWayH") ^ slanted.Stats.PiecesByType.ContainsKey("ThreeWayHFlip"), "H or flipped H");

        var y = Build([..Pair("Wide", 300, 200, 300, 300), ..Pair("Wide", 300, 300, 225, 375), ..Pair("Wide", 300, 300, 375, 375)]);
        Assert(y.Stats is { YJunctions: 1, Tees: 0, SlantedTees: 0 } && y.Stats.PiecesByType["ThreeWayY"] == 1, $"two arms 45 degrees either side of the stem make a Y {y.Stats}");

        var cross = Build([..Pair("Wide", 100, 300, 300, 300), ..Pair("Wide", 300, 300, 500, 300), ..Pair("Wide", 300, 100, 300, 300), ..Pair("Wide", 300, 300, 300, 500)]);
        Assert(cross.Stats.FourWays == 1 && cross.Stats.PiecesByType["FourWay"] == 1 && cross.Stats.Tees == 0, $"four arms make a crossing {cross.Stats}");

        // Every junction mesh stays finite and inside the neighbourhood of the junction plus the arms.
        foreach (var model in new[] { tee, slanted, y, cross })
            Assert(model.Meshes.Single().Positions.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X is > 0 and < 600 && p.Y is > 0 and < 600), "finite and on the map");
    }

    private static void CrossTypeJoin()
    {
        // The road "Wide" ends on the side of "Narrow": its open end is flagged as a join.
        var model = Build([..Pair("Wide", 100, 100, 200, 100, flags2: MapObject.FlagRoadJoin), ..Pair("Narrow", 200, 50, 200, 150)]);
        Assert(model.Stats is { AlphaJoins: 1, CrossTypeJoins: 1 } && model.Stats.PiecesByType["AlphaJoin"] == 1, $"alpha join against the other type {model.Stats}");
        var wide = model.Meshes.Single(mesh => mesh.Template == "Wide");
        var narrow = model.Meshes.Single(mesh => mesh.Template == "Narrow");
        Assert(wide.Stacking > narrow.Stacking, $"the joining road is drawn above the road it joins: {wide.Stacking} / {narrow.Stacking}");
        Assert(model.Stats.Junctions.Single() is { Piece: RoadPiece.AlphaJoin, Template: "Wide" }, "join reported");

        var open = Build(Pair("Wide", 100, 100, 200, 100, flags2: MapObject.FlagRoadJoin));
        Assert(open.Stats is { AlphaJoins: 1, CrossTypeJoins: 0 }, $"a join against nothing is still an alpha cap {open.Stats}");
        Assert(open.Meshes.Single().Stacking == 0, "no stacking change without another type");
    }

    private static void Counting()
    {
        var unknown = Build([..Pair("Missing", 100, 100, 200, 100), ..Pair("wide", 100, 150, 200, 150), ..Pair("Wide", 100, 200, 200, 200)]);
        Assert(unknown.Stats is { UnknownTemplateSegments: 2, Segments: 1, RoadPointPairs: 3 }, $"unknown names are counted, not drawn {unknown.Stats}");
        Assert(unknown.Stats.UnknownTemplates["Missing"] == 1 && unknown.Stats.UnknownTemplates["wide"] == 1, "by name, case sensitive");

        var duplicate = Build([..Pair("Wide", 100, 100, 200, 100), ..Pair("Wide", 200, 100, 100, 100)]);
        Assert(duplicate.Stats is { Segments: 1, DuplicateSegments: 1 }, $"the same segment twice, either direction {duplicate.Stats}");

        var unpaired = Build([new MapObject("Wide", 100m, 100m, 0m, 0m, MapObject.FlagRoadPoint1, MapProperties.Empty),
            new MapObject("Wide", 300m, 300m, 0m, 0m, 0, MapProperties.Empty), ..Pair("Wide", 100, 200, 200, 200)]);
        Assert(unpaired.Stats is { UnpairedRoadPoints: 1, Segments: 1 }, $"a first point without a second is counted {unpaired.Stats}");

        var degenerate = Build(Pair("Wide", 150, 150, 150, 150));
        Assert(degenerate.Stats.Segments == 1 && degenerate.Meshes.Single().Positions.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y)), "identical end points are pushed 0.25 apart");

        var budget = Build([..Pair("Wide", 100, 100, 150, 100), ..Pair("Wide", 100, 200, 150, 200), ..Pair("Wide", 100, 300, 150, 300), ..Pair("Wide", 100, 400, 150, 400)],
            catalog: new RoadCatalog([("Wide", "wide.tga", 20f, 0.9f)], 2));
        Assert(budget.Stats is { Segments: 2, DroppedBySegmentBudget: 2 }, $"the segment budget drops the rest, counted {budget.Stats}");
    }

    private static void Deterministic()
    {
        MapObject[] objects =
        [
            ..Pair("Wide", 100, 300, 300, 300), ..Pair("Wide", 300, 300, 500, 300), ..Pair("Wide", 300, 100, 300, 300), ..Pair("Wide", 300, 300, 300, 500),
            ..Pair("Narrow", 50, 50, 150, 50), ..Pair("Narrow", 150, 50, 150, 120),
        ];
        var first = Build(objects);
        var second = Build(objects);
        Assert(first.Meshes.Length == second.Meshes.Length, "same mesh count");
        for (var i = 0; i < first.Meshes.Length; i++)
        {
            Assert(first.Meshes[i].Positions.SequenceEqual(second.Meshes[i].Positions) && first.Meshes[i].Uv.SequenceEqual(second.Meshes[i].Uv)
                && first.Meshes[i].Indices.SequenceEqual(second.Meshes[i].Indices) && first.Meshes[i].Colors.SequenceEqual(second.Meshes[i].Colors), $"mesh {i} identical");
            var mesh = first.Meshes[i];
            for (var t = 0; t < mesh.Indices.Length; t += 3)
            {
                var a = mesh.Positions[mesh.Indices[t]];
                var b = mesh.Positions[mesh.Indices[t + 1]];
                var c = mesh.Positions[mesh.Indices[t + 2]];
                Assert((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) >= 0f, $"{mesh.Template} triangle {t / 3} is counter-clockwise seen from above");
            }
        }
        Assert(first.Stats.Pieces == second.Stats.Pieces && first.Stats.Triangles == second.Stats.Triangles
            && first.Stats.PiecesByType.SequenceEqual(second.Stats.PiecesByType) && first.Stats.Junctions.SequenceEqual(second.Stats.Junctions), "same stats");
    }

    private static void Sampler()
    {
        // A spike at height vertex (1, 1) of a flat map.
        var map = SyntheticTerrain.Map(6, 6, 0, (x, y) => (byte)(x == 1 && y == 1 ? 80 : 16));
        var sampler = new TerrainHeightSampler(map.HeightField);
        Near(50.0, sampler.MaxCellHeight(5f, 5f), 1e-4, "cell 0 includes the spike corner");
        Near(50.0, sampler.MaxCellHeight(-5f, 5f), 1e-4, "the index truncates towards zero: x in (-10, 0) is cell 0");
        Near(50.0, sampler.MaxCellHeight(15f, 15f), 1e-4, "cell (1, 1) has the spike as its lower corner");
        Near(10.0, sampler.MaxCellHeight(35f, 35f), 1e-4, "a cell away from the spike");
        Near(10.0, sampler.MaxCellHeight(1000f, 1000f), 1e-4, "clamped to the last cell");

        // The terrain's own per-vertex normals are what the sampler hands the road lighting.
        var hilly = SyntheticTerrain.Map(8, 7, 2, (x, y) => (byte)(16 + 5 * x + 3 * y * (x % 3)));
        var hills = new TerrainHeightSampler(hilly.HeightField);
        var model = TerrainRenderModel.Build(hilly, SyntheticTerrain.Lookup(), SyntheticTerrain.Options(), "hilly");
        var mesh = model.Chunks[0].Base;
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var position = mesh.Positions[i];
            var normal = hills.VertexNormal((int)MathF.Round(position.X / 10f) + hills.Border, (int)MathF.Round(position.Y / 10f) + hills.Border);
            Assert(Vector3.Distance(normal, mesh.Normals[i]) < 1e-5f, $"vertex normal at {position}");
        }
    }
}
