using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride.Layers;

/// <summary>Placeholder until the object and skeleton layer is implemented.</summary>
internal sealed class ObjectLayer : IWorldLayer
{
    public string Name => "object";

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
