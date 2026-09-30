using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

public enum W3dSubObjectKind
{
    Mesh,
    Box,
    NullObject,
}

public enum W3dHierarchySource
{
    /// <summary>Bare mesh file: a single-pivot default tree (HTreeClass::Init_Default).</summary>
    Default,

    /// <summary>The hierarchy chunk in the model file itself.</summary>
    OwnFile,

    /// <summary>Resolved through <c>&lt;HierarchyName&gt;.W3D</c>.</summary>
    External,
}

/// <summary>
/// One render object of an HLOD LOD array attached to a bone. Rigid meshes are drawn with the bone's world matrix; skins
/// are deformed per vertex (<see cref="W3dModel.SkinVertices"/>) and ignore the bone.
/// </summary>
public sealed record W3dSubObject(int LodIndex, string Name, int BoneIndex, W3dSubObjectKind Kind, W3dMesh? Mesh, W3dBox? Box)
{
    public bool IsSkinned => Mesh?.IsSkin == true;

    /// <summary>Boxes, null objects and the obsolete collision/shadow mesh kinds are never drawn.</summary>
    public bool IsRenderable => Kind == W3dSubObjectKind.Mesh && !IsCollisionOnly && !IsShadowOnly;

    /// <summary>Boxes, obsolete AABOX/OBBOX geometry and the obsolete W3D_MESH_FLAG_COLLISION_BOX.</summary>
    public bool IsCollisionOnly => Kind == W3dSubObjectKind.Box ||
                                   Mesh is { GeometryType: W3dMeshGeometryType.AxisAlignedBox or W3dMeshGeometryType.OrientedBox } ||
                                   (Mesh is not null && (Mesh.Attributes & 0x1u) != 0);

    /// <summary>Obsolete shadow geometry type or W3D_MESH_FLAG_SHADOW.</summary>
    public bool IsShadowOnly => Mesh is { GeometryType: W3dMeshGeometryType.ObsoleteShadow } ||
                                (Mesh is not null && (Mesh.Attributes & 0x4u) != 0);

    /// <summary>Initial visibility before animation visibility channels and INI hide/show rules.</summary>
    public bool IsVisibleByDefault => IsRenderable && Mesh?.IsHidden == false;

    public int SortLevel => Mesh?.SortLevel ?? 0;
    public bool IsTwoSided => Mesh?.IsTwoSided == true;
    public bool IsCameraFacing => Mesh?.IsCameraFacing == true;
    public bool CastsShadow => Mesh?.CastsShadow == true;
}

public sealed record W3dBoneMatch(string Name, int BoneIndex, bool FromSubObject);

/// <summary>
/// A renderer-independent model: skeleton, sub-objects per LOD and bone lookup. Immutable and shareable across all
/// instances; per-instance state lives in <see cref="W3dPose"/>.
/// </summary>
public sealed class W3dModel
{
    private const int MaximumNumberedBone = 99;

    private W3dModel(
        string name,
        W3dSkeleton skeleton,
        W3dHierarchySource hierarchySource,
        int lodCount,
        ImmutableArray<W3dSubObject> subObjects,
        ImmutableArray<W3dHlodSubObject> unresolvedSubObjects,
        ImmutableArray<string> unreferencedMeshes,
        ImmutableArray<W3dHlodSubObject> aggregates,
        ImmutableArray<W3dHlodSubObject> proxies,
        ImmutableArray<W3dIgnoredChunk> ignoredChunks)
    {
        Name = name;
        Skeleton = skeleton;
        HierarchySource = hierarchySource;
        LodCount = lodCount;
        SubObjects = subObjects;
        UnresolvedSubObjects = unresolvedSubObjects;
        UnreferencedMeshes = unreferencedMeshes;
        Aggregates = aggregates;
        Proxies = proxies;
        IgnoredChunks = ignoredChunks;
    }

    public string Name { get; }
    public W3dSkeleton Skeleton { get; }
    public W3dHierarchySource HierarchySource { get; }
    public int LodCount { get; }

    /// <summary>All sub-objects of all LODs; the most detailed LOD is <c>LodCount - 1</c>.</summary>
    public ImmutableArray<W3dSubObject> SubObjects { get; }

    /// <summary>
    /// HLOD entries naming render objects that exist nowhere in the file (export leftovers such as splines). The
    /// original's asset manager returns null for them and HLodClass skips them; here they are listed.
    /// </summary>
    public ImmutableArray<W3dHlodSubObject> UnresolvedSubObjects { get; }

    /// <summary>Meshes in the file that no LOD array references (never instantiated by the original HLOD).</summary>
    public ImmutableArray<string> UnreferencedMeshes { get; }

    /// <summary>HLOD aggregate entries (external models attached to bones); not resolved by this layer.</summary>
    public ImmutableArray<W3dHlodSubObject> Aggregates { get; }

    /// <summary>HLOD proxy entries (application objects at a bone); exposed, not instantiated.</summary>
    public ImmutableArray<W3dHlodSubObject> Proxies { get; }

    public ImmutableArray<W3dIgnoredChunk> IgnoredChunks { get; }

