using System.Buffers.Binary;
using System.Collections.Immutable;

namespace CnCNew.Audio.Movies;

public sealed record BinkAudioTrack(int TrackId, int SampleRate, int Channels, bool SixteenBit, bool UsesDct, uint MaxDecodedSize);

/// <summary>
/// Header of a Bink 1 video (<c>.bik</c>). Only the container header and the frame index are read; decoding the
/// video or audio needs the proprietary Bink codec, which this project does not have (see docs/AUDIO.md).
/// </summary>
public sealed record BinkHeader(
    string Signature,
    char Revision,
    uint FileSize,
    int FrameCount,
    uint LargestFrameSize,
    int Width,
    int Height,
    uint FpsNumerator,
    uint FpsDenominator,
    uint VideoFlags,
    ImmutableArray<BinkAudioTrack> AudioTracks,
    int KeyFrames)
{
    public const int MaxAudioTracks = 256;

    public double FramesPerSecond => (double)FpsNumerator / FpsDenominator;
    public double DurationSeconds => FrameCount / FramesPerSecond;

    /// <summary>
    /// Layout after the public format descriptions of the Bink 1 container (MultimediaWiki "Bink Container"; the
    /// demuxer <c>read_header</c> in FFmpeg's <c>libavformat/bink.c</c> documents the same fields). All values are
    /// little-endian: signature <c>BIK</c> + revision letter, file size − 8, frame count, largest frame size, frame count
    /// again, width, height, fps numerator and denominator, video flags, audio track count; then per track the maximum
    /// decoded size, per track sample rate (uint16) and flags (uint16: 0x1000 DCT, 0x2000 stereo, 0x4000 16-bit), per
    /// track an id; then the frame index (frame count + 1 offsets, bit 0 = key frame).
    /// </summary>
    public static BinkHeader Parse(ReadOnlySpan<byte> data, long fileLength)
    {
        if (data.Length < 44) throw new InvalidDataException("A Bink header needs at least 44 bytes.");
        if (data[..3].SequenceEqual("KB2"u8)) throw new NotSupportedException("Bink 2 ('KB2') files are not supported.");
        if (!data[..3].SequenceEqual("BIK"u8)) throw new InvalidDataException("Not a Bink file (signature is not 'BIK').");
        var revision = (char)data[3];
        if (revision is < 'b' or > 'k') throw new NotSupportedException($"Unknown Bink 1 revision '{revision}'.");

        var fileSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8;
        var frames = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var largest = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        var fpsNumerator = BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        var fpsDenominator = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[36..]);
        var trackCount = BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);

        if (fileSize != fileLength) throw new InvalidDataException($"Bink header announces {fileSize} bytes but the file has {fileLength}.");
        if (frames == 0 || frames > 1_000_000) throw new InvalidDataException($"Implausible Bink frame count {frames}.");
        if (largest > fileSize) throw new InvalidDataException("The largest Bink frame is larger than the file.");
        if (width is 0 or > 8192 || height is 0 or > 8192) throw new InvalidDataException($"Implausible Bink size {width}×{height}.");
        if (fpsNumerator == 0 || fpsDenominator == 0) throw new InvalidDataException("Bink frame rate numerator and denominator must be positive.");
        if (trackCount > MaxAudioTracks) throw new InvalidDataException($"{trackCount} Bink audio tracks exceed the limit of {MaxAudioTracks}.");

        var offset = 44;
        var tableLength = (int)trackCount * 12 + ((int)frames + 1) * 4;
        if (data.Length < offset + tableLength) throw new InvalidDataException("The Bink audio track table or frame index is truncated.");
        var tracks = ImmutableArray.CreateBuilder<BinkAudioTrack>((int)trackCount);
        for (var track = 0; track < trackCount; track++)
        {
            var maxDecoded = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4 * track)..]);
            var rateOffset = offset + 4 * (int)trackCount + 4 * track;
            var rate = BinaryPrimitives.ReadUInt16LittleEndian(data[rateOffset..]);
            var trackFlags = BinaryPrimitives.ReadUInt16LittleEndian(data[(rateOffset + 2)..]);
            var id = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 8 * (int)trackCount + 4 * track)..]));
            if (rate == 0) throw new InvalidDataException($"Bink audio track {track} has sample rate 0.");
            tracks.Add(new BinkAudioTrack(id, rate, (trackFlags & 0x2000) != 0 ? 2 : 1, (trackFlags & 0x4000) != 0, (trackFlags & 0x1000) != 0, maxDecoded));
        }
        offset += (int)trackCount * 12;

        var keyFrames = 0;
        var previous = 0u;
        for (var frame = 0; frame <= frames; frame++)
        {
            var entry = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4 * frame)..]);
            var position = entry & ~1u;
            if (frame < frames && (entry & 1) != 0) keyFrames++;
            if (frame > 0 && position <= previous) throw new InvalidDataException($"Bink frame index is not increasing at frame {frame}.");
            if (position > fileSize) throw new InvalidDataException($"Bink frame {frame} starts beyond the end of the file.");
            previous = position;
        }
        if (previous != fileSize) throw new InvalidDataException($"The Bink frame index ends at {previous}, not at the file end {fileSize}.");

        return new BinkHeader("BIK" + revision, revision, fileSize, (int)frames, largest, (int)width, (int)height,
            fpsNumerator, fpsDenominator, flags, tracks.MoveToImmutable(), keyFrames);
    }

    /// <summary>Reads just enough of <paramref name="stream"/> for the header and frame index.</summary>
    public static BinkHeader Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> head = stackalloc byte[44];
        stream.ReadExactly(head);
        var frames = (long)BinaryPrimitives.ReadUInt32LittleEndian(head[8..]);
        var tracks = (long)BinaryPrimitives.ReadUInt32LittleEndian(head[40..]);
        var needed = 44 + Math.Min(tracks, MaxAudioTracks + 1) * 12 + (Math.Min(frames, 1_000_001) + 1) * 4;
        var buffer = new byte[Math.Min(needed, stream.Length)];
        head.CopyTo(buffer);
        stream.ReadAtLeast(buffer.AsSpan(44), buffer.Length - 44, throwOnEndOfStream: false);
        return Parse(buffer, stream.Length);
    }
}
