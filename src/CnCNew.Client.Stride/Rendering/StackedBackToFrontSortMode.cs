using Stride.Rendering;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Back to front, with the stacking of the roads inside it (D-7): road meshes overlap and are blended in the order of <c>W3DRoadBuffer::drawRoads</c>, so they share one bounding box
/// and with it one distance. Among equal distances Stride goes by the state key of an object, which depends on the effects compiled so far, and then by its index in the visibility
/// group, which changes as objects come and go - that is how the order of the roads came to differ between two runs. Here the state bits of a road hold its place.
/// </summary>
internal sealed class StackedBackToFrontSortMode : BackToFrontSortMode
{
    // BackToFrontSortMode: the distance lies above the 24 bits of the state.
    private const ulong StateBits = 0xFFFFFF;

    /// <summary>The road meshes of the map with their place in the stacking order.</summary>
    public Dictionary<Mesh, int> Stacking { get; } = [];

    public override unsafe void GenerateSortKey(RenderView renderView, RenderViewStage renderViewStage, SortKey* sortKeys)
    {
        base.GenerateSortKey(renderView, renderViewStage, sortKeys);
        if (Stacking.Count == 0) return;
        var renderNodes = renderViewStage.RenderNodes;
        for (var index = 0; index < renderNodes.Count; index++)
            if (renderNodes[index].RenderObject is RenderMesh { Mesh: { } mesh } && Stacking.TryGetValue(mesh, out var place))
                sortKeys[index].Value = (sortKeys[index].Value & ~StateBits) | (uint)place;
    }
}
