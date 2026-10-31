using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Shadows;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Tests;

/// <summary>The shadow geometry without a renderer: silhouettes and volumes worked out by hand on a cube, the light of a shadow, the place of a decal.</summary>
internal static class ShadowTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ShadowTests));
        harness.Run("A vertex repeated at a seam is one vertex, and a cube has twelve edges with two polygons each", Welding);
        harness.Run("A cube under a light from above: four edges, running as the lit polygon runs", SilhouetteOfACube);
        harness.Run("A cube under a slanted light: the six edges of the outline of its lit faces", SlantedSilhouette);
        harness.Run("An open polygon casts when it faces the light, and nothing when it faces away", OpenPolygon);
        harness.Run("Degenerate polygons are no geometry", Degenerate);
        harness.Run("The sides of a volume: down to the floor along the light, front side out", VolumeSides);
        harness.Run("A corner under the floor stays where it is", CornerBelowFloor);
        harness.Run("The sun: the first light of the objects, or straight down", SunDirection);
        harness.Run("ShadowSizeX of a volume is the lowest elevation of the sun", ElevationLimit);
        harness.Run("Extrusion: flat ground needs the buffer, a dip takes the floor down", ExtrusionOnTheFlat);
        harness.Run("Extrusion: at a cliff the light is made steeper", ExtrusionAtACliff);
        harness.Run("A decal turns with its object; offset X goes forward, offset Y to the right", DecalPlace);
        harness.Run("A decal without a texture is shadow.tga, with one its name and .tga", DecalTexture);
        harness.Run("The clouds slide with game time and a picture shifts by parts of a texel", CloudShift);
        return harness.Failures;
    }

    /// <summary>A unit cube from (0,0,0) to (1,1,1) with counter-clockwise faces seen from outside, every vertex three times over as a mesh with seams has it.</summary>
    private static ShadowCasterMesh Cube()
    {
        Vector3[] corners = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [2, 3, 7, 6], [1, 2, 6, 5], [3, 0, 4, 7]];
        var positions = new List<Vector3>();
        var triangles = new List<(int, int, int)>();
        foreach (var face in faces)
        {
            var start = positions.Count;
            foreach (var corner in face) positions.Add(corners[corner]);
            triangles.Add((start, start + 1, start + 2));
            triangles.Add((start, start + 2, start + 3));
        }

        return ShadowCasterMesh.Build(positions, triangles)!;
    }

    private static void Welding()
    {
        var cube = Cube();
        TestHarness.Assert(cube.Vertices.Length == 8, $"eight vertices, got {cube.Vertices.Length}");
        TestHarness.Assert(cube.PolygonCount == 12, $"twelve polygons, got {cube.PolygonCount}");
        // Six faces of two triangles have eighteen distinct edges: twelve of the cube and six diagonals.
        TestHarness.Assert(cube.EdgeCount == 18, $"eighteen edges, got {cube.EdgeCount}");
    }

    private static void SilhouetteOfACube()
    {
        var cube = Cube();
        var pairs = new int[cube.EdgeCount * 2];
        var edges = cube.Silhouette(-Vector3.UnitZ, pairs);
        TestHarness.Assert(edges == 4, $"the rim of the top face, got {edges} edges");
        for (var edge = 0; edge < edges; edge++)
        {
            var from = cube.Vertices[pairs[edge * 2]];
            var to = cube.Vertices[pairs[edge * 2 + 1]];
            TestHarness.Assert(from.Z == 1f && to.Z == 1f, "the rim lies at the top");
            // Counter-clockwise around the top face, seen from above: the outside lies to the right of the direction of the edge.
            var outward = Vector3.Cross(to - from, Vector3.UnitZ);
            var middle = (from + to) * 0.5f - new Vector3(0.5f, 0.5f, 1f);
            TestHarness.Assert(Vector3.Dot(outward, middle) > 0f, "the edge runs as the lit top face runs");
        }
    }

    private static void SlantedSilhouette()
    {
        var cube = Cube();
        var pairs = new int[cube.EdgeCount * 2];
        var edges = cube.Silhouette(Vector3.Normalize(new Vector3(-1, -1, -1)), pairs);
        TestHarness.Assert(edges == 6, $"six edges, got {edges}");
        // A closed outline: every vertex of it is left as often as it is reached.
        var balance = new int[8];
        for (var edge = 0; edge < edges; edge++)
        {
            balance[pairs[edge * 2]]++;
            balance[pairs[edge * 2 + 1]]--;
        }

        TestHarness.Assert(balance.All(value => value == 0), "the silhouette is a closed loop");
    }

    private static void OpenPolygon()
    {
        var plate = ShadowCasterMesh.Build([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [(0, 1, 2)])!;
        var pairs = new int[plate.EdgeCount * 2];
        TestHarness.Assert(plate.Silhouette(-Vector3.UnitZ, pairs) == 3, "a polygon that faces the light casts from its three open edges");
        TestHarness.Assert(plate.Silhouette(Vector3.UnitZ, pairs) == 0, "one that faces away casts nothing (buildSilhouette looks at visible polygons only)");
    }

    private static void Degenerate()
    {
        TestHarness.Assert(ShadowCasterMesh.Build([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)], [(0, 1, 2)]) is null, "a line has no normal");
        TestHarness.Assert(ShadowCasterMesh.Build([new(0, 0, 0), new(0, 0, 0), new(1, 1, 0)], [(0, 1, 2)]) is null, "two corners merged");
    }

    private static void VolumeSides()
    {
        var cube = Cube();
        var pairs = new int[cube.EdgeCount * 2];
        var edges = cube.Silhouette(-Vector3.UnitZ, pairs);
        var corners = new Vector3[edges * 4];
        var quads = ShadowVolumeWriter.Write(cube.Vertices, pairs, edges, Matrix4x4.CreateTranslation(10, 0, 5), -Vector3.UnitZ, 2f, corners);
        TestHarness.Assert(quads == 4, "one side per edge");
        for (var quad = 0; quad < quads; quad++)
        {
            var (from, to, lowTo, lowFrom) = (corners[quad * 4], corners[quad * 4 + 1], corners[quad * 4 + 2], corners[quad * 4 + 3]);
            TestHarness.Near(6f, from.Z, 1e-5, "the edge in the world");
            TestHarness.Near(2f, lowTo.Z, 1e-5, "down to the floor");
            TestHarness.Near(from.X, lowFrom.X, 1e-5, "straight down, as the light runs");
            // Seen from outside the corners run clockwise, the opposite of the normal of the first triangle: Direct3D's front face.
            var normal = Vector3.Cross(to - from, lowTo - from);
            var outward = (from + lowTo) * 0.5f - new Vector3(10.5f, 0.5f, 4f);
            TestHarness.Assert(Vector3.Dot(normal, outward) < 0f, "the side's winding is clockwise from outside");
        }
    }

    private static void CornerBelowFloor()
    {
        var cube = Cube();
        var pairs = new int[cube.EdgeCount * 2];
        var edges = cube.Silhouette(-Vector3.UnitZ, pairs);
        var corners = new Vector3[edges * 4];
        ShadowVolumeWriter.Write(cube.Vertices, pairs, edges, Matrix4x4.Identity, -Vector3.UnitZ, 5f, corners);
        TestHarness.Near(1f, corners[2].Z, 1e-5, "above the floor there is nothing to extrude");
    }

    private static void SunDirection()
    {
        var lighting = new TerrainLighting("AFTERNOON", [new TerrainLight(Vector3.One, Vector3.One, new Vector3(-0.5f, 0.5f, -1f))]);
        var sun = ShadowSun.Direction(lighting);
        TestHarness.Near(1f, sun.Length(), 1e-5, "unit length");
        TestHarness.Assert(sun.X < 0f && sun.Y > 0f && sun.Z < 0f, "the light travels along LightPos");
        TestHarness.Assert(ShadowSun.Direction(new TerrainLighting("AFTERNOON", [])) == -Vector3.UnitZ, "no light: straight down");
    }

    private static void ElevationLimit()
    {
        var low = Vector3.Normalize(new Vector3(1, 0, -0.5f));
        var limited = ShadowSun.Limited(low, ShadowSun.LengthScale(45f));
        TestHarness.Near(limited.X, -limited.Z, 1e-5, "45 degrees");
        TestHarness.Assert(ShadowSun.Limited(low, 0f) == low, "no limit");
        var steep = Vector3.Normalize(new Vector3(0.2f, 0, -1f));
        TestHarness.Assert(ShadowSun.Limited(steep, ShadowSun.LengthScale(45f)) == steep, "a sun above the limit stays");
    }

    private static TerrainHeightSampler Field(Func<int, int, byte> heights)
        => new(SyntheticTerrain.Map(64, 64, 2, heights).HeightField);

    private static void ExtrusionOnTheFlat()
    {
        var flat = Field((_, _) => 16);
        var height = flat.HeightAt(100f, 100f);
        var light = Vector3.Normalize(new Vector3(1, 0, -1));
        var position = new Vector3(100f, 100f, height);
        var (padding, scale) = ShadowSun.OptimalExtrusion(flat, position, position + new Vector3(-10, -10, 0), position + new Vector3(10, 10, 40), light, 0f);
        TestHarness.Near(ShadowSun.ExtrusionBuffer, padding, 1e-4, "only the buffer");
        TestHarness.Near(0f, scale, 1e-5, "no limit");

        var dip = Field((x, _) => (byte)(x > 13 && x < 18 ? 13 : 16));
        (padding, scale) = ShadowSun.OptimalExtrusion(dip, position, position + new Vector3(-10, -10, 0), position + new Vector3(10, 10, 40), light, 0f);
        TestHarness.Near(3f * TerrainHeightSampler.HeightScale + ShadowSun.ExtrusionBuffer, padding, 1e-3, "the floor follows the dip");
        TestHarness.Near(0f, scale, 1e-5, "still no limit: the dip is within the margin");
    }

    private static void ExtrusionAtACliff()
    {
        var cliff = Field((x, _) => (byte)(x > 15 ? 2 : 32));
        var height = cliff.HeightAt(100f, 100f);
        var light = Vector3.Normalize(new Vector3(1, 0, -1));
        var position = new Vector3(100f, 100f, height);
        var (_, scale) = ShadowSun.OptimalExtrusion(cliff, position, position + new Vector3(-10, -10, 0), position + new Vector3(10, 10, 40), light, 0f);
        TestHarness.Assert(scale > 1f, $"the sun has to stand above 45 degrees for the shadow to end before the cliff, got {scale}");
    }

    private static void DecalPlace()
    {
        var north = ShadowDecal.Under("a.tga", new Vector3(100, 200, 7), MathF.PI / 2f, 14, 20, 3, 2);
        TestHarness.Near(0f, north.U.X, 1e-5, "u follows the heading");
        TestHarness.Near(1f, north.U.Y, 1e-5, "u follows the heading");
        TestHarness.Near(-1f, north.V.X, 1e-5, "v is the object's left");
        // Facing north, forward is +Y and the right is +X.
        TestHarness.Near(102f, north.Center.X, 1e-4, "offset Y to the right");
        TestHarness.Near(203f, north.Center.Y, 1e-4, "offset X forward");
        var corner = north.At(1f, 0.5f);
        TestHarness.Near(203f + 7f, corner.Y, 1e-4, "half the width along u");
    }

    private static void CloudShift()
    {
        var (u, v) = TerrainClouds.Offset(10.0);
        TestHarness.Near(-0.2f, u, 1e-5, "0.02 a second");
        TestHarness.Near(-0.3f, v, 1e-5, "and half as much again the other way");
        TestHarness.Near(0f, TerrainClouds.Offset(100.0).U, 1e-4, "a whole turn is nothing");
        TestHarness.Near(1f / 315f, TerrainClouds.Stretch, 1e-7, "31.5 cells");

        // Two texels, black and white: half a texel is grey, one texel swaps them.
        byte[] source = [0, 0, 0, 255, 200, 200, 200, 255];
        var target = new byte[8];
        TerrainClouds.Shift(source, 2, 1, 0.5f, 0f, target);
        TestHarness.Assert(target[0] == 200 && target[4] == 0, "one texel: the target shows the source one to the right");
        TerrainClouds.Shift(source, 2, 1, 0.25f, 0f, target);
        TestHarness.Near(100, target[0], 1, "half a texel: the mean");
        TestHarness.Near(255, target[3], 1, "alpha is shifted like the rest");
        TerrainClouds.Shift(source, 2, 1, -0.5f, 0f, target);
        TestHarness.Assert(target[0] == 200, "a negative offset wraps");
    }

    private static void DecalTexture()
    {
        TestHarness.Assert(new ShadowSpec(false, true, null, 0, 0, 0, 0).DecalTexture == "shadow.tga", "the default");
        TestHarness.Assert(new ShadowSpec(false, true, "ShadowI", 0, 0, 0, 0).DecalTexture == "ShadowI.tga", "the name");
        TestHarness.Assert(ShadowSpec.From(["SHADOW_VOLUME"], null, 0, 0, 0, 0).Volume, "a volume");
        TestHarness.Assert(ShadowSpec.From([], null, 0, 0, 0, 0) == ShadowSpec.None, "none");
    }
}
