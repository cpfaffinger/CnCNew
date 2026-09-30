using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary>
/// A decoded hierarchy animation (W3D_CHUNK_ANIMATION or W3D_CHUNK_COMPRESSED_ANIMATION). Channel values are deltas
/// applied on top of the hierarchy's base pose. Immutable and shareable; evaluate it through a <see cref="W3dAnimationBinding"/>.
/// </summary>
public sealed class W3dAnimation
{
    public W3dAnimation(
        uint version,
        string name,
        string hierarchyName,
        int frameCount,
        int frameRate,
        W3dAnimationCompression compression,
        ImmutableArray<W3dMotionChannel> channels,
        ImmutableArray<W3dVisibilityChannel> visibilityChannels,
        int surplusChannelBytes)
    {
        if (frameCount < 1 || frameRate < 1)
        {
            throw new ArgumentException($"Animation '{hierarchyName}.{name}' needs at least one frame and a positive frame rate.");
        }

        foreach (var channel in channels)
        {
            var matches = compression switch
            {
                W3dAnimationCompression.None => channel is W3dRawMotionChannel,
                W3dAnimationCompression.TimeCoded => channel is W3dTimeCodedMotionChannel,
                W3dAnimationCompression.AdaptiveDelta => channel is W3dAdaptiveDeltaMotionChannel,
                _ => false,
            };
            if (!matches)
            {
                throw new ArgumentException(
                    $"Animation '{hierarchyName}.{name}' mixes a {channel.GetType().Name} into a {compression} animation.");
            }
        }

        Version = version;
        Name = name;
        HierarchyName = hierarchyName;
        FrameCount = frameCount;
        FrameRate = frameRate;
        Compression = compression;
        Channels = channels;
        VisibilityChannels = visibilityChannels;
        SurplusChannelBytes = surplusChannelBytes;
    }

    public uint Version { get; }
    public string Name { get; }
    public string HierarchyName { get; }

    /// <summary>The asset-manager key of the original: <c>HierarchyName.Name</c>.</summary>
    public string FullName => $"{HierarchyName}.{Name}";

    public int FrameCount { get; }
    public int FrameRate { get; }
    public W3dAnimationCompression Compression { get; }
    public ImmutableArray<W3dMotionChannel> Channels { get; }
    public ImmutableArray<W3dVisibilityChannel> VisibilityChannels { get; }

    /// <summary>
    /// Bytes after the declared frame range of uncompressed channels. The exporter bug noted in motchan.cpp
    /// MotionChannelClass::Load_W3D wrote them; the original skips them and so do we, but they are counted.
    /// </summary>
    public int SurplusChannelBytes { get; }

    public double DurationSeconds => (double)FrameCount / FrameRate;

    /// <summary>
    /// Binds the channels to a hierarchy with <paramref name="pivotCount"/> pivots. Channels addressing pivots beyond the
    /// hierarchy are discarded and counted, as HRawAnimClass/HCompressedAnimClass::Load_W3D do.
    /// </summary>
    public W3dAnimationBinding Bind(int pivotCount) => new(this, pivotCount);
}

/// <summary>Per-pivot channel lookup of an animation for a hierarchy of known size. Immutable and shareable.</summary>
public sealed class W3dAnimationBinding
{
    private readonly PivotMotion[] _pivots;

