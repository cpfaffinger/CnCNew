using System.Numerics;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// The triangles of every road piece, ported from <c>W3DRoadBuffer::preloadRoadSegment</c>, <c>loadCurve</c>, <c>loadTee</c>, <c>loadY</c>, <c>loadH</c>,
/// <c>loadAlphaJoin</c>, <c>loadFloatSection</c> and <c>loadFloat4PtSection</c>. A piece is a quadrilateral, cut into columns along its length (one column per
/// map cell); each column is flattened to the largest height of the cells it crosses and floats a fraction of a height step above the ground, and columns that a
/// straight line between their neighbours reproduces are dropped. The texture coordinates place the road's own sprite in a 512 x 512 strip atlas: segments, curves,
/// tees, crossings, Y and H pieces and the alpha join are fixed rectangles of one road texture (offsets in 512ths below).
/// </summary>
internal static class RoadGeometry
{
    /// <summary><c>FLOAT_AMOUNT</c>: a road floats this far above the highest corner of the cell, against z-fighting.</summary>
    public const float FloatAmount = TerrainHeightSampler.HeightScale / 8f;

    private const float MaxError = TerrainHeightSampler.HeightScale * 1.1f;
    private const int MaxRows = 100;

    /// <summary>Fills <see cref="RoadSeg.Positions"/>, <see cref="RoadSeg.Uvs"/> and <see cref="RoadSeg.Indices"/> of one segment.</summary>
    public static void Load(RoadSeg road, TerrainHeightSampler heights)
    {
        var loc1 = road.P1.Loc;
        var loc2 = road.P2.Loc;
        switch (road.Type)
        {
            case RoadPiece.Segment: LoadSegment(road, heights); break;
            case RoadPiece.Curve: LoadCurve(road, loc1, loc2, road.Scale, heights); break;
            case RoadPiece.ThreeWayY: LoadY(road, loc1, loc2, road.Scale, heights); break;
            case RoadPiece.ThreeWayH: LoadH(road, loc1, loc2, false, road.Scale, heights); break;
            case RoadPiece.ThreeWayHFlip: LoadH(road, loc1, loc2, true, road.Scale, heights); break;
            case RoadPiece.Tee: LoadTee(road, loc1, loc2, false, road.Scale, heights); break;
            case RoadPiece.FourWay: LoadTee(road, loc1, loc2, true, road.Scale, heights); break;
            case RoadPiece.AlphaJoin: LoadAlphaJoin(road, loc1, loc2, road.Scale, heights); break;
            default: throw new InvalidDataException($"Unknown road piece {road.Type}.");
        }
    }

    private static Vector2 Normalized(Vector2 value)
    {
        var length2 = value.LengthSquared();
        return length2 != 0f ? value * (1f / MathF.Sqrt(length2)) : value;
    }

    /// <summary>The forward/normal pair every <c>load*</c> starts with; a (near) zero vector falls back to +x / +y like the reference.</summary>
    private static (Vector2 Vector, Vector2 Normal) Axes(Vector2 loc1, Vector2 loc2)
    {
        var roadVector = loc2 - loc1;
        var roadNormal = new Vector2(-roadVector.Y, roadVector.X);
        if (MathF.Abs(roadVector.X) < RoadLayout.MinRoadSegment && MathF.Abs(roadVector.Y) < RoadLayout.MinRoadSegment) return (new Vector2(1f, 0f), new Vector2(0f, 1f));
        return (Normalized(roadVector), Normalized(roadNormal));
    }

    private static void LoadSegment(RoadSeg road, TerrainHeightSampler heights)
    {
        var roadVector = road.P2.Loc - road.P1.Loc;
        var roadNormal = new Vector2(-roadVector.Y, roadVector.X);
        const float uOffset = 0f;
        const float vOffset = 85f / 512f;
        var roadHeight = road.WidthInTexture * road.Scale / 2f;
        roadNormal = Normalized(roadNormal) * roadHeight;
        Span<Vector2> corners = stackalloc Vector2[4];
        corners[Corner.BottomLeft] = road.P1.Bottom;
        corners[Corner.TopLeft] = road.P1.Top;
        corners[Corner.BottomRight] = road.P2.Bottom;
        corners[Corner.TopRight] = road.P2.Top;
        LoadFloat4PtSection(road, road.P1.Loc, roadNormal, roadVector, corners, uOffset, vOffset, road.Scale, road.Scale, heights);
    }

