using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Presentation;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Objects;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <summary>
/// A running game with everything the presentation needs around the simulation: projected frames, effects, fog, camera and minimap. The simulation behind
/// it is a skirmish (<see cref="LiveMatch"/>) or a campaign mission (<see cref="LiveMission"/>); the HUD (<c>MatchController</c>), the audio director and the
/// renderer only see this class. <see cref="Advance"/> is called once per rendered frame; it steps the simulation at the rate of its world and keeps every derived layer in
/// sync. No Stride types here, so the whole loop runs headless in tests.
/// </summary>
public abstract class LiveGame
{
    /// <summary>
    /// How much game time one <see cref="Advance"/> may catch up at most, in original frames (0.27 s): a time budget, so a slow frame costs the same game time at every
    /// tick rate. What is left over is dropped down to one tick.
    /// </summary>
    private const int MaxCatchUpFrames = 8;

    private readonly double _tickSeconds;
    private readonly long _maxTicksPerFrame;

    /// <summary>
    /// Whether the load reads the armies of the sides that play as well as what stands on the map (<see cref="PreloadedModels"/>). The scene behind the menus has nobody who builds, and
    /// reading every army there held the menu back for seconds after the intro (the original reads nothing ahead unless <c>m_preloadAssets</c> is set).
    /// </summary>
    public bool PreloadsArmies { get; set; } = true;

    private FrameProjector _projector;
    private readonly FrameProjectorOptions _projectorOptions;
    private readonly SimulationFxWorldView _fxView;
    private readonly List<SoundCue> _soundCues = [];

    /// <summary>The announcer's superweapon detected and ready events (<see cref="CnCNew.Game.Hud.SuperweaponAnnouncer"/>); a mission sets what its scripts hid.</summary>
    public CnCNew.Game.Hud.SuperweaponAnnouncer SuperweaponAnnouncements { get; } = new();
    private ITickDriver _driver;
    private long _beforeTickDoneFor = -1;
    private double _fraction;
    private IPresentationPhaseObserver? _phaseObserver;

    /// <param name="world">The simulation world the presentation reads.</param>
    /// <param name="players">The players the house colors are resolved for.</param>
    /// <param name="human">The viewer: whose fog, selection and orders the presentation shows.</param>
    /// <param name="cameraStart">Where the camera starts (world x/y).</param>
    private MapEnvironment? _environment;

    /// <summary>The <c>map.ini</c> beside the map, if it has one.</summary>
    private string? ReadMapIni()
    {
        var path = System.IO.Path.GetDirectoryName(MapPath)?.Replace('\\', '/') + "/map.ini";
        if (!Data.Content.Exists(path)) return null;
        using var reader = new StreamReader(Data.Content.OpenRead(path));
        return reader.ReadToEnd();
    }

