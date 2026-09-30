using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dHlodSubObject(int BoneIndex, string Name);

/// <summary>One level of detail. The original orders the arrays by cost; the last array is the most detailed.</summary>
public sealed record W3dHlodLod(float MaxScreenSize, ImmutableArray<W3dHlodSubObject> SubObjects);

public sealed record W3dHlod(
    uint Version,
    string Name,
    string HierarchyName,
    ImmutableArray<W3dHlodLod> Lods,
    ImmutableArray<W3dHlodSubObject> Aggregates,
    ImmutableArray<W3dHlodSubObject> Proxies);

/// <summary>Collision box render object (W3D_CHUNK_BOX). The original never renders it (HLodClass::Render skips OBBOX).</summary>
public sealed record W3dBox(uint Version, uint Attributes, string Name, W3dRgb Color, Vector3 Center, Vector3 Extent)
{
    public bool IsOriented => (Attributes & 0x1u) != 0;
    public bool IsAxisAligned => (Attributes & 0x2u) != 0;
    public uint CollisionTypes => (Attributes & 0xff0u) >> 4;
}

/// <summary>Placeholder render object (W3D_CHUNK_NULL_OBJECT); never rendered.</summary>
public sealed record W3dNullObject(uint Version, uint Attributes, string Name);

public static class W3dHlodDecoder
{
    private const int HeaderSize = 40;
    private const int ArrayHeaderSize = 8;
    private const int SubObjectSize = 36;
    private const int BoxSize = 68;
    private const int NullObjectSize = 48;

    public static W3dHlod Decode(ReadOnlySpan<byte> data, W3dChunk hlod)
    {
        ArgumentNullException.ThrowIfNull(hlod);
        W3dBinary.RequireContainer(hlod, "HLOD");
        if (hlod.Children.Count == 0 || hlod.Children[0].Type != W3dChunkType.HlodHeader)
        {
            throw new InvalidDataException($"HLOD at {hlod.HeaderOffset} does not start with its header chunk.");
        }

        var header = W3dBinary.Slice(data, W3dBinary.RequireSize(
            W3dBinary.RequireLeaf(hlod.Children[0], "HLOD header"), HeaderSize, "HLOD header"));
        var version = W3dBinary.ReadUInt32(header, 0);
        var lodCount = W3dBinary.ReadUInt32(header, 4);
        var name = W3dBinary.ReadFixedString(header.Slice(8, W3dBinary.NameLength));
        var hierarchyName = W3dBinary.ReadFixedString(header.Slice(24, W3dBinary.NameLength));
        if (lodCount < 1 || lodCount > 255)
        {
            throw new InvalidDataException($"HLOD '{name}' declares {lodCount} LODs.");
        }

        var lods = ImmutableArray.CreateBuilder<W3dHlodLod>((int)lodCount);
        ImmutableArray<W3dHlodSubObject>? aggregates = null;
        ImmutableArray<W3dHlodSubObject>? proxies = null;
        foreach (var child in hlod.Children.Skip(1))
        {
            switch (child.Type)
            {
                case W3dChunkType.HlodLodArray when lods.Count < lodCount && aggregates is null && proxies is null:
                    var (maxScreenSize, subObjects) = ReadArray(data, child, name, "LOD array");
                    lods.Add(new W3dHlodLod(maxScreenSize, subObjects));
                    break;
                case W3dChunkType.HlodAggregateArray when aggregates is null:
                    aggregates = ReadArray(data, child, name, "aggregate array").SubObjects;
                    break;
                case W3dChunkType.HlodProxyArray when proxies is null:
                    proxies = ReadArray(data, child, name, "proxy array").SubObjects;
                    break;
                default:
                    throw new InvalidDataException(
                        $"HLOD '{name}' contains unexpected, misplaced or duplicate chunk 0x{child.Type:X8} at {child.HeaderOffset}.");
            }
        }

        if (lods.Count != lodCount)
        {
            throw new InvalidDataException($"HLOD '{name}' declares {lodCount} LOD arrays but contains {lods.Count}.");
        }

        return new W3dHlod(version, name, hierarchyName, lods.MoveToImmutable(), aggregates ?? [], proxies ?? []);
    }

