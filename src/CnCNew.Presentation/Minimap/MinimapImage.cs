using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Presentation.Minimap;

/// <summary>An entity marker in world coordinates; the caller decides visibility (hide enemies in fog before passing them).</summary>
public readonly record struct MinimapDot(Vector2 World, Rgba32 Color);

/// <summary>
/// The radar image of a map (<c>W3DRadar::buildTerrainTexture</c>) and its composition with fog, entity dots and the camera footprint.
/// The image covers the active map boundary (<c>W3DTerrainLogic::getExtent</c>, boundary 0 by default); its aspect follows the map (the longer side is the requested
/// size). Pixel row 0 is the north edge (largest y), column 0 the west edge. RGBA8, row-major.
/// </summary>
public sealed class MinimapImage
{
    private MinimapImage(int width, int height, Vector2 worldExtent, ImmutableArray<byte> terrain)
    {
        Width = width;
        Height = height;
        WorldExtent = worldExtent;
        TerrainRgba = terrain;
    }

    public int Width { get; }
    public int Height { get; }
    public Vector2 WorldExtent { get; }

    /// <summary>The static terrain layer (no fog, no dots).</summary>
    public ImmutableArray<byte> TerrainRgba { get; }

    /// <summary>Continuous pixel position of a world point (pixel centres at +0.5).</summary>
    public Vector2 WorldToPixel(Vector2 world) => new(world.X / WorldExtent.X * Width, (1f - world.Y / WorldExtent.Y) * Height);

    /// <summary>World point under a continuous pixel position: minimap clicks move the camera or issue commands with it.</summary>
    public Vector2 PixelToWorld(Vector2 pixel) => new(pixel.X / Width * WorldExtent.X, (1f - pixel.Y / Height) * WorldExtent.Y);

