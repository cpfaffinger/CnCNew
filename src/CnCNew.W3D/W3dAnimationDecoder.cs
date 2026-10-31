using System.Collections.Immutable;

namespace CnCNew.W3D;

public static class W3dAnimationDecoder
{
    private const int HeaderSize = 44;
    private const int RawChannelHeaderSize = 12;
    private const int BitChannelHeaderSize = 9;
    private const int TimeCodedChannelHeaderSize = 8;
    private const int AdaptiveDeltaChannelHeaderSize = 12;
    private const int TimeCodedBitChannelHeaderSize = 8;

    public static W3dAnimation Decode(ReadOnlySpan<byte> data, W3dChunk animation)
    {
        ArgumentNullException.ThrowIfNull(animation);
        return animation.Type switch
        {
            W3dChunkType.Animation => DecodeRaw(data, animation),
            W3dChunkType.CompressedAnimation => DecodeCompressed(data, animation),
            _ => throw new ArgumentException($"Chunk 0x{animation.Type:X8} is not an animation.", nameof(animation)),
        };
    }

    private static W3dAnimation DecodeRaw(ReadOnlySpan<byte> data, W3dChunk animation)
    {
        var (version, name, hierarchyName, frameCount, frameRate, _) =
            ReadHeader(data, animation, W3dChunkType.AnimationHeader, compressed: false);
        W3dBinary.ValidateVersion(version, W3dBinary.MakeVersion(3, 0), $"Animation '{hierarchyName}.{name}'");
        var channels = ImmutableArray.CreateBuilder<W3dMotionChannel>();
        var bits = ImmutableArray.CreateBuilder<W3dVisibilityChannel>();
        var surplus = 0;
        foreach (var child in animation.Children.Skip(1))
        {
            var context = $"Animation '{hierarchyName}.{name}' chunk 0x{child.Type:X8} at {child.HeaderOffset}";
            var payload = W3dBinary.Slice(data, W3dBinary.RequireLeaf(child, context));
            switch (child.Type)
            {
                case W3dChunkType.AnimationChannel:
                    channels.Add(ReadRawChannel(payload, frameCount, context, ref surplus));
                    break;
                case W3dChunkType.BitChannel:
                    bits.Add(ReadRawBitChannel(payload, frameCount, context));
                    break;
                default:
                    throw new InvalidDataException($"{context} is not an animation channel.");
            }
        }

        return new W3dAnimation(
            version,
            name,
            hierarchyName,
            frameCount,
            frameRate,
            W3dAnimationCompression.None,
            channels.ToImmutable(),
            bits.ToImmutable(),
            surplus);
    }

    private static W3dAnimation DecodeCompressed(ReadOnlySpan<byte> data, W3dChunk animation)
    {
        var (version, name, hierarchyName, frameCount, frameRate, flavor) =
            ReadHeader(data, animation, W3dChunkType.CompressedAnimationHeader, compressed: true);
        var compression = flavor switch
        {
            0 => W3dAnimationCompression.TimeCoded,
            1 => W3dAnimationCompression.AdaptiveDelta,
            _ => throw new InvalidDataException($"Compressed animation '{hierarchyName}.{name}' has unknown flavor {flavor}."),
        };
        var channels = ImmutableArray.CreateBuilder<W3dMotionChannel>();
        var bits = ImmutableArray.CreateBuilder<W3dVisibilityChannel>();
        var surplus = 0;
        foreach (var child in animation.Children.Skip(1))
        {
            var context = $"Compressed animation '{hierarchyName}.{name}' chunk 0x{child.Type:X8} at {child.HeaderOffset}";
            var payload = W3dBinary.Slice(data, W3dBinary.RequireLeaf(child, context));
            switch (child.Type)
            {
                case W3dChunkType.CompressedAnimationChannel when compression == W3dAnimationCompression.TimeCoded:
                    channels.Add(ReadTimeCodedChannel(payload, frameCount, context));
                    break;
                case W3dChunkType.CompressedAnimationChannel:
                    channels.Add(ReadAdaptiveDeltaChannel(payload, context, ref surplus));
                    break;
                case W3dChunkType.CompressedBitChannel:
                    bits.Add(ReadTimeCodedBitChannel(payload, frameCount, context));
                    break;
                default:
                    throw new InvalidDataException($"{context} is not a compressed animation channel.");
            }
        }

        return new W3dAnimation(
            version,
            name,
            hierarchyName,
            frameCount,
            frameRate,
            compression,
            channels.ToImmutable(),
            bits.ToImmutable(),
            surplus);
    }

