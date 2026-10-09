using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CnCNew.Content;
using CnCNew.Data.Definitions;
using CnCNew.Formats;
using CnCNew.SimTool.Manifests;
using CnCNew.SimTool.Perf;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

internal delegate (CompiledDefinitionGraph Graph, TemplateCatalog Catalog, string Fingerprint) CompileShipped(string directory);

/// <summary>
/// <c>SimTool perf</c> and <c>perf-mission</c> (docs/PERFORMANCE.md): where the time of a logic tick goes. The simulation holds no clock; it only tells an
/// <see cref="ITickPhaseObserver"/> where its phases begin and end, and this tool reads the clock there. Nothing here changes the run: the final hash is
/// printed and must equal the hash of the same run without the observer.
/// </summary>
internal static class PerfCommand
{
    private const string Usage = """
        perf <content-dir> --map <virtual-map-path> (--players N | --factions A,B,..) [--ticks N] [--seed N] [--replay] [--csv FILE] [--json FILE] [--label TEXT] [--top K] [--sample N] [--no-modules] [--no-observer] [--movement [--movement-tick N]] [--vision-probe] [--vision-reference] [--prefix-probe]
        perf-mission <content-dir> --map <virtual-map-path> [--ticks N] [--difficulty easy|normal|hard] [--seed N] [--csv FILE] [--json FILE] [--label TEXT] [--top K] [--sample N] [--no-modules] [--no-observer] [--script-detail] [--movement [--movement-tick N]] [--vision-probe] [--vision-reference] [--prefix-probe]
        """;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed record Common(string Directory, string MapPath, long Ticks, string? Csv, int Top, int Sample, bool Modules, bool Observe, string? Json, string Label, bool VisionProbe, bool VisionReference)
    {
        public static Common Parse(string[] args) => new(
            args[1],
            Arg(args, "--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.\n" + Usage),
            ToolRate.Budget(args, "--ticks", 18000),
            Arg(args, "--csv"),
            int.Parse(Arg(args, "--top") ?? "15", Invariant),
            Math.Max(1, int.Parse(Arg(args, "--sample") ?? "300", Invariant)),
            !args.Contains("--no-modules"),
            !args.Contains("--no-observer"),
            Arg(args, "--json"),
            Arg(args, "--label") ?? string.Empty,
            args.Contains("--vision-probe"),
            args.Contains("--vision-reference"));

        /// <summary>The parameters every run has; two summaries with different parameters are different measurements.</summary>
        public ManifestObject Parameters() => new ManifestObject().Add("map", MapPath).Add("ticks", Ticks).Add("observer", Observe).Add("modules", Observe && Modules);
    }

    private static string? Arg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary><c>--movement</c>: installs the movement probe (path searches and the entity scans of a movement step); it needs the observer to know which unit is being updated.</summary>
    private static MovementRecorder? MovementProbe(string[] args, SimulationWorld world, TickRecorder recorder)
    {
        if (!args.Contains("--movement") || world.Navigation is not { } grid || world.PhaseObserver is null) return null;
        var movement = new MovementRecorder(world, recorder) { DumpTick = long.Parse(Arg(args, "--movement-tick") ?? "-1", Invariant) };
        grid.Probe = movement;
        return movement;
    }

    // ------------------------------------------------------------------------------------------------- skirmish

    public static int Skirmish(string[] args, CompileShipped compile)
    {
        CultureInfo.CurrentCulture = Invariant;
        var common = Common.Parse(args);
        var options = SkirmishMatchOptions.Standard with { MaxTicks = common.Ticks };
        if (Arg(args, "--seed") is { } seed) options = options with { AiSeed = uint.Parse(seed, Invariant), WorldSeed = uint.Parse(seed, Invariant) ^ 0xC0FFEE06 };
        var hardware = HardwareLine();
        Console.WriteLine("Hardware: " + hardware);
        var setup = Stopwatch.StartNew();
        var (_, catalog, fingerprint) = compile(common.Directory);
        var content = ContentFileSystem.Open(common.Directory);
        using var mapStream = content.OpenRead(common.MapPath);
        var map = SageMapDecoder.Decode(mapStream);
        var terrain = TerrainMap.Build(map, common.MapPath);
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        var wanted = Arg(args, "--factions")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = wanted is null
            ? everyone.Take(int.Parse(Arg(args, "--players") ?? "2", Invariant)).ToImmutableArray()
            : [..wanted.Select(name => everyone.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
        Console.WriteLine($"Perf on {common.MapPath} ({terrain.CellsX}x{terrain.CellsY} cells, {terrain.StartPositions.Length} start positions, terrain {terrain.Fingerprint[..16]}), definitions {fingerprint[..16]}; {chosen.Length} computer players: {string.Join(", ", chosen.Select(value => value.Faction.Name))}.");
        Console.WriteLine($"Setup (definitions, model geometry, map): {setup.Elapsed.TotalSeconds:F1} s. Budget {common.Ticks} ticks; observer {(common.Observe ? "attached" : "ABSENT (--no-observer): only the whole tick is timed")}; per-module timing {(common.Observe && common.Modules ? "on" : "off")}.");

        var session = SkirmishMatchSession.Start(options, catalog, fingerprint, terrain, map.Objects, [..chosen.Select(participant => new SkirmishSlot(participant))]);
        var world = session.World;
        var recorder = new TickRecorder(world, common.Modules) { VisionProbe = common.VisionProbe, PrefixProbe = args.Contains("--prefix-probe") };
        if (common.Observe) world.PhaseObserver = recorder;
        var movement = MovementProbe(args, world, recorder);
        if (common.VisionReference && world.Vision is { } referenceVision) referenceVision.ReferenceMode = true;
        var gcBefore = GcCounts();
        var wall = Stopwatch.StartNew();
        var chain = StateHash.Seed;
        while (!session.IsOver)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var result = session.Step();
            var elapsed = Stopwatch.GetTimestamp() - start;
            recorder.Commit(result.Tick, elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated, world.Entities.Count);
            chain = StateHash.Mix(chain, result.StateHash);
        }
        wall.Stop();
        var gcAfter = GcCounts();
        world.PhaseObserver = null;
        if (world.Navigation is { } grid) grid.Probe = null;
        var finalHash = world.StateHash;
        var summary = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = "skirmish", ["map"] = common.MapPath, ["cells"] = $"{terrain.CellsX}x{terrain.CellsY}", ["players"] = chosen.Length.ToString(Invariant),
        };
        Console.WriteLine(session.Winner is null
            ? $"Match: {recorder.Count} ticks in {wall.Elapsed.TotalSeconds:F1} s wall time, no winner (tick budget)."
            : $"Match: {recorder.Count} ticks in {wall.Elapsed.TotalSeconds:F1} s wall time, winner player {session.Winner.Value.Value} at tick {session.VictoryTick}.");
        var json = PerfSummary.Create("perf", common.Label, hardware, common.Parameters()
            .Add("factions", string.Join(",", chosen.Select(value => value.Faction.Name))).Add("aiSeed", options.AiSeed).Add("worldSeed", options.WorldSeed));
        Report(recorder, common, summary, json, gcBefore, gcAfter, [TickPhase.SkirmishAi, TickPhase.SkirmishVictory], [TickPhase.MissionScripts, TickPhase.MissionEvents]);
        movement?.Report(terrain.CellsX, recorder.Count, summary);
        var results = new ManifestObject().Add("ticks", recorder.Count).Add("finalHash", finalHash.ToString("X16")).Add("chainHash", chain.ToString("X16"))
            .Add("winner", session.Winner is { } winner ? winner.Value : 0L).Add("victoryTick", session.Winner is null ? -1L : session.VictoryTick);
        json.Add("results", results);
        Console.WriteLine($"Final hash {finalHash:X16}, chain {chain:X16} (must equal the same run without the observer; `ai-match --max-ticks {common.Ticks}` prints the same pair).");
        summary["hash"] = finalHash.ToString("X16");
        summary["chain"] = chain.ToString("X16");

        var failed = false;
        double? replayTicksPerSecond = null;
        if (args.Contains("--replay"))
        {
            // The recorded commands on a fresh world, no AI, no observer: what a client that has to catch up would run.
            var replay = session.ToReplay();
            var fresh = ReplayRunner.CreateWorld(replay.Header, catalog, terrain);
            var replayAllocated = GC.GetAllocatedBytesForCurrentThread();
            var replayGc = GcCounts();
            var watch = Stopwatch.StartNew();
            var run = ReplayRunner.Run(replay, fresh);
            watch.Stop();
            var replayGcAfter = GcCounts();
            replayTicksPerSecond = run.TicksExecuted / watch.Elapsed.TotalSeconds;
            var same = run.FinalHash == finalHash;
            failed |= !same;
            Console.WriteLine();
            Console.WriteLine($"Replay without AI and without observer: {run.TicksExecuted} ticks, {run.SubmittedCommands} commands in {watch.Elapsed.TotalSeconds:F2} s = {watch.Elapsed.TotalSeconds / run.TicksExecuted * 1e6:F1} us/tick, {replayTicksPerSecond:F0} ticks/s; "
                              + $"{(GC.GetAllocatedBytesForCurrentThread() - replayAllocated) / Math.Max(1, run.TicksExecuted)} bytes/tick, GC gen0/1/2 {replayGcAfter.Gen0 - replayGc.Gen0}/{replayGcAfter.Gen1 - replayGc.Gen1}/{replayGcAfter.Gen2 - replayGc.Gen2}.");
            Console.WriteLine($"  catch-up speed: {replayTicksPerSecond / RunRate:F1}x real time at {ToolRate.Current} (replayed ticks per second / the tick rate of this run).");
            Console.WriteLine($"  replay final hash {run.FinalHash:X16} == observed live run: {same}.");
            summary["replay_us"] = (watch.Elapsed.TotalSeconds / run.TicksExecuted * 1e6).ToString("F1", Invariant);
            summary["catchup"] = (replayTicksPerSecond.Value / RunRate).ToString("F1", Invariant);
            json.Add("replay", new ManifestObject().Add("perTickNs", PerfSummary.Nanoseconds(watch.Elapsed.TotalSeconds / run.TicksExecuted * 1e6)).Add("ticksPerSecond", (long)Math.Round(replayTicksPerSecond.Value))
                .Add("bytesPerTickMean", (GC.GetAllocatedBytesForCurrentThread() - replayAllocated) / Math.Max(1, run.TicksExecuted))
                .Add("gen0", replayGcAfter.Gen0 - replayGc.Gen0).Add("gen1", replayGcAfter.Gen1 - replayGc.Gen1).Add("gen2", replayGcAfter.Gen2 - replayGc.Gen2).Add("hashEqualsLive", same));
        }

        Console.WriteLine();
        results.Add("incrementalEqualsFull", CheckIncrementalHash(world, summary));
        var fullHash = world.StateHash;
        var snapshot = Measure(() => WorldSnapshot.Write(world), out var writeMs);
        var restored = Measure(() => WorldSnapshot.Restore(snapshot, catalog, fingerprint, terrain), out var restoreMs);
        var restoredSame = restored.StateHash == fullHash;
        failed |= !restoredSame;
        PrintSnapshot("WorldSnapshot", snapshot.Length, world.Entities.Count, writeMs, restoreMs, restoredSame, summary, json);

        Extrapolate(recorder, replayTicksPerSecond, summary);
        Console.WriteLine();
        Console.WriteLine("Probes for the three suspicions of docs/handoffs/HANDOFF-PHASE18.md item 5 (end state of this run):");
        ProbeVision(world, recorder);
        ProbeDescribe(world, recorder);
        Console.WriteLine("  (c) MissionSession.StateHash: not part of a skirmish; use perf-mission.");
        PrintSummary(summary);
        WriteJson(common, json);
        return failed ? 1 : 0;
    }