    private static void LoadCurve(RoadSeg road, Vector2 loc1, Vector2 loc2, float scale, TerrainHeightSampler heights)
    {
        const float uOffset = 4f / 512f;
        var vOffset = 255f / 512f; // CORNER_RADIUS radius 1.5 curve
        var tight = road.CurveRadius == RoadLayout.TightCornerRadius;
        if (tight) vOffset = 425f / 512f;

        var roadVector = loc2 - loc1;
        var roadNormal = new Vector2(-roadVector.Y, roadVector.X);
        var roadLen = scale;
        var curveHeight = road.WidthInTexture * scale / 2f;
        roadVector = Normalized(roadVector) * roadLen;
        roadNormal = Normalized(roadNormal) * MathF.Abs(curveHeight);

        Span<Vector2> c = stackalloc Vector2[4];
        if (tight)
        {
            c[Corner.BottomLeft] = loc1 - roadNormal;
            c[Corner.BottomRight] = c[Corner.BottomLeft] + roadVector * 0.5f;
            c[Corner.TopRight] = c[Corner.BottomRight] + 2f * roadNormal;
            c[Corner.TopLeft] = c[Corner.BottomLeft] + 2f * roadNormal;

            c[Corner.BottomRight] += roadVector * 0.1f;
            c[Corner.BottomRight] += roadNormal * 0.2f;
            c[Corner.BottomLeft] -= roadNormal * 0.1f;
            c[Corner.BottomLeft] -= roadVector * 0.02f;
            c[Corner.TopLeft] -= roadVector * 0.02f;
            c[Corner.TopRight] -= roadVector * 0.4f;
            c[Corner.TopRight] += roadNormal * 0.2f;
        }
        else
        {
            c[Corner.BottomLeft] = loc1 - roadNormal;
            c[Corner.BottomRight] = c[Corner.BottomLeft] + roadVector;
            c[Corner.TopRight] = c[Corner.BottomRight] + 2f * roadNormal;
            c[Corner.TopLeft] = c[Corner.BottomLeft] + 2f * roadNormal;

            c[Corner.BottomRight] += roadVector * 0.1f;
            c[Corner.BottomRight] += roadNormal * 0.4f;
            c[Corner.BottomLeft] -= roadNormal * 0.2f;
            c[Corner.BottomLeft] -= roadVector * 0.02f;
            c[Corner.TopLeft] -= roadVector * 0.02f;
            c[Corner.TopRight] -= roadVector * 0.4f;
            c[Corner.TopRight] += roadNormal * 0.4f;
        }
        LoadFloat4PtSection(road, loc1, roadNormal, roadVector, c, uOffset, vOffset, scale, scale, heights);
    }

    private static void LoadTee(RoadSeg road, Vector2 loc1, Vector2 loc2, bool is4Way, float scale, TerrainHeightSampler heights)
    {
        var uOffset = 425f / 512f;
        var vOffset = 255f / 512f;
        if (is4Way)
        {
            uOffset = 425f / 512f;
            vOffset = 425f / 512f;
        }
        var teeFactor = scale * RoadLayout.TeeWidthAdjustment / 2f;
        var left = road.WidthInTexture * scale / 2f;
        LoadFloatSection(road, loc1, loc2 - loc1, teeFactor, left, teeFactor, uOffset, vOffset, scale, heights);
    }

    private static void LoadAlphaJoin(RoadSeg road, Vector2 loc1, Vector2 loc2, float scale, TerrainHeightSampler heights)
    {
        var (roadVector, roadNormal) = Axes(loc1, loc2);
        const float uOffset = 106f / 512f;
        const float vOffset = 425f / 512f;
        var roadWidth = scale;
        var uScale = road.WidthInTexture;
        roadVector *= roadWidth * 48f / 128f; // just the 48 pixels of the join sprite
        roadNormal *= uScale * (1f + 8f / 128f); // plus 8 extra pixels

        Span<Vector2> c = stackalloc Vector2[4];
        c[Corner.TopLeft] = loc1 + roadNormal * 0.5f - roadVector * 0.65f;
        c[Corner.BottomLeft] = c[Corner.TopLeft] - roadNormal;
        c[Corner.BottomRight] = c[Corner.BottomLeft] + roadVector;
        c[Corner.TopRight] = c[Corner.TopLeft] + roadVector;
        LoadFloat4PtSection(road, loc1, roadNormal, roadVector, c, uOffset, vOffset, scale, uScale, heights);
    }

