using System.Collections.Frozen;

namespace CnCNew.Presentation.Input;

/// <summary>
/// The keyboard-bindable actions: every retail name of <c>GameMessageMetaTypeNames</c> (<c>MetaEvent.cpp</c>; the debug and cheat names are left
/// out, a <c>CommandMap.ini</c> entry naming one is reported as unknown) plus the engine actions after <see cref="CameraScrollUp"/>, which the
/// original handles outside the meta map: <c>LookAtXlat.cpp</c> scrolls on the raw arrow keys, attack move and guard are control-bar buttons.
/// </summary>
public enum MetaAction
{
    SaveView1, SaveView2, SaveView3, SaveView4, SaveView5, SaveView6, SaveView7, SaveView8,
    ViewView1, ViewView2, ViewView3, ViewView4, ViewView5, ViewView6, ViewView7, ViewView8,
    CreateTeam0, CreateTeam1, CreateTeam2, CreateTeam3, CreateTeam4, CreateTeam5, CreateTeam6, CreateTeam7, CreateTeam8, CreateTeam9,
    SelectTeam0, SelectTeam1, SelectTeam2, SelectTeam3, SelectTeam4, SelectTeam5, SelectTeam6, SelectTeam7, SelectTeam8, SelectTeam9,
    AddTeam0, AddTeam1, AddTeam2, AddTeam3, AddTeam4, AddTeam5, AddTeam6, AddTeam7, AddTeam8, AddTeam9,
    ViewTeam0, ViewTeam1, ViewTeam2, ViewTeam3, ViewTeam4, ViewTeam5, ViewTeam6, ViewTeam7, ViewTeam8, ViewTeam9,
    SelectMatchingUnits, SelectNextUnit, SelectPrevUnit, SelectNextWorker, SelectPrevWorker, SelectNextIdleWorker, SelectHero,
    SelectAll, SelectAllAircraft, ViewCommandCenter, ViewLastRadarEvent,
    Scatter, Stop, Deploy, CreateFormation, Follow,
    ChatPlayers, ChatAllies, ChatEveryone, Diplomacy, PlaceBeacon, DeleteBeacon, Options,
    IncreaseMaxRenderFps, DecreaseMaxRenderFps, IncreaseLogicTimeScale, DecreaseLogicTimeScale,
    ToggleLowerDetails, ToggleControlBar, TogglePlayerObserver,
    BeginPathBuild, EndPathBuild, BeginForceAttack, EndForceAttack, BeginForceMove, EndForceMove,
    BeginWaypoints, EndWaypoints, BeginPreferSelection, EndPreferSelection,
    TakeScreenshot, TakeScreenshotPng, AllCheer,
    BeginCameraRotateLeft, EndCameraRotateLeft, BeginCameraRotateRight, EndCameraRotateRight,
    BeginCameraZoomIn, EndCameraZoomIn, BeginCameraZoomOut, EndCameraZoomOut, CameraReset,
    ToggleCameraTrackingDrawable, ToggleFastForwardReplay, TogglePause, TogglePauseAlt, StepFrame, StepFrameAlt, DemoInstantQuit,

    CameraScrollUp, CameraScrollDown, CameraScrollLeft, CameraScrollRight,
    AttackMove, Guard,
}

public static class MetaActionNames
{
    private static readonly (string Name, MetaAction Action)[] Table = BuildTable();
    private static readonly FrozenDictionary<string, MetaAction> ByName = Table.ToFrozenDictionary(pair => pair.Name, pair => pair.Action, StringComparer.OrdinalIgnoreCase);
    private static readonly FrozenDictionary<MetaAction, string> ByAction = Table.ToFrozenDictionary(pair => pair.Action, pair => pair.Name);

    /// <summary>The first engine action: these are not <c>CommandMap.ini</c> names in the retail data.</summary>
    public static bool IsEngineAction(MetaAction action) => action >= MetaAction.CameraScrollUp;

    public static bool TryParse(string name, out MetaAction action) => ByName.TryGetValue(name.Trim(), out action);

    public static string NameOf(MetaAction action) => ByAction[action];

    public static bool IsTeamAction(MetaAction action, out TeamActionKind kind, out int group)
    {
        (kind, group) = action switch
        {
            >= MetaAction.CreateTeam0 and <= MetaAction.CreateTeam9 => (TeamActionKind.Create, action - MetaAction.CreateTeam0),
            >= MetaAction.SelectTeam0 and <= MetaAction.SelectTeam9 => (TeamActionKind.Select, action - MetaAction.SelectTeam0),
            >= MetaAction.AddTeam0 and <= MetaAction.AddTeam9 => (TeamActionKind.Add, action - MetaAction.AddTeam0),
            >= MetaAction.ViewTeam0 and <= MetaAction.ViewTeam9 => (TeamActionKind.View, action - MetaAction.ViewTeam0),
            _ => (TeamActionKind.Create, -1),
        };
        return group >= 0;
    }

