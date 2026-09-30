using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary>
/// Immutable bone set of a hierarchy (HTreeClass): names, parents and base-pose transforms relative to the parent,
/// in System.Numerics row-vector convention (<c>world = local * parentWorld</c>). Shared by all instances.
/// </summary>
public sealed class W3dSkeleton
{
    public const string DefaultRootName = "ROOTTRANSFORM";

    private readonly FrozenDictionary<string, int> _boneIndices;

    private W3dSkeleton(string name, ImmutableArray<string> boneNames, ImmutableArray<int> parents, ImmutableArray<Matrix4x4> restTransforms)
    {
        Name = name;
        BoneNames = boneNames;
        Parents = parents;
        RestTransforms = restTransforms;
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < boneNames.Length; index++)
        {
            // HTreeClass::Get_Bone_Index returns the first case-insensitive match.
            indices.TryAdd(boneNames[index], index);
        }

        _boneIndices = indices.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var rest = new Matrix4x4[boneNames.Length];
        rest[0] = Matrix4x4.Identity;
        for (var index = 1; index < rest.Length; index++)
        {
            rest[index] = restTransforms[index] * rest[parents[index]];
        }

        RestWorldTransforms = [.. rest];
    }

    public string Name { get; }
    public ImmutableArray<string> BoneNames { get; }

    /// <summary>Parent bone index; -1 for the root (bone 0).</summary>
    public ImmutableArray<int> Parents { get; }

    /// <summary>Base pose relative to the parent: <c>R(pivot rotation) * T(pivot translation)</c>. Bone 0's is ignored.</summary>
    public ImmutableArray<Matrix4x4> RestTransforms { get; }

    /// <summary>Base pose in model space with an identity root ("pristine" bones of W3DModelDraw).</summary>
    public ImmutableArray<Matrix4x4> RestWorldTransforms { get; }

    public int BoneCount => BoneNames.Length;

    public static W3dSkeleton FromHierarchy(W3dHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var names = ImmutableArray.CreateBuilder<string>(hierarchy.Pivots.Length);
        var parents = ImmutableArray.CreateBuilder<int>(hierarchy.Pivots.Length);
        var rest = ImmutableArray.CreateBuilder<Matrix4x4>(hierarchy.Pivots.Length);
        foreach (var pivot in hierarchy.Pivots)
        {
            names.Add(pivot.Name);
            parents.Add(pivot.ParentIndex);
            // HTreeClass::read_pivots: BaseTransform = Translate(t) * Build_Matrix3D(q) in column-vector form.
            rest.Add(Matrix4x4.CreateFromQuaternion(pivot.Rotation) * Matrix4x4.CreateTranslation(pivot.Translation));
        }

        return new W3dSkeleton(hierarchy.Name, names.MoveToImmutable(), parents.MoveToImmutable(), rest.MoveToImmutable());
    }

    /// <summary>The single-pivot tree the original builds for render objects without a hierarchy (HTreeClass::Init_Default).</summary>
    public static W3dSkeleton CreateDefault(string name)
        => new(name, [DefaultRootName], [-1], [Matrix4x4.Identity]);

    /// <summary>Case-insensitive exact lookup; -1 if the bone does not exist.</summary>
    public int FindBone(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _boneIndices.TryGetValue(name, out var index) ? index : -1;
    }
}
