using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.Game.Campaign;
using CnCNew.SimTool.Manifests;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>Entry points of <c>reference-manifest</c> and <c>input-manifest</c> and what both share.</summary>
internal static class ManifestCommands
{
    public static int Run(string[] args) => args[0] == "input-manifest" ? InputManifest.Command(args) : ReferenceManifest.Command(args);

    public static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    public static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary><c>--jobs N</c>: how many runs are computed side by side; half the logical processors when absent.</summary>
    public static int Jobs(string[] args)
    {
        var jobs = Option(args, "--jobs") is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : Math.Max(1, Environment.ProcessorCount / 2);
        return jobs >= 1 ? jobs : throw new ArgumentException("--jobs must be at least 1.");
    }
}

/// <summary>What one unit of work of a run returned.</summary>
/// <param name="IncrementalMismatches">With <c>--verify-incremental</c>: the ticks in which the incrementally maintained state hash differed from a full recomputation.</param>
/// <param name="Findings">With <c>--explain-incremental</c>: per object type and fields, how often an object changed without being marked dirty.</param>
internal sealed record JobResult(ManifestObject Values, long TicksVerified = 0, long IncrementalMismatches = 0, ImmutableArray<string> FirstMismatches = default, ImmutableArray<string> Findings = default);

internal sealed record RunJob(string Label, long Weight, bool NeedsContent, Func<SimContext?, VerifyMode, JobResult> Work);

/// <summary>One run of the manifest: a stable id, its set, its parameters and the work that produces its results (one job, or one per map for the sweep).</summary>
internal sealed record RunSpec(string Id, bool Quick, string Kind, ManifestObject Parameters, ImmutableArray<RunJob> Jobs, Func<ImmutableArray<JobResult>, ManifestObject> Combine)
{
    public string Set => Quick ? ReferenceManifest.QuickSet : ReferenceManifest.FullSet;
}

internal sealed record RunOutcome(ManifestObject? Entry, string? Error, long TicksVerified, long IncrementalMismatches, ImmutableArray<string> FirstMismatches, ImmutableArray<string> Findings, double Seconds);

/// <summary>
/// The reference manifest (<c>baselines/reference-manifest.json</c>): a fixed list of headless runs with their end tick, winner, final state hash and
/// hash chain. <c>write</c> computes and stores them, <c>check</c> computes them again and names the first field that differs. It is the yardstick for
/// every change that claims to leave the simulation's results alone (<c>docs/MULTIPLAYER-PLAN.md</c> §6, <c>docs/SIMULATION-CORE.md</c> §37).
/// </summary>
internal static class ReferenceManifest
{
    public const string Kind = "reference-manifest";
    public const int Schema = 1;
    public const string QuickSet = "quick";
    public const string FullSet = "full";

    private const long AiMatchTicks = 45_000;
    private const long EightPlayerTicks = 9_000;
    private const long EightPlayerQuickTicks = 3_000;
    private const long SweepTicks = 3_000;
    private const long MissionTicks = 3_000;
    private const int StressVerifyInterval = 1_000;

    private static readonly (string Name, string Path)[] AiMaps =
    [
        ("AlpineAssault", "Maps/Alpine Assault/Alpine Assault.map"), ("ArmoredFury", "Maps/Armored Fury/Armored Fury.map"),
        ("BitterWinter", "Maps/Bitter Winter/Bitter Winter.map"), ("DeathValley", "Maps/Death Valley/Death Valley.map"),
    ];

    private static readonly string[][] AiPairings =
    [
        ["FactionChina", "FactionGLA"], ["FactionAmerica", "FactionChina"], ["FactionGLA", "FactionAmerica"], ["FactionChinaTankGeneral", "FactionAmericaAirForceGeneral"],
    ];

    /// <summary>The four two-player runs of the quick set: the shortest decided ones, on three maps, every base faction at least twice.</summary>
    private static readonly string[] QuickAiRuns = ["AlpineAssault_GLA-America", "BitterWinter_GLA-America", "DeathValley_America-China", "DeathValley_China-GLA"];

    /// <summary>Three maps with eight start positions and different sizes; the first one also runs shortened in the quick set.</summary>
    private static readonly (string Name, string Path)[] EightPlayerMaps =
    [
        ("TwilightFlame", "Maps/Twilight Flame/Twilight Flame.map"), ("FortressAvalanche", "Maps/Fortress Avalanche/Fortress Avalanche.map"), ("IronDragon", "Maps/Iron Dragon/Iron Dragon.map"),
    ];

    /// <summary>The first eight playable factions in catalog order (what <c>ai-match --players 8</c> takes), spelled out so that the list cannot drift.</summary>
    private static readonly string[] EightPlayerFactions =
    [
        "FactionAmerica", "FactionAmericaAirForceGeneral", "FactionAmericaLaserGeneral", "FactionAmericaSuperWeaponGeneral",
        "FactionBossGeneral", "FactionChina", "FactionChinaInfantryGeneral", "FactionChinaNukeGeneral",
    ];

