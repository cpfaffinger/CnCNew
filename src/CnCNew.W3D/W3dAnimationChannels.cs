using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary>Channel type ids as stored in the channel <c>Flags</c> field (w3d_file.h ANIM_CHANNEL_*).</summary>
public enum W3dChannelType : ushort
{
    X = 0,
    Y = 1,
    Z = 2,
    XRotation = 3,
    YRotation = 4,
    ZRotation = 5,
    Quaternion = 6,
    TimeCodedX = 7,
    TimeCodedY = 8,
    TimeCodedZ = 9,
    TimeCodedQuaternion = 10,
    AdaptiveDeltaX = 11,
    AdaptiveDeltaY = 12,
    AdaptiveDeltaZ = 13,
    AdaptiveDeltaQuaternion = 14,
}

public enum W3dAnimationCompression
{
    None,
    TimeCoded,
    AdaptiveDelta,
}

public abstract class W3dMotionChannel
{
    private protected W3dMotionChannel(int pivot, W3dChannelType type)
    {
        Pivot = pivot;
        Type = type;
    }

    public int Pivot { get; }
    public W3dChannelType Type { get; }
    public int VectorLength => Type == W3dChannelType.Quaternion ? 4 : 1;

    /// <summary>All stored values in frame order (VectorLength floats per sample), for audits.</summary>
    public abstract ImmutableArray<float> Values { get; }
}

/// <summary>Uncompressed channel (W3dAnimChannelStruct); frames outside First..Last are identity (motchan.h Get_Vector).</summary>
public sealed class W3dRawMotionChannel : W3dMotionChannel
{
    public W3dRawMotionChannel(int pivot, W3dChannelType type, int firstFrame, int lastFrame, ImmutableArray<float> values)
        : base(pivot, type)
    {
        if (firstFrame < 0 || lastFrame < firstFrame || values.Length != (lastFrame - firstFrame + 1) * VectorLength)
        {
            throw new ArgumentException("Raw channel frame range does not match its value count.");
        }

        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        Values = values;
    }

    public int FirstFrame { get; }
    public int LastFrame { get; }
    public override ImmutableArray<float> Values { get; }

    public float GetScalar(int frame)
        => frame < FirstFrame || frame > LastFrame ? 0.0f : Values[frame - FirstFrame];

    public Quaternion GetQuaternion(int frame)
    {
        if (frame < FirstFrame || frame > LastFrame)
        {
            return Quaternion.Identity;
        }

        var offset = (frame - FirstFrame) * 4;
        return new Quaternion(Values[offset], Values[offset + 1], Values[offset + 2], Values[offset + 3]);
    }
}

/// <summary>
/// Time-coded channel (W3dTimeCodedAnimChannelStruct): sparse keys, each a uint32 frame whose MSB marks a step
/// ("binary movement") into that key. Re-implemented from motchan.cpp TimeCodedMotionChannelClass::Get_Vector,
/// Get_QuatVector and binary_search_index.
/// </summary>
public sealed class W3dTimeCodedMotionChannel : W3dMotionChannel
{
    public const uint BinaryMovementFlag = 0x80000000;

    public W3dTimeCodedMotionChannel(int pivot, W3dChannelType type, ImmutableArray<uint> timeCodes, ImmutableArray<float> values)
        : base(pivot, type)
    {
        if (timeCodes.IsDefaultOrEmpty || values.Length != timeCodes.Length * VectorLength)
        {
            throw new ArgumentException("Time-coded channel key count does not match its value count.");
        }

        if ((timeCodes[0] & ~BinaryMovementFlag) != 0)
        {
            throw new ArgumentException("The first time code of a channel must be frame 0.");
        }

        for (var index = 1; index < timeCodes.Length; index++)
        {
            if ((timeCodes[index] & ~BinaryMovementFlag) <= (timeCodes[index - 1] & ~BinaryMovementFlag))
            {
                throw new ArgumentException("Time codes must be strictly increasing.");
            }
        }

        TimeCodes = timeCodes;
        Values = values;
    }

    public ImmutableArray<uint> TimeCodes { get; }
    public override ImmutableArray<float> Values { get; }

    public int GetKeyFrame(int key) => (int)(TimeCodes[key] & ~BinaryMovementFlag);

    public float GetScalar(float frame)
    {
        var (key, next, ratio) = Locate(frame);
        if (next < 0)
        {
            return Values[key];
        }

        return W3dQuaternionMath.Lerp(Values[key], Values[next], ratio);
    }

