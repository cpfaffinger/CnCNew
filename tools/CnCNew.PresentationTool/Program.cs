using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;

const string Usage = """
    Usage: CnCNew.PresentationTool <command> [arguments]
      object-audit <content-dir>   Draw modules, model states, condition names, draw keys and Art/W3D references of every retail object.
      model-audit <content-dir> [--structures] [--model NAME] [--parts]   Mesh-by-mesh draw accounting of every selected model, plus the animation handles the model states name.
      angle-audit <content-dir> [--maps N]   Which convention (sign, quarter turn) of the stored map object angle lines up rows of fences and walls.
      fx-audit <content-dir>       FX inventory, references, textures and an execution sweep.
      terrain-audit <content-dir> [--map <virtual map path>]   Terrain render data, atlas and GPU memory of every retail map.
      scenario-audit <content-dir> [--name NAME] [--every N] [--ticks N]   Runs a staged demo (capture-building, tank-duel, jet-strafe) headless and checks what it is meant to show; without --name lists them.
      road-audit <content-dir> [--map <virtual map path>]      Road layout (segments, curves, tees, crossings, joins), templates and textures of every retail map.
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    switch (args[0])
    {
        case "object-audit" when args.Length == 2: return ObjectAuditCommand(args[1]);
        case "model-audit": return ModelAuditCommand.Run(args);
        case "angle-audit": return AngleAuditCommand.Run(args);
        case "fx-audit": return FxAuditCommand.Run(args);
        case "terrain-audit": return CnCNew.PresentationTool.TerrainAudit.Run(args[1..]);
        case "scenario-audit": return CnCNew.PresentationTool.ScenarioAudit.Run(args[1..]);
        case "road-audit": return CnCNew.PresentationTool.RoadAudit.Run(args[1..]);
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException or ArgumentException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

static int ObjectAuditCommand(string contentDirectory)
{
    var content = ContentFileSystem.Open(contentDirectory);
    var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default,
        DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
    var report = ObjectAudit.Run(graph, content.Exists);
    foreach (var line in report.Lines) Console.WriteLine(line);
    if (report.Unclassified == 0) return 0;
    Console.Error.WriteLine($"{report.Unclassified} unknown condition name(s) or draw key(s) are not allow-listed (DrawKeyCatalog.AllowList).");
    return 1;
}

file sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
