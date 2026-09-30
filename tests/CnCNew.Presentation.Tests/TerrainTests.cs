using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

internal static class TerrainTests
{
    private const float Eps = 1e-4f;

    public static int Run(string[] args)
    {
        var t = new TestHarness("Terrain");
        t.Run("flat map geometry, lighting and chunks", FlatMap);
        t.Run("tile quadrant UVs and atlas layout", TileQuadrants);
        t.Run("reference TGA orientation quirk", TopOriginTexture);
        t.Run("blend alpha patterns and triangle flips", BlendPatterns);
        t.Run("3-way blend pass and forced flip", ExtraBlend);
        t.Run("stored cliff UV mapping", CliffMapping);
        t.Run("steep cell UV stretch", Stretch);
        t.Run("unknown terrain type samples texel zero", UnknownTerrainType);
        t.Run("chunking of a map larger than one chunk", Chunking);
        t.Run("height sampler matches the simulation", HeightSamplerMatchesSimulation);
        t.Run("height sampler normals and extents", SamplerNormals);
        t.Run("standing water and river meshes", Water);
        t.Run("atlas mip chain", MipChain);
        t.Run("builds are deterministic", Deterministic);
        return t.Failures;
    }

    private static TerrainRenderModel Build(MapData map, bool adjustCliffs = true, TerrainTextureLookup? lookup = null)
        => TerrainRenderModel.Build(map, lookup ?? SyntheticTerrain.Lookup(), SyntheticTerrain.Options(adjustCliffs), "synthetic");

    private static void FlatMap()
    {
        var model = Build(SyntheticTerrain.Map(5, 4, 1, (_, _) => 16));
        Assert(model.CellsX == 4 && model.CellsY == 3 && model.Chunks.Length == 1, "4x3 cells in one chunk");
        var mesh = model.Chunks[0].Base;
        Assert(mesh.VertexCount == 12 * 4 && mesh.TriangleCount == 24, "four vertices and two triangles per cell");
        Assert(model.Chunks[0].Blend is null && model.Chunks[0].ExtraBlend is null, "no blend passes without blend tiles");
        // Cell (0,0) is at world (-10,-10): the border vertex; height 16 * 0.625 = 10.
        Assert(mesh.Positions[0] == new Vector3(-10, -10, 10) && mesh.Positions[2] == new Vector3(0, 0, 10), "cell corners in world space");
        Assert(mesh.Indices.Take(6).SequenceEqual(new ushort[] { 0, 1, 2, 0, 2, 3 }), "unflipped cells split along corner 0-2");
        Assert(mesh.Normals.All(normal => Vector3.Distance(normal, Vector3.UnitZ) < Eps), "flat normals point up");
        Assert(mesh.Colors.All(color => color == new Rgba32(204, 204, 204, 255)), "ambient 0.2 + diffuse 0.6 = 0.8 = 204");
        Assert(model.Chunks[0].Bounds == new Aabb(new Vector3(-10, -10, 10), new Vector3(30, 20, 10)), "chunk bounds");
        Assert(model.Stats.BaseTriangles == 24 && model.Stats.CliffCells == 0 && model.Stats.StretchedCells == 0, "stats");
        Assert(model.Stats.TotalGpuBytes == 48 * 36 + 72 * 2 + model.Atlas.BytesWithMips, "GPU bytes = vertices + indices + atlas mips");
    }