    public Quaternion GetQuaternion(float frame)
    {
        var (key, next, ratio) = Locate(frame);
        var q1 = ReadQuaternion(key);
        return next < 0 ? q1 : W3dQuaternionMath.Slerp(q1, ReadQuaternion(next), ratio);
    }

    private Quaternion ReadQuaternion(int key)
    {
        var offset = key * 4;
        return new Quaternion(Values[offset], Values[offset + 1], Values[offset + 2], Values[offset + 3]);
    }

    private (int Key, int Next, float Ratio) Locate(float frame)
    {
        var timeCode = (uint)frame;
        var low = 0;
        var high = TimeCodes.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >> 1;
            if ((TimeCodes[middle] & ~BinaryMovementFlag) <= timeCode)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low == TimeCodes.Length - 1 || (TimeCodes[low + 1] & BinaryMovementFlag) != 0)
        {
            return (low, -1, 0);
        }

        float time1 = GetKeyFrame(low);
        float time2 = GetKeyFrame(low + 1);
        return (low, low + 1, (frame - time1) / (time2 - time1));
    }
}

/// <summary>
/// Adaptive-delta channel (W3dAdaptiveDeltaAnimChannelStruct): the frame-0 vector as floats followed by, per group of
/// 16 frames and per vector component, a 9-byte packet (filter-table index + 16 signed 4-bit deltas). Decoded once into a
/// dense table; the accumulation order equals motchan.cpp AdaptiveDeltaMotionChannelClass::decompress.
/// </summary>
public sealed class W3dAdaptiveDeltaMotionChannel : W3dMotionChannel
{
    private const int PacketSize = 9;
    private static readonly float[] FilterTable = CreateFilterTable();

    private W3dAdaptiveDeltaMotionChannel(int pivot, W3dChannelType type, int frameCount, float scale, ImmutableArray<float> values)
        : base(pivot, type)
    {
        FrameCount = frameCount;
        Scale = scale;
        Values = values;
    }

    public int FrameCount { get; }
    public float Scale { get; }

    /// <summary>The decoded dense table (FrameCount x VectorLength).</summary>
    public override ImmutableArray<float> Values { get; }

    public static int GetEncodedSize(int frameCount, int vectorLength)
        => vectorLength * 4 + (frameCount - 1 + 15) / 16 * PacketSize * vectorLength;

    public static W3dAdaptiveDeltaMotionChannel Decode(
        int pivot,
        W3dChannelType type,
        int frameCount,
        float scale,
        ReadOnlySpan<byte> encoded)
    {
        if (frameCount < 1)
        {
            throw new InvalidDataException("Adaptive-delta channels need at least one frame.");
        }

        var vectorLength = type == W3dChannelType.Quaternion ? 4 : 1;
        if (encoded.Length < GetEncodedSize(frameCount, vectorLength))
        {
            throw new InvalidDataException(
                $"Adaptive-delta channel for pivot {pivot} has {encoded.Length} data bytes; expected {GetEncodedSize(frameCount, vectorLength)}.");
        }

        var values = new float[frameCount * vectorLength];
        for (var component = 0; component < vectorLength; component++)
        {
            var value = W3dBinary.ReadSingle(encoded, component * 4);
            values[component] = value;
            for (var frame = 1; frame < frameCount; frame++)
            {
                var group = (frame - 1) >> 4;
                var nibbleIndex = (frame - 1) & 0xf;
                var packet = vectorLength * 4 + (group * vectorLength + component) * PacketSize;
                var filter = FilterTable[encoded[packet]] * scale;
                var packed = encoded[packet + 1 + (nibbleIndex >> 1)];
                var nibble = (nibbleIndex & 1) != 0 ? packed >> 4 : packed & 0xf;
                var factor = (nibble & 0x8) != 0 ? nibble - 16 : nibble;
                value += factor * filter;
                values[frame * vectorLength + component] = value;
            }
        }

        return new W3dAdaptiveDeltaMotionChannel(pivot, type, frameCount, scale, [.. values]);
    }

    public float GetScalar(float frame)
    {
        var frame1 = (uint)frame;
        var ratio = frame - frame1;
        return W3dQuaternionMath.Lerp(Get(frame1, 0), Get(frame1 + 1, 0), ratio);
    }

