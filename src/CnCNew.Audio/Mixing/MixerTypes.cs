using System.Numerics;
using CnCNew.Audio.Catalog;

namespace CnCNew.Audio.Mixing;

/// <summary>
/// Volume groups. The reference has four (<c>AudioAffect</c>: music, sound, sound 3D, speech); <see cref="Ambient"/> is
/// an extra group for drawable ambient loops so that the client can lower them independently.
/// </summary>
public enum AudioBus
{
    Music,
    Sfx,
    Voice,
    Ambient,
    Ui,
}

/// <summary>Channel pools with separate voice limits, like the reference's 2D samples, 3D samples and streams.</summary>
public enum VoicePool
{
    Flat,
    Positional,
    Stream,
}

/// <summary>
/// <see cref="Inverse"/> is the retail curve (<c>MilesAudioManager::getEffectiveVolume</c>: full volume up to the
/// minimum distance, then min/d, silent from the maximum distance on); <see cref="Linear"/> is the commented-out
/// alternative in the same function (1 − (d − min)/(max − min)).
/// </summary>
public enum DistanceModel
{
    Inverse,
    Linear,
}

public readonly record struct VoiceHandle(long Value)
{
    public static VoiceHandle None => default;
    public bool IsValid => Value > 0;
}

/// <summary>The microphone. Defaults to the reference orientation (<c>m_listenerOrientation</c> = +Y forward, Z up).</summary>
public sealed record AudioListener(Vector3 Position, Vector3 Forward, Vector3 Up)
{
    public static AudioListener Default { get; } = new(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);
}

public sealed record BusVolumes(float Music = 1f, float Sfx = 1f, float Voice = 1f, float Ambient = 1f, float Ui = 1f)
{
    public float this[AudioBus bus] => bus switch
    {
        AudioBus.Music => Music,
        AudioBus.Sfx => Sfx,
        AudioBus.Voice => Voice,
        AudioBus.Ambient => Ambient,
        _ => Ui,
    };

    public BusVolumes With(AudioBus bus, float volume) => bus switch
    {
        AudioBus.Music => this with { Music = volume },
        AudioBus.Sfx => this with { Sfx = volume },
        AudioBus.Voice => this with { Voice = volume },
        AudioBus.Ambient => this with { Ambient = volume },
        _ => this with { Ui = volume },
    };
}

/// <param name="SampleRate">Output rate of <see cref="SoftwareMixer.Render"/>.</param>
/// <param name="FlatVoices">Simultaneous non-positional voices (<c>SampleCount2D</c>).</param>
/// <param name="PositionalVoices">Simultaneous 3D voices (<c>SampleCount3D</c>).</param>
/// <param name="StreamVoices">Simultaneous streamed dialog voices (<c>StreamCount</c>).</param>
/// <param name="MinSampleVolume">Requests and cullable 3D voices quieter than this are dropped (<c>MinSampleVolume</c>).</param>
/// <param name="Seed">Seed of the mixer's own <see cref="Selection.SeededRng"/>, used for per-loop sound choices.</param>
public sealed record MixerOptions(
    int SampleRate = 44100,
    int FlatVoices = 4,
    int PositionalVoices = 25,
    int StreamVoices = 3,
    float MinSampleVolume = 0f,
    DistanceModel DistanceModel = DistanceModel.Inverse,
    ulong Seed = 1,
    BusVolumes? Buses = null)
{
    /// <summary>Limits and default bus volumes from <c>AudioSettings.ini</c> (2D/3D volumes after <c>Relative2DVolume</c>).</summary>
    public static MixerOptions FromSettings(AudioSettingsInfo settings, ulong seed = 1)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new MixerOptions(settings.OutputRate, settings.SampleCount2D, settings.SampleCount3D, settings.StreamCount,
            settings.MinSampleVolume, DistanceModel.Inverse, seed,
            new BusVolumes(settings.DefaultMusicVolume, settings.Effective3DSoundVolume, settings.DefaultSpeechVolume,
                settings.Effective3DSoundVolume, settings.Effective2DSoundVolume));
    }
}

/// <summary>Counters since the mixer was created; a copy is published after every <see cref="SoftwareMixer.Render"/>.</summary>
public sealed record MixerStatistics(
    long Started,
    long Finished,
    long Stopped,
    long Stolen,
    long ReplacedByLimit,
    long RejectedDistance,
    long RejectedMuted,
    long RejectedLimit,
    long RejectedNoChannel,
    long CulledQuiet,
    long ClippedSamples,
    long RenderedFrames);
