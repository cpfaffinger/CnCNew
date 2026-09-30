using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CnCNew.Audio;

/// <summary>
/// Decoded audio: interleaved float samples in [-1, 1]. <see cref="Samples"/> holds
/// <see cref="FrameCount"/> × <see cref="Channels"/> values (frame-major, channel-minor).
/// </summary>
public sealed record PcmBuffer
{
    public PcmBuffer(int sampleRate, int channels, ImmutableArray<float> samples)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        if (channels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo buffers are supported.");
        if (samples.IsDefault) throw new ArgumentException("Samples must be initialized.", nameof(samples));
        if (samples.Length % channels != 0) throw new ArgumentException("The sample count must be a multiple of the channel count.", nameof(samples));
        SampleRate = sampleRate;
        Channels = channels;
        Samples = samples;
    }

    public int SampleRate { get; }
    public int Channels { get; }
    public ImmutableArray<float> Samples { get; }
    public int FrameCount => Samples.Length / Channels;
    public double DurationSeconds => (double)FrameCount / SampleRate;

    /// <summary>Takes ownership of <paramref name="samples"/> without copying; the caller must not modify the array afterwards.</summary>
    public static PcmBuffer FromOwnedArray(int sampleRate, int channels, float[] samples)
        => new(sampleRate, channels, ImmutableCollectionsMarshal.AsImmutableArray(samples));
}

/// <summary>Channel and rate conversion helpers used by the decoders and the mixer.</summary>
public static class PcmConvert
{
    public static float FromInt16(short value) => value / 32768f;
    public static float FromUInt8(byte value) => (value - 128) / 128f;

    /// <summary>Averages both channels; a mono buffer is returned unchanged.</summary>
    public static PcmBuffer ToMono(PcmBuffer buffer)
    {
        if (buffer.Channels == 1) return buffer;
        var source = buffer.Samples.AsSpan();
        var result = new float[buffer.FrameCount];
        for (var frame = 0; frame < result.Length; frame++)
            result[frame] = (source[2 * frame] + source[2 * frame + 1]) * 0.5f;
        return PcmBuffer.FromOwnedArray(buffer.SampleRate, 1, result);
    }

    /// <summary>Duplicates a mono channel into both stereo channels; a stereo buffer is returned unchanged.</summary>
    public static PcmBuffer ToStereo(PcmBuffer buffer)
    {
        if (buffer.Channels == 2) return buffer;
        var source = buffer.Samples.AsSpan();
        var result = new float[source.Length * 2];
        for (var frame = 0; frame < source.Length; frame++)
            result[2 * frame] = result[2 * frame + 1] = source[frame];
        return PcmBuffer.FromOwnedArray(buffer.SampleRate, 2, result);
    }

    /// <summary>
    /// Linear-interpolation resampler. Output frame <c>i</c> samples the source at <c>i × sourceRate / targetRate</c>;
    /// the last source frame is held beyond the end. Linear interpolation has no anti-aliasing filter: downsampling folds
    /// content above the new Nyquist frequency back into the audible band and upsampling slightly dulls the top octave.
    /// That is adequate for the 22/44.1 kHz game assets and keeps the mixer allocation-free.
    /// </summary>
    public static PcmBuffer Resample(PcmBuffer buffer, int targetRate)
    {
        if (targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(targetRate), targetRate, "The target rate must be positive.");
        if (targetRate == buffer.SampleRate || buffer.FrameCount == 0) return buffer;
        var channels = buffer.Channels;
        var source = buffer.Samples.AsSpan();
        var frames = (int)((long)buffer.FrameCount * targetRate / buffer.SampleRate);
        var result = new float[frames * channels];
        var step = (double)buffer.SampleRate / targetRate;
        var last = buffer.FrameCount - 1;
        for (var frame = 0; frame < frames; frame++)
        {
            var position = frame * step;
            var index = (int)position;
            var fraction = (float)(position - index);
            var next = Math.Min(index + 1, last);
            for (var channel = 0; channel < channels; channel++)
            {
                var a = source[index * channels + channel];
                var b = source[next * channels + channel];
                result[frame * channels + channel] = a + (b - a) * fraction;
            }
        }
        return PcmBuffer.FromOwnedArray(targetRate, channels, result);
    }
}
