using System.Runtime.CompilerServices;

namespace CnCNew.Video;

/// <summary>
/// The video codec of Bink 1, revision <c>i</c>: every plane of a frame is a grid of 8 x 8 blocks (16 x 16 "scaled" ones cover four cells), whose types, colours, patterns, motion
/// values, DC values and run lengths lie in separate bundles of entropy-coded values, refilled a row of blocks at a time. Rewritten from the public description of the format
/// (MultimediaWiki "Bink Video") and FFmpeg's <c>libavcodec/bink.c</c> and <c>binkdsp.c</c> (LGPL 2.1 or later); see docs/CLIENT-VIDEO.md. The planes are 8-bit Y, U, V (4:2:0) and
/// optionally A; arithmetic on pixels wraps at 256 as in those sources, it does not saturate. A stream that cannot be decoded throws <see cref="InvalidDataException"/>.
/// </summary>
public sealed class BinkVideoDecoder
{
    private const int SkipBlock = 0, ScaledBlock = 1, MotionBlock = 2, RunBlock = 3, ResidueBlock = 4, IntraBlock = 5, FillBlock = 6, InterBlock = 7, PatternBlock = 8, RawBlock = 9;
    private const int BlockTypes = 0, SubBlockTypes = 1, Colors = 2, Pattern = 3, XOffsets = 4, YOffsets = 5, IntraDc = 6, InterDc = 7, Runs = 8;
    private const int BundleCount = 9;
    private const int DcStartBits = 11;
    private const int MaxColorRead = 2;

    private static readonly ushort[][] TreeTables = BuildTreeTables();

    private readonly int _width;
    private readonly int _height;
    private readonly PlaneShape[] _shapes;
    private byte[][] _current;
    private byte[][] _last;
    private readonly Bundle[] _bundles = new Bundle[BundleCount];
    private readonly Tree[] _colorHigh = new Tree[16];
    private int _colorLast;
    private BinkBitReader _bits;

    private readonly int[] _dctBlock = new int[64];
    private readonly int[] _dctTemp = new int[64];
    private readonly short[] _residue = new short[64];
    private readonly byte[] _smallBlock = new byte[64];
    private readonly int[] _coefficientIndices = new int[64];
    private readonly int[] _coefficientList = new int[128];
    private readonly int[] _modeList = new int[128];
    private readonly int[] _nonZero = new int[64];
    private readonly int[] _colors = new int[2];
    private readonly byte[] _mergeA = new byte[16];
    private readonly byte[] _mergeB = new byte[16];

    public BinkVideoDecoder(int width, int height, bool hasAlpha)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _width = width;
        _height = height;
        HasAlpha = hasAlpha;
        var luma = new PlaneShape(width, height, (width + 7) >> 3, (height + 7) >> 3);
        var chroma = new PlaneShape(width >> 1, height >> 1, (width + 15) >> 4, (height + 15) >> 4);
        _shapes = hasAlpha ? [luma, chroma, chroma, luma] : [luma, chroma, chroma];
        _current = Array.ConvertAll(_shapes, shape => new byte[shape.Stride * shape.Rows]);
        _last = Array.ConvertAll(_shapes, shape => new byte[shape.Stride * shape.Rows]);

