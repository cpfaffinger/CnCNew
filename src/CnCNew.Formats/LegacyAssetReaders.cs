using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace CnCNew.Formats;

public sealed record DdsMetadata(int Width, int Height, int MipCount, string PixelFormat, long ExpectedMinimumBytes);
public sealed record RiffMetadata(string FormType, IReadOnlyList<string> Chunks);
public sealed record BinkMetadata(string Version, int Frames, int Width, int Height, int FramesPerSecondNumerator, int FramesPerSecondDenominator);
public sealed record StrEntry(string Key, string Value);
public sealed record TextAssetMetadata(int Version, IReadOnlyList<string> References);
public sealed record SageChunkMetadata(string Name, ushort Version, int Bytes, long HeaderOffset);
public sealed record SageMapMetadata(string Container, int ExpandedBytes, IReadOnlyDictionary<uint, string> Symbols, IReadOnlyList<SageChunkMetadata> Chunks);

public static partial class LegacyAssetReaders
{
    private const int MaximumDimension = 32768;

    public static DdsMetadata ReadDds(Stream stream)
    {
        var data = ReadAll(stream, 128);
        if (!data.AsSpan(0, 4).SequenceEqual("DDS "u8) || U32(data, 4) != 124 || U32(data, 76) != 32)
        {
            throw new InvalidDataException("DDS header or pixel-format header is invalid.");
        }

        var height = Count(U32(data, 12), "DDS height", MaximumDimension);
        var width = Count(U32(data, 16), "DDS width", MaximumDimension);
        var mipCount = U32(data, 28) == 0 ? 1 : Count(U32(data, 28), "DDS mip count", 32);
        var fourCc = Encoding.ASCII.GetString(data, 84, 4);
        var bits = U32(data, 88);
        var format = fourCc.TrimEnd('\0') switch
        {
            "DXT1" => "BC1",
            "DXT3" => "BC2",
            "DXT5" => "BC3",
            "" when bits is 24 or 32 => $"RGB{bits}",
            var value => throw new InvalidDataException($"Unsupported DDS pixel format '{value}' ({bits} bits)."),
        };

        long payload = 0;
        var w = width;
        var h = height;
        for (var level = 0; level < mipCount; level++)
        {
            payload = checked(payload + (format switch
            {
                "BC1" => checked((long)((w + 3) / 4) * ((h + 3) / 4) * 8),
                "BC2" or "BC3" => checked((long)((w + 3) / 4) * ((h + 3) / 4) * 16),
                _ => checked((long)w * h * (bits / 8)),
            }));
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        var required = checked(128 + payload);
        if (stream.Length < required)
        {
            throw new InvalidDataException($"DDS is truncated: {stream.Length} bytes, at least {required} required.");
        }

        return new DdsMetadata(width, height, mipCount, format, required);
    }

    public static RiffMetadata ReadRiff(Stream stream, string expectedForm)
    {
        var header = ReadAll(stream, 12);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8))
        {
            throw new InvalidDataException("RIFF signature is missing.");
        }

        var declaredEnd = checked((long)U32(header, 4) + 8);
        // Every shipped ACON cursor writes the total file size into RIFF's size-minus-eight field.
        // Preserve this single, inventoried legacy writer quirk; WAVE remains strictly standard.
        var shippedAconSizeQuirk = expectedForm == "ACON" && declaredEnd == stream.Length + 8;
        if (declaredEnd != stream.Length && !shippedAconSizeQuirk)
        {
            throw new InvalidDataException($"RIFF declares {declaredEnd} bytes but stream has {stream.Length}.");
        }

        var form = Encoding.ASCII.GetString(header, 8, 4);
        if (form != expectedForm)
        {
            throw new InvalidDataException($"RIFF form is '{form}', expected '{expectedForm}'.");
        }

        var chunks = new List<string>();
        Span<byte> chunkHeader = stackalloc byte[8];
        while (stream.Position < stream.Length)
        {
            ReadExactly(stream, chunkHeader);
            var id = Encoding.ASCII.GetString(chunkHeader[..4]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
            var padded = checked((long)size + (size & 1));
            if (padded > stream.Length - stream.Position)
            {
                throw new InvalidDataException($"RIFF chunk '{id}' exceeds file end at offset {stream.Position - 8}.");
            }
            chunks.Add(id);
            stream.Position += padded;
        }

        if (expectedForm == "WAVE" && (!chunks.Contains("fmt ") || !chunks.Contains("data")))
        {
            throw new InvalidDataException("WAVE requires fmt and data chunks.");
        }
        return new RiffMetadata(form, chunks);
    }

