using System.Collections.Immutable;
using CnCNew.Simulation.Ids;

namespace CnCNew.Game.Audio;

/// <summary><c>AudioAffect</c> of <c>AudioManager::setVolume</c> as a mission script uses it: <c>MUSIC_SET_VOLUME</c>, the 2D sound (<c>AudioAffect_Sound</c>, the UI bus), the 3D sound (<c>AudioAffect_Sound3D</c>, world and ambient buses) and <c>SPEECH_SET_VOLUME</c>.</summary>
public enum ScriptVolumeKind { Music, Sound, Sound3D, Speech }

/// <param name="MusicVolume">The scripts' own volume factors (0..1, the player's slider is applied on top); see <see cref="AudioDirector.SetScriptVolume"/>.</param>
/// <param name="EventVolumes">The per-event overrides of <c>AUDIO_OVERRIDE_VOLUME_TYPE</c> (a volume of 0 is what <c>SOUND_DISABLE_TYPE</c> sets), sorted by event name.</param>
/// <param name="ObjectsSilenced">Objects whose ambient loop <c>DISABLE_OBJECT_SOUND</c> switched off.</param>
/// <param name="Muted">Sound requests culled because their event was muted by a script (<see cref="AudioOutcomes.ScriptMuted"/>).</param>
/// <param name="VoicesRemoved">Voices stopped by <c>SOUND_REMOVE_TYPE</c> and <c>SOUND_REMOVE_ALL_DISABLED</c>.</param>
public sealed record AudioScriptState(float MusicVolume, float SoundVolume, float Sound3DVolume, float SpeechVolume, ImmutableSortedDictionary<string, float> EventVolumes,
    int ObjectsSilenced, bool EvaEnabled, bool AmbientPaused, long Muted, long VolumeChanges, long VoicesRemoved);

/// <summary>
/// The sound control a mission script has (<c>ScriptActions::doAudioSetVolume</c>, <c>doSoundOverrideVolume</c>, <c>doSoundEnableType</c>, <c>doSoundRemoveAllDisabled</c>, <c>doSoundRemoveType</c>,
/// <c>doEnableObjectSound</c>, <c>doEvaEnabledDisabled</c>, <c>doAmbientSoundsPause</c>; <c>docs/SCRIPT-ENGINE.md</c> §7a). The director only keeps the state and applies it to the voices it
/// starts or already plays; nothing here is part of the simulation.
/// </summary>
public sealed partial class AudioDirector
{
    /// <summary>The event-volume value that removes an override (<c>-100 / 100</c>: what <c>AUDIO_RESTORE_VOLUME_TYPE</c> passes).</summary>
    public const float RestoreEventVolume = -1f;

    private readonly float[] _scriptVolumes = [1f, 1f, 1f, 1f];
    private readonly Dictionary<string, float> _eventVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<uint> _objectSoundOff = [];
    private bool _ambientPaused;
    private long _scriptMuted, _scriptVolumeChanges, _scriptVoicesRemoved;

    /// <summary>The script volume factor of <paramref name="kind"/> (1 until a script sets it).</summary>
    public float ScriptVolume(ScriptVolumeKind kind) => _scriptVolumes[(int)kind];

    /// <summary>
    /// <c>AudioManager::setVolume</c> with a script's value: <paramref name="percent"/> (0..100, as the script writes it) becomes a factor clamped to 0..1 that multiplies the player's slider of that kind
    /// (<c>m_musicVolume = m_scriptMusicVolume * m_systemMusicVolume</c>). Speech is the voice bus (Eva, unit and mission dialogue), music the music bus, 2D sound the UI bus, 3D sound the world and ambient buses.
    /// </summary>
    public void SetScriptVolume(ScriptVolumeKind kind, float percent)
    {
        _scriptVolumes[(int)kind] = Math.Clamp(percent / 100f, 0f, 1f);
        _scriptVolumeChanges++;
        SyncBuses();
    }

