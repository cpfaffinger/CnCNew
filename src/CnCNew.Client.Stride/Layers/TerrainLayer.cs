using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers;

/// <summary>Placeholder until the terrain, water and fog overlay layer is implemented.</summary>
internal sealed class TerrainLayer : IWorldLayer
{
    public string Name => "terrain";

    public void Attach(RenderServices services, LiveMatch match)
    {
    }

    public void Update(in WorldFrame frame)
    {
    }

    public void Dispose()
    {
    }
}
