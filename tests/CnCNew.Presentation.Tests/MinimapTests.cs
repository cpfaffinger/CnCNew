using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

internal static class MinimapTests
{
    private static readonly Rgba32 Water = new(140, 140, 255, 255);

    public static int Run(string[] args)
    {
        var t = new TestHarness("Minimap");
        t.Run("size follows the map aspect", Size);
        t.Run("pixel and world mapping", Mapping);
        t.Run("height shading", Shading);
        t.Run("terrain layer with water", TerrainLayer);
        t.Run("fog, dots and camera footprint", Compose);
        return t.Failures;
    }

    private static (MinimapImage Image, TerrainRenderModel Model) Build(int size, MapData map)
    {
        var model = TerrainRenderModel.Build(map, SyntheticTerrain.Lookup(), SyntheticTerrain.Options());
        return (MinimapImage.Build(model, new TerrainHeightSampler(map.HeightField), TerrainMap.Build(map, "synthetic"), Water, size), model);
    }

    private static void Size()
    {
        Assert(MinimapImage.SizeFor(new Vector2(4000, 2000), 128) == (128, 64), "wide map");
        Assert(MinimapImage.SizeFor(new Vector2(1000, 3000), 120) == (40, 120), "tall map");
        Assert(MinimapImage.SizeFor(new Vector2(5000, 10), 64) == (64, 1), "at least one pixel");
        Throws<ArgumentOutOfRangeException>(() => MinimapImage.SizeFor(new Vector2(10, 10), 0), "size must be positive");
    }

    private static void Mapping()
    {
        var (image, _) = Build(32, SyntheticTerrain.Map(81, 41, 0, (_, _) => 0));
        Assert(image.Width == 32 && image.Height == 16 && image.WorldExtent == new Vector2(800, 400), "800x400 world in 32x16 pixels");
        Assert(image.WorldToPixel(new Vector2(0, 400)) == Vector2.Zero && image.WorldToPixel(new Vector2(800, 0)) == new Vector2(32, 16), "north-west is pixel (0, 0)");
        Assert(image.PixelToWorld(new Vector2(16, 8)) == new Vector2(400, 200), "centre");
        var world = new Vector2(123.5f, 321.25f);
        Assert(Vector2.Distance(image.PixelToWorld(image.WorldToPixel(world)), world) < 1e-3f, "round trip");
    }

    private static void Shading()
    {
        var gray = new Vector3(0.5f);
        Assert(MinimapImage.ShadeForHeight(gray, 10, 20, 10, 0) == gray, "at the middle height unchanged");
        Assert(Vector3.Distance(MinimapImage.ShadeForHeight(gray, 20, 20, 10, 0), new Vector3(0.975f)) < 1e-5f, "top: 95 % towards white");
        Assert(Vector3.Distance(MinimapImage.ShadeForHeight(gray, 0, 20, 10, 0), new Vector3(0.2f)) < 1e-5f, "bottom: 60 % towards black");
        Assert(Vector3.Distance(MinimapImage.ShadeForHeight(gray, 5, 20, 10, 0), new Vector3(0.35f)) < 1e-5f, "halfway down");
    }

    private static void TerrainLayer()
    {
        var lake = new MapPolygonTrigger("Lake", "", 1, true, false, 0, [new(0, 0, 20), new(200, 0, 20), new(200, 400, 20), new(0, 400, 20)]);
        var (image, model) = Build(40, SyntheticTerrain.Map(81, 41, 0, (_, _) => 0, triggers: [lake]));
        Pixel(image.TerrainRgba.AsSpan(), image.Width, 2, 8, out var water);
        // Ground 0 under a water level of 20 with the map minimum 0: interpolateColorForHeight darkens the radar water colour fully (60 %).
        Assert(water == new Rgba32(56, 56, 102, 255), $"lake pixel is the darkened radar water colour ({water})");
        Pixel(image.TerrainRgba.AsSpan(), image.Width, 30, 8, out var land);
        // Flat land at the map's highest (and only) height: interpolateColorForHeight leaves the tile colour unchanged.
        Assert(land == model.CellColor(60, 20) && land == SyntheticTerrain.TileColor(0, 0), $"land pixel is the tile average ({land})");
    }

    private static void Compose()
    {
        var map = SyntheticTerrain.Map(81, 41, 0, (_, _) => 0);
        var (image, _) = Build(40, map);
        var fog = new FogOfWarGrid(80, 40, VisionTests.Settings);
        fog.SetStates(Enumerable.Range(0, 80 * 40).Select(index => (index % 80) < 40 ? FogCellState.Visible : FogCellState.Unexplored).ToArray());
        fog.Snap();
        var red = new Rgba32(255, 0, 0, 255);
        var rgba = image.Compose(fog, [new MinimapDot(new Vector2(100, 300), red)], null);
        Pixel(rgba, image.Width, 30, 8, out var shrouded);
        Assert(shrouded == new Rgba32(0, 0, 0, 255), "unexplored is black");
        Pixel(rgba, image.Width, 10, 16, out var clear);
        Assert(clear == SyntheticTerrain.TileColor(0, 0), "visible keeps the terrain");
        // World (100, 300) is pixel (5, 5); a 2-pixel dot covers pixels 4..5 in both axes.
        Pixel(rgba, image.Width, 4, 4, out var dot1);
        Pixel(rgba, image.Width, 5, 5, out var dot2);
        Pixel(rgba, image.Width, 6, 6, out var outside);
        Assert(dot1 == red && dot2 == red && outside != red, "dot square");
        var camera = new RtsCamera(CameraTests.Settings, new TerrainHeightSampler(map.HeightField), new Vector2(1024, 768));
        var framed = image.Compose(null, [], camera.GroundFootprint());
        var white = 0;
        for (var i = 0; i < framed.Length; i += 4)
            if (framed[i] == 255 && framed[i + 1] == 255 && framed[i + 2] == 255) white++;
        Assert(white > 20, $"footprint outline drawn ({white} pixels)");
        Assert(image.TerrainRgba.SequenceEqual(image.Compose(null, [], null)), "composition without overlays is the terrain layer");
    }

    private static void Pixel(ReadOnlySpan<byte> rgba, int width, int x, int y, out Rgba32 color)
    {
        var offset = (y * width + x) * 4;
        color = new Rgba32(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
    }
}
