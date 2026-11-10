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
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;
using UiScaleMode = CnCNew.Ui.Layout.UiScaleMode;
using SettingsScaleMode = CnCNew.Ui.Settings.UiScaleMode;
using GameSettings = CnCNew.Ui.Settings.GameSettings;
using AspectHandling = CnCNew.Ui.Settings.AspectHandling;
using CnCNew.Ui.Skins;

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
public sealed partial class MatchController
{
    private readonly HudAssets _assets;
    private readonly TooltipPainter _tooltips;
    private readonly CommandButtonDescription _descriptions;
    private readonly UiWindowTree _tree;
    private readonly UiWindow _controlBarRoot;
    private readonly ModernControlBar? _modernBar;
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
        Retaliation = settings.Controls.Retaliation;

        _assets = HudAssets.Load(match.Data, language);
        _tooltips = new TooltipPainter(match.Data.Graph);
        _descriptions = new CommandButtonDescription(match.Data.Graph, _assets.Text);
        _textMeasure = textMeasure ?? new MonospaceTextMeasure();
        TextStyle = InGameUiStyle.FromGraph(match.Data.Graph);
        Messages = new HudMessageFeed(new UiMessageLog(TextStyle.Settings), _assets.Text, match.Data.Graph) { Clock = () => Match.Clock.OriginalFrames };
        var services = _assets.BuildTreeServices(textMeasure ?? new MonospaceTextMeasure());
        Skin = UiSkin.For(settings.Display.Skin);
        TextStyle = TextStyle.WithSkin(Skin);
        _scaleOptions = Skin.HudScale(new UiScaleOptions
        {
            Mode = MapScaleMode(settings.Display.UiScale),
            TextScale = settings.Accessibility.UiTextScalePercent / 100m,
        });
        match.Camera.KeepVerticalView = settings.Display.Aspect == AspectHandling.KeepVertical;
        ApplyDisplayOptions(settings);
        var viewport = new UiViewport(Math.Max(1, (int)match.Camera.ViewportSize.X), Math.Max(1, (int)match.Camera.ViewportSize.Y));
        _tree = new UiWindowTree(viewport, _scaleOptions, services);
        _tree.TooltipOverride = CommandButtonTooltip;

        // The modern skin keeps the bar's pictures (cameos, portraits, the small buttons) and changes their type; the band around them is its own (ModernControlBar). The in-game menu is drawn like the shell's dialogs.
        var controlBarRoots = _tree.AddLayout(Skin.Derive(_assets.ControlBar, _assets.HeaderTemplates, keepArt: true));
        _controlBarRoot = controlBarRoots.Single(window => window.Name == "ControlBar.wnd:ControlBarParent");
        _modernBar = Skin.IsModern ? new ModernControlBar(_tree, _controlBarRoot, Skin) { OpacityPercent = _barOpacityPercent } : null;
        if (_modernBar is not null) _tree.FindByName("ControlBar.wnd:ButtonGeneral")?.SetEnabledImage0(null);
        _leftHud = _tree.FindByName("ControlBar.wnd:LeftHUD") ?? throw new InvalidOperationException("ControlBar.wnd has no LeftHUD window.");
        // The retail file leaves the observer panels visible; this HUD is for the human player only.
        _tree.FindByName("ControlBar.wnd:ObserverPlayerInfoWindow")?.SetHidden(true);
        _tree.FindByName("ControlBar.wnd:ObserverPlayerListWindow")?.SetHidden(true);
        _tree.Register("ControlBarSystem", OnControlBarSystem);
        AddPowerBar();
        AddGeneralsWindow();
        AddDiplomacyWindow();

        var quitMenuRoots = _tree.AddLayout(Skin.Derive(_assets.QuitMenu, _assets.HeaderTemplates));
        _quitMenuRoot = quitMenuRoots.Single(window => window.Name == "QuitMenu.wnd:QuitMenuParent");
        _quitMenuRoot.SetHidden(true);
        _tree.Register("QuitMenuSystem", OnQuitMenuSystem);

        var humanParticipant = match.Data.Factions.FirstOrDefault(
            participant => participant.Faction.Name.Equals(match.HumanFaction, StringComparison.OrdinalIgnoreCase));
        if (humanParticipant is not null && _assets.Schemes.TryGetForSide(humanParticipant.Side, out var scheme)) _scheme = scheme;
        ApplySchemeImages();

        var buttons = CommandButtonCatalog.Build(match.Data.Graph);
        _shippedHotkeys = HotkeyMap.FromGraph(match.Data.Graph).WithEngineDefaults();
        var hotkeys = _shippedHotkeys.ApplySettings(settings.Controls.Hotkeys);
        Input = new GameInputController(match.Human, match.Commands, hotkeys, buttons,
            label => _assets.Text.TryGet(label, out var resolved) ? resolved : null,
            new GameInputSettings(MapMouseScheme(settings), Camera: MapCameraSettings(settings), Commands: new CommandSettings(PlacementViewAngle: template => match.Data.Visuals.TryGet(template, out var visual) ? visual.PlacementViewAngle : 0f)));
        Input.UiRequest += OnInputUiRequest;
        // InGameUI::selectMatchingAcrossScreen and friends tell the player what a selection of many units did, in the message list.
        Input.Selection.Announced += announcement => Messages.Message(announcement switch
        {
            SelectionAnnouncement.NothingSelected => "GUI:NothingSelected",
            SelectionAnnouncement.AcrossScreen => "GUI:SelectedAcrossScreen",
            _ => "GUI:SelectedAcrossMap",
        });

        match.BeforeTick = world => Input.Tick(world);
        match.CommandResolved += Input.Commands.OnResolved;
        match.TickStepped += OnTickStepped;
        match.CommandIssued += (command, result) =>
        {
            if (result.Accepted && command is CnCNew.Simulation.Commands.PurchaseUpgradeCommand purchase) Messages.ExpectUpgrade(purchase.Upgrade);
        };

        // A match of a server: its indicators (docs/GAME-HUD.md section 21). What happens once goes to the message list as a line of its own.
        if (match is LiveMatch { Remote: not null } served)
            Net = new NetHud(new LiveMatchNetSource(served), line => Messages.Log.Add(line, Messages.Clock())) { DiagnosticsDirectory = served.DiagnosticsDirectory, Skin = Skin };

