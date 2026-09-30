using Stride.Core.Mathematics;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using BoundingBox = Stride.Core.Mathematics.BoundingBox;
using BoundingSphere = Stride.Core.Mathematics.BoundingSphere;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// A visible stand-in for a drawable whose W3D model failed to load: an axis-aligned unit box (-1..1 on every axis, one un-shaded flat
/// color) placed and scaled like the real model would be. Counted in <see cref="ObjectLayerStats.Placeholders"/> so missing content is
/// never silently invisible.
/// </summary>
internal static class PlaceholderBox
{
    private static readonly Vector3[] Corners =
    [
        new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1),
    ];

    private static readonly int[][] Faces =
    [
        [0, 1, 2, 3], [5, 4, 7, 6], [4, 0, 3, 7], [1, 5, 6, 2], [3, 2, 6, 7], [4, 5, 1, 0],
    ];

    public static Model Build(GraphicsDevice device, SharedIndexBuffer indices, Color4 color)
    {
        var vertices = new W3dRenderVertex[Faces.Length * 6];
        var cursor = 0;
        foreach (var face in Faces)
        {
            var a = Corners[face[0]];
            var b = Corners[face[1]];
            var c = Corners[face[2]];
            var d = Corners[face[3]];
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            void Add(Vector3 position)
            {
                vertices[cursor++] = new W3dRenderVertex(
                    position, normal,
                    Color.White, Color.White, Color.White, Color.White,
                    Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            }

            Add(a); Add(b); Add(c);
            Add(a); Add(c); Add(d);
        }

        var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(device, vertices, GraphicsResourceUsage.Default);
        var material = Material.New(device, new MaterialDescriptor
        {
            Attributes =
            {
                Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
            },
        });
        var box = new BoundingBox(new Vector3(-1), new Vector3(1));
        var model = new Model
        {
            // Root uses the same world-matrix-override trick as every other object-layer node (see ObjectLayer), so the caller can
            // place and scale the box exactly like a rigid part, without going through Entity.Transform/Quaternion conversions.
            Skeleton = new Skeleton
            {
                Nodes = [new ModelNodeDefinition { ParentIndex = -1, Name = "Root", Flags = ModelNodeFlags.EnableRender | ModelNodeFlags.OverrideWorldMatrix }],
            },
            BoundingBox = box,
            BoundingSphere = BoundingSphere.FromBox(box),
        };
        model.Materials.Add(material);
        model.Meshes.Add(new Mesh
        {
            NodeIndex = 0,
            Name = "Placeholder",
            Draw = new MeshDraw
            {
                StartLocation = 0,
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = vertices.Length,
                VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Length)],
                IndexBuffer = indices.GetBinding(vertices.Length),
            },
            MaterialIndex = 0,
            BoundingBox = box,
            BoundingSphere = BoundingSphere.FromBox(box),
        });
        return model;
    }
}
