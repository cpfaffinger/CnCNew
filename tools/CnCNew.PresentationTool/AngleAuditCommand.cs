using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Formats;
using CnCNew.Presentation.Objects;
using CnCNew.W3D;

/// <summary>
/// <c>angle-audit &lt;content-dir&gt; [--maps N]</c>: measures whether the angle stored with a map object turns its model the way the level designers lined the objects up
/// (docs/DEMO-FINDINGS.md #5). Long thin models (fences, walls, rails) are placed in rows; for each such object the direction to the nearest object of the same
/// template is compared with the model's long axis turned by the stored angle. Four hypotheses are scored (angle as stored, negated, each plus a quarter turn): the one
/// that lines the rows up tells which convention the client has to use. A hypothesis that wins by a wide margin is evidence, not an impression.
/// </summary>
static class AngleAuditCommand
{
    private sealed record Elongated(string Template, string Model, bool LongAxisIsX, float Major, float Minor);

    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: CnCNew.PresentationTool angle-audit <content-dir> [--maps N]");
            return 2;
        }

        var limit = int.MaxValue;
        var index = Array.IndexOf(args, "--maps");
        if (index >= 0 && index + 1 < args.Length) limit = int.Parse(args[index + 1], CultureInfo.InvariantCulture);
        var content = ContentFileSystem.Open(args[1]);
        var graph = DefinitionCompiler.Compile(new AngleContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var visuals = ObjectVisualCatalog.Build(graph);
        var library = new W3dAssetLibrary(path => content.Exists(path) ? content.OpenRead(path) : null);
        var shapes = new Dictionary<string, Elongated?>(StringComparer.OrdinalIgnoreCase);
        var hypotheses = new[] { ("angle as stored", 1f, 0f), ("angle negated", -1f, 0f), ("stored + quarter turn", 1f, MathF.PI / 2), ("negated + quarter turn", -1f, MathF.PI / 2) };
        var perTemplate = new SortedDictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        long samples = 0;
        var wellAligned = new long[hypotheses.Length];

        var maps = content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).Take(limit).ToArray();
        foreach (var path in maps)
        {
            using var stream = content.OpenRead(path);
            var map = SageMapDecoder.Decode(stream);
            var groups = map.Objects.Where(value => !value.IsWaypoint).GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups)
            {
                if (!shapes.TryGetValue(group.Key, out var shape)) shapes[group.Key] = shape = ShapeOf(group.Key, visuals, library);
                if (shape is null) continue;
                var items = group.ToArray();
                if (items.Length < 2) continue;
                foreach (var item in items)
                {
                    var nearest = items.Where(other => !ReferenceEquals(other, item))
                        .Select(other => (Other: other, Distance: Math.Sqrt((double)((other.X - item.X) * (other.X - item.X) + (other.Y - item.Y) * (other.Y - item.Y)))))
                        .OrderBy(pair => pair.Distance).First();
                    if (nearest.Distance < 1 || nearest.Distance > shape.Major * 1.6) continue; // a neighbour in the same row, not just the next fence on the map
                    var rowDirection = (float)Math.Atan2((double)(nearest.Other.Y - item.Y), (double)(nearest.Other.X - item.X));
                    samples++;
                    if (!perTemplate.TryGetValue(group.Key, out var counts)) perTemplate[group.Key] = counts = new int[hypotheses.Length + 1];
                    counts[^1]++;
                    for (var h = 0; h < hypotheses.Length; h++)
                    {
                        var (_, sign, offset) = hypotheses[h];
                        var axis = sign * (float)item.Angle + offset + (shape.LongAxisIsX ? 0f : MathF.PI / 2);
                        if (AngleBetweenAxes(axis, rowDirection) < 5f * MathF.PI / 180f)
                        {
                            wellAligned[h]++;
                            counts[h]++;
                        }
                    }
                }
            }
        }

        Console.WriteLine($"maps scanned {maps.Length}; elongated templates {shapes.Values.Count(value => value is not null)} of {shapes.Count} placed templates; row samples {samples}");
        for (var h = 0; h < hypotheses.Length; h++)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {hypotheses[h].Item1,-26} rows lined up within 5 degrees: {wellAligned[h],7} ({(samples == 0 ? 0 : 100.0 * wellAligned[h] / samples):F1} %)"));
        Console.WriteLine("per template (samples, then the share of each hypothesis in the order above):");
        foreach (var (name, counts) in perTemplate.OrderByDescending(pair => pair.Value[^1]).Take(20))
        {
            var shares = string.Join("  ", Enumerable.Range(0, hypotheses.Length).Select(h => string.Create(CultureInfo.InvariantCulture, $"{100.0 * counts[h] / counts[^1],5:F1}%")));
            Console.WriteLine($"  {name,-34} {counts[^1],6}  {shares}");
        }
        return 0;
    }

    private static Elongated? ShapeOf(string template, ObjectVisualCatalog visuals, W3dAssetLibrary library)
    {
        if (!visuals.TryGet(template, out var visual)) return null;
        var modelName = visual.Draws.SelectMany(draw => draw.States).Select(state => state.Model).FirstOrDefault(model => model is not null);
        if (modelName is null) return null;
        try
        {
            var model = library.GetModel(modelName);
            var meshes = model.GetLod(model.LodCount - 1).Where(sub => sub.IsRenderable && sub.Mesh is not null).Select(sub => sub.Mesh!).ToArray();
            if (meshes.Length == 0) return null;
            var minimum = new System.Numerics.Vector3(float.MaxValue);
            var maximum = new System.Numerics.Vector3(float.MinValue);
            foreach (var mesh in meshes)
            {
                minimum = System.Numerics.Vector3.Min(minimum, mesh.BoundsMinimum);
                maximum = System.Numerics.Vector3.Max(maximum, mesh.BoundsMaximum);
            }

            var x = maximum.X - minimum.X;
            var y = maximum.Y - minimum.Y;
            var major = Math.Max(x, y);
            var minor = Math.Max(Math.Min(x, y), 0.01f);
            // Rows of this kind: long compared to wide, and long enough that its neighbour is next to it.
            return major / minor >= 3f && major >= 8f ? new Elongated(template, modelName, x >= y, major, minor) : null;
        }
        catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>The smaller angle between two undirected axes, in radians (0 to pi/2).</summary>
    private static float AngleBetweenAxes(float first, float second)
    {
        var difference = (first - second) % MathF.PI;
        if (difference < 0) difference += MathF.PI;
        return Math.Min(difference, MathF.PI - difference);
    }
}

file sealed class AngleContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