    private static void TileQuadrants()
    {
        // Cell (x, y) uses tile value x + 4 * y: grass tiles 0 and 1 (quadrants 0..3) in the first row, rock tiles in the second.
        var tiles = new short[4 * 3];
        tiles[0] = 0; tiles[1] = 1; tiles[2] = 2;
        tiles[4] = (4 << 2) | 3; tiles[5] = (7 << 2);
        var model = Build(SyntheticTerrain.Map(4, 3, 0, (_, _) => 0, tiles: tiles));
        var atlas = model.Atlas;
        Assert(atlas.Width == 2048 && atlas.Height == 192, $"atlas 2048x192, got {atlas.Width}x{atlas.Height}");
        Assert(model.Regions.Length == 2 && model.Regions[0] == new TerrainAtlasRegion(0, SyntheticTerrain.GrassClass, 4, 4, 2)
               && model.Regions[1] == new TerrainAtlasRegion(1, SyntheticTerrain.RockClass, 140, 4, 2), "class blocks placed with a 4-pixel border");
        var uv = model.Chunks[0].Base.Uv;
        Vector2 Px(float x, float y) => new(x / 2048f, y / 192f);
        // Tile 0 lies in the lower block row (y 68..132); quadrant 0 is its lower-left 32x32 pixels.
        Assert(uv[0] == Px(4, 132) && uv[1] == Px(36, 132) && uv[2] == Px(36, 100) && uv[3] == Px(4, 100), "tile value 0: lower-left quadrant, V inverted");
        // Tile value 1: right half; value 2: upper half.
        Assert(uv[4] == Px(36, 132) && uv[5] == Px(68, 132) && uv[6] == Px(68, 100), "tile value 1: lower-right quadrant");
        Assert(uv[8] == Px(4, 100) && uv[10] == Px(36, 68), "tile value 2: upper-left quadrant");
        // Rock tile 4 quadrant 3 (upper right), rock tile 7 = i 1, j 1 in the upper block row.
        var cell4 = 3 * 4; // second row starts after the three cells of row 0
        Assert(uv[cell4] == Px(140 + 32, 100) && uv[cell4 + 2] == Px(140 + 64, 68), "rock tile 4 quadrant 3");
        Assert(uv[cell4 + 4] == Px(140 + 64, 68) && uv[cell4 + 6] == Px(140 + 96, 36), "rock tile 7 lies in the upper block row");
        // The pixels behind the UVs carry the colours the TGA stored for those tiles.
        Vector2 Mid(int cell) => (uv[cell * 4] + uv[cell * 4 + 2]) * 0.5f;
        Assert(SyntheticTerrain.Sample(atlas, Mid(0)) == SyntheticTerrain.TileColor(0, 0), "tile 0 colour");
        Assert(SyntheticTerrain.Sample(atlas, Mid(2)) == SyntheticTerrain.TileColor(0, 0), "tile 0 upper quadrant has the same colour");
        Assert(SyntheticTerrain.Sample(atlas, Mid(4)) == SyntheticTerrain.TileColor(1, 1, 1), "rock tile 7 = (i 1, j 1) of rock.tga");
        // The border repeats the opposite edge of the class (wrap-around).
        Assert(SyntheticTerrain.Sample(atlas, Px(2.5f, 100)) == SyntheticTerrain.TileColor(1, 0), "left border copies the right edge");
        Assert(model.CellColor(0, 0) == SyntheticTerrain.TileColor(0, 0), "radar colour = tile average");
    }

    private static void TopOriginTexture()
    {
        var tiles = new short[4 * 3];
        tiles[0] = 4 << 2;
        var model = Build(SyntheticTerrain.Map(4, 3, 0, (_, _) => 0, tiles: tiles), lookup: SyntheticTerrain.Lookup(rockTopOrigin: true));
        var uv = model.Chunks[0].Base.Uv;
        var mid = (uv[0] + uv[2]) * 0.5f;
        // readTiles ignores the origin bit: the first stored row (the image top here) becomes the bottom, so tile 4 shows image tile (0, 1).
        Assert(SyntheticTerrain.Sample(model.Atlas, mid) == SyntheticTerrain.TileColor(0, 1, 1), "top-origin file is mirrored like the reference");
        Assert(model.Stats.MirroredTextures == 1, "mirrored texture counted");
    }

    private static MapBlendedTile Blend(bool h = false, bool v = false, bool right = false, bool left = false, byte inverted = 0, bool longDiagonal = false)
        => new((1 << 2) | 0, h, v, right, left, inverted, longDiagonal, -1);

