using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Audio;

/// <summary>
/// The sound of one client process: owns the <see cref="AudioDirector"/> (and with it the mixer) from start to exit and connects it to whatever is on screen —
/// the shell (menu music, button clicks) or a running game (<see cref="Attach"/>: the simulation's sound cues, Eva, ambient loops, the side's music, the unit
/// acknowledgements of selections and orders, the control bar clicks). The host calls <see cref="Update"/> once per rendered frame and renders
/// <see cref="Mixer"/> to a device or to an <see cref="OfflineAudioRenderer"/>. Nothing here writes to the simulation: it only reads the game through
/// <see cref="LiveGame"/> and listens to events the HUD already raises. No Stride types; the whole class runs headless in tests.
/// </summary>
public sealed class GameAudio : IDisposable, IMissionAudioControl
{
    private readonly FxCatalog _fx;
    private readonly SortedDictionary<string, int> _unknownHooks = new(StringComparer.Ordinal);
    private LiveGame? _live;
    private MatchController? _hud;
    private ImmutableHashSet<uint> _selected = [];
    private bool _disposed;

    public GameAudio(AudioDirector director, FxCatalog fx)
    {
        Director = director ?? throw new ArgumentNullException(nameof(director));
        _fx = fx ?? throw new ArgumentNullException(nameof(fx));
    }

    /// <summary>Builds the director (mixer from <c>AudioSettings.ini</c>, <c>Eva.ini</c>) for the loaded game data.</summary>
    public static GameAudio Create(GameData data, AudioSettings? settings = null, AudioDirectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new GameAudio(AudioDirector.Create(data, options, settings), data.Fx);
    }

    public AudioDirector Director { get; }
    public CnCNew.Audio.Mixing.SoftwareMixer Mixer => Director.Mixer;

    /// <summary>The game the director currently listens to, or <see langword="null"/> while the shell is showing.</summary>
    public LiveGame? Game => _live;

    public bool Muted
    {
        get => Director.Muted;
        set => Director.Muted = value;
    }

    /// <summary><c>MiscAudio</c> keys a caller asked for that the data does not define, by key (never played, never silently skipped).</summary>
    public ImmutableSortedDictionary<string, int> UnknownHooks => _unknownHooks.ToImmutableSortedDictionary(StringComparer.Ordinal);

    /// <summary>The volume sliders of the options screen; takes effect with the next rendered block.</summary>
    public void ApplySettings(AudioSettings settings) => Director.ApplySettings(settings);

    /// <summary>The shell is on screen: the menu theme plays (looped) until a game is attached.</summary>
    public void EnterShell()
    {
        Detach();
        Director.PlayMenuMusic();
    }

