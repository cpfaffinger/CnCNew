using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Selection;

namespace CnCNew.Audio.Mixing;

/// <summary>
/// Device-independent software mixer producing interleaved stereo float PCM.
/// <para>Threading: the control methods (<see cref="Play"/>, <see cref="Stop"/>, <see cref="SetListener"/>, …) only
/// enqueue commands into a lock-free queue and may be called from any one game thread; <see cref="Render"/> must be
/// called from a single audio thread, applies all queued commands at the start of the block and then mixes. Read-only
/// properties (<see cref="ActiveVoiceCount"/>, peaks, <see cref="Statistics"/>, <see cref="IsActive"/>) are snapshots
/// published at the end of each <see cref="Render"/>.</para>
/// <para>Determinism: the mixer has no thread, device or clock of its own. For the same sequence of commands and
/// <see cref="Render"/> calls (same block sizes) the output is bit-identical on the same machine and runtime.</para>
/// </summary>
public sealed class SoftwareMixer : IDisposable
{
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly List<Voice> _voices = [];
    private readonly List<Voice> _music = [];
    private readonly SeededRng _rng;
    private long _nextHandle;
    private long _nextOrder;
    private AudioListener _listener = AudioListener.Default;
    private BusVolumes _buses;
    private float _master = 1f;
    private bool _membershipChanged = true;
    private long _started, _finished, _stopped, _stolen, _replaced, _rejectedDistance, _rejectedMuted, _rejectedLimit, _rejectedNoChannel, _culled, _clipped, _rendered;

    private volatile MixerStatistics _statistics = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private volatile ImmutableHashSet<long> _active = [];
    private volatile int _activeVoices;
    private volatile int _activeMusic;
    private float _peakLeft;
    private float _peakRight;

    public SoftwareMixer(MixerOptions? options = null)
    {
        Options = options ?? new MixerOptions();
        if (Options.SampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(options), "The output sample rate must be positive.");
        if (Options.FlatVoices < 0 || Options.PositionalVoices < 0 || Options.StreamVoices < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Voice limits must not be negative.");
        _buses = Options.Buses ?? new BusVolumes();
        _rng = new SeededRng(Options.Seed);
    }

    public MixerOptions Options { get; }
    public int SampleRate => Options.SampleRate;

    /// <summary>Sound voices (not music) alive after the last <see cref="Render"/>.</summary>
    public int ActiveVoiceCount => _activeVoices;
    public int ActiveMusicCount => _activeMusic;
    public float PeakLeft => Volatile.Read(ref _peakLeft);
    public float PeakRight => Volatile.Read(ref _peakRight);
    public MixerStatistics Statistics => _statistics;

    /// <summary>True while the voice or music track is alive; queued requests count only after the next <see cref="Render"/>.</summary>
    public bool IsActive(VoiceHandle handle) => _active.Contains(handle.Value);

    // ------------------------------------------------------------------------------------------ control (game thread)

    public VoiceHandle Play(PlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pitch <= 0f || !float.IsFinite(request.Pitch)) throw new ArgumentOutOfRangeException(nameof(request), "Pitch must be positive.");
        if (request.LoopCount < 0) throw new ArgumentOutOfRangeException(nameof(request), "LoopCount must not be negative.");
        var handle = new VoiceHandle(Interlocked.Increment(ref _nextHandle));
        _commands.Enqueue(new PlayCommand(handle, request));
        return handle;
    }

    public void Stop(VoiceHandle handle, float fadeOutSeconds = 0f) => _commands.Enqueue(new StopCommand(handle, fadeOutSeconds));
    public void SetVoicePosition(VoiceHandle handle, Vector3 position) => _commands.Enqueue(new PositionCommand(handle, position));
    public void SetListener(AudioListener listener) => _commands.Enqueue(new ListenerCommand(listener ?? throw new ArgumentNullException(nameof(listener))));
    public void SetBusVolume(AudioBus bus, float volume) => _commands.Enqueue(new BusCommand(bus, Math.Max(0f, volume)));
    public void SetMasterVolume(float volume) => _commands.Enqueue(new MasterCommand(Math.Max(0f, volume)));
    public void StopAll(float fadeOutSeconds = 0f) => _commands.Enqueue(new StopAllCommand(fadeOutSeconds));

    /// <summary>
    /// Starts a music track and cross-fades from the current one over <paramref name="crossfadeSeconds"/> (0 cuts).
    /// The mixer owns <paramref name="stream"/> and disposes it when the track ends or is replaced.
    /// </summary>
    public VoiceHandle PlayMusic(IPcmStream stream, float crossfadeSeconds = 0f, float volume = 1f, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var handle = new VoiceHandle(Interlocked.Increment(ref _nextHandle));
        _commands.Enqueue(new MusicCommand(handle, stream, Math.Max(0f, crossfadeSeconds), volume, loop));
        return handle;
    }

