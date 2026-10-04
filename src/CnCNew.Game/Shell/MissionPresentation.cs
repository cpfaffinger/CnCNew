using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell;

/// <summary>One <c>RADAR_CREATE_EVENT</c>/<c>OBJECT_CREATE_RADAR_EVENT</c>/<c>TEAM_CREATE_RADAR_EVENT</c> output, with the
/// world position resolved (for the object/team forms) at the tick the event fired. <see cref="EventType"/> is the raw
/// <c>RadarEventType</c> ordinal the script passed (<c>doRadarCreateEvent</c>, <c>ScriptActions.cpp</c>); this build does
/// not map it to a color/icon yet, so a host renders every blip the same way until it does.</summary>
public readonly record struct RadarBlip(float X, float Y, int EventType, long CreatedAtTick);

/// <summary>What happened to one <see cref="ScriptOutput"/> type over a mission: how often the scripts emitted it, how often it was handed to a consumer
/// (camera, overlay, audio, radar, input), and how often that consumer exists in the running host - so "displayed" is never claimed for a headless run.</summary>
public readonly record struct MissionOutputCount(string Action, long Emitted, long Forwarded, long Displayed, string How)
{
    public long NotDisplayed => Emitted - Displayed;
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
    private const long DefaultTextTicks = 5 * SimulationTiming.LogicFramesPerSecond;
    private const long DefaultPlaceholderTicks = 3 * SimulationTiming.LogicFramesPerSecond;
    private const long RadarBlipLifetimeTicks = 8 * SimulationTiming.LogicFramesPerSecond;
    private const string CameraKey = "<camera>";
    private const int JournalCapacity = 4000;

    private enum Channel { Camera, Overlay, Audio, Radar, Input }

    private readonly MissionSession _session;
    private readonly Func<string, string> _resolveText;
    private readonly List<RadarBlip> _radar = [];
    private readonly SortedDictionary<string, long> _emitted = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long[]> _handled = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _problems = new(StringComparer.Ordinal);
    private readonly List<string> _journal = [];
    private readonly List<(string Key, long AtTick)> _completions = [];
    private readonly SortedDictionary<string, (string Label, bool IsTimer)> _counters = new(StringComparer.Ordinal);
    private readonly bool[] _consumers = new bool[5];

    private long _captionExpiresAtTick;
    private long _cinematicExpiresAtTick;
    private long _movieExpiresAtTick;
    private long _subtitleExpiresAtTick;
    private long _cameraCompletesAtTick;
    private bool _cameraPending;
    private bool _countersVisible = true;
    private bool _inputLocked;
    private long _lastObservedTick;

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
    }

    public string? CaptionText { get; private set; }
    public string? CinematicText { get; private set; }
    public string? SubtitleEvent { get; private set; }
    public string? MoviePlaceholder { get; private set; }
    public bool CameraMoveActive => _cameraPending;

    /// <summary><c>CAMERA_LETTERBOX_BEGIN</c>..<c>END</c>: the original hides the control bar and shows black bars (<c>ScriptActions::doLetterBoxMode</c>).</summary>
    public bool Letterbox { get; private set; }

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
    /// <see cref="CnCNew.Game.Hud.MatchController.BuildDrawList"/> uses, so a host's existing <c>UiRenderer</c> draws it: the fade, the letterbox bars, caption and
    /// cinematic text, the speech/movie placeholder, counters and countdown timers, the input-lock notice and the outcome banner. Radar blips are read from
    /// <see cref="RadarBlipsForDisplay"/>: turning a world position into a minimap pixel needs the host's own minimap transform.
    /// </summary>
    public ImmutableArray<UiDrawCommand> BuildOverlayCommands(int viewportWidth, int viewportHeight)
    {
        _consumers[(int)Channel.Overlay] = true;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var white = new WndColor(255, 255, 255, 255);
        var black = new WndColor(0, 0, 0, 255);
        var bar = Letterbox ? viewportHeight / 8 : 0;
        if (FadeAlpha() is { } fade && fade.Alpha > 0)
            commands.Add(new UiFillRect(new UiRect(0, 0, viewportWidth, viewportHeight), fade.White ? new WndColor(255, 255, 255, fade.Alpha) : new WndColor(0, 0, 0, fade.Alpha)));
        if (Letterbox)
        {
            commands.Add(new UiFillRect(new UiRect(0, 0, viewportWidth, bar), black));
            commands.Add(new UiFillRect(new UiRect(0, viewportHeight - bar, viewportWidth, bar), black));
        }
        if (CaptionText is { Length: > 0 } caption)
        {
            Centered(commands, caption, new UiFont("Arial", 18, true), bar + 24, viewportWidth, white);
        }
        if (CinematicText is { Length: > 0 } cinematic)
        {
            Centered(commands, cinematic, new UiFont("Arial", 16, true), viewportHeight - Math.Max(bar, 96) + 8, viewportWidth, white);
        }
        var placeholderName = MoviePlaceholder ?? SubtitleEvent;
        if (placeholderName is { Length: > 0 })
        {
            // No Bink decoder and no decoded dialogue/subtitle text in this build (docs/CAMPAIGN-AND-SAVEGAMES.md): the
            // raw event/movie name is shown, clearly marked as a placeholder rather than invented dialogue.
            var text = MoviePlaceholder is not null ? $"(movie placeholder: {placeholderName})" : $"(speech: {placeholderName})";
            // Inside the lower letterbox bar; without the letterbox above the control bar instead of on top of it.
            Centered(commands, text, new UiFont("Arial", 14, false), Letterbox ? Math.Max(0, viewportHeight - 48) : viewportHeight * 3 / 4, viewportWidth, white);
        }
        if (_countersVisible)
        {
            var y = bar + 64;
            foreach (var (name, display) in _counters)
            {
                var value = _session.Engine.CounterValue(name);
                // InGameUI::addNamedTimer shows a countdown as minutes:seconds of its remaining logic frames, a counter as its plain value.
                var shown = display.IsTimer ? string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, value) / SimulationTiming.LogicFramesPerSecond / 60}:{Math.Max(0, value) / SimulationTiming.LogicFramesPerSecond % 60:00}") : value.ToString(CultureInfo.InvariantCulture);
                var rect = new UiRect(viewportWidth - 420, y, 404, 24);
                commands.Add(new UiTextDraw($"{display.Label} {shown}", null, new UiFont("Arial", 14, true), white, black, rect.X, y, UiTextAlignment.Left, rect));
                y += 26;
            }
        }
        if (_inputLocked && OutcomeBanner is null)
        {
            Centered(commands, "(cinematic: the mission script has disabled input)", new UiFont("Arial", 12, false), Math.Max(4, bar - 28), viewportWidth, new WndColor(200, 200, 200, 255));
        }
        if (OutcomeBanner is { } banner)
        {
            var rect = new UiRect(0, viewportHeight / 3, viewportWidth, 48);
            commands.Add(new UiFillRect(new UiRect(0, rect.Y - 12, viewportWidth, 72), new WndColor(0, 0, 0, 150)));
            Centered(commands, banner, new UiFont("Arial", 28, true), rect.Y, viewportWidth, white);
        }
        return commands.ToImmutable();
    }

    /// <summary>
    /// The host's text measure (the renderer's glyph metrics): a draw command carries the final left edge of its text, so centring needs the real width. Without one the
    /// width is estimated from the font size.
    /// </summary>
    public IUiTextMeasure? TextMeasure { get; set; }

    /// <summary>Adds <paramref name="text"/> centred at <paramref name="y"/>, wrapped at word boundaries when it is wider than the screen (a mission objective is a long sentence).</summary>
    private void Centered(ImmutableArray<UiDrawCommand>.Builder commands, string text, UiFont font, int y, int viewportWidth, WndColor color)
    {
        int Width(string value) => TextMeasure?.MeasureWidth(value, font) ?? (int)(value.Length * font.Size * 0.6f);
        var lineHeight = TextMeasure?.LineHeight(font) ?? font.Size + 6;
        var available = Math.Max(64, viewportWidth - 64);
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Width(candidate) > available)
            {
                lines.Add(line);
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) lines.Add(line);
        foreach (var value in lines)
        {
            var clip = new UiRect(0, y, viewportWidth, lineHeight + 4);
            commands.Add(new UiTextDraw(value, null, font, color, new WndColor(0, 0, 0, 255), Math.Max(0, (viewportWidth - Width(value)) / 2), y, UiTextAlignment.Left, clip));
            y += lineHeight;
        }
    }

    private (bool White, byte Alpha)? FadeAlpha()
    {
        if (_fade is not { } fade) return null;
        var elapsed = _session.World.Tick - fade.Start;
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
        _lastObservedTick = _session.World.Tick;
        var tick = _lastObservedTick;
        if (CaptionText is not null && tick >= _captionExpiresAtTick) CaptionText = null;
        if (CinematicText is not null && tick >= _cinematicExpiresAtTick) CinematicText = null;
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
            if (_journal.Count < JournalCapacity && HowDisplayed(output.Action) != NoDisplay) _journal.Add($"{output.Tick} {output.Action}({string.Join(", ", output.Arguments)})");
            Apply(output, _lastObservedTick);
        }
        _radar.RemoveAll(blip => _session.World.Tick - blip.CreatedAtTick > RadarBlipLifetimeTicks);
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
                var seconds = Math.Max(0, (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond);
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
                else seconds = Math.Max(0, (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond);
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
                Letterbox = true;
                Handled(action, Channel.Overlay);
                return;
            case "CAMERA_LETTERBOX_END":
                Letterbox = false;
                Handled(action, Channel.Overlay);
                return;
            case "CAMERA_FADE_ADD" or "CAMERA_FADE_SUBTRACT" or "CAMERA_FADE_MULTIPLY":
            {
                // doCameraFade*(minFade, maxFade, framesIncrease, framesHold, framesDecrease). Add brightens towards white, subtract darkens towards black; multiply
                // darkens as its factor falls below 1, so it is shown as black with the inverted factor.
                if (!Number(output, 0, out var min) || !Number(output, 1, out var max) || !Number(output, 2, out var increase) || !Number(output, 3, out var hold) || !Number(output, 4, out var decrease))
                {
                    Problem(action, "fade arguments missing");
                    return;
                }
                if (action == "CAMERA_FADE_MULTIPLY") (min, max) = (1 - min, 1 - max);
                _fade = (action == "CAMERA_FADE_ADD", min, max, tick, (long)increase, (long)hold, (long)decrease);
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
                var played = Audio?.Invoke(action, name) == true;
                if (played) Handled(action, Channel.Audio);
                else Problem(action, Audio is null ? "no audio connected" : $"'{name}' did not start");
                if (action is "SPEECH_PLAY" or "SOUND_PLAY_NAMED")
                {
                    // No subtitle text exists for a speech event: its name is shown, marked as a placeholder, whether or not the sound plays.
                    if (action == "SPEECH_PLAY")
                    {
                        SubtitleEvent = name;
                        _subtitleExpiresAtTick = tick + DefaultPlaceholderTicks;
                        if (!played) Handled(action, Channel.Overlay);
                    }
                    _completions.Add((name, tick + DefaultPlaceholderTicks));
                }
                return;
            }
            case "MUSIC_SET_TRACK":
                if (output.Arguments.Length == 0) { Problem(action, "track name missing"); return; }
                if (Audio?.Invoke(action, output.Arguments[0]) == true) Handled(action, Channel.Audio);
                else Problem(action, Audio is null ? "no audio connected" : $"'{output.Arguments[0]}' did not start");
                _completions.Add((output.Arguments[0], tick));
                return;
            case "MOVIE_PLAY_FULLSCREEN" or "MOVIE_PLAY_RADAR":
                if (output.Arguments.Length == 0) { Problem(action, "movie name missing"); return; }
                MoviePlaceholder = output.Arguments[0];
                _movieExpiresAtTick = tick + DefaultPlaceholderTicks;
                _completions.Add((output.Arguments[0], tick + DefaultPlaceholderTicks));
                Handled(action, Channel.Overlay);
                return;
            case "DISPLAY_TEXT" or "INGAME_POPUP_MESSAGE":
                if (output.Arguments.Length == 0) { Problem(action, "text label missing"); return; }
                CaptionText = _resolveText(output.Arguments[0]);
                _captionExpiresAtTick = tick + DefaultTextTicks;
                Handled(action, Channel.Overlay);
                return;
            case "DISPLAY_CINEMATIC_TEXT":
            {
                // doDisplayCinematicText(displayText, fontType, timeInSeconds): the text of the lower letterbox bar.
                if (output.Arguments.Length == 0) { Problem(action, "text label missing"); return; }
                CinematicText = _resolveText(output.Arguments[0]);
                _cinematicExpiresAtTick = tick + (Number(output, 2, out var seconds) && seconds > 0 ? DurationTicks(seconds) : DefaultTextTicks);
                Handled(action, Channel.Overlay);
                return;
            }
            case "SHOW_MILITARY_CAPTION":
                if (output.Arguments.Length > 1 && long.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
                {
                    CaptionText = _resolveText(output.Arguments[0]);
                    _captionExpiresAtTick = tick + MillisecondsToTicks(durationMs);
                    Handled(action, Channel.Overlay);
                }
                else Problem(action, "caption arguments missing");
                return;
            case "DISPLAY_COUNTDOWN_TIMER" or "DISPLAY_COUNTER":
                // doDisplayCountdownTimer(timerName, timerText) / doDisplayCounter(counterName, counterText): a named line of the timer display.
                if (output.Arguments.Length < 2) { Problem(action, "arguments missing"); return; }
                _counters[output.Arguments[0]] = (_resolveText(output.Arguments[1]), action == "DISPLAY_COUNTDOWN_TIMER");
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
        }
    }

    // ------------------------------------------------------------------------------------------------ counters

    /// <summary>What the client does with an output type, for the report and <c>docs/SCRIPT-ENGINE.md</c> §7a.</summary>
    public static string HowDisplayed(string action) => action switch
    {
        "MOVE_CAMERA_TO" => "camera pans to the waypoint over the script's duration",
        "MOVE_CAMERA_ALONG_WAYPOINT_PATH" => "camera pans straight to the path's first waypoint (the path itself is not flown)",
        "RESET_CAMERA" => "camera pans to the waypoint and returns to default angle, pitch and zoom",
        "SETUP_CAMERA" => "camera cuts to the waypoint, looks at the second waypoint, takes zoom and pitch",
        "ROTATE_CAMERA" => "camera rotates in place over the script's duration",
        "ZOOM_CAMERA" or "CAMERA_MOD_SET_FINAL_ZOOM" => "camera height changes (clamped to the playable zoom range)",
        "PITCH_CAMERA" or "CAMERA_MOD_SET_FINAL_PITCH" => "camera pitch changes (clamped to the camera's pitch range)",
        "CAMERA_MOD_LOOK_TOWARD" or "CAMERA_MOD_FINAL_LOOK_TOWARD" or "CAMERA_LOOK_TOWARD_WAYPOINT" => "camera turns towards the waypoint",
        "CAMERA_FOLLOW_NAMED" or "CAMERA_STOP_FOLLOW" => "camera stays on the named object",
        "CAMERA_LETTERBOX_BEGIN" or "CAMERA_LETTERBOX_END" => "black bars, control bar hidden",
        "CAMERA_FADE_ADD" or "CAMERA_FADE_SUBTRACT" or "CAMERA_FADE_MULTIPLY" => "full-screen white/black fade",
        "DISABLE_INPUT" or "ENABLE_INPUT" => "the player's world input is dropped, with a notice on screen",
        "SPEECH_PLAY" => "audio event played; its name on screen as a placeholder subtitle",
        "SOUND_PLAY_NAMED" or "PLAY_SOUND_EFFECT" or "PLAY_SOUND_EFFECT_AT" => "audio event played (not positioned)",
        "MUSIC_SET_TRACK" => "music track started",
        "MOVIE_PLAY_FULLSCREEN" or "MOVIE_PLAY_RADAR" => "text placeholder with the movie name (no video decoder)",
        "DISPLAY_TEXT" or "INGAME_POPUP_MESSAGE" or "SHOW_MILITARY_CAPTION" => "caption text at the top",
        "DISPLAY_CINEMATIC_TEXT" => "text in the lower bar",
        "DISPLAY_COUNTDOWN_TIMER" or "DISPLAY_COUNTER" or "HIDE_COUNTDOWN_TIMER" or "HIDE_COUNTER" or "ENABLE_COUNTDOWN_TIMER_DISPLAY" or "DISABLE_COUNTDOWN_TIMER_DISPLAY" => "named counter/timer line at the top right",
        "RADAR_CREATE_EVENT" or "OBJECT_CREATE_RADAR_EVENT" or "TEAM_CREATE_RADAR_EVENT" => "blinking dot on the minimap",
        "VICTORY" or "DEFEAT" => "outcome banner",
        _ => NoDisplay,
    };

    private const string NoDisplay = "no display in this build";

    /// <summary>Per output type: emitted by the scripts, handed to a consumer, displayed (the consumer exists in this host).</summary>
    public ImmutableArray<MissionOutputCount> Counts()
    {
        var result = ImmutableArray.CreateBuilder<MissionOutputCount>();
        foreach (var (action, emitted) in _emitted)
        {
            long forwarded = 0, displayed = 0;
            if (_handled.TryGetValue(action, out var counts))
            {
                for (var channel = 0; channel < counts.Length; channel++)
                {
                    forwarded += counts[channel];
                    // Audio is only counted when a voice really started; the other channels need a host that asked for them.
                    if (channel == (int)Channel.Audio || _consumers[channel]) displayed += counts[channel];
                }
            }
            result.Add(new MissionOutputCount(action, emitted, forwarded, displayed, HowDisplayed(action)));
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
        lines.Add(string.Create(culture, $"mission outputs: {counts.Length} types, emitted {counts.Sum(count => count.Emitted)}, forwarded {counts.Sum(count => count.Forwarded)}, displayed {counts.Sum(count => count.Displayed)}, not displayed {counts.Sum(count => count.NotDisplayed)}"));
        foreach (var count in counts)
            lines.Add(string.Create(culture, $"mission output {count.Action}: emitted {count.Emitted}, forwarded {count.Forwarded}, displayed {count.Displayed}, not displayed {count.NotDisplayed} ({count.How})"));
        lines.Add($"mission output problems: {(_problems.Count == 0 ? "none" : string.Join("; ", _problems.Select(pair => string.Create(culture, $"{pair.Key} x{pair.Value}"))))}");
        lines.Add(string.Create(culture, $"mission presentation: input locked for {InputLockedTicks} ticks, locked at exit {_inputLocked}, letterbox at exit {Letterbox}, camera move pending at exit {_cameraPending}, outcome banner {OutcomeBanner ?? "none"}"));
        return lines.ToImmutable();
    }

    private static long DurationTicks(double seconds) => Math.Max(0, (long)Math.Round(seconds * SimulationTiming.LogicFramesPerSecond));
    private static long MillisecondsToTicks(long milliseconds) => Math.Max(0, (long)Math.Round(milliseconds / 1000.0 * SimulationTiming.LogicFramesPerSecond));

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
