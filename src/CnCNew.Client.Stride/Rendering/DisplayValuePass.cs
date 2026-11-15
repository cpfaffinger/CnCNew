using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Draws what blends after the opaque world - Stride's transparent stage (roads, ground decals and tread marks, the stencil shadows, water, translucent meshes) and then the stage of the
/// blended effects (<see cref="Layers.Fx.FxMaterialKey.InEffectsStage"/>) - blending in display values as the original's Direct3D 8 frame buffer does
/// (<see cref="CnCNew.Presentation.Fx.FrameBufferBlend"/>, issues 356 and 359). The world's target stores sRGB and blends in linear light, and a texture created with an sRGB format accepts
/// no view without that conversion, so the picture of the opaque world is copied into a scratch target of the same bytes without it, the stages are drawn there with the world's depth and
/// stencil, and the bytes are copied back. Every material of these stages puts out display values: the effects read their texels and vertex colours as they are stored, every other
/// one encodes its shaded colour (<see cref="DisplayValueOutput"/>). <c>RTS3DScene::Flush</c> keeps the same order: decals and shadows, water, translucent meshes, particles last.
/// </summary>
internal sealed class DisplayValuePass : SceneRendererBase
{
    /// <summary>The render group of the effect runs of <see cref="EffectsStage"/>; the scene's transparent stage leaves it out.</summary>
    public const RenderGroup EffectsGroup = RenderGroup.Group30;

    public const RenderGroupMask EffectsGroupMask = RenderGroupMask.Group30;

    private Texture? _scratch;

    /// <summary>Stride's transparent stage, taken away from the forward renderer (Stride's serialization wants a parameterless constructor, so the stages are set after construction).</summary>
    public RenderStage? TransparentStage { get; set; }

    /// <summary>The stage of the blended effects, drawn after <see cref="TransparentStage"/>.</summary>
    public RenderStage? EffectsStage { get; set; }

    /// <summary>Frames in which the pass drew something (for the report).</summary>
    public long FramesDrawn { get; private set; }

    protected override void CollectCore(RenderContext context)
    {
        var output = context.RenderOutput;
        output.RenderTargetFormat0 = output.RenderTargetFormat0.ToNonSRgb();
        foreach (var stage in Stages())
        {
            var own = output;
            stage.OutputValidator.Validate(ref own);
            context.RenderView.RenderStages.Add(stage);
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        var commandList = drawContext.CommandList;
        if (commandList.RenderTargetCount == 0 || commandList.RenderTarget is not { } target) return;
        var drawn = false;
        foreach (var stage in Stages()) drawn |= HasNodes(context.RenderView, stage);
        if (!drawn) return;
        var depth = commandList.DepthStencilBuffer;
        var viewport = commandList.Viewport;
        using (drawContext.PushRenderTargetsAndRestore())
        {
            var scratch = target.ViewFormat.IsSRgb() ? Scratch(target) : null;
            if (scratch is not null)
            {
                commandList.Copy(target, scratch);
                commandList.SetRenderTarget(depth, scratch);
                commandList.SetViewport(viewport);
            }

            // Without a scratch target the target already holds display values.
            foreach (var stage in Stages()) context.RenderSystem.Draw(drawContext, context.RenderView, stage);
            if (scratch is not null) commandList.Copy(scratch, target);
        }

        FramesDrawn++;
    }

    protected override void Destroy()
    {
        _scratch?.Dispose();
        _scratch = null;
        base.Destroy();
    }

    private IEnumerable<RenderStage> Stages()
    {
        if (TransparentStage is { } transparent) yield return transparent;
        if (EffectsStage is { } effects) yield return effects;
    }

    private static bool HasNodes(RenderView view, RenderStage wanted)
    {
        foreach (var stage in view.RenderStages)
        {
            if (stage.Index == wanted.Index) return stage.SortedRenderNodes is { Count: > 0 };
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
/// The pipeline state of <see cref="DisplayValuePass"/>'s effect stage: the blend of the material pass (Stride applies it only in its own transparent stage), the depth read with
/// <c>PASS_LEQUAL</c> and never written, like the W3D sprite shaders (<c>SC_ADD_SPRITE</c>, <c>SC_ALPHA_SPRITE</c>, <c>SC_MUL_SPRITE</c> in <c>shader.cpp</c>).
/// </summary>
internal sealed class EffectsStagePipelineProcessor : PipelineProcessor
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
