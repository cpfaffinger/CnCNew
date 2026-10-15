using System.Diagnostics;
using System.Globalization;
using System.Text;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.SimTool.Survey;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

namespace CnCNew.SimTool;

/// <summary>
/// <c>ai-survey</c>: every two-player pairing of the reference manifest once per seed at one tick rate, recorded only (no replay runs, no snapshot
/// continuation). A match as a whole is chaotic and cannot be compared case by case between two rates; a survey per rate is compared as a distribution
/// by <c>rate-distribution</c> (<c>docs/SIMULATION-CORE.md</c> §40). The budget is game time, so it means the same at every rate.
/// </summary>
internal static class AiSurvey
{
    private const string UsageText = """
        Usage: CnCNew.SimTool ai-survey <content-dir> --out <tsv> [--seeds N] [--first-seed N] [--tick-rate 30|60|120] [--minutes M] [--pairings all|<id>,<id>] [--jobs N]
               [--composition uniform|exchange|default,..] [--tactics balanced|rush|turtle|eco|default,..] [--strength easy|medium|hard,..]
          plays every AI-vs-AI pairing of the reference manifest once per seed (default 20 seeds from 1), record only, with a budget of M game
          minutes (default 25: 45,000 ticks at 30 Hz); the seed is applied like `ai-match --seed N`. One line per match, sorted by pairing and seed.
          --composition names how the first and the second player's AI chooses its army units, --tactics their profile, --strength what they may know
          and how fast they decide (the built-in AI only; one value applies to the first player only).
        """;