    /// <summary>The two missions of the quick set (their values are also quoted in the handoff of phase 18).</summary>
    private static readonly string[] QuickMissions = ["mission_MD_GLA05", "mission_MD_USA05"];

    private const string UsageText = """
        Usage: CnCNew.SimTool reference-manifest write <content-dir> <file> [--set quick|full] [--jobs N]
               CnCNew.SimTool reference-manifest check <content-dir> <file> [--set quick|full] [--only <run-id>[,<run-id>]] [--verify-incremental|--explain-incremental] [--jobs N]
               CnCNew.SimTool reference-manifest list  <content-dir>
               CnCNew.SimTool reference-manifest show  <content-dir> [--set quick|full] [--only <run-id>[,<run-id>]] [--verify-incremental|--explain-incremental] [--jobs N]
          write   computes the runs of the set and writes the file; a run that does not repeat is recorded as such, with a warning
          check   computes them again; one line per run with OK or the first differing field, exit code 1 on any difference
          show    computes runs and prints their entries; reads and writes no file
          --set   quick: a few minutes; full (default): every run. --only picks runs by id, whatever their set.
          --verify-incremental   in run 1 of every checked run, compare the incremental state hash with a full recomputation
                                 after every tick (the synthetic stress run: every 1,000 ticks)
          --explain-incremental  the same, and name the objects and fields that changed without being marked dirty
        """;

    public static int Command(string[] args)
    {
        if (args.Length >= 3 && args[1] == "list")
        {
            var listed = Plan(ContentFileSystem.Open(args[2]), out var missing);
            foreach (var spec in listed.OrderBy(spec => spec.Id, StringComparer.Ordinal))
                Console.WriteLine($"{spec.Id,-58} {spec.Set,-5} {spec.Kind}");
            foreach (var pair in missing.Values) Console.WriteLine($"{pair.Key,-58} unavailable: {pair.Value.Display}");
            return 0;
        }
        var show = args.Length >= 3 && args[1] == "show";
        if (!show && (args.Length < 4 || args[1] is not ("write" or "check"))) return ManifestCommands.Fail(UsageText);
        var set = ManifestCommands.Option(args, "--set") ?? FullSet;
        if (set is not (QuickSet or FullSet)) return ManifestCommands.Fail($"Unknown set '{set}'; use {QuickSet} or {FullSet}.");
        var jobs = ManifestCommands.Jobs(args);
        var verify = args.Contains("--explain-incremental") ? VerifyMode.Explain : args.Contains("--verify-incremental") ? VerifyMode.Verify : VerifyMode.None;
        var path = show ? string.Empty : Path.GetFullPath(args[3]);
        var plan = Plan(ContentFileSystem.Open(args[2]), out var unavailable);
        var selected = plan.Where(spec => set == FullSet || spec.Quick).ToList();
        if (ManifestCommands.Option(args, "--only") is { } only)
        {
            if (args[1] == "write") return ManifestCommands.Fail("--only is a check option; a manifest is written as a whole set.");
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = wanted.Where(id => plan.All(spec => spec.Id != id)).ToArray();
            if (unknown.Length > 0) return ManifestCommands.Fail($"Unknown run id(s): {string.Join(", ", unknown)}. `reference-manifest list <content-dir>` prints the ids.");
            selected = plan.Where(spec => wanted.Contains(spec.Id, StringComparer.Ordinal)).ToList();
        }
        selected.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        if (verify != VerifyMode.None && args[1] == "write") return ManifestCommands.Fail("--verify-incremental and --explain-incremental are check options.");

        var watch = Stopwatch.StartNew();
        Console.Error.WriteLine($"{selected.Count} runs ({selected.Sum(spec => spec.Jobs.Length)} jobs), {jobs} side by side{(verify != VerifyMode.None ? ", incremental hash verified" : string.Empty)}.");
        var outcomes = Execute(args[2], selected, jobs, verify);
        Console.Error.WriteLine($"Computed in {watch.Elapsed.TotalSeconds:F1} s.");
        if (show)
        {
            var shown = new ManifestObject();
            foreach (var spec in selected) shown.Add(spec.Id, outcomes[spec.Id].Entry ?? new ManifestObject().Add("error", outcomes[spec.Id].Error ?? "unknown"));
            Console.Write(ManifestDocument.Write(shown));
            foreach (var spec in selected.Where(_ => verify != VerifyMode.None))
            {
                Console.WriteLine($"{spec.Id}: incremental hash differed from the full hash in {outcomes[spec.Id].IncrementalMismatches} of {outcomes[spec.Id].TicksVerified} verified ticks{(outcomes[spec.Id].FirstMismatches.IsEmpty ? string.Empty : "; first: " + string.Join(" | ", outcomes[spec.Id].FirstMismatches))}");
                foreach (var finding in outcomes[spec.Id].Findings) Console.WriteLine($"    changed without MarkDirty: {finding}");
            }
            return 0;
        }
        return args[1] == "write" ? Write(path, selected, outcomes, unavailable) : Check(path, set, ManifestCommands.Option(args, "--only") is not null, selected, outcomes, unavailable, verify);
    }

