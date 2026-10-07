using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Formats;
using CnCNew.Data.Snapshots;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

const string Usage = """
    Usage: CnCNew.SimTool <command> [arguments]
      audit-modules [content-dir]                          bindings vs catalog; with a content directory also module use counts
      audit-coverage <content-dir> [--objects TEXT]        module coverage of the compiled shipped objects (retail base data);
                                                           --objects lists blocked objects whose name or missing modules contain TEXT
      replay-record <replay.bin> [--ticks N] [--population N]
                                                           record the synthetic stress scenario as a replay with checkpoints
      replay-run <replay.bin>                              run a replay, verifying its checkpoints
      hash <replay.bin>                                    run a replay and print final hash and hash-chain digest
      reference-match <content-dir> [--max-ticks N] [--min-ticks N] [--world-seed HEX] [--driver-seed HEX] [--record-only] [--out replay.bin] [--diagnose]
                                                           headless free-for-all of all playable factions: record, two fresh runs, snapshot continuation
      ai-match <content-dir> --map <virtual-map-path> [--players N] [--factions A,B,..] [--max-ticks N] [--seed N] [--out replay.bin] [--trace N [--trace-army]]
                                                           skirmish AIs on a real map: record, two fresh runs, snapshot continuation
      skirmish-sweep <content-dir> [--max-ticks N]        starts a short two-AI skirmish on every map with >= 2 start positions;
                                                           reports which start and end (by victory or the tick budget) without crashing
      mission-run <content-dir> --map <virtual-map-path> [--ticks N] [--difficulty easy|normal|hard] [--seed N]
                                                           run a campaign/scripted mission headless: two fresh runs, save/restore continuation, script and combat report
      unit-audit <content-dir> --map <virtual-map-path> [--factions A,B] [--max-ticks N] [--seed N]
                                                           AI skirmish; samples every 15 ticks: units standing inside a structure footprint, units with a move order that do not move (stuck), longest stalls
      stress [--ticks N] [--population N] [--out replay.bin]
                                                           record + two fresh runs + snapshot continuation, leak and budget checks
      perf <content-dir> --map <virtual-map-path> (--players N | --factions A,B,..) [--ticks N] [--replay] [--csv FILE] [--json FILE] [--label TEXT] [--top K] [--sample N] [--no-modules] [--no-observer]
      perf-mission <content-dir> --map <virtual-map-path> [--ticks N] [--difficulty easy|normal|hard] [--csv FILE] [--json FILE] [--label TEXT] [--top K] [--sample N] [--no-modules] [--no-observer]
                                                           time per tick and tick phase, module updates, allocations, replay catch-up speed, snapshot cost (docs/PERFORMANCE.md)
      catalog-cost <content-dir> [--rounds N]              time of TemplateCatalog.Build for 30, 60 and 120 Hz (the catalog is built per tick rate)
      rate-audit <content-dir> [--tick-rate N] [--ranges [--ranges-above UNITS]]
                                                           what the catalog loses at the rate: conversions and catalog values that round to zero or are
                                                           more than 1 % / 10 % off, by function and by type and field, with the worst examples;
                                                           --ranges lists the largest magnitude of every fixed-point field (which may be squared: docs/FIXED-POINT-Q39-24.md)
      range-audit <content-dir> [--frames N] [--distance UNITS]
                                                           every retail object with a weapon is ordered to fire at a tank and at a soldier; lists every run that
                                                           threw and the weapons whose data lie beyond the square limit of the fixed-point numbers
      rate-compare <content-dir> (--list | --group G | <case-id>) [--tsv FILE]
                                                           cases of the rate-equivalence suite (docs/SIMULATION-CORE.md §39): a small scene with retail objects at 30, 60 and
                                                           120 Hz, its game quantities in seconds and world units side by side with the tolerance verdict; ignores --tick-rate
      perf-compare <old.json> <new.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out FILE]
                                                           two --json summaries metric by metric; marks changes beyond the threshold (default 10 %); a report, never a failing test (docs/PERF-GAME-LAYER.md)
      ai-survey <content-dir> --out <tsv> [--seeds N] [--first-seed N] [--minutes M] [--pairings all|<id>,<id>] [--jobs N]
                                                           every AI pairing of the reference manifest once per seed (default 20 from 1), record only, budget in game
                                                           minutes (default 25); one line per match: outcome, end tick, game time, commands, final hash
      rate-distribution <tsv-reference> <tsv-candidate> [--out <report.md>]
                                                           two surveys of the same pairings and seeds at two rates, compared as distributions against limits fixed in
                                                           advance (docs/SIMULATION-CORE.md §40); exit code 0 all tests passed, 1 a test failed, 2 input error
    Every command that creates a world takes --tick-rate N (30, 60 or 120; default 30): the catalog is built for that rate and the world runs at it.
    Tick budgets (--ticks, --max-ticks) stay ticks of that rate. The manifest commands always run at the recorded 30 Hz.
    reference-match, ai-match and mission-run take --time-probe: counters of the time service over the first run (per-tick amounts that round to
    zero at run time, the one-frame minimum engaging, durations off the grid), by place in the source. Diagnostics, not state.
    """;

// Determinism matrix (docs/DETERMINISM.md): every command can run under another culture to show that nothing culture-dependent reaches a result.
if (Environment.GetEnvironmentVariable("CNCNEW_TEST_CULTURE") is { Length: > 0 } testCulture)
{
    var culture = CultureInfo.GetCultureInfo(testCulture);
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
    Console.Error.WriteLine($"culture: {culture.Name} (CNCNEW_TEST_CULTURE)");
}

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    CnCNew.SimTool.ToolRate.Read(args);
}
catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

