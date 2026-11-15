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
        Materials = new W3dMaterialFactory(graphicsDevice, Textures, DepthWrites, displayValueBlending: true);
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

    /// <summary>The pass that blends in display values, made with the compositor (null before).</summary>
    public DisplayValuePass? DisplayValues { get; private set; }

    /// <summary>
    /// The default forward compositor with the W3D depth-write processor installed. The forward renderer draws the opaque world only; what blends - Stride's transparent stage and the
    /// stage of the blended effects - is drawn after it by <see cref="DisplayValuePass"/>, in display values (issues 356 and 359).
    /// </summary>
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

        // The blended effects leave the world's stages for a stage of their own, drawn last. Their runs are placed on the view axis in the builder's order, so back to front by
        // that point is the order of DoParticles.
        stageSelector.RenderGroup &= ~DisplayValuePass.EffectsGroupMask;
        var effectsStage = new RenderStage("Effects", "Main") { SortMode = new BackToFrontSortMode() };
        compositor.RenderStages.Add(effectsStage);
        meshFeature.RenderStageSelectors.Add(new SimpleGroupToRenderStageSelector { RenderStage = effectsStage, EffectName = stageSelector.EffectName, RenderGroup = DisplayValuePass.EffectsGroupMask });
        meshFeature.PipelineProcessors.Add(new EffectsStagePipelineProcessor { RenderStage = effectsStage });
        var camera = (SceneCameraRenderer)compositor.Game;
        var forward = (ForwardRenderer)camera.Child;
        // The transparent stage's materials put out display values only with the encoding shader; without it they stay with the forward renderer, in linear light.
        if (DisplayValueOutput.Available) forward.TransparentRenderStage = null;
        DisplayValues = new DisplayValuePass { TransparentStage = DisplayValueOutput.Available ? stageSelector.TransparentRenderStage : null, EffectsStage = effectsStage };
        camera.Child = new SceneRendererCollection { forward, DisplayValues };
        return compositor;
    }

    public void Dispose() => Textures.Dispose();
}
