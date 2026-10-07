using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Game.Data;
using CnCNew.Presentation;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell;

/// <summary>One <c>RADAR_CREATE_EVENT</c>/<c>OBJECT_CREATE_RADAR_EVENT</c>/<c>TEAM_CREATE_RADAR_EVENT</c> output, with the
/// world position resolved (for the object/team forms) at the tick the event fired. <see cref="EventType"/> is the raw
/// <c>RadarEventType</c> ordinal the script passed (<c>doRadarCreateEvent</c>, <c>ScriptActions.cpp</c>); this build does
/// not map it to a color/icon yet, so a host renders every blip the same way until it does.</summary>
public readonly record struct RadarBlip(float X, float Y, int EventType, long CreatedAtTick);

/// <summary>What happened to one <see cref="ScriptOutput"/> type over a mission: how often the scripts emitted it, how often it was handed to a consumer
/// (camera, overlay, audio, radar, input, HUD, objects, post effects), and how often that consumer exists in the running host - so "displayed" is never claimed for a headless run.
/// <see cref="State"/> is the type's entry in <see cref="MissionOutputCatalog"/>: a type that is not <see cref="MissionOutputState.Displayed"/> has <see cref="Displayed"/> 0 however often a model kept it.</summary>
public readonly record struct MissionOutputCount(string Action, long Emitted, long Forwarded, long Displayed, string How, MissionOutputState State = MissionOutputState.Displayed)
{
    /// <summary>Handled by the simulation, not by the host (<see cref="MissionOutputState.HandledElsewhere"/>).</summary>
    public long HandledElsewhere => State == MissionOutputState.HandledElsewhere ? Emitted : 0;

    public long NotDisplayed => Emitted - Displayed - HandledElsewhere;
}

/// <summary>
/// An <c>INGAME_POPUP_MESSAGE</c> (<c>InGameUI::popupMessage</c>): the resolved text, the window's position as a percentage of the screen (clamped to 0..100), its width in pixels (at least 50) and
/// whether it pauses the game until it is closed.
/// </summary>
public sealed record PopupMessage(string Text, int XPercent, int YPercent, int Width, bool Pause);

/// <summary>The screen post effects the scripts switch on, for the host's renderer (<see cref="MissionPresentationDirector.PostEffects"/>).</summary>
/// <param name="BlackWhite">0..1: how far the world is turned grey-scale (<c>ScreenBWFilter::m_curFadeValue</c>).</param>
/// <param name="MotionBlurCount">0 when off, else 5..60 in steps of 5: how strong the zoom blur is (<c>ScreenMotionBlurFilter::m_maxCount</c>; the zoom factor is <c>sqrt(1 - count / 60 * 0.9)</c>).</param>
/// <param name="MotionBlurAdditive">The saturating variant of the blur (the layers add up instead of blending).</param>
public readonly record struct MissionPostEffects(float BlackWhite, int MotionBlurCount, bool MotionBlurAdditive)
{
    public const int MotionBlurMaxCount = 60;
    public const int MotionBlurStep = 5;

    public bool Any => BlackWhite > 0f || MotionBlurCount > 0;

    /// <summary><c>ScreenMotionBlurFilter::postRender</c>: the texture coordinates of the full frame shrink about the centre by this factor (the view zooms in).</summary>
    public float MotionBlurZoomFactor => MotionBlurCount <= 0 ? 1f : MathF.Sqrt(1f - MotionBlurCount / (float)MotionBlurMaxCount * 0.90f);
}

/// <summary>
/// Drains a <see cref="MissionSession"/>'s <see cref="ScriptEngine.DrainOutputs"/> every tick and turns the presentation
/// actions a client is responsible for into renderer-agnostic state: scripted camera moves (pan, zoom, pitch, rotation,
/// follow), letterbox, fades, the input lock, on-screen text (captions, cinematic text, counters and countdown timers,
/// the outcome banner), speech/sound/music requests for the host's audio, a movie placeholder name (no Bink decoder in
/// this build, see <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>) and radar blips. Every output is counted per type
/// (<see cref="Counts"/>): emitted, handed to a consumer, displayed; what no consumer shows stays visible as "not displayed".
/// <para>
/// For camera moves (<c>MOVE_CAMERA_TO</c>/<c>MOVE_CAMERA_ALONG_WAYPOINT_PATH</c>/<c>ROTATE_CAMERA</c>/<c>RESET_CAMERA</c>)
/// the real duration is the action's own seconds parameter (<c>ScriptActions::doMoveCameraTo</c> etc., read directly from
/// <see cref="ScriptOutput.Arguments"/>): this director calls <see cref="ScriptEngine.CompletePresentation"/> at that
/// exact tick, so <c>CAMERA_MOVEMENT_FINISHED</c>/<c>HAS_FINISHED_*</c> conditions resolve from a real completion rather
/// than a guess. Speech/sound/movie have no decoded duration anywhere in this build (no subtitle text and no audio length
/// data for a named sound event), so they use a fixed placeholder duration instead — a documented, honest limit, not a
/// silent guess at correctness.
/// </para>
/// </summary>
public sealed class MissionPresentationDirector
{
    // Durations of the director itself, in original frames (3 s and 8 s); they become ticks of the session's world where they are used.
    private const long DefaultPlaceholderFrames = 3 * OriginalFrame.PerSecond;
    private const long RadarBlipLifetimeFrames = 8 * OriginalFrame.PerSecond;
    private const string CameraKey = "<camera>";
    private const int JournalCapacity = 4000;

    private enum Channel { Camera, Overlay, Audio, Radar, Input, Hud, Objects, Post }

    private readonly MissionSession _session;
    private readonly Func<string, string> _resolveText;
    private readonly List<RadarBlip> _radar = [];
    private readonly SortedDictionary<string, long> _emitted = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long[]> _handled = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _problems = new(StringComparer.Ordinal);
    private readonly List<string> _journal = [];
    private readonly List<(string Key, long AtTick)> _completions = [];
    private readonly SortedDictionary<string, (string Label, bool IsTimer)> _counters = new(StringComparer.Ordinal);
    private readonly bool[] _consumers = new bool[8];
    private readonly PlayerId _human;

    private UiMessageLog? _messages;
    private MilitaryCaptionState? _militaryCaption;
    private CinematicTextState? _cinematic;
    private bool _textProblemReported;
    private long _movieExpiresAtTick;
    private long _subtitleExpiresAtTick;
    private long _cameraCompletesAtTick;
    private bool _cameraPending;
    private bool _countersVisible = true;
    private bool _inputLocked;
    private long _lastObservedTick;
    private long _tickBeforeStep;
    private long _frozenSteps;

    // Scripted camera: requests are recorded when the script fires and resolved against the real camera in ApplyCameraTo.
    private readonly Tween _moveX = new(), _moveY = new(), _zoom = new(), _pitch = new(), _angle = new();
    private (float Factor, double Seconds)? _zoomRequest, _pitchRequest;
    private (float Turns, double Seconds)? _rotateRequest;
    private (Vector2 Target, double Seconds)? _lookRequest;
    private double? _resetRequest;
    private string? _followName;
    private (bool White, float Min, float Max, long Start, long Increase, long Hold, long Decrease)? _fade;

    /// <param name="resolveText">Resolves a <c>DISPLAY_TEXT</c>/<c>SHOW_MILITARY_CAPTION</c>/<c>INGAME_POPUP_MESSAGE</c> CSF
    /// label to its localized text (e.g. <c>GameData.LoadText(language).Get</c>); defaults to the label itself when omitted
    /// (headless tests, or a host that has not wired text lookup yet).</param>
    public MissionPresentationDirector(MissionSession session, Func<string, string>? resolveText = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _resolveText = resolveText ?? (label => label);
        _human = MissionSession.LocalPlayerOf(session.Program, session.Options.HumanSideName);
    }

    /// <summary>
    /// The presentation clock: the world tick plus the steps in which the scripts froze time (<c>FREEZE_TIME</c>; <c>MissionSession.Step</c> runs the scripts but not the world then). Camera moves, fades,
    /// captions, the blur and the grey-scale ramp run on it, so they go on while the world stands still, exactly as the original's views do; without a freeze it equals the world tick.
    /// </summary>
    /// <remarks>
    /// The unit is the tick of the session's world. Everything the scripts and the data give in original frames (fade and grey-scale frames, caption times, <c>InGameUI</c>
    /// durations) is converted with <see cref="Time"/> where it meets this clock, and what the original advances once per frame (texts, filters) advances on
    /// <see cref="FrameClock"/>: at 60 Hz it lasts the same game time as at 30 Hz.
    /// </remarks>
    public long Clock => _session.World.Tick + _frozenSteps;

    /// <summary>The presentation clock in original frames (1/30 s): the unit of the texts (<see cref="UiMessageLog"/>, <see cref="MilitaryCaptionState"/>, named timers). Equal to <see cref="Clock"/> at 30 Hz.</summary>
    public long FrameClock => FrameOf(Clock);

    private SimulationTime Time => _session.World.Time;

    private long FrameOf(long clock) => GameClock.OriginalFrameOf(clock, Time.TicksPerOriginalFrame);

    /// <summary>The mission scripts' sound control (volumes, muted events, Eva, ambient loops); without it the output counts as not played.</summary>
    public IMissionAudioControl? AudioControl { get; set; }

    private IMissionObjectEffects? _objectEffects;
    private IMissionHudControl? _hud;

    /// <summary>The object layer (flashes, house colours); without it the output counts as not displayed. Connecting one is what makes the director count its outputs as displayed.</summary>
    public IMissionObjectEffects? ObjectEffects
    {
        get => _objectEffects;
        set
        {
            _objectEffects = value;
            _consumers[(int)Channel.Objects] = value is not null;
        }
    }

    /// <summary>The HUD (control bar overrides, icon overlay, selectability); without it the output counts as not displayed. Connecting one is what makes the director count its outputs as displayed.</summary>
    public IMissionHudControl? Hud
    {
        get => _hud;
        set
        {
            _hud = value;
            _consumers[(int)Channel.Hud] = value is not null;
        }
    }

