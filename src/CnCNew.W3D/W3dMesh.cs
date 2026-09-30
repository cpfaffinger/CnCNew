using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dMesh(
    string Name,
    string ContainerName,
    uint Version,
    uint Attributes,
    int MaterialCount,
    int SortLevel,
    uint VertexChannels,
    uint FaceChannels,
    Vector3 BoundsMinimum,
    Vector3 BoundsMaximum,
    Vector3 SphereCenter,
    float SphereRadius,
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<Vector3> Normals,
    IReadOnlyList<W3dTriangle> Triangles,
    W3dMaterialData Materials,
    IReadOnlyList<W3dMaterialVariant> MaterialVariants)
{
    /// <summary>
    /// W3D_CHUNK_VERTEX_INFLUENCES: one bone per vertex (W3dVertInfStruct.BoneIdx); empty for rigid meshes. Skin vertices
    /// and normals are stored in the space of their bone (MeshGeometryClass::get_deformed_vertices).
    /// </summary>
    public ImmutableArray<ushort> VertexBoneIndices { get; init; } = [];

    public bool IsTwoSided => (Attributes & 0x00002000u) != 0;

    /// <summary>W3D_MESH_FLAG_HIDDEN: the mesh starts hidden (MeshClass::Init calls Set_Hidden).</summary>
    public bool IsHidden => (Attributes & 0x00001000u) != 0;

    public bool CastsShadow => (Attributes & 0x00008000u) != 0;

    /// <summary>W3D_MESH_FLAG_COLLISION_TYPE_* bits shifted down (physical, projectile, vis, camera, vehicle).</summary>
    public uint CollisionTypes => (Attributes & 0x00000ff0u) >> 4;

    public W3dMeshGeometryType GeometryType => (W3dMeshGeometryType)((Attributes >> 16) & 0xffu);

    public bool IsSkin => GeometryType == W3dMeshGeometryType.Skin;

    /// <summary>Camera-aligned (billboard around the pivot) or camera-oriented geometry.</summary>
    public bool IsCameraFacing =>
        GeometryType is W3dMeshGeometryType.CameraAligned or W3dMeshGeometryType.CameraOriented;
}

/// <summary>W3D_MESH_FLAG_GEOMETRY_TYPE_* (w3d_file.h).</summary>
public enum W3dMeshGeometryType : byte
{
    Normal = 0,
    CameraAligned = 1,
    Skin = 2,
    ObsoleteShadow = 3,
    AxisAlignedBox = 4,
    OrientedBox = 5,
    CameraOriented = 6,
}
