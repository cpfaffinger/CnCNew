using System.Collections.Immutable;

namespace CnCNew.Game.Shell;

/// <summary>What this client does with one <c>ScriptOutput</c> type (<c>docs/SCRIPT-ENGINE.md</c> §7a).</summary>
public enum MissionOutputState
{
    /// <summary>A consumer in this client acts on it where the player sees, hears or is limited by it; the counter shows "displayed" only when the consumer really acted.</summary>
    Displayed,

    /// <summary>No consumer shows it, for the reason in the description (a render or online option, a screen the client does not have, an effect nothing in this build can draw).</summary>
    NotDisplayed,

    /// <summary>Not the host's job: the simulation carries it out itself (the output only tells the host it happened).</summary>
    HandledElsewhere,
}

/// <param name="Description">What the client does (<see cref="MissionOutputState.Displayed"/>) or why it does not.</param>
public sealed record MissionOutputInfo(string Action, MissionOutputState State, string Description);

/// <summary>
/// The per-type table behind the director's counters and the report: one entry for every presentation output the script engine forwards (<c>ScriptCoverage.ForwardedActions</c>) plus
/// <c>VICTORY</c>/<c>DEFEAT</c>. A type missing from the table is reported as not displayed, never silently as shown (a test keeps the table complete).
/// </summary>
public static class MissionOutputCatalog
{
    private static readonly ImmutableDictionary<string, MissionOutputInfo> Table = Build();

    public static MissionOutputInfo Describe(string action)
        => Table.TryGetValue(action, out var info) ? info : new MissionOutputInfo(action, MissionOutputState.NotDisplayed, "no display in this build (not in the output table)");

    public static ImmutableArray<MissionOutputInfo> All => [..Table.Values.OrderBy(info => info.Action, StringComparer.Ordinal)];