    private static void BlendPatterns()
    {
        (MapBlendedTile Tile, byte[] Alpha, bool Flip)[] cases =
        [
            (Blend(h: true), [0, 255, 255, 0], false),
            (Blend(h: true, inverted: 1), [255, 0, 0, 255], false),
            (Blend(h: true, inverted: 2), [0, 255, 255, 0], true),
            (Blend(v: true), [0, 0, 255, 255], false),
            (Blend(v: true, inverted: 1), [255, 255, 0, 0], false),
            (Blend(right: true), [0, 0, 255, 0], true),
            (Blend(right: true, longDiagonal: true), [0, 255, 255, 255], true),
            (Blend(right: true, inverted: 1), [0, 255, 0, 0], false),
            (Blend(right: true, inverted: 1, longDiagonal: true), [255, 255, 255, 0], false),
            (Blend(left: true), [0, 0, 0, 255], false),
            (Blend(left: true, longDiagonal: true), [255, 0, 255, 255], false),
            (Blend(left: true, inverted: 1), [255, 0, 0, 0], true),
            (Blend(left: true, inverted: 1, longDiagonal: true), [255, 255, 0, 255], true),
        ];
        foreach (var (tile, alpha, flip) in cases)
        {
            var blends = new short[3 * 3];
            blends[0] = 1;
            var model = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, blends: blends, blendedTiles: [tile]));
            var chunk = model.Chunks[0];
            var mesh = chunk.Blend ?? throw new InvalidOperationException($"{tile}: blend pass expected");
            Assert(mesh.VertexCount == 4 && model.Stats.BlendCells == 1, $"{tile}: one blend cell");
            Assert(mesh.Colors.Select(color => color.A).SequenceEqual(alpha), $"{tile}: alpha {string.Join(",", mesh.Colors.Select(color => color.A))}");
            ushort[] expected = flip ? [0, 1, 3, 1, 2, 3] : [0, 1, 2, 0, 2, 3];
            Assert(mesh.Indices.SequenceEqual(expected), $"{tile}: blend flip");
            Assert(chunk.Base.Indices.Take(6).SequenceEqual(expected), $"{tile}: base pass uses the same diagonal");
            // Blend UVs: tile 1 quadrant 0 of grass (lower-left quadrant of the lower-right tile).
            Assert(mesh.Uv[0] == new Vector2(68 / 2048f, 132 / 192f), $"{tile}: blend tile UV");
        }
    }

    private static void ExtraBlend()
    {
        var extras = new short[3 * 3];
        extras[1] = 1;
        var model = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, extraBlends: extras, blendedTiles: [Blend(v: true, inverted: 2)]));
        var mesh = model.Chunks[0].ExtraBlend ?? throw new InvalidOperationException("3-way pass expected");
        Assert(model.Stats.ExtraBlendCells == 1 && mesh.Colors.Select(color => color.A).SequenceEqual(new byte[] { 0, 0, 255, 255 }), "3-way alpha");
        Assert(mesh.Indices.SequenceEqual(new ushort[] { 0, 1, 3, 1, 2, 3 }), "forced flip bit flips the 3-way cell");
        Assert(mesh.Positions[0] == new Vector3(10, 0, 0), "3-way cell (1, 0)");
        var off = TerrainRenderModel.Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, extraBlends: extras, blendedTiles: [Blend(v: true)]),
            SyntheticTerrain.Lookup(), SyntheticTerrain.Options() with { Use3WayBlends = false });
        Assert(off.Chunks[0].ExtraBlend is null, "Use3WayTerrainBlends = 0 drops the 3-way pass");
    }

    private static void CliffMapping()
    {
        var cliffIndices = new short[3 * 3];
        cliffIndices[0] = 1;
        // Offsets in TEXTURE_WIDTH units from the class block's lower-left corner; v negative is upwards.
        var info = new MapCliffInfo(0, [0m, 0m, 0.03125m, 0m, 0.03125m, -0.046875m, 0m, -0.0625m], true, false);
        var model = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, cliffIndices: cliffIndices, cliffInfos: [info]));
        var uv = model.Chunks[0].Base.Uv;
        Vector2 Px(float x, float y) => new(x / 2048f, y / 192f);
        Assert(uv[0] == Px(4, 132) && uv[1] == Px(68, 132) && uv[2] == Px(68, 36) && uv[3] == Px(4, 4), "cliff UVs = block corner + offset * 2048");
        Assert(model.Stats.CliffMappedCells == 1, "cliff-mapped cell counted");
        // The stored flip bit is not the triangle flip: getAlphaUVData recomputes it from the heights for stretched cells (flat here: no flip).
        Assert(model.Chunks[0].Base.Indices.Take(6).SequenceEqual(new ushort[] { 0, 1, 2, 0, 2, 3 }), "flat cliff-mapped cell keeps the 0-2 diagonal");
        var raw = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, cliffIndices: cliffIndices, cliffInfos: [info]), adjustCliffs: false);
        Assert(raw.Chunks[0].Base.Uv[3] == Px(4, 100), "AdjustCliffTextures = No keeps the quadrant UVs");
    }

    private static void Stretch()
    {
        // Cell (0, 0): corners h0 = h1 = h2 = 0, h3 = (0, 1) = 100: one high corner, 100 * 0.0625 = 6.25 >= 1.5, divisor clamps to 1.
        var model = Build(SyntheticTerrain.Map(3, 3, 0, (x, y) => (byte)(x == 0 && y == 1 ? 100 : 0)));
        var uv = model.Chunks[0].Base.Uv;
        Vector2 Px(float x, float y) => new(x / 2048f, y / 192f);
        Assert(uv[3] == Px(4, 4), $"V[3] = maxV - 128 = block top, got {uv[3] * new Vector2(2048, 192)}");
        Assert(uv[0] == Px(4, 132) && uv[2] == Px(36, 100), "other corners keep the quadrant");
        Assert(model.Stats.StretchedCells >= 1 && model.Stats.CliffCells == 2, $"stretch and cliff counts {model.Stats.StretchedCells}/{model.Stats.CliffCells}");
        // |p0 - p2| = 0 is not greater than |p1 - p3| = 100: the stretched cell keeps the 0-2 diagonal; cell (0,1) above has p0 = 100, p2 = 0.
        Assert(model.Chunks[0].Base.Indices.Take(6).SequenceEqual(new ushort[] { 0, 1, 2, 0, 2, 3 }), "stretched cell diagonal");
    }

    private static void UnknownTerrainType()
    {
        var map = SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, classes: [new MapTextureClass(0, 4, 2, "Undefined"), new MapTextureClass(4, 4, 2, SyntheticTerrain.RockClass)]);
        var model = Build(map);
        Assert(model.TextureIssues.Single() is { ClassName: "Undefined", Status: TerrainTextureStatus.UnknownTerrainType }, "unknown terrain type reported");
        Assert(model.Stats.MissingTextureCells == 4, "all cells use the missing class");
        var texel = new Vector2(0.5f / model.Atlas.Width, 0.5f / model.Atlas.Height);
        Assert(model.Chunks[0].Base.Uv.All(uv => uv == texel), "zero UVs as the reference (texel centre)");
        Assert(model.Regions.Single().ClassName == SyntheticTerrain.RockClass && model.Regions[0].X == 4, "only resolved classes are placed");
    }

    private static void Chunking()
    {
        var model = TerrainRenderModel.Build(SyntheticTerrain.Map(40, 35, 2, (x, y) => (byte)(x + y)), SyntheticTerrain.Lookup(),
            SyntheticTerrain.Options() with { ChunkCells = 16 });
        Assert(model.ChunksX == 3 && model.ChunksY == 3 && model.Chunks.Length == 9, "39x34 cells in 16-cell chunks");
        var last = model.ChunkAt(2, 2);
        Assert(last.FirstCellX == 32 && last.FirstCellY == 32 && last.CellsX == 7 && last.CellsY == 2, "edge chunk size");
        Assert(model.Chunks.Sum(chunk => chunk.Base.VertexCount) == 39 * 34 * 4, "every cell exactly once");
        Assert(last.Bounds.Min == new Vector3(300, 300, 64 * 0.625f) && last.Bounds.Max.X == 370, "edge chunk bounds in world units");
        Throws<ArgumentOutOfRangeException>(() => TerrainRenderModel.Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0), SyntheticTerrain.Lookup(),
            SyntheticTerrain.Options() with { ChunkCells = 33 }), "chunks above 32 cells would overflow 16-bit indices");
    }

    private static void HeightSamplerMatchesSimulation()
    {
        var map = SyntheticTerrain.Map(12, 10, 2, (x, y) => (byte)((x * 37 + y * 91 + x * y * 13) % 200));
        var terrain = TerrainMap.Build(map, "synthetic");
        var sampler = new TerrainHeightSampler(map.HeightField);
        Assert(sampler.PlayableCellsX == terrain.CellsX && sampler.PlayableCellsY == terrain.CellsY, "logical extent matches TerrainMap");
        var worst = 0f;
        for (var y = -25m; y <= 90m; y += 1.7m)
            for (var x = -25m; x <= 110m; x += 2.3m)
            {
                var expected = (float)terrain.HeightAt(Fixed.FromDecimal(x), Fixed.FromDecimal(y)).ToDecimal();
                worst = Math.Max(worst, Math.Abs(expected - sampler.HeightAt((float)x, (float)y)));
            }
        Assert(worst < 0.01f, $"deviation {worst}");
        Near(map.HeightField.HeightAt(3, 4) * 0.625, sampler.HeightAt(10, 20), 1e-4, "vertex heights are exact");
    }

    private static void SamplerNormals()
    {
        // Plane z = 0.625 * 2 * x per vertex: 1.25 per cell of 10 units = slope 0.125.
        var sampler = new TerrainHeightSampler(SyntheticTerrain.Map(6, 6, 1, (x, _) => (byte)(2 * x)).HeightField);
        var expected = Vector3.Normalize(new Vector3(-0.125f, 0, 1));
        Assert(Vector3.Distance(sampler.NormalAt(12, 7), expected) < Eps && Vector3.Distance(sampler.NormalAt(3, 18), expected) < Eps, "plane normal in both triangles");
        Assert(sampler.FieldMin == new Vector2(-10, -10) && sampler.FieldMax == new Vector2(40, 40) && sampler.PlayableMax == new Vector2(30, 30), "extents");
        Assert(sampler.IsInsideField(-10, 40) && !sampler.IsInsideField(-10.5f, 0), "inside test");
        Near(sampler.HeightAt(-500, 0), 0, 1e-5, "outside clamps to the edge");
    }

    private static void Water()
    {
        var square = new MapPolygonTrigger("Lake", "", 1, true, false, 0, [new(0, 0, 5), new(100, 0, 5), new(100, 100, 5), new(0, 100, 5)]);
        var river = new MapPolygonTrigger("River", "", 2, true, true, 1,
            [new(0, 0, 3), new(0, 20, 3), new(50, 20, 3), new(100, 20, 3), new(100, 0, 3), new(50, 0, 3)]);
        var model = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0, triggers: [square, river, square with { IsWater = false, Name = "Trigger" }]));
        Assert(model.Water.Length == 2, "two water polygons, the plain trigger is ignored");
        var lake = model.Water[0];
        Assert(lake.Positions.Length == 9 && lake.Indices.Length == 8 * 3, "100x100 lake: 3x3 vertices, 8 triangles");
        Assert(lake.Positions.All(position => position.Z == 5) && lake.Uv[2] == new Vector2(100 / 150f), "flat at the point height, UV = xy / 150");
        Assert(lake.Color == SyntheticTerrain.Water.SurfaceColor && lake.Texture == "water.tga" && lake.UvScrollPerSecond == Vector2.Zero, "lake look");
        for (var i = 0; i < lake.Indices.Length; i += 3)
        {
            var a = lake.Positions[lake.Indices[i]];
            var b = lake.Positions[lake.Indices[i + 1]];
            var c = lake.Positions[lake.Indices[i + 2]];
            Assert((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0, "counter-clockwise from above");
        }
        var flow = model.Water[1];
        // Inner bank starts at point 2 (RiverStart + 1) walking forward, outer bank at point 1 walking backward.
        Assert(flow.IsRiver && flow.Positions.Length == 6 && flow.Indices.Length == 12, "three bank pairs, two rectangles");
        Assert(flow.Positions[0] == new Vector3(50, 20, 3) && flow.Positions[1] == new Vector3(0, 20, 3), "first bank pair");
        Assert(flow.Uv[0] == new Vector2(0.5f, 0) && flow.Uv[1] == Vector2.Zero, "U across the river");
        // Lengths: 20+50+50+20+50 = 190; end length (segment 1) 50; (95 - 50) / 50 / 2 rectangles = 0.45 per step.
        Near(0.45, flow.Uv[2].Y, 1e-5, "V step along the river");
        Assert(Vector2.Distance(flow.UvScrollPerSecond, new Vector2(0, -0.06f)) < 1e-6f, "river flows with 0.002 per frame");
        Assert(model.Stats.WaterSurfaces == 2 && model.Stats.WaterTriangles == 12, "water stats");
    }

    private static void MipChain()
    {
        var atlas = Build(SyntheticTerrain.Map(3, 3, 0, (_, _) => 0)).Atlas;
        var levels = atlas.BuildMipChain();
        Assert(levels.Length == 12 && levels[1].Width == 1024 && levels[1].Height == 96 && levels[^1].Width == 1 && levels[^1].Height == 1, "2048x192 down to 1x1");
        Assert(levels.Sum(level => (long)level.Width * level.Height * 4) == atlas.BytesWithMips, "memory estimate matches the chain");
        var solid = new TerrainAtlas(2, 2, [10, 20, 30, 255, 30, 40, 50, 255, 50, 60, 70, 255, 70, 80, 90, 255]);
        Assert(solid.BuildMipChain()[1].Rgba.SequenceEqual(new byte[] { 40, 50, 60, 255 }), "box filter");
    }

    private static void Deterministic()
    {
        MapData Map() => SyntheticTerrain.Map(20, 18, 1, (x, y) => (byte)((x * 7 + y * 11) % 90));
        var first = Build(Map());
        var second = Build(Map());
        Assert(first.Atlas.Rgba.SequenceEqual(second.Atlas.Rgba), "atlas bytes");
        Assert(first.Chunks.Length == second.Chunks.Length, "chunk count");
        for (var i = 0; i < first.Chunks.Length; i++)
        {
            var a = first.Chunks[i].Base;
            var b = second.Chunks[i].Base;
            Assert(a.Positions.SequenceEqual(b.Positions) && a.Uv.SequenceEqual(b.Uv) && a.Colors.SequenceEqual(b.Colors) && a.Indices.SequenceEqual(b.Indices), $"chunk {i}");
        }
        Assert(first.Stats == second.Stats, "stats");
    }
}
