using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Spectator;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Spectator;

/// <summary>The detail panel of the observer HUD (the StarCraft II observer's tabs are the model for what is shown, not for the look).</summary>
public enum SpectatorTab
{
    Production,
    Units,
    Losses,
    Economy,
}

/// <summary>What the spectator can do; every key and every button of the observer HUD is one of these.</summary>
public enum SpectatorAction
{
    TabProduction,
    TabUnits,
    TabLosses,
    TabEconomy,
    NextTab,
    FollowFirst,
    FollowSecond,
    FreeCamera,
    JumpFirst,
    JumpSecond,
    Overview,
    CycleFog,
    Pause,
    Speed1,
    Speed2,
    Speed4,
    Speed8,
    TogglePanels,
    Quit,
    /// <summary>A replay: back to the start of the recording.</summary>
    SeekStart,
    /// <summary>A replay: back to the previous checkpoint.</summary>
    SeekBack,
    /// <summary>A replay: on to the next checkpoint.</summary>
    SeekForward,
}

/// <param name="MaxZoomHeight">The spectator's zoom-out limit as a height above ground; <see langword="null"/>: the height from which the whole map is on screen.</param>
/// <param name="Speed">The game speed the match starts with (1 is real time).</param>
/// <param name="Seed">The seed of the match, shown in the HUD so a match can be repeated (0: the built-in seeds).</param>
public sealed record SpectatorOptions(float? MaxZoomHeight = null, double Speed = 1, long Seed = 0);

/// <summary>
/// The spectator of a skirmish between computer players (docs/SPECTATOR-MODE.md): nobody plays, the viewer watches. It owns the view (free camera with a zoom-out limit of
/// its own, a whole-map overview, following the simulated camera of a computer player), the fog switch (everything, or one player's view), the game speed and the observer
/// panels (<see cref="SpectatorStats"/>, drawn by <see cref="SpectatorHud"/>).
/// <para>
/// It reads the simulation and writes nothing to it: the orders of the computer players come from <see cref="CnCNew.Simulation.Skirmish.SkirmishMatchSession.RecordedCommands"/>,
/// the statistics from the events of every tick, and the match's own command sink refuses every order (<see cref="SpectatorCommandSink"/>). The camera scrolling of the
/// player's HUD is reused through <paramref name="hud"/>'s input controller, which only ever sees pointer moves, the scroll keys and the right and middle button here.
/// </para>
/// <para>Call order per rendered frame: <see cref="Process"/> for every input event, <see cref="LiveGame.Advance"/>, <see cref="Update"/>, <see cref="BuildDrawList"/>.</para>
/// </summary>
public sealed class SpectatorController
{
    /// <summary>The panels are rebuilt this often (original frames): six times a second is more than a reader needs.</summary>
    public const int PanelIntervalFrames = 5;

    /// <summary>A frame counts as "far zoom" for the cost report above this multiple of a player's highest camera.</summary>
    public const float FarZoomFactor = 2f;

    private static readonly (KeyCode Key, SpectatorAction Action)[] KeyTable =
    [
        (KeyCode.P, SpectatorAction.TabProduction), (KeyCode.U, SpectatorAction.TabUnits), (KeyCode.L, SpectatorAction.TabLosses), (KeyCode.E, SpectatorAction.TabEconomy),
        (KeyCode.Tab, SpectatorAction.NextTab),
        (KeyCode.F1, SpectatorAction.FollowFirst), (KeyCode.F2, SpectatorAction.FollowSecond), (KeyCode.F3, SpectatorAction.FreeCamera),
        (KeyCode.J, SpectatorAction.JumpFirst), (KeyCode.K, SpectatorAction.JumpSecond),
        (KeyCode.O, SpectatorAction.Overview), (KeyCode.F, SpectatorAction.CycleFog),
        (KeyCode.Space, SpectatorAction.Pause), (KeyCode.D1, SpectatorAction.Speed1), (KeyCode.D2, SpectatorAction.Speed2), (KeyCode.D4, SpectatorAction.Speed4), (KeyCode.D8, SpectatorAction.Speed8),
        (KeyCode.H, SpectatorAction.TogglePanels), (KeyCode.Escape, SpectatorAction.Quit),
        (KeyCode.Home, SpectatorAction.SeekStart), (KeyCode.PageUp, SpectatorAction.SeekBack), (KeyCode.PageDown, SpectatorAction.SeekForward),
    ];