    private static ImmutableDictionary<string, MissionOutputInfo> Build()
    {
        var table = ImmutableDictionary.CreateBuilder<string, MissionOutputInfo>(StringComparer.Ordinal);
        void Shown(string description, params string[] actions)
        {
            foreach (var action in actions) table[action] = new MissionOutputInfo(action, MissionOutputState.Displayed, description);
        }
        void Not(string reason, params string[] actions)
        {
            foreach (var action in actions) table[action] = new MissionOutputInfo(action, MissionOutputState.NotDisplayed, "not displayed: " + reason);
        }
        void Elsewhere(string description, params string[] actions)
        {
            foreach (var action in actions) table[action] = new MissionOutputInfo(action, MissionOutputState.HandledElsewhere, description);
        }

        // ---- camera (docs/SCRIPT-ENGINE.md §7a, MissionPresentationDirector.ApplyCameraTo)
        Shown("camera pans to the waypoint over the script's duration", "MOVE_CAMERA_TO");
        Shown("camera pans straight to the path's first waypoint (the path itself is not flown)", "MOVE_CAMERA_ALONG_WAYPOINT_PATH");
        Shown("camera pans to the waypoint and returns to default angle, pitch and zoom", "RESET_CAMERA");
        Shown("camera cuts to the waypoint, looks at the second waypoint, takes zoom and pitch", "SETUP_CAMERA");
        Shown("camera rotates in place over the script's duration", "ROTATE_CAMERA");
        Shown("camera height changes (clamped to the playable zoom range)", "ZOOM_CAMERA", "CAMERA_MOD_SET_FINAL_ZOOM");
        Shown("camera pitch changes (clamped to the camera's pitch range)", "PITCH_CAMERA", "CAMERA_MOD_SET_FINAL_PITCH");
        Shown("camera turns towards the waypoint", "CAMERA_MOD_LOOK_TOWARD", "CAMERA_MOD_FINAL_LOOK_TOWARD", "CAMERA_LOOK_TOWARD_WAYPOINT");
        Shown("camera turns towards the object (its position when the action ran)", "CAMERA_LOOK_TOWARD_OBJECT");
        Shown("camera stays on the named object", "CAMERA_FOLLOW_NAMED", "CAMERA_STOP_FOLLOW");
        Shown("camera stays on the named object (approximation of the tether: the play radius is ignored, the object is centred)", "CAMERA_TETHER_NAMED", "CAMERA_STOP_TETHER_NAMED");
        Shown("the view shakes (W3DView::shake: the type's GameData intensity, decaying and reversing every logic frame)", "SCREEN_SHAKE");
        Shown("the camera is confined to the map border with that index (TerrainLogic::setActiveBoundary); the minimap keeps border 0", "MAP_SWITCH_BORDER");
        Shown("black bars, control bar hidden", "CAMERA_LETTERBOX_BEGIN", "CAMERA_LETTERBOX_END");
        Shown("full-screen white/black fade", "CAMERA_FADE_ADD", "CAMERA_FADE_SUBTRACT", "CAMERA_FADE_MULTIPLY");
        Shown("the world turns grey-scale (red-channel mono, an approximation of the luminance 0.3/0.59/0.11) over the given frames; a post pass of the Stride client", "CAMERA_BW_MODE_BEGIN", "CAMERA_BW_MODE_END");
        Shown("zoom blur into the old view, cut to the waypoint at the apex, zoom blur out of the new one (ScreenMotionBlurFilter, 24 logic frames); a post pass of the Stride client", "CAMERA_MOTION_BLUR_JUMP");

        // ---- input, text, ton, film, radar (unchanged from the first Phase 16 session)
        Shown("the player's world input is dropped (nothing is shown for it, as in the original)", "DISABLE_INPUT", "ENABLE_INPUT");
        Shown("audio event played; its name on screen as a placeholder subtitle", "SPEECH_PLAY");
        Shown("audio event played (not positioned)", "SOUND_PLAY_NAMED", "PLAY_SOUND_EFFECT", "PLAY_SOUND_EFFECT_AT");
        Shown("music track started", "MUSIC_SET_TRACK");
        Shown("text placeholder with the movie name (no video decoder)", "MOVIE_PLAY_FULLSCREEN", "MOVIE_PLAY_RADAR");
        Shown("a line of the message list at the top left (InGameUI::message: MessageFont, MessagePosition, fades after about nine seconds)", "DISPLAY_TEXT");
        Shown("a message window with an OK button (InGamePopupMessage.wnd; pauses the game when the script says so)", "INGAME_POPUP_MESSAGE");
        Shown("the label typed out character by character at the bottom left, title line then body (InGameUI::militarySubtitle: MilitaryCaption fonts and position, blinking block cursor, fade)", "SHOW_MILITARY_CAPTION");
        Shown("centered text at 0.9 of the screen height in the font the script names (W3DDisplay::draw, DISPLAY_CINEMATIC_TEXT)", "DISPLAY_CINEMATIC_TEXT");
        Shown("a named counter/timer line at NamedTimerCountdownPosition, stacked upward (InGameUI::postDraw: NamedTimerCountdown fonts and colors)", "DISPLAY_COUNTDOWN_TIMER", "DISPLAY_COUNTER", "HIDE_COUNTDOWN_TIMER", "HIDE_COUNTER", "ENABLE_COUNTDOWN_TIMER_DISPLAY", "DISABLE_COUNTDOWN_TIMER_DISPLAY");
        Shown("blinking dot on the minimap", "RADAR_CREATE_EVENT", "OBJECT_CREATE_RADAR_EVENT", "TEAM_CREATE_RADAR_EVENT");
        Shown("outcome banner", "VICTORY", "DEFEAT");

        // ---- sound control (AudioDirector.Script.cs)
        Shown("the scripts' music volume multiplies the player's music slider (AudioManager::setVolume)", "MUSIC_SET_VOLUME");
        Shown("the scripts' sound volume multiplies the player's effects slider (2D and 3D sound buses)", "SOUND_SET_VOLUME");
        Shown("the scripts' speech volume multiplies the player's voice slider (Eva, unit and mission dialogue)", "SPEECH_SET_VOLUME");
        Shown("the event gets that volume for voices started from now on and for its playing voices (AudioManager::setAudioEventVolumeOverride)", "AUDIO_OVERRIDE_VOLUME_TYPE");
        Shown("the event's volume override is removed (all of them for the empty event name)", "AUDIO_RESTORE_VOLUME_TYPE", "AUDIO_RESTORE_VOLUME_ALL_TYPE");
        Shown("the event is muted: voices of it are not started (AudioManager::setAudioEventEnabled)", "SOUND_DISABLE_TYPE");
        Shown("the event may sound again (all muted events for SOUND_ENABLE_ALL)", "SOUND_ENABLE_TYPE", "SOUND_ENABLE_ALL");
        Shown("the playing voices of every muted event are stopped", "SOUND_REMOVE_ALL_DISABLED");
        Shown("the playing voices of the event are stopped", "SOUND_REMOVE_TYPE");
        Shown("the ambient loop of the named object is switched off / on (Drawable::enableAmbientSoundFromScript)", "ENABLE_OBJECT_SOUND", "DISABLE_OBJECT_SOUND");
        Shown("no ambient loop plays while paused (AudioManager::pauseAmbient)", "SOUND_AMBIENT_PAUSE", "SOUND_AMBIENT_RESUME");
        Shown("Eva stays silent while disabled and loses the announcements flagged meanwhile (Eva::setEvaEnabled)", "EVA_SET_ENABLED_DISABLED");

        // ---- control bar and HUD (MissionController, CommandBarModel)
        Shown("the button leaves (or takes a slot of) the command set of the object type, for every object that uses the set (GameLogic::setControlBarOverride)",
            "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE", "COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT");
        Shown("the healing icons over repaired objects are left out while off (GameLogic::setDrawIconUI; the player's own selection marks stay)", "OPTIONS_SET_DRAWICON_UI_MODE");
        Shown("only the Selectable flag has a consumer (the player can no longer select the object); the other flags are listed under problems", "UNIT_AFFECT_OBJECT_PANEL_FLAGS", "TEAM_AFFECT_OBJECT_PANEL_FLAGS");
        Shown("the drawable flashes its house colour every half second for the given seconds (Drawable::colorFlash)", "NAMED_FLASH", "TEAM_FLASH");
        Shown("the whole superweapon timer list (top right, \"Name: m:ss\" per public-timer power) is hidden or shown again (InGameUI::setSuperweaponDisplayEnabledByScript)", "DISABLE_SPECIAL_POWER_DISPLAY", "ENABLE_SPECIAL_POWER_DISPLAY");
        Shown("the timer of that object's superweapon is hidden or shown again in the list (InGameUI::hideObjectSuperweaponDisplayByScript)", "NAMED_HIDE_SPECIAL_POWER_DISPLAY", "NAMED_SHOW_SPECIAL_POWER_DISPLAY");
        Shown("the drawable flashes white every half second for the given seconds (Drawable::colorFlash)", "NAMED_FLASH_WHITE", "TEAM_FLASH_WHITE");
        Shown("the object is drawn in that house colour (Object::setCustomIndicatorColor), also on the minimap", "NAMED_CUSTOM_COLOR");

        // ---- handled by the simulation
        Elsewhere("MissionSession.Step runs the scripts but not the world while time is frozen (GameLogic::update returns after ScriptEngine::UPDATE); the director's clock keeps running", "FREEZE_TIME", "UNFREEZE_TIME");

        // ---- not displayed, with the reason
        Not("the control bar model keeps the availability (Hidden/Disabled sciences are hidden/restricted PurchaseScience buttons), but the client has no Generals Powers panel to show it", "PLAYER_SCIENCE_AVAILABILITY");
        Not("the client has no radar-facility rule: the minimap is always live, so there is nothing to force on or off", "RADAR_FORCE_ENABLE", "RADAR_REVERT_TO_NORMAL", "RADAR_ENABLE", "RADAR_DISABLE", "REFRESH_RADAR");
        Not("the mission result screen shows one row without statistics; the scoring flags are kept by the director", "ENABLE_SCORING", "DISABLE_SCORING", "PLAYER_EXCLUDE_FROM_SCORE_SCREEN");
        Not("the fog texture covers the playable area only and the terrain beyond it keeps the fog's edge value; there is no separate shroud ring for the active border to switch", "ENABLE_BORDER_SHROUD", "DISABLE_BORDER_SHROUD");
        Not("the sky box model is not drawn by the terrain layer", "DRAW_SKYBOX_BEGIN", "DRAW_SKYBOX_END");
        Not("the terrain layer draws the whole height field with its border, there is no render window to oversize", "OVERSIZE_TERRAIN");
        Not("the client culls no drawables by a guard band, there is nothing to widen", "RESIZE_VIEW_GUARDBAND");
        Not("the water plane has a fixed height; the original also moves the logic water level (TerrainLogic), which has no counterpart", "WATER_CHANGE_HEIGHT", "WATER_CHANGE_HEIGHT_OVER_TIME");
        Not("no snow or rain is drawn", "SHOW_WEATHER");
        Not("a render option of the original (units behind buildings as markers); the client draws no occlusion markers", "OPTIONS_SET_OCCLUSION_MODE");
        Not("a render option of the original (dynamic particle cap); the client's particle budget is fixed", "OPTIONS_SET_PARTICLE_CAP_MODE");
        Not("the frame pacing of the client is not script controlled", "SET_FPS_LIMIT");
        Not("debug output of the map designers (message box / log); never shown", "DEBUG_MESSAGE_BOX", "DEBUG_STRING");
        Not("the control bar has no button cameo flash", "CAMEO_FLASH");
        Not("emoticon bubbles over objects are not drawn", "NAMED_SET_EMOTICON", "TEAM_SET_EMOTICON");
        Not("the rotational camera shaker (CameraShakeSystemClass) needs a camera roll offset the camera does not have", "CAMERA_ADD_SHAKER_AT");
        Not("the camera mod that freezes the angle during a move is not modelled", "CAMERA_MOD_FREEZE_ANGLE", "CAMERA_MOD_FREEZE_TIME");
        Not("no retail effect or no campaign map uses it", "CAMERA_MOVE_HOME", "MOVE_CAMERA_TO_SELECTION", "CAMERA_SET_DEFAULT", "CAMERA_SET_AUDIBLE_DISTANCE", "CAMERA_ENABLE_SLAVE_MODE", "CAMERA_DISABLE_SLAVE_MODE",
            "CAMERA_MOD_SET_FINAL_SPEED_MULTIPLIER", "CAMERA_MOD_SET_ROLLING_AVERAGE", "SET_VISUAL_SPEED_MULTIPLIER", "CAMERA_FADE_SATURATE");
        Not("the pan variants of the motion blur are not modelled (only the jump is)", "CAMERA_MOTION_BLUR", "CAMERA_MOTION_BLUR_FOLLOW", "CAMERA_MOTION_BLUR_END_FOLLOW");
        Not("not used by any campaign map; background sounds are not suspended by scripts", "SUSPEND_BACKGROUND_SOUNDS", "RESUME_BACKGROUND_SOUNDS");
        Not("not used by any campaign map", "OBJECT_FORCE_SELECT", "SET_TREE_SWAY", "SET_INFANTRY_LIGHTING_OVERRIDE", "RESET_INFANTRY_LIGHTING_OVERRIDE");
        return table.ToImmutable();
    }
}