    private static (uint Version, string Name, string HierarchyName, int FrameCount, int FrameRate, int Flavor) ReadHeader(
        ReadOnlySpan<byte> data,
        W3dChunk animation,
        uint headerType,
        bool compressed)
    {
        W3dBinary.RequireContainer(animation, "Animation");
        if (animation.Children.Count == 0 || animation.Children[0].Type != headerType)
        {
            throw new InvalidDataException($"Animation at {animation.HeaderOffset} does not start with its header chunk.");
        }

        var header = W3dBinary.Slice(data, W3dBinary.RequireSize(
            W3dBinary.RequireLeaf(animation.Children[0], "Animation header"), HeaderSize, "Animation header"));
        var version = W3dBinary.ReadUInt32(header, 0);
        var name = W3dBinary.ReadFixedString(header.Slice(4, W3dBinary.NameLength));
        var hierarchyName = W3dBinary.ReadFixedString(header.Slice(20, W3dBinary.NameLength));
        var frameCount = W3dBinary.ReadUInt32(header, 36);
        var frameRate = compressed ? W3dBinary.ReadUInt16(header, 40) : W3dBinary.ReadUInt32(header, 40);
        var flavor = compressed ? W3dBinary.ReadUInt16(header, 42) : 0;
        if (frameCount < 1 || frameCount > ushort.MaxValue + 1u || frameRate < 1 || frameRate > 1000)
        {
            throw new InvalidDataException(
                $"Animation '{hierarchyName}.{name}' has {frameCount} frames at {frameRate} fps.");
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(hierarchyName))
        {
            throw new InvalidDataException($"Animation at {animation.HeaderOffset} has an empty name or hierarchy name.");
        }

        return (version, name, hierarchyName, (int)frameCount, (int)frameRate, flavor);
    }

    private static W3dRawMotionChannel ReadRawChannel(ReadOnlySpan<byte> payload, int frameCount, string context, ref int surplus)
    {
        if (payload.Length < RawChannelHeaderSize + 4)
        {
            throw new InvalidDataException($"{context} has only {payload.Length} bytes.");
        }

        var firstFrame = W3dBinary.ReadUInt16(payload, 0);
        var lastFrame = W3dBinary.ReadUInt16(payload, 2);
        var vectorLength = W3dBinary.ReadUInt16(payload, 4);
        var type = ToMotionType(W3dBinary.ReadUInt16(payload, 6), allowEuler: true, context);
        var pivot = W3dBinary.ReadUInt16(payload, 8);
        ValidateVectorLength(type, vectorLength, context);
        if (lastFrame < firstFrame || lastFrame >= frameCount)
        {
            throw new InvalidDataException($"{context} covers frames {firstFrame}..{lastFrame} of {frameCount}.");
        }

        var valueCount = (lastFrame - firstFrame + 1) * vectorLength;
        var required = RawChannelHeaderSize + valueCount * 4;
        if (payload.Length < required)
        {
            throw new InvalidDataException($"{context} has {payload.Length} bytes; its frame range needs {required}.");
        }

        surplus += payload.Length - required;
        var values = new float[valueCount];
        for (var index = 0; index < valueCount; index++)
        {
            values[index] = W3dBinary.ReadSingle(payload, RawChannelHeaderSize + index * 4);
        }

        return new W3dRawMotionChannel(pivot, type, firstFrame, lastFrame, [.. values]);
    }

