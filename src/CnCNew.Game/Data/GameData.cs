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
using CnCNew.Simulation.Time;
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
        AttachedEffects = CnCNew.Presentation.Fx.AttachedEffectCatalog.Build(graph);
        ModelBones = new ModelBoneTable(content);
    }

    /// <summary>The continuous effects objects carry outside the FX lists of events (docs/PRESENTATION-FX.md, "Attached effects").</summary>
    public CnCNew.Presentation.Fx.AttachedEffectCatalog AttachedEffects { get; }

    /// <summary>The movies of <c>Video.ini</c> (docs/CLIENT-VIDEO.md).</summary>
    public CnCNew.Game.Movies.MovieCatalog Movies => _movies ??= CnCNew.Game.Movies.MovieCatalog.FromGraph(Graph);

    private CnCNew.Game.Movies.MovieCatalog? _movies;

    /// <summary>The rest-pose bones of the draw models, read when first asked for.</summary>
    public ModelBoneTable ModelBones { get; }

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

    /// <summary>
    /// Identifies a map across machines: the SHA-256 of the map file from the content manifest. The terrain fingerprint covers what the simulation's terrain
    /// reads; the file hash covers everything else in the file as well (sides, teams, scripts, trigger areas).
    /// </summary>
    public string MapFileHash(string mapPath) => Content.FileHash(mapPath);

    /// <summary>
    /// The tick rate a session runs at when its host names none: the product rate (<see cref="ProductRate"/>, the one definition of it). The one place of the game layer that
    /// names a simulation rate; a host that wants another one passes it to <see cref="Load"/> (the client's <c>--tick-rate</c>), and everything else reads <see cref="Rate"/>
    /// or the world. Not to be confused with <see cref="CnCNew.Data.OriginalFrame"/>, the unit the original's client content is authored in.
    /// </summary>
    public static SimulationRate DefaultSessionRate { get; } = SimulationRate.FromTicksPerSecond(ProductRate.TicksPerSecond);

    /// <summary>The tick rate the catalogs were built for: every world of this data runs at it (a setting of the session, chosen at <see cref="Load"/>).</summary>
    public SimulationRate Rate => Templates.Rate;

    /// <summary>The rules the catalogs were built with (<c>docs/GGC-FIXES.md</c>): the product's unless the host chose others at <see cref="Load"/>.</summary>
    public CnCNew.Simulation.Rules.RuleProfile Rules => Templates.Rules;

    /// <summary>Compiles the retail base data (map overlays excluded) and builds every catalog; the slow part of starting the game.</summary>
    /// <param name="rate">The tick rate of the sessions this data is for; <see cref="DefaultSessionRate"/> when null.</param>
    /// <param name="rules">The rules of the sessions this data is for (<c>docs/GGC-FIXES.md</c>); the product's when null. Folded into <see cref="Fingerprint"/>.</param>
    public static GameData Load(string contentDirectory, Action<string>? progress = null, bool modelGeometry = true, SimulationRate? rate = null,
        CnCNew.Simulation.Rules.RuleProfile? rules = null)
    {
        progress?.Invoke("Opening content");
        var content = ContentFileSystem.Open(contentDirectory);
        if (content.FileCount == 0 || content.EnumerateFiles().Any(path => path.EndsWith(".big", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The runtime content directory is empty or still contains BIG archives.");
        progress?.Invoke("Compiling definitions");
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
        progress?.Invoke("Building catalogs");
        return Build(content, graph, modelGeometry, rate ?? DefaultSessionRate, rules ?? CnCNew.Simulation.Rules.RuleProfile.Product, null);
    }

    private static GameData Build(ContentFileSystem content, CompiledDefinitionGraph graph, bool modelGeometry, SimulationRate rate, CnCNew.Simulation.Rules.RuleProfile rules, string? fingerprintOfOverlay)
    {
        var visuals = ObjectVisualCatalog.Build(graph);
        // The airfields' parking and runway points come from the draw models' bones, which the simulation cannot read: they enter the world as fingerprinted data (docs/SIM-JET-FLIGHT.md).
        var geometry = modelGeometry ? ModelGeometryLoader.Load(graph, visuals, content) : ModelGeometry.None;
        var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, geometry, rules);
        var fingerprint = fingerprintOfOverlay ?? templates.Rules.Combine(templates.Rate.Combine(geometry.Combine(DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256)));
        var fx = FxCatalog.Build(graph);
        var audio = AudioCatalog.FromGraph(graph, content.Exists);
        var images = MappedImageTable.FromGraph(graph);
        var factions = ReferenceMatchScenario.Participants(templates, out _);
        var maps = EnumerateMaps(content);
        return new GameData(content, graph, templates, fingerprint, visuals, fx, audio, images, factions, maps) { _modelGeometry = modelGeometry };
    }

    private bool _modelGeometry;

    /// <summary>What a map's <c>map.ini</c> asked for that does not exist, and what became of it.</summary>
    public ImmutableArray<string> MapRuleNotes { get; private set; } = [];

    private GameData? _root;

    // Two, because the shell map stays behind the menus while a match on another map is played.
    private readonly List<(string Fingerprint, GameData Data)> _mapData = [];

    /// <summary>The fingerprint of the data <see cref="ForMap"/> gives for this map, without building it: the replay list and a saved game judge a file by this.</summary>
    public string FingerprintFor(string mapPath) => MapRules.FingerprintFor(Content, (_root ?? this).Fingerprint, mapPath);

    /// <summary>
    /// The data a match on this map runs with: the base data with the map's <c>map.ini</c> on top (<see cref="MapRules"/>), or the base data for a map without one. Asked of the data of another map
    /// it answers from the base. Throws <see cref="MapRulesException"/> when the rules do not compile.
    /// </summary>
    public GameData ForMap(string mapPath)
    {
        var root = _root ?? this;
        if (MapRules.OverlayOf(Content, mapPath) is not { } overlay) return root;
        var fingerprint = FingerprintFor(mapPath);
        lock (root._mapData)
        {
            foreach (var known in root._mapData) if (known.Fingerprint == fingerprint) return known.Data;
            try
            {
                var notes = new List<string>();
                var graph = MapRules.Compile(Content, overlay, root.Templates, notes);
                var data = Build(Content, graph, _modelGeometry, Rate, Rules, fingerprint);
                data.MapRuleNotes = [.. notes];
                data._root = root;
                root._mapData.Insert(0, (fingerprint, data));
                if (root._mapData.Count > 2) root._mapData.RemoveAt(2);
                return data;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new MapRulesException($"The rules of this map ({overlay}) cannot be read: {exception.Message}", exception);
            }
        }
    }

    /// <summary>The script library of the original skirmish AI in the content.</summary>
    public const string SkirmishScriptLibraryPath = "Data/Scripts/SkirmishScripts.scb";

    private (CnCNew.Data.World.MapScriptLibrary Library, CnCNew.Simulation.Skirmish.Original.AiData Data)? _originalAi;

    /// <summary>
    /// What a match with the original skirmish AI needs beside the catalogs (docs/SKIRMISH-AI-ORIGINAL.md): the script library <c>SkirmishScripts.scb</c> and the typed
    /// <c>AIData.ini</c>. Read from the content on first use and kept; a content without either cannot play that AI and says which part is missing.
    /// </summary>
    public (CnCNew.Data.World.MapScriptLibrary Library, CnCNew.Simulation.Skirmish.Original.AiData Data) LoadOriginalAi()
    {
        if (_originalAi is { } loaded) return loaded;
        var data = CnCNew.Simulation.Skirmish.Original.AiData.TryBuild(Graph) ?? throw new InvalidDataException("The content has no AIData block: the original skirmish AI cannot be played.");
        if (!Content.Exists(SkirmishScriptLibraryPath)) throw new InvalidDataException($"The content has no {SkirmishScriptLibraryPath}: the original skirmish AI cannot be played.");
        CnCNew.Data.World.MapScriptLibrary library;
        using (var stream = Content.OpenRead(SkirmishScriptLibraryPath)) library = CnCNew.Formats.SageMapDecoder.DecodeScriptLibrary(stream);
        _originalAi = (library, data);
        return (library, data);
    }

    /// <summary>Virtual paths of every <c>Maps/*.map</c> file, ordinal order. Needs only a directory listing, not compiled definitions, so callers that have not finished <see cref="Load"/> yet (a boot/loading screen) can use it too.</summary>
    public static ImmutableArray<string> EnumerateMaps(ContentFileSystem content)
        => ImmutableArray.CreateRange(content.EnumerateFiles()
            .Where(path => path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal));

    /// <summary>
    /// Decodes the mouse cursors <c>Mouse.ini</c> names (<c>Data/Cursors/*.ani</c>). Never throws for a bad cursor: missing and undecodable files are listed in
    /// the result, which the caller reports.
    /// </summary>
    public CnCNew.Presentation.Input.CursorSet LoadCursors()
        => CnCNew.Presentation.Input.CursorSet.Load(CnCNew.Presentation.Input.MouseCursorCatalog.FromGraph(Graph), Content.Exists, Content.OpenRead);

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
        if (content.Exists(PatchStringFile))
        {
            using var patch = content.OpenRead(PatchStringFile);
            // The language's own table comes first: a label both define keeps its text there and is listed in Duplicates.
            table = new StringTable(table.Source, table.Format, table.Entries.Concat(StrReader.Read(patch, PatchStringFile).Entries));
        }
        return new LocalizedText(table);
    }

    /// <summary>
    /// The string file the retail installation carries beside its patched layouts (<c>PatchWindow.big</c>): the patched <c>MainMenu.wnd</c> names the label
    /// <c>GUI:CustomMission</c>, which only this file defines, not the language's <c>generals.csf</c>. The published source of the original does not read the file
    /// (<c>GameTextManager::init</c> opens the CSF or <c>Generals.str</c> only), so that the retail executable does is inferred from the data, not shown.
    /// </summary>
    public const string PatchStringFile = "Data/Patch.str";
}

/// <summary>A map's <c>map.ini</c> that this game cannot read.</summary>
public sealed class MapRulesException(string message, Exception inner) : Exception(message, inner);

/// <summary>The base data and one map's overlay: a match sees its own map's rules and no other map's.</summary>
internal sealed class OverlaySource(ContentFileSystem content, string overlay, string overlayText) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles()
        => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/map.ini", StringComparison.OrdinalIgnoreCase)).Append(overlay);
    public Stream OpenRead(string virtualPath)
        => virtualPath.Equals(overlay, StringComparison.OrdinalIgnoreCase) ? new MemoryStream(System.Text.Encoding.Latin1.GetBytes(overlayText)) : content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

public sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
