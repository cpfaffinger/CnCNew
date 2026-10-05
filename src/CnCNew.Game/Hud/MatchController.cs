using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Game.Live;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using UiScaleMode = CnCNew.Ui.Layout.UiScaleMode;
using SettingsScaleMode = CnCNew.Ui.Settings.UiScaleMode;
using GameSettings = CnCNew.Ui.Settings.GameSettings;

namespace CnCNew.Game.Hud;

/// <summary>The four buttons of the in-game <c>QuitMenu.wnd</c> plus resume; <see cref="MatchController.MenuActionRequested"/> reports
/// whichever the player clicked so the host (shell) can act on it (only <see cref="Resume"/> is handled here, by closing the menu).</summary>
public enum MenuAction
{
    Resume,
    SaveLoad,
    Options,
    Restart,
    Exit,
}

/// <summary>
/// The human player's whole control surface for a running <see cref="LiveMatch"/>: owns the <see cref="GameInputController"/> and the
/// retail control-bar HUD, turns <see cref="InputEvent"/>s into simulation commands (exclusively through <see cref="LiveMatch.Commands"/>,
/// so <c>Session.ToReplay()</c> reproduces everything the player did) and produces a <see cref="UiDrawList"/> plus screen-space overlay
/// data for the host renderer. No Stride types: the whole class runs headless in tests.
/// <para>
/// Call order once per rendered frame: <see cref="Process"/> for every queued input event, then (after <see cref="LiveMatch.Advance"/>
/// has stepped the simulation for the frame) <see cref="Update"/>, then <see cref="BuildDrawList"/>.
/// </para>
/// </summary>
public sealed class MatchController
{
    private readonly HudAssets _assets;
    private readonly UiWindowTree _tree;
    private readonly UiWindow _controlBarRoot;
    private readonly UiWindow _quitMenuRoot;
    private readonly UiWindow _leftHud;
    private readonly UiScaleOptions _scaleOptions;
    private readonly ControlBarSchemeInfo? _scheme;
    private readonly List<(string Text, long ExpiresAtMs)> _ticker = [];
    private readonly OrderHintBook _hints = new();
    private const long TickerLifetimeMs = 8000;
    private const int TickerCapacity = 6;

    private bool _uiOwnsMouse;
    private bool _minimapPanning;
    private bool _wasPausedBeforeMenu;
    private long _clockMs;
    private bool _matchEndedFired;

    public MatchController(LiveGame match, GameSettings settings, string language = "English", IUiTextMeasure? textMeasure = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(settings);
        Match = match;
        Stats = new MatchStatsTracker();

        _assets = HudAssets.Load(match.Data, language);
        var services = _assets.BuildTreeServices(textMeasure ?? new MonospaceTextMeasure());
        _scaleOptions = new UiScaleOptions
        {
            Mode = MapScaleMode(settings.Display.UiScale),
            TextScale = settings.Accessibility.UiTextScalePercent / 100m,
        };
        var viewport = new UiViewport(Math.Max(1, (int)match.Camera.ViewportSize.X), Math.Max(1, (int)match.Camera.ViewportSize.Y));
        _tree = new UiWindowTree(viewport, _scaleOptions, services);

        var controlBarRoots = _tree.AddLayout(_assets.ControlBar);
        _controlBarRoot = controlBarRoots.Single(window => window.Name == "ControlBar.wnd:ControlBarParent");
        _leftHud = _tree.FindByName("ControlBar.wnd:LeftHUD") ?? throw new InvalidOperationException("ControlBar.wnd has no LeftHUD window.");
        // The retail file leaves the observer panels visible; this HUD is for the human player only.
        _tree.FindByName("ControlBar.wnd:ObserverPlayerInfoWindow")?.SetHidden(true);
        _tree.FindByName("ControlBar.wnd:ObserverPlayerListWindow")?.SetHidden(true);
        _tree.Register("ControlBarSystem", OnControlBarSystem);

        var quitMenuRoots = _tree.AddLayout(_assets.QuitMenu);
        _quitMenuRoot = quitMenuRoots.Single(window => window.Name == "QuitMenu.wnd:QuitMenuParent");
        _quitMenuRoot.SetHidden(true);
        _tree.Register("QuitMenuSystem", OnQuitMenuSystem);

        var humanParticipant = match.Data.Factions.FirstOrDefault(
            participant => participant.Faction.Name.Equals(match.HumanFaction, StringComparison.OrdinalIgnoreCase));
        if (humanParticipant is not null && _assets.Schemes.TryGetForSide(humanParticipant.Side, out var scheme)) _scheme = scheme;

        var buttons = CommandButtonCatalog.Build(match.Data.Graph);
        var hotkeys = HotkeyMap.FromGraph(match.Data.Graph).WithEngineDefaults();
        Input = new GameInputController(match.Human, match.Commands, hotkeys, buttons,
            label => _assets.Text.TryGet(label, out var resolved) ? resolved : null,
            new GameInputSettings(Camera: MapCameraSettings(settings)));
        Input.UiRequest += OnInputUiRequest;

        match.BeforeTick = world => Input.Tick(world);
        match.TickStepped += OnTickStepped;

        CommandBar = Input.CommandBar!.Build(match.World, Input.Selection);
        Hud = Input.CommandBar.Hud(match.World, match.Human);
        Overlay = HudOverlay.Empty;
    }