    /// <summary>The <c>GameData</c> intensities of the camera shake types (<c>SCREEN_SHAKE</c>); without them a shake is a reported problem.</summary>
    public CameraShakeIntensities? ShakeIntensities { get; set; }

    /// <summary>
    /// What the mission's own texts are drawn from (positions, colors and durations of <c>InGameUI.ini</c>, fonts of <c>Language.ini</c>). Without it the texts are still kept as state but never
    /// drawn, and each text output is a reported problem - a size or a font is never invented (<c>docs/TEXT-AUDIT.md</c>).
    /// </summary>
    public InGameUiStyle? TextStyle { get; set; }

    /// <summary>How the host scales text for the screen (the same options as its HUD), for <see cref="BuildOverlayCommands"/>.</summary>
    public UiScaleOptions TextScaleOptions { get; set; } = new();

    /// <summary><c>DISPLAY_TEXT</c>: the message lines at the top left (<c>InGameUI::message</c>); <see langword="null"/> until the first one.</summary>
    public UiMessageLog? Messages => _messages;

    /// <summary>
    /// Makes <paramref name="log"/> the message list: the original has one (<c>InGameUI::m_uiMessages</c>) for the scripts' <c>DISPLAY_TEXT</c> and the game's own messages
    /// (<see cref="CnCNew.Game.Hud.HudMessageFeed"/>), so a mission HUD hands its list over and this director fades and draws it.
    /// </summary>
    public void UseMessages(UiMessageLog log) => _messages = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary><c>SHOW_MILITARY_CAPTION</c>: the caption being typed, shown or faded out, or <see langword="null"/>.</summary>
    public MilitaryCaptionState? MilitaryCaption => _militaryCaption;

    /// <summary>The audio event the original plays for every character the military caption types (<c>InGameUI::update</c>).</summary>
    public const string TypingClickEvent = "MilitarySubtitlesTyping";

    /// <summary>Characters the military captions typed so far: one click each was asked of the host's audio.</summary>
    public long TypingClicks { get; private set; }

    /// <summary>How many of <see cref="TypingClicks"/> started a voice (0 without connected audio; the audio layer may also refuse one, e.g. when the event is unknown or muted).</summary>
    public long TypingClicksStarted { get; private set; }

    /// <summary><c>DISPLAY_CINEMATIC_TEXT</c>: the text near the bottom of the screen, or <see langword="null"/>.</summary>
    public CinematicTextState? Cinematic => _cinematic;

    /// <summary><c>INGAME_POPUP_MESSAGE</c>: the message window on screen (<c>InGameUI::m_popupMessageData</c>) until its OK button is pressed (<see cref="DismissPopup"/>), or <see langword="null"/>.</summary>
    public PopupMessage? Popup { get; private set; }

    /// <summary>Opens a message popup (<c>InGameUI::popupMessage</c>); a popup that is already open is replaced. The script outputs call this, a host or a test may too.</summary>
    public void ShowPopup(PopupMessage message) => Popup = message ?? throw new ArgumentNullException(nameof(message));

    /// <summary>The OK button (or Enter/Escape) of the popup: <c>InGameUI::clearPopupMessageData</c>. The host unpauses the game when the popup had paused it.</summary>
    public void DismissPopup() => Popup = null;

    public string? SubtitleEvent { get; private set; }
    public string? MoviePlaceholder { get; private set; }
    public bool CameraMoveActive => _cameraPending;

    /// <summary><c>CAMERA_LETTERBOX_BEGIN</c>..<c>END</c>: the original hides the control bar and shows black bars (<c>ScriptActions::doLetterBoxMode</c>).</summary>
    public bool Letterbox { get; private set; }

    private long? _letterboxSwitchedAt;

    /// <summary>
    /// <c>LETTER_BOX_FADE_TIME</c> (1000 ms) in original frames of the presentation clock; the original counts wall-clock milliseconds. The fade stays on the presentation
    /// clock (it stands with a paused game and is reproducible); in seconds it is the same at every tick rate.
    /// </summary>
    public const int LetterboxFadeFrames = OriginalFrame.PerSecond;

    /// <summary>
    /// <c>m_letterBoxFadeLevel</c> (<c>W3DDisplay::renderLetterBox</c>): rises from 0 to 1 over a second after the letterbox began, falls back to 0 over a second after it ended.
    /// </summary>
    public float LetterboxFadeLevel
    {
        get
        {
            if (_letterboxSwitchedAt is not { } since) return 0f;
            var progress = Math.Clamp((Clock - since) / (float)Time.OriginalFrames(LetterboxFadeFrames), 0f, 1f);
            return Letterbox ? progress : 1f - progress;
        }
    }

    private void SetLetterbox(bool enabled)
    {
        // W3DDisplay::enableLetterBox: only a change restarts the fade.
        if (Letterbox == enabled) return;
        Letterbox = enabled;
        _letterboxSwitchedAt = Clock;
    }

    /// <summary>
    /// <c>W3DDisplay::renderLetterBox</c>: the bars cover what a 16:9 picture leaves of the screen, <c>(height - 9/16 * width) / 2</c> pixels at the top and at the bottom - 75 at 800x600,
    /// none on a 16:9 screen or a wider one (the rectangles then lie outside the screen). They are black with the alpha of <paramref name="fadeLevel"/>. While the letterbox fades out
    /// the original draws the top bar only (the bottom one's call is commented out), so the bottom bar goes at once.
    /// </summary>
    public static ImmutableArray<UiDrawCommand> LetterboxBars(int width, int height, bool enabled, float fadeLevel)
    {
        var bar = (int)((height - 9f / 16f * width) * 0.5f);
        var alpha = (byte)(int)(Math.Clamp(fadeLevel, 0f, 1f) * 255f);
        if (bar <= 0 || alpha == 0) return [];
        var color = new WndColor(0, 0, 0, alpha);
        var top = new UiFillRect(new UiRect(0, 0, width, bar), color);
        if (!enabled) return [top];
        var bottomY = (int)(height - (height - 9f / 16f * width) * 0.5f);
        return [top, new UiFillRect(new UiRect(0, bottomY, width, height - bottomY), color)];
    }

    /// <summary><c>DISABLE_INPUT</c>..<c>ENABLE_INPUT</c> (<c>ScriptActions::doDisableInput</c>): the host drops the player's world input while this is set. Reading it tells the director a host honours it.</summary>
    public bool InputLocked
    {
        get
        {
            _consumers[(int)Channel.Input] = true;
            return _inputLocked;
        }
    }

    /// <summary>Raised when the scripts disable the input: the original also clears the selection and every pending command mode.</summary>
    public event Action? InputDisabled;

    /// <summary>How many logic ticks the scripts kept the input disabled so far.</summary>
    public long InputLockedTicks { get; private set; }

    /// <summary>The scripted end of the mission as text for the banner, or <see langword="null"/> while it runs.</summary>
    public string? OutcomeBanner { get; private set; }

    /// <summary>
    /// The host's audio for <c>SPEECH_PLAY</c>, <c>SOUND_PLAY_NAMED</c>, <c>PLAY_SOUND_EFFECT</c>, <c>PLAY_SOUND_EFFECT_AT</c> and <c>MUSIC_SET_TRACK</c>: called with the action
    /// and the event name, returns whether a voice started. Without it (or when it returns false) the output counts as not played.
    /// </summary>
    public Func<string, string, bool>? Audio { get; set; }

    /// <summary>The camera move's destination in world units, when it names a waypoint the map actually has
    /// (<c>MOVE_CAMERA_TO</c>/<c>MOVE_CAMERA_ALONG_WAYPOINT_PATH</c>/<c>RESET_CAMERA</c>/<c>SETUP_CAMERA</c>); <see langword="null"/> while no
    /// move is active, or for a move kind with no fixed target (<c>ROTATE_CAMERA</c>, <c>CAMERA_MOVE_HOME</c>,
    /// <c>MOVE_CAMERA_TO_SELECTION</c>).</summary>
    public (float X, float Y)? CameraTarget { get; private set; }

    // ---- scripted switches the host (or a later screen) reads; none of them is part of the simulation
    private readonly Queue<int> _pendingShakes = new();
    private readonly SortedSet<string> _scoreScreenExcluded = new(StringComparer.Ordinal);
    private readonly SortedSet<uint> _hiddenPowerDisplays = [];
    private int _shakeCount;
    private int? _pendingBoundary;

    // The grey-scale and the motion blur are the two screen filters of W3DView (one filter at a time in the original: a new one replaces the other).
    private bool _bwOn;
    private int _bwDirection;
    private int _bwFrames;
    private int _bwFrame;
    private float _bwLevel;
    private bool _blurActive;
    private bool _blurDecrement;
    private bool _blurAdditive;
    private int _blurCount;
    private (float X, float Y)? _blurTarget;
    private bool _blurJumpDue;

    /// <summary>
    /// The screen filters of the moment for the host's renderer (<c>CAMERA_BW_MODE_*</c>, <c>CAMERA_MOTION_BLUR_JUMP</c>); reading it tells the director the host draws them, which is what lets the
    /// two output types count as displayed. A headless run never reads it.
    /// </summary>
    public MissionPostEffects PostEffects
    {
        get
        {
            _consumers[(int)Channel.Post] = true;
            return new MissionPostEffects(_bwLevel, _blurActive ? _blurCount : 0, _blurAdditive);
        }
    }

    /// <summary><c>ENABLE_SCORING</c>/<c>DISABLE_SCORING</c> (<c>GameLogic::enableScoring</c>): while off the original's score keeper counts nothing. Kept for a statistics screen; the client's mission result shows none yet.</summary>
    public bool ScoringEnabled { get; private set; } = true;

    /// <summary><c>PLAYER_EXCLUDE_FROM_SCORE_SCREEN</c>: the sides a mission result must not list (<c>Player::setListInScoreScreen(false)</c>).</summary>
    public IReadOnlyCollection<string> ScoreScreenExcluded => _scoreScreenExcluded;

    /// <summary><c>DISABLE_SPECIAL_POWER_DISPLAY</c> (<c>InGameUI::setSuperweaponDisplayEnabledByScript</c>): the superweapon timers are hidden as a whole.</summary>
    public bool SpecialPowerDisplayEnabled { get; private set; } = true;

