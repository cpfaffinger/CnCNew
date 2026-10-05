using CnCNew.Game.Data;
using CnCNew.Game.Live;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering.Lights;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>Owns the Stride scene of one running match: camera entity, sun, and the world layers.</summary>
internal sealed class LiveMatchHost : IDisposable
{
    private readonly CameraComponent _camera;
    private readonly IWorldLayer[] _layers;
    private double _total;

    public LiveMatchHost(global::Stride.Engine.Game game, GameData data, LiveGame match)
    {
        Match = match;
        Scene = new Scene();
        Services = new RenderServices(game.GraphicsDevice, game.GraphicsContext, Scene, data);
        game.SceneSystem.GraphicsCompositor = Services.CreateCompositor();

        _camera = new CameraComponent
        {
            Slot = game.SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId(),
            UseCustomViewMatrix = true,
            UseCustomProjectionMatrix = true,
        };
        Scene.Entities.Add(new Entity("Camera") { _camera });

        SceneLighting.Install(Scene, CnCNew.Presentation.Terrain.TerrainLighting.ObjectsFromDefinitions(data.Graph));

        _layers = WorldLayers.CreateAll();
        foreach (var layer in _layers) layer.Attach(Services, match);
        game.SceneSystem.SceneInstance = new SceneInstance(game.Services, Scene);
    }

    public LiveGame Match { get; }

    /// <summary>The minimap texture the terrain layer composes (terrain, fog, entity dots, camera footprint).</summary>
    public Texture MinimapTexture => _layers.OfType<CnCNew.Client.Stride.Layers.TerrainLayer>().Single().MinimapTexture;

    /// <summary>Gives the minimap the dots of what the viewer sees (the terrain layer would otherwise show terrain, fog and the camera only).</summary>
    public void UpdateMinimap(CommandList commandList, IReadOnlyList<CnCNew.Presentation.Minimap.MinimapDot> dots)
        => _layers.OfType<CnCNew.Client.Stride.Layers.TerrainLayer>().Single().UpdateMinimap(commandList, dots);
    public Scene Scene { get; }
    public RenderServices Services { get; }

    /// <summary>
    /// Advances the match by the frame time, then refreshes camera and layers. A HUD controller passes <paramref name="step"/> to advance the match and the camera itself
    /// (it applies its own camera intent and clocks); without one the camera just settles and the match advances.
    /// </summary>
    public void Update(GameTime gameTime, CommandList commandList, int width, int height, double? fixedSeconds = null, Action<double>? step = null)
    {
        var seconds = fixedSeconds ?? gameTime.Elapsed.TotalSeconds;
        _total += seconds;
        if (width > 0 && height > 0) Match.Camera.SetViewportSize(new System.Numerics.Vector2(width, height));
        if (step is null)
        {
            Match.Camera.Update((float)seconds, false);
            Match.Advance(seconds);
        }
        else step(seconds);
        _camera.ViewMatrix = Match.Camera.View.ToStride();
        _camera.ProjectionMatrix = Match.Camera.Projection.ToStride();
        var frame = new WorldFrame(Match, Match.ObjectFrame, commandList, (float)seconds, _total);
        foreach (var layer in _layers) layer.Update(frame);
    }

    public void Dispose()
    {
        foreach (var layer in _layers) layer.Dispose();
        Services.Dispose();
    }
}
