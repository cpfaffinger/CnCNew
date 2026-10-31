using Stride.Rendering;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Back to front, with two things of the original's order inside it. <b>Layers:</b> <c>RTS3DScene::Flush</c> draws roads and ground decals with the terrain, then the stencil shadows, and only
/// after them water, translucent meshes and particles; here all of those are in Stride's transparent stage, so the passes of the first group get a layer of their own, which the top byte of the
/// key puts before everything else. <b>Stacking of the roads</b> (D-7): road meshes overlap and are blended in the order of <c>W3DRoadBuffer::drawRoads</c>, so they share one bounding box and
/// with it one distance. Among equal distances Stride goes by the state key of an object, which depends on the effects compiled so far, and then by its index in the visibility group, which
/// changes as objects come and go - that is how the order of the roads came to differ between two runs. Here the state bits of a road hold its place.
/// </summary>
internal sealed class StackedBackToFrontSortMode : BackToFrontSortMode
{
    public const int RoadLayer = 1;
    public const int GroundDecalLayer = 2;
    public const int ShadowVolumeLayer = 3;
    public const int ShadowFillLayer = 4;

    private const int NormalLayer = 16;

    // BackToFrontSortMode: the distance lies above the 24 bits of the state, the top byte holds the render feature's key; only the mesh feature draws in this stage.
    private const ulong StateBits = 0xFFFFFF;
    private const ulong LayerBits = 0xFF00000000000000;
    private const int LayerShift = 56;

    /// <summary>The road meshes of the map with their place in the stacking order.</summary>
    public Dictionary<Mesh, int> Stacking { get; } = [];

    /// <summary>The material passes that are drawn before everything else in this stage, each with its layer.</summary>
    public Dictionary<MaterialPass, int> Layers { get; } = [];

    public override unsafe void GenerateSortKey(RenderView renderView, RenderViewStage renderViewStage, SortKey* sortKeys)
    {
        base.GenerateSortKey(renderView, renderViewStage, sortKeys);
        var renderNodes = renderViewStage.RenderNodes;
        for (var index = 0; index < renderNodes.Count; index++)
        {
            var layer = NormalLayer;
            if (renderNodes[index].RenderObject is RenderMesh renderMesh)
            {
                if (renderMesh.MaterialPass is { } pass && Layers.TryGetValue(pass, out var own)) layer = own;
                if (renderMesh.Mesh is { } mesh && Stacking.TryGetValue(mesh, out var place)) sortKeys[index].Value = (sortKeys[index].Value & ~StateBits) | (uint)place;
            }

            sortKeys[index].Value = (sortKeys[index].Value & ~LayerBits) | ((ulong)layer << LayerShift);
        }
    }
}
