using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

// CnCNew.NetTool (phase 25, docs/NETWORK-PROTOCOL.md section 8): the multi-client probe on real maps, in one process.
if (args.Length == 0) return Fail(NetTool.Usage);
try
{
    return args[0] switch
    {
        "loopback-match" => NetTool.LoopbackMatch(args),
        "probe-suite" => NetTool.ProbeSuite(args),
        "local-cost" => NetTool.LocalCost(args),
        "identity" => NetTool.Identity(),
        "proxy" => NetTool.Proxy(args),
        "client" => NetTool.Client(args),
        "net-match" => NetTool.NetMatch(args),
        "net-matrix" => NetTool.NetMatrix(args),
        "pacing" => NetTool.Pacing(args),
        "load" => NetTool.Load(args),
        "seal-cost" => NetTool.SealCost(args),
        "desync-diff" => NetTool.DesyncDiff(args),
        "replay-check" => NetTool.ReplayCheck(args),
        "snapshot-cost" => NetTool.SnapshotCost(args),
        _ => Fail(NetTool.Usage),
    };
}
catch (Exception exception) when (exception is ArgumentException or FormatException or FileNotFoundException or InvalidDataException)
{
    return Fail(exception.Message);
}

static int Fail(string text)
{
    Console.Error.WriteLine(text);
    return 2;
}

static partial class NetTool
{
    public const string Usage = """
        Usage: CnCNew.NetTool <command> <content-dir> [options]
          loopback-match <content-dir> --map <map> --humans H --ais A [--ticks T | --seconds S | --minutes M] [--delay MS] [--jitter MS]
                         [--net-seed N] [--salt N] [--tick-rate 30|60|120]
              H clients, each with its own world, against one relay over the loopback; the humans are command generators. After every tick the session hashes
              of all worlds are compared; at the end the relay's recording is played as a replay.
          probe-suite <content-dir> [--minutes M] [--jobs N] [--maps a,b,c] [--tick-rate N]
              the matrix of the brief: three eight-player maps x mixes 1+7, 2+6, 4+4, 8+0 x (no delay | delay and jitter) x twice
          local-cost <content-dir> --map <map> [--players N] [--ticks T]
              the tick cost of a local skirmish with and without the network path, and the memory of n worlds in one process
          identity
              the build identity of this process
          proxy --listen PORT --server HOST:PORT [--delay MS] [--jitter MS] [--loss-percent P] [--dup-percent P] [--reorder-percent P] [--rate-kbps N --queue-kb N]
                [--seed N] [--run-seconds S]
              a UDP intermediary: every client that sends to the listen port gets its own way to the server, delayed, lost, duplicated and reordered per direction,
              reproducibly from the seed
          client <content-dir> --server HOST:PORT --map <map> --humans H --ais A --members M --seat I [--room NAME] [--name N] [--ticks T | --seconds S | --minutes M]
                 [--result FILE] [--leave-at-tick T] [--vanish-at-tick T] [--freeze-at-tick T] [--slow-ms MS] [--hitch-at-tick T --hitch-ms MS]
                 [--rejoin-at-tick T --rejoin-after-ms MS] [--corrupt-at-tick T] [--diagnostics DIR] [--watch]
              a client without a window as a process of its own: joins the room, takes its seat (-1: watches), plays with a command generator, writes what it measured
          net-match <content-dir> --map <map> --humans H --ais A [--watchers W] [--minutes M] [--delay MS] [--jitter MS] [--loss-percent P] --out DIR [--disturb seat:kind:tick[:arg]] [--linux server,2,5]
              one match of real processes: the dedicated server, the proxy and one client process per member, on this machine; compares what the clients report
              (--linux: the server with its proxy and the named clients run under Linux in WSL, with the same binaries; exit code 3 when there is no such environment)
          net-matrix <content-dir> --map <map> --humans H --ais A [--watchers W] [--minutes M] --out DIR [--delays 10,40,80,150] [--jitters 0,10,30] [--losses 0,1,3] [--lines d/j/l,...]
              net-match for every line of the matrix; writes the table
              (--disturb kinds: leave, vanish, freeze, slow, hitch, rejoin (tick[:ms away]), corrupt; --late-watchers N --late-after-seconds S: watchers that come
              when the match runs; --expect-desync: the match is meant to break - the server must report it and name the parts, every client must write its package)
          desync-diff <content-dir> <package A> <package B> [--out FILE]
              the packages two clients wrote at a desync: the first differing tick, the parts of the hash, the objects, and the fields of the client that diverged
          replay-check <content-dir> <file.replay> [--agreed-tick T --agreed-hash H]
              plays the file a server wrote and compares the session hash with the last one the clients agreed on (from the note beside the file)
          snapshot-cost <content-dir> --map <map> [--players 8] [--minutes 20] [--every-minutes 5] [--out FILE]
              the state of a running match: size written and packed, time to write, pack, unpack and build it
          pacing [--seconds S]
              how evenly this machine wakes a loop that waits for a tick: sleep, socket wait, socket wait with the fine timer
          seal-cost [--datagrams N] [--handshakes N]
              what the sealed transport costs this machine: the cryptography of a handshake, sealing and opening a datagram at the sizes a match sends
          load --server HOST:PORT [--server-key FINGERPRINT] --rooms N [--members 8] [--watchers 0] [--seconds S] [--match-seconds M] [--orders-per-second R]
               [--churn-percent P] [--state-kb K] [--threads T] [--tick-rate 60] [--seed N] [--label TEXT] [--out FILE] [--room-password TEXT]
              rooms of members that speak the whole protocol through the sealed transport and simulate nothing (needs no game data): match after match in every
              room; with churn one player of a match vanishes, leaves, or vanishes and comes back with its token. For the capacity and the endurance of a server.
          (net-match also takes --server HOST:PORT --server-log FILE: a server that runs already; --server-package DIR: the server of a self-contained package;
           --room-password TEXT; --client-server-key FINGERPRINT: what the clients are told to expect instead of the key the server printed.
           client also takes --server-key FINGERPRINT and --room-password TEXT.)
        """;