    public IEnumerable<W3dSubObject> GetLod(int lodIndex) => SubObjects.Where(subObject => subObject.LodIndex == lodIndex);

    public W3dPose CreatePose() => new(Skeleton);

    /// <summary>
    /// Builds a model from a decoded file. <paramref name="resolveSkeleton"/> maps a hierarchy name to a skeleton when the
    /// file does not contain it (null if missing).
    /// </summary>
    public static W3dModel Create(string name, W3dAssetFile file, Func<string, W3dSkeleton?> resolveSkeleton)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(resolveSkeleton);
        foreach (var mesh in file.Meshes)
        {
            if (!Enum.IsDefined(mesh.GeometryType))
            {
                throw new InvalidDataException($"Model '{name}' mesh '{mesh.Name}' has unknown geometry type {(int)mesh.GeometryType}.");
            }
        }

        if (file.Hlods.Length == 0)
        {
            return CreateBareMesh(name, file);
        }

        var hlod = SelectHlod(name, file);
        W3dSkeleton skeleton;
        W3dHierarchySource source;
        var ownHierarchy = file.Hierarchies.FirstOrDefault(hierarchy =>
            hierarchy.Name.Equals(hlod.HierarchyName, StringComparison.OrdinalIgnoreCase));
        if (ownHierarchy is not null)
        {
            skeleton = W3dSkeleton.FromHierarchy(ownHierarchy);
            source = W3dHierarchySource.OwnFile;
        }
        else
        {
            skeleton = resolveSkeleton(hlod.HierarchyName)
                ?? throw new W3dMissingAssetException(
                    $"Model '{name}' references hierarchy '{hlod.HierarchyName}', which could not be resolved.", hlod.HierarchyName);
            source = W3dHierarchySource.External;
        }

        var meshes = new Dictionary<string, W3dMesh>(StringComparer.OrdinalIgnoreCase);
        foreach (var mesh in file.Meshes)
        {
            if (!meshes.TryAdd(GetFullName(mesh), mesh))
            {
                throw new InvalidDataException($"Model '{name}' contains mesh '{GetFullName(mesh)}' twice.");
            }
        }

