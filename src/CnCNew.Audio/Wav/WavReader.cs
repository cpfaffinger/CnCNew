using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace CnCNew.Audio.Wav;

public static class WavFormatTags
{
    public const ushort Pcm = 0x0001;
    public const ushort MsAdpcm = 0x0002;
    public const ushort IeeeFloat = 0x0003;
    public const ushort ImaAdpcm = 0x0011;
    public const ushort Extensible = 0xFFFE;

    public static string Name(ushort tag) => tag switch
    {
        Pcm => "PCM",
        MsAdpcm => "MS-ADPCM",
        IeeeFloat => "IEEE-float",
        ImaAdpcm => "IMA-ADPCM",
        Extensible => "Extensible",
        _ => $"0x{tag:X4}",
    };
}

/// <summary>The <c>fmt </c> chunk. <see cref="EncodingTag"/> is the effective tag (the sub-format of <c>WAVE_FORMAT_EXTENSIBLE</c>).</summary>
public sealed record WavFormat(
    ushort FormatTag,
    ushort EncodingTag,
    int Channels,
    int SampleRate,
    int ByteRate,
    int BlockAlign,
    int BitsPerSample,
    int SamplesPerBlock)
{
    public string Description => $"{WavFormatTags.Name(EncodingTag)} {BitsPerSample}-bit {(Channels == 1 ? "mono" : Channels == 2 ? "stereo" : Channels + "ch")} {SampleRate} Hz";
}

public sealed record WavChunkInfo(string Id, long Offset, uint DeclaredSize, long AvailableSize);

/// <summary>A tolerated deviation from the RIFF/WAVE specification. Every tolerance has a stable code and is counted by the callers.</summary>
public sealed record WavDiagnostic(string Code, string Message);

public sealed record WavDecodeResult(
    PcmBuffer Pcm,
    WavFormat Format,
    ImmutableArray<WavChunkInfo> Chunks,
    int? FactFrames,
    ImmutableArray<WavDiagnostic> Diagnostics);

/// <param name="Strict">When set, every tolerance (see <see cref="WavDiagnostic"/>) is an <see cref="InvalidDataException"/> instead.</param>
public sealed record WavReadOptions(bool Strict = false)
{
    public static WavReadOptions Tolerant { get; } = new();
    public static WavReadOptions StrictMode { get; } = new(true);
}

/// <summary>
/// RIFF/WAVE reader after the Microsoft "Multimedia Programming Interface and Data Specifications 1.0" (1991, RIFF chunks,
/// <c>fmt </c>, <c>fact</c>, <c>data</c>, word-aligned chunk padding) and the "Multimedia Standards Update" (1994,
/// <c>WAVE_FORMAT_ADPCM</c> and <c>WAVE_FORMAT_DVI_ADPCM</c>) plus <c>WAVEFORMATEXTENSIBLE</c> (<c>mmreg.h</c>).
/// Unsupported encodings, a missing <c>fmt </c>/<c>data</c> chunk or an inconsistent format are errors; the tolerated
/// deviations (truncated data, missing pad byte, size mismatches) are reported as <see cref="WavDiagnostic"/>s.
/// </summary>
public static class WavReader
{
    public static WavDecodeResult Decode(Stream stream, WavReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Decode(copy.GetBuffer().AsSpan(0, (int)copy.Length), options);
    }

