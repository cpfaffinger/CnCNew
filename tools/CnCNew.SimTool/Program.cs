using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

const string Usage = """
    Usage: CnCNew.SimTool <command> [arguments]
      audit-modules                                        bindings vs catalog: implemented, bound, planned targets
      audit-coverage <content-dir>                         module coverage of the compiled shipped objects (retail base data)
      replay-record <replay.bin> [--ticks N] [--population N]
                                                           record the synthetic stress scenario as a replay with checkpoints
      replay-run <replay.bin>                              run a replay, verifying its checkpoints
      hash <replay.bin>                                    run a replay and print final hash and hash-chain digest
      stress [--ticks N] [--population N] [--out replay.bin]
                                                           record + two fresh runs + snapshot continuation, leak and budget checks
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    return args[0] switch
    {
        "audit-modules" => AuditModules(),
        "audit-coverage" when args.Length >= 2 => AuditCoverage(args[1]),
        "replay-record" when args.Length >= 2 => RecordReplay(args[1], Options(args)),
        "replay-run" when args.Length >= 2 => RunReplay(args[1]),
        "hash" when args.Length >= 2 => HashReplay(args[1]),
        "stress" => Stress(Options(args), Arg(args, "--out")),
        _ => Fail(Usage),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

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

static int AuditModules()
{
    var registry = ModuleRegistry.Default;
    var table = ModuleBindingTable.Default;
    var descriptors = registry.Descriptors.ToArray();
    Console.WriteLine($"Catalog: {descriptors.Length} module types; simulation {descriptors.Count(value => value.Category == ModuleCategory.Simulation)}, presentation {descriptors.Count(value => value.Category == ModuleCategory.Presentation)}.");
    Console.WriteLine($"Implemented and bound: {table.Bindings.Length} ({string.Join(", ", table.Bindings.Select(value => value.Name))}).");
    foreach (var group in descriptors.Where(value => value.CompatibilityOnly).GroupBy(value => value.PlannedTarget).OrderBy(value => value.Key, StringComparer.Ordinal))
        Console.WriteLine($"Not implemented, planned for {group.Key}: {group.Count()}");
    return 0;
}

static (CompiledDefinitionGraph Graph, TemplateCatalog Catalog, string Fingerprint) CompileShipped(string directory)
{
    var content = ContentFileSystem.Open(directory);
    var source = new ContentSource(content);
    var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
    var graph = DefinitionCompiler.Compile(source, ModuleRegistry.Default, options);
    var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
    return (graph, catalog, DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256);
}

static int AuditCoverage(string directory)
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
    Console.WriteLine("Presentation modules (never instantiated by the simulation), objects using them:");
    foreach (var (module, objects) in report.PresentationOnly.Take(12)) Console.WriteLine($"  {module,-42} {objects,5}");

    // Instantiate every instantiable object, tick, kill: proves the compiled definitions drive the simulation.
    var players = System.Collections.Immutable.ImmutableArray.Create(new PlayerConfiguration(new CnCNew.Simulation.Ids.PlayerId(1), "P1"));
    var world = new SimulationWorld(new WorldConfiguration(new CnCNew.Simulation.Ids.WorldId(1), 1, players, "shipped-objects", fingerprint), catalog);
    var sequence = 0UL;
    foreach (var template in catalog.Templates.Where(value => value.IsInstantiable))
        world.Submit(new SpawnCommand(new CnCNew.Simulation.Ids.PlayerId(1), ++sequence, 0, template.Name, CnCNew.Simulation.Ids.PlayerId.System,
            CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero));
    world.Step();
    Console.WriteLine();
    Console.WriteLine($"Spawned {world.Metrics.Entities} of {report.Instantiable} instantiable objects ({world.Metrics.Modules} modules).");
    return world.Metrics.Entities == report.Instantiable ? 0 : 1;
}

static int RecordReplay(string path, StressOptions options)
{
    var fixture = new SyntheticFixture();
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
    var fixture = new SyntheticFixture();
    var replay = ReplayFormat.Read(File.ReadAllBytes(path));
    var result = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { VerifyIncrementalEvery = 5000 });
    Console.WriteLine($"Replay ok: {result.TicksExecuted} ticks, {result.VerifiedCheckpoints}/{replay.Checkpoints.Length} checkpoints verified, final hash {result.FinalHash:X16}.");
    return 0;
}

static int HashReplay(string path)
{
    var fixture = new SyntheticFixture();
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

    var fixture = new SyntheticFixture();
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
