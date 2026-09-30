using System.Collections.Immutable;
using CnCNew.Data.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>How the engine treats one condition or action type. Nothing is ever dropped: a type is executed, forwarded to the presentation layer, or counted as missing.</summary>
public enum ScriptSupport
{
    /// <summary>The type changes or reads engine, world or team state as the reference does (or as documented in <c>MAP-SCRIPTS.md</c> §8 where the simulation cannot model the reference exactly).</summary>
    Executed,

    /// <summary>A presentation/host concern (camera, sound, speech, movie, radar, UI text): the engine records it as a <see cref="ScriptOutput"/> for the client and does not touch the simulation.</summary>
    Forwarded,

    /// <summary>No implementation: executing it increments a per-type counter (<see cref="ScriptEngine.MissingActionHits"/>/<see cref="ScriptEngine.MissingConditionHits"/>); a missing condition evaluates to <see langword="false"/>.</summary>
    Missing,
}

/// <summary>
/// The support table of the script engine, one place for the engine's dispatch and for the audits. Type names are the ones of
/// <see cref="MapScriptCatalog"/> (<c>Scripts.h</c> enum order), so a typo here cannot compile silently: <see cref="Validate"/> runs in the tests.
/// </summary>
public static class ScriptCoverage
{
    public static readonly ImmutableHashSet<string> ExecutedConditions = ImmutableHashSet.Create(StringComparer.Ordinal,
        "CONDITION_FALSE", "CONDITION_TRUE", "COUNTER", "FLAG", "TIMER_EXPIRED",
        "PLAYER_ALL_DESTROYED", "PLAYER_ALL_BUILDFACILITIES_DESTROYED", "PLAYER_HAS_CREDITS", "PLAYER_HAS_N_OR_FEWER_BUILDINGS",
        "PLAYER_HAS_N_OR_FEWER_FACTION_BUILDINGS", "PLAYER_HAS_POWER", "PLAYER_HAS_NO_POWER", "PLAYER_HAS_OBJECT_COMPARISON",
        "PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA", "PLAYER_LOST_OBJECT_TYPE", "PLAYER_ACQUIRED_SCIENCE", "START_POSITION_IS", "MISSION_ATTEMPTS",
        "NAMED_DESTROYED", "NAMED_NOT_DESTROYED", "NAMED_DYING", "NAMED_TOTALLY_DEAD", "NAMED_CREATED", "NAMED_OWNED_BY_PLAYER", "UNIT_HEALTH",
        "NAMED_INSIDE_AREA", "NAMED_OUTSIDE_AREA", "NAMED_ENTERED_AREA", "NAMED_EXITED_AREA",
        "TEAM_HAS_UNITS", "TEAM_DESTROYED", "TEAM_CREATED", "TEAM_OWNED_BY_PLAYER", "TEAM_INSIDE_AREA_PARTIALLY", "TEAM_INSIDE_AREA_ENTIRELY",
        "TEAM_OUTSIDE_AREA_ENTIRELY", "TEAM_ENTERED_AREA_ENTIRELY", "TEAM_ENTERED_AREA_PARTIALLY", "TEAM_EXITED_AREA_ENTIRELY", "TEAM_EXITED_AREA_PARTIALLY",
        "MULTIPLAYER_ALLIED_VICTORY", "MULTIPLAYER_ALLIED_DEFEAT", "MULTIPLAYER_PLAYER_DEFEAT",
        "HAS_FINISHED_SPEECH", "HAS_FINISHED_AUDIO", "HAS_FINISHED_VIDEO", "MUSIC_TRACK_HAS_COMPLETED", "CAMERA_MOVEMENT_FINISHED", "NAMED_SELECTED",
        "NAMED_ATTACKED_BY_PLAYER", "TEAM_ATTACKED_BY_PLAYER", "NAMED_ATTACKED_BY_OBJECTTYPE", "TEAM_ATTACKED_BY_OBJECTTYPE", "PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA",
        "BUILT_BY_PLAYER", "UNIT_HAS_OBJECT_STATUS", "TEAM_ALL_HAS_OBJECT_STATUS", "TEAM_SOME_HAVE_OBJECT_STATUS", "NAMED_REACHED_WAYPOINTS_END", "TEAM_REACHED_WAYPOINTS_END", "SKIRMISH_PLAYER_FACTION", "BUILDING_ENTERED_BY_PLAYER", "PLAYER_DESTROYED_N_BUILDINGS_PLAYER");