    public static int Run(string[] args)
    {
        var outPath = ManifestCommands.Option(args, "--out");
        if (args.Length < 2 || outPath is null) return ManifestCommands.Fail(UsageText);
        var directory = args[1];
        var seeds = int.Parse(ManifestCommands.Option(args, "--seeds") ?? "20", CultureInfo.InvariantCulture);
        var firstSeed = uint.Parse(ManifestCommands.Option(args, "--first-seed") ?? "1", CultureInfo.InvariantCulture);
        var minutes = long.Parse(ManifestCommands.Option(args, "--minutes") ?? "25", CultureInfo.InvariantCulture);
        if (seeds < 1 || minutes < 1) return ManifestCommands.Fail("--seeds and --minutes must be at least 1.");
        var jobs = ManifestCommands.Jobs(args);
        var rate = ToolRate.Current;
        var maxTicks = checked(minutes * 60 * rate.TicksPerSecond);

        var pairings = ReferenceManifest.AiPairingRuns().OrderBy(pairing => pairing.Id, StringComparer.Ordinal).ToArray();
        if (ManifestCommands.Option(args, "--pairings") is { } only && only != "all")
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = wanted.Where(id => pairings.All(pairing => pairing.Id != id)).ToArray();
            if (unknown.Length > 0 || wanted.Length == 0) return ManifestCommands.Fail($"Unknown pairing id(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", pairings.Select(pairing => pairing.Id))}.");
            pairings = [..pairings.Where(pairing => wanted.Contains(pairing.Id, StringComparer.Ordinal))];
        }
        var content = ContentFileSystem.Open(directory);
        foreach (var pairing in pairings)
            if (!content.Exists(pairing.Map)) throw new FileNotFoundException($"The map {pairing.Map} is not in the content.");

        var queue = pairings.SelectMany(pairing => Enumerable.Range(0, seeds).Select(index => (Pairing: pairing, Seed: checked(firstSeed + (uint)index)))).ToArray();
        var rows = new SurveyRow?[queue.Length];
        var fingerprints = new string?[jobs];
        var next = -1;
        var done = 0;
        var failed = 0;
        var gate = new object();
        var total = Stopwatch.StartNew();
        Console.Error.WriteLine($"{queue.Length} matches ({pairings.Length} pairings x {seeds} seeds from {firstSeed.ToString(CultureInfo.InvariantCulture)}) at {rate}, budget {minutes.ToString(CultureInfo.InvariantCulture)} game minutes = {maxTicks.ToString(CultureInfo.InvariantCulture)} ticks, {jobs} side by side.");

        // As `reference-manifest check` does it: every worker builds its own catalogs, nothing of the simulation is shared between threads.
        var threads = Enumerable.Range(0, Math.Min(jobs, queue.Length)).Select(worker => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= queue.Length) return;
                var (pairing, seed) = queue[index];
                var watch = Stopwatch.StartNew();
                SurveyRow row;
                try
                {
                    if (context is null)
                    {
                        context = SimContext.Load(directory, rate);
                        fingerprints[worker] = context.Fingerprint;
                    }
                    row = OriginalAi.Requested(args)
                        ? OriginalAi.Survey(context, pairing.Id, pairing.Map, pairing.Factions, seed, maxTicks, args)
                        : Play(context, pairing.Id, pairing.Map, pairing.Factions, seed, maxTicks, Choices(args));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    row = new SurveyRow(pairing.Id, pairing.Map, pairing.Factions[0], pairing.Factions[1], seed, rate.TicksPerSecond, SurveyRow.Failed, "-", 0, 0, 0, 0, 0, "-", -1, -1, -1, -1);
                    lock (gate)
                    {
                        failed++;
                        Console.Error.WriteLine($"FAILED {pairing.Id} seed {seed.ToString(CultureInfo.InvariantCulture)}: {exception.GetType().Name}: {exception.Message}");
                    }
                }
                lock (gate)
                {
                    rows[index] = row;
                    // The line itself goes to the log as well: a survey that is interrupted after hours leaves its finished matches there.
                    Console.Error.WriteLine($"[{++done,4}/{queue.Length}] {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s\t{SurveyFile.Line(row)}");
                }
            }
        }, 64 * 1024 * 1024) { IsBackground = true, Name = $"survey-worker-{worker}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        var distinct = fingerprints.Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1) throw new InvalidOperationException($"Independent builds of the catalogs gave different world fingerprints: {string.Join(", ", distinct)}.");
        File.WriteAllText(Path.GetFullPath(outPath), SurveyFile.Write(rows.Select(row => row!)), new UTF8Encoding(false));
        var decided = rows.Count(row => row!.Decided);
        Console.Error.WriteLine($"Computed in {total.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
        Console.WriteLine($"Survey written: {queue.Length} matches at {rate}, {decided} decided, {queue.Length - decided - failed} without a winner, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// How the AI of each slot plays: <c>--composition a,b</c> (<c>uniform</c>, <c>exchange</c>, <c>default</c>), <c>--tactics a,b</c> (<c>balanced</c>, <c>rush</c>, <c>turtle</c>, <c>eco</c>,
    /// <c>default</c>), <c>--strength a,b</c> (<c>easy</c>, <c>medium</c>, <c>hard</c>); absent: the default for every slot.
    /// </summary>
    internal static AiSlotChoice[]? Choices(string[] args)
    {
        static string[] List(string[] args, string name) => ManifestCommands.Option(args, name) is { } text ? text.Split(',', StringSplitOptions.TrimEntries) : [];
        var compositions = List(args, "--composition");
        var tactics = List(args, "--tactics");
        var strengths = List(args, "--strength");
        var count = Math.Max(compositions.Length, Math.Max(tactics.Length, strengths.Length));
        if (count == 0) return null;
        return [..Enumerable.Range(0, count).Select(index => new AiSlotChoice(
            index < compositions.Length ? compositions[index] switch
            {
                "uniform" => ArmyComposition.Uniform,
                "exchange" => ArmyComposition.ExchangeValue,
                "default" => (ArmyComposition?)null,
                var value => throw new ArgumentException($"Unknown composition '{value}' (uniform, exchange, default)."),
            } : null,
            index < tactics.Length ? AiSlotChoice.ParseTactics(tactics[index]) : null,
            index < strengths.Length ? AiSlotChoice.ParseStrength(strengths[index]) : AiStrength.Medium))];
    }

    private static SurveyRow Play(SimContext context, string id, string mapPath, string[] factions, uint seed, long maxTicks, AiSlotChoice[]? choices)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        // Exactly `ai-match --seed N --max-ticks T`.
        var options = SkirmishMatchOptions.Standard with { MaxTicks = maxTicks, AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 };
        var scenario = new SkirmishMatchScenario(options);
        SkirmishMatchSession? played = null;
        long[] barracks = [-1, -1], attack = [-1, -1];
        // Observation only: two milestones per side, read after every tick until they are found. Nothing is written to the world.
        var recorded = scenario.Record(context.Catalog, context.Fingerprint, terrain, map.Objects, ReferenceManifest.Participants(context, factions), session =>
        {
            played = session;
            var world = session.World;
            for (var side = 0; side < 2; side++)
            {
                if (attack[side] < 0 && session.Ais[side] is { Stats.Waves: > 0 }) attack[side] = world.Tick;
                if (barracks[side] >= 0) continue;
                var owner = session.Players[side].Id;
                foreach (var entity in world.Entities)
                    if (entity.Owner == owner && !entity.EffectivelyDead && entity.Template.IsKindOf("FS_BARRACKS") && !entity.HasStatus(ObjectStatusBits.UnderConstruction))
                    {
                        barracks[side] = world.Tick;
                        break;
                    }
            }
        }, choices);
        var rate = context.Catalog.Rate.TicksPerSecond;
        for (var side = 0; side < 2; side++)
            if (scenario.Players[side].Faction != factions[side]) throw new InvalidOperationException($"Player {side + 1} plays {scenario.Players[side].Faction}, not {factions[side]}.");
        var slot = scenario.Winner is { } winner ? scenario.Players.IndexOf(scenario.Players.First(player => player.Id == winner)) + 1 : 0;
        var endTick = slot == 0 ? recorded.Header.EndTick : scenario.VictoryTick;
        return new SurveyRow(id, mapPath, factions[0], factions[1], seed, rate, slot == 0 ? SurveyRow.Stalemate : SurveyRow.Winner, slot == 0 ? "-" : factions[slot - 1], slot,
            endTick, endTick * 1000 / rate, recorded.Header.EndTick, recorded.Commands.Length, played is null ? "-" : played.World.StateHash.ToString("X16", CultureInfo.InvariantCulture),
            barracks[0], barracks[1], attack[0], attack[1]);
    }
}