    private static W3dRawVisibilityChannel ReadRawBitChannel(ReadOnlySpan<byte> payload, int frameCount, string context)
    {
        if (payload.Length < BitChannelHeaderSize + 1)
        {
            throw new InvalidDataException($"{context} has only {payload.Length} bytes.");
        }

        var firstFrame = W3dBinary.ReadUInt16(payload, 0);
        var lastFrame = W3dBinary.ReadUInt16(payload, 2);
        var type = W3dBinary.ReadUInt16(payload, 4);
        var pivot = W3dBinary.ReadUInt16(payload, 6);
        var defaultValue = payload[8];
        if (type != 0)
        {
            throw new InvalidDataException($"{context} has unknown bit channel type {type}; only visibility (0) exists.");
        }

        // Any value but 0 means visible: BitChannelClass::Get_Bit returns DefaultVal as it stands, and two models of the community patch write 255.
        if (lastFrame < firstFrame || lastFrame >= frameCount)
        {
            throw new InvalidDataException($"{context} has frames {firstFrame}..{lastFrame} of {frameCount}.");
        }

        var byteCount = (lastFrame - firstFrame + 1 + 7) / 8;
        if (payload.Length != BitChannelHeaderSize + byteCount)
        {
            throw new InvalidDataException(
                $"{context} has {payload.Length} bytes; expected {BitChannelHeaderSize + byteCount}.");
        }

        return new W3dRawVisibilityChannel(pivot, defaultValue != 0, firstFrame, lastFrame, [.. payload[BitChannelHeaderSize..]]);
    }

    private static W3dTimeCodedMotionChannel ReadTimeCodedChannel(ReadOnlySpan<byte> payload, int frameCount, string context)
    {
        if (payload.Length < TimeCodedChannelHeaderSize + 8)
        {
            throw new InvalidDataException($"{context} has only {payload.Length} bytes.");
        }

        var keyCount = W3dBinary.ReadUInt32(payload, 0);
        var pivot = W3dBinary.ReadUInt16(payload, 4);
        var vectorLength = payload[6];
        var type = ToMotionType(payload[7], allowEuler: false, context);
        ValidateVectorLength(type, vectorLength, context);
        if (keyCount < 1 || keyCount > frameCount)
        {
            throw new InvalidDataException($"{context} declares {keyCount} keys for {frameCount} frames.");
        }

        var packetSize = (vectorLength + 1) * 4;
        var expected = TimeCodedChannelHeaderSize + (int)keyCount * packetSize;
        if (payload.Length != expected)
        {
            throw new InvalidDataException($"{context} has {payload.Length} bytes; expected {expected}.");
        }

        var timeCodes = new uint[keyCount];
        var values = new float[keyCount * vectorLength];
        for (var key = 0; key < keyCount; key++)
        {
            var offset = TimeCodedChannelHeaderSize + key * packetSize;
            timeCodes[key] = W3dBinary.ReadUInt32(payload, offset);
            if ((timeCodes[key] & ~W3dTimeCodedMotionChannel.BinaryMovementFlag) >= frameCount)
            {
                throw new InvalidDataException($"{context} key {key} lies beyond frame {frameCount - 1}.");
            }

            for (var component = 0; component < vectorLength; component++)
            {
                values[key * vectorLength + component] = W3dBinary.ReadSingle(payload, offset + 4 + component * 4);
            }
        }

        try
        {
            return new W3dTimeCodedMotionChannel(pivot, type, [.. timeCodes], [.. values]);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{context}: {exception.Message}", exception);
        }
    }

