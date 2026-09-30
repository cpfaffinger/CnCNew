using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Client.Stride.Layers.Terrain;

/// <summary>All water surfaces that share texture, scrolling and river-ness as one mesh (<see cref="BaseUv"/> plus <see cref="Scroll"/> times time).</summary>
internal sealed record WaterBatch(string Texture, bool IsRiver, Vector2 Scroll, WaterVertex[] Vertices, Vector2[] BaseUv, uint[] Indices, Vector3 BoundsMin, Vector3 BoundsMax)
{
    public bool Animated => Scroll != Vector2.Zero;

    /// <summary>Writes the scrolled texture coordinates for <paramref name="seconds"/> (wrapped per frame to keep float precision).</summary>
    public void ApplyScroll(double seconds)
    {
        var offset = new Vector2(Wrap(Scroll.X * seconds), Wrap(Scroll.Y * seconds));
        for (var i = 0; i < Vertices.Length; i++) Vertices[i].Uv = BaseUv[i] + offset;
    }

    private static float Wrap(double value) => (float)(value - Math.Floor(value));
}

internal static class WaterMeshBuilder
{
    public static List<WaterBatch> Build(IReadOnlyList<WaterSurface> surfaces, Vector2 fogScale, byte[] colorTable)
    {
        var groups = new List<(string Texture, bool IsRiver, Vector2 Scroll, List<WaterSurface> Members)>();
        foreach (var surface in surfaces)
        {
            var group = groups.FindIndex(g => g.IsRiver == surface.IsRiver && g.Scroll == surface.UvScrollPerSecond
                && g.Texture.Equals(surface.Texture, StringComparison.OrdinalIgnoreCase));
            if (group < 0)
            {
                groups.Add((surface.Texture, surface.IsRiver, surface.UvScrollPerSecond, []));
                group = groups.Count - 1;
            }
            groups[group].Members.Add(surface);
        }
        var batches = new List<WaterBatch>();
        foreach (var (texture, isRiver, scroll, members) in groups)
        {
            var vertexCount = members.Sum(surface => surface.Positions.Length);
            var indexCount = members.Sum(surface => surface.Indices.Length);
            var vertices = new WaterVertex[vertexCount];
            var baseUv = new Vector2[vertexCount];
            var indices = new uint[indexCount];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            int vertexOffset = 0, indexOffset = 0;
            foreach (var surface in members)
            {
                if (surface.Uv.Length != surface.Positions.Length || surface.Indices.Length % 3 != 0)
                    throw new InvalidDataException($"Water surface '{surface.Name}' has inconsistent vertex data.");
                var color = PackedColor.Pack(colorTable[surface.Color.R], colorTable[surface.Color.G], colorTable[surface.Color.B], surface.Color.A);
                for (var i = 0; i < surface.Positions.Length; i++)
                {
                    var position = surface.Positions[i];
                    vertices[vertexOffset + i] = new WaterVertex
                    {
                        Position = position,
                        Color = color,
                        Uv = surface.Uv[i],
                        Fog = new Vector2(position.X * fogScale.X, position.Y * fogScale.Y),
                    };
                    baseUv[vertexOffset + i] = surface.Uv[i];
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
                foreach (var index in surface.Indices)
                {
                    if ((uint)index >= (uint)surface.Positions.Length) throw new InvalidDataException($"Water surface '{surface.Name}' has index {index} beyond {surface.Positions.Length} vertices.");
                    indices[indexOffset++] = (uint)(vertexOffset + index);
                }
                vertexOffset += surface.Positions.Length;
            }
            if (vertexCount > 0) batches.Add(new WaterBatch(texture, isRiver, scroll, vertices, baseUv, indices, min, max));
        }
        return batches;
    }
}
