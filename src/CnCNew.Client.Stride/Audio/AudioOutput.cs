using System.Diagnostics;
using CnCNew.Audio.Mixing;
using Stride.Audio;
using StrideAudioListener = Stride.Audio.AudioListener;

namespace CnCNew.Client.Stride.Audio;

/// <summary>
/// Feeds <see cref="SoftwareMixer.Render"/> to the OS sound device through Stride's native audio layer
/// (<c>Stride.Audio</c>: <see cref="AudioEngine"/>, <see cref="DynamicSoundSource"/>, <see cref="SoundInstance"/>). The
/// mixer already produces finished interleaved stereo float, so this class carries no per-voice or spatialization state:
/// it is one continuous stream, refilled from a single block of the mixer every time Stride's own streaming worker
/// thread (<see cref="DynamicSoundSource"/>'s static background task) asks for data. An own low-latency thread polls
/// <see cref="AudioEngine.Update"/>, since the main render loop that would otherwise host that call belongs to files
/// this package does not own (see <c>docs/GAME-AUDIO.md</c>).
/// <para>
/// When no audio device is available — missing native library, no hardware, or any other failure at the device
/// boundary — <see cref="Start"/> catches it once, counts it in <see cref="DeviceFailures"/>, and every other member
/// becomes a no-op: the client keeps running silently instead of crashing.
/// </para>
/// </summary>
internal sealed class AudioOutput : IDisposable
{
    /// <summary>20 ms at 44100 Hz; four of them (<see cref="BufferCount"/>) give about 80 ms of output latency.</summary>
    public const int BufferFrames = 882;
    public const int BufferCount = 4;
    private const int PumpIntervalMilliseconds = 10;

    private readonly SoftwareMixer _mixer;
    private AudioEngine? _engine;
    private StrideAudioListener? _listener;
    private MixerSoundSource? _source;
    private SoundInstance? _instance;
    private Thread? _pumpThread;
    private volatile bool _running;
    private long _deviceFailures;
    private bool _disposed;

    public AudioOutput(SoftwareMixer mixer) => _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));

    public SoftwareMixer Mixer => _mixer;

    /// <summary>True once a device was opened and the stream is playing.</summary>
    public bool IsAvailable => _instance is not null;

    /// <summary>Failed device opens (missing native library, no hardware, ...); 0 means the device came up on the first try.</summary>
    public long DeviceFailures => Interlocked.Read(ref _deviceFailures);

    /// <summary>
    /// Detected gaps in the refill cadence (see <see cref="MixerSoundSource.Underruns"/>): the streaming thread could
    /// not keep the native ring buffer fed, so the device likely played a stale or silent buffer. 0 before <see cref="Start"/>
    /// or while unavailable.
    /// </summary>
    public long Underruns => _source?.Underruns ?? 0;

    /// <summary>Opens the default audio device and starts streaming; safe to call once, a no-op if already started or already failed.</summary>
    public void Start()
    {
        if (_disposed || _running) return;
        try
        {
            _engine = new AudioEngine((uint)_mixer.SampleRate);
            _listener = new StrideAudioListener(_engine);
            _source = new MixerSoundSource(_mixer, BufferFrames, BufferCount);
            _instance = new SoundInstance(_engine, _listener, _source, _mixer.SampleRate, mono: false, spatialized: false);
            _source.BindInstance(_instance);
            _instance.Volume = 1f;
            _instance.Play();
            _running = true;
            _pumpThread = new Thread(Pump) { IsBackground = true, Name = "CnCNew Audio Pump" };
            _pumpThread.Start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AudioEngine.Logger.Warning($"No audio device available; the game will run silently ({exception.Message}).");
            Interlocked.Increment(ref _deviceFailures);
            _instance = null;
            _source = null;
            _listener?.Dispose();
            _listener = null;
            _engine?.Dispose();
            _engine = null;
            _running = false;
        }
    }

    /// <summary>Polls <see cref="AudioEngine.Update"/> at audio-buffer cadence; Stride's own worker thread fills the native ring buffer.</summary>
    private void Pump()
    {
        while (_running && !_disposed)
        {
            try
            {
                _engine?.Update();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                AudioEngine.Logger.Warning($"Audio engine update failed ({exception.Message}).");
            }
            Thread.Sleep(PumpIntervalMilliseconds);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _pumpThread?.Join(TimeSpan.FromSeconds(1));
        _instance?.Stop();
        _instance?.Dispose();
        _listener?.Dispose();
        _engine?.Dispose();
    }
}

/// <summary>
/// The <see cref="DynamicSoundSource"/> that never runs out of data: every buffer is one <see cref="SoftwareMixer.Render"/>
/// block. <see cref="BindInstance"/> closes the construction cycle that the public Stride API requires (a
/// <see cref="SoundInstance"/> needs an already-built <see cref="DynamicSoundSource"/>, and the base constructor of this
/// class only needs the instance to store as a field, never to dereference it) — <see cref="AudioOutput.Start"/>
/// constructs this with a null instance, builds the real <see cref="SoundInstance"/> from it, then hands it back here.
/// </summary>
internal sealed class MixerSoundSource : DynamicSoundSource
{
    private readonly SoftwareMixer _mixer;
    private readonly float[] _floatScratch;
    private readonly short[] _shortScratch;
    private readonly double _bufferSeconds;
    private readonly int _numberOfBuffers;
    private readonly Stopwatch _clock = new();
    private long _framesFilled;
    private long _underruns;

    public MixerSoundSource(SoftwareMixer mixer, int bufferFrames, int numberOfBuffers)
        : base(null!, numberOfBuffers, bufferFrames * 2 * sizeof(short))
    {
        _mixer = mixer;
        _floatScratch = new float[bufferFrames * 2];
        _shortScratch = new short[bufferFrames * 2];
        _bufferSeconds = (double)bufferFrames / mixer.SampleRate;
        _numberOfBuffers = numberOfBuffers;
        NewSources.Add(this);
    }

    /// <summary>Detected refill gaps: a call landed more than one buffer duration late, so the device likely starved in between.</summary>
    public long Underruns => Interlocked.Read(ref _underruns);

    public override int MaxNumberOfBuffers => _numberOfBuffers;

    /// <summary>Attaches the <see cref="SoundInstance"/> built from this source (see the class summary); called exactly once, before <see cref="SoundInstance.Play"/>.</summary>
    public void BindInstance(SoundInstance instance) => soundInstance = instance ?? throw new ArgumentNullException(nameof(instance));

    public override void SetLooped(bool looped)
    {
        // The mixer's own music sequencing decides looping (CnCNew.Game.Audio.AudioDirector); the output stream never loops as a whole.
    }

    protected override void ExtractAndFillData()
    {
        if (!_clock.IsRunning) _clock.Start();
        var expected = _framesFilled / (double)_mixer.SampleRate;
        var actual = _clock.Elapsed.TotalSeconds;
        if (_framesFilled > 0 && actual - expected > _bufferSeconds)
        {
            Interlocked.Increment(ref _underruns);
            _clock.Restart();
            _framesFilled = 0;
        }

        var frames = _mixer.Render(_floatScratch);
        var samples = frames * 2;
        for (var index = 0; index < samples; index++)
            _shortScratch[index] = (short)Math.Clamp(MathF.Round(_floatScratch[index] * short.MaxValue), short.MinValue, short.MaxValue);
        _framesFilled += frames;
        FillBuffer(_shortScratch, samples * sizeof(short), AudioLayer.BufferType.None);
    }
}
