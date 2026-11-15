using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Draws the blended effects (<see cref="Layers.Fx.FxMaterialKey.InGammaPass"/>) after the world, blending in display values as the original's frame buffer does
/// (<see cref="CnCNew.Presentation.Fx.FrameBufferBlend"/>, issue 356). The world's target stores sRGB and blends in linear light, and a texture created with an sRGB format accepts no view
/// without that conversion, so the finished picture is copied into a scratch target of the same bytes without it, the effects are drawn there with the world's depth (read, never
/// written), and the bytes are copied back. <c>RTS3DScene::Flush</c> draws the particles last as well, after water and the translucent meshes.
/// </summary>
internal sealed class GammaEffectsRenderer : SceneRendererBase
{
    /// <summary>The render group of the effect runs this renderer draws; the scene's transparent stage leaves it out.</summary>
    public const RenderGroup Group = RenderGroup.Group30;

    public const RenderGroupMask GroupMask = RenderGroupMask.Group30;

    private Texture? _scratch;

    /// <summary>The stage of the effect runs (Stride's serialization wants a parameterless constructor, so it is set after construction).</summary>
    public RenderStage? RenderStage { get; set; }

    /// <summary>Frames in which the pass drew something (for the report).</summary>
    public long FramesDrawn { get; private set; }

    protected override void CollectCore(RenderContext context)
    {
        if (RenderStage is null) return;
        var output = context.RenderOutput;
        output.RenderTargetFormat0 = output.RenderTargetFormat0.ToNonSRgb();
        RenderStage.OutputValidator.Validate(ref output);
        context.RenderView.RenderStages.Add(RenderStage);
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        var commandList = drawContext.CommandList;
        if (RenderStage is null || commandList.RenderTargetCount == 0 || commandList.RenderTarget is not { } target || !HasNodes(context.RenderView, RenderStage)) return;
        var depth = commandList.DepthStencilBuffer;
        var viewport = commandList.Viewport;
        using (drawContext.PushRenderTargetsAndRestore())
        {
            if (!target.ViewFormat.IsSRgb())
            {
                // Nothing to convert: the target already holds display values.
                context.RenderSystem.Draw(drawContext, context.RenderView, RenderStage);
            }
            else
            {
                var scratch = Scratch(target);
                commandList.Copy(target, scratch);
                commandList.SetRenderTarget(depth, scratch);
                commandList.SetViewport(viewport);
                context.RenderSystem.Draw(drawContext, context.RenderView, RenderStage);
                commandList.Copy(scratch, target);
            }
        }

        FramesDrawn++;
    }

    protected override void Destroy()
    {
        _scratch?.Dispose();
        _scratch = null;
        base.Destroy();
    }

    private static bool HasNodes(RenderView view, RenderStage effects)
    {
        foreach (var stage in view.RenderStages)
        {
            if (stage.Index == effects.Index) return stage.SortedRenderNodes is { Count: > 0 };
        }

        return false;
    }

    /// <summary>A target of the same size and bytes as <paramref name="target"/> whose views write without sRGB encoding (a copy between the two moves the bytes unchanged).</summary>
    private Texture Scratch(Texture target)
    {
        var format = target.Format.ToNonSRgb();
        if (_scratch is { } existing && existing.Width == target.Width && existing.Height == target.Height && existing.Format == format) return existing;
        _scratch?.Dispose();
        _scratch = Texture.New2D(GraphicsDevice, target.Width, target.Height, format, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        return _scratch;
    }
}

/// <summary>
/// The pipeline state of <see cref="GammaEffectsRenderer"/>'s stage: the blend of the material pass (Stride applies it only in its own transparent stage), the depth read with
/// <c>PASS_LEQUAL</c> and never written, like the W3D sprite shaders (<c>SC_ADD_SPRITE</c>, <c>SC_ALPHA_SPRITE</c>, <c>SC_MUL_SPRITE</c> in <c>shader.cpp</c>).
/// </summary>
internal sealed class GammaEffectsPipelineProcessor : PipelineProcessor
{
    public RenderStage? RenderStage { get; set; }

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (RenderStage is null || renderNode.RenderStage != RenderStage || renderObject is not RenderMesh mesh) return;
        pipelineState.BlendState = mesh.MaterialPass.BlendState ?? BlendStates.AlphaBlend;
        pipelineState.DepthStencilState = DepthStencilStates.DepthRead;
        pipelineState.DepthStencilState.DepthBufferFunction = CompareFunction.LessEqual;
    }
}