try
{
    return args[0] switch
    {
        "audit-modules" => AuditModules(args.Length >= 2 ? args[1] : null),
        "audit-coverage" when args.Length >= 2 => AuditCoverage(args[1], args),
        "replay-record" when args.Length >= 2 => RecordReplay(args[1], Options(args)),
        "replay-run" when args.Length >= 2 => RunReplay(args[1]),
        "hash" when args.Length >= 2 => HashReplay(args[1]),
        "stress" => Stress(Options(args), Arg(args, "--out")),
        "reference-match" when args.Length >= 2 => ReferenceMatch(args[1], args),
        "ai-match" when args.Length >= 2 => AiMatch(args[1], args),
        "reference-manifest" or "input-manifest" => CnCNew.SimTool.ManifestCommands.Run(args),
        "skirmish-sweep" when args.Length >= 2 => SkirmishSweep(args[1], args),
        "unit-audit" when args.Length >= 2 => UnitAudit(args[1], args),
        "mission-run" when args.Length >= 2 => MissionRun(args[1], args),
        "perf" when args.Length >= 2 => CnCNew.SimTool.PerfCommand.Skirmish(args, CompileShipped),
        "perf-mission" when args.Length >= 2 => CnCNew.SimTool.PerfCommand.Mission(args, CompileShipped),
        "perf-compare" => CnCNew.SimTool.PerfCompareCommand.Run(args),
        "path-bench" when args.Length >= 2 => CnCNew.SimTool.PathBench.Run(args),
        "catalog-cost" when args.Length >= 2 => CnCNew.SimTool.CatalogCost.Run(args),
        "rate-audit" when args.Length >= 2 => CnCNew.SimTool.RateAudit.Run(args),
        "range-audit" when args.Length >= 2 => CnCNew.SimTool.RangeAudit.Run(args, CompileShipped),
        "ai-survey" => CnCNew.SimTool.AiSurvey.Run(args),
        "rate-distribution" => CnCNew.SimTool.Survey.RateDistribution.Run(args),
        "rate-compare" when args.Length >= 2 => CnCNew.SimTool.RateCompare.Run(args, CompileGraph),
        _ => Fail(Usage),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

/// <summary>
/// Demo finding #12/#13 (docs/DEMO-FINDINGS.md): a unit that stands inside a structure and a unit that bumps into an obstacle and stands there before it finds a way round.
/// Plays a skirmish between AIs and samples every 15 ticks: a mobile ground unit whose cell is blocked by a structure footprint (inside), and a unit with a move
/// order whose speed is zero (stalled), with the longest runs per template.
/// </summary>
static int UnitAudit(string directory, string[] args)
{
    var mapPath = Arg(args, "--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.");
    var options = SkirmishMatchOptions.Standard with { MaxTicks = long.Parse(Arg(args, "--max-ticks") ?? "9000", CultureInfo.InvariantCulture) };
    if (Arg(args, "--seed") is { } seed) options = options with { AiSeed = uint.Parse(seed, CultureInfo.InvariantCulture), WorldSeed = uint.Parse(seed, CultureInfo.InvariantCulture) ^ 0xC0FFEE06 };
    var (_, catalog, fingerprint) = CompileShipped(directory);
    var content = ContentFileSystem.Open(directory);
    using var mapStream = content.OpenRead(mapPath);
    var map = SageMapDecoder.Decode(mapStream);
    var terrain = TerrainMap.Build(map, mapPath);
    var everyone = ReferenceMatchScenario.Participants(catalog, out _);
    var wanted = Arg(args, "--factions")?.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var chosen = wanted is null ? everyone.Take(2).ToImmutableArray()
        : [..wanted.Select(name => everyone.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
    var session = SkirmishMatchSession.Start(options, catalog, fingerprint, terrain, map.Objects, [..chosen.Select(participant => new SkirmishSlot(participant))]);
    var world = session.World;
    // Sampled every 15 logic frames of the original and reported from two seconds on, at every tick rate (15 and 60 ticks at 30 Hz).
    var sampleEvery = world.Time.OriginalFrames(15);
    var reportFrom = world.Time.OriginalFrames(60);
    var insideRun = new Dictionary<uint, (long Start, string Template, string Structure)>();
    var stallRun = new Dictionary<uint, (long Start, string Template)>();
    var insideReports = new List<string>();
    var detail = new List<string>();
    var idleInside = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var longestStall = new SortedDictionary<string, long>(StringComparer.Ordinal);
    long samples = 0, insideSamples = 0, stallSamples = 0;
    var insideTemplates = new SortedDictionary<string, int>(StringComparer.Ordinal);
    while (!session.IsOver)
    {
        session.Step();
        if (world.Tick % sampleEvery != 0 || world.Navigation is not { } grid) continue;
        var seenInside = new HashSet<uint>();
        var seenStalled = new HashSet<uint>();
        foreach (var entity in world.Entities)
        {
            if (entity.EffectivelyDead || entity.ContainedBy.IsValid || entity.Template.IsKindOf("STRUCTURE") || entity.Template.IsKindOf("AIRCRAFT") || entity.Template.Locomotor is null) continue;
            samples++;
            if (grid.IsBlockedByObstacle(TerrainMap.CellOf(entity.X), TerrainMap.CellOf(entity.Y)) && !entity.Template.IsKindOf("PROJECTILE"))
            {
                insideSamples++;
                seenInside.Add(entity.Id.Value);
                if (entity.Speed == Fixed.Zero && !entity.HasMoveTarget && !entity.Template.IsKindOf("HARVESTER"))
                {
                    var owner = world.Entities.Where(other => other.Template.IsKindOf("STRUCTURE") && !other.EffectivelyDead && Math.Abs(D(other.X - entity.X)) < 90 && Math.Abs(D(other.Y - entity.Y)) < 90).Select(other => other.Template.Name).FirstOrDefault() ?? "?";
                    var rally = session.Ais.ElementAtOrDefault(entity.Owner.Value - 1)?.RallyPoint;
                    var key = $"{entity.Template.Name} idle at ({entity.X.Floor()},{entity.Y.Floor()}) near {owner}, own rally {(rally is { } point ? $"({point.X.Floor()},{point.Y.Floor()})" : "none")}, attacking {session.Ais.ElementAtOrDefault(entity.Owner.Value - 1)?.Attacking}";
                    idleInside[key] = idleInside.GetValueOrDefault(key) + 1;
                }
                if (!insideRun.TryGetValue(entity.Id.Value, out var run))
                {
                    // The structure whose box really contains the unit (a rotated half-extent test), else the nearest one.
                    bool Contains(Entity other)
                    {
                        var dx = D(entity.X - other.X);
                        var dy = D(entity.Y - other.Y);
                        var cos = Math.Cos(D(other.Angle));
                        var sin = Math.Sin(D(other.Angle));
                        var lx = dx * cos + dy * sin;
                        var ly = -dx * sin + dy * cos;
                        return other.Template.IsBox ? Math.Abs(lx) <= D(other.Template.GeometryMajorRadius) && Math.Abs(ly) <= D(other.Template.GeometryMinorRadius) : lx * lx + ly * ly <= D(other.Template.GeometryMajorRadius) * D(other.Template.GeometryMajorRadius);
                    }

                    var structures = world.Entities.Where(other => other.Template.IsKindOf("STRUCTURE") && !other.EffectivelyDead).ToArray();
                    var near = structures.Where(Contains).OrderBy(other => (other.X - entity.X) * (other.X - entity.X) + (other.Y - entity.Y) * (other.Y - entity.Y)).FirstOrDefault()
                               ?? structures.OrderBy(other => (other.X - entity.X) * (other.X - entity.X) + (other.Y - entity.Y) * (other.Y - entity.Y)).FirstOrDefault();
                    insideRun[entity.Id.Value] = (world.Tick, entity.Template.Name, near?.Template.Name ?? "?");
                    if (near is not null && !near.Template.Name.Contains("SupplyDock", StringComparison.Ordinal) && !(entity.Template.IsKindOf("INFANTRY") && near.Template.IsKindOf("FS_BARRACKS")) && !(entity.Template.IsKindOf("VEHICLE") && near.Template.IsKindOf("FS_WARFACTORY")) && detail.Count < 24)
                        detail.Add($"  @{world.Tick} #{entity.Id.Value} {entity.Template.Name} at ({entity.X.Floor()},{entity.Y.Floor()}) speed {entity.Speed} move {(entity.HasMoveTarget ? $"({entity.MoveTargetX.Floor()},{entity.MoveTargetY.Floor()})" : "none")} in {near.Template.Name} at ({near.X.Floor()},{near.Y.Floor()}) angle {near.Angle} geometry {near.Template.GeometryMajorRadius}x{near.Template.GeometryMinorRadius}");
                    insideTemplates[entity.Template.Name + " in " + (near?.Template.Name ?? "?")] = insideTemplates.GetValueOrDefault(entity.Template.Name + " in " + (near?.Template.Name ?? "?")) + 1;
                }
            }
            if (entity.HasMoveTarget && entity.Speed == Fixed.Zero)
            {
                stallSamples++;
                seenStalled.Add(entity.Id.Value);
                if (!stallRun.ContainsKey(entity.Id.Value)) stallRun[entity.Id.Value] = (world.Tick, entity.Template.Name);
                var length = world.Tick - stallRun[entity.Id.Value].Start;
                if (length > longestStall.GetValueOrDefault(entity.Template.Name)) longestStall[entity.Template.Name] = length;
            }
        }
        foreach (var id in insideRun.Keys.Where(id => !seenInside.Contains(id)).ToArray())
        {
            var run = insideRun[id];
            if (world.Tick - run.Start >= reportFrom) insideReports.Add($"unit #{id} {run.Template} inside {run.Structure} for {world.Tick - run.Start} ticks (from {run.Start})");
            insideRun.Remove(id);
        }
        foreach (var id in stallRun.Keys.Where(id => !seenStalled.Contains(id)).ToArray()) stallRun.Remove(id);
    }
    foreach (var (id, run) in insideRun) insideReports.Add($"unit #{id} {run.Template} inside {run.Structure} since tick {run.Start} (still at the end, {world.Tick - run.Start} ticks)");
    Console.WriteLine($"unit audit on {mapPath}, {world.Tick} ticks, {samples} unit samples (every {sampleEvery} ticks): inside a structure footprint {insideSamples}, move order without movement {stallSamples}");
    foreach (var pair in insideTemplates) Console.WriteLine($"  first seen inside: {pair.Key} x{pair.Value}");
    foreach (var line in insideReports.Take(30)) Console.WriteLine("  " + line);
    Console.WriteLine($"idle (no order, not moving) inside a structure footprint cell: {idleInside.Values.Sum()} samples");
    foreach (var (key, count) in idleInside.OrderByDescending(pair => pair.Value).Take(12)) Console.WriteLine($"  {count,4} samples: {key}");
    Console.WriteLine("inside structures that produce nothing (first sightings):");
    foreach (var line in detail) Console.WriteLine(line);
    Console.WriteLine("longest run of a move order without movement per template (ticks):");
    foreach (var (name, ticks) in longestStall.OrderByDescending(pair => pair.Value).Take(15)) Console.WriteLine($"  {name,-40} {ticks}");
    return 0;
}

static double D(Fixed value) => CnCNew.Presentation.FixedConvert.ToDouble(value);

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static StressOptions Options(string[] args)
{
    var options = StressOptions.Standard;
    if (Arg(args, "--ticks") is { } ticks) options = options with { Ticks = long.Parse(ticks, CultureInfo.InvariantCulture) };
    if (Arg(args, "--population") is { } population) options = options with { TargetPopulation = int.Parse(population, CultureInfo.InvariantCulture) };
    options = options with { DrainTicks = Math.Min(options.DrainTicks, options.Ticks / 4), CheckpointInterval = (int)Math.Max(1, Math.Min(1000, options.Ticks / 20)) };
    return options;
}

static int AuditModules(string? directory)
{
    var registry = ModuleRegistry.Default;
    var table = ModuleBindingTable.Default;
    var descriptors = registry.Descriptors.ToArray();
    Console.WriteLine($"Catalog: {descriptors.Length} module types; simulation {descriptors.Count(value => value.Category == ModuleCategory.Simulation)}, presentation {descriptors.Count(value => value.Category == ModuleCategory.Presentation)}.");
    Console.WriteLine($"Implemented and bound: {table.Bindings.Length} ({string.Join(", ", table.Bindings.Select(value => value.Name))}).");
    foreach (var group in descriptors.Where(value => value.CompatibilityOnly).GroupBy(value => value.PlannedTarget).OrderBy(value => value.Key, StringComparer.Ordinal))
        Console.WriteLine($"Not implemented, planned for {group.Key}: {group.Count()}");
    if (directory is null) return 0;

    var (graph, _, _) = CompileShipped(directory);
    var uses = new Dictionary<string, int>(StringComparer.Ordinal);
    var objects = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var definition in graph.Definitions.OfType<ObjectDefinition>())
        foreach (var module in definition.Modules)
        {
            uses[module.Name] = uses.GetValueOrDefault(module.Name) + 1;
            if (!objects.TryGetValue(module.Name, out var set)) objects[module.Name] = set = [];
            set.Add(definition.OriginalName);
        }
    var totalUses = uses.Values.Sum();
    var implementedUses = table.Bindings.Sum(value => uses.GetValueOrDefault(value.Name));
    Console.WriteLine();
    Console.WriteLine($"Module uses in the retail base objects (inherited default modules included): {totalUses}; implemented modules cover {implementedUses} ({100.0 * implementedUses / totalUses:F1}%).");
    Console.WriteLine($"  {"module",-36} {"uses",6} {"objects",8}  status");
    foreach (var (name, count) in uses.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
    {
        var descriptor = registry.GetRequired(name);
        var status = !descriptor.CompatibilityOnly ? "IMPLEMENTED" : descriptor.Category == ModuleCategory.Presentation ? "presentation" : descriptor.PlannedTarget;
        Console.WriteLine($"  {name,-36} {count,6} {objects[name].Count,8}  {status}");
    }
    return 0;
}

static CompiledDefinitionGraph CompileGraph(string directory)
{
    var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
    return DefinitionCompiler.Compile(new ContentSource(ContentFileSystem.Open(directory)), ModuleRegistry.Default, options);
}

static (CompiledDefinitionGraph Graph, TemplateCatalog Catalog, string Fingerprint) CompileShipped(string directory)
{
    var content = ContentFileSystem.Open(directory);
    var source = new ContentSource(content);
    var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
    var graph = DefinitionCompiler.Compile(source, ModuleRegistry.Default, options);
    // The same world the client simulates (GameData.Load): the airfields' parking and runway points come from the draw models' bones and are part of the fingerprint.
    var geometry = CnCNew.Game.Data.ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);
    var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, CnCNew.SimTool.ToolRate.Current, geometry);
    return (graph, catalog, catalog.Rate.Combine(geometry.Combine(DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256)));
}

static int AuditCoverage(string directory, string[] args)
{
    var (graph, catalog, fingerprint) = CompileShipped(directory);
    var report = ModuleCoverage.Compute(catalog);
    Console.WriteLine($"Definitions: {graph.Audit.Definitions} (retail base data, map overlays excluded), fingerprint {fingerprint[..16]}.");
    Console.WriteLine($"Objects: {report.Objects}; fully instantiable today: {report.Instantiable} ({100.0 * report.Instantiable / report.Objects:F1}%); blocked: {report.Blocked}.");
    Console.WriteLine($"Objects without any simulation module: {report.WithoutSimulationModules}.");
    Console.WriteLine();
    Console.WriteLine("Blocking modules (objects blocked / objects blocked only by this module):");
    foreach (var blocker in report.Blockers) Console.WriteLine($"  {blocker.Module,-42} {blocker.ObjectsBlocked,5} {blocker.ObjectsBlockedOnlyByThisModule,5}");
    Console.WriteLine();
    Console.WriteLine("Greedy plan (fractional-credit ranking; gain = objects freed by this step, cumulative = instantiable after it):");
    foreach (var step in report.Plan) Console.WriteLine($"  {step.Module,-42} +{step.Gain,4} -> {step.Cumulative,5}");
    Console.WriteLine();
    Console.WriteLine("Presentation modules (never instantiated by the simulation), objects using them:");
    foreach (var (module, objects) in report.PresentationOnly.Take(12)) Console.WriteLine($"  {module,-42} {objects,5}");
    if (Arg(args, "--objects") is { } filter)
    {
        Console.WriteLine();
        Console.WriteLine($"Blocked objects whose name or missing modules contain '{filter}' (object: missing modules):");
        foreach (var template in catalog.Templates.Where(value => !value.IsInstantiable).OrderBy(value => value.Name, StringComparer.Ordinal))
            if (template.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || template.MissingModules.Any(module => module.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"  {template.Name}: {string.Join(", ", template.MissingModules)}");
    }

    // Instantiate every instantiable object, tick, kill: proves the compiled definitions drive the simulation.
    var players = System.Collections.Immutable.ImmutableArray.Create(new PlayerConfiguration(new CnCNew.Simulation.Ids.PlayerId(1), "P1"));
    var world = new SimulationWorld(new WorldConfiguration(new CnCNew.Simulation.Ids.WorldId(1), catalog.Rate, 1, players, "shipped-objects", fingerprint), catalog);
    var sequence = 0UL;
    foreach (var template in catalog.Templates.Where(value => value.IsInstantiable))
        world.Submit(new SpawnCommand(new CnCNew.Simulation.Ids.PlayerId(1), ++sequence, 0, template.Name, CnCNew.Simulation.Ids.PlayerId.System,
            CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero));
    world.Step();
    Console.WriteLine();
    Console.WriteLine($"Spawned {world.Metrics.Entities} of {report.Instantiable} instantiable objects ({world.Metrics.Modules} modules).");
    return world.Metrics.Entities == report.Instantiable ? 0 : 1;
}

static int ReferenceMatch(string directory, string[] args)
{
    var options = ReferenceMatchOptions.Standard;
    if (Arg(args, "--max-ticks") is { } maxTicks) options = options with { MaxTicks = long.Parse(maxTicks, CultureInfo.InvariantCulture) };
    if (Arg(args, "--min-ticks") is { } minTicks) options = options with { MinTicks = long.Parse(minTicks, CultureInfo.InvariantCulture) };
    if (Arg(args, "--world-seed") is { } worldSeed) options = options with { WorldSeed = uint.Parse(worldSeed, NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
    if (Arg(args, "--driver-seed") is { } driverSeed) options = options with { DriverSeed = uint.Parse(driverSeed, NumberStyles.HexNumber, CultureInfo.InvariantCulture) };
    var watch = Stopwatch.StartNew();
    var (_, catalog, fingerprint) = CompileShipped(directory);
    var factions = ReferenceMatchScenario.Participants(catalog, out var excluded);
    Console.WriteLine($"Reference match on retail base data {fingerprint[..16]}: {factions.Length} playable factions.");
    foreach (var participant in factions)
        Console.WriteLine($"  {participant.Faction.Name} (side {participant.Side}): base {participant.BaseBuilding}{(participant.Substitution is null ? "" : $" [stand-in, {participant.Substitution}]")}");
    foreach (var line in excluded) Console.WriteLine($"  excluded: {line}");

    if (factions.Length < 2 || args.Contains("--diagnose"))
    {
        if (factions.Length >= 2) Console.WriteLine("Diagnosis (--diagnose):");
        else Console.WriteLine("The retail reference match cannot run yet: fewer than two factions can field a base and an armed unit. Simulation coverage by side:");
        foreach (var line in ReferenceMatchScenario.DescribeCoverage(catalog)) Console.WriteLine($"  {line}");
        Console.WriteLine("Blocking modules per side (objects blocked / objects not instantiable):");
        foreach (var line in ReferenceMatchScenario.DescribeBlockers(catalog)) Console.WriteLine($"  {line}");
        Console.WriteLine("Cheapest candidates per side (object: missing modules):");
        foreach (var line in ReferenceMatchScenario.DescribeClosest(catalog, 5)) Console.WriteLine($"  {line}");
        if (factions.Length < 2) return 3;
    }

    var scenario = new ReferenceMatchScenario(options);
    watch.Restart();
    var timeProbe = CnCNew.SimTool.TimeProbeReport.Attach(args, catalog.Rate);
    var recorded = scenario.Record(catalog, fingerprint);
    CnCNew.SimTool.TimeProbeReport.Detach(timeProbe, catalog.Rate, recorded.Header.EndTick);
    Console.WriteLine($"Recorded {recorded.Commands.Length} commands ({scenario.ProductionOrders} production orders, {scenario.Orders} move orders, {scenario.DockOrders} dock orders, {scenario.Shots} shots) in {watch.Elapsed.TotalSeconds:F1} s; peak {scenario.PeakAlive} living objects; {scenario.StartObjectsSkipped} starting objects skipped.");
    foreach (var line in scenario.SkippedStartObjects) Console.WriteLine($"  skipped start object: {line}");
    foreach (var line in scenario.SupplyNotes) Console.WriteLine($"  supply note: {line}");
    Console.WriteLine($"Supply deliveries at supply centers: {scenario.SupplyDeliveries}, paying {scenario.SupplyDeliveryMoney} in total.");
    Console.WriteLine(scenario.Winner is null
        ? $"No winner within {recorded.Header.EndTick} ticks."
        : $"Winner: player {scenario.Winner.Value.Value} ({scenario.WinnerFaction}) at tick {scenario.VictoryTick} ({CnCNew.SimTool.ToolRate.Minutes(scenario.VictoryTick):F1} game minutes); replay length {recorded.Header.EndTick} ticks.");
    // --record-only: the course of the match under these seeds, without the replay checks (a survey over seeds).
    if (args.Contains("--record-only")) return 0;

    var checkpointRun = ReplayRunner.Run(recorded, catalog, fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval });
    var replay = recorded with { Checkpoints = checkpointRun.Checkpoints };
    var bytes = ReplayFormat.Write(replay);
    Console.WriteLine($"Replay file: {bytes.Length} bytes, {replay.Checkpoints.Length} checkpoints, SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}.");
    if (Arg(args, "--out") is { } path) File.WriteAllBytes(Path.GetFullPath(path), bytes);
    replay = ReplayFormat.Read(bytes);

    watch.Restart();
    var first = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 5_000 });
    Console.WriteLine($"Run 1 (checkpoints verified {first.VerifiedCheckpoints}/{replay.Checkpoints.Length}): {watch.Elapsed.TotalSeconds:F1} s, {first.SubmittedCommands} commands, {first.RejectedCommands} rejected, final hash {first.FinalHash:X16}.");
    var second = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true });
    var identical = first.TickHashes.AsSpan().SequenceEqual(second.TickHashes);
    Console.WriteLine($"Run 2 (fresh): tick hashes identical over {second.TickHashes.Length} ticks: {identical}; chain {Chain(second.TickHashes):X16}.");
    var half = replay.Header.EndTick / 2;
    var head = ReplayRunner.CreateWorld(replay.Header, catalog);
    var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), catalog, fingerprint);
    var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
    var continued = headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(second.TickHashes);
    Console.WriteLine($"Snapshot continuation at tick {half}: identical over {headRun.TickHashes.Length + tail.TickHashes.Length} ticks: {continued}.");

    var world = ReplayRunner.CreateWorld(replay.Header, catalog);
    ReplayRunner.Run(replay, world);
    var survivors = world.Entities.Where(value => !value.Owner.IsSystem).GroupBy(value => value.Owner.Value).OrderBy(group => group.Key).Select(group => $"{replay.Header.Players.First(player => player.Id.Value == group.Key).Name}={group.Count()}");
    Console.WriteLine($"End state: {world.Metrics}; survivors by owner: {string.Join(", ", survivors)}.");
    if (args.Contains("--diagnose"))
        Console.WriteLine($"Survivors by template: {string.Join(", ", world.Entities.Where(value => !value.Owner.IsSystem).GroupBy(value => value.Template.Name).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(12).Select(group => $"{group.Key}={group.Count()}"))}.");
    Console.WriteLine($"Rejections by reason: {string.Join(", ", Enum.GetValues<CommandRejection>().Where(reason => world.RejectionCounts[(int)reason] > 0).Select(reason => $"{reason}={world.RejectionCounts[(int)reason]}"))}.");
    return identical && continued && first.FinalHash == second.FinalHash ? 0 : 1;
}

