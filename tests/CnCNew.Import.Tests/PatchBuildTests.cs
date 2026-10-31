using System.Buffers.Binary;
using System.Text;
using CnCNew.Formats;
using CnCNew.Images;
using CnCNew.Import;
using CnCNew.Ui.Text;

/// <summary>What the patch's tool chain makes of its sources (docs/PATCH-LAYER.md), on small synthetic sources.</summary>
internal static class PatchBuildTests
{
    public static void Run()
    {
        OptionalBlocksLeaveAnIniFile();
        TheStringFileBecomesTheEnglishTable();
        PhotoshopPicturesAreRead();
        PicturesBecomeWhatTheGameLoads();
        Console.WriteLine("All patch build tests passed.");
    }

    private static void OptionalBlocksLeaveAnIniFile()
    {
        var source = Encoding.ASCII.GetBytes("CommandSet A\r\n;patch104p-core-begin\r\n  1 = First\r\n;patch104p-core-end\r\n;patch104p-optional-begin\r\n  1 = Second\r\n;patch104p-optional-end\r\nEnd\r\n");
        var text = Encoding.ASCII.GetString(PatchBuild.WithoutOptional(source));
        Assert(text == "CommandSet A\r\n;patch104p-core-begin\r\n  1 = First\r\n;patch104p-core-end\r\nEnd\r\n", $"The optional block is gone and nothing else: {text}");
        var plain = "Object A\nEnd\n"u8.ToArray();
        Assert(ReferenceEquals(PatchBuild.WithoutOptional(plain), plain), "A file without a block stays as it is.");
        var output = PatchBuild.Outputs("Data/INI/CommandSet.ini", source).Single();
        Assert(output.Path == "Data/INI/CommandSet.ini" && Encoding.ASCII.GetString(output.Bytes) == text, "An INI file of the patch keeps its path.");
    }

    private static void TheStringFileBecomesTheEnglishTable()
    {
        var source = Encoding.UTF8.GetBytes(string.Join("\r\n",
            "// comment",
            "GUI:Patch",
            "US: \"Patch: \\\"one\\\"\\nnext ©\"",
            "DE: \"Fehler\"",
            "//patch104p-optional-begin",
            "US: \"optional\"",
            "//patch104p-optional-end",
            "END",
            "",
            "//patch104p-optional-begin",
            "GUI:Optional",
            "US: \"optional\"",
            "END",
            "//patch104p-optional-end",
            "CONTROLBAR:Speech",
            "US: \"first\" speechname",
            "US: second",
            "END",
            ""));
        var (path, bytes) = PatchBuild.Outputs("Data/generals.str", source).Single();
        Assert(path == "Data/English/generals.csf", "The string file becomes the English table.");
        var table = CsfReader.Read(bytes);
        Assert(table.Language == CsfLanguage.Us && table.Labels.Length == 2, $"Two labels, the optional one is gone ({table.Labels.Length}).");
        Assert(table.Labels[0] is { Label: "GUI:Patch" } && table.Labels[0].Values.Single() == new CsfValue("Patch: \"one\"\nnext ©", null), $"The text with its escapes: {table.Labels[0].Values[0]}");
        Assert(table.Labels[1].Values.Single() == new CsfValue("first", "speechname"), $"The first text of the language with its speech: {table.Labels[1].Values[0]}");
        Throws(() => PatchBuild.Strings("A\r\nDE: \"x\"\r\nEND\r\n"u8.ToArray()), "a label without English text");
        Throws(() => PatchBuild.Strings("A\r\nUS: \"x\"\r\nEND\r\nA\r\nUS: \"y\"\r\nEND\r\n"u8.ToArray()), "a label defined again");
        Throws(() => PatchBuild.Strings("A\r\nUS: \"x\"\r\n"u8.ToArray()), "a label without END");
    }