    public static readonly ImmutableHashSet<string> ExecutedActions = ImmutableHashSet.Create(StringComparer.Ordinal,
        "NO_OP", "SET_FLAG", "SET_COUNTER", "INCREMENT_COUNTER", "DECREMENT_COUNTER", "SET_TIMER", "SET_MILLISECOND_TIMER", "SET_RANDOM_TIMER",
        "SET_RANDOM_MSEC_TIMER", "STOP_TIMER", "RESTART_TIMER", "ADD_TO_MSEC_TIMER", "SUB_FROM_MSEC_TIMER",
        "ENABLE_SCRIPT", "DISABLE_SCRIPT", "CALL_SUBROUTINE", "OBJECTLIST_ADDOBJECTTYPE", "OBJECTLIST_REMOVEOBJECTTYPE",
        "VICTORY", "DEFEAT", "QUICKVICTORY", "PLAYER_SET_MONEY", "PLAYER_GIVE_MONEY", "PLAYER_RELATES_PLAYER", "PLAYER_GRANT_SCIENCE",
        "PLAYER_KILL", "NAMED_KILL", "TEAM_KILL", "NAMED_DELETE", "TEAM_DELETE", "TEAM_DELETE_LIVING", "NAMED_DAMAGE", "DAMAGE_MEMBERS_OF_TEAM",
        "CREATE_OBJECT", "CREATE_NAMED_ON_TEAM_AT_WAYPOINT", "CREATE_UNNAMED_ON_TEAM_AT_WAYPOINT", "UNIT_SPAWN_NAMED_LOCATION_ORIENTATION",
        "PLAYER_TRANSFER_OWNERSHIP_PLAYER", "NAMED_TRANSFER_OWNERSHIP_PLAYER", "TEAM_TRANSFER_TO_PLAYER",
        "MOVE_TEAM_TO", "MOVE_NAMED_UNIT_TO", "TEAM_FOLLOW_WAYPOINTS", "TEAM_FOLLOW_WAYPOINTS_EXACT", "NAMED_FOLLOW_WAYPOINTS", "NAMED_FOLLOW_WAYPOINTS_EXACT",
        "NAMED_STOP", "TEAM_STOP", "TEAM_STOP_AND_DISBAND", "NAMED_GUARD", "TEAM_GUARD", "TEAM_GUARD_POSITION", "TEAM_GUARD_OBJECT", "TEAM_GUARD_AREA",
        "CREATE_REINFORCEMENT_TEAM", "SET_ATTACK_PRIORITY_THING", "SET_ATTACK_PRIORITY_KIND_OF", "SET_DEFAULT_ATTACK_PRIORITY", "NAMED_APPLY_ATTACK_PRIORITY_SET", "TEAM_APPLY_ATTACK_PRIORITY_SET",
        "NAMED_HUNT", "TEAM_HUNT", "PLAYER_HUNT", "TEAM_ATTACK_TEAM", "TEAM_ATTACK_NAMED", "TEAM_ATTACK_AREA", "NAMED_ATTACK_NAMED", "NAMED_ATTACK_TEAM", "NAMED_ATTACK_AREA");

