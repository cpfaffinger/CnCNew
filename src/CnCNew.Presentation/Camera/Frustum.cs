using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// The six planes of a view frustum from a row-vector world-to-clip matrix with depth in [0, 1] (<see cref="Matrix4x4.CreatePerspectiveFieldOfView"/>).
/// Plane normals point inwards.
/// </summary>
public readonly struct Frustum
{
    private readonly Plane _left, _right, _bottom, _top, _near, _far;

    private Frustum(Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        _left = left;
        _right = right;
        _bottom = bottom;
        _top = top;
        _near = near;
        _far = far;
    }

    public static Frustum FromViewProjection(Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        static Plane P(Vector4 v) => Plane.Normalize(new Plane(v.X, v.Y, v.Z, v.W));
        return new Frustum(P(c4 + c1), P(c4 - c1), P(c4 + c2), P(c4 - c2), P(c3), P(c4 - c3));
    }

    /// <summary>Conservative box test: false only when the box lies completely outside one plane.</summary>
    public bool Intersects(Aabb box)
    {
        return Inside(_left, box) && Inside(_right, box) && Inside(_bottom, box) && Inside(_top, box) && Inside(_near, box) && Inside(_far, box);

        static bool Inside(Plane plane, Aabb box)
        {
            var positive = new Vector3(
                plane.Normal.X >= 0 ? box.Max.X : box.Min.X,
                plane.Normal.Y >= 0 ? box.Max.Y : box.Min.Y,
                plane.Normal.Z >= 0 ? box.Max.Z : box.Min.Z);
            return Vector3.Dot(plane.Normal, positive) + plane.D >= 0;
        }
    }

    public bool Contains(Vector3 point)
    {
        foreach (var plane in new[] { _left, _right, _bottom, _top, _near, _far })
            if (Vector3.Dot(plane.Normal, point) + plane.D < 0) return false;
        return true;
    }
}
