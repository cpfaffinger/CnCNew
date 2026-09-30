using System.Numerics;

namespace CnCNew.W3D;

public static class W3dQuaternionMath
{
    private const float Epsilon = 0.0001f;

    /// <summary>
    /// Shortest-arc spherical interpolation without renormalisation, re-implemented from WWMath quat.cpp
    /// <c>Fast_Slerp</c> (C fallback); the original's table-based acos/sin approximations are replaced by exact math.
    /// </summary>
    public static Quaternion Slerp(Quaternion p, Quaternion q, float alpha)
    {
        var cosTheta = p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;
        var flip = cosTheta < 0.0f;
        if (flip)
        {
            cosTheta = -cosTheta;
        }

        float beta;
        if (1.0f - cosTheta < Epsilon * Epsilon)
        {
            beta = 1.0f - alpha;
        }
        else
        {
            var theta = MathF.Acos(MathF.Min(cosTheta, 1.0f));
            var inverseSinTheta = 1.0f / MathF.Sin(theta);
            beta = MathF.Sin(theta - alpha * theta) * inverseSinTheta;
            alpha = MathF.Sin(alpha * theta) * inverseSinTheta;
        }

        if (flip)
        {
            alpha = -alpha;
        }

        return new Quaternion(
            beta * p.X + alpha * q.X,
            beta * p.Y + alpha * q.Y,
            beta * p.Z + alpha * q.Z,
            beta * p.W + alpha * q.W);
    }

    public static float Lerp(float a, float b, float alpha) => a + (b - a) * alpha;
}
