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

    /// <summary>The default forward compositor with the W3D depth-write processor installed.</summary>
    public GraphicsCompositor CreateCompositor()
    {
        var compositor = GraphicsCompositorHelper.CreateDefault(false);
        var meshFeature = compositor.RenderFeatures.OfType<MeshRenderFeature>().Single();
        var stageSelector = meshFeature.RenderStageSelectors.OfType<MeshTransparentRenderStageSelector>().Single();
        meshFeature.PipelineProcessors.Add(new W3dDepthWritePipelineProcessor(DepthWrites, stageSelector.OpaqueRenderStage, stageSelector.TransparentRenderStage));
        return compositor;
    }

    public void Dispose() => Textures.Dispose();
}
