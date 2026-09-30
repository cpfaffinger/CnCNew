using NLayer;

namespace CnCNew.Audio.Mp3;

/// <summary>
/// Streaming MPEG-1/2/2.5 Layer I–III decoder on top of NLayer (<c>MpegFile</c>, MIT licence; see docs/AUDIO.md for the
/// dependency decision). Only one decoded MPEG frame (at most 1152 samples per channel) is buffered by NLayer, so music
/// tracks never sit fully decoded in memory. The stream owns <paramref name="source"/>.
/// </summary>
public sealed class Mp3PcmStream : IPcmStream
{
    private readonly MpegFile _file;
    private readonly Stream _source;

    public Mp3PcmStream(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek) throw new ArgumentException("MP3 streams must be seekable so that music can loop.", nameof(source));
        _source = source;
        try
        {
            _file = new MpegFile(source);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            source.Dispose();
            throw new InvalidDataException("The MP3 stream has no decodable MPEG audio frame.", exception);
        }
        SampleRate = _file.SampleRate;
        Channels = _file.Channels;
        if (SampleRate <= 0 || Channels is < 1 or > 2)
        {
            Dispose();
            throw new InvalidDataException($"Unsupported MP3 layout: {Channels} channel(s) at {SampleRate} Hz.");
        }
    }

    public int SampleRate { get; }
    public int Channels { get; }

    /// <summary>Duration reported by NLayer from the frame index (exact after a full scan, estimated for VBR before).</summary>
    public TimeSpan Duration => _file.Duration;

    public int Read(Span<float> destination)
    {
        var wanted = destination.Length / Channels * Channels;
        var total = 0;
        while (total < wanted)
        {
            var read = _file.ReadSamples(destination[total..wanted]);
            if (read <= 0) break;
            total += read;
        }
        return total;
    }

    public void Rewind() => _file.Time = TimeSpan.Zero;

    public void Dispose()
    {
        _file?.Dispose();
        _source.Dispose();
    }
}

public static class Mp3Decoder
{
    /// <summary>Decodes a whole MP3 into memory (used for speech files and tests; music should stream through <see cref="Mp3PcmStream"/>).</summary>
    public static PcmBuffer DecodeAll(Stream source)
    {
        using var stream = new Mp3PcmStream(source);
        var chunk = new float[8192 * stream.Channels];
        var samples = new List<float>();
        int read;
        while ((read = stream.Read(chunk)) > 0)
            samples.AddRange(chunk.AsSpan(0, read));
        return PcmBuffer.FromOwnedArray(stream.SampleRate, stream.Channels, [..samples]);
    }
}
