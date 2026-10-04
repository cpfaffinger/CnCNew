using System.Buffers.Binary;

namespace CnCNew.Images.Tests;

/// <summary>Builds tiny synthetic <c>.cur</c> and <c>.ani</c> files byte by byte (no excerpt of any shipped cursor).</summary>
internal static class CursorFixtures
{
    /// <summary>
    /// A cursor file with one 24-bit image: <paramref name="pixels"/> are top-down rows of (red, green, blue), <paramref name="transparent"/> the
    /// top-down AND mask (true = the screen shows through).
    /// </summary>
    public static byte[] Cur(int width, int height, int hotspotX, int hotspotY, (byte R, byte G, byte B)[] pixels, bool[] transparent)
    {
        var colorStride = (width * 24 + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var bitmap = new byte[40 + (colorStride + maskStride) * height];
        BinaryPrimitives.WriteUInt32LittleEndian(bitmap, 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmap.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmap.AsSpan(14), 24);
        for (var y = 0; y < height; y++)
        {
            var row = 40 + (height - 1 - y) * colorStride;
            var maskRow = 40 + colorStride * height + (height - 1 - y) * maskStride;
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = pixels[y * width + x];
                bitmap[row + x * 3] = blue;
                bitmap[row + x * 3 + 1] = green;
                bitmap[row + x * 3 + 2] = red;
                if (transparent[y * width + x]) bitmap[maskRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        return Directory(2, (byte)width, (byte)height, (ushort)hotspotX, (ushort)hotspotY, bitmap);
    }

    /// <summary>A cursor file with one 4-bit palette image (the format of the retail cursors): top-down palette indices and mask.</summary>
    public static byte[] PaletteCur(int width, int height, int hotspotX, int hotspotY, (byte R, byte G, byte B)[] palette, byte[] indices, bool[] transparent)
    {
        var colorStride = (width * 4 + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var bitmap = new byte[40 + 64 + (colorStride + maskStride) * height];
        BinaryPrimitives.WriteUInt32LittleEndian(bitmap, 40);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmap.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmap.AsSpan(14), 4);
        for (var index = 0; index < palette.Length; index++)
        {
            bitmap[40 + index * 4] = palette[index].B;
            bitmap[40 + index * 4 + 1] = palette[index].G;
            bitmap[40 + index * 4 + 2] = palette[index].R;
        }
        for (var y = 0; y < height; y++)
        {
            var row = 104 + (height - 1 - y) * colorStride;
            var maskRow = 104 + colorStride * height + (height - 1 - y) * maskStride;
            for (var x = 0; x < width; x++)
            {
                bitmap[row + (x >> 1)] |= (byte)(indices[y * width + x] << ((x & 1) == 0 ? 4 : 0));
                if (transparent[y * width + x]) bitmap[maskRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        return Directory(2, (byte)width, (byte)height, (ushort)hotspotX, (ushort)hotspotY, bitmap);
    }

    private static byte[] Directory(ushort type, byte width, byte height, ushort hotspotX, ushort hotspotY, byte[] bitmap)
    {
        var file = new byte[22 + bitmap.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);
        file[6] = width;
        file[7] = height;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10), hotspotX);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), hotspotY);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), (uint)bitmap.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 22);
        bitmap.CopyTo(file, 22);
        return file;
    }

    /// <summary>
    /// An animated cursor of the given frames (each a whole <c>.cur</c> file). <paramref name="sequence"/> and <paramref name="rates"/> are
    /// optional tables; <paramref name="sizeIncludesHeader"/> reproduces the retail quirk of writing the whole file size into the RIFF size field.
    /// </summary>
    public static byte[] Ani(byte[][] frames, int defaultRate, int[]? sequence = null, int[]? rates = null, bool sizeIncludesHeader = false, string? extraChunk = null)
    {
        using var stream = new MemoryStream();
        void Raw(ReadOnlySpan<byte> bytes) => stream.Write(bytes);
        void Dword(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }
        void Chunk(string id, byte[] body)
        {
            Raw(System.Text.Encoding.ASCII.GetBytes(id));
            Dword((uint)body.Length);
            Raw(body);
            if ((body.Length & 1) != 0) stream.WriteByte(0);
        }
        byte[] Table(int[] values)
        {
            var body = new byte[values.Length * 4];
            for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(index * 4), (uint)values[index]);
            return body;
        }

        Raw("RIFF"u8);
        Dword(0);
        Raw("ACON"u8);
        // An INFO list with an odd-sized title exercises the word alignment of chunks.
        Chunk("LIST", [.."INFO"u8, .."INAM"u8, 5, 0, 0, 0, (byte)'T', (byte)'e', (byte)'s', (byte)'t', 0, 0]);
        var steps = sequence?.Length ?? frames.Length;
        Chunk("anih", Table([36, frames.Length, steps, 0, 0, 0, 0, defaultRate, sequence is null ? 1 : 3]));
        if (rates is not null) Chunk("rate", Table(rates));
        if (sequence is not null) Chunk("seq ", Table(sequence));
        if (extraChunk is not null) Chunk(extraChunk, [1, 2, 3, 4]);
        using (var list = new MemoryStream())
        {
            list.Write("fram"u8);
            foreach (var frame in frames)
            {
                list.Write("icon"u8);
                var size = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)frame.Length);
                list.Write(size);
                list.Write(frame);
                if ((frame.Length & 1) != 0) list.WriteByte(0);
            }
            Chunk("LIST", list.ToArray());
        }
        var file = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(sizeIncludesHeader ? file.Length : file.Length - 8));
        return file;
    }
}