    public static WavDecodeResult Decode(ReadOnlySpan<byte> file, WavReadOptions? options = null)
    {
        options ??= WavReadOptions.Tolerant;
        var diagnostics = new List<WavDiagnostic>();
        void Tolerate(string code, string message)
        {
            if (options.Strict) throw new InvalidDataException($"WAV {code}: {message}");
            diagnostics.Add(new WavDiagnostic(code, message));
        }

        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        if (riffSize != file.Length - 8)
            Tolerate("riff-size-mismatch", $"RIFF size {riffSize} but {file.Length - 8} bytes follow the header.");

        var chunks = new List<WavChunkInfo>();
        WavFormat? format = null;
        byte[] formatExtra = [];
        int? factFrames = null;
        var dataOffset = -1;
        var dataLength = 0;
        var offset = 12;
        while (offset + 8 <= file.Length)
        {
            var id = Encoding.ASCII.GetString(file.Slice(offset, 4));
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(file[(offset + 4)..]);
            var bodyOffset = offset + 8;
            var available = Math.Min((long)declared, file.Length - bodyOffset);
            chunks.Add(new WavChunkInfo(id, offset, declared, available));
            if (available < declared)
                Tolerate(id == "data" ? "truncated-data" : "truncated-chunk", $"Chunk '{id}' declares {declared} bytes but only {available} remain.");
            var body = file.Slice(bodyOffset, (int)available);
            switch (id)
            {
                case "fmt ":
                    if (format is not null) throw new InvalidDataException("Duplicate 'fmt ' chunk.");
                    (format, formatExtra) = ParseFormat(body);
                    break;
                case "fact":
                    if (body.Length >= 4) factFrames = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body));
                    break;
                case "data":
                    if (dataOffset >= 0) throw new InvalidDataException("Duplicate 'data' chunk.");
                    dataOffset = bodyOffset;
                    dataLength = body.Length;
                    break;
            }

