using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using CnCNew.Presentation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary><c>W3DBridge::load</c> and <c>getIndicesNVertices</c>: the sections of a model laid along a bridge. The art is a synthetic box per section; every number here is worked out by hand.</summary>
internal static class BridgeMeshTests
{
    private static readonly TerrainLighting Light = new("TEST", [new TerrainLight(new Vector3(0.25f), new Vector3(0.75f), new Vector3(0f, 0f, -1f))]);

    public static int Run(string[] args)
    {
        var t = new TestHarness("BridgeMesh");
        t.Run("left end, spans and right end reach from point to point", Sectional);
        t.Run("as many spans as fit, rounded to the nearest", SpanCount);
        t.Run("a sloping bridge tilts its height, and the scale widens it", Slope);
        t.Run("a model of one piece, or with sections that do not meet, is stretched whole", FixedBridges);
        t.Run("texture coordinates and the light of the normals", UvAndLight);
        t.Run("the bytes are the same on every run", Bytes);
        t.Run("a state without art keeps the state before", ShownState);
        return t.Failures;
    }

    /// <summary>A box from x0 to x1, 40 wide, 2 high; its top two vertices look up, the bottom ones down.</summary>
    private static BridgeArtSection Box(float x0, float x1) => new(
        [new Vector3(x0, -20f, 2f), new Vector3(x1, -20f, 2f), new Vector3(x1, 20f, 2f), new Vector3(x0, 20f, 2f), new Vector3(x0, -20f, 0f), new Vector3(x1, 20f, 0f)],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ],
        [new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 1f)],
        [0, 1, 2, 0, 2, 3, 4, 5, 1]);

    /// <summary>Ends of 10 and a span of 50: a model of 70.</summary>
    private static BridgeArt Art(float scale = 1f) => new("Sample.tga", scale, Box(0f, 10f), Box(10f, 60f), Box(60f, 70f));

    private static void Sectional()
    {
        // 170 long: 10 + 3 x 50 + 10 fits exactly.
        var mesh = BridgeMeshBuilder.Build("X", 0, Art(), new Vector3(100f, 200f, 5f), new Vector3(270f, 200f, 5f), Light);
        Assert(mesh.Sectional && mesh.Spans == 3, $"Three spans ({mesh.Spans}).");
        Assert(mesh.Positions.Length == 6 * 5 && mesh.Indices.Length == 9 * 5, "Five sections of six vertices and three triangles.");
        Near(100f, mesh.Bounds.Min.X, 0.001, "The left end begins at the 'from' point");
        Near(270f, mesh.Bounds.Max.X, 0.001, "The right end ends at the 'to' point");
        Near(180f, mesh.Bounds.Min.Y, 0.001, "Half the width to one side");
        Near(220f, mesh.Bounds.Max.Y, 0.001, "And to the other");
        Near(5f, mesh.Bounds.Min.Z, 0.001, "The model's zero is the deck's point");
        Near(7f, mesh.Bounds.Max.Z, 0.001, "And its height stands over it");
        Assert(mesh.Indices.Max() == mesh.Positions.Length - 1 && mesh.Indices[9] == 6 && mesh.Indices[^1] == 25, "Each section's indices follow its first vertex.");
        // The second span lies 50 further along than the first.
        Near(mesh.Positions[12].X + 50f, mesh.Positions[18].X, 0.001, "The spans follow each other");
    }

    private static void SpanCount()
    {
        static int Spans(float length) => BridgeMeshBuilder.Build("X", 0, Art(), Vector3.Zero, new Vector3(length, 0f, 0f), Light).Spans;
        // floor((length - 20 + 25) / 50): the bridge is stretched to the nearest whole number of spans.
        Assert(Spans(170f) == 3 && Spans(144f) == 2 && Spans(146f) == 3 && Spans(194f) == 3 && Spans(196f) == 4, "Rounded to the nearest whole span.");
        Assert(Spans(30f) == 0 && Spans(10f) == 0, "A short bridge is its two ends.");
        var stretched = BridgeMeshBuilder.Build("X", 0, Art(), Vector3.Zero, new Vector3(150f, 0f, 0f), Light);
        Near(150f, stretched.Bounds.Max.X, 0.001, "Whatever the length, it reaches its end");
        Assert(stretched.Spans == 3 && Math.Abs(stretched.Positions[7].X - 150f * 60f / 170f) < 0.001f, "The sections are squeezed along it, and only along it.");
    }

    private static void Slope()
    {
        // 3-4-5: rising 30 over a run of 40.
        var mesh = BridgeMeshBuilder.Build("X", 0, Art(2f), new Vector3(0f, 0f, 10f), new Vector3(40f, 0f, 40f), Light);
        var top = mesh.Positions[0];
        // vecZ = (-0.6, 0, 0.8) x 2, and the height of 2 stands on that.
        Near(10f + 2f * 0.8f * 2f, top.Z, 0.001, "The height stands square on the slope");
        Near(-2f * 0.6f * 2f, top.X, 0.001, "And leans back");
        Near(-20f * 2f, top.Y, 0.001, "The scale widens the bridge");
        Near(40f, mesh.Positions[^1].Z, 0.001, "The right end's bottom lies on the 'to' height");
    }

    private static void FixedBridges()
    {
        var whole = BridgeMeshBuilder.Build("X", 0, new BridgeArt("S.tga", 1f, Box(0f, 50f), null, null), new Vector3(0f, 0f, 0f), new Vector3(200f, 0f, 0f), Light);
        Assert(!whole.Sectional && whole.Spans == 0 && whole.Positions.Length == 6, "One piece.");
        Near(200f, whole.Bounds.Max.X, 0.001, "Stretched over the whole bridge");

        // A span that overlaps the left end by more than 5 % of the model makes the bridge FIXED_BRIDGE: one span, stretched.
        var gapped = BridgeMeshBuilder.Build("X", 0, new BridgeArt("S.tga", 1f, Box(0f, 30f), Box(10f, 60f), Box(60f, 70f)), Vector3.Zero, new Vector3(500f, 0f, 0f), Light);
        Assert(!gapped.Sectional && gapped.Spans == 1 && gapped.Positions.Length == 18, $"One span however long ({gapped.Spans}).");
        Near(500f, gapped.Bounds.Max.X, 0.001, "And it still reaches its end");
    }

    private static void UvAndLight()
    {
        var mesh = BridgeMeshBuilder.Build("X", 0, Art(), Vector3.Zero, new Vector3(170f, 0f, 0f), Light);
        Assert(mesh.Uv[1] == new Vector2(1f, 0f) && mesh.Uv[8] == new Vector2(1f, 1f), "The coordinates of each section are its own.");
        Assert(mesh.Colors[0] == new Rgba32(255, 255, 255, 255), $"A face up gets ambient and diffuse ({mesh.Colors[0]}).");
        Assert(mesh.Colors[4] == new Rgba32(64, 64, 64, 255), $"A face down only the ambient ({mesh.Colors[4]}).");
    }

    private static void Bytes()
    {
        static string Hash()
        {
            var mesh = BridgeMeshBuilder.Build("X", 0, Art(0.75f), new Vector3(123.5f, 45.25f, 7f), new Vector3(456.75f, 300.5f, 21f), Light);
            using var stream = new MemoryStream();
            var number = new byte[4];
            void Single(float value)
            {
                BinaryPrimitives.WriteSingleLittleEndian(number, value);
                stream.Write(number);
            }

            for (var index = 0; index < mesh.Positions.Length; index++)
            {
                var (position, uv, color) = (mesh.Positions[index], mesh.Uv[index], mesh.Colors[index]);
                Single(position.X);
                Single(position.Y);
                Single(position.Z);
                Single(uv.X);
                Single(uv.Y);
                stream.WriteByte(color.R);
                stream.WriteByte(color.G);
                stream.WriteByte(color.B);
                stream.WriteByte(color.A);
            }

            foreach (var index in mesh.Indices)
            {
                BinaryPrimitives.WriteInt32LittleEndian(number, index);
                stream.Write(number);
            }

            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        Assert(Hash() == Hash(), "Two builds, one set of bytes.");
    }

    private static void ShownState()
    {
        var mesh = BridgeMeshBuilder.Build("X", 0, Art(), Vector3.Zero, new Vector3(170f, 0f, 0f), Light);
        var states = new BridgeMesh?[] { mesh, null, mesh, null };
        Assert(BridgeMeshBuilder.ShownState(2, 0, states) == 2, "A state with art is shown.");
        Assert(BridgeMeshBuilder.ShownState(1, 2, states) == 2 && BridgeMeshBuilder.ShownState(3, 0, states) == 0, "One without keeps what was there.");
    }
}