static int AiMatch(string directory, string[] args)
{
    var mapPath = Arg(args, "--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.");
    var options = SkirmishMatchOptions.Standard;
    if (Arg(args, "--max-ticks") is { } maxTicks) options = options with { MaxTicks = long.Parse(maxTicks, CultureInfo.InvariantCulture) };
    if (Arg(args, "--seed") is { } seed) options = options with { AiSeed = uint.Parse(seed, CultureInfo.InvariantCulture), WorldSeed = uint.Parse(seed, CultureInfo.InvariantCulture) ^ 0xC0FFEE06 };
    var watch = Stopwatch.StartNew();
    var (_, catalog, fingerprint) = CompileShipped(directory);
    var content = ContentFileSystem.Open(directory);
    using var mapStream = content.OpenRead(mapPath);
    var map = SageMapDecoder.Decode(mapStream);
    var terrain = TerrainMap.Build(map, mapPath);
    var everyone = ReferenceMatchScenario.Participants(catalog, out _);
    var wanted = Arg(args, "--factions")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var chosen = wanted is null
        ? everyone.Take(int.Parse(Arg(args, "--players") ?? "2", CultureInfo.InvariantCulture)).ToImmutableArray()
        : [..wanted.Select(name => everyone.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
    Console.WriteLine($"AI match on {mapPath} ({terrain.CellsX}x{terrain.CellsY} cells, {terrain.StartPositions.Length} start positions, terrain {terrain.Fingerprint[..16]}), definitions {fingerprint[..16]}.");
    foreach (var participant in chosen)
        Console.WriteLine($"  {participant.Faction.Name} (side {participant.Side}): base {participant.BaseBuilding}{(participant.Substitution is null ? "" : $" [stand-in, {participant.Substitution}]")}");

    var scenario = new SkirmishMatchScenario(options);
    // --trace N: every N ticks one line per player (money, structures, army, harvesters, attack waves) and the supply boxes left on the map: where a match's economy and armies go over time.
    var trace = Arg(args, "--trace") is { } every ? long.Parse(every, CultureInfo.InvariantCulture) : 0;
    var timeProbe = CnCNew.SimTool.TimeProbeReport.Attach(args, catalog.Rate);
    var recorded = scenario.Record(catalog, fingerprint, terrain, map.Objects, chosen, trace <= 0 ? null : session =>
    {
        var world = session.World;
        if (world.Tick % trace != 0) return;
        var boxes = world.Entities.Sum(entity => (long)CnCNew.Simulation.Views.EntityPresentationView.WarehouseSupplyBoxes(entity));
        var line = new System.Text.StringBuilder($"  trace {world.Tick,6}: map boxes {boxes,5}");
        for (var index = 0; index < session.Players.Length; index++)
        {
            var player = session.Players[index];
            var own = world.Entities.Where(entity => entity.Owner == player.Id && !entity.EffectivelyDead).ToList();
            var structures = own.Count(entity => entity.Template.IsKindOf("STRUCTURE"));
            var harvesters = own.Count(entity => entity.Template.IsKindOf("HARVESTER"));
            var builders = own.Count(entity => entity.Template.IsKindOf("DOZER"));
            var army = own.Count - structures - harvesters - builders;
            var stats = session.Ais[index]?.Stats;
            // How far the nearest stocked warehouse is from the player's nearest supply center (cash generators): the length of a gathering trip.
            var trip = -1L;
            foreach (var center in own.Where(entity => entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE")))
                foreach (var warehouse in world.Entities.Where(entity => CnCNew.Simulation.Views.EntityPresentationView.WarehouseSupplyBoxes(entity) > 0))
                {
                    var dx = (warehouse.X - center.X).ToDecimal();
                    var dy = (warehouse.Y - center.Y).ToDecimal();
                    var distance = (long)Math.Sqrt((double)(dx * dx + dy * dy));
                    if (trip < 0 || distance < trip) trip = distance;
                }
            line.Append($" | {player.Faction?.Replace("Faction", string.Empty)}: ${(long)world.MoneyOf(player.Id).Floor(),6} bld {structures,2} army {army,3} harv {harvesters,2} doz {builders} trip {trip,4} waves {stats?.Waves ?? 0} prod {stats?.Productions ?? 0}");
        }
        Console.WriteLine(line.ToString());
        if (!args.Contains("--trace-army")) return;
        // --trace-army: per player the army by type and what it is doing (marching, on an attack-move, fighting a victim, idle), how far it is from the nearest enemy structure, and the AI's own view.
        for (var index = 0; index < session.Players.Length; index++)
        {
            var player = session.Players[index];
            var enemyStructures = world.Entities.Where(entity => world.AreEnemies(player.Id, entity.Owner) && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE")).ToList();
            var army = world.Entities.Where(entity => entity.Owner == player.Id && !entity.EffectivelyDead && !entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("DOZER"));
            foreach (var group in army.GroupBy(entity => entity.Template.Name).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                long Nearest(CnCNew.Simulation.World.Entity unit) => enemyStructures.Count == 0 ? -1 : enemyStructures.Min(structure =>
                {
                    var dx = (structure.X - unit.X).ToDecimal();
                    var dy = (structure.Y - unit.Y).ToDecimal();
                    return (long)Math.Sqrt((double)(dx * dx + dy * dy));
                });
                Console.WriteLine($"      {player.Faction?.Replace("Faction", string.Empty)} {group.Key} x{group.Count()}: moving {group.Count(unit => unit.HasMoveTarget)}, attack-move {group.Count(world.IsAttackMoving)}, fighting {group.Count(unit => world.AcquiredVictimOf(unit).IsValid)}, contained {group.Count(unit => unit.ContainedBy.IsValid)}, weapon range {(long)group.Max(unit => world.MaxWeaponRange(unit)).Floor()}, nearest enemy structure {group.Min(Nearest)}..{group.Max(Nearest)}");
            }
            if (session.Ais[index] is { } ai) Console.WriteLine($"      {player.Faction?.Replace("Faction", string.Empty)} AI: {ai.DescribeState()}");
        }
    });
    CnCNew.SimTool.TimeProbeReport.Detach(timeProbe, catalog.Rate, recorded.Header.EndTick);
    Console.WriteLine($"Recorded {recorded.Commands.Length} commands over {recorded.Header.EndTick} ticks in {watch.Elapsed.TotalSeconds:F1} s; {scenario.WarehousesSpawned} map warehouses spawned.");
    foreach (var note in scenario.Notes) Console.WriteLine($"  note: {note}");
    for (var index = 0; index < scenario.Ais.Length; index++)
    {
        var stats = scenario.Ais[index].Stats;
        var final = scenario.Finals[index];
        Console.WriteLine($"  {final.Faction}: {(final.Defeated ? "defeated" : "alive")}, {final.Objects} objects, {final.Money} money; commands {stats.Commands} (rejected {stats.Rejected}), built {stats.Constructions} (resumed {stats.Resumes}, cancelled {stats.CancelledSites}), produced {stats.Productions}, waves {stats.Waves}, powers {stats.PowerUses}, sciences {stats.SciencePurchases}, repairs {stats.Repairs}, docks {stats.DockOrders}, tunnel entries {stats.TunnelEntries}, transport loads {stats.TransportLoads}, evacuations {stats.Evacuations}, idle thinks {stats.IdleThinks}");
        if (stats.RejectionsByReason.Count > 0) Console.WriteLine($"    rejections: {string.Join(", ", stats.RejectionsByReason.Select(pair => $"{pair.Key}={pair.Value}"))}");
    }
    Console.WriteLine(scenario.Winner is null
        ? $"No winner within {recorded.Header.EndTick} ticks (stalemate)."
        : $"Winner: player {scenario.Winner.Value.Value} ({scenario.Players.First(value => value.Id == scenario.Winner.Value).Faction}) at tick {scenario.VictoryTick} ({CnCNew.SimTool.ToolRate.Minutes(scenario.VictoryTick):F1} game minutes).");

    var checkpointRun = ReplayRunner.Run(recorded, catalog, fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval }, terrain);
    var replay = recorded with { Checkpoints = checkpointRun.Checkpoints };
    var bytes = ReplayFormat.Write(replay);
    Console.WriteLine($"Replay file: {bytes.Length} bytes, {replay.Checkpoints.Length} checkpoints, SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}.");
    if (Arg(args, "--out") is { } path) File.WriteAllBytes(Path.GetFullPath(path), bytes);
    replay = ReplayFormat.Read(bytes);

    watch.Restart();
    var first = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 5_000 }, terrain);
    Console.WriteLine($"Run 1 (checkpoints verified {first.VerifiedCheckpoints}/{replay.Checkpoints.Length}): {watch.Elapsed.TotalSeconds:F1} s, {first.SubmittedCommands} commands, {first.RejectedCommands} rejected, final hash {first.FinalHash:X16}.");
    var second = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true }, terrain);
    var identical = first.TickHashes.AsSpan().SequenceEqual(second.TickHashes);
    Console.WriteLine($"Run 2 (fresh): tick hashes identical over {second.TickHashes.Length} ticks: {identical}; chain {Chain(second.TickHashes):X16}.");
    var half = replay.Header.EndTick / 2;
    var head = ReplayRunner.CreateWorld(replay.Header, catalog, terrain);
    var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), catalog, fingerprint, terrain);
    var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
    var continued = headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(second.TickHashes);
    Console.WriteLine($"Snapshot continuation at tick {half}: identical over {headRun.TickHashes.Length + tail.TickHashes.Length} ticks: {continued}.");

    var world = ReplayRunner.CreateWorld(replay.Header, catalog, terrain);
    ReplayRunner.Run(replay, world);
    Console.WriteLine($"End state: {world.Metrics}.");
    foreach (var player in scenario.Players)
        Console.WriteLine($"  {player.Faction}: {string.Join(", ", world.Entities.Where(value => value.Owner == player.Id && !value.EffectivelyDead).GroupBy(value => value.Template.Name).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}={group.Count()}"))}");
    Console.WriteLine($"Rejections by reason: {string.Join(", ", Enum.GetValues<CommandRejection>().Where(reason => world.RejectionCounts[(int)reason] > 0).Select(reason => $"{reason}={world.RejectionCounts[(int)reason]}"))}.");
    return identical && continued && first.FinalHash == second.FinalHash ? 0 : 1;
}

