using CnCNew.Client.Stride.Layers.Objects;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;
using CnCNew.Presentation.Terrain;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace CnCNew.Client.Stride.Layers;

/// <summary>
/// The sky box (<c>WaterRenderObjClass::Render</c>): the model <c>new_skybox</c> scaled by <c>SkyBoxScale</c>, centred on the camera at the height <c>SkyBoxPositionZ</c>, drawn while a script has
/// asked for it with <c>DRAW_SKYBOX_BEGIN</c> (<c>W3DDisplay::reset</c> turns it off for every map). It stands far outside the clip planes of a low camera, so it is drawn smaller by a factor
/// around the eye, which changes nothing on the screen, and writes no depth.
/// </summary>
internal sealed class SkyLayer : IWorldLayer
{
    private const string ModelName = "new_skybox";

    /// <summary>The half width of the model.</summary>
    private const float ModelExtent = 750f;

    private readonly List<IDisposable> _resources = [];
    private RenderServices? _services;
    private Entity? _entity;
    private ModelComponent? _component;
    private MapEnvironment? _environment;

    public string Name => "sky";

    public void Attach(RenderServices services, LiveGame match)
    {
        _services = services;
        _environment = match.Environment;
        var library = new W3dAssetLibrary(path => services.Content.Exists(path) ? services.Content.OpenRead(path) : null);
        var source = library.GetModel(ModelName) ?? throw new InvalidDataException($"The sky box model '{ModelName}' is not in the content.");
        var model = new Model();
        var lod = source.LodCount - 1;
        foreach (var sub in source.GetLod(lod))
        {
            if (!sub.IsRenderable || sub.Mesh is not { } mesh) continue;
            foreach (var batch in W3dRenderPlan.Build(mesh))
            {
                var texture = batch.Passes.Count > 0 ? batch.Passes[0].TextureIds.ElementAtOrDefault(0) : null;
                if (texture is not { } id || id >= mesh.Materials.Textures.Count) continue;
                var name = _environment.Skybox.Replace(mesh.Materials.Textures[(int)id].Name);
                var vertices = ObjectRenderVertexBuilder.Build(mesh, batch).Vertices;
                var indices = Enumerable.Range(0, vertices.Length).ToArray();
                var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(services.GraphicsDevice, vertices, GraphicsResourceUsage.Immutable);
                var indexBuffer = global::Stride.Graphics.Buffer.Index.New(services.GraphicsDevice, indices);
                _resources.Add(vertexBuffer);
                _resources.Add(indexBuffer);
                var materialIndex = model.Materials.Count;
                model.Materials.Add(CreateMaterial(services, name));
                model.Meshes.Add(new Mesh
                {
                    Draw = new MeshDraw
                    {
                        PrimitiveType = PrimitiveType.TriangleList,
                        DrawCount = indices.Length,
                        VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Length)],
                        IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length),
                    },
                    MaterialIndex = materialIndex,
                    BoundingBox = new BoundingBox(new Vector3(-ModelExtent), new Vector3(ModelExtent)),
                    BoundingSphere = new BoundingSphere(Vector3.Zero, ModelExtent * 2f),
                });
            }
        }

        model.BoundingBox = new BoundingBox(new Vector3(-ModelExtent), new Vector3(ModelExtent));
        model.BoundingSphere = new BoundingSphere(Vector3.Zero, ModelExtent * 2f);
        _component = new ModelComponent(model) { IsShadowCaster = false, Enabled = false };
        _entity = new Entity("sky box") { _component };
        services.Scene.Entities.Add(_entity);
    }

    public void Update(in WorldFrame frame)
    {
        if (_component is not { } component || _entity is not { } entity || _environment is not { } environment) return;
        var shown = frame.Match.SkyboxShown;
        component.Enabled = shown;
        if (!shown) return;
        var camera = frame.Match.Camera;
        var eye = camera.Eye;
        var scale = environment.SkyboxScale;
        var reach = ModelExtent * scale * 1.74f + MathF.Abs(eye.Z - environment.SkyboxZ);
        var shrink = MathF.Min(1f, camera.FarPlane * 0.95f / reach);
        var center = new System.Numerics.Vector3(eye.X, eye.Y, environment.SkyboxZ);
        entity.Transform.Position = (eye + (center - eye) * shrink).ToStride();
        entity.Transform.Scale = new Vector3(scale * shrink);
    }

    public void Dispose()
    {
        if (_entity is not null) _services?.Scene.Entities.Remove(_entity);
        foreach (var resource in _resources) resource.Dispose();
        _resources.Clear();
        _entity = null;
        _component = null;
    }

    /// <summary>The texture as it is, seen from inside: the model's lighting asks for none, and the original clamps its textures against seams at the corners.</summary>
    private static Material CreateMaterial(RenderServices services, string texture)
    {
        var color = new ComputeTextureColor(services.Textures.Resolve(new W3dTextureReference(texture)), TextureCoordinate.Texcoord0, Vector2.One, Vector2.Zero)
        {
            Filtering = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Clamp,
            AddressModeV = TextureAddressMode.Clamp,
        };
        var material = Material.New(services.GraphicsDevice, new MaterialDescriptor { Attributes = { Emissive = new MaterialEmissiveMapFeature(color), CullMode = CullMode.None } });
        var pass = material.Passes[0];
        pass.CullMode = CullMode.None;
        pass.HasTransparency = false;
        services.DepthWrites[pass] = false;
        services.OpaqueOrder.First.Add(pass);
        return material;
    }
}