    private static readonly string[] EightPlayerMaps =
        ["Maps/Twilight Flame/Twilight Flame.map", "Maps/Fortress Avalanche/Fortress Avalanche.map", "Maps/Iron Dragon/Iron Dragon.map"];

    /// <summary>The first eight playable factions in catalog order, as the eight-player runs of the reference manifest take them.</summary>
    private static readonly string[] EightFactions =
    [
        "FactionAmerica", "FactionAmericaAirForceGeneral", "FactionAmericaLaserGeneral", "FactionAmericaSuperWeaponGeneral",
        "FactionBossGeneral", "FactionChina", "FactionChinaInfantryGeneral", "FactionChinaNukeGeneral",
    ];

    internal static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static long Number(string[] args, string name, long fallback) => Option(args, name) is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private static SimulationRate Rate(string[] args)
        => Option(args, "--tick-rate") is { } text ? SimulationRate.FromTicksPerSecond(int.Parse(text, CultureInfo.InvariantCulture)) : GameData.DefaultSessionRate;

    private static long Ticks(string[] args, SimulationRate rate, long fallbackSeconds)
    {
        if (Option(args, "--ticks") is { } ticks) return long.Parse(ticks, CultureInfo.InvariantCulture);
        if (Option(args, "--minutes") is { } minutes) return long.Parse(minutes, CultureInfo.InvariantCulture) * 60 * rate.TicksPerSecond;
        return Number(args, "--seconds", fallbackSeconds) * rate.TicksPerSecond;
    }

    public static int Identity()
    {
        var identity = BuildIdentity.Current;
        Console.WriteLine($"build identity {identity.Id} (commit {identity.Commit}, modified {identity.Modified.ToString().ToLowerInvariant()}); protocol {Protocol.Version}, formats {FormatVersions.Current}");
        return 0;
    }

    private sealed record Loaded(GameData Data, TerrainMap Terrain, CnCNew.Data.World.MapData Map, string MapPath);

