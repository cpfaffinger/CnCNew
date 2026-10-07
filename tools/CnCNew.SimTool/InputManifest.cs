using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Formats;
using CnCNew.Game.Campaign;
using CnCNew.SimTool.Manifests;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// Everything a headless run needs from one content directory, built the way the client builds it (<c>GameData.Load</c>): the compiled retail base
/// definitions, the model bone table and the catalogs. One instance is used by one thread; the manifest workers each build their own, which also
/// proves that independent builds agree.
/// </summary>
internal sealed class SimContext
{
    private static readonly object CompileGate = new();

    private SimContext(ContentFileSystem content, TemplateCatalog catalog, string fingerprint, string definitionSha256, long definitionBytes, int definitions, ModelGeometry geometry)
    {
        Content = content;
        Catalog = catalog;
        Fingerprint = fingerprint;
        DefinitionSha256 = definitionSha256;
        DefinitionBytes = definitionBytes;
        Definitions = definitions;
        Geometry = geometry;
    }

    public ContentFileSystem Content { get; }
    public TemplateCatalog Catalog { get; }
    /// <summary>What a world, a replay and a save are checked against: the definition fingerprint combined with the bone table.</summary>
    public string Fingerprint { get; }
    public string DefinitionSha256 { get; }
    public long DefinitionBytes { get; }
    public int Definitions { get; }
    public ModelGeometry Geometry { get; }

    /// <summary>Compiles under a process-wide gate: the loaders of the client layer are not known to be safe to run side by side.</summary>
    /// <param name="rate">The rate the catalog is built for; the manifests' rate when absent (<c>ai-survey</c> passes its own).</param>
    public static SimContext Load(string directory, CnCNew.Simulation.Time.SimulationRate? rate = null)
    {
        lock (CompileGate)
        {
            var content = ContentFileSystem.Open(directory);
            var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
            var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
            var geometry = CnCNew.Game.Data.ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate ?? ToolRate.Manifest, geometry);
            var (sha256, length) = DefinitionSnapshotWriter.ComputeFingerprint(graph);
            return new SimContext(content, catalog, catalog.Rate.Combine(geometry.Combine(sha256)), sha256, length, graph.Audit.Definitions, geometry);
        }
    }

    public static CampaignCatalog LoadCampaigns(ContentFileSystem content)
    {
        using var stream = content.OpenRead(InputManifest.CampaignFile);
        var campaigns = CampaignCatalog.Parse(stream);
        if (!content.Exists(InputManifest.ChallengeFile)) return campaigns;
        using var personas = new StreamReader(content.OpenRead(InputManifest.ChallengeFile));
        return campaigns.WithPersonas(personas.ReadToEnd());
    }

    public static string[] MapPaths(ContentFileSystem content)
        => [..content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

    private sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}

/// <summary>
/// The input manifest (<c>baselines/input-manifest.json</c>): what goes <em>into</em> the simulation, hashed, so that two machines that disagree on a
/// run can first tell whether they were fed the same thing. It holds the definition fingerprint, the bone table, the combined world fingerprint, a
/// content hash of every built catalog, and per map the file hash of the content manifest and the terrain fingerprint.
/// </summary>
internal static class InputManifest
{
    public const string Kind = "input-manifest";
    public const int Schema = 1;
    public const string CampaignFile = "Data/INI/Campaign.ini";
    public const string ChallengeFile = "Data/INI/ChallengeMode.ini";