    /// <summary><c>NAMED_HIDE_SPECIAL_POWER_DISPLAY</c>: the objects whose superweapon timer a script hid.</summary>
    public IReadOnlyCollection<uint> SpecialPowerDisplayHidden => _hiddenPowerDisplays;

    /// <summary><c>DRAW_SKYBOX_BEGIN</c>/<c>END</c> and <c>ENABLE_/DISABLE_BORDER_SHROUD</c>: the scripts' switches, kept for a renderer that draws them.</summary>
    public bool SkyboxRequested { get; private set; }

    public bool BorderShroudEnabled { get; private set; } = true;

    public ImmutableArray<RadarBlip> RadarBlips => [.._radar];

    /// <summary>The radar blips for a host that draws them on its minimap; calling this tells the director the radar events are displayed.</summary>
    public ImmutableArray<RadarBlip> RadarBlipsForDisplay()
    {
        _consumers[(int)Channel.Radar] = true;
        return RadarBlips;
    }

    /// <summary>The first outputs of the mission the client has a display for, in order (<c>tick ACTION(arguments)</c>), for a host's diagnostic log; bounded.</summary>
    public IReadOnlyList<string> Journal => _journal;

    // ------------------------------------------------------------------------------------------------ camera

    private sealed class Tween
    {
        private float _from = float.NaN;
        private float _to;
        private double _duration;
        private double _elapsed;

        public bool Active { get; private set; }

        public void Start(float to, double seconds)
        {
            Active = true;
            _from = float.NaN;
            _to = to;
            _duration = seconds;
            _elapsed = 0;
        }

        public void Stop() => Active = false;

        /// <summary>The value after another <paramref name="seconds"/>, eased in and out (the original eases with the script's ease times; a smooth step stands in for them).</summary>
        public float Advance(float current, double seconds)
        {
            if (float.IsNaN(_from)) _from = current;
            _elapsed += seconds;
            var t = _duration <= 0 || double.IsPositiveInfinity(seconds) ? 1.0 : Math.Min(1.0, _elapsed / _duration);
            if (t >= 1.0) Active = false;
            return _from + (_to - _from) * (float)(t * t * (3 - 2 * t));
        }
    }

    /// <summary>
    /// Plays the scripted camera on <paramref name="camera"/>: call once per rendered frame with the frame time (0 while the game is paused). A move pans the pivot to
    /// its waypoint over the script's own duration, zoom/pitch/rotation follow their requests, a followed object keeps the camera on it. Without a frame time the
    /// pending move is applied at once (headless). When no scripted move is active the camera is left alone, so the player's own scrolling is never fought.
    /// <see cref="RtsCamera"/> is a <c>CnCNew.Presentation</c> type, not a Stride one, so this runs headless in tests.
    /// </summary>
    public void ApplyCameraTo(RtsCamera camera, double seconds = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _consumers[(int)Channel.Camera] = true;
        if (seconds <= 0) return;

        if (_pendingBoundary is { } boundary)
        {
            _pendingBoundary = null;
            try
            {
                camera.ActiveBoundary = boundary;
                Handled("MAP_SWITCH_BORDER", Channel.Camera);
            }
            catch (ArgumentOutOfRangeException)
            {
                Problem("MAP_SWITCH_BORDER", $"the map has no border {boundary}");
            }
        }
        if (_blurJumpDue)
        {
            // ScreenMotionBlurFilter::postRender at the apex: TheTacticalView->lookAt(zoomToPos) - the cut happens while the picture is at its most blurred.
            _blurJumpDue = false;
            if (_blurTarget is { } jump) camera.CenterOn(new Vector2(jump.X, jump.Y));
        }
        while (_pendingShakes.Count > 0)
        {
            // ScriptActions::doScreenShake -> W3DView::shake at the view position: the full intensity of the type, a random direction (the reference draws it from the client random stream; a golden-angle sequence here).
            var type = _pendingShakes.Dequeue();
            if (ShakeIntensities is not { } intensities) { Problem("SCREEN_SHAKE", "no shake intensities connected"); continue; }
            var intensity = type switch { 0 => intensities.Subtle, 1 => intensities.Normal, 2 => intensities.Strong, 3 => intensities.Severe, 4 => intensities.CineExtreme, _ => intensities.CineInsane };
            camera.Shake(camera.Pivot, intensity, _shakeCount++ * 2.3999632f);
            Handled("SCREEN_SHAKE", Channel.Camera);
        }

        if (_resetRequest is { } reset)
        {
            _resetRequest = null;
            // View::resetCamera: default angle, pitch and zoom at the waypoint.
            _angle.Start(0f, reset);
            _pitch.Start(DefaultPitch(camera), reset);
            _zoom.Start(camera.Settings.MaxHeightAboveGround, reset);
        }
        if (_moveX.Active || _moveY.Active)
        {
            var x = _moveX.Advance(camera.Pivot.X, seconds);
            var y = _moveY.Advance(camera.Pivot.Y, seconds);
            camera.CenterOn(new Vector2(x, y));
        }
        else if (_followName is { } follow && TryNamedPosition(follow, out var followX, out var followY)) camera.CenterOn(new Vector2(followX, followY));

        if (_lookRequest is { } look)
        {
            _lookRequest = null;
            var from = CameraTarget is { } destination ? new Vector2(destination.X, destination.Y) : camera.Pivot;
            _angle.Start(AngleToward(camera, from, look.Target), look.Seconds);
        }
        if (_rotateRequest is { } rotate)
        {
            _rotateRequest = null;
            _angle.Start(camera.Angle + rotate.Turns * 2 * MathF.PI, rotate.Seconds);
        }
        if (_zoomRequest is { } zoom)
        {
            _zoomRequest = null;
            // View::zoomCamera: the zoom is a factor of the default (maximum) height; the camera clamps what it cannot show.
            _zoom.Start(zoom.Factor * camera.Settings.MaxHeightAboveGround, zoom.Seconds);
        }
        if (_pitchRequest is { } pitch)
        {
            _pitchRequest = null;
            _pitch.Start(pitch.Factor * DefaultPitch(camera), pitch.Seconds);
        }
        if (_angle.Active) camera.SetAngle(_angle.Advance(camera.Angle, seconds));
        if (_zoom.Active) camera.SetHeightAboveGround(_zoom.Advance(camera.HeightAboveGround, seconds));
        if (_pitch.Active) camera.SetPitch(_pitch.Advance(camera.Pitch, seconds));
    }

    private static float DefaultPitch(RtsCamera camera)
    {
        var current = camera.Pitch;
        camera.SetPitchToDefault();
        var result = camera.Pitch;
        camera.SetPitch(current);
        return result;
    }

    /// <summary>The camera angle that looks from <paramref name="from"/> towards <paramref name="target"/> on the ground (<c>View::cameraModLookToward</c>); which way a larger angle turns the view is probed, not assumed.</summary>
    private static float AngleToward(RtsCamera camera, Vector2 from, Vector2 target)
    {
        static Vector2 Forward(RtsCamera view)
        {
            var eye = view.Eye;
            var look = view.Target;
            return new Vector2(look.X - eye.X, look.Y - eye.Y);
        }
        var before = camera.Angle;
        var forward = Forward(camera);
        var wanted = target - from;
        if (forward.LengthSquared() < 1e-6f || wanted.LengthSquared() < 1e-6f) return before;
        camera.SetAngle(before + 0.05f);
        var probed = Forward(camera);
        camera.SetAngle(before);
        var sign = forward.X * probed.Y - forward.Y * probed.X < 0 ? -1f : 1f;
        var delta = MathF.IEEERemainder(MathF.Atan2(wanted.Y, wanted.X) - MathF.Atan2(forward.Y, forward.X), 2 * MathF.PI);
        return before + sign * delta;
    }

    // ------------------------------------------------------------------------------------------------ overlay

    /// <summary>
    /// The mission's own screen content as <see cref="UiDrawCommand"/>s - the same renderer-neutral vocabulary
    /// <see cref="CnCNew.Game.Hud.MatchController.BuildDrawList"/> uses, so a host's existing <c>UiRenderer</c> draws it: the fade, the letterbox bars, then the texts the way
    /// <c>InGameUI</c> and <c>W3DDisplay</c> draw them (<see cref="MissionTextOverlay"/>: message lines, the typed military caption, named timers and counters, cinematic text; fonts from
    /// <c>Language.ini</c>, positions and colors from <c>InGameUI.ini</c>, see <see cref="TextStyle"/>), the speech/movie placeholder and the outcome banner. The order is the original's
    /// (<c>W3DDisplay::draw</c>): the in-game texts (<c>InGameUI::postDraw</c>), then the letterbox bars over them (<see cref="LetterboxBars"/>), then the cinematic text over the black.
    /// Radar blips are read from <see cref="RadarBlipsForDisplay"/>: turning a world position into a minimap pixel needs the host's own minimap transform.
    /// </summary>
    public ImmutableArray<UiDrawCommand> BuildOverlayCommands(int viewportWidth, int viewportHeight)
    {
        _consumers[(int)Channel.Overlay] = true;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var black = new WndColor(0, 0, 0, 255);
        if (FadeAlpha() is { } fade && fade.Alpha > 0)
            commands.Add(new UiFillRect(new UiRect(0, 0, viewportWidth, viewportHeight), fade.White ? new WndColor(255, 255, 255, fade.Alpha) : new WndColor(0, 0, 0, fade.Alpha)));
        AppendTexts(commands, viewportWidth, viewportHeight, overLetterbox: false);
        commands.AddRange(LetterboxBars(viewportWidth, viewportHeight, Letterbox, LetterboxFadeLevel));
        AppendTexts(commands, viewportWidth, viewportHeight, overLetterbox: true);
        if (OutcomeBanner is { } banner && TextMeasure is { } bannerMeasure)
        {
            // Not a text of the original (it goes to a score screen and a movie): an honest placeholder, in a fixed font because no data names one.
            var rect = new UiRect(0, viewportHeight / 3, viewportWidth, 48);
            commands.Add(new UiFillRect(new UiRect(0, rect.Y - 12, viewportWidth, 72), new WndColor(0, 0, 0, 150)));
            commands.AddRange(MissionTextOverlay.CenteredText(banner, new UiFont("Arial", 28, true), bannerMeasure, viewportWidth, viewportHeight, rect.Y, new WndColor(255, 255, 255, 255), black));
        }
        return commands.ToImmutable();
    }

