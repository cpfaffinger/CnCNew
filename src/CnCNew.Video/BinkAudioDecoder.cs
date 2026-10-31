namespace CnCNew.Video;

/// <summary>
/// Bink audio in its DCT variant, the one the game ships: a stream of blocks, each per channel two floats, quantisers per critical band, then runs of coefficients of a common bit
/// width, transformed by an inverse DCT; the first samples of a block fade over from the end of the one before. Rewritten from the public description of the format
/// (MultimediaWiki "Bink Audio") and FFmpeg's <c>libavcodec/binkaudio.c</c> (LGPL 2.1 or later); see docs/CLIENT-VIDEO.md. A packet that cannot be decoded throws
/// <see cref="InvalidDataException"/>.
/// </summary>
public sealed class BinkAudioDecoder
{
    private const int QuantBits = 8;
    private const int MaxBands = 25;
    private static ReadOnlySpan<ushort> CriticalFrequencies =>
    [
        100, 200, 300, 400, 510, 630, 770, 920, 1080, 1270, 1480, 1720, 2000, 2320, 2700, 3150, 3700, 4400, 5300, 6400, 7700, 9500, 12000, 15500, 24500,
    ];
    private static ReadOnlySpan<byte> RunLengths => [2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 32, 64];

    private readonly int _channels;
    private readonly int _frameLength;
    private readonly int _overlap;
    private readonly int _bandCount;
    private readonly float _root;
    private readonly int[] _bands = new int[MaxBands + 1];
    private readonly float[] _quantTable = new float[96];
    private readonly float[] _quant = new float[MaxBands];
    private readonly float[][] _previous;
    private readonly float[][] _output;
    private readonly float[] _coefficients;
    private readonly BinkDct _dct;
    private short[] _pcm = new short[8192];
    private bool _first = true;

    public BinkAudioDecoder(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels), channels, "Bink audio has one or two channels here.");
        SampleRate = sampleRate;
        _channels = channels;
        var frameBits = sampleRate < 22050 ? 9 : sampleRate < 44100 ? 10 : 11;
        _frameLength = 1 << frameBits;
        _overlap = _frameLength / 16;
        var halfRate = (sampleRate + 1) / 2;
        var frequencies = CriticalFrequencies;
        _root = (float)(_frameLength / (Math.Sqrt(_frameLength) * 32768.0));
        for (var index = 0; index < 96; index++) _quantTable[index] = (float)Math.Exp(index * 0.15289164787221953823) * _root;

        _bandCount = 1;
        while (_bandCount < MaxBands && halfRate > frequencies[_bandCount - 1]) _bandCount++;
        _bands[0] = 2;
        for (var index = 1; index < _bandCount; index++) _bands[index] = (frequencies[index - 1] * _frameLength / halfRate) & ~1;
        _bands[_bandCount] = _frameLength;

        _previous = new float[channels][];
        _output = new float[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            _previous[channel] = new float[_overlap];
            _output[channel] = new float[_frameLength];
        }
        _coefficients = new float[_frameLength + 2];
        _dct = new BinkDct(_frameLength);
    }

    public int SampleRate { get; }
    public int Channels => _channels;

    /// <summary>The samples of one block per channel.</summary>
    public int BlockSamples => _frameLength - _overlap;

    /// <summary>Decodes one audio packet of <paramref name="data"/> (which has <see cref="BinkBitReader.Padding"/> spare bytes behind it): interleaved 16-bit samples, valid until the next call.</summary>
    internal ReadOnlySpan<short> Decode(byte[] data, int offset, int length)
    {
        if (length < 4) throw new InvalidDataException("A Bink audio packet is too small.");
        var bits = new BinkBitReader(data, offset, length);
        bits.Skip(32); // the size of the packet decoded, which only the demuxer's clock uses
        var samples = 0;
        while (bits.BitsLeft > 0)
        {
            var needed = samples + BlockSamples * _channels;
            if (needed > _pcm.Length) Array.Resize(ref _pcm, Math.Max(needed, _pcm.Length * 2));
            DecodeBlock(ref bits);
            Interleave(samples);
            samples = needed;
            bits.AlignTo32();
        }
        return _pcm.AsSpan(0, samples);
    }

    private void DecodeBlock(ref BinkBitReader bits)
    {
        var coefficients = _coefficients;
        var frameLength = _frameLength;
        bits.Skip(2);
        for (var channel = 0; channel < _channels; channel++)
        {
            if (bits.BitsLeft < 58 + _bandCount * QuantBits) throw new InvalidDataException("A Bink audio block ends early.");
            coefficients[0] = ReadFloat(ref bits) * _root;
            coefficients[1] = ReadFloat(ref bits) * _root;
            for (var band = 0; band < _bandCount; band++) _quant[band] = _quantTable[Math.Min((int)bits.Read(QuantBits), 95)];

            var bandIndex = 0;
            var quant = _quant[0];
            var position = 2;
            while (position < frameLength)
            {
                var stop = position + 8;
                if (bits.ReadBit()) stop = position + RunLengths[(int)bits.Read(4)] * 8;
                stop = Math.Min(stop, frameLength);
                var width = (int)bits.Read(4);
                if (width == 0)
                {
                    Array.Clear(coefficients, position, stop - position);
                    position = stop;
                    while (_bands[bandIndex] < position) quant = _quant[bandIndex++];
                    continue;
                }
                while (position < stop)
                {
                    if (_bands[bandIndex] == position) quant = _quant[bandIndex++];
                    var value = (int)bits.Read(width);
                    coefficients[position++] = value == 0 ? 0f : bits.ReadBit() ? -quant * value : quant * value;
                }
            }

            coefficients[0] *= 2f;
            _dct.Inverse(coefficients, _output[channel]);
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            var output = _output[channel];
            var previous = _previous[channel];
            if (!_first)
            {
                var count = _overlap * _channels;
                var weight = channel;
                for (var index = 0; index < _overlap; index++, weight += _channels)
                    output[index] = (previous[index] * (count - weight) + output[index] * weight) / count;
            }
            Array.Copy(output, frameLength - _overlap, previous, 0, _overlap);
        }
        _first = false;
    }

    private void Interleave(int start)
    {
        var count = BlockSamples;
        for (var channel = 0; channel < _channels; channel++)
        {
            var output = _output[channel];
            var position = start + channel;
            for (var index = 0; index < count; index++, position += _channels)
                _pcm[position] = (short)Math.Clamp((int)MathF.Round(output[index] * 32768f), short.MinValue, short.MaxValue);
        }
    }

    private static float ReadFloat(ref BinkBitReader bits)
    {
        var power = (int)bits.Read(5);
        var value = MathF.ScaleB(bits.Read(23), power - 23);
        return bits.ReadBit() ? -value : value;
    }
}

