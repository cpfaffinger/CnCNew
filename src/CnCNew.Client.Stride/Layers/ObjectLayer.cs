using CnCNew.Client.Stride.Layers.Objects;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers;

/// <summary>
/// Draws units, buildings, props and scenery of a live match as animated W3D models. <c>WorldLayers.CreateAll()</c> needs a
/// parameterless, unqualified <c>ObjectLayer</c> in this namespace, so this class is a thin forwarder; the actual implementation is
/// <see cref="ObjectLayerCore"/> under <c>Layers/Objects</c> (see its remarks for the instancing strategy and
/// <c>docs/CLIENT-OBJECTS.md</c> for the full write-up).
/// </summary>
internal sealed class ObjectLayer : IWorldLayer
{
    private readonly ObjectLayerCore _core = new();

    public string Name => _core.Name;

    /// <summary>Per-frame counters (drawn, culled, pooled, models loaded, triangles, timing); see <see cref="ObjectLayerStats"/>.</summary>
    public ObjectLayerStats Stats => _core.Stats;

    public void Attach(RenderServices services, LiveMatch match) => _core.Attach(services, match);

    public void Update(in WorldFrame frame) => _core.Update(frame);

    public void Dispose() => _core.Dispose();
}