    private static void LoadY(RoadSeg road, Vector2 loc1, Vector2 loc2, float scale, TerrainHeightSampler heights)
    {
        var (roadVector, roadNormal) = Axes(loc1, loc2);
        const float uOffset = 255f / 512f;
        const float vOffset = 226f / 512f;
        var roadWidth = scale;
        roadVector *= roadWidth;
        roadNormal *= roadWidth;

        Span<Vector2> c = stackalloc Vector2[4];
        roadVector *= 1.59f;
        c[Corner.TopLeft] = loc1 + roadNormal * 0.29f - roadVector * 0.5f;
        c[Corner.BottomLeft] = c[Corner.TopLeft] - roadNormal * 1.08f;
        c[Corner.BottomRight] = c[Corner.BottomLeft] + roadVector;
        c[Corner.TopRight] = c[Corner.TopLeft] + roadVector;
        LoadFloat4PtSection(road, loc1, roadNormal, roadVector, c, uOffset, vOffset, scale, scale, heights);
    }

    private static void LoadH(RoadSeg road, Vector2 loc1, Vector2 loc2, bool flip, float scale, TerrainHeightSampler heights)
    {
        var (roadVector, roadNormal) = Axes(loc1, loc2);
        const float uOffset = 202f / 512f;
        const float vOffset = 364f / 512f;
        var roadWidth = scale;
        roadVector *= roadWidth;
        roadNormal *= roadWidth;

        Span<Vector2> c = stackalloc Vector2[4];
        roadNormal *= 1.35f;
        c[Corner.BottomLeft] = flip
            ? loc1 - roadNormal * 0.20f - roadVector * road.WidthInTexture / 2f
            : loc1 - roadNormal * 0.8f - roadVector * road.WidthInTexture / 2f;
        var width = roadVector * road.WidthInTexture / 2f;
        width += roadVector * 1.2f;
        c[Corner.BottomRight] = c[Corner.BottomLeft] + width;
        c[Corner.TopRight] = c[Corner.BottomRight] + roadNormal;
        c[Corner.TopLeft] = c[Corner.BottomLeft] + roadNormal;
        if (flip) roadNormal = -roadNormal;
        LoadFloat4PtSection(road, loc1, roadNormal, roadVector, c, uOffset, vOffset, scale, scale, heights);
    }

    private static void LoadFloatSection(RoadSeg road, Vector2 loc, Vector2 roadVector, float halfHeight, float left, float right,
        float uOffset, float vOffset, float scale, TerrainHeightSampler heights)
    {
        var roadNormal = new Vector2(-roadVector.Y, roadVector.X);
        roadVector = Normalized(roadVector) * right;
        roadNormal = Normalized(roadNormal);
        if (halfHeight < 0f) halfHeight = -halfHeight;
        roadNormal *= halfHeight;

        var roadLeft = Normalized(roadVector) * left;
        roadVector += roadLeft;
        var leftCenter = loc - roadLeft;

        Span<Vector2> c = stackalloc Vector2[4];
        c[Corner.BottomLeft] = leftCenter - roadNormal;
        c[Corner.BottomRight] = c[Corner.BottomLeft] + roadVector;
        c[Corner.TopRight] = c[Corner.BottomRight] + 2f * roadNormal;
        c[Corner.TopLeft] = c[Corner.BottomLeft] + 2f * roadNormal;
        LoadFloat4PtSection(road, loc, roadNormal, roadVector, c, uOffset, vOffset, scale, scale, heights);
    }

    /// <summary>One column of the strip: the lower and upper edge point (both at the column's flattened height) and the vertex numbers once written.</summary>
    private struct Column
    {
        public bool Deleted;
        public float UIndex;
        public Vector3 Low;
        public Vector3 High;
        public int LowIndex;
        public int HighIndex;
    }

