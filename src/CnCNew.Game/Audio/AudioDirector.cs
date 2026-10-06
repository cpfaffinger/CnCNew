using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Audio;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Mixing;
using CnCNew.Audio.Selection;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;
using CnCNew.Ui.Settings;
using AudioEventInfo = CnCNew.Audio.Catalog.AudioEventInfo;

namespace CnCNew.Game.Audio;

/// <summary>Which order a unit acknowledges (<c>Object</c> voice keys <c>VoiceMove</c>, <c>VoiceAttack</c>, ...).</summary>
public enum UnitVoiceKind { Move, Attack, Guard, Enter, Garrison }

/// <param name="Seed">Seed of the director's own generator (sound variations, Eva picks, playlist shuffle); not the simulation's.</param>
/// <param name="CueLimits">Flood protection applied to derived cues before they are resolved (see <see cref="SoundCueRateLimiter"/>).</param>
/// <param name="MusicCrossfadeSeconds">Null uses <c>TimeToFadeAudio</c> of <c>AudioSettings.ini</c>.</param>
/// <param name="MusicGapSeconds">Silence between two in-game tracks.</param>
/// <param name="PauseFadeSeconds">Fade of the sounds that are cut when the game pauses.</param>
/// <param name="PausedMusicVolume">Music bus factor while paused.</param>
/// <param name="MaxAmbientVoices">Drawable ambient loops (<c>SoundAmbient</c>) audible at once, nearest first.</param>
public sealed record AudioDirectorOptions
{
    public ulong Seed { get; init; } = 1;
    public SoundCueLimits CueLimits { get; init; } = new(3, 10, 0f, 48);
    public float? MusicCrossfadeSeconds { get; init; }
    public float MusicGapSeconds { get; init; } = 3f;
    public float PauseFadeSeconds { get; init; } = 0.15f;
    public float PausedMusicVolume { get; init; } = 0.4f;
    public TimeOfDay TimeOfDay { get; init; } = TimeOfDay.Afternoon;
    public int MaxAmbientVoices { get; init; } = 12;
    public float AmbientUpdateSeconds { get; init; } = 0.25f;
    public float UnitVoiceFadeSeconds { get; init; } = 0.08f;
}

/// <summary>What the director needs to know about the running match; <see cref="LiveAudioView"/> answers it from a <see cref="LiveGame"/>.</summary>
public interface IAudioGameView
{
    PlayerId Human { get; }

    /// <summary>The local player's template side as <c>Eva.ini</c> spells it (<c>America</c>, <c>ChinaTankGeneral</c>, ...).</summary>
    string HumanSide { get; }

    /// <summary>The presentation clock of the game (<see cref="LiveGame.Clock"/>): the announcer counts its original frames, the flood protection converts cue ticks with its rate.</summary>
    GameClock Clock { get; }

    /// <summary>The local player's energy balance is negative (<c>Energy::hasSufficientPower</c> is false).</summary>
    bool LowPower { get; }

    Relationship RelationshipOf(PlayerId from, PlayerId to);

    /// <summary>True when the local player cannot see the position.</summary>
    bool IsShrouded(Vector3 position);
}

public sealed class LiveAudioView(LiveGame live) : IAudioGameView
{
    private string? _side;

    public PlayerId Human => live.Human;

    public string HumanSide => _side ??= live.Data.Factions
        .FirstOrDefault(value => value.Faction.Name.Equals(live.HumanFaction, StringComparison.OrdinalIgnoreCase))?.Side ?? "";

    public GameClock Clock => live.Clock;
    public bool LowPower => live.World.EnergyProductionOf(live.Human) < 0;
    public Relationship RelationshipOf(PlayerId from, PlayerId to) => live.World.RelationshipOf(from, to);
    public bool IsShrouded(Vector3 position) => live.FxView.IsShrouded(position);
}

/// <summary>Names of the per-cue outcomes counted in <see cref="AudioDirectorStats.Outcomes"/>; every cue ends in exactly one of them.</summary>
public static class AudioOutcomes
{
    public const string Played = "played";
    public const string EvaRequested = "eva:requested";
    public const string EvaOtherPlayer = "dropped:eva-other-player";
    public const string EvaUnknown = "dropped:eva-unknown-event";
    public const string UiOtherPlayer = "dropped:ui-other-player";
    public const string MusicCueUnsupported = "dropped:music-cue";
    public const string Unresolved = "dropped:unresolved-event";
    public const string NotLocal = "dropped:not-local";
    public const string NoOwner = "dropped:no-owner";
    public const string Shrouded = "dropped:shrouded";
    public const string CulledDistance = "dropped:distance";
    public const string CulledQuiet = "dropped:quiet";
    public const string NoSounds = "dropped:no-sounds";
    public const string DecodeFailed = "dropped:no-decodable-sound";
    public const string WorldWithoutPosition = "note:world-without-position";
    public const string Paused = "dropped:paused";
    public const string RateLimited = "dropped:rate-limited";
    public const string MissingVoiceKey = "dropped:no-voice-key";

    /// <summary>A mission script muted the event (<c>SOUND_DISABLE_TYPE</c>, or <c>AUDIO_OVERRIDE_VOLUME_TYPE</c> with a volume below the minimum): <c>AudioManager::addAudioEvent</c> culls it as muted.</summary>
    public const string ScriptMuted = "dropped:script-muted";
}

