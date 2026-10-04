using System.Buffers.Binary;

namespace CnCNew.Images.Tests;

/// <summary>Decoding of Windows cursor (<c>.cur</c>) and animated cursor (<c>.ani</c>) files, on synthetic files only.</summary>
internal static class CursorReaderTests
{
    private static readonly (byte R, byte G, byte B) Red = (255, 0, 0), Green = (0, 255, 0), Black = (0, 0, 0), White = (255, 255, 255);

    public static void Run()
    {
        // 3 x 2, hot spot (2, 1): red, green, a transparent pixel / an inverting pixel (white under the mask), black, green.
        var cur = CursorFixtures.Cur(3, 2, 2, 1, [Red, Green, Black, White, Black, Green], [false, false, true, true, false, false]);
        var image = CursorReader.ReadCur(cur).Single();
        Assert(image is { Width: 3, Height: 2, HotspotX: 2, HotspotY: 1 }, "cursor size and hot spot");
        Assert(image.RgbaPixels.SequenceEqual(new byte[]
        {
            255, 0, 0, 255,  0, 255, 0, 255,  0, 0, 0, 0,
            255, 255, 255, 255,  0, 0, 0, 255,  0, 255, 0, 255,
        }), "24-bit rows are bottom-up BGR; a masked black pixel is transparent, a masked colour stays opaque");
        Assert(image.InvertedPixels == 1, "the inverting pixel is counted");

        // The retail format: 4 bits per pixel with a palette, rows padded to four bytes (five pixels need three nibble bytes plus padding).
        var paletted = CursorFixtures.PaletteCur(5, 1, 0, 0, [Black, Red, Green], [1, 2, 0, 2, 1], [false, false, true, false, false]);
        var fromPalette = CursorReader.ReadCur(paletted).Single();
        Assert(fromPalette.RgbaPixels.SequenceEqual(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0, 255, 0, 255, 255, 0, 0, 255 }), "4-bit palette pixels, high nibble first");

        var frameA = CursorFixtures.Cur(1, 1, 0, 0, [Red], [false]);
        var frameB = CursorFixtures.Cur(1, 1, 0, 0, [Green], [false]);
        var plain = CursorReader.ReadAni(CursorFixtures.Ani([frameA, frameB], defaultRate: 6));
        Assert(plain.Frames.Length == 2 && plain.Steps.SequenceEqual([new AniStep(0, 6), new AniStep(1, 6)]) && plain.TotalJiffies == 12, "frames in file order at the header's rate");
        Assert(plain.FrameIndexAt(0) == 0 && plain.FrameIndexAt(99) == 0 && plain.FrameIndexAt(100) == 1 && plain.FrameIndexAt(199) == 1 && plain.FrameIndexAt(200) == 0,
            "six jiffies are 100 ms; the animation loops");
        Assert(plain.FrameAt(150).RgbaPixels[1] == 255, "the second frame is the green one");

        var sequenced = CursorReader.ReadAni(CursorFixtures.Ani([frameA, frameB], defaultRate: 10, sequence: [0, 1, 0], rates: [3, 6, 0], sizeIncludesHeader: true));
        Assert(sequenced.Steps.SequenceEqual([new AniStep(0, 3), new AniStep(1, 6), new AniStep(0, 1)]), "sequence and rate tables; a zero rate is one jiffy; the retail RIFF size quirk is accepted");
        Assert(sequenced.FrameIndexAt(49) == 0 && sequenced.FrameIndexAt(50) == 1 && sequenced.FrameIndexAt(149) == 1 && sequenced.FrameIndexAt(150) == 0 && sequenced.FrameIndexAt(167) == 0,
            "frame by time over steps of different length (3, 6 and 1 jiffies)");
        var single = CursorReader.ReadAni(CursorFixtures.Ani([frameA], defaultRate: 10));
        Assert(single.FrameIndexAt(123_456) == 0, "a single frame never changes");

        Fails(() => CursorReader.ReadCur(cur.AsSpan(0, cur.Length - 1)), "truncated cursor");
        Fails(() => CursorReader.ReadCur(new byte[] { 0, 0, 3, 0, 1, 0 }), "wrong cursor type");
        var compressed = (byte[])cur.Clone();
        compressed[22 + 16] = 1;
        Fails(() => CursorReader.ReadCur(compressed), "compressed cursor bitmap");
        var hotspotOutside = CursorFixtures.Cur(1, 1, 0, 0, [Red], [false]);
        hotspotOutside[10] = 5;
        Fails(() => CursorReader.ReadCur(hotspotOutside), "hot spot outside the image");
        Fails(() => CursorReader.ReadAni(cur), "a cursor file is not an animated cursor");
        Fails(() => CursorReader.ReadAni(CursorFixtures.Ani([frameA, frameB], 6, sequence: [0, 2])), "sequence entry beyond the frames");
        Fails(() => CursorReader.ReadAni(CursorFixtures.Ani([frameA, frameB], 6, rates: [1])), "rate table of the wrong length");
        Fails(() => CursorReader.ReadAni(CursorFixtures.Ani([frameA], 6, extraChunk: "junk")), "unknown chunk");
        var wrongLength = CursorFixtures.Ani([frameA], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(wrongLength.AsSpan(4), (uint)wrongLength.Length + 3);
        Fails(() => CursorReader.ReadAni(wrongLength), "RIFF length that matches neither convention");
        var ani = CursorFixtures.Ani([frameA, frameB], 6);
        Fails(() => CursorReader.ReadAni(ani.AsSpan(0, ani.Length - 4)), "truncated animated cursor");

        Console.WriteLine("All cursor reader tests passed (cur 24-bit and 4-bit, ani frames, sequence, rates, frame by time, 10 rejected files).");
    }

    private delegate void Attempt();

    private static void Fails(Attempt attempt, string name)
    {
        try
        {
            attempt();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException($"{name} was accepted.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