    /// <param name="houseColors">The colour of each player by id where the match names one (a skirmish slot's multiplayer colour); the others are resolved by <see cref="TeamColors"/>.</param>
    protected LiveGame(GameData data, SimulationWorld world, MapData map, string mapPath, TerrainMap terrain, IEnumerable<PlayerConfiguration> players, PlayerId human,
        uint fxSeed, Vector2 viewport, Vector2 cameraStart, IReadOnlyDictionary<int, CnCNew.Data.Definitions.ColorValue>? houseColors = null)
    {
        MapPath = mapPath;
        var graph = data.Graph;
        Data = data;
        World = world;
        Terrain = terrain;
        Map = map;
        Human = human;
        // One tick of this world in seconds, and the catch-up budget in its ticks: both follow the rate of the world, never a constant of the game layer.
        _tickSeconds = 1.0 / world.Time.TicksPerSecond;
        _driver = new LocalTickDriver(_tickSeconds);
        _maxTicksPerFrame = world.Time.OriginalFrames(MaxCatchUpFrames);

        var renderOptions = TerrainRenderOptions.ForMap(graph, map);
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
        Camera = new RtsCamera(RtsCameraSettings.FromDefinitions(graph), Heights, viewport) { Decks = world.Bridges };
        // The owner's wish (INPUT-03): half as far again as MaxCameraHeight of the data. A modern screen shows more of the map than the original's did at this height.
        Camera.SetMaxHeightAboveGround(Camera.MaxHeightAboveGround * 1.5f);
        Camera.InitializeForMap(cameraStart);
        Fog = FogOfWarGrid.ForVision(world.Vision!, FogSettings.FromDefinitions(graph, 510f));
        var water = renderOptions.Water?.RadarColor ?? new Rgba32(100, 120, 140, 255);
        Minimap = MinimapImage.Build(TerrainModel, Heights, terrain, water, 256, bridges: WorkingBridges(world));
        Colors = TeamColors.Resolve(players, graph, houseColors);
        var scenery = _scenery = StaticScenery.Build(map.Objects, data.Visuals, Heights.HeightAt).Items;
        FogViewer = human;
        _projectorOptions = new FrameProjectorOptions { Viewer = human, Colors = Colors, Scenery = scenery, ModelBone = data.ModelBones.Find, PosedBone = data.ModelBones.FindPosed, SelectionFlash = SelectionFlashSettings.FromDefinitions(graph), InterpolateValues = InterpolateValues };
        _projector = new FrameProjector(data.Visuals, _projectorOptions);
        Fx = new FxExecutor(data.Fx, world.Time, fxSeed, new ParticleBudget());
        Attached = new AttachedEffects(data.AttachedEffects, Fx, data.ModelBones.Find, fxSeed) { PosedBones = data.ModelBones.FindPosed, DeployedBones = data.ModelBones.FindAtFinalFrame };
        Fx.RequestPlace = Attached.UplinkLaunchPlace;
        Fx.ProjectileStreams = () => Attached.ProjectileStreams;
        Fx.Ropes = () => Attached.Ropes;
        Fx.ObjectDecals = () => Attached.Decals;
        Treads = new TreadMarks(new TreadSettings(), data.ModelBones.Find) { Viewer = human };
        Fx.TreadMarks = Treads.Snapshot;
        Attached.Viewer = human;
        // Bones and muzzles are those of the object's current model (the attached effects know which state every object shows).
        _fxView = new SimulationFxWorldView(world,
            (entity, bone) => _fxView!.GetPose(entity) is { } pose ? Attached.BonePosition(entity, bone, pose) : null,
            (entity, _) => _fxView!.GetPose(entity) is { } pose ? Attached.MuzzlePosition(entity, world.FindEntity(entity)?.CurrentWeaponSlot ?? 0, pose) : null,
            (entity, _) => _fxView!.GetPose(entity) is { } pose ? Attached.MuzzleTransform(entity, world.FindEntity(entity)?.CurrentWeaponSlot ?? 0, pose) : null);
        _fxView.DrawnPlacement = entity => _projector.TryGetDependencyPlacement(entity, out var position, out var yaw) ? (position, yaw) : null;
        Frame = RenderFrame.Empty;
        SyncAfterTick(default);
        Fog.Snap();
        Frame = _projector.Project(1f);
    }

    /// <summary><c>W3DRadar::buildTerrainTexture</c>'s "working bridge": its object is there and is no rubble.</summary>
    private static List<MinimapBridge> WorkingBridges(SimulationWorld world)
    {
        var bridges = new List<MinimapBridge>();
        foreach (var bridge in world.Bridges)
            if (bridge.DamageState != CnCNew.Simulation.Modules.BuiltIn.BodyDamageStates.Rubble && bridge.ObjectId.IsValid && world.Templates.Bridges.TryGet(bridge.TemplateName, out var block))
                bridges.Add(new MinimapBridge(bridge, new Rgba32(block.RadarColor.Red, block.RadarColor.Green, block.RadarColor.Blue, 255)));
        return bridges;
    }

    public GameData Data { get; }
    public SimulationWorld World { get; }
    public TerrainMap Terrain { get; }

    /// <summary>
    /// Whether the games created from now on draw health, construction progress, wheel travel and chassis pose on their way between two ticks
    /// (<see cref="FrameProjectorOptions.InterpolateValues"/>); the client's <c>--no-value-interpolation</c> turns it off.
    /// </summary>
    public static bool InterpolateValues { get; set; } = true;