    /// <summary>
    /// The host's text measure (the renderer's glyph metrics): a draw command carries the final left edge of its text, so laying out and centring need the real widths. Without one no text is
    /// drawn and the first text output is reported as a problem.
    /// </summary>
    public IUiTextMeasure? TextMeasure { get; set; }

    /// <summary>The texts under the letterbox bars (messages, military caption, timers: <c>InGameUI::postDraw</c>) or the ones over them (cinematic text, the speech/movie placeholder).</summary>
    private void AppendTexts(ImmutableArray<UiDrawCommand>.Builder commands, int width, int height, bool overLetterbox)
    {
        var timers = _countersVisible && _counters.Count > 0;
        if (_messages is not { IsEmpty: false } && _militaryCaption is null && _cinematic is null && SubtitleEvent is null && MoviePlaceholder is null && !timers) return;
        if (TextStyle is not { } style || TextMeasure is not { } measure)
        {
            if (!_textProblemReported && !overLetterbox)
            {
                _textProblemReported = true;
                Problem("on-screen text", TextStyle is null ? "no InGameUI style connected (positions, colors and fonts come from InGameUI.ini and Language.ini)" : "no text measure connected");
            }
            return;
        }
        var options = TextScaleOptions;
        if (!overLetterbox)
        {
            if (_messages is { IsEmpty: false } messages) commands.AddRange(MissionTextOverlay.Messages(messages, style, options, measure, width, height));
            if (_militaryCaption is { } caption) commands.AddRange(MissionTextOverlay.MilitaryCaption(caption, style, options, measure, width, height));
            if (timers)
                commands.AddRange(MissionTextOverlay.NamedTimers(_counters.Select(pair => (pair.Value.Label, pair.Value.IsTimer, CounterForDisplay(pair.Key))), FrameClock, style, options, measure, width, height));
            return;
        }
        if (_cinematic is { } cinematic) commands.AddRange(MissionTextOverlay.Cinematic(cinematic, style, options, measure, width, height));
        var placeholderName = MoviePlaceholder ?? SubtitleEvent;
        if (placeholderName is { Length: > 0 })
        {
            // No Bink decoder and no decoded dialogue/subtitle text in this build (docs/CAMPAIGN-AND-SAVEGAMES.md): the raw event/movie name is shown, clearly marked as a placeholder
            // rather than invented dialogue, in the message font of the data. Inside the lower letterbox bar; without the letterbox above the control bar instead of on top of it.
            var text = MoviePlaceholder is not null ? $"(movie placeholder: {placeholderName})" : $"(speech: {placeholderName})";
            var font = style.Font(InGameUiFontRole.Message, new UiViewport(width, height), options);
            commands.AddRange(MissionTextOverlay.CenteredText(text, font, measure, width, height, Letterbox ? Math.Max(0, height - 48) : height * 3 / 4, new WndColor(255, 255, 255, 255), new WndColor(0, 0, 0, 255)));
        }
    }

    // A timer of the script VM counts original frames at every tick rate, and so does the display (InGameUI::postDraw divides by LOGICFRAMES_PER_SECOND). A counter is a plain number.
    private long CounterForDisplay(string name) => _session.Engine.CounterValue(name);

    private (bool White, byte Alpha)? FadeAlpha()
    {
        if (_fade is not { } fade) return null;
        var elapsed = Clock - fade.Start;
        float value;
        if (elapsed < fade.Increase) value = fade.Min + (fade.Max - fade.Min) * (elapsed / (float)Math.Max(1, fade.Increase));
        else if (elapsed < fade.Increase + fade.Hold) value = fade.Max;
        else if (elapsed < fade.Increase + fade.Hold + fade.Decrease) value = fade.Max - (fade.Max - fade.Min) * ((elapsed - fade.Increase - fade.Hold) / (float)Math.Max(1, fade.Decrease));
        else value = fade.Min;
        return (fade.White, (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f));
    }

    // ------------------------------------------------------------------------------------------------ ticking

    /// <summary>
    /// Call once per tick, split around <see cref="MissionSession.Step"/> exactly like <see cref="ScriptEngine.Observe"/>:
    /// <see cref="BeforeStep"/> first, then <c>Step()</c>, then <see cref="AfterStep"/>. Completing a presentation takes
    /// effect for <see cref="ScriptEngine"/>'s conditions starting with the <em>next</em> <c>Update</c> call (the engine's
    /// own <see cref="ScriptEngineOptions.PresentationLatencyTicks"/> auto-expiry works the same way, removing expired
    /// entries at the top of <c>Update</c> before that tick's scripts run) - completing it only after <c>Step()</c> would
    /// make a camera move finish one tick later than its real duration, since the tick whose scripts should see it as
    /// finished has already run by then.
    /// </summary>
    public void BeforeStep()
    {
        _tickBeforeStep = _session.World.Tick;
        _lastObservedTick = _tickBeforeStep + _frozenSteps;
        var tick = _lastObservedTick;
        if (MoviePlaceholder is not null && tick >= _movieExpiresAtTick) MoviePlaceholder = null;
        if (SubtitleEvent is not null && tick >= _subtitleExpiresAtTick) SubtitleEvent = null;
        for (var index = _completions.Count - 1; index >= 0; index--)
        {
            if (tick < _completions[index].AtTick) continue;
            _session.Engine.CompletePresentation(_completions[index].Key);
            _completions.RemoveAt(index);
        }
        if (_cameraPending && tick >= _cameraCompletesAtTick)
        {
            _session.Engine.CompletePresentation(CameraKey);
            _cameraPending = false;
            CameraTarget = null;
        }
        if (_inputLocked) InputLockedTicks++;
    }

    /// <summary>Call once per tick, right after <see cref="MissionSession.Step"/>: drains and applies the outputs that step
    /// produced (its durations are measured from the tick <see cref="BeforeStep"/> observed, which is the tick
    /// <see cref="ScriptEngine.Update"/> actually ran with - not <see cref="MissionSession.World"/>'s tick now, which has
    /// already moved on).</summary>
    public void AfterStep()
    {
        foreach (var output in _session.Engine.DrainOutputs())
        {
            _emitted[output.Action] = _emitted.GetValueOrDefault(output.Action) + 1;
            // Only what the client can show: the options, score-screen and command-bar outputs of a mission's first tick would bury the timeline.
            if (_journal.Count < JournalCapacity && MissionOutputCatalog.Describe(output.Action).State == MissionOutputState.Displayed) _journal.Add($"{output.Tick} {output.Action}({string.Join(", ", output.Arguments)})");
            Apply(output, _lastObservedTick);
        }
        // A step that left the world tick where it was is a frozen one (FREEZE_TIME): the presentation clock goes on without it.
        var worldStepped = _session.World.Tick != _tickBeforeStep;
        if (!worldStepped) _frozenSteps++;
        // Texts and filters are the original's per-frame updates: they advance when the presentation clock begins an original frame (every step at 30 Hz).
        if (GameClock.BeginsOriginalFrame(Clock, Time.TicksPerOriginalFrame))
        {
            AdvanceText(FrameClock);
            AdvanceFilters(worldStepped);
        }
        _radar.RemoveAll(blip => Clock - blip.CreatedAtTick > Time.OriginalFrames(RadarBlipLifetimeFrames));
    }

    /// <summary>
    /// <c>InGameUI::update</c> for the texts: the message lines fade and go, the military caption types, blinks and fades, the cinematic text runs out. One update per original frame of the
    /// presentation clock (so a text goes on while a script freezes the world, as the original's does); <paramref name="clock"/> is <see cref="FrameClock"/>.
    /// </summary>
    private void AdvanceText(long clock)
    {
        _messages?.Update(clock);
        if (_militaryCaption is { } caption)
        {
            var typed = caption.TypedCharacters;
            caption.Update(clock);
            // InGameUI::update: every appended character plays the click (static AudioEventRTS click("MilitarySubtitlesTyping")); a line break does not.
            if (caption.TypedCharacters > typed)
            {
                TypingClicks++;
                if (TryPlay("SHOW_MILITARY_CAPTION", TypingClickEvent) == true) TypingClicksStarted++;
            }
            if (caption.Finished) _militaryCaption = null;
        }
        if (_cinematic is { } cinematic && clock >= cinematic.ExpiresAtFrame) _cinematic = null;
    }

    /// <summary>
    /// The two screen filters advance once per original frame. The grey-scale ramps over its frames (<c>ScreenBWFilter::set</c> counts <c>m_curFadeFrame</c> per drawn frame; an original frame stands in for one);
    /// the motion blur adds 5 to its count for 12 frames, cuts to the target at 60 and takes 5 off for 11 more (<c>ScreenMotionBlurFilter::postRender</c>, only on a frame the game logic has changed).
    /// </summary>
    private void AdvanceFilters(bool worldStepped)
    {
        if (_bwDirection > 0)
        {
            _bwFrame++;
            if (_bwFrame < _bwFrames) _bwLevel = _bwFrame / (float)_bwFrames;
            else { _bwFrame = 0; _bwLevel = 1f; _bwDirection = 0; }
        }
        else if (_bwDirection < 0)
        {
            _bwFrame++;
            if (_bwFrame < _bwFrames) _bwLevel = 1f - _bwFrame / (float)_bwFrames;
            else { _bwFrame = 0; _bwLevel = 0f; _bwOn = false; _bwDirection = 0; }
        }
        if (!_blurActive || !worldStepped) return;
        if (!_blurDecrement)
        {
            _blurCount += MissionPostEffects.MotionBlurStep;
            if (_blurCount >= MissionPostEffects.MotionBlurMaxCount)
            {
                _blurCount = MissionPostEffects.MotionBlurMaxCount;
                _blurDecrement = true;
                _blurJumpDue = true;
            }
        }
        else
        {
            _blurCount -= MissionPostEffects.MotionBlurStep;
            if (_blurCount < 1) { _blurCount = 0; _blurActive = false; _blurDecrement = false; }
        }
    }