    public LiveGame Match { get; }
    public GameInputController Input { get; }
    public MatchStatsTracker Stats { get; }

    public CommandBarView CommandBar { get; private set; }
    public PlayerHud Hud { get; private set; }
    public HudOverlay Overlay { get; private set; }
    public bool IsMenuOpen { get; private set; }

    /// <summary>
    /// <see langword="false"/> while a mission script has disabled the input (<c>InGameUI::setInputEnabled</c>): every event except the menu key is dropped and the
    /// pointer no longer scrolls the camera; the in-game menu stays reachable and works as usual.
    /// </summary>
    public bool WorldInputEnabled { get; set; } = true;

    /// <summary>Meta actions the controller does not handle itself (pause, screenshot, chat, diplomacy, control bar toggle, time scale, ...).
    /// <see cref="MetaAction.Options"/> is handled internally (opens/closes the in-game menu) and never appears here.</summary>
    public event Action<MetaAction>? UiRequest;

    /// <summary>A button of the in-game menu was clicked. <see cref="MenuAction.Resume"/> is also handled internally (the menu closes).</summary>
    public event Action<MenuAction>? MenuActionRequested;

    /// <summary>Every command feedback of the frame before it becomes ticker text: the audio layer turns it into unit acknowledgements and the no-can-do sound.</summary>
    public event Action<CommandFeedback>? CommandFeedbackObserved;

    /// <summary>A button of the control bar or of the in-game menu was clicked (the click sound).</summary>
    public event Action? ButtonClicked;

    /// <summary>Raised exactly once, the first frame <see cref="Simulation.Skirmish.SkirmishMatchSession.IsOver"/> is observed.</summary>
    public event Action<MatchResult>? MatchEnded;

    // ------------------------------------------------------------------------------------------------------------ input

    public void Process(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        if (!WorldInputEnabled && !IsMenuOpen && inputEvent is not KeyEvent { Key: KeyCode.Escape }) return;
        switch (inputEvent)
        {
            case MouseMoveEvent move:
                _tree.MouseMove(Round(move.Position.X), Round(move.Position.Y));
                if (_minimapPanning && TryMinimapWorld(move.Position, out var panTo)) Match.Camera.CenterOn(panTo);
                else if (!_uiOwnsMouse) Input.Process(inputEvent, Match.World, Match.Camera);
                return;
            case MouseButtonEvent button:
                HandleMouseButton(button);
                return;
            case MouseWheelEvent:
                if (!_uiOwnsMouse) Input.Process(inputEvent, Match.World, Match.Camera);
                return;
            case KeyEvent key:
                HandleKey(key);
                return;
            case FocusLostEvent:
                _uiOwnsMouse = false;
                _minimapPanning = false;
                Input.Process(inputEvent, Match.World, Match.Camera);
                return;
            default:
                Input.Process(inputEvent, Match.World, Match.Camera);
                return;
        }
    }

    private void HandleMouseButton(MouseButtonEvent button)
    {
        if (!IsMenuOpen && TryMinimapWorld(button.Position, out var world))
        {
            _uiOwnsMouse = true;
            if (button.IsDown)
            {
                if (button.Button == MouseButton.Left) { Match.Camera.CenterOn(world); _minimapPanning = true; }
                else if (button.Button == MouseButton.Right)
                    Input.Commands.IssueContext(Match.World, new ContextHint(ContextAction.Move, CursorKind.Move, EntityId.Invalid, new Vector3(world.X, world.Y, 0f)), null);
            }
            else _minimapPanning = false;
            return;
        }

        var x = Round(button.Position.X);
        var y = Round(button.Position.Y);
        var uiButton = ToUiButton(button.Button);
        if (button.IsDown)
        {
            var isUi = IsMenuOpen || _tree.InputTarget(x, y) is not null;
            _uiOwnsMouse = isUi;
            if (isUi) { _tree.MouseDown(x, y, uiButton); _tree.Dispatch(); }
            else Input.Process(button, Match.World, Match.Camera);
        }
        else
        {
            if (_uiOwnsMouse)
            {
                _tree.MouseUp(x, y, uiButton);
                _tree.Dispatch();
                _uiOwnsMouse = false;
            }
            else Input.Process(button, Match.World, Match.Camera);
        }
    }