    internal W3dAnimationBinding(W3dAnimation animation, int pivotCount)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pivotCount);
        Animation = animation;
        PivotCount = pivotCount;
        _pivots = new PivotMotion[pivotCount];
        var discarded = new List<string>();
        foreach (var channel in animation.Channels)
        {
            if (channel.Pivot >= pivotCount)
            {
                discarded.Add($"{channel.Type}@{channel.Pivot}");
                continue;
            }

            ref var motion = ref _pivots[channel.Pivot];
            switch (channel.Type)
            {
                case W3dChannelType.X:
                    motion.X = Assign(motion.X, channel);
                    break;
                case W3dChannelType.Y:
                    motion.Y = Assign(motion.Y, channel);
                    break;
                case W3dChannelType.Z:
                    motion.Z = Assign(motion.Z, channel);
                    break;
                case W3dChannelType.Quaternion:
                    motion.Q = Assign(motion.Q, channel);
                    break;
                case W3dChannelType.XRotation or W3dChannelType.YRotation or W3dChannelType.ZRotation:
                    IgnoredEulerChannels++;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Animation '{animation.FullName}' channel type {channel.Type} cannot be bound.");
            }
        }

        foreach (var channel in animation.VisibilityChannels)
        {
            if (channel.Pivot >= pivotCount)
            {
                discarded.Add($"Visibility@{channel.Pivot}");
                continue;
            }

            if (_pivots[channel.Pivot].Visibility is not null)
            {
                throw new InvalidDataException(
                    $"Animation '{animation.FullName}' has two visibility channels for pivot {channel.Pivot}.");
            }

            _pivots[channel.Pivot].Visibility = channel;
        }

        DiscardedChannels = [.. discarded];

        W3dMotionChannel Assign(W3dMotionChannel? existing, W3dMotionChannel channel)
            => existing is null
                ? channel
                : throw new InvalidDataException(
                    $"Animation '{animation.FullName}' has two {channel.Type} channels for pivot {channel.Pivot}.");
    }

    public W3dAnimation Animation { get; }
    public int PivotCount { get; }

    /// <summary>Channels whose pivot does not exist in the bound hierarchy (type@pivot), dropped like the original.</summary>
    public ImmutableArray<string> DiscardedChannels { get; }

    /// <summary>Euler channels (XR/YR/ZR) are loaded by the original but never evaluated; counted here.</summary>
    public int IgnoredEulerChannels { get; }

    public bool HasMotion(int pivot)
    {
        if ((uint)pivot >= (uint)PivotCount)
        {
            return false;
        }

        var motion = _pivots[pivot];
        return motion.X is not null || motion.Y is not null || motion.Z is not null || motion.Q is not null ||
               motion.Visibility is not null;
    }

    /// <summary>
    /// Returns the translation/rotation delta and visibility of <paramref name="pivot"/> at a fractional frame.
    /// Uncompressed channels use HRawAnimClass::Get_Translation/Get_Orientation (interpolated, the next frame of the last
    /// frame wraps to 0) or, with <see cref="W3dRawSampling.Nearest"/>, the retail HTreeClass::Anim_Update overload for
    /// raw animations (round to the nearest frame, no interpolation).
    /// </summary>
    public void Sample(int pivot, float frame, W3dRawSampling rawSampling, out Vector3 translation, out Quaternion rotation, out bool visible)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pivot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pivot, PivotCount);
        if (!float.IsFinite(frame) || frame < 0 || frame >= Animation.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), frame, $"Frame must be in [0, {Animation.FrameCount}).");
        }

        var motion = _pivots[pivot];
        visible = motion.Visibility?.GetVisible((int)frame) ?? true;
        switch (Animation.Compression)
        {
            case W3dAnimationCompression.None when rawSampling == W3dRawSampling.Nearest:
                SampleRawNearest(motion, frame, out translation, out rotation, out var nearestFrame);
                visible = motion.Visibility?.GetVisible(nearestFrame) ?? true;
                break;
            case W3dAnimationCompression.None:
                SampleRawInterpolated(motion, frame, out translation, out rotation);
                break;
            case W3dAnimationCompression.TimeCoded:
                translation = new Vector3(
                    (motion.X as W3dTimeCodedMotionChannel)?.GetScalar(frame) ?? 0,
                    (motion.Y as W3dTimeCodedMotionChannel)?.GetScalar(frame) ?? 0,
                    (motion.Z as W3dTimeCodedMotionChannel)?.GetScalar(frame) ?? 0);
                rotation = (motion.Q as W3dTimeCodedMotionChannel)?.GetQuaternion(frame) ?? Quaternion.Identity;
                break;
            case W3dAnimationCompression.AdaptiveDelta:
                translation = new Vector3(
                    (motion.X as W3dAdaptiveDeltaMotionChannel)?.GetScalar(frame) ?? 0,
                    (motion.Y as W3dAdaptiveDeltaMotionChannel)?.GetScalar(frame) ?? 0,
                    (motion.Z as W3dAdaptiveDeltaMotionChannel)?.GetScalar(frame) ?? 0);
                rotation = (motion.Q as W3dAdaptiveDeltaMotionChannel)?.GetQuaternion(frame) ?? Quaternion.Identity;
                break;
            default:
                throw new InvalidOperationException($"Unknown compression {Animation.Compression}.");
        }
    }

    private void SampleRawInterpolated(PivotMotion motion, float frame, out Vector3 translation, out Quaternion rotation)
    {
        // WWMath::Float_To_Long uses fistp (round half to even); frame - 0.499999 therefore yields floor(frame).
        var frame0 = (int)MathF.Round(frame - 0.499999f, MidpointRounding.ToEven);
        var frame1 = frame0 + 1;
        var ratio = frame - frame0;
        if (frame1 >= Animation.FrameCount)
        {
            frame1 = 0;
        }

        var x = motion.X as W3dRawMotionChannel;
        var y = motion.Y as W3dRawMotionChannel;
        var z = motion.Z as W3dRawMotionChannel;
        var q = motion.Q as W3dRawMotionChannel;
        var translation0 = new Vector3(x?.GetScalar(frame0) ?? 0, y?.GetScalar(frame0) ?? 0, z?.GetScalar(frame0) ?? 0);
        var q0 = q?.GetQuaternion(frame0) ?? Quaternion.Identity;
        var q1 = q?.GetQuaternion(frame1) ?? Quaternion.Identity;
        if (ratio == 0.0f)
        {
            translation = translation0;
            rotation = q0;
            return;
        }

        var translation1 = new Vector3(x?.GetScalar(frame1) ?? 0, y?.GetScalar(frame1) ?? 0, z?.GetScalar(frame1) ?? 0);
        translation = new Vector3(
            W3dQuaternionMath.Lerp(translation0.X, translation1.X, ratio),
            W3dQuaternionMath.Lerp(translation0.Y, translation1.Y, ratio),
            W3dQuaternionMath.Lerp(translation0.Z, translation1.Z, ratio));
        rotation = ratio == 1.0f ? q1 : W3dQuaternionMath.Slerp(q0, q1, ratio);
    }

    private void SampleRawNearest(PivotMotion motion, float frame, out Vector3 translation, out Quaternion rotation, out int nearest)
    {
        nearest = (int)MathF.Round(frame, MidpointRounding.ToEven);
        if (nearest >= Animation.FrameCount)
        {
            nearest = 0;
        }

        translation = new Vector3(
            (motion.X as W3dRawMotionChannel)?.GetScalar(nearest) ?? 0,
            (motion.Y as W3dRawMotionChannel)?.GetScalar(nearest) ?? 0,
            (motion.Z as W3dRawMotionChannel)?.GetScalar(nearest) ?? 0);
        rotation = (motion.Q as W3dRawMotionChannel)?.GetQuaternion(nearest) ?? Quaternion.Identity;
    }

    private struct PivotMotion
    {
        public W3dMotionChannel? X;
        public W3dMotionChannel? Y;
        public W3dMotionChannel? Z;
        public W3dMotionChannel? Q;
        public W3dVisibilityChannel? Visibility;
    }
}

public enum W3dRawSampling
{
    /// <summary>Linear/slerp between neighbouring frames (HRawAnimClass, GeneralsGameCode default).</summary>
    Interpolated,

    /// <summary>Nearest frame without interpolation (retail HTreeClass::Anim_Update for HRawAnimClass).</summary>
    Nearest,
}