    private static ManifestObject Header() => new ManifestObject()
        .Add("commandTableVersion", CommandTypes.Version)
        .Add("missionSnapshotVersion", MissionSnapshot.Version)
        .Add("replayFormatVersion", ReplayFormat.CurrentVersion)
        .Add("tickRate", ToolRate.Manifest.TicksPerSecond)
        .Add("worldSnapshotVersion", WorldSnapshot.Version);

    private static int Write(string path, List<RunSpec> selected, Dictionary<string, RunOutcome> outcomes, ManifestObject unavailable)
    {
        var runs = new ManifestObject();
        var problems = 0;
        var notRepeating = new List<string>();
        foreach (var spec in selected)
        {
            var outcome = outcomes[spec.Id];
            if (outcome.Entry is null)
            {
                problems++;
                Console.WriteLine($"{spec.Id}: FAILED {outcome.Error}");
                continue;
            }
            var unstable = UnstableFlags(outcome.Entry);
            if (unstable.Length > 0) notRepeating.Add(spec.Id);
            Console.WriteLine($"{spec.Id}: {Summary(outcome.Entry)}{(unstable.Length > 0 ? $" NOT REPEATING: {string.Join(", ", unstable)} is false" : string.Empty)}");
            runs.Add(spec.Id, outcome.Entry);
        }
        if (problems > 0)
        {
            Console.WriteLine($"{problems} run(s) failed; no manifest written.");
            return 1;
        }
        var root = new ManifestObject().Add("kind", Kind).Add("schema", Schema).Add("simulation", Header()).Add("unavailable", unavailable).Add("runs", runs);
        File.WriteAllBytes(path, ManifestDocument.WriteBytes(root));
        Console.WriteLine($"Reference manifest written: {selected.Count} runs ({selected.Count(spec => spec.Quick)} in the quick set).");
        // The manifest states what was measured. A false repeat flag is a defect of the simulation that the file makes visible; it is not hidden by refusing to write.
        if (notRepeating.Count > 0) Console.WriteLine($"WARNING: {notRepeating.Count} run(s) are recorded with a false repeat flag: {string.Join(", ", notRepeating)}.");
        return 0;
    }

    private static int Check(string path, string set, bool only, List<RunSpec> selected, Dictionary<string, RunOutcome> outcomes, ManifestObject unavailable, VerifyMode verify)
    {
        var expected = ManifestDocument.Read(File.ReadAllBytes(path));
        var differing = 0;
        void Compare(string label, ManifestNode left, ManifestNode right)
        {
            if (ManifestDocument.FirstDifference(left, right) is not { } difference) return;
            differing++;
            Console.WriteLine($"{label}: DIFFERENT {difference}");
        }
        Compare("manifest kind", expected.TryGet("kind", out var kind) ? kind : ManifestDifference.Missing, Kind);
        Compare("manifest schema", expected.TryGet("schema", out var schema) ? schema : ManifestDifference.Missing, Schema);
        Compare("simulation", expected.TryGet("simulation", out var simulation) ? simulation : new ManifestObject(), Header());
        Compare("unavailable", expected.TryGet("unavailable", out var missing) ? missing : new ManifestObject(), unavailable);
        var expectedRuns = expected.TryGet("runs", out var node) && node is ManifestObject runs ? runs : new ManifestObject();

        // Runs the file lists for this set but this tool does not know: the list of runs itself has changed.
        if (!only)
            foreach (var pair in expectedRuns.Values)
            {
                var inSet = set == FullSet || (pair.Value is ManifestObject entry && entry.TryGet("set", out var entrySet) && entrySet.Display == QuickSet);
                if (!inSet || selected.Any(spec => spec.Id == pair.Key)) continue;
                differing++;
                Console.WriteLine($"{pair.Key}: DIFFERENT the manifest lists this run in the {set} set, this tool does not");
            }

        long ticksVerified = 0, mismatches = 0;
        var notRepeating = new List<string>();
        foreach (var spec in selected)
        {
            var outcome = outcomes[spec.Id];
            ticksVerified += outcome.TicksVerified;
            mismatches += outcome.IncrementalMismatches;
            var incremental = verify == VerifyMode.None ? string.Empty
                : outcome.IncrementalMismatches == 0 ? $" [incremental hash equal to the full hash in {outcome.TicksVerified} verified ticks]"
                : $" [INCREMENTAL HASH MISMATCH in {outcome.IncrementalMismatches} of {outcome.TicksVerified} verified ticks; first: {string.Join(" | ", outcome.FirstMismatches)}]";
            if (outcome.Entry is null)
            {
                differing++;
                Console.WriteLine($"{spec.Id}: FAILED {outcome.Error}{incremental}");
                continue;
            }
            if (UnstableFlags(outcome.Entry).Length > 0) notRepeating.Add(spec.Id);
            var difference = !expectedRuns.TryGet(spec.Id, out var expectedEntry)
                ? new ManifestDifference(spec.Id, ManifestDifference.Missing, "a run of this tool that the manifest does not list")
                : ManifestDocument.FirstDifference(expectedEntry, outcome.Entry);
            if (difference is null && outcome.IncrementalMismatches == 0)
            {
                Console.WriteLine($"{spec.Id}: OK{incremental}");
                continue;
            }
            differing++;
            var text = difference is not null ? $"DIFFERENT {difference}" : "as recorded, but INCREMENTAL HASH";
            Console.WriteLine($"{spec.Id}: {text}{incremental}");
            foreach (var finding in outcome.Findings) Console.WriteLine($"    changed without MarkDirty: {finding}");
        }
        if (verify != VerifyMode.None) Console.WriteLine($"Incremental hash: {ticksVerified} ticks verified, {mismatches} mismatch(es).");
        if (notRepeating.Count > 0) Console.WriteLine($"Note: {notRepeating.Count} run(s) carry a false repeat flag: {string.Join(", ", notRepeating)}.");
        Console.WriteLine(differing == 0 ? $"Reference manifest ({(only ? "selected runs" : set)}): {selected.Count} runs identical." : $"Reference manifest ({(only ? "selected runs" : set)}): {differing} difference(s) in {selected.Count} runs.");
        return differing == 0 ? 0 : 1;
    }