    private void HandleKey(KeyEvent key)
    {
        if (IsMenuOpen)
        {
            if (key is { Key: KeyCode.Escape, IsDown: true, IsRepeat: false }) CloseMenu();
            return;
        }
        Input.Process(key, Match.World, Match.Camera);
    }

    /// <summary>Screen position to minimap world position, honoring the letterbox fit of a square-or-not minimap inside the (usually wider) LeftHUD rectangle.</summary>
    private bool TryMinimapWorld(Vector2 screen, out Vector2 world)
    {
        world = default;
        var rect = _leftHud.Rect;
        if (!rect.Contains((int)screen.X, (int)screen.Y)) return false;
        var minimap = Match.Minimap;
        var scale = Math.Min(rect.Width / (float)minimap.Width, rect.Height / (float)minimap.Height);
        var drawWidth = minimap.Width * scale;
        var drawHeight = minimap.Height * scale;
        var offsetX = rect.X + (rect.Width - drawWidth) / 2f;
        var offsetY = rect.Y + (rect.Height - drawHeight) / 2f;
        var px = (screen.X - offsetX) / scale;
        var py = (screen.Y - offsetY) / scale;
        if (px < 0 || py < 0 || px >= minimap.Width || py >= minimap.Height) return false;
        world = minimap.PixelToWorld(new Vector2(px, py));
        return true;
    }

    private void OnInputUiRequest(MetaAction action)
    {
        if (action == MetaAction.Options) { OpenMenu(); return; }
        UiRequest?.Invoke(action);
    }

    private void OpenMenu()
    {
        if (IsMenuOpen) return;
        IsMenuOpen = true;
        _wasPausedBeforeMenu = Match.Paused;
        Match.Paused = true;
        _quitMenuRoot.SetHidden(false);
    }

    private void CloseMenu()
    {
        if (!IsMenuOpen) return;
        IsMenuOpen = false;
        Match.Paused = _wasPausedBeforeMenu;
        _quitMenuRoot.SetHidden(true);
        MenuActionRequested?.Invoke(MenuAction.Resume);
    }