        var boxes = new Dictionary<string, W3dBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in file.Boxes)
        {
            if (meshes.ContainsKey(box.Name) || !boxes.TryAdd(box.Name, box))
            {
                throw new InvalidDataException($"Model '{name}' contains render object '{box.Name}' twice.");
            }
        }

        var nullObjects = file.NullObjects.ToDictionary(nullObject => nullObject.Name, StringComparer.OrdinalIgnoreCase);
        var subObjects = ImmutableArray.CreateBuilder<W3dSubObject>();
        var unresolved = ImmutableArray.CreateBuilder<W3dHlodSubObject>();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var lodIndex = 0; lodIndex < hlod.Lods.Length; lodIndex++)
        {
            foreach (var entry in hlod.Lods[lodIndex].SubObjects)
            {
                if (entry.BoneIndex >= skeleton.BoneCount)
                {
                    throw new InvalidDataException(
                        $"Model '{name}' attaches '{entry.Name}' to bone {entry.BoneIndex}, but '{skeleton.Name}' has {skeleton.BoneCount} bones.");
                }

                W3dSubObject subObject;
                if (meshes.TryGetValue(entry.Name, out var mesh))
                {
                    ValidateSkin(name, mesh, skeleton);
                    subObject = new W3dSubObject(lodIndex, entry.Name, entry.BoneIndex, W3dSubObjectKind.Mesh, mesh, null);
                    referenced.Add(entry.Name);
                }
                else if (boxes.TryGetValue(entry.Name, out var box))
                {
                    subObject = new W3dSubObject(lodIndex, entry.Name, entry.BoneIndex, W3dSubObjectKind.Box, null, box);
                }
                else if (nullObjects.ContainsKey(entry.Name))
                {
                    subObject = new W3dSubObject(lodIndex, entry.Name, entry.BoneIndex, W3dSubObjectKind.NullObject, null, null);
                }
                else
                {
                    unresolved.Add(entry);
                    continue;
                }

                subObjects.Add(subObject);
            }
        }

        return new W3dModel(
            name,
            skeleton,
            source,
            hlod.Lods.Length,
            subObjects.ToImmutable(),
            unresolved.ToImmutable(),
            [.. meshes.Keys.Where(mesh => !referenced.Contains(mesh)).Order(StringComparer.OrdinalIgnoreCase)],
            hlod.Aggregates,
            hlod.Proxies,
            file.IgnoredChunks);
    }

    /// <summary>Case-insensitive exact bone lookup (HTreeClass::Get_Bone_Index); -1 if missing.</summary>
    public int FindBone(string name) => Skeleton.FindBone(name);

    /// <summary>
    /// Resolves an INI bone name the way W3DModelDraw's doSingleBoneName does: the unadorned name and then
    /// <c>NAME01..NAME99</c> up to the first gap as bones (bone 0 counts as "not found"); only if none exists as a bone,
    /// the same names are looked up as sub-objects of the most detailed LOD (full or suffix name) and their bones used.
    /// </summary>
    public ImmutableArray<W3dBoneMatch> FindBones(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var matches = ImmutableArray.CreateBuilder<W3dBoneMatch>();
        Collect(candidate => Skeleton.FindBone(candidate) is > 0 and var bone ? bone : null, fromSubObject: false);
        if (matches.Count == 0)
        {
            Collect(candidate => FindSubObject(candidate, LodCount - 1)?.BoneIndex, fromSubObject: true);
        }

        return matches.ToImmutable();

        void Collect(Func<string, int?> find, bool fromSubObject)
        {
            if (find(name) is { } unadorned)
            {
                matches.Add(new W3dBoneMatch(name, unadorned, fromSubObject));
            }

            for (var index = 1; index <= MaximumNumberedBone; index++)
            {
                var numbered = $"{name}{index:00}";
                if (find(numbered) is not { } bone)
                {
                    break;
                }

                matches.Add(new W3dBoneMatch(numbered, bone, fromSubObject));
            }
        }
    }

    /// <summary>RenderObjClass::Get_Sub_Object_By_Name: full name first, then the part after the container's dot.</summary>
    public W3dSubObject? FindSubObject(string name, int lodIndex)
    {
        ArgumentNullException.ThrowIfNull(name);
        var lod = SubObjects.Where(subObject => subObject.LodIndex == lodIndex).ToArray();
        return lod.FirstOrDefault(subObject => subObject.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? lod.FirstOrDefault(subObject => GetSuffix(subObject.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// CPU skinning (MeshGeometryClass::get_deformed_vertices): each vertex and normal is transformed by the world matrix of
    /// its single influencing bone; normals use the rotation part only and are not renormalised, as in the original.
    /// </summary>
    public static void SkinVertices(W3dMesh mesh, ReadOnlySpan<Matrix4x4> boneWorldTransforms, Span<Vector3> positions, Span<Vector3> normals)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var bones = mesh.VertexBoneIndices;
        if (bones.Length != mesh.Vertices.Count)
        {
            throw new ArgumentException($"Mesh '{mesh.Name}' has no vertex influences for all vertices.", nameof(mesh));
        }

        if (positions.Length < bones.Length || (!normals.IsEmpty && normals.Length < bones.Length))
        {
            throw new ArgumentException("Output spans are smaller than the vertex count.");
        }

        var skinNormals = !normals.IsEmpty && mesh.Normals.Count == bones.Length;
        for (var vertex = 0; vertex < bones.Length; vertex++)
        {
            var transform = boneWorldTransforms[bones[vertex]];
            positions[vertex] = Vector3.Transform(mesh.Vertices[vertex], transform);
            if (skinNormals)
            {
                normals[vertex] = Vector3.TransformNormal(mesh.Normals[vertex], transform);
            }
        }
    }

    public static string GetFullName(W3dMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return string.IsNullOrEmpty(mesh.ContainerName) ? mesh.Name : $"{mesh.ContainerName}.{mesh.Name}";
    }

    private static string GetSuffix(string name)
    {
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static W3dHlod SelectHlod(string name, W3dAssetFile file)
    {
        if (file.Hlods.Length == 1)
        {
            return file.Hlods[0];
        }

        var named = file.Hlods.Where(hlod => hlod.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return named.Length == 1
            ? named[0]
            : throw new InvalidDataException($"Model '{name}' file contains {file.Hlods.Length} HLODs and none uniquely matches its name.");
    }

    private static W3dModel CreateBareMesh(string name, W3dAssetFile file)
    {
        if (file.Meshes.Length != 1 || file.Boxes.Length != 0 || file.Hierarchies.Length != 0)
        {
            throw new InvalidDataException(
                $"File '{name}' has no HLOD and {file.Meshes.Length} meshes, {file.Boxes.Length} boxes, " +
                $"{file.Hierarchies.Length} hierarchies; only single bare meshes are models without an HLOD.");
        }

        var mesh = file.Meshes[0];
        var skeleton = W3dSkeleton.CreateDefault(GetFullName(mesh));
        ValidateSkin(name, mesh, skeleton);
        return new W3dModel(
            name,
            skeleton,
            W3dHierarchySource.Default,
            1,
            [new W3dSubObject(0, GetFullName(mesh), 0, W3dSubObjectKind.Mesh, mesh, null)],
            [],
            [],
            [],
            [],
            file.IgnoredChunks);
    }

    private static void ValidateSkin(string modelName, W3dMesh mesh, W3dSkeleton skeleton)
    {
        if (mesh.IsSkin != (mesh.VertexBoneIndices.Length > 0))
        {
            throw new InvalidDataException(
                $"Model '{modelName}' mesh '{mesh.Name}' is {(mesh.IsSkin ? "a skin without" : "rigid but has")} vertex influences.");
        }

        foreach (var bone in mesh.VertexBoneIndices)
        {
            if (bone >= skeleton.BoneCount)
            {
                throw new InvalidDataException(
                    $"Model '{modelName}' skin '{mesh.Name}' references bone {bone}; '{skeleton.Name}' has {skeleton.BoneCount} bones.");
            }
        }
    }
}

public sealed class W3dMissingAssetException(string message, string assetName) : IOException(message)
{
    public string AssetName { get; } = assetName;
}