    /// <summary>The repeat flags of a run that are false.</summary>
    private static string[] UnstableFlags(ManifestObject entry)
        => [..entry.Object("results").Values.Where(pair => pair.Key.StartsWith("identical", StringComparison.Ordinal) && pair.Value is ManifestFlag { Value: false }).Select(pair => pair.Key)];

    private static string Summary(ManifestObject entry)
    {
        var results = entry.Object("results");
        string Field(string name) => results.TryGet(name, out var value) ? $" {name} {value.Display}" : string.Empty;
        return (Field("endTick") + Field("outcome") + Field("winner") + Field("victoryTick") + Field("outcomeTick") + Field("finalHash") + Field("chainHash") + Field("combinedSha256") + Field("startedAndEnded")).TrimStart();
    }

    // ---------------------------------------------------------------------------------------------------------------- plan

    /// <summary>The fixed list of runs. It reads the campaign list and the map names of the content, nothing else; ids and parameters do not depend on a run.</summary>
    public static List<RunSpec> Plan(ContentFileSystem content, out ManifestObject unavailable)
    {
        var plan = new List<RunSpec>();
        unavailable = new ManifestObject();
        var rate = (long)ToolRate.Manifest.TicksPerSecond;

        var reference = ReferenceMatchOptions.Standard;
        plan.Add(Single("reference-match", true, "reference-match", new ManifestObject()
            .Add("worldSeed", Hex(reference.WorldSeed)).Add("driverSeed", Hex(reference.DriverSeed)).Add("maxTicks", reference.MaxTicks).Add("graceTicks", reference.GraceTicks)
            .Add("minTicks", reference.MinTicks).Add("arenaRadius", reference.ArenaRadius).Add("income", reference.Income).Add("incomeInterval", reference.IncomeInterval).Add("tickRate", rate),
            60, true, (context, verify) => ReferenceMatchJob(context!, reference, verify)));

        var stress = StressOptions.Standard;
        plan.Add(Single("stress", true, "stress", new ManifestObject()
            .Add("worldSeed", Hex(stress.WorldSeed)).Add("driverSeed", Hex(stress.DriverSeed)).Add("ticks", stress.Ticks).Add("targetPopulation", stress.TargetPopulation)
            .Add("drainTicks", stress.DrainTicks).Add("fixture", "synthetic").Add("tickRate", rate),
            120, false, (_, verify) => StressJob(stress, verify)));

        var skirmish = SkirmishMatchOptions.Standard;
        ManifestObject AiParameters(string map, string[] factions, long maxTicks) => new ManifestObject()
            .Add("map", Require(content, map)).Add("factions", string.Join(",", factions)).Add("players", factions.Length).Add("maxTicks", maxTicks).Add("graceTicks", skirmish.GraceTicks)
            .Add("worldSeed", Hex(skirmish.WorldSeed)).Add("aiSeed", Hex(skirmish.AiSeed)).Add("startMoney", skirmish.StartMoney).Add("sciencePointInterval", skirmish.SciencePointInterval).Add("tickRate", rate);
        foreach (var (name, map) in AiMaps)
            foreach (var pairing in AiPairings)
            {
                var id = $"{name}_{string.Join("-", pairing.Select(Short))}";
                plan.Add(Single(id, QuickAiRuns.Contains(id), "ai-match", AiParameters(map, pairing, AiMatchTicks), AiMatchTicks / 500, true,
                    (context, verify) => AiMatchJob(context!, map, pairing, skirmish with { MaxTicks = AiMatchTicks }, verify)));
            }
        foreach (var id in QuickAiRuns)
            if (plan.All(spec => spec.Id != id)) throw new InvalidOperationException($"The quick set names the unknown run {id}.");

        for (var index = 0; index < EightPlayerMaps.Length; index++)
        {
            var (name, map) = EightPlayerMaps[index];
            plan.Add(Single($"ai8_{name}", false, "ai-match", AiParameters(map, EightPlayerFactions, EightPlayerTicks), EightPlayerTicks / 20, true,
                (context, verify) => AiMatchJob(context!, map, EightPlayerFactions, skirmish with { MaxTicks = EightPlayerTicks }, verify)));
            if (index == 0)
                plan.Add(Single($"ai8_{name}_short", true, "ai-match", AiParameters(map, EightPlayerFactions, EightPlayerQuickTicks), EightPlayerQuickTicks / 20, true,
                    (context, verify) => AiMatchJob(context!, map, EightPlayerFactions, skirmish with { MaxTicks = EightPlayerQuickTicks }, verify)));
        }

        var sweepOptions = skirmish with { MaxTicks = SweepTicks, GraceTicks = ToolRate.GraceWithinBudget(skirmish.GraceTicks, SweepTicks, ToolRate.Manifest) };
        var paths = SimContext.MapPaths(content);
        plan.Add(new RunSpec("skirmish-sweep", false, "skirmish-sweep", new ManifestObject()
                .Add("maps", paths.Length).Add("factions", "the first two playable factions in catalog order").Add("maxTicks", SweepTicks).Add("graceTicks", sweepOptions.GraceTicks)
                .Add("worldSeed", Hex(skirmish.WorldSeed)).Add("aiSeed", Hex(skirmish.AiSeed)).Add("tickRate", rate),
            [..paths.Select(path => new RunJob($"skirmish-sweep {path}", 4, true, (context, verify) => SweepMapJob(context!, path, sweepOptions, verify)))],
            results => CombineSweep(paths, results)));

        // Per campaign its first and its last mission (one when it has only one). Campaigns that share a map with the same human faction share the run.
        var missions = new SortedDictionary<string, (string Map, bool Challenge, string Faction, SortedSet<string> Campaigns)>(StringComparer.Ordinal);
        foreach (var campaign in SimContext.LoadCampaigns(content).Campaigns)
        {
            var sequence = campaign.Sequence;
            if (sequence.IsEmpty) continue;
            foreach (var mission in sequence.Length == 1 ? new[] { sequence[0] } : new[] { sequence[0], sequence[^1] })
            {
                var map = CampaignCatalog.NormalizeMapPath(mission.MapPath);
                var id = $"mission_{Path.GetFileNameWithoutExtension(map)}{(campaign.IsChallenge ? "_" + Short(campaign.PlayerFaction) : string.Empty)}";
                if (!content.Exists(map))
                {
                    if (!unavailable.TryGet(id, out _)) unavailable.Add(id, $"map {map} is not in the content");
                    continue;
                }
                if (!missions.TryGetValue(id, out var entry)) missions[id] = entry = (content.GetEntry(map).VirtualPath, campaign.IsChallenge, campaign.IsChallenge ? campaign.PlayerFaction : string.Empty, new SortedSet<string>(StringComparer.Ordinal));
                entry.Campaigns.Add($"{campaign.Name}:{mission.Name}");
            }
        }
        var standard = MissionOptions.Standard;
        foreach (var (id, mission) in missions)
            plan.Add(Single(id, QuickMissions.Contains(id), "mission-run", new ManifestObject()
                    .Add("map", mission.Map).Add("campaigns", string.Join(",", mission.Campaigns)).Add("ticks", MissionTicks).Add("difficulty", standard.Difficulty.ToString())
                    .Add("worldSeed", Hex(standard.WorldSeed)).Add("challenge", mission.Challenge).Add("humanFaction", mission.Faction).Add("tickRate", rate),
                12, true, (context, verify) => MissionJob(context!, mission.Map, mission.Challenge, mission.Faction, MissionTicks, verify)));
        foreach (var id in QuickMissions)
            if (plan.All(spec => spec.Id != id)) throw new InvalidOperationException($"The quick set names the unknown run {id}.");

        if (plan.Select(spec => spec.Id).Distinct(StringComparer.Ordinal).Count() != plan.Count) throw new InvalidOperationException("Run ids are not unique.");
        return plan;

        static RunSpec Single(string id, bool quick, string kind, ManifestObject parameters, long weight, bool needsContent, Func<SimContext?, VerifyMode, JobResult> work)
            => new(id, quick, kind, parameters, [new RunJob(id, weight, needsContent, work)], results => results[0].Values);

        static string Short(string faction) => faction.StartsWith("Faction", StringComparison.Ordinal) ? faction["Faction".Length..] : faction;

        static string Require(ContentFileSystem content, string path) => content.Exists(path) ? content.GetEntry(path).VirtualPath : throw new FileNotFoundException($"The reference map {path} is not in the content.");
    }