    // -------------------------------------------------------------------------------------------------- mission

    public static int Mission(string[] args, CompileShipped compile)
    {
        CultureInfo.CurrentCulture = Invariant;
        var common = Common.Parse(args);
        var options = MissionOptions.Standard with { Difficulty = Enum.Parse<ScriptDifficulty>(Arg(args, "--difficulty") ?? "Normal", true), MaxTicks = 0 };
        if (Arg(args, "--seed") is { } seed) options = options with { WorldSeed = uint.Parse(seed, Invariant) };
        var hardware = HardwareLine();
        Console.WriteLine("Hardware: " + hardware);
        var setup = Stopwatch.StartNew();
        var (_, catalog, fingerprint) = compile(common.Directory);
        var content = ContentFileSystem.Open(common.Directory);
        using var mapStream = content.OpenRead(common.MapPath);
        var map = SageMapDecoder.Decode(mapStream);
        var terrain = TerrainMap.Build(map, common.MapPath);
        Console.WriteLine($"Perf on mission {common.MapPath} ({terrain.CellsX}x{terrain.CellsY} cells), {options.Difficulty}, terrain {terrain.Fingerprint[..16]}, definitions {fingerprint[..16]}.");
        Console.WriteLine($"Setup (definitions, model geometry, map): {setup.Elapsed.TotalSeconds:F1} s. Budget {common.Ticks} ticks; observer {(common.Observe ? "attached" : "ABSENT (--no-observer): only the whole tick is timed")}; per-module timing {(common.Observe && common.Modules ? "on" : "off")}.");

        var start = Stopwatch.StartNew();
        var session = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        var world = session.World;
        Console.WriteLine($"Session start (objects placed): {start.Elapsed.TotalMilliseconds:F0} ms, {world.Entities.Count} objects, {world.Configuration.Players.Length} players.");
        var recorder = new TickRecorder(world, common.Modules) { VisionProbe = common.VisionProbe, PrefixProbe = args.Contains("--prefix-probe") };
        if (common.Observe) world.PhaseObserver = recorder;
        // Opt-in: the per-condition clock reads would otherwise be part of the MissionScripts phase of every profile.
        var scriptRecorder = args.Contains("--script-detail") ? new ScriptRecorder() : null;
        session.Engine.Observer = scriptRecorder;
        var movement = MovementProbe(args, world, recorder);
        if (common.VisionReference && world.Vision is { } referenceVision) referenceVision.ReferenceMode = true;
        var gcBefore = GcCounts();
        var wall = Stopwatch.StartNew();
        var chain = StateHash.Seed;
        // How much of the first step is the runtime compiling the code it runs for the first time (the first step is the longest of most missions).
        var jitBefore = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
        var firstStepJit = TimeSpan.Zero;
        var firstStep = TimeSpan.Zero;
        // The same loop as MissionScenario.Continue (mission-run), so that final hash and chain are comparable.
        while (world.Tick < common.Ticks && !session.IsOver)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var begin = Stopwatch.GetTimestamp();
            var result = session.Step();
            var elapsed = Stopwatch.GetTimestamp() - begin;
            if (recorder.Count == 0)
            {
                firstStepJit = System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jitBefore;
                firstStep = Stopwatch.GetElapsedTime(begin, begin + elapsed);
            }
            recorder.Commit(result.Tick, elapsed, GC.GetAllocatedBytesForCurrentThread() - allocated, world.Entities.Count);
            chain = StateHash.Mix(chain, result.StateHash);
            // Suspicion (c), outside the tick time: what one complete description of the script state costs at this point of the mission. Reading only.
            if (recorder.Count % common.Sample == 0)
            {
                var hashStart = Stopwatch.GetTimestamp();
                session.Engine.StateHash();
                recorder.ScriptHashSamples.Add((result.Tick, Stopwatch.GetTimestamp() - hashStart));
            }
        }
        wall.Stop();
        var gcAfter = GcCounts();
        world.PhaseObserver = null;
        session.Engine.Observer = null;
        if (world.Navigation is { } grid) grid.Probe = null;
        var finalHash = session.StateHash();
        var summary = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = "mission", ["map"] = common.MapPath, ["cells"] = $"{terrain.CellsX}x{terrain.CellsY}", ["players"] = world.Configuration.Players.Length.ToString(Invariant),
        };
        Console.WriteLine($"Mission: {recorder.Count} steps to tick {world.Tick} in {wall.Elapsed.TotalSeconds:F1} s wall time; outcome {session.Outcome}; scripts executed {session.Engine.ExecutedScripts}, actions {session.Engine.ExecutedActions}.");
        var json = PerfSummary.Create("perf-mission", common.Label, hardware, common.Parameters().Add("difficulty", options.Difficulty.ToString()).Add("worldSeed", options.WorldSeed));
        Report(recorder, common, summary, json, gcBefore, gcAfter, [TickPhase.MissionScripts, TickPhase.MissionEvents], [TickPhase.SkirmishAi, TickPhase.SkirmishVictory]);
        movement?.Report(terrain.CellsX, recorder.Count, summary);
        Console.WriteLine($"First step: {firstStep.TotalMilliseconds:F1} ms, of which {firstStepJit.TotalMilliseconds:F1} ms compiling methods on this thread (System.Runtime.JitInfo; a cost of the process, not of the mission).");
        summary["first_step_us"] = (firstStep.TotalMilliseconds * 1000).ToString("F1", Invariant);
        summary["first_step_jit_us"] = (firstStepJit.TotalMilliseconds * 1000).ToString("F1", Invariant);
        scriptRecorder?.Report(recorder.Count, common.Top, summary);
        var results = new ManifestObject().Add("ticks", recorder.Count).Add("finalHash", finalHash.ToString("X16")).Add("chainHash", chain.ToString("X16")).Add("outcome", session.Outcome.ToString());
        json.Add("results", results);
        Console.WriteLine($"Final hash {finalHash:X16}, chain {chain:X16} (must equal the same run without the observer; `mission-run --ticks {common.Ticks}` prints the same pair).");
        summary["hash"] = finalHash.ToString("X16");
        summary["chain"] = chain.ToString("X16");

        Console.WriteLine();
        results.Add("incrementalEqualsFull", CheckIncrementalHash(world, summary));
        var fullHash = session.StateHash();
        var snapshot = Measure(() => MissionSnapshot.Write(session), out var writeMs);
        var restored = Measure(() => MissionSnapshot.Restore(snapshot, catalog, fingerprint, terrain, map), out var restoreMs);
        var restoredSame = restored.StateHash() == fullHash;
        PrintSnapshot("MissionSnapshot", snapshot.Length, world.Entities.Count, writeMs, restoreMs, restoredSame, summary, json);

        Extrapolate(recorder, null, summary);
        Console.WriteLine();
        Console.WriteLine("Probes for the three suspicions of docs/handoffs/HANDOFF-PHASE18.md item 5 (end state of this run):");
        ProbeVision(world, recorder);
        ProbeDescribe(world, recorder);
        ProbeScriptHash(session, recorder);
        PrintSummary(summary);
        WriteJson(common, json);
        return restoredSame ? 0 : 1;
    }

    // --------------------------------------------------------------------------------------------------- report

    private static string HardwareLine()
        => $"{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}; {Environment.ProcessorCount} logical processors; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB memory; "
           + $"{RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; Server GC {System.Runtime.GCSettings.IsServerGC}; clock resolution {(1e9 / Stopwatch.Frequency).ToString("F0", Invariant)} ns.";

    private static void WriteJson(Common common, ManifestObject json)
    {
        if (common.Json is not { } file) return;
        var path = Path.GetFullPath(file);
        PerfSummary.Write(path, json);
        Console.WriteLine($"JSON: summary written to {Path.GetFileName(path)} (compare two of them with `SimTool perf-compare <old.json> <new.json>`).");
    }

    private static (int Gen0, int Gen1, int Gen2) GcCounts() => (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    private static double Us(double stopwatchTicks) => stopwatchTicks * 1e6 / Stopwatch.Frequency;

    private static T Measure<T>(Func<T> action, out double bestMilliseconds)
    {
        // Best of three: the first call pays for the JIT.
        var best = double.MaxValue;
        var value = default(T)!;
        for (var round = 0; round < 3; round++)
        {
            var start = Stopwatch.GetTimestamp();
            value = action();
            best = Math.Min(best, Us(Stopwatch.GetTimestamp() - start) / 1000);
        }
        bestMilliseconds = best;
        return value;
    }

    private readonly record struct Distribution(double Mean, double Median, double P95, double P99, double Max, double Sum)
    {
        public static Distribution Of(List<int> values)
        {
            if (values.Count == 0) return default;
            var sorted = values.ToArray();
            Array.Sort(sorted);
            long sum = 0;
            foreach (var value in sorted) sum += value;
            // Nearest rank: the smallest value with at least the given share of the ticks at or below it.
            int Rank(double share) => sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Length) - 1, 0, sorted.Length - 1)];
            return new Distribution(Us((double)sum / sorted.Length), Us(Rank(0.50)), Us(Rank(0.95)), Us(Rank(0.99)), Us(sorted[^1]), Us(sum));
        }

        public ManifestObject ToNode() => PerfSummary.Distribution(Mean, Median, P95, P99, Max);

        public string Row(string name, double totalSum)
            => $"  {name,-18} {Mean,10:F1} {Median,10:F1} {P95,10:F1} {P99,10:F1} {Max,11:F1} {(totalSum <= 0 ? 0 : 100 * Sum / totalSum),6:F1} %";
    }

    private static void Report(TickRecorder recorder, Common common, SortedDictionary<string, string> summary, ManifestObject json, (int Gen0, int Gen1, int Gen2) gcBefore, (int Gen0, int Gen1, int Gen2) gcAfter,
        TickPhase[] sessionPhases, TickPhase[] foreignPhases)
    {
        var total = Distribution.Of(recorder.Total);
        Console.WriteLine();
        Console.WriteLine($"Time per tick in microseconds over {recorder.Count} ticks (whole session step; quantiles by nearest rank):");
        Console.WriteLine($"  {"",-18} {"mean",10} {"median",10} {"95 %",10} {"99 %",10} {"max",11} {"share",8}");
        Console.WriteLine(total.Row("tick", total.Sum));
        summary["ticks"] = recorder.Count.ToString(Invariant);
        summary["mean_us"] = total.Mean.ToString("F1", Invariant);
        summary["p50_us"] = total.Median.ToString("F1", Invariant);
        summary["p95_us"] = total.P95.ToString("F1", Invariant);
        summary["p99_us"] = total.P99.ToString("F1", Invariant);
        summary["max_us"] = total.Max.ToString("F1", Invariant);
        json.Add("tick", total.ToNode());
        var phasesJson = new ManifestObject();
        var modulesJson = new ManifestObject();
        json.Add("phases", phasesJson).Add("modules", modulesJson);
        var warm = Math.Min(300, recorder.Count / 2);
        if (recorder.Count > warm && warm > 0)
            Console.WriteLine($"  mean without the first {warm} ticks (JIT warm-up): {Us(recorder.Total.Skip(warm).Average(value => (double)value)):F1} us.");

        if (recorder.Observed)
        {
            double phaseSum = 0, worldSum = 0;
            foreach (var phase in TickRecorder.Phases)
            {
                if (foreignPhases.Contains(phase)) continue;
                var distribution = Distribution.Of(recorder.Phase(phase));
                phaseSum += distribution.Sum;
                if (!sessionPhases.Contains(phase)) worldSum += distribution.Sum;
                Console.WriteLine(distribution.Row((sessionPhases.Contains(phase) ? "+ " : "  ") + phase, total.Sum));
                summary[$"phase_{phase}_us"] = distribution.Mean.ToString("F1", Invariant);
                phasesJson.Add(phase.ToString(), distribution.ToNode());
            }
            Console.WriteLine($"  {"  (unattributed)",-18} {(total.Sum - phaseSum) / recorder.Count,10:F1} {"",10} {"",10} {"",10} {"",11} {100 * (total.Sum - phaseSum) / total.Sum,6:F1} %");
            Console.WriteLine($"  world step alone (the phases without '+'): mean {worldSum / recorder.Count:F1} us; '+' marks the session's own parts; unattributed = between the phases, including the observer's own clock reads.");
            summary["world_mean_us"] = (worldSum / recorder.Count).ToString("F1", Invariant);
            phasesJson.Add("(unattributed)", new ManifestObject().Add("meanNs", PerfSummary.Nanoseconds((total.Sum - phaseSum) / recorder.Count)));
            phasesJson.Add("(world)", new ManifestObject().Add("meanNs", PerfSummary.Nanoseconds(worldSum / recorder.Count)));
            if (sessionPhases.Contains(TickPhase.SkirmishAi)) summary["ai_mean_us"] = Distribution.Of(recorder.Phase(TickPhase.SkirmishAi)).Mean.ToString("F1", Invariant);
            if (sessionPhases.Contains(TickPhase.MissionScripts)) summary["scripts_mean_us"] = Distribution.Of(recorder.Phase(TickPhase.MissionScripts)).Mean.ToString("F1", Invariant);

            // The very first ticks pay for the JIT; the longest tick after them is the one that says something about the simulation.
            foreach (var from in warm > 0 && recorder.Count > warm ? new[] { 0, warm } : [0])
            {
                var longest = recorder.LongestTick(from);
                var dominant = TickRecorder.Phases.Where(phase => !foreignPhases.Contains(phase)).MaxBy(phase => recorder.Phase(phase)[longest]);
                Console.WriteLine($"Longest tick{(from == 0 ? "" : $" after the first {from}")}: step {longest} (tick {recorder.Ticks[longest]}), {Us(recorder.Total[longest]):F1} us, {recorder.Objects[longest]} objects, {recorder.Allocated[longest]} bytes allocated; "
                                  + $"dominated by {dominant} with {Us(recorder.Phase(dominant)[longest]):F1} us ({100.0 * recorder.Phase(dominant)[longest] / Math.Max(1, recorder.Total[longest]):F0} %).");
                summary[from == 0 ? "longest_phase" : "longest_phase_warm"] = dominant.ToString();
                summary[from == 0 ? "longest_us" : "longest_us_warm"] = Us(recorder.Total[longest]).ToString("F1", Invariant);
            }

            if (recorder.ModuleTiming)
            {
                var modules = recorder.ModuleStats();
                var updates = Distribution.Of(recorder.Phase(TickPhase.Updates)).Sum;
                var moduleSum = modules.Sum(value => Us(value.Ticks));
                Console.WriteLine();
                Console.WriteLine($"Update phase by module type, top {Math.Min(common.Top, modules.Count)} of {modules.Count} by total time (timed at the scheduler's dispatch; {modules.Sum(value => value.Calls)} calls = {(double)modules.Sum(value => value.Calls) / recorder.Count:F0} per tick, {100 * moduleSum / Math.Max(1, updates):F1} % of the phase inside module updates):");
                Console.WriteLine($"  {"module",-36} {"calls",12} {"total ms",11} {"mean us",9} {"max us",10} {"of phase",9} {"bytes/tick",11} {"bytes/call",11}");
                foreach (var module in modules.Take(common.Top))
                    modulesJson.Add(module.Name, new ManifestObject().Add("calls", module.Calls).Add("totalNs", PerfSummary.Nanoseconds(Us(module.Ticks))).Add("meanNs", PerfSummary.Nanoseconds(Us(module.Ticks) / module.Calls)).Add("maxNs", PerfSummary.Nanoseconds(Us(module.Max))));
                foreach (var module in modules.Take(common.Top))
                    Console.WriteLine($"  {module.Name,-36} {module.Calls,12} {Us(module.Ticks) / 1000,11:F1} {Us(module.Ticks) / module.Calls,9:F2} {Us(module.Max),10:F1} {100 * Us(module.Ticks) / Math.Max(1, updates),7:F1} % {module.Bytes / Math.Max(1, recorder.Count),11} {module.Bytes / module.Calls,11}");
            }
            else Console.WriteLine("Update phase by module type: switched off (--no-modules).");
        }
        else Console.WriteLine("  no phase split: the observer is absent.");

        long allocated = 0;
        foreach (var value in recorder.Allocated) allocated += value;
        var sortedAllocations = recorder.Allocated.ToArray();
        Array.Sort(sortedAllocations);
        Console.WriteLine();
        Console.WriteLine($"Allocations: {allocated / Math.Max(1, recorder.Count)} bytes/tick mean, {sortedAllocations[sortedAllocations.Length / 2]} median, {sortedAllocations[^1]} max; {allocated / (1024.0 * 1024.0):F1} MiB over the run "
                          + $"({allocated / (1024.0 * 1024.0) / Math.Max(1, recorder.Count) * ToolRate.Current.Time.TicksPerMinute:F0} MiB per game minute at {ToolRate.Current}); GC gen0/1/2 = {gcAfter.Gen0 - gcBefore.Gen0}/{gcAfter.Gen1 - gcBefore.Gen1}/{gcAfter.Gen2 - gcBefore.Gen2}.");
        if (recorder.Observed)
        {
            // Where the bytes come from: the allocation counter of the thread read at the phase boundaries.
            var parts = TickRecorder.Phases.Where(phase => !foreignPhases.Contains(phase) && recorder.PhaseBytes(phase) / Math.Max(1, recorder.Count) > 0)
                .OrderByDescending(recorder.PhaseBytes).Select(phase => $"{phase} {recorder.PhaseBytes(phase) / Math.Max(1, recorder.Count)}");
            Console.WriteLine($"Allocated bytes per tick by phase: {string.Join(", ", parts)}.");
            foreach (var phase in TickRecorder.Phases)
                if (!foreignPhases.Contains(phase)) summary[$"alloc_{phase}_bytes"] = (recorder.PhaseBytes(phase) / Math.Max(1, recorder.Count)).ToString(Invariant);
        }
        Console.WriteLine($"Objects: min {recorder.Objects.Min()}, max {recorder.Objects.Max()}, end {recorder.Objects[^1]}.");
        summary["alloc_bytes_per_tick"] = (allocated / Math.Max(1, recorder.Count)).ToString(Invariant);
        summary["gc"] = $"{gcAfter.Gen0 - gcBefore.Gen0}/{gcAfter.Gen1 - gcBefore.Gen1}/{gcAfter.Gen2 - gcBefore.Gen2}";
        summary["objects_min"] = recorder.Objects.Min().ToString(Invariant);
        summary["objects_max"] = recorder.Objects.Max().ToString(Invariant);
        summary["objects_end"] = recorder.Objects[^1].ToString(Invariant);
        json.Add("allocation", new ManifestObject().Add("bytesPerTickMean", allocated / Math.Max(1, recorder.Count)).Add("bytesPerTickMedian", sortedAllocations[sortedAllocations.Length / 2]).Add("bytesPerTickMax", sortedAllocations[^1])
            .Add("gen0", gcAfter.Gen0 - gcBefore.Gen0).Add("gen1", gcAfter.Gen1 - gcBefore.Gen1).Add("gen2", gcAfter.Gen2 - gcBefore.Gen2));
        json.Add("objects", new ManifestObject().Add("min", recorder.Objects.Min()).Add("max", recorder.Objects.Max()).Add("end", recorder.Objects[^1]));

        if (common.Csv is { } csv)
        {
            var path = Path.GetFullPath(csv);
            File.WriteAllText(path, recorder.ToCsv(common.Sample, foreignPhases));
            Console.WriteLine($"CSV: one row per {common.Sample} ticks written to {Path.GetFileName(path)}.");
        }
    }

    /// <summary>
    /// The incrementally maintained hash against a full recomputation (which also refreshes the caches). A difference is a state change that did not mark its
    /// entity dirty (docs/handoffs/HANDOFF-PHASE18.md, finding D8): the run's hash is then not the hash of its state, and a snapshot restores to the full one.
    /// </summary>
    private static bool CheckIncrementalHash(SimulationWorld world, SortedDictionary<string, string> summary)
    {
        var incremental = world.StateHash;
        var full = world.ComputeFullHash();
        summary["incremental_equals_full"] = incremental == full ? "yes" : "NO";
        Console.WriteLine(incremental == full
            ? $"World hash at the end: incremental {incremental:X16} equals a full recomputation."
            : $"WARNING: the world's incremental hash {incremental:X16} differs from a full recomputation {full:X16}: some state change did not mark its entity dirty (finding D8). The snapshot below is compared with the full hash.");
        return incremental == full;
    }

    private static void PrintSnapshot(string kind, int bytes, int objects, double writeMs, double restoreMs, bool same, SortedDictionary<string, string> summary, ManifestObject json)
    {
        json.Add("snapshot", new ManifestObject().Add("bytes", bytes).Add("bytesPerObject", bytes / Math.Max(1, objects)).Add("writeNs", PerfSummary.Nanoseconds(writeMs * 1000)).Add("restoreNs", PerfSummary.Nanoseconds(restoreMs * 1000)).Add("restoredHashEqual", same));
        Console.WriteLine($"{kind} at the end: {bytes} bytes for {objects} objects ({bytes / Math.Max(1, objects)} bytes/object); write {writeMs:F1} ms, restore {restoreMs:F1} ms (best of three each); restored hash equal: {same}.");
        summary["snapshot_bytes"] = bytes.ToString(Invariant);
        summary["snapshot_bytes_per_object"] = (bytes / Math.Max(1, objects)).ToString(Invariant);
        summary["snapshot_write_ms"] = writeMs.ToString("F1", Invariant);
        summary["snapshot_restore_ms"] = restoreMs.ToString("F1", Invariant);
    }

    private static void Extrapolate(TickRecorder recorder, double? replayTicksPerSecond, SortedDictionary<string, string> summary)
    {
        var mean = Distribution.Of(recorder.Total).Mean;
        var basis = replayTicksPerSecond is { } rate ? 1e6 / rate : mean;
        Console.WriteLine();
        // Measured at the rate of this run, not extrapolated to another one: a run at another rate does other work per tick.
        Console.WriteLine($"Real time at {ToolRate.Current}: live step {mean:F1} us x {RunRate} ticks per second = {mean * RunRate / 1e4:F1} % of one core; "
                          + $"catch-up {1e6 / (basis * RunRate):F1}x real time ({(replayTicksPerSecond is null ? "from the live step including the session's own parts; run with --replay for the figure without AI" : "from the replay without AI")}).");
        summary["tick_rate"] = RunRate.ToString(Invariant);
        summary["core_percent"] = (mean * RunRate / 1e4).ToString("F1", Invariant);
        if (!summary.ContainsKey("catchup")) summary["catchup_live"] = (1e6 / (basis * RunRate)).ToString("F1", Invariant);
    }

    /// <summary>Ticks per second of this run (<c>--tick-rate</c>): what the real-time figures (catch-up, share of a core) are measured against.</summary>
    private static long RunRate => ToolRate.Current.TicksPerSecond;

    private static void PrintSummary(SortedDictionary<string, string> summary)
    {
        Console.WriteLine();
        Console.WriteLine("summary: " + string.Join(";", summary.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    // --------------------------------------------------------------------------------------------------- probes

    /// <summary>(a) "The vision hash is recomputed every three ticks over every cell for every player."</summary>
    private static void ProbeVision(SimulationWorld world, TickRecorder recorder)
    {
        if (world.Vision is not { } vision)
        {
            Console.WriteLine("  (a) vision: the world has no terrain, so no vision map.");
            return;
        }
        var cells = (long)vision.CellsX * vision.CellsY * vision.PlayerSlots;
        // Read before the probe below adds its own calls.
        var counters = vision.Counters;
        long counted = counters.Refreshes, slotsUnchanged = counters.SlotsUnchanged, slotsPatched = counters.SlotsPatched, slotsRedrawn = counters.SlotsRedrawn, circlesSkipped = counters.CirclesSkipped, cleared = counters.CellsCleared, circles = counters.Circles, revealed = counters.CellsRevealed, tested = counters.CellsTested;
        long hashCalls = counters.HashCalls, hashUnchanged = counters.HashUnchanged, compared = counters.BytesCompared, hashed = counters.BytesHashed;
        var kept = vision.CurrentHash();
        const int rounds = 20;
        ulong sink = 0;
        var start = Stopwatch.GetTimestamp();
        for (var round = 0; round < rounds; round++) sink = vision.Hash();
        var perCall = Us(Stopwatch.GetTimestamp() - start) / rounds;
        Console.WriteLine($"  (a) vision: {vision.CellsX}x{vision.CellsY} cells x {vision.PlayerSlots} slots (players + system) = {cells} cell bytes. VisionMap.Hash() alone (the full pass, the definition of the value), {rounds} calls on the end state: {perCall:F1} us per call = {perCall * 1000 / cells:F2} ns per cell byte (check {sink:X16}).");
        Console.WriteLine($"      the kept hash (VisionMap.CurrentHash) on the end state {(kept == sink ? "equals the full pass" : $"is {kept:X16}: WARNING, it DIFFERS from the full pass")}; mode: {(vision.ReferenceMode ? "REFERENCE (--vision-reference: the original computation over every cell)" : "incremental")}.");
        if (counted > 0 && hashCalls > 0)
            Console.WriteLine(string.Create(Invariant, $"      counters (deterministic): {counted} refreshes; per refresh {(double)cleared / counted:F0} cell bytes cleared ({100.0 * cleared / counted / cells:F1} % of the table), {(double)circles / counted:F1} circles ({(double)circlesSkipped / counted:F1} of them not drawn in full; of the slot refreshes {100.0 * slotsUnchanged / counted / vision.PlayerSlots:F1} % left alone, {100.0 * slotsPatched / counted / vision.PlayerSlots:F1} % patched, {100.0 * slotsRedrawn / counted / vision.PlayerSlots:F1} % redrawn), {(double)tested / counted:F0} radius tests, {(double)revealed / counted:F0} cell bytes written; ")
                              + string.Create(Invariant, $"{hashCalls} hash calls, {hashUnchanged} of them without a changed byte ({100.0 * hashUnchanged / hashCalls:F1} %); per call {(double)hashed / hashCalls:F0} bytes mixed ({100.0 * hashed / hashCalls / cells:F1} % of the table) and {(double)compared / hashCalls:F0} bytes compared."));
        if (recorder.VisionProbe && recorder.VisionProbeRefreshes > 0)
            Console.WriteLine(string.Create(Invariant, $"      probe (--vision-probe, its time is in 'unattributed'): {recorder.VisionProbeRefreshes} refreshes, {recorder.VisionProbeUnchanged} left every cell byte as it was; changed cell bytes per refresh: mean {(double)recorder.VisionProbeChanged / recorder.VisionProbeRefreshes:F1}, maximum {recorder.VisionProbeChangedMax}; ")
                              + $"kept hash against the full pass after every refresh: {(recorder.VisionProbeMismatches == 0 ? "always equal" : $"WARNING, {recorder.VisionProbeMismatches} MISMATCHES")}.");
        if (recorder.VisionProbe && recorder.VisionProbeRefreshes > 1)
            Console.WriteLine(string.Create(Invariant, $"      prototype of a two-level hash (NOT the value in use; one chain per row, one chain over the {(long)vision.CellsY * vision.PlayerSlots} row values): changed rows per refresh mean {(double)recorder.VisionProbeChangedRows / (recorder.VisionProbeRefreshes - 1):F1}; hashing them again plus the chain over the row values took {Us((double)recorder.VisionProbePrototypeTicks / (recorder.VisionProbeRefreshes - 1)):F1} us per refresh (check {recorder.VisionProbePrototypeSink:X16})."));
        if (!recorder.Observed) return;
        var total = Distribution.Of(recorder.Total).Sum;
        var hashing = recorder.Phase(TickPhase.VisionHash);
        var reveal = recorder.Phase(TickPhase.Vision);
        var refreshes = 0;
        long hashTicks = 0, revealTicks = 0;
        for (var index = 0; index < recorder.Count; index++)
        {
            if (recorder.Ticks[index] % world.VisionInterval != 0) continue;
            refreshes++;
            hashTicks += hashing[index];
            revealTicks += reveal[index];
        }
        if (refreshes == 0) return;
        Console.WriteLine($"      in the run: {refreshes} refreshes in {recorder.Count} ticks (every {world.VisionInterval} ticks); per refresh the hash phase took {Us((double)hashTicks / refreshes):F1} us and the reveal phase "
                          + $"(forget what was visible, then one circle per looking object and viewer) {Us((double)revealTicks / refreshes):F1} us; "
                          + $"share of all tick time: hash {100 * Us(hashTicks) / total:F1} %, reveal {100 * Us(revealTicks) / total:F1} %.");
    }

    /// <summary>(b) "Describe hashes template, module and tag names character by character."</summary>
    private static void ProbeDescribe(SimulationWorld world, TickRecorder recorder)
    {
        var entities = world.Entities.OrderBy(entity => entity.Id.Value).ToArray();
        if (entities.Length == 0)
        {
            Console.WriteLine("  (b) describe: no objects at the end of the run.");
            return;
        }
        var counter = new CountingWriter();
        foreach (var entity in entities) entity.Describe(counter);
        var words = counter.Words;
        var textWords = counter.TextCharacters + counter.Texts;
        Console.WriteLine($"  (b) describe: {entities.Length} objects, {world.Metrics.Modules} modules; one description of all of them feeds {words} words into the hash, of which {textWords} ({100.0 * textWords / words:F1} %) come from {counter.Texts} text values "
                          + $"(one word per character plus the length): {counter.Describe()}.");
        const int rounds = 20;
        var real = new HashStateWriter();
        var lengthOnly = new LengthOnlyHashWriter();
        // Interleaved so that neither variant gets the warmer cache.
        long realTicks = 0, lengthTicks = 0;
        for (var round = 0; round < rounds + 2; round++)
        {
            var start = Stopwatch.GetTimestamp();
            foreach (var entity in entities) { real.Reset(); entity.Describe(real); }
            var middle = Stopwatch.GetTimestamp();
            foreach (var entity in entities) { lengthOnly.Reset(); entity.Describe(lengthOnly); }
            var end = Stopwatch.GetTimestamp();
            if (round < 2) continue;
            realTicks += middle - start;
            lengthTicks += end - middle;
        }
        var realNs = Us(realTicks) * 1000 / rounds / entities.Length;
        var lengthNs = Us(lengthTicks) * 1000 / rounds / entities.Length;
        Console.WriteLine($"      hashing every object once ({rounds} rounds): {realNs:F0} ns per object with the real hash writer, {lengthNs:F0} ns with a writer that mixes only the length of a text: the characters cost {realNs - lengthNs:F0} ns per object = {100 * (realNs - lengthNs) / realNs:F0} % of describing it.");
        ProbeDescribeByModule(entities);
        if (!recorder.Observed) return;
        var total = Distribution.Of(recorder.Total).Sum;
        var hash = Distribution.Of(recorder.Phase(TickPhase.Hash));
        var dirty = recorder.Dirty.Count == 0 ? 0 : recorder.Dirty.Average(value => (double)value);
        Console.WriteLine($"      in the run: the hash phase took {hash.Mean:F1} us per tick ({100 * hash.Sum / total:F1} % of all tick time) for {dirty:F1} touched objects per tick on average ({100 * dirty / Math.Max(1.0, recorder.Objects.Average(value => (double)value)):F0} % of the objects) "
                          + $"= {(dirty <= 0 ? 0 : hash.Mean * 1000 / dirty):F0} ns per touched object; by the ratio above about {hash.Mean * (realNs - lengthNs) / realNs:F1} us per tick ({100 * hash.Sum / total * (realNs - lengthNs) / realNs:F1} % of all tick time) go into text characters.");
        if (recorder.PrefixProbe)
        {
            var (moved, still) = (recorder.PrefixMoved, recorder.PrefixStill);
            Console.WriteLine(string.Create(Invariant, $"      prefix probe (pairs of consecutive ticks every 250): {recorder.PrefixSame} object samples unchanged; {moved.Objects} changed in their first words (position: they moved), {moved.Words} words, {moved.Prefix} of them before the first change; "
                              + $"{still.Objects} changed later, {still.Words} words, {still.Prefix} of them ({(still.Words == 0 ? 0 : 100.0 * still.Prefix / still.Words):F1} %) before the first change; "
                              + $"of all words of changed objects {(moved.Words + still.Words == 0 ? 0 : 100.0 * (moved.Prefix + still.Prefix) / (moved.Words + still.Words)):F1} % lie before the first change."));
            var parts = recorder.Parts;
            var changedObjects = Math.Max(1, moved.Objects + still.Objects);
            Console.WriteLine(string.Create(Invariant, $"      the changed objects taken apart: the object's own fields ({(double)parts.HeadWords / changedObjects:F1} words per object) changed in {100.0 * parts.HeadChanged / changedObjects:F1} % of them; "
                              + $"of {(double)parts.Modules / changedObjects:F1} modules per object {(double)parts.ModulesChanged / changedObjects:F2} changed in their own state ({(double)parts.ChangedModuleWords / changedObjects:F1} of {(double)parts.ModuleWords / changedObjects:F1} module-state words per object); "
                              + $"the rest of the {(double)(moved.Words + still.Words) / changedObjects:F0} words per object are module frames (index, name, tag)."));
            var single = recorder.SingleModuleChanges;
            Console.WriteLine(string.Create(Invariant, $"      {single.Values.Sum()} of the changed object samples ({100.0 * single.Values.Sum() / changedObjects:F1} %) changed in exactly one module and not in their own fields: "
                              + $"{string.Join(", ", single.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(10).Select(pair => pair.Key + " " + pair.Value.ToString(Invariant)))}."));
            var only = recorder.OnlyWakeMoved;
            Console.WriteLine(string.Create(Invariant, $"      of the {moved.Objects + still.Objects} changed object samples {only.Objects} ({(moved.Objects + still.Objects == 0 ? 0 : 100.0 * only.Objects / (moved.Objects + still.Objects)):F1} %) changed in nothing but wake ticks that moved from this tick to the next "
                              + $"({only.Words} of {moved.Words + still.Words} words = {(moved.Words + still.Words == 0 ? 0 : 100.0 * only.Words / (moved.Words + still.Words)):F1} %): modules that ran, changed nothing and asked to run again."));
        }
        var work = recorder.HashWork;
        var steps = Math.Max(1, recorder.Count);
        Console.WriteLine(string.Create(Invariant, $"      counters of the incremental hash over the run (deterministic): {work.Entities} objects described ({(double)work.Entities / steps:F1} per tick), {work.Words} words folded ({(double)work.Words / steps:F0} per tick, "
                          + $"{(work.Entities == 0 ? 0 : (double)work.Words / work.Entities):F0} per object), {work.Unchanged} of the described objects ({(work.Entities == 0 ? 0 : 100.0 * work.Unchanged / work.Entities):F2} %) came out with the hash they had; "
                          + $"{(work.Words == 0 ? 0 : hash.Sum * 1000 / work.Words):F2} ns per word; the hash phase allocated {recorder.HashAllocated} bytes ({(double)recorder.HashAllocated / steps:F1} per tick)."));
    }

    /// <summary>
    /// The two halves of the hash phase on the end state, in a warm loop: writing the words of every object down, and folding them (in one chain, and side by side as the
    /// incremental hash does). Then, per module type, what one Describe writes and allocates: counted, not timed (one call is far shorter than the clock's resolution).
    /// </summary>
    private static void ProbeDescribeByModule(Entity[] entities)
    {
        const int repeats = 200;
        var writer = new WordStateWriter();
        var byName = new SortedDictionary<string, (long Modules, long Words, long Bytes)>(StringComparer.Ordinal);
        long wholeTicks = 0, wholeWords = 0, foldTicks = 0, moduleWords = 0;
        foreach (var entity in entities)
        {
            for (var warm = 0; warm < 3; warm++) { writer.Reset(); entity.Describe(writer); }
            var start = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < repeats; repeat++) { writer.Reset(); entity.Describe(writer); }
            wholeTicks += Stopwatch.GetTimestamp() - start;
            wholeWords += writer.Count;
            ulong sink = 0;
            start = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < repeats; repeat++) sink += StateHash.Fold(StateHash.Seed, writer.Words);
            foldTicks += Stopwatch.GetTimestamp() - start;
            GC.KeepAlive(sink);
            foreach (var module in entity.Modules)
            {
                writer.Reset();
                module.Describe(writer);
                var before = GC.GetAllocatedBytesForCurrentThread();
                writer.Reset();
                module.Describe(writer);
                var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                var entry = byName.GetValueOrDefault(module.ModuleName);
                byName[module.ModuleName] = (entry.Modules + 1, entry.Words + writer.Count, entry.Bytes + bytes);
                moduleWords += writer.Count;
            }
        }
        // The same words folded the way the incremental hash folds them: batches of objects, four chains side by side.
        long streamTicks = 0;
        var ends = new int[64];
        var hashes = new ulong[64];
        for (var from = 0; from < entities.Length; from += ends.Length)
        {
            var count = Math.Min(ends.Length, entities.Length - from);
            writer.Reset();
            for (var index = 0; index < count; index++)
            {
                entities[from + index].Describe(writer);
                ends[index] = writer.Count;
            }
            StateHash.FoldStreams(writer.Words, ends.AsSpan(0, count), hashes.AsSpan(0, count));
            var start = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < repeats; repeat++) StateHash.FoldStreams(writer.Words, ends.AsSpan(0, count), hashes.AsSpan(0, count));
            streamTicks += Stopwatch.GetTimestamp() - start;
        }
        // Prototype for docs/PERF-HASH.md (a different construction, not the hash of this build): module name and tag as one prepared word each instead of one word per character.
        long shortWriteTicks = 0, shortFoldTicks = 0, shortWords = 0;
        var compact = new CompactNameWriter();
        for (var from = 0; from < entities.Length; from += ends.Length)
        {
            var count = Math.Min(ends.Length, entities.Length - from);
            for (var warm = 0; warm < 3; warm++)
            {
                compact.Reset();
                for (var index = 0; index < count; index++) entities[from + index].Describe(compact);
            }
            var start = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < repeats; repeat++)
            {
                compact.Reset();
                for (var index = 0; index < count; index++)
                {
                    entities[from + index].Describe(compact);
                    ends[index] = compact.Count;
                }
            }
            shortWriteTicks += Stopwatch.GetTimestamp() - start;
            shortWords += compact.Count;
            start = Stopwatch.GetTimestamp();
            for (var repeat = 0; repeat < repeats; repeat++) StateHash.FoldStreams(compact.Words, ends.AsSpan(0, count), hashes.AsSpan(0, count));
            shortFoldTicks += Stopwatch.GetTimestamp() - start;
        }
        double PerObject(long ticks) => Us(ticks) * 1000 / repeats / entities.Length;
        double PerWord(long ticks) => wholeWords == 0 ? 0 : Us(ticks) * 1000 / repeats / wholeWords;
        Console.WriteLine(string.Create(Invariant, $"      the two halves, warm ({repeats} repeats per object): writing the words down {PerObject(wholeTicks):F0} ns per object ({(double)wholeWords / entities.Length:F0} words, {PerWord(wholeTicks):F2} ns per word); "
                          + $"folding them in one chain {PerObject(foldTicks):F0} ns per object ({PerWord(foldTicks):F2} ns per word), side by side as the incremental hash does (StateHash.FoldStreams, batches of {ends.Length} objects) {PerObject(streamTicks):F0} ns per object ({PerWord(streamTicks):F2} ns per word)."));
        Console.WriteLine(string.Create(Invariant, $"      prototype, NOT this build's hash (docs/PERF-HASH.md): module name and tag as one word each leave {(double)shortWords / entities.Length:F0} words per object; writing them down {PerObject(shortWriteTicks):F0} ns per object "
                          + $"(through the plain interface, no prepared frames), folding side by side {PerObject(shortFoldTicks):F0} ns per object."));
        Console.WriteLine(string.Create(Invariant, $"      of the {wholeWords} words {moduleWords} ({100.0 * moduleWords / Math.Max(1, wholeWords):F1} %) are the modules' own state; module types by what one Describe allocates, then by words:"));
        Console.WriteLine("      module type                              modules   words/call  bytes/call");
        foreach (var (name, value) in byName.OrderByDescending(pair => pair.Value.Bytes).ThenByDescending(pair => pair.Value.Words).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(12))
            Console.WriteLine(string.Create(Invariant, $"      {name,-40} {value.Modules,7} {(double)value.Words / value.Modules,12:F1} {(double)value.Bytes / value.Modules,11:F1}"));
    }

    /// <summary>(c) "MissionSession.StateHash() describes the script state completely every time."</summary>
    private static void ProbeScriptHash(MissionSession session, TickRecorder recorder)
    {
        var counter = new CountingWriter();
        session.Engine.Describe(counter);
        const int rounds = 20;
        ulong sink = 0;
        session.Engine.StateHash();
        var start = Stopwatch.GetTimestamp();
        for (var round = 0; round < rounds; round++) sink = session.Engine.StateHash();
        var engine = Us(Stopwatch.GetTimestamp() - start) / rounds;
        start = Stopwatch.GetTimestamp();
        ulong worldSink = 0;
        for (var round = 0; round < rounds; round++) worldSink = session.World.StateHash;
        var world = Us(Stopwatch.GetTimestamp() - start) / rounds;
        var before = GC.GetAllocatedBytesForCurrentThread();
        session.Engine.StateHash();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var mean = Distribution.Of(recorder.Total).Mean;
        Console.WriteLine($"  (c) script state: one ScriptEngine.Describe feeds {counter.Words} words into the hash ({counter.TextCharacters + counter.Texts} of them from {counter.Texts} text values); ScriptEngine.StateHash() costs {engine:F1} us and allocates {allocated} bytes per call "
                          + $"({rounds} calls, nothing changed in between: there is no incremental path), the world's incremental hash with nothing touched {world:F2} us (check {sink:X16}/{worldSink:X16}).");
        Console.WriteLine($"      The tick time above contains no such call (MissionSession.Step returns the world's hash only); a caller that wants the combined hash every tick would add {engine:F1} us to a mean tick of {mean:F1} us ({100 * engine / Math.Max(0.001, mean):F0} %).");
        if (recorder.ScriptHashSamples.Count > 0)
            Console.WriteLine($"      over the run ({recorder.ScriptHashSamples.Count} samples, one call each, outside the tick time): median {Us(recorder.ScriptHashSamples.Select(value => value.Elapsed).Order().ElementAt(recorder.ScriptHashSamples.Count / 2)):F1} us, min {Us(recorder.ScriptHashSamples.Min(value => value.Elapsed)):F1} us, max {Us(recorder.ScriptHashSamples.Max(value => value.Elapsed)):F1} us, "
                              + $"first {Us(recorder.ScriptHashSamples[0].Elapsed):F1} us (tick {recorder.ScriptHashSamples[0].Tick}), last {Us(recorder.ScriptHashSamples[^1].Elapsed):F1} us (tick {recorder.ScriptHashSamples[^1].Tick}).");
    }

    /// <summary>
    /// Prototype writer for the proposal in docs/PERF-HASH.md: like <see cref="WordStateWriter"/>, but the texts named "name" and "tag" (module name and tag) become one word
    /// each, as if their hash had been prepared. The result is a different hash construction; it is measured here, never used.
    /// </summary>
    private sealed class CompactNameWriter : IStateWriter
    {
        private ulong[] _words = new ulong[65536];
        private int _count;
        public int Count => _count;
        public ReadOnlySpan<ulong> Words => _words.AsSpan(0, _count);
        public bool KeepsNames => false;
        public void Reset() => _count = 0;

        private void Add(ulong word)
        {
            if (_count == _words.Length) Array.Resize(ref _words, _words.Length * 2);
            _words[_count++] = word;
        }

        public void BeginObject(string? name) => Add(0xB1);
        public void EndObject() => Add(0xE1);
        public void BeginArray(string name) => Add(0xB2);
        public void EndArray() => Add(0xE2);
        public void Bool(string name, bool value) => Add(value ? 0xF1UL : 0xF0UL);
        public void Int(string name, long value) => Add(unchecked((ulong)value));
        public void UInt(string name, ulong value) => Add(value);
        public void Fixed(string name, Fixed value) => Add(unchecked((ulong)value.Raw));

        public void Text(string name, string value)
        {
            if (name is "name" or "tag")
            {
                Add((ulong)value.Length);
                return;
            }
            Add((ulong)value.Length);
            foreach (var character in value) Add(character);
        }
    }

    /// <summary>Counts what a description feeds into <see cref="HashStateWriter"/>: one word per call, a text one word for its length and one per character.</summary>
    private sealed class CountingWriter : IStateWriter
    {
        private readonly SortedDictionary<string, (long Values, long Characters)> _texts = new(StringComparer.Ordinal);
        public long Words { get; private set; }
        public long Texts { get; private set; }
        public long TextCharacters { get; private set; }

        public void BeginObject(string? name) => Words++;
        public void EndObject() => Words++;
        public void BeginArray(string name) => Words++;
        public void EndArray() => Words++;
        public void Bool(string name, bool value) => Words++;
        public void Int(string name, long value) => Words++;
        public void UInt(string name, ulong value) => Words++;
        public void Fixed(string name, Fixed value) => Words++;

        public void Text(string name, string value)
        {
            Words += 1 + value.Length;
            Texts++;
            TextCharacters += value.Length;
            var entry = _texts.GetValueOrDefault(name);
            _texts[name] = (entry.Values + 1, entry.Characters + value.Length);
        }

        /// <summary>The text fields by key, largest first.</summary>
        public string Describe()
            => string.Join(", ", _texts.OrderByDescending(pair => pair.Value.Characters).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(6).Select(pair => $"\"{pair.Key}\" {pair.Value.Values} values / {pair.Value.Characters} characters"));
    }

    /// <summary><see cref="HashStateWriter"/> with one difference: a text contributes its length only. The gap to the real writer is the cost of the characters.</summary>
    private sealed class LengthOnlyHashWriter : IStateWriter
    {
        private ulong _hash = StateHash.Seed;
        public void Reset() => _hash = StateHash.Seed;
        public void BeginObject(string? name) => _hash = StateHash.Mix(_hash, 0xB1);
        public void EndObject() => _hash = StateHash.Mix(_hash, 0xE1);
        public void BeginArray(string name) => _hash = StateHash.Mix(_hash, 0xB2);
        public void EndArray() => _hash = StateHash.Mix(_hash, 0xE2);
        public void Bool(string name, bool value) => _hash = StateHash.Mix(_hash, value ? 0xF1UL : 0xF0UL);
        public void Int(string name, long value) => _hash = StateHash.Mix(_hash, unchecked((ulong)value));
        public void UInt(string name, ulong value) => _hash = StateHash.Mix(_hash, value);
        public void Fixed(string name, Fixed value) => _hash = StateHash.Mix(_hash, unchecked((ulong)value.Raw));
        public void Text(string name, string value) => _hash = StateHash.Mix(_hash, (ulong)value.Length);
    }
}

