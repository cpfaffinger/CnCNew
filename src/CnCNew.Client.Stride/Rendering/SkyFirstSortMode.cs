using Stride.Rendering;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Stride's order of the opaque stage with the sky box in front of it. The sky writes no depth and has to stand inside the far plane, so its faces lie nearer than distant terrain: it must be
/// drawn first, and everything else over it.
/// </summary>
internal sealed class SkyFirstSortMode : StateChangeSortMode
{
    /// <summary>The material passes of the sky box.</summary>
    public HashSet<MaterialPass> First { get; } = [];

    public override unsafe void GenerateSortKey(RenderView renderView, RenderViewStage renderViewStage, SortKey* sortKeys)
    {
        base.GenerateSortKey(renderView, renderViewStage, sortKeys);
        if (First.Count == 0) return;
        var renderNodes = renderViewStage.RenderNodes;
        for (var index = 0; index < renderNodes.Count; index++)
            if (renderNodes[index].RenderObject is RenderMesh { MaterialPass: { } pass } && First.Contains(pass)) sortKeys[index].Value = 0;
    }
}