    public static BinkMetadata ReadBink(Stream stream)
    {
        var header = ReadAll(stream, 44);
        var signature = Encoding.ASCII.GetString(header, 0, 4);
        if (!signature.StartsWith("BIK", StringComparison.Ordinal) || signature[3] is < 'a' or > 'z')
        {
            throw new InvalidDataException("Unsupported Bink signature.");
        }
        var declared = checked((long)U32(header, 4) + 8);
        if (declared != stream.Length)
        {
            throw new InvalidDataException($"Bink declares {declared} bytes but stream has {stream.Length}.");
        }
        var frames = Count(U32(header, 8), "Bink frame", 10_000_000);
        var width = Count(U32(header, 20), "Bink width", MaximumDimension);
        var height = Count(U32(header, 24), "Bink height", MaximumDimension);
        var rate = Count(U32(header, 28), "Bink frame-rate numerator", int.MaxValue);
        var divisor = Count(U32(header, 32), "Bink frame-rate denominator", int.MaxValue);
        return new BinkMetadata(signature, frames, width, height, rate, divisor);
    }

    public static void ValidateMp3(Stream stream)
    {
        var data = ReadAll(stream, 10);
        long offset = 0;
        if (data.AsSpan(0, 3).SequenceEqual("ID3"u8))
        {
            if (data[3] is 0 or > 4 || data.Skip(6).Any(value => (value & 0x80) != 0))
            {
                throw new InvalidDataException("Invalid ID3 version or synchsafe size.");
            }
            offset = 10L + (data[6] << 21 | data[7] << 14 | data[8] << 7 | data[9]);
        }
        if (offset + 2 > stream.Length)
        {
            throw new InvalidDataException("MP3 contains no MPEG frame.");
        }
        stream.Position = offset;
        Span<byte> sync = stackalloc byte[2];
        ReadExactly(stream, sync);
        if (sync[0] != 0xff || (sync[1] & 0xe0) != 0xe0)
        {
            throw new InvalidDataException($"MP3 frame sync missing at offset {offset}.");
        }
    }

    public static IReadOnlyList<StrEntry> ReadStr(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.Latin1, false, leaveOpen: true);
        var text = reader.ReadToEnd();
        var entries = new List<StrEntry>();
        var index = 0;
        while (index < text.Length)
        {
            SkipSpaceAndComments(text, ref index);
            if (index == text.Length) break;
            var lineEnd = text.IndexOfAny(['\r', '\n'], index);
            if (lineEnd < 0) throw new InvalidDataException($"STR key at offset {index} has no value line.");
            var key = text[index..lineEnd].Trim();
            index = lineEnd;
            SkipSpaceAndComments(text, ref index);
            if (index >= text.Length || text[index] != '"')
                throw new InvalidDataException($"STR key '{key}' has no quoted value at offset {index}.");
            var value = ReadQuoted(text, ref index, key);
            SkipWhitespace(text, ref index);
            if (index + 3 <= text.Length && text.AsSpan(index, 3).Equals("END", StringComparison.OrdinalIgnoreCase)) index += 3;
            if (string.IsNullOrWhiteSpace(key) || entries.Any(entry => entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"STR contains empty or duplicate key '{key}'.");
            entries.Add(new StrEntry(key, value));
        }
        return entries;
    }