/// <summary>Reads the clock at the phase boundaries the world reports and keeps one value per tick and phase (in clock units; converted when reported).</summary>
internal sealed class TickRecorder(SimulationWorld world, bool moduleTiming) : ITickPhaseObserver
{
    public static readonly TickPhase[] Phases = Enum.GetValues<TickPhase>();

    private sealed class ModuleStat
    {
        public long Calls;
        public long Ticks;
        public long Max;
        public long Bytes;
    }

    private readonly long[] _current = new long[Phases.Length];
    private readonly long[] _phaseBytes = new long[Phases.Length];
    private long _phaseStartBytes;
    private long _updateStartBytes;
    private readonly List<int>[] _phases = [..Phases.Select(_ => new List<int>())];
    // Keyed by the name's reference (cheap at the dispatch point); names that are equal but distinct instances are merged when reported.
    private readonly Dictionary<string, ModuleStat> _modules = new(ReferenceEqualityComparer.Instance);
    private long _phaseStart;
    private long _updateStart;
    private int _dirty;
    private bool _sawPhase;
    private long _hashAllocationStart;

    /// <summary>Bytes allocated inside the hash phase over the run.</summary>
    public long HashAllocated { get; private set; }

    /// <summary>The world's own counters of the incremental hash at the end of the last hash phase.</summary>
    public HashWork HashWork { get; private set; }