/// <summary>
/// `docs/HANDOFF-PHASE10.md` point 1 / `docs/IMPLEMENTATION-PLAN.md` §9's open "alle Skirmish-Karten start- und beendbar" acceptance: every
/// map with at least two start positions (<c>MapTool audit</c>: 75 of 150 maps have none at all — those are campaign maps, not a gap) gets a
/// short, two-faction <see cref="SkirmishMatchScenario"/>. "Startable and endable" here means the match session runs its full lifecycle
/// (starting objects spawn, the AIs think and act, the session reaches a winner or its own tick budget) without throwing; it is not a balance
/// or win-rate measurement (that is <c>ai-match</c>'s and the 16-map table's job, run far longer).
/// </summary>
static int SkirmishSweep(string directory, string[] args)
{
    var maxTicks = long.Parse(Arg(args, "--max-ticks") ?? "3000", CultureInfo.InvariantCulture);
    var watch = Stopwatch.StartNew();
    var (_, catalog, fingerprint) = CompileShipped(directory);
    var content = ContentFileSystem.Open(directory);
    var everyone = ReferenceMatchScenario.Participants(catalog, out _);
    if (everyone.Length < 2) { Console.Error.WriteLine("Fewer than two playable factions in this content set."); return 1; }
    var chosen = everyone.Take(2).ToImmutableArray();
    var paths = content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

    var zeroStarts = 0;
    var oneStart = 0;
    var started = 0;
    var finishedWithWinner = 0;
    var finishedAtBudget = 0;
    var failures = new List<string>();
    foreach (var path in paths)
    {
        TerrainMap terrain;
        ImmutableArray<CnCNew.Data.World.MapObject> mapObjects;
        try
        {
            using var stream = content.OpenRead(path);
            var map = SageMapDecoder.Decode(stream);
            terrain = TerrainMap.Build(map, path);
            mapObjects = map.Objects;
        }
        catch (Exception exception)
        {
            failures.Add($"{path}: decode/terrain failed: {exception.GetType().Name}: {exception.Message}");
            continue;
        }
        if (terrain.StartPositions.Length == 0) { zeroStarts++; continue; }
        if (terrain.StartPositions.Length == 1) { oneStart++; continue; }
        started++;
        try
        {
            var options = SkirmishMatchOptions.Standard with { MaxTicks = maxTicks, GraceTicks = CnCNew.SimTool.ToolRate.GraceWithinBudget(SkirmishMatchOptions.Standard.GraceTicks, maxTicks, catalog.Rate) };
            var scenario = new SkirmishMatchScenario(options);
            scenario.Record(catalog, fingerprint, terrain, mapObjects, chosen);
            if (scenario.Winner is not null) finishedWithWinner++;
            else finishedAtBudget++;
        }
        catch (Exception exception)
        {
            failures.Add($"{path}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    Console.WriteLine($"{paths.Length} maps; {zeroStarts} with no start waypoint at all (campaign maps, `MapTool audit`'s figure), {oneStart} with exactly one (not enough for a 2-player skirmish); {started} with >= 2 attempted with {chosen[0].Faction.Name} vs {chosen[1].Faction.Name}.");
    Console.WriteLine($"Started and ended cleanly: {started - failures.Count} ({finishedWithWinner} with a winner within {maxTicks} ticks, {finishedAtBudget} reached the tick budget undecided); {failures.Count} failed. {watch.Elapsed.TotalSeconds:F1} s.");
    foreach (var failure in failures) Console.WriteLine($"  FAIL {failure}");
    return failures.Count == 0 ? 0 : 1;
}

static int RecordReplay(string path, StressOptions options)
{
    var fixture = new SyntheticFixture(CnCNew.SimTool.ToolRate.Current);
    var scenario = new StressScenario(options);
    var recorded = scenario.Record(fixture);
    var checkpoints = ReplayRunner.Run(recorded, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval });
    var replay = recorded with { Checkpoints = checkpoints.Checkpoints };
    var bytes = ReplayFormat.Write(replay);
    File.WriteAllBytes(Path.GetFullPath(path), bytes);
    Console.WriteLine($"Recorded {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints, {replay.Header.EndTick} ticks, peak {scenario.PeakAlive} entities; {bytes.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}.");
    return 0;
}

static int RunReplay(string path)
{
    var fixture = new SyntheticFixture(CnCNew.SimTool.ToolRate.Current);
    var replay = ReplayFormat.Read(File.ReadAllBytes(path));
    var result = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { VerifyIncrementalEvery = 5000 });
    Console.WriteLine($"Replay ok: {result.TicksExecuted} ticks, {result.VerifiedCheckpoints}/{replay.Checkpoints.Length} checkpoints verified, final hash {result.FinalHash:X16}.");
    return 0;
}

static int HashReplay(string path)
{
    var fixture = new SyntheticFixture(CnCNew.SimTool.ToolRate.Current);
    var replay = ReplayFormat.Read(File.ReadAllBytes(path));
    var result = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
    Console.WriteLine($"final hash {result.FinalHash:X16}; hash chain {Chain(result.TickHashes):X16}; {result.TicksExecuted} ticks");
    return 0;
}

static ulong Chain(ulong[] hashes)
{
    var chain = StateHash.Seed;
    foreach (var hash in hashes) chain = StateHash.Mix(chain, hash);
    return chain;
}

static int MissionRun(string directory, string[] args)
{
    var mapPath = Arg(args, "--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.");
    var ticks = long.Parse(Arg(args, "--ticks") ?? "9000", CultureInfo.InvariantCulture);
    var options = MissionOptions.Standard with { Difficulty = Enum.Parse<ScriptDifficulty>(Arg(args, "--difficulty") ?? "Normal", true), MaxTicks = 0 };
    if (Arg(args, "--seed") is { } seed) options = options with { WorldSeed = uint.Parse(seed, CultureInfo.InvariantCulture) };
    var (_, catalog, fingerprint) = CompileShipped(directory);
    var content = ContentFileSystem.Open(directory);
    using var mapStream = content.OpenRead(mapPath);
    var map = SageMapDecoder.Decode(mapStream);
    var terrain = TerrainMap.Build(map, mapPath);
    Console.WriteLine($"Mission {mapPath} ({terrain.CellsX}x{terrain.CellsY} cells), {options.Difficulty}, definitions {fingerprint[..16]}, terrain {terrain.Fingerprint[..16]}.");
    var watch = Stopwatch.StartNew();
    var timeProbe = CnCNew.SimTool.TimeProbeReport.Attach(args, catalog.Rate);
    var first = MissionScenario.Run(options, catalog, fingerprint, terrain, map, ticks);
    CnCNew.SimTool.TimeProbeReport.Detach(timeProbe, catalog.Rate, first.Ticks);
    Console.WriteLine($"Run 1: {watch.Elapsed.TotalSeconds:F1} s.");
    Print(first);
    var second = MissionScenario.Run(options, catalog, fingerprint, terrain, map, ticks);
    Console.WriteLine($"Run 2 (fresh): chain {second.Chain:X16}, final {second.FinalHash:X16}: identical to run 1: {second.Chain == first.Chain && second.FinalHash == first.FinalHash}.");
    var (original, restored, bytes) = MissionScenario.VerifyContinuation(options, catalog, fingerprint, terrain, map, ticks);
    Console.WriteLine($"Save/restore at tick {ticks / 2} ({bytes} bytes): continuation identical: {original.Chain == restored.Chain && original.FinalHash == restored.FinalHash} (chain {restored.Chain:X16}).");
    return original.Chain == restored.Chain && second.Chain == first.Chain ? 0 : 1;

    static void Print(MissionRunReport report)
    {
        Console.WriteLine($"  {report.Players} players, {report.PlacedObjects} placed objects ({report.SkippedObjects} skipped over {report.SkippedTemplates} templates), {report.Entities} entities at tick {report.Ticks}.");
        Console.WriteLine($"  scripts executed {report.ExecutedScripts}, actions executed {report.ExecutedActions}, forwarded {report.ForwardedActions}, shots {report.Shots}; outcome {report.Outcome}{(report.OutcomeTick >= 0 ? $" at tick {report.OutcomeTick}" : "")}{(report.Ended ? " (ended)" : "")}.");
        Console.WriteLine($"  final hash {report.FinalHash:X16}, chain {report.Chain:X16}.");
        Console.WriteLine($"  missing actions {report.MissingActionTotal} ({report.MissingActions.Length} types): {string.Join(", ", report.MissingActions.Take(12).Select(item => $"{item.Name}={item.Count}"))}");
        Console.WriteLine($"  missing conditions {report.MissingConditionTotal} ({report.MissingConditions.Length} types): {string.Join(", ", report.MissingConditions.Take(12).Select(item => $"{item.Name}={item.Count}"))}");
        foreach (var item in report.Diagnostics.Take(10)) Console.WriteLine($"  diagnostic: {item.Name} x{item.Count}");
        foreach (var item in report.Skipped.OrderByDescending(value => value.Count).Take(8)) Console.WriteLine($"  skipped object {item.Template} x{item.Count}");
    }
}


static int Stress(StressOptions options, string? replayOut)
{
    var report = new List<string>();
    void Say(string line)
    {
        Console.WriteLine(line);
        report.Add(line);
    }
    var total = Stopwatch.StartNew();
    Say($"Stress: {options.Ticks} ticks, target {options.TargetPopulation} long-lived entities, world seed {options.WorldSeed:X8}, driver seed {options.DriverSeed:X8}.");
    Say($"Hardware: {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}; {Environment.ProcessorCount} logical processors; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB memory; {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; Server GC {System.Runtime.GCSettings.IsServerGC}.");

    var fixture = new SyntheticFixture(CnCNew.SimTool.ToolRate.Current);
    var watch = Stopwatch.StartNew();
    var scenario = new StressScenario(options);
    var recorded = scenario.Record(fixture);
    Say($"Recorded {recorded.Commands.Length} commands in {watch.Elapsed.TotalSeconds:F1} s; peak {scenario.PeakAlive} simultaneously alive entities.");

    // Run 1 (also creates the checkpoints), run 2, and the continuation run all must agree tick by tick.
    watch.Restart();
    var checkpointRun = ReplayRunner.Run(recorded, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval });
    var replay = recorded with { Checkpoints = checkpointRun.Checkpoints };
    Say($"Checkpoint run: {watch.Elapsed.TotalSeconds:F1} s.");
    var bytes = ReplayFormat.Write(replay);
    Say($"Replay file: {bytes.Length} bytes, {replay.Checkpoints.Length} checkpoints, SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}.");
    if (replayOut is not null) File.WriteAllBytes(Path.GetFullPath(replayOut), bytes);
    replay = ReplayFormat.Read(bytes);

    long maxEntities = 0, maxTimers = 0, maxUpdates = 0, maxModules = 0, maxPendingCommands = 0;
    long allocated = 0, sampledTicks = 0;
    var tickTimes = new List<double>();
    long slowest = 0;
    var firstWorld = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
    watch.Restart();
    var firstRun = RunMeasured(replay, firstWorld, ref maxEntities, ref maxTimers, ref maxUpdates, ref maxModules, ref maxPendingCommands, ref allocated, ref sampledTicks, ref slowest, options);
    var firstSeconds = watch.Elapsed.TotalSeconds;
    Say($"Run 1: {firstSeconds:F1} s ({firstSeconds / options.Ticks * 1e6:F1} us/tick), {firstRun.SubmittedCommands} commands, {firstRun.VerifiedCheckpoints} checkpoints verified, final hash {firstRun.FinalHash:X16}.");
    Say($"Peaks: {maxEntities} entities, {maxModules} modules, {maxUpdates} scheduled updates, {maxTimers} timers, {maxPendingCommands} pending commands; slowest tick {slowest / (double)Stopwatch.Frequency * 1000:F2} ms.");
    var perTick = sampledTicks == 0 ? 0 : allocated / sampledTicks;
    Say($"Steady-state allocation of Step(): {perTick} bytes/tick over {sampledTicks} sampled ticks (commands are allocated by the caller and excluded).");

    watch.Restart();
    var second = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
    Say($"Run 2 (fresh, hashes recorded): {watch.Elapsed.TotalSeconds:F1} s, final hash {second.FinalHash:X16}.");
    var firstAgain = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
    var identical = firstAgain.TickHashes.AsSpan().SequenceEqual(second.TickHashes);
    Say($"Run 3 (fresh): tick hashes identical to run 2 over {second.TickHashes.Length} ticks: {identical}; chain {Chain(second.TickHashes):X16}.");
    if (!identical) return 1;

    // Snapshot continuation: stop halfway, snapshot, restore, finish; the concatenated hashes equal a straight run.
    var half = options.Ticks / 2;
    var head = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
    var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
    var snapshot = WorldSnapshot.Write(head);
    Say($"Snapshot at tick {half}: {snapshot.Length} bytes, {head.Metrics.Entities} entities, SHA-256 {WorldSnapshot.Sha256(snapshot)}.");
    watch.Restart();
    var restored = WorldSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint);
    Say($"Restore: {watch.Elapsed.TotalSeconds:F2} s, hash {restored.StateHash:X16} == {head.StateHash:X16}: {restored.StateHash == head.StateHash}.");
    var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
    var continuation = headRun.TickHashes.Concat(tail.TickHashes).ToArray();
    var continued = continuation.AsSpan().SequenceEqual(second.TickHashes);
    Say($"Continuation after snapshot: tick hashes identical over {continuation.Length} ticks: {continued}; final hash {tail.FinalHash:X16}.");
    if (!continued) return 1;

    var end = firstWorld.Metrics;
    var baseline = end.Entities == 0 && end.Modules == 0 && end.ScheduledUpdates == 0 && end.Timers == 0 && end.PendingEvents == 0 && end.PendingCommands == 0 && end.PendingSpawns == 0 && end.PendingDestroys == 0;
    Say($"End state: {end}. Tables at baseline: {baseline}.");
    Say($"Spawn/destroy operations: {end.SpawnedTotal} spawned, {end.DestroyedTotal} destroyed; rejections by reason: {string.Join(", ", Enum.GetValues<CommandRejection>().Select(reason => $"{reason}={firstWorld.RejectionCounts[(int)reason]}"))}.");
    var process = Process.GetCurrentProcess();
    Say($"Process: peak working set {process.PeakWorkingSet64 / (1024 * 1024)} MiB, managed heap {GC.GetTotalMemory(false) / (1024 * 1024)} MiB, GC gen0/1/2 = {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}.");
    Say($"Total: {total.Elapsed.TotalSeconds:F1} s.");

    var failures = new List<string>();
    if (!baseline) failures.Add("tables did not return to their baseline");
    if (end.SpawnedTotal != end.DestroyedTotal) failures.Add("spawn/destroy mismatch");
    if (maxEntities < Math.Min(10_000, options.TargetPopulation)) failures.Add($"only {maxEntities} entities were alive at the peak");
    if (options.Ticks >= 216_000 && end.SpawnedTotal < 100_000) failures.Add("fewer than 100,000 spawn operations");
    foreach (var failure in failures) Console.Error.WriteLine("STRESS FAILURE: " + failure);
    return failures.Count == 0 ? 0 : 1;
}

