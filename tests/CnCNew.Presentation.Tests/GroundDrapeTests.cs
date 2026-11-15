using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Presentation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// #367: an overlay on the ground - a missile's delivery decal, a radius cursor, a scorch, a shadow decal, a light pulse - is laid on the terrain's own cells as
/// <c>W3DProjectedShadowManager::queueDecal</c> projects it, so no rise of the ground inside its circle hides a part of it. Before, a decal was a grid of at most 8 x 8 quads
/// of its own, 52 units wide for the nuclear missile's 210, and the ground rose through it.
/// </summary>
internal static class GroundDrapeTests
{
    private const float Lift = 0.3f;

    public static int Run(string[] args)
    {
        var t = new TestHarness("GroundDrape");
        t.Run("a turned decal on hills lies on or above both ways the ground can split a cell, everywhere, and touches it at the height-map's vertices", AboveGround);
        t.Run("the pieces cover exactly the decal's rectangle, each within one cell, with the texture mapped from above", Coverage);
        t.Run("the nuclear missile's delivery decal (radius 210) on a steep ridge is drawn whole and above the ground", NuclearDecal);
        t.Run("a decal that reaches past the height field keeps the part on the field and no more", PastTheField);
        return t.Failures;
    }

    /// <summary>Hills of every slope: a steep ridge across x, bumps of a few cells and a rise of the whole field, from 0 to 255 raw.</summary>
    private static TerrainHeightSampler Hills(int size = 129, int border = 4)
    {
        var field = new MapHeightField(size, size, border, [new MapBoundary(size - 1 - 2 * border, size - 1 - 2 * border)],
            [..Enumerable.Range(0, size * size).Select(index =>
            {
                var x = index % size;
                var y = index / size;
                var ridge = Math.Max(0, 120 - 12 * Math.Abs(x - size / 2));
                var bumps = 40 + 40 * Math.Sin(x * 0.9) * Math.Cos(y * 1.3);
                return (byte)Math.Clamp(ridge + bumps + y * 0.4, 0, 255);
            })]);
        return new TerrainHeightSampler(field);
    }

    /// <summary>The ground split along the other diagonal of its cell (1-3), the way a blend tile may flip it.</summary>
    private static float OtherSplit(TerrainHeightSampler heights, float x, float y)
    {
        const float cell = TerrainHeightSampler.CellSize;
        var gx = x / cell + heights.Border;
        var gy = y / cell + heights.Border;
        var ix = Math.Min((int)MathF.Floor(gx), heights.VertexWidth - 2);
        var iy = Math.Min((int)MathF.Floor(gy), heights.VertexHeight - 2);
        var fx = gx - ix;
        var fy = gy - iy;
        float P(int px, int py) => heights.RawHeight(px, py) * TerrainHeightSampler.HeightScale;
        float p0 = P(ix, iy), p1 = P(ix + 1, iy), p2 = P(ix + 1, iy + 1), p3 = P(ix, iy + 1);
        // Triangles (0, 1, 3) below the diagonal fx + fy = 1 and (1, 2, 3) above it.
        return fx + fy <= 1f ? p0 + fx * (p1 - p0) + fy * (p3 - p0) : p2 + (1f - fx) * (p3 - p2) + (1f - fy) * (p1 - p2);
    }

    private static IEnumerable<(Vector3 Point, Vector2 Uv)> Samples(DrapedQuad piece)
    {
        foreach (var (a, b, c) in new[] { (piece.A, piece.B, piece.C), (piece.A, piece.C, piece.D) })
            for (var i = 0; i <= 4; i++)
                for (var j = 0; j <= 4 - i; j++)
                {
                    var (wa, wb) = (i / 4f, j / 4f);
                    var wc = 1f - wa - wb;
                    yield return (a.Position * wa + b.Position * wb + c.Position * wc, a.Uv * wa + b.Uv * wb + c.Uv * wc);
                }
    }

    private static List<DrapedQuad> Drape(TerrainHeightSampler heights, Vector2 center, float angle, float sizeU, float sizeV)
    {
        var pieces = new List<DrapedQuad>();
        var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var v = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        GroundDrape.Drape(heights, center, u, v, sizeU, sizeV, Lift, pieces);
        return pieces;
    }