    public bool ModuleTiming { get; } = moduleTiming;
    public bool Observed { get; private set; }
    public List<long> Ticks { get; } = [];
    public List<int> Total { get; } = [];
    public List<long> Allocated { get; } = [];
    public List<int> Objects { get; } = [];
    public List<int> Dirty { get; } = [];
    public List<(long Tick, long Elapsed)> ScriptHashSamples { get; } = [];
    public int Count => Total.Count;
    public List<int> Phase(TickPhase phase) => _phases[(int)phase];

    /// <summary>Index of the longest step at or after <paramref name="from"/>.</summary>
    public int LongestTick(int from)
    {
        var best = Math.Min(from, Total.Count - 1);
        for (var index = best + 1; index < Total.Count; index++)
            if (Total[index] > Total[best]) best = index;
        return best;
    }

    public void PhaseStarted(TickPhase phase)
    {
        _sawPhase = true;
        // What the hash phase is about to describe again; read before it clears the list.
        if (phase == TickPhase.Hash)
        {
            _dirty = world.DirtyEntityCount;
            _hashAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        }
        _phaseStartBytes = GC.GetAllocatedBytesForCurrentThread();
        _phaseStart = Stopwatch.GetTimestamp();
    }

    public void PhaseEnded(TickPhase phase)
    {
        _current[(int)phase] += Stopwatch.GetTimestamp() - _phaseStart;
        _phaseBytes[(int)phase] += GC.GetAllocatedBytesForCurrentThread() - _phaseStartBytes;
        if (VisionProbe && phase == TickPhase.VisionHash && world.Vision is { } vision && world.Tick % world.VisionInterval == 0) ProbeVisionRefresh(vision);
        if (phase != TickPhase.Hash) return;
        HashAllocated += GC.GetAllocatedBytesForCurrentThread() - _hashAllocationStart;
        // Read here, before the probes at the end of the run ask for the hash again.
        HashWork = world.HashWorkDone;
        if (PrefixProbe && world.Tick % PrefixProbeInterval is 0 or 1) SamplePrefix(world.Tick % PrefixProbeInterval == 1);
    }

