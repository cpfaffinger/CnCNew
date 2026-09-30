namespace CnCNew.Audio;

/// <summary>
/// A pull source of interleaved float PCM, used for streamed music. Implementations are not thread-safe; the mixer
/// reads them only from <see cref="Mixing.SoftwareMixer.Render"/>.
/// </summary>
public interface IPcmStream : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }

    /// <summary>Fills <paramref name="destination"/> with whole frames and returns the number of floats written; 0 means end of stream.</summary>
    int Read(Span<float> destination);

    /// <summary>Restarts at the first frame (used for looping).</summary>
    void Rewind();
}

/// <summary>Streams a fully decoded <see cref="PcmBuffer"/>.</summary>
public sealed class PcmBufferStream(PcmBuffer buffer) : IPcmStream
{
    private int _position;

    public int SampleRate => buffer.SampleRate;
    public int Channels => buffer.Channels;

    public int Read(Span<float> destination)
    {
        var count = Math.Min(destination.Length / Channels * Channels, buffer.Samples.Length - _position);
        buffer.Samples.AsSpan().Slice(_position, count).CopyTo(destination);
        _position += count;
        return count;
    }

    public void Rewind() => _position = 0;

    public void Dispose()
    {
    }
}
