using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dRenderPass(
    int MaterialPassIndex,
    W3dMaterialPass MaterialPass,
    uint? ShaderId,
    W3dShader? Shader,
    IReadOnlyList<uint?> TextureIds)
{
    public W3dDetailColorFunction EffectiveDetailColorFunction
        => Shader?.DetailColorFunction ?? W3dDetailColorFunction.Disabled;

    public W3dDetailAlphaFunction EffectiveDetailAlphaFunction
        => Shader?.DetailAlphaFunction ?? W3dDetailAlphaFunction.Disabled;
}

public sealed record W3dRenderBatch(
    IReadOnlyList<W3dRenderPass> Passes,
    IReadOnlyList<int> TriangleIndices);

public static class W3dRenderPlan
{
    public static IReadOnlyList<W3dRenderBatch> Build(W3dMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Materials.Passes.Count == 0)
        {
            return [new W3dRenderBatch([], Enumerable.Range(0, mesh.Triangles.Count).ToArray())];
        }

        // Group by the complete ordered pass state so all passes for a triangle remain one draw
        // unit and cannot be independently reordered by the presentation backend.
        return Enumerable.Range(0, mesh.Triangles.Count)
            .GroupBy(triangleIndex => CreateKey(mesh, triangleIndex), RenderBatchKeyComparer.Instance)
            .Select(group => new W3dRenderBatch(
                group.Key.Passes.Select(pass => new W3dRenderPass(
                    pass.MaterialPassIndex,
                    mesh.Materials.Passes[pass.MaterialPassIndex],
                    pass.ShaderId,
                    pass.ShaderId is uint shaderId ? mesh.Materials.Shaders[checked((int)shaderId)] : null,
                    pass.TextureIds)).ToArray(),
                group.ToArray()))
            .ToArray();
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

    public static W3dTextureMapper GetTextureMapper(
        W3dMesh mesh,
        W3dMaterialPass pass,
        int vertexIndex,
        int textureStageIndex)
    {
        if (textureStageIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(textureStageIndex));
        }

        if (pass.VertexMaterialIds.Count == 0)
        {
            return W3dTextureMapper.Uv;
        }

        var materialId = ResolveRequiredId(pass.VertexMaterialIds, vertexIndex);
        var material = mesh.Materials.VertexMaterials[checked((int)materialId)];
        return textureStageIndex == 0 ? material.Mapper0 : material.Mapper1;
    }

    public static W3dRgb GetSecondaryVertexColor(W3dMaterialPass? pass, int vertexIndex)
        => pass?.SpecularColors.Count > vertexIndex
            ? pass.SpecularColors[vertexIndex]
            : new W3dRgb(0, 0, 0);

    public static Vector2 GetTextureCoordinate(
        W3dTextureStage? stage,
        int triangleIndex,
        int corner,
        int vertexIndex,
        W3dTextureMapper? mapper = null,
        double totalSeconds = 0,
        Vector3? normal = null,
        Vector3? viewDirection = null)
    {
        var effectiveMapper = mapper ?? W3dTextureMapper.Uv;
        Vector2 source;
        if (effectiveMapper is W3dEnvironmentTextureMapper environment)
        {
            source = W3dTextureMapperEvaluator.EvaluateEnvironment(
                environment,
                normal ?? Vector3.UnitZ,
                viewDirection ?? Vector3.UnitZ);
        }
        else if (stage is null || stage.TextureCoordinates.Count == 0)
        {
            source = Vector2.Zero;
        }
        else
        {
            var coordinateIndex = vertexIndex;
            if (stage.PerFaceTextureCoordinateIndices.Count > 0)
            {
                if ((uint)triangleIndex >= stage.PerFaceTextureCoordinateIndices.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(triangleIndex));
                }

                var indices = stage.PerFaceTextureCoordinateIndices[triangleIndex];
                coordinateIndex = checked((int)(corner switch
                {
                    0 => indices.Index0,
                    1 => indices.Index1,
                    2 => indices.Index2,
                    _ => throw new ArgumentOutOfRangeException(nameof(corner)),
                }));
            }

            source = stage.TextureCoordinates[coordinateIndex];
        }

        if (!float.IsFinite(source.X) || !float.IsFinite(source.Y))
        {
            return Vector2.Zero;
        }

        var transformed = W3dTextureMapperEvaluator.Evaluate(effectiveMapper, totalSeconds).Apply(source);
        return new Vector2(transformed.X, 1 - transformed.Y);
    }

    private static RenderBatchKey CreateKey(W3dMesh mesh, int triangleIndex)
        => new(mesh.Materials.Passes.Select((pass, passIndex) => new RenderPassKey(
            passIndex,
            ResolveId(pass.ShaderIds, triangleIndex),
            pass.TextureStages.Select(stage => ResolveId(stage.TextureIds, triangleIndex)).ToArray())).ToArray());

    private static uint? ResolveId(IReadOnlyList<uint> ids, int elementIndex)
        => ids.Count switch
        {
            0 => null,
            1 => ids[0] == uint.MaxValue ? null : ids[0],
            _ => ids[elementIndex] == uint.MaxValue ? null : ids[elementIndex],
        };

    private static uint ResolveRequiredId(IReadOnlyList<uint> ids, int elementIndex)
        => ids.Count == 1 ? ids[0] : ids[elementIndex];

    private sealed record RenderPassKey(int MaterialPassIndex, uint? ShaderId, IReadOnlyList<uint?> TextureIds);

    private sealed record RenderBatchKey(IReadOnlyList<RenderPassKey> Passes);

    private sealed class RenderBatchKeyComparer : IEqualityComparer<RenderBatchKey>
    {
        public static RenderBatchKeyComparer Instance { get; } = new();

        public bool Equals(RenderBatchKey? left, RenderBatchKey? right)
            => ReferenceEquals(left, right) || left is not null && right is not null &&
                left.Passes.Count == right.Passes.Count &&
                left.Passes.Zip(right.Passes).All(pair =>
                    pair.First.MaterialPassIndex == pair.Second.MaterialPassIndex &&
                    pair.First.ShaderId == pair.Second.ShaderId &&
                    pair.First.TextureIds.SequenceEqual(pair.Second.TextureIds));

        public int GetHashCode(RenderBatchKey value)
        {
            var hash = new HashCode();
            foreach (var pass in value.Passes)
            {
                hash.Add(pass.MaterialPassIndex);
                hash.Add(pass.ShaderId);
                foreach (var textureId in pass.TextureIds)
                {
                    hash.Add(textureId);
                }
            }

            return hash.ToHashCode();
        }
    }
}
