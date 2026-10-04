using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Presentation.Terrain;
using CnCNew.W3D;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>Roads of the real runtime content; runs only with <c>--content-dir &lt;dir&gt;</c> (<c>PresentationTool road-audit</c> prints the same numbers).</summary>
internal static class RoadRealDataTests
{
    /// <summary>A road vertex further than this from the box of the map's road points is a layout error (a blown-up miter or intersection).</summary>
    private const float MaxOverhang = 300f;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP Road real-data tests (pass --content-dir <dir>)");
            return 0;
        }
        var content = ContentFileSystem.Open(args[index + 1]);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var catalog = RoadCatalog.FromDefinitions(graph);
        var lighting = TerrainLighting.FromDefinitions(graph);
        var maps = content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

        var t = new TestHarness("RoadRealData");
        t.Run("Roads.ini templates and GameData budget", () => Templates(catalog));
        t.Run("every map's roads build and every used texture resolves", () => AllMaps(content, maps, catalog, lighting));
        t.Run("a real map builds the same roads twice", () => Deterministic(content, catalog, lighting));
        return t.Failures;
    }

    private static void Templates(RoadCatalog catalog)
    {
        Assert(catalog.Count == 63, $"63 Road blocks, found {catalog.Count}");
        Assert(catalog.MaxSegments == 4000, $"MaxRoadSegments {catalog.MaxSegments}");
        Assert(catalog.TryGet("TwoLane", out var twoLane) && twoLane is { Id: 1, Texture: "TRTwoLane.tga", Width: 35f, WidthInTexture: 0.9f }, "TwoLane is the first block of the INI");
        Assert(catalog.TryGet("GrassStrip", out var grass) && grass is { Width: 8f, Texture: "TRGrassStrip.tga" }, "an integer RoadWidth reads as a real");
        Assert(catalog.Templates.Select(template => template.Id).SequenceEqual(Enumerable.Range(1, catalog.Count)), "ids are 1..n in declaration order");
    }

    private static (RoadModel Model, Vector2[] Points) Build(ContentFileSystem content, string path, RoadCatalog catalog, TerrainLighting lighting)
    {
        using var stream = content.OpenRead(path);
        var map = SageMapDecoder.Decode(stream);
        var model = RoadModel.Build(map.Objects, new TerrainHeightSampler(map.HeightField), catalog, lighting);
        return (model, map.Objects.Where(o => (o.Flags & (MapObject.FlagRoadPoint1 | MapObject.FlagRoadPoint2)) != 0).Select(o => new Vector2((float)o.X, (float)o.Y)).ToArray());
    }

    private static void AllMaps(ContentFileSystem content, string[] maps, RoadCatalog catalog, TerrainLighting lighting)
    {
        long pairs = 0, segments = 0, triangles = 0, unknown = 0, dropped = 0;
        var withRoads = 0;
        var texturesChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in maps)
        {
            var (model, points) = Build(content, path, catalog, lighting);
            pairs += model.Stats.RoadPointPairs;
            segments += model.Stats.Segments;
            triangles += model.Stats.Triangles;
            unknown += model.Stats.UnknownTemplateSegments;
            dropped += model.Stats.DroppedBySegmentBudget;
            if (model.Stats.RoadPointPairs > 0) withRoads++;
            Assert(model.Stats.UnknownTemplates.Keys.All(name => name.Length == 0), $"{path}: unknown road templates {string.Join(", ", model.Stats.UnknownTemplates.Keys)}");
            if (points.Length == 0)
            {
                Assert(model.Meshes.IsEmpty, $"{path}: no road points, no meshes");
                continue;
            }
            var min = points.Aggregate(Vector2.Min) - new Vector2(MaxOverhang);
            var max = points.Aggregate(Vector2.Max) + new Vector2(MaxOverhang);
            foreach (var mesh in model.Meshes)
            {
                Assert(mesh.Positions.Length > 0 && mesh.Uv.Length == mesh.Positions.Length && mesh.Colors.Length == mesh.Positions.Length && mesh.Indices.Length % 3 == 0, $"{path}: {mesh.Template} arrays");
                Assert(mesh.Indices.All(i => i >= 0 && i < mesh.Positions.Length), $"{path}: {mesh.Template} index range");
                Assert(mesh.Positions.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y),
                    $"{path}: {mesh.Template} has a non-finite or far-off vertex");
                Assert(mesh.Uv.All(uv => float.IsFinite(uv.X) && float.IsFinite(uv.Y)), $"{path}: {mesh.Template} uv");
                if (texturesChecked.Add(mesh.Texture))
                    Assert(W3dTexturePathResolver.Resolve(mesh.Texture, content.Exists).Found, $"road texture {mesh.Texture} ({mesh.Template}, {path}) is not in the content");
            }
        }
        Assert(withRoads == 130, $"130 of 150 maps have roads, found {withRoads}");
        Assert(pairs == 40_960 && segments == 40_801, $"road point pairs {pairs} and segments {segments}");
        Assert(unknown == 3, $"three segments name no template (USA02), found {unknown}");
        Assert(dropped == 0, $"no map reaches MaxRoadSegments, {dropped} dropped");
        Assert(triangles == 206_956, $"golden triangle count {triangles}");
    }

    private static void Deterministic(ContentFileSystem content, RoadCatalog catalog, TerrainLighting lighting)
    {
        var first = Build(content, "Maps/Alpine Assault/Alpine Assault.map", catalog, lighting).Model;
        var second = Build(content, "Maps/Alpine Assault/Alpine Assault.map", catalog, lighting).Model;
        Assert(first.Meshes.Length == 6 && first.Stats is { Segments: 313, DuplicateSegments: 80 }, $"Alpine Assault {first.Stats}");
        for (var i = 0; i < first.Meshes.Length; i++)
            Assert(first.Meshes[i].Positions.SequenceEqual(second.Meshes[i].Positions) && first.Meshes[i].Uv.SequenceEqual(second.Meshes[i].Uv)
                && first.Meshes[i].Indices.SequenceEqual(second.Meshes[i].Indices), $"mesh {first.Meshes[i].Template} identical");
    }

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
