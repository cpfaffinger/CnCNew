using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CnCNew.Video;

/// <summary>
/// The bit stream of Bink: 32-bit little-endian words read from the lowest bit up, so a value's first bit is the lowest bit of its byte. The reader never reads past the
/// end of its stretch: a read beyond it throws <see cref="InvalidDataException"/>, and the buffer needs eight spare bytes behind the stretch (<see cref="BinkFile"/> keeps them).
/// </summary>
internal struct BinkBitReader
{
    /// <summary>The bytes a reader may look at behind the end of its stretch (one 64-bit word).</summary>
    public const int Padding = 8;

    private readonly byte[] _data;
    private readonly int _startBit;
    private readonly int _endBit;
    private int _bit;

    public BinkBitReader(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length + Padding > data.Length) throw new ArgumentOutOfRangeException(nameof(length), "A bit reader needs its stretch and the padding behind it inside the buffer.");
        if (length > int.MaxValue / 8 - offset) throw new ArgumentOutOfRangeException(nameof(length), "A bit stream may not span more than 256 MiB.");
        _data = data;
        _startBit = offset * 8;
        _bit = _startBit;
        _endBit = (offset + length) * 8;
    }

    /// <summary>The bits read so far.</summary>
    public readonly int Position => _bit - _startBit;

    public readonly int BitsLeft => _endBit - _bit;

    /// <summary>The next 32 bits without moving; the lowest bit of the result is the next bit of the stream.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint Peek()
    {
        var offset = _bit >> 3;
        // The check is against the padded end: the padding exists so that a peek at the very end stays inside the buffer.
        if ((uint)_bit > (uint)_endBit) Overrun();
        var word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_data), offset));
        return (uint)(word >> (_bit & 7));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count) => _bit += count;

    /// <summary>A value of 0 to 32 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Read(int count)
    {
        if (count == 0) return 0;
        var value = Peek() & (uint.MaxValue >> (32 - count));
        _bit += count;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBit()
    {
        var value = (Peek() & 1) != 0;
        _bit++;
        return value;
    }

    /// <summary>Moves on to the next multiple of 32 bits from the start of the stretch.</summary>
    public void AlignTo32()
    {
        var misaligned = Position & 31;
        if (misaligned != 0) _bit += 32 - misaligned;
    }

    public static int Log2(int value) => BitOperations.Log2((uint)value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Overrun() => throw new InvalidDataException("The Bink bit stream ended early.");
}
