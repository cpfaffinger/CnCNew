using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers;

/// <summary>Placeholder until the effect layer is implemented.</summary>
internal sealed class FxLayer : IWorldLayer
{
    public string Name => "fx";

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