    /// <summary>
    /// When the next tick of a match of a server is late, moving objects are drawn on along their last step for up to one tick instead of standing still until the
    /// tick arrives (<see cref="FrameProjector.Project"/>, docs/PRESENTATION-OBJECTS.md section 12). Off by default: measured, it has next to nothing to act on
    /// (a tick was late in 0 to 5 of 7,178 frames) and changes nothing that was measured (docs/MULTIPLAYER-FINDINGS.md section 3). The client's
    /// <c>--extrapolate</c> turns it on.
    /// </summary>
    public static bool Extrapolate { get; set; }
    public MapData Map { get; }

    /// <summary>The virtual path of the <c>.map</c> file.</summary>
    public string MapPath { get; }

    /// <summary>The sky and the weather of this map (<see cref="MapEnvironment"/>), built when first asked for.</summary>
    public MapEnvironment Environment => _environment ??= MapEnvironment.FromDefinitions(Data.Graph, ReadMapIni());

    /// <summary>A script has asked for the sky box (<c>DRAW_SKYBOX_BEGIN</c>); <c>W3DDisplay::reset</c> turns it off for every map.</summary>
    public virtual bool SkyboxShown => false;

    /// <summary>The map's border is black (<c>ENABLE_BORDER_SHROUD</c>, the start of every map).</summary>
    public virtual bool BorderShroudShown => true;

    /// <summary>Snow and rain are shown if the map has them (<c>SHOW_WEATHER</c>, <c>SnowManager::setVisible</c>).</summary>
    public virtual bool WeatherShown => true;
    /// <summary>The terrain as drawn; a building that flattens its site makes new chunks of it (<see cref="TakeChangedTerrainChunks"/>).</summary>
    public TerrainRenderModel TerrainModel { get; private set; }
    public TerrainHeightSampler Heights { get; }
    public RtsCamera Camera { get; }
    public FogOfWarGrid Fog { get; }
    public MinimapImage Minimap { get; }
    public FxExecutor Fx { get; }

    /// <summary>The continuous effects of the objects shown: model-state particle systems, the body's fire and smoke, bone effects, damage transitions, missile exhaust.</summary>
    public AttachedEffects Attached { get; }

    /// <summary>The track marks of the vehicles (<c>W3DTerrainTracks</c>).</summary>
    public TreadMarks Treads { get; }
    public TeamColors Colors { get; }
    public IFxWorldView FxView => _fxView;

    /// <summary>
    /// Whether the minimap is drawn and takes clicks (<c>W3DLeftHUDDraw</c>, <c>LeftHUDInput</c>): the radar forced on by a mission, or not hidden and the viewer has a radar
    /// (<c>Player::hasRadar</c>). A spectator's own minimap is not ruled by it.
    /// </summary>
    public bool RadarOn => this is LiveMission { Director: var director } ? director.RadarForced || (!director.RadarHidden && World.HasRadar(Human)) : World.HasRadar(Human);

    /// <summary>The viewer: slot 0 of a skirmish, the local side of a mission.</summary>
    public PlayerId Human { get; }

    public int HumanVisionSlot => World.VisionSlotOf(Human);

    /// <summary>
    /// Whose fog of war the presentation shows: <see cref="Human"/> in every game a player plays; a spectator may look through another player's eyes or, with
    /// <see langword="null"/>, see everything (<see cref="ShowWorldAs"/>). Presentation only: the simulation's vision is not touched.
    /// </summary>
    public PlayerId? FogViewer { get; private set; }

