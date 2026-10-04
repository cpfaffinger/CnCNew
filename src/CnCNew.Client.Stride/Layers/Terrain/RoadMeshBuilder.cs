using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Client.Stride.Layers.Terrain;

/// <summary>One road template of a map as GPU-ready vertices: the water vertex layout (position, packed lit colour, texture coordinate, fog coordinate) and 32-bit indices.</summary>
internal sealed record RoadBatch(string Template, string Texture, int Stacking, WaterVertex[] Vertices, uint[] Indices, Vector3 BoundsMin, Vector3 BoundsMax);

internal static class RoadMeshBuilder
{
    /// <summary>Converts the meshes of a <see cref="RoadModel"/> to batches in draw order (stacking, then template order, like <c>drawRoads</c>); throws on inconsistent meshes.</summary>
    public static List<RoadBatch> Build(RoadModel model, Vector2 fogScale, byte[] colorTable)
    {
        var batches = new List<RoadBatch>();
        foreach (var mesh in model.Meshes.OrderBy(mesh => mesh.Stacking))
        {
            if (mesh.Uv.Length != mesh.Positions.Length || mesh.Colors.Length != mesh.Positions.Length || mesh.Indices.Length % 3 != 0)
                throw new InvalidDataException($"Road mesh '{mesh.Template}' has inconsistent vertex data.");
            var vertices = new WaterVertex[mesh.Positions.Length];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var i = 0; i < vertices.Length; i++)
            {
                var position = mesh.Positions[i];
                var color = mesh.Colors[i];
                vertices[i] = new WaterVertex
                {
                    Position = position,
                    Color = PackedColor.Pack(colorTable[color.R], colorTable[color.G], colorTable[color.B], color.A),
                    Uv = mesh.Uv[i],
                    Fog = new Vector2(position.X * fogScale.X, position.Y * fogScale.Y),
                };
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
            var indices = new uint[mesh.Indices.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                if ((uint)mesh.Indices[i] >= (uint)vertices.Length) throw new InvalidDataException($"Road mesh '{mesh.Template}' has index {mesh.Indices[i]} beyond {vertices.Length} vertices.");
                indices[i] = (uint)mesh.Indices[i];
            }
            batches.Add(new RoadBatch(mesh.Template, mesh.Texture, mesh.Stacking, vertices, indices, min, max));
        }
        return batches;
    }
}
