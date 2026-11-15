using System.Numerics;

namespace CnCNew.Presentation.Terrain;

/// <summary>A corner of a piece of a draped overlay: where it lies in the world and where it lies in the overlay (0 to 1 across its rectangle along each axis).</summary>
public readonly record struct DrapedCorner(Vector3 Position, Vector2 Uv);

/// <summary>Four corners drawn as the triangles (A, B, C) and (A, C, D); a triangle repeats its last corner.</summary>
public readonly record struct DrapedQuad(DrapedCorner A, DrapedCorner B, DrapedCorner C, DrapedCorner D);

/// <summary>
/// Lays an overlay on the ground the way <c>W3DProjectedShadowManager::queueDecal</c> projects a decal: not a flat picture and not a grid of its own, but the terrain's own
/// cells under it, each corner at the height of its height-map vertex and the overlay's texture mapped onto them from above. So the overlay follows the ground through every
/// cell and is never cut by a rise inside it, while objects in front still hide it (the depth test stays). Two differences from the original, each with its reason:
/// <list type="bullet">
/// <item>The original lets its cells reach past the overlay and relies on clamped texture addressing and a transparent border; scorches share one atlas
/// (<c>EXScorch01.tga</c>), so here each triangle is clipped to the overlay's rectangle and nothing outside it is drawn.</item>
/// <item>The original splits a cell along the height map's own diagonal (<c>WorldHeightMap::getFlipState</c>, which the terrain's blend tiles decide). Here a cell is split
/// along the diagonal whose middle lies higher: of the two ways to split four corners that one is the upper one everywhere in the cell, so the overlay lies on or above
/// the drawn ground whichever way the terrain split it - never under it.</item>
/// </list>
/// </summary>
public static class GroundDrape
{
    /// <summary><c>queueDecal</c> clips a decal to 104 vertices per side so that it fits its index buffer; that is more than any overlay of the data needs (a radius of 300 is 61).</summary>
    public const int MaxVerticesPerSide = 104;

    private struct Point
    {
        public float X, Y, Z, U, V;
    }

    /// <summary>
    /// Appends to <paramref name="output"/> the pieces of the rectangle centred at <paramref name="center"/> whose texture axes run along <paramref name="axisU"/> and
    /// <paramref name="axisV"/> (unit length, at right angles) for <paramref name="sizeU"/> and <paramref name="sizeV"/>, laid on <paramref name="heights"/> and lifted by
    /// <paramref name="lift"/>. Returns the number of pieces appended.
    /// </summary>
    public static int Drape(TerrainHeightSampler heights, Vector2 center, Vector2 axisU, Vector2 axisV, float sizeU, float sizeV, float lift, List<DrapedQuad> output)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(output);
        if (!(sizeU > 0f) || !(sizeV > 0f)) return 0;
        var halfU = axisU * (sizeU * 0.5f);
        var halfV = axisV * (sizeV * 0.5f);
        var reachX = MathF.Abs(halfU.X) + MathF.Abs(halfV.X);
        var reachY = MathF.Abs(halfU.Y) + MathF.Abs(halfV.Y);
        const float cell = TerrainHeightSampler.CellSize;
        var border = heights.Border;
        var startX = Math.Clamp((int)MathF.Floor((center.X - reachX) / cell) + border, 0, heights.VertexWidth - 1);
        var endX = Math.Clamp((int)MathF.Ceiling((center.X + reachX) / cell) + border, 0, heights.VertexWidth - 1);
        var startY = Math.Clamp((int)MathF.Floor((center.Y - reachY) / cell) + border, 0, heights.VertexHeight - 1);
        var endY = Math.Clamp((int)MathF.Ceiling((center.Y + reachY) / cell) + border, 0, heights.VertexHeight - 1);
        Trim(ref startX, ref endX);
        Trim(ref startY, ref endY);
        if (endX <= startX || endY <= startY) return 0;

        var before = output.Count;
        var toU = axisU / sizeU;
        var toV = axisV / sizeV;
        Span<Point> corners = stackalloc Point[4];
        Span<Point> polygon = stackalloc Point[9];
        Span<Point> scratch = stackalloc Point[9];
        for (var j = startY; j < endY; j++)
        {
            for (var i = startX; i < endX; i++)
            {
                // Corners 0..3 counter-clockwise from the cell's lower left, as the terrain mesh numbers them.
                Corner(ref corners[0], i, j);
                Corner(ref corners[1], i + 1, j);
                Corner(ref corners[2], i + 1, j + 1);
                Corner(ref corners[3], i, j + 1);
                if (AllOutside(corners)) continue;
                // The upper of the two splits: along 0-2 when its middle lies at least as high as the middle of 1-3.
                var first = corners[0].Z + corners[2].Z >= corners[1].Z + corners[3].Z ? 0 : 1;
                if (AllInside(corners))
                {
                    output.Add(new DrapedQuad(Of(corners[first]), Of(corners[first + 1]), Of(corners[first + 2]), Of(corners[(first + 3) & 3])));
                    continue;
                }

                Triangle(corners[first], corners[first + 1], corners[first + 2], polygon, scratch, output);
                Triangle(corners[first], corners[first + 2], corners[(first + 3) & 3], polygon, scratch, output);
            }
        }