    public void StopMusic(float fadeOutSeconds = 0f) => _commands.Enqueue(new StopMusicCommand(fadeOutSeconds));

    // ------------------------------------------------------------------------------------------ render (audio thread)

    /// <summary>Mixes the next block into <paramref name="interleavedStereo"/> (overwritten) and returns the frame count.</summary>
    public int Render(Span<float> interleavedStereo)
    {
        if (interleavedStereo.Length % 2 != 0) throw new ArgumentException("The buffer must hold whole stereo frames.", nameof(interleavedStereo));
        var frames = interleavedStereo.Length / 2;
        while (_commands.TryDequeue(out var command))
            Apply(command);

        interleavedStereo.Clear();
        if (frames > 0)
        {
            MixAll(_voices, interleavedStereo, frames);
            MixAll(_music, interleavedStereo, frames);
        }

        float peakLeft = 0f, peakRight = 0f;
        for (var index = 0; index < interleavedStereo.Length; index += 2)
        {
            var left = interleavedStereo[index] * _master;
            var right = interleavedStereo[index + 1] * _master;
            peakLeft = Math.Max(peakLeft, Math.Abs(left));
            peakRight = Math.Max(peakRight, Math.Abs(right));
            if (left is > 1f or < -1f) { _clipped++; left = Math.Clamp(left, -1f, 1f); }
            if (right is > 1f or < -1f) { _clipped++; right = Math.Clamp(right, -1f, 1f); }
            interleavedStereo[index] = left;
            interleavedStereo[index + 1] = right;
        }
        _rendered += frames;
        Publish(peakLeft, peakRight);
        return frames;
    }

    public void Dispose()
    {
        foreach (var voice in _voices.Concat(_music)) voice.Resampler.Dispose();
        _voices.Clear();
        _music.Clear();
        while (_commands.TryDequeue(out var command))
            if (command is MusicCommand music) music.Stream.Dispose();
    }

    private void Publish(float peakLeft, float peakRight)
    {
        Volatile.Write(ref _peakLeft, peakLeft);
        Volatile.Write(ref _peakRight, peakRight);
        _activeVoices = _voices.Count;
        _activeMusic = _music.Count;
        if (_membershipChanged)
        {
            _active = _voices.Concat(_music).Select(voice => voice.Handle.Value).ToImmutableHashSet();
            _membershipChanged = false;
        }
        _statistics = new MixerStatistics(_started, _finished, _stopped, _stolen, _replaced, _rejectedDistance, _rejectedMuted,
            _rejectedLimit, _rejectedNoChannel, _culled, _clipped, _rendered);
    }

    private void Apply(Command command)
    {
        switch (command)
        {
            case PlayCommand play: Admit(play.Handle, play.Request); break;
            case StopCommand stop:
                foreach (var voice in _voices.Concat(_music).Where(value => value.Handle == stop.Handle).ToArray())
                    StopVoice(voice, stop.FadeOutSeconds);
                break;
            case PositionCommand position:
                foreach (var voice in _voices.Where(value => value.Handle == position.Handle && value.Position is not null))
                    voice.Position = position.Position;
                break;
            case ListenerCommand listener: _listener = listener.Listener; break;
            case BusCommand bus: _buses = _buses.With(bus.Bus, bus.Volume); break;
            case MasterCommand master: _master = master.Volume; break;
            case StopAllCommand all:
                foreach (var voice in _voices.Concat(_music).ToArray()) StopVoice(voice, all.FadeOutSeconds);
                break;
            case MusicCommand music: StartMusic(music); break;
            case StopMusicCommand stopMusic:
                foreach (var voice in _music.ToArray()) StopVoice(voice, stopMusic.FadeOutSeconds);
                break;
        }
    }

    private void StopVoice(Voice voice, float fadeOutSeconds)
    {
        if (voice.Stopping) return;
        _stopped++;
        var fadeFrames = (int)(fadeOutSeconds * Options.SampleRate);
        if (fadeFrames <= 0 || voice.Envelope <= 0f)
        {
            Remove(voice);
            return;
        }
        voice.Stopping = true;
        voice.EnvelopeStep = -voice.Envelope / fadeFrames;
    }

    private void Remove(Voice voice)
    {
        (voice.Pool is null ? _music : _voices).Remove(voice);
        voice.Resampler.Dispose();
        _membershipChanged = true;
    }

