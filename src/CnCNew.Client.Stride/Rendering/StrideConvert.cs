using Stride.Core.Mathematics;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// Stride world space is the simulation's world space (X east, Y north, Z up, right-handed): layers feed simulation coordinates straight
/// to Stride and the camera uses the matrices of <c>RtsCamera</c> (row vectors, right-handed, clip depth 0..1) unchanged. Nothing in the
/// client relies on Stride's Y-up convention; light directions are set explicitly.
/// </summary>
internal static class StrideConvert
{
    public static Vector3 ToStride(this NumericsVector3 value) => new(value.X, value.Y, value.Z);

    public static Vector2 ToStride(this NumericsVector2 value) => new(value.X, value.Y);

    public static Matrix ToStride(this NumericsMatrix m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