            var next = (long)bodyOffset + declared;
            if ((declared & 1) != 0)
            {
                // RIFF pads odd chunks to a word boundary; some writers omit the pad byte.
                if (next + 1 <= file.Length - 8 && !LooksLikeChunkId(file, (int)next + 1) && LooksLikeChunkId(file, (int)next))
                    Tolerate("unpadded-odd-chunk", $"Chunk '{id}' has an odd size without the RIFF pad byte.");
                else
                    next++;
            }
            if (next > file.Length) break;
            offset = (int)next;
        }
        if (offset < file.Length && offset + 8 > file.Length && file.Length - offset > 1)
            Tolerate("trailing-bytes", $"{file.Length - offset} bytes after the last chunk do not form a chunk header.");

        if (format is null) throw new InvalidDataException("The WAV file has no 'fmt ' chunk.");
        if (dataOffset < 0) throw new InvalidDataException("The WAV file has no 'data' chunk.");
        var data = file.Slice(dataOffset, dataLength);

        float[] samples = format.EncodingTag switch
        {
            WavFormatTags.Pcm => DecodePcm(data, format, Tolerate),
            WavFormatTags.IeeeFloat => DecodeFloat(data, format, Tolerate),
            WavFormatTags.ImaAdpcm => ImaAdpcm.Decode(data, format, Tolerate),
            WavFormatTags.MsAdpcm => MsAdpcm.Decode(data, format, formatExtra, Tolerate),
            _ => throw new NotSupportedException($"WAV encoding {WavFormatTags.Name(format.EncodingTag)} is not supported."),
        };

        var frames = samples.Length / format.Channels;
        if (factFrames is { } fact && format.EncodingTag is WavFormatTags.ImaAdpcm or WavFormatTags.MsAdpcm)
        {
            // Compressed files store the exact length in 'fact'; the last block is padded.
            if (fact < frames)
            {
                Array.Resize(ref samples, fact * format.Channels);
                frames = fact;
            }
            else if (fact > frames)
                Tolerate("fact-exceeds-data", $"'fact' announces {fact} frames but the data holds {frames}.");
        }

        return new WavDecodeResult(PcmBuffer.FromOwnedArray(format.SampleRate, format.Channels, samples), format,
            [..chunks], factFrames, [..diagnostics]);
    }

    private static bool LooksLikeChunkId(ReadOnlySpan<byte> file, int offset)
    {
        if (offset < 0 || offset + 4 > file.Length) return false;
        foreach (var value in file.Slice(offset, 4))
            if (value is < 0x20 or > 0x7E) return false;
        return true;
    }

    private static (WavFormat Format, byte[] Extra) ParseFormat(ReadOnlySpan<byte> body)
    {
        if (body.Length < 16) throw new InvalidDataException($"The 'fmt ' chunk has {body.Length} bytes; at least 16 are required.");
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        var sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]));
        var byteRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(body[8..]));
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(body[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
        ReadOnlySpan<byte> extra = [];
        if (body.Length >= 18)
        {
            var extraSize = BinaryPrimitives.ReadUInt16LittleEndian(body[16..]);
            extra = body.Slice(18, Math.Min(extraSize, body.Length - 18));
        }

        if (channels is < 1 or > 2) throw new NotSupportedException($"WAV files with {channels} channels are not supported.");
        if (sampleRate <= 0) throw new InvalidDataException($"Invalid WAV sample rate {sampleRate}.");
        if (blockAlign == 0) throw new InvalidDataException("WAV block alignment is zero.");

        var encoding = tag;
        if (tag == WavFormatTags.Extensible)
        {
            if (extra.Length < 22) throw new InvalidDataException("WAVE_FORMAT_EXTENSIBLE requires a 22-byte extension.");
            // The sub-format GUID is {xxxxxxxx-0000-0010-8000-00AA00389B71}; its first two bytes are the classic tag.
            encoding = BinaryPrimitives.ReadUInt16LittleEndian(extra[6..]);
            if (!extra.Slice(6, 16)[2..].SequenceEqual(ExtensibleGuidTail))
                throw new NotSupportedException("WAVE_FORMAT_EXTENSIBLE with a non-standard sub-format GUID is not supported.");
        }

        var samplesPerBlock = encoding switch
        {
            WavFormatTags.ImaAdpcm => ImaAdpcm.SamplesPerBlock(blockAlign, channels),
            WavFormatTags.MsAdpcm => MsAdpcm.SamplesPerBlock(blockAlign, channels),
            _ => 1,
        };
        return (new WavFormat(tag, encoding, channels, sampleRate, byteRate, blockAlign, bits, samplesPerBlock), extra.ToArray());
    }

    private static ReadOnlySpan<byte> ExtensibleGuidTail => [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];

    private static float[] DecodePcm(ReadOnlySpan<byte> data, WavFormat format, Action<string, string> tolerate)
    {
        var bytesPerSample = format.BitsPerSample switch
        {
            8 => 1,
            16 => 2,
            24 => 3,
            32 => 4,
            _ => throw new NotSupportedException($"PCM with {format.BitsPerSample} bits per sample is not supported."),
        };
        var frameSize = bytesPerSample * format.Channels;
        if (format.BlockAlign != frameSize)
            throw new InvalidDataException($"PCM block alignment {format.BlockAlign} does not match {format.Channels} × {bytesPerSample} bytes.");
        var frames = data.Length / frameSize;
        if (data.Length % frameSize != 0)
            tolerate("partial-frame", $"{data.Length % frameSize} trailing data bytes do not form a whole frame.");

        var result = new float[frames * format.Channels];
        for (var index = 0; index < result.Length; index++)
        {
            var at = index * bytesPerSample;
            result[index] = bytesPerSample switch
            {
                1 => PcmConvert.FromUInt8(data[at]),
                2 => PcmConvert.FromInt16(BinaryPrimitives.ReadInt16LittleEndian(data[at..])),
                3 => ((data[at] | (data[at + 1] << 8) | ((sbyte)data[at + 2] << 16))) / 8388608f,
                _ => BinaryPrimitives.ReadInt32LittleEndian(data[at..]) / 2147483648f,
            };
        }
        return result;
    }

    private static float[] DecodeFloat(ReadOnlySpan<byte> data, WavFormat format, Action<string, string> tolerate)
    {
        if (format.BitsPerSample != 32) throw new NotSupportedException($"IEEE float with {format.BitsPerSample} bits is not supported.");
        var frameSize = 4 * format.Channels;
        var frames = data.Length / frameSize;
        if (data.Length % frameSize != 0)
            tolerate("partial-frame", $"{data.Length % frameSize} trailing data bytes do not form a whole frame.");
        var result = new float[frames * format.Channels];
        for (var index = 0; index < result.Length; index++)
            result[index] = Math.Clamp(BinaryPrimitives.ReadSingleLittleEndian(data[(index * 4)..]), -1f, 1f);
        return result;
    }
}
