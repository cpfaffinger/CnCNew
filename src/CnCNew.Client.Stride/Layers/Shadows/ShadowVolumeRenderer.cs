using System.Numerics;
using System.Runtime.InteropServices;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Shadows;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using StrideBuffer = Stride.Graphics.Buffer;
using StrideVector3 = Stride.Core.Mathematics.Vector3;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace CnCNew.Client.Stride.Layers.Shadows;

/// <summary>What one rigid part of a live drawable remembers of its shadow: the silhouette for the light it last had, and for a thing that stands still the corners of the volume.</summary>
internal sealed class ShadowPartState
{
    public Vector3 LocalLight;
    public int[] Pairs = [];
    public int EdgeCount = -1;
    public Vector3[] Corners = [];
    public int Quads = -1;
    public Matrix4x4 World;
    public float Floor;
    public Vector3 Light;
}

/// <summary>
/// The stencil shadows of the objects (<c>W3DVolumetricShadow</c>, <c>docs/CLIENT-SHADOWS.md</c>): every frame the sides of all shadow volumes go into one vertex buffer, and one mesh draws them
/// into the stencil buffer; a second mesh, a rectangle over the screen, then darkens what lies inside a volume. Both are drawn in the transparent stage, after the roads and the ground decals
/// and before everything else that stage holds, the place <c>RTS3DScene::Flush</c> gives them.
/// </summary>
internal sealed class ShadowVolumeRenderer : IDisposable
{
    /// <summary>The cosine of the angle the light may turn against a part before its silhouette is found again; the original's <c>cosAngleToCare</c> plays the same part.</summary>
    private const float SilhouetteTolerance = 0.9997f;

    private const int MaxQuads = 1 << 17;

    private readonly RenderServices _services;
    private readonly Vector3[] _corners = new Vector3[MaxQuads * 4];
    private readonly Vector3[] _fillCorners = new Vector3[4];
    private readonly StrideBuffer _vertexBuffer;
    private readonly StrideBuffer _indexBuffer;
    private readonly StrideBuffer _fillBuffer;
    private readonly StrideBuffer _fillIndices;
    private readonly Mesh _volumeMesh;
    private readonly ModelComponent _volume;
    private readonly ModelComponent _fill;
    private int _quads;