    /// <summary>
    /// A match, mission or scenario started: its side's playlist replaces the menu theme, and the HUD's selection changes, command feedback and button clicks
    /// reach the director from now on. The simulation's own sounds arrive through <see cref="Update"/>.
    /// </summary>
    public void Attach(LiveGame live, MatchController hud)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(hud);
        Detach();
        _live = live;
        _hud = hud;
        _selected = [..hud.Input.Selection.Selected.Select(id => id.Value)];
        hud.Input.Selection.SelectionChanged += OnSelectionChanged;
        hud.CommandFeedbackObserved += OnCommandFeedback;
        hud.ButtonClicked += OnButtonClicked;
        Director.StartMatchMusic(new LiveAudioView(live).HumanSide);
    }

    /// <summary>The game is over or was left: its listeners are removed and its music stops (the caller starts the menu theme with <see cref="EnterShell"/>).</summary>
    public void Detach()
    {
        if (_hud is { } hud)
        {
            hud.Input.Selection.SelectionChanged -= OnSelectionChanged;
            hud.CommandFeedbackObserved -= OnCommandFeedback;
            hud.ButtonClicked -= OnButtonClicked;
        }
        if (_live is not null) Director.StopMusic();
        // What a mission script did to the volumes, muted events and Eva ends with the game (AudioManager::reset).
        Director.ResetScriptState();
        _live = null;
        _hud = null;
        _selected = [];
    }

    /// <summary>Once per rendered frame, after the game advanced: cues, Eva, ambient loops, music and volumes (<see cref="AudioDirector.Update"/>); without a game only music and volumes.</summary>
    public void Update(double seconds)
    {
        if (_disposed) return;
        if (_live is { } live) Director.Update(live, seconds);
        else Director.Tick(null, seconds);
    }

    /// <summary>Plays an event by its catalog name (the shell's click names); an unknown name is counted by the director.</summary>
    public bool PlayUi(string eventName) => Director.PlayUi(eventName);

    /// <summary>Plays the event a <c>MiscAudio</c> key names (<c>GUIClickSound</c>, <c>NoCanDoSound</c>, ...); a key the data lacks is counted in <see cref="UnknownHooks"/>.</summary>
    public bool PlayHook(string miscAudioKey)
    {
        if (!_fx.MiscAudio.TryGetValue(miscAudioKey, out var name))
        {
            _unknownHooks[miscAudioKey] = _unknownHooks.GetValueOrDefault(miscAudioKey) + 1;
            return false;
        }
        return FxCatalog.IsAudible(name) && Director.PlayUi(name);
    }

    /// <summary>
    /// A sound a mission script asked for (<c>MissionPresentationDirector.Audio</c>): <c>MUSIC_SET_TRACK</c> changes the track, every other action
    /// (<c>SPEECH_PLAY</c>, <c>SOUND_PLAY_NAMED</c>, <c>PLAY_SOUND_EFFECT</c>, <c>PLAY_SOUND_EFFECT_AT</c>) plays the named event without a position. Returns whether a
    /// voice started; an unknown name is counted by the director like any other.
    /// </summary>
    public bool PlayScripted(string action, string eventName)
        => !string.IsNullOrWhiteSpace(eventName) && (action == "MUSIC_SET_TRACK" ? Director.SetMusicTrack(eventName) : Director.PlayUi(eventName));

    // ---- what a mission script may do to the sound (IMissionAudioControl; AudioDirector.Script.cs holds the state)

    /// <summary><c>MUSIC_SET_VOLUME</c>, <c>SOUND_SET_VOLUME</c> (the 2D and the 3D sound, as <c>AudioAffect_Sound | AudioAffect_Sound3D</c>) and <c>SPEECH_SET_VOLUME</c>, in percent.</summary>
    public void SetVolume(MissionVolume which, float percent)
    {
        switch (which)
        {
            case MissionVolume.Music: Director.SetScriptVolume(ScriptVolumeKind.Music, percent); break;
            case MissionVolume.Sound:
                Director.SetScriptVolume(ScriptVolumeKind.Sound, percent);
                Director.SetScriptVolume(ScriptVolumeKind.Sound3D, percent);
                break;
            default: Director.SetScriptVolume(ScriptVolumeKind.Speech, percent); break;
        }
    }

    /// <summary>A script's per-event volume in percent (<c>doSoundOverrideVolume</c> divides by 100: -100 is the "restore" value, an empty name forgets every override).</summary>
    public void OverrideEventVolume(string eventName, float percent) => Director.OverrideEventVolume(eventName, percent / 100f);

    public void SetEventEnabled(string eventName, bool enabled) => Director.SetEventEnabled(eventName, enabled);
    public int RemoveDisabledEvents() => Director.RemoveDisabledEvents();
    public int RemoveEvent(string eventName) => Director.RemoveEvent(eventName);
    public void SetEvaEnabled(bool enabled) => Director.EvaEnabled = enabled;
    public bool SetObjectSoundEnabled(EntityId entity, bool enabled) => Director.SetObjectSoundEnabled(entity, enabled);
    public void SetAmbientPaused(bool paused) => Director.SetAmbientPaused(paused);

    private void OnButtonClicked() => PlayHook("GUIClickSound");

    private void OnSelectionChanged(ImmutableArray<EntityId> selected)
    {
        var before = _selected;
        _selected = [..selected.Select(id => id.Value)];
        if (_live is not { } live) return;
        // Only a unit that joined the selection speaks (a selection that shrinks because a unit died says nothing), and only the local player's own units answer.
        string? speaker = null;
        var own = 0;
        foreach (var id in selected)
        {
            if (live.World.FindEntity(id) is not { } entity || entity.Owner != live.Human) continue;
            own++;
            if (speaker is null && !before.Contains(id.Value)) speaker = entity.Template.Name;
        }
        if (speaker is not null) SelectionAcknowledged(speaker, own);
    }

    /// <summary>The selection gained <paramref name="template"/> and now holds <paramref name="ownCount"/> own objects: its select voice plays.</summary>
    public bool SelectionAcknowledged(string template, int ownCount) => Director.OnUnitSelected(template, ownCount);

    private void OnCommandFeedback(CommandFeedback feedback)
    {
        string? speaker = null;
        if (_live is { } live && _hud is { } hud) speaker = hud.Input.Selection.OwnSelected(live.World).FirstOrDefault()?.Template.Name;
        CommandAcknowledged(feedback, speaker);
    }

    /// <summary>
    /// The audible side of an order the player gave: the first own selected unit (<paramref name="speakerTemplate"/>) confirms a move, attack or enter order with its
    /// voice; a click that cannot become an order gives the no-can-do sound; an order refused for lack of money makes Eva say so. A refusal for any other reason is
    /// already a <c>CommandRejected</c> event of the simulation and arrives as a sound cue, so nothing is played for it here.
    /// </summary>
    public void CommandAcknowledged(CommandFeedback feedback, string? speakerTemplate)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        UnitVoiceKind? voice = feedback.Kind switch
        {
            FeedbackKind.MoveMarker or FeedbackKind.WaypointMarker => UnitVoiceKind.Move,
            FeedbackKind.AttackMarker => UnitVoiceKind.Attack,
            FeedbackKind.EnterMarker => UnitVoiceKind.Enter,
            _ => null,
        };
        if (voice is { } kind)
        {
            if (speakerTemplate is not null) Director.OnUnitOrdered(speakerTemplate, kind);
            return;
        }
        switch (feedback.Kind)
        {
            case FeedbackKind.InvalidOrder or FeedbackKind.Unsupported:
                PlayHook("NoCanDoSound");
                break;
            case FeedbackKind.Rejected when feedback.Rejection == CommandRejection.InsufficientFunds:
                Director.NotifyInsufficientFunds();
                break;
        }
    }

    /// <summary>
    /// The console report of a run: one line per source (requested, started, ended, dropped), the mixer's own voice counters, and by name everything that
    /// could not be played (unknown events, files that are missing or do not decode, unit voice keys a template lacks, Eva events nobody defined).
    /// </summary>
    public ImmutableArray<string> Report()
    {
        var stats = Director.Stats;
        var culture = CultureInfo.InvariantCulture;
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var (name, counters) in stats.Sources)
            lines.Add(string.Create(culture, $"audio source {name}: requested {counters.Requested}, started {counters.Started}, ended {counters.Ended}, dropped {counters.Dropped}"));
        var mixer = stats.Mixer;
        lines.Add(string.Create(culture, $"audio mixer: rendered {mixer.RenderedFrames} frames ({mixer.RenderedFrames / (double)Mixer.SampleRate:F2} s), voices started {mixer.Started}, finished {mixer.Finished}, stopped {mixer.Stopped}, stolen {mixer.Stolen}, replaced by limit {mixer.ReplacedByLimit}, rejected (limit {mixer.RejectedLimit}, no channel {mixer.RejectedNoChannel}, distance {mixer.RejectedDistance}, muted {mixer.RejectedMuted}), culled quiet {mixer.CulledQuiet}, clipped samples {mixer.ClippedSamples}"));
        lines.Add(string.Create(culture, $"audio music: started {stats.MusicStarted}, ended {stats.MusicEnded}, failed {stats.MusicFailed}, available {stats.MusicAvailable}, track {stats.MusicTrack ?? "-"}"));
        lines.Add(string.Create(culture, $"audio eva: requested {stats.EvaRequested}, played {stats.EvaPlayed}, expired {stats.EvaExpired}, side missing {stats.EvaSideMissing}"));
        lines.Add($"audio outcomes: {Join(stats.Outcomes)}");
        lines.Add($"audio started events ({stats.StartedEvents.Count} names, {stats.StartedEvents.Values.Sum().ToString(culture)} voices): {Join(stats.StartedEvents)}");
        lines.Add($"audio unknown event names ({stats.UnresolvedEvents.Count}): {Join(stats.UnresolvedEvents)}");
        lines.Add($"audio missing or undecodable sound files ({stats.DecodeFailures.Count}): {Join(stats.DecodeFailures)}");
        lines.Add($"audio missing unit voice keys: {Join(stats.MissingUnitVoices)}");
        lines.Add($"audio unknown eva events: {Join(stats.UnknownEvaEvents)}");
        lines.Add($"audio unknown MiscAudio keys: {Join(_unknownHooks)}");
        lines.Add($"audio skipped movies: {Join(stats.SkippedMovies)}");
        var script = Director.ScriptState;
        lines.Add(string.Create(culture, $"audio script control: volume music {script.MusicVolume:F2} sound {script.SoundVolume:F2}/{script.Sound3DVolume:F2} speech {script.SpeechVolume:F2}, {script.VolumeChanges} volume changes, {script.EventVolumes.Count} event overrides ({Join(script.EventVolumes)}), {script.ObjectsSilenced} objects silenced, eva {(script.EvaEnabled ? "on" : "off")}, ambient {(script.AmbientPaused ? "paused" : "on")}, {script.Muted} requests muted, {script.VoicesRemoved} voices removed"));
        return lines.ToImmutable();
    }

    private static string Join<T>(IEnumerable<KeyValuePair<string, T>> values)
    {
        var text = string.Join(", ", values.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));
        return text.Length == 0 ? "none" : text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Detach();
        _disposed = true;
        Director.Dispose();
    }
}
