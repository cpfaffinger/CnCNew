using System.Collections.Immutable;
using CnCNew.Audio.Catalog;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Images;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Data;

/// <summary>Everything the client derives once from the runtime content directory: compiled definitions and the catalogs built on them.</summary>
public sealed class GameData
{
    private GameData(ContentFileSystem content, CompiledDefinitionGraph graph, TemplateCatalog templates, string fingerprint,
        ObjectVisualCatalog visuals, FxCatalog fx, AudioCatalog audio, MappedImageTable images, ImmutableArray<MatchParticipant> factions,
        ImmutableArray<string> maps)
    {
        Content = content;
        Graph = graph;
        Templates = templates;
        Fingerprint = fingerprint;
        Visuals = visuals;
        Fx = fx;
        Audio = audio;
        Images = images;
        Factions = factions;
        Maps = maps;
    }

    public ContentFileSystem Content { get; }
    public CompiledDefinitionGraph Graph { get; }
    public TemplateCatalog Templates { get; }
    public string Fingerprint { get; }
    public ObjectVisualCatalog Visuals { get; }
    public FxCatalog Fx { get; }
    public AudioCatalog Audio { get; }
    public MappedImageTable Images { get; }

    /// <summary>Playable factions (retail base data; ones that cannot field a base are left out).</summary>
    public ImmutableArray<MatchParticipant> Factions { get; }

    /// <summary>Virtual paths of every map file, ordinal order.</summary>
    public ImmutableArray<string> Maps { get; }

    /// <summary>Compiles the retail base data (map overlays excluded) and builds every catalog; the slow part of starting the game.</summary>
    public static GameData Load(string contentDirectory, Action<string>? progress = null, bool modelGeometry = true)
    {
        progress?.Invoke("Opening content");
        var content = ContentFileSystem.Open(contentDirectory);
        if (content.FileCount == 0 || content.EnumerateFiles().Any(path => path.EndsWith(".big", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The runtime content directory is empty or still contains BIG archives.");
        progress?.Invoke("Compiling definitions");
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
        progress?.Invoke("Building catalogs");
        var visuals = ObjectVisualCatalog.Build(graph);
        // The airfields' parking and runway points come from the draw models' bones, which the simulation cannot read: they enter the world as fingerprinted data (docs/SIM-JET-FLIGHT.md).
        var geometry = modelGeometry ? ModelGeometryLoader.Load(graph, visuals, content) : ModelGeometry.None;
        var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, geometry);
        var fingerprint = geometry.Combine(DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256);
        var fx = FxCatalog.Build(graph);
        var audio = AudioCatalog.FromGraph(graph, content.Exists);
        var images = MappedImageTable.FromGraph(graph);
        var factions = ReferenceMatchScenario.Participants(templates, out _);
        var maps = EnumerateMaps(content);
        return new GameData(content, graph, templates, fingerprint, visuals, fx, audio, images, factions, maps);
    }

    /// <summary>Virtual paths of every <c>Maps/*.map</c> file, ordinal order. Needs only a directory listing, not compiled definitions, so callers that have not finished <see cref="Load"/> yet (a boot/loading screen) can use it too.</summary>
    public static ImmutableArray<string> EnumerateMaps(ContentFileSystem content)
        => ImmutableArray.CreateRange(content.EnumerateFiles()
            .Where(path => path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal));

    /// <summary>Loads the main string table of a language (<see cref="LanguageCatalog"/> probe order).</summary>
    public LocalizedText LoadText(string language) => LoadText(Content, language);

    /// <summary>
    /// Loads the main string table of a language directly from a content file system, without a compiled
    /// <see cref="GameData"/> instance. Reading CSF/STR text needs only the raw content archive, not the compiled
    /// definition graph, so a boot/loading screen can resolve labels before <see cref="Load"/> finishes.
    /// </summary>
    public static LocalizedText LoadText(ContentFileSystem content, string language)
    {
        var files = LanguageCatalog.Resolve(language, content.Exists);
        if (files.StringFile is null) throw new FileNotFoundException($"No string table for language '{language}' (probed {string.Join(", ", files.Candidates)}).");
        using var stream = content.OpenRead(files.StringFile);
        var table = files.StringFormat == StringTableFormat.Csf
            ? StringTable.FromCsf(CsfReader.Read(stream), files.StringFile)
            : StrReader.Read(stream, files.StringFile);
        return new LocalizedText(table);
    }
}

public sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
