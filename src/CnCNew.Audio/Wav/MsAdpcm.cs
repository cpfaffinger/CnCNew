using System.Buffers.Binary;

namespace CnCNew.Audio.Wav;

/// <summary>
/// Microsoft ADPCM (<c>WAVE_FORMAT_ADPCM</c>, 0x0002) as specified by the Microsoft "Multimedia Standards Update"
/// (rev. 3.0, 1994): the <c>fmt </c> extension carries <c>wSamplesPerBlock</c>, <c>wNumCoef</c> and the predictor
/// coefficient pairs; each block header holds per channel the predictor index (uint8), then per channel the initial
/// delta, second sample and first sample (int16 each). The two header samples are emitted oldest first, followed by
/// 4-bit codes, high nibble first, alternating channels for stereo.
/// </summary>
internal static class MsAdpcm
{
    private static readonly int[] AdaptationTable = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

    public static int SamplesPerBlock(int blockAlign, int channels)
    {
        var header = 7 * channels;
        if (blockAlign <= header) throw new InvalidDataException($"MS ADPCM block alignment {blockAlign} is invalid for {channels} channel(s).");
        return (blockAlign - header) * 2 / channels + 2;
    }

    public static float[] Decode(ReadOnlySpan<byte> data, WavFormat format, ReadOnlySpan<byte> extra, Action<string, string> tolerate)
    {
        if (format.BitsPerSample != 4) throw new NotSupportedException($"MS ADPCM with {format.BitsPerSample} bits per sample is not supported.");
        if (extra.Length < 4) throw new InvalidDataException("MS ADPCM 'fmt ' extension is missing.");
        var declaredSamplesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(extra);
        var coefficientCount = BinaryPrimitives.ReadUInt16LittleEndian(extra[2..]);
        if (coefficientCount == 0 || extra.Length < 4 + coefficientCount * 4)
            throw new InvalidDataException($"MS ADPCM declares {coefficientCount} coefficient pairs but the extension has {extra.Length} bytes.");
        var coefficients = new (int First, int Second)[coefficientCount];
        for (var index = 0; index < coefficientCount; index++)
            coefficients[index] = (BinaryPrimitives.ReadInt16LittleEndian(extra[(4 + index * 4)..]), BinaryPrimitives.ReadInt16LittleEndian(extra[(6 + index * 4)..]));

        var channels = format.Channels;
        var blockAlign = format.BlockAlign;
        var header = 7 * channels;
        var samplesPerBlock = format.SamplesPerBlock;
        if (declaredSamplesPerBlock != samplesPerBlock)
        {
            if (declaredSamplesPerBlock > samplesPerBlock)
                throw new InvalidDataException($"MS ADPCM declares {declaredSamplesPerBlock} samples per block but a {blockAlign}-byte block holds {samplesPerBlock}.");
            tolerate("adpcm-samples-per-block", $"MS ADPCM declares {declaredSamplesPerBlock} samples per block; the block holds {samplesPerBlock}.");
            samplesPerBlock = declaredSamplesPerBlock;
        }

        var frames = 0;
        for (var offset = 0; offset < data.Length; offset += blockAlign)
        {
            var length = Math.Min(blockAlign, data.Length - offset);
            if (length < header)
            {
                tolerate("partial-block", $"{length} trailing bytes are shorter than an MS ADPCM block header.");
                break;
            }
            frames += Math.Min(samplesPerBlock, 2 + (length - header) * 2 / channels);
        }

        var output = new float[frames * channels];
        var outFrame = 0;
        Span<int> delta = stackalloc int[channels];
        Span<int> sample1 = stackalloc int[channels];
        Span<int> sample2 = stackalloc int[channels];
        Span<int> predictorIndex = stackalloc int[channels];
        for (var offset = 0; offset + header <= data.Length; offset += blockAlign)
        {
            var block = data.Slice(offset, Math.Min(blockAlign, data.Length - offset));
            var blockFrames = Math.Min(samplesPerBlock, 2 + (block.Length - header) * 2 / channels);
            for (var channel = 0; channel < channels; channel++)
            {
                predictorIndex[channel] = block[channel];
                if (predictorIndex[channel] >= coefficientCount)
                    throw new InvalidDataException($"MS ADPCM predictor index {predictorIndex[channel]} exceeds the {coefficientCount} coefficient pairs.");
                delta[channel] = BinaryPrimitives.ReadInt16LittleEndian(block[(channels + 2 * channel)..]);
                sample1[channel] = BinaryPrimitives.ReadInt16LittleEndian(block[(3 * channels + 2 * channel)..]);
                sample2[channel] = BinaryPrimitives.ReadInt16LittleEndian(block[(5 * channels + 2 * channel)..]);
                output[outFrame * channels + channel] = PcmConvert.FromInt16((short)sample2[channel]);
                output[(outFrame + 1) * channels + channel] = PcmConvert.FromInt16((short)sample1[channel]);
            }

            var codes = (blockFrames - 2) * channels;
            for (var code = 0; code < codes; code++)
            {
                var value = block[header + code / 2];
                var nibble = (code & 1) == 0 ? value >> 4 : value & 0x0F;
                var channel = code % channels;
                var (first, second) = coefficients[predictorIndex[channel]];
                var predicted = (sample1[channel] * first + sample2[channel] * second) >> 8;
                var signed = nibble >= 8 ? nibble - 16 : nibble;
                var decoded = Math.Clamp(predicted + signed * delta[channel], short.MinValue, short.MaxValue);
                sample2[channel] = sample1[channel];
                sample1[channel] = decoded;
                delta[channel] = Math.Max(16, AdaptationTable[nibble] * delta[channel] >> 8);
                output[(outFrame + 2 + code / channels) * channels + channel] = PcmConvert.FromInt16((short)decoded);
            }
            outFrame += blockFrames;
        }
        return output;
    }
}