    public Quaternion GetQuaternion(float frame)
    {
        var frame1 = (uint)frame;
        var ratio = frame - frame1;
        var q1 = new Quaternion(Get(frame1, 0), Get(frame1, 1), Get(frame1, 2), Get(frame1, 3));
        var q2 = new Quaternion(Get(frame1 + 1, 0), Get(frame1 + 1, 1), Get(frame1 + 1, 2), Get(frame1 + 1, 3));
        return W3dQuaternionMath.Slerp(q1, q2, ratio);
    }

    private float Get(uint frame, int component)
    {
        var clamped = (int)Math.Min(frame, (uint)(FrameCount - 1));
        return Values[clamped * VectorLength + component];
    }

    /// <summary>motchan.cpp filtertable: 16 powers of ten, then 1 - sin(90 deg * i / 240) for i = 0..239.</summary>
    private static float[] CreateFilterTable()
    {
        var table = new float[256];
        float[] powers =
        [
            0.00000001f, 0.0000001f, 0.000001f, 0.00001f, 0.0001f, 0.001f, 0.01f, 0.1f,
            1.0f, 10.0f, 100.0f, 1000.0f, 10000.0f, 100000.0f, 1000000.0f, 10000000.0f,
        ];
        powers.CopyTo(table, 0);
        const int generated = 256 - 16;
        for (var index = 0; index < generated; index++)
        {
            var ratio = index / (float)generated;
            var radians = (double)(90.0f * ratio) * (double)3.141592654f / 180.0;
            table[index + 16] = 1.0f - MathF.Sin((float)radians);
        }

        return table;
    }

    public static float GetFilter(int index) => FilterTable[index];
}

public abstract class W3dVisibilityChannel
{
    private protected W3dVisibilityChannel(int pivot, bool defaultValue)
    {
        Pivot = pivot;
        DefaultValue = defaultValue;
    }

    public int Pivot { get; }
    public bool DefaultValue { get; }
    public abstract bool GetVisible(int frame);
}

/// <summary>Uncompressed bit channel (W3dBitChannelStruct); LSB-first bits for First..Last, default outside.</summary>
public sealed class W3dRawVisibilityChannel : W3dVisibilityChannel
{
    public W3dRawVisibilityChannel(int pivot, bool defaultValue, int firstFrame, int lastFrame, ImmutableArray<byte> bits)
        : base(pivot, defaultValue)
    {
        if (firstFrame < 0 || lastFrame < firstFrame || bits.Length != (lastFrame - firstFrame + 1 + 7) / 8)
        {
            throw new ArgumentException("Bit channel frame range does not match its byte count.");
        }

        FirstFrame = firstFrame;
        LastFrame = lastFrame;
        Bits = bits;
    }

    public int FirstFrame { get; }
    public int LastFrame { get; }
    public ImmutableArray<byte> Bits { get; }

    public override bool GetVisible(int frame)
    {
        if (frame < FirstFrame || frame > LastFrame)
        {
            return DefaultValue;
        }

        var bit = frame - FirstFrame;
        return (Bits[bit >> 3] & (1 << (bit & 7))) != 0;
    }
}

/// <summary>
/// Time-coded bit channel (W3dTimeCodedBitChannelStruct): each key is a frame with the state in its MSB. Like
/// motchan.cpp TimeCodedBitChannelClass::Get_Bit the last key at or before the frame wins and key 0 covers earlier
/// frames; the stored default value is not consulted by the original evaluation.
/// </summary>
public sealed class W3dTimeCodedVisibilityChannel : W3dVisibilityChannel
{
    public const uint StateMask = 0x80000000;

    public W3dTimeCodedVisibilityChannel(int pivot, bool defaultValue, ImmutableArray<uint> keys)
        : base(pivot, defaultValue)
    {
        if (keys.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Time-coded bit channels need at least one key.");
        }

        for (var index = 1; index < keys.Length; index++)
        {
            if ((keys[index] & ~StateMask) <= (keys[index - 1] & ~StateMask))
            {
                throw new ArgumentException("Bit channel time codes must be strictly increasing.");
            }
        }

        Keys = keys;
    }

    public ImmutableArray<uint> Keys { get; }

    public override bool GetVisible(int frame)
    {
        var selected = 0;
        for (var index = 1; index < Keys.Length && (Keys[index] & ~StateMask) <= (uint)Math.Max(frame, 0); index++)
        {
            selected = index;
        }

        return (Keys[selected] & StateMask) != 0;
    }
}
