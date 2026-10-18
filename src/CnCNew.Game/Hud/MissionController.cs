using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Ui.Settings;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Hud;

/// <summary>
/// The playable HUD of a campaign mission: the skirmish HUD (<see cref="MatchController"/>: control bar, production, selection, orders, in-game menu, ticker) running over a
/// <see cref="LiveMission"/>, plus what only a mission has — the scripts' own on-screen text and radar events drawn over the HUD, and the human's selection reported to the
/// script engine (<c>NAMED_SELECTED</c>). The skirmish classes stay skirmish typed; <see cref="MatchController"/> only needs a <see cref="LiveGame"/>, so nothing of it was
/// copied.
/// </summary>
public sealed class MissionController : IMissionHudControl
{
    private bool _endedFired;

    public MissionController(LiveMission mission, GameSettings settings, string language = "English", IUiTextMeasure? textMeasure = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        Mission = mission;
        Hud = new MatchController(mission, settings, language, textMeasure);
        Hud.Input.Selection.SelectionChanged += mission.Bridge.OnSelectionChanged;
        Hud.MatchEnded += OnEnded;
        mission.Director.InputDisabled += OnInputDisabled;
        mission.Director.TextMeasure = textMeasure ?? new MonospaceTextMeasure();
        mission.Director.TextStyle = Hud.TextStyle;
        mission.Director.TextScaleOptions = Hud.ScaleOptions;
        Hud.Superweapons.HiddenByScript = id => _hiddenSuperweapons.Contains(id.Value);
        mission.Director.Hud = this;
        // One message list (InGameUI::m_uiMessages): the scripts' DISPLAY_TEXT and the game's own messages share it, the director fades and draws it.
        mission.Director.UseMessages(Hud.Messages.Log);
        Hud.OwnsMessageList = false;
        Hud.Messages.Clock = () => mission.Director.FrameClock;
        Hud.Messages.AnnounceDefeats = false;
        _textMeasure = textMeasure ?? new MonospaceTextMeasure();
    }

    private readonly IUiTextMeasure _textMeasure;
    private MissionPopupWindow? _popup;
    private PopupMessage? _shownPopup;
    private bool _pausedBeforePopup;
    private long _popupClockMs;

    private readonly HashSet<uint> _hiddenSuperweapons = [];

    public void SetSpecialPowerDisplay(bool enabled) => Hud.Superweapons.DisplayEnabled = enabled;

    public bool SetObjectSpecialPowerDisplay(EntityId entity, bool visible)
    {
        if (Mission.World.FindEntity(entity) is null) return false;
        if (visible) _hiddenSuperweapons.Remove(entity.Value);
        else _hiddenSuperweapons.Add(entity.Value);
        return true;
    }

    // ---- IMissionHudControl: what the mission's scripts may change about the control bar and the selection (docs/SCRIPT-ENGINE.md §7a)

    private CommandBarModel ControlBar => Hud.Input.CommandBar ?? throw new InvalidOperationException("The HUD has no control bar model.");

    public bool RemoveCommandButton(string buttonName, string objectType) => ControlBar.RemoveButton(buttonName, objectType);

    public bool AddCommandButton(string buttonName, string objectType, int slot) => ControlBar.AddButton(buttonName, objectType, slot);

    public bool SetScienceAvailability(string science, string availability)
    {
        if (!ControlBarScriptState.TryParseAvailability(availability, out var parsed) || !Mission.World.Sciences.TryGet(science, out _)) return false;
        ControlBar.Script.SetAvailability(science, parsed);
        return true;
    }

    public void SetIconUiEnabled(bool enabled) => Hud.IconUiEnabled = enabled;

    public bool SetObjectSelectable(EntityId entity, bool selectable)
    {
        if (Mission.World.FindEntity(entity) is null) return false;
        Hud.Input.Selection.Picker.SetScriptSelectable(entity, selectable);
        // Object::setSelectable(false) drops the object from the selection.
        if (!selectable) Hud.Input.Selection.Replace(Hud.Input.Selection.Selected);
        return true;
    }

    private long _lastInputTime;

    /// <summary><c>ScriptActions::doDisableInput</c>: besides dropping input the original clears the selection, the pending command modes and every held key and button.</summary>
    private void OnInputDisabled()
    {
        Hud.Process(new FocusLostEvent(_lastInputTime));
        Hud.Input.Commands.Cancel();
        Hud.Input.Selection.Clear();
    }

