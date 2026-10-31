using Stride.Graphics;
using Stride.Rendering;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>What a material pass does to the stencil buffer.</summary>
internal enum ShadowStencilRole
{
    /// <summary>The sides of the shadow volumes: no colour, no depth; a front face that passes the depth test counts up, a back face down.</summary>
    Volume,

    /// <summary>The rectangle over the screen: draws where the count is not zero, whatever the depth.</summary>
    Fill,
}

/// <summary>
/// The render states of <c>W3DVolumetricShadowManager::renderShadows</c> and <c>renderStencilShadows</c>. The original draws the volume twice with the culling turned (<c>D3DSTENCILOP_INCR</c>,
/// <c>D3DSTENCILOP_DECRSAT</c>); Direct3D 11 has the two faces in one state, with wrapping counters, so that a count that overflows comes back.
/// </summary>
internal sealed class ShadowStencilPipelineProcessor : PipelineProcessor
{
    private readonly IReadOnlyDictionary<MaterialPass, ShadowStencilRole> _roles = new Dictionary<MaterialPass, ShadowStencilRole>();

    public ShadowStencilPipelineProcessor()
    {
    }

    public ShadowStencilPipelineProcessor(IReadOnlyDictionary<MaterialPass, ShadowStencilRole> roles) => _roles = roles;

    public override void Process(RenderNodeReference renderNodeReference, ref RenderNode renderNode, RenderObject renderObject, PipelineStateDescription pipelineState)
    {
        if (renderObject is not RenderMesh { MaterialPass: { } pass } || !_roles.TryGetValue(pass, out var role)) return;
        ref var state = ref pipelineState.DepthStencilState;
        state.DepthBufferWriteEnable = false;
        state.StencilEnable = true;
        state.StencilMask = 0xFF;
        state.StencilWriteMask = 0xFF;
        pipelineState.RasterizerState.CullMode = CullMode.None;
        if (role == ShadowStencilRole.Volume)
        {
            state.DepthBufferEnable = true;
            state.DepthBufferFunction = CompareFunction.LessEqual;
            state.FrontFace = Count(StencilOperation.Increment);
            state.BackFace = Count(StencilOperation.Decrement);
            pipelineState.BlendState.RenderTarget0.ColorWriteChannels = ColorWriteChannels.None;
            return;
        }

        state.DepthBufferEnable = false;
        var test = new DepthStencilStencilOpDescription
        {
            StencilFunction = CompareFunction.NotEqual,
            StencilPass = StencilOperation.Keep,
            StencilFail = StencilOperation.Keep,
            StencilDepthBufferFail = StencilOperation.Keep,
        };
        state.FrontFace = test;
        state.BackFace = test;
    }

    private static DepthStencilStencilOpDescription Count(StencilOperation pass) => new()
    {
        StencilFunction = CompareFunction.Always,
        StencilPass = pass,
        StencilFail = StencilOperation.Keep,
        StencilDepthBufferFail = StencilOperation.Keep,
    };
}
