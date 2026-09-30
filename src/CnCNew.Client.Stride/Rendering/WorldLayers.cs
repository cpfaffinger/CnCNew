using CnCNew.Client.Stride.Layers;

namespace CnCNew.Client.Stride.Rendering;

internal static class WorldLayers
{
    /// <summary>Draw order is scene order; one line per layer.</summary>
    public static IWorldLayer[] CreateAll() =>
    [
        new TerrainLayer(),
        new ObjectLayer(),
        new FxLayer(),
    ];
}
