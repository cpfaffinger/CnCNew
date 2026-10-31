using System.Buffers.Binary;

namespace CnCNew.Tests;

// Synthetic Bink files for the tests of the decoder and of the players above it: nothing here comes from a game file.

/// <summary>Writes the Bink bit stream: values from their lowest bit on, 32-bit alignment from the start of the packet.</summary>
public sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private int _used;

    public int Bits => _bytes.Count * 8 - (_used == 0 ? 0 : 8 - _used);

    public BitWriter Put(uint value, int count)
    {
        for (var bit = 0; bit < count; bit++)
        {
            if (_used == 0) _bytes.Add(0);
            if (((value >> bit) & 1) != 0) _bytes[^1] |= (byte)(1 << _used);
            _used = (_used + 1) & 7;
        }
        return this;
    }

    public BitWriter Align32()
    {
        while (Bits % 32 != 0) Put(0, 1);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>A plane in the simplest form the format has: every tree is the identity tree, so a symbol is four plain bits.</summary>
public sealed class PlaneWriter(BitWriter bits, int blocksWide)
{
    private readonly bool[] _closed = new bool[8];
    private int Width => blocksWide * 8;
    private int TypeLength => int.Log2((Width >> 3) + 511) + 1;
    private int SubTypeLength => int.Log2((Width >> 4) + 511) + 1;
    private int ColorLength => int.Log2(blocksWide * 64 + 511) + 1;
    private int PatternLength => int.Log2((blocksWide << 3) + 511) + 1;
    private int RunLength => int.Log2(blocksWide * 48 + 511) + 1;

    public void Bundles()
    {
        for (var tree = 0; tree < 2; tree++) bits.Put(0, 4); // block types, sub block types
        for (var tree = 0; tree < 16; tree++) bits.Put(0, 4); // the high nibbles of the colours
        bits.Put(0, 4); // the low nibbles
        bits.Put(0, 4); // patterns
        bits.Put(0, 4); // x offsets
        bits.Put(0, 4); // y offsets
        bits.Put(0, 4); // runs
    }

    /// <summary>One row of blocks: its types, then the values the row needs of each bundle. A bundle that gets none says so once; the decoder then asks it nothing more in this plane.</summary>
    public void Row(int[] types, int[]? subTypes = null, int[]? colors = null, int[]? patterns = null, int[]? xOffsets = null, int[]? yOffsets = null, int[]? intraDc = null, int[]? interDc = null)
    {
        bits.Put((uint)types.Length, TypeLength).Put(0, 1);
        foreach (var type in types) bits.Put((uint)type, 4);
        if (subTypes is null) Close(0, SubTypeLength);
        else
        {
            bits.Put((uint)subTypes.Length, SubTypeLength).Put(0, 1);
            foreach (var value in subTypes) bits.Put((uint)value, 4);
        }
        if (colors is null) Close(1, ColorLength);
        else
        {
            bits.Put((uint)colors.Length, ColorLength).Put(0, 1);
            foreach (var value in colors) bits.Put((uint)(value >> 4), 4).Put((uint)(value & 15), 4);
        }
        if (patterns is null) Close(2, PatternLength);
        else
        {
            bits.Put((uint)patterns.Length, PatternLength);
            foreach (var pattern in patterns) bits.Put((uint)(pattern & 15), 4).Put((uint)(pattern >> 4), 4);
        }
        Motion(3, xOffsets);
        Motion(4, yOffsets);
        Dc(5, intraDc, signed: false);
        Dc(6, interDc, signed: true);
        Close(7, RunLength);
    }

    private void Close(int bundle, int length)
    {
        if (_closed[bundle]) return;
        _closed[bundle] = true;
        bits.Put(0, length);
    }

    private void Motion(int bundle, int[]? values)
    {
        if (values is null)
        {
            Close(bundle, TypeLength);
            return;
        }
        bits.Put((uint)values.Length, TypeLength).Put(0, 1);
        foreach (var value in values)
        {
            bits.Put((uint)Math.Abs(value), 4);
            if (value != 0) bits.Put(value < 0 ? 1u : 0u, 1);
        }
    }

    private void Dc(int bundle, int[]? values, bool signed)
    {
        if (values is null)
        {
            Close(bundle, TypeLength);
            return;
        }
        bits.Put((uint)values.Length, TypeLength);
        bits.Put((uint)Math.Abs(values[0]), signed ? 10 : 11);
        if (signed && values[0] != 0) bits.Put(values[0] < 0 ? 1u : 0u, 1);
        for (var index = 1; index < values.Length; index += 8)
        {
            // Groups of up to eight values share a bit width.
            var group = values[index..Math.Min(index + 8, values.Length)];
            bits.Put(12, 4);
            var last = values[index - 1];
            foreach (var value in group)
            {
                var difference = value - last;
                bits.Put((uint)Math.Abs(difference), 12);
                if (difference != 0) bits.Put(difference < 0 ? 1u : 0u, 1);
                last = value;
            }
        }
    }
}

public static class BinkFixtures
{
    public const int Skip = 0, Scaled = 1, Motion = 2, Intra = 5, Fill = 6, Pattern = 8;

    /// <summary>A file of one video size whose frames are <paramref name="frames"/>, optionally with one audio track per frame (the size word is written here).</summary>
    public static byte[] File(int width, int height, byte[][] frames, byte[][]? audio = null, uint flags = 0, char revision = 'i', ushort audioFlags = 0x7000, int sampleRate = 44100)
    {
        var tracks = audio is null ? 0 : 1;
        var headerLength = 44 + tracks * 12 + (frames.Length + 1) * 4;
        var bodies = new byte[frames.Length][];
        for (var frame = 0; frame < frames.Length; frame++)
        {
            using var body = new MemoryStream();
            if (audio is not null)
            {
                body.Write(BitConverter.GetBytes(audio[frame].Length));
                body.Write(audio[frame]);
            }
            body.Write(frames[frame]);
            // The index's lowest bit is the key flag, so a frame starts at an even offset.
            if (body.Length % 2 != 0) body.WriteByte(0);
            bodies[frame] = body.ToArray();
        }
        var total = headerLength + bodies.Sum(body => body.Length);
        var data = new byte[total];
        var span = data.AsSpan();
        "BIK"u8.CopyTo(span);
        data[3] = (byte)revision;
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(total - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)frames.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)bodies.Max(body => body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)frames.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 30);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)tracks);
        var offset = 44;
        if (tracks == 1)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], 1234);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 4)..], (ushort)sampleRate);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 6)..], audioFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 8)..], 7);
            offset += 12;
        }
        var position = headerLength;
        for (var frame = 0; frame <= frames.Length; frame++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], (uint)position | (frame == 0 ? 1u : 0u));
            offset += 4;
            if (frame < frames.Length)
            {
                bodies[frame].CopyTo(span[position..]);
                position += bodies[frame].Length;
            }
        }
        return data;
    }

    /// <summary>A 16 x 16 frame of filled 8 x 8 luminance blocks and one filled block in each chroma plane.</summary>
    public static byte[] FilledFrame(int[] luma, int v, int u)
    {
        var bits = new BitWriter();
        bits.Put(0, 32);
        LumaFill(bits, luma);
        bits.Align32();
        ChromaFill(bits, v);
        bits.Align32();
        ChromaFill(bits, u);
        bits.Align32();
        return bits.ToArray();
    }

    public static void LumaFill(BitWriter bits, int[] luma)
    {
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        plane.Row([Fill, Fill], colors: [luma[0], luma[1]]);
        plane.Row([Fill, Fill], colors: [luma[2], luma[3]]);
    }

    public static void ChromaFill(BitWriter bits, int value)
    {
        var plane = new PlaneWriter(bits, 1);
        plane.Bundles();
        plane.Row([Fill], colors: [value]);
    }

    /// <summary>A movie of <paramref name="frames"/> filled 16 x 16 frames at 30 frames a second, each a step lighter, without sound.</summary>
    public static byte[] FilledMovie(int frames)
    {
        var bodies = new byte[frames][];
        for (var frame = 0; frame < frames; frame++)
        {
            var luma = 16 + frame % 200;
            bodies[frame] = FilledFrame([luma, luma, luma, luma], 128, 128);
        }
        return File(16, 16, bodies);
    }

    /// <summary>An audio packet of one block per <paramref name="blocks"/> entry: the first two coefficients of each channel, every other coefficient zero.</summary>
    public static byte[] AudioPacket(int channels, int frameLength, (float First, float Second)[] blocks)
    {
        const int bandCount = 25;
        var bits = new BitWriter();
        bits.Put(0, 32);
        foreach (var (first, second) in blocks)
        {
            bits.Put(0, 2);
            for (var channel = 0; channel < channels; channel++)
            {
                PutFloat(bits, first);
                PutFloat(bits, second);
                for (var band = 0; band < bandCount; band++) bits.Put(0, 8);
                for (var position = 2; position < frameLength;)
                {
                    bits.Put(1, 1).Put(15, 4).Put(0, 4); // a run of 64 x 8 coefficients of width 0
                    position += 512;
                }
            }
            bits.Align32();
        }
        return bits.ToArray();
    }

    private static void PutFloat(BitWriter bits, float value)
    {
        var magnitude = MathF.Abs(value);
        var power = magnitude == 0f ? 0 : Math.Clamp(MathF.ILogB(magnitude) + 1, 0, 31);
        bits.Put((uint)power, 5).Put((uint)MathF.Round(MathF.ScaleB(magnitude, 23 - power)), 23).Put(value < 0 ? 1u : 0u, 1);
    }
}
