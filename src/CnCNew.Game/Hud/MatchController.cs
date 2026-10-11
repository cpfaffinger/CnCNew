using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
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
    private readonly TooltipPainter _tooltips;
    private readonly CommandButtonDescription _descriptions;
    private readonly UiWindowTree _tree;
    private readonly UiWindow _controlBarRoot;
    private readonly UiWindow _quitMenuRoot;
    private readonly UiWindow _leftHud;
    private readonly UiScaleOptions _scaleOptions;
    private readonly ControlBarSchemeInfo? _scheme;
    private readonly OrderHintBook _hints = new();
    private readonly IUiTextMeasure _textMeasure;
    private ImmutableArray<SuperweaponLine> _superweaponLines = [];
    private long _superweaponTick = -1;
    private bool _superweaponEnabled = true;

    private bool _showsSideLogo = true;
    private bool _uiOwnsMouse;
    private bool _minimapPanning;
    private bool _wasPausedBeforeMenu;
    private long _clockMs;
    private bool _matchEndedFired;
    private readonly SortedSet<string> _missingSchemeImages = new(StringComparer.Ordinal);

    public MatchController(LiveGame match, GameSettings settings, string language = "English", IUiTextMeasure? textMeasure = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(settings);
        Match = match;
        Stats = new MatchStatsTracker();

        _assets = HudAssets.Load(match.Data, language);
        _tooltips = new TooltipPainter(match.Data.Graph);
        _descriptions = new CommandButtonDescription(match.Data.Graph, _assets.Text);
        _textMeasure = textMeasure ?? new MonospaceTextMeasure();
        TextStyle = InGameUiStyle.FromGraph(match.Data.Graph);
        Messages = new HudMessageFeed(new UiMessageLog(TextStyle.Settings), _assets.Text, match.Data.Graph) { Clock = () => Match.Clock.OriginalFrames };
        var services = _assets.BuildTreeServices(textMeasure ?? new MonospaceTextMeasure());
        _scaleOptions = new UiScaleOptions
        {
            Mode = MapScaleMode(settings.Display.UiScale),
            TextScale = settings.Accessibility.UiTextScalePercent / 100m,
        };
        var viewport = new UiViewport(Math.Max(1, (int)match.Camera.ViewportSize.X), Math.Max(1, (int)match.Camera.ViewportSize.Y));
        _tree = new UiWindowTree(viewport, _scaleOptions, services);
        _tree.TooltipOverride = CommandButtonTooltip;

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
        ApplySchemeImages();

        var buttons = CommandButtonCatalog.Build(match.Data.Graph);
        var hotkeys = HotkeyMap.FromGraph(match.Data.Graph).WithEngineDefaults();
        Input = new GameInputController(match.Human, match.Commands, hotkeys, buttons,
            label => _assets.Text.TryGet(label, out var resolved) ? resolved : null,
            new GameInputSettings(MapMouseScheme(settings), Camera: MapCameraSettings(settings), Commands: new CommandSettings(PlacementViewAngle: template => match.Data.Visuals.TryGet(template, out var visual) ? visual.PlacementViewAngle : 0f)));
        Input.UiRequest += OnInputUiRequest;

        match.BeforeTick = world => Input.Tick(world);
        match.CommandResolved += Input.Commands.OnResolved;
        match.TickStepped += OnTickStepped;
        match.CommandIssued += (command, result) =>
        {
            if (result.Accepted && command is CnCNew.Simulation.Commands.PurchaseUpgradeCommand purchase) Messages.ExpectUpgrade(purchase.Upgrade);
        };

        CommandBar = Input.CommandBar!.Build(match.World, Input.Selection);
        Hud = Input.CommandBar.Hud(match.World, match.Human);
        Overlay = HudOverlay.Empty;
    }

    public LiveGame Match { get; }
    public HudAssets Assets => _assets;
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

    /// <summary>
    /// <c>GameLogic::setDrawIconUI</c> (a mission script's <c>OPTIONS_SET_DRAWICON_UI_MODE</c>): while <see langword="false"/> the drawables show no icons (<c>Drawable::drawIconUI</c> draws nothing), so the healing icons
    /// over repaired objects are left out. The selection markers of the player's own selection stay (the original draws those through the selection, not through the icon pass).
    /// </summary>
    public bool IconUiEnabled { get; set; } = true;

    /// <summary>The superweapon countdowns at the top right (<c>InGameUI::draw</c>): every public-timer power of every player. A mission's scripts hide the list or single objects through it.</summary>
    public SuperweaponTimers Superweapons { get; } = new();

    /// <summary>What the in-game text is drawn from: the positions and colors of <c>InGameUI.ini</c> and the fonts of <c>Language.ini</c> (the superweapon list here, the mission texts through <see cref="MissionController"/>).</summary>
    public InGameUiStyle TextStyle { get; }

    /// <summary>
    /// The message list at the top left (<c>InGameUI::message</c>): what the game tells the player about refused orders, finished buildings and upgrades, attacks and defeated
    /// players (<see cref="HudMessageFeed"/>). The lines fade as the original's do (<see cref="UiMessageLog"/>).
    /// </summary>
    public HudMessageFeed Messages { get; }

    /// <summary>
    /// <see langword="true"/> (a skirmish): this HUD fades the message lines once per logic step and draws them. A mission sets it to <see langword="false"/>: its presentation director
    /// owns the list then (it shares it with <c>DISPLAY_TEXT</c>, updates it on its own clock and draws it with the other mission texts, also while the control bar is hidden).
    /// </summary>
    public bool OwnsMessageList { get; set; } = true;

    /// <summary>
    /// Images the human side's control bar scheme names that the mapped image table does not have (sorted): the window or art part that wanted one is drawn without it,
    /// as the original draws nothing for a null image, and the name is listed here instead of being dropped.
    /// </summary>
    public IReadOnlyCollection<string> MissingSchemeImages => _missingSchemeImages;

    /// <summary>The text scaling this HUD was built with (the display settings' UI scale and the accessibility text scale).</summary>
    public UiScaleOptions ScaleOptions => _scaleOptions;

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
                Messages.ClientNote($"not supported in this build: {name.Split(':')[^1]}");
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
        DrainFeedback();
        Overlay = ComputeOverlay();
        // The building follows the cursor in the world, too: the object layer draws it translucent (InGameUI::handleBuildPlacements).
        Match.PlacementGhost = !IsMenuOpen && Input.Commands.Mode == CommandModeKind.Placement ? Input.Commands.Ghost : null;
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

    /// <summary>Maps the options-screen 0..1 sliders onto the input layer's camera tuning. The scroll slider is the original's own value:
    /// <c>OptionsMenu.cpp</c> stores <c>m_keyboardScrollFactor = sliderPosition / 100</c>, which scales key and screen-edge scrolling only
    /// (<c>LookAtXlat.cpp</c>; right-drag scrolling does not depend on it). The rotate slider has no retail counterpart: 0.5 keeps the engine
    /// default, the ends halve/double it (docs/GAME-HUD.md).</summary>
    public static MouseScheme MapMouseScheme(GameSettings settings)
        => settings.Controls.AlternateMouse ? MouseScheme.RightClickCommands : MouseScheme.LeftClickCommands;

    public static CameraSettings MapCameraSettings(GameSettings settings)
    {
        var defaults = new CameraSettings();
        var rotateFactor = 0.5f + settings.Controls.CameraRotateSpeed;
        return defaults with
        {
            EdgeScrollEnabled = settings.Controls.EdgeScrolling,
            KeyboardScrollSpeedFactor = settings.Controls.ScrollSpeed,
            KeyboardRotateRadiansPerFrame = defaults.KeyboardRotateRadiansPerFrame * rotateFactor,
        };
    }

    // ------------------------------------------------------------------------------------------------------------ messages and stats

    private void DrainFeedback()
    {
        foreach (var feedback in Input.Commands.DrainFeedback())
        {
            CommandFeedbackObserved?.Invoke(feedback);
            _hints.Observe(feedback, _clockMs);
            Messages.Observe(feedback);
        }
    }

    private void OnTickStepped(TickResult result)
    {
        Stats.Observe(result, Match.World);
        foreach (var ev in result.Events) Messages.Observe(ev, Match.World, Match.Human);
        // InGameUI::update runs once per original frame: the lines fade on the game clock's 1/30 s grid, not per simulation tick.
        if (OwnsMessageList && Match.Clock.IsOriginalFrameBoundary) Messages.Log.Update(Match.Clock.OriginalFrames);
        if (Match.IsOver && !_matchEndedFired)
        {
            _matchEndedFired = true;
            MatchEnded?.Invoke(Stats.Snapshot(Match.Players, Match.Winner, Match.World.Tick));
        }
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
        foreach (var drawable in IconUiEnabled ? Match.Frame.Drawables : ImmutableArray<CnCNew.Presentation.Objects.DrawableInstance>.Empty)
        {
            if (!drawable.ShowsHealing || !drawable.Entity.IsValid || world.FindEntity(drawable.Entity) is not { } healed) continue;
            if (!Input.Selection.Picker.TryProject(view, healed, out var projected)) continue;
            var kind = drawable.IsStructure ? HealIconKind.Structure : drawable.Visual.IsKindOf("VEHICLE") ? HealIconKind.Vehicle : HealIconKind.Default;
            heals.Add(new HealIconMarker(drawable.Entity, new Vector2(projected.Center.X, projected.Center.Y - projected.Radius), MathF.Max(24f, projected.Radius * 1.8f), kind));
        }

        var hint = DescribeHint(Input.Hint);
        return new HudOverlay(box, selected.ToImmutable(), rally.ToImmutable(), orderLines.ToImmutable(), placement, hint, [..Messages.Log.Lines.Select(line => line.Text)], heals.ToImmutable(),
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
        // The scheme art is drawn by two marker windows of the control bar (W3DCommandBarBackgroundDraw, W3DCommandBarForegroundDraw): the background under the windows that
        // follow the marker, the foreground over those before it. The world-space feedback (selection rings, health bars, orders, rally points, placement ghost, healing icons)
        // lies between the 3D scene and the control bar chrome.
        var commands = HudOverlayRenderer.Build(Overlay, _clockMs).AddRange(ResolveControlBarCallbacks(baseList.Commands)).AddRange(extra).AddRange(BuildSuperweaponCommands(width, height))
            .Add(new UiCustomDraw("Viewport3D", "Viewport", viewportRect));
        // InGameUI::postDraw: the message lines lie over the windows.
        if (OwnsMessageList && !Messages.Log.IsEmpty) commands = commands.AddRange(MissionTextOverlay.Messages(Messages.Log, TextStyle, _scaleOptions, _textMeasure, width, height));
        // Mouse::drawTooltip lies over everything, the window tree's own tooltips (TOOLTIPTEXT) and the command button descriptions alike.
        if (_tree.Tooltip is { } tooltip) commands = commands.AddRange(_tooltips.Build(tooltip, _tree.Viewport, _scaleOptions, _tree.Services.TextMeasure));
        return baseList with { Commands = commands };
    }

    /// <summary>
    /// The description popup of the control bar (<c>ControlBar::populateBuildTooltipLayout</c>): for a command button its name, cost, description, why it cannot be bought and what
    /// it requires (<see cref="CommandButtonDescription"/>); for the money and the power window their two texts. The original shows them in <c>ControlBarPopupDescription.wnd</c>, a
    /// popup with its own draw function above the control bar; here they are the lines of the pointer tooltip.
    /// </summary>
    private string? CommandButtonTooltip(UiWindow window)
    {
        const string prefix = "ControlBar.wnd:ButtonCommand";
        if (window.Name == "ControlBar.wnd:MoneyDisplay") return _descriptions.Money().ToTooltip();
        if (window.Name == "ControlBar.wnd:PowerWindow") return _descriptions.Power(Hud.EnergyProduction, Hud.EnergyConsumption).ToTooltip();
        if (!window.Name.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(window.Name.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var slot)) return null;
        if (slot is < 1 or > CommandBarModel.GridSlots || CommandBar[slot] is not { Availability: not ButtonAvailability.Hidden } button) return null;
        var selected = Input.Selection.Selected.Select(Match.World.FindEntity).FirstOrDefault(entity => entity is not null);
        var tooltip = _descriptions.Build(button, Match.World, Match.Human, selected).ToTooltip();
        return tooltip.Length == 0 ? null : tooltip;
    }

    /// <summary>
    /// The draw callbacks of <c>ControlBar.wnd</c> this HUD knows, replaced by what they draw (<c>W3DControlBar.cpp</c>):
    /// <c>W3DCommandBarBackgroundDraw</c> and <c>W3DCommandBarForegroundDraw</c> draw the scheme's art parts (<c>ControlBarScheme::drawBackground</c>: layers 5 to 3,
    /// <c>drawForeground</c>: layers 2 to 0), each at the place of its marker window in the window order; <c>W3DRightHUDDraw</c> draws the window's image while it has the
    /// <c>IMAGE</c> status, which <c>ControlBar::setPortraitByObject</c> sets when nothing with a portrait is selected, so the scheme's <c>RightHUDImage</c> (the side's logo,
    /// <c>ControlBar::updateRightHUDImage</c>) shows then. Every other callback stays a <see cref="UiCustomDraw"/> for the host, which counts what it does not draw.
    /// </summary>
    private ImmutableArray<UiDrawCommand> ResolveControlBarCallbacks(ImmutableArray<UiDrawCommand> commands)
    {
        if (_scheme is not { } scheme) return commands;
        var result = ImmutableArray.CreateBuilder<UiDrawCommand>(commands.Length + scheme.ImageParts.Length);
        foreach (var command in commands)
        {
            switch (command)
            {
                case UiCustomDraw { Callback: "W3DCommandBarBackgroundDraw" }:
                    AddSchemeParts(result, scheme, scheme.BackgroundParts);
                    break;
                case UiCustomDraw { Callback: "W3DCommandBarForegroundDraw" }:
                    AddSchemeParts(result, scheme, scheme.ForegroundParts);
                    break;
                case UiCustomDraw { Callback: "W3DRightHUDDraw", Window: "ControlBar.wnd:RightHUD" } rightHud:
                    if (_showsSideLogo && SchemeImage(scheme.RightHudImage) is { } logo) result.Add(new UiImageDraw(logo, rightHud.Rect, WndColor.White, false, false));
                    break;
                default:
                    result.Add(command);
                    break;
            }
        }
        return result.ToImmutable();
    }

    private void AddSchemeParts(ImmutableArray<UiDrawCommand>.Builder commands, ControlBarSchemeInfo scheme, IEnumerable<ControlBarImagePart> parts)
    {
        foreach (var part in parts)
        {
            if (SchemeImage(part.ImageName) is not { } image) continue;
            commands.Add(new UiImageDraw(image, SchemeRectOnScreen(scheme, new SchemeRect(part.X, part.Y, part.X + part.Width, part.Y + part.Height)), WndColor.White, false, false));
        }
    }

    /// <summary>The image name if the table has it; an unknown name is listed in <see cref="MissingSchemeImages"/> and gives null (the original's null image), an empty one is no image.</summary>
    private string? SchemeImage(string name)
    {
        if (name.Length == 0) return null;
        if (_assets.Images.TryGet(name, out _)) return name;
        _missingSchemeImages.Add(name);
        return null;
    }

    /// <summary>
    /// <c>ControlBarScheme::init</c>: the scheme hands its state images to the control bar's own buttons (<c>GadgetButtonSetEnabledImage</c>, <c>...HiliteImage</c>,
    /// <c>...HiliteSelectedImage</c>, <c>...DisabledImage</c>), the two images of the under-attack window, the experience bar's foreground and, through
    /// <c>ControlBar::updateBuildQueueDisabledImages</c>, the picture of an empty queue place. The layout names no images for these windows.
    /// </summary>
    private void ApplySchemeImages()
    {
        if (_scheme is not { } scheme) return;
        void Button(string name, SchemeButtonImages images, bool withDisabled = true)
        {
            if (_tree.FindByName(name) is not { } window) return;
            window.SetDrawImage(WndDrawDataKind.Enabled, 0, SchemeImage(images.Enabled));
            window.SetDrawImage(WndDrawDataKind.Hilite, 0, SchemeImage(images.Hilited));
            window.SetDrawImage(WndDrawDataKind.Hilite, 1, SchemeImage(images.Pushed));
            if (withDisabled) window.SetDrawImage(WndDrawDataKind.Disabled, 0, SchemeImage(images.Disabled));
        }
        Button("ControlBar.wnd:ButtonOptions", scheme.OptionsImages);
        Button("ControlBar.wnd:ButtonIdleWorker", scheme.WorkerImages);
        Button("ControlBar.wnd:ButtonPlaceBeacon", scheme.BeaconImages);
        Button("ControlBar.wnd:PopupCommunicator", scheme.ChatImages);
        Button("ControlBar.wnd:ButtonGeneral", scheme.GeneralImages);
        // ControlBar::setUpDownImages: the toggle of a control bar that is up shows the "down" images; it has no disabled image.
        Button("ControlBar.wnd:ButtonLarge", scheme.ToggleDownImages, withDisabled: false);
        if (_tree.FindByName("ControlBar.wnd:WinUAttack") is { } glow)
        {
            glow.SetDrawImage(WndDrawDataKind.Enabled, 0, SchemeImage(scheme.UnderAttackImages.Enabled));
            glow.SetDrawImage(WndDrawDataKind.Disabled, 0, SchemeImage(scheme.UnderAttackImages.Hilited));
        }
        _tree.FindByName("ControlBar.wnd:ExpBarForeground")?.SetEnabledImage0(SchemeImage(scheme.ExpBarForegroundImage));
        if (SchemeImage(scheme.QueueButtonImage) is { } emptyPlace)
            for (var index = 1; index <= CommandBarModel.MaxQueueButtons; index++)
                _tree.FindByName($"ControlBar.wnd:ButtonQueue{index:00}")?.SetDrawImage(WndDrawDataKind.Disabled, 0, emptyPlace);
    }

    /// <summary>
    /// <c>ControlBarScheme::init</c> moves the windows the scheme has a rectangle for (<c>winSetPosition</c>/<c>winSetSize</c> with <c>resMultiplier</c>); the rectangles the
    /// layout gives them are placeholders. Done again for every draw list, as the viewport may have changed.
    /// </summary>
    private void PlaceSchemeWindows(ControlBarSchemeInfo scheme)
    {
        void Place(string name, SchemeRect rect) => _tree.FindByName(name)?.SetScreenRect(SchemeRectOnScreen(scheme, rect));
        Place("ControlBar.wnd:PopupCommunicator", scheme.Chat);
        Place("ControlBar.wnd:ButtonIdleWorker", scheme.Worker);
        Place("ControlBar.wnd:ButtonOptions", scheme.Options);
        Place("ControlBar.wnd:ButtonPlaceBeacon", scheme.Beacon);
        Place("ControlBar.wnd:MoneyDisplay", scheme.Money);
        Place("ControlBar.wnd:PowerWindow", scheme.PowerBar);
        Place("ControlBar.wnd:ButtonGeneral", scheme.General);
        Place("ControlBar.wnd:ButtonLarge", scheme.MinMax);
        Place("ControlBar.wnd:WinUAttack", scheme.UnderAttack);
    }

    /// <summary>
    /// A rectangle of the scheme on the screen. The scheme carries the resolution it was written for (<c>ScreenCreationRes</c>) and the original scales every
    /// position of it by display / creation resolution per axis (<c>resMultiplier</c> in <c>ControlBarScheme::init</c>, <c>m_multiplyer</c> in
    /// <c>ControlBarSchemeManager::setControlBarSchemeByPlayer</c>), independently of the resolution <c>ControlBar.wnd</c> was written for. Here the scheme goes through the
    /// same kind of transform as the control bar's windows, anchored like the control bar itself, so the art and the windows stay on each other in every scale mode.
    /// </summary>
    private UiRect SchemeRectOnScreen(ControlBarSchemeInfo scheme, SchemeRect rect)
    {
        var bar = _controlBarRoot.Transform;
        var transform = UiScaler.Create(_tree.Viewport, scheme.CreationWidth, scheme.CreationHeight, bar.AnchorX, bar.AnchorY);
        return transform.Map(new WndScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom, scheme.CreationWidth, scheme.CreationHeight));
    }

    /// <summary>The <c>SelectPortrait</c> of the single selected object, if the image table has it.</summary>
    private string? SelectedPortrait()
    {
        if (Input.Selection.Selected.ToArray() is not [var only] || Match.World.FindEntity(only) is not { } entity) return null;
        return Match.Data.Visuals.TryGet(entity.Template.Name, out var visual) && visual.SelectPortrait is { Length: > 0 } image && _assets.Images.TryGet(image, out _) ? image : null;
    }

    private void RefreshDynamicWindows()
    {
        var view = CommandBar;
        // InGameUI::update: the money window shows "GUI:ControlBarMoneyDisplay" with the local player's money; ControlBarScheme::updateControlBarScheme
        // puts it at the scheme's money rectangle. The layout file keeps a placeholder text and a 100x100 rectangle at the top left.
        _tree.FindByName("ControlBar.wnd:MoneyDisplay")?.SetText(_assets.Text.Format("GUI:ControlBarMoneyDisplay", (int)Hud.Money.ToDecimal()));
        // ControlBarScheme::init puts the money window, the power window (the bar is drawn there, and its description pops up there) and the bar's own buttons at the scheme's rectangles.
        if (_scheme is { } placed) PlaceSchemeWindows(placed);
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

        // ControlBar::populateBuildQueue: a place of the queue nothing stands in is a disabled button, which shows the scheme's QueueButtonImage; without a scheme image the
        // place stays hidden as before (a disabled button of the layout has no picture of its own).
        var emptyPlaces = _scheme is { } queueScheme && SchemeImage(queueScheme.QueueButtonImage) is not null;
        _tree.FindByName("ControlBar.wnd:ProductionQueueWindow")!.SetHidden(view.Queue.IsEmpty);
        for (var index = 0; index < CommandBarModel.MaxQueueButtons; index++)
        {
            var place = _tree.FindByName($"ControlBar.wnd:ButtonQueue{(index + 1):00}")!;
            place.SetHidden(index >= view.Queue.Length && !emptyPlaces);
            place.SetEnabled(index < view.Queue.Length);
        }

        // ControlBar::setPortraitByObject: one selected object shows its SelectPortrait in the cameo window and the right-hand window loses its image; with nothing (or
        // several things) selected the unit window is hidden and the right-hand window shows the side's logo. The queue lies over both.
        var portrait = view.Context == CommandBarContext.Command && view.Queue.IsEmpty ? SelectedPortrait() : null;
        _tree.FindByName("ControlBar.wnd:WinUnitSelected")!.SetHidden(portrait is null);
        _tree.FindByName("ControlBar.wnd:CameoWindow")?.SetEnabledImage0(portrait);
        _showsSideLogo = portrait is null && view.Queue.IsEmpty;
    }

    /// <summary>The superweapon countdowns over the world view (see <see cref="SuperweaponTimers"/>); each owner's house colour, the names from the string table.</summary>
    private ImmutableArray<UiDrawCommand> BuildSuperweaponCommands(int width, int height)
    {
        // The list only changes with the logic tick (or a script's switch, which the next tick shows); a frame in between reuses it.
        if (_superweaponTick != Match.Tick || _superweaponEnabled != Superweapons.DisplayEnabled)
        {
            _superweaponLines = Superweapons.Collect(Match.World, label => _assets.Text.TryGet(label, out var resolved) ? resolved : null);
            _superweaponTick = Match.Tick;
            _superweaponEnabled = Superweapons.DisplayEnabled;
        }
        var lines = _superweaponLines;
        return SuperweaponTimers.Draw(lines, player => { var house = Match.Colors.Of(player); return new WndColor(house.Red, house.Green, house.Blue, 255); }, Match.Clock.OriginalFrames, width, height, _controlBarRoot.Rect.Y, _textMeasure, TextStyle, _scaleOptions);
    }

    private ImmutableArray<UiDrawCommand> BuildDynamicCommands()
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var cooldown = new WndColor(0, 0, 0, 160);

        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            var button = CommandBar[slot];
            if (button is null || button.Availability == ButtonAvailability.Hidden) continue;
            var rect = _tree.FindByName($"ControlBar.wnd:ButtonCommand{slot:00}")!.Rect;
            if (button.Button.ButtonImage.Length > 0 && _assets.Images.TryGet(button.Button.ButtonImage, out _))
                commands.Add(new UiImageDraw(button.Button.ButtonImage, rect, WndColor.White, false, false));
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
            var powerRect = SchemeRectOnScreen(scheme, scheme.PowerBar);
            var powerColor = Hud.Underpowered ? new WndColor(220, 40, 40, 255) : new WndColor(40, 200, 60, 255);
            var ratio = Hud.EnergyConsumption <= 0 ? 1f : Math.Clamp(Hud.EnergyProduction / (float)Hud.EnergyConsumption, 0f, 1f);
            commands.Add(new UiFillRect(powerRect with { Width = (int)(powerRect.Width * ratio) }, powerColor));
        }
        return commands.ToImmutable();
    }
}
