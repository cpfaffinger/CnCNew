using System.Diagnostics;
using System.Globalization;
using System.Text;
using CnCNew.Content;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

namespace CnCNew.SimTool;

/// <summary>
/// <c>ai-profiles</c> and <c>ai-profiles-report</c> (Stage 3, S3-M1; docs/SKIRMISH-AI.md): the survey of the built-in AI's tactics profiles and strengths. A <b>subject</b> - one profile at
/// medium strength, or one strength without a profile - plays one side of every two-player pairing of the reference manifest against the AI as it was (no profile, medium); the
/// subject <c>default</c> is that AI against itself, the yardstick. Per match and side the survey writes what the brief names: the outcome, the time of the first attack, the army
/// value over time, and two numbers of the build order (base defences and supply centers at minute 10). The report reads the files and checks the thresholds below, which were
/// written down and committed before the first survey ran; a threshold that is missed is reported as missed.
/// </summary>
internal static class AiProfiles
{
    private const string UsageText = """
        Usage: CnCNew.SimTool ai-profiles <content-dir> --subject default|balanced|rush|turtle|eco|easy|hard --side a|b --out <tsv> [--seeds N] [--first-seed N] [--minutes M]
                 [--pairings all|<id>,<id>] [--jobs N]
               CnCNew.SimTool ai-profiles-report <tsv> [<tsv> ...] [--out <report.md>]
          ai-profiles: the subject plays side a (the pairing's first faction) or b of every AI pairing of the reference manifest against the AI without a profile at medium
          strength, once per seed (default 4 seeds from 1), budget M game minutes (default 25). One line per match.
          ai-profiles-report: per subject over both sides, medians and the win rate against the subject 'default', and every threshold with its verdict.
        """;

    /// <summary>The army value is sampled at these game minutes.</summary>
    internal static readonly int[] ValueMinutes = [5, 10, 15, 20];

    internal static readonly string[] Columns =
    [
        "subject", "side", "pairing", "seed", "tickRate", "subjectFaction", "opponentFaction", "outcome", "subjectWon", "endGameSeconds", "firstAttackSubject", "firstAttackOpponent",
        "valueSubject5", "valueSubject10", "valueSubject15", "valueSubject20", "valueOpponent5", "valueOpponent10", "valueOpponent15", "valueOpponent20",
        "defensesSubject10", "supplyCentersSubject10", "finalHash",
    ];

    public static int Run(string[] args)
    {
        var outPath = ManifestCommands.Option(args, "--out");
        var subject = ManifestCommands.Option(args, "--subject");
        var side = ManifestCommands.Option(args, "--side");
        if (args.Length < 2 || outPath is null || subject is null || side is not ("a" or "b")) return ManifestCommands.Fail(UsageText);
        var choice = Subject(subject);
        var directory = args[1];
        var seeds = int.Parse(ManifestCommands.Option(args, "--seeds") ?? "4", CultureInfo.InvariantCulture);
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
            pairings = [..pairings.Where(pairing => wanted.Contains(pairing.Id, StringComparer.Ordinal))];
            if (pairings.Length != wanted.Length) return ManifestCommands.Fail("Unknown pairing id(s).");
        }