    public static TextAssetMetadata ReadWnd(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.Latin1, false, leaveOpen: true);
        var text = reader.ReadToEnd();
        var versionMatch = WndVersionRegex().Match(text);
        if (!versionMatch.Success || !int.TryParse(versionMatch.Groups[1].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version) || version != 2)
            throw new InvalidDataException("WND FILE_VERSION must be 2.");
        ValidateBalanced(text, '{', '}', "WND braces");
        return new TextAssetMetadata(version, ExtractReferences(text));
    }

    public static SageMapMetadata ReadSageMap(Stream stream)
    {
        var (container, expanded) = ExpandSageMap(stream);
        var parsed = ParseSageChunks(expanded);
        return new SageMapMetadata(container, expanded.Length, parsed.Symbols, parsed.Chunks);
    }

    /// <summary>Unwraps the raw, EAR/RefPack or ZL5/zlib container of a map and returns the plain chunk-table bytes.</summary>
    public static (string Container, byte[] Bytes) ExpandSageMap(Stream stream)
    {
        var source = ReadWholeStream(stream, 8);
        byte[] expanded;
        string container;
        if (source.AsSpan(0, 4).SequenceEqual("CkMp"u8))
        {
            expanded = source;
            container = "CkMp";
        }
        else if (source.AsSpan(0, 4).SequenceEqual(new byte[] { 0x45, 0x41, 0x52, 0x00 }))
        {
            var expected = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)));
            expanded = DecompressRefPack(source.AsSpan(8), expected);
            container = "EAR";
        }
        else if (source.AsSpan(0, 4).SequenceEqual(new byte[] { 0x5a, 0x4c, 0x35, 0x00 }))
        {
            var expected = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)));
            using var compressed = new MemoryStream(source, 8, source.Length - 8, writable: false);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream(expected);
            zlib.CopyTo(output);
            expanded = output.ToArray();
            if (expanded.Length != expected)
                throw new InvalidDataException($"ZL5 expands to {expanded.Length} bytes; expected {expected}.");
            container = "ZL5";
        }
        else throw new InvalidDataException("MAP must use a raw CkMp, EAR/RefPack, or ZL5/zlib container.");
        return (container, expanded);
    }

    public static void ValidateSageMap(Stream stream) => ReadSageMap(stream);

    public static void ValidateScriptBinary(Stream stream)
    {
        ParseSageChunks(ReadWholeStream(stream, 8));
    }

    public static void ValidateWak(Stream stream)
    {
        if (stream.Length < 4 || (stream.Length - 4) % 20 != 0)
            throw new InvalidDataException("WAK length does not match its 20-byte record layout.");
        var header = ReadAll(stream, (int)Math.Min(20, stream.Length));
        for (var offset = 0; offset + 4 <= Math.Min(16, header.Length); offset += 4)
        {
            var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset)));
            if (!float.IsFinite(value)) throw new InvalidDataException($"WAK contains non-finite coordinate at offset {offset}.");
        }
    }

    public static IReadOnlyList<string> ExtractReferences(string text)
        => ReferenceRegex().Matches(LineCommentRegex().Replace(text, string.Empty)).Select(match => NormalizeReferenceMatch(match.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizeReferenceMatch(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        foreach (var root in new[] { "Art/", "Data/", "Maps/", "Window/" })
        {
            var index = normalized.IndexOf(root, StringComparison.OrdinalIgnoreCase);
            if (index > 0) return normalized[index..];
        }
        if (!normalized.Contains('/') && normalized.Contains(' '))
            return normalized[(normalized.LastIndexOf(' ') + 1)..];
        return normalized;
    }

    private static byte[] ReadAll(Stream stream, int count)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Asset stream must be readable and seekable.", nameof(stream));
        if (stream.Length < count) throw new InvalidDataException($"Asset is truncated: {stream.Length} bytes; {count} required.");
        var data = new byte[count];
        ReadExactly(stream, data);
        return data;
    }
    private static byte[] ReadWholeStream(Stream stream, int minimum)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Asset stream must be readable and seekable.", nameof(stream));
        if (stream.Length < minimum || stream.Length > int.MaxValue)
            throw new InvalidDataException($"Asset length {stream.Length} is invalid; at least {minimum} bytes are required.");
        var data = new byte[stream.Length];
        ReadExactly(stream, data);
        return data;
    }

    internal static (IReadOnlyDictionary<uint, string> Symbols, IReadOnlyList<SageChunkMetadata> Chunks) ParseSageChunks(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 4).SequenceEqual("CkMp"u8))
            throw new InvalidDataException("SAGE chunk table signature CkMp is missing.");
        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (count <= 0 || count > 65535) throw new InvalidDataException($"SAGE symbol count {count} is invalid.");
        var symbols = new Dictionary<uint, string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var offset = 8;
        for (var index = 0; index < count; index++)
        {
            if (offset >= data.Length) throw new InvalidDataException($"SAGE symbol {index} is truncated at offset {offset}.");
            var length = data[offset++];
            if (length > data.Length - offset - 4)
                throw new InvalidDataException($"SAGE symbol {index} has invalid length {length} at offset {offset - 1}.");
            var name = Encoding.Latin1.GetString(data, offset, length);
            offset += length;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            offset += 4;
            if (!symbols.TryAdd(id, name) || (name.Length != 0 && !names.Add(name)))
                throw new InvalidDataException($"SAGE symbol table contains duplicate ID {id} or name '{name}'.");
        }

        var chunks = new List<SageChunkMetadata>();
        while (offset < data.Length)
        {
            var headerOffset = offset;
            if (data.Length - offset < 10) throw new InvalidDataException($"SAGE chunk header is truncated at offset {offset}.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 6));
            offset += 10;
            if (!symbols.TryGetValue(id, out var name))
                throw new InvalidDataException($"SAGE chunk at offset {headerOffset} uses unknown symbol ID {id}.");
            if (size < 0 || size > data.Length - offset)
                throw new InvalidDataException($"SAGE chunk '{name}' at offset {headerOffset} has invalid size {size}.");
            chunks.Add(new SageChunkMetadata(name, version, size, headerOffset));
            offset += size;
        }
        return (symbols, chunks);
    }

    private static byte[] DecompressRefPack(ReadOnlySpan<byte> source, int expected)
    {
        var packed = source.ToArray();
        if (expected <= 0 || expected > 512 * 1024 * 1024) throw new InvalidDataException($"EAR expanded size {expected} is invalid.");
        if (packed.Length < 5 || packed[0] != 0x10 || packed[1] != 0xfb)
            throw new InvalidDataException("EAR RefPack signature 10 FB is missing.");
        var declared = packed[2] << 16 | packed[3] << 8 | packed[4];
        if (declared != expected) throw new InvalidDataException($"EAR sizes disagree: wrapper {expected}, RefPack {declared}.");
        var output = new byte[expected];
        var input = 5;
        var written = 0;
        while (true)
        {
            if (input >= packed.Length) throw new InvalidDataException("RefPack command stream is truncated.");
            var control = packed[input++];
            if (control >= 0xfc)
            {
                CopyLiteral(control & 3);
                break;
            }
            int literals, length, distance;
            if (control >= 0xe0)
            {
                CopyLiteral(((control & 0x1f) << 2) + 4);
                continue;
            }
            if (control >= 0xc0)
            {
                Require(3);
                var b1 = packed[input++]; var b2 = packed[input++]; var b3 = packed[input++];
                literals = control & 3;
                length = ((control & 0x0c) << 6) + b3 + 5;
                distance = ((control & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else if (control >= 0x80)
            {
                Require(2);
                var b1 = packed[input++]; var b2 = packed[input++];
                literals = b1 >> 6;
                length = (control & 0x3f) + 4;
                distance = ((b1 & 0x3f) << 8) + b2 + 1;
            }
            else
            {
                Require(1);
                var b1 = packed[input++];
                literals = control & 3;
                length = ((control & 0x1c) >> 2) + 3;
                distance = ((control & 0x60) << 3) + b1 + 1;
            }
            CopyLiteral(literals);
            if (distance > written || length > output.Length - written)
                throw new InvalidDataException($"RefPack back-reference at input {input} is outside the output window.");
            for (var index = 0; index < length; index++)
            {
                output[written] = output[written - distance];
                written++;
            }
        }
        if (written != expected || input != packed.Length)
            throw new InvalidDataException($"RefPack ended at input {input}/{packed.Length}, output {written}/{expected}.");
        return output;

        void Require(int count)
        {
            if (count > packed.Length - input) throw new InvalidDataException("RefPack command arguments are truncated.");
        }
        void CopyLiteral(int count)
        {
            if (count > packed.Length - input || count > output.Length - written)
                throw new InvalidDataException("RefPack literal exceeds input or output bounds.");
            packed.AsSpan(input, count).CopyTo(output.AsSpan(written));
            input += count; written += count;
        }
    }
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static int Count(uint value, string name, int maximum)
        => value is 0 || value > maximum ? throw new InvalidDataException($"{name} value {value} is invalid.") : (int)value;
    private static void ReadExactly(Stream stream, Span<byte> data)
    {
        try { stream.ReadExactly(data); } catch (EndOfStreamException error) { throw new InvalidDataException("Unexpected asset file end.", error); }
    }
    private static void SkipWhitespace(string text, ref int index) { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
    private static void SkipSpaceAndComments(string text, ref int index)
    {
        while (true)
        {
            SkipWhitespace(text, ref index);
            if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
            { index = text.IndexOfAny(['\r','\n'], index); if (index < 0) index = text.Length; continue; }
            if (index < text.Length && text[index] == ';')
            { index = text.IndexOfAny(['\r','\n'], index); if (index < 0) index = text.Length; continue; }
            break;
        }
    }
    private static string ReadQuoted(string text, ref int index, string key)
    {
        index++;
        var result = new StringBuilder();
        while (index < text.Length)
        {
            var value = text[index++];
            if (value == '"') return result.ToString();
            if (value == '\\' && index < text.Length) result.Append(text[index++]); else result.Append(value);
        }
        throw new InvalidDataException($"STR value for '{key}' is unterminated.");
    }
    private static void ValidateBalanced(string text, char open, char close, string description)
    {
        var depth = 0; var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '"' && (index == 0 || text[index - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (text[index] == open) depth++;
            else if (text[index] == close && --depth < 0) throw new InvalidDataException($"{description} close before open at offset {index}.");
        }
        if (quoted || depth != 0) throw new InvalidDataException($"{description} are unbalanced.");
    }

    [GeneratedRegex(@"(?im)^\s*FILE_VERSION\s*=\s*(\d+)\s*;")]
    private static partial Regex WndVersionRegex();
    [GeneratedRegex(@"(?i)(?:[A-Za-z0-9_. -]+[/\\])*[A-Za-z0-9_. -]+\.(?:w3d|dds|tga|wav|mp3|bik|ani|wnd|str|map|ini|scb|wak)")]
    private static partial Regex ReferenceRegex();
    [GeneratedRegex(@"(?m)(?://|;|#).*$")]
    private static partial Regex LineCommentRegex();
}