/// <summary>Who asked for a sound; the director counts every request, start, end and drop per source (<see cref="AudioDirectorStats.Sources"/>).</summary>
public enum AudioSource
{
    /// <summary>Derived cues of simulation events and FX lists (weapon fire, explosions, deaths, construction, the capture tick, ...).</summary>
    Simulation,
    /// <summary>Button clicks and other host requests (<see cref="AudioDirector.PlayUi"/>).</summary>
    Ui,
    /// <summary>Selection and order acknowledgements of the local player's units.</summary>
    UnitVoice,
    Eva,
    /// <summary>Looping <c>SoundAmbient</c> of drawables.</summary>
    Ambient,
    Music,
}

/// <param name="Requested">Requests of this source that reached the director.</param>
/// <param name="Started">Requests handed to the mixer as a voice (the mixer may still refuse or steal it: <see cref="MixerStatistics"/>).</param>
/// <param name="Ended">Started voices the mixer no longer plays (finished, stopped, stolen or refused).</param>
/// <param name="Dropped">Requests that never became a voice (unknown event, missing file, culled, rate limited, not for the local player, ...).</param>
public readonly record struct AudioSourceCounters(long Requested, long Started, long Ended, long Dropped);

/// <summary>Counters of a director; every sorted collection is a copy, so two identical runs produce equal statistics.</summary>
public sealed record AudioDirectorStats(
    ImmutableSortedDictionary<string, AudioSourceCounters> Sources,
    ImmutableSortedDictionary<string, long> StartedEvents,
    ImmutableSortedDictionary<string, long> Outcomes,
    ImmutableSortedDictionary<string, int> UnresolvedEvents,
    ImmutableSortedDictionary<string, int> MissingUnitVoices,
    ImmutableSortedDictionary<string, int> SkippedMovies,
    ImmutableSortedDictionary<string, int> UnknownEvaEvents,
    long CuesReceived,
    long UiPlayed,
    long UnitVoicesPlayed,
    long EvaRequested,
    long EvaPlayed,
    long EvaExpired,
    long EvaSideMissing,
    long MusicStarted,
    long MusicFailed,
    long MusicEnded,
    bool MusicAvailable,
    string? MusicTrack,
    long AmbientStarted,
    int AmbientActive,
    int TrackedVoices,
    int CachedSounds,
    long DecodedSamples,
    ImmutableSortedDictionary<string, string> DecodeFailures,
    MixerStatistics Mixer)
{
    public long Count(string outcome) => Outcomes.GetValueOrDefault(outcome);
}

/// <summary>
/// Turns game events into mixer requests. One instance per running game; call it from the game thread only (the mixer itself is rendered
/// by the audio thread, see <c>AudioOutput</c> in the client). Nothing here touches a device, a clock or a thread: time is the sum of the
/// <c>dt</c> values the caller passes, all variation comes from <see cref="AudioDirectorOptions.Seed"/>, so two runs with the same inputs
/// issue identical mixer commands. Every cue, music track, movie and Eva event that cannot be played is counted in <see cref="Stats"/>.
/// See <c>docs/GAME-AUDIO.md</c> for the rules and the reference sources.
/// </summary>
public sealed partial class AudioDirector : IDisposable
{
    private const float BusChangeThreshold = 0.002f;
    private const float PositionChangeThreshold = 0.5f;

    /// <param name="Event">The catalog event of the voice, so a script's per-event volume or removal (<c>docs/SCRIPT-ENGINE.md</c> §7a) finds it again.</param>
    /// <param name="Shift">The random volume shift the voice was started with; the live volume of a voice is the event's override times this.</param>
    private sealed record Tracked(VoiceHandle Handle, long IssuedFrames, AudioSource Source, string Event, float Shift);

    private sealed class AmbientEntry(VoiceHandle handle, long issuedFrames, Vector3 position, double nextAllowed)
    {
        public VoiceHandle Handle { get; } = handle;
        public long IssuedFrames { get; } = issuedFrames;
        public Vector3 Position { get; set; } = position;
        public double NextAllowed { get; } = nextAllowed;
        public string Event { get; init; } = string.Empty;
        public float Shift { get; init; } = 1f;
    }

    private readonly AudioCatalog _catalog;
    private readonly FxCatalog _fx;
    private readonly Func<string, Stream> _open;
    private readonly SoftwareMixer _mixer;
    private readonly bool _ownsMixer;
    private readonly AudioDirectorOptions _options;
    private readonly SeededRng _rng;
    private readonly SoundCache _cache;
    private readonly SoundCueRateLimiter _limiter;
    private readonly EvaAnnouncer _eva;
    private readonly Dictionary<string, SoundSelectionState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, long> _outcomes = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _startedEvents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AudioSource[] AllSources = Enum.GetValues<AudioSource>();
    private readonly long[] _requested = new long[AllSources.Length];
    private readonly long[] _started = new long[AllSources.Length];
    private readonly long[] _ended = new long[AllSources.Length];
    private readonly long[] _dropped = new long[AllSources.Length];
    private readonly SortedDictionary<string, int> _unresolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, int> _missingVoices = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _movies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Tracked> _tracked = [];
    private readonly SortedDictionary<long, AmbientEntry> _ambient = [];
    private readonly Dictionary<string, AudioEventInfo?> _ambientByTemplate = new(StringComparer.OrdinalIgnoreCase);
    private readonly float[] _sentBuses = new float[Enum.GetValues<AudioBus>().Length];
    private float _sentMaster = float.NaN;
    private readonly float _uiRatio;

    private AudioSettings _settings;
    private Vector3 _listenerPosition;
    private Vector3 _listenerForward = Vector3.UnitY;
    private float _zoomVolume = 1f;
    private bool _listenerSent;
    private bool _paused;
    private bool _muted;
    private double _now;
    private double _lastAmbient = double.NegativeInfinity;
    private long _lastEvaFrame = -1;
    private long _cuesReceived, _uiPlayed, _unitVoicesPlayed, _ambientStarted;

