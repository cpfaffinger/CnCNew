using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace CnCNew.W3D;

internal static class W3dBinary
{
    public const int NameLength = 16;

    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, W3dChunk chunk)
        => data.Slice(checked((int)chunk.DataOffset), chunk.Size);

    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static float ReadSingle(ReadOnlySpan<byte> data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));

    public static Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
        => new(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));

    public static Quaternion ReadQuaternion(ReadOnlySpan<byte> data, int offset)
        => new(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8), ReadSingle(data, offset + 12));

    public static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        var terminator = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator >= 0 ? data[..terminator] : data).TrimEnd();
    }

    public static W3dChunk RequireSize(W3dChunk chunk, int expectedSize, string description)
    {
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"{description} chunk 0x{chunk.Type:X8} at {chunk.HeaderOffset} has {chunk.Size} bytes; expected {expectedSize}.");
        }

        return chunk;
    }

    public static W3dChunk RequireLeaf(W3dChunk chunk, string description)
    {
        if (chunk.IsContainer)
        {
            throw new InvalidDataException(
                $"{description} chunk 0x{chunk.Type:X8} at {chunk.HeaderOffset} must not be a container.");
        }

        return chunk;
    }

    public static W3dChunk RequireContainer(W3dChunk chunk, string description)
    {
        if (!chunk.IsContainer)
        {
            throw new InvalidDataException(
                $"{description} chunk 0x{chunk.Type:X8} at {chunk.HeaderOffset} must be a container.");
        }

        return chunk;
    }

    public static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    public static void ValidateVersion(uint version, uint minimum, string description)
    {
        if (version < minimum)
        {
            throw new InvalidDataException(
                $"{description} version {version >> 16}.{version & 0xffff} predates {minimum >> 16}.{minimum & 0xffff}; " +
                "the pre-3.0 root-pivot shift of the original loader is not supported.");
        }
    }

    public static uint MakeVersion(uint major, uint minor) => (major << 16) | minor;
}
