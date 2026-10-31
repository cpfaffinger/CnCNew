using System.Buffers.Binary;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Video;

var run = new Runner();
run.Test("Bit reader: least significant bit first, 32-bit alignment, overrun", Tests.BitReader);
run.Test("Header: audio tracks, frame index and rejection", Tests.Header);
run.Test("Container: frames, packets, what is refused", Tests.Container);
run.Test("Video: filled blocks, the plane order and the colours", Tests.FilledFrame);
run.Test("Video: DC-only intra blocks, skipped and moved blocks", Tests.IntraSkipAndMotion);
run.Test("Video: 16 x 16 blocks, doubled", Tests.ScaledBlocks);
run.Test("Video: an alpha plane comes first and reaches the picture", Tests.AlphaPlane);
run.Test("Video: a damaged frame is an error, never a crash", Tests.DamagedFrame);
run.Test("Audio: the inverse DCT equals its definition", Tests.InverseDct);
run.Test("Audio: a constant block, the overlap between blocks", Tests.AudioBlocks);

var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (contentDirectory is not null) run.Test("Real data: every movie's header parses", () => Tests.RealHeaders(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: a few movies decode twice to the same checksums", () => Tests.RealDecode(contentDirectory));
return run.Finish();

sealed class Runner
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} video test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine("All video tests passed.");
        return 0;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Near(double actual, double expected, double tolerance, string message)
    {
        if (!(Math.Abs(actual - expected) <= tolerance))
            throw new InvalidOperationException($"{message} (expected {expected} ± {tolerance}, got {actual})");
    }

    public static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}

/// <summary>Writes the Bink bit stream: values from their lowest bit on, 32-bit alignment from the start of the packet.</summary>
sealed class BitWriter
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
sealed class PlaneWriter(BitWriter bits, int blocksWide)
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

static class Fixtures
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

    public static byte[] Rgba(BinkMovie movie)
    {
        var rgba = new byte[movie.Width * movie.Height * 4];
        movie.CopyRgba(rgba);
        return rgba;
    }

    public static int Red(BinkMovie movie, int x, int y) => Rgba(movie)[(y * movie.Width + x) * 4];

    /// <summary>The red of a neutral picture of luminance <paramref name="y"/>: BT.601 in limited range.</summary>
    public static int Grey(int y) => Math.Clamp((298 * (y - 16) + 128) >> 8, 0, 255);

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

static class Tests
{
    public static void BitReader()
    {
        byte[] data = [0b1010_0110, 0xFF, 0x00, 0x80, 0x12, 0x34, 0, 0, 0, 0, 0, 0, 0, 0];
        var bits = new BinkBitReader(data, 0, 6);
        Check.That(bits.Read(1) == 0, "The first bit is the lowest of the first byte.");
        Check.That(bits.Read(3) == 0b011, "A value keeps its order from the low bit.");
        Check.That(bits.ReadBit() == false, "Bit 4.");
        Check.That(bits.Read(3) == 0b101, "Up to the end of the byte.");
        Check.That(bits.Read(12) == 0x0FF, "A value across two bytes.");
        Check.That(bits.Position == 20, "The position counts bits.");
        bits.AlignTo32();
        Check.That(bits.Position == 32, "Alignment is to the next multiple of 32.");
        Check.That(bits.Read(16) == 0x3412 && bits.BitsLeft == 0, "The last word, read to the end.");
        var reader = bits;
        Check.That(reader.Read(1) == 0, "A peek at the very end stays inside the padding.");
        Check.Throws<InvalidDataException>(() => { var over = reader; over.Read(1); }, "ended early");
    }

