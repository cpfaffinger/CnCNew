using System.Buffers.Binary;
using System.Text;
using CnCNew.Images;

namespace CnCNew.Import;

/// <summary>
/// What the patch's own tool chain (its <c>ModBundleCoreItems.json</c> and <c>ModBundleCoreAudioItems.json</c>) makes of its sources, rewritten for the files the core
/// bundles use: a text block marked as optional leaves an INI file, the multi-language string file becomes the English CSF, a Photoshop or TGA source becomes a DDS or TGA
/// the game loads. Everything else is taken as it is. The result depends only on the bytes of the sources, so an installation has the recorded export.
/// </summary>
public static class PatchBuild
{
    private const string GenerateMip = "Art/Textures/GenerateMip/";
    private const string NoMip = "Art/Textures/NoMip/";
    private const string GenerateTga = "Art/Textures/GenerateTga/";
    private const string StringSource = "Data/generals.str";
    private const string EnglishStrings = "Data/English/generals.csf";
    private const string Language = "US";
    private const string OptionalBegin = "patch104p-optional-begin";
    private const string OptionalEnd = "patch104p-optional-end";

    /// <summary>The two textures the patch's bundle names beside the rule for its directory.</summary>
    private static readonly (string Source, string Target)[] SecondNames = [("exhorde", "exhordeb"), ("exhorde_up", "exhordeb_up")];

    /// <summary>Whether the core bundles take the file from a source of the patch.</summary>
    public static bool IsConverted(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return path.Equals(StringSource, StringComparison.OrdinalIgnoreCase)
            || extension == ".psd" && (Starts(path, GenerateMip) || Starts(path, NoMip) || Starts(path, GenerateTga))
            || extension == ".tga" && (Starts(path, GenerateMip) || Starts(path, NoMip));
    }