    private static void AboveGround()
    {
        var heights = Hills();
        var pieces = Drape(heights, new Vector2(600f, 300f), 0.37f, 94f, 70f);
        Assert(pieces.Count > 60, $"a decal of 94 x 70 lies on many cells ({pieces.Count} pieces)");
        var lowest = float.MaxValue;
        var vertexCorners = 0;
        foreach (var piece in pieces)
        {
            foreach (var (point, _) in Samples(piece))
            {
                var ground = MathF.Max(heights.HeightAt(point.X, point.Y), OtherSplit(heights, point.X, point.Y));
                lowest = MathF.Min(lowest, point.Z - ground);
            }

            foreach (var corner in new[] { piece.A, piece.B, piece.C, piece.D })
            {
                var gx = corner.Position.X / TerrainHeightSampler.CellSize;
                var gy = corner.Position.Y / TerrainHeightSampler.CellSize;
                if (MathF.Abs(gx - MathF.Round(gx)) > 1e-4f || MathF.Abs(gy - MathF.Round(gy)) > 1e-4f) continue;
                vertexCorners++;
                var raw = heights.RawHeight((int)MathF.Round(gx) + heights.Border, (int)MathF.Round(gy) + heights.Border) * TerrainHeightSampler.HeightScale;
                Near(raw + Lift, corner.Position.Z, 1e-3, $"a corner on the height map's vertex {corner.Position} lies at its height plus the lift");
            }
        }

        Assert(lowest >= Lift - 1e-3f, $"no point of the decal lies under the ground split either way plus the lift (lowest {lowest:0.0000} above it)");
        Assert(vertexCorners > 100, $"the inner pieces stand on the height map's own vertices ({vertexCorners} corners)");
    }

    private static void Coverage()
    {
        var heights = Hills();
        var center = new Vector2(410f, 520f);
        const float angle = -0.81f;
        const float sizeU = 63f, sizeV = 41f;
        var pieces = Drape(heights, center, angle, sizeU, sizeV);
        var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var v = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
        double area = 0;
        foreach (var piece in pieces)
        {
            area += Area(piece.A, piece.B, piece.C) + Area(piece.A, piece.C, piece.D);
            var middle = (piece.A.Position + piece.B.Position + piece.C.Position + piece.D.Position) / 4f;
            var cellX = MathF.Floor(middle.X / TerrainHeightSampler.CellSize);
            var cellY = MathF.Floor(middle.Y / TerrainHeightSampler.CellSize);
            foreach (var corner in new[] { piece.A, piece.B, piece.C, piece.D })
            {
                Assert(corner.Position.X >= cellX * 10f - 1e-3f && corner.Position.X <= cellX * 10f + 10f + 1e-3f
                    && corner.Position.Y >= cellY * 10f - 1e-3f && corner.Position.Y <= cellY * 10f + 10f + 1e-3f, $"a piece lies within one cell ({corner.Position})");
                Assert(corner.Uv.X >= -1e-4f && corner.Uv.X <= 1f + 1e-4f && corner.Uv.Y >= -1e-4f && corner.Uv.Y <= 1f + 1e-4f, $"nothing outside the decal is drawn ({corner.Uv})");
                var offset = new Vector2(corner.Position.X, corner.Position.Y) - center;
                Near(Vector2.Dot(offset, u) / sizeU + 0.5f, corner.Uv.X, 1e-4, "u is the decal's own axis seen from above");
                Near(Vector2.Dot(offset, v) / sizeV + 0.5f, corner.Uv.Y, 1e-4, "v is the decal's own axis seen from above");
            }
        }

        Near(sizeU * sizeV, area, 0.5, "the pieces cover the decal's rectangle once, seen from above");

        static double Area(DrapedCorner a, DrapedCorner b, DrapedCorner c)
            => Math.Abs((b.Position.X - a.Position.X) * (c.Position.Y - a.Position.Y) - (c.Position.X - a.Position.X) * (b.Position.Y - a.Position.Y)) / 2.0;
    }

    private static void NuclearDecal()
    {
        var heights = Hills();
        // Across the ridge in the middle of the field: 120 raw (75 units) up and down within 100 units.
        var center = new Vector2((heights.VertexWidth / 2 - heights.Border) * TerrainHeightSampler.CellSize + 3f, 620f);
        var pieces = Drape(heights, center, 0f, 420f, 420f);
        var lowest = float.MaxValue;
        foreach (var piece in pieces)
            foreach (var (point, _) in Samples(piece))
                lowest = MathF.Min(lowest, point.Z - heights.HeightAt(point.X, point.Y));
        Assert(lowest >= Lift - 1e-3f, $"the whole circle lies above the ridge (lowest {lowest:0.0000})");
        Assert(pieces.Count is > 1700 and < 2600, $"one piece per cell inside, a few more at the edge ({pieces.Count})");
    }

    private static void PastTheField()
    {
        var heights = Hills(33, 2);
        var pieces = Drape(heights, new Vector2(-20f, 100f), 0.2f, 120f, 120f);
        Assert(pieces.Count > 0, "the part on the field is drawn");
        foreach (var piece in pieces)
            foreach (var corner in new[] { piece.A, piece.B, piece.C, piece.D })
                Assert(heights.IsInsideField(corner.Position.X, corner.Position.Y), $"nothing beyond the field ({corner.Position})");
        Assert(Drape(heights, new Vector2(-900f, 100f), 0f, 50f, 50f).Count == 0, "a decal wholly beyond the field has no piece");
        Assert(Drape(heights, new Vector2(100f, 100f), 0f, 0f, 50f).Count == 0, "a decal without size has no piece");
    }
}
