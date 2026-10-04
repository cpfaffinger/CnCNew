using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Formats;
using CnCNew.Presentation.Terrain;
using CnCNew.W3D;

namespace CnCNew.PresentationTool;

/// <summary>
/// <c>road-audit</c>: lays out and triangulates the roads of every map (or one) of a runtime content directory and prints deterministic counts: road point pairs,
/// segments, curve/tee/crossing/Y/H/join pieces, templates per map, unknown templates and road textures that do not resolve. Exit code 1 when a map fails, a vertex
/// is not finite or lies far outside the map, a template is unknown, or a used road texture is missing.
/// </summary>
internal static class RoadAudit
{
    /// <summary>
    /// A road vertex further than this outside the box of the map's road points is a layout error (the largest legitimate overhang is a curve or tee arm, a few road
    /// widths; some retail roads run off the map, which the road points themselves show).
    /// </summary>
    private const float MaxOverhang = 300f;

    public static int Run(string[] args)
    {
        if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--map"))
        {
            Console.Error.WriteLine("Usage: CnCNew.PresentationTool road-audit <content-dir> [--map <virtual map path>]");
            return 2;
        }
        var watch = Stopwatch.StartNew();
        var content = ContentFileSystem.Open(args[0]);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var catalog = RoadCatalog.FromDefinitions(graph);
        var lighting = TerrainLighting.FromDefinitions(graph);
        var paths = args.Length == 3
            ? [args[2]]
            : content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

        Console.WriteLine($"road templates {catalog.Count}, segment budget {catalog.MaxSegments}");
        var failures = new List<string>();
        var rows = new List<(string Path, RoadStats Stats, RoadModel Model)>();
        foreach (var path in paths)
        {
            try
            {
                using var stream = content.OpenRead(path);
                var map = SageMapDecoder.Decode(stream);
                var heights = new TerrainHeightSampler(map.HeightField);
                var model = RoadModel.Build(map.Objects, heights, catalog, lighting);
                var points = map.Objects.Where(mapObject => (mapObject.Flags & (Data.World.MapObject.FlagRoadPoint1 | Data.World.MapObject.FlagRoadPoint2)) != 0).ToList();
                var minX = points.Count == 0 ? 0f : (float)points.Min(point => point.X) - MaxOverhang;
                var minY = points.Count == 0 ? 0f : (float)points.Min(point => point.Y) - MaxOverhang;
                var maxX = points.Count == 0 ? 0f : (float)points.Max(point => point.X) + MaxOverhang;
                var maxY = points.Count == 0 ? 0f : (float)points.Max(point => point.Y) + MaxOverhang;
                foreach (var mesh in model.Meshes)
                    foreach (var position in mesh.Positions)
                    {
                        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                            failures.Add($"{path}: {mesh.Template} has a non-finite vertex");
                        else if (position.X < minX || position.Y < minY || position.X > maxX || position.Y > maxY)
                            failures.Add($"{path}: {mesh.Template} has a vertex far outside its road points at {FormattableString.Invariant($"{position}")}");
                    }
                rows.Add((path, model.Stats, model));
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
            {
                failures.Add($"{path}: {exception.Message}");
            }
        }

        string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        var withRoads = rows.Where(row => row.Stats.RoadPointPairs > 0).ToList();
        foreach (var (path, stats, model) in withRoads)
            Console.WriteLine($"  {path}: pairs {stats.RoadPointPairs}, segments {stats.Segments}, pieces {stats.Pieces} ({string.Join(", ", stats.PiecesByType.Select(pair => $"{pair.Key} {pair.Value}"))}), " +
                              $"templates {model.Meshes.Length}, triangles {N(stats.Triangles)}, mitres {stats.Mitres}, aborted curves {stats.CurvesAborted}, duplicates {stats.DuplicateSegments}, dropped {stats.DroppedBySegmentBudget}");

        if (args.Length == 3)
        {
            foreach (var junction in rows.SelectMany(row => row.Stats.Junctions))
                Console.WriteLine(FormattableString.Invariant($"  {junction.Piece} {junction.Template} at {junction.Location.X:F0},{junction.Location.Y:F0}"));
            foreach (var mesh in rows.SelectMany(row => row.Model.Meshes))
                Console.WriteLine(FormattableString.Invariant($"  mesh {mesh.Template} ({mesh.Texture}): stacking {mesh.Stacking}, {mesh.TriangleCount} triangles, bounds {mesh.Bounds.Min.X:F0},{mesh.Bounds.Min.Y:F0}..{mesh.Bounds.Max.X:F0},{mesh.Bounds.Max.Y:F0}, centre {mesh.Bounds.Center.X:F0},{mesh.Bounds.Center.Y:F0}"));
        }
        Console.WriteLine($"maps: {paths.Length}, built {rows.Count}, failed {paths.Length - rows.Count}, with roads {withRoads.Count}");
        long Sum(Func<RoadStats, int> selector) => rows.Sum(row => (long)selector(row.Stats));
        Console.WriteLine($"totals: road point pairs {N(Sum(s => s.RoadPointPairs))}, segments {N(Sum(s => s.Segments))}, pieces {N(Sum(s => s.Pieces))}, triangles {N(Sum(s => s.Triangles))}");
        Console.WriteLine($"  corners with curves {N(Sum(s => s.Curves))}, mitred corners {N(Sum(s => s.Mitres))} (curve attempts aborted {N(Sum(s => s.CurvesAborted))}), tees {N(Sum(s => s.Tees))}, " +
                          $"slanted (H) tees {N(Sum(s => s.SlantedTees))}, Y junctions {N(Sum(s => s.YJunctions))}, four-way crossings {N(Sum(s => s.FourWays))}");
        Console.WriteLine($"  alpha joins {N(Sum(s => s.AlphaJoins))} (against another road type {N(Sum(s => s.CrossTypeJoins))}), duplicate segments dropped {N(Sum(s => s.DuplicateSegments))}, " +
                          $"dropped by segment budget {N(Sum(s => s.DroppedBySegmentBudget))}, unpaired road points {N(Sum(s => s.UnpairedRoadPoints))}, unknown template segments {N(Sum(s => s.UnknownTemplateSegments))}");
        var pieces = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var (type, count) in row.Stats.PiecesByType) pieces[type] = pieces.GetValueOrDefault(type) + count;
        Console.WriteLine($"  pieces by type: {string.Join(", ", pieces.Select(pair => $"{pair.Key} {N(pair.Value)}"))}");

        var usage = new SortedDictionary<string, (int Segments, int Maps)>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var (name, count) in row.Stats.SegmentsByTemplate)
            {
                var (segments, maps) = usage.GetValueOrDefault(name);
                usage[name] = (segments + count, maps + 1);
            }
        Console.WriteLine($"templates used: {usage.Count} of {catalog.Count}");
        foreach (var (name, (segments, maps)) in usage)
            Console.WriteLine($"  {name}: {segments} segments on {maps} maps ({(catalog.TryGet(name, out var template) ? $"{template.Texture}, width {template.Width}, in texture {template.WidthInTexture}" : "?")})");

