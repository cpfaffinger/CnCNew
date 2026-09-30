using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// Water look from <c>Water.ini</c>: the <c>WaterSet</c> of the configured time of day and the <c>WaterTransparency</c> block
/// (<c>W3DWater.cpp</c>, <c>WaterTransparencySetting</c>). <see cref="SurfaceColor"/> is the vertex colour the reference computes for polygon
/// water in <c>drawTrapezoidWater</c>/<c>drawRiverWater</c>: with the default white <c>StandingWaterColor</c> the terrain ambient plus the
/// downward share of every global light's diffuse, times the <c>WaterSet DiffuseColor</c>; alpha is that colour's alpha.
/// </summary>
public sealed record WaterAppearance(
    string TimeOfDay,
    string StandingWaterTexture,
    Rgba32 SurfaceColor,
    Rgba32 RadarColor,
    float TransparentWaterDepth,
    float TransparentWaterMinOpacity,
    bool AdditiveBlending,
    float UScrollPerMs,
    float VScrollPerMs)
{
    /// <summary><c>WaterRenderObjClass::update</c>: <c>m_riverVOrigin += 0.002</c> per 30 Hz logic frame.</summary>
    public const float RiverVScrollPerSecond = 0.002f * 30f;

    /// <summary><c>drawTrapezoidWater</c>: <c>waterFactor = 150</c> world units per texture repeat.</summary>
    public const float StandingWaterUnitsPerRepeat = 150f;

    public static WaterAppearance FromDefinitions(CompiledDefinitionGraph graph, TerrainLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        var set = WorldDefinitionReader.Compatibility(graph, "WaterSet", lighting.TimeOfDay);
        var transparency = WorldDefinitionReader.Compatibility(graph, "WaterTransparency", "WaterTransparency");
        var diffuse = WorldDefinitionReader.Color(set, "DiffuseColor");
        var standing = WorldDefinitionReader.UnitColor(transparency, "StandingWaterColor");
        Vector3 shade;
        if (standing == Vector3.One)
        {
            shade = lighting.Lights[0].Ambient;
            foreach (var light in lighting.Lights)
                if (-light.Position.Z > 0) shade += -light.Position.Z * light.Diffuse;
            shade *= new Vector3(diffuse.Red / 255f, diffuse.Green / 255f, diffuse.Blue / 255f);
        }
        else
        {
            shade = standing == Vector3.Zero ? Vector3.One : standing;
        }
        var radar = WorldDefinitionReader.Color(transparency, "RadarWaterColor");
        return new WaterAppearance(
            lighting.TimeOfDay,
            WorldDefinitionReader.Text(transparency, "StandingWaterTexture"),
            Rgba32.FromUnit(shade, diffuse.Alpha),
            new Rgba32(radar.Red, radar.Green, radar.Blue, 255),
            WorldDefinitionReader.Real(transparency, "TransparentWaterDepth"),
            WorldDefinitionReader.Real(transparency, "TransparentWaterMinOpacity"),
            WorldDefinitionReader.Boolean(transparency, "AdditiveBlending"),
            WorldDefinitionReader.Real(set, "UScrollPerMS"),
            WorldDefinitionReader.Real(set, "VScrollPerMS"));
    }
}

/// <summary>
/// One water polygon as a flat, double-sided mesh (the reference disables culling for water). <see cref="Uv"/> are the base texture
/// coordinates; a renderer adds <see cref="UvScrollPerSecond"/> times elapsed seconds. Indices are counter-clockwise seen from above.
/// </summary>
public sealed record WaterSurface(
    string Name,
    bool IsRiver,
    ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector2> Uv,
    ImmutableArray<int> Indices,
    Rgba32 Color,
    string Texture,
    Vector2 UvScrollPerSecond,
    Aabb Bounds);

public static class WaterSurfaceBuilder
{
    /// <summary>
    /// Standing water follows <c>WaterRenderObjClass::renderWater</c>: the polygon is cut into the quads (0, k, k+1, k+2) for odd k, each quad a
    /// bilinear patch of at most 50x50 rectangles about 8 cells long, UV = position / 150. Rivers follow <c>drawRiverWater</c>: a strip
    /// between the two banks that start at <c>RiverStart</c>, U across (0.5 on the inner bank, 0 on the outer), V along the river.
    /// Point z values are used as stored (the reference draws the polygon at its point heights).
    /// </summary>
    public static ImmutableArray<WaterSurface> Build(MapData map, WaterAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(appearance);
        var result = ImmutableArray.CreateBuilder<WaterSurface>();
        foreach (var trigger in map.Triggers)
        {
            if (!trigger.IsWater || trigger.Points.Length <= 2) continue;
            var surface = trigger.IsRiver ? BuildRiver(trigger, appearance) : BuildStanding(trigger, appearance);
            if (surface is not null) result.Add(surface);
        }
        return result.ToImmutable();
    }

