using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Per-frame inputs of a world layer; all data is immutable or owned by the match. <paramref name="DeltaSeconds"/> and <paramref name="TotalSeconds"/> are real time (the fog and the
/// minimap keep their pace in a pause); <paramref name="AnimationDeltaSeconds"/> and <paramref name="AnimationSeconds"/> are the clock of the picture's motion, which a pause stops
/// (<see cref="LiveGame.AnimationSeconds"/>): model animations, water, animated textures, wheels.
/// </summary>
internal readonly record struct WorldFrame(LiveGame Match, RenderFrame Objects, CommandList CommandList, float DeltaSeconds, double TotalSeconds, float AnimationDeltaSeconds, double AnimationSeconds);

/// <summary>
/// One slice of the 3D world (terrain, objects, effects). A layer creates its GPU resources in <see cref="Attach"/>, adds its entities to
/// <see cref="RenderServices.Scene"/> and refreshes them in <see cref="Update"/> once per rendered frame. It reads the match, never changes it.
/// </summary>
internal interface IWorldLayer : IDisposable
{
    string Name { get; }

    void Attach(RenderServices services, LiveGame match);

    void Update(in WorldFrame frame);
}
