using System.Numerics;

namespace CnCNew.W3D;

public sealed record W3dTriangle(
    uint Vertex0,
    uint Vertex1,
    uint Vertex2,
    uint Attributes,
    Vector3 Normal,
    float Distance);