    /// <summary>
    /// Admission after <c>SoundManager::canPlayNow</c>: distance cull, limit (<c>doesViolateLimit</c>), free channel,
    /// else steal the lowest-priority voice below the request (<c>findLowestPrioritySound</c>, oldest first on ties),
    /// else replace an instance of the same INTERRUPT event, else drop. Requests below the minimum volume are muted
    /// (<c>AudioManager::addAudioEvent</c>).
    /// </summary>
    private void Admit(VoiceHandle handle, PlayRequest request)
    {
        if (request.Volume < Options.MinSampleVolume)
        {
            _rejectedMuted++;
            return;
        }
        var pool = request.Pool;
        if (pool == VoicePool.Positional && request.Cullable && request.MaxDistance > 0f
            && Vector3.Distance(request.Position!.Value, _listener.Position) >= request.MaxDistance)
        {
            _rejectedDistance++;
            return;
        }

        if (request.LimitGroup is not null && request.Limit > 0)
        {
            var same = _voices.Where(value => value.Pool == pool && request.LimitGroup.Equals(value.LimitGroup, StringComparison.OrdinalIgnoreCase)).ToArray();
            switch (SoundSelector.CheckLimit(request.Limit, request.Interrupt, same.Length))
            {
                case LimitDecision.Reject:
                    _rejectedLimit++;
                    return;
                case LimitDecision.PlayAndStopOldest:
                    Remove(same[0]);
                    _replaced++;
                    break;
            }
        }

        var capacity = pool switch
        {
            VoicePool.Flat => Options.FlatVoices,
            VoicePool.Positional => Options.PositionalVoices,
            _ => Options.StreamVoices,
        };
        if (_voices.Count(value => value.Pool == pool) >= capacity)
        {
            var victim = _voices.Where(value => value.Pool == pool && value.Priority < request.Priority)
                .OrderBy(value => value.Priority).ThenBy(value => value.Order).FirstOrDefault();
            if (victim is null && request.Interrupt && request.LimitGroup is not null)
                victim = _voices.FirstOrDefault(value => value.Pool == pool && request.LimitGroup.Equals(value.LimitGroup, StringComparison.OrdinalIgnoreCase));
            if (victim is null || capacity == 0)
            {
                _rejectedNoChannel++;
                return;
            }
            Remove(victim);
            _stolen++;
        }

        var segments = request.Source switch
        {
            BufferVoiceSource buffer => SegmentFrameSource.ForBuffer(buffer.Buffer, request.LoopCount, request.DelayMilliseconds, Options.SampleRate),
            EventVoiceSource source => SegmentFrameSource.ForEvent(source, request.LoopCount, _rng, Options.SampleRate),
            _ => throw new NotSupportedException($"Unknown voice source {request.Source.GetType().Name}."),
        };
        var voice = new Voice(handle, _nextOrder++, pool, request.Bus, request.Priority, request.LimitGroup,
            new Resampler(new SegmentFrameSource(segments, Options.SampleRate)))
        {
            Position = request.Position,
            Volume = request.Volume,
            Pitch = request.Pitch,
            MinDistance = request.MinDistance,
            MaxDistance = request.MaxDistance,
            Cullable = request.Cullable,
        };
        StartEnvelope(voice, request.FadeInSeconds);
        _voices.Add(voice);
        _started++;
        _membershipChanged = true;
    }

    private void StartMusic(MusicCommand command)
    {
        foreach (var current in _music.ToArray()) StopVoice(current, command.CrossfadeSeconds);
        var voice = new Voice(command.Handle, _nextOrder++, null, AudioBus.Music, AudioPriority.Critical, null,
            new Resampler(new StreamFrameSource(command.Stream, command.Loop)))
        {
            Volume = command.Volume,
            Cullable = false,
        };
        StartEnvelope(voice, command.CrossfadeSeconds);
        _music.Add(voice);
        _started++;
        _membershipChanged = true;
    }

    private void StartEnvelope(Voice voice, float fadeInSeconds)
    {
        var fadeFrames = (int)(fadeInSeconds * Options.SampleRate);
        if (fadeFrames <= 0) return;
        voice.Envelope = 0f;
        voice.EnvelopeStep = 1f / fadeFrames;
    }

    private void MixAll(List<Voice> voices, Span<float> output, int frames)
    {
        var index = 0;
        while (index < voices.Count)
        {
            var voice = voices[index];
            var cull = voice.Pool == VoicePool.Positional && voice.Cullable && Options.MinSampleVolume > 0f
                && voice.Volume * Attenuate(voice) < Options.MinSampleVolume;
            if (cull) _culled++;
            if (cull || !Mix(voice, output, frames))
            {
                voices.RemoveAt(index);
                voice.Resampler.Dispose();
                _membershipChanged = true;
                continue;
            }
            index++;
        }
    }