    /// <summary>
    /// A spectator's switch of the view (docs/SPECTATOR-MODE.md): the objects, the remembered buildings and the fog are those of <paramref name="viewer"/>, or the whole
    /// world without fog for <see langword="null"/>. What the previous view remembered (ghosts of buildings, explored scenery) is dropped with it.
    /// </summary>
    public void ShowWorldAs(PlayerId? viewer)
    {
        if (viewer == FogViewer) return;
        FogViewer = viewer;
        Treads.Viewer = viewer;
        _projector = new FrameProjector(Data.Visuals, _projectorOptions with { Viewer = viewer }) { PhaseObserver = _phaseObserver };
        _projector.Advance(World);
        if (viewer is { } player) Fog.Update(World.Vision!, World.VisionSlotOf(player));
        else
        {
            var clear = new FogCellState[Fog.CellsX * Fog.CellsY];
            Array.Fill(clear, FogCellState.Visible);
            Fog.SetStates(clear);
        }
        Fog.Snap();
        Frame = _projector.Project(1f);
    }

    /// <summary>
    /// Who releases the ticks (<see cref="ITickDriver"/>): locally the wall-time accumulator. Replaced by a host that takes its ticks from somewhere else; pause and speed
    /// asked for so far go to the new driver as requests.
    /// </summary>
    public ITickDriver TickDriver
    {
        get => _driver;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            value.RequestPause(_driver.Paused);
            value.RequestSpeed(_driver.Speed);
            _driver = value;
        }
    }

    /// <summary>Whether the game stands still. Setting it is a request to the tick driver; reading it gives what the driver granted.</summary>
    public bool Paused
    {
        get => _driver.Paused;
        set => _driver.RequestPause(value);
    }

    /// <summary>Simulation speed factor: 1 is real time. Setting it is a request to the tick driver.</summary>
    public double Speed
    {
        get => _driver.Speed;
        set => _driver.RequestSpeed(value);
    }

    /// <summary>
    /// The clock of the picture's own motion - model animations (a radar dish, a flag), the scroll of water and of animated textures, wheels and their shadows. It runs on real time but
    /// stands still while the game is paused: <c>W3DDisplay::draw</c> stops W3D's sync time when <c>GameLogic::isGamePaused</c>, and everything W3D animates follows that time.
    /// </summary>
    public double AnimationSeconds { get; private set; }

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
    {
        get
        {
            var frame = PlacementGhost is { } ghost && PlacementPreview.Create(Data.Visuals, ghost, Human, Colors) is { } preview
                ? Frame with { Drawables = Frame.Drawables.Add(preview) }
                : Frame;
            if (RallyMarkers.IsDefaultOrEmpty) return frame;
            _downwindAngle ??= PlacementPreview.DownwindAngle(Data.Graph);
            var markers = RallyMarkers.Select(place => PlacementPreview.RallyMarker(Data.Visuals, place, _downwindAngle.Value, Human, Colors)).OfType<CnCNew.Presentation.Objects.DrawableInstance>();
            return frame with { Drawables = frame.Drawables.AddRange(markers) };
        }
    }

    /// <summary>The rally points of the selected factories (<c>ControlBar::showRallyPoint</c>), set by the HUD.</summary>
    public ImmutableArray<Vector3> RallyMarkers { get; set; } = [];

    private float? _downwindAngle;
    public long Tick => World.Tick;

    /// <summary>
    /// The presentation clock: game time from the world's tick, its rate and the interpolation fraction of the last <see cref="Advance"/>. It stands while paused and
    /// runs with <see cref="Speed"/>. Everything that shows or sounds for a duration reads this (or the tick with <c>World.Time</c>), never a rate of its own.
    /// </summary>
    public GameClock Clock => GameClock.Of(World, _fraction);

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
    public ICommandSink Commands => _commands ??= CreateCommandSink();

    /// <summary>The sink the viewer's input is given: the viewer's end of <see cref="CommandPath"/>. A game nobody plays overrides it with one that refuses.</summary>
    protected virtual ICommandSink CreateCommandSink() => CommandPath.For(Human);

    /// <summary>
    /// The two-stage command path of this game (docs/PRESENTATION-INPUT.md, "Command intent, pre-check, result"): orders are handed over as intents, pre-checked at once and
    /// submitted to the simulation immediately before the step of the tick their scheduler gave them. One path for a skirmish, a mission and a staged scene.
    /// </summary>
    public CommandPath CommandPath
    {
        get
        {
            if (_commandPath is not null) return _commandPath;
            _commandPath = new CommandPath(World, IssueNow);
            _commandPath.PreChecked += (command, receipt) => CommandPreChecked?.Invoke(command, receipt);
            _commandPath.Resolved += resolution =>
            {
                // A bundle carries the commands of every player; the viewer's display and statistics hang on the viewer's own.
                if (resolution.Player != Human) return;
                CommandIssued?.Invoke(resolution.Command, resolution.Result);
                CommandResolved?.Invoke(resolution);
            };
            return _commandPath;
        }
    }

    /// <summary>
    /// The lockstep probe: every order of the viewer is executed <paramref name="ticks"/> ticks after it was given, as if it had travelled to a server and back (0: in the tick
    /// it was given, the default). Only while no order is on its way.
    /// </summary>
    public virtual void UseInputDelay(int ticks) => CommandPath.UseScheduler(new LocalCommandScheduler(ticks));

    /// <summary>The submission of the simulation behind this game: builds the command of <paramref name="player"/> at the current tick with that player's next sequence number and submits it.</summary>
    protected abstract CnCNew.Simulation.Commands.SubmitResult IssueNow(PlayerId player, Func<ulong, long, CnCNew.Simulation.Commands.SimCommand> make);

    /// <summary>
    /// Raised for every order of the viewer when it is submitted to the simulation, with the command as submitted and the final answer (accepted, or why not): for statistics,
    /// diagnostics and the client's input report. With no input delay that is the next step after the order was given.
    /// </summary>
    public event Action<CnCNew.Simulation.Commands.SimCommand, CnCNew.Simulation.Commands.SubmitResult>? CommandIssued;

    /// <summary>Raised when an order is given, with the command as the local pre-check saw it (no sequence number yet) and its receipt: what immediate display and sound hang on.</summary>
    public event Action<CnCNew.Simulation.Commands.SimCommand, CommandReceipt>? CommandPreChecked;

    /// <summary>Raised with the final answer to an order (<see cref="CommandResolution.Deviates"/>: it differs from the pre-check).</summary>
    public event Action<CommandResolution>? CommandResolved;

    private ICommandSink? _commands;
    private CommandPath? _commandPath;

    /// <summary>Invoked immediately before every simulation step of <see cref="Advance"/> (per-tick input bookkeeping).</summary>
    public Action<SimulationWorld>? BeforeTick { get; set; }

    /// <summary>
    /// A measuring tool's hook into <see cref="Advance"/> (docs/PERF-GAME-LAYER.md): told where each part begins and ends. Null in the game. It observes only.
    /// </summary>
    public IPresentationPhaseObserver? PhaseObserver
    {
        get => _phaseObserver;
        set
        {
            _phaseObserver = value;
            _projector.PhaseObserver = value;
        }
    }

    /// <summary>Raised after every step with its result (events for statistics, ticker and sound).</summary>
    public event Action<TickResult>? TickStepped;

    /// <summary>A mission script's colour flash of one drawable (<c>FrameProjector.FlashScripted</c>): presentation only, the simulation is not touched.</summary>
    public bool FlashDrawable(EntityId entity, Vector3? color, int seconds) => _projector.FlashScripted(entity, color, seconds, World);

    /// <summary>Takes another's beacon out of this machine's picture (<c>FrameProjector.HideBeacon</c>).</summary>
    public void HideBeacon(EntityId beacon) => _projector.HideBeacon(beacon);

    /// <summary>The viewer hid this beacon (<see cref="HideBeacon"/>).</summary>
    public bool IsBeaconHidden(EntityId beacon) => _projector.IsBeaconHidden(beacon);

    /// <summary>A mission script's house colour for one drawable (<c>FrameProjector.SetHouseColor</c>): presentation only.</summary>
    public bool SetDrawableHouseColor(EntityId entity, uint argb) => _projector.SetHouseColor(entity, argb, World);

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
        if (!IsOver)
        {
            _driver.AddTime(seconds);
            var steps = 0;
            // The driver releases the ticks: time must be due and the tick's command bundle present.
            while (steps < _maxTicksPerFrame && !IsOver && _driver.IsDue(World.Tick))
            {
                var observer = _phaseObserver;
                observer?.PhaseStarted(PresentationPhase.BeforeTick);
                // Once per tick, before the driver closes the tick: what the bookkeeping orders now still belongs to this tick's bundle. A tick that then has to wait
                // for its bundle does not get it a second time.
                if (_beforeTickDoneFor != World.Tick)
                {
                    _beforeTickDoneFor = World.Tick;
                    BeforeTick?.Invoke(World);
                }
                if (!_driver.TryReleaseTick(World.Tick, _commandPath?.CanStep ?? true))
                {
                    observer?.PhaseEnded(PresentationPhase.BeforeTick);
                    break;
                }
                // The commands of this tick are submitted immediately before its step, in the order of the bundle.
                _commandPath?.SubmitDue();
                observer?.PhaseEnded(PresentationPhase.BeforeTick);
                observer?.PhaseStarted(PresentationPhase.StepSimulation);
                var steppedTick = World.Tick;
                var result = StepSimulation();
                _driver.TickCompleted(steppedTick);
                observer?.PhaseEnded(PresentationPhase.StepSimulation);
                observer?.PhaseStarted(PresentationPhase.SyncAfterTick);
                SyncAfterTick(result);
                observer?.PhaseEnded(PresentationPhase.SyncAfterTick);
                observer?.PhaseStarted(PresentationPhase.TickStepped);
                TickStepped?.Invoke(result);
                observer?.PhaseEnded(PresentationPhase.TickStepped);
                steps++;
            }
            if (steps == _maxTicksPerFrame) _driver.LimitBacklog();
        }
        var frameObserver = _phaseObserver;
        frameObserver?.PhaseStarted(PresentationPhase.FogAdvance);
        Fog.Advance((float)seconds);
        frameObserver?.PhaseEnded(PresentationPhase.FogAdvance);
        frameObserver?.PhaseStarted(PresentationPhase.OnAdvanced);
        OnAdvanced(Paused ? 0 : seconds * Speed);
        frameObserver?.PhaseEnded(PresentationPhase.OnAdvanced);
        frameObserver?.PhaseStarted(PresentationPhase.Project);
        var alpha = Paused ? 1f : (float)_driver.Fraction;
        if (!Paused) _fraction = alpha;
        Frame = _projector.Project(alpha, Extrapolate && !Paused ? (float)_driver.Ahead : 0f);
        frameObserver?.PhaseEnded(PresentationPhase.Project);
        if (!Paused) AnimationSeconds += seconds;
    }

    /// <summary>Called once per <see cref="Advance"/> after the ticks, before the frame is projected, with the game time that passed (0 while paused); a mission plays its scripted camera here.</summary>
    protected virtual void OnAdvanced(double gameSeconds) { }

    private void SyncAfterTick(TickResult result)
    {
        var observer = _phaseObserver;
        observer?.PhaseStarted(PresentationPhase.ProjectorObserve);
        if (result.Events is not null) _projector.Observe(result.Events, World);
        observer?.PhaseEnded(PresentationPhase.ProjectorObserve);
        observer?.PhaseStarted(PresentationPhase.ProjectorAdvance);
        _projector.Advance(World);
        observer?.PhaseEnded(PresentationPhase.ProjectorAdvance);
        observer?.PhaseStarted(PresentationPhase.FxViewRefresh);
        _fxView.Refresh();
        if (result.Events is not null) Attached.Observe(result.Events, World);
        Attached.Update(World, _projector.Projected, _fxView, World.Tick / World.Time.TicksPerOriginalFrame);
        Treads.Update(World, _projector.Projected, _fxView);
        observer?.PhaseEnded(PresentationPhase.FxViewRefresh);
        observer?.PhaseStarted(PresentationPhase.FxExecuteEvents);
        if (result.Events is not null) Fx.ExecuteEvents(result.Events, _fxView);
        observer?.PhaseEnded(PresentationPhase.FxExecuteEvents);
        observer?.PhaseStarted(PresentationPhase.FxUpdate);
        // Effects are authored per original frame: their clock is game time on the 1/30 s grid, not the tick. At 30 Hz every tick begins a frame.
        if (World.Time.IsOriginalFrameBoundary(World.Tick))
        {
            Fx.Update(_fxView);
            Fx.TrackSounds(World, _fxView);
        }
        observer?.PhaseEnded(PresentationPhase.FxUpdate);
        observer?.PhaseStarted(PresentationPhase.DrainSoundCues);
        _soundCues.AddRange(Fx.DrainSoundCues());
        // The announcer's superweapon events that belong to the objects: a finished structure, a power that turned ready (once per original frame, like the timer list's draw).
        if (result.Events is not null) SuperweaponAnnouncements.Observe(result.Events, World, _soundCues);
        if (World.Time.IsOriginalFrameBoundary(World.Tick)) SuperweaponAnnouncements.Update(World, _soundCues);
        observer?.PhaseEnded(PresentationPhase.DrainSoundCues);
        observer?.PhaseStarted(PresentationPhase.FogUpdate);
        if (FogViewer is { } fogViewer && World.Tick % World.VisionInterval == 0) Fog.Update(World.Vision!, World.VisionSlotOf(fogViewer));
        observer?.PhaseEnded(PresentationPhase.FogUpdate);
        SyncSites();
    }

    private readonly SortedSet<int> _changedChunks = [];
    private ImmutableArray<SceneryItem> _scenery = [];
    private int _sitesSeen;

    /// <summary>
    /// Buildings begun since the last tick (<see cref="SimulationWorld.ClearedSites"/>): the trees, props and fluff their ground plan touches leave the map
    /// (<c>BuildAssistant::clearRemovableForConstruction</c>), and the ground that was levelled under them is drawn, seen by the camera and stood on by the objects as the simulation has it.
    /// </summary>
    private void SyncSites()
    {
        var sites = World.ClearedSites;
        if (sites.Count == _sitesSeen) return;
        for (var index = _sitesSeen; index < sites.Count; index++)
            foreach (var item in _scenery)
            {
                if (!ClearedByBuilding(item, out var radius)) continue;
                if (sites[index].Touches(FixedConvert.FromSingle(item.Position.X), FixedConvert.FromSingle(item.Position.Y), radius)) _projector.RemoveScenery(item.Index);
            }
        _sitesSeen = sites.Count;
        if (World.Terrain is not { } terrain) return;
        var heights = terrain.CopyRawHeights();
        Heights.ReplaceHeights(heights);
        TerrainModel = TerrainModel.Reshape(heights, out var changed);
        foreach (var chunk in changed) _changedChunks.Add(chunk);
    }

    /// <summary>
    /// Whether a scenery item is what <c>removeTreesAndPropsForConstruction</c> takes off the map, and the radius of the upright cylinder it counts as: a tree of the tree buffer is twice
    /// the buffer's tree radius of 7 wide (<c>W3DTreeBuffer::removeTreesForConstruction</c>), a prop twice the radius of its model (<c>W3DPropBuffer::removePropsForConstruction</c>; the template's
    /// bounding circle stands in for the model's sphere, which the simulation does not have).
    /// </summary>
    private bool ClearedByBuilding(SceneryItem item, out Fixed radius)
    {
        radius = Fixed.Zero;
        if (item.Visual.IsKindOf("OPTIMIZED_TREE"))
        {
            radius = Fixed.FromInt(14);
            return true;
        }
        if (!item.Visual.IsKindOf("PROP") && !item.Visual.IsKindOf("CLEARED_BY_BUILD")) return false;
        if (!World.Templates.TryGet(item.Visual.TemplateName, out var template)) return false;
        radius = template.BoundingCircleRadius * Fixed.FromInt(2);
        return true;
    }

    /// <summary>The chunks of <see cref="TerrainModel"/> that were made again since the last call, in ascending order; a layer that holds them as buffers replaces those.</summary>
    public ImmutableArray<int> TakeChangedTerrainChunks()
    {
        var chunks = _changedChunks.ToImmutableArray();
        _changedChunks.Clear();
        return chunks;
    }
}
