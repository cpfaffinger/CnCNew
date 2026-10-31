using CnCNew.Content;
using CnCNew.Game.Data;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>What every world layer shares: the device, the scene to add entities to and the W3D material/texture machinery.</summary>
internal sealed class RenderServices : IDisposable
{
    public RenderServices(GraphicsDevice graphicsDevice, GraphicsContext graphicsContext, Scene scene, GameData data)
    {
        GraphicsDevice = graphicsDevice;
        GraphicsContext = graphicsContext;
        Scene = scene;
        Data = data;
        Textures = new W3dTextureCache(graphicsDevice, data.Content);
        Materials = new W3dMaterialFactory(graphicsDevice, Textures, DepthWrites);
    }

    public GraphicsDevice GraphicsDevice { get; }
    public GraphicsContext GraphicsContext { get; }
    public Scene Scene { get; }
    public GameData Data { get; }
    public ContentFileSystem Content => Data.Content;

    /// <summary>Decoded and uploaded textures by W3D reference; shared by every layer.</summary>
    public W3dTextureCache Textures { get; }

    /// <summary>Creates materials that follow the W3D material pass rules; the depth-write decisions are shared with the compositor.</summary>
    public W3dMaterialFactory Materials { get; }

    public Dictionary<MaterialPass, bool> DepthWrites { get; } = [];

    /// <summary>The order of the transparent stage, with the ground layers and the stacking of the roads.</summary>
    public StackedBackToFrontSortMode TransparentOrder { get; } = new();

    /// <summary>The order of the opaque stage, with the sky box first.</summary>
    public SkyFirstSortMode OpaqueOrder { get; } = new();

    /// <summary>The material passes of the stencil shadows.</summary>
    public Dictionary<MaterialPass, ShadowStencilRole> StencilRoles { get; } = [];

    /// <summary>The shadow decals of the objects drawn this frame; the object layer fills it, the effect layer draws it.</summary>
    public List<CnCNew.Presentation.Shadows.ShadowDecal> ShadowDecals { get; } = [];

    /// <summary>The default forward compositor with the W3D depth-write processor installed.</summary>
    public GraphicsCompositor CreateCompositor()
    {
        // W3DDisplay clears to black; where no terrain is and no sky box is shown, there is nothing.
        var compositor = GraphicsCompositorHelper.CreateDefault(false, clearColor: new global::Stride.Core.Mathematics.Color4(0f, 0f, 0f, 1f));
        var meshFeature = compositor.RenderFeatures.OfType<MeshRenderFeature>().Single();
        var stageSelector = meshFeature.RenderStageSelectors.OfType<MeshTransparentRenderStageSelector>().Single();
        meshFeature.PipelineProcessors.Add(new W3dDepthWritePipelineProcessor(DepthWrites, stageSelector.OpaqueRenderStage, stageSelector.TransparentRenderStage));
        meshFeature.PipelineProcessors.Add(new ShadowStencilPipelineProcessor(StencilRoles));
        stageSelector.TransparentRenderStage.SortMode = TransparentOrder;
        stageSelector.OpaqueRenderStage.SortMode = OpaqueOrder;
        return compositor;
    }

    public void Dispose() => Textures.Dispose();
}