    /// <summary>Bytes allocated inside a phase over the whole run (the phases of a tick do not nest).</summary>
    public long PhaseBytes(TickPhase phase) => _phaseBytes[(int)phase];

    /// <summary>The module whose update is running right now (<see langword="null"/> between updates).</summary>
    public SimModule? Current { get; private set; }

    /// <summary>With <c>--vision-probe</c>: after every refresh, counts the cell bytes that differ from the refresh before and compares the kept hash with the full pass.</summary>
    public bool VisionProbe { get; init; }
    public long VisionProbeRefreshes { get; private set; }
    public long VisionProbeUnchanged { get; private set; }
    public long VisionProbeChanged { get; private set; }
    public long VisionProbeChangedMax { get; private set; }
    public long VisionProbeMismatches { get; private set; }
    /// <summary>Rows (of one slot) that differed from the refresh before, summed over the probed refreshes.</summary>
    public long VisionProbeChangedRows { get; private set; }
    /// <summary>Clock units the prototype of a two-level hash took (changed rows hashed again, then one chain over the row hashes), summed.</summary>
    public long VisionProbePrototypeTicks { get; private set; }
    private byte[][]? _visionBefore;
    private ulong[][]? _prototypeRows;
    private readonly List<(int Slot, int Row)> _changedRows = [];