        var cells = luma.BlocksWide * luma.BlocksHigh;
        for (var index = 0; index < BundleCount; index++) _bundles[index] = new Bundle(index is IntraDc or InterDc ? 0 : cells * 64, index is IntraDc or InterDc ? cells * 32 : 0);
        for (var index = 0; index < 16; index++) _colorHigh[index] = new Tree();
    }

    public int Width => _width;
    public int Height => _height;
    public bool HasAlpha { get; }

    /// <summary>Decodes one video packet of <paramref name="data"/>, which has <see cref="BinkBitReader.Padding"/> spare bytes behind it. An empty packet repeats the picture.</summary>
    internal void Decode(byte[] data, int offset, int length)
    {
        if (length == 0) return;
        try
        {
            _bits = new BinkBitReader(data, offset, length);
            if (HasAlpha)
            {
                _bits.Skip(32); // the size of the plane
                DecodePlane(3, chroma: false);
            }
            _bits.Skip(32);
            var bitCount = length * 8;
            for (var plane = 0; plane < 3; plane++)
            {
                // From revision h on the plane after luminance is V: the bit stream's order is Y, V, U.
                var index = plane == 0 ? 0 : plane ^ 3;
                DecodePlane(index, plane != 0);
                if (_bits.Position >= bitCount && plane < 2)
                {
                    // A frame that ends here keeps the rest of the last picture.
                    for (var rest = plane + 1; rest < 3; rest++)
                    {
                        var other = rest ^ 3;
                        Array.Copy(_last[other], _current[other], _current[other].Length);
                    }
                    break;
                }
            }
        }
        catch (IndexOutOfRangeException exception)
        {
            throw new InvalidDataException("A Bink video frame leaves its tables.", exception);
        }
        (_current, _last) = (_last, _current);
    }

    /// <summary>Copies the last decoded picture as 8-bit R, G, B, A in rows from the top; without an alpha plane A is 255.</summary>
    public void CopyRgba(Span<byte> destination)
    {
        var size = _width * _height * 4;
        if (destination.Length < size) throw new ArgumentException($"The picture needs {size} bytes.", nameof(destination));
        var yPlane = _last[0];
        var uPlane = _last[1];
        var vPlane = _last[2];
        var yStride = _shapes[0].Stride;
        var cStride = _shapes[1].Stride;
        var alpha = HasAlpha ? _last[3] : null;
        var output = 0;
        for (var row = 0; row < _height; row++)
        {
            var yRow = row * yStride;
            var cRow = (row >> 1) * cStride;
            var alphaRow = row * yStride;
            for (var column = 0; column < _width; column++)
            {
                var c = cRow + (column >> 1);
                var y = 298 * (yPlane[yRow + column] - 16) + 128;
                var u = uPlane[c] - 128;
                var v = vPlane[c] - 128;
                destination[output] = Clamp((y + 409 * v) >> 8);
                destination[output + 1] = Clamp((y - 100 * u - 208 * v) >> 8);
                destination[output + 2] = Clamp((y + 516 * u) >> 8);
                destination[output + 3] = alpha is null ? (byte)255 : alpha[alphaRow + column];
                output += 4;
            }
        }
    }

    /// <summary>A plane of the last decoded picture, for the checks: 0 Y, 1 U, 2 V, 3 A.</summary>
    public ReadOnlySpan<byte> Plane(int index, out int stride, out int rows)
    {
        var shape = _shapes[index];
        stride = shape.Stride;
        rows = shape.Rows;
        return _last[index];
    }

    /// <summary>Feeds the visible part of the last picture's planes to <paramref name="sink"/>, row by row: the bytes a frame's checksum is taken over.</summary>
    public void WriteVisible(Action<ReadOnlySpan<byte>> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        for (var plane = 0; plane < _shapes.Length; plane++)
        {
            var shape = _shapes[plane];
            for (var row = 0; row < shape.Height; row++) sink(_last[plane].AsSpan(row * shape.Stride, shape.Width));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Clamp(int value) => (uint)value <= 255u ? (byte)value : value < 0 ? (byte)0 : (byte)255;

    // ------------------------------------------------------------------------------------------ bundles

    private static ushort[][] BuildTreeTables()
    {
        var tables = new ushort[16][];
        for (var tree = 0; tree < 16; tree++)
        {
            var longest = BinkTables.TreeLengths[tree * 16 + 15];
            var table = new ushort[1 << longest];
            for (var symbol = 0; symbol < 16; symbol++)
            {
                int code = BinkTables.TreeBits[tree * 16 + symbol];
                int length = BinkTables.TreeLengths[tree * 16 + symbol];
                for (var high = 0; high < 1 << (longest - length); high++) table[code | (high << length)] = (ushort)((length << 8) | symbol);
            }
            tables[tree] = table;
        }
        return tables;
    }

    private void ReadTree(Tree tree)
    {
        tree.Table = TreeTables[(int)_bits.Read(4)];
        var symbols = tree.Symbols;
        if (ReferenceEquals(tree.Table, TreeTables[0]))
        {
            for (var index = 0; index < 16; index++) symbols[index] = (byte)index;
            return;
        }
        if (_bits.ReadBit())
        {
            var last = (int)_bits.Read(3);
            Span<bool> used = stackalloc bool[16];
            used.Clear();
            for (var index = 0; index <= last; index++)
            {
                symbols[index] = (byte)_bits.Read(4);
                used[symbols[index]] = true;
            }
            for (var index = 0; index < 16 && last < 15; index++)
                if (!used[index]) symbols[++last] = (byte)index;
            return;
        }

        var rounds = (int)_bits.Read(2);
        var input = _mergeA;
        var output = _mergeB;
        for (var index = 0; index < 16; index++) input[index] = (byte)index;
        for (var round = 0; round <= rounds; round++)
        {
            var size = 1 << round;
            for (var start = 0; start < 16; start += size << 1) Merge(output, start, input, start, size);
            (input, output) = (output, input);
        }
        Array.Copy(input, symbols, 16);
    }

    /// <summary>Merges two neighbouring lists of <paramref name="size"/> entries, each bit choosing which goes next.</summary>
    private void Merge(byte[] destination, int target, byte[] source, int first, int size)
    {
        var second = first + size;
        var left = size;
        var right = size;
        do
        {
            if (!_bits.ReadBit())
            {
                destination[target++] = source[first++];
                left--;
            }
            else
            {
                destination[target++] = source[second++];
                right--;
            }
        } while (left != 0 && right != 0);
        while (left-- > 0) destination[target++] = source[first++];
        while (right-- > 0) destination[target++] = source[second++];
    }

    private void InitializeLengths(int width, int blocksWide)
    {
        width = (width + 7) & ~7;
        var byWidth = BinkBitReader.Log2((width >> 3) + 511) + 1;
        _bundles[BlockTypes].Length = byWidth;
        _bundles[SubBlockTypes].Length = BinkBitReader.Log2((width >> 4) + 511) + 1;
        _bundles[Colors].Length = BinkBitReader.Log2(blocksWide * 64 + 511) + 1;
        _bundles[IntraDc].Length = byWidth;
        _bundles[InterDc].Length = byWidth;
        _bundles[XOffsets].Length = byWidth;
        _bundles[YOffsets].Length = byWidth;
        _bundles[Pattern].Length = BinkBitReader.Log2((blocksWide << 3) + 511) + 1;
        _bundles[Runs].Length = BinkBitReader.Log2(blocksWide * 48 + 511) + 1;
    }

    private void ReadBundle(int index)
    {
        if (index == Colors)
        {
            for (var tree = 0; tree < 16; tree++) ReadTree(_colorHigh[tree]);
            _colorLast = 0;
        }
        if (index is not IntraDc and not InterDc) ReadTree(_bundles[index].Tree);
        _bundles[index].Decoded = 0;
        _bundles[index].Read = 0;
    }

    /// <summary>How many values a bundle takes in now: 0 while it still holds unread ones or after it said it holds no more.</summary>
    private int NextCount(Bundle bundle)
    {
        if (bundle.Decoded < 0 || bundle.Decoded > bundle.Read) return 0;
        var count = (int)_bits.Read(bundle.Length);
        if (count == 0) bundle.Decoded = -1;
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadHuffman(Tree tree)
    {
        var table = tree.Table;
        var entry = table[_bits.Peek() & (uint)(table.Length - 1)];
        _bits.Skip(entry >> 8);
        return tree.Symbols[entry & 15];
    }

    private void ReadRuns(Bundle bundle)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var end = bundle.Decoded + count;
        if (end > bundle.Bytes.Length) throw new InvalidDataException("Too many Bink run values.");
        if (_bits.ReadBit())
        {
            bundle.Bytes.AsSpan(bundle.Decoded, count).Fill((byte)_bits.Read(4));
            bundle.Decoded = end;
            return;
        }
        while (bundle.Decoded < end) bundle.Bytes[bundle.Decoded++] = (byte)ReadHuffman(bundle.Tree);
    }

    private void ReadMotionValues(Bundle bundle)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var end = bundle.Decoded + count;
        if (end > bundle.Bytes.Length) throw new InvalidDataException("Too many Bink motion values.");
        if (_bits.ReadBit())
        {
            var value = (int)_bits.Read(4);
            if (value != 0 && _bits.ReadBit()) value = -value;
            bundle.Bytes.AsSpan(bundle.Decoded, count).Fill((byte)value);
            bundle.Decoded = end;
            return;
        }
        while (bundle.Decoded < end)
        {
            var value = ReadHuffman(bundle.Tree);
            if (value != 0 && _bits.ReadBit()) value = -value;
            bundle.Bytes[bundle.Decoded++] = (byte)value;
        }
    }

    private void ReadBlockTypes(Bundle bundle)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var end = bundle.Decoded + count;
        if (end > bundle.Bytes.Length) throw new InvalidDataException("Too many Bink block types.");
        if (_bits.ReadBit())
        {
            bundle.Bytes.AsSpan(bundle.Decoded, count).Fill((byte)_bits.Read(4));
            bundle.Decoded = end;
            return;
        }
        var last = 0;
        while (bundle.Decoded < end)
        {
            var value = ReadHuffman(bundle.Tree);
            if (value < 12)
            {
                last = value;
                bundle.Bytes[bundle.Decoded++] = (byte)value;
                continue;
            }
            var run = value switch { 12 => 4, 13 => 8, 14 => 12, _ => 32 };
            if (end - bundle.Decoded < run) throw new InvalidDataException("A Bink block type run leaves its row.");
            bundle.Bytes.AsSpan(bundle.Decoded, run).Fill((byte)last);
            bundle.Decoded += run;
        }
    }

    private void ReadPatterns(Bundle bundle)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var end = bundle.Decoded + count;
        if (end > bundle.Bytes.Length) throw new InvalidDataException("Too many Bink pattern values.");
        while (bundle.Decoded < end)
        {
            var low = ReadHuffman(bundle.Tree);
            var high = ReadHuffman(bundle.Tree);
            bundle.Bytes[bundle.Decoded++] = (byte)(low | (high << 4));
        }
    }

    private void ReadColors(Bundle bundle)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var end = bundle.Decoded + count;
        if (end > bundle.Bytes.Length) throw new InvalidDataException("Too many Bink colour values.");
        if (_bits.ReadBit())
        {
            _colorLast = ReadHuffman(_colorHigh[_colorLast]);
            var value = (_colorLast << 4) | ReadHuffman(bundle.Tree);
            bundle.Bytes.AsSpan(bundle.Decoded, count).Fill((byte)value);
            bundle.Decoded = end;
            return;
        }
        while (bundle.Decoded < end)
        {
            if (_bits.BitsLeft < MaxColorRead) throw new InvalidDataException("The Bink colours end early.");
            _colorLast = ReadHuffman(_colorHigh[_colorLast]);
            bundle.Bytes[bundle.Decoded++] = (byte)((_colorLast << 4) | ReadHuffman(bundle.Tree));
        }
    }

    private void ReadDcs(Bundle bundle, bool signed)
    {
        var count = NextCount(bundle);
        if (count == 0) return;
        var value = (int)_bits.Read(DcStartBits - (signed ? 1 : 0));
        if (value != 0 && signed && _bits.ReadBit()) value = -value;
        var values = bundle.Words;
        var position = bundle.Decoded;
        if (position >= values.Length) throw new InvalidDataException("Too many Bink DC values.");
        values[position++] = (short)value;
        count--;
        for (var done = 0; done < count; done += 8)
        {
            var group = Math.Min(count - done, 8);
            if (values.Length - position < group) throw new InvalidDataException("Too many Bink DC values.");
            var size = (int)_bits.Read(4);
            if (size == 0)
            {
                values.AsSpan(position, group).Fill((short)value);
                position += group;
                continue;
            }
            for (var index = 0; index < group; index++)
            {
                var difference = (int)_bits.Read(size);
                if (difference != 0 && _bits.ReadBit()) difference = -difference;
                value += difference;
                if (value is < short.MinValue or > short.MaxValue) throw new InvalidDataException($"A Bink DC value left its range: {value}.");
                values[position++] = (short)value;
            }
        }
        bundle.Decoded = position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Value(int bundle)
    {
        var entry = _bundles[bundle];
        return entry.Bytes[entry.Read++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SignedValue(int bundle)
    {
        var entry = _bundles[bundle];
        return (sbyte)entry.Bytes[entry.Read++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DcValue(int bundle)
    {
        var entry = _bundles[bundle];
        return entry.Words[entry.Read++];
    }

    // ------------------------------------------------------------------------------------------ coefficients

    /// <summary>The DCT coefficients of a block, largest bit width first, then the index of its quantisation matrix.</summary>
    private int ReadDctCoefficients(int[] block, out int count)
    {
        var scan = BinkTables.Scan;
        var coefficients = _coefficientList;
        var modes = _modeList;
        var indices = _coefficientIndices;
        var start = 64;
        var end = 64;
        coefficients[end] = 4; modes[end++] = 0;
        coefficients[end] = 24; modes[end++] = 0;
        coefficients[end] = 44; modes[end++] = 0;
        coefficients[end] = 1; modes[end++] = 3;
        coefficients[end] = 2; modes[end++] = 3;
        coefficients[end] = 3; modes[end++] = 3;

        count = 0;
        for (var bits = (int)_bits.Read(4) - 1; bits >= 0; bits--)
        {
            var position = start;
            while (position < end)
            {
                if ((modes[position] | coefficients[position]) == 0 || !_bits.ReadBit())
                {
                    position++;
                    continue;
                }
                var coefficient = coefficients[position];
                var mode = modes[position];
                switch (mode)
                {
                    case 0:
                    case 2:
                        if (mode == 0)
                        {
                            coefficients[position] = coefficient + 4;
                            modes[position] = 1;
                        }
                        else
                        {
                            coefficients[position] = 0;
                            modes[position++] = 0;
                        }
                        for (var step = 0; step < 4; step++, coefficient++)
                        {
                            if (_bits.ReadBit())
                            {
                                coefficients[--start] = coefficient;
                                modes[start] = 3;
                                continue;
                            }
                            block[scan[coefficient]] = ReadCoefficient(bits);
                            indices[count++] = coefficient;
                        }
                        break;
                    case 1:
                        modes[position] = 2;
                        for (var step = 0; step < 3; step++)
                        {
                            coefficient += 4;
                            coefficients[end] = coefficient;
                            modes[end++] = 2;
                        }
                        break;
                    default:
                        block[scan[coefficient]] = ReadCoefficient(bits);
                        indices[count++] = coefficient;
                        coefficients[position] = 0;
                        modes[position++] = 0;
                        break;
                }
            }
        }
        return (int)_bits.Read(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadCoefficient(int bits)
    {
        if (bits == 0) return _bits.ReadBit() ? -1 : 1;
        var value = (int)_bits.Read(bits) | (1 << bits);
        return _bits.ReadBit() ? -value : value;
    }

    private void Unquantize(int[] block, ReadOnlySpan<int> matrices, int quantIndex, int count)
    {
        var scan = BinkTables.Scan;
        var quant = matrices.Slice(quantIndex * 64, 64);
        block[0] = (block[0] * quant[0]) >> 11;
        var indices = _coefficientIndices;
        for (var index = 0; index < count; index++)
        {
            var position = indices[index];
            int place = scan[position];
            block[place] = (block[place] * quant[position]) >> 11;
        }
    }

    /// <summary>The residue after a motion copy, bit plane after bit plane, until <paramref name="masks"/> bits were spent.</summary>
    private void ReadResidue(short[] block, int masks)
    {
        var scan = BinkTables.Scan;
        var coefficients = _coefficientList;
        var modes = _modeList;
        var nonZero = _nonZero;
        var nonZeroCount = 0;
        var start = 64;
        var end = 64;
        coefficients[end] = 4; modes[end++] = 0;
        coefficients[end] = 24; modes[end++] = 0;
        coefficients[end] = 44; modes[end++] = 0;
        coefficients[end] = 0; modes[end++] = 2;

        for (var mask = 1 << (int)_bits.Read(3); mask != 0; mask >>= 1)
        {
            for (var index = 0; index < nonZeroCount; index++)
            {
                if (!_bits.ReadBit()) continue;
                ref var value = ref block[nonZero[index]];
                value = (short)(value < 0 ? value - mask : value + mask);
                if (--masks < 0) return;
            }
            var position = start;
            while (position < end)
            {
                if ((coefficients[position] | modes[position]) == 0 || !_bits.ReadBit())
                {
                    position++;
                    continue;
                }
                var coefficient = coefficients[position];
                var mode = modes[position];
                switch (mode)
                {
                    case 0:
                    case 2:
                        if (mode == 0)
                        {
                            coefficients[position] = coefficient + 4;
                            modes[position] = 1;
                        }
                        else
                        {
                            coefficients[position] = 0;
                            modes[position++] = 0;
                        }
                        for (var step = 0; step < 4; step++, coefficient++)
                        {
                            if (_bits.ReadBit())
                            {
                                coefficients[--start] = coefficient;
                                modes[start] = 3;
                                continue;
                            }
                            int place = scan[coefficient];
                            nonZero[nonZeroCount++] = place;
                            block[place] = (short)(_bits.ReadBit() ? -mask : mask);
                            if (--masks < 0) return;
                        }
                        break;
                    case 1:
                        modes[position] = 2;
                        for (var step = 0; step < 3; step++)
                        {
                            coefficient += 4;
                            coefficients[end] = coefficient;
                            modes[end++] = 2;
                        }
                        break;
                    default:
                    {
                        int place = scan[coefficient];
                        nonZero[nonZeroCount++] = place;
                        block[place] = (short)(_bits.ReadBit() ? -mask : mask);
                        coefficients[position] = 0;
                        modes[position++] = 0;
                        if (--masks < 0) return;
                        break;
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------ transforms

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Scale(int factor, int value) => (factor * value) >> 11;

    /// <summary>The inverse DCT of <paramref name="block"/> into <paramref name="pixels"/> (with its stride), added to what is there when <paramref name="add"/> is set.</summary>
    private void InverseDct(int[] block, byte[] pixels, int offset, int stride, bool add)
    {
        const int A1 = 2896, A2 = 2217, A3 = 3784, A4 = -5352;
        var temp = _dctTemp;
        for (var column = 0; column < 8; column++)
        {
            int s0 = block[column], s1 = block[column + 8], s2 = block[column + 16], s3 = block[column + 24], s4 = block[column + 32], s5 = block[column + 40], s6 = block[column + 48], s7 = block[column + 56];
            if ((s1 | s2 | s3 | s4 | s5 | s6 | s7) == 0)
            {
                temp[column] = temp[column + 8] = temp[column + 16] = temp[column + 24] = temp[column + 32] = temp[column + 40] = temp[column + 48] = temp[column + 56] = s0;
                continue;
            }
            var a0 = s0 + s4;
            var a1 = s0 - s4;
            var a2 = s2 + s6;
            var a3 = Scale(A1, s2 - s6);
            var a4 = s5 + s3;
            var a5 = s5 - s3;
            var a6 = s1 + s7;
            var a7 = s1 - s7;
            var b0 = a4 + a6;
            var b1 = Scale(A3, a5 + a7);
            var b2 = Scale(A4, a5) - b0 + b1;
            var b3 = Scale(A1, a6 - a4) - b2;
            var b4 = Scale(A2, a7) + b3 - b1;
            temp[column] = a0 + a2 + b0;
            temp[column + 8] = a1 + a3 - a2 + b2;
            temp[column + 16] = a1 - a3 + a2 + b3;
            temp[column + 24] = a0 - a2 - b4;
            temp[column + 32] = a0 - a2 + b4;
            temp[column + 40] = a1 - a3 + a2 - b3;
            temp[column + 48] = a1 + a3 - a2 - b2;
            temp[column + 56] = a0 + a2 - b0;
        }

        for (var row = 0; row < 8; row++)
        {
            var line = row * 8;
            int s0 = temp[line], s1 = temp[line + 1], s2 = temp[line + 2], s3 = temp[line + 3], s4 = temp[line + 4], s5 = temp[line + 5], s6 = temp[line + 6], s7 = temp[line + 7];
            var a0 = s0 + s4;
            var a1 = s0 - s4;
            var a2 = s2 + s6;
            var a3 = Scale(A1, s2 - s6);
            var a4 = s5 + s3;
            var a5 = s5 - s3;
            var a6 = s1 + s7;
            var a7 = s1 - s7;
            var b0 = a4 + a6;
            var b1 = Scale(A3, a5 + a7);
            var b2 = Scale(A4, a5) - b0 + b1;
            var b3 = Scale(A1, a6 - a4) - b2;
            var b4 = Scale(A2, a7) + b3 - b1;
            var out0 = (a0 + a2 + b0 + 0x7F) >> 8;
            var out1 = (a1 + a3 - a2 + b2 + 0x7F) >> 8;
            var out2 = (a1 - a3 + a2 + b3 + 0x7F) >> 8;
            var out3 = (a0 - a2 - b4 + 0x7F) >> 8;
            var out4 = (a0 - a2 + b4 + 0x7F) >> 8;
            var out5 = (a1 - a3 + a2 - b3 + 0x7F) >> 8;
            var out6 = (a1 + a3 - a2 - b2 + 0x7F) >> 8;
            var out7 = (a0 + a2 - b0 + 0x7F) >> 8;
            var target = pixels.AsSpan(offset + row * stride, 8);
            if (add)
            {
                target[0] = (byte)(target[0] + out0);
                target[1] = (byte)(target[1] + out1);
                target[2] = (byte)(target[2] + out2);
                target[3] = (byte)(target[3] + out3);
                target[4] = (byte)(target[4] + out4);
                target[5] = (byte)(target[5] + out5);
                target[6] = (byte)(target[6] + out6);
                target[7] = (byte)(target[7] + out7);
            }
            else
            {
                target[0] = (byte)out0;
                target[1] = (byte)out1;
                target[2] = (byte)out2;
                target[3] = (byte)out3;
                target[4] = (byte)out4;
                target[5] = (byte)out5;
                target[6] = (byte)out6;
                target[7] = (byte)out7;
            }
        }
    }

    /// <summary>Doubles the 8 x 8 block <paramref name="source"/> to 16 x 16.</summary>
    private static void ScaleBlock(byte[] source, byte[] pixels, int offset, int stride)
    {
        for (var row = 0; row < 8; row++)
        {
            var first = pixels.AsSpan(offset, 16);
            var second = pixels.AsSpan(offset + stride, 16);
            for (var column = 0; column < 8; column++)
            {
                var value = source[row * 8 + column];
                first[column * 2] = value;
                first[column * 2 + 1] = value;
                second[column * 2] = value;
                second[column * 2 + 1] = value;
            }
            offset += stride * 2;
        }
    }

    private static void CopyBlock(byte[] source, int from, byte[] target, int to, int stride)
    {
        for (var row = 0; row < 8; row++)
        {
            source.AsSpan(from, 8).CopyTo(target.AsSpan(to, 8));
            from += stride;
            to += stride;
        }
    }

    private void AddResidue(byte[] pixels, int offset, int stride)
    {
        var block = _residue;
        for (var row = 0; row < 8; row++)
        {
            var target = pixels.AsSpan(offset, 8);
            for (var column = 0; column < 8; column++) target[column] = (byte)(target[column] + block[row * 8 + column]);
            offset += stride;
        }
    }

    // ------------------------------------------------------------------------------------------ planes

    private void DecodePlane(int planeIndex, bool chroma)
    {
        var shape = _shapes[planeIndex];
        var target = _current[planeIndex];
        var previous = _last[planeIndex];
        var stride = shape.Stride;
        var blocksWide = shape.BlocksWide;
        var blocksHigh = shape.BlocksHigh;
        InitializeLengths(Math.Max(shape.Width, 8), blocksWide);
        for (var index = 0; index < BundleCount; index++) ReadBundle(index);
        var referenceLimit = (blocksWide - 1 + stride * (blocksHigh - 1)) * 8;
        var dct = _dctBlock;
        var small = _smallBlock;
        var patterns = BinkTables.Patterns;

        for (var blockRow = 0; blockRow < blocksHigh; blockRow++)
        {
            ReadBlockTypes(_bundles[BlockTypes]);
            ReadBlockTypes(_bundles[SubBlockTypes]);
            ReadColors(_bundles[Colors]);
            ReadPatterns(_bundles[Pattern]);
            ReadMotionValues(_bundles[XOffsets]);
            ReadMotionValues(_bundles[YOffsets]);
            ReadDcs(_bundles[IntraDc], signed: false);
            ReadDcs(_bundles[InterDc], signed: true);
            ReadRuns(_bundles[Runs]);

            var position = 8 * blockRow * stride;
            for (var blockColumn = 0; blockColumn < blocksWide; blockColumn++, position += 8)
            {
                var type = Value(BlockTypes);
                // On odd lines and columns a 16 x 16 type is a part of the block already decoded.
                if (((blockRow & 1) != 0 || (blockColumn & 1) != 0) && type == ScaledBlock)
                {
                    blockColumn++;
                    position += 8;
                    continue;
                }
                switch (type)
                {
                    case SkipBlock:
                        CopyBlock(previous, position, target, position, stride);
                        break;
                    case ScaledBlock:
                    {
                        var subType = Value(SubBlockTypes);
                        switch (subType)
                        {
                            case RunBlock:
                                ReadRun(small, 0, 8, patterns);
                                break;
                            case IntraBlock:
                            {
                                Array.Clear(dct);
                                dct[0] = DcValue(IntraDc);
                                var quant = ReadDctCoefficients(dct, out var count);
                                Unquantize(dct, BinkTables.IntraQuant, quant, count);
                                InverseDct(dct, small, 0, 8, add: false);
                                break;
                            }
                            case FillBlock:
                            {
                                var color = (byte)Value(Colors);
                                for (var row = 0; row < 16; row++) target.AsSpan(position + row * stride, 16).Fill(color);
                                break;
                            }
                            case PatternBlock:
                                _colors[0] = Value(Colors);
                                _colors[1] = Value(Colors);
                                for (var row = 0; row < 8; row++)
                                {
                                    var bits = Value(Pattern);
                                    for (var column = 0; column < 8; column++, bits >>= 1) small[column + row * 8] = (byte)_colors[bits & 1];
                                }
                                break;
                            case RawBlock:
                                for (var row = 0; row < 8; row++)
                                    for (var column = 0; column < 8; column++)
                                        small[column + row * 8] = (byte)Value(Colors);
                                break;
                            default:
                                throw new InvalidDataException($"Incorrect Bink 16 x 16 block type {subType}.");
                        }
                        if (subType != FillBlock) ScaleBlock(small, target, position, stride);
                        blockColumn++;
                        position += 8;
                        break;
                    }
                    case MotionBlock:
                        CopyMotion(previous, target, position, stride, referenceLimit);
                        break;
                    case RunBlock:
                        ReadRun(target, position, stride, patterns);
                        break;
                    case ResidueBlock:
                    {
                        CopyMotion(previous, target, position, stride, referenceLimit);
                        Array.Clear(_residue);
                        var masks = (int)_bits.Read(7);
                        ReadResidue(_residue, masks);
                        AddResidue(target, position, stride);
                        break;
                    }
                    case IntraBlock:
                    {
                        Array.Clear(dct);
                        dct[0] = DcValue(IntraDc);
                        var quant = ReadDctCoefficients(dct, out var count);
                        Unquantize(dct, BinkTables.IntraQuant, quant, count);
                        InverseDct(dct, target, position, stride, add: false);
                        break;
                    }
                    case FillBlock:
                    {
                        var color = (byte)Value(Colors);
                        for (var row = 0; row < 8; row++) target.AsSpan(position + row * stride, 8).Fill(color);
                        break;
                    }
                    case InterBlock:
                    {
                        CopyMotion(previous, target, position, stride, referenceLimit);
                        Array.Clear(dct);
                        dct[0] = DcValue(InterDc);
                        var quant = ReadDctCoefficients(dct, out var count);
                        Unquantize(dct, BinkTables.InterQuant, quant, count);
                        InverseDct(dct, target, position, stride, add: true);
                        break;
                    }
                    case PatternBlock:
                        _colors[0] = Value(Colors);
                        _colors[1] = Value(Colors);
                        for (var row = 0; row < 8; row++)
                        {
                            var bits = Value(Pattern);
                            var line = target.AsSpan(position + row * stride, 8);
                            for (var column = 0; column < 8; column++, bits >>= 1) line[column] = (byte)_colors[bits & 1];
                        }
                        break;
                    case RawBlock:
                    {
                        var colors = _bundles[Colors];
                        for (var row = 0; row < 8; row++) colors.Bytes.AsSpan(colors.Read + row * 8, 8).CopyTo(target.AsSpan(position + row * stride, 8));
                        colors.Read += 64;
                        break;
                    }
                    default:
                        throw new InvalidDataException($"Unknown Bink block type {type}.");
                }
            }
        }
        _bits.AlignTo32();
    }

    private void CopyMotion(byte[] previous, byte[] target, int position, int stride, int limit)
    {
        var x = SignedValue(XOffsets);
        var y = SignedValue(YOffsets);
        var reference = position + x + y * stride;
        if (reference < 0 || reference > limit) throw new InvalidDataException($"A Bink motion copy leaves its picture: {x}, {y}.");
        CopyBlock(previous, reference, target, position, stride);
    }

    /// <summary>A block of runs of colours along one of the sixteen patterns.</summary>
    private void ReadRun(byte[] target, int offset, int stride, ReadOnlySpan<byte> patterns)
    {
        var pattern = patterns.Slice((int)_bits.Read(4) * 64, 64);
        var scan = 0;
        var done = 0;
        do
        {
            var run = Value(Runs) + 1;
            done += run;
            if (done > 64) throw new InvalidDataException("A Bink run leaves its block.");
            if (_bits.ReadBit())
            {
                var color = (byte)Value(Colors);
                for (var index = 0; index < run; index++)
                {
                    int cell = pattern[scan++];
                    target[offset + (cell & 7) + (cell >> 3) * stride] = color;
                }
            }
            else
            {
                for (var index = 0; index < run; index++)
                {
                    int cell = pattern[scan++];
                    target[offset + (cell & 7) + (cell >> 3) * stride] = (byte)Value(Colors);
                }
            }
        } while (done < 63);
        if (done == 63)
        {
            int cell = pattern[scan];
            target[offset + (cell & 7) + (cell >> 3) * stride] = (byte)Value(Colors);
        }
    }

    /// <summary>A plane's size and its grid of blocks. A 16 x 16 block on the last column may reach one cell past the grid, which is what the extra cell of stride and rows is for.</summary>
    private sealed record PlaneShape(int Width, int Height, int BlocksWide, int BlocksHigh)
    {
        public int Stride => (BlocksWide + 1) * 8;
        public int Rows => (BlocksHigh + 1) * 8;
    }

    private sealed class Tree
    {
        public ushort[] Table = TreeTables[0];
        public readonly byte[] Symbols = new byte[16];
    }

    private sealed class Bundle(int bytes, int words)
    {
        public int Length;
        public readonly Tree Tree = new();
        public readonly byte[] Bytes = new byte[bytes];
        public readonly short[] Words = new short[words];

        /// <summary>How far values were decoded; -1 once the bundle said there are no more in this plane.</summary>
        public int Decoded;
        public int Read;
    }
}