    /// <summary>
    /// <c>AudioManager::setAudioEventVolumeOverride</c>: an empty <paramref name="eventName"/> forgets every override; <see cref="RestoreEventVolume"/> removes the event's; any other
    /// <paramref name="volume"/> (a factor, 1 = the catalog's own, 2 = twice as loud) replaces the event's volume for every voice started from now on and is applied to the voices of that event that
    /// are playing right now (<c>adjustVolumeOfPlayingAudio</c>). Restoring and forgetting leave playing voices as they are, like the reference.
    /// </summary>
    public void OverrideEventVolume(string eventName, float volume)
    {
        if (_disposed) return;
        if (string.IsNullOrEmpty(eventName))
        {
            _eventVolumes.Clear();
            return;
        }
        // An event the audio catalog does not define can never match a voice: counted like every unknown name, not stored for nothing.
        if (_catalog.Find(eventName) is null)
        {
            _unresolved[eventName] = _unresolved.GetValueOrDefault(eventName) + 1;
            return;
        }
        if (volume == RestoreEventVolume)
        {
            _eventVolumes.Remove(eventName);
            return;
        }
        _eventVolumes[eventName] = volume;
        foreach (var voice in _tracked)
            if (voice.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)) _mixer.SetVoiceVolume(voice.Handle, volume * voice.Shift);
        foreach (var entry in _ambient.Values)
            if (entry.Handle.IsValid && entry.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)) _mixer.SetVoiceVolume(entry.Handle, volume * entry.Shift);
    }

    /// <summary><c>AudioManager::setAudioEventEnabled</c> (<c>SOUND_DISABLE_TYPE</c>/<c>SOUND_ENABLE_TYPE</c>/<c>SOUND_ENABLE_ALL</c>): disabling is a volume override of 0, enabling removes the override, an empty name enables everything.</summary>
    public void SetEventEnabled(string eventName, bool enabled) => OverrideEventVolume(eventName, enabled ? RestoreEventVolume : 0f);

    private bool IsScriptDisabled(string eventName) => _eventVolumes.TryGetValue(eventName, out var volume) && (volume <= 0f || volume < _catalog.Settings.MinSampleVolume);

    /// <summary><c>SOUND_REMOVE_ALL_DISABLED</c> (<c>AudioManager::removeDisabledEvents</c>): stops the voices of every event a script disabled. Returns how many.</summary>
    public int RemoveDisabledEvents() => StopEvents(IsScriptDisabled);

    /// <summary><c>SOUND_REMOVE_TYPE</c> (<c>AudioManager::removeAudioEvent</c>): stops the playing voices of one event. Returns how many.</summary>
    public int RemoveEvent(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        return StopEvents(name => name.Equals(eventName, StringComparison.OrdinalIgnoreCase));
    }

    private int StopEvents(Func<string, bool> selected)
    {
        if (_disposed) return 0;
        var stopped = 0;
        foreach (var voice in _tracked.Where(voice => selected(voice.Event)).ToArray())
        {
            _mixer.Stop(voice.Handle, 0.05f);
            _tracked.Remove(voice);
            _ended[(int)voice.Source]++;
            stopped++;
        }
        foreach (var (key, entry) in _ambient.Where(pair => pair.Value.Handle.IsValid && selected(pair.Value.Event)).ToArray())
        {
            _mixer.Stop(entry.Handle, 0.05f);
            _ended[(int)AudioSource.Ambient]++;
            _ambient.Remove(key);
            stopped++;
        }
        _scriptVoicesRemoved += stopped;
        return stopped;
    }

    /// <summary><c>Eva::setEvaEnabled</c> (<c>EVA_SET_ENABLED_DISABLED</c>): while off, the announcer is not updated at all and flagged events are lost.</summary>
    public bool EvaEnabled
    {
        get => _eva.Enabled;
        set => _eva.Enabled = value;
    }

    /// <summary>
    /// <c>Drawable::enableAmbientSoundFromScript</c> (<c>ENABLE_OBJECT_SOUND</c>/<c>DISABLE_OBJECT_SOUND</c>): the ambient loop of one object is kept off (a running loop stops with the next ambient
    /// update). Returns whether the object is a valid one.
    /// </summary>
    public bool SetObjectSoundEnabled(EntityId entity, bool enabled)
    {
        if (!entity.IsValid || _disposed) return false;
        if (enabled) _objectSoundOff.Remove(entity.Value);
        else _objectSoundOff.Add(entity.Value);
        return true;
    }

    /// <summary><c>SOUND_AMBIENT_PAUSE</c>/<c>SOUND_AMBIENT_RESUME</c> (<c>AudioManager::pauseAmbient</c>): no ambient loop plays while paused.</summary>
    public void SetAmbientPaused(bool paused) => _ambientPaused = paused;

    /// <summary>
    /// Forgets everything a mission script did (<c>AudioManager::reset</c>: script volumes back to 1, no event overrides): the game ended or a new one begins, and the menu must not stay as quiet as the
    /// last cinematic left the mixer. Counters stay.
    /// </summary>
    public void ResetScriptState()
    {
        Array.Fill(_scriptVolumes, 1f);
        _eventVolumes.Clear();
        _objectSoundOff.Clear();
        _ambientPaused = false;
        _eva.Enabled = true;
        SyncBuses();
    }

    /// <summary>What the scripts did to the sound so far (for the report and the tests).</summary>
    public AudioScriptState ScriptState => new(_scriptVolumes[(int)ScriptVolumeKind.Music], _scriptVolumes[(int)ScriptVolumeKind.Sound], _scriptVolumes[(int)ScriptVolumeKind.Sound3D],
        _scriptVolumes[(int)ScriptVolumeKind.Speech], _eventVolumes.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase), _objectSoundOff.Count, EvaEnabled, _ambientPaused,
        _scriptMuted, _scriptVolumeChanges, _scriptVoicesRemoved);
}
