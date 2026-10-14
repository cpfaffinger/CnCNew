using System.Buffers.Binary;

namespace CnCNew.W3D;

public static class W3dChunkReader
{
    private const uint ContainerFlag = 0x80000000;
    private const uint SizeMask = 0x7fffffff;
    private const int HeaderSize = 8;
    private const int MaximumDepth = 256;

    public static W3dFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("W3D input must be a readable, seekable stream.", nameof(stream));
        }

        stream.Position = 0;
        var length = stream.Length;
        var chunks = ReadChunks(stream, length, 0, out var trailing);
        if (stream.Position + trailing != length)
        {
            throw new InvalidDataException($"W3D parsing stopped at {stream.Position} of {length} bytes.");
        }

        return new W3dFile(length, chunks) { TrailingBytes = trailing };
    }

    private static IReadOnlyList<W3dChunk> ReadChunks(Stream stream, long boundary, int depth) => ReadChunks(stream, boundary, depth, out _);

    // trailing: at the top level only, fewer bytes than a chunk header after the last chunk. The original's loader (ChunkLoadClass::Open_Chunk, WWLib chunkio.cpp)
    // stops there because its read of the next header comes back short; inside a chunk the same remainder is an error here as there.
    private static IReadOnlyList<W3dChunk> ReadChunks(Stream stream, long boundary, int depth, out int trailing)
    {
        trailing = 0;
        if (depth > MaximumDepth)
        {
            throw new InvalidDataException($"W3D chunk nesting exceeds {MaximumDepth} levels.");
        }

        var chunks = new List<W3dChunk>();
        Span<byte> header = stackalloc byte[HeaderSize];
        while (stream.Position < boundary)
        {
            var headerOffset = stream.Position;
            if (depth == 0 && boundary - headerOffset < HeaderSize && boundary == stream.Length)
            {
                trailing = checked((int)(boundary - headerOffset));
                break;
            }

            if (boundary - headerOffset < HeaderSize)
            {
                throw new InvalidDataException(
                    $"W3D chunk header at {headerOffset} exceeds its parent boundary {boundary}.");
            }

            stream.ReadExactly(header);
            var type = BinaryPrimitives.ReadUInt32LittleEndian(header);
            var encodedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            var isContainer = (encodedSize & ContainerFlag) != 0;
            var size = checked((int)(encodedSize & SizeMask));
            var dataOffset = stream.Position;
            var endOffset = checked(dataOffset + size);
            if (endOffset > boundary || endOffset > stream.Length)
            {
                throw new InvalidDataException(
                    $"W3D chunk 0x{type:X8} at {headerOffset} ends at {endOffset}, beyond boundary {boundary}.");
            }

            IReadOnlyList<W3dChunk> children;
            if (isContainer)
            {
                children = ReadChunks(stream, endOffset, depth + 1);
            }
            else
            {
                children = [];
                stream.Position = endOffset;
            }

            chunks.Add(new W3dChunk(type, headerOffset, dataOffset, size, isContainer, children));
        }

        if (stream.Position + trailing != boundary)
        {
            throw new InvalidDataException(
                $"W3D chunk sequence ended at {stream.Position}, expected boundary {boundary}.");
        }

        return chunks;
    }
}
