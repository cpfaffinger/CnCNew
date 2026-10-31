using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// One sub-object of a bridge model (<c>BRIDGE_LEFT</c>, <c>BRIDGE_SPAN</c>, <c>BRIDGE_RIGHT</c>), at its rest pose in the model's space: X along the bridge, Y across it, Z up
/// (the positions and normals are those <c>getModelVertices</c> gets from <c>Matrix3D::Transform_Vector</c> and <c>Rotate_Vector</c>).
/// </summary>
public sealed record BridgeArtSection(ImmutableArray<Vector3> Positions, ImmutableArray<Vector3> Normals, ImmutableArray<Vector2> Uv, ImmutableArray<int> Indices)
{
    public float MinX => Positions.Length == 0 ? 0f : Positions.Min(position => position.X);
    public float MaxX => Positions.Length == 0 ? 0f : Positions.Max(position => position.X);
}

/// <summary>What <c>W3DBridge::load</c> reads for one damage state. Without a span the bridge is one piece; the left section is the only one a model must have.</summary>
public sealed record BridgeArt(string Texture, float Scale, BridgeArtSection Left, BridgeArtSection? Span, BridgeArtSection? Right);

/// <summary>
/// One terrain bridge in one damage state, in world space, in the order the original writes its buffers: left end, spans, right end. <see cref="Colors"/> is the static terrain
/// lighting of the vertex (alpha 255), and the texture's alpha decides what is drawn (<c>detailAlphaShader</c>: alpha test, depth write, no culling).
/// </summary>
public sealed record BridgeMesh(
    string Template,
    int DamageState,
    string Texture,
    int Spans,
    bool Sectional,
    ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector2> Uv,
    ImmutableArray<Rgba32> Colors,
    ImmutableArray<int> Indices,
    Aabb Bounds)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>
/// <c>W3DBridge</c> as triangles (<c>docs/CLIENT-ROADS.md</c>): the model's left end at the 'from' point, as many spans as fit, the right end at the 'to' point, the whole stretched along the
/// bridge to reach it exactly and scaled across it by the bridge's scale. Pure and deterministic; a landmark bridge is an object and is drawn as one.
/// </summary>
public static class BridgeMeshBuilder
{
    /// <summary>The first <c>load</c> and <c>getIndicesNVertices</c>: <paramref name="start"/> and <paramref name="end"/> are the deck's two points.</summary>
    public static BridgeMesh Build(string template, int damageState, BridgeArt art, Vector3 start, Vector3 end, TerrainLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(lighting);
        var (left, span, right) = (art.Left, art.Span, art.Right);
        var (leftMin, leftMax) = (left.MinX, left.MaxX);
        var sectional = span is not null && right is not null;
        var (spanMin, spanMax) = sectional ? (span!.MinX, span.MaxX) : (leftMax, leftMax);
        var (rightMin, rightMax) = sectional ? (right!.MinX, right.MaxX) : (leftMax, leftMax);
        var length = MathF.Max(rightMax - leftMin, 1f);
        if (sectional)
        {
            // "make sure the sections align."
            var allowableError = 0.05f * length;
            if (leftMax > spanMin + allowableError || rightMin < spanMax - allowableError) sectional = false;
        }

        var vec = end - start;
        if (vec.LengthSquared() < 1f) vec = Vector3.Normalize(vec);
        var vecNormal = Vector3.Normalize(new Vector3(-vec.Y, vec.X, 0f)) * art.Scale;
        var desiredLength = vec.Length();
        // "Rotate along the y axis to get the appropriate Z height adjustment."
        var deltaZ = (end.Z - start.Z) / desiredLength;
        var vecZ = new Vector3(-deltaZ, 0f, MathF.Sqrt(1f - deltaZ * deltaZ)) * art.Scale;
        var xOffset = -leftMin;
        var writer = new Writer(start, vecNormal, vecZ, lighting);

        var spans = 0;
        if (span is null)
        {
            writer.Vec = vec / length;
            writer.Add(left, xOffset);
        }
        else
        {
            var spanLength = rightMin - leftMax;
            spans = 1;
            if (sectional)
            {
                var spannable = desiredLength - (length - spanLength);
                spans = Math.Max((int)MathF.Floor((spannable + spanLength / 2f) / spanLength), 0);
            }

            writer.Vec = vec / (length + (spans - 1) * spanLength);
            writer.Add(left, xOffset);
            for (var index = 0; index < spans; index++) writer.Add(span, xOffset + index * spanLength);
            if (right is not null) writer.Add(right, xOffset + (spans - 1) * spanLength);
        }

        return new BridgeMesh(template, damageState, art.Texture, spans, sectional, writer.Positions.ToImmutableArray(), writer.Uv.ToImmutableArray(), writer.Colors.ToImmutableArray(), writer.Indices.ToImmutableArray(), writer.Bounds());
    }

    /// <summary><c>W3DBridgeBuffer::drawBridges</c>: the state a bridge shows: its own, or - "put the old model back" - the one it showed when its own has no model.</summary>
    public static int ShownState(int damageState, int shown, IReadOnlyList<BridgeMesh?> states)
        => damageState >= 0 && damageState < states.Count && states[damageState] is not null ? damageState : shown;

    private sealed class Writer(Vector3 start, Vector3 vecNormal, Vector3 vecZ, TerrainLighting lighting)
    {
        public Vector3 Vec { get; set; }
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> Uv { get; } = [];
        public List<Rgba32> Colors { get; } = [];
        public List<int> Indices { get; } = [];

        /// <summary><c>getModelVertices</c> and <c>getModelIndices</c>.</summary>
        public void Add(BridgeArtSection section, float xOffset)
        {
            var first = Positions.Count;
            for (var index = 0; index < section.Positions.Length; index++)
            {
                var vertex = section.Positions[index];
                Positions.Add((vertex.X + xOffset) * Vec + vertex.Y * vecNormal + vertex.Z * vecZ + start);
                var normal = section.Normals[index];
                Colors.Add(Rgba32.FromUnit(lighting.Shade(Vector3.Normalize(normal.X * Vec + normal.Y * vecNormal + normal.Z * vecZ)), 255));
                Uv.Add(section.Uv[index]);
            }

            foreach (var index in section.Indices) Indices.Add(first + index);
        }

        public Aabb Bounds()
        {
            var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            foreach (var position in Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            return new Aabb(min, max);
        }
    }
}
