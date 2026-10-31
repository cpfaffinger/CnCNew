using System.Numerics;

namespace CnCNew.Presentation.Shadows;

/// <summary>
/// The shadow geometry of one mesh (<c>W3DShadowGeometryMesh</c>): duplicate vertices merged (a W3D mesh repeats a vertex per texture seam, <c>initFromHLOD</c>), a normal per polygon and
/// the edges with the polygons on both sides (<c>buildPolygonNeighbors</c>). A silhouette is what <c>W3DVolumetricShadow::buildSilhouette</c> finds: the edges between a polygon that
/// faces the light and one that does not, and the open edges of a polygon that faces it.
/// </summary>
public sealed class ShadowCasterMesh
{
    private readonly Vector3[] _normals;
    private readonly int[] _edgeVertices;
    private readonly int[] _edgeFaces;
    private readonly bool[] _lit;

    private ShadowCasterMesh(Vector3[] vertices, Vector3[] normals, int[] edgeVertices, int[] edgeFaces)
    {
        Vertices = vertices;
        _normals = normals;
        _edgeVertices = edgeVertices;
        _edgeFaces = edgeFaces;
        _lit = new bool[normals.Length];
    }

    /// <summary>The merged vertices in the mesh's own space.</summary>
    public Vector3[] Vertices { get; }

    public int EdgeCount => _edgeFaces.Length / 2;

    public int PolygonCount => _normals.Length;

    /// <summary>
    /// Builds the geometry from a mesh's vertices and its counter-clockwise triangles. Null when nothing remains: a mesh of only degenerate polygons casts no shadow. Two polygons share
    /// an edge when they run along it in opposite directions; a third that meets the same edge counts as open, like a polygon without a neighbour in <c>buildPolygonNeighbors</c>.
    /// </summary>
    public static ShadowCasterMesh? Build(IReadOnlyList<Vector3> positions, IReadOnlyList<(int A, int B, int C)> triangles)
    {
        var merged = new Dictionary<Vector3, int>();
        var parents = new int[positions.Count];
        var vertices = new List<Vector3>(positions.Count);
        for (var index = 0; index < positions.Count; index++)
        {
            if (!merged.TryGetValue(positions[index], out var parent))
            {
                parent = vertices.Count;
                merged.Add(positions[index], parent);
                vertices.Add(positions[index]);
            }

            parents[index] = parent;
        }

        var normals = new List<Vector3>(triangles.Count);
        var open = new Dictionary<(int From, int To), int>();
        var edgeVertices = new List<int>(triangles.Count * 3);
        var edgeFaces = new List<int>(triangles.Count * 3);
        foreach (var (a, b, c) in triangles)
        {
            var (first, second, third) = (parents[a], parents[b], parents[c]);
            if (first == second || second == third || third == first) continue;
            var normal = Vector3.Cross(vertices[second] - vertices[first], vertices[third] - vertices[first]);
            if (normal.LengthSquared() == 0f) continue;
            var face = normals.Count;
            normals.Add(Vector3.Normalize(normal));
            AddEdge(first, second, face);
            AddEdge(second, third, face);
            AddEdge(third, first, face);
        }

        foreach (var ((from, to), face) in open.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key.From).ThenBy(pair => pair.Key.To))
        {
            edgeVertices.Add(from);
            edgeVertices.Add(to);
            edgeFaces.Add(face);
            edgeFaces.Add(-1);
        }

        return normals.Count == 0 ? null : new ShadowCasterMesh([.. vertices], [.. normals], [.. edgeVertices], [.. edgeFaces]);

        void AddEdge(int from, int to, int face)
        {
            if (open.Remove((to, from), out var other))
            {
                edgeVertices.Add(to);
                edgeVertices.Add(from);
                edgeFaces.Add(other);
                edgeFaces.Add(face);
            }
            else open.TryAdd((from, to), face);
        }
    }

    /// <summary>
    /// The silhouette for a light that travels along <paramref name="lightDirection"/> (in the mesh's own space): pairs of vertex indices, each edge running as it does in the polygon that
    /// faces the light, so that <see cref="ShadowVolumeWriter"/> gives every side of the volume the same outward direction. Returns the number of edges; <paramref name="pairs"/> needs
    /// twice <see cref="EdgeCount"/> entries.
    /// </summary>
    public int Silhouette(Vector3 lightDirection, int[] pairs)
    {
        var lit = _lit;
        var normals = _normals;
        for (var face = 0; face < normals.Length; face++) lit[face] = Vector3.Dot(lightDirection, normals[face]) < 0f;

        var count = 0;
        var faces = _edgeFaces;
        var edges = _edgeVertices;
        for (var edge = 0; edge < faces.Length; edge += 2)
        {
            var left = faces[edge];
            var right = faces[edge + 1];
            var leftLit = lit[left];
            var rightLit = right >= 0 && lit[right];
            if (leftLit == rightLit) continue;
            if (leftLit)
            {
                pairs[count * 2] = edges[edge];
                pairs[count * 2 + 1] = edges[edge + 1];
            }
            else
            {
                pairs[count * 2] = edges[edge + 1];
                pairs[count * 2 + 1] = edges[edge];
            }

            count++;
        }

        return count;
    }
}

/// <summary>Writes the sides of a shadow volume (<c>W3DVolumetricShadow::constructVolume</c>).</summary>
public static class ShadowVolumeWriter
{
    /// <summary>
    /// Four corners per silhouette edge: the edge in the world and the same edge carried along the light down to the plane <c>z = floorZ</c>. The corners are in the order
    /// <c>from, to, extruded to, extruded from</c>, which seen from outside the volume is clockwise, Direct3D's front face (W3D's polygons run the other way).
    /// A corner already below the floor stays where it is. Returns the number of quads written; <paramref name="corners"/> holds four per edge.
    /// </summary>
    public static int Write(Vector3[] vertices, int[] pairs, int edgeCount, in Matrix4x4 world, Vector3 lightDirection, float floorZ, Span<Vector3> corners)
    {
        var slope = lightDirection.Z < -1e-4f ? 1f / -lightDirection.Z : 0f;
        var quads = 0;
        for (var edge = 0; edge < edgeCount; edge++)
        {
            var from = Vector3.Transform(vertices[pairs[edge * 2]], world);
            var to = Vector3.Transform(vertices[pairs[edge * 2 + 1]], world);
            var offset = quads * 4;
            corners[offset] = from;
            corners[offset + 1] = to;
            corners[offset + 2] = to + lightDirection * (MathF.Max(0f, to.Z - floorZ) * slope);
            corners[offset + 3] = from + lightDirection * (MathF.Max(0f, from.Z - floorZ) * slope);
            quads++;
        }

        return quads;
    }
}
