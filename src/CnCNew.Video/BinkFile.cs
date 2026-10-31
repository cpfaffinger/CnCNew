using System.Buffers.Binary;

namespace CnCNew.Video;

/// <summary>Where one frame's parts lie in the file: the packet of the first audio track (empty without one) and the video packet.</summary>
public readonly record struct BinkFramePackets(int AudioOffset, int AudioLength, int VideoOffset, int VideoLength);

/// <summary>
/// A Bink movie held in memory, as the original holds it (<c>BinkOpen</c> with <c>BINKPRELOADALL</c>), and the way into its frames. It accepts exactly what the game ships:
/// revision <c>i</c> of Bink 1, YUV 4:2:0 with or without an alpha plane, and audio tracks in the DCT variant; anything else is refused with a clear error, nothing is guessed
/// (docs/CLIENT-VIDEO.md). The layout of a frame is the demuxer's in FFmpeg's <c>libavformat/bink.c</c> and MultimediaWiki's "Bink Container": for every audio track a 32-bit size
/// and that many bytes, then the video packet to the end of the frame.
/// </summary>
public sealed class BinkFile
{
    /// <summary>Bit 20 of the video flags: an alpha plane comes before the luminance plane.</summary>
    public const uint AlphaFlag = 0x00100000;
    private const char SupportedRevision = 'i';

    private readonly int[] _frameOffsets;

    private BinkFile(BinkHeader header, byte[] data, int[] frameOffsets)
    {
        Header = header;
        Data = data;
        _frameOffsets = frameOffsets;
    }

    public BinkHeader Header { get; }

    public bool HasAlpha => (Header.VideoFlags & AlphaFlag) != 0;

    /// <summary>The file's bytes and <see cref="BinkBitReader.Padding"/> spare bytes behind them.</summary>
    internal byte[] Data { get; }

    /// <summary>Reads the whole movie from <paramref name="stream"/>.</summary>
    public static BinkFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek)
        {
            var length = checked((int)(stream.Length - stream.Position));
            var data = new byte[length + BinkBitReader.Padding];
            stream.ReadExactly(data, 0, length);
            return Parse(data, length);
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.GetBuffer();
        var size = checked((int)copy.Length);
        var padded = new byte[size + BinkBitReader.Padding];
        Array.Copy(bytes, padded, size);
        return Parse(padded, size);
    }

    /// <summary>A movie from its bytes; the bytes are copied.</summary>
    public static BinkFile Open(ReadOnlySpan<byte> file)
    {
        var data = new byte[file.Length + BinkBitReader.Padding];
        file.CopyTo(data);
        return Parse(data, file.Length);
    }

    private static BinkFile Parse(byte[] data, int length)
    {
        var header = BinkHeader.Parse(data.AsSpan(0, length), length);
        if (header.Revision != SupportedRevision)
            throw new NotSupportedException($"Bink revision '{header.Revision}' is not supported: the game ships revision '{SupportedRevision}' only.");
        if ((header.VideoFlags & ~AlphaFlag) != 0)
            throw new NotSupportedException($"Bink video flags 0x{header.VideoFlags:X8} are not supported: the game ships none but the alpha flag.");
        foreach (var track in header.AudioTracks)
            if (!track.UsesDct) throw new NotSupportedException($"Bink audio track {track.TrackId} is coded in the RDFT variant: the game ships the DCT variant only.");

        var index = header.FrameIndexOffset;
        var offsets = new int[header.FrameCount + 1];
        for (var frame = 0; frame <= header.FrameCount; frame++)
            offsets[frame] = (int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index + 4 * frame)) & ~1u);
        return new BinkFile(header, data, offsets);
    }

    /// <summary>True when frame <paramref name="frame"/> is marked as a key frame in the index.</summary>
    public bool IsKeyFrame(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, Header.FrameCount);
        return (BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(Header.FrameIndexOffset + 4 * frame)) & 1) != 0;
    }

    /// <summary>Finds the packets of frame <paramref name="frame"/>.</summary>
    public BinkFramePackets Packets(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, Header.FrameCount);
        var position = _frameOffsets[frame];
        var end = _frameOffsets[frame + 1];
        int audioOffset = 0, audioLength = 0;
        for (var track = 0; track < Header.AudioTracks.Length; track++)
        {
            if (end - position < 4) throw new InvalidDataException($"Bink frame {frame} ends inside the size of audio track {track}.");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(position));
            position += 4;
            if (size > (uint)(end - position)) throw new InvalidDataException($"Bink frame {frame}: audio track {track} announces {size} bytes, the frame has {end - position} left.");
            if (track == 0 && size >= 4) (audioOffset, audioLength) = (position, (int)size);
            position += (int)size;
        }
        return new BinkFramePackets(audioOffset, audioLength, position, end - position);
    }
}