    private static W3dAdaptiveDeltaMotionChannel ReadAdaptiveDeltaChannel(ReadOnlySpan<byte> payload, string context, ref int surplus)
    {
        if (payload.Length < AdaptiveDeltaChannelHeaderSize + 4)
        {
            throw new InvalidDataException($"{context} has only {payload.Length} bytes.");
        }

        var frameCount = W3dBinary.ReadUInt32(payload, 0);
        var pivot = W3dBinary.ReadUInt16(payload, 4);
        var vectorLength = payload[6];
        var type = ToMotionType(payload[7], allowEuler: false, context);
        var scale = W3dBinary.ReadSingle(payload, 8);
        ValidateVectorLength(type, vectorLength, context);
        if (frameCount < 1 || frameCount > ushort.MaxValue + 1u || !float.IsFinite(scale))
        {
            throw new InvalidDataException($"{context} has {frameCount} frames and scale {scale}.");
        }

        var encoded = payload[AdaptiveDeltaChannelHeaderSize..];
        var required = W3dAdaptiveDeltaMotionChannel.GetEncodedSize((int)frameCount, vectorLength);
        if (encoded.Length < required)
        {
            throw new InvalidDataException($"{context} has {encoded.Length} data bytes; expected {required}.");
        }

        surplus += encoded.Length - required;
        return W3dAdaptiveDeltaMotionChannel.Decode(pivot, type, (int)frameCount, scale, encoded);
    }

    private static W3dTimeCodedVisibilityChannel ReadTimeCodedBitChannel(ReadOnlySpan<byte> payload, int frameCount, string context)
    {
        if (payload.Length < TimeCodedBitChannelHeaderSize + 4)
        {
            throw new InvalidDataException($"{context} has only {payload.Length} bytes.");
        }

        var keyCount = W3dBinary.ReadUInt32(payload, 0);
        var pivot = W3dBinary.ReadUInt16(payload, 4);
        var type = payload[6];
        var defaultValue = payload[7];
        if (type != 0 || defaultValue > 1)
        {
            throw new InvalidDataException($"{context} has bit channel type {type} and default {defaultValue}.");
        }

        if (keyCount < 1 || payload.Length != TimeCodedBitChannelHeaderSize + keyCount * 4)
        {
            throw new InvalidDataException($"{context} declares {keyCount} keys in {payload.Length} bytes.");
        }

        var keys = new uint[keyCount];
        for (var index = 0; index < keys.Length; index++)
        {
            keys[index] = W3dBinary.ReadUInt32(payload, TimeCodedBitChannelHeaderSize + index * 4);
            if ((keys[index] & ~W3dTimeCodedVisibilityChannel.StateMask) >= frameCount)
            {
                throw new InvalidDataException($"{context} key {index} lies beyond frame {frameCount - 1}.");
            }
        }

        try
        {
            return new W3dTimeCodedVisibilityChannel(pivot, defaultValue != 0, [.. keys]);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{context}: {exception.Message}", exception);
        }
    }

    private static W3dChannelType ToMotionType(int value, bool allowEuler, string context)
    {
        // The loaders (HRawAnimClass/HCompressedAnimClass::add_channel) bind only the ANIM_CHANNEL_X..Q ids; the
        // TIMECODED_*/ADAPTIVEDELTA_* enumerators are never used as channel flags.
        var type = (W3dChannelType)value;
        return type switch
        {
            W3dChannelType.X or W3dChannelType.Y or W3dChannelType.Z or W3dChannelType.Quaternion => type,
            W3dChannelType.XRotation or W3dChannelType.YRotation or W3dChannelType.ZRotation when allowEuler => type,
            _ => throw new InvalidDataException($"{context} has unsupported channel type {value}."),
        };
    }

    private static void ValidateVectorLength(W3dChannelType type, int vectorLength, string context)
    {
        var expected = type == W3dChannelType.Quaternion ? 4 : 1;
        if (vectorLength != expected)
        {
            throw new InvalidDataException($"{context} ({type}) has vector length {vectorLength}; expected {expected}.");
        }
    }
}
