using System.Buffers.Binary;

namespace CnCNew.Audio.Wav;

/// <summary>
/// IMA/DVI ADPCM (<c>WAVE_FORMAT_DVI_ADPCM</c>, 0x0011) as specified by the IMA "Recommended Practices for Enhancing
/// Digital Audio Compatibility in Multimedia Systems" (rev. 3.00, 1992; step and index tables, nibble decoder) and the
/// Microsoft "Multimedia Standards Update" (1994; block layout). Each block starts with one 4-byte header per channel
/// (int16 first sample, uint8 step index, reserved byte), followed by 4-byte words per channel in channel order, each word
/// carrying 8 samples, low nibble first.
/// </summary>
internal static class ImaAdpcm
{
    private static readonly short[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
        1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    private static readonly sbyte[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];

    public static int SamplesPerBlock(int blockAlign, int channels)
    {
        var header = 4 * channels;
        if (blockAlign <= header || (blockAlign - header) % (4 * channels) != 0)
            throw new InvalidDataException($"IMA ADPCM block alignment {blockAlign} is invalid for {channels} channel(s).");
        return (blockAlign - header) * 2 / channels + 1;
    }

    public static float[] Decode(ReadOnlySpan<byte> data, WavFormat format, Action<string, string> tolerate)
    {
        var channels = format.Channels;
        var blockAlign = format.BlockAlign;
        var header = 4 * channels;
        if (format.BitsPerSample != 4) throw new NotSupportedException($"IMA ADPCM with {format.BitsPerSample} bits per sample is not supported.");

        var fullBlocks = data.Length / blockAlign;
        var tail = data.Length % blockAlign;
        var tailGroups = tail >= header ? (tail - header) / (4 * channels) : 0;
        if (tail > 0 && tail < header)
            tolerate("partial-block", $"{tail} trailing bytes are shorter than an IMA ADPCM block header.");
        var tailFrames = tail >= header ? 1 + tailGroups * 8 : 0;
        var frames = fullBlocks * format.SamplesPerBlock + tailFrames;
        var output = new float[frames * channels];

        var outFrame = 0;
        Span<int> predictor = stackalloc int[channels];
        Span<int> index = stackalloc int[channels];
        for (var offset = 0; offset + header <= data.Length; offset += blockAlign)
        {
            var block = data.Slice(offset, Math.Min(blockAlign, data.Length - offset));
            var groups = (block.Length - header) / (4 * channels);
            for (var channel = 0; channel < channels; channel++)
            {
                predictor[channel] = BinaryPrimitives.ReadInt16LittleEndian(block[(4 * channel)..]);
                index[channel] = block[4 * channel + 2];
                if (index[channel] > 88)
                {
                    tolerate("ima-step-index-clamped", $"Block header step index {index[channel]} exceeds 88.");
                    index[channel] = 88;
                }
                output[outFrame * channels + channel] = PcmConvert.FromInt16((short)predictor[channel]);
            }

            for (var group = 0; group < groups; group++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var word = block.Slice(header + (group * channels + channel) * 4, 4);
                    for (var nibbleIndex = 0; nibbleIndex < 8; nibbleIndex++)
                    {
                        var value = word[nibbleIndex >> 1];
                        var nibble = (nibbleIndex & 1) == 0 ? value & 0x0F : value >> 4;
                        var sample = DecodeNibble(nibble, ref predictor[channel], ref index[channel]);
                        output[(outFrame + 1 + group * 8 + nibbleIndex) * channels + channel] = PcmConvert.FromInt16(sample);
                    }
                }
            }
            outFrame += 1 + groups * 8;
        }
        return output;
    }

    private static short DecodeNibble(int nibble, ref int predictor, ref int index)
    {
        int step = StepTable[index];
        var diff = step >> 3;
        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 1) != 0) diff += step >> 2;
        predictor = Math.Clamp((nibble & 8) != 0 ? predictor - diff : predictor + diff, short.MinValue, short.MaxValue);
        index = Math.Clamp(index + IndexTable[nibble], 0, 88);
        return (short)predictor;
    }
}