static ReplayRunResult RunMeasured(ReplayFile replay, SimulationWorld world, ref long maxEntities, ref long maxTimers, ref long maxUpdates, ref long maxModules,
    ref long maxPendingCommands, ref long allocated, ref long sampledTicks, ref long slowest, StressOptions options)
{
    // Manual loop (instead of ReplayRunner.Run) so that only Step() is measured for time and allocation.
    var expected = replay.Checkpoints.ToDictionary(value => value.Tick, value => value.Hash);
    var commandIndex = 0;
    var submitted = 0;
    var verified = 0;
    var warmup = options.Ticks / 10;
    var window = options.Ticks / 2;
    while (world.Tick < replay.Header.EndTick)
    {
        var tick = world.Tick;
        while (commandIndex < replay.Commands.Length && replay.Commands[commandIndex].Tick == tick)
        {
            world.Submit(replay.Commands[commandIndex++]);
            submitted++;
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var result = world.Step();
        var elapsed = Stopwatch.GetTimestamp() - start;
        var after = GC.GetAllocatedBytesForCurrentThread();
        if (tick >= warmup && tick < warmup + window)
        {
            allocated += after - before;
            sampledTicks++;
        }
        slowest = Math.Max(slowest, elapsed);
        if (expected.TryGetValue(tick, out var hash))
        {
            if (hash != result.StateHash) throw new ReplayDivergenceException(tick, hash, result.StateHash);
            verified++;
        }
        var metrics = world.Metrics;
        maxEntities = Math.Max(maxEntities, metrics.Entities);
        maxTimers = Math.Max(maxTimers, metrics.Timers);
        maxUpdates = Math.Max(maxUpdates, metrics.ScheduledUpdates);
        maxModules = Math.Max(maxModules, metrics.Modules);
        maxPendingCommands = Math.Max(maxPendingCommands, metrics.PendingCommands);
        if ((tick + 1) % 20_000 == 0) world.VerifyIncrementalHash();
    }
    return new ReplayRunResult(world.Tick, world.Tick, world.StateHash, [], [], verified, submitted, 0);
}

file sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

