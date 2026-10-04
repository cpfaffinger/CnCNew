using System.Buffers.Binary;
using System.Collections.Immutable;

namespace CnCNew.Images;

/// <summary>
/// One decoded cursor picture: straight-alpha RGBA rows from the top, and the hot spot (the pixel that sits on the pointer position).
/// <paramref name="InvertedPixels"/> counts "invert the screen" pixels (AND mask set, XOR colour not black), which a blended cursor cannot
/// reproduce; they are decoded as opaque pixels of their XOR colour and counted so that a caller can report them.
/// </summary>
public sealed record CursorImage(int Width, int Height, int HotspotX, int HotspotY, byte[] RgbaPixels, int InvertedPixels = 0);

/// <summary>One step of an animated cursor: which frame, and for how many jiffies (1/60 s) it stays.</summary>
public readonly record struct AniStep(int Frame, int Jiffies);

/// <summary>A decoded animated cursor (<c>RIFF ACON</c>): the distinct frames and the play order with a duration per step; the animation loops.</summary>
public sealed record AniCursor(ImmutableArray<CursorImage> Frames, ImmutableArray<AniStep> Steps)
{
    public const int JiffiesPerSecond = 60;

    public int TotalJiffies { get; } = Steps.Sum(step => step.Jiffies);

    /// <summary>The frame shown <paramref name="milliseconds"/> after the cursor became current (looping).</summary>
    public int FrameIndexAt(long milliseconds)
    {
        if (Steps.Length == 1 || TotalJiffies <= 0) return Steps[0].Frame;
        var position = Math.Max(0, milliseconds) * JiffiesPerSecond / 1000 % TotalJiffies;
        foreach (var step in Steps)
        {
            if (position < step.Jiffies) return step.Frame;
            position -= step.Jiffies;
        }
        return Steps[^1].Frame;
    }

    public CursorImage FrameAt(long milliseconds) => Frames[FrameIndexAt(milliseconds)];
}

/// <summary>
/// Decoder for Windows cursor files: <c>.cur</c>/<c>.ico</c> (an image directory followed by device-independent bitmaps with a colour
/// ("XOR") image and a 1-bit transparency ("AND") mask) and <c>.ani</c> (a RIFF container of such images with an optional rate and
/// sequence table). Written from the public file format descriptions; everything the decoder does not understand is an
/// <see cref="InvalidDataException"/>, never a guessed picture.
/// </summary>
public static class CursorReader
{
    private const int MaximumDimension = 256;
    private const int MaximumFrames = 4096;

