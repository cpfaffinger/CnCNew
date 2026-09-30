using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>Per-frame inputs of a world layer; all data is immutable or owned by the match.</summary>
internal readonly record struct WorldFrame(LiveMatch Match, RenderFrame Objects, CommandList CommandList, float DeltaSeconds, double TotalSeconds);

/// <summary>
/// One slice of the 3D world (terrain, objects, effects). A layer creates its GPU resources in <see cref="Attach"/>, adds its entities to
/// <see cref="RenderServices.Scene"/> and refreshes them in <see cref="Update"/> once per rendered frame. It reads the match, never changes it.
/// </summary>
internal interface IWorldLayer : IDisposable
{
    string Name { get; }

    void Attach(RenderServices services, LiveMatch match);

    void Update(in WorldFrame frame);
}