        BeaconsEnabled = match is LiveMatch { Remote: not null, Setup.Spectator: false };
        Messages.PlayerName = player => Net?.NameOfSeat(player.Value);
        CommandBar = Input.CommandBar!.Build(match.World, Input.Selection);
        Hud = Input.CommandBar.Hud(match.World, match.Human);
        Generals = Input.CommandBar.Generals(match.World, match.Human);
        Overlay = HudOverlay.Empty;
    }

    public LiveGame Match { get; }
    public HudAssets Assets => _assets;

    /// <summary>The skin the HUD is drawn in (the settings' <c>display.skin</c>, docs/UI-MODERN.md).</summary>
    public UiSkin Skin { get; }

    /// <summary>The window tree of the control bar and the in-game menu (read by the readability check of the skins).</summary>
    public UiWindowTree Tree => _tree;
    public GameInputController Input { get; }
    public MatchStatsTracker Stats { get; }

    public CommandBarView CommandBar { get; private set; }
    public PlayerHud Hud { get; private set; }
    public HudOverlay Overlay { get; private set; }
    public bool IsMenuOpen { get; private set; }

    /// <summary>
    /// The centre, in screen pixels, of a window of the control bar or of the in-game menu a person could press now (it and everything above it is shown and enabled), or null.
    /// A scripted person (<see cref="CnCNew.Game.Shell.ShellScript"/>) finds the buttons of the HUD by their names here and clicks them with the device's events.
    /// </summary>
    public Vector2? PressablePoint(string windowName)
        => _tree.FindByName(windowName) is { IsVisible: true, IsEffectivelyEnabled: true } window ? new Vector2(window.Rect.X + window.Rect.Width / 2f, window.Rect.Y + window.Rect.Height / 2f) : null;

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

    /// <summary>The indicators of a match of a server (connection, waiting, pause, chat); null in every other game.</summary>
    public NetHud? Net { get; }

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
        // The chat line of a match of a server takes the keys while it is open, and opens on Enter (to all) and Backspace (to the allies).
        if (Net is { } net && WorldInputEnabled && net.HandleInput(inputEvent, IsMenuOpen)) return;
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
        // LeftHUDInput: a radar that is off eats the click over its window.
        if (!IsMenuOpen && !Match.RadarOn && _leftHud.Rect.Contains(Round(button.Position.X), Round(button.Position.Y)))
        {
            _uiOwnsMouse = true;
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
        // DiplomacyInput: the open window takes Escape for itself.
        if (IsDiplomacyOpen && key is { Key: KeyCode.Escape, IsDown: true, IsRepeat: false })
        {
            HideDiplomacy();
            return;
        }
        Input.Process(key, Match.World, Match.Camera);
    }

    /// <summary>Screen position to minimap world position, honoring the letterbox fit of a square-or-not minimap inside the (usually wider) LeftHUD rectangle.</summary>
    private bool TryMinimapWorld(Vector2 screen, out Vector2 world)
    {
        world = default;
        var rect = _leftHud.Rect;
        // LeftHUDInput: without a radar (or with it hidden) the window eats the input; the click does not reach the world.
        if (!rect.Contains((int)screen.X, (int)screen.Y) || !Match.RadarOn) return false;
        var minimap = Match.Minimap;
        var (scale, offset) = MinimapFit(rect);
        var px = (screen.X - offset.X) / scale;
        var py = (screen.Y - offset.Y) / scale;
        if (px < 0 || py < 0 || px >= minimap.Width || py >= minimap.Height) return false;
        world = minimap.PixelToWorld(new Vector2(px, py));
        return true;
    }

    /// <summary>The scale and the top-left corner of the minimap inside its window, the aspect-preserving rule the client draws it with.</summary>
    private (float Scale, Vector2 Offset) MinimapFit(UiRect rect)
    {
        var minimap = Match.Minimap;
        var scale = Math.Min(rect.Width / (float)minimap.Width, rect.Height / (float)minimap.Height);
        return (scale, new Vector2(rect.X + (rect.Width - minimap.Width * scale) / 2f, rect.Y + (rect.Height - minimap.Height * scale) / 2f));
    }

    /// <summary>
    /// The camera's footprint on the radar as lines of this frame: the radar's picture is composed four times a second, and the frame must follow the camera, which moves in
    /// every drawn frame. Drawn over the radar's window; the client's picture no longer holds it.
    /// </summary>
    private void AddRadarOutline(ImmutableArray<UiDrawCommand>.Builder commands, UiRect rect)
    {
        if (!Match.RadarOn) return;
        var (scale, offset) = MinimapFit(rect);
        var minimap = Match.Minimap;
        var clip = new UiRect((int)MathF.Floor(offset.X), (int)MathF.Floor(offset.Y), (int)MathF.Ceiling(minimap.Width * scale), (int)MathF.Ceiling(minimap.Height * scale));
        var footprint = Match.Camera.GroundFootprint();
        var thickness = MathF.Max(1f, MathF.Round(rect.Height / 170f));
        commands.Add(new UiPushClip(clip));
        for (var corner = 0; corner < footprint.Length; corner++)
        {
            var from = offset + minimap.WorldToPixel(footprint[corner]) * scale;
            var to = offset + minimap.WorldToPixel(footprint[(corner + 1) % footprint.Length]) * scale;
            commands.Add(new UiLineDraw(from.X, from.Y, to.X, to.Y, thickness, WndColor.White));
        }
        commands.Add(new UiPopClip());
    }

    private void OnInputUiRequest(MetaAction action)
    {
        if (action == MetaAction.Options) { OpenMenu(); return; }
        if (action == MetaAction.Diplomacy) { ToggleDiplomacy(); return; }
        if (action == MetaAction.PlaceBeacon) { ArmBeacon(); return; }
        if (action == MetaAction.DeleteBeacon) { RemoveSelectedBeacons(); return; }
        // MSG_META_VIEW_LAST_RADAR_EVENT: "You don't need radar for the space bar."
        if (action is MetaAction.ViewLastRadarEvent or MetaAction.ViewNewerRadarEvent) { ViewRadarHistory(older: action == MetaAction.ViewLastRadarEvent); return; }
        UiRequest?.Invoke(action);
    }

    /// <summary>How long the space bar can rest before its pointer into the history of radar events goes back to the newest one.</summary>
    public const long RadarHistoryResetMilliseconds = 3000;

    private int _radarHistoryIndex;
    private long _radarHistoryPressedMs = -RadarHistoryResetMilliseconds;
    private long _radarHistorySeen;

    /// <summary>
    /// The space bar. The original looks at the last radar event only (<c>getLastEventLoc</c>); here the first press looks at the newest event, each further press within
    /// <see cref="RadarHistoryResetMilliseconds"/> of the one before goes one event further back, Shift+Space one forward, and a rest of that long (or a new event) puts the pointer back on the newest.
    /// The ends of the history hold (no wrap-around).
    /// </summary>
    private void ViewRadarHistory(bool older)
    {
        var history = Messages.RadarHistory;
        if (history.Count == 0) return;
        var fresh = _clockMs - _radarHistoryPressedMs >= RadarHistoryResetMilliseconds || _radarHistorySeen != Messages.RadarHistoryVersion;
        if (fresh) _radarHistoryIndex = 0;
        else _radarHistoryIndex = Math.Clamp(_radarHistoryIndex + (older ? 1 : -1), 0, history.Count - 1);
        _radarHistoryPressedMs = _clockMs;
        _radarHistorySeen = Messages.RadarHistoryVersion;
        Match.Camera.CenterOn(history[history.Count - 1 - _radarHistoryIndex]);
    }

    private void OpenMenu()
    {
        if (IsMenuOpen) return;
        IsMenuOpen = true;
        _wasPausedBeforeMenu = Match.Paused;
        Match.Paused = true;
        HideGeneralsWindow();
        HideDiplomacy();
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

    // ControlBar::update: every 10 frames each button of the bar with a flash count left changes its look; the count stands still while the button is not in the bar.
    private const double FlashChangeSeconds = 10.0 / CnCNew.Data.OriginalFrame.PerSecond;
    private readonly Dictionary<string, int> _flashCounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flashing = new(StringComparer.Ordinal);
    private double _flashClock;

    /// <summary>A script's <c>CAMEO_FLASH</c> (<see cref="CnCNew.Game.Shell.IMissionHudControl.FlashCommandButton"/>).</summary>
    public void FlashCommandButton(string buttonName, int count)
    {
        if (count > 0) _flashCounts[buttonName] = count;
        else _flashCounts.Remove(buttonName);
    }

    /// <summary>The buttons that show the flash now.</summary>
    public IReadOnlyCollection<string> FlashingButtons => _flashing;

    private void StepFlashes(double deltaSeconds)
    {
        if (_flashCounts.Count == 0) return;
        _flashClock += deltaSeconds;
        while (_flashClock >= FlashChangeSeconds)
        {
            _flashClock -= FlashChangeSeconds;
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
            {
                if (CommandBar[slot] is not { Button.Name: var name } || !_flashCounts.TryGetValue(name, out var count)) continue;
                if (count % 2 == 0) _flashing.Add(name);
                else _flashing.Remove(name);
                if (--count > 0) _flashCounts[name] = count;
                else _flashCounts.Remove(name);
            }
        }
    }

    private void OnControlBarSystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind is not (UiEventKind.ButtonSelected or UiEventKind.ButtonSelectedRight)) return;
        ButtonClicked?.Invoke();
        var name = uiEvent.Source;
        if (PressPower(name)) return;
        if (SlotOf(name, "ControlBar.wnd:ButtonCommand") is { } slot && slot >= 1 && slot <= CommandBarModel.GridSlots)
        {
            // ControlBar::processContextSensitiveButtonClick: a button that is pressed stops flashing.
            if (CommandBar[slot] is { Button.Name: var pressed })
            {
                _flashCounts.Remove(pressed);
                _flashing.Remove(pressed);
            }
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
            case "ControlBar.wnd:OCLTimerSellButton":
                Input.Commands.Sell(CommandBar.Producer);
                break;
            case "ControlBar.wnd:ButtonIdleWorker":
                Input.DispatchMeta(MetaAction.SelectNextIdleWorker, Match.World, Match.Camera);
                break;
            case "ControlBar.wnd:ButtonOptions":
                OpenMenu();
                break;
            case "ControlBar.wnd:ButtonGeneral":
                CloseMenu();
                ToggleGeneralsWindow();
                break;
            case "ControlBar.wnd:PopupCommunicator":
                ToggleDiplomacy();
                break;
            case BeaconButton:
                ArmBeacon();
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
        Net?.Update();
        if (!IsMenuOpen)
        {
            var intent = Input.UpdateCamera(Match.Camera, (float)deltaSeconds);
            if (!WorldInputEnabled) intent = intent with { Scroll = Vector2.Zero, RotateDelta = 0f, ZoomDelta = 0f, Reset = false, CenterOn = null };
            ApplyCamera(intent);
            Match.Camera.Update((float)deltaSeconds, intent.Scroll != Vector2.Zero);
        }
        StepFlashes(deltaSeconds);

        // The world moved under a resting pointer: the context (and with it the cursor) is evaluated again every frame, not only on input events.
        if (!PointerOverWindow) Input.RefreshHint(Match.World, Match.Camera);
        Input.Selection.TrackIdleWorkers(Match.World);
        Input.Selection.SelectNewGunships(Match.World);
        CommandBar = Input.CommandBar!.Build(Match.World, Input.Selection);
        Hud = Input.CommandBar.Hud(Match.World, Match.Human);
        UpdateGenerals();
        DrainFeedback();
        Overlay = ComputeOverlay();
        // The building follows the cursor in the world, too: the object layer draws it translucent (InGameUI::handleBuildPlacements).
        Match.PlacementGhost = !IsMenuOpen && Input.Commands.Mode == CommandModeKind.Placement ? Input.Commands.Ghost : null;
        UpdateRadiusCursor();
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

    private readonly HotkeyMap _shippedHotkeys;

    /// <summary>The keys the player's saved bindings (<see cref="CnCNew.Ui.Settings.ControlSettings.Hotkeys"/>) put over the shipped map; takes effect with the next key event.</summary>
    public void ApplyHotkeys(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Input.Hotkeys = _shippedHotkeys.ApplySettings(settings.Controls.Hotkeys);
    }

    /// <summary>
    /// The options of <c>display</c> that change how the HUD is drawn and take effect with the next frame: the opacity of the modern bar (<see cref="DisplaySettings.BarOpacityPercent"/>), the style
    /// of the selection markers and which objects show a health bar and how thick (docs/GAME-HUD.md "Phase 65"). The skin itself is chosen when the controller is made.
    /// </summary>
    public void ApplyDisplayOptions(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        BarOpacityPercent = settings.Display.BarOpacityPercent;
        MarkerStyle = settings.Display.MarkerStyle;
        HealthBars = settings.Display.HealthBars;
        HealthBarThickness = settings.Display.HealthBarThickness;
    }

    public int BarOpacityPercent { get => _modernBar?.OpacityPercent ?? _barOpacityPercent; private set { _barOpacityPercent = value; if (_modernBar is not null) _modernBar.OpacityPercent = value; } }

    private int _barOpacityPercent = DisplaySettings.DefaultBarOpacityPercent;

    public SelectionMarkerStyle MarkerStyle { get; private set; }

    public HealthBarMode HealthBars { get; private set; }

    public HealthBarHeight HealthBarThickness { get; private set; }

    /// <summary>The viewer's option "Retaliation" (<see cref="CnCNew.Ui.Settings.ControlSettings.Retaliation"/>); the match follows it within a second.</summary>
    public bool Retaliation { get; set; }

    private void OnTickStepped(TickResult result)
    {
        Stats.Observe(result, Match.World);
        foreach (var ev in result.Events)
        {
            Messages.Observe(ev, Match.World, Match.Human);
            WorldAnimationOf(ev);
            if (ev.Kind == SimEventKind.SupplyDeposited && Match.World.FindEntity(ev.Entity) is { } earner && earner.Owner == Match.Human) AddFloatingMoney(earner, ev.Value);
        }
        // InGameUI::update runs once per original frame: the lines fade on the game clock's 1/30 s grid, not per simulation tick.
        if (OwnsMessageList && Match.Clock.IsOriginalFrameBoundary) Messages.Log.Update(Match.Clock.OriginalFrames);
        // Player::update: once a second the client posts its option when it differs from the player's mode in the world (MSG_ENABLE_RETALIATION_MODE).
        if (Match.Clock.IsOriginalFrameBoundary && Match.Clock.OriginalFrames % CnCNew.Data.OriginalFrame.PerSecond == 0 && !Match.IsOver
            && Match.World.RetaliationEnabled(Match.Human) != Retaliation)
        {
            var enabled = Retaliation;
            Match.Commands.Issue((sequence, tick) => new CnCNew.Simulation.Commands.SetRetaliationCommand(Match.Human, sequence, tick, enabled));
        }
        if (Match.IsOver && !_matchEndedFired)
        {
            _matchEndedFired = true;
            MatchEnded?.Invoke(Stats.Snapshot(Match.Players, Match.Winner, Match.World.Tick));
        }
    }

    // ------------------------------------------------------------------------------------------------------------ overlay

    /// <summary><c>Drawable::drawHealthBar</c>: under construction, or disabled other than by being held, the bar runs from blue to cyan.</summary>
    private bool BarIsBlue(Entity entity)
    {
        var disabled = entity.DisabledFlags(Match.World.Tick);
        return entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) || (disabled != 0 && (disabled & CnCNew.Simulation.World.DisabledMask.Held) == 0);
    }

    /// <summary>
    /// Which objects the always-shown bars of <see cref="HealthBars"/> are for: what a player can select and shoot at and that is alive - not a prop, a mine, a crate, a projectile or
    /// anything the original keeps out of the interface (<c>IGNORED_IN_GUI</c>, and <c>FORCEATTACKABLE</c> for which <c>Drawable::drawHealthBar</c> returns); an object with no health has no bar.
    /// </summary>
    private static bool ShowsAmbientBar(Entity unit)
        => !unit.EffectivelyDead && unit.Body is { } body && body.MaxHealth.Raw != 0 && body.Health.Raw != 0
           && unit.Template.IsKindOf("SELECTABLE") && !unit.Template.IsKindOf("NO_SELECT") && !unit.Template.IsKindOf("IGNORED_IN_GUI") && !unit.Template.IsKindOf("FORCEATTACKABLE")
           && !unit.Template.IsKindOf("MINE") && !unit.Template.IsKindOf("PROJECTILE") && !unit.Template.IsKindOf("CRATE");

    private HudOverlay ComputeOverlay()
    {
        var world = Match.World;
        var view = Match.Camera;

        // Only the plain pointer draws a box: a drag while a building is placed turns the building, and a pending target takes clicks (OnGesture never selects in either mode).
        var box = SelectionBoxOverlay.None;
        if (Input.Commands.Mode == CommandModeKind.None && Input.State.HasMouse && Input.State.IsDragging(MouseButton.Left) && Input.State.PressPosition(MouseButton.Left) is { } start)
            box = new SelectionBoxOverlay(true, start, Input.State.MousePosition);

        var selected = ImmutableArray.CreateBuilder<UnitMarker>();
        var rally = ImmutableArray.CreateBuilder<RallyPointMarker>();
        var rallyPlaces = ImmutableArray.CreateBuilder<Vector3>();
        var activeKind = Input.Selection.ActiveSubgroupIn(world);
        foreach (var id in Input.Selection.Selected)
        {
            if (world.FindEntity(id) is not { } entity) continue;
            if (Input.Selection.Picker.TryProject(view, entity, out var projected))
            {
                var health = entity.Body is { } body && body.MaxHealth.Raw != 0 ? (float)(body.Health / body.MaxHealth).ToDecimal() : 1f;
                var mine = entity.Owner == Match.Human;
                var seats = mine && entity.Container is { ContainMax: > 0 } held ? ((int, int, int)?)(held.ContainMax, held.Contained.Count, held.Contained.Count(rider => world.FindEntity(rider)?.Template.IsKindOf("INFANTRY") == true)) : null;
                selected.Add(new UnitMarker(entity.Id, projected.Center, projected.Radius, health, entity.Owner == Match.Human, world.AreEnemies(Match.Human, entity.Owner), entity.Template.IsKindOf("STRUCTURE"))
                {
                    Ammo = mine ? world.AmmoPips(entity) : null,
                    Seats = seats,
                    GroupNumber = mine ? Input.Selection.GroupOf(entity.Id) : -1,
                    GroupText = mine && Input.Selection.GroupOf(entity.Id) is >= 0 and var group ? _assets.Text.Get("NUMBER:" + group.ToString(CultureInfo.InvariantCulture)) : string.Empty,
                    ActiveKind = activeKind is not null && entity.Template.Name.Equals(activeKind, StringComparison.Ordinal),
                    Damage = entity.DamageState(),
                    Blue = BarIsBlue(entity),
                });
            }
            if (SimulationQueries.ProductionOf(entity) is { } production)
            {
                var state = SimulationQueries.Describe(production);
                if (state.Bool("hasRally") && state.Values.TryGetValue("rallyX", out var rawX) && rawX is Fixed rallyX
                    && state.Values.TryGetValue("rallyY", out var rawY) && rawY is Fixed rallyY
                    && view.TryWorldToScreen(OnGround(new Vector2((float)rallyX.ToDecimal(), (float)rallyY.ToDecimal())), out var rallyScreen))
                {
                    rally.Add(new RallyPointMarker(entity.Id, rallyScreen));
                    rallyPlaces.Add(OnGround(new Vector2((float)rallyX.ToDecimal(), (float)rallyY.ToDecimal())));
                }
            }
        }
        Match.RallyMarkers = rallyPlaces.ToImmutable();
        if (Input.State.HasMouse && !PointerOverWindow && Input.Selection.Picker.Pick(world, Match.Human, view, Input.State.MousePosition) is { } over && !Input.Selection.Selected.Contains(over.Id)
            && Input.Selection.Picker.TryProject(view, over, out var hovered))
        {
            var mine = over.Owner == Match.Human;
            var seats = mine && over.Container is { ContainMax: > 0 } held ? ((int, int, int)?)(held.ContainMax, held.Contained.Count, held.Contained.Count(rider => world.FindEntity(rider)?.Template.IsKindOf("INFANTRY") == true)) : null;
            selected.Add(new UnitMarker(over.Id, hovered.Center, hovered.Radius, over.Body is { } body && body.MaxHealth.Raw != 0 ? (float)(body.Health / body.MaxHealth).ToDecimal() : 1f, mine, world.AreEnemies(Match.Human, over.Owner), over.Template.IsKindOf("STRUCTURE"))
            {
                Hovered = true,
                Ammo = mine ? world.AmmoPips(over) : null,
                Seats = seats,
                Damage = over.DamageState(),
                Blue = BarIsBlue(over),
            });
        }
        // display.healthBarMode: every object in view, or every damaged one, shows its bar beside what is selected or under the pointer.
        if (HealthBars != HealthBarMode.SelectedOnly && IconUiEnabled)
        {
            var marked = new HashSet<EntityId>(selected.Select(marker => marker.Entity));
            foreach (var drawable in Match.Frame.Drawables)
            {
                if (!drawable.Entity.IsValid || marked.Contains(drawable.Entity) || world.FindEntity(drawable.Entity) is not { } unit || !ShowsAmbientBar(unit)) continue;
                if (!HealthBarRule.Shows(HealthBars, false, unit.Body is { } damaged && damaged.Health < damaged.MaxHealth)) continue;
                if (!Input.Selection.Picker.TryProject(view, unit, out var around)) continue;
                var health = unit.Body is { } body && body.MaxHealth.Raw != 0 ? (float)(body.Health / body.MaxHealth).ToDecimal() : 1f;
                selected.Add(new UnitMarker(unit.Id, around.Center, around.Radius, health, unit.Owner == Match.Human, world.AreEnemies(Match.Human, unit.Owner), unit.Template.IsKindOf("STRUCTURE"))
                {
                    BarOnly = true,
                    Damage = unit.DamageState(),
                    Blue = BarIsBlue(unit),
                });
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

        // Drawable::drawIconUI runs for every drawable. The veterancy comes last there, so that it shows over the pips; a construction's percent shows on dead things too.
        var ranks = ImmutableArray.CreateBuilder<VeterancyMarker>();
        var icons = ImmutableArray.CreateBuilder<UnitIcon>();
        var labels = ImmutableArray.CreateBuilder<UnitLabel>();
        foreach (var drawable in IconUiEnabled ? Match.Frame.Drawables : ImmutableArray<CnCNew.Presentation.Objects.DrawableInstance>.Empty)
        {
            if (!drawable.Entity.IsValid || world.FindEntity(drawable.Entity) is not { } unit) continue;
            var building = unit.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) && !unit.HasStatus(SoldStatus);
            var bonuses = unit.WeaponBonusConditions;
            var disabled = !unit.EffectivelyDead && (unit.DisabledFlags(world.Tick) & IconDisables) != 0;
            var enthusiastic = !unit.EffectivelyDead && (bonuses & EnthusiasticBonus) != 0;
            var carBomb = unit.Owner == Match.Human && unit.HasStatus(CarBombStatus);
            var bomb = IsStickyBomb(unit) && unit.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.StickyBombUpdate>().FirstOrDefault() is { Target.IsValid: true } sticky ? sticky : null;
            if (!building && unit.VeterancyLevel == 0 && !disabled && !enthusiastic && !carBomb && bomb is null) continue;
            if (!Input.Selection.Picker.TryProject(view, unit, out var projected)) continue;
            var bar = MathF.Max(26f, projected.Radius * (drawable.IsStructure ? 1.1f : 1.7f));
            var (barLeft, barTop) = (projected.Center.X - bar / 2f, projected.Center.Y - MathF.Max(projected.Radius, 6f) - 11f);
            if (building) labels.Add(new UnitLabel(unit.Id, new Vector2(projected.Center.X, projected.Center.Y), _assets.Text.Format("CONTROLBAR:UnderConstructionDesc", (decimal)(unit.ConstructionShare.ToDecimal() * 100m))));
            if (unit.EffectivelyDead || unit.Template.IsKindOf("IGNORED_IN_GUI")) continue;
            if (enthusiastic && Animations.FrameAt((bonuses & SubliminalBonus) != 0 ? "Subliminal" : "Enthusiastic", _clockMs, unit.Id.Value) is { } frame)
            {
                var size = (int)(bar * (drawable.IsStructure || drawable.Visual.IsKindOf("HUGE_VEHICLE") ? 1f : drawable.Visual.IsKindOf("VEHICLE") ? 0.75f : 0.5f));
                icons.Add(new UnitIcon(unit.Id, frame, new UiRect((int)(barLeft + bar * 0.25f - size * 0.5f), (int)(barTop + 5f + size * 0.25f), size, size)));
            }
            if (disabled && Animations.FrameAt("Disabled", _clockMs, 0) is { } lightning)
            {
                var size = Math.Max(12, (int)(bar * 0.3f));
                icons.Add(new UnitIcon(unit.Id, lightning, new UiRect((int)barLeft, (int)barTop - size, size, size)));
            }
            if (carBomb && Animations.FrameAt("CarBomb", _clockMs, 0) is { } burning)
            {
                var size = (int)(bar * 0.5f);
                icons.Add(new UnitIcon(unit.Id, burning, new UiRect((int)(barLeft + bar / 2f - size / 2f), (int)barTop + 3 + BombIconOffset, size, size)));
            }
            if (bomb is not null)
            {
                // drawBombed: a remote charge shows its picture, a timed one the same with the seconds that are left over it.
                var size = (int)(bar * 0.65f);
                var at = new UiRect((int)(barLeft + bar / 2f - size / 2f), (int)barTop + 3 + BombIconOffset, size, size);
                if (Animations.FrameAt("BombRemote", _clockMs, 0) is { } charge) icons.Add(new UnitIcon(unit.Id, charge, at));
                if (unit.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.LifetimeUpdate>().FirstOrDefault() is { DieTick: > 0 } fuse
                    && Animations.CountdownFrame("BombTimed", (fuse.DieTick - world.Tick + world.Time.TicksPerSecond - 1) / world.Time.TicksPerSecond) is { } count)
                    icons.Add(new UnitIcon(unit.Id, count, at));
            }
            if (unit.VeterancyLevel > 0) ranks.Add(new VeterancyMarker(unit.Id, new Vector2(projected.Center.X + bar / 2f, barTop), unit.VeterancyLevel));
        }

        FloatingMoneyLabels(labels);
        DrawWorldAnimations(icons);
        var hint = DescribeHint(Input.Hint);
        return new HudOverlay(box, selected.ToImmutable(), rally.ToImmutable(), orderLines.ToImmutable(), placement, hint, [..Messages.Log.Lines.Select(line => line.Text)], heals.ToImmutable(),
            waypointPaths.ToImmutable(), hints.ToImmutable(), ranks.ToImmutable(), icons.ToImmutable(), labels.ToImmutable());

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
            return _leftHud.Rect.Contains(Round(position.X), Round(position.Y)) || _tree.InputTarget(Round(position.X), Round(position.Y)) is not null;
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
    public bool IsWindowEnabled(string decoratedName) => _tree.FindByName(decoratedName)?.IsEnabled ?? false;
    public bool IsWindowShown(string decoratedName) => _tree.FindByName(decoratedName)?.IsVisible ?? false;
    public int WindowProgress(string decoratedName) => _tree.FindByName(decoratedName)?.ProgressPercent ?? -1;

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

        _modernBar?.Place();
        RefreshDynamicWindows();
        RefreshGeneralsWindow();
        RefreshBeaconButton();
        if (IsDiplomacyOpen) RefreshDiplomacy();
        var baseList = _tree.BuildDrawList();
        var extra = BuildDynamicCommands();
        var viewportRect = new UiRect(0, 0, width, _controlBarRoot.Rect.Y);
        // The scheme art is drawn by two marker windows of the control bar (W3DCommandBarBackgroundDraw, W3DCommandBarForegroundDraw): the background under the windows that
        // follow the marker, the foreground over those before it. The world-space feedback (selection rings, health bars, orders, rally points, placement ghost, healing icons)
        // lies between the 3D scene and the control bar chrome.
        var commands = HudOverlayRenderer.Build(Overlay, _clockMs, new HudMarkerOptions(MarkerStyle, HealthBarThickness)).AddRange(BarChrome()).AddRange(ResolveControlBarCallbacks(baseList.Commands)).AddRange(extra).AddRange(BuildSuperweaponCommands(width, height))
            .Add(new UiCustomDraw("Viewport3D", "Viewport", viewportRect));
        // InGameUI::postDraw: the message lines lie over the windows.
        if (OwnsMessageList && !Messages.Log.IsEmpty) commands = commands.AddRange(MissionTextOverlay.Messages(Messages.Log, TextStyle, _scaleOptions, _textMeasure, width, height));
        // The indicators of a match of a server lie over the HUD.
        if (Net is { } net) commands = commands.AddRange(net.Build(width, height, _textMeasure));
        // Mouse::drawTooltip lies over everything, the window tree's own tooltips (TOOLTIPTEXT) and the command button descriptions alike.
        if (_tree.Tooltip is { } tooltip) commands = commands.AddRange(_tooltips.Build(tooltip, _tree.Viewport, _scaleOptions, _tree.Services.TextMeasure, Skin));
        return baseList with { Commands = commands };
    }

    /// <summary>The band of the modern skin under the bar's windows (<see cref="ModernControlBar"/>); nothing while a mission has hidden the bar, and nothing in the original skin, whose art the marker windows draw.</summary>
    private ImmutableArray<UiDrawCommand> BarChrome() => _modernBar is { } bar && !_controlBarRoot.IsHidden ? bar.Chrome() : [];

    /// <summary>What the selection plate of the modern bar shows.</summary>
    private ModernSelection? SelectionForPlate()
    {
        var entities = Input.Selection.Selected.Select(Match.World.FindEntity).Where(entity => entity is not null).Select(entity => entity!).ToArray();
        if (entities.Length == 0) return null;
        var first = entities[0];
        var health = first.Body is { } body && body.MaxHealth.Raw != 0 ? (float)(body.Health / body.MaxHealth).ToDecimal() : 1f;
        if (entities.Length == 1) return new ModernSelection(_descriptions.DisplayName(first.Template.Name), 1, health, []);
        var kinds = ImmutableArray.CreateBuilder<(string, int)>();
        var active = Input.Selection.ActiveSubgroupIn(Match.World);
        var activeIndex = -1;
        foreach (var group in entities.GroupBy(entity => entity.Template.Name, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
            if (Match.Data.Visuals.TryGet(group.Key, out var visual) && visual.SelectPortrait is { Length: > 0 } image && _assets.Images.TryGet(image, out _))
            {
                if (group.Key.Equals(active, StringComparison.Ordinal)) activeIndex = kinds.Count;
                kinds.Add((image, group.Count()));
            }
        // The active kind of a mixed selection (Tab) names itself, with its place among the kinds.
        if (CommandBar.Group is { Kind: SelectionGroupKind.Subgroup } subgroup)
            return new ModernSelection(string.Create(CultureInfo.InvariantCulture, $"{subgroup.Count} x {_descriptions.DisplayName(subgroup.Template)} ({subgroup.Index}/{subgroup.Total})"), entities.Length, health, kinds.ToImmutable(), activeIndex);
        // A selection of one kind is named after it; a mixed one is named after none of its kinds (the original shows no single name for a group): its count, the kinds below.
        var count = entities.Length.ToString(CultureInfo.InvariantCulture);
        var mixed = entities.Any(entity => !entity.Template.Name.Equals(first.Template.Name, StringComparison.Ordinal));
        return new ModernSelection(mixed ? count + " units" : count + " x " + _descriptions.DisplayName(first.Template.Name), entities.Length, health, kinds.ToImmutable(), activeIndex);
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
        if (window.Name == "ControlBar.wnd:PopupCommunicator") return CommunicatorTooltip();
        if (GeneralsButton(window.Name) is { } science) return _descriptions.Build(science, Match.World, Match.Human, null).ToTooltip() is { Length: > 0 } text ? text : null;
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
        var result = ImmutableArray.CreateBuilder<UiDrawCommand>(commands.Length + (_scheme?.ImageParts.Length ?? 0) + 6);
        foreach (var command in commands)
        {
            switch (command)
            {
                case UiCustomDraw { Callback: "W3DLeftHUDDraw" } radar:
                    result.Add(radar);
                    AddRadarOutline(result, radar.Rect);
                    break;
                case UiCustomDraw { Callback: "W3DCommandBarBackgroundDraw" or "W3DCommandBarForegroundDraw" } when _modernBar is not null:
                    break;
                case UiCustomDraw { Callback: "W3DCommandBarBackgroundDraw" } when _scheme is { } scheme:
                    AddSchemeParts(result, scheme, scheme.BackgroundParts);
                    break;
                case UiCustomDraw { Callback: "W3DCommandBarForegroundDraw" } when _scheme is { } scheme:
                    AddSchemeParts(result, scheme, scheme.ForegroundParts);
                    break;
                case UiCustomDraw { Callback: "W3DRightHUDDraw", Window: "ControlBar.wnd:RightHUD" } rightHud when _scheme is { } scheme:
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
        if (_modernBar is null) Button("ControlBar.wnd:ButtonGeneral", scheme.GeneralImages);
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
        // The bar of a structure group or of the active kind of a mixed selection shows the portrait of that kind, with the count over it (GroupTag).
        string name;
        if (CommandBar.Group is { } group) name = group.Template;
        else if (Input.Selection.Selected.ToArray() is [var only] && Match.World.FindEntity(only) is { } entity) name = entity.Template.Name;
        else return null;
        return Match.Data.Visuals.TryGet(name, out var visual) && visual.SelectPortrait is { Length: > 0 } image && _assets.Images.TryGet(image, out _) ? image : null;
    }

    private static readonly int SoldStatus = CnCNew.Simulation.Modules.ObjectStatusBits.Index("SOLD");
    private static readonly uint EnthusiasticBonus = BonusBit("ENTHUSIASTIC"), SubliminalBonus = BonusBit("SUBLIMINAL");
    private static uint BonusBit(string name) => CnCNew.Data.Definitions.NameTablesPhase5.WeaponBonusCondition.TryIndexOf(name, out var index) ? 1u << index : throw new InvalidOperationException(name);

    private static readonly int CarBombStatus = CnCNew.Simulation.Modules.ObjectStatusBits.Index("IS_CARBOMB");

    /// <summary><c>BOMB_ICON_EXTRA_OFFSET</c>.</summary>
    private const int BombIconOffset = 5;

    // By template, so that the overlay does not walk the modules of every drawable in every frame.
    private readonly Dictionary<string, bool> _stickyBombs = [];

    private bool IsStickyBomb(CnCNew.Simulation.World.Entity unit)
    {
        if (_stickyBombs.TryGetValue(unit.Template.Name, out var known)) return known;
        return _stickyBombs[unit.Template.Name] = unit.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.StickyBombUpdate>().Any();
    }

    /// <summary>The disables <c>drawDisabled</c> shows the lightning for.</summary>
    private const CnCNew.Simulation.World.DisabledMask IconDisables = CnCNew.Simulation.World.DisabledMask.Hacked | CnCNew.Simulation.World.DisabledMask.Paralyzed | CnCNew.Simulation.World.DisabledMask.Emp
        | CnCNew.Simulation.World.DisabledMask.Subdued | CnCNew.Simulation.World.DisabledMask.Underpowered;

    /// <summary><c>InGameUI::addFloatingText</c>: what a player earned, over the object that earned it. <c>FloatingTextTimeOut</c> is ten frames of the original, then it fades.</summary>
    private readonly List<(Vector3 Place, string Text, WndColor Color, long Frame)> _floatingMoney = [];

    private const long FloatingFrames = 10, FloatingFadeFrames = 30;

    private void AddFloatingMoney(Entity earner, long value)
    {
        var house = Match.Colors.Of(earner.Owner);
        var (x, y) = ((float)earner.X.ToDecimal(), (float)earner.Y.ToDecimal());
        _floatingMoney.Add((new Vector3(x, y, Match.Heights.HeightAt(x, y)), _assets.Text.Format("GUI:AddCash", value), new WndColor(house.Red, house.Green, house.Blue, 230), Match.Clock.OriginalFrames));
    }

    /// <summary><c>InGameUI::addWorldAnimation</c>, <c>WORLD_ANIM_FADE_ON_EXPIRE</c>: a picture that stands in the world, rises and fades in its last second.</summary>
    private readonly List<(string Name, Vector3 Place, long Frame, long Frames, float RisePerFrame)> _worldAnimations = [];

    private const long WorldAnimationFadeFrames = 30;

    private void WorldAnimationOf(SimEvent ev)
    {
        var settings = Match.Data.Fx.Settings;
        var info = ev.Kind switch
        {
            SimEventKind.RankGained => settings.LevelGain,
            SimEventKind.HealedInBurst => settings.Healed,
            SimEventKind.CrateAnimation => new CnCNew.Presentation.Fx.WorldAnimationInfo(ev.Text, (ev.Value & uint.MaxValue) / 1000f, ev.Value >> 32),
            _ => null,
        };
        if (info is null || info.Name.Length == 0 || info.Seconds <= 0f || Match.World.FindEntity(ev.Entity) is not { } subject) return;
        // Object::onVeterancyLevelChanged: "hideAnimationForStealth".
        if (ev.Kind == SimEventKind.RankGained && subject.Owner != Match.Human && subject.HasStatus(StealthedStatus) && !subject.HasStatus(DetectedStatus) && !subject.HasStatus(DisguisedStatus)) return;
        var z = (float)subject.Z.ToDecimal() + (ev.Kind == SimEventKind.CrateAnimation ? 0f : (float)subject.Template.GeometryHeight.ToDecimal());
        _worldAnimations.Add((info.Name, new Vector3((float)subject.X.ToDecimal(), (float)subject.Y.ToDecimal(), z), Match.Clock.OriginalFrames, (long)MathF.Ceiling(info.Seconds * CnCNew.Data.OriginalFrame.PerSecond),
            info.RisePerSecond / CnCNew.Data.OriginalFrame.PerSecond));
    }

    private void DrawWorldAnimations(ImmutableArray<UnitIcon>.Builder icons)
    {
        if (_worldAnimations.Count == 0) return;
        var now = Match.Clock.OriginalFrames;
        _worldAnimations.RemoveAll(animation => now >= animation.Frame + animation.Frames);
        foreach (var (name, place, frame, frames, rise) in _worldAnimations)
        {
            var at = place + new Vector3(0f, 0f, rise * (now - frame));
            if (Match.FxView.IsShrouded(at) || !Match.Camera.TryWorldToScreen(at, out var screen) || Animations.FrameAt(name, _clockMs, 0) is not { } picture || !_assets.Images.TryGet(picture, out var image)) continue;
            var left = frame + frames - now;
            var alpha = left < WorldAnimationFadeFrames ? (byte)(255 * left / WorldAnimationFadeFrames) : (byte)255;
            icons.Add(new UnitIcon(default, picture, new UiRect((int)screen.X - image.Width / 2, (int)screen.Y - image.Height / 2, image.Width, image.Height)) { Alpha = alpha });
        }
    }

    private static readonly int StealthedStatus = CnCNew.Simulation.Modules.ObjectStatusBits.Index("STEALTHED"), DetectedStatus = CnCNew.Simulation.Modules.ObjectStatusBits.Index("DETECTED"),
        DisguisedStatus = CnCNew.Simulation.Modules.ObjectStatusBits.Index("DISGUISED");

    private void FloatingMoneyLabels(ImmutableArray<UnitLabel>.Builder labels)
    {
        var view = Match.Camera;
        var now = Match.Clock.OriginalFrames;
        _floatingMoney.RemoveAll(entry => now - entry.Frame > FloatingFrames + FloatingFadeFrames);
        foreach (var (place, text, color, frame) in _floatingMoney)
        {
            if (!view.TryWorldToScreen(place, out var screen)) continue;
            var age = now - frame;
            var alpha = age <= FloatingFrames ? color.A : (byte)(color.A * (FloatingFadeFrames - (age - FloatingFrames)) / FloatingFadeFrames);
            // FloatingTextMoveUpSpeed: one pixel a frame of the original.
            labels.Add(new UnitLabel(default, new Vector2(screen.X, screen.Y - age), text) { Color = color with { A = alpha } });
        }
    }

    private Anim2DTable? _animations;

    /// <summary><c>Animation2D.ini</c>.</summary>
    private Anim2DTable Animations => _animations ??= Anim2DTable.Load(Match.Data.Content);

    private ImmutableDictionary<string, string>? _upgradeImages;

    /// <summary><c>ButtonImage</c> of every upgrade, by its name.</summary>
    private ImmutableDictionary<string, string> UpgradeImages => _upgradeImages ??= Match.Data.Graph.Definitions.OfType<CnCNew.Data.Definitions.UpgradeDefinition>()
        .Select(upgrade => (upgrade.OriginalName, Image: upgrade.Fields.LastOrDefault(entry => entry.Name.Equals("ButtonImage", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty))
        .Where(upgrade => upgrade.Image.Length > 0).GroupBy(upgrade => upgrade.OriginalName, StringComparer.Ordinal).ToImmutableDictionary(group => group.Key, group => group.Last().Image, StringComparer.Ordinal);

    /// <summary><c>ControlBar::setPortraitByObject</c>: the upgrades the object names beside its portrait, lit when it or its player has them.</summary>
    private void ShowUpgradeCameos(bool shown)
    {
        var entity = shown && Input.Selection.Selected.ToArray() is [var only] ? Match.World.FindEntity(only) : null;
        var cameos = entity is not null && Match.Data.Visuals.TryGet(entity.Template.Name, out var visual) ? visual.UpgradeCameos : [];
        for (var place = 0; place < 5; place++)
        {
            if (_tree.FindByName($"ControlBar.wnd:UnitUpgrade{place + 1}") is not { } window) continue;
            var name = place < cameos.Length ? cameos[place] : string.Empty;
            if (entity is null || name.Length == 0 || !Match.World.Upgrades.TryGet(name, out var upgrade) || !UpgradeImages.TryGetValue(name, out var image) || !_assets.Images.TryGet(image, out _))
            {
                window.SetHidden(true);
                continue;
            }
            window.SetHidden(false);
            window.SetEnabledImage0(image);
            window.SetEnabled(entity.CompletedUpgrades.Has(upgrade.Index) || Match.World.PlayerUpgrades(entity.Owner).Has(upgrade.Index));
        }
    }

    private void RefreshDynamicWindows()
    {
        var view = CommandBar;
        // InGameUI::update: the money window shows "GUI:ControlBarMoneyDisplay" with the local player's money; ControlBarScheme::updateControlBarScheme
        // puts it at the scheme's money rectangle. The layout file keeps a placeholder text and a 100x100 rectangle at the top left.
        _tree.FindByName("ControlBar.wnd:MoneyDisplay")?.SetText(_assets.Text.Format("GUI:ControlBarMoneyDisplay", (int)Hud.Money.ToDecimal()));
        // ControlBarScheme::init puts the money window, the power window (the bar is drawn there, and its description pops up there) and the bar's own buttons at the scheme's rectangles.
        if (_scheme is { } placed && _modernBar is null) PlaceSchemeWindows(placed);
        _tree.FindByName("ControlBar.wnd:CommandWindow")!.SetHidden(view.Context is not (CommandBarContext.Command or CommandBarContext.MultiSelect));
        _tree.FindByName("ControlBar.wnd:UnderConstructionWindow")!.SetHidden(view.Context != CommandBarContext.UnderConstruction);
        // InGameUI::updateIdleWorker: the button is enabled while the local player has an idle worker, disabled otherwise.
        _tree.FindByName("ControlBar.wnd:ButtonIdleWorker")?.SetEnabled(Input.Selection.IdleWorkerCount > 0);
        RefreshOclTimer(view);
        var underConstructionDesc = _tree.FindByName("ControlBar.wnd:UnderConstructionDesc");
        if (view.Context == CommandBarContext.UnderConstruction && Match.World.FindEntity(view.Producer) is { } site) underConstructionDesc?.SetText(site.Template.Name);

        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            var window = _tree.FindByName($"ControlBar.wnd:ButtonCommand{slot:00}")!;
            var button = view[slot];
            window.SetHidden(button is null || button.Availability == ButtonAvailability.Hidden);
            if (button is not null) window.SetEnabled(button.IsEnabled);
            // GadgetCheckLikeButtonSetVisualCheck: a check-like button shows what is on.
            window.SetChecked(button is { Availability: ButtonAvailability.Active });
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
        var portrait = view.Context is CommandBarContext.Command or CommandBarContext.OclTimer && view.Queue.IsEmpty ? SelectedPortrait() : null;
        _tree.FindByName("ControlBar.wnd:WinUnitSelected")!.SetHidden(portrait is null);
        _tree.FindByName("ControlBar.wnd:CameoWindow")?.SetEnabledImage0(portrait);
        ShowUpgradeCameos(portrait is not null);
        RefreshPowerBar();
        _showsSideLogo = portrait is null && view.Queue.IsEmpty;
    }

    /// <summary>
    /// <c>ControlBar::populateOCLTimer</c> and <c>updateOCLTimerTextDisplay</c>: the window of a building that creates something on a timer shows "minutes:seconds" and the share of the delay
    /// that has passed; its sell button shows for a building that is not a tech building.
    /// </summary>
    private void RefreshOclTimer(CommandBarView view)
    {
        var shown = view.Context == CommandBarContext.OclTimer && view.Timer is not null;
        _tree.FindByName("ControlBar.wnd:OCLTimerWindow")?.SetHidden(!shown);
        if (!shown) return;
        var timer = view.Timer!;
        _tree.FindByName("ControlBar.wnd:OCLTimerSellButton")?.SetHidden(!timer.CanSell);
        _tree.FindByName("ControlBar.wnd:OCLTimerProgressBar")?.SetProgress((int)(timer.Fraction * 100f));
        var seconds = timer.RemainingSeconds;
        _tree.FindByName("ControlBar.wnd:OCLTimerStaticText")?.SetText(_assets.Text.Format(seconds % 60 < 10 ? "CONTROLBAR:OCLTimerDescWithPadding" : "CONTROLBAR:OCLTimerDesc", seconds / 60, seconds % 60));
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

    private static readonly WndColor CantAffordTint = new(144, 144, 144, 255);

    /// <summary>
    /// What <c>W3DGadgetPushButtonImageDraw</c> lays over a cameo button (<c>WIN_STATUS_USE_OVERLAY_STATES</c>): the clock of a power or a clip that is not ready (<c>GadgetButtonDrawInverseClock</c>),
    /// then, on an enabled button, <c>Cameo_push</c> while it is pushed (held down, or a check-like button that is on) and <c>Cameo_hilited</c> while the pointer lies on it - the press and
    /// release a person sees.
    /// </summary>
    private void AddCameoState(ImmutableArray<UiDrawCommand>.Builder commands, UiWindow window, CommandBarButton button, WndColor clock)
    {
        if (button.Availability == ButtonAvailability.NotReady && button.ReadyFraction is { } fraction) commands.AddRange(UiClock.Remaining(window.Rect, (int)(fraction * 100), clock));
        if (!window.IsEnabled) return;
        var overlay = window.IsSelected ? "Cameo_push" : window.IsHilited ? "Cameo_hilited" : null;
        if (overlay is not null && _assets.Images.TryGet(overlay, out _)) commands.Add(new UiImageDraw(overlay, window.Rect, WndColor.White, false, false));
    }

    private ImmutableArray<UiDrawCommand> BuildDynamicCommands()
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        // ControlBarScheme's BuildUpClockColor (R:0 G:0 B:0 A:160 in every shipped scheme).
        var cooldown = new WndColor(0, 0, 0, 160);
        // populateOCLTimer: setControlCommand(Command_Sell) gives the sell button its cameo.
        if (CommandBar is { Context: CommandBarContext.OclTimer, Timer.CanSell: true } && _tree.FindByName("ControlBar.wnd:OCLTimerSellButton") is { } sellButton && _assets.Images.TryGet("SSSell2", out _))
            commands.Add(new UiImageDraw("SSSell2", sellButton.Rect, WndColor.White, false, false));

        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            var button = CommandBar[slot];
            if (button is null || button.Availability == ButtonAvailability.Hidden) continue;
            var window = _tree.FindByName($"ControlBar.wnd:ButtonCommand{slot:00}")!;
            var rect = window.Rect;
            // populateInvDataCallback: an exit button wears the cameo and the rank of who sits there.
            var rider = button.Rider.IsValid ? Match.World.FindEntity(button.Rider) : null;
            var cameo = rider is not null && Match.Data.Visuals.TryGet(rider.Template.Name, out var seated) && seated.ButtonImage is { Length: > 0 } own ? own : button.Button.ButtonImage;
            // W3DGadgetPushButtonImageDraw: a disabled button that is merely not ready keeps its colours under the clock, one the player cannot pay for is dark (0x90), any other grey.
            if (cameo.Length > 0 && _assets.Images.TryGet(cameo, out _))
                commands.Add(new UiImageDraw(cameo, rect, button.Availability == ButtonAvailability.CantAfford ? CantAffordTint : WndColor.White, false, false) { Grayscale = button.Availability == ButtonAvailability.Restricted });
            if (rider is { VeterancyLevel: > 0 and <= 3 } && _assets.Images.TryGet($"SSChevron{rider.VeterancyLevel}L", out _))
                commands.Add(new UiImageDraw($"SSChevron{rider.VeterancyLevel}L", rect, WndColor.White, false, false));
            // W3DGadgetPushButtonImageDraw: WIN_STATUS_FLASHING lays the pushed overlay over the cameo.
            if (_flashing.Contains(button.Button.Name) && _assets.Images.TryGet("Cameo_push", out _)) commands.Add(new UiImageDraw("Cameo_push", rect, WndColor.White, false, false));
            AddCameoState(commands, window, button, cooldown);
        }

        for (var index = 0; index < CommandBar.Queue.Length; index++)
        {
            var entry = CommandBar.Queue[index];
            var rect = _tree.FindByName($"ControlBar.wnd:ButtonQueue{(index + 1):00}")!.Rect;
            if (entry.ButtonImage.Length > 0 && _assets.Images.TryGet(entry.ButtonImage, out _))
                commands.Add(new UiImageDraw(entry.ButtonImage, rect, WndColor.White, false, false));
            // ControlBar::updateContextCommand: the first entry is in production and wears the inverse clock of its percent.
            if (index == 0) commands.AddRange(UiClock.Remaining(rect, (int)(entry.Progress * 100), cooldown));
        }

        if (_modernBar is { } modern)
        {
            if (!_controlBarRoot.IsHidden)
            {
                commands.AddRange(modern.Power(Hud.EnergyProduction, Hud.EnergyConsumption, _textMeasure, _scaleOptions.TextScale));
                commands.AddRange(modern.Selection(SelectionForPlate(), _textMeasure, _scaleOptions.TextScale, CommandBar.GroupQueues));
                if (Generals is { } general) commands.AddRange(modern.General(_assets.Text.Get("SCIENCE:Rank" + general.RankLevel.ToString(CultureInfo.InvariantCulture)), general.ProgressPercent, StarBlinks && _starFlash, _textMeasure, _scaleOptions.TextScale));
            }
        }
        else if (_scheme is { } scheme)
        {
            var powerRect = SchemeRectOnScreen(scheme, scheme.PowerBar);
            var powerColor = Hud.Underpowered ? new WndColor(220, 40, 40, 255) : new WndColor(40, 200, 60, 255);
            var ratio = Hud.EnergyConsumption <= 0 ? 1f : Math.Clamp(Hud.EnergyProduction / (float)Hud.EnergyConsumption, 0f, 1f);
            commands.Add(new UiFillRect(powerRect with { Width = (int)(powerRect.Width * ratio) }, powerColor));
        }
        if (_modernBar is null && !_controlBarRoot.IsHidden && CommandBar.Group is { } group) commands.AddRange(GroupTag(group));
        _trayCells = [];
        if (!_controlBarRoot.IsHidden && CommandBar.GroupQueues.Length > 1 && _modernBar is not { HasPlate: true }) commands.AddRange(QueueLoads(CommandBar.GroupQueues));
        AddPowerCameos(commands, cooldown);
        AddGeneralsCameos(commands);
        return commands.ToImmutable();
    }

    /// <summary>Where the cells of the queue loads stood in the last drawn frame (the tray's or the modern plate's); empty when none were drawn. Tests check that they overlap nothing.</summary>
    public ImmutableArray<UiRect> QueueLoadCells => _modernBar is { HasPlate: true } plate && !plate.QueueCells.IsEmpty ? plate.QueueCells : _trayCells;

    private ImmutableArray<UiRect> _trayCells = [];

    /// <summary>
    /// How full the queue of each building of a structure group is, as a tray over the bar's top edge at the right end (<see cref="QueueLoadStrip"/>): in the original skin and in a
    /// window without a selection plate. The modern bar with a plate shows the same cells in the plate. The original has no such display: it shows one object's queue, and a full
    /// queue only greys the build buttons.
    /// </summary>
    private ImmutableArray<UiDrawCommand> QueueLoads(ImmutableArray<QueueLoad> loads)
    {
        _trayCells = [];
        if (_tree.FindByName("ControlBar.wnd:RightHUD") is not { } anchor) return [];
        var font = TextStyle.Font(InGameUiFontRole.Message, _tree.Viewport, _scaleOptions);
        var (numerator, denominator) = UiScaler.UniformScale(_tree.Viewport, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight);
        int Scaled(int units) => Math.Max(1, (int)(units * numerator / denominator));
        var pad = Math.Max(2, Scaled(3));
        var rowHeight = QueueLoadStrip.RowHeight(_textMeasure.LineHeight(font), Math.Max(3, Scaled(5)), pad);
        // Up to three rows, the first nearest the bar, over the bar's top edge and the right end of its portrait window.
        var bottom = _controlBarRoot.Rect.Y - Scaled(3);
        var area = new UiRect(anchor.Rect.X, bottom - (rowHeight + pad) * 3, anchor.Rect.Width, (rowHeight + pad) * 3);
        var layout = QueueLoadStrip.Place(loads, area, true, Math.Max(2, Scaled(3)), Math.Max(1, Scaled(1)), Math.Max(3, Scaled(5)), pad, _textMeasure.LineHeight(font), text => _textMeasure.MeasureWidth(text, font));
        var palette = Skin.IsModern ? Skin.Style.Palette : null;
        var colours = palette is null
            ? new QueueLoadStrip.Colours(new WndColor(0, 0, 0, 170), new WndColor(120, 130, 140, 255), new WndColor(60, 64, 70, 230), new WndColor(235, 235, 235, 255), new WndColor(235, 70, 60, 255), WndColor.White, new WndColor(0, 0, 0, 255))
            : new QueueLoadStrip.Colours(palette.Control, palette.Border, palette.Canvas, palette.Good, palette.Bad, palette.Text, WndColor.Undefined);
        _trayCells = [.. layout.Cells.Select(cell => cell.Rect)];
        return QueueLoadStrip.Draw(layout, colours, font, new UiRect(0, 0, _tree.Viewport.Width, _tree.Viewport.Height));
    }

    /// <summary>
    /// What the original bar cannot say about several buildings that share it, or about the active kind of a mixed selection: its name and how many there are (and, for a kind, its
    /// place among the kinds Tab walks), over the portrait window. The modern bar's plate says the same in its own text.
    /// </summary>
    private ImmutableArray<UiDrawCommand> GroupTag(SelectionGroup group)
    {
        if (_tree.FindByName("ControlBar.wnd:RightHUD") is not { } anchor) return [];
        var name = _descriptions.DisplayName(group.Template);
        var text = group.Kind == SelectionGroupKind.Structures
            ? string.Create(CultureInfo.InvariantCulture, $"{name} x{group.Count}")
            : string.Create(CultureInfo.InvariantCulture, $"{name} x{group.Count} [{group.Index}/{group.Total}]");
        var font = TextStyle.Font(InGameUiFontRole.Message, _tree.Viewport, _scaleOptions);
        var rect = anchor.Rect;
        var width = _textMeasure.MeasureWidth(text, font);
        var (x, y) = (rect.X + (rect.Width - width) / 2, rect.Y - _textMeasure.LineHeight(font) - 1);
        return [new UiTextDraw(text, null, font, WndColor.White, new WndColor(0, 0, 0, 255), x, y, UiTextAlignment.Left, new UiRect(rect.X - 8, y - 2, rect.Width + 16, _textMeasure.LineHeight(font) + 4))];
    }
}