    public static W3dBox DecodeBox(ReadOnlySpan<byte> data, W3dChunk chunk)
    {
        var payload = W3dBinary.Slice(data, W3dBinary.RequireSize(W3dBinary.RequireLeaf(chunk, "Box"), BoxSize, "Box"));
        var attributes = W3dBinary.ReadUInt32(payload, 4);
        var name = W3dBinary.ReadFixedString(payload.Slice(8, 2 * W3dBinary.NameLength));
        if ((attributes & ~0xff3u) != 0 || (attributes & 0x3u) == 0x3u)
        {
            throw new InvalidDataException($"Box '{name}' has unknown attribute bits 0x{attributes:X8}.");
        }

        return new W3dBox(
            W3dBinary.ReadUInt32(payload, 0),
            attributes,
            name,
            new W3dRgb(payload[40], payload[41], payload[42]),
            W3dBinary.ReadVector3(payload, 44),
            W3dBinary.ReadVector3(payload, 56));
    }

    public static W3dNullObject DecodeNullObject(ReadOnlySpan<byte> data, W3dChunk chunk)
    {
        var payload = W3dBinary.Slice(data, W3dBinary.RequireSize(
            W3dBinary.RequireLeaf(chunk, "Null object"), NullObjectSize, "Null object"));
        return new W3dNullObject(
            W3dBinary.ReadUInt32(payload, 0),
            W3dBinary.ReadUInt32(payload, 4),
            W3dBinary.ReadFixedString(payload.Slice(16, 2 * W3dBinary.NameLength)));
    }

    private static (float MaxScreenSize, ImmutableArray<W3dHlodSubObject> SubObjects) ReadArray(
        ReadOnlySpan<byte> data,
        W3dChunk array,
        string hlodName,
        string description)
    {
        W3dBinary.RequireContainer(array, $"HLOD '{hlodName}' {description}");
        if (array.Children.Count == 0 || array.Children[0].Type != W3dChunkType.HlodSubObjectArrayHeader)
        {
            throw new InvalidDataException($"HLOD '{hlodName}' {description} at {array.HeaderOffset} has no array header.");
        }

        var header = W3dBinary.Slice(data, W3dBinary.RequireSize(
            W3dBinary.RequireLeaf(array.Children[0], "HLOD array header"), ArrayHeaderSize, "HLOD array header"));
        var count = W3dBinary.ReadUInt32(header, 0);
        var maxScreenSize = W3dBinary.ReadSingle(header, 4);
        if (count != array.Children.Count - 1)
        {
            throw new InvalidDataException(
                $"HLOD '{hlodName}' {description} declares {count} sub-objects but contains {array.Children.Count - 1} chunks.");
        }

        var result = ImmutableArray.CreateBuilder<W3dHlodSubObject>((int)count);
        foreach (var child in array.Children.Skip(1))
        {
            if (child.Type != W3dChunkType.HlodSubObject)
            {
                throw new InvalidDataException(
                    $"HLOD '{hlodName}' {description} contains unexpected chunk 0x{child.Type:X8} at {child.HeaderOffset}.");
            }

            var payload = W3dBinary.Slice(data, W3dBinary.RequireSize(
                W3dBinary.RequireLeaf(child, "HLOD sub-object"), SubObjectSize, "HLOD sub-object"));
            var boneIndex = W3dBinary.ReadUInt32(payload, 0);
            var name = W3dBinary.ReadFixedString(payload.Slice(4, 2 * W3dBinary.NameLength));
            if (boneIndex > ushort.MaxValue)
            {
                throw new InvalidDataException($"HLOD '{hlodName}' sub-object '{name}' has bone index {boneIndex}.");
            }

            result.Add(new W3dHlodSubObject((int)boneIndex, name));
        }

        return (maxScreenSize, result.MoveToImmutable());
    }
}