    private readonly MatchController _hud;
    private readonly SpectatorHud _renderer;
    private readonly RtsCamera _probe;
    private readonly SortedDictionary<int, int> _commandsByPlayer = [];
    private readonly SortedSet<string> _tabsShown = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _fogModes = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _speeds = new(StringComparer.Ordinal);
    private readonly List<SimCommand> _tickCommands = [];
    private readonly Dictionary<(int Player, string Type), int> _tickRejections = [];
    private readonly Dictionary<(int Player, string Type), int> _tickOrders = [];
    private readonly SortedDictionary<int, int> _rejectedByPlayer = [];
    private int _commandCursor;
    private long _panelTick;
    private Vector2 _pointer;
    private bool _minimapPanning;
    private (Vector2 Pivot, float Height, float Pitch)? _beforeOverview;
    private Vector2 _overviewViewport;
    private float _overviewHeight;
    private int _fogIndex = -1;

    public SpectatorController(LiveMatch match, MatchController hud, SpectatorOptions? options = null, IUiTextMeasure? textMeasure = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(hud);
        if (!match.Setup.Spectator) throw new ArgumentException("The match was not created for a spectator (MatchSetup.Spectator).", nameof(match));
        Match = match;
        _hud = hud;
        Options = options ?? new SpectatorOptions();
        var time = match.World.Time;
        Stats = new SpectatorStats(time.TicksPerSecond);
        var starts = match.Terrain.StartPositions;
        Cameras = [..match.Players.Select((player, index) => new AiCamera(player.Id,
            new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(starts[index].X), CnCNew.Presentation.FixedConvert.ToSingle(starts[index].Y)), time.TicksPerOriginalFrame))];
        _probe = new RtsCamera(match.Camera.Settings, match.Heights, match.Camera.ViewportSize);
        _renderer = new SpectatorHud(match, hud.Assets.Images, textMeasure ?? new MonospaceTextMeasure());
        PlayerMaxHeight = match.Camera.Settings.MaxHeightAboveGround;

