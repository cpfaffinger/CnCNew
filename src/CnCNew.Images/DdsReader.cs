using System.Buffers.Binary;

namespace CnCNew.Images;

/// <summary>
/// Reads the largest level of a DDS file into RGBA pixels: DXT1, DXT3, DXT5 and uncompressed 32-bit images, which is what the game ships. The GPU reads every other texture directly; this is
/// for the few pictures the client has to change on the CPU. Anything else is refused.
/// </summary>
public static class DdsReader
{
    private const int HeaderSize = 128;

    public static TgaImage Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    public static TgaImage Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x20534444u) throw new InvalidDataException("Not a DDS file.");
        var height = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        if (width <= 0 || height <= 0) throw new InvalidDataException($"A DDS image of {width} x {height}.");
        var fourCc = data.Slice(84, 4);
        var bits = BinaryPrimitives.ReadInt32LittleEndian(data[88..]);
        var pixels = new byte[width * height * 4];
        var body = data[HeaderSize..];
        if (fourCc.SequenceEqual("DXT1"u8)) DecodeBlocks(body, width, height, 8, pixels, Block.Dxt1);
        else if (fourCc.SequenceEqual("DXT3"u8)) DecodeBlocks(body, width, height, 16, pixels, Block.Dxt3);
        else if (fourCc.SequenceEqual("DXT5"u8)) DecodeBlocks(body, width, height, 16, pixels, Block.Dxt5);
        else if (fourCc.SequenceEqual(new byte[4]) && bits == 32) Uncompressed(data, body, width, height, pixels);
        else throw new InvalidDataException($"A DDS image in the format '{System.Text.Encoding.ASCII.GetString(fourCc)}' with {bits} bits.");
        return new TgaImage(width, height, pixels);
    }

    private enum Block
    {
        Dxt1,
        Dxt3,
        Dxt5,
    }

    private static void Uncompressed(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body, int width, int height, byte[] pixels)
    {
        var masks = new[] { BinaryPrimitives.ReadUInt32LittleEndian(header[92..]), BinaryPrimitives.ReadUInt32LittleEndian(header[96..]), BinaryPrimitives.ReadUInt32LittleEndian(header[100..]), BinaryPrimitives.ReadUInt32LittleEndian(header[104..]) };
        if (body.Length < width * height * 4) throw new InvalidDataException("The DDS image is cut short.");
        for (var index = 0; index < width * height; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(body[(index * 4)..]);
            for (var channel = 0; channel < 4; channel++)
                pixels[index * 4 + channel] = masks[channel] == 0 ? (byte)255 : (byte)((value & masks[channel]) >> System.Numerics.BitOperations.TrailingZeroCount(masks[channel]));
        }
    }

    private static void DecodeBlocks(ReadOnlySpan<byte> body, int width, int height, int blockBytes, byte[] pixels, Block kind)
    {
        var (columns, rows) = ((width + 3) / 4, (height + 3) / 4);
        if (body.Length < columns * rows * blockBytes) throw new InvalidDataException("The DDS image is cut short.");
        Span<byte> colors = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[8];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var block = body.Slice((row * columns + column) * blockBytes, blockBytes);
                var colorBlock = kind == Block.Dxt1 ? block : block[8..];
                var first = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock);
                var second = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock[2..]);
                Expand(first, colors);
                Expand(second, colors[4..]);
                var opaqueOrder = kind != Block.Dxt1 || first > second;
                for (var channel = 0; channel < 3; channel++)
                {
                    colors[8 + channel] = (byte)(opaqueOrder ? (2 * colors[channel] + colors[4 + channel]) / 3 : (colors[channel] + colors[4 + channel]) / 2);
                    colors[12 + channel] = (byte)(opaqueOrder ? (colors[channel] + 2 * colors[4 + channel]) / 3 : 0);
                }

                colors[3] = colors[7] = colors[11] = 255;
                colors[15] = (byte)(opaqueOrder ? 255 : 0);
                if (kind == Block.Dxt5) AlphaRamp(block[0], block[1], alphas);
                var indices = BinaryPrimitives.ReadUInt32LittleEndian(colorBlock[4..]);
                var alphaBits = kind == Block.Dxt5 ? BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16 : 0UL;
                for (var texel = 0; texel < 16; texel++)
                {
                    var x = column * 4 + texel % 4;
                    var y = row * 4 + texel / 4;
                    var pick = (int)((indices >> (texel * 2)) & 3);
                    var alpha = kind switch
                    {
                        Block.Dxt3 => (byte)(((block[texel / 2] >> (texel % 2 * 4)) & 15) * 17),
                        Block.Dxt5 => alphas[(int)((alphaBits >> (texel * 3)) & 7)],
                        _ => colors[pick * 4 + 3],
                    };
                    if (x >= width || y >= height) continue;
                    var target = (y * width + x) * 4;
                    pixels[target] = colors[pick * 4];
                    pixels[target + 1] = colors[pick * 4 + 1];
                    pixels[target + 2] = colors[pick * 4 + 2];
                    pixels[target + 3] = alpha;
                }
            }
        }
    }

    private static void Expand(ushort color, Span<byte> rgb)
    {
        var (red, green, blue) = (color >> 11, (color >> 5) & 63, color & 31);
        rgb[0] = (byte)((red << 3) | (red >> 2));
        rgb[1] = (byte)((green << 2) | (green >> 4));
        rgb[2] = (byte)((blue << 3) | (blue >> 2));
    }

    private static void AlphaRamp(byte first, byte second, Span<byte> alphas)
    {
        alphas[0] = first;
        alphas[1] = second;
        if (first > second)
        {
            for (var step = 1; step <= 6; step++) alphas[step + 1] = (byte)(((7 - step) * first + step * second) / 7);
        }
        else
        {
            for (var step = 1; step <= 4; step++) alphas[step + 1] = (byte)(((5 - step) * first + step * second) / 5);
            alphas[6] = 0;
            alphas[7] = 255;
        }
    }
}
