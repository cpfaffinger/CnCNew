using CnCNew.Images;

var uncompressed = CreateTga(2, 2, imageType: 2, bitsPerPixel: 24, descriptor: 0,
[
    255, 0, 0,  255, 255, 255,
    0, 0, 255,  0, 255, 0,
]);
var image = TgaReader.Read(uncompressed);
Assert(image.Width == 2 && image.Height == 2, "TGA dimensions mismatch.");
Assert(image.RgbaPixels[..4].SequenceEqual(new byte[] { 255, 0, 0, 255 }), "Bottom-origin TGA was not flipped.");
Assert(image.RgbaPixels[4..8].SequenceEqual(new byte[] { 0, 255, 0, 255 }), "TGA BGR conversion failed.");

var rle = CreateTga(2, 1, imageType: 10, bitsPerPixel: 32, descriptor: 0x20,
[
    0x81, 30, 20, 10, 40,
]);
var rleImage = TgaReader.Read(rle);
Assert(rleImage.RgbaPixels.SequenceEqual(new byte[] { 10, 20, 30, 40, 10, 20, 30, 40 }), "TGA RLE decode failed.");

Console.WriteLine("All TGA image tests passed.");
DdsTests();
CnCNew.Images.Tests.CursorReaderTests.Run();
return 0;

static void DdsTests()
{
    // One 4 x 4 DXT1 block: red and blue end points, a row of each of the four indices; DXT5 adds a ramp of alpha.
    var dxt1 = Dds(4, 4, "DXT1", 0, [0x00, 0xF8, 0x1F, 0x00, 0b11_10_01_00, 0b11_10_01_00, 0b11_10_01_00, 0b11_10_01_00]);
    var image = DdsReader.Read(dxt1);
    Assert(image.Width == 4 && image.Height == 4, "DDS size.");
    Assert(image.RgbaPixels[..4].SequenceEqual(new byte[] { 255, 0, 0, 255 }), "First end point.");
    Assert(image.RgbaPixels[4..8].SequenceEqual(new byte[] { 0, 0, 255, 255 }), "Second end point.");
    Assert(image.RgbaPixels[8..12].SequenceEqual(new byte[] { 170, 0, 85, 255 }), "A third of the way.");

    var dxt5 = Dds(4, 4, "DXT5", 0, [255, 0, 0, 0, 0, 0, 0, 0, 0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0]);
    Assert(DdsReader.Read(dxt5).RgbaPixels[3] == 255, "Alpha end point.");

    var plain = Dds(1, 1, "\0\0\0\0", 32, [10, 20, 30, 40]);
    Assert(DdsReader.Read(plain).RgbaPixels.SequenceEqual(new byte[] { 30, 20, 10, 40 }), "32-bit masks.");

    try
    {
        DdsReader.Read(Dds(1, 1, "ATI2", 0, new byte[16]));
        Assert(false, "An unknown format is refused.");
    }
    catch (InvalidDataException)
    {
    }

    Console.WriteLine("All DDS image tests passed.");
}

static byte[] Dds(int width, int height, string fourCc, int bits, byte[] payload)
{
    var header = new byte[128];
    "DDS "u8.CopyTo(header);
    BitConverter.TryWriteBytes(header.AsSpan(12), height);
    BitConverter.TryWriteBytes(header.AsSpan(16), width);
    for (var index = 0; index < 4; index++) header[84 + index] = (byte)fourCc[index];
    BitConverter.TryWriteBytes(header.AsSpan(88), bits);
    BitConverter.TryWriteBytes(header.AsSpan(92), 0x00FF0000u);
    BitConverter.TryWriteBytes(header.AsSpan(96), 0x0000FF00u);
    BitConverter.TryWriteBytes(header.AsSpan(100), 0x000000FFu);
    BitConverter.TryWriteBytes(header.AsSpan(104), 0xFF000000u);
    return [.. header, .. payload];
}

static MemoryStream CreateTga(ushort width, ushort height, byte imageType, byte bitsPerPixel, byte descriptor, byte[] payload)
{
    var header = new byte[18];
    header[2] = imageType;
    BitConverter.TryWriteBytes(header.AsSpan(12), width);
    BitConverter.TryWriteBytes(header.AsSpan(14), height);
    header[16] = bitsPerPixel;
    header[17] = descriptor;
    return new MemoryStream([.. header, .. payload]);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