        match.Speed = Options.Speed > 0 ? Options.Speed : 1;
        NoteSpeed();
        ApplyZoomLimit();
        match.ShowWorldAs(null);
        _fogModes.Add(FogLabel);
        _tabsShown.Add(Tab.ToString());
        // The HUD of a player registered before this one: its per-tick bookkeeping stays, this adds the spectator's.
        match.TickStepped += OnTickStepped;
        Panels = Stats.Build(match.World, match.Players, match.Colors, match.Winner, match.IsOver);
        _panelTick = match.Tick;
    }

    public LiveMatch Match { get; }
    public SpectatorOptions Options { get; }
    public SpectatorStats Stats { get; }

    /// <summary>One simulated camera per player, in player order.</summary>
    public ImmutableArray<AiCamera> Cameras { get; }

    /// <summary>The panels as of the last rebuild (<see cref="PanelIntervalFrames"/>).</summary>
    public SpectatorPanels Panels { get; private set; }

    public SpectatorTab Tab { get; private set; } = SpectatorTab.Production;
    public bool PanelsVisible { get; private set; } = true;

    /// <summary>Index into <see cref="Cameras"/> of the simulated camera the main view follows, or null for the free camera.</summary>
    public int? Followed { get; private set; }

    /// <summary>The whole-map view is on (<see cref="SpectatorAction.Overview"/>).</summary>
    public bool Overview => _beforeOverview is not null;

    /// <summary>Whose fog is shown: null for everything, else the player.</summary>
    public PlayerId? FogViewer => _fogIndex < 0 ? null : Match.Players[_fogIndex].Id;

    public string FogLabel => FogViewer is { } viewer ? $"Player {viewer.Value}" : "All";

    public bool QuitRequested { get; private set; }

    /// <summary>A player's zoom-out limit (<c>MaxCameraHeight</c>) and the spectator's.</summary>
    public float PlayerMaxHeight { get; }
    public float SpectatorMaxHeight => Match.Camera.MaxHeightAboveGround;

    /// <summary>The highest camera height the view was at.</summary>
    public float HighestReached { get; private set; }

    /// <summary>The camera is further out than <see cref="FarZoomFactor"/> times a player's limit (the frames the cost report counts as far).</summary>
    public bool IsFarZoom => Match.Camera.HeightAboveGround > PlayerMaxHeight * FarZoomFactor;

    public int ActionsTaken { get; private set; }
    public int PanelRebuilds { get; private set; }
    public IReadOnlyDictionary<int, int> CommandsByPlayer => _commandsByPlayer;

    // ------------------------------------------------------------------------------------------------------------ input

    public void Process(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        switch (inputEvent)
        {
            case KeyEvent key:
                if (key.Key is KeyCode.Up or KeyCode.Down or KeyCode.Left or KeyCode.Right) Forward(key);
                else if (key is { IsDown: true, IsRepeat: false } && ActionOf(key.Key) is { } action) Do(action);
                return;
            case MouseMoveEvent move:
                _pointer = move.Position;
                if (_minimapPanning && _renderer.TryMinimapWorld(move.Position, out var panTo)) CenterFree(panTo);
                Forward(move);
                return;
            case MouseButtonEvent { Button: MouseButton.Left } button:
                _pointer = button.Position;
                if (!button.IsDown) { _minimapPanning = false; return; }
                if (_renderer.ActionAt(button.Position) is { } clicked) Do(clicked);
                else if (_renderer.TryMinimapWorld(button.Position, out var world)) { CenterFree(world); _minimapPanning = true; }
                return;
            case MouseButtonEvent button:
                // Right drag scrolls, middle drag rotates (the player's camera gestures); with nothing selected and the refusing sink a right click orders nothing.
                Forward(button);
                return;
            case MouseWheelEvent wheel:
                Match.Camera.SetHeightAboveGround(SpectatorCamera.WheelZoom(Match.Camera.HeightAboveGround, wheel.Notches));
                return;
            case FocusLostEvent:
                _minimapPanning = false;
                Forward(inputEvent);
                return;
        }
    }

    private void Forward(InputEvent inputEvent) => _hud.Input.Process(inputEvent, Match.World, Match.Camera);

    public static SpectatorAction? ActionOf(KeyCode key)
    {
        foreach (var (candidate, action) in KeyTable)
            if (candidate == key) return action;
        return null;
    }

    /// <summary>The key of an action as the HUD prints it ("F1", "Space", "P").</summary>
    public static string KeyHint(SpectatorAction action)
    {
        foreach (var (key, candidate) in KeyTable)
            if (candidate == action)
                return key switch
                {
                    >= KeyCode.D1 and <= KeyCode.D9 => ((char)('1' + (key - KeyCode.D1))).ToString(),
                    KeyCode.Escape => "Esc",
                    _ => key.ToString(),
                };
        return string.Empty;
    }

    public void Do(SpectatorAction action)
    {
        ActionsTaken++;
        switch (action)
        {
            case SpectatorAction.TabProduction: ShowTab(SpectatorTab.Production); break;
            case SpectatorAction.TabUnits: ShowTab(SpectatorTab.Units); break;
            case SpectatorAction.TabLosses: ShowTab(SpectatorTab.Losses); break;
            case SpectatorAction.TabEconomy: ShowTab(SpectatorTab.Economy); break;
            case SpectatorAction.NextTab: ShowTab((SpectatorTab)(((int)Tab + 1) % Enum.GetValues<SpectatorTab>().Length)); break;
            case SpectatorAction.FollowFirst: Follow(0); break;
            case SpectatorAction.FollowSecond: Follow(1); break;
            case SpectatorAction.FreeCamera: Followed = null; break;
            case SpectatorAction.JumpFirst: Jump(0); break;
            case SpectatorAction.JumpSecond: Jump(1); break;
            case SpectatorAction.Overview: ToggleOverview(); break;
            case SpectatorAction.CycleFog:
                _fogIndex = _fogIndex + 1 >= Match.Players.Length ? -1 : _fogIndex + 1;
                Match.ShowWorldAs(FogViewer);
                _fogModes.Add(FogLabel);
                break;
            case SpectatorAction.Pause: Match.Paused = !Match.Paused; NoteSpeed(); break;
            case SpectatorAction.Speed1: SetSpeed(1); break;
            case SpectatorAction.Speed2: SetSpeed(2); break;
            case SpectatorAction.Speed4: SetSpeed(4); break;
            case SpectatorAction.Speed8: SetSpeed(8); break;
            case SpectatorAction.TogglePanels: PanelsVisible = !PanelsVisible; break;
            case SpectatorAction.Quit: QuitRequested = true; break;
            case SpectatorAction.SeekStart: if (Replay is not null) SeekRequest = 0; break;
            case SpectatorAction.SeekBack: if (Replay is { } back) SeekRequest = back.PreviousCheckpoint(); break;
            case SpectatorAction.SeekForward: if (Replay is { } forward) SeekRequest = forward.NextCheckpoint(); break;
        }
    }

    /// <summary>The playback this spectator watches, or null for a match that is played by computer players now.</summary>
    public CnCNew.Game.Replays.ReplayPlayback? Replay => Match.Replay;

    /// <summary>
    /// The tick a seek key or button asked for, until the host takes it (<see cref="TakeSeekRequest"/>): the host seeks the playback, builds its scene for the new presentation
    /// and gives it a new controller that takes over this one's view (<see cref="TakeViewFrom"/>).
    /// </summary>
    public long? SeekRequest { get; private set; }

    public long? TakeSeekRequest()
    {
        var request = SeekRequest;
        SeekRequest = null;
        return request;
    }

    /// <summary>
    /// After a seek: the view of the controller of the previous presentation - tab, panels, the camera, which simulated camera is followed, the fog and the speed - goes on
    /// here, so that a seek moves the match and nothing else.
    /// </summary>
    public void TakeViewFrom(SpectatorController previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        Tab = previous.Tab;
        PanelsVisible = previous.PanelsVisible;
        _tabsShown.UnionWith(previous._tabsShown);
        _fogModes.UnionWith(previous._fogModes);
        _speeds.UnionWith(previous._speeds);
        ActionsTaken = previous.ActionsTaken;
        _fogIndex = previous._fogIndex;
        Match.ShowWorldAs(FogViewer);
        Match.Speed = previous.Match.Speed;
        Match.Paused = previous.Match.Paused;
        var from = previous.Match.Camera;
        var camera = Match.Camera;
        camera.SetMaxHeightAboveGround(from.MaxHeightAboveGround);
        camera.SetPitch(from.Pitch);
        camera.SetAngle(from.Angle);
        camera.SetHeightAboveGround(from.HeightAboveGround);
        camera.SettleZoom();
        camera.CenterOn(from.Pivot);
        _beforeOverview = previous._beforeOverview;
        Followed = previous.Followed;
        HighestReached = previous.HighestReached;
    }

    private void ShowTab(SpectatorTab tab)
    {
        Tab = tab;
        PanelsVisible = true;
        _tabsShown.Add(tab.ToString());
    }

    private void SetSpeed(double speed)
    {
        Match.Speed = speed;
        Match.Paused = false;
        NoteSpeed();
    }

    private void NoteSpeed() => _speeds.Add(Match.Paused ? "paused" : string.Create(CultureInfo.InvariantCulture, $"{Match.Speed:0.##}x"));

    public string SpeedLabel => Match.Paused ? "PAUSED" : string.Create(CultureInfo.InvariantCulture, $"{Match.Speed:0.##}x");

    private void Follow(int index)
    {
        if (index >= Cameras.Length) return;
        if (Followed == index) { Followed = null; return; }
        LeaveOverview();
        Followed = index;
        Match.Camera.CenterOn(Cameras[index].Position);
    }

    private void Jump(int index)
    {
        if (index >= Cameras.Length) return;
        LeaveOverview();
        Followed = null;
        Match.Camera.CenterOn(Cameras[index].Position);
    }

    private void CenterFree(Vector2 world)
    {
        Followed = null;
        Match.Camera.CenterOn(world);
    }

    // ------------------------------------------------------------------------------------------------------------ camera

    /// <summary>The height from which the whole map is on screen in the overview, for the current viewport (found again when the window changes).</summary>
    public float OverviewHeight
    {
        get
        {
            var viewport = Match.Camera.ViewportSize;
            if (viewport != _overviewViewport)
            {
                _overviewViewport = viewport;
                _overviewHeight = SpectatorCamera.OverviewHeight(Match.Camera.Settings, Match.Heights, viewport, Match.Camera.Angle, SpectatorCamera.OverviewPitchDegrees * MathF.PI / 180f);
            }
            return _overviewHeight;
        }
    }

    private void ApplyZoomLimit() => Match.Camera.SetMaxHeightAboveGround(Options.MaxZoomHeight ?? OverviewHeight);

    private void ToggleOverview()
    {
        var camera = Match.Camera;
        if (_beforeOverview is { } before)
        {
            _beforeOverview = null;
            camera.SetPitch(before.Pitch);
            camera.SetHeightAboveGround(before.Height);
            // The zoom first: while the camera is still far out its constraints hold the pivot in the middle of the map.
            camera.SettleZoom();
            camera.CenterOn(before.Pivot);
            return;
        }
        Followed = null;
        _beforeOverview = (camera.Pivot, camera.HeightAboveGround, camera.Pitch);
        var pitch = SpectatorCamera.OverviewPitchDegrees * MathF.PI / 180f;
        camera.SetPitch(pitch);
        // The height for the view as it is turned now. An own limit (--spectator-max-zoom) applies here too: the overview then shows as much as the limit allows.
        var fit = camera.Angle == 0f ? OverviewHeight : SpectatorCamera.OverviewHeight(camera.Settings, Match.Heights, camera.ViewportSize, camera.Angle, pitch);
        if (Options.MaxZoomHeight is null && fit > camera.MaxHeightAboveGround) camera.SetMaxHeightAboveGround(fit);
        camera.SetHeightAboveGround(fit);
        camera.CenterOn(Match.Heights.BoundaryMax(0) / 2);
        camera.SettleZoom();
    }

    private void LeaveOverview()
    {
        if (Overview) ToggleOverview();
    }

    /// <summary>The camera for this frame and, when the simulation moved on, the panels. Call after <see cref="LiveGame.Advance"/>.</summary>
    public void Update(double deltaSeconds)
    {
        var camera = Match.Camera;
        if (camera.ViewportSize != _overviewViewport && Options.MaxZoomHeight is null) ApplyZoomLimit();
        var intent = _hud.Input.UpdateCamera(camera, (float)deltaSeconds);
        if (intent.Reset)
        {
            LeaveOverview();
            camera.SetZoomToDefault();
            camera.SetAngle(0f);
            camera.SetPitchToDefault();
        }
        if (intent.Scroll != Vector2.Zero)
        {
            Followed = null;
            // A scroll step is a fixed ground distance at a player's height; up here the same step would crawl, so it grows with the height.
            camera.ScrollByIntent(intent.Scroll * MathF.Max(1f, camera.HeightAboveGround / PlayerMaxHeight));
        }
        if (intent.RotateDelta != 0f) camera.RotateBy(intent.RotateDelta);
        if (Followed is { } followed)
        {
            // The simulated camera moves once per tick; the view glides after it on frame time so it does not step at the tick rate.
            var target = Cameras[followed].Position;
            var offset = target - camera.Pivot;
            var blend = 1f - MathF.Exp(-(float)deltaSeconds * 8f);
            camera.CenterOn(offset.Length() > 400f ? target : camera.Pivot + offset * blend);
        }
        camera.Update((float)deltaSeconds, intent.Scroll != Vector2.Zero);
        HighestReached = MathF.Max(HighestReached, camera.HeightAboveGround);

        var interval = Match.World.Time.OriginalFrames(PanelIntervalFrames);
        if (Match.Tick != _panelTick && (Match.Tick - _panelTick >= interval || Match.IsOver || Match.Paused)) RebuildPanels();
    }

    private void RebuildPanels()
    {
        _panelTick = Match.Tick;
        Panels = Stats.Build(Match.World, Match.Players, Match.Colors, Match.Winner, Match.IsOver);
        PanelRebuilds++;
    }

    private void OnTickStepped(TickResult result)
    {
        var world = Match.World;
        Stats.Observe(result, world);
        var recorded = Match.RecordedCommands;
        _tickCommands.Clear();
        _tickOrders.Clear();
        _tickRejections.Clear();
        for (; _commandCursor < recorded.Count; _commandCursor++)
        {
            var command = recorded[_commandCursor];
            if (command.Player.IsSystem) continue;
            _tickCommands.Add(command);
            _commandsByPlayer[command.Player.Value] = _commandsByPlayer.GetValueOrDefault(command.Player.Value) + 1;
            var key = (command.Player.Value, command.GetType().Name);
            _tickOrders[key] = _tickOrders.GetValueOrDefault(key) + 1;
        }
        // A computer player repeats an order the simulation refuses (a power that is not ready, a spot that cannot be built on) every time it thinks. A refused order
        // shows nothing, so it must not move the camera: the simulation reports each refusal of the tick with the player and the order's type, and when every order
        // of a type was refused, those orders are left out. (When only some were refused the event does not say which; they all stay.)
        if (_tickCommands.Count > 0 && result.Events is not null)
        {
            foreach (var ev in result.Events)
            {
                if (ev.Kind != CnCNew.Simulation.Events.SimEventKind.CommandRejected) continue;
                _tickRejections[(ev.Player.Value, ev.Text)] = _tickRejections.GetValueOrDefault((ev.Player.Value, ev.Text)) + 1;
                _rejectedByPlayer[ev.Player.Value] = _rejectedByPlayer.GetValueOrDefault(ev.Player.Value) + 1;
            }
            if (_tickRejections.Count > 0)
                _tickCommands.RemoveAll(command => (command.Player.Value, command.GetType().Name) is var key && _tickRejections.GetValueOrDefault(key) >= _tickOrders.GetValueOrDefault(key));
        }
        Vector2? PositionOf(EntityId id) => world.FindEntity(id) is { } entity && SimulationQueries.IsAlive(entity)
            ? new Vector2(CnCNew.Presentation.FixedConvert.ToSingle(entity.X), CnCNew.Presentation.FixedConvert.ToSingle(entity.Y))
            : null;
        foreach (var camera in Cameras)
        {
            if (_tickCommands.Count > 0) camera.Observe(result.Tick, _tickCommands, PositionOf);
            camera.Advance(PositionOf);
        }
    }

    /// <summary>What a player would see with the simulated camera of <paramref name="index"/>: the ground corners of a player's view at its default zoom, centred on the camera's position.</summary>
    public Vector2[] FootprintOf(int index)
    {
        _probe.SetViewportSize(Match.Camera.ViewportSize);
        _probe.CenterOn(Cameras[index].Position);
        _probe.SettleZoom();
        return _probe.GroundFootprint();
    }

    // ------------------------------------------------------------------------------------------------------------ output

    public UiDrawList BuildDrawList(int width, int height) => _renderer.Build(this, width, height);

    /// <summary>The window rectangle of an action's button in the last layout, for a test or a script that clicks it.</summary>
    public UiRect? ButtonRect(SpectatorAction action, int width, int height) => _renderer.ButtonRect(this, action, width, height);

    /// <summary>The run as lines for the console of a headless check (<c>scripts/check-spectator.ps1</c> reads them); the panels of the last tick follow as <c>spectator panel:</c> lines.</summary>
    public IReadOnlyList<string> Report()
    {
        if (_panelTick != Match.Tick) RebuildPanels();
        var lines = new List<string>();
        string Number(float value) => value.ToString("F0", CultureInfo.InvariantCulture);
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"spectator: map {Match.Setup.MapPath}, seed {Options.Seed}, rate {Match.World.Time.TicksPerSecond}, ticks={Match.Tick}, gameSeconds={Match.Tick / Match.World.Time.TicksPerSecond}, over={Match.IsOver}, winner={(Match.Winner is { } winner ? winner.Value : 0)}, victoryTick={Match.VictoryTick}, stateHash={Match.World.StateHash:X16}"));
        lines.Add($"spectator commands: {string.Join(", ", Match.Players.Select(player => $"player{player.Id.Value}={_commandsByPlayer.GetValueOrDefault(player.Id.Value)}"))}, rejected=[{string.Join(" ", Match.Players.Select(player => $"player{player.Id.Value}:{_rejectedByPlayer.GetValueOrDefault(player.Id.Value)}"))}], viewerOrdersRefused={Match.SpectatorSink?.Refused ?? 0}, viewerOrdersRecorded={Match.RecordedCommands.Count(command => !command.Player.IsSystem && !Match.Players.Any(player => player.Id == command.Player))}");
        lines.Add($"spectator ai: {string.Join(", ", Match.Players.Select((player, index) => $"player{player.Id.Value}={Match.Setup.AiOf(index)}"))}, missingScriptTypeHits={Match.MissingScriptTypeHits}");
        lines.Add($"spectator camera: playerMaxHeight={Number(PlayerMaxHeight)}, spectatorMaxHeight={Number(SpectatorMaxHeight)}, overviewHeight={Number(OverviewHeight)}, highestReached={Number(HighestReached)}, heightNow={Number(Match.Camera.HeightAboveGround)}, overview={Overview}, followed={(Followed is { } followed ? followed + 1 : 0)}");
        for (var index = 0; index < Cameras.Length; index++)
        {
            var camera = Cameras[index];
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"spectator ai-camera {index + 1}: player={camera.Player.Value}, orders={camera.OrdersSeen}, interests={camera.Switches}, cuts={camera.Cuts}, now={camera.Interest.Kind}, position={camera.Position.X:F0};{camera.Position.Y:F0}, kinds=[{string.Join(" ", camera.SwitchesByKind.Select(pair => $"{pair.Key}:{pair.Value}"))}]"));
        }
        lines.Add($"spectator view: actions={ActionsTaken}, tabs=[{string.Join(" ", _tabsShown)}], fog=[{string.Join(" ", _fogModes)}], speeds=[{string.Join(" ", _speeds)}], panelRebuilds={PanelRebuilds}, events={Stats.EventsSeen}, unresolvedEvents={Stats.Unresolved}");
        foreach (var line in Panels.Dump().Split('\n', StringSplitOptions.RemoveEmptyEntries)) lines.Add($"spectator panel: {line}");
        return lines;
    }
}