    private static WaterSurface BuildStanding(MapPolygonTrigger trigger, WaterAppearance appearance)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var points = trigger.Points;
        for (var k = 1; k < points.Length - 1; k += 2)
        {
            var pt3 = ToVector(points[0]);
            var pt2 = ToVector(points[k]);
            var pt1 = ToVector(points[k + 1]);
            var pt0 = k + 2 < points.Length ? ToVector(points[k + 2]) : pt1;
            AddTrapezoid(pt0, pt1, pt2, pt3, positions, indices);
        }
        var uv = positions.Select(position => new Vector2(position.X, position.Y) / WaterAppearance.StandingWaterUnitsPerRepeat).ToImmutableArray();
        return new WaterSurface(trigger.Name, false, [..positions], uv, [..indices], appearance.SurfaceColor, appearance.StandingWaterTexture,
            Vector2.Zero, Aabb.FromPoints(positions.ToArray()));
    }

    /// <summary><c>drawTrapezoidWater</c>: origin = pt0, u edge to pt1, v edge to pt3, bilinear towards pt2.</summary>
    private static void AddTrapezoid(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, List<Vector3> positions, List<int> indices)
    {
        var uVec1 = p1 - p0;
        var vVec1 = p3 - p0;
        var uVec2 = p2 - p3;
        var vVec2 = p2 - p1;
        var uCount = Math.Clamp((int)((uVec1.Length() + uVec2.Length()) / (8 * TerrainHeightSampler.CellSize)), 1, 50) + 1;
        var vCount = Math.Clamp((int)((vVec1.Length() + vVec2.Length()) / (8 * TerrainHeightSampler.CellSize)), 1, 50) + 1;
        var first = positions.Count;
        for (var j = 0; j < vCount; j++)
        {
            var dv = (float)j / (vCount - 1);
            for (var i = 0; i < uCount; i++)
            {
                var du = (float)i / (uCount - 1);
                positions.Add(p0 + uVec1 * du + vVec1 * dv + dv * du * (vVec2 - vVec1));
            }
        }
        for (var j = 0; j < vCount - 1; j++)
            for (var i = 0; i < uCount - 1; i++)
            {
                var a = first + j * uCount + i;
                var b = first + j * uCount + i + 1;
                var c = first + (j + 1) * uCount + i + 1;
                var d = first + (j + 1) * uCount + i;
                AddTriangle(positions, indices, a, c, d);
                AddTriangle(positions, indices, a, b, c);
            }
    }

    private static WaterSurface? BuildRiver(MapPolygonTrigger trigger, WaterAppearance appearance)
    {
        var points = trigger.Points;
        var count = points.Length;
        var innerIndex = trigger.RiverStart;
        if (innerIndex < 0 || innerIndex >= count - 1) return null;
        var outerIndex = innerIndex + 1;
        float endLength = 0, totalLength = 0;
        for (var i = 0; i < count - 1; i++)
        {
            var length = Vector2.Distance(new Vector2(points[i].X, points[i].Y), new Vector2(points[i + 1].X, points[i + 1].Y));
            totalLength += length;
            if (i == innerIndex) endLength = length;
        }
        var rectangles = count / 2 - 1;
        if (rectangles < 1 || endLength <= 0) return null;
        var vScale = (totalLength / 2 - endLength) / endLength / rectangles;
        var positions = new List<Vector3>();
        var uv = new List<Vector2>();
        for (var i = 0; i < count / 2; i++)
        {
            var inner = points[outerIndex];
            var outer = points[innerIndex];
            outerIndex = outerIndex + 1 >= count ? 0 : outerIndex + 1;
            innerIndex = innerIndex - 1 < 0 ? count - 1 : innerIndex - 1;
            positions.Add(ToVector(inner));
            uv.Add(new Vector2(0.5f, vScale * i));
            positions.Add(ToVector(outer));
            uv.Add(new Vector2(0f, vScale * i));
        }
        var indices = new List<int>();
        for (var i = 0; i < rectangles; i++)
        {
            AddTriangle(positions, indices, i * 2, i * 2 + 1, i * 2 + 3);
            AddTriangle(positions, indices, i * 2, i * 2 + 3, i * 2 + 2);
        }
        return new WaterSurface(trigger.Name, true, [..positions], [..uv], [..indices], appearance.SurfaceColor, appearance.StandingWaterTexture,
            new Vector2(0f, -WaterAppearance.RiverVScrollPerSecond), Aabb.FromPoints(positions.ToArray()));
    }

    /// <summary>Adds a triangle, reordered to be counter-clockwise from above (water is drawn double-sided, the order only normalizes output).</summary>
    private static void AddTriangle(List<Vector3> positions, List<int> indices, int a, int b, int c)
    {
        var ab = positions[b] - positions[a];
        var ac = positions[c] - positions[a];
        var cross = ab.X * ac.Y - ab.Y * ac.X;
        indices.Add(a);
        if (cross < 0)
        {
            indices.Add(c);
            indices.Add(b);
        }
        else
        {
            indices.Add(b);
            indices.Add(c);
        }
    }

    private static Vector3 ToVector(MapPoint point) => new(point.X, point.Y, point.Z);
}