    public static int Command(string[] args)
    {
        if (args.Length < 4 || args[1] is not ("write" or "check")) return ManifestCommands.Fail("Usage: CnCNew.SimTool input-manifest write|check <content-dir> <file> [--jobs N]");
        var jobs = ManifestCommands.Jobs(args);
        var watch = Stopwatch.StartNew();
        var actual = Build(args[2], jobs, Console.Error);
        Console.Error.WriteLine($"Built the input manifest in {watch.Elapsed.TotalSeconds:F1} s.");
        var path = Path.GetFullPath(args[3]);
        if (args[1] == "write")
        {
            File.WriteAllBytes(path, ManifestDocument.WriteBytes(actual));
            Console.WriteLine($"Input manifest written: {actual.Object("maps").Values.Count} maps, {actual.Object("catalogs").Values.Count} catalog hashes, world fingerprint {actual.Text("worldFingerprint")[..16]}.");
            return 0;
        }

        var expected = ManifestDocument.Read(File.ReadAllBytes(path));
        var differing = 0;
        foreach (var section in Sections(expected, actual))
        {
            // Per-item sections (catalogs, files, maps) are reported item by item so that one changed map does not hide the next.
            var items = section is "catalogs" or "files" or "maps" ? Items(expected, actual, section) : [string.Empty];
            var bad = 0;
            foreach (var item in items)
            {
                var difference = ManifestDocument.FirstDifference(Pick(expected, section, item), Pick(actual, section, item), item.Length == 0 ? section : section + "." + item);
                if (difference is null) continue;
                bad++;
                Console.WriteLine($"{(item.Length == 0 ? section : section + " " + item)}: DIFFERENT {difference}");
            }
            if (bad == 0) Console.WriteLine($"{section}: OK{(items.Length > 1 ? $" ({items.Length} entries)" : string.Empty)}");
            differing += bad;
        }
        Console.WriteLine(differing == 0 ? "Input manifest: identical." : $"Input manifest: {differing} difference(s).");
        return differing == 0 ? 0 : 1;

        static string[] Sections(ManifestObject left, ManifestObject right) => [..left.Values.Keys.Union(right.Values.Keys).Order(StringComparer.Ordinal)];

        static string[] Items(ManifestObject left, ManifestObject right, string section)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var side in new[] { left, right })
                if (side.TryGet(section, out var node) && node is ManifestObject items) names.UnionWith(items.Values.Keys);
            return [..names];
        }

        static ManifestNode Pick(ManifestObject root, string section, string item)
        {
            if (!root.TryGet(section, out var node)) return new ManifestText(ManifestDifference.Missing);
            if (item.Length == 0) return node;
            return node is ManifestObject items && items.TryGet(item, out var value) ? value : new ManifestText(ManifestDifference.Missing);
        }
    }

    public static ManifestObject Build(string directory, int jobs, TextWriter log)
    {
        var context = SimContext.Load(directory);
        var root = new ManifestObject()
            .Add("kind", Kind)
            .Add("schema", Schema)
            .Add("definitions", new ManifestObject().Add("sha256", context.DefinitionSha256.ToUpperInvariant()).Add("bytes", context.DefinitionBytes).Add("definitions", context.Definitions))
            .Add("modelGeometry", new ManifestObject().Add("sha256", context.Geometry.Fingerprint.ToUpperInvariant()).Add("airfields", context.Geometry.Airfields.Count))
            .Add("worldFingerprint", context.Fingerprint.ToUpperInvariant());

        var catalog = context.Catalog;
        var catalogs = new ManifestObject();
        // The rate a catalog was built for is part of it, but the recorded hashes are of catalogs that had no such field. While the rate is the original's
        // 30 Hz the field is left out of the description, so the recorded 30 Hz values stay comparable; at any other rate it is described.
        static bool RateAtOriginal(System.Reflection.FieldInfo field, object? value) => field.FieldType == typeof(SimulationRate) && ReferenceEquals(value, SimulationRate.Original);
        void Describe(string name, object value, long items)
        {
            var hash = CanonicalObjectHasher.HashOnLargeStack(value, RateAtOriginal);
            catalogs.Add(name, new ManifestObject().Add("items", items).Add("sha256", hash.Sha256).Add("values", hash.Values));
            log.WriteLine($"  catalog {name}: {items} items, {hash.Values} values described{(hash.Opaque.Count == 0 ? string.Empty : "; only named: " + string.Join(", ", hash.Opaque.Select(pair => $"{pair.Key} x{pair.Value}")))}");
        }
        Describe("templates", catalog.Templates, catalog.Templates.Length);
        Describe("weapons", catalog.Weapons, catalog.Weapons.Weapons.Length);
        Describe("upgrades", catalog.Upgrades, catalog.Upgrades.Upgrades.Length);
        Describe("specialPowers", catalog.SpecialPowers, catalog.SpecialPowers.Powers.Length);
        Describe("sciences", catalog.Sciences, catalog.Sciences.Sciences.Length);
        Describe("commands", catalog.Commands, 1);
        Describe("factions", catalog.Factions, catalog.Factions.Factions.Length);
        Describe("crates", catalog.Crates, catalog.Crates.Crates.Length);
        Describe("creationLists", catalog.CreationLists, catalog.CreationLists.Lists.Length);
        // The catalog as a whole: the nine parts above plus the global values of GameData.ini it carries (gravity, thresholds, bonuses) and its own lookup tables.
        Describe("whole", catalog, catalog.Templates.Length);
        root.Add("catalogs", catalogs);

        var files = new ManifestObject();
        foreach (var path in new[] { CampaignFile, ChallengeFile })
            if (context.Content.Exists(path)) files.Add(path, FileEntry(context.Content, path));
        root.Add("files", files);

        // Maps are independent of the definitions; they are decoded side by side and written in path order.
        var paths = SimContext.MapPaths(context.Content);
        var entries = new ConcurrentDictionary<string, ManifestObject>(StringComparer.Ordinal);
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = jobs }, path =>
        {
            var entry = FileEntry(context.Content, path);
            using var stream = context.Content.OpenRead(path);
            var terrain = TerrainMap.Build(SageMapDecoder.Decode(stream), path);
            entry.Add("terrainFingerprint", terrain.Fingerprint.ToUpperInvariant()).Add("cellsX", terrain.CellsX).Add("cellsY", terrain.CellsY).Add("startPositions", terrain.StartPositions.Length);
            entries[path] = entry;
        });
        var maps = new ManifestObject();
        foreach (var path in paths) maps.Add(path, entries[path]);
        root.Add("maps", maps);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {paths.Length} maps"));
        return root;
    }

    /// <summary>Size and SHA-256 as the content manifest states them (the runtime refuses a file whose size differs; <c>AssetTool verify</c> checks the hash).</summary>
    private static ManifestObject FileEntry(ContentFileSystem content, string path)
    {
        var entry = content.GetEntry(path);
        return new ManifestObject().Add("bytes", entry.Bytes).Add("contentSha256", entry.Sha256.ToUpperInvariant());
    }
}
