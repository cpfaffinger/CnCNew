using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Objects;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <summary>
/// A running game with everything the presentation needs around the simulation: projected frames, effects, fog, camera and minimap. The simulation behind
/// it is a skirmish (<see cref="LiveMatch"/>) or a campaign mission (<see cref="LiveMission"/>); the HUD (<c>MatchController</c>), the audio director and the
/// renderer only see this class. <see cref="Advance"/> is called once per rendered frame; it steps the simulation at 30 Hz and keeps every derived layer in
/// sync. No Stride types here, so the whole loop runs headless in tests.
/// </summary>
public abstract class LiveGame
{
    private const double TickSeconds = 1.0 / SimulationTiming.LogicFramesPerSecond;
    private const int MaxTicksPerFrame = 8;

    private readonly FrameProjector _projector;
    private readonly SimulationFxWorldView _fxView;
    private readonly List<SoundCue> _soundCues = [];
    private double _accumulator;

    /// <param name="world">The simulation world the presentation reads.</param>
    /// <param name="players">The players the house colors are resolved for.</param>
    /// <param name="human">The viewer: whose fog, selection and orders the presentation shows.</param>
    /// <param name="cameraStart">Where the camera starts (world x/y).</param>
    protected LiveGame(GameData data, SimulationWorld world, MapData map, string mapPath, TerrainMap terrain, IEnumerable<PlayerConfiguration> players, PlayerId human,
        uint fxSeed, Vector2 viewport, Vector2 cameraStart)
    {
        var graph = data.Graph;
        Data = data;
        World = world;
        Terrain = terrain;
        Map = map;
        Human = human;

        var renderOptions = TerrainRenderOptions.FromDefinitions(graph);
        var lookup = new TerrainTextureLookup(TerrainTextureCatalog.FromDefinitions(graph), path =>
        {
            if (!data.Content.Exists(path)) return null;
            using var stream = data.Content.OpenRead(path);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
        TerrainModel = TerrainRenderModel.Build(map, lookup, renderOptions, mapPath);
        Heights = new TerrainHeightSampler(map.HeightField);
        Camera = new RtsCamera(RtsCameraSettings.FromDefinitions(graph), Heights, viewport);
        Camera.InitializeForMap(cameraStart);
        Fog = FogOfWarGrid.ForVision(world.Vision!, FogSettings.FromDefinitions(graph, 510f));
        var water = renderOptions.Water?.RadarColor ?? new Rgba32(100, 120, 140, 255);
        Minimap = MinimapImage.Build(TerrainModel, Heights, terrain, water, 256);
        Colors = TeamColors.Resolve(players, graph);
        var scenery = StaticScenery.Build(map.Objects, data.Visuals, Heights.HeightAt).Items;
        _projector = new FrameProjector(data.Visuals, new FrameProjectorOptions { Viewer = human, Colors = Colors, Scenery = scenery, SelectionFlash = SelectionFlashSettings.FromDefinitions(graph) });
        Fx = new FxExecutor(data.Fx, fxSeed, new ParticleBudget());
        _fxView = new SimulationFxWorldView(world);
        Frame = RenderFrame.Empty;
        SyncAfterTick(default);
        Fog.Snap();
        Frame = _projector.Project(1f);
    }

    public GameData Data { get; }
    public SimulationWorld World { get; }
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

    /// <summary>The viewer: slot 0 of a skirmish, the local side of a mission.</summary>
    public PlayerId Human { get; }

    public int HumanVisionSlot => World.VisionSlotOf(Human);
    public bool Paused { get; set; }

    /// <summary>Simulation speed factor: 1 is real time.</summary>
    public double Speed { get; set; } = 1;

    public RenderFrame Frame { get; private set; }

    /// <summary>
    /// Where the human is about to place a structure (the HUD sets it every frame while the placement mode is on, null otherwise): <see cref="ObjectFrame"/> then carries
    /// the translucent building at that spot. It never enters <see cref="Frame"/>, so nothing that reads the frame for objects (minimap dots, heal icons, picking) sees it.
    /// </summary>
    public PlacementGhost? PlacementGhost { get; set; }

    /// <summary>
    /// The frame the object layer draws: <see cref="Frame"/> plus the placement preview (<see cref="CnCNew.Presentation.Objects.PlacementPreview"/>) of
    /// <see cref="PlacementGhost"/>, if there is one and its template has a model.
    /// </summary>
    public RenderFrame ObjectFrame
        => PlacementGhost is { } ghost && PlacementPreview.Create(Data.Visuals, ghost, Human, Colors) is { } preview
            ? Frame with { Drawables = Frame.Drawables.Add(preview) }
            : Frame;
    public long Tick => World.Tick;

    /// <summary>Faction name of the viewer's player (selects the control bar scheme and the Eva voice).</summary>
    public abstract string HumanFaction { get; }

    /// <summary>The simulation has reached its end (a skirmish winner, a mission outcome and its end-game delay).</summary>
    public abstract bool IsOver { get; }

    /// <summary>The winner once <see cref="IsOver"/>; <see langword="null"/> while running or when nobody (or the viewer's enemy side only) won.</summary>
    public abstract PlayerId? Winner { get; }

    /// <summary>Every player of the game, for the end statistics.</summary>
    public abstract ImmutableArray<PlayerConfiguration> Players { get; }

    /// <summary>
    /// Command sink of the human player: everything the human changes in the world goes through it, so the simulation's recording (a skirmish's replay, a mission's
    /// per-player sequence counter) sees every order. Created lazily; one instance per game.
    /// </summary>
    public ICommandSink Commands => _observedCommands ??= new ObservedCommandSink(this, CreateCommandSink());

    /// <summary>The sink of the simulation behind this game (a skirmish session, a mission session, a staged scene); called once.</summary>
    protected abstract ICommandSink CreateCommandSink();

    /// <summary>Raised for every order the human submitted through <see cref="Commands"/>, with the simulation's answer (accepted, or why not): for sound, diagnostics and the client's input replay report.</summary>
    public event Action<CnCNew.Simulation.Commands.SimCommand, CnCNew.Simulation.Commands.SubmitResult>? CommandIssued;

    private ICommandSink? _observedCommands;

    private sealed class ObservedCommandSink(LiveGame game, ICommandSink inner) : ICommandSink
    {
        public CnCNew.Simulation.Commands.SubmitResult Issue(Func<ulong, long, CnCNew.Simulation.Commands.SimCommand> make)
        {
            CnCNew.Simulation.Commands.SimCommand? made = null;
            var result = inner.Issue((sequence, tick) => made = make(sequence, tick));
            if (made is not null) game.CommandIssued?.Invoke(made, result);
            return result;
        }
    }

    /// <summary>Invoked immediately before every simulation step of <see cref="Advance"/> (per-tick input bookkeeping).</summary>
    public Action<SimulationWorld>? BeforeTick { get; set; }

    /// <summary>Raised after every step with its result (events for statistics, ticker and sound).</summary>
    public event Action<TickResult>? TickStepped;

    /// <summary>Sound cues of the effects executed since the last call.</summary>
    public ImmutableArray<SoundCue> DrainSoundCues()
    {
        var result = ImmutableArray.CreateRange(_soundCues);
        _soundCues.Clear();
        return result;
    }

    /// <summary>One logic tick of the simulation behind this game.</summary>
    protected abstract TickResult StepSimulation();

    /// <summary>Steps the simulation for <paramref name="seconds"/> of wall time (respecting pause and speed) and refreshes every derived layer.</summary>
    public void Advance(double seconds)
    {
        if (!Paused && !IsOver)
        {
            _accumulator += seconds * Speed;
            var steps = 0;
            while (_accumulator >= TickSeconds && steps < MaxTicksPerFrame && !IsOver)
            {
                BeforeTick?.Invoke(World);
                var result = StepSimulation();
                SyncAfterTick(result);
                TickStepped?.Invoke(result);
                _accumulator -= TickSeconds;
                steps++;
            }
            if (steps == MaxTicksPerFrame) _accumulator = Math.Min(_accumulator, TickSeconds);
        }
        Fog.Advance((float)seconds);
        OnAdvanced(Paused ? 0 : seconds * Speed);
        Frame = _projector.Project(Paused ? 1f : (float)Math.Clamp(_accumulator / TickSeconds, 0, 1));
    }

    /// <summary>Called once per <see cref="Advance"/> after the ticks, before the frame is projected, with the game time that passed (0 while paused); a mission plays its scripted camera here.</summary>
    protected virtual void OnAdvanced(double gameSeconds) { }

    private void SyncAfterTick(TickResult result)
    {
        if (result.Events is not null) _projector.Observe(result.Events, World);
        _projector.Advance(World);
        _fxView.Refresh();
        if (result.Events is not null) Fx.ExecuteEvents(result.Events, _fxView);
        Fx.Update(_fxView);
        _soundCues.AddRange(Fx.DrainSoundCues());
        if (World.Tick % SimulationWorld.VisionIntervalTicks == 0) Fog.Update(World.Vision!, HumanVisionSlot);
    }
}
