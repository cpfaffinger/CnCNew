using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Client.Stride.Layers.Terrain;

/// <summary>A <see cref="BridgeMesh"/> as GPU-ready vertices: the layout of the roads and the water (position, packed lit colour, texture coordinate, fog coordinate) and 32-bit indices.</summary>
internal static class BridgeVertices
{
    public static (WaterVertex[] Vertices, uint[] Indices) Build(BridgeMesh mesh, Vector2 fogScale, byte[] colorTable)
    {
        if (mesh.Uv.Length != mesh.Positions.Length || mesh.Colors.Length != mesh.Positions.Length || mesh.Indices.Length % 3 != 0)
            throw new InvalidDataException($"Bridge mesh '{mesh.Template}' has inconsistent vertex data.");
        var vertices = new WaterVertex[mesh.Positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var (position, color) = (mesh.Positions[i], mesh.Colors[i]);
            vertices[i] = new WaterVertex
            {
                Position = position,
                Color = PackedColor.Pack(colorTable[color.R], colorTable[color.G], colorTable[color.B], color.A),
                Uv = mesh.Uv[i],
                Fog = new Vector2(position.X * fogScale.X, position.Y * fogScale.Y),
            };
        }

        var indices = new uint[mesh.Indices.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            if ((uint)mesh.Indices[i] >= (uint)vertices.Length) throw new InvalidDataException($"Bridge mesh '{mesh.Template}' has index {mesh.Indices[i]} beyond {vertices.Length} vertices.");
            indices[i] = (uint)mesh.Indices[i];
        }

        return (vertices, indices);
    }
}