    private enum MusicMode { None, Menu, Match }

    private MusicMode _musicMode;
    private MusicPlaylist? _playlist;
    private VoiceHandle _musicHandle;
    private long _musicIssued;
    private double _musicGapUntil = double.NegativeInfinity;
    private bool _musicDead;
    private string? _musicTrack;
    private long _musicStarted, _musicFailed, _musicEnded;

    private VoiceHandle _unitVoice;
    private VoiceHandle _evaVoice;
    private long _evaIssued;
    private bool _disposed;

    public AudioDirector(AudioCatalog catalog, FxCatalog fx, EvaCatalog eva, Func<string, Stream> open, SoftwareMixer mixer, AudioDirectorOptions? options = null,
        AudioSettings? settings = null, bool ownsMixer = false)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fx = fx ?? throw new ArgumentNullException(nameof(fx));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        _ownsMixer = ownsMixer;
        _options = options ?? new AudioDirectorOptions();
        _settings = settings ?? GameSettings.Default.Audio;
        _rng = new SeededRng(_options.Seed);
        _cache = new SoundCache(open);
        _limiter = new SoundCueRateLimiter(_options.CueLimits);
        _eva = new EvaAnnouncer(eva ?? throw new ArgumentNullException(nameof(eva)), new SeededRng(_options.Seed ^ 0xE7A));
        var s = catalog.Settings;
        _uiRatio = s.DefaultSoundVolume > 0f ? s.Effective2DSoundVolume / s.DefaultSoundVolume : 1f;
        Array.Fill(_sentBuses, float.NaN);
        SyncBuses();
    }

    /// <summary>Builds the mixer from <c>AudioSettings.ini</c> and loads <c>Eva.ini</c>; the director owns the mixer it creates.</summary>
    public static AudioDirector Create(GameData data, AudioDirectorOptions? options = null, AudioSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new AudioDirectorOptions();
        EvaCatalog eva;
        using (var stream = data.Content.OpenRead(EvaCatalog.DefaultVirtualPath)) eva = EvaCatalog.Parse(stream);
        var mixer = new SoftwareMixer(MixerOptions.FromSettings(data.Audio.Settings, options.Seed));
        return new AudioDirector(data.Audio, data.Fx, eva, data.Content.OpenRead, mixer, options, settings, ownsMixer: true);
    }

    public SoftwareMixer Mixer => _mixer;
    public bool IsPaused => _paused;
    public AudioListener Listener => new(_listenerPosition, _listenerForward, Vector3.UnitZ);

    /// <summary>
    /// Silences the whole output (master volume 0) without touching the slider values; everything keeps being requested, mixed and counted,
    /// so a muted run has the same counters as an audible one.
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value) return;
            _muted = value;
            SyncBuses();
        }
    }

    public AudioDirectorStats Stats => new(
        AllSources.ToImmutableSortedDictionary(source => source.ToString(), SourceCounters, StringComparer.Ordinal),
        _startedEvents.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase),
        _outcomes.ToImmutableSortedDictionary(StringComparer.Ordinal),
        _unresolved.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase),
        _missingVoices.ToImmutableSortedDictionary(StringComparer.Ordinal),
        _movies.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase),
        _eva.UnknownEvents,
        _cuesReceived, _uiPlayed, _unitVoicesPlayed,
        _eva.Requested, _eva.Played, _eva.Expired, _eva.SideMissing,
        _musicStarted, _musicFailed, _musicEnded, !_musicDead, _musicTrack,
        _ambientStarted, _ambient.Count, _tracked.Count,
        _cache.Count, _cache.DecodedSamples, _cache.Failures, _mixer.Statistics);

    private AudioSourceCounters SourceCounters(AudioSource source) => source switch
    {
        // The announcer keeps its own books: a request is an accepted event, a drop is one that expired, has no line for the side or no playable sound.
        AudioSource.Eva => new AudioSourceCounters(_eva.Requested, _started[(int)source], _ended[(int)source], _eva.Expired + _eva.SideMissing + _dropped[(int)source]),
        AudioSource.Music => new AudioSourceCounters(_musicStarted + _musicFailed, _musicStarted, _musicEnded, _musicFailed),
        _ => new AudioSourceCounters(_requested[(int)source], _started[(int)source], _ended[(int)source], _dropped[(int)source]),
    };

    // ---------------------------------------------------------------------------------------------- frame update

    /// <summary>
    /// The per-rendered-frame entry point: listener from the camera, pause, derived cues, Eva, music, ambient loops and bus volumes.
    /// <c>LiveMatch</c> does not forward its tick events, so unit voices, UI sounds and the insufficient-funds notice still need the
    /// explicit calls (<see cref="OnUnitSelected"/>, <see cref="OnUnitOrdered"/>, <see cref="PlayUi"/>, <see cref="NotifyInsufficientFunds"/>).
    /// </summary>
    public void Update(LiveGame live, double dt)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_disposed) return;
        var view = _liveView is not null && ReferenceEquals(_liveViewMatch, live) ? _liveView : (_liveView = new LiveAudioView(live));
        _liveViewMatch = live;
        var target = live.Camera.Target;
        SetListener(target, live.Heights.HeightAt(target.X, target.Y), live.Camera.Eye);
        SetPaused(live.Paused);
        Submit(live.DrainSoundCues(), view);
        Tick(view, dt);
        UpdateAmbient(live.Frame.Drawables, view);
    }

    private LiveAudioView? _liveView;
    private LiveGame? _liveViewMatch;

    /// <summary>
    /// Advances the director's time by <paramref name="dt"/> seconds, runs the Eva announcer (when a match view is given and the game is
    /// not paused), the music sequencer and the bus volumes.
    /// </summary>
    public void Tick(IAudioGameView? view, double dt)
    {
        if (_disposed) return;
        _now += Math.Max(0, dt);
        PruneTracked();
        if (view is not null && !_paused) UpdateEva(view);
        UpdateMusic();
        SyncBuses();
    }

    /// <summary>
    /// Places the microphone like <c>AudioManager::update</c> (<see cref="Microphone"/>) and orients it by the horizontal view direction,
    /// so that screen-right is stereo-right however the camera is rotated (the reference always uses +Y forward; an own decision).
    /// </summary>
    public void SetListener(Vector3 lookAt, float terrainHeight, Vector3 eye)
    {
        var (position, zoom) = Microphone.Place(lookAt, terrainHeight, eye, _catalog.Settings);
        var horizontal = new Vector3(lookAt.X - eye.X, lookAt.Y - eye.Y, 0f);
        var forward = horizontal.LengthSquared() > 1e-6f ? Vector3.Normalize(horizontal) : _listenerForward;
        if (!_listenerSent || Vector3.DistanceSquared(position, _listenerPosition) > 1e-4f || Vector3.DistanceSquared(forward, _listenerForward) > 1e-6f)
        {
            _listenerPosition = position;
            _listenerForward = forward;
            _listenerSent = true;
            _mixer.SetListener(new AudioListener(position, forward, Vector3.UnitZ));
        }
        _zoomVolume = zoom;
    }

    /// <summary>
    /// Pausing cuts every world sound (short fade) and ducks the music; UI sounds, unit voices and Eva played while paused still sound.
    /// The reference pauses the audio engine per affect group (<c>AudioManager::pauseAudio</c>); a mixer without pause state cuts instead.
    /// </summary>
    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        if (paused)
        {
            foreach (var voice in _tracked)
            {
                _mixer.Stop(voice.Handle, _options.PauseFadeSeconds);
                _ended[(int)voice.Source]++;
            }
            _tracked.Clear();
            foreach (var entry in _ambient.Values)
            {
                _mixer.Stop(entry.Handle, _options.PauseFadeSeconds);
                if (entry.Handle.IsValid) _ended[(int)AudioSource.Ambient]++;
            }
            _ambient.Clear();
        }
        SyncBuses();
    }

    /// <summary>Applies new volume settings (master and bus sliders of the options screen) to the mixer.</summary>
    public void ApplySettings(AudioSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        SyncBuses();
    }

    // ---------------------------------------------------------------------------------------------- cues

    /// <summary>Resolves and plays derived cues (see <see cref="SoundCue"/>); while paused they are discarded and counted.</summary>
    public void Submit(ImmutableArray<SoundCue> cues, IAudioGameView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (cues.IsDefaultOrEmpty || _disposed) return;
        _cuesReceived += cues.Length;
        _requested[(int)AudioSource.Simulation] += cues.Length;
        if (_paused)
        {
            Drop(AudioSource.Simulation, AudioOutcomes.Paused, cues.Length);
            return;
        }
        var before = _limiter.DroppedByRate + _limiter.DroppedByFrameCap + _limiter.DroppedByDistance;
        var accepted = _limiter.Filter(cues, view.Clock.TicksPerOriginalFrame, _listenerPosition);
        Drop(AudioSource.Simulation, AudioOutcomes.RateLimited, _limiter.DroppedByRate + _limiter.DroppedByFrameCap + _limiter.DroppedByDistance - before);
        foreach (var cue in accepted) Dispatch(cue, view);
    }

    private void Dispatch(SoundCue cue, IAudioGameView view)
    {
        switch (cue.Kind)
        {
            case SoundCueKind.Eva:
                // An accepted request is neither a start nor a drop of the simulation source: the announcer's own counters (source Eva) continue it.
                if (cue.Player != view.Human) Drop(AudioSource.Simulation, AudioOutcomes.EvaOtherPlayer);
                else if (_eva.Request(cue.EventName)) Count(AudioOutcomes.EvaRequested);
                else Drop(AudioSource.Simulation, AudioOutcomes.EvaUnknown);
                return;
            case SoundCueKind.Music:
                Drop(AudioSource.Simulation, AudioOutcomes.MusicCueUnsupported);
                return;
            case SoundCueKind.UiSound when !cue.Player.IsSystem && cue.Player != view.Human:
                Drop(AudioSource.Simulation, AudioOutcomes.UiOtherPlayer);
                return;
        }

        var info = _catalog.Find(cue.EventName);
        if (info is null)
        {
            _unresolved[cue.EventName] = _unresolved.GetValueOrDefault(cue.EventName) + 1;
            Drop(AudioSource.Simulation, AudioOutcomes.Unresolved);
            return;
        }
        if (cue.Kind != SoundCueKind.UiSound)
        {
            var local = ShouldPlayLocally(info, cue.Player, view);
            if (local is not null)
            {
                Drop(AudioSource.Simulation, local);
                return;
            }
        }

        Vector3? position = null;
        if ((info.SoundTypes & SoundTypeFlags.World) != 0 && cue.Kind != SoundCueKind.UiSound)
        {
            if (cue.Position is { } at) position = at;
            else Count(AudioOutcomes.WorldWithoutPosition);
        }
        if (position is { } where && (info.SoundTypes & SoundTypeFlags.Shrouded) == 0 && view.IsShrouded(where))
        {
            Drop(AudioSource.Simulation, AudioOutcomes.Shrouded);
            return;
        }
        if (position is { } heard && CullReason(info, heard) is { } culled)
        {
            Drop(AudioSource.Simulation, culled);
            return;
        }

        Play(AudioSource.Simulation, info, position, cue.Kind == SoundCueKind.Voice ? AudioBus.Voice : null, null);
    }

    /// <summary>Starts the event for <paramref name="source"/> and books the result: a started voice is tracked until the mixer drops it, anything else is a counted drop.</summary>
    private VoiceHandle Play(AudioSource source, AudioEventInfo info, Vector3? position, AudioBus? bus, bool? streaming, bool track = true)
        => Play(source, info, position, bus, streaming, track, out _);

    private VoiceHandle Play(AudioSource source, AudioEventInfo info, Vector3? position, AudioBus? bus, bool? streaming, bool track, out float shift)
    {
        var (handle, outcome, started) = Start(info, position, bus, streaming);
        shift = started;
        if (!handle.IsValid)
        {
            Drop(source, outcome);
            return handle;
        }
        Count(outcome);
        _started[(int)source]++;
        _startedEvents[info.Name] = _startedEvents.GetValueOrDefault(info.Name) + 1;
        if (track) _tracked.Add(new Tracked(handle, _mixer.Statistics.RenderedFrames, source, info.Name, started));
        return handle;
    }

    private void Drop(AudioSource source, string outcome, long amount = 1)
    {
        if (amount <= 0) return;
        _dropped[(int)source] += amount;
        Count(outcome, amount);
    }

    /// <summary>
    /// <c>AudioManager::shouldPlayLocally</c> (<c>GameAudio.cpp</c>): null when the local player hears the sound, else the outcome name.
    /// Observer mode is not modelled, the human player always is the local player.
    /// </summary>
    private static string? ShouldPlayLocally(AudioEventInfo info, PlayerId owner, IAudioGameView view)
    {
        if (info.Type == AudioType.Music) return null;
        const SoundTypeFlags restrictions = SoundTypeFlags.Player | SoundTypeFlags.Allies | SoundTypeFlags.Enemies | SoundTypeFlags.Everyone;
        var types = info.SoundTypes;
        if ((types & restrictions) == 0 || (types & SoundTypeFlags.Everyone) != 0) return null;
        if ((types & SoundTypeFlags.Player) != 0 && (types & SoundTypeFlags.Ui) != 0 && owner.IsSystem) return null;
        if (owner.IsSystem) return AudioOutcomes.NoOwner;
        if ((types & SoundTypeFlags.Player) != 0) return owner == view.Human ? null : AudioOutcomes.NotLocal;
        if ((types & SoundTypeFlags.Allies) != 0)
            return owner != view.Human && view.RelationshipOf(owner, view.Human) == Relationship.Allies ? null : AudioOutcomes.NotLocal;
        return view.RelationshipOf(owner, view.Human) == Relationship.Enemies ? null : AudioOutcomes.NotLocal;
    }

    /// <summary>Distance and quietness test before the sound is decoded; the mixer repeats it for voices already playing.</summary>
    private string? CullReason(AudioEventInfo info, Vector3 position)
    {
        var global = (info.SoundTypes & SoundTypeFlags.Global) != 0;
        if (global || info.Priority == AudioPriority.Critical) return null;
        var distance = Vector3.Distance(position, _listenerPosition);
        if (info.MaxRange > 0f && distance >= info.MaxRange) return AudioOutcomes.CulledDistance;
        var gain = info.Volume * Spatial.Attenuation(_mixer.Options.DistanceModel, distance, info.MinRange, info.MaxRange);
        return gain < _mixer.Options.MinSampleVolume ? AudioOutcomes.CulledQuiet : null;
    }

    /// <summary>Picks the variation, decodes the sounds and hands the request to the mixer; the outcome name says why nothing started.</summary>
    private (VoiceHandle Handle, string Outcome, float Shift) Start(AudioEventInfo info, Vector3? position, AudioBus? bus, bool? streaming)
    {
        // A script's volume override for this event (AudioManager::addAudioEvent: the event takes the overridden volume; below the minimum it is culled as muted).
        float? volumeOverride = _eventVolumes.TryGetValue(info.Name, out var scripted) ? scripted : null;
        if (volumeOverride is { } wanted && (wanted <= 0f || wanted < _catalog.Settings.MinSampleVolume))
        {
            _scriptMuted++;
            return (VoiceHandle.None, AudioOutcomes.ScriptMuted, 1f);
        }
        if (!_states.TryGetValue(info.Name, out var state)) _states[info.Name] = state = new SoundSelectionState();
        var plan = SoundSelector.Pick(info, _rng, state, _options.TimeOfDay);
        if (plan is null) return (VoiceHandle.None, AudioOutcomes.NoSounds, 1f);
        var request = PlayRequest.ForEvent(plan, state, _catalog.Settings, _cache.GetOrLoad, position, volumeOverride);
        if (request.Source is EventVoiceSource { Buffers.IsEmpty: true }) return (VoiceHandle.None, AudioOutcomes.DecodeFailed, 1f);
        if (bus is { } target) request = request with { Bus = target };
        if (streaming is { } stream) request = request with { Streaming = stream };
        return (_mixer.Play(request), AudioOutcomes.Played, plan.VolumeShift);
    }

    /// <summary>
    /// A voice counts as playing until the mixer has rendered a tenth of a second after the request: its membership is published only after
    /// the render that applied the command, which may be the one after the request was queued.
    /// </summary>
    private bool IsLive(VoiceHandle handle, long issuedFrames)
    {
        if (!handle.IsValid) return false;
        var rendered = _mixer.Statistics.RenderedFrames;
        return rendered < issuedFrames + _mixer.SampleRate / 10 || _mixer.IsActive(handle);
    }

    private void PruneTracked() => _tracked.RemoveAll(voice =>
    {
        if (IsLive(voice.Handle, voice.IssuedFrames)) return false;
        _ended[(int)voice.Source]++;
        return true;
    });

    private void Count(string outcome, long amount = 1)
    {
        if (amount > 0) _outcomes[outcome] = _outcomes.GetValueOrDefault(outcome) + amount;
    }

    // ---------------------------------------------------------------------------------------------- UI, units, Eva

    /// <summary>Plays a non-positional event by name (button clicks, <c>MiscAudio</c> hooks); false and counted when it does not exist or has no sound.</summary>
    public bool PlayUi(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (_disposed) return false;
        _requested[(int)AudioSource.Ui]++;
        var info = _catalog.Find(eventName);
        if (info is null)
        {
            _unresolved[eventName] = _unresolved.GetValueOrDefault(eventName) + 1;
            Drop(AudioSource.Ui, AudioOutcomes.Unresolved);
            return false;
        }
        if (!Play(AudioSource.Ui, info, null, null, null).IsValid) return false;
        _uiPlayed++;
        return true;
    }

    /// <summary>
    /// The acknowledgement of a selection: <c>VoiceGroupSelect</c> for several units when the template has it, else <c>VoiceSelect</c>
    /// (<c>ControlBar</c>/<c>SelectionXlat</c> of the reference).
    /// </summary>
    public bool OnUnitSelected(string template, int selectedCount)
    {
        var info = _fx.FindObject(template);
        var key = selectedCount > 1 && info?.Sound("VoiceGroupSelect") is not null ? "VoiceGroupSelect" : "VoiceSelect";
        return PlayUnitVoice(template, key);
    }

    public bool OnUnitOrdered(string template, UnitVoiceKind kind) => PlayUnitVoice(template, kind switch
    {
        UnitVoiceKind.Move => "VoiceMove",
        UnitVoiceKind.Attack => "VoiceAttack",
        UnitVoiceKind.Guard => "VoiceGuard",
        UnitVoiceKind.Enter => "VoiceEnter",
        UnitVoiceKind.Garrison => "VoiceGarrison",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

    private bool PlayUnitVoice(string template, string key)
    {
        if (_disposed) return false;
        _requested[(int)AudioSource.UnitVoice]++;
        var name = _fx.FindObject(template)?.Sound(key);
        if (name is null)
        {
            _missingVoices[key] = _missingVoices.GetValueOrDefault(key) + 1;
            Drop(AudioSource.UnitVoice, AudioOutcomes.MissingVoiceKey);
            return false;
        }
        var info = _catalog.Find(name);
        if (info is null)
        {
            _unresolved[name] = _unresolved.GetValueOrDefault(name) + 1;
            Drop(AudioSource.UnitVoice, AudioOutcomes.Unresolved);
            return false;
        }
        // One acknowledgement at a time: the new line cuts the previous one of the same player (only one unit "speaks").
        var handle = Play(AudioSource.UnitVoice, info, null, AudioBus.Voice, true);
        if (!handle.IsValid) return false;
        if (_unitVoice.IsValid) _mixer.Stop(_unitVoice, _options.UnitVoiceFadeSeconds);
        _unitVoice = handle;
        _unitVoicesPlayed++;
        return true;
    }

    /// <summary>The build queue refused an order for lack of money (<c>EVA_InsufficientFunds</c> of <c>Player::buildingDeniedByFunds</c>).</summary>
    public void NotifyInsufficientFunds()
    {
        if (!_eva.Request("InsufficientFunds")) Count(AudioOutcomes.EvaUnknown);
        else Count(AudioOutcomes.EvaRequested);
    }

    private void UpdateEva(IAudioGameView view)
    {
        // Once per original frame of game time: the announcer counts logic frames of the original (its waits in Eva.ini are 1/30 s units), and a render frame may step
        // several ticks (the catch-up of LiveGame) or, at a higher tick rate, less than one original frame.
        var frame = view.Clock.OriginalFrames;
        if (frame == _lastEvaFrame) return;
        _lastEvaFrame = frame;
        var speaking = IsLive(_evaVoice, _evaIssued);
        var dialog = _eva.Update(frame, view.LowPower, speaking, view.HumanSide);
        if (dialog is null) return;
        var info = _catalog.Find(dialog);
        if (info is null)
        {
            _unresolved[dialog] = _unresolved.GetValueOrDefault(dialog) + 1;
            Drop(AudioSource.Eva, AudioOutcomes.Unresolved);
            return;
        }
        var handle = Play(AudioSource.Eva, info, null, AudioBus.Voice, true);
        if (!handle.IsValid) return;
        _evaVoice = handle;
        _evaIssued = _mixer.Statistics.RenderedFrames;
    }

    /// <summary>Cutscene movies are Bink video with an audio track; no decoder exists in the runtime, so the request is counted and skipped.</summary>
    public void OnMovieRequested(string movieName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(movieName);
        _movies[movieName] = _movies.GetValueOrDefault(movieName) + 1;
    }

    // ---------------------------------------------------------------------------------------------- ambient

    /// <summary>
    /// Drawable ambient loops (<c>SoundAmbient</c> of the object, <c>Drawable::startAmbientSound</c>): the nearest
    /// <see cref="AudioDirectorOptions.MaxAmbientVoices"/> audible drawables keep a looping positional voice on the ambient bus; it follows
    /// the drawable and is faded out when the drawable leaves the range, the fog or the ranking. Evaluated every
    /// <see cref="AudioDirectorOptions.AmbientUpdateSeconds"/>.
    /// </summary>
    public void UpdateAmbient(ImmutableArray<DrawableInstance> drawables, IAudioGameView view)
    {
        if (_disposed || _paused || _options.MaxAmbientVoices <= 0 || drawables.IsDefault) return;
        if (_now - _lastAmbient < _options.AmbientUpdateSeconds) return;
        _lastAmbient = _now;

        var candidates = new List<(float Distance, long Key, DrawableInstance Drawable, AudioEventInfo Info)>();
        foreach (var drawable in drawables)
        {
            if (drawable.IsGhost) continue;
            // SOUND_AMBIENT_PAUSE keeps every ambient loop silent; DISABLE_OBJECT_SOUND silences the loops of single named objects (Drawable::enableAmbientSoundFromScript).
            if (_ambientPaused || (drawable.Entity.IsValid && _objectSoundOff.Contains(drawable.Entity.Value))) continue;
            var info = AmbientOf(drawable.TemplateName);
            if (info is null) continue;
            // A script-disabled event is not started at all (and a running loop of it is stopped below: it no longer ranks among the wanted ones).
            if (_eventVolumes.TryGetValue(info.Name, out var scriptVolume) && (scriptVolume <= 0f || scriptVolume < _catalog.Settings.MinSampleVolume)) continue;
            var global = (info.SoundTypes & SoundTypeFlags.Global) != 0;
            var max = global ? _catalog.Settings.GlobalMaxRange : info.MaxRange;
            var distance = Vector3.Distance(drawable.Position, _listenerPosition);
            if (max > 0f && distance >= max) continue;
            if ((info.SoundTypes & SoundTypeFlags.Shrouded) == 0 && view.IsShrouded(drawable.Position)) continue;
            candidates.Add((distance, drawable.Entity.IsValid ? drawable.Entity.Value : -1L - drawable.SceneryIndex, drawable, info));
        }
        candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Key.CompareTo(b.Key));
        if (candidates.Count > _options.MaxAmbientVoices) candidates.RemoveRange(_options.MaxAmbientVoices, candidates.Count - _options.MaxAmbientVoices);

        var wanted = candidates.Select(value => value.Key).ToHashSet();
        foreach (var key in _ambient.Keys.ToArray())
        {
            var entry = _ambient[key];
            var finished = !IsLive(entry.Handle, entry.IssuedFrames);
            if (finished || !wanted.Contains(key))
            {
                if (!finished) _mixer.Stop(entry.Handle, 0.25f);
                if (entry.Handle.IsValid) _ended[(int)AudioSource.Ambient]++;
                _ambient.Remove(key);
                // A one-shot ambient event is triggered again only after TimeBetweenDrawableSounds; remember the gap by keeping a dead entry.
                if (finished && entry.NextAllowed > _now && wanted.Contains(key)) _ambient[key] = new AmbientEntry(VoiceHandle.None, 0, entry.Position, entry.NextAllowed);
            }
        }
        foreach (var (_, key, drawable, info) in candidates)
        {
            if (_ambient.TryGetValue(key, out var existing))
            {
                if (existing.Handle.IsValid && Vector3.DistanceSquared(existing.Position, drawable.Position) > PositionChangeThreshold * PositionChangeThreshold)
                {
                    existing.Position = drawable.Position;
                    _mixer.SetVoicePosition(existing.Handle, drawable.Position);
                }
                if (existing.Handle.IsValid || existing.NextAllowed > _now) continue;
                _ambient.Remove(key);
            }
            _requested[(int)AudioSource.Ambient]++;
            var handle = Play(AudioSource.Ambient, info, drawable.Position, AudioBus.Ambient, null, false, out var ambientShift);
            if (!handle.IsValid)
            {
                // Keep the failure from being retried every 0.25 s.
                _ambient[key] = new AmbientEntry(VoiceHandle.None, 0, drawable.Position, _now + _catalog.Settings.TimeBetweenDrawableSoundsMs / 1000.0);
                continue;
            }
            _ambientStarted++;
            var next = info.IsPermanent ? double.PositiveInfinity : _now + _catalog.Settings.TimeBetweenDrawableSoundsMs / 1000.0;
            _ambient[key] = new AmbientEntry(handle, _mixer.Statistics.RenderedFrames, drawable.Position, next) { Event = info.Name, Shift = ambientShift };
        }
    }

    private AudioEventInfo? AmbientOf(string template)
    {
        if (_ambientByTemplate.TryGetValue(template, out var cached)) return cached;
        AudioEventInfo? info = null;
        if (_fx.FindObject(template)?.Sound("SoundAmbient") is { } name)
        {
            info = _catalog.Find(name);
            if (info is null) _unresolved[name] = _unresolved.GetValueOrDefault(name) + 1;
        }
        _ambientByTemplate[template] = info;
        return info;
    }

    // ---------------------------------------------------------------------------------------------- music

    /// <summary>Starts the menu theme (<c>Shell</c> of <c>Music.ini</c>) looped, cross-fading from whatever plays.</summary>
    public void PlayMenuMusic()
    {
        _musicMode = MusicMode.Menu;
        _playlist = null;
        _musicDead = false;
        var track = _catalog.Find("Shell");
        if (track is null || !StartTrack(track, loop: true)) _musicDead = true;
    }

    /// <summary>Starts the shuffled in-game tracks of <paramref name="side"/> (for example <c>America</c>); the next track follows after a gap.</summary>
    public void StartMatchMusic(string side)
    {
        _musicMode = MusicMode.Match;
        _playlist = MusicPlaylist.ForSide(_catalog, side, new SeededRng(_options.Seed ^ 0x3C51C));
        _musicDead = false;
        _musicGapUntil = double.NegativeInfinity;
        if (_playlist.UsesAllSides) Count("note:music-all-sides");
        AdvancePlaylist();
    }

    /// <summary>
    /// <c>MUSIC_SET_TRACK</c> of a mission script (<c>ScriptActions::doMusicTrackChange</c>): the named track of <c>Music.ini</c> replaces what plays; afterwards the
    /// side's playlist goes on. False (and counted as an unknown event or a failed track) when the track does not exist or does not decode.
    /// </summary>
    public bool SetMusicTrack(string trackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackName);
        if (_disposed) return false;
        var track = _catalog.Find(trackName);
        if (track is null)
        {
            _unresolved[trackName] = _unresolved.GetValueOrDefault(trackName) + 1;
            return false;
        }
        return StartTrack(track, loop: false);
    }

    public void StopMusic()
    {
        _musicMode = MusicMode.None;
        _playlist = null;
        _musicHandle = VoiceHandle.None;
        _musicTrack = null;
        _mixer.StopMusic(_options.MusicCrossfadeSeconds ?? _catalog.Settings.TimeToFadeAudioMs / 1000f);
    }

    private void UpdateMusic()
    {
        if (_musicMode != MusicMode.Match || _musicDead) return;
        if (_musicHandle.IsValid)
        {
            if (IsLive(_musicHandle, _musicIssued)) return;
            _musicEnded++;
            _musicHandle = VoiceHandle.None;
            _musicTrack = null;
            _musicGapUntil = _now + _options.MusicGapSeconds;
        }
        if (_now >= _musicGapUntil) AdvancePlaylist();
    }

    private void AdvancePlaylist()
    {
        if (_playlist is null) return;
        // A broken track is counted and skipped; when every track of a full round fails, music is marked unavailable instead of spinning.
        for (var attempt = 0; attempt < Math.Max(1, _playlist.Count); attempt++)
        {
            var track = _playlist.Next();
            if (track is null) break;
            if (StartTrack(track, loop: false)) return;
        }
        _musicDead = true;
    }

    private bool StartTrack(AudioEventInfo track, bool loop)
    {
        var path = track.File?.VirtualPath;
        if (path is null)
        {
            _musicFailed++;
            return false;
        }
        try
        {
            var opened = _open(path);
            Stream source;
            if (opened.CanSeek) source = opened;
            else
            {
                using (opened)
                {
                    var copy = new MemoryStream();
                    opened.CopyTo(copy);
                    copy.Position = 0;
                    source = copy;
                }
            }
            var stream = AudioFileDecoder.OpenStream(source);
            _musicHandle = _mixer.PlayMusic(stream, _options.MusicCrossfadeSeconds ?? _catalog.Settings.TimeToFadeAudioMs / 1000f, track.Volume, loop);
            _musicIssued = _mixer.Statistics.RenderedFrames;
            _musicTrack = track.Name;
            _musicStarted++;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or FileNotFoundException or EndOfStreamException or IOException)
        {
            _musicFailed++;
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------- volumes

    /// <summary>
    /// Bus volumes are the final values of <see cref="AudioSettings"/> (the options screen starts at the retail defaults):
    /// sound effects and ambient follow the camera zoom (<c>set3DVolumeAdjustment</c>), UI sounds carry the retail 2D/3D ratio
    /// (<c>Relative2DVolume</c>), music is ducked while paused. Only changed values reach the mixer.
    /// </summary>
    private void SyncBuses()
    {
        // The mission scripts' own volumes (AudioManager::setVolume without AudioAffect_SystemSetting) multiply the player's: music, speech, 2D sound (UI) and 3D sound (world and ambient).
        var volumes = new BusVolumes(
            Music: _settings.Music * (_paused ? _options.PausedMusicVolume : 1f) * _scriptVolumes[(int)ScriptVolumeKind.Music],
            Sfx: _settings.Effects * _zoomVolume * _scriptVolumes[(int)ScriptVolumeKind.Sound3D],
            Voice: _settings.Voice * _scriptVolumes[(int)ScriptVolumeKind.Speech],
            Ambient: _settings.Ambient * _zoomVolume * _scriptVolumes[(int)ScriptVolumeKind.Sound3D],
            Ui: _settings.Effects * _uiRatio * _scriptVolumes[(int)ScriptVolumeKind.Sound]);
        var master = _muted ? 0f : _settings.Master;
        if (float.IsNaN(_sentMaster) || Math.Abs(_sentMaster - master) > BusChangeThreshold)
        {
            _sentMaster = master;
            _mixer.SetMasterVolume(master);
        }
        foreach (var bus in Enum.GetValues<AudioBus>())
        {
            var value = volumes[bus];
            if (!float.IsNaN(_sentBuses[(int)bus]) && Math.Abs(_sentBuses[(int)bus] - value) <= BusChangeThreshold) continue;
            _sentBuses[(int)bus] = value;
            _mixer.SetBusVolume(bus, value);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mixer.StopAll();
        _tracked.Clear();
        _ambient.Clear();
        if (_ownsMixer) _mixer.Dispose();
    }
}
