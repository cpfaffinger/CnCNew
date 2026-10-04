using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.W3D;

/// <summary>
/// <c>model-audit &lt;content-dir&gt; [--structures] [--model NAME] [--parts]</c>: mesh-by-mesh accounting of every model the retail object definitions select
/// (docs/DEMO-FINDINGS.md #2): total, drawn, hidden-by-default and excluded triangles, triangles the render plan loses, unresolved/aggregate/proxy entries,
/// sub-objects no condition state ever shows, and (second section) the animation handles of the model states that cannot be loaded. Non-zero when a model fails to
/// load or loses triangles; everything else is reported, never silently skipped.
/// </summary>
static class ModelAuditCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: CnCNew.PresentationTool model-audit <content-dir> [--structures] [--model NAME] [--parts]");
            return 2;
        }

        var structuresOnly = args.Contains("--structures");
        var showParts = args.Contains("--parts");
        var onlyModel = ArgumentOf(args, "--model");
        var content = ContentFileSystem.Open(args[1]);
        var graph = DefinitionCompiler.Compile(new AuditContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var visuals = ObjectVisualCatalog.Build(graph);
        var library = new W3dAssetLibrary(path => content.Exists(path) ? content.OpenRead(path) : null);

        // model name -> (templates using it, states using it)
        var usage = new SortedDictionary<string, ModelUsage>(StringComparer.OrdinalIgnoreCase);
        var animationUsers = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var visual in visuals.Visuals)
        {
            foreach (var draw in visual.Draws)
            {
                foreach (var state in draw.States.Concat(draw.Transitions))
                {
                    if (state.Model is { } model)
                    {
                        if (!usage.TryGetValue(model, out var entry)) usage[model] = entry = new ModelUsage();
                        entry.Templates.Add(visual.TemplateName);
                        if (visual.IsStructure) entry.Structure = true;
                        entry.States.Add(state);
                    }

                    foreach (var animation in state.Animations)
                        animationUsers[animation.Name] = animationUsers.GetValueOrDefault(animation.Name) + 1;
                }
            }
        }

        var failures = new List<string>();
        var audits = new List<(W3dModelDrawAudit Audit, ModelUsage Usage)>();
        foreach (var (name, entry) in usage)
        {
            if (structuresOnly && !entry.Structure) continue;
            if (onlyModel is not null && !name.Equals(onlyModel, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                audits.Add((W3dModelDrawAudit.Analyze(library.GetModel(name)), entry));
            }
            catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
            {
                failures.Add($"{name}: {exception.Message}");
            }
        }

        var lost = audits.Where(item => item.Audit.LostTriangles > 0).ToArray();
        var neverShown = new List<string>();
        foreach (var (audit, entry) in audits)
        {
            foreach (var part in audit.Parts.Where(part => part.Disposition == W3dPartDisposition.HiddenByDefault))
                if (!entry.States.Any(state => state.SubObjects.Any(sub => !sub.Hide && Matches(part.Name, sub.SubObject))))
                    neverShown.Add($"{audit.Model}.{part.Name} ({part.Triangles} triangles)");
        }

        Console.WriteLine($"models selected by object states: {usage.Count} ({usage.Values.Count(entry => entry.Structure)} used by structures); audited {audits.Count}; failed to load {failures.Count}");
        var parts = audits.SelectMany(item => item.Audit.Parts).ToArray();
        Console.WriteLine($"sub-objects (most detailed LOD): {parts.Length}; drawn {parts.Count(part => part.Disposition == W3dPartDisposition.Drawn)}, hidden by default {parts.Count(part => part.Disposition == W3dPartDisposition.HiddenByDefault)}, "
                          + $"collision-only {parts.Count(part => part.Disposition == W3dPartDisposition.CollisionOnly)}, shadow-only {parts.Count(part => part.Disposition == W3dPartDisposition.ShadowOnly)}, "
                          + $"null objects {parts.Count(part => part.Disposition == W3dPartDisposition.NullObject)}, without triangles {parts.Count(part => part.Disposition == W3dPartDisposition.NoTriangles)}");
        Console.WriteLine($"triangles: total {audits.Sum(item => (long)item.Audit.TotalTriangles)}, drawn {audits.Sum(item => (long)item.Audit.DrawnTriangles)}, hidden by default {audits.Sum(item => (long)item.Audit.HiddenByDefaultTriangles)}, "
                          + $"excluded by design {audits.Sum(item => (long)item.Audit.ExcludedTriangles)}, lost in the render plan {audits.Sum(item => (long)item.Audit.LostTriangles)}");
        Console.WriteLine($"two-sided drawn parts {parts.Count(part => part.TwoSided && part.Disposition == W3dPartDisposition.Drawn)}, camera-facing parts (drawn unaligned) {audits.Sum(item => item.Audit.CameraFacingParts)}, skinned parts {parts.Count(part => part.Skinned)}");
        Console.WriteLine($"models with aggregates {audits.Count(item => item.Audit.Aggregates > 0)} ({audits.Sum(item => item.Audit.Aggregates)} entries), proxies {audits.Count(item => item.Audit.Proxies > 0)} ({audits.Sum(item => item.Audit.Proxies)}), "
                          + $"unresolved entries {audits.Count(item => item.Audit.Unresolved > 0)} ({audits.Sum(item => item.Audit.Unresolved)}), unreferenced meshes {audits.Count(item => item.Audit.Unreferenced > 0)} ({audits.Sum(item => item.Audit.Unreferenced)}), "
                          + $"ignored chunks {audits.Count(item => item.Audit.IgnoredChunks > 0)} ({audits.Sum(item => item.Audit.IgnoredChunks)})");
        Console.WriteLine($"hidden-by-default sub-objects that no condition state of any using object shows: {neverShown.Count}");
        foreach (var line in neverShown.Take(25)) Console.WriteLine("  never shown: " + line);
        foreach (var line in failures.Take(25)) Console.WriteLine("  load failure: " + line);
        foreach (var (audit, _) in lost.Take(25)) Console.WriteLine($"  LOST {audit.Model}: {audit.LostTriangles} of {audit.TotalTriangles} triangles");

        Console.WriteLine();
        Console.WriteLine("model                                  tris drawn hidden excl lost parts agg prox unres");
        foreach (var (audit, entry) in audits.OrderBy(item => item.Audit.Model, StringComparer.OrdinalIgnoreCase))
        {
            if (!showParts && onlyModel is null && audits.Count > 60) break;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{audit.Model,-36} {audit.TotalTriangles,6} {audit.DrawnTriangles,5} {audit.HiddenByDefaultTriangles,6} {audit.ExcludedTriangles,4} {audit.LostTriangles,4} {audit.Parts.Length,5} {audit.Aggregates,3} {audit.Proxies,4} {audit.Unresolved,5}  {(entry.Structure ? "structure " : "")}{string.Join(",", entry.Templates.Take(2))}"));
            if (showParts || onlyModel is not null)
                foreach (var part in audit.Parts)
                    Console.WriteLine($"    {part.Name,-32} bone {part.BoneIndex,2} {part.Disposition,-15} tris {part.Triangles,5} covered {part.CoveredTriangles,5} batches {part.Batches,2} passes {part.Passes,2}{(part.TwoSided ? " two-sided" : "")}{(part.Skinned ? " skin" : "")}{(part.CameraFacing ? " camera-facing" : "")}");
        }

        // Animations: every handle the states name must load (the client counts and warns about each missing one; here they are listed).
        var missingAnimations = new List<string>();
        var loaded = 0;
        foreach (var (name, users) in animationUsers)
        {
            if (name.IndexOf('.') <= 0 || name.EndsWith('.')) { missingAnimations.Add($"{name} (x{users}): not of the form HIERARCHY.ANIMATION"); continue; }
            try
            {
                if (library.GetAnimation(name) is null) missingAnimations.Add($"{name} (x{users})");
                else loaded++;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or W3dMissingAssetException)
            {
                missingAnimations.Add($"{name} (x{users}): {exception.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"animation handles named by model states: {animationUsers.Count}; loaded {loaded}; missing {missingAnimations.Count}");
        foreach (var line in missingAnimations.Take(40)) Console.WriteLine("  missing animation: " + line);
        return failures.Count == 0 && lost.Length == 0 ? 0 : 1;
    }

    private static bool Matches(string partName, string wanted)
    {
        if (partName.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return true;
        var dot = partName.IndexOf('.');
        return dot >= 0 && partName[(dot + 1)..].Equals(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ArgumentOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed class ModelUsage
    {
        public SortedSet<string> Templates { get; } = new(StringComparer.Ordinal);
        public bool Structure { get; set; }
        public List<ModelStateSpec> States { get; } = [];
    }
}

file sealed class AuditContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