    /// <summary>Every image of a <c>.cur</c> or <c>.ico</c> file, in directory order. An icon has no hot spot: its centre is used, as Windows does.</summary>
    public static ImmutableArray<CursorImage> ReadCur(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6) throw new InvalidDataException("Cursor file is shorter than its header.");
        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data);
        var type = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (reserved != 0 || type is not (1 or 2)) throw new InvalidDataException($"Not a cursor or icon file (reserved {reserved}, type {type}).");
        if (count == 0) throw new InvalidDataException("Cursor file has no images.");
        if (data.Length < 6 + 16 * count) throw new InvalidDataException("Cursor directory is truncated.");
        var images = ImmutableArray.CreateBuilder<CursorImage>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = data.Slice(6 + 16 * index, 16);
            var hotspotX = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
            var hotspotY = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset > data.Length || size > data.Length - offset) throw new InvalidDataException($"Cursor image {index} lies outside the file.");
            images.Add(ReadBitmap(data.Slice((int)offset, (int)size), type == 2 ? (hotspotX, hotspotY) : null, index));
        }
        return images.MoveToImmutable();
    }

    /// <summary>An animated cursor. A frame with several images contributes its largest one (ties: the later, deeper entry).</summary>
    public static AniCursor ReadAni(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("ACON"u8))
            throw new InvalidDataException("Not a RIFF ACON animated cursor.");
        var declaredEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        // The shipped cursors write the whole file size into the size field (which should exclude the first eight bytes); see LegacyAssetReaders.ReadRiff.
        if (declaredEnd != data.Length && declaredEnd != data.Length + 8L)
            throw new InvalidDataException($"RIFF length {declaredEnd} does not match the file length {data.Length}.");

        int? frameCount = null, stepCount = null, defaultRate = null;
        var flags = 0u;
        uint[]? rates = null, sequence = null;
        var frames = ImmutableArray.CreateBuilder<CursorImage>();
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var id = data.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            var body = position + 8;
            if (size > data.Length - body) throw new InvalidDataException($"ANI chunk '{Name(id)}' at {position} lies outside the file.");
            var chunk = data.Slice(body, (int)size);
            if (id.SequenceEqual("anih"u8))
            {
                if (chunk.Length < 36) throw new InvalidDataException("ANI header chunk is shorter than 36 bytes.");
                frameCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]));
                stepCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[8..]));
                defaultRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(chunk[28..]));
                flags = BinaryPrimitives.ReadUInt32LittleEndian(chunk[32..]);
                if (frameCount is < 1 or > MaximumFrames || stepCount is < 1 or > MaximumFrames) throw new InvalidDataException($"ANI header declares {frameCount} frames and {stepCount} steps.");
                if ((flags & 1) == 0) throw new InvalidDataException("ANI frames are raw bitmaps (AF_ICON not set), which is not supported.");
            }
            else if (id.SequenceEqual("rate"u8)) rates = ReadTable(chunk, "rate");
            else if (id.SequenceEqual("seq "u8)) sequence = ReadTable(chunk, "seq ");
            else if (id.SequenceEqual("LIST"u8))
            {
                if (chunk.Length < 4) throw new InvalidDataException("ANI LIST chunk without a type.");
                if (chunk[..4].SequenceEqual("fram"u8)) ReadFrames(chunk[4..], frames);
                else if (!chunk[..4].SequenceEqual("INFO"u8)) throw new InvalidDataException($"Unknown ANI list '{Name(chunk[..4])}'.");
            }
            else throw new InvalidDataException($"Unknown ANI chunk '{Name(id)}' at {position}.");
            position = body + (int)size + ((int)size & 1);
        }

        if (frameCount is not { } declaredFrames || stepCount is not { } steps || defaultRate is not { } rate) throw new InvalidDataException("ANI file has no header chunk.");
        if (frames.Count != declaredFrames) throw new InvalidDataException($"ANI header declares {declaredFrames} frames, the file holds {frames.Count}.");
        if (rates is not null && rates.Length != steps) throw new InvalidDataException($"ANI rate table has {rates.Length} entries for {steps} steps.");
        var hasSequence = (flags & 2) != 0;
        if (hasSequence != (sequence is not null)) throw new InvalidDataException("ANI sequence flag and sequence chunk disagree.");
        if (sequence is not null && sequence.Length != steps) throw new InvalidDataException($"ANI sequence table has {sequence.Length} entries for {steps} steps.");
        if (sequence is null && steps != declaredFrames) throw new InvalidDataException($"ANI without a sequence table has {steps} steps for {declaredFrames} frames.");

        var order = ImmutableArray.CreateBuilder<AniStep>(steps);
        for (var index = 0; index < steps; index++)
        {
            var frame = sequence is null ? index : checked((int)sequence[index]);
            if (frame >= declaredFrames) throw new InvalidDataException($"ANI step {index} shows frame {frame} of {declaredFrames}.");
            // A rate of zero means "as fast as possible"; one jiffy is the shortest step this clock has.
            order.Add(new AniStep(frame, Math.Max(1, rates is null ? rate : checked((int)rates[index]))));
        }
        return new AniCursor(frames.ToImmutable(), order.MoveToImmutable());
    }

    private static void ReadFrames(ReadOnlySpan<byte> list, ImmutableArray<CursorImage>.Builder frames)
    {
        var position = 0;
        while (position + 8 <= list.Length)
        {
            var id = list.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(list[(position + 4)..]);
            if (size > list.Length - position - 8) throw new InvalidDataException("ANI frame chunk lies outside its list.");
            if (!id.SequenceEqual("icon"u8)) throw new InvalidDataException($"Unknown ANI frame chunk '{Name(id)}'.");
            if (frames.Count >= MaximumFrames) throw new InvalidDataException("ANI file has too many frames.");
            var images = ReadCur(list.Slice(position + 8, (int)size));
            var best = images[0];
            foreach (var image in images)
                if (image.Width * image.Height >= best.Width * best.Height) best = image;
            frames.Add(best);
            position += 8 + (int)size + ((int)size & 1);
        }
    }

    private static uint[] ReadTable(ReadOnlySpan<byte> chunk, string name)
    {
        if (chunk.Length % 4 != 0 || chunk.Length / 4 > MaximumFrames) throw new InvalidDataException($"ANI '{name}' table has an invalid length {chunk.Length}.");
        var values = new uint[chunk.Length / 4];
        for (var index = 0; index < values.Length; index++) values[index] = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(index * 4)..]);
        return values;
    }

    /// <summary>A bitmap of a cursor resource: <c>BITMAPINFOHEADER</c>, palette, colour rows and mask rows, both bottom-up and padded to four bytes.</summary>
    private static CursorImage ReadBitmap(ReadOnlySpan<byte> data, (int X, int Y)? hotspot, int index)
    {
        if (data.Length >= 8 && data[..4].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }))
            throw new InvalidDataException($"Cursor image {index} is PNG compressed, which is not supported.");
        if (data.Length < 40) throw new InvalidDataException($"Cursor image {index} is shorter than its bitmap header.");
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var doubleHeight = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
        if (headerSize != 40) throw new InvalidDataException($"Cursor image {index} has a {headerSize} byte bitmap header (expected 40).");
        if (compression != 0) throw new InvalidDataException($"Cursor image {index} uses bitmap compression {compression}.");
        if (bits is not (1 or 4 or 8 or 24 or 32)) throw new InvalidDataException($"Cursor image {index} has {bits} bits per pixel.");
        if (width is < 1 or > MaximumDimension || doubleHeight < 2 || doubleHeight % 2 != 0 || doubleHeight / 2 > MaximumDimension)
            throw new InvalidDataException($"Cursor image {index} has invalid dimensions {width}x{doubleHeight}/2.");
        var height = doubleHeight / 2;

        var paletteCount = bits > 8 ? 0 : colorsUsed == 0 ? 1 << bits : checked((int)colorsUsed);
        if (paletteCount > 1 << bits) throw new InvalidDataException($"Cursor image {index} declares {paletteCount} palette entries for {bits} bits.");
        var paletteOffset = 40;
        var colorStride = (width * bits + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var colorOffset = paletteOffset + 4 * paletteCount;
        var maskOffset = colorOffset + colorStride * height;
        if (data.Length < maskOffset + maskStride * height) throw new InvalidDataException($"Cursor image {index} is truncated.");

        var rgba = new byte[width * height * 4];
        var hasAlpha = false;
        if (bits == 32)
            for (var offset = colorOffset + 3; offset < maskOffset && !hasAlpha; offset += 4) hasAlpha = data[offset] != 0;
        var inverted = 0;
        for (var y = 0; y < height; y++)
        {
            var colorRow = data.Slice(colorOffset + (height - 1 - y) * colorStride, colorStride);
            var maskRow = data.Slice(maskOffset + (height - 1 - y) * maskStride, maskStride);
            for (var x = 0; x < width; x++)
            {
                byte red, green, blue, alpha = 255;
                switch (bits)
                {
                    case 32:
                        (blue, green, red) = (colorRow[x * 4], colorRow[x * 4 + 1], colorRow[x * 4 + 2]);
                        if (hasAlpha) alpha = colorRow[x * 4 + 3];
                        break;
                    case 24:
                        (blue, green, red) = (colorRow[x * 3], colorRow[x * 3 + 1], colorRow[x * 3 + 2]);
                        break;
                    default:
                    {
                        var paletteIndex = bits switch
                        {
                            8 => colorRow[x],
                            4 => (colorRow[x >> 1] >> ((x & 1) == 0 ? 4 : 0)) & 0x0f,
                            _ => (colorRow[x >> 3] >> (7 - (x & 7))) & 1,
                        };
                        if (paletteIndex >= paletteCount) throw new InvalidDataException($"Cursor image {index} uses palette entry {paletteIndex} of {paletteCount}.");
                        var entry = data.Slice(paletteOffset + 4 * paletteIndex, 4);
                        (blue, green, red) = (entry[0], entry[1], entry[2]);
                        break;
                    }
                }
                if (!hasAlpha && ((maskRow[x >> 3] >> (7 - (x & 7))) & 1) != 0)
                {
                    // Mask set: the screen shows through, XOR-ed with the colour. Black leaves it untouched (transparent); anything else inverts it.
                    if ((red | green | blue) == 0) alpha = 0;
                    else inverted++;
                }
                var target = (y * width + x) * 4;
                rgba[target] = red;
                rgba[target + 1] = green;
                rgba[target + 2] = blue;
                rgba[target + 3] = alpha;
            }
        }
        var (hotX, hotY) = hotspot ?? (width / 2, height / 2);
        if (hotX >= width || hotY >= height) throw new InvalidDataException($"Cursor image {index} has its hot spot {hotX},{hotY} outside its {width}x{height} pixels.");
        return new CursorImage(width, height, hotX, hotY, rgba, inverted);
    }

    private static string Name(ReadOnlySpan<byte> id) => System.Text.Encoding.ASCII.GetString(id).Replace('\0', '?');
}