    private void ProbeVisionRefresh(CnCNew.Simulation.Terrain.VisionMap vision)
    {
        var first = _visionBefore is null;
        _visionBefore ??= [..Enumerable.Range(0, vision.PlayerSlots).Select(slot => new byte[vision.CellBytes(slot).Length])];
        _prototypeRows ??= [..Enumerable.Range(0, vision.PlayerSlots).Select(_ => new ulong[vision.CellsY])];
        long changed = 0;
        _changedRows.Clear();
        for (var slot = 0; slot < vision.PlayerSlots; slot++)
        {
            var now = vision.CellBytes(slot);
            var before = _visionBefore[slot];
            if (!first && now.SequenceEqual(before)) continue;
            for (var row = 0; row < vision.CellsY; row++)
            {
                var nowRow = now.Slice(row * vision.CellsX, vision.CellsX);
                var beforeRow = before.AsSpan(row * vision.CellsX, vision.CellsX);
                if (!first && nowRow.SequenceEqual(beforeRow)) continue;
                _changedRows.Add((slot, row));
                for (var index = 0; index < nowRow.Length; index++)
                    if (nowRow[index] != beforeRow[index]) changed++;
            }
            now.CopyTo(before);
        }
        VisionProbeRefreshes++;
        if (changed == 0) VisionProbeUnchanged++;
        VisionProbeChanged += changed;
        if (changed > VisionProbeChangedMax) VisionProbeChangedMax = changed;
        if (vision.CurrentHash() != vision.Hash()) VisionProbeMismatches++;

        // Prototype of another construction (a decision for later, see docs/PERF-VISION.md): one chain per row, one chain over the row values.
        // Timed: hashing the changed rows again and the chain over all row values. Not timed: finding the changed rows (the map already knows the touched rows).
        if (!first) VisionProbeChangedRows += _changedRows.Count;
        var start = Stopwatch.GetTimestamp();
        foreach (var (slot, row) in _changedRows)
        {
            var cells = vision.CellBytes(slot).Slice(row * vision.CellsX, vision.CellsX);
            var hash = StateHash.Mix(StateHash.Seed, ((ulong)slot << 32) | (uint)row);
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(cells);
            foreach (var word in words) hash = StateHash.Mix(hash, word);
            ulong rest = 0;
            for (int index = words.Length * 8, filled = 0; index < cells.Length; index++, filled++) rest |= (ulong)cells[index] << (filled * 8);
            _prototypeRows[slot][row] = StateHash.Mix(hash, rest);
        }
        var top = StateHash.Seed;
        foreach (var rows in _prototypeRows)
            foreach (var value in rows) top = StateHash.Mix(top, value);
        _prototypeSink ^= top;
        if (!first) VisionProbePrototypeTicks += Stopwatch.GetTimestamp() - start;
    }