    private static (string, MetaAction)[] BuildTable()
    {
        var table = new List<(string, MetaAction)>();
        for (var index = 1; index <= 8; index++)
        {
            table.Add(($"SAVE_VIEW{index}", MetaAction.SaveView1 + index - 1));
            table.Add(($"VIEW_VIEW{index}", MetaAction.ViewView1 + index - 1));
        }
        for (var index = 0; index <= 9; index++)
        {
            table.Add(($"CREATE_TEAM{index}", MetaAction.CreateTeam0 + index));
            table.Add(($"SELECT_TEAM{index}", MetaAction.SelectTeam0 + index));
            table.Add(($"ADD_TEAM{index}", MetaAction.AddTeam0 + index));
            table.Add(($"VIEW_TEAM{index}", MetaAction.ViewTeam0 + index));
        }
        table.AddRange([
            ("SELECT_MATCHING_UNITS", MetaAction.SelectMatchingUnits), ("SELECT_NEXT_UNIT", MetaAction.SelectNextUnit), ("SELECT_PREV_UNIT", MetaAction.SelectPrevUnit),
            ("SELECT_NEXT_WORKER", MetaAction.SelectNextWorker), ("SELECT_PREV_WORKER", MetaAction.SelectPrevWorker), ("SELECT_NEXT_IDLE_WORKER", MetaAction.SelectNextIdleWorker),
            ("SELECT_HERO", MetaAction.SelectHero), ("SELECT_ALL", MetaAction.SelectAll), ("SELECT_ALL_AIRCRAFT", MetaAction.SelectAllAircraft),
            ("VIEW_COMMAND_CENTER", MetaAction.ViewCommandCenter), ("VIEW_LAST_RADAR_EVENT", MetaAction.ViewLastRadarEvent),
            ("SCATTER", MetaAction.Scatter), ("STOP", MetaAction.Stop), ("DEPLOY", MetaAction.Deploy), ("CREATE_FORMATION", MetaAction.CreateFormation), ("FOLLOW", MetaAction.Follow),
            ("CHAT_PLAYERS", MetaAction.ChatPlayers), ("CHAT_ALLIES", MetaAction.ChatAllies), ("CHAT_EVERYONE", MetaAction.ChatEveryone), ("DIPLOMACY", MetaAction.Diplomacy),
            ("PLACE_BEACON", MetaAction.PlaceBeacon), ("DELETE_BEACON", MetaAction.DeleteBeacon), ("OPTIONS", MetaAction.Options),
            ("INCREASE_MAX_RENDER_FPS", MetaAction.IncreaseMaxRenderFps), ("DECREASE_MAX_RENDER_FPS", MetaAction.DecreaseMaxRenderFps),
            ("INCREASE_LOGIC_TIME_SCALE", MetaAction.IncreaseLogicTimeScale), ("DECREASE_LOGIC_TIME_SCALE", MetaAction.DecreaseLogicTimeScale),
            ("TOGGLE_LOWER_DETAILS", MetaAction.ToggleLowerDetails), ("TOGGLE_CONTROL_BAR", MetaAction.ToggleControlBar), ("TOGGLE_PLAYER_OBSERVER", MetaAction.TogglePlayerObserver),
            ("BEGIN_PATH_BUILD", MetaAction.BeginPathBuild), ("END_PATH_BUILD", MetaAction.EndPathBuild),
            ("BEGIN_FORCEATTACK", MetaAction.BeginForceAttack), ("END_FORCEATTACK", MetaAction.EndForceAttack),
            ("BEGIN_FORCEMOVE", MetaAction.BeginForceMove), ("END_FORCEMOVE", MetaAction.EndForceMove),
            ("BEGIN_WAYPOINTS", MetaAction.BeginWaypoints), ("END_WAYPOINTS", MetaAction.EndWaypoints),
            ("BEGIN_PREFER_SELECTION", MetaAction.BeginPreferSelection), ("END_PREFER_SELECTION", MetaAction.EndPreferSelection),
            ("TAKE_SCREENSHOT", MetaAction.TakeScreenshot), ("TAKE_SCREENSHOT_PNG", MetaAction.TakeScreenshotPng), ("ALL_CHEER", MetaAction.AllCheer),
            ("BEGIN_CAMERA_ROTATE_LEFT", MetaAction.BeginCameraRotateLeft), ("END_CAMERA_ROTATE_LEFT", MetaAction.EndCameraRotateLeft),
            ("BEGIN_CAMERA_ROTATE_RIGHT", MetaAction.BeginCameraRotateRight), ("END_CAMERA_ROTATE_RIGHT", MetaAction.EndCameraRotateRight),
            ("BEGIN_CAMERA_ZOOM_IN", MetaAction.BeginCameraZoomIn), ("END_CAMERA_ZOOM_IN", MetaAction.EndCameraZoomIn),
            ("BEGIN_CAMERA_ZOOM_OUT", MetaAction.BeginCameraZoomOut), ("END_CAMERA_ZOOM_OUT", MetaAction.EndCameraZoomOut),
            ("CAMERA_RESET", MetaAction.CameraReset), ("TOGGLE_CAMERA_TRACKING_DRAWABLE", MetaAction.ToggleCameraTrackingDrawable),
            ("TOGGLE_FAST_FORWARD_REPLAY", MetaAction.ToggleFastForwardReplay), ("TOGGLE_PAUSE", MetaAction.TogglePause), ("TOGGLE_PAUSE_ALT", MetaAction.TogglePauseAlt),
            ("STEP_FRAME", MetaAction.StepFrame), ("STEP_FRAME_ALT", MetaAction.StepFrameAlt), ("DEMO_INSTANT_QUIT", MetaAction.DemoInstantQuit),
            ("ENGINE_CAMERA_SCROLL_UP", MetaAction.CameraScrollUp), ("ENGINE_CAMERA_SCROLL_DOWN", MetaAction.CameraScrollDown),
            ("ENGINE_CAMERA_SCROLL_LEFT", MetaAction.CameraScrollLeft), ("ENGINE_CAMERA_SCROLL_RIGHT", MetaAction.CameraScrollRight),
            ("ENGINE_ATTACK_MOVE", MetaAction.AttackMove), ("ENGINE_GUARD", MetaAction.Guard),
        ]);
        var missing = Enum.GetValues<MetaAction>().Except(table.Select(pair => pair.Item2)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException($"MetaAction without INI name: {string.Join(", ", missing)}");
        return [..table];
    }
}

public enum TeamActionKind
{
    Create,
    Select,
    Add,
    View,
}