    private float Attenuate(Voice voice)
        => voice.Position is { } position
            ? Spatial.Attenuation(Options.DistanceModel, Vector3.Distance(position, _listener.Position), voice.MinDistance, voice.MaxDistance)
            : 1f;

    /// <summary>Mixes one voice; gains ramp linearly across the block to avoid zipper noise. Returns false when it ended.</summary>
    private bool Mix(Voice voice, Span<float> output, int frames)
    {
        var gain = voice.Volume * _buses[voice.Bus];
        float targetLeft, targetRight;
        if (voice.Position is { } position)
        {
            var (panLeft, panRight) = Spatial.EqualPowerPan(Spatial.Pan(_listener, position));
            var attenuation = Attenuate(voice);
            targetLeft = gain * attenuation * panLeft;
            targetRight = gain * attenuation * panRight;
        }
        else
            targetLeft = targetRight = gain;
        if (!voice.HasGains)
        {
            voice.GainLeft = targetLeft;
            voice.GainRight = targetRight;
            voice.HasGains = true;
        }

        var gainLeft = voice.GainLeft;
        var gainRight = voice.GainRight;
        var stepLeft = (targetLeft - gainLeft) / frames;
        var stepRight = (targetRight - gainRight) / frames;
        var positional = voice.Position is not null;
        var alive = true;
        for (var frame = 0; frame < frames; frame++)
        {
            if (!voice.Resampler.Next(voice.Pitch, Options.SampleRate, out var left, out var right))
            {
                _finished++;
                alive = false;
                break;
            }
            gainLeft += stepLeft;
            gainRight += stepRight;
            var envelope = voice.Envelope;
            if (voice.EnvelopeStep != 0f)
            {
                envelope += voice.EnvelopeStep;
                if (voice.EnvelopeStep > 0f && envelope >= 1f)
                {
                    envelope = 1f;
                    voice.EnvelopeStep = 0f;
                }
                else if (voice.EnvelopeStep < 0f && envelope <= 0f)
                {
                    envelope = 0f;
                    voice.EnvelopeStep = 0f;
                }
                voice.Envelope = envelope;
            }
            if (positional)
            {
                var mono = (left + right) * 0.5f * envelope;
                output[2 * frame] += mono * gainLeft;
                output[2 * frame + 1] += mono * gainRight;
            }
            else
            {
                output[2 * frame] += left * gainLeft * envelope;
                output[2 * frame + 1] += right * gainRight * envelope;
            }
            if (voice.Stopping && envelope <= 0f)
            {
                alive = false;
                break;
            }
        }
        voice.GainLeft = targetLeft;
        voice.GainRight = targetRight;
        return alive;
    }

    private sealed class Voice(VoiceHandle handle, long order, VoicePool? pool, AudioBus bus, AudioPriority priority, string? limitGroup, Resampler resampler)
    {
        public VoiceHandle Handle { get; } = handle;
        public long Order { get; } = order;
        public VoicePool? Pool { get; } = pool;
        public AudioBus Bus { get; } = bus;
        public AudioPriority Priority { get; } = priority;
        public string? LimitGroup { get; } = limitGroup;
        public Resampler Resampler { get; } = resampler;
        public Vector3? Position { get; set; }
        public float Volume { get; init; }
        public float Pitch { get; init; } = 1f;
        public float MinDistance { get; init; }
        public float MaxDistance { get; init; }
        public bool Cullable { get; init; }
        public float Envelope { get; set; } = 1f;
        public float EnvelopeStep { get; set; }
        public bool Stopping { get; set; }
        public float GainLeft { get; set; }
        public float GainRight { get; set; }
        public bool HasGains { get; set; }
    }

    private abstract record Command;
    private sealed record PlayCommand(VoiceHandle Handle, PlayRequest Request) : Command;
    private sealed record StopCommand(VoiceHandle Handle, float FadeOutSeconds) : Command;
    private sealed record PositionCommand(VoiceHandle Handle, Vector3 Position) : Command;
    private sealed record ListenerCommand(AudioListener Listener) : Command;
    private sealed record BusCommand(AudioBus Bus, float Volume) : Command;
    private sealed record MasterCommand(float Volume) : Command;
    private sealed record StopAllCommand(float FadeOutSeconds) : Command;
    private sealed record MusicCommand(VoiceHandle Handle, IPcmStream Stream, float CrossfadeSeconds, float Volume, bool Loop) : Command;
    private sealed record StopMusicCommand(float FadeOutSeconds) : Command;
}