    public ShadowVolumeRenderer(RenderServices services)
    {
        _services = services;
        var device = services.GraphicsDevice;
        var indices = new int[MaxQuads * 6];
        for (int quad = 0, vertex = 0, index = 0; quad < MaxQuads; quad++, vertex += 4, index += 6)
        {
            indices[index] = vertex;
            indices[index + 1] = vertex + 1;
            indices[index + 2] = vertex + 2;
            indices[index + 3] = vertex + 2;
            indices[index + 4] = vertex + 3;
            indices[index + 5] = vertex;
        }

        var layout = new VertexDeclaration(VertexElement.Position<StrideVector3>());
        _vertexBuffer = StrideBuffer.Vertex.New(device, _corners.Length * Marshal.SizeOf<Vector3>(), GraphicsResourceUsage.Dynamic);
        _indexBuffer = StrideBuffer.Index.New(device, indices);
        _fillBuffer = StrideBuffer.Vertex.New(device, _fillCorners.Length * Marshal.SizeOf<Vector3>(), GraphicsResourceUsage.Dynamic);
        _fillIndices = StrideBuffer.Index.New(device, new[] { 0, 1, 2, 2, 3, 0 });

        var everywhere = new BoundingBox(new StrideVector3(-1e6f), new StrideVector3(1e6f));
        var sphere = BoundingSphere.FromBox(everywhere);
        // No colour reaches the target from this pass; what the mesh draws is the stencil count.
        var volumeMaterial = Create(new ComputeColor(new Color4(0f, 0f, 0f, 1f)), new BlendStateDescription(Blend.One, Blend.Zero), ShadowStencilRole.Volume, StackedBackToFrontSortMode.ShadowVolumeLayer);
        // D3DBLEND_DESTCOLOR, D3DBLEND_ZERO: the target times the colour. The original multiplies in gamma space, and a product in linear space differs only by a power, so the factor is the
        // colour's linear value.
        var brightness = device.ColorSpace == ColorSpace.Linear ? MathUtil.SRgbToLinear(ShadowSun.StencilBrightness) : ShadowSun.StencilBrightness;
        var fillMaterial = Create(new ComputeColor(new Color4(brightness, brightness, brightness, 1f)), Multiply(Blend.DestinationColor, Blend.Zero), ShadowStencilRole.Fill, StackedBackToFrontSortMode.ShadowFillLayer);

        _volumeMesh = new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = 0,
                VertexBuffers = [new VertexBufferBinding(_vertexBuffer, layout, _corners.Length)],
                IndexBuffer = new IndexBufferBinding(_indexBuffer, true, indices.Length),
            },
            MaterialIndex = 0,
            BoundingBox = everywhere,
            BoundingSphere = sphere,
        };
        _volume = Add("shadow volumes", volumeMaterial, _volumeMesh);
        _fill = Add("shadow fill", fillMaterial, new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = 6,
                VertexBuffers = [new VertexBufferBinding(_fillBuffer, layout, _fillCorners.Length)],
                IndexBuffer = new IndexBufferBinding(_fillIndices, true, 6),
            },
            MaterialIndex = 0,
            BoundingBox = everywhere,
            BoundingSphere = sphere,
        });
    }

    /// <summary>Sides of volumes that did not fit this frame (counted, never silently lost).</summary>
    public long DroppedQuads { get; private set; }

    public int Quads => _quads;

    public void Begin() => _quads = 0;

    /// <summary>Adds the volume of one part. <paramref name="keepCorners"/>: the thing stands still, its corners are kept and copied until its transform changes.</summary>
    public void Add(ShadowCasterMesh caster, ShadowPartState state, in Matrix4x4 world, Vector3 light, float floorZ, bool keepCorners)
    {
        // The rows of the world matrix are the part's axes in the world, so these products are the light in the part's space; the matrices turn and scale evenly, and only the direction counts.
        var local = Vector3.Normalize(new Vector3(
            light.X * world.M11 + light.Y * world.M12 + light.Z * world.M13,
            light.X * world.M21 + light.Y * world.M22 + light.Z * world.M23,
            light.X * world.M31 + light.Y * world.M32 + light.Z * world.M33));
        if (state.EdgeCount < 0 || Vector3.Dot(local, state.LocalLight) < SilhouetteTolerance)
        {
            if (state.Pairs.Length == 0) state.Pairs = new int[caster.EdgeCount * 2];
            state.EdgeCount = caster.Silhouette(local, state.Pairs);
            state.LocalLight = local;
            state.Quads = -1;
        }

        var edges = state.EdgeCount;
        if (edges == 0) return;
        if (_quads + edges > MaxQuads)
        {
            DroppedQuads += edges;
            return;
        }

        var target = _corners.AsSpan(_quads * 4, edges * 4);
        if (keepCorners && state.Quads == edges && state.Floor == floorZ && state.World == world && state.Light == light)
        {
            state.Corners.AsSpan(0, edges * 4).CopyTo(target);
        }
        else
        {
            ShadowVolumeWriter.Write(caster.Vertices, state.Pairs, edges, world, light, floorZ, target);
            if (keepCorners)
            {
                if (state.Corners.Length < edges * 4) state.Corners = new Vector3[edges * 4];
                target.CopyTo(state.Corners);
                state.Quads = edges;
                state.Floor = floorZ;
                state.World = world;
                state.Light = light;
            }
        }

        _quads += edges;
    }

    public void End(CommandList commandList, RtsCamera camera)
    {
        var any = _quads > 0;
        _volume.Enabled = any;
        _fill.Enabled = any;
        if (!any) return;
        _vertexBuffer.SetData(commandList, (ReadOnlySpan<Vector3>)_corners.AsSpan(0, _quads * 4));
        _volumeMesh.Draw.DrawCount = _quads * 6;

        // The screen as a rectangle in the world, between the clip planes; the fill ignores depth, so where it lies matters only to the clipper.
        if (!Matrix4x4.Invert(camera.View * camera.Projection, out var inverse)) return;
        _fillCorners[0] = Unproject(-1f, 1f);
        _fillCorners[1] = Unproject(1f, 1f);
        _fillCorners[2] = Unproject(1f, -1f);
        _fillCorners[3] = Unproject(-1f, -1f);
        _fillBuffer.SetData(commandList, (ReadOnlySpan<Vector3>)_fillCorners);

        Vector3 Unproject(float x, float y)
        {
            var point = Vector4.Transform(new Vector4(x, y, 0.5f, 1f), inverse);
            return new Vector3(point.X, point.Y, point.Z) / point.W;
        }
    }

    public void Dispose()
    {
        _services.Scene.Entities.Remove(_volume.Entity);
        _services.Scene.Entities.Remove(_fill.Entity);
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
        _fillBuffer.Dispose();
        _fillIndices.Dispose();
    }

    /// <summary>A blend of the colour with itself; the two-argument constructor would use the same factors for alpha, and Direct3D 11 refuses colour factors there.</summary>
    internal static BlendStateDescription Multiply(Blend source, Blend destination)
    {
        var description = new BlendStateDescription(source, destination);
        description.RenderTarget0.AlphaSourceBlend = Blend.Zero;
        description.RenderTarget0.AlphaDestinationBlend = Blend.One;
        return description;
    }

    private Material Create(IComputeColor color, BlendStateDescription blend, ShadowStencilRole role, int layer)
    {
        var material = Material.New(_services.GraphicsDevice, new MaterialDescriptor
        {
            Attributes = { Emissive = new MaterialEmissiveMapFeature(color) { UseAlpha = true }, CullMode = CullMode.None },
        });
        var pass = material.Passes[0];
        pass.CullMode = CullMode.None;
        pass.HasTransparency = true;
        pass.BlendState = blend;
        pass.AlphaToCoverage = false;
        _services.StencilRoles[pass] = role;
        _services.TransparentOrder.Layers[pass] = layer;
        return material;
    }

    private ModelComponent Add(string name, Material material, Mesh mesh)
    {
        var model = new Model { BoundingBox = mesh.BoundingBox, BoundingSphere = mesh.BoundingSphere };
        model.Materials.Add(material);
        model.Meshes.Add(mesh);
        var component = new ModelComponent(model) { IsShadowCaster = false, Enabled = false };
        _services.Scene.Entities.Add(new Entity(name) { component });
        return component;
    }
}
