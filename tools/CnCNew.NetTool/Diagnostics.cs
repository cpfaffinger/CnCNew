using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CnCNew.Game.Data;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Server;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Time;

// The commands of phase 27 (docs/NETWORK-DIAGNOSTICS.md): what a desync left behind, what a server recorded, and what a state costs to hand over.
static partial class NetTool
{
    private static SimulationRate RateOf(ReplayFile replay)
        => SimulationRate.FromTicksPerSecond((SkirmishReplay.DescriptionOf(replay.Header) ?? throw new InvalidDataException("The recording has no match description.")).TickRate);

    /// <summary>desync-diff: two packages of one desync - the tick, the parts of the hash, the objects, and the fields of the client that diverged.</summary>
    public static int DesyncDiff(string[] args)
    {
        if (args.Length < 4) throw new ArgumentException(Usage);
        var (a, b) = (args[2], args[3]);
        foreach (var directory in new[] { a, b })
            if (!File.Exists(Path.Combine(directory, "replay.bin"))) throw new FileNotFoundException($"{directory} is not a diagnostics package (no replay.bin).");
        var rate = RateOf(ReplayFormat.Read(File.ReadAllBytes(Path.Combine(a, "replay.bin"))));
        var data = GameData.Load(args[1], modelGeometry: true, rate: rate);
        var maps = new Dictionary<string, MatchMap>(StringComparer.Ordinal);
        MatchMap LoadMap(string path)
        {
            if (!maps.TryGetValue(path, out var map))
            {
                var loaded = Load(data, path);
                maps[path] = map = new MatchMap(loaded.Terrain, loaded.Map.Objects);
            }
            return map;
        }
        var result = DesyncPackageDiff.Compare(a, b, data.Templates, data.Fingerprint, LoadMap);
        Console.WriteLine($"A: {a}");
        Console.WriteLine($"B: {b}");
        Console.Write(result.Summary);
        if (Option(args, "--out") is { } file)
            File.WriteAllText(file, JsonSerializer.Serialize(new
            {
                result.FirstDifferingTick, Parts = result.Parts.ToArray(), Objects = result.Objects.ToArray(), FieldsA = result.FieldsA.ToArray(), FieldsB = result.FieldsB.ToArray(), result.StateTickA, result.StateTickB,
            }, ResultJson));
        return 0;
    }

    /// <summary>
    /// replay-check: plays the file a server wrote and compares the session hash with the last one the clients of that match agreed on (the note beside the file, or the
    /// options). The server never simulated; that the file leads to the clients' hash is what makes it the record of the match.
    /// </summary>
    public static int ReplayCheck(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException(Usage);
        var file = args[2];
        var replay = ReplayFormat.Read(File.ReadAllBytes(file));
        var agreedTick = Number(args, "--agreed-tick", -1);
        var agreedHash = Option(args, "--agreed-hash") ?? string.Empty;
        if (agreedTick < 0 && File.Exists(file + ".json"))
        {
            using var note = JsonDocument.Parse(File.ReadAllText(file + ".json"));
            agreedTick = note.RootElement.GetProperty("lastAgreedTick").GetInt64();
            agreedHash = note.RootElement.GetProperty("lastAgreedHash").GetString() ?? string.Empty;
        }
        var description = SkirmishReplay.DescriptionOf(replay.Header) ?? throw new InvalidDataException("The file has no match description.");
        var data = GameData.Load(args[1], modelGeometry: true, rate: RateOf(replay));
        var loaded = Load(data, description.MapPath);
        // Up to the tick the clients agreed on: the ticks below the end are played, so the state after that tick is the one they hashed.
        var end = agreedTick >= 0 && agreedHash.Length > 0 ? Math.Min(replay.Header.EndTick, agreedTick + 1) : replay.Header.EndTick;
        var watch = Stopwatch.StartNew();
        var played = SkirmishReplay.Play(replay with { Header = replay.Header with { EndTick = end } }, data.Templates, data.Fingerprint, loaded.Terrain, loaded.Map.Objects, audit: false);
        var hash = played.FinalSessionHash.ToString("X16", CultureInfo.InvariantCulture);
        Console.WriteLine(Invariant($"{Path.GetFileName(file)}: {description.MapPath}, {description.TickRate} Hz, {replay.Header.EndTick} ticks recorded, {replay.Commands.Length} commands of the players ({played.HumanCommands} fed in); played to tick {played.EndTick} in {watch.Elapsed.TotalSeconds:F1} s"));
        Console.WriteLine($"session hash after tick {played.EndTick - 1}: {hash}");
        if (agreedHash.Length == 0)
        {
            Console.WriteLine("no agreed hash to compare with (no note beside the file, no --agreed-tick/--agreed-hash)");
            return 0;
        }
        var equal = string.Equals(hash, agreedHash, StringComparison.OrdinalIgnoreCase) && played.EndTick == agreedTick + 1;
        Console.WriteLine(equal ? $"ok   the clients of the match agreed on {agreedHash} for tick {agreedTick}: the file is the match" : $"FAIL the clients agreed on {agreedHash} for tick {agreedTick}");
        return equal ? 0 : 1;
    }

