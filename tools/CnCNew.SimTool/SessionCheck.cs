using System.Diagnostics;
using System.Globalization;
using CnCNew.Formats;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

namespace CnCNew.SimTool;

/// <summary>
/// <c>session-check</c> (phase 24): the runs of the reference manifest's AI table and its eight-player runs as live sessions that are saved at several ticks; every save is
/// restored into a fresh session and continued to the end of the match, and the session hash (world plus every computer player) is compared with the uninterrupted session in
/// every tick. The replay of every continued session is compared with the uninterrupted one byte for byte, and the replay of the match is played as a match
/// (<see cref="SkirmishReplay.Play"/>): the computer players decide again and every command they give is compared with the recorded one.
/// </summary>
internal static class SessionCheck
{
    private const string UsageText = "Usage: CnCNew.SimTool session-check <content-dir> [--only <id>[,<id>]] [--jobs N] [--eight-minutes M]";

    /// <summary>Save points in logic frames of the original; each is shifted by its index in ticks, so that at 60 and 120 Hz most of them lie inside a frame.</summary>
    private static readonly long[] SaveFrames = [300, 2_111, 6_007, 12_503, 21_001, 33_333];

    private sealed record Row(string Id, bool Ok, string Text);

    public static int Run(string[] args)
    {
        if (args.Length < 2) return ManifestCommands.Fail(UsageText);
        var directory = args[1];
        var jobs = ManifestCommands.Jobs(args);
        var rate = ToolRate.Current;
        var eightMinutes = long.Parse(ManifestCommands.Option(args, "--eight-minutes") ?? "5", CultureInfo.InvariantCulture);
        var runs = new List<(string Id, string Map, string[] Factions, long MaxTicks)>();
        var standard = SkirmishMatchOptions.StandardAt(rate);
        foreach (var (id, map, factions) in ReferenceManifest.AiPairingRuns()) runs.Add((id, map, factions, rate.Time.OriginalFrames(ReferenceManifest.AiMatchFrames)));
        foreach (var (id, map, factions) in ReferenceManifest.EightPlayerRuns()) runs.Add((id, map, factions, checked(eightMinutes * 60 * rate.TicksPerSecond)));
        if (ManifestCommands.Option(args, "--only") is { } only)
        {
            var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unknown = wanted.Where(id => runs.All(run => run.Id != id)).ToArray();
            if (unknown.Length > 0 || wanted.Length == 0) return ManifestCommands.Fail($"Unknown run id(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", runs.Select(run => run.Id))}");
            runs = [..runs.Where(run => wanted.Contains(run.Id, StringComparer.Ordinal))];
        }

        var rows = new Row?[runs.Count];
        var next = -1;
        var gate = new object();
        var total = Stopwatch.StartNew();
        Console.Error.WriteLine($"{runs.Count} runs at {rate}, {jobs} job(s).");
        // As `reference-manifest check` does it: every worker builds its own catalogs, nothing of the simulation is shared between threads.
        var threads = Enumerable.Range(0, Math.Min(jobs, runs.Count)).Select(_ => new Thread(() =>
        {
            SimContext? context = null;
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= runs.Count) return;
                var run = runs[index];
                var watch = Stopwatch.StartNew();
                Row row;
                try
                {
                    context ??= SimContext.Load(directory, rate);
                    row = Check(context, run.Id, run.Map, run.Factions, standard with { MaxTicks = run.MaxTicks });
                }
                catch (Exception exception)
                {
                    row = new Row(run.Id, false, $"{exception.GetType().Name}: {exception.Message}");
                }
                rows[index] = row;
                lock (gate) Console.WriteLine($"{(row.Ok ? "ok  " : "FAIL")} {row.Id,-38} {row.Text} [{watch.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s]");
            }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        var failed = rows.Count(row => row is not { Ok: true });
        Console.WriteLine($"Session check: {rows.Length - failed} of {rows.Length} runs continue identically from every save; {total.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s.");
        return failed == 0 ? 0 : 1;
    }

