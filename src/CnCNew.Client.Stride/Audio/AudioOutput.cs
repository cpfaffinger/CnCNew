using CnCNew.Audio.Mixing;

namespace CnCNew.Client.Stride.Audio;

/// <summary>Placeholder until the mixer is connected to a Stride sound source.</summary>
internal sealed class AudioOutput(SoftwareMixer mixer) : IDisposable
{
    public SoftwareMixer Mixer => mixer;

    public void Start()
    {
    }

    public void Dispose()
    {
    }
}