    private sealed record SnapshotRow(long Tick, double GameMinutes, int Objects, int StateBytes, int PackedBytes, double WriteMs, double PackMs, double UnpackMs, double RestoreMs, bool Equal);

    /// <summary>
    /// snapshot-cost: what the state of a running match costs to hand over (docs/NETWORK-DIAGNOSTICS.md): its size as written and packed, the time to write and pack it
    /// on the member that gives it, and the time to unpack and build it on the one that joins - at several moments of a match of computer players.
    /// </summary>
    public static int SnapshotCost(string[] args)
    {
        var mapPath = Option(args, "--map");
        if (args.Length < 2 || mapPath is null) throw new ArgumentException(Usage);
        var rate = Rate(args);
        var players = (int)Number(args, "--players", 8);
        var minutes = Number(args, "--minutes", 20);
        var every = Number(args, "--every-minutes", 5);
        var repeats = (int)Number(args, "--repeats", 3);
        var data = GameData.Load(args[1], modelGeometry: true, rate: rate);
        var loaded = Load(data, mapPath);
        var total = minutes * 60 * rate.TicksPerSecond;
        var settings = Settings(loaded, 1, players - 1, Math.Max(total + 1, SkirmishMatchOptions.StandardAt(rate).MaxTicks));
        var session = SkirmishMatchSession.Start(settings.ToDescription(string.Empty, data.Fingerprint.ToUpperInvariant(), 1, 2), data.Templates, loaded.Terrain, loaded.Map.Objects);
        var rows = new List<SnapshotRow>();
        static double Median(List<double> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
        Console.WriteLine($"{mapPath}, {players} players (the seat of the first stands idle), {rate.TicksPerSecond} Hz; every value the median of {repeats}");
        for (var minute = every; minute <= minutes; minute += every)
        {
            var until = minute * 60 * rate.TicksPerSecond;
            while (session.World.Tick < until) session.Step();
            var (write, pack, unpack, restore) = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
            byte[] state = [], packed = [];
            var equal = true;
            for (var round = 0; round < repeats; round++)
            {
                var watch = Stopwatch.StartNew();
                state = session.Save();
                write.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                packed = SessionTransfer.Pack(state);
                pack.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                var unpacked = SessionTransfer.Unpack(packed);
                unpack.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                var restored = SkirmishMatchSession.Restore(unpacked, data.Templates, data.Fingerprint, loaded.Terrain);
                restore.Add(watch.Elapsed.TotalMilliseconds);
                equal &= restored.StateHash() == session.StateHash() && restored.World.Tick == session.World.Tick;
            }
            var row = new SnapshotRow(session.World.Tick, minute, session.World.Entities.Count(), state.Length, packed.Length, Median(write), Median(pack), Median(unpack), Median(restore), equal);
            rows.Add(row);
            Console.WriteLine(Invariant($"{(row.Equal ? "ok  " : "FAIL")} minute {row.GameMinutes,3:F0} (tick {row.Tick}): {row.Objects} objects; state {row.StateBytes / 1024.0:F0} KB, packed {row.PackedBytes / 1024.0:F0} KB; write {row.WriteMs:F1} ms + pack {row.PackMs:F1} ms on the giver; unpack {row.UnpackMs:F1} ms + build {row.RestoreMs:F1} ms on the joiner"));
        }
        // What a client pays every tick to be able to say, at a desync, which part and which object differed: the parts of the hash and the table of object hashes.
        var diagnostics = new SessionDiagnostics();
        var (plain, recorded) = (new List<double>(), new List<double>());
        for (var round = 0; round < 6; round++)
        {
            var watch = Stopwatch.StartNew();
            for (var tick = 0; tick < 600; tick++)
            {
                var at = session.World.Tick;
                session.Step();
                var hash = session.StateHash();
                if (round % 2 == 1) diagnostics.Record(at, session, hash);
            }
            (round % 2 == 1 ? recorded : plain).Add(watch.Elapsed.TotalMilliseconds * 1000 / 600);
        }
        Console.WriteLine(Invariant($"a tick with its session hash: {Median(plain):F0} us; with the diagnosis record (parts of the hash, object table): {Median(recorded):F0} us - {Median(recorded) - Median(plain):F0} us more"));
        if (Option(args, "--out") is { } file) File.WriteAllText(file, JsonSerializer.Serialize(rows, ResultJson));
        return rows.All(row => row.Equal) ? 0 : 1;
    }
}