    private void Handled(string action, Channel channel)
    {
        if (!_handled.TryGetValue(action, out var counts)) _handled[action] = counts = new long[_consumers.Length];
        counts[(int)channel]++;
    }

    private void Problem(string action, string what) => _problems[$"{action}: {what}"] = _problems.GetValueOrDefault($"{action}: {what}") + 1;

    private static bool Number(ScriptOutput output, int index, out float value)
    {
        value = 0;
        return output.Arguments.Length > index && float.TryParse(output.Arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private (float X, float Y)? WaypointAt(ScriptOutput output, int index)
    {
        if (output.Arguments.Length <= index) { Problem(output.Action, "waypoint argument missing"); return null; }
        if (_session.Program.FindWaypoint(output.Arguments[index]) is not { } waypoint) { Problem(output.Action, $"unknown waypoint '{output.Arguments[index]}'"); return null; }
        return ((float)waypoint.X.ToDecimal(), (float)waypoint.Y.ToDecimal());
    }

    private void Apply(ScriptOutput output, long tick)
    {
        var action = output.Action;
        switch (action)
        {
            case "MOVE_CAMERA_TO" or "MOVE_CAMERA_ALONG_WAYPOINT_PATH" or "RESET_CAMERA":
            {
                // doMoveCameraTo/doMoveCameraAlongWaypointPath/doResetCamera (ScriptActions.cpp): parameter 0 is the waypoint name, 1 is the move's duration in seconds.
                // The path variant flies along the waypoint's links in the original; here the camera goes straight to the named waypoint.
                Number(output, 1, out var seconds);
                CameraTarget = WaypointAt(output, 0);
                _cameraPending = true;
                _cameraCompletesAtTick = tick + DurationTicks(seconds);
                if (CameraTarget is not { } target) return;
                _followName = null;
                _moveX.Start(target.X, seconds);
                _moveY.Start(target.Y, seconds);
                if (action == "RESET_CAMERA") _resetRequest = seconds;
                Handled(action, Channel.Camera);
                return;
            }
            case "SETUP_CAMERA":
            {
                // doSetupCamera(waypoint, zoom, pitch, lookAtWaypoint): an instant cut to the waypoint, looking at the second one.
                if (WaypointAt(output, 0) is not { } position || WaypointAt(output, 3) is not { } lookAt) return;
                CameraTarget = position;
                _followName = null;
                _moveX.Start(position.X, 0);
                _moveY.Start(position.Y, 0);
                _lookRequest = (new Vector2(lookAt.X, lookAt.Y), 0);
                if (Number(output, 1, out var zoom)) _zoomRequest = (zoom, 0);
                if (Number(output, 2, out var pitch)) _pitchRequest = (pitch, 0);
                Handled(action, Channel.Camera);
                return;
            }
            case "ROTATE_CAMERA":
            {
                // doRotateCamera(rotations, sec, easeIn, easeOut): no waypoint, a rotation in place.
                Number(output, 1, out var seconds);
                CameraTarget = null;
                _cameraPending = true;
                _cameraCompletesAtTick = tick + DurationTicks(seconds);
                if (!Number(output, 0, out var turns)) { Problem(action, "rotation argument missing"); return; }
                _rotateRequest = (turns, seconds);
                Handled(action, Channel.Camera);
                return;
            }
            case "ZOOM_CAMERA" or "PITCH_CAMERA":
            {
                // doZoomCamera(zoom, sec, ...) / doPitchCamera(pitch, sec, ...).
                if (!Number(output, 0, out var factor)) { Problem(action, "value argument missing"); return; }
                Number(output, 1, out var seconds);
                if (action == "ZOOM_CAMERA") _zoomRequest = (factor, seconds);
                else _pitchRequest = (factor, seconds);
                Handled(action, Channel.Camera);
                return;
            }
            case "CAMERA_MOD_SET_FINAL_ZOOM" or "CAMERA_MOD_SET_FINAL_PITCH":
            {
                // cameraModFinalZoom/Pitch change where the move in progress ends; here they reach their value together with that move.
                if (!Number(output, 0, out var factor)) { Problem(action, "value argument missing"); return; }
                var seconds = Math.Max(0, (_cameraCompletesAtTick - tick) / (double)Time.TicksPerSecond);
                if (action == "CAMERA_MOD_SET_FINAL_ZOOM") _zoomRequest = (factor, seconds);
                else _pitchRequest = (factor, seconds);
                Handled(action, Channel.Camera);
                return;
            }
            case "CAMERA_MOD_LOOK_TOWARD" or "CAMERA_MOD_FINAL_LOOK_TOWARD" or "CAMERA_LOOK_TOWARD_WAYPOINT":
            {
                if (WaypointAt(output, 0) is not { } lookAt) return;
                double seconds;
                if (action == "CAMERA_LOOK_TOWARD_WAYPOINT")
                {
                    // doRotateCameraTowardWaypoint(waypoint, sec, ...): a camera movement of its own.
                    Number(output, 1, out var own);
                    seconds = own;
                    _cameraPending = true;
                    _cameraCompletesAtTick = tick + DurationTicks(seconds);
                }
                else seconds = Math.Max(0, (_cameraCompletesAtTick - tick) / (double)Time.TicksPerSecond);
                _lookRequest = (new Vector2(lookAt.X, lookAt.Y), seconds);
                Handled(action, Channel.Camera);
                return;
            }
            case "CAMERA_FOLLOW_NAMED":
                // doCameraFollowNamed(unit, snapToUnit): the view locks onto the object until CAMERA_STOP_FOLLOW.
                if (output.Arguments.Length == 0 || !TryNamedPosition(output.Arguments[0], out _, out _)) { Problem(action, "unknown named object"); return; }
                _followName = output.Arguments[0];
                _moveX.Stop();
                _moveY.Stop();
                Handled(action, Channel.Camera);
                return;
            case "CAMERA_STOP_FOLLOW":
                _followName = null;
                Handled(action, Channel.Camera);
                return;
            case "CAMERA_MOVE_HOME" or "MOVE_CAMERA_TO_SELECTION":
                // Neither retail action takes a duration (doCameraMoveHome is empty in the reference); complete on the very next tick's BeforeStep().
                CameraTarget = null;
                _cameraPending = true;
                _cameraCompletesAtTick = tick;
                return;
            case "CAMERA_LETTERBOX_BEGIN":
                SetLetterbox(true);
                Handled(action, Channel.Overlay);
                return;
            case "CAMERA_LETTERBOX_END":
                SetLetterbox(false);
                Handled(action, Channel.Overlay);
                return;
            case "CAMERA_FADE_ADD" or "CAMERA_FADE_SUBTRACT" or "CAMERA_FADE_MULTIPLY":
            {
                // doCameraFade*(minFade, maxFade, framesIncrease, framesHold, framesDecrease): the frame counts are original frames whatever the tick rate. Add brightens towards white, subtract darkens towards black; multiply
                // darkens as its factor falls below 1, so it is shown as black with the inverted factor.
                if (!Number(output, 0, out var min) || !Number(output, 1, out var max) || !Number(output, 2, out var increase) || !Number(output, 3, out var hold) || !Number(output, 4, out var decrease))
                {
                    Problem(action, "fade arguments missing");
                    return;
                }
                if (action == "CAMERA_FADE_MULTIPLY") (min, max) = (1 - min, 1 - max);
                _fade = (action == "CAMERA_FADE_ADD", min, max, tick, Time.OriginalFrames((long)increase), Time.OriginalFrames((long)hold), Time.OriginalFrames((long)decrease));
                Handled(action, Channel.Overlay);
                return;
            }
            case "DISABLE_INPUT":
                _inputLocked = true;
                InputDisabled?.Invoke();
                Handled(action, Channel.Input);
                return;
            case "ENABLE_INPUT":
                _inputLocked = false;
                Handled(action, Channel.Input);
                return;
            case "SPEECH_PLAY" or "SOUND_PLAY_NAMED" or "PLAY_SOUND_EFFECT" or "PLAY_SOUND_EFFECT_AT":
            {
                if (output.Arguments.Length == 0) { Problem(action, "event name missing"); return; }
                var name = output.Arguments[0];
                var played = TryPlay(action, name) == true;
                if (played) Handled(action, Channel.Audio);
                else Problem(action, !HasAudio ? "no audio connected" : $"'{name}' did not start");
                if (action is "SPEECH_PLAY" or "SOUND_PLAY_NAMED")
                {
                    if (action == "SPEECH_PLAY")
                    {
                        // doSpeechPlay: the subtitle label DIALOGEVENT:<name>Subtitle is typed out as a military caption for 8 seconds when the string table has it and it is neither empty nor
                        // starts with '*' (the retail English table marks 2454 of its 2455 subtitles with '*', so the original shows none of them).
                        TryShowSpeechSubtitle(name, tick);
                        // Apart from that no text exists for a speech event: its name is shown, marked as a placeholder, whether or not the sound plays.
                        SubtitleEvent = name;
                        _subtitleExpiresAtTick = tick + Time.OriginalFrames(DefaultPlaceholderFrames);
                        if (!played) Handled(action, Channel.Overlay);
                    }
                    _completions.Add((name, tick + Time.OriginalFrames(DefaultPlaceholderFrames)));
                }
                return;
            }
            case "MUSIC_SET_TRACK":
                if (output.Arguments.Length == 0) { Problem(action, "track name missing"); return; }
                if (TryPlay(action, output.Arguments[0]) == true) Handled(action, Channel.Audio);
                else Problem(action, !HasAudio ? "no audio connected" : $"'{output.Arguments[0]}' did not start");
                _completions.Add((output.Arguments[0], tick));
                return;
            case "MOVIE_PLAY_FULLSCREEN" or "MOVIE_PLAY_RADAR":
                if (output.Arguments.Length == 0) { Problem(action, "movie name missing"); return; }
                MoviePlaceholder = output.Arguments[0];
                _movieExpiresAtTick = tick + Time.OriginalFrames(DefaultPlaceholderFrames);
                _completions.Add((output.Arguments[0], tick + Time.OriginalFrames(DefaultPlaceholderFrames)));
                Handled(action, Channel.Overlay);
                return;
            case "DISPLAY_TEXT":
            {
                // doDisplayText -> InGameUI::message: one more line of the message list at the top left (the label's text), which fades on its own.
                if (output.Arguments.Length == 0) { Problem(action, "text label missing"); return; }
                if (TextStyle is not { } style) { Problem(action, "no InGameUI style connected"); return; }
                (_messages ??= new UiMessageLog(style.Settings)).Add(ResolveLabel(action, output.Arguments[0]), FrameOf(tick));
                Handled(action, Channel.Overlay);
                return;
            }
            case "INGAME_POPUP_MESSAGE":
            {
                // doInGamePopupMessage(message, x, y, width, pause) -> InGameUI::popupMessage: a window of InGamePopupMessage.wnd at x/y percent of the screen, closed by its OK button.
                if (output.Arguments.Length < 5 || !int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var popupX)
                    || !int.TryParse(output.Arguments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var popupY) || !int.TryParse(output.Arguments[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var popupWidth)
                    || !int.TryParse(output.Arguments[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var popupPause))
                {
                    Problem(action, "popup arguments missing");
                    return;
                }
                ShowPopup(new PopupMessage(ResolveLabel(action, output.Arguments[0]), Math.Clamp(popupX, 0, 100), Math.Clamp(popupY, 0, 100), Math.Max(50, popupWidth), popupPause != 0));
                Handled(action, Channel.Overlay);
                return;
            }
            case "DISPLAY_CINEMATIC_TEXT":
            {
                // doDisplayCinematicText(displayText, fontType, timeInSeconds): W3DDisplay::draw shows it for 30 * seconds frames, centered near the bottom, in the font of the script.
                if (output.Arguments.Length < 3) { Problem(action, "arguments missing"); return; }
                if (!MissionTextOverlay.TryParseCinematicFont(output.Arguments[1], out var font, out var fontProblem)) { Problem(action, fontProblem); return; }
                if (!Number(output, 2, out var seconds)) { Problem(action, "duration missing"); return; }
                _cinematic = new CinematicTextState(ResolveLabel(action, output.Arguments[0]), font, FrameOf(tick) + (long)seconds * OriginalFrame.PerSecond);
                Handled(action, Channel.Overlay);
                return;
            }
            case "SHOW_MILITARY_CAPTION":
            {
                // doMilitaryCaption(label, durationMs) -> InGameUI::militarySubtitle: replaces the caption on screen, then types the new one.
                if (output.Arguments.Length < 2 || !long.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs)) { Problem(action, "caption arguments missing"); return; }
                if (TextStyle is not { } style) { Problem(action, "no InGameUI style connected"); return; }
                _militaryCaption = null;
                var language = style.Language.Settings;
                _militaryCaption = MilitaryCaptionState.Begin(ResolveLabel(action, output.Arguments[0]), durationMs, FrameOf(tick), style.Settings, language.MilitaryCaptionSpeed, language.MilitaryCaptionDelayMilliseconds, out var problem);
                if (_militaryCaption is null) Problem(action, problem ?? "no caption");
                else Handled(action, Channel.Overlay);
                return;
            }
            case "DISPLAY_COUNTDOWN_TIMER" or "DISPLAY_COUNTER":
                // doDisplayCountdownTimer(timerName, timerText) / doDisplayCounter(counterName, counterText): a named line of the timer display.
                if (output.Arguments.Length < 2) { Problem(action, "arguments missing"); return; }
                _counters[output.Arguments[0]] = (ResolveLabel(action, output.Arguments[1]), action == "DISPLAY_COUNTDOWN_TIMER");
                Handled(action, Channel.Overlay);
                return;
            case "HIDE_COUNTDOWN_TIMER" or "HIDE_COUNTER":
                if (output.Arguments.Length == 0) { Problem(action, "arguments missing"); return; }
                _counters.Remove(output.Arguments[0]);
                Handled(action, Channel.Overlay);
                return;
            case "ENABLE_COUNTDOWN_TIMER_DISPLAY" or "DISABLE_COUNTDOWN_TIMER_DISPLAY":
                _countersVisible = action == "ENABLE_COUNTDOWN_TIMER_DISPLAY";
                Handled(action, Channel.Overlay);
                return;
            case "RADAR_CREATE_EVENT":
                if (output.Arguments.Length > 1 && TryParseCoord(output.Arguments[0], out var x, out var y) && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventType))
                {
                    _radar.Add(new RadarBlip(x, y, eventType, tick));
                    Handled(action, Channel.Radar);
                }
                else Problem(action, "arguments missing");
                return;
            case "OBJECT_CREATE_RADAR_EVENT":
                if (output.Arguments.Length > 1 && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectEventType)
                    && TryNamedPosition(output.Arguments[0], out var ox, out var oy))
                {
                    _radar.Add(new RadarBlip(ox, oy, objectEventType, tick));
                    Handled(action, Channel.Radar);
                }
                else Problem(action, "unknown named object");
                return;
            case "TEAM_CREATE_RADAR_EVENT":
                if (output.Arguments.Length > 1 && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamEventType)
                    && TryTeamPosition(output.Arguments[0], out var tx, out var ty))
                {
                    _radar.Add(new RadarBlip(tx, ty, teamEventType, tick));
                    Handled(action, Channel.Radar);
                }
                else Problem(action, "team has no living member");
                return;
            case "VICTORY" or "DEFEAT":
                OutcomeBanner = action == "VICTORY" ? "MISSION ACCOMPLISHED" : "MISSION FAILED";
                _inputLocked = false;
                Handled(action, Channel.Overlay);
                return;
            default:
                ApplyScene(output, tick);
                return;
        }
    }

    private const long SpeechSubtitleMilliseconds = 8000;

    private void TryShowSpeechSubtitle(string speechName, long tick)
    {
        if (TextStyle is not { } style) return;
        var label = $"DIALOGEVENT:{speechName}Subtitle";
        var subtitle = _resolveText(label);
        // A host that cannot resolve a label hands it back unchanged: that is "not found" here, and the usual case (the retail table has labels for most speech events).
        if (string.Equals(subtitle, label, StringComparison.Ordinal) || subtitle.Length == 0 || subtitle[0] == '*') return;
        var language = style.Language.Settings;
        _militaryCaption = MilitaryCaptionState.Begin(subtitle, SpeechSubtitleMilliseconds, FrameOf(tick), style.Settings, language.MilitaryCaptionSpeed, language.MilitaryCaptionDelayMilliseconds, out _);
    }

    private bool HasAudio => AudioControl is not null || Audio is not null;

    /// <summary>Plays a scripted sound through the host's audio; <see langword="null"/> when no audio is connected.</summary>
    private bool? TryPlay(string action, string eventName)
    {
        if (AudioControl is { } control) return control.PlayScripted(action, eventName);
        return Audio?.Invoke(action, eventName);
    }

    private bool TryNamedId(string name, out EntityId id)
    {
        id = EntityId.Invalid;
        foreach (var (named, value) in _session.Engine.NamedObjects)
        {
            if (!string.Equals(named, name, StringComparison.Ordinal)) continue;
            id = new EntityId(value);
            return _session.World.FindEntity(id) is not null;
        }
        return false;
    }

    private IEnumerable<EntityId> TeamMembers(string teamName)
    {
        var team = _session.Engine.TeamInstances.FirstOrDefault(instance => string.Equals(instance.Prototype, teamName, StringComparison.Ordinal) && instance.Members.Count > 0);
        if (team is null) yield break;
        foreach (var member in team.Members)
            if (_session.World.FindEntity(new EntityId(member)) is not null) yield return new EntityId(member);
    }

    /// <summary>The object a script names, or the living members of the team it names.</summary>
    private List<EntityId> Targets(bool namedObject, string name)
    {
        if (!namedObject) return [..TeamMembers(name)];
        return TryNamedId(name, out var id) ? [id] : [];
    }

    private static string Argument(ScriptOutput output, int index) => output.Arguments.Length > index ? output.Arguments[index] : string.Empty;

    /// <summary>
    /// The outputs that change the sound, the control bar, single objects and the screen filters rather than the camera and the overlay (<c>docs/SCRIPT-ENGINE.md</c> §7a). Each case names the
    /// <c>ScriptActions</c> function it follows; a consumer that is not connected leaves a reported problem, and the counter stays at "not displayed".
    /// </summary>
    private void ApplyScene(ScriptOutput output, long tick)
    {
        var action = output.Action;
        switch (action)
        {
            // ---- sound control: ScriptActions::doAudioSetVolume, doSoundOverrideVolume, doSoundEnableType, doSoundRemoveAllDisabled, doSoundRemoveType, doEnableObjectSound, doEvaEnabledDisabled, doAmbientSoundsPause
            case "MUSIC_SET_VOLUME" or "SOUND_SET_VOLUME" or "SPEECH_SET_VOLUME":
            {
                if (!Number(output, 0, out var percent)) { Problem(action, "volume argument missing"); return; }
                if (AudioControl is not { } control) { Problem(action, "no audio control connected"); return; }
                control.SetVolume(action == "MUSIC_SET_VOLUME" ? MissionVolume.Music : action == "SOUND_SET_VOLUME" ? MissionVolume.Sound : MissionVolume.Speech, percent);
                Handled(action, Channel.Audio);
                return;
            }
            case "AUDIO_OVERRIDE_VOLUME_TYPE" or "AUDIO_RESTORE_VOLUME_TYPE" or "AUDIO_RESTORE_VOLUME_ALL_TYPE" or "SOUND_DISABLE_TYPE" or "SOUND_ENABLE_TYPE" or "SOUND_ENABLE_ALL"
                or "SOUND_REMOVE_ALL_DISABLED" or "SOUND_REMOVE_TYPE" or "EVA_SET_ENABLED_DISABLED" or "SOUND_AMBIENT_PAUSE" or "SOUND_AMBIENT_RESUME":
            {
                if (AudioControl is not { } control) { Problem(action, "no audio control connected"); return; }
                var name = action is "AUDIO_RESTORE_VOLUME_ALL_TYPE" or "SOUND_ENABLE_ALL" or "SOUND_REMOVE_ALL_DISABLED" or "EVA_SET_ENABLED_DISABLED" or "SOUND_AMBIENT_PAUSE" or "SOUND_AMBIENT_RESUME" ? string.Empty : Argument(output, 0);
                if (name.Length == 0 && action is "AUDIO_OVERRIDE_VOLUME_TYPE" or "AUDIO_RESTORE_VOLUME_TYPE" or "SOUND_DISABLE_TYPE" or "SOUND_ENABLE_TYPE" or "SOUND_REMOVE_TYPE") { Problem(action, "event name missing"); return; }
                switch (action)
                {
                    case "AUDIO_OVERRIDE_VOLUME_TYPE":
                        if (!Number(output, 1, out var percent)) { Problem(action, "volume argument missing"); return; }
                        control.OverrideEventVolume(name, percent);
                        break;
                    case "AUDIO_RESTORE_VOLUME_TYPE" or "AUDIO_RESTORE_VOLUME_ALL_TYPE": control.OverrideEventVolume(name, -100f); break;
                    case "SOUND_DISABLE_TYPE": control.SetEventEnabled(name, false); break;
                    case "SOUND_ENABLE_TYPE" or "SOUND_ENABLE_ALL": control.SetEventEnabled(name, true); break;
                    case "SOUND_REMOVE_ALL_DISABLED": control.RemoveDisabledEvents(); break;
                    case "SOUND_REMOVE_TYPE": control.RemoveEvent(name); break;
                    case "EVA_SET_ENABLED_DISABLED":
                        if (!Number(output, 0, out var enabled)) { Problem(action, "flag argument missing"); return; }
                        control.SetEvaEnabled(enabled != 0f);
                        break;
                    default: control.SetAmbientPaused(action == "SOUND_AMBIENT_PAUSE"); break;
                }
                Handled(action, Channel.Audio);
                return;
            }
            case "ENABLE_OBJECT_SOUND" or "DISABLE_OBJECT_SOUND":
            {
                if (AudioControl is not { } control) { Problem(action, "no audio control connected"); return; }
                if (!TryNamedId(Argument(output, 0), out var entity)) { Problem(action, "unknown named object"); return; }
                if (control.SetObjectSoundEnabled(entity, action == "ENABLE_OBJECT_SOUND")) Handled(action, Channel.Audio);
                else Problem(action, "the object has no ambient sound to switch");
                return;
            }

            // ---- time: the simulation freezes the world itself (MissionSession.Step); nothing for the host to do
            case "FREEZE_TIME" or "UNFREEZE_TIME":
                return;

            // ---- camera: ScriptActions::doScreenShake, doBorderSwitch, doRotateCameraTowardObject, doCameraTetherNamed
            case "SCREEN_SHAKE":
                if (!Number(output, 0, out var shake) || shake is < 0 or > 5) { Problem(action, "shake type must be 0..5"); return; }
                _pendingShakes.Enqueue((int)shake);
                return;
            case "MAP_SWITCH_BORDER":
                if (!Number(output, 0, out var border)) { Problem(action, "border argument missing"); return; }
                _pendingBoundary = (int)border;
                return;
            case "CAMERA_LOOK_TOWARD_OBJECT":
            {
                // doRotateCameraTowardObject(name, sec, holdSec, easeIn, easeOut): a camera movement of its own that turns the view to the object and holds it there.
                if (!TryNamedPosition(Argument(output, 0), out var x, out var y)) { Problem(action, "unknown named object"); return; }
                Number(output, 1, out var seconds);
                Number(output, 2, out var hold);
                _cameraPending = true;
                _cameraCompletesAtTick = tick + DurationTicks(seconds + Math.Max(0, hold));
                _lookRequest = (new Vector2(x, y), seconds);
                Handled(action, Channel.Camera);
                return;
            }
            case "CAMERA_TETHER_NAMED":
                // doCameraTetherNamed(unit, snapToUnit, play): approximated by the follow lock; the object is centred instead of being allowed a play radius.
                if (!TryNamedPosition(Argument(output, 0), out _, out _)) { Problem(action, "unknown named object"); return; }
                _followName = Argument(output, 0);
                _moveX.Stop();
                _moveY.Stop();
                Handled(action, Channel.Camera);
                return;
            case "CAMERA_STOP_TETHER_NAMED":
                _followName = null;
                Handled(action, Channel.Camera);
                return;

            // ---- screen filters: ScriptActions::doBlackWhiteMode, doCameraMotionBlurJump
            case "CAMERA_BW_MODE_BEGIN":
                Number(output, 0, out var beginFrames);
                _blurActive = false;
                _blurCount = 0;
                _bwOn = true;
                _bwDirection = 1;
                _bwFrames = Math.Max(0, (int)beginFrames);
                _bwFrame = 0;
                Handled(action, Channel.Post);
                return;
            case "CAMERA_BW_MODE_END":
                // Only a grey-scale that is on can be faded out ("mode already set, turn it off").
                Number(output, 0, out var endFrames);
                if (_bwOn)
                {
                    _bwDirection = -1;
                    _bwFrames = Math.Max(0, (int)endFrames);
                    _bwFrame = 0;
                }
                Handled(action, Channel.Post);
                return;
            case "CAMERA_MOTION_BLUR_JUMP":
            {
                // The zoom blur replaces the grey-scale (one view filter at a time) and cuts the camera to the waypoint at its apex.
                if (WaypointAt(output, 0) is not { } target) return;
                Number(output, 1, out var saturate);
                _bwOn = false;
                _bwDirection = 0;
                _bwLevel = 0f;
                _blurActive = true;
                _blurDecrement = false;
                _blurAdditive = saturate != 0f;
                _blurCount = 0;
                _blurTarget = target;
                _blurJumpDue = false;
                Handled(action, Channel.Post);
                return;
            }

            // ---- objects: ScriptActions::doNamedFlash, doTeamFlash, doNamedCustomColor
            case "NAMED_FLASH" or "NAMED_FLASH_WHITE" or "TEAM_FLASH" or "TEAM_FLASH_WHITE":
            {
                if (ObjectEffects is not { } effects) { Problem(action, "no object layer connected"); return; }
                Number(output, 1, out var seconds);
                if (seconds <= 0) return;
                var white = action.EndsWith("_WHITE", StringComparison.Ordinal);
                var members = Targets(action.StartsWith("NAMED", StringComparison.Ordinal), Argument(output, 0));
                if (members.Count == 0) { Problem(action, action.StartsWith("NAMED", StringComparison.Ordinal) ? "unknown named object" : "team has no living member"); return; }
                var flashed = false;
                foreach (var member in members) flashed |= effects.Flash(member, white ? Vector3.One : null, (int)seconds);
                if (flashed) Handled(action, Channel.Objects);
                else Problem(action, "the object is not drawn");
                return;
            }
            case "NAMED_CUSTOM_COLOR":
            {
                if (ObjectEffects is not { } effects) { Problem(action, "no object layer connected"); return; }
                if (!TryNamedId(Argument(output, 0), out var entity)) { Problem(action, "unknown named object"); return; }
                if (!long.TryParse(Argument(output, 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var color)) { Problem(action, "color argument missing"); return; }
                if (effects.SetHouseColor(entity, unchecked((uint)color))) Handled(action, Channel.Objects);
                else Problem(action, "the object is not drawn");
                return;
            }

            // ---- control bar and HUD: ScriptActions::doRemoveCommandBarButton, doAddCommandBarButton, doPlayerSetScienceAvailability, doSetDrawIconUIMode, changeObjectPanelFlagForSingleObject
            case "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE" or "COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT":
            {
                if (Hud is not { } hud) { Problem(action, "no HUD connected"); return; }
                var ok = action == "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE"
                    ? hud.RemoveCommandButton(Argument(output, 0), Argument(output, 1))
                    : Number(output, 2, out var slot) && hud.AddCommandButton(Argument(output, 0), Argument(output, 1), (int)slot);
                if (ok) Handled(action, Channel.Hud);
                else Problem(action, $"button '{Argument(output, 0)}' or object type '{Argument(output, 1)}' unknown to the command sets");
                return;
            }
            case "PLAYER_SCIENCE_AVAILABILITY":
            {
                if (Hud is not { } hud) { Problem(action, "no HUD connected"); return; }
                if (_session.Program.FindPlayer(Argument(output, 0)) is not { } player) { Problem(action, $"unknown player '{Argument(output, 0)}'"); return; }
                if (player.Id != _human) { Problem(action, "availability of a side other than the local player (no UI for it)"); return; }
                if (hud.SetScienceAvailability(Argument(output, 1), Argument(output, 2))) Handled(action, Channel.Hud);
                else Problem(action, $"science '{Argument(output, 1)}' or availability '{Argument(output, 2)}' unknown");
                return;
            }
            case "OPTIONS_SET_DRAWICON_UI_MODE":
                if (Hud is not { } iconHud) { Problem(action, "no HUD connected"); return; }
                if (!Number(output, 0, out var icons)) { Problem(action, "flag argument missing"); return; }
                iconHud.SetIconUiEnabled(icons != 0f);
                Handled(action, Channel.Hud);
                return;
            case "UNIT_AFFECT_OBJECT_PANEL_FLAGS" or "TEAM_AFFECT_OBJECT_PANEL_FLAGS":
            {
                if (Hud is not { } hud) { Problem(action, "no HUD connected"); return; }
                var flag = Argument(output, 1);
                if (!Number(output, 2, out var on)) { Problem(action, "flag value missing"); return; }
                if (!flag.Equals("Selectable", StringComparison.OrdinalIgnoreCase)) { Problem(action, $"flag '{flag}' has no consumer (it needs simulation state)"); return; }
                var targets = Targets(action.StartsWith("UNIT", StringComparison.Ordinal), Argument(output, 0));
                if (targets.Count == 0) { Problem(action, "unknown named object or empty team"); return; }
                var acted = false;
                foreach (var target in targets) acted |= hud.SetObjectSelectable(target, on != 0f);
                if (acted) Handled(action, Channel.Hud);
                return;
            }

            // ---- state a later screen reads (no consumer in this client, see MissionOutputCatalog)
            case "ENABLE_SCORING" or "DISABLE_SCORING":
                ScoringEnabled = action == "ENABLE_SCORING";
                return;
            case "PLAYER_EXCLUDE_FROM_SCORE_SCREEN":
                _scoreScreenExcluded.Add(Argument(output, 0));
                return;
            case "ENABLE_SPECIAL_POWER_DISPLAY" or "DISABLE_SPECIAL_POWER_DISPLAY":
                // InGameUI::setSuperweaponDisplayEnabledByScript: the whole superweapon timer list.
                SpecialPowerDisplayEnabled = action == "ENABLE_SPECIAL_POWER_DISPLAY";
                if (Hud is not { } powerHud) { Problem(action, "no HUD connected"); return; }
                powerHud.SetSpecialPowerDisplay(SpecialPowerDisplayEnabled);
                Handled(action, Channel.Hud);
                return;
            case "NAMED_HIDE_SPECIAL_POWER_DISPLAY" or "NAMED_SHOW_SPECIAL_POWER_DISPLAY":
            {
                // InGameUI::hideObjectSuperweaponDisplayByScript / showObjectSuperweaponDisplayByScript: the timer of one object.
                if (!TryNamedId(Argument(output, 0), out var powerObject)) { Problem(action, "unknown named object"); return; }
                var visible = action == "NAMED_SHOW_SPECIAL_POWER_DISPLAY";
                if (visible) _hiddenPowerDisplays.Remove(powerObject.Value);
                else _hiddenPowerDisplays.Add(powerObject.Value);
                if (Hud is not { } objectHud) { Problem(action, "no HUD connected"); return; }
                if (objectHud.SetObjectSpecialPowerDisplay(powerObject, visible)) Handled(action, Channel.Hud);
                return;
            }
            case "DRAW_SKYBOX_BEGIN" or "DRAW_SKYBOX_END":
                SkyboxRequested = action == "DRAW_SKYBOX_BEGIN";
                return;
            case "ENABLE_BORDER_SHROUD" or "DISABLE_BORDER_SHROUD":
                BorderShroudEnabled = action == "ENABLE_BORDER_SHROUD";
                return;
        }
    }

    // ------------------------------------------------------------------------------------------------ counters

    /// <summary>What the client does with an output type, or why it does not (<see cref="MissionOutputCatalog"/>), for the report and <c>docs/SCRIPT-ENGINE.md</c> §7a.</summary>
    public static string HowDisplayed(string action) => MissionOutputCatalog.Describe(action).Description;

    /// <summary>Per output type: emitted by the scripts, handed to a consumer, displayed (the consumer exists in this host and the type has a display at all).</summary>
    public ImmutableArray<MissionOutputCount> Counts()
    {
        var result = ImmutableArray.CreateBuilder<MissionOutputCount>();
        foreach (var (action, emitted) in _emitted)
        {
            var info = MissionOutputCatalog.Describe(action);
            long forwarded = 0, displayed = 0;
            if (_handled.TryGetValue(action, out var counts))
            {
                for (var channel = 0; channel < counts.Length; channel++)
                {
                    forwarded += counts[channel];
                    // Audio is only counted when a voice really started or a control was applied; the other channels need a host that asked for them.
                    if (channel == (int)Channel.Audio || _consumers[channel]) displayed += counts[channel];
                }
            }
            // A type without a display ("not displayed: reason") is never shown, however often a model kept its value; one the simulation handles is not the host's.
            if (info.State != MissionOutputState.Displayed) displayed = 0;
            result.Add(new MissionOutputCount(action, emitted, forwarded, displayed, info.Description, info.State));
        }
        return result.ToImmutable();
    }
    /// <summary>Why outputs of a handled type could not be shown (unknown waypoint, sound that did not start, ...), with counts.</summary>
    public ImmutableSortedDictionary<string, long> Problems => _problems.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The console report of a mission run: one line per output type and the totals.</summary>
    public ImmutableArray<string> Report()
    {
        var counts = Counts();
        var lines = ImmutableArray.CreateBuilder<string>();
        var culture = CultureInfo.InvariantCulture;
        lines.Add(string.Create(culture, $"mission outputs: {counts.Length} types, emitted {counts.Sum(count => count.Emitted)}, forwarded {counts.Sum(count => count.Forwarded)}, displayed {counts.Sum(count => count.Displayed)}, handled elsewhere {counts.Sum(count => count.HandledElsewhere)}, not displayed {counts.Sum(count => count.NotDisplayed)}"));
        foreach (var count in counts)
            lines.Add(string.Create(culture, $"mission output {count.Action}: emitted {count.Emitted}, forwarded {count.Forwarded}, displayed {count.Displayed}, handled elsewhere {count.HandledElsewhere}, not displayed {count.NotDisplayed} ({count.How})"));
        lines.Add($"mission output problems: {(_problems.Count == 0 ? "none" : string.Join("; ", _problems.Select(pair => string.Create(culture, $"{pair.Key} x{pair.Value}"))))}");
        lines.Add(string.Create(culture, $"mission presentation: input locked for {InputLockedTicks} ticks, locked at exit {_inputLocked}, letterbox at exit {Letterbox}, camera move pending at exit {_cameraPending}, outcome banner {OutcomeBanner ?? "none"}"));
        lines.Add(string.Create(culture, $"mission caption typing: {TypingClicks} characters typed, {TypingClicksStarted} clicks started ({(HasAudio ? TypingClickEvent : "no audio connected")})"));
        return lines.ToImmutable();
    }

    // Script seconds to ticks of the session's world, rounded to whole original frames first (the deadline falls on the same game time at every rate).
    private long DurationTicks(double seconds) => Time.OriginalFrames(Math.Max(0, (long)Math.Round(seconds * OriginalFrame.PerSecond)));

    /// <summary>The text of a CSF label; a label the host cannot resolve comes back unchanged and is counted as a problem, so the screen shows the label and the report says why.</summary>
    private string ResolveLabel(string action, string label)
    {
        var text = _resolveText(label);
        if (string.Equals(text, label, StringComparison.Ordinal)) Problem(action, $"label '{label}' has no text (shown as the label)");
        return text;
    }

    private static bool TryParseCoord(string text, out float x, out float y)
    {
        x = y = 0;
        var parts = text.Split(',');
        return parts.Length >= 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
    }

    private bool TryNamedPosition(string name, out float x, out float y)
    {
        x = y = 0;
        foreach (var (named, id) in _session.Engine.NamedObjects)
        {
            if (!string.Equals(named, name, StringComparison.Ordinal)) continue;
            if (_session.World.FindEntity(new EntityId(id)) is not { } entity) return false;
            x = (float)entity.X.ToDecimal();
            y = (float)entity.Y.ToDecimal();
            return true;
        }
        return false;
    }

    private bool TryTeamPosition(string teamName, out float x, out float y)
    {
        x = y = 0;
        var team = _session.Engine.TeamInstances.FirstOrDefault(instance => string.Equals(instance.Prototype, teamName, StringComparison.Ordinal));
        if (team is null || team.Members.Count == 0) return false;
        if (_session.World.FindEntity(new EntityId(team.Members[0])) is not { } entity) return false;
        x = (float)entity.X.ToDecimal();
        y = (float)entity.Y.ToDecimal();
        return true;
    }
}