/// <summary>
/// The inverse DCT of Bink audio, a DCT-III of a power-of-two length N:
/// <c>x[n] = 2/N * (X[0]/2 + sum over k from 1 to N-1 of X[k] cos(pi k (n + 1/2) / N))</c>. It is computed through one complex FFT of length N (Makhoul 1980): the spectrum is
/// turned by a quarter of a sample and made Hermitian, and the real result lies in the even samples upward and the odd samples downward.
/// </summary>
internal sealed class BinkDct
{
    private readonly int _length;
    private readonly float[] _rotationCos;
    private readonly float[] _rotationSin;
    private readonly float[] _twiddleCos;
    private readonly float[] _twiddleSin;
    private readonly int[] _reversed;
    private readonly float[] _real;
    private readonly float[] _imaginary;

    public BinkDct(int length)
    {
        if (length < 2 || !int.IsPow2(length)) throw new ArgumentOutOfRangeException(nameof(length), length, "The transform length must be a power of two.");
        _length = length;
        _rotationCos = new float[length];
        _rotationSin = new float[length];
        _twiddleCos = new float[length / 2];
        _twiddleSin = new float[length / 2];
        _reversed = new int[length];
        _real = new float[length];
        _imaginary = new float[length];
        for (var index = 0; index < length; index++)
        {
            var angle = Math.PI * index / (2.0 * length);
            _rotationCos[index] = (float)Math.Cos(angle);
            _rotationSin[index] = (float)Math.Sin(angle);
        }
        for (var index = 0; index < length / 2; index++)
        {
            var angle = 2.0 * Math.PI * index / length;
            _twiddleCos[index] = (float)Math.Cos(angle);
            _twiddleSin[index] = (float)Math.Sin(angle);
        }
        var bits = int.Log2(length);
        for (var index = 0; index < length; index++)
        {
            var reversed = 0;
            for (var bit = 0; bit < bits; bit++)
                if ((index & (1 << bit)) != 0) reversed |= 1 << (bits - 1 - bit);
            _reversed[index] = reversed;
        }
    }

    public void Inverse(ReadOnlySpan<float> spectrum, Span<float> output)
    {
        var length = _length;
        var real = _real;
        var imaginary = _imaginary;
        real[0] = spectrum[0];
        imaginary[0] = 0f;
        for (var index = 1; index < length; index++)
        {
            // e^(i pi k / 2N) * (X[k] - i X[N-k]), written into its bit-reversed place.
            var place = _reversed[index];
            var a = spectrum[index];
            var b = -spectrum[length - index];
            real[place] = a * _rotationCos[index] - b * _rotationSin[index];
            imaginary[place] = a * _rotationSin[index] + b * _rotationCos[index];
        }

        for (var size = 2; size <= length; size <<= 1)
        {
            var half = size >> 1;
            var step = length / size;
            for (var start = 0; start < length; start += size)
            {
                for (int low = start, high = start + half, twiddle = 0; low < start + half; low++, high++, twiddle += step)
                {
                    var cos = _twiddleCos[twiddle];
                    var sin = _twiddleSin[twiddle];
                    var tr = real[high] * cos - imaginary[high] * sin;
                    var ti = real[high] * sin + imaginary[high] * cos;
                    real[high] = real[low] - tr;
                    imaginary[high] = imaginary[low] - ti;
                    real[low] += tr;
                    imaginary[low] += ti;
                }
            }
        }

        var scale = 1f / length;
        for (var index = 0; index < length / 2; index++)
        {
            output[2 * index] = real[index] * scale;
            output[2 * index + 1] = real[length - 1 - index] * scale;
        }
    }
}
