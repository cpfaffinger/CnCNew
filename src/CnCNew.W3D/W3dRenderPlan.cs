using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dRenderBatch(
    int MaterialPassIndex,
    W3dMaterialPass? MaterialPass,
    uint? ShaderId,
    W3dShader? Shader,
    IReadOnlyList<uint?> TextureIds,
    IReadOnlyList<int> TriangleIndices)
{
    public byte DetailColorFunction => Shader?.DetailColorFunction ?? 0;

    public byte DetailAlphaFunction => Shader?.DetailAlphaFunction ?? 0;
}

public static class W3dRenderPlan
{
    public static IReadOnlyList<W3dRenderBatch> Build(W3dMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (mesh.Materials.Passes.Count == 0)
        {
            return
            [
                new W3dRenderBatch(
                    -1,
                    null,
                    null,
                    null,
                    [],
                    Enumerable.Range(0, mesh.Triangles.Count).ToArray()),
            ];
        }

        var batches = new List<W3dRenderBatch>();
        for (var passIndex = 0; passIndex < mesh.Materials.Passes.Count; passIndex++)
        {
            var pass = mesh.Materials.Passes[passIndex];
            var groups = Enumerable.Range(0, mesh.Triangles.Count)
                .GroupBy(triangleIndex => new BatchKey(
                    ResolveId(pass.ShaderIds, triangleIndex),
                    pass.TextureStages.Select(stage => ResolveId(stage.TextureIds, triangleIndex)).ToArray()),
                    BatchKeyComparer.Instance);

            foreach (var group in groups)
            {
                var shader = group.Key.ShaderId is uint shaderId
                    ? mesh.Materials.Shaders[checked((int)shaderId)]
                    : null;
                batches.Add(new W3dRenderBatch(
                    passIndex,
                    pass,
                    group.Key.ShaderId,
                    shader,
                    group.Key.TextureIds,
                    group.ToArray()));
            }
        }

        return batches;
    }

    public static W3dRgba GetVertexColor(W3dMesh mesh, W3dMaterialPass? pass, int vertexIndex)
    {
        var red = 255;
        var green = 255;
        var blue = 255;
        var alpha = 255;
        if (pass?.DiffuseColors.Count == mesh.Vertices.Count)
        {
            var color = pass.DiffuseColors[vertexIndex];
            red = color.Red;
            green = color.Green;
            blue = color.Blue;
            alpha = color.Alpha;
        }

        if (pass is not null && pass.VertexMaterialIds.Count > 0)
        {
            var materialId = ResolveRequiredId(pass.VertexMaterialIds, vertexIndex);
            var material = mesh.Materials.VertexMaterials[checked((int)materialId)];
            red = red * material.Diffuse.Red / 255;
            green = green * material.Diffuse.Green / 255;
            blue = blue * material.Diffuse.Blue / 255;
            alpha = (int)Math.Clamp(alpha * material.Opacity, 0, 255);
        }

        return new W3dRgba((byte)red, (byte)green, (byte)blue, (byte)alpha);
    }

    public static Vector2 GetTextureCoordinate(
        W3dTextureStage? stage,
        int triangleIndex,
        int corner,
        int vertexIndex)
    {
        if (stage is null || stage.TextureCoordinates.Count == 0)
        {
            return Vector2.Zero;
        }

        var coordinateIndex = vertexIndex;
        if (stage.PerFaceTextureCoordinateIndices.Count > 0)
        {
            var indices = stage.PerFaceTextureCoordinateIndices[triangleIndex];
            coordinateIndex = checked((int)(corner switch
            {
                0 => indices.Index0,
                1 => indices.Index1,
                2 => indices.Index2,
                _ => throw new ArgumentOutOfRangeException(nameof(corner)),
            }));
        }

        var source = stage.TextureCoordinates[coordinateIndex];
        return float.IsFinite(source.X) && float.IsFinite(source.Y)
            ? new Vector2(source.X, 1 - source.Y)
            : Vector2.Zero;
    }

    private static uint? ResolveId(IReadOnlyList<uint> ids, int elementIndex)
        => ids.Count switch
        {
            0 => null,
            1 => ids[0],
            _ => ids[elementIndex],
        };

    private static uint ResolveRequiredId(IReadOnlyList<uint> ids, int elementIndex)
        => ids.Count == 1 ? ids[0] : ids[elementIndex];

    private sealed record BatchKey(uint? ShaderId, IReadOnlyList<uint?> TextureIds);

    private sealed class BatchKeyComparer : IEqualityComparer<BatchKey>
    {
        public static BatchKeyComparer Instance { get; } = new();

        public bool Equals(BatchKey? left, BatchKey? right)
            => ReferenceEquals(left, right) ||
               left is not null && right is not null &&
               left.ShaderId == right.ShaderId &&
               left.TextureIds.SequenceEqual(right.TextureIds);

        public int GetHashCode(BatchKey value)
        {
            var hash = new HashCode();
            hash.Add(value.ShaderId);
            foreach (var textureId in value.TextureIds)
            {
                hash.Add(textureId);
            }

            return hash.ToHashCode();
        }
    }
}
