using System.Globalization;
using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Terrain;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>Checks against the real runtime content; runs only with <c>--content-dir &lt;dir&gt;</c>.</summary>
internal static class WorldRealDataTests
{
    private static readonly string[] SampleMaps = ["Maps/Alpine Assault/Alpine Assault.map", "Maps/USA01/USA01.map", "Maps/Red Rock/Red Rock.map"];

    /// <summary>Terrain types that shipped maps name but no retail INI defines (see <c>PresentationTool terrain-audit</c>).</summary>
    private static readonly HashSet<string> KnownUndefinedTerrainTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CliffLargeType5", "CliffMediumType6", "CliffMediumType7", "DirtMediumType6", "RocksType5b",
    };

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP World real-data tests (pass --content-dir <dir>)");
            return 0;
        }
        var content = ContentFileSystem.Open(args[index + 1]);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var options = TerrainRenderOptions.FromDefinitions(graph);
        var lookup = new TerrainTextureLookup(TerrainTextureCatalog.FromDefinitions(graph), path => content.Exists(path) ? Read(content, path) : null);
        var maps = content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

        var t = new TestHarness("WorldRealData");
        t.Run("GameData-derived settings", () => Settings(graph, options));
        foreach (var sample in SampleMaps)
            t.Run($"sample map {sample}", () => SampleMap(content, maps, sample, lookup, options, graph));
        t.Run("every map builds", () => AllMaps(content, maps, lookup, options));
        return t.Failures;
    }

    private static void Settings(CompiledDefinitionGraph graph, TerrainRenderOptions options)
    {
        var camera = RtsCameraSettings.FromDefinitions(graph);
        Assert(camera is { PitchDegrees: 37.5f, YawDegrees: 0f, CameraHeight: 232f, MaxHeightAboveGround: 310f, MinHeightAboveGround: 120f }, $"camera {camera}");
        Assert(camera is { HorizontalScrollSpeedFactor: 1.6f, VerticalScrollSpeedFactor: 2f, MaxShakeRange: 150f }, "scroll and shake");
        Assert(CameraShakeIntensities.FromDefinitions(graph).For("strong") == 2.5f, "shake intensities");
        Assert(options.Lighting.TimeOfDay == "AFTERNOON" && options.Lighting.Lights.Length == 3, "afternoon lighting with three lights");
        Assert(options.Lighting.Lights[0].Position == new Vector3(-0.81f, 0.38f, -0.45f), "first light position");
        Assert(options.AdjustCliffTextures && options.Use3WayBlends, "cliff adjustment and 3-way blends on");
        var fog = FogSettings.FromDefinitions(graph, 510f);
        Assert(fog is { ShroudLevel: 0, FogLevel: 127, ClearLevel: 255 }, "shroud levels");
        Assert(options.Water is { StandingWaterTexture: "TWWater01.tga", RadarColor.B: 255, SurfaceColor.A: 255 }, $"water {options.Water}");
    }

    private static void SampleMap(ContentFileSystem content, string[] maps, string sample, TerrainTextureLookup lookup, TerrainRenderOptions options, CompiledDefinitionGraph graph)
    {
        var path = maps.Single(candidate => candidate.Equals(sample, StringComparison.OrdinalIgnoreCase));
        MapData map;
        using (var stream = content.OpenRead(path)) map = SageMapDecoder.Decode(stream);
        var model = TerrainRenderModel.Build(map, lookup, options, path);
        Assert(model.TextureIssues.All(issue => KnownUndefinedTerrainTypes.Contains(issue.ClassName)), $"unexpected texture issues: {string.Join("; ", model.TextureIssues)}");
        Assert(model.Stats.BaseTriangles == model.CellsX * model.CellsY * 2 && model.Stats.BlendCells > 0, "one base quad per cell and some blends");
        Assert(model.Atlas.Height <= 4096 && model.Stats.TotalGpuBytes < 300L * 1024 * 1024, "atlas and GPU budget");

        var terrain = TerrainMap.Build(map, path);
        var sampler = new TerrainHeightSampler(map.HeightField);
        var worst = 0f;
        for (var cy = 0; cy <= terrain.CellsY; cy += 3)
            for (var cx = 0; cx <= terrain.CellsX; cx += 3)
            {
                var x = cx * 10 + 3.75m;
                var y = cy * 10 + 6.25m;
                var expected = (float)terrain.HeightAt(Fixed.FromDecimal(x), Fixed.FromDecimal(y)).ToDecimal();
                worst = Math.Max(worst, Math.Abs(expected - sampler.HeightAt((float)x, (float)y)));
            }
        Assert(worst < 0.01f, $"sampler deviates {worst} from TerrainMap.HeightAt");

        var camera = new RtsCamera(RtsCameraSettings.FromDefinitions(graph), sampler, new Vector2(1280, 720));
        var start = terrain.StartPositions.FirstOrDefault();
        if (start is not null) camera.InitializeForMap(new Vector2((float)start.X.ToDecimal(), (float)start.Y.ToDecimal()));
        var hits = 0;
        foreach (var screen in new[] { new Vector2(640, 360), new Vector2(100, 600), new Vector2(1200, 200), new Vector2(640, 700) })
        {
            if (!camera.TryScreenToGround(screen, out var world)) continue;
            hits++;
            Assert(camera.TryWorldToScreen(world, out var back) && Vector2.Distance(back, screen) < 0.5f, $"round trip {screen} -> {world} -> {back}");
        }
        Assert(hits >= 3, $"picking hits the terrain ({hits})");
        Assert(camera.VisibleChunks(model).Count is > 0 and var visible && visible < model.Chunks.Length, "culling keeps a part of the chunks");

        var minimap = MinimapImage.Build(model, sampler, terrain, options.Water!.RadarColor, 128);
        Assert(Math.Max(minimap.Width, minimap.Height) == 128 && minimap.TerrainRgba.Length == minimap.Width * minimap.Height * 4, "minimap size");
        var vision = new VisionMap(terrain.CellsX, terrain.CellsY, 1);
        vision.Reveal(0, Fixed.FromInt(terrain.CellsX * 5), Fixed.FromInt(terrain.CellsY * 5), Fixed.FromInt(300));
        var fog = FogOfWarGrid.ForVision(vision, FogSettings.FromDefinitions(graph, 510f));
        fog.Update(vision, 0);
        fog.Snap();
        Assert(fog.IsVisibleAt(new Vector2(terrain.CellsX * 5, terrain.CellsY * 5)) && !fog.IsExploredAt(new Vector2(5, 5)), "fog from the vision map");
        var composed = minimap.Compose(fog, [new MinimapDot(new Vector2(terrain.CellsX * 5, terrain.CellsY * 5), new Rgba32(255, 0, 0, 255))], camera.GroundFootprint());
        Assert(composed.Length == minimap.TerrainRgba.Length && composed.Take(4).SequenceEqual(new byte[] { 0, 0, 0, 255 }), "unexplored corner is black");
        Console.WriteLine($"  {path}: {model.CellsX}x{model.CellsY} cells, atlas {model.Atlas.Width}x{model.Atlas.Height}, GPU {model.Stats.TotalGpuBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes, " +
                          $"sampler deviation {worst.ToString("F4", CultureInfo.InvariantCulture)}, picks {hits}");
    }

    private static void AllMaps(ContentFileSystem content, string[] maps, TerrainTextureLookup lookup, TerrainRenderOptions options)
    {
        Assert(maps.Length == 150, $"150 retail maps, found {maps.Length}");
        long maxBytes = 0;
        foreach (var path in maps)
        {
            MapData map;
            using (var stream = content.OpenRead(path)) map = SageMapDecoder.Decode(stream);
            var model = TerrainRenderModel.Build(map, lookup, options, path);
            Assert(model.TextureIssues.All(issue => KnownUndefinedTerrainTypes.Contains(issue.ClassName) && issue.Status == TerrainTextureStatus.UnknownTerrainType),
                $"{path}: {string.Join("; ", model.TextureIssues)}");
            Assert(model.Stats.InvalidTileReferences == 0, $"{path}: invalid tile references");
            maxBytes = Math.Max(maxBytes, model.Stats.TotalGpuBytes);
        }
        Assert(maxBytes < 300L * 1024 * 1024, $"largest map needs {maxBytes:N0} GPU bytes");
        Console.WriteLine($"  {maps.Length} maps built, largest estimated GPU footprint {maxBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes");
    }

    private static byte[] Read(ContentFileSystem content, string path)
    {
        using var stream = content.OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