    private static string Hex(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);
    private static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

    // ----------------------------------------------------------------------------------------------------------- execution

    /// <summary>
    /// Runs every job of the selected runs on <paramref name="jobs"/> worker threads, the heaviest first. Each worker builds its own catalogs (nothing of
    /// the simulation is shared between threads), and all workers must arrive at the same world fingerprint. The result does not depend on the order in
    /// which jobs finish.
    /// </summary>
    private static Dictionary<string, RunOutcome> Execute(string directory, List<RunSpec> selected, int jobs, VerifyMode verify)
    {
        var queue = selected.SelectMany(spec => spec.Jobs.Select((job, index) => (Spec: spec, Job: job, Index: index)))
            .OrderByDescending(item => item.Job.Weight).ThenBy(item => item.Job.Label, StringComparer.Ordinal).ToArray();
        var results = selected.ToDictionary(spec => spec.Id, spec => new JobResult?[spec.Jobs.Length], StringComparer.Ordinal);
        var errors = selected.ToDictionary(spec => spec.Id, _ => new string?[1], StringComparer.Ordinal);
        var seconds = selected.ToDictionary(spec => spec.Id, _ => new double[1], StringComparer.Ordinal);
        var fingerprints = new string?[jobs];
        var next = -1;
        var done = 0;
        var gate = new object();

        var threads = Enumerable.Range(0, Math.Min(jobs, queue.Length)).Select(worker => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= queue.Length) return;
                var (spec, job, slot) = queue[index];
                var watch = Stopwatch.StartNew();
                try
                {
                    if (job.NeedsContent && context is null)
                    {
                        context = SimContext.Load(directory);
                        fingerprints[worker] = context.Fingerprint;
                    }
                    var result = job.Work(context, verify);
                    lock (gate) results[spec.Id][slot] = result;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    lock (gate) errors[spec.Id][0] ??= $"{exception.GetType().Name}: {exception.Message}";
                }
                lock (gate)
                {
                    seconds[spec.Id][0] += watch.Elapsed.TotalSeconds;
                    Console.Error.WriteLine($"[{++done,3}/{queue.Length}] {job.Label}: {watch.Elapsed.TotalSeconds:F1} s");
                }
            }
        }, 64 * 1024 * 1024) { IsBackground = true, Name = $"manifest-worker-{worker}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        var distinct = fingerprints.Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1) throw new InvalidOperationException($"Independent builds of the catalogs gave different world fingerprints: {string.Join(", ", distinct)}.");

        var outcomes = new Dictionary<string, RunOutcome>(StringComparer.Ordinal);
        foreach (var spec in selected)
        {
            var parts = results[spec.Id];
            if (errors[spec.Id][0] is { } error || parts.Any(part => part is null))
            {
                outcomes[spec.Id] = new RunOutcome(null, errors[spec.Id][0] ?? "a job did not run", 0, 0, [], [], seconds[spec.Id][0]);
                continue;
            }
            var complete = parts.Select(part => part!).ToImmutableArray();
            var entry = new ManifestObject().Add("kind", spec.Kind).Add("set", spec.Set).Add("parameters", spec.Parameters).Add("results", spec.Combine(complete));
            outcomes[spec.Id] = new RunOutcome(entry, null, complete.Sum(part => part.TicksVerified), complete.Sum(part => part.IncrementalMismatches),
                [..complete.SelectMany(part => part.FirstMismatches.IsDefault ? [] : part.FirstMismatches).Take(4)],
                [..complete.SelectMany(part => part.Findings.IsDefault ? [] : part.Findings)], seconds[spec.Id][0]);
        }
        return outcomes;
    }

    private static ulong Chain(ReadOnlySpan<ulong> hashes)
    {
        var chain = StateHash.Seed;
        foreach (var hash in hashes) chain = StateHash.Mix(chain, hash);
        return chain;
    }

    /// <summary>
    /// What every recorded run is measured by: the replay is written and read back, run fresh twice (tick hashes compared) and a third time with a
    /// snapshot written and restored half way. Final hash and chain are those of run 1.
    /// </summary>
    private static ManifestObject Replayed(ReplayFile recorded, TemplateCatalog catalog, string fingerprint, TerrainMap? terrain, IncrementalVerifier verifier)
    {
        var bytes = ReplayFormat.Write(recorded);
        var replay = ReplayFormat.Read(bytes);
        var first = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true, AfterTick = verifier.Enabled ? (world, _) => verifier.AfterTick(world) : null }, terrain);
        var second = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true }, terrain);
        var identical = first.FinalHash == second.FinalHash && first.TickHashes.AsSpan().SequenceEqual(second.TickHashes);

        var half = replay.Header.EndTick / 2;
        var head = ReplayRunner.CreateWorld(replay.Header, catalog, terrain);
        var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
        var snapshot = WorldSnapshot.Write(head);
        var restored = WorldSnapshot.Restore(snapshot, catalog, fingerprint, terrain);
        var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
        var continued = tail.FinalHash == first.FinalHash && headRun.TickHashes.Length + tail.TickHashes.Length == first.TickHashes.Length
            && first.TickHashes.AsSpan(0, headRun.TickHashes.Length).SequenceEqual(headRun.TickHashes) && first.TickHashes.AsSpan(headRun.TickHashes.Length).SequenceEqual(tail.TickHashes);

        return new ManifestObject()
            .Add("endTick", replay.Header.EndTick).Add("finalHash", Hex(first.FinalHash)).Add("chainHash", Hex(Chain(first.TickHashes)))
            .Add("commands", recorded.Commands.Length).Add("rejectedCommands", first.RejectedCommands).Add("commandStreamSha256", Convert.ToHexString(SHA256.HashData(bytes)))
            .Add("identicalInRun2", identical)
            .Add("snapshotTick", half).Add("snapshotBytes", snapshot.Length).Add("snapshotSha256", WorldSnapshot.Sha256(snapshot)).Add("identicalAfterSnapshotContinuation", continued);
    }

    private static void AddOutcome(ManifestObject values, string? winner, long victoryTick)
    {
        values.Add("outcome", winner is null ? "stalemate" : "winner");
        if (winner is null) return;
        values.Add("winner", winner).Add("victoryTick", victoryTick);
    }

    private static JobResult ReferenceMatchJob(SimContext context, ReferenceMatchOptions options, VerifyMode verify)
    {
        var scenario = new ReferenceMatchScenario(options);
        var recorded = scenario.Record(context.Catalog, context.Fingerprint);
        var verifier = new IncrementalVerifier(verify);
        var values = Replayed(recorded, context.Catalog, context.Fingerprint, null, verifier);
        AddOutcome(values, scenario.Winner is null ? null : scenario.WinnerFaction, scenario.VictoryTick);
        values.Add("players", scenario.Players.Length).Add("peakLivingObjects", scenario.PeakAlive);
        return verifier.Result(values);
    }

    private static JobResult StressJob(StressOptions options, VerifyMode verify)
    {
        var fixture = new SyntheticFixture(ToolRate.Manifest);
        var scenario = new StressScenario(options);
        var recorded = scenario.Record(fixture);
        var verifier = new IncrementalVerifier(verify, StressVerifyInterval);
        var values = Replayed(recorded, fixture.Templates, fixture.Fingerprint, null, verifier);
        values.Add("outcome", "none").Add("peakLivingObjects", scenario.PeakAlive);
        return verifier.Result(values);
    }

    private static ImmutableArray<MatchParticipant> Participants(SimContext context, IReadOnlyList<string> factions)
    {
        var everyone = ReferenceMatchScenario.Participants(context.Catalog, out _);
        return [..factions.Select(name => everyone.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.Ordinal)) ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
    }

    private static JobResult AiMatchJob(SimContext context, string mapPath, string[] factions, SkirmishMatchOptions options, VerifyMode verify)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var scenario = new SkirmishMatchScenario(options);
        var recorded = scenario.Record(context.Catalog, context.Fingerprint, terrain, map.Objects, Participants(context, factions));
        var verifier = new IncrementalVerifier(verify);
        var values = Replayed(recorded, context.Catalog, context.Fingerprint, terrain, verifier);
        AddOutcome(values, scenario.Winner is { } winner ? scenario.Players.First(player => player.Id == winner).Faction : null, scenario.VictoryTick);
        return verifier.Result(values);
    }

    /// <summary>One map of the sweep: decoded, and when it has at least two start positions played live by the first two factions to its end or the tick budget.</summary>
    private static JobResult SweepMapJob(SimContext context, string path, SkirmishMatchOptions options, VerifyMode verify)
    {
        var values = new ManifestObject();
        try
        {
            using var stream = context.Content.OpenRead(path);
            var map = SageMapDecoder.Decode(stream);
            var terrain = TerrainMap.Build(map, path);
            if (terrain.StartPositions.Length < 2) return new JobResult(values.Add("status", terrain.StartPositions.Length == 0 ? "no-start" : "one-start"));
            var chosen = ReferenceMatchScenario.Participants(context.Catalog, out _).Take(2).ToImmutableArray();
            var session = SkirmishMatchSession.Start(options, context.Catalog, context.Fingerprint, terrain, map.Objects, [..chosen.Select(participant => new SkirmishSlot(participant))]);
            var verifier = new IncrementalVerifier(verify);
            var chain = StateHash.Seed;
            while (!session.IsOver)
            {
                chain = StateHash.Mix(chain, session.Step().StateHash);
                verifier.AfterTick(session.World);
            }
            values.Add("status", "ended").Add("endTick", session.World.Tick).Add("finalHash", Hex(session.World.StateHash)).Add("chainHash", Hex(chain));
            AddOutcome(values, session.Winner is { } winner ? session.Players.First(player => player.Id == winner).Faction : null, session.VictoryTick);
            return verifier.Result(values);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new JobResult(new ManifestObject().Add("status", "failed").Add("error", $"{exception.GetType().Name}: {exception.Message}"));
        }
    }

    private static ManifestObject CombineSweep(string[] paths, ImmutableArray<JobResult> results)
    {
        var maps = new ManifestObject();
        var text = new StringBuilder("skirmish-sweep-v1\n");
        long noStart = 0, oneStart = 0, ended = 0, withWinner = 0, atBudget = 0, failed = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            var values = results[index].Values;
            switch (values.Text("status"))
            {
                case "no-start": noStart++; continue;
                case "one-start": oneStart++; continue;
                case "failed": failed++; break;
                default:
                    ended++;
                    if (values.Text("outcome") == "winner") withWinner++;
                    else atBudget++;
                    break;
            }
            maps.Add(paths[index], values);
            text.Append(paths[index]).Append('\t').Append(ManifestDocument.Write(values).ReplaceLineEndings("\n"));
        }
        return new ManifestObject()
            .Add("mapsWithoutStart", noStart).Add("mapsWithOneStart", oneStart).Add("attempted", ended + failed).Add("startedAndEnded", ended).Add("failed", failed)
            .Add("withWinner", withWinner).Add("atTickBudget", atBudget).Add("combinedSha256", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))).Add("maps", maps);
    }

    /// <summary>
    /// A mission over a fixed number of ticks: two fresh runs, and a third that is saved half way and continued twice, by the session that was saved and by
    /// the restored one. The hash at the save point is taken before the save and again from the restored session.
    /// <c>identicalAfterSaveRestore</c> is what <c>mission-run</c> and the campaign audit check (the two continuations agree);
    /// <c>identicalToUninterruptedRunAfterSaveRestore</c> also asks that they agree with run 1, which was never saved. The two differ when an object carries a
    /// stale incremental hash at the save point: writing a snapshot recomputes every hash (finding D8, docs/SIMULATION-CORE.md §37).
    /// </summary>
    private static JobResult MissionJob(SimContext context, string mapPath, bool challenge, string faction, long ticks, VerifyMode verify)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var options = MissionOptions.Standard with { MaxTicks = 0, HumanSideName = challenge ? ChallengeSides.HumanSideFor(map) : string.Empty, HumanFaction = challenge ? faction : string.Empty };
        var verifier = new IncrementalVerifier(verify);
        var first = MissionScenario.Run(options, context.Catalog, context.Fingerprint, terrain, map, ticks, verifier.Enabled ? session => verifier.AfterTick(session.World) : null);
        var second = MissionScenario.Run(options, context.Catalog, context.Fingerprint, terrain, map, ticks);

        var original = MissionSession.Start(options, context.Catalog, context.Fingerprint, terrain, map);
        var chain = StateHash.Seed;
        while (original.World.Tick < ticks / 2 && !original.IsOver) chain = StateHash.Mix(chain, original.Step().StateHash);
        var saveTick = original.World.Tick;
        var hashAtSave = original.StateHash();
        var snapshot = MissionSnapshot.Write(original);
        var restored = MissionSnapshot.Restore(snapshot, context.Catalog, context.Fingerprint, terrain, map);
        var hashAfterRestore = restored.StateHash();
        var saved = MissionScenario.Continue(original, terrain.Name, ticks, null, chain);
        var continued = MissionScenario.Continue(restored, terrain.Name, ticks, null, chain);

        var values = new ManifestObject()
            .Add("endTick", first.Ticks).Add("finalHash", Hex(first.FinalHash)).Add("chainHash", Hex(first.Chain))
            .Add("outcome", first.Outcome.ToString().ToLowerInvariant()).Add("outcomeTick", first.OutcomeTick).Add("ended", first.Ended)
            .Add("humanSide", options.HumanSideName).Add("players", first.Players).Add("placedObjects", first.PlacedObjects).Add("skippedObjects", first.SkippedObjects)
            .Add("objects", first.Entities).Add("executedScripts", first.ExecutedScripts).Add("executedActions", first.ExecutedActions).Add("shots", first.Shots)
            .Add("missingActionHits", first.MissingActionTotal).Add("missingConditionHits", first.MissingConditionTotal)
            .Add("identicalInRun2", second.FinalHash == first.FinalHash && second.Chain == first.Chain)
            .Add("saveTick", saveTick).Add("hashAtSave", Hex(hashAtSave)).Add("hashAfterRestore", Hex(hashAfterRestore))
            .Add("snapshotBytes", snapshot.Length).Add("snapshotSha256", MissionSnapshot.Sha256(snapshot))
            .Add("restoredFinalHash", Hex(continued.FinalHash)).Add("restoredChainHash", Hex(continued.Chain))
            .Add("identicalAfterSaveRestore", continued.FinalHash == saved.FinalHash && continued.Chain == saved.Chain)
            .Add("identicalToUninterruptedRunAfterSaveRestore", hashAtSave == hashAfterRestore && continued.FinalHash == first.FinalHash && continued.Chain == first.Chain);
        return verifier.Result(values);
    }
}
