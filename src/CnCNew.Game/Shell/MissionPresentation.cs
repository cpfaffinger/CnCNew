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

/// <summary>
/// Drains a <see cref="MissionSession"/>'s <see cref="ScriptEngine.DrainOutputs"/> every tick and turns the presentation
/// actions a client is responsible for (<c>docs/HANDOFF-PHASE10.md</c> item 6) into renderer-agnostic state: the active
/// on-screen caption/subtitle text, a movie placeholder name (no Bink decoder in this build, see
/// <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>), whether a scripted camera move is still playing, and recent radar blips.
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

    private readonly MissionSession _session;
    private readonly Func<string, string> _resolveText;
    private readonly List<RadarBlip> _radar = [];

    private long _captionExpiresAtTick;
    private long _movieExpiresAtTick;
    private long _subtitleExpiresAtTick;
    private long _cameraCompletesAtTick;
    private bool _cameraPending;

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
    public string? SubtitleEvent { get; private set; }
    public string? MoviePlaceholder { get; private set; }
    public bool CameraMoveActive => _cameraPending;

    /// <summary>The camera move's destination in world units, when it names a waypoint the map actually has
    /// (<c>MOVE_CAMERA_TO</c>/<c>MOVE_CAMERA_ALONG_WAYPOINT_PATH</c>/<c>RESET_CAMERA</c>); <see langword="null"/> while no
    /// move is active, or for a move kind with no fixed target (<c>ROTATE_CAMERA</c>, <c>CAMERA_MOVE_HOME</c>,
    /// <c>MOVE_CAMERA_TO_SELECTION</c>) - a host renders those as a cut/rotation in place instead of a pan.</summary>
    public (float X, float Y)? CameraTarget { get; private set; }
    public ImmutableArray<RadarBlip> RadarBlips => [.._radar];

    private (float X, float Y)? _lastAppliedCameraTarget;

    /// <summary>
    /// Pans <paramref name="camera"/> to <see cref="CameraTarget"/> once, the first call after a new target appears (never
    /// repeatedly every frame, which would fight the camera's own pan/zoom easing and the player's input once a cutscene
    /// hands control back). <see cref="RtsCamera"/> is a <c>CnCNew.Presentation</c> type, not a Stride one, so this runs
    /// headless in tests; a rotation-only move (<c>ROTATE_CAMERA</c>) or one with no resolvable waypoint is not applied
    /// here (no target position to pan to) - a host that wants the rotation itself reads <see cref="CameraMoveActive"/>.
    /// </summary>
    public void ApplyCameraTo(RtsCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (CameraTarget is not { } target || target == _lastAppliedCameraTarget) return;
        camera.CenterOn(new Vector2(target.X, target.Y));
        _lastAppliedCameraTarget = target;
    }

    /// <summary>
    /// On-screen text for the current caption/subtitle/movie placeholder, as <see cref="UiDrawCommand"/>s - the same
    /// renderer-neutral vocabulary <see cref="CnCNew.Game.Hud.MatchController.BuildDrawList"/> uses for its own dynamic HUD
    /// overlays, so a host's existing <c>UiRenderer</c> (<c>CnCNew.Client.Stride</c>) draws these for free; no new
    /// renderer is needed just to show this text. Radar blips are read directly from <see cref="RadarBlips"/>: turning a
    /// world position into a minimap pixel needs the host's own <c>MinimapImage</c>/camera transform, which this
    /// Stride-free layer does not own.
    /// </summary>
    public ImmutableArray<UiDrawCommand> BuildOverlayCommands(int viewportWidth, int viewportHeight)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        var white = new WndColor(255, 255, 255, 255);
        var black = new WndColor(0, 0, 0, 255);
        if (CaptionText is { Length: > 0 } caption)
        {
            var rect = new UiRect(0, 24, viewportWidth, 32);
            commands.Add(new UiTextDraw(caption, null, new UiFont("Arial", 18, true), white, black, viewportWidth / 2, 24, UiTextAlignment.Center, rect));
        }
        var placeholderName = MoviePlaceholder ?? SubtitleEvent;
        if (placeholderName is { Length: > 0 })
        {
            // No Bink decoder and no decoded dialogue/subtitle text in this build (docs/CAMPAIGN-AND-SAVEGAMES.md): the
            // raw event/movie name is shown, clearly marked as a placeholder rather than invented dialogue.
            var text = MoviePlaceholder is not null ? $"(movie placeholder: {placeholderName})" : $"(speech: {placeholderName})";
            var rect = new UiRect(0, Math.Max(0, viewportHeight - 48), viewportWidth, 24);
            commands.Add(new UiTextDraw(text, null, new UiFont("Arial", 14, false), white, black, viewportWidth / 2, rect.Y, UiTextAlignment.Center, rect));
        }
        return commands.ToImmutable();
    }

    private long _lastObservedTick;

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
        if (MoviePlaceholder is not null && tick >= _movieExpiresAtTick) MoviePlaceholder = null;
        if (SubtitleEvent is not null && tick >= _subtitleExpiresAtTick)
        {
            _session.Engine.CompletePresentation(SubtitleEvent);
            SubtitleEvent = null;
        }
        if (_cameraPending && tick >= _cameraCompletesAtTick)
        {
            _session.Engine.CompletePresentation(CameraKey);
            _cameraPending = false;
            CameraTarget = null;
        }
    }

    /// <summary>Call once per tick, right after <see cref="MissionSession.Step"/>: drains and applies the outputs that step
    /// produced (its durations are measured from the tick <see cref="BeforeStep"/> observed, which is the tick
    /// <see cref="ScriptEngine.Update"/> actually ran with - not <see cref="MissionSession.World"/>'s tick now, which has
    /// already moved on).</summary>
    public void AfterStep()
    {
        foreach (var output in _session.Engine.DrainOutputs()) Apply(output, _lastObservedTick);
        _radar.RemoveAll(blip => _session.World.Tick - blip.CreatedAtTick > RadarBlipLifetimeTicks);
    }

    private void Apply(ScriptOutput output, long tick)
    {
        switch (output.Action)
        {
            case "MOVE_CAMERA_TO" or "MOVE_CAMERA_ALONG_WAYPOINT_PATH" or "RESET_CAMERA":
                // doMoveCameraTo/doMoveCameraAlongWaypointPath/doResetCamera (ScriptActions.cpp): parameter 0 is the
                // waypoint name, 1 is the move's duration in seconds.
                StartCameraMove(output, tick, secondsIndex: 1, waypointIndex: 0);
                return;
            case "ROTATE_CAMERA":
                // doRotateCamera(rotations, sec, easeIn, easeOut): no waypoint, a rotation in place.
                StartCameraMove(output, tick, secondsIndex: 1, waypointIndex: null);
                return;
            case "CAMERA_MOVE_HOME" or "MOVE_CAMERA_TO_SELECTION":
                // Neither retail action takes a duration (an instant cut); complete on the very next tick's BeforeStep().
                CameraTarget = null;
                _cameraPending = true;
                _cameraCompletesAtTick = tick;
                return;
            case "SPEECH_PLAY" or "SOUND_PLAY_NAMED":
                if (output.Arguments.Length > 0) { SubtitleEvent = output.Arguments[0]; _subtitleExpiresAtTick = tick + DefaultPlaceholderTicks; }
                return;
            case "MOVIE_PLAY_FULLSCREEN" or "MOVIE_PLAY_RADAR":
                if (output.Arguments.Length > 0)
                {
                    MoviePlaceholder = output.Arguments[0];
                    _movieExpiresAtTick = tick + DefaultPlaceholderTicks;
                    SubtitleEvent = output.Arguments[0];
                    _subtitleExpiresAtTick = tick + DefaultPlaceholderTicks;
                }
                return;
            case "DISPLAY_TEXT" or "DISPLAY_CINEMATIC_TEXT" or "INGAME_POPUP_MESSAGE":
                if (output.Arguments.Length > 0) { CaptionText = _resolveText(output.Arguments[0]); _captionExpiresAtTick = tick + DefaultTextTicks; }
                return;
            case "SHOW_MILITARY_CAPTION":
                if (output.Arguments.Length > 1 && long.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs))
                {
                    CaptionText = _resolveText(output.Arguments[0]);
                    _captionExpiresAtTick = tick + MillisecondsToTicks(durationMs);
                }
                return;
            case "RADAR_CREATE_EVENT":
                if (output.Arguments.Length > 1 && TryParseCoord(output.Arguments[0], out var x, out var y) && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var eventType))
                    _radar.Add(new RadarBlip(x, y, eventType, tick));
                return;
            case "OBJECT_CREATE_RADAR_EVENT":
                if (output.Arguments.Length > 1 && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectEventType)
                    && TryNamedPosition(output.Arguments[0], out var ox, out var oy))
                    _radar.Add(new RadarBlip(ox, oy, objectEventType, tick));
                return;
            case "TEAM_CREATE_RADAR_EVENT":
                if (output.Arguments.Length > 1 && int.TryParse(output.Arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamEventType)
                    && TryTeamPosition(output.Arguments[0], out var tx, out var ty))
                    _radar.Add(new RadarBlip(tx, ty, teamEventType, tick));
                return;
        }
    }

    private void StartCameraMove(ScriptOutput output, long tick, int secondsIndex, int? waypointIndex)
    {
        var seconds = output.Arguments.Length > secondsIndex
            && double.TryParse(output.Arguments[secondsIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        CameraTarget = waypointIndex is { } index && output.Arguments.Length > index && _session.Program.FindWaypoint(output.Arguments[index]) is { } waypoint
            ? ((float)waypoint.X.ToDecimal(), (float)waypoint.Y.ToDecimal())
            : null;
        _cameraPending = true;
        _cameraCompletesAtTick = tick + DurationTicks(seconds);
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
