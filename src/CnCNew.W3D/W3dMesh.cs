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
    public bool IsTwoSided => (Attributes & 0x00002000u) != 0;
}
