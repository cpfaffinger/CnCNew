using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary>One pivot (bone) of a W3D hierarchy tree, relative to its parent.</summary>
public sealed record W3dPivot(
    string Name,
    int ParentIndex,
    Vector3 Translation,
    Vector3 EulerAngles,
    Quaternion Rotation);

public sealed record W3dHierarchy(
    uint Version,
    string Name,
    Vector3 Center,
    ImmutableArray<W3dPivot> Pivots,
    ImmutableArray<Matrix4x4> PivotFixups);

public static class W3dHierarchyDecoder
{
    private const int HeaderSize = 36;
    private const int PivotSize = 60;
    private const int FixupSize = 48;

    public static W3dHierarchy Decode(ReadOnlySpan<byte> data, W3dChunk hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        W3dBinary.RequireContainer(hierarchy, "Hierarchy");
        if (hierarchy.Children.Count == 0 || hierarchy.Children[0].Type != W3dChunkType.HierarchyHeader)
        {
            throw new InvalidDataException($"Hierarchy at {hierarchy.HeaderOffset} does not start with its header chunk.");
        }

        var header = W3dBinary.Slice(data, W3dBinary.RequireSize(
            W3dBinary.RequireLeaf(hierarchy.Children[0], "Hierarchy header"), HeaderSize, "Hierarchy header"));
        var version = W3dBinary.ReadUInt32(header, 0);
        var name = W3dBinary.ReadFixedString(header.Slice(4, W3dBinary.NameLength));
        W3dBinary.ValidateVersion(version, W3dBinary.MakeVersion(3, 0), $"Hierarchy '{name}'");
        var pivotCount = W3dBinary.ReadUInt32(header, 20);
        var center = W3dBinary.ReadVector3(header, 24);
        if (pivotCount < 1 || pivotCount > ushort.MaxValue)
        {
            throw new InvalidDataException($"Hierarchy '{name}' declares {pivotCount} pivots.");
        }

        ImmutableArray<W3dPivot>? pivots = null;
        ImmutableArray<Matrix4x4>? fixups = null;
        foreach (var child in hierarchy.Children.Skip(1))
        {
            switch (child.Type)
            {
                case W3dChunkType.Pivots when pivots is null:
                    pivots = ReadPivots(data, W3dBinary.RequireLeaf(child, "Pivots"), (int)pivotCount, name);
                    break;
                case W3dChunkType.PivotFixups when fixups is null:
                    fixups = ReadFixups(data, W3dBinary.RequireLeaf(child, "Pivot fixups"), (int)pivotCount, name);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Hierarchy '{name}' contains unexpected or duplicate chunk 0x{child.Type:X8} at {child.HeaderOffset}.");
            }
        }

        if (pivots is null)
        {
            throw new InvalidDataException($"Hierarchy '{name}' has no pivot chunk.");
        }

        return new W3dHierarchy(version, name, center, pivots.Value, fixups ?? []);
    }

    private static ImmutableArray<W3dPivot> ReadPivots(ReadOnlySpan<byte> data, W3dChunk chunk, int count, string name)
    {
        W3dBinary.RequireSize(chunk, checked(count * PivotSize), $"Hierarchy '{name}' pivots");
        var payload = W3dBinary.Slice(data, chunk);
        var result = ImmutableArray.CreateBuilder<W3dPivot>(count);
        for (var index = 0; index < count; index++)
        {
            var pivot = payload.Slice(index * PivotSize, PivotSize);
            var pivotName = W3dBinary.ReadFixedString(pivot[..W3dBinary.NameLength]);
            var parent = W3dBinary.ReadUInt32(pivot, 16);
            int parentIndex;
            if (parent == uint.MaxValue)
            {
                if (index != 0)
                {
                    throw new InvalidDataException($"Hierarchy '{name}' pivot {index} '{pivotName}' has no parent.");
                }

                parentIndex = -1;
            }
            else
            {
                if (index == 0 || parent >= index)
                {
                    throw new InvalidDataException(
                        $"Hierarchy '{name}' pivot {index} '{pivotName}' has parent {parent}; parents must precede children and pivot 0 is the root.");
                }

                parentIndex = (int)parent;
            }

            result.Add(new W3dPivot(
                pivotName,
                parentIndex,
                W3dBinary.ReadVector3(pivot, 20),
                W3dBinary.ReadVector3(pivot, 32),
                W3dBinary.ReadQuaternion(pivot, 44)));
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<Matrix4x4> ReadFixups(ReadOnlySpan<byte> data, W3dChunk chunk, int count, string name)
    {
        W3dBinary.RequireSize(chunk, checked(count * FixupSize), $"Hierarchy '{name}' pivot fixups");
        var payload = W3dBinary.Slice(data, chunk);
        var result = ImmutableArray.CreateBuilder<Matrix4x4>(count);
        for (var index = 0; index < count; index++)
        {
            // The file stores a MAX 4x3 matrix: four rows of three floats, the last row being the translation,
            // which is exactly System.Numerics' row-vector layout.
            var m = payload.Slice(index * FixupSize, FixupSize);
            result.Add(new Matrix4x4(
                W3dBinary.ReadSingle(m, 0), W3dBinary.ReadSingle(m, 4), W3dBinary.ReadSingle(m, 8), 0,
                W3dBinary.ReadSingle(m, 12), W3dBinary.ReadSingle(m, 16), W3dBinary.ReadSingle(m, 20), 0,
                W3dBinary.ReadSingle(m, 24), W3dBinary.ReadSingle(m, 28), W3dBinary.ReadSingle(m, 32), 0,
                W3dBinary.ReadSingle(m, 36), W3dBinary.ReadSingle(m, 40), W3dBinary.ReadSingle(m, 44), 1));
        }

        return result.MoveToImmutable();
    }
}