        var queue = pairings.SelectMany(pairing => Enumerable.Range(0, seeds).Select(index => (Pairing: pairing, Seed: checked(firstSeed + (uint)index)))).ToArray();
        var rows = new string?[queue.Length];
        var next = -1;
        var done = 0;
        var failed = 0;
        var gate = new object();
        var total = Stopwatch.StartNew();
        Console.Error.WriteLine($"{queue.Length} matches: subject {subject} on side {side}, {seeds} seeds from {firstSeed.ToString(CultureInfo.InvariantCulture)}, {rate}, {minutes.ToString(CultureInfo.InvariantCulture)} game minutes, {jobs} side by side.");
        var threads = Enumerable.Range(0, Math.Min(jobs, queue.Length)).Select(worker => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= queue.Length) return;
                var (pairing, seed) = queue[index];
                var watch = Stopwatch.StartNew();
                string row;
                try
                {
                    context ??= SimContext.Load(directory, rate);
                    row = Play(context, subject, side, choice, pairing.Id, pairing.Map, pairing.Factions, seed, maxTicks);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    row = string.Empty;
                    lock (gate)
                    {
                        failed++;
                        Console.Error.WriteLine($"FAILED {pairing.Id} seed {seed.ToString(CultureInfo.InvariantCulture)}: {exception.GetType().Name}: {exception.Message}");
                    }
                }
                lock (gate)
                {
                    rows[index] = row;
                    Console.Error.WriteLine($"[{++done,4}/{queue.Length}] {watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s\t{row}");
                }
            }
        }, 64 * 1024 * 1024) { IsBackground = true, Name = $"profile-worker-{worker}" }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        var text = new StringBuilder(string.Join('\t', Columns)).Append('\n');
        foreach (var row in rows.Where(row => !string.IsNullOrEmpty(row))) text.Append(row).Append('\n');
        File.WriteAllText(Path.GetFullPath(outPath), text.ToString(), new UTF8Encoding(false));
        Console.Error.WriteLine($"Computed in {total.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s.");
        Console.WriteLine($"Profile survey written: {queue.Length - failed} matches, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>A subject's slot: a profile at medium strength, or a strength without a profile; <c>default</c> is the AI as it was.</summary>
    internal static AiSlotChoice Subject(string subject) => subject switch
    {
        "default" => AiSlotChoice.Default,
        "easy" or "hard" => new AiSlotChoice(Strength: AiSlotChoice.ParseStrength(subject)),
        _ => new AiSlotChoice(Tactics: AiSlotChoice.ParseTactics(subject) ?? throw new ArgumentException("A subject names a profile or a strength.")),
    };

    private static string Play(SimContext context, string subject, string side, AiSlotChoice choice, string id, string mapPath, string[] factions, uint seed, long maxTicks)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var options = SkirmishMatchOptions.Standard with { MaxTicks = maxTicks, AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 };
        var mine = side == "a" ? 0 : 1;
        var choices = new[] { AiSlotChoice.Default, AiSlotChoice.Default };
        choices[mine] = choice;
        var session = SkirmishMatchSession.Start(options, context.Catalog, context.Fingerprint, terrain, map.Objects,
            [..ReferenceManifest.Participants(context, factions).Select((participant, index) => choices[index].Slot(participant))]);
        var world = session.World;
        var rate = world.Time.Rate.TicksPerSecond;
        long[] attack = [-1, -1];
        var values = new long[2, ValueMinutes.Length];
        long defenses = -1, centers = -1;
        void Sample(int minute, bool final)
        {
            var at = Array.IndexOf(ValueMinutes, minute);
            for (var slot = 0; slot < 2; slot++)
            {
                var owner = session.Players[slot].Id;
                long value = 0, defense = 0, center = 0;
                foreach (var entity in world.Entities)
                {
                    if (entity.Owner != owner || entity.EffectivelyDead || entity.Body is null) continue;
                    var template = entity.Template;
                    if (template.IsKindOf("STRUCTURE"))
                    {
                        if (entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
                        if (template.IsKindOf("FS_BASE_DEFENSE")) defense++;
                        if (template.IsKindOf("CASH_GENERATOR")) center++;
                        continue;
                    }
                    if (template.IsKindOf("HARVESTER") || template.IsKindOf("DOZER") || template.IsKindOf("PROJECTILE")) continue;
                    if (template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE") || template.IsKindOf("AIRCRAFT")) value += (long)template.BuildCost.Floor();
                }
                if (at >= 0) values[slot, at] = value;
                if ((minute == 10 || final) && slot == mine) { defenses = defense; centers = center; }
            }
        }
        while (!session.IsOver)
        {
            session.Step();
            for (var slot = 0; slot < 2; slot++)
                if (attack[slot] < 0 && session.Ais[slot] is { Stats.Waves: > 0 }) attack[slot] = world.Tick;
            if (world.Tick % (60L * rate) != 0) continue;
            var minute = (int)(world.Tick / (60L * rate));
            if (Array.IndexOf(ValueMinutes, minute) >= 0 || minute == 10) Sample(minute, false);
        }
        // A match that ended before minute 10 counts its defences and supply centers as they stood at the end.
        if (defenses < 0) Sample(-1, true);
        // A match that ends before a sampling minute keeps the value of the end: what stood when it ended stays what stands.
        for (var slot = 0; slot < 2; slot++)
            for (var at = 1; at < ValueMinutes.Length; at++)
                if (ValueMinutes[at] * 60L * rate > world.Tick) values[slot, at] = values[slot, at - 1];
        var winner = session.Winner is { } player ? session.Players.IndexOf(session.Players.First(value => value.Id == player)) : -1;
        var outcome = winner < 0 ? "stalemate" : "winner";
        var won = winner < 0 ? "-" : winner == mine ? "1" : "0";
        var end = winner < 0 ? world.Tick : session.VictoryTick;
        static string Seconds(long tick, int rate) => tick < 0 ? "-" : (tick / rate).ToString(CultureInfo.InvariantCulture);
        var other = 1 - mine;
        return string.Join('\t', subject, side, id, seed.ToString(CultureInfo.InvariantCulture), rate.ToString(CultureInfo.InvariantCulture), factions[mine], factions[other], outcome, won,
            Seconds(end, rate), Seconds(attack[mine], rate), Seconds(attack[other], rate),
            string.Join('\t', Enumerable.Range(0, ValueMinutes.Length).Select(at => values[mine, at].ToString(CultureInfo.InvariantCulture))),
            string.Join('\t', Enumerable.Range(0, ValueMinutes.Length).Select(at => values[other, at].ToString(CultureInfo.InvariantCulture))),
            defenses.ToString(CultureInfo.InvariantCulture), centers.ToString(CultureInfo.InvariantCulture), world.StateHash.ToString("X16", CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------------------------------------------------------------- report

    /// <summary>One subject's numbers over both sides.</summary>
    private sealed record Summary(string Subject, int Matches, int Decided, int Won, double FirstAttack, double Value5, double Value10, double Value15, double Value20, double Defenses10, double Centers10)
    {
        public double WinRate => Decided == 0 ? double.NaN : (double)Won / Decided;
    }

    /// <summary>
    /// The thresholds, fixed before the first survey (docs/SKIRMISH-AI.md section 4). Each compares a subject with the subject <c>default</c> (the AI as it was against itself) over
    /// the same pairings, seeds and both sides. A first attack that never came counts as the end of the budget.
    /// </summary>
    private static IEnumerable<(string Id, string Subject, string Text, Func<Func<string, Summary>, bool> Holds)> Thresholds()
    {
        yield return ("R1", "rush", "median first attack at most 70 % of the default's", get => get("rush").FirstAttack <= 0.7 * get("default").FirstAttack);
        yield return ("R2", "rush", "median base defences at minute 10 is 0", get => get("rush").Defenses10 == 0);
        yield return ("T1", "turtle", "median first attack at least 150 % of the default's", get => get("turtle").FirstAttack >= 1.5 * get("default").FirstAttack);
        yield return ("T2", "turtle", "median base defences at minute 10 at least the default's + 2", get => get("turtle").Defenses10 >= get("default").Defenses10 + 2);
        yield return ("E1", "eco", "median supply centers at minute 10 at least the default's + 1", get => get("eco").Centers10 >= get("default").Centers10 + 1);
        yield return ("E2", "eco", "median army value at minute 20 at least 110 % of the default's", get => get("eco").Value20 >= 1.1 * get("default").Value20);
        yield return ("S1", "easy", "wins at most 40 % of its decided matches against the default", get => get("easy").WinRate <= 0.40);
        yield return ("S2", "hard", "wins at least 55 % of its decided matches against the default", get => get("hard").WinRate >= 0.55);
        foreach (var subject in new[] { "rush", "turtle", "eco", "easy", "hard" })
            yield return ("D-" + subject, subject, "differs from the default by at least 10 points of win rate, 20 % of the median first attack or 20 % of the median army value at minute 10",
                get => Differs(get(subject), get("default")));
    }

    private static bool Differs(Summary subject, Summary yardstick)
        => Math.Abs(subject.WinRate - yardstick.WinRate) >= 0.10 || Math.Abs(subject.FirstAttack - yardstick.FirstAttack) >= 0.2 * yardstick.FirstAttack
           || Math.Abs(subject.Value10 - yardstick.Value10) >= 0.2 * yardstick.Value10;

    public static int Report(string[] args)
    {
        var files = args.Skip(1).TakeWhile(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length == 0) return ManifestCommands.Fail(UsageText);
        var lines = new List<string[]>();
        foreach (var file in files)
        {
            var text = File.ReadAllLines(file);
            if (text.Length == 0 || text[0] != string.Join('\t', Columns)) return ManifestCommands.Fail($"{file}: not a profile survey.");
            lines.AddRange(text.Skip(1).Where(line => line.Length > 0).Select(line => line.Split('\t')));
        }
        int Column(string name) => Array.IndexOf(Columns, name);
        static double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            return sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }
        var summaries = new SortedDictionary<string, Summary>(StringComparer.Ordinal);
        foreach (var group in lines.GroupBy(cells => cells[Column("subject")], StringComparer.Ordinal))
        {
            var rows = group.ToList();
            double Number(string[] cells, string name) => double.Parse(cells[Column(name)], CultureInfo.InvariantCulture);
            var decided = rows.Count(cells => cells[Column("subjectWon")] != "-");
            summaries[group.Key] = new Summary(group.Key, rows.Count, decided, rows.Count(cells => cells[Column("subjectWon")] == "1"),
                Median(rows.Select(cells => cells[Column("firstAttackSubject")] == "-" ? Number(cells, "endGameSeconds") : Number(cells, "firstAttackSubject"))),
                Median(rows.Select(cells => Number(cells, "valueSubject5"))), Median(rows.Select(cells => Number(cells, "valueSubject10"))),
                Median(rows.Select(cells => Number(cells, "valueSubject15"))), Median(rows.Select(cells => Number(cells, "valueSubject20"))),
                Median(rows.Select(cells => Number(cells, "defensesSubject10"))), Median(rows.Select(cells => Number(cells, "supplyCentersSubject10"))));
        }
        var report = new StringBuilder();
        report.Append("| Subject | Matches | Decided | Won | Win rate | First attack (s, median) | Army value 5 / 10 / 15 / 20 min (median) | Defences at 10 min | Supply centers at 10 min |\n");
        report.Append("| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: |\n");
        static string F(double value) => double.IsNaN(value) ? "-" : value.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (var summary in summaries.Values)
            report.Append($"| {summary.Subject} | {summary.Matches} | {summary.Decided} | {summary.Won} | {(double.IsNaN(summary.WinRate) ? "-" : (summary.WinRate * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %")} | {F(summary.FirstAttack)} | {F(summary.Value5)} / {F(summary.Value10)} / {F(summary.Value15)} / {F(summary.Value20)} | {F(summary.Defenses10)} | {F(summary.Centers10)} |\n");
        report.Append("\n| Threshold | Subject | What | Verdict |\n| --- | --- | --- | --- |\n");
        var missed = 0;
        foreach (var (id, subject, what, holds) in Thresholds())
        {
            string verdict;
            if (!summaries.ContainsKey(subject) || !summaries.ContainsKey("default")) verdict = "NOT RUN";
            else if (holds(name => summaries[name])) verdict = "met";
            else { verdict = "NOT MET"; missed++; }
            report.Append($"| {id} | {subject} | {what} | {verdict} |\n");
        }
        Console.Write(report.ToString());
        if (ManifestCommands.Option(args, "--out") is { } outPath) File.WriteAllText(Path.GetFullPath(outPath), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{missed} threshold(s) not met.");
        return 0;
    }
}