    private static Loaded Load(GameData data, string mapPath)
    {
        if (!data.Content.Exists(mapPath)) throw new FileNotFoundException($"The map {mapPath} is not in the content.");
        using var stream = data.Content.OpenRead(mapPath);
        var map = SageMapDecoder.Decode(stream);
        return new Loaded(data, TerrainMap.Build(map, mapPath), map, mapPath);
    }

    private static RoomSettings Settings(Loaded loaded, int humans, int ais, long maxTicks)
    {
        if (humans < 1 || ais < 0 || humans + ais < 2 || humans + ais > Protocol.MaxSlots) throw new ArgumentException("A match has 1 to 8 humans and 2 to 8 participants.");
        if (humans + ais > loaded.Terrain.StartPositions.Length) throw new ArgumentException($"The map has {loaded.Terrain.StartPositions.Length} start positions.");
        var standard = SkirmishMatchOptions.StandardAt(loaded.Data.Rate);
        var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>();
        for (var index = 0; index < humans + ais; index++)
            slots.Add(new MatchSlotDescription(index < humans ? MatchSlotKind.Human : MatchSlotKind.Computer, EightFactions[index], StartPosition: index));
        return new RoomSettings(loaded.Data.Rate.TicksPerSecond, loaded.MapPath, loaded.Terrain.Fingerprint, maxTicks, standard.StartMoney, standard.GraceTicks, standard.SciencePointInterval, slots.ToImmutable());
    }

    private static string Describe(LoopbackProbeResult result, double seconds)
        => string.Create(CultureInfo.InvariantCulture, $"{result.Clients} client(s), {result.Pacing.ToString().ToLowerInvariant()}-paced, {result.TicksPlayed} ticks, final session hash {result.FinalSessionHash:X16}, agreed through tick {result.LastAgreedTick}, {result.BundleCommands} commands relayed ({result.IntentsRelayed} intents), order-to-execution mean {result.MeanIntentLatencyTicks:F1} / max {result.MaxIntentLatencyTicks} ticks, {result.Stalls} waits, {result.MessagesSent} messages / {result.BytesSent / 1024} KB, replay {(result.ReplayedSessionHash == result.FinalSessionHash ? "equal" : "DIFFERENT")} ({result.ReplayedHumanCommands} commands) [{seconds:F0} s]");

    public static int LoopbackMatch(string[] args)
    {
        var mapPath = Option(args, "--map");
        if (args.Length < 2 || mapPath is null) throw new ArgumentException(Usage);
        var rate = Rate(args);
        var data = GameData.Load(args[1], modelGeometry: true, rate: rate);
        var loaded = Load(data, mapPath);
        var ticks = Ticks(args, rate, 60);
        var settings = Settings(loaded, (int)Number(args, "--humans", 2), (int)Number(args, "--ais", 0), Math.Max(ticks, SkirmishMatchOptions.StandardAt(rate).MaxTicks));
        var delay = (int)Number(args, "--delay", 0);
        var jitter = (int)Number(args, "--jitter", 0);
        var options = new LoopbackProbeOptions(ticks, new LinkQuality(delay, jitter), new LinkQuality(delay, jitter), (uint)Number(args, "--net-seed", 1), GeneratorSeedSalt: (uint)Number(args, "--salt", 0));
        var watch = Stopwatch.StartNew();
        var result = new LoopbackProbe(data.Templates, data.Fingerprint, loaded.Terrain, loaded.Map.Objects).Run(settings, options);
        Console.WriteLine($"{(result.Ok ? "ok  " : "FAIL")} {mapPath}: {Describe(result, watch.Elapsed.TotalSeconds)}");
        if (!result.Ok) Console.WriteLine($"     {result.Problem}");
        return result.Ok ? 0 : 1;
    }

