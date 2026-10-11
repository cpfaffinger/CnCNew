using System.Buffers.Binary;
using System.Text;

namespace CnCNew.Net;

/// <summary>A message that does not follow the protocol: truncated, oversized, an unknown value where a known one is required. It is counted and discarded, never guessed at.</summary>
public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Little-endian writer of the protocol's primitive values. Strings are u16-length-prefixed UTF-8; byte blocks are u32-length-prefixed.</summary>
public sealed class WireWriter
{
    private readonly MemoryStream _stream = new();
    private readonly byte[] _scratch = new byte[8];

    public int Length => (int)_stream.Length;
    public byte[] ToArray() => _stream.ToArray();

    public void U8(byte value) => _stream.WriteByte(value);
    public void Bool(bool value) => _stream.WriteByte(value ? (byte)1 : (byte)0);

    public void U16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_scratch, value);
        _stream.Write(_scratch, 0, 2);
    }

    public void U32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_scratch, value);
        _stream.Write(_scratch, 0, 4);
    }

    public void I32(int value) => U32(unchecked((uint)value));

    public void U64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_scratch, value);
        _stream.Write(_scratch, 0, 8);
    }

    public void I64(long value) => U64(unchecked((ulong)value));

    public void Text(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > maxBytes || bytes.Length > ushort.MaxValue) throw new ProtocolException($"A text of {bytes.Length} bytes exceeds its limit of {maxBytes}.");
        U16((ushort)bytes.Length);
        _stream.Write(bytes);
    }

    public void Bytes(ReadOnlySpan<byte> value, int maxBytes)
    {
        if (value.Length > maxBytes) throw new ProtocolException($"A block of {value.Length} bytes exceeds its limit of {maxBytes}.");
        U32((uint)value.Length);
        _stream.Write(value);
    }

    public void Raw(ReadOnlySpan<byte> value) => _stream.Write(value);
}

/// <summary>The reader to <see cref="WireWriter"/>; every read is bounds-checked and every length is checked against its limit before anything is allocated.</summary>
public ref struct WireReader(ReadOnlySpan<byte> data)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public readonly int Remaining => _data.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > Remaining) throw new ProtocolException("The message is truncated.");
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    public byte U8() => Take(1)[0];

    public bool Bool() => U8() switch
    {
        0 => false,
        1 => true,
        var other => throw new ProtocolException($"A flag has the value {other}."),
    };

    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int I32() => unchecked((int)U32());
    public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public long I64() => unchecked((long)U64());

    public string Text(int maxBytes)
    {
        var length = U16();
        if (length > maxBytes) throw new ProtocolException($"A text of {length} bytes exceeds its limit of {maxBytes}.");
        try
        {
            return StrictUtf8.GetString(Take(length));
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolException("A text is not valid UTF-8.");
        }
    }

    public byte[] Bytes(int maxBytes)
    {
        var length = U32();
        if (length > (uint)maxBytes) throw new ProtocolException($"A block of {length} bytes exceeds its limit of {maxBytes}.");
        return Take((int)length).ToArray();
    }

    public ReadOnlySpan<byte> Raw(int count) => Take(count);

    public readonly void End()
    {
        if (Remaining != 0) throw new ProtocolException($"The message has {Remaining} trailing bytes.");
    }
}
