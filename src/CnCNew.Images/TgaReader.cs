using System.Buffers.Binary;

namespace CnCNew.Images;

public static class TgaReader
{
    private const int HeaderSize = 18;
    private const int MaximumPixelCount = 64 * 1024 * 1024;

    public static TgaImage Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The TGA stream must be readable.", nameof(stream));
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        ReadExactly(stream, header);
        var idLength = header[0];
        var colorMapType = header[1];
        var imageType = header[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
        var bitsPerPixel = header[16];
        var descriptor = header[17];
        if (colorMapType != 0 || imageType is not (2 or 10) || bitsPerPixel is not (24 or 32))
        {
            throw new InvalidDataException(
                $"Unsupported TGA format: color map {colorMapType}, image type {imageType}, {bitsPerPixel} bpp.");
        }

        if (width == 0 || height == 0 || checked((long)width * height) > MaximumPixelCount)
        {
            throw new InvalidDataException($"Invalid or excessive TGA dimensions {width}x{height}.");
        }

        if ((descriptor & 0xc0) != 0)
        {
            throw new InvalidDataException("Interleaved TGA images are not supported.");
        }

        SkipExactly(stream, idLength);
        var bytesPerPixel = bitsPerPixel / 8;
        var pixelCount = checked(width * height);
        var rgba = new byte[checked(pixelCount * 4)];
        var topOrigin = (descriptor & 0x20) != 0;
        var rightOrigin = (descriptor & 0x10) != 0;
        Span<byte> pixel = stackalloc byte[4];
        var written = 0;

        while (written < pixelCount)
        {
            if (imageType == 2)
            {
                ReadExactly(stream, pixel[..bytesPerPixel]);
                WritePixel(rgba, width, height, written++, pixel, bytesPerPixel, topOrigin, rightOrigin);
                continue;
            }

            var packetHeader = stream.ReadByte();
            if (packetHeader < 0)
            {
                throw new EndOfStreamException("Unexpected end of TGA RLE stream.");
            }

            var count = (packetHeader & 0x7f) + 1;
            if (written + count > pixelCount)
            {
                throw new InvalidDataException("TGA RLE packet exceeds the declared image dimensions.");
            }

            if ((packetHeader & 0x80) != 0)
            {
                ReadExactly(stream, pixel[..bytesPerPixel]);
                for (var index = 0; index < count; index++)
                {
                    WritePixel(rgba, width, height, written++, pixel, bytesPerPixel, topOrigin, rightOrigin);
                }
            }
            else
            {
                for (var index = 0; index < count; index++)
                {
                    ReadExactly(stream, pixel[..bytesPerPixel]);
                    WritePixel(rgba, width, height, written++, pixel, bytesPerPixel, topOrigin, rightOrigin);
                }
            }
        }

        return new TgaImage(width, height, rgba);
    }

    private static void WritePixel(
        byte[] output,
        int width,
        int height,
        int sourceIndex,
        ReadOnlySpan<byte> bgra,
        int bytesPerPixel,
        bool topOrigin,
        bool rightOrigin)
    {
        var sourceX = sourceIndex % width;
        var sourceY = sourceIndex / width;
        var destinationX = rightOrigin ? width - 1 - sourceX : sourceX;
        var destinationY = topOrigin ? sourceY : height - 1 - sourceY;
        var offset = checked((destinationY * width + destinationX) * 4);
        output[offset] = bgra[2];
        output[offset + 1] = bgra[1];
        output[offset + 2] = bgra[0];
        output[offset + 3] = bytesPerPixel == 4 ? bgra[3] : (byte)255;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("TGA data is truncated.", exception);
        }
    }

    private static void SkipExactly(Stream stream, int count)
    {
        Span<byte> buffer = stackalloc byte[256];
        while (count > 0)
        {
            var length = Math.Min(count, buffer.Length);
            ReadExactly(stream, buffer[..length]);
            count -= length;
        }
    }
}