    private ulong _prototypeSink;
    public ulong VisionProbePrototypeSink => _prototypeSink;

    private const int PrefixProbeInterval = 250;
    private readonly Dictionary<ulong, ulong[]> _previousWords = [];
    private readonly Dictionary<ulong, ulong[]> _previousParts = [];
    private readonly WordStateWriter _probeWords = new();
    private readonly WordStateWriter _partWords = new();

    /// <summary>With <c>--prefix-probe</c>: every 250 ticks all objects are written down in two consecutive ticks and compared (costs tick time; not for timing runs).</summary>
    public bool PrefixProbe { get; init; }

    /// <summary>Objects whose words changed from one sampled tick to the next, their words, and the words before the first changed one; split into objects that moved and others.</summary>
    public (long Objects, long Words, long Prefix) PrefixMoved, PrefixStill;
    public long PrefixSame { get; private set; }

    /// <summary>Changed objects whose only changed words are wake ticks that moved from this tick to the next (an update ran and had nothing else to say), and their words.</summary>
    public (long Objects, long Words) OnlyWakeMoved;

    /// <summary>Changed objects whose own fields did not change and exactly one module did, by that module's type.</summary>
    public SortedDictionary<string, long> SingleModuleChanges { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The changed objects taken apart: how often the object's own fields (everything before the module list) changed and how many words they are; how many of the modules changed
    /// in their own state and how many words those are; how many modules and module-state words the changed objects have in all.
    /// </summary>
    public (long HeadChanged, long HeadWords, long ModulesChanged, long ChangedModuleWords, long Modules, long ModuleWords) Parts;

    /// <summary>Head hash, head word count, then per module the hash and the word count of its own state.</summary>
    private ulong[] TakeApart(Entity entity, int totalWords)
    {
        var modules = entity.Modules;
        var parts = new ulong[2 + 2 * modules.Count];
        var rest = totalWords - 2;
        for (var index = 0; index < modules.Count; index++)
        {
            var module = modules[index];
            _partWords.Reset();
            module.Describe(_partWords);
            parts[2 + 2 * index] = StateHash.Fold(StateHash.Seed, _partWords.Words);
            parts[3 + 2 * index] = (ulong)_partWords.Count;
            // The frame of a module: BeginObject, index, the two texts with their lengths, BeginObject, and two EndObject after the state.
            rest -= 7 + module.ModuleName.Length + module.Tag.Length + _partWords.Count;
        }
        parts[0] = StateHash.Fold(StateHash.Seed, _probeWords.Words[..rest]);
        parts[1] = (ulong)rest;
        return parts;
    }

    private void SamplePrefix(bool second)
    {
        if (!second)
        {
            _previousWords.Clear();
            _previousParts.Clear();
        }
        foreach (var entity in world.Entities)
        {
            _probeWords.Reset();
            entity.Describe(_probeWords);
            var now = _probeWords.Words;
            if (!second)
            {
                _previousWords[entity.Id.Value] = now.ToArray();
                _previousParts[entity.Id.Value] = TakeApart(entity, now.Length);
                continue;
            }
            if (!_previousWords.TryGetValue(entity.Id.Value, out var before)) continue;
            var common = now.CommonPrefixLength(before);
            if (common == now.Length && common == before.Length)
            {
                PrefixSame++;
                continue;
            }
            // A description starts with BeginObject, id, template, the template name, owner, x, y, z and angle.
            var head = 4 + entity.Template.Name.Length + 5;
            ref var bucket = ref common < head ? ref PrefixMoved : ref PrefixStill;
            bucket = (bucket.Objects + 1, bucket.Words + now.Length, bucket.Prefix + common);
            // After the tick, a module that ran and wants to run again next tick has the wake tick world.Tick; before, it had the one before.
            var onlyWake = now.Length == before.Length;
            for (var index = common; onlyWake && index < now.Length; index++)
                onlyWake = now[index] == before[index] || (now[index] == (ulong)world.Tick && before[index] == now[index] - 1);
            if (onlyWake) OnlyWakeMoved = (OnlyWakeMoved.Objects + 1, OnlyWakeMoved.Words + now.Length);
            var parts = TakeApart(entity, now.Length);
            var old = _previousParts[entity.Id.Value];
            if (old.Length != parts.Length) continue;
            // Which module types are behind the objects that changed in one module only (and not in their own fields): the candidates for hashing a module change alone.
            var changedModules = 0;
            var changedIndex = -1;
            for (var index = 2; index < parts.Length; index += 2)
            {
                if (parts[index] == old[index] && parts[index + 1] == old[index + 1]) continue;
                changedModules++;
                changedIndex = (index - 2) / 2;
            }
            if (parts[0] == old[0] && changedModules == 1)
            {
                var name = entity.Modules[changedIndex].ModuleName + (onlyWake ? " (wake tick only)" : " (more than the wake tick)");
                SingleModuleChanges[name] = SingleModuleChanges.GetValueOrDefault(name) + 1;
            }
            if (parts[0] != old[0]) Parts.HeadChanged++;
            Parts.HeadWords += (long)parts[1];
            for (var index = 2; index < parts.Length; index += 2)
            {
                Parts.Modules++;
                Parts.ModuleWords += (long)parts[index + 1];
                if (parts[index] == old[index] && parts[index + 1] == old[index + 1]) continue;
                Parts.ModulesChanged++;
                Parts.ChangedModuleWords += (long)parts[index + 1];
            }
        }
    }

    public void UpdateStarted(SimModule module)
    {
        Current = module;
        if (!ModuleTiming) return;
        _updateStartBytes = GC.GetAllocatedBytesForCurrentThread();
        _updateStart = Stopwatch.GetTimestamp();
    }

    public void UpdateEnded(SimModule module)
    {
        Current = null;
        if (!ModuleTiming) return;
        var elapsed = Stopwatch.GetTimestamp() - _updateStart;
        var name = module.ModuleName;
        if (!_modules.TryGetValue(name, out var stat)) _modules[name] = stat = new ModuleStat();
        stat.Calls++;
        stat.Ticks += elapsed;
        stat.Bytes += GC.GetAllocatedBytesForCurrentThread() - _updateStartBytes;
        if (elapsed > stat.Max) stat.Max = elapsed;
    }

    /// <summary>Closes one session step: the caller measured the whole step and its allocations.</summary>
    public void Commit(long tick, long elapsed, long allocated, int objects)
    {
        if (_sawPhase) Observed = true;
        Ticks.Add(tick);
        Total.Add(checked((int)elapsed));
        Allocated.Add(allocated);
        Objects.Add(objects);
        if (_sawPhase) Dirty.Add(_dirty);
        for (var index = 0; index < _current.Length; index++)
        {
            _phases[index].Add(checked((int)_current[index]));
            _current[index] = 0;
        }
        _dirty = 0;
        _sawPhase = false;
    }

    public List<(string Name, long Calls, long Ticks, long Max, long Bytes)> ModuleStats()
        => [.._modules.GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, group.Sum(pair => pair.Value.Calls), group.Sum(pair => pair.Value.Ticks), group.Max(pair => pair.Value.Max), group.Sum(pair => pair.Value.Bytes)))
            .OrderByDescending(value => value.Item3).ThenBy(value => value.Key, StringComparer.Ordinal)];

    /// <summary>One row per <paramref name="window"/> steps: the object count at the end of the window and the means over it.</summary>
    public string ToCsv(int window, TickPhase[] omitted)
    {
        var invariant = CultureInfo.InvariantCulture;
        var phases = Phases.Where(phase => !omitted.Contains(phase)).ToArray();
        var builder = new StringBuilder();
        builder.Append("tick,steps,objects,tick_mean_us,tick_max_us,alloc_mean_bytes,dirty_mean");
        if (Observed) foreach (var phase in phases) builder.Append(invariant, $",{phase}_mean_us");
        if (ScriptHashSamples.Count > 0) builder.Append(",script_state_hash_us");
        builder.Append('\n');
        var sample = 0;
        for (var from = 0; from < Count; from += window)
        {
            var to = Math.Min(Count, from + window);
            var steps = to - from;
            long total = 0, max = 0, allocated = 0, dirty = 0;
            for (var index = from; index < to; index++)
            {
                total += Total[index];
                max = Math.Max(max, Total[index]);
                allocated += Allocated[index];
                if (Observed) dirty += Dirty[index];
            }
            double Us(double value) => value * 1e6 / Stopwatch.Frequency;
            builder.Append(invariant, $"{Ticks[to - 1]},{steps},{Objects[to - 1]},{Us((double)total / steps):F1},{Us(max):F1},{allocated / steps},{(double)dirty / steps:F1}");
            if (Observed)
                foreach (var phase in phases)
                {
                    long sum = 0;
                    var values = Phase(phase);
                    for (var index = from; index < to; index++) sum += values[index];
                    builder.Append(invariant, $",{Us((double)sum / steps):F1}");
                }
            if (ScriptHashSamples.Count > 0)
            {
                // The sample taken at the end of a full window, if any.
                var value = sample < ScriptHashSamples.Count && steps == window ? Us(ScriptHashSamples[sample++].Elapsed).ToString("F1", invariant) : string.Empty;
                builder.Append(',').Append(value);
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