    private void OnQuitMenuSystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind != UiEventKind.ButtonSelected) return;
        ButtonClicked?.Invoke();
        switch (uiEvent.Source)
        {
            case "QuitMenu.wnd:ButtonReturn": CloseMenu(); break;
            case "QuitMenu.wnd:ButtonOptions": MenuActionRequested?.Invoke(MenuAction.Options); break;
            case "QuitMenu.wnd:ButtonSaveLoad": MenuActionRequested?.Invoke(MenuAction.SaveLoad); break;
            case "QuitMenu.wnd:ButtonRestart": MenuActionRequested?.Invoke(MenuAction.Restart); break;
            case "QuitMenu.wnd:ButtonExit": MenuActionRequested?.Invoke(MenuAction.Exit); break;
        }
    }

    private void OnControlBarSystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind is not (UiEventKind.ButtonSelected or UiEventKind.ButtonSelectedRight)) return;
        ButtonClicked?.Invoke();
        var name = uiEvent.Source;
        if (SlotOf(name, "ControlBar.wnd:ButtonCommand") is { } slot && slot >= 1 && slot <= CommandBarModel.GridSlots)
        {
            Input.CommandBar!.Activate(Match.World, CommandBar, slot, Input.Commands);
            return;
        }
        if (SlotOf(name, "ControlBar.wnd:ButtonQueue") is { } queueSlot && queueSlot >= 1)
        {
            Input.Commands.CancelProduction(CommandBar.Producer, queueSlot - 1);
            return;
        }
        switch (name)
        {
            case "ControlBar.wnd:ButtonCancelConstruction":
                Input.Commands.CancelConstruction(CommandBar.Producer);
                break;
            case "ControlBar.wnd:ButtonIdleWorker":
                Input.DispatchMeta(MetaAction.SelectNextIdleWorker, Match.World, Match.Camera);
                break;
            case "ControlBar.wnd:ButtonOptions":
                OpenMenu();
                break;
            case "ControlBar.wnd:ButtonGeneral":
            case "ControlBar.wnd:ButtonPlaceBeacon":
            case "ControlBar.wnd:PopupCommunicator":
                AddTicker($"{name.Split(':')[^1]} is not implemented.");
                break;
        }
    }

    private static int? SlotOf(string name, string prefix)
        => name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name.AsSpan(prefix.Length), out var value) ? value : null;

    private static UiMouseButton ToUiButton(MouseButton button) => button switch
    {
        MouseButton.Left => UiMouseButton.Left,
        MouseButton.Right => UiMouseButton.Right,
        _ => UiMouseButton.Middle,
    };

    private static int Round(float value) => (int)MathF.Round(value);

    // ------------------------------------------------------------------------------------------------------------ per-frame update

    /// <summary>Applies the camera intent, refreshes the command bar and HUD numbers, and recomputes the overlay. Call once per
    /// rendered frame, after <see cref="LiveMatch.Advance"/> has stepped the simulation.</summary>
    public void Update(double deltaSeconds)
    {
        if (!IsMenuOpen)
        {
            var intent = Input.UpdateCamera(Match.Camera, (float)deltaSeconds);
            if (!WorldInputEnabled) intent = intent with { Scroll = Vector2.Zero, RotateDelta = 0f, ZoomDelta = 0f, Reset = false, CenterOn = null };
            ApplyCamera(intent);
            Match.Camera.Update((float)deltaSeconds, intent.Scroll != Vector2.Zero);
        }

        // The world moved under a resting pointer: the context (and with it the cursor) is evaluated again every frame, not only on input events.
        if (!PointerOverWindow) Input.RefreshHint(Match.World, Match.Camera);
        CommandBar = Input.CommandBar!.Build(Match.World, Input.Selection);
        Hud = Input.CommandBar.Hud(Match.World, Match.Human);
        DrainFeedbackToTicker();
        Overlay = ComputeOverlay();
        _clockMs += (long)Math.Round(deltaSeconds * 1000);
        _tree.AdvanceTo(_clockMs);
    }

    private void ApplyCamera(CameraIntent intent)
    {
        if (intent.Reset)
        {
            Match.Camera.SetZoomToDefault();
            Match.Camera.SetAngle(0f);
            Match.Camera.SetPitchToDefault();
        }
        if (intent.CenterOn is { } center) Match.Camera.CenterOn(center);
        if (intent.Scroll != Vector2.Zero) Match.Camera.ScrollByIntent(intent.Scroll);
        if (intent.RotateDelta != 0f) Match.Camera.RotateBy(intent.RotateDelta);
        if (intent.ZoomDelta != 0f) Match.Camera.SetHeightAboveGround(Match.Camera.HeightAboveGround + intent.ZoomDelta);
    }

    private static UiScaleMode MapScaleMode(SettingsScaleMode mode) => mode switch
    {
        SettingsScaleMode.Stretch => UiScaleMode.Stretch,
        SettingsScaleMode.IntegerMultiple => UiScaleMode.Uniform,
        _ => UiScaleMode.Anchor,
    };

    /// <summary>Maps the options-screen 0..1 scroll/rotate sliders onto the input layer's camera tuning: 0.5 (the default) keeps the
    /// engine defaults, the ends halve/double them. There is no retail table for this since the original slider drives an entirely
    /// different (non-linear) internal speed constant; this is a reasonable, documented approximation (see docs/GAME-HUD.md).</summary>
    private static CameraSettings MapCameraSettings(GameSettings settings)
    {
        var defaults = new CameraSettings();
        var scrollFactor = 0.5f + settings.Controls.ScrollSpeed;
        var rotateFactor = 0.5f + settings.Controls.CameraRotateSpeed;
        return defaults with
        {
            EdgeScrollEnabled = settings.Controls.EdgeScrolling,
            HorizontalScrollSpeedFactor = defaults.HorizontalScrollSpeedFactor * scrollFactor,
            VerticalScrollSpeedFactor = defaults.VerticalScrollSpeedFactor * scrollFactor,
            KeyboardScrollSpeedFactor = defaults.KeyboardScrollSpeedFactor * scrollFactor,
            KeyboardRotateRadiansPerFrame = defaults.KeyboardRotateRadiansPerFrame * rotateFactor,
        };
    }

    // ------------------------------------------------------------------------------------------------------------ ticker and stats

    private void DrainFeedbackToTicker()
    {
        foreach (var feedback in Input.Commands.DrainFeedback())
        {
            CommandFeedbackObserved?.Invoke(feedback);
            _hints.Observe(feedback, _clockMs);
            var text = feedback.Kind switch
            {
                FeedbackKind.Rejected => $"Rejected: {feedback.Detail}",
                FeedbackKind.Unsupported => $"Not supported: {feedback.Detail}",
                FeedbackKind.InvalidOrder => "Cannot do that.",
                _ => null,
            };
            if (text is not null) AddTicker(text);
        }
    }

    private void OnTickStepped(TickResult result)
    {
        Stats.Observe(result, Match.World);
        foreach (var ev in result.Events) ObserveTickerEvent(ev);
        if (Match.IsOver && !_matchEndedFired)
        {
            _matchEndedFired = true;
            MatchEnded?.Invoke(Stats.Snapshot(Match.Players, Match.Winner, Match.World.Tick));
        }
    }

    /// <summary>The EVA-style ticker text, derived only from <see cref="SimEvent"/>s the simulation already raises (documented in
    /// docs/GAME-HUD.md): construction and production completion, player defeat, special power use, upgrades and rejected commands.</summary>
    private void ObserveTickerEvent(SimEvent ev)
    {
        var world = Match.World;
        string? text = ev.Kind switch
        {
            SimEventKind.ConstructionCompleted => world.FindEntity(ev.Entity) is { } site ? $"{site.Template.Name} construction complete" : null,
            SimEventKind.ProductionCompleted when (uint)ev.Value < (uint)world.Templates.Templates.Length => $"{world.Templates[(int)ev.Value].Name} ready",
            SimEventKind.PlayerDefeated => $"Player {ev.Player.Value} has been defeated",
            SimEventKind.SpecialPowerUsed => $"{ev.Text} activated",
            SimEventKind.UpgradeCompleted => "Upgrade complete",
            SimEventKind.CommandRejected => "Command rejected",
            _ => null,
        };
        if (text is not null) AddTicker(text);
    }

    private void AddTicker(string text)
    {
        _ticker.Add((text, _clockMs + TickerLifetimeMs));
        while (_ticker.Count > TickerCapacity) _ticker.RemoveAt(0);
    }

    private ImmutableArray<string> TickerSnapshot()
    {
        _ticker.RemoveAll(entry => entry.ExpiresAtMs <= _clockMs);
        return [.._ticker.Select(entry => entry.Text)];
    }

    // ------------------------------------------------------------------------------------------------------------ overlay

    private HudOverlay ComputeOverlay()
    {
        var world = Match.World;
        var view = Match.Camera;

        var box = SelectionBoxOverlay.None;
        if (Input.State.HasMouse && Input.State.IsDragging(MouseButton.Left) && Input.State.PressPosition(MouseButton.Left) is { } start)
            box = new SelectionBoxOverlay(true, start, Input.State.MousePosition);

        var selected = ImmutableArray.CreateBuilder<UnitMarker>();
        var rally = ImmutableArray.CreateBuilder<RallyPointMarker>();
        foreach (var id in Input.Selection.Selected)
        {
            if (world.FindEntity(id) is not { } entity) continue;
            if (Input.Selection.Picker.TryProject(view, entity, out var projected))
            {
                var health = entity.Body is { } body && body.MaxHealth.Raw != 0 ? (float)(body.Health / body.MaxHealth).ToDecimal() : 1f;
                selected.Add(new UnitMarker(entity.Id, projected.Center, projected.Radius, health, entity.Owner == Match.Human, world.AreEnemies(Match.Human, entity.Owner), entity.Template.IsKindOf("STRUCTURE")));
            }
            if (SimulationQueries.ProductionOf(entity) is { } production)
            {
                var state = SimulationQueries.Describe(production);
                if (state.Bool("hasRally") && state.Values.TryGetValue("rallyX", out var rawX) && rawX is Fixed rallyX
                    && state.Values.TryGetValue("rallyY", out var rawY) && rawY is Fixed rallyY
                    && view.TryWorldToScreen(OnGround(new Vector2((float)rallyX.ToDecimal(), (float)rallyY.ToDecimal())), out var rallyScreen))
                    rally.Add(new RallyPointMarker(entity.Id, rallyScreen));
            }
        }

        // Standing attack, attack-move and guard orders of the selected own units: a line to the goal (the original draws none; kept as an aid, for the selection only).
        var orderLines = ImmutableArray.CreateBuilder<OrderLine>();
        foreach (var unit in Input.Selection.OwnSelected(world))
        {
            if (!Input.Commands.Orders.TryGet(unit.Id, out var order) || order.Kind == StandingOrderKind.Waypoints) continue;
            var from = SimulationQueries.GroundOf(unit);
            var goal = order.Kind == StandingOrderKind.Attack && world.FindEntity(order.Target) is { } victim ? SimulationQueries.GroundOf(victim) : order.Destination;
            if (view.TryWorldToScreen(OnGround(from), out var fromScreen) && view.TryWorldToScreen(OnGround(goal), out var toScreen))
                orderLines.Add(new OrderLine(unit.Id, [fromScreen, toScreen], order.Kind == StandingOrderKind.Attack));
        }

        // Waypoint paths: only while Alt is held, for the selection (W3DWaypointBuffer::drawWaypoints).
        var waypointPaths = ImmutableArray.CreateBuilder<WaypointPathOverlay>();
        foreach (var path in OrderMarkers.WaypointPaths(world, Input.Selection, Input.Commands.Orders, Input.Commands.WaypointMode))
        {
            var points = ImmutableArray.CreateBuilder<Vector2>();
            if (!view.TryWorldToScreen(OnGround(path.Start), out var startScreen)) continue;
            points.Add(startScreen);
            foreach (var station in path.Stations)
            {
                if (!view.TryWorldToScreen(OnGround(station), out var stationScreen)) break;
                points.Add(stationScreen);
            }
            if (points.Count >= 2) waypointPaths.Add(new WaypointPathOverlay(path.Unit, points.ToImmutable()));
        }

        var hints = ImmutableArray.CreateBuilder<OrderHintMarker>();
        foreach (var orderHint in _hints.Active(_clockMs))
        {
            var position = orderHint.Target.IsValid && world.FindEntity(orderHint.Target) is { } marked
                ? OnGround(SimulationQueries.GroundOf(marked))
                : OnGround(new Vector2(orderHint.Position.X, orderHint.Position.Y));
            if (view.TryWorldToScreen(position, out var hintScreen)) hints.Add(new OrderHintMarker(orderHint.Kind, hintScreen, orderHint.Age(_clockMs)));
        }

        var placement = PlacementGhostOverlay.None;
        if (Input.Commands.Mode == CommandModeKind.Placement && Input.Commands.Ghost is { } ghost && world.Templates.TryGet(ghost.Template, out var template))
        {
            var footprint = Footprint(template, ghost.Position, ghost.Angle);
            var screenPoints = ImmutableArray.CreateBuilder<Vector2>();
            var ok = true;
            foreach (var corner in footprint)
                if (view.TryWorldToScreen(corner, out var corner2D)) screenPoints.Add(corner2D);
                else { ok = false; break; }
            if (ok) placement = new PlacementGhostOverlay(true, screenPoints.ToImmutable(), ghost.IsValid);
        }

        // Healed objects in view show the wrench / cross over their health bar, selected or not (Drawable::drawHealing runs for every drawable).
        var heals = ImmutableArray.CreateBuilder<HealIconMarker>();
        foreach (var drawable in Match.Frame.Drawables)
        {
            if (!drawable.ShowsHealing || !drawable.Entity.IsValid || world.FindEntity(drawable.Entity) is not { } healed) continue;
            if (!Input.Selection.Picker.TryProject(view, healed, out var projected)) continue;
            var kind = drawable.IsStructure ? HealIconKind.Structure : drawable.Visual.IsKindOf("VEHICLE") ? HealIconKind.Vehicle : HealIconKind.Default;
            heals.Add(new HealIconMarker(drawable.Entity, new Vector2(projected.Center.X, projected.Center.Y - projected.Radius), MathF.Max(24f, projected.Radius * 1.8f), kind));
        }

        var hint = DescribeHint(Input.Hint);
        return new HudOverlay(box, selected.ToImmutable(), rally.ToImmutable(), orderLines.ToImmutable(), placement, hint, TickerSnapshot(), heals.ToImmutable(),
            waypointPaths.ToImmutable(), hints.ToImmutable());

        Vector3 OnGround(Vector2 point) => new(point.X, point.Y, Match.Heights.HeightAt(point.X, point.Y));
    }

    // ------------------------------------------------------------------------------------------------------------ cursor

    /// <summary>The pointer is over the control bar, the minimap or the open in-game menu, or a press that began there is still held.</summary>
    public bool PointerOverWindow
    {
        get
        {
            if (IsMenuOpen || _uiOwnsMouse) return true;
            if (!Input.State.HasMouse) return false;
            var position = Input.State.MousePosition;
            return TryMinimapWorld(position, out _) || _tree.InputTarget(Round(position.X), Round(position.Y)) is not null;
        }
    }

    /// <summary>The cursor to show this frame (<see cref="GameInputController.Cursor"/>): the arrow over the HUD and in the menu, otherwise scroll, mode or context cursor.</summary>
    public CursorState Cursor => IsMenuOpen ? CursorState.Arrow : Input.Cursor(PointerOverWindow);

    /// <summary>The pointer position in viewport pixels, or null before the first mouse event.</summary>
    public Vector2? PointerPosition => Input.State.HasMouse ? Input.State.MousePosition : null;

    private static string? DescribeHint(ContextHint hint) => hint.Action switch
    {
        ContextAction.None or ContextAction.Select => null,
        ContextAction.Invalid => "Cannot do that here.",
        _ => hint.Action.ToString(),
    };

    /// <summary>The four ground corners of the construction footprint, rotated by <paramref name="angle"/> (radians). A non-box geometry
    /// (rare for buildings) is approximated by its bounding circle's square, since the simulation only exposes that radius.</summary>
    private static Vector3[] Footprint(ObjectTemplate template, Vector3 position, float angle)
    {
        float halfX, halfY;
        if (template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase))
        {
            halfX = (float)template.GeometryMajorRadius.ToDecimal();
            halfY = (float)template.GeometryMinorRadius.ToDecimal();
        }
        else halfX = halfY = (float)template.BoundingCircleRadius.ToDecimal();

        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        Vector3 Corner(float sx, float sy)
        {
            var dx = sx * halfX;
            var dy = sy * halfY;
            return new Vector3(position.X + dx * cos - dy * sin, position.Y + dx * sin + dy * cos, position.Z);
        }
        return [Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1)];
    }

    /// <summary>
    /// The current screen rectangle of a decorated window name (e.g. <c>"ControlBar.wnd:ButtonCommand01"</c>), for a host that wants
    /// to hit-test without duplicating the layout's scaling math, and for headless tests that click a real button. Null if no such
    /// window exists in <c>ControlBar.wnd</c> or <c>QuitMenu.wnd</c>.
    /// </summary>
    public UiRect? WindowRect(string decoratedName) => _tree.FindByName(decoratedName)?.Rect;

    // ------------------------------------------------------------------------------------------------------------ draw list

    /// <summary>
    /// The HUD for a <paramref name="width"/> x <paramref name="height"/> viewport: the retail control bar chrome (built from
    /// <c>ControlBar.wnd</c>) with dynamic cameo/hotkey/cost/cooldown overlays appended, plus two synthetic <see cref="UiCustomDraw"/>
    /// entries the host renderer fills in itself: <c>W3DLeftHUDDraw</c> (unimplemented in the original too; its window is the minimap
    /// frame) and <c>Viewport3D</c> (this package's own addition, the screen area above the control bar the 3D scene should render into).
    /// </summary>
    public UiDrawList BuildDrawList(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "The viewport needs at least 1x1 pixels.");
        if (_tree.Viewport.Width != width || _tree.Viewport.Height != height) _tree.SetViewport(new UiViewport(width, height), _scaleOptions);

        RefreshDynamicWindows();
        var baseList = _tree.BuildDrawList();
        var extra = BuildDynamicCommands();
        var viewportRect = new UiRect(0, 0, width, _controlBarRoot.Rect.Y);
        // The scheme art is the control bar's own background (W3DCommandBarBackgroundDraw in the original): it lies under the window tree, whose windows draw their own chrome on top.
        // The world-space feedback (selection rings, health bars, orders, rally points, placement ghost, healing icons) lies between the 3D scene and the control bar chrome.
        var commands = HudOverlayRenderer.Build(Overlay, _clockMs).AddRange(BuildSchemeBackground()).AddRange(ResolveHardCodedImages(baseList.Commands)).AddRange(extra).Add(new UiCustomDraw("Viewport3D", "Viewport", viewportRect));
        return baseList with { Commands = commands };
    }

    /// <summary>
    /// <c>ControlBar.wnd</c> names the right-hand portrait window\x27s image <c>HardCodedWithControlBarSchemeIni</c>: the original replaces it with the scheme\x27s <c>RightHUDImage</c> (the side logo).
    /// </summary>
    private ImmutableArray<UiDrawCommand> ResolveHardCodedImages(ImmutableArray<UiDrawCommand> commands)
    {
        const string placeholder = "HardCodedWithControlBarSchemeIni";
        if (_scheme is not { RightHudImage.Length: > 0 } scheme) return commands;
        return [..commands.Select(command => command is UiImageDraw draw && draw.Image == placeholder ? draw with { Image = scheme.RightHudImage } : command)];
    }

    /// <summary>
    /// <c>ControlBarScheme::drawBackground</c>: the <c>ImagePart</c>s of the human side's scheme in layer order (the file draws layer 0 last, on top), each placed by the
    /// same transform as the scheme's money and power rectangles; an image the table does not know is skipped (the scheme names a few art pieces this data set lacks).
    /// </summary>
    private ImmutableArray<UiDrawCommand> BuildSchemeBackground()
    {
        if (_scheme is not { } scheme) return [];
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        foreach (var part in scheme.ImageParts.Select((value, index) => (value, index)).OrderByDescending(item => item.value.Layer).ThenBy(item => item.index).Select(item => item.value))
        {
            if (!_assets.Images.TryGet(part.ImageName, out _)) continue;
            var rect = _controlBarRoot.Transform.Map(new WndScreenRect(part.X, part.Y, part.X + part.Width, part.Y + part.Height, 1920, 1080));
            commands.Add(new UiImageDraw(part.ImageName, rect, WndColor.White, false, false));
        }
        return commands.ToImmutable();
    }

    private void RefreshDynamicWindows()
    {
        var view = CommandBar;
        // InGameUI::update: the money window shows "GUI:ControlBarMoneyDisplay" with the local player's money; ControlBarScheme::updateControlBarScheme
        // puts it at the scheme's money rectangle. The layout file keeps a placeholder text and a 100x100 rectangle at the top left.
        if (_tree.FindByName("ControlBar.wnd:MoneyDisplay") is { } moneyWindow)
        {
            moneyWindow.SetText(_assets.Text.Format("GUI:ControlBarMoneyDisplay", (int)Hud.Money.ToDecimal()));
            if (_scheme is { } moneyScheme)
                moneyWindow.SetScreenRect(_controlBarRoot.Transform.Map(new WndScreenRect(moneyScheme.Money.Left, moneyScheme.Money.Top, moneyScheme.Money.Right, moneyScheme.Money.Bottom, 1920, 1080)));
        }
        _tree.FindByName("ControlBar.wnd:CommandWindow")!.SetHidden(view.Context is not (CommandBarContext.Command or CommandBarContext.MultiSelect));
        _tree.FindByName("ControlBar.wnd:UnderConstructionWindow")!.SetHidden(view.Context != CommandBarContext.UnderConstruction);
        var underConstructionDesc = _tree.FindByName("ControlBar.wnd:UnderConstructionDesc");
        if (view.Context == CommandBarContext.UnderConstruction && Match.World.FindEntity(view.Producer) is { } site) underConstructionDesc?.SetText(site.Template.Name);

        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            var window = _tree.FindByName($"ControlBar.wnd:ButtonCommand{slot:00}")!;
            var button = view[slot];
            window.SetHidden(button is null || button.Availability == ButtonAvailability.Hidden);
            if (button is not null) window.SetEnabled(button.IsEnabled);
        }

        _tree.FindByName("ControlBar.wnd:ProductionQueueWindow")!.SetHidden(view.Queue.IsEmpty);
        for (var index = 0; index < CommandBarModel.MaxQueueButtons; index++)
            _tree.FindByName($"ControlBar.wnd:ButtonQueue{(index + 1):00}")!.SetHidden(index >= view.Queue.Length);

        _tree.FindByName("ControlBar.wnd:WinUnitSelected")!.SetHidden(view.Context != CommandBarContext.Command);
    }

    private ImmutableArray<UiDrawCommand> BuildDynamicCommands()
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var smallFont = new UiFont("Arial", 10, false);
        var white = new WndColor(255, 255, 255, 255);
        var black = new WndColor(0, 0, 0, 255);
        var yellow = new WndColor(255, 255, 0, 255);
        var cooldown = new WndColor(0, 0, 0, 160);

        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            var button = CommandBar[slot];
            if (button is null || button.Availability == ButtonAvailability.Hidden) continue;
            var rect = _tree.FindByName($"ControlBar.wnd:ButtonCommand{slot:00}")!.Rect;
            if (button.Button.ButtonImage.Length > 0 && _assets.Images.TryGet(button.Button.ButtonImage, out _))
                commands.Add(new UiImageDraw(button.Button.ButtonImage, rect, WndColor.White, false, false));
            if (button.Hotkey is { } hotkey)
                commands.Add(new UiTextDraw(char.ToUpperInvariant(hotkey).ToString(), null, smallFont, white, black, rect.X + 2, rect.Y + 2, UiTextAlignment.Left, rect));
            if (button.Cost.Raw != 0)
            {
                var text = ((int)button.Cost.ToDecimal()).ToString(CultureInfo.InvariantCulture);
                commands.Add(new UiTextDraw(text, null, smallFont, yellow, black, rect.Right - 4 - text.Length * 6, rect.Bottom - 14, UiTextAlignment.Left, rect));
            }
            if (button.Availability == ButtonAvailability.NotReady && button.ReadyFraction is { } fraction)
            {
                var wipe = (int)(rect.Height * (1 - fraction));
                if (wipe > 0) commands.Add(new UiFillRect(new UiRect(rect.X, rect.Y, rect.Width, wipe), cooldown));
            }
        }

        for (var index = 0; index < CommandBar.Queue.Length; index++)
        {
            var entry = CommandBar.Queue[index];
            var rect = _tree.FindByName($"ControlBar.wnd:ButtonQueue{(index + 1):00}")!.Rect;
            if (entry.ButtonImage.Length > 0 && _assets.Images.TryGet(entry.ButtonImage, out _))
                commands.Add(new UiImageDraw(entry.ButtonImage, rect, WndColor.White, false, false));
            if (index == 0 && entry.Progress > 0f)
            {
                var wipe = (int)(rect.Height * (1 - entry.Progress));
                if (wipe > 0) commands.Add(new UiFillRect(new UiRect(rect.X, rect.Y, rect.Width, wipe), cooldown));
            }
        }

        if (_scheme is { } scheme)
        {
            var powerRect = _controlBarRoot.Transform.Map(new WndScreenRect(scheme.PowerBar.Left, scheme.PowerBar.Top, scheme.PowerBar.Right, scheme.PowerBar.Bottom, 1920, 1080));
            var powerColor = Hud.Underpowered ? new WndColor(220, 40, 40, 255) : new WndColor(40, 200, 60, 255);
            var ratio = Hud.EnergyConsumption <= 0 ? 1f : Math.Clamp(Hud.EnergyProduction / (float)Hud.EnergyConsumption, 0f, 1f);
            commands.Add(new UiFillRect(powerRect with { Width = (int)(powerRect.Width * ratio) }, powerColor));
        }
        return commands.ToImmutable();
    }
}
