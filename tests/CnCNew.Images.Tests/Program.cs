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
return 0;

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
