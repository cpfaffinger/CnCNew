using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Objects;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <param name="MapPath">Virtual path of the <c>.map</c> file.</param>
/// <param name="HumanFaction">Faction name of the human slot (player 1).</param>
/// <param name="Opponents">One computer slot per entry, in order (players 2..).</param>
public sealed record MatchSetup(string MapPath, string HumanFaction, ImmutableArray<string> Opponents, SkirmishMatchOptions Options)
{
    /// <summary>0 keeps the seeds of <see cref="Options"/>.</summary>
    public int Seed { get; init; }
}

/// <summary>
/// A running skirmish with everything the presentation needs around it: the session (replayable), projected frames, effects, fog, camera
/// and minimap. <see cref="Advance"/> is called once per rendered frame; it steps the simulation at 30 Hz and keeps every derived layer in
/// sync. No Stride types here, so the whole loop runs headless in tests.
/// </summary>
public sealed class LiveMatch
{
    private const double TickSeconds = 1.0 / SimulationTiming.LogicFramesPerSecond;
    private const int MaxTicksPerFrame = 8;

    private readonly FrameProjector _projector;
    private readonly SimulationFxWorldView _fxView;
    private readonly List<SoundCue> _soundCues = [];
    private double _accumulator;

    private LiveMatch(GameData data, MatchSetup setup, SkirmishMatchSession session, TerrainMap terrain, MapData map, TerrainRenderModel terrainModel,
        TerrainHeightSampler heights, RtsCamera camera, FogOfWarGrid fog, MinimapImage minimap, FrameProjector projector, FxExecutor fx, TeamColors colors)
    {
        Data = data;
        Setup = setup;
        Session = session;
        Terrain = terrain;
        Map = map;
        TerrainModel = terrainModel;
        Heights = heights;
        Camera = camera;
        Fog = fog;
        Minimap = minimap;
        _projector = projector;
        Fx = fx;
        Colors = colors;
        _fxView = new SimulationFxWorldView(session.World);
        Frame = RenderFrame.Empty;
    }

    public GameData Data { get; }
    public MatchSetup Setup { get; }
    public SkirmishMatchSession Session { get; }
    public SimulationWorld World => Session.World;
    public TerrainMap Terrain { get; }
    public MapData Map { get; }
    public TerrainRenderModel TerrainModel { get; }
    public TerrainHeightSampler Heights { get; }
    public RtsCamera Camera { get; }
    public FogOfWarGrid Fog { get; }
    public MinimapImage Minimap { get; }
    public FxExecutor Fx { get; }
    public TeamColors Colors { get; }
    public IFxWorldView FxView => _fxView;

    /// <summary>The human player (slot 0 of the setup).</summary>
    public PlayerId Human => new(1);

    public int HumanVisionSlot => World.VisionSlotOf(Human);
    public bool Paused { get; set; }

    /// <summary>Simulation speed factor: 1 is real time.</summary>
    public double Speed { get; set; } = 1;

    public RenderFrame Frame { get; private set; }
    public long Tick => World.Tick;

    /// <summary>Sound cues of the effects executed since the last call.</summary>
    public ImmutableArray<SoundCue> DrainSoundCues()
    {
        var result = ImmutableArray.CreateRange(_soundCues);
        _soundCues.Clear();
        return result;
    }

    public static LiveMatch Create(GameData data, MatchSetup setup, Vector2 viewport)
    {
        var graph = data.Graph;
        MapData map;
        using (var stream = data.Content.OpenRead(setup.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, setup.MapPath);

        MatchParticipant Find(string name) => data.Factions.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown or unplayable faction '{name}'.");
        var slots = ImmutableArray.CreateBuilder<SkirmishSlot>();
        slots.Add(new SkirmishSlot(Find(setup.HumanFaction), IsHuman: true));
        foreach (var opponent in setup.Opponents) slots.Add(new SkirmishSlot(Find(opponent)));
        var options = setup.Options;
        if (setup.Seed != 0) options = options with { AiSeed = unchecked((uint)setup.Seed), WorldSeed = unchecked((uint)setup.Seed) ^ 0xC0FFEE06 };
        var session = SkirmishMatchSession.Start(options, data.Templates, data.Fingerprint, terrain, map.Objects, slots.ToImmutable());

        var renderOptions = TerrainRenderOptions.FromDefinitions(graph);
        var lookup = new TerrainTextureLookup(TerrainTextureCatalog.FromDefinitions(graph), path =>
        {
            if (!data.Content.Exists(path)) return null;
            using var stream = data.Content.OpenRead(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
        var model = TerrainRenderModel.Build(map, lookup, renderOptions, setup.MapPath);
        var heights = new TerrainHeightSampler(map.HeightField);
        var camera = new RtsCamera(RtsCameraSettings.FromDefinitions(graph), heights, viewport);
        var start = terrain.StartPositions[0];
        camera.InitializeForMap(new Vector2(start.X.Raw / 65536f, start.Y.Raw / 65536f));
        var fog = FogOfWarGrid.ForVision(session.World.Vision!, FogSettings.FromDefinitions(graph, 510f));
        var water = renderOptions.Water?.RadarColor ?? new Rgba32(100, 120, 140, 255);
        var minimap = MinimapImage.Build(model, heights, terrain, water, 256);
        var colors = TeamColors.Resolve(session.Players, graph);
        var scenery = StaticScenery.Build(map.Objects, data.Visuals, heights.HeightAt).Items;
        var projector = new FrameProjector(data.Visuals, new FrameProjectorOptions { Viewer = new PlayerId(1), Colors = colors, Scenery = scenery });
        var fx = new FxExecutor(data.Fx, options.WorldSeed, new ParticleBudget());
        var live = new LiveMatch(data, setup, session, terrain, map, model, heights, camera, fog, minimap, projector, fx, colors);
        live.SyncAfterTick(default);
        live.Fog.Snap();
        live.Frame = projector.Project(1f);
        return live;
    }

    /// <summary>Steps the simulation for <paramref name="seconds"/> of wall time (respecting pause and speed) and refreshes every derived layer.</summary>
    public void Advance(double seconds)
    {
        if (!Paused && !Session.IsOver)
        {
            _accumulator += seconds * Speed;
            var steps = 0;
            while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame && !Session.IsOver)
            {
                SyncAfterTick(Session.Step());
                _accumulator -= TickSeconds;
                steps++;
            }
            if (steps == MaxTicksPerFrame) _accumulator = Math.Min(_accumulator, TickSeconds);
        }
        Fog.Advance((float)seconds);
        Frame = _projector.Project(Paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1));
    }

    private void SyncAfterTick(TickResult result)
    {
        _projector.Advance(World);
        _fxView.Refresh();
        if (result.Events is not null) Fx.ExecuteEvents(result.Events, _fxView);
        Fx.Update(_fxView);
        _soundCues.AddRange(Fx.DrainSoundCues());
        if (World.Tick % SimulationWorld.VisionIntervalTicks == 0) Fog.Update(World.Vision!, HumanVisionSlot);
    }
}