    /// <summary>Image size for a map extent: the longer side gets <paramref name="size"/> pixels.</summary>
    public static (int Width, int Height) SizeFor(Vector2 worldExtent, int size)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));
        return worldExtent.X >= worldExtent.Y
            ? (size, Math.Max(1, (int)MathF.Round(size * worldExtent.Y / worldExtent.X)))
            : (Math.Max(1, (int)MathF.Round(size * worldExtent.X / worldExtent.Y)), size);
    }

    /// <summary>
    /// Terrain layer: per pixel the average of the 3x3 neighbouring samples; a land sample is the base tile's average colour
    /// (<c>getTerrainColorAt</c>) brightened above and darkened below the average terrain height (<c>interpolateColorForHeight</c>); a sample
    /// under water is the <c>RadarWaterColor</c> darkened with depth. Cliffs show only through height shading, as in the reference.
    /// </summary>
    public static MinimapImage Build(TerrainRenderModel model, TerrainHeightSampler heights, TerrainMap logic, Rgba32 waterColor, int size, int boundary = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(logic);
        var extent = heights.BoundaryMax(boundary);
        var (width, height) = SizeFor(extent, size);
        var image = new MinimapImage(width, height, extent, []);
        var hiZ = heights.MaxHeight;
        var loZ = heights.MinHeight;
        var water = waterColor.ToUnitRgb();
        var ground = new float[width * height];
        var waterLevel = new float?[width * height];
        double landSum = 0;
        var landCount = 0;
        for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var world = image.PixelToWorld(new Vector2(px + 0.5f, py + 0.5f));
                var z = heights.HeightAt(world.X, world.Y);
                ground[py * width + px] = z;
                if (logic.TryWaterLevel(ToFixed(world.X), ToFixed(world.Y), out var level) && z < (float)level.ToDecimal())
                {
                    waterLevel[py * width + px] = (float)level.ToDecimal();
                }
                else
                {
                    landSum += z;
                    landCount++;
                }
            }
        var averageZ = landCount == 0 ? 0f : (float)(landSum / landCount);
        var samples = new (bool Water, Vector3 Color)[width * height];
        for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var index = py * width + px;
                if (waterLevel[index] is { } waterZ)
                {
                    samples[index] = (true, ShadeForHeight(water, ground[index], waterZ, waterZ, loZ));
                }
                else
                {
                    // W3DRadar passes (average, extent hi.z, extent lo.z) as (hiZ, midZ, loZ): land darkens from the highest point downwards.
                    var color = CellColor(model, heights, image.PixelToWorld(new Vector2(px + 0.5f, py + 0.5f))).ToUnitRgb();
                    samples[index] = (false, ShadeForHeight(color, ground[index], averageZ, hiZ, loZ));
                }
            }

        var rgba = new byte[width * height * 4];
        for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var isWater = samples[py * width + px].Water;
                var sum = Vector3.Zero;
                var count = 0;
                for (var j = py - 1; j <= py + 1; j++)
                {
                    if (j < 0 || j >= height) continue;
                    for (var i = px - 1; i <= px + 1; i++)
                    {
                        if (i < 0 || i >= width) continue;
                        var sample = samples[j * width + i];
                        // Water pixels average only water neighbours (W3DRadar samples isUnderwater per neighbour); land averages all.
                        if (isWater && !sample.Water) continue;
                        sum += sample.Color;
                        count++;
                    }
                }
                var color = Rgba32.FromUnit(sum / Math.Max(count, 1), 255);
                var offset = (py * width + px) * 4;
                rgba[offset] = color.R;
                rgba[offset + 1] = color.G;
                rgba[offset + 2] = color.B;
                rgba[offset + 3] = 255;
            }
        return new MinimapImage(width, height, extent, System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(rgba));
    }

    /// <summary>
    /// Terrain layer multiplied by the fog level of the cell under each pixel centre (shroud black, fog half, clear full), then entity dots
    /// (<paramref name="dotSize"/> pixels square, centred) and the camera footprint as a closed white polyline.
    /// </summary>
    public byte[] Compose(FogOfWarGrid? fog, IReadOnlyList<MinimapDot> dots, IReadOnlyList<Vector2>? cameraFootprint, int dotSize = 2)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var rgba = TerrainRgba.ToArray();
        if (fog is not null)
            for (var py = 0; py < Height; py++)
                for (var px = 0; px < Width; px++)
                {
                    var world = PixelToWorld(new Vector2(px + 0.5f, py + 0.5f));
                    var level = fog.LevelAt(FogOfWarGrid.CellOf(world.X), FogOfWarGrid.CellOf(world.Y));
                    var offset = (py * Width + px) * 4;
                    for (var channel = 0; channel < 3; channel++) rgba[offset + channel] = (byte)((rgba[offset + channel] * level + 127) / 255);
                }
        foreach (var dot in dots)
        {
            var center = WorldToPixel(dot.World);
            var x0 = (int)MathF.Floor(center.X - dotSize / 2f);
            var y0 = (int)MathF.Floor(center.Y - dotSize / 2f);
            for (var y = y0; y < y0 + dotSize; y++)
                for (var x = x0; x < x0 + dotSize; x++) Put(rgba, x, y, dot.Color);
        }
        if (cameraFootprint is { Count: >= 2 })
            for (var i = 0; i < cameraFootprint.Count; i++)
                Line(rgba, WorldToPixel(cameraFootprint[i]), WorldToPixel(cameraFootprint[(i + 1) % cameraFootprint.Count]), new Rgba32(255, 255, 255, 255));
        return rgba;
    }

    private void Put(byte[] rgba, int x, int y, Rgba32 color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        var offset = (y * Width + x) * 4;
        rgba[offset] = color.R;
        rgba[offset + 1] = color.G;
        rgba[offset + 2] = color.B;
        rgba[offset + 3] = color.A;
    }

    /// <summary>Bresenham line between pixel positions, clipped per pixel.</summary>
    private void Line(byte[] rgba, Vector2 from, Vector2 to, Rgba32 color)
    {
        int x0 = (int)MathF.Floor(from.X), y0 = (int)MathF.Floor(from.Y), x1 = (int)MathF.Floor(to.X), y1 = (int)MathF.Floor(to.Y);
        // Keep far-away footprint corners from producing enormous loops.
        const int limit = 1 << 14;
        x0 = Math.Clamp(x0, -limit, limit); y0 = Math.Clamp(y0, -limit, limit);
        x1 = Math.Clamp(x1, -limit, limit); y1 = Math.Clamp(y1, -limit, limit);
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            Put(rgba, x0, y0, color);
            if (x0 == x1 && y0 == y1) return;
            var twice = 2 * error;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }

    private static Rgba32 CellColor(TerrainRenderModel model, TerrainHeightSampler heights, Vector2 world)
    {
        var cx = Math.Clamp((int)MathF.Floor(world.X / TerrainHeightSampler.CellSize) + heights.Border, 0, model.CellsX - 1);
        var cy = Math.Clamp((int)MathF.Floor(world.Y / TerrainHeightSampler.CellSize) + heights.Border, 0, model.CellsY - 1);
        return model.CellColor(cx, cy);
    }

    /// <summary><c>W3DRadar::interpolateColorForHeight(color, height, hiZ, midZ, loZ)</c>: towards 95 % white above <paramref name="midZ"/>, towards 60 % black below it.</summary>
    public static Vector3 ShadeForHeight(Vector3 color, float height, float hiZ, float midZ, float loZ)
    {
        if (hiZ == midZ) hiZ = midZ + 0.1f;
        if (midZ == loZ) loZ = midZ - 0.1f;
        if (hiZ == loZ) hiZ = loZ + 0.2f;
        Vector3 target;
        float t;
        if (height >= midZ)
        {
            t = (height - midZ) / (hiZ - midZ);
            target = color + (Vector3.One - color) * 0.95f;
        }
        else
        {
            t = (midZ - height) / (midZ - loZ);
            target = color - color * 0.60f;
        }
        return Vector3.Clamp(color + (target - color) * t, Vector3.Zero, Vector3.One);
    }

    private static Fixed ToFixed(float value) => Fixed.FromRaw((long)MathF.Round(value * Fixed.ScaleRaw));
}
