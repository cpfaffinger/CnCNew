using System.Numerics;
using CnCNew.Simulation.Ids;

namespace CnCNew.Game.Shell;

/// <summary>Which of the sound volumes a mission script sets (<c>MUSIC_SET_VOLUME</c>, <c>SOUND_SET_VOLUME</c> = the 2D and the 3D sound, <c>SPEECH_SET_VOLUME</c>).</summary>
public enum MissionVolume { Music, Sound, Speech }

/// <summary>
/// What the host's sound offers a mission script (implemented by <c>GameAudio</c>; <c>docs/SCRIPT-ENGINE.md</c> §7a). Volumes are percentages as the script writes them. Every method says
/// whether the host really acted, so the director counts an output as displayed only when something changed.
/// </summary>
public interface IMissionAudioControl
{
    /// <summary><c>SPEECH_PLAY</c>, <c>SOUND_PLAY_NAMED</c>, <c>PLAY_SOUND_EFFECT*</c> and <c>MUSIC_SET_TRACK</c>: whether a voice started.</summary>
    bool PlayScripted(string action, string eventName);

    void SetVolume(MissionVolume which, float percent);

    /// <summary>A per-event volume (<c>AUDIO_OVERRIDE_VOLUME_TYPE</c>); -100 restores the event, an empty name forgets every override (<c>AUDIO_RESTORE_VOLUME_*</c>).</summary>
    void OverrideEventVolume(string eventName, float percent);

    /// <summary><c>SOUND_DISABLE_TYPE</c>/<c>SOUND_ENABLE_TYPE</c>; an empty name with <paramref name="enabled"/> enables everything (<c>SOUND_ENABLE_ALL</c>).</summary>
    void SetEventEnabled(string eventName, bool enabled);

    /// <summary><c>SOUND_REMOVE_ALL_DISABLED</c>: the number of voices stopped.</summary>
    int RemoveDisabledEvents();

    /// <summary><c>SOUND_REMOVE_TYPE</c>: the number of voices stopped.</summary>
    int RemoveEvent(string eventName);

    /// <summary><c>EVA_SET_ENABLED_DISABLED</c>.</summary>
    void SetEvaEnabled(bool enabled);

    /// <summary><c>ENABLE_OBJECT_SOUND</c>/<c>DISABLE_OBJECT_SOUND</c>: the ambient loop of one object.</summary>
    bool SetObjectSoundEnabled(EntityId entity, bool enabled);

    /// <summary><c>SOUND_AMBIENT_PAUSE</c>/<c>SOUND_AMBIENT_RESUME</c>.</summary>
    void SetAmbientPaused(bool paused);
}

/// <summary>What the host's object layer offers a mission script (implemented by <c>LiveMission</c> over the frame projector; presentation only, the simulation never sees it).</summary>
public interface IMissionObjectEffects
{
    /// <summary>
    /// <c>NAMED_FLASH</c>/<c>NAMED_FLASH_WHITE</c>/<c>TEAM_FLASH*</c> (<c>ScriptActions::doNamedFlash</c>): the drawable plays its colour flash every half second for <paramref name="seconds"/> seconds.
    /// <paramref name="color"/> is the flash colour as 0..1 red, green and blue; <see langword="null"/> is the house colour of the object (<c>Object::getIndicatorColor</c>). False when the object is not drawn.
    /// </summary>
    bool Flash(EntityId entity, Vector3? color, int seconds);

    /// <summary><c>NAMED_CUSTOM_COLOR</c> (<c>Object::setCustomIndicatorColor</c>): the house colour of one object; <paramref name="argb"/> 0 restores the owner's colour. False when the object is not drawn.</summary>
    bool SetHouseColor(EntityId entity, uint argb);
}

/// <summary>
/// What the host's HUD offers a mission script (implemented by <c>MissionController</c>): the control bar's script overrides, the sciences the player may buy, the icon overlay and the
/// selectability of single objects. The simulation never sees any of it.
/// </summary>
public interface IMissionHudControl
{
    /// <summary><c>COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE</c> (<c>ScriptActions::doRemoveCommandBarButton</c>): the button leaves the command set of the object type. False when the type or the button is unknown.</summary>
    bool RemoveCommandButton(string buttonName, string objectType);

    /// <summary><c>COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT</c> (<c>doAddCommandBarButton</c>): the button takes <paramref name="slot"/> (1..14) of the object type's command set. False when type, button or slot is invalid.</summary>
    bool AddCommandButton(string buttonName, string objectType, int slot);

    /// <summary><c>PLAYER_SCIENCE_AVAILABILITY</c> (<c>Player::setScienceAvailability</c>) for the local player: "Available", "Disabled" or "Hidden". False when the name of the availability or of the science is unknown.</summary>
    bool SetScienceAvailability(string science, string availability);

    /// <summary><c>OPTIONS_SET_DRAWICON_UI_MODE</c> (<c>GameLogic::setDrawIconUI</c>): the icon overlay (heal icons and selection marks) of the drawables.</summary>
    void SetIconUiEnabled(bool enabled);

    /// <summary><c>UNIT_AFFECT_OBJECT_PANEL_FLAGS</c> "Selectable" (<c>Object::setSelectable</c>): the object can or cannot be selected by the player.</summary>
    bool SetObjectSelectable(EntityId entity, bool selectable);
}
