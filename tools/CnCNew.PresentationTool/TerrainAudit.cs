using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Formats;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;

namespace CnCNew.PresentationTool;

/// <summary>
/// <c>terrain-audit</c>: builds the terrain render data of every map (or one) of a runtime content directory and prints deterministic counts,
/// the largest map per metric, every unresolved terrain texture and the height-sampler deviation from the simulation. Exit code 1 when a
/// map fails to build, a texture is unresolved or the sampler disagrees with the simulation.
/// </summary>
internal static class TerrainAudit
{
    private const float HeightTolerance = 0.01f;

    /// <summary>
    /// Terrain types that shipped maps name but no retail INI defines (<c>Terrain.ini</c> has only variants such as <c>CliffMediumType6b</c>).
    /// The reference renders their cells with zero UVs, and so does the render model; any other unresolved texture fails the audit.
    /// </summary>
    private static readonly HashSet<string> KnownUndefinedTerrainTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CliffLargeType5", "CliffMediumType6", "CliffMediumType7", "DirtMediumType6", "RocksType5b",
    };

    public static int Run(string[] args)
    {
        if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--map"))
        {
            Console.Error.WriteLine("Usage: CnCNew.PresentationTool terrain-audit <content-dir> [--map <virtual map path>]");
            return 2;
        }
        var watch = Stopwatch.StartNew();
        var content = ContentFileSystem.Open(args[0]);
        var graph = CompileRetail(content);
        var options = TerrainRenderOptions.FromDefinitions(graph);
        var catalog = TerrainTextureCatalog.FromDefinitions(graph);
        var lookup = new TerrainTextureLookup(catalog, path => content.Exists(path) ? ReadAll(content, path) : null);
        var paths = args.Length == 3
            ? [args[2]]
            : content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

        Console.WriteLine($"terrain types {catalog.Count}, time of day {options.Lighting.TimeOfDay}, lights {options.Lighting.Lights.Length}, " +
                          $"adjust cliff textures {options.AdjustCliffTextures}, 3-way blends {options.Use3WayBlends}, water texture {options.Water!.StandingWaterTexture}");
        var failures = new List<string>();
        var unresolved = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var rows = new List<(string Path, TerrainRenderStats Stats, TerrainRenderModel Model, float MaxDeviation)>();
        foreach (var path in paths)
        {
            try
            {
                using var stream = content.OpenRead(path);
                var map = SageMapDecoder.Decode(stream);
                var model = TerrainRenderModel.Build(map, lookup, options, path);
                foreach (var issue in model.TextureIssues)
                {
                    var key = $"{issue.ClassName} ({issue.Status}: {issue.Reason})";
                    if (!unresolved.TryGetValue(key, out var maps)) unresolved[key] = maps = new SortedSet<string>(StringComparer.Ordinal);
                    maps.Add(path);
                }
                var deviation = MaxHeightDeviation(map, path);
                if (deviation > HeightTolerance) failures.Add($"{path}: height sampler deviates {deviation:F4} from the simulation");
                rows.Add((path, model.Stats, model, deviation));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                failures.Add($"{path}: {exception.Message}");
            }
        }

        string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1", CultureInfo.InvariantCulture);
        foreach (var (path, stats, model, deviation) in rows)
            Console.WriteLine($"  {path}: {model.CellsX}x{model.CellsY} cells, {stats.Chunks} chunks, {N(stats.BaseVertices + stats.BlendVertices + stats.ExtraBlendVertices)} vertices, " +
                              $"{N(stats.BaseTriangles + stats.BlendTriangles + stats.ExtraBlendTriangles)} triangles, atlas {model.Atlas.Width}x{model.Atlas.Height} " +
                              $"({stats.TextureClasses} classes), blend {N(stats.BlendCells)}, 3-way {N(stats.ExtraBlendCells)}, cliff {N(stats.CliffCells)} " +
                              $"(mapped {N(stats.CliffMappedCells)}, stretched {N(stats.StretchedCells)}), water {stats.WaterSurfaces}, GPU {Mb(stats.TotalGpuBytes)} MB, " +
                              $"max height deviation {deviation.ToString("F4", CultureInfo.InvariantCulture)}");

        Console.WriteLine($"maps: {paths.Length}, built {rows.Count}, failed {paths.Length - rows.Count}");
        if (rows.Count > 0)
        {
            long Sum(Func<TerrainRenderStats, long> selector) => rows.Sum(row => selector(row.Stats));
            Console.WriteLine($"totals: chunks {N(Sum(s => s.Chunks))}, cells {N(Sum(s => s.Cells))}, vertices {N(Sum(s => s.BaseVertices + s.BlendVertices + s.ExtraBlendVertices))}, " +
                              $"triangles {N(Sum(s => s.BaseTriangles + s.BlendTriangles + s.ExtraBlendTriangles))}");
            Console.WriteLine($"  base triangles {N(Sum(s => s.BaseTriangles))}, blend cells {N(Sum(s => s.BlendCells))}, 3-way blend cells {N(Sum(s => s.ExtraBlendCells))}, " +
                              $"custom-edge blend cells (not drawn) {N(Sum(s => s.CustomEdgeBlendCells))}, flipped cells {N(Sum(s => s.FlippedCells))}");
            Console.WriteLine($"  cliff cells {N(Sum(s => s.CliffCells))}, cliff-mapped cells {N(Sum(s => s.CliffMappedCells))}, stretched cells {N(Sum(s => s.StretchedCells))}, " +
                              $"missing-texture cells {N(Sum(s => s.MissingTextureCells))}, invalid tile references {N(Sum(s => s.InvalidTileReferences))}");
            Console.WriteLine($"  water surfaces {N(Sum(s => s.WaterSurfaces))} (rivers {N(rows.Sum(row => row.Model.Water.Count(water => water.IsRiver)))}), water triangles {N(Sum(s => s.WaterTriangles))}, " +
                              $"mirrored texture uses {N(Sum(s => s.MirroredTextures))}, textures with TGA id field {N(Sum(s => s.IdFieldTextures))}");
            Console.WriteLine($"  GPU bytes: meshes {Mb(Sum(s => s.MeshBytes))} MB, atlases with mips {Mb(Sum(s => s.AtlasBytes))} MB, water {Mb(Sum(s => s.WaterBytes))} MB");
            void Max(string label, Func<TerrainRenderStats, TerrainRenderModel, long> selector, Func<long, string> format)
            {
                var best = rows.OrderByDescending(row => selector(row.Stats, row.Model)).ThenBy(row => row.Path, StringComparer.Ordinal).First();
                Console.WriteLine($"  max {label}: {format(selector(best.Stats, best.Model))} ({best.Path})");
            }
            Max("GPU bytes", (s, _) => s.TotalGpuBytes, value => $"{N(value)} ({Mb(value)} MB)");
            Max("mesh bytes", (s, _) => s.MeshBytes, value => $"{N(value)} ({Mb(value)} MB)");
            Max("atlas bytes with mips", (s, _) => s.AtlasBytes, value => $"{N(value)} ({Mb(value)} MB)");
            Max("atlas height", (_, m) => m.Atlas.Height, N);
            Max("texture classes", (s, _) => s.TextureClasses, N);
            Max("cells", (s, _) => s.Cells, N);
            Max("chunks", (s, _) => s.Chunks, N);
            Max("triangles", (s, _) => s.BaseTriangles + s.BlendTriangles + s.ExtraBlendTriangles, N);
            Max("water surfaces", (s, _) => s.WaterSurfaces, N);
            Console.WriteLine($"  max height deviation from simulation: {rows.Max(row => row.MaxDeviation).ToString("F4", CultureInfo.InvariantCulture)} (tolerance {HeightTolerance.ToString(CultureInfo.InvariantCulture)})");
        }
        var unexpected = 0;
        Console.WriteLine($"unresolved terrain textures: {unresolved.Count}");
        foreach (var (key, maps) in unresolved)
        {
            var known = KnownUndefinedTerrainTypes.Contains(key[..key.IndexOf(' ')]) && key.Contains(nameof(TerrainTextureStatus.UnknownTerrainType), StringComparison.Ordinal);
            if (!known) unexpected++;
            Console.WriteLine($"  {key}: {maps.Count} maps, first {maps.First()}{(known ? " [known retail data defect]" : " [UNEXPECTED]")}");
        }
        foreach (var failure in failures) Console.WriteLine($"FAIL {failure}");
        Console.WriteLine($"build time total (not deterministic): {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");
        return failures.Count == 0 && unexpected == 0 && rows.Count == paths.Length ? 0 : 1;
    }

    /// <summary>Largest difference between the float sampler and <c>TerrainMap.HeightAt</c> over every cell corner and centre of the playable area.</summary>
    private static float MaxHeightDeviation(Data.World.MapData map, string path)
    {
        var terrain = TerrainMap.Build(map, path);
        var sampler = new TerrainHeightSampler(map.HeightField);
        var worst = 0f;
        for (var cy = 0; cy <= terrain.CellsY; cy++)
            for (var cx = 0; cx <= terrain.CellsX; cx++)
                for (var sub = 0; sub < 2; sub++)
                {
                    var x = cx * 10 + sub * 5 + sub * 0.25m;
                    var y = cy * 10 + sub * 5 - sub * 0.25m;
                    var expected = (float)terrain.HeightAt(Fixed.FromDecimal(x), Fixed.FromDecimal(y)).ToDecimal();
                    worst = Math.Max(worst, Math.Abs(expected - sampler.HeightAt((float)x, (float)y)));
                }
        return worst;
    }

    private static CompiledDefinitionGraph CompileRetail(ContentFileSystem content)
    {
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        return DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default, options);
    }

    private static byte[] ReadAll(ContentFileSystem content, string path)
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