/// <summary>
/// Bridges the human player's in-mission UI to the <see cref="ScriptEngine"/> feedback it reads
/// (<c>docs/HANDOFF-PHASE10.md</c> item 7): selecting a named object calls <see cref="ScriptEngine.SelectNamed"/> so
/// <c>NAMED_SELECTED</c> conditions resolve, and <see cref="NotifyUiInteraction"/> is a direct passthrough to
/// <see cref="ScriptEngine.SignalUiInteraction"/> for whatever player-driven interaction a mission script waits on with a
/// <c>FLAG</c> condition. This build does not model a table from a specific command-bar button to a specific mission's
/// flag name (missions do not carry that metadata anywhere in the compiled content), so a host calls
/// <see cref="NotifyUiInteraction"/> with whatever flag name is relevant to the UI element it owns; the plumbing to the
/// engine is what this class guarantees.
/// </summary>
public sealed class MissionUiBridge
{
    private readonly MissionSession _session;
    private readonly Func<EntityId, string?> _scriptNameOf;

    /// <param name="scriptNameOf">Looks up the script name of a selected entity, if any (reverse of
    /// <see cref="ScriptEngine.NamedObjects"/>); a host already holding that map (e.g. from spawning/placing) may supply a
    /// faster lookup, otherwise <see cref="Default"/> scans <see cref="ScriptEngine.NamedObjects"/> each call.</param>
    public MissionUiBridge(MissionSession session, Func<EntityId, string?>? scriptNameOf = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _scriptNameOf = scriptNameOf ?? Default;
    }

    private string? Default(EntityId id)
    {
        foreach (var (name, value) in _session.Engine.NamedObjects)
            if (value == id.Value) return name;
        return null;
    }

    /// <summary>Call when the player's selection changes (<c>SelectionModel.SelectionChanged</c>): every selected entity that
    /// is a named mission object is reported to the engine (<c>NAMED_SELECTED</c>); selection is not cleared here (a mission
    /// script clears it itself via <c>CLEAR_SELECTED_NAMED</c>/winning the game, same as the reference).</summary>
    public void OnSelectionChanged(ImmutableArray<EntityId> selected)
    {
        foreach (var id in selected)
            if (_scriptNameOf(id) is { Length: > 0 } name) _session.Engine.SelectNamed(name);
    }

    /// <summary>Direct passthrough to <see cref="ScriptEngine.SignalUiInteraction"/>.</summary>
    public void NotifyUiInteraction(string flagName) => _session.Engine.SignalUiInteraction(flagName);
}
