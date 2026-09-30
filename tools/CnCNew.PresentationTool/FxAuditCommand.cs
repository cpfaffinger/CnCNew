using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Fx;

/// <summary><c>fx-audit &lt;content-dir&gt;</c>: FX inventory, references, textures and an execution sweep; non-zero on unexplained findings.</summary>
static class FxAuditCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: CnCNew.PresentationTool fx-audit <content-dir>");
            return 2;
        }
        var content = ContentFileSystem.Open(args[1]);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new FxContentSource(content), ModuleRegistry.Default, options);
        var catalog = FxCatalog.Build(graph);
        var report = FxAudit.Run(graph, catalog, content.Exists);
        foreach (var line in report.Lines) Console.WriteLine(line);

        var sweep = FxAudit.ExecuteAll(catalog);
        Console.WriteLine($"Execution sweep: {sweep.FxListsExecuted} FXLists in both forms, {sweep.ParticleSystemsSpawned} particle systems spawned, "
            + $"{sweep.Frames} frames, peak {sweep.PeakParticles} particles, {sweep.SoundCues} sound cues, checksum {sweep.Checksum:X16}.");
        var stats = sweep.Particles;
        Console.WriteLine($"  particles created {stats.ParticlesCreated}, systems created {stats.SystemsCreated}, below min priority {stats.RejectedBelowMinPriority}, "
            + $"field cap {stats.RejectedFieldCap}, budget {stats.RejectedBudget}, per-system cap {stats.RejectedPerSystemCap}, evicted {stats.EvictedForBudget}, "
            + $"system cap {stats.RejectedSystemCap}, missing templates {stats.MissingTemplates}.");
        foreach (var (key, value) in sweep.Counters) Console.WriteLine($"  {key}: {value}");
        return report.IsClean ? 0 : 1;
    }
}

file sealed class FxContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
