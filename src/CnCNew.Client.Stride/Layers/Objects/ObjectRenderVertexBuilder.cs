using System.Numerics;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Color = Stride.Core.Mathematics.Color;
using Vector2 = Stride.Core.Mathematics.Vector2;
using Vector3 = Stride.Core.Mathematics.Vector3;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Builds <see cref="W3dRenderVertex"/> arrays for one render batch of one mesh, for the object layer. Unlike
/// <see cref="W3dStrideScene"/> (a self-contained single-model preview with its own framing camera), positions and normals are copied
/// straight from the W3D source with no centering, scale or axis swap: a rigid part's vertices are already in the space of the bone it is
/// attached to (see <see cref="W3dModel.SkinVertices"/> remarks), and the object layer places that space in the world by writing the
/// Stride skeleton node's world matrix directly (see <see cref="ModelAsset"/>). Environment-mapped stages use one fixed light-ish view
/// direction instead of a per-instance one, because the geometry is shared by every instance of the asset; this is a documented
/// simplification (real per-pixel environment mapping is out of scope for the object layer).
/// </summary>
internal static class ObjectRenderVertexBuilder
{
    private static readonly System.Numerics.Vector3 FixedViewDirection =
        System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0, -0.6f, 0.8f));

    /// <summary><paramref name="sourceIndices"/> is the source mesh-vertex index of each output vertex, for CPU re-skinning.</summary>
    public readonly record struct BuiltBatch(W3dRenderVertex[] Vertices, int[] SourceIndices);

    public static BuiltBatch Build(W3dMesh mesh, W3dRenderBatch batch, double totalSeconds = 0)
    {
        var vertices = new W3dRenderVertex[batch.TriangleIndices.Count * 3];
        var sourceIndices = new int[vertices.Length];
        var cursor = 0;
        foreach (var triangleIndex in batch.TriangleIndices)
        {
            var triangle = mesh.Triangles[triangleIndex];
            AddVertex(vertices, sourceIndices, ref cursor, mesh, batch, triangleIndex, 0, triangle.Vertex0, totalSeconds);
            AddVertex(vertices, sourceIndices, ref cursor, mesh, batch, triangleIndex, 1, triangle.Vertex1, totalSeconds);
            AddVertex(vertices, sourceIndices, ref cursor, mesh, batch, triangleIndex, 2, triangle.Vertex2, totalSeconds);
        }

        return new BuiltBatch(vertices, sourceIndices);
    }

    public static bool HasAnimatedMapper(W3dMesh mesh, W3dRenderBatch batch)
    {
        foreach (var pass in batch.Passes)
        {
            foreach (var triangleIndex in batch.TriangleIndices)
            {
                var triangle = mesh.Triangles[triangleIndex];
                foreach (var vertexIndex in new[] { triangle.Vertex0, triangle.Vertex1, triangle.Vertex2 })
                {
                    for (var stageIndex = 0; stageIndex < pass.MaterialPass.TextureStages.Count; stageIndex++)
                    {
                        if (W3dRenderPlan.GetTextureMapper(mesh, pass.MaterialPass, checked((int)vertexIndex), stageIndex)
                            is W3dLinearOffsetTextureMapper or W3dGridTextureMapper)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static void AddVertex(
        W3dRenderVertex[] output,
        int[] sourceIndices,
        ref int cursor,
        W3dMesh mesh,
        W3dRenderBatch batch,
        int triangleIndex,
        int corner,
        uint vertexIndex,
        double totalSeconds)
    {
        var index = checked((int)vertexIndex);
        var position = mesh.Vertices[index];
        var normal = mesh.Normals.Count == mesh.Vertices.Count ? mesh.Normals[index] : System.Numerics.Vector3.UnitZ;
        var colors = new Color[4];
        var coordinates = new Vector2[4];
        Array.Fill(colors, Color.White);
        for (var passIndex = 0; passIndex < batch.Passes.Count; passIndex++)
        {
            var pass = batch.Passes[passIndex].MaterialPass;
            var primary = W3dRenderPlan.GetVertexColor(mesh, pass, index);
            var secondary = W3dRenderPlan.GetSecondaryVertexColor(pass, index);
            colors[passIndex * 2] = new Color(primary.Red, primary.Green, primary.Blue, primary.Alpha);
            colors[passIndex * 2 + 1] = new Color(secondary.Red, secondary.Green, secondary.Blue, (byte)255);
            for (var stageIndex = 0; stageIndex < pass.TextureStages.Count; stageIndex++)
            {
                var mapper = W3dRenderPlan.GetTextureMapper(mesh, pass, index, stageIndex);
                var uv = W3dRenderPlan.GetTextureCoordinate(
                    pass.TextureStages[stageIndex],
                    triangleIndex,
                    corner,
                    index,
                    mapper,
                    totalSeconds,
                    normal,
                    FixedViewDirection);
                coordinates[passIndex * 2 + stageIndex] = new Vector2(uv.X, uv.Y);
            }
        }

        output[cursor] = new W3dRenderVertex(
            new Vector3(position.X, position.Y, position.Z),
            new Vector3(normal.X, normal.Y, normal.Z),
            colors[0], colors[1], colors[2], colors[3],
            coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
        sourceIndices[cursor] = index;
        cursor++;
    }
}