    /// <summary>The runtime files a source of the patch gives, with their virtual paths.</summary>
    public static IEnumerable<(string Path, byte[] Bytes)> Outputs(string path, byte[] source)
    {
        if (path.Equals(StringSource, StringComparison.OrdinalIgnoreCase))
        {
            yield return (EnglishStrings, Strings(source));
            yield break;
        }
        if (Starts(path, GenerateTga))
        {
            yield return ($"Art/Textures/{Path.GetFileNameWithoutExtension(path)}.tga", Tga(Picture(path, source)));
            yield break;
        }
        if (IsConverted(path))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var dds = Dds(Picture(path, source), mips: Starts(path, GenerateMip));
            yield return ($"Art/Textures/{name}.dds", dds);
            foreach (var (from, to) in SecondNames)
                if (name.Equals(from, StringComparison.OrdinalIgnoreCase)) yield return ($"Art/Textures/{to}.dds", dds);
            yield break;
        }
        yield return (path, path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? WithoutOptional(source) : source);
    }

    /// <summary>The bundle's <c>excludeMarkersList</c>: from a line with the optional marker to the line that ends the block, both included. Every other byte stays.</summary>
    public static byte[] WithoutOptional(byte[] source)
    {
        var text = Encoding.Latin1.GetString(source);
        if (!text.Contains(OptionalBegin, StringComparison.Ordinal)) return source;
        var result = new StringBuilder(text.Length);
        var skipping = false;
        for (var start = 0; start < text.Length;)
        {
            var end = text.IndexOf('\n', start);
            end = end < 0 ? text.Length : end + 1;
            var line = text[start..end];
            start = end;
            if (line.Contains(OptionalBegin, StringComparison.Ordinal)) skipping = true;
            else if (line.Contains(OptionalEnd, StringComparison.Ordinal)) skipping = false;
            else if (!skipping) result.Append(line);
        }
        return Encoding.Latin1.GetBytes(result.ToString());
    }

    /// <summary>
    /// The multi-language string file as the English CSF (<c>CsfReader</c> reads it): a label, one <c>XX: "text"</c> line per language, <c>END</c>. The first text of the
    /// language counts, as the game keeps the first value of a label; a text stays as written apart from its escapes, the game's own whitespace rules apply when it is read.
    /// </summary>
    public static byte[] Strings(byte[] source)
    {
        var labels = new List<(string Label, string Text, string Speech)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? label = null;
        string? text = null;
        var speech = string.Empty;
        var skipping = false;
        var number = 0;
        foreach (var raw in Encoding.UTF8.GetString(source).Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                if (line.Contains(OptionalBegin, StringComparison.Ordinal)) skipping = true;
                else if (line.Contains(OptionalEnd, StringComparison.Ordinal)) skipping = false;
                continue;
            }
            if (line.Length == 0 || skipping) continue;
            if (label is null)
            {
                label = line;
                if (!seen.Add(label)) throw new InvalidDataException($"The patch's string file defines '{label}' again (line {number}).");
                text = null;
                speech = string.Empty;
                continue;
            }
            if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                labels.Add((label, text ?? throw new InvalidDataException($"'{label}' of the patch's string file has no {Language} text (line {number})."), speech));
                label = null;
                continue;
            }
            if (line.Length < 3 || line[2] != ':') throw new InvalidDataException($"Line {number} of the patch's string file is neither a text nor END: '{line}'.");
            if (text is null && line.StartsWith(Language, StringComparison.Ordinal)) (text, speech) = Quoted(line[3..].Trim());
        }
        if (label is not null) throw new InvalidDataException($"'{label}' of the patch's string file has no END.");

        using var output = new MemoryStream();
        Tag(output, "CSF ");
        foreach (var value in new[] { 3, labels.Count, labels.Count, 0, 0 }) Integer(output, value);
        foreach (var (name, value, voice) in labels)
        {
            Tag(output, "LBL ");
            Integer(output, 1);
            Integer(output, name.Length);
            output.Write(Encoding.ASCII.GetBytes(name));
            Tag(output, voice.Length == 0 ? "STR " : "STRW");
            Integer(output, value.Length);
            foreach (var character in value)
            {
                output.WriteByte((byte)~(character & 0xff));
                output.WriteByte((byte)~(character >> 8));
            }
            if (voice.Length == 0) continue;
            Integer(output, voice.Length);
            output.Write(Encoding.ASCII.GetBytes(voice));
        }
        return output.ToArray();
    }

    private static (string Text, string Speech) Quoted(string value)
    {
        if (!value.StartsWith('"')) throw new InvalidDataException($"A text of the patch's string file is not quoted: '{value}'.");
        var text = new StringBuilder(value.Length);
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"') return (text.ToString(), value[(index + 1)..].Trim());
            if (character != '\\') { text.Append(character); continue; }
            index++;
            if (index == value.Length) break;
            text.Append(value[index] switch { 't' => '\t', 'n' => '\n', var other => other });
        }
        throw new InvalidDataException($"A text of the patch's string file does not end: '{value}'.");
    }

    /// <summary>An 8-bit RGB picture (<c>GameFilesEdited</c> has no other), top row first, four bytes a pixel.</summary>
    private static TgaImage Picture(string path, byte[] source)
    {
        if (path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) return Psd(source, path);
        using var stream = new MemoryStream(source);
        return TgaReader.Read(stream);
    }

    /// <summary>The merged image of a Photoshop file: channels one after the other, raw or packed row by row (PackBits), the fourth channel being alpha.</summary>
    public static TgaImage Psd(byte[] file, string name)
    {
        var data = file.AsSpan();
        if (data.Length < 26 || !data[..4].SequenceEqual("8BPS"u8) || BinaryPrimitives.ReadUInt16BigEndian(data[4..]) != 1)
            throw new InvalidDataException($"'{name}' is not a Photoshop file of version 1.");
        var channels = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[14..]));
        var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[18..]));
        var depth = BinaryPrimitives.ReadUInt16BigEndian(data[22..]);
        var mode = BinaryPrimitives.ReadUInt16BigEndian(data[24..]);
        if (depth != 8 || mode != 3 || channels is < 3 or > 4) throw new InvalidDataException($"'{name}' has {channels} channels of {depth} bits in mode {mode}; only 8-bit RGB is known.");
        var position = 26;
        for (var section = 0; section < 3; section++) position = checked(position + 4 + (int)BinaryPrimitives.ReadUInt32BigEndian(data[position..]));
        var compression = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
        position += 2;
        var planes = new byte[channels][];
        for (var channel = 0; channel < channels; channel++) planes[channel] = new byte[width * height];
        if (compression == 0)
        {
            foreach (var plane in planes)
            {
                data.Slice(position, plane.Length).CopyTo(plane);
                position += plane.Length;
            }
        }
        else if (compression == 1)
        {
            var counts = position;
            position += 2 * channels * height;
            for (var channel = 0; channel < channels; channel++)
            {
                for (var row = 0; row < height; row++)
                {
                    var length = BinaryPrimitives.ReadUInt16BigEndian(data[(counts + 2 * (channel * height + row))..]);
                    Unpack(data.Slice(position, length), planes[channel].AsSpan(row * width, width), name);
                    position += length;
                }
            }
        }
        else
        {
            throw new InvalidDataException($"'{name}' uses compression {compression}.");
        }
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            pixels[index * 4] = planes[0][index];
            pixels[index * 4 + 1] = planes[1][index];
            pixels[index * 4 + 2] = planes[2][index];
            pixels[index * 4 + 3] = channels == 4 ? planes[3][index] : byte.MaxValue;
        }
        return new TgaImage(width, height, pixels);
    }

    private static void Unpack(ReadOnlySpan<byte> packed, Span<byte> row, string name)
    {
        var read = 0;
        var written = 0;
        while (read < packed.Length && written < row.Length)
        {
            int header = (sbyte)packed[read++];
            if (header >= 0)
            {
                packed.Slice(read, header + 1).CopyTo(row[written..]);
                read += header + 1;
                written += header + 1;
            }
            else if (header != sbyte.MinValue)
            {
                row.Slice(written, 1 - header).Fill(packed[read++]);
                written += 1 - header;
            }
        }
        if (written != row.Length) throw new InvalidDataException($"A packed row of '{name}' gives {written} of {row.Length} bytes.");
    }

    /// <summary>Uncompressed BGRA with the mip chain down to one pixel when <paramref name="mips"/>, each level the average of four pixels of the one above.</summary>
    public static byte[] Dds(TgaImage image, bool mips)
    {
        var levels = new List<TgaImage> { image };
        while (mips && (levels[^1].Width > 1 || levels[^1].Height > 1)) levels.Add(Half(levels[^1]));
        using var output = new MemoryStream();
        const int Caps = 0x1, Height = 0x2, Width = 0x4, Pitch = 0x8, PixelFormat = 0x1000, MipCount = 0x20000;
        const int Texture = 0x1000, Complex = 0x8, MipMap = 0x400000, RgbWithAlpha = 0x41;
        Tag(output, " SDD");
        foreach (var value in new[] { 124, Caps | Height | Width | Pitch | PixelFormat | (mips ? MipCount : 0), image.Height, image.Width, image.Width * 4, 0, levels.Count }) Integer(output, value);
        for (var reserved = 0; reserved < 11; reserved++) Integer(output, 0);
        foreach (var value in new[] { 32, RgbWithAlpha, 0, 32, 0x00ff0000, 0x0000ff00, 0x000000ff, unchecked((int)0xff000000) }) Integer(output, value);
        Integer(output, Texture | (mips ? Complex | MipMap : 0));
        for (var reserved = 0; reserved < 4; reserved++) Integer(output, 0);
        foreach (var level in levels)
        {
            var pixels = level.RgbaPixels;
            for (var index = 0; index < pixels.Length; index += 4)
            {
                output.WriteByte(pixels[index + 2]);
                output.WriteByte(pixels[index + 1]);
                output.WriteByte(pixels[index]);
                output.WriteByte(pixels[index + 3]);
            }
        }
        return output.ToArray();
    }

    private static TgaImage Half(TgaImage image)
    {
        var (width, height) = (Math.Max(1, image.Width / 2), Math.Max(1, image.Height / 2));
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var (left, top) = (x * image.Width / width, y * image.Height / height);
                    var (right, bottom) = (image.Width > 1 ? left + 1 : left, image.Height > 1 ? top + 1 : top);
                    var sum = Sample(image, left, top, channel) + Sample(image, right, top, channel) + Sample(image, left, bottom, channel) + Sample(image, right, bottom, channel);
                    pixels[(y * width + x) * 4 + channel] = (byte)((sum + 2) / 4);
                }
            }
        }
        return new TgaImage(width, height, pixels);
    }

    private static int Sample(TgaImage image, int x, int y, int channel) => image.RgbaPixels[(y * image.Width + x) * 4 + channel];

    /// <summary>Uncompressed, 32 bits, top row first.</summary>
    private static byte[] Tga(TgaImage image)
    {
        var output = new byte[18 + image.RgbaPixels.Length];
        output[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(12), (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(14), (ushort)image.Height);
        output[16] = 32;
        output[17] = 0x28;
        for (var index = 0; index < image.RgbaPixels.Length; index += 4)
        {
            output[18 + index] = image.RgbaPixels[index + 2];
            output[19 + index] = image.RgbaPixels[index + 1];
            output[20 + index] = image.RgbaPixels[index];
            output[21 + index] = image.RgbaPixels[index + 3];
        }
        return output;
    }

    private static bool Starts(string path, string prefix) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static void Tag(Stream output, string tag)
    {
        var bytes = Encoding.ASCII.GetBytes(tag);
        Array.Reverse(bytes);
        output.Write(bytes);
    }

    private static void Integer(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }
}