        return output.Count - before;

        void Corner(ref Point point, int vertexX, int vertexY)
        {
            point.X = (vertexX - border) * cell;
            point.Y = (vertexY - border) * cell;
            point.Z = heights.RawHeight(vertexX, vertexY) * TerrainHeightSampler.HeightScale + lift;
            var offset = new Vector2(point.X, point.Y) - center;
            point.U = Vector2.Dot(offset, toU) + 0.5f;
            point.V = Vector2.Dot(offset, toV) + 0.5f;
        }
    }

    private static void Trim(ref int start, ref int end)
    {
        var extra = end - start + 1 - MaxVerticesPerSide;
        if (extra <= 0) return;
        start += extra / 2;
        end -= extra - extra / 2;
    }

    private static DrapedCorner Of(in Point point) => new(new Vector3(point.X, point.Y, point.Z), new Vector2(point.U, point.V));

    private static bool AllInside(ReadOnlySpan<Point> points)
    {
        foreach (var point in points)
            if (point.U < 0f || point.U > 1f || point.V < 0f || point.V > 1f) return false;
        return true;
    }

    private static bool AllOutside(ReadOnlySpan<Point> points)
    {
        bool belowU = true, aboveU = true, belowV = true, aboveV = true;
        foreach (var point in points)
        {
            belowU &= point.U < 0f;
            aboveU &= point.U > 1f;
            belowV &= point.V < 0f;
            aboveV &= point.V > 1f;
        }

        return belowU || aboveU || belowV || aboveV;
    }

    /// <summary>Clips one triangle of a cell to the rectangle (Sutherland-Hodgman against u = 0, u = 1, v = 0, v = 1) and appends the polygon as a fan of pieces.</summary>
    private static void Triangle(in Point a, in Point b, in Point c, Span<Point> polygon, Span<Point> scratch, List<DrapedQuad> output)
    {
        polygon[0] = a;
        polygon[1] = b;
        polygon[2] = c;
        var count = 3;
        count = Clip(polygon, count, scratch, 0, 0f, true);
        count = Clip(scratch, count, polygon, 0, 1f, false);
        count = Clip(polygon, count, scratch, 1, 0f, true);
        count = Clip(scratch, count, polygon, 1, 1f, false);
        if (count < 3) return;
        for (var index = 1; index + 1 < count; index += 2)
        {
            var last = index + 2 < count ? index + 2 : index + 1;
            output.Add(new DrapedQuad(Of(polygon[0]), Of(polygon[index]), Of(polygon[index + 1]), Of(polygon[last])));
        }
    }

    /// <summary>One plane of the clip: keeps what lies at or above <paramref name="limit"/> (<paramref name="keepAbove"/>) or at or below it, along u (axis 0) or v (axis 1).</summary>
    private static int Clip(ReadOnlySpan<Point> input, int count, Span<Point> result, int axis, float limit, bool keepAbove)
    {
        var written = 0;
        if (count == 0) return 0;
        var previous = input[count - 1];
        var previousDistance = Distance(previous, axis, limit, keepAbove);
        for (var index = 0; index < count; index++)
        {
            var current = input[index];
            var distance = Distance(current, axis, limit, keepAbove);
            if ((distance >= 0f) != (previousDistance >= 0f))
            {
                var t = previousDistance / (previousDistance - distance);
                result[written++] = Lerp(previous, current, t);
            }

            if (distance >= 0f) result[written++] = current;
            previous = current;
            previousDistance = distance;
        }

        return written;
    }

    private static float Distance(in Point point, int axis, float limit, bool keepAbove)
    {
        var value = axis == 0 ? point.U : point.V;
        return keepAbove ? value - limit : limit - value;
    }

    private static Point Lerp(in Point from, in Point to, float t) => new()
    {
        X = from.X + (to.X - from.X) * t,
        Y = from.Y + (to.Y - from.Y) * t,
        Z = from.Z + (to.Z - from.Z) * t,
        U = from.U + (to.U - from.U) * t,
        V = from.V + (to.V - from.V) * t,
    };
}