    /// <summary>
    /// <c>loadFloat4PtSection</c>: the road is laid into the quadrilateral of <paramref name="corners"/> (bilinear), <paramref name="loc"/> is the point with
    /// the texture coordinate (<paramref name="uOffset"/>, <paramref name="vOffset"/>), <paramref name="roadVector"/>/<paramref name="roadNormal"/> give the
    /// texture directions and the extent (their lengths set the column and sample counts). One road texture width spans four times the scale along the road.
    /// </summary>
    private static void LoadFloat4PtSection(RoadSeg road, Vector2 loc, Vector2 roadNormal, Vector2 roadVector, ReadOnlySpan<Vector2> corners,
        float uOffset, float vOffset, float uScale, float vScale, TerrainHeightSampler heights)
    {
        var roadLen = roadVector.Length();
        var halfHeight = roadNormal.Length();
        roadNormal = Normalized(roadNormal);
        roadVector = Normalized(roadVector);
        var uCount = (int)(roadLen / TerrainHeightSampler.CellSize) + 1;
        if (uCount < 2) uCount = 2;
        var vCount = (int)(2f * halfHeight / TerrainHeightSampler.CellSize) + 1;
        if (vCount < 2) vCount = 2;
        if (vCount > MaxRows) vCount = MaxRows;

        var origin = corners[Corner.BottomLeft];
        var uVector = corners[Corner.BottomRight] - corners[Corner.BottomLeft];
        var vVector1 = corners[Corner.TopLeft] - corners[Corner.BottomLeft];
        var vVector2 = corners[Corner.TopRight] - corners[Corner.BottomRight];

        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        Column previous = new() { Deleted = true };
        Column current = new() { Deleted = true };
        Column next = default;
        for (var i = 0; i <= uCount; i++)
        {
            var iFactor = i / (float)(uCount - 1);
            var iBarFactor = 1f - iFactor;
            if (i < uCount)
            {
                next = new Column { UIndex = i };
                var maxHeight = heights.MinHeight;
                Vector2 lowXy = default, highXy = default;
                for (var j = 0; j < vCount; j++)
                {
                    var jFactor = j / (float)(vCount - 1);
                    var jBarFactor = 1f - jFactor;
                    var point = origin + uVector * jBarFactor * iFactor + uVector * jFactor * iFactor + vVector1 * iBarFactor * jFactor + vVector2 * iFactor * jFactor;
                    var z = heights.MaxCellHeight(point.X, point.Y);
                    if (z > maxHeight) maxHeight = z;
                    if (j == 0) lowXy = point;
                    if (j == vCount - 1) highXy = point;
                }
                // Every column is collapsed to its two edge points at the highest cell height under the column.
                next.Low = new Vector3(lowXy, maxHeight);
                next.High = new Vector3(highXy, maxHeight);
                if (i < 2)
                {
                    current = next;
                }
                else
                {
                    // The reference weights the neighbours by distance to the *current* column instead of the other one (an inverted interpolation);
                    // it is reproduced because it decides which columns are dropped.
                    var theZ = previous.Low.Z * (current.UIndex - previous.UIndex) + next.Low.Z * (next.UIndex - current.UIndex);
                    theZ /= next.UIndex - previous.UIndex;
                    if (theZ >= current.Low.Z && theZ < current.Low.Z + MaxError)
                    {
                        theZ = previous.High.Z * (current.UIndex - previous.UIndex) + next.High.Z * (next.UIndex - current.UIndex);
                        theZ /= next.UIndex - previous.UIndex;
                        if (theZ >= current.High.Z && theZ < current.High.Z + MaxError) current.Deleted = true;
                    }
                }
            }
            if (!current.Deleted && i != 1)
            {
                current.LowIndex = positions.Count;
                AddVertex(current.Low, loc, roadNormal, roadVector, uOffset, vOffset, uScale, vScale, positions, uvs);
                current.HighIndex = positions.Count;
                AddVertex(current.High, loc, roadNormal, roadVector, uOffset, vOffset, uScale, vScale, positions, uvs);
                if (i > 1)
                {
                    // (prev high, prev low, cur low) and (prev high, cur low, cur high)
                    indices.Add(previous.HighIndex); indices.Add(previous.LowIndex); indices.Add(current.LowIndex);
                    indices.Add(previous.HighIndex); indices.Add(current.LowIndex); indices.Add(current.HighIndex);
                    previous = current;
                }
                else if (i == 0)
                {
                    previous = current;
                }
            }
            current = next;
        }
        road.Positions = [..positions];
        road.Uvs = [..uvs];
        road.Indices = [..indices];
    }

    private static void AddVertex(Vector3 point, Vector2 loc, Vector2 roadNormal, Vector2 roadVector, float uOffset, float vOffset, float uScale, float vScale,
        List<Vector3> positions, List<Vector2> uvs)
    {
        var offset = new Vector2(point.X, point.Y) - loc;
        var v = Vector2.Dot(roadNormal, offset);
        var u = Vector2.Dot(roadVector, offset);
        uvs.Add(new Vector2(uOffset + u / (uScale * 4f), vOffset - v / (vScale * 4f))); // a road is 1/16 of the texture height
        positions.Add(new Vector3(point.X, point.Y, point.Z + FloatAmount));
    }

    internal static class Corner
    {
        public const int BottomLeft = 0;
        public const int BottomRight = 1;
        public const int TopLeft = 2;
        public const int TopRight = 3;
    }
}