    public static readonly ImmutableHashSet<string> ForwardedActions = ImmutableHashSet.Create(StringComparer.Ordinal,
        "DEBUG_MESSAGE_BOX", "DEBUG_STRING", "PLAY_SOUND_EFFECT", "PLAY_SOUND_EFFECT_AT", "SOUND_PLAY_NAMED", "SPEECH_PLAY", "MOVIE_PLAY_FULLSCREEN", "MOVIE_PLAY_RADAR",
        "MUSIC_SET_TRACK", "MUSIC_SET_VOLUME", "SUSPEND_BACKGROUND_SOUNDS", "RESUME_BACKGROUND_SOUNDS", "SOUND_AMBIENT_PAUSE", "SOUND_AMBIENT_RESUME",
        "SOUND_DISABLE_TYPE", "SOUND_ENABLE_TYPE", "SOUND_ENABLE_ALL", "AUDIO_OVERRIDE_VOLUME_TYPE", "AUDIO_RESTORE_VOLUME_TYPE", "AUDIO_RESTORE_VOLUME_ALL_TYPE",
        "SOUND_SET_VOLUME", "SPEECH_SET_VOLUME", "SOUND_REMOVE_ALL_DISABLED", "SOUND_REMOVE_TYPE", "ENABLE_OBJECT_SOUND", "DISABLE_OBJECT_SOUND", "EVA_SET_ENABLED_DISABLED",
        "MOVE_CAMERA_TO", "MOVE_CAMERA_ALONG_WAYPOINT_PATH", "ROTATE_CAMERA", "RESET_CAMERA", "MOVE_CAMERA_TO_SELECTION", "CAMERA_MOVE_HOME", "SETUP_CAMERA",
        "ZOOM_CAMERA", "PITCH_CAMERA", "CAMERA_FOLLOW_NAMED", "CAMERA_STOP_FOLLOW", "CAMERA_TETHER_NAMED", "CAMERA_STOP_TETHER_NAMED", "CAMERA_SET_DEFAULT",
        "CAMERA_LOOK_TOWARD_OBJECT", "CAMERA_LOOK_TOWARD_WAYPOINT", "CAMERA_SET_AUDIBLE_DISTANCE", "CAMERA_ENABLE_SLAVE_MODE", "CAMERA_DISABLE_SLAVE_MODE",
        "CAMERA_ADD_SHAKER_AT", "SCREEN_SHAKE", "CAMERA_MOD_FREEZE_TIME", "CAMERA_MOD_SET_FINAL_ZOOM", "CAMERA_MOD_SET_FINAL_PITCH", "CAMERA_MOD_FREEZE_ANGLE",
        "CAMERA_MOD_SET_FINAL_SPEED_MULTIPLIER", "CAMERA_MOD_SET_ROLLING_AVERAGE", "CAMERA_MOD_FINAL_LOOK_TOWARD", "CAMERA_MOD_LOOK_TOWARD",
        "CAMERA_LETTERBOX_BEGIN", "CAMERA_LETTERBOX_END", "CAMERA_FADE_ADD", "CAMERA_FADE_SUBTRACT", "CAMERA_FADE_SATURATE", "CAMERA_FADE_MULTIPLY",
        "CAMERA_BW_MODE_BEGIN", "CAMERA_BW_MODE_END", "CAMERA_MOTION_BLUR", "CAMERA_MOTION_BLUR_JUMP", "CAMERA_MOTION_BLUR_FOLLOW", "CAMERA_MOTION_BLUR_END_FOLLOW",
        "SET_VISUAL_SPEED_MULTIPLIER", "FREEZE_TIME", "UNFREEZE_TIME", "DRAW_SKYBOX_BEGIN", "DRAW_SKYBOX_END", "OVERSIZE_TERRAIN", "SET_TREE_SWAY", "SHOW_WEATHER",
        "SET_INFANTRY_LIGHTING_OVERRIDE", "RESET_INFANTRY_LIGHTING_OVERRIDE", "RESIZE_VIEW_GUARDBAND", "WATER_CHANGE_HEIGHT", "WATER_CHANGE_HEIGHT_OVER_TIME",
        "MAP_SWITCH_BORDER", "DISABLE_BORDER_SHROUD", "ENABLE_BORDER_SHROUD", "SET_FPS_LIMIT", "OPTIONS_SET_OCCLUSION_MODE", "OPTIONS_SET_DRAWICON_UI_MODE",
        "OPTIONS_SET_PARTICLE_CAP_MODE", "DISPLAY_TEXT", "DISPLAY_CINEMATIC_TEXT", "SHOW_MILITARY_CAPTION", "INGAME_POPUP_MESSAGE",
        "DISPLAY_COUNTDOWN_TIMER", "HIDE_COUNTDOWN_TIMER", "ENABLE_COUNTDOWN_TIMER_DISPLAY", "DISABLE_COUNTDOWN_TIMER_DISPLAY", "DISPLAY_COUNTER", "HIDE_COUNTER",
        "DISABLE_SPECIAL_POWER_DISPLAY", "ENABLE_SPECIAL_POWER_DISPLAY", "NAMED_HIDE_SPECIAL_POWER_DISPLAY", "NAMED_SHOW_SPECIAL_POWER_DISPLAY",
        "CAMEO_FLASH", "NAMED_FLASH", "TEAM_FLASH", "NAMED_FLASH_WHITE", "TEAM_FLASH_WHITE", "NAMED_CUSTOM_COLOR", "NAMED_SET_EMOTICON", "TEAM_SET_EMOTICON",
        "RADAR_CREATE_EVENT", "RADAR_DISABLE", "RADAR_ENABLE", "RADAR_FORCE_ENABLE", "RADAR_REVERT_TO_NORMAL", "REFRESH_RADAR", "OBJECT_CREATE_RADAR_EVENT", "TEAM_CREATE_RADAR_EVENT",
        "DISABLE_INPUT", "ENABLE_INPUT", "OBJECT_FORCE_SELECT", "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE", "COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT",
        "PLAYER_SCIENCE_AVAILABILITY", "UNIT_AFFECT_OBJECT_PANEL_FLAGS", "TEAM_AFFECT_OBJECT_PANEL_FLAGS", "ENABLE_SCORING", "DISABLE_SCORING", "PLAYER_EXCLUDE_FROM_SCORE_SCREEN");

    public static ScriptSupport ConditionSupport(string typeName) => ExecutedConditions.Contains(typeName) ? ScriptSupport.Executed : ScriptSupport.Missing;

    public static ScriptSupport ActionSupport(string typeName)
        => ExecutedActions.Contains(typeName) ? ScriptSupport.Executed : ForwardedActions.Contains(typeName) ? ScriptSupport.Forwarded : ScriptSupport.Missing;

    /// <summary>Every name in the support tables must be a real type of <see cref="MapScriptCatalog"/>; returns the ones that are not (empty when consistent).</summary>
    public static ImmutableArray<string> Validate()
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        var conditions = MapScriptCatalog.ConditionTypeNames.ToHashSet(StringComparer.Ordinal);
        var actions = MapScriptCatalog.ScriptActionTypeNames.ToHashSet(StringComparer.Ordinal);
        foreach (var name in ExecutedConditions) if (!conditions.Contains(name)) problems.Add($"condition {name}");
        foreach (var name in ExecutedActions) if (!actions.Contains(name)) problems.Add($"action {name}");
        foreach (var name in ForwardedActions)
        {
            if (!actions.Contains(name)) problems.Add($"forwarded action {name}");
            if (ExecutedActions.Contains(name)) problems.Add($"action {name} is both executed and forwarded");
        }
        return problems.ToImmutable();
    }
}
