namespace CnCNew.Video;

/// <summary>The sound of a movie: interleaved 16-bit samples.</summary>
public sealed record BinkSound(int SampleRate, int Channels, short[] Samples)
{
    public int FrameCount => Samples.Length / Channels;
    public double Seconds => (double)FrameCount / SampleRate;
}

/// <summary>A movie being decoded: its frames one after the other, and its sound as a whole. A frame depends on the one before it, so none can be left out.</summary>
public sealed class BinkMovie
{
    private readonly BinkVideoDecoder _video;

    public BinkMovie(BinkFile file)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        _video = new BinkVideoDecoder(file.Header.Width, file.Header.Height, file.HasAlpha);
    }

    public BinkFile File { get; }
    public int Width => File.Header.Width;
    public int Height => File.Header.Height;
    public int FrameCount => File.Header.FrameCount;
    public bool HasAlpha => File.HasAlpha;

    /// <summary>The frames per second as the header states them.</summary>
    public double FramesPerSecond => File.Header.FramesPerSecond;

    /// <summary>The number of frames decoded so far; the picture is frame <c>FramesDecoded - 1</c>.</summary>
    public int FramesDecoded { get; private set; }

    public bool IsAtEnd => FramesDecoded >= FrameCount;

    /// <summary>Decodes the next frame; false at the end, when the last picture stays.</summary>
    public bool DecodeNextFrame()
    {
        if (IsAtEnd) return false;
        var packets = File.Packets(FramesDecoded);
        _video.Decode(File.Data, packets.VideoOffset, packets.VideoLength);
        FramesDecoded++;
        return true;
    }

    /// <summary>Copies the picture as 8-bit R, G, B, A (<c>Width * Height * 4</c> bytes).</summary>
    public void CopyRgba(Span<byte> destination) => _video.CopyRgba(destination);

    /// <summary>Feeds the visible bytes of the picture's planes to <paramref name="sink"/>.</summary>
    public void WritePlanes(Action<ReadOnlySpan<byte>> sink) => _video.WriteVisible(sink);

    /// <summary>The first audio track, decoded as a whole; <see langword="null"/> when the movie has none.</summary>
    public BinkSound? DecodeSound()
    {
        if (File.Header.AudioTracks.IsEmpty) return null;
        var track = File.Header.AudioTracks[0];
        var decoder = new BinkAudioDecoder(track.SampleRate, track.Channels);
        var samples = new List<short>();
        for (var frame = 0; frame < FrameCount; frame++)
        {
            var packets = File.Packets(frame);
            if (packets.AudioLength > 0) samples.AddRange(decoder.Decode(File.Data, packets.AudioOffset, packets.AudioLength));
        }
        return new BinkSound(track.SampleRate, track.Channels, samples.ToArray());
    }
}