    public static int ProbeSuite(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException(Usage);
        var rate = Rate(args);
        var ticks = Ticks(args, rate, 120);
        var jobs = (int)Number(args, "--jobs", 2);
        var maps = Option(args, "--maps")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? EightPlayerMaps;
        (int Humans, int Ais)[] mixes = [(1, 7), (2, 6), (4, 4), (8, 0)];
        (string Name, LinkQuality Link)[] networks = [("plain", default), ("delay 40 ms, jitter 60 ms", new LinkQuality(40, 60))];
        var runs = (from map in maps from mix in mixes from network in networks from repeat in new[] { 1, 2 } select (Map: map, Mix: mix, Network: network, Repeat: repeat)).ToArray();
        var rows = new (bool Ok, string Text, ulong Hash)[runs.Length];
        var next = -1;
        var gate = new object();
        var total = Stopwatch.StartNew();
        Console.Error.WriteLine($"{runs.Length} runs at {rate}, {ticks} ticks each, {jobs} job(s); build {BuildIdentity.Current.Id}.");
        // Every worker loads its own game data: nothing of the simulation is shared between threads.
        var threads = Enumerable.Range(0, Math.Min(jobs, runs.Length)).Select(_ => new Thread(() =>
        {
            GameData? data = null;
            var loadedMaps = new Dictionary<string, Loaded>(StringComparer.Ordinal);
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= runs.Length) return;
                var run = runs[index];
                var watch = Stopwatch.StartNew();
                (bool, string, ulong) row;
                try
                {
                    data ??= GameData.Load(args[1], modelGeometry: true, rate: rate);
                    if (!loadedMaps.TryGetValue(run.Map, out var loaded)) loadedMaps[run.Map] = loaded = Load(data, run.Map);
                    var settings = Settings(loaded, run.Mix.Humans, run.Mix.Ais, Math.Max(ticks, SkirmishMatchOptions.StandardAt(rate).MaxTicks));
                    var options = new LoopbackProbeOptions(ticks, run.Network.Link, run.Network.Link, NetworkSeed: 7);
                    var result = new LoopbackProbe(data.Templates, data.Fingerprint, loaded.Terrain, loaded.Map.Objects).Run(settings, options);
                    row = (result.Ok, $"{Describe(result, watch.Elapsed.TotalSeconds)}{(result.Ok ? string.Empty : " :: " + result.Problem)}", result.FinalSessionHash);
                }
                catch (Exception exception)
                {
                    row = (false, $"{exception.GetType().Name}: {exception.Message}", 0);
                }
                rows[index] = row;
                lock (gate) Console.WriteLine($"{(row.Item1 ? "ok  " : "FAIL")} {Path.GetFileNameWithoutExtension(run.Map),-18} {run.Mix.Humans}+{run.Mix.Ais} {run.Network.Name,-26} run {run.Repeat}: {row.Item2}");
            }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();
        var failed = rows.Count(row => !row.Ok);
        // The two repeats of a run must be the same match.
        var unequal = 0;
        for (var index = 0; index + 1 < runs.Length; index += 2)
            if (rows[index].Ok && rows[index + 1].Ok && rows[index].Hash != rows[index + 1].Hash) unequal++;
        Console.WriteLine($"Probe suite: {rows.Length - failed} of {rows.Length} runs hash-identical on all clients in every tick and equal to their replay; {unequal} pair(s) of repeats differ; {total.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} s.");
        return failed == 0 && unequal == 0 ? 0 : 1;
    }

    /// <summary>
    /// The cost of the network path in a local skirmish: the same match once stepped directly on its session and once through the embedded relay (client, loopback, server,
    /// bundle per tick, hash report per tick), alternating in one process; and the managed memory of n worlds of that match.
    /// </summary>
    public static int LocalCost(string[] args)
    {
        var mapPath = Option(args, "--map");
        if (args.Length < 2 || mapPath is null) throw new ArgumentException(Usage);
        var rate = Rate(args);
        var data = GameData.Load(args[1], modelGeometry: true, rate: rate);
        var loaded = Load(data, mapPath);
        var players = (int)Number(args, "--players", 8);
        var ticks = Ticks(args, rate, 300);
        var settings = Settings(loaded, 1, players - 1, Math.Max(ticks, SkirmishMatchOptions.StandardAt(rate).MaxTicks)) with { HasSeed = true, Seed = 0xC0FFEE06, AiSeed = 0x5EED0006 };

        (double MicrosPerTick, ulong Hash, long Messages, long Bytes) Direct()
        {
            var description = settings.ToDescription(BuildIdentity.Current.Id, data.Fingerprint.ToUpperInvariant(), settings.Seed, settings.AiSeed);
            var session = SkirmishMatchSession.Start(description, data.Templates, loaded.Terrain, loaded.Map.Objects);
            var watch = Stopwatch.StartNew();
            while (session.World.Tick < ticks) session.Step();
            return (watch.Elapsed.TotalMilliseconds * 1000 / ticks, session.StateHash(), 0, 0);
        }

        (double MicrosPerTick, ulong Hash, long Messages, long Bytes) Relayed()
        {
            var relay = LocalRelay.Open(data.Fingerprint);
            relay.StartMatch(settings, 0);
            var session = SkirmishMatchSession.Start(relay.Client.Description!, data.Templates, loaded.Terrain, loaded.Map.Objects, 0, data.Fingerprint, BuildIdentity.Current.Id);
            relay.ReportLoaded(session.StateHash());
            var tickSeconds = 1.0 / rate.TicksPerSecond;
            var driver = new LockstepDriver(relay.Client, tickSeconds, relay.Pump, session.StateHash, settings.MaxTicks - 1);
            var path = new CommandPath(session.World, session.Issue, driver);
            var watch = Stopwatch.StartNew();
            while (session.World.Tick < ticks)
            {
                driver.AddTime(tickSeconds);
                var tick = session.World.Tick;
                if (!driver.IsDue(tick) || !driver.TryReleaseTick(tick, path.CanStep)) throw new InvalidOperationException($"The local relay did not release tick {tick}.");
                path.SubmitDue();
                session.Step();
                driver.TickCompleted(tick);
            }
            relay.Pump();
            if (relay.Server.LastAgreedTick != ticks - 1) throw new InvalidOperationException("The local relay did not see every hash.");
            return (watch.Elapsed.TotalMilliseconds * 1000 / ticks, session.StateHash(), relay.Network.MessagesSent, relay.Network.BytesSent);
        }

        Direct();
        Console.WriteLine($"{mapPath}, {players} players (1 seat, {players - 1} computer players), {ticks} ticks at {rate}; build {BuildIdentity.Current.Id}");
        var direct = new List<double>();
        var relayed = new List<double>();
        ulong hash = 0;
        for (var round = 0; round < 3; round++)
        {
            var a = Direct();
            var b = Relayed();
            if (a.Hash != b.Hash) { Console.WriteLine($"FAIL: the relayed match ends in {b.Hash:X16}, the direct one in {a.Hash:X16}"); return 1; }
            hash = a.Hash;
            direct.Add(a.MicrosPerTick);
            relayed.Add(b.MicrosPerTick);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  round {round + 1}: direct {a.MicrosPerTick:F1} us per tick, through the relay {b.MicrosPerTick:F1} us per tick ({b.Messages} messages, {b.Bytes / 1024} KB, {(double)b.Bytes / ticks:F0} bytes per tick)"));
        }
        direct.Sort();
        relayed.Sort();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"median: direct {direct[1]:F1} us, relayed {relayed[1]:F1} us, difference {relayed[1] - direct[1]:+0.0;-0.0} us per tick; both end in session hash {hash:X16}"));

        // Memory of n worlds of this match in one process (what the multi-client probe and a test with several clients hold).
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var worlds = new List<SkirmishMatchSession>();
        var description2 = settings.ToDescription(string.Empty, data.Fingerprint.ToUpperInvariant(), settings.Seed, settings.AiSeed);
        foreach (var count in new[] { 1, 2, 4, 8 })
        {
            while (worlds.Count < count)
            {
                var session = SkirmishMatchSession.Start(description2, data.Templates, loaded.Terrain, loaded.Map.Objects);
                for (var tick = 0; tick < Math.Min(ticks, 3_600); tick++) session.Step();
                worlds.Add(session);
            }
            var bytes = GC.GetTotalMemory(true) - before;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"memory: {count} world(s) after {Math.Min(ticks, 3_600)} ticks hold {bytes / (1024.0 * 1024.0):F1} MB of managed memory ({bytes / (1024.0 * 1024.0) / count:F1} MB per world; catalog and terrain are shared)"));
        }
        GC.KeepAlive(worlds);
        return 0;
    }
}