    private static void PhotoshopPicturesAreRead()
    {
        // Two by two, red, green, blue and alpha planes.
        byte[][] planes = [[10, 20, 30, 40], [50, 60, 70, 80], [90, 100, 110, 120], [255, 200, 100, 0]];
        byte[] expected = [10, 50, 90, 255, 20, 60, 100, 200, 30, 70, 110, 100, 40, 80, 120, 0];
        var raw = PatchBuild.Psd(Psd(2, 2, planes, packed: false), "raw");
        Assert(raw is { Width: 2, Height: 2 } && raw.RgbaPixels.SequenceEqual(expected), "A raw picture is read, top row first.");
        var packed = PatchBuild.Psd(Psd(2, 2, planes, packed: true), "packed");
        Assert(packed.RgbaPixels.SequenceEqual(expected), "A packed picture gives the same pixels.");
        var opaque = PatchBuild.Psd(Psd(1, 1, [[1], [2], [3]], packed: true), "opaque");
        Assert(opaque.RgbaPixels.SequenceEqual(new byte[] { 1, 2, 3, 255 }), "Three channels are opaque.");
        // PackBits: a run of three and a copy of two.
        var runs = Psd(5, 1, [[], [], []], packed: true, rows: [[0xFE, 7, 0x01, 8, 9], [0xFC, 1], [0xFC, 2]]);
        Assert(PatchBuild.Psd(runs, "runs").RgbaPixels.Where((_, index) => index % 4 == 0).SequenceEqual(new byte[] { 7, 7, 7, 8, 9 }), "Runs and copies are unpacked.");
        Throws(() => PatchBuild.Psd(Encoding.ASCII.GetBytes(new string('x', 40)), "bad"), "a file that is no Photoshop file");
    }

    private static void PicturesBecomeWhatTheGameLoads()
    {
        var bytes = Psd(4, 2, [new byte[8], new byte[8], new byte[8]], packed: false);
        var outputs = PatchBuild.Outputs("Art/Textures/GenerateMip/exhorde.psd", bytes).ToArray();
        Assert(outputs.Select(output => output.Path).SequenceEqual(["Art/Textures/exhorde.dds", "Art/Textures/exhordeb.dds"]), "The bundle's second name is made as well.");
        using (var stream = new MemoryStream(outputs[0].Bytes))
        {
            var dds = LegacyAssetReaders.ReadDds(stream);
            Assert(dds is { Width: 4, Height: 2, MipCount: 3, PixelFormat: "RGB32" } && dds.ExpectedMinimumBytes == outputs[0].Bytes.Length, $"The chain goes down to one pixel: {dds}");
        }
        var flat = PatchBuild.Outputs("Art/Textures/NoMip/a.psd", bytes).Single();
        using (var stream = new MemoryStream(flat.Bytes))
            Assert(flat.Path == "Art/Textures/a.dds" && LegacyAssetReaders.ReadDds(stream).MipCount == 1, "A picture of NoMip has no chain.");

        var image = new TgaImage(2, 2, [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 3]);
        var mipped = PatchBuild.Dds(image, mips: true);
        Assert(mipped.Length == 128 + 4 * 4 + 4, "Two levels.");
        Assert(mipped.AsSpan(128, 4).SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Bytes in the file's order: blue, green, red, alpha.");
        Assert(mipped.AsSpan(128 + 16, 4).SequenceEqual(new byte[] { 128, 128, 128, 192 }), "A level is the rounded average of four pixels.");

        var tga = PatchBuild.Outputs("Art/Textures/GenerateTga/a.psd", Psd(1, 2, [[1, 2], [3, 4], [5, 6]], packed: true)).Single();
        using (var stream = new MemoryStream(tga.Bytes))
        {
            var read = TgaReader.Read(stream);
            Assert(tga.Path == "Art/Textures/a.tga" && read is { Width: 1, Height: 2 } && read.RgbaPixels.SequenceEqual(new byte[] { 1, 3, 5, 255, 2, 4, 6, 255 }), "A picture of GenerateTga is a TGA.");
        }
    }

    private static byte[] Psd(int width, int height, byte[][] planes, bool packed, byte[][]? rows = null)
    {
        using var file = new MemoryStream();
        file.Write("8BPS"u8);
        Big16(file, 1);
        file.Write(new byte[6]);
        Big16(file, planes.Length);
        Big32(file, height);
        Big32(file, width);
        Big16(file, 8);
        Big16(file, 3);
        for (var section = 0; section < 3; section++) Big32(file, 0);
        Big16(file, packed ? 1 : 0);
        if (!packed)
        {
            foreach (var plane in planes) file.Write(plane);
            return file.ToArray();
        }
        var lines = rows ?? planes.SelectMany(plane => Enumerable.Range(0, height).Select(row => Literal(plane.AsSpan(row * width, width)))).ToArray();
        foreach (var line in lines) Big16(file, line.Length);
        foreach (var line in lines) file.Write(line);
        return file.ToArray();
    }

    private static byte[] Literal(ReadOnlySpan<byte> row)
    {
        var line = new byte[row.Length + 1];
        line[0] = (byte)(row.Length - 1);
        row.CopyTo(line.AsSpan(1));
        return line;
    }

    private static void Big16(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        stream.Write(bytes);
    }

    private static void Big32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws(Action action, string what)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException($"Expected a refusal: {what}.");
    }
}