    public LiveMission Mission { get; }

    /// <summary>The control bar, selection and order handling; feed it input events and draw its list as for a skirmish.</summary>
    public MatchController Hud { get; }

    public MissionPresentationDirector Director => Mission.Director;
    public MissionOutcome Outcome => Mission.Outcome;

    /// <summary>Raised once when the mission is over (victory or defeat plus the end-game delay): the host then calls <c>GameShell.MissionFinished</c>.</summary>
    public event Action<MissionOutcome>? MissionEnded;

    public void Process(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        _lastInputTime = inputEvent.TimeMs;
        // A message popup (INGAME_POPUP_MESSAGE) is a window of its own: it takes its events first, and while it pauses the game it takes all of them.
        if (_popup is { IsOpen: true } popup && popup.Process(inputEvent)) return;
        Hud.Process(inputEvent);
    }

    /// <summary>The message popup on screen, if a script opened one (<see cref="MissionPresentationDirector.Popup"/>).</summary>
    public MissionPopupWindow? Popup => _popup;

    /// <summary>
    /// <c>InGameUI::popupMessage</c>/<c>clearPopupMessageData</c> on the host side: opens the window for a new <see cref="MissionPresentationDirector.Popup"/> (pausing the game when the script asked for it) and
    /// removes it when the script side dropped it.
    /// </summary>
    private void SyncPopup()
    {
        var wanted = Director.Popup;
        if (ReferenceEquals(wanted, _shownPopup)) return;
        if (_shownPopup is not null) ClosePopupState();
        if (wanted is null) return;
        var viewport = new UiViewport(Math.Max(1, (int)Mission.Camera.ViewportSize.X), Math.Max(1, (int)Mission.Camera.ViewportSize.Y));
        if (_popup is null)
        {
            _popup = new MissionPopupWindow(Hud.Assets, _textMeasure, Hud.ScaleOptions, viewport, Hud.Skin);
            _popup.Closed += () => { ClosePopupState(); Director.DismissPopup(); };
        }
        _popup.Show(wanted, viewport);
        _shownPopup = wanted;
        if (wanted.Pause)
        {
            _pausedBeforePopup = Mission.Paused;
            Mission.Paused = true;
        }
    }

    private void ClosePopupState()
    {
        _popup?.Hide();
        if (_shownPopup is { Pause: true }) Mission.Paused = _pausedBeforePopup;
        _shownPopup = null;
    }

    /// <summary>One rendered frame: advances the mission (<see cref="LiveGame.Advance"/>, which steps the simulation at 30 Hz), then the HUD's own clocks (ticker, quit menu).</summary>
    public void Update(double deltaSeconds)
    {
        Mission.Advance(deltaSeconds);
        // After the ticks of this frame: a script that disabled the input in them already cleared selection and modes (OnInputDisabled) while the HUD still took events.
        Hud.WorldInputEnabled = !Director.InputLocked;
        SyncPopup();
        _popupClockMs += (long)Math.Round(deltaSeconds * 1000);
        _popup?.Advance(_popupClockMs);
        Hud.Update(deltaSeconds);
    }

    /// <summary>
    /// The HUD with the mission's on-screen text (caption, subtitle, movie placeholder) appended; radar events are read from
    /// <see cref="MissionPresentationDirector.RadarBlips"/> by the host, which owns the minimap transform.
    /// </summary>
    public UiDrawList BuildDrawList(int width, int height)
    {
        var hud = Hud.BuildDrawList(width, height);
        var overlay = Director.BuildOverlayCommands(width, height);
        // CAMERA_LETTERBOX_BEGIN hides the control bar (ScriptActions::doLetterBoxMode: HideControlBar); the in-game menu, when open, stays.
        var commands = Director.Letterbox && !Hud.IsMenuOpen ? overlay : hud.Commands.AddRange(overlay);
        if (_popup is { IsOpen: true } popup) commands = commands.AddRange(popup.BuildCommands(width, height));
        return hud with { Commands = commands };
    }

    private void OnEnded(MatchResult _)
    {
        if (_endedFired) return;
        _endedFired = true;
        MissionEnded?.Invoke(Outcome);
    }
}
