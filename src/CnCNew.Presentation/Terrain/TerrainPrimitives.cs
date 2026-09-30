using System.Numerics;

namespace CnCNew.Presentation.Terrain;

/// <summary>An 8-bit RGBA colour in byte order R, G, B, A (the memory layout of <c>R8G8B8A8_UNorm</c>).</summary>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public static Rgba32 FromUnit(Vector3 rgb, byte alpha) => new(ToByte(rgb.X), ToByte(rgb.Y), ToByte(rgb.Z), alpha);

    public Vector3 ToUnitRgb() => new(R / 255f, G / 255f, B / 255f);

    /// <summary>Unit float to byte the way <c>REAL_TO_INT</c> does for colours: clamp to [0, 1], scale, round half up.</summary>
    public static byte ToByte(float unit) => (byte)Math.Clamp((int)MathF.Floor(Math.Clamp(unit, 0f, 1f) * 255f + 0.5f), 0, 255);
}

/// <summary>Axis-aligned box in world space.</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;

    public static Aabb FromPoints(ReadOnlySpan<Vector3> points)
    {
        if (points.IsEmpty) throw new ArgumentException("A bounding box needs at least one point.", nameof(points));
        var min = points[0];
        var max = points[0];
        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new Aabb(min, max);
    }
}
