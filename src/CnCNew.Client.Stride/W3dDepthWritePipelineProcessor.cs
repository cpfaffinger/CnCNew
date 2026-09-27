using Stride.Graphics;
using Stride.Rendering;

namespace CnCNew.Client.Stride;

internal sealed class W3dDepthWritePipelineProcessor : PipelineProcessor
{
    private IReadOnlyDictionary<MaterialPass, bool> _depthWrites = new Dictionary<MaterialPass, bool>();
    private RenderStage? _opaqueStage;
    private RenderStage? _transparentStage;

    public W3dDepthWritePipelineProcessor()
    {
    }

    public W3dDepthWritePipelineProcessor(
        IReadOnlyDictionary<MaterialPass, bool> depthWrites,
        RenderStage opaqueStage,
        RenderStage transparentStage)
    {
        _depthWrites = depthWrites;
        _opaqueStage = opaqueStage;
        _transparentStage = transparentStage;
    }

    public override void Process(
        RenderNodeReference renderNodeReference,
        ref RenderNode renderNode,
        RenderObject renderObject,
        PipelineStateDescription pipelineState)
    {
        if ((renderNode.RenderStage == _opaqueStage || renderNode.RenderStage == _transparentStage) &&
            renderObject is RenderMesh mesh &&
            _depthWrites.TryGetValue(mesh.MaterialPass, out var enabled))
        {
            pipelineState.DepthStencilState.DepthBufferWriteEnable = enabled;
        }
    }
}