        var unknown = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (path, stats, _) in rows)
            foreach (var name in stats.UnknownTemplates.Keys)
            {
                if (!unknown.TryGetValue(name, out var maps)) unknown[name] = maps = new SortedSet<string>(StringComparer.Ordinal);
                maps.Add(path);
            }
        Console.WriteLine($"unknown road templates named by map objects: {unknown.Count}");
        var unexpected = 0;
        foreach (var (name, maps) in unknown)
        {
            // Retail data: three segments of Maps/USA02/USA02.map name no template at all; the reference would draw them as the first road type at width 8, the
            // port skips them and counts them here.
            var known = name.Length == 0;
            if (!known) unexpected++;
            Console.WriteLine($"  '{name}': {rows.Sum(row => row.Stats.UnknownTemplates.GetValueOrDefault(name))} segments on {maps.Count} maps, first {maps.First()}{(known ? " [known retail data defect]" : " [UNEXPECTED]")}");
        }

        var missing = 0;
        foreach (var template in catalog.Templates)
        {
            var resolution = W3dTexturePathResolver.Resolve(template.Texture, content.Exists);
            if (resolution.Found) continue;
            var used = usage.ContainsKey(template.Name);
            if (used) missing++;
            Console.WriteLine($"  {template.Name}: texture {template.Texture} NOT FOUND ({(used ? "used by a map [UNEXPECTED]" : "not used by any map")})");
        }
        Console.WriteLine($"road textures of used templates missing: {missing}");
        foreach (var failure in failures.Distinct()) Console.WriteLine($"FAIL {failure}");
        Console.WriteLine($"build time total (not deterministic): {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");
        return failures.Count == 0 && unexpected == 0 && missing == 0 && rows.Count == paths.Length ? 0 : 1;
    }

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