    private static Row Check(SimContext context, string id, string mapPath, string[] factions, SkirmishMatchOptions options)
    {
        using var stream = context.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var slots = ReferenceManifest.Participants(context, factions).Select(participant => new SkirmishSlot(participant)).ToArray();
        var description = SkirmishMatchSession.Describe(options, context.Catalog, context.Fingerprint, terrain, [..slots], mapPath);
        var session = SkirmishMatchSession.Start(description, context.Catalog, terrain, map.Objects, options.CheckpointInterval);
        var time = session.World.Time;
        var saveTicks = SaveFrames.Select((frames, index) => time.OriginalFrames(frames) + index).ToArray();
        var saves = new SortedDictionary<long, byte[]>();
        var hashes = new List<ulong>();
        double saveMs = 0;
        double restoreMs = 0;
        while (!session.IsOver)
        {
            if (Array.IndexOf(saveTicks, session.World.Tick) >= 0)
            {
                var watch = Stopwatch.StartNew();
                saves[session.World.Tick] = session.Save();
                saveMs = Math.Max(saveMs, watch.Elapsed.TotalMilliseconds);
            }
            session.Step();
            hashes.Add(session.StateHash());
        }
        var reference = ReplayFormat.Write(session.ToReplay());
        // The replay of the match played as a match: the computer players decide again and are compared with their recorded commands.
        var audit = SkirmishReplay.Play(ReplayFormat.Read(reference), context.Catalog, context.Fingerprint, terrain, map.Objects);
        if (audit.FirstDeviation is { } deviation) return new Row(id, false, $"the playback of the replay deviates from the recording: {deviation}");
        if (audit.FinalSessionHash != session.StateHash() || audit.EndTick != session.World.Tick) return new Row(id, false, "the playback of the replay ends in another state than the match");
        long continued = 0;
        long bytes = 0;
        foreach (var (tick, save) in saves)
        {
            bytes = Math.Max(bytes, save.Length);
            var restoreWatch = Stopwatch.StartNew();
            var restored = SkirmishMatchSession.Restore(save, context.Catalog, context.Fingerprint, terrain);
            restoreMs = Math.Max(restoreMs, restoreWatch.Elapsed.TotalMilliseconds);
            while (!restored.IsOver)
            {
                var at = restored.World.Tick;
                restored.Step();
                continued++;
                if (restored.StateHash() != hashes[checked((int)at)])
                    return new Row(id, false, $"the session saved at tick {tick} differs after tick {at} (world {(restored.World.StateHash == WorldHashAt(description, context, terrain, map, options, at) ? "equal" : "differs")})");
            }
            if (restored.Winner != session.Winner || restored.VictoryTick != session.VictoryTick || restored.EndTick != session.EndTick)
                return new Row(id, false, $"the session saved at tick {tick} ends differently");
            if (!ReplayFormat.Write(restored.ToReplay()).AsSpan().SequenceEqual(reference)) return new Row(id, false, $"the session saved at tick {tick} yields another replay");
        }
        if (saves.Count == 0) return new Row(id, false, "the match ended before the first save point");
        var outcome = session.Winner is { } winner ? $"winner {session.Players.First(player => player.Id == winner).Faction} at {session.VictoryTick}" : "no winner";
        return new Row(id, true, $"{outcome}, end {session.EndTick}; {saves.Count} saves (ticks {string.Join(", ", saves.Keys)}), {continued} continued ticks identical, largest save {bytes / 1024} KB (slowest save {saveMs.ToString("F0", CultureInfo.InvariantCulture)} ms, slowest restore {restoreMs.ToString("F0", CultureInfo.InvariantCulture)} ms); replay played with the AI: {audit.AuditedCommands} commands audited, no deviation");
    }

    /// <summary>For the report of a deviation only: the world hash of the uninterrupted session after <paramref name="tick"/>, by playing it once more.</summary>
    private static ulong WorldHashAt(MatchDescription description, SimContext context, TerrainMap terrain, CnCNew.Data.World.MapData map, SkirmishMatchOptions options, long tick)
    {
        var session = SkirmishMatchSession.Start(description, context.Catalog, terrain, map.Objects, options.CheckpointInterval);
        while (session.World.Tick <= tick) session.Step();
        return session.World.StateHash;
    }
}