    public static void Header()
    {
        var file = Fixtures.File(640, 480, [new byte[16], new byte[16], new byte[16]], [new byte[4], new byte[4], new byte[4]], audioFlags: 0x3000);
        var header = BinkHeader.Parse(file, file.Length);
        Check.That(header.Signature == "BIKi" && header.FrameCount == 3 && header.Width == 640 && header.Height == 480, "Signature, frames and size.");
        Check.Near(header.FramesPerSecond, 30, 0.001, "Frame rate numerator/denominator.");
        Check.Near(header.DurationSeconds, 0.1, 1e-9, "Duration.");
        Check.That(header.AudioTracks.Single() == new BinkAudioTrack(7, 44100, 2, false, true, 1234), "Audio track rate, stereo and DCT flags.");
        Check.That(header.KeyFrames == 1, "Key frames from bit 0 of the frame index.");
        var read = BinkHeader.Read(new MemoryStream(file));
        Check.That(read with { AudioTracks = header.AudioTracks } == header && read.AudioTracks.SequenceEqual(header.AudioTracks), "Stream reading gives the same header.");
        Check.Throws<NotSupportedException>(() => BinkHeader.Parse([.. "KB2j"u8, .. new byte[60]], 64), "Bink 2");
        Check.Throws<InvalidDataException>(() => BinkHeader.Parse(file, file.Length + 1), "announces");
        var broken = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(44 + 12 + 4), 0);
        Check.Throws<InvalidDataException>(() => BinkHeader.Parse(broken, broken.Length), "frame index");
    }

    public static void Container()
    {
        byte[][] video = [[1, 2, 3, 4], [5, 6, 7, 8, 9, 10, 11, 12]];
        byte[][] audio = [new byte[8], new byte[2]];
        var data = Fixtures.File(16, 16, video, audio);
        var file = BinkFile.Open(data);
        var first = file.Packets(0);
        var second = file.Packets(1);
        Check.That(first.AudioLength == 8 && first.VideoLength == 4, "Audio and video of the first frame.");
        Check.That(second.AudioLength == 0 && second.VideoLength == 8, "An audio packet under four bytes counts as none.");
        Check.That(file.IsKeyFrame(0) && !file.IsKeyFrame(1), "The key flag of the index.");
        Check.That(BinkFile.Open(new NonSeekingStream(data)).Packets(1) == file.Packets(1), "A stream that cannot seek reads the same.");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(Fixtures.File(16, 16, video, revision: 'h')), "revision 'h'");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(Fixtures.File(16, 16, video, flags: 0x00020000)), "flags");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(Fixtures.File(16, 16, video, audio, audioFlags: 0x6000)), "RDFT");
        var lying = (byte[])data.Clone();
        var firstFrame = (int)(BinaryPrimitives.ReadUInt32LittleEndian(lying.AsSpan(44 + 12)) & ~1u);
        BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(firstFrame), 4096);
        Check.Throws<InvalidDataException>(() => BinkFile.Open(lying).Packets(0), "announces");
    }

    public static void FilledFrame()
    {
        var movie = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [Fixtures.FilledFrame([16, 235, 128, 16], 128, 128), Fixtures.FilledFrame([81, 81, 81, 81], 240, 90)])));
        Check.That(movie.DecodeNextFrame(), "The first frame.");
        var rgba = Fixtures.Rgba(movie);
        Check.That(rgba[0] == 0 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 255, "Y 16 is black, without alpha opaque.");
        Check.That(rgba[(0 * 16 + 8) * 4] == 255, "Y 235 is white, top right block.");
        Check.That(rgba[(8 * 16 + 0) * 4] == 130, "Y 128 is a middle grey, bottom left block.");
        Check.That(movie.DecodeNextFrame(), "The second frame.");
        rgba = Fixtures.Rgba(movie);
        // BT.601 limited range; the first chroma plane of the stream is V.
        Check.That(rgba[0] == 255 && rgba[1] == 0 && rgba[2] == 0, $"V 240, U 90, Y 81 is pure red, got {rgba[0]},{rgba[1]},{rgba[2]}.");
        Check.That(!movie.DecodeNextFrame() && movie.IsAtEnd && movie.FramesDecoded == 2, "The end of the movie.");
        Check.That(Fixtures.Rgba(movie)[0] == 255, "The last picture stays.");
    }

    public static void IntraSkipAndMotion()
    {
        // Frame 1: two intra blocks with a DC value alone over two filled ones. After the matrix (11 fractional bits) and the transform (8) the DC is the pixel.
        var quant = BinkTables.IntraQuant[0];
        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        var dc = (200 << 19) / quant;
        var half = (100 << 19) / quant;
        var black = (16 << 19) / quant + 1;
        plane.Row([Fixtures.Intra, Fixtures.Intra], intraDc: [dc, half]);
        bits.Put(0, 4).Put(0, 4); // no coefficient bits, quantiser 0
        bits.Put(0, 4).Put(0, 4);
        plane.Row([Fixtures.Intra, Fixtures.Intra], intraDc: [black, black]);
        bits.Put(0, 4).Put(0, 4);
        bits.Put(0, 4).Put(0, 4);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();

        // Frame 2: a block moved onto itself and one skipped, then the left block copied from one block up.
        var next = new BitWriter();
        next.Put(0, 32);
        plane = new PlaneWriter(next, 2);
        plane.Bundles();
        plane.Row([Fixtures.Motion, Fixtures.Skip], xOffsets: [0], yOffsets: [0]);
        plane.Row([Fixtures.Motion, Fixtures.Skip], xOffsets: [0], yOffsets: [-8]);
        next.Align32();
        Fixtures.ChromaFill(next, 128);
        next.Align32();
        Fixtures.ChromaFill(next, 128);
        next.Align32();

        var movie = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [bits.ToArray(), next.ToArray()])));
        movie.DecodeNextFrame();
        var left = Fixtures.Red(movie, 3, 3);
        var right = Fixtures.Red(movie, 12, 3);
        Check.Near(left, Fixtures.Grey(200), 2, "The left block's DC.");
        Check.Near(right, Fixtures.Grey(100), 2, "The right block's DC.");
        Check.That(Fixtures.Red(movie, 3, 12) == 0, "The bottom row is black.");
        movie.DecodeNextFrame();
        Check.That(Fixtures.Red(movie, 3, 3) == left && Fixtures.Red(movie, 12, 3) == right, "A skipped block keeps the last picture.");
        Check.That(Fixtures.Red(movie, 3, 12) == left, "A motion block copies from the last picture.");
        Check.That(Fixtures.Red(movie, 12, 12) == 0, "A skipped block keeps the black.");
    }

    public static void ScaledBlocks()
    {
        // A 16 x 16 frame of one scaled pattern block: the pattern 0xAA doubles to stripes two pixels wide.
        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        plane.Row([Fixtures.Scaled], subTypes: [Fixtures.Pattern], colors: [16, 235], patterns: [0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA]);
        plane.Row([Fixtures.Scaled]);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();
        var movie = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [bits.ToArray()])));
        movie.DecodeNextFrame();
        Check.That(Fixtures.Red(movie, 0, 0) == 0 && Fixtures.Red(movie, 1, 15) == 0, "Bit 0 of the pattern is the first colour, doubled.");
        Check.That(Fixtures.Red(movie, 2, 0) == 255 && Fixtures.Red(movie, 3, 15) == 255, "Bit 1 is the second colour, doubled.");
        Check.That(Fixtures.Red(movie, 14, 7) == 255, "The stripes reach the last column.");

        // A chroma plane of one 8 x 8 block is a 16 x 16 area of the picture; the U plane decides the blue channel.
        var red = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [Fixtures.FilledFrame([128, 128, 128, 128], 128, 240)])));
        red.DecodeNextFrame();
        Check.That(Fixtures.Rgba(red)[2] == 255, "U 240 raises blue.");
    }

    public static void AlphaPlane()
    {
        var bits = new BitWriter();
        bits.Put(0, 32);
        Fixtures.LumaFill(bits, [10, 20, 30, 40]);
        bits.Align32();
        bits.Put(0, 32);
        Fixtures.LumaFill(bits, [128, 128, 128, 128]);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();
        Fixtures.ChromaFill(bits, 128);
        bits.Align32();
        var movie = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [bits.ToArray()], flags: BinkFile.AlphaFlag)));
        Check.That(movie.HasAlpha, "The flag.");
        movie.DecodeNextFrame();
        var rgba = Fixtures.Rgba(movie);
        Check.That(rgba[3] == 10 && rgba[(8 * 4) + 3] == 20 && rgba[((8 * 16) * 4) + 3] == 30 && rgba[((8 * 16 + 8) * 4) + 3] == 40, "The alpha of each block.");
    }

    public static void DamagedFrame()
    {
        var frame = Fixtures.FilledFrame([128, 128, 128, 128], 128, 128);
        var movie = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [frame[..24]])));
        Check.Throws<InvalidDataException>(() => movie.DecodeNextFrame(), "ended early");

        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        plane.Row([Fixtures.Motion, Fixtures.Motion], xOffsets: [-15, -15], yOffsets: [-15, -15]);
        bits.Put(0, 256);
        var moved = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [bits.ToArray()])));
        Check.Throws<InvalidDataException>(() => moved.DecodeNextFrame(), "leaves its picture");

        var unknown = new BitWriter();
        unknown.Put(0, 32);
        plane = new PlaneWriter(unknown, 2);
        plane.Bundles();
        plane.Row([11, 11]);
        unknown.Put(0, 256);
        var strange = new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [unknown.ToArray()])));
        Check.Throws<InvalidDataException>(() => strange.DecodeNextFrame(), "Unknown Bink block type");
    }

    public static void InverseDct()
    {
        foreach (var length in new[] { 8, 512, 2048 })
        {
            var random = new Random(length);
            var spectrum = new float[length];
            for (var index = 0; index < length; index++) spectrum[index] = (float)(random.NextDouble() * 2 - 1);
            var result = new float[length];
            new BinkDct(length).Inverse(spectrum, result);
            for (var sample = 0; sample < length; sample += Math.Max(1, length / 64))
            {
                var sum = spectrum[0] / 2.0;
                for (var k = 1; k < length; k++) sum += spectrum[k] * Math.Cos(Math.PI * k * (sample + 0.5) / length);
                Check.Near(result[sample], 2.0 / length * sum, 2e-4, $"Sample {sample} of a transform of {length}.");
            }
        }
    }

    public static void AudioBlocks()
    {
        const int frameLength = 2048;
        const int overlap = 128;
        // A block of coefficient 0 alone is constant: the file's value times 2 / sqrt(N) in 16-bit samples.
        const double expected = 8000;
        var value = (float)(expected * Math.Sqrt(frameLength) / 2);
        var packet = Fixtures.AudioPacket(2, frameLength, [(value, 0f), (0f, 0f)]);
        var file = Fixtures.File(16, 16, [Fixtures.FilledFrame([16, 16, 16, 16], 128, 128)], [packet]);
        var sound = new BinkMovie(BinkFile.Open(file)).DecodeSound();
        Check.That(sound is { SampleRate: 44100, Channels: 2 }, "The track's rate and channels.");
        var samples = sound!.Samples;
        Check.That(samples.Length == 2 * 2 * (frameLength - overlap), $"Two blocks of 1,920 stereo samples, got {samples.Length}.");
        Check.Near(samples[0], expected, 2, "The first block's level, left.");
        Check.Near(samples[1], expected, 2, "The first block's level, right.");
        Check.Near(samples[3838], expected, 2, "The level holds to the block's end.");
        // The second block's first samples fade from the end of the first to its own silence, the channels a half step apart.
        var seam = 2 * (frameLength - overlap);
        Check.Near(samples[seam], expected, 2, "The fade starts at the old level.");
        Check.Near(samples[seam + 2 * overlap - 2], expected / overlap, 3, "It ends a step above the new level.");
        Check.That(samples[seam + 2 * overlap] == 0, "Behind the overlap the block's own silence.");
        Check.That(samples[seam + 2] < samples[seam] && samples[seam + 3] < samples[seam + 1], "The fade falls on both channels.");
        Check.Throws<InvalidDataException>(() => new BinkMovie(BinkFile.Open(Fixtures.File(16, 16, [Fixtures.FilledFrame([16, 16, 16, 16], 128, 128)], [packet[..24]]))).DecodeSound(), "ends early");
    }

    public static void RealHeaders(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var movies = content.EnumerateFiles().Where(path => path.EndsWith(".bik", StringComparison.OrdinalIgnoreCase)).ToArray();
        Check.That(movies.Length > 0, "No movie in the content.");
        var totalSeconds = 0.0;
        foreach (var path in movies)
        {
            using var stream = content.OpenRead(path);
            var header = BinkHeader.Read(stream);
            totalSeconds += header.DurationSeconds;
            Check.That(header.Revision == 'i', $"{path}: the game ships revision i.");
        }
        Console.WriteLine($"       {movies.Length} Bink movies, {totalSeconds / 60:0.0} min total");
    }

    public static void RealDecode(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        foreach (var path in new[] { "Data/Movies/VS_small.bik", "Data/Movies/GC_Background.bik", "Data/English/Movies/EA_LOGO640.BIK", "Data/English/Movies/Comp_TankGen_000.bik" })
        {
            var first = Checksums(content, path);
            var second = Checksums(content, path);
            Check.That(first == second, $"{path}: two runs differ.");
            Console.WriteLine($"       {path}: {first}");
        }
    }

    private static string Checksums(ContentFileSystem content, string path)
    {
        using var stream = content.OpenRead(path);
        var movie = new BinkMovie(BinkFile.Open(stream));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (movie.DecodeNextFrame()) movie.WritePlanes(hash.AppendData);
        var sound = movie.DecodeSound();
        if (sound is not null)
        {
            var bytes = new byte[sound.Samples.Length * 2];
            Buffer.BlockCopy(sound.Samples, 0, bytes, 0, bytes.Length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>A stream that says it cannot seek.</summary>
sealed class NonSeekingStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}
