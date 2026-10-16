using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Replays;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Game.Spectator;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using MatchResult = CnCNew.Game.Shell.MatchResult;

namespace CnCNew.Game.Tests;

/// <summary>
/// The replay browser (docs/REPLAY-BROWSER.md) without a person: a skirmish is recorded as the client records it, the file is read back, judged against this game, listed,
/// played to the end and sought to checkpoints - and every judgement is a hash: the playback reaches the session hash the recorded match had at every tick the recording kept
/// one, after every seek, and at the end; the server's file of the same match plays to the same end; a file of another build, another rule profile, another rate, a missing
/// map, with a changed byte or with a header that does not describe its match is refused with its reason. The replay list is drawn in six resolutions and pinned by hashes in
/// <c>baselines/replay-screens.txt</c> (<c>CNCNEW_UPDATE_BASELINES=1</c> records them; checksums only), and a <see cref="ShellScript"/> walks the menus as a person would.
/// What only a person can judge - whether the list is easy to read and the playback controls feel right - is not judged here.
/// </summary>
internal static class ReplayBrowserTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const int NavWidth = 1280, NavHeight = 720;
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/replay-screens.txt";
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 16, 14, 23, 0, TimeSpan.Zero);
    private const string TestBuild = "0123456789abcdef0123456789abcdef01234567";
    private const string OtherBuild = "fedcba9876543210fedcba9876543210fedcba98";
    private const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly Vector2 Viewport = new(NavWidth, NavHeight);

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ReplayBrowserTests));
        harness.Run("The replay file: written and read again, its header without the match, a changed byte, a cut end and another version are refused", FileFormat);
        harness.Run("The store: names of new recordings, the order of the list, renaming and deleting refuse what does not fit", Store);
        harness.Run("Compatibility: every reason a replay does not play is found in its order and has words of its own; every ending has words", Compatibility);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ReplayBrowserTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var contentDir = args[index + 1];
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        Recorded? recorded = null;
        harness.Run("A skirmish recorded as it is played plays back to every recorded hash, the recorded result and the recorded final hash", () => recorded = RecordAndPlay(data));
        if (recorded is null) return harness.Failures;
        harness.Run("Seeking: three checkpoints back and forth, each at the hash the recording kept there, pause, speed and view kept; from each the playback reaches the recorded end",
            () => Seeking(data, recorded));
        harness.Run("The server's file of the same match (what the relay sent) plays to the same end as the client's replay", () => ServerFileSameMatch(data, recorded));
        harness.Run("Incompatible files are refused with their reason: another build, another rule profile, damaged, another rate, a missing map, a header that does not describe its match",
            () => Refusals(data, contentDir, recorded));
        var baselines = LoadBaselines();
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("The replay list in six resolutions: empty, a list, a file that does not play, the reason, rename, a name taken, delete, the score of a playback", () => Screens(data, baselines, updating));
        harness.Run("No two states of the list share a picture", () => TestHarness.Assert(baselines.Count >= 48 && baselines.Values.Distinct(StringComparer.Ordinal).Count() == baselines.Count,
            $"{baselines.Count} hashes, {baselines.Values.Distinct(StringComparer.Ordinal).Count()} distinct"));
        if (updating) SaveBaselines(baselines);
        harness.Run("A person at the menus: a skirmish recorded, found in the list, played to the end with seeks back and forth, renamed and deleted", () => PersonAtTheMenus(data));
        // --replay-measure-minutes N (docs/REPLAY-BROWSER.md section 7): an eight-player match of computer players, recorded and played with seeks, measured in real time.
        var minutesAt = Array.IndexOf(args, "--replay-measure-minutes");
        if (minutesAt >= 0 && minutesAt + 1 < args.Length && int.TryParse(args[minutesAt + 1], out var minutes) && minutes > 0)
            harness.Run($"Measured: an eight-player match of {minutes} game minutes recorded, played and sought", () => Measure(data, minutes));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- without game data

    private static MatchRecordingInfo SyntheticInfo(string mapPath = "Maps/Alpine Assault/Alpine Assault.map", int tickRate = 60, string simulationId = TestBuild, string fingerprint = Fingerprint,
        ReplayEndingKind ending = ReplayEndingKind.Completed, int winner = 1, long seconds = 754, string recorded = "2026-10-16T14:23:00Z", string rules = "original")
        => new(recorded, "local", mapPath, tickRate, rules, fingerprint, "TERRAIN", simulationId, CommandTypes.Version, CnCNew.Simulation.Snapshots.WorldSnapshot.Version,
            ReplayFormat.CurrentVersion, MatchDescription.EncodingVersion, 1,
            [new ReplayPlayer(1, 0, "FactionAmerica", string.Empty, 0, 2, false, string.Empty), new ReplayPlayer(2, 1, "FactionChina", string.Empty, 0, 3, true, "builtin/rush")],
            new ReplayEnding(ending, seconds * tickRate, ending == ReplayEndingKind.Desync ? "The games were out of sync since tick 9000." : string.Empty), winner, seconds * tickRate, 0,
            5L * tickRate, [(0, 0x1111UL), (5L * tickRate, 0x2222UL)], 0xABCDEF0123456789UL, 0x0123456789ABCDEFUL,
            [new ReplayResultRow(1, winner == 1, winner == 2, 12, 3, 7, 4, 1, 4200), new ReplayResultRow(2, winner == 2, winner == 1, 9, 7, 3, 2, 4, 3100)]);

    private static ReplayMachine Machine(int tickRate = 60, string rules = "original", string build = TestBuild, string fingerprint = Fingerprint, bool hasMaps = true)
        => new(fingerprint, tickRate, rules, build, _ => hasMaps);

    private static void FileFormat()
    {
        var info = SyntheticInfo();
        var inner = Encoding.ASCII.GetBytes("the simulation's replay bytes");
        var bytes = MatchRecordingFormat.Write(info, inner);
        var read = MatchRecordingFormat.Read(bytes);
        TestHarness.Assert(read.Replay.AsSpan().SequenceEqual(inner), "the match inside comes back byte for byte");
        TestHarness.Assert(MatchRecordingFormat.Write(read.Info, read.Replay).AsSpan().SequenceEqual(bytes), "a file read and written again is the same file");
        TestHarness.Assert(read.Info.Players.SequenceEqual(info.Players) && read.Info.Result.SequenceEqual(info.Result) && read.Info.Hashes.SequenceEqual(info.Hashes) && read.Info.Ending == info.Ending
            && read.Info.FinalSessionHash == info.FinalSessionHash && read.Info.MapPath == info.MapPath && read.Info.DurationSeconds == 754, "every field of the header comes back");
        for (var position = 0; position < bytes.Length; position += Math.Max(1, bytes.Length / 37))
        {
            var damaged = (byte[])bytes.Clone();
            damaged[position] ^= 0x20;
            var refused = Refusal(() => MatchRecordingFormat.Read(damaged));
            TestHarness.Assert(refused is not null, $"a changed byte at {position} is refused");
        }
        TestHarness.Assert(Refusal(() => MatchRecordingFormat.Read(bytes.AsSpan(0, bytes.Length - 5))) is { Kind: ReplayCompatibilityKind.Damaged }, "a cut end is refused as damaged");
        TestHarness.Assert(Refusal(() => MatchRecordingFormat.Read(Encoding.ASCII.GetBytes("CNCRPLAY not ours"))) is { } foreign && foreign.Message.Contains("not a replay", StringComparison.Ordinal),
            "another kind of file is not taken for a replay");
        var other = (byte[])bytes.Clone();
        other[8] = 2;
        SHA256.HashData(other.AsSpan(0, other.Length - 32)).CopyTo(other, other.Length - 32);
        TestHarness.Assert(Refusal(() => MatchRecordingFormat.Read(other)) is { Kind: ReplayCompatibilityKind.OtherFormat }, "a file of another version is refused as another format");
    }

    private static ReplayRefusedException? Refusal(Action action)
    {
        try { action(); }
        catch (ReplayRefusedException refusal) { return refusal; }
        return null;
    }

    private static void Store()
    {
        var directory = RunDirectory("store");
        var store = new ReplayStore(directory);
        TestHarness.Assert(store.List(Machine()).IsEmpty, "a directory that is not there lists nothing");
        var first = store.NewName(RecordedAt, "Maps/Alpine Assault/Alpine Assault.map");
        TestHarness.Assert(first == "2026-10-16 1423 Alpine Assault", $"a new recording is named by its date and its map ({first})");
        store.Save(first, MatchRecordingFormat.Write(SyntheticInfo(recorded: "2026-10-16T14:23:00Z"), [1, 2, 3]));
        var second = store.NewName(RecordedAt, "Maps/Alpine Assault/Alpine Assault.map");
        TestHarness.Assert(second == "2026-10-16 1423 Alpine Assault (2)", $"a taken name gets a number ({second})");
        store.Save(second, MatchRecordingFormat.Write(SyntheticInfo(recorded: "2026-10-17T09:00:00Z"), [4]));
        store.Save("older", MatchRecordingFormat.Write(SyntheticInfo(recorded: "2026-10-01T09:00:00Z", simulationId: OtherBuild), [5]));
        File.WriteAllBytes(Path.Combine(directory, "broken" + MatchRecordingFormat.Extension), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(directory, "not a replay.txt"), [1]);
        var listed = store.List(Machine());
        TestHarness.Assert(listed.Select(entry => entry.Name).SequenceEqual([second, first, "older", "broken"]), $"the newest first, unreadable files last ({string.Join(" | ", listed.Select(entry => entry.Name))})");
        TestHarness.Assert(listed[0].Compatibility.IsPlayable && listed[2].Compatibility.Kind == ReplayCompatibilityKind.OtherBuild && listed[3].Compatibility.Kind == ReplayCompatibilityKind.Damaged
            && listed[3].Info is null, "each file has its state: playable, another build, damaged");
        foreach (var bad in new[] { "", "  ", " lead", "trail ", "a/b", "a\\b", "x:y", "what?", "dots.", ".hidden", new string('n', 65) })
            TestHarness.Assert(Refusal(() => store.Rename(first, bad)) is not null, $"'{bad}' is refused as a name");
        TestHarness.Assert(Refusal(() => store.Rename(first, "older")) is { } taken && taken.Message.Contains("already", StringComparison.Ordinal), "a name that is taken is refused");
        TestHarness.Assert(Refusal(() => store.Rename("missing", "anything")) is not null, "a replay that is not there cannot be renamed");
        store.Rename(first, "My best match");
        TestHarness.Assert(store.Exists("My best match") && !store.Exists(first) && store.List(Machine()).Any(entry => entry.Name == "My best match" && entry.Info is not null), "a rename keeps the file under its new name");
        store.Rename("My best match", "my best match");
        TestHarness.Assert(store.Exists("my best match"), "a rename that changes only the case is a rename");
        TestHarness.Assert(store.Delete("older") && !store.Delete("older") && store.List(Machine()).Length == 3, "a deleted replay is gone; deleting it again does nothing");
        Directory.Delete(directory, recursive: true);
    }

    private static void Compatibility()
    {
        var info = SyntheticInfo();
        TestHarness.Assert(ReplayCompatibility.Judge(info, Machine()).IsPlayable, "a replay of this build, rate, profile and data plays");
        var cases = new (ReplayCompatibilityKind Kind, MatchRecordingInfo Info, ReplayMachine Machine, string Word)[]
        {
            (ReplayCompatibilityKind.OtherFormat, info with { ReplayFormatVersion = ReplayFormat.CurrentVersion - 1 }, Machine(), "format"),
            (ReplayCompatibilityKind.OtherSimulation, info with { CommandTableVersion = CommandTypes.Version - 1 }, Machine(), "simulation version"),
            (ReplayCompatibilityKind.OtherSimulation, info with { DescriptionEncoding = MatchDescription.EncodingVersion - 1 }, Machine(), "simulation version"),
            (ReplayCompatibilityKind.OtherBuild, info with { SimulationId = OtherBuild }, Machine(), "fedcba987654"),
            (ReplayCompatibilityKind.OtherRate, info, Machine(tickRate: 30), "--tick-rate 60"),
            (ReplayCompatibilityKind.OtherRules, info, Machine(rules: "fixes"), "--rules original"),
            (ReplayCompatibilityKind.OtherDefinitions, info, Machine(fingerprint: "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210"), "game data"),
            (ReplayCompatibilityKind.MapMissing, info, Machine(hasMaps: false), "Alpine Assault"),
        };
        var reasons = new List<string>();
        foreach (var (kind, judged, machine, word) in cases)
        {
            var result = ReplayCompatibility.Judge(judged, machine);
            TestHarness.Assert(result.Kind == kind && !result.IsPlayable, $"{kind}: found ({result.Kind})");
            TestHarness.Assert(result.Reason.Contains(word, StringComparison.Ordinal) && result.Label.Length > 0, $"{kind}: the reason names it ('{result.Reason}')");
            if (kind != ReplayCompatibilityKind.OtherSimulation) reasons.Add(result.Reason);
        }
        TestHarness.Assert(reasons.Distinct(StringComparer.Ordinal).Count() == reasons.Count, "no two reasons share their words");
        // The order of the checks: a file of another build and another rate is refused as another build - the first reason is the one that matters.
        TestHarness.Assert(ReplayCompatibility.Judge(info with { SimulationId = OtherBuild }, Machine(tickRate: 30)).Kind == ReplayCompatibilityKind.OtherBuild, "the build is checked before the rate");
        TestHarness.Assert(ReplayCompatibility.Judge(info with { SimulationId = string.Empty }, Machine()).IsPlayable, "a description that states no build is not refused for it");
        foreach (var kind in Enum.GetValues<ReplayEndingKind>())
        {
            var line = ReplayText.EndingLine(SyntheticInfo(ending: kind));
            TestHarness.Assert(line.Length > 5, $"the ending {kind} has words ('{line}')");
        }
        TestHarness.Assert(Enum.GetValues<ReplayEndingKind>().Select(kind => ReplayText.EndingLine(SyntheticInfo(ending: kind))).Distinct().Count() == Enum.GetValues<ReplayEndingKind>().Length,
            "no two endings share their words");
        TestHarness.Assert(ReplayText.EndingLine(SyntheticInfo(winner: 0)) == "No winner (time limit)" && ReplayText.EndingLine(SyntheticInfo(winner: 2)) == "China won", "a winner and the time limit");
        TestHarness.Throws<ArgumentOutOfRangeException>(() => ReplayText.EndingLine(SyntheticInfo() with { Ending = new ReplayEnding((ReplayEndingKind)99, 0) }), "an ending without words is an error");
        // How a match ended, from the facts of this game and its server: the order of the reasons.
        var desync = new CnCNew.Net.DesyncMessage(900, []);
        var finished = new CnCNew.Net.MatchEndMessage(1_000, 1, CnCNew.Net.EndReason.Finished);
        var limit = new CnCNew.Net.MatchEndMessage(1_000, 0, CnCNew.Net.EndReason.ServerLimit);
        ReplayEndingKind Of(bool over, bool left, bool network, CnCNew.Net.DesyncMessage? d, CnCNew.Net.MatchEndMessage? e, bool open)
            => MatchRecorder.EndingOf(1_000, over, left, network, d, e, open, CnCNew.Net.Udp.UdpCloseReason.Timeout).Kind;
        TestHarness.Assert(Of(true, false, false, null, null, true) == ReplayEndingKind.Completed && Of(false, false, false, null, null, true) == ReplayEndingKind.Abandoned, "a local match: its rules, or closed");
        TestHarness.Assert(Of(false, false, true, desync, null, true) == ReplayEndingKind.Desync && Of(true, true, true, desync, limit, false) == ReplayEndingKind.Desync, "a desync comes first");
        TestHarness.Assert(Of(false, true, true, null, null, true) == ReplayEndingKind.Left && Of(true, true, true, null, finished, true) == ReplayEndingKind.Completed, "leaving a running match; leaving after its end is not leaving");
        TestHarness.Assert(Of(false, false, true, null, limit, true) == ReplayEndingKind.ServerEnded && Of(false, false, true, null, finished, false) == ReplayEndingKind.Completed, "the server's end: a limit, or its rules");
        TestHarness.Assert(Of(false, false, true, null, null, false) == ReplayEndingKind.ConnectionLost && Of(false, false, true, null, null, true) == ReplayEndingKind.Abandoned, "a lost connection, or the game closed");
        TestHarness.Assert(MatchRecorder.EndingOf(1_000, false, false, true, desync, null, true, CnCNew.Net.Udp.UdpCloseReason.Timeout).Detail.Contains("900", StringComparison.Ordinal)
            && MatchRecorder.EndingOf(1_000, false, false, true, null, null, false, CnCNew.Net.Udp.UdpCloseReason.Timeout).Detail.Length > 10, "the desync names its tick, a lost connection its reason");
        TestHarness.Assert(ReplayText.Duration(754) == "12:34" && ReplayText.Duration(3_725) == "1:02:05" && ReplayText.Date("2026-10-16T14:23:00Z") == "2026-10-16 14:23", "length and date as the list writes them");
    }

    // ------------------------------------------------------------------------------------------------- a recorded match

    private sealed record Recorded(byte[] File, MatchRecordingInfo Info, LiveMatch Live, MapData Map, TerrainMap Terrain, Dictionary<long, ulong> Straight, long CheckpointInterval);

    /// <summary>The match of these tests: a human (America) who gives group orders against a computer player (China) on Alpine Assault, for 70 seconds of game time.</summary>
    private const int MatchSeconds = 70;
    private const int CheckpointSeconds = 10;

    private static Recorded RecordAndPlay(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var options = SkirmishMatchOptions.StandardAt(data.Rate) with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(MatchSeconds) };
        var live = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], options) { Seed = 7 }, Viewport);
        var recorder = MatchRecorder.Attach(live) ?? throw new InvalidOperationException("the match cannot be recorded");
        var orders = 0;
        var nextOrder = data.Rate.Time.WholeSecondsToTicks(8);
        while (!live.IsOver)
        {
            if (live.Tick >= nextOrder)
            {
                nextOrder += data.Rate.Time.WholeSecondsToTicks(11);
                var units = live.World.Entities.Where(entity => entity.Owner == live.Human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead).Select(entity => entity.Id).Take(4).ToArray();
                if (units.Length > 0)
                {
                    var home = live.Terrain.StartPositions[live.Match.Slots[0].StartPosition];
                    var offset = Fixed.FromInt(orders % 2 == 0 ? 140 : -90);
                    live.Commands.Issue((sequence, tick) => new GroupMoveCommand(live.Human, sequence, tick, [.. units], home.X + offset, home.Y + offset, false));
                    orders++;
                }
            }
            live.Advance(0.25);
        }
        TestHarness.Assert(orders >= 4, $"the human gave orders ({orders})");
        var bytes = recorder.Build(MatchRecorder.EndingOf(live, leftByChoice: false), RecordedAt);
        var info = MatchRecordingFormat.Read(bytes).Info;
        TestHarness.Assert(info.Ending.Kind == ReplayEndingKind.Completed && info.EndTick == live.Tick && info.Winner == 0 && info.LocalPlayer == live.Human.Value && info.Source == "local",
            "the header says how the match ended, when, and who played here");
        TestHarness.Assert(info.SimulationId == CnCNew.Net.BuildIdentity.Current.Id && info.TickRate == data.Rate.TicksPerSecond && info.Rules == data.Rules.Name && info.DefinitionFingerprint == data.Fingerprint.ToUpperInvariant(),
            "the header names the build, the rate, the rule profile and the definitions");
        TestHarness.Assert(info.Hashes.Length == MatchSeconds / MatchRecorder.HashIntervalSeconds + 1 && info.FinalSessionHash == live.Session.StateHash(), $"the session hash every five seconds and at the end ({info.Hashes.Length})");
        TestHarness.Assert(info.Players.Length == 2 && !info.Players[0].IsComputer && info.Players[1].IsComputer && info.Players[1].Ai == "builtin", "the players: a human and the built-in AI");
        TestHarness.Assert(info.Result.Any(row => row.UnitsBuilt + row.BuildingsBuilt + row.Resources > 0), "the counters of the score screen are recorded");

        // The straight playback: every recorded hash on the way, the result and the final hash.
        var playback = ReplayPlayback.Open(data, bytes, Viewport, new ReplayPlaybackOptions(CheckpointSeconds));
        TestHarness.Assert(playback.Match.Setup.Spectator && playback.Match.Replay == playback && playback.Match.Tick == 0, "the playback is a spectator's match at tick 0");
        var straight = new Dictionary<long, ulong> { [0] = playback.Match.Session.StateHash() };
        playback.Match.TickStepped += _ => { if (playback.Match.Tick % playback.CheckpointInterval == 0) straight[playback.Match.Tick] = playback.Match.Session.StateHash(); };
        var refused = playback.Match.Commands.Issue((sequence, tick) => new SetMoveTargetCommand(playback.Match.Human, sequence, tick, new CnCNew.Simulation.Ids.EntityId(1), Fixed.FromInt(10), Fixed.FromInt(10)));
        TestHarness.Assert(!refused.Accepted, "nobody gives orders in a playback");
        PlayToEnd(playback);
        TestHarness.Assert(playback.Match.Tick == info.EndTick, $"the playback ends at the recorded end ({playback.Match.Tick} of {info.EndTick})");
        TestHarness.Assert(playback.HashesChecked == info.Hashes.Length && playback.HashMismatches == 0, $"every recorded hash is the playback's ({playback.HashesChecked} checked, {playback.HashMismatches} differ)");
        TestHarness.Assert(playback.FinalHashMatches, "the playback ends in the recorded final session hash");
        TestHarness.Assert(playback.ResultMatchesRecorded(out var difference), $"the score screen of the playback is the recorded one ({difference})");
        TestHarness.Assert(playback.CheckpointTicks.Count == MatchSeconds / CheckpointSeconds + 1, $"a checkpoint every {CheckpointSeconds} s from tick 0 to the end ({playback.CheckpointTicks.Count})");
        foreach (var (tick, hash) in info.Hashes)
            if (straight.TryGetValue(tick, out var played)) TestHarness.Assert(played == hash, $"tick {tick}: the playback's hash is the recorded one");
        Console.WriteLine($"       recorded {info.EndTick} ticks, {bytes.Length} bytes, {info.Hashes.Length} hashes, {playback.CheckpointTicks.Count} checkpoints");
        MapData map;
        using (var stream = data.Content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
        return new Recorded(bytes, info, live, map, TerrainMap.Build(map, mapPath), straight, playback.CheckpointInterval);
    }

    private static void PlayToEnd(ReplayPlayback playback)
    {
        for (var frame = 0; frame < 100_000 && !playback.IsOver; frame++) playback.Match.Advance(0.25);
    }

    private static void Seeking(GameData data, Recorded recorded)
    {
        var playback = ReplayPlayback.Open(data, recorded.File, Viewport, new ReplayPlaybackOptions(CheckpointSeconds));
        var interval = recorded.CheckpointInterval;
        // Forward from tick 0 straight to the fourth checkpoint: nothing kept yet, the playback runs there without drawing.
        var generation = playback.Generation;
        var first = playback.SeekTo(4 * interval);
        TestHarness.Assert(!first.Restored && first.Tick == 4 * interval && first.SteppedTicks == 4 * interval && playback.Generation == generation + 1, "a seek forward runs on from where the playback stands");
        TestHarness.Assert(first.SessionHash == recorded.Straight[4 * interval] && first.RecordedHash == recorded.Straight[4 * interval], "the seek forward reaches the hash the recording kept there");
        var player2 = playback.Match.Players[1].Id;
        playback.Match.Paused = true;
        playback.Match.Speed = 4;
        playback.Match.ShowWorldAs(player2);
        foreach (var target in new[] { 2 * interval, 1 * interval, 5 * interval })
        {
            var seek = playback.SeekTo(target);
            TestHarness.Assert(seek.Tick == target && playback.Match.Tick == target, $"the seek to tick {target} lands there");
            TestHarness.Assert(seek.SessionHash == recorded.Straight[target] && seek.RecordedHash == recorded.Straight[target] && seek.HashMatches,
                $"tick {target}: the session after the seek has the hash of the straight playback and of the recording");
            TestHarness.Assert(playback.Match.Paused && playback.Match.Speed == 4 && playback.Match.FogViewer == player2, $"tick {target}: pause, speed and the view of player {player2.Value} stay");
        }
        // From every one of the three checkpoints the playback goes on to the recorded end.
        foreach (var target in new[] { 2 * interval, 1 * interval, 5 * interval })
        {
            playback.SeekTo(target);
            playback.Match.Paused = false;
            PlayToEnd(playback);
            var same = playback.ResultMatchesRecorded(out var difference);
            TestHarness.Assert(playback.FinalHashMatches && same && playback.HashMismatches == 0,
                $"from tick {target} the playback reaches the recorded final hash and result ({difference}, {playback.HashMismatches} mismatches)");
        }
        var back = playback.SeekTo(0);
        TestHarness.Assert(back.Restored && back.Tick == 0 && back.SessionHash == recorded.Straight[0], "back to the start: tick 0 is the description itself");
        var beyond = playback.SeekTo(recorded.Info.EndTick + 1_000);
        TestHarness.Assert(beyond.Tick == recorded.Info.EndTick && playback.IsOver && playback.FinalHashMatches, "a seek beyond the end stops at the end");
        playback.SeekTo(3 * interval + data.Rate.Time.WholeSecondsToTicks(2));
        TestHarness.Assert(playback.PreviousCheckpoint() == 3 * interval && playback.NextCheckpoint() == 4 * interval, "previous and next checkpoint around a tick between them");
        playback.SeekTo(3 * interval + 7);
        TestHarness.Assert(playback.PreviousCheckpoint() == 2 * interval, "within a second after a checkpoint, back goes to the one before it (pressing twice goes back twice)");
        playback.SeekTo(3 * interval);
        TestHarness.Assert(playback.PreviousCheckpoint() == 2 * interval, "standing on a checkpoint, back goes to the one before");
    }

    private static void ServerFileSameMatch(GameData data, Recorded recorded)
    {
        var server = recorded.Live.Relay.Server.Recording;
        var file = CnCNew.Net.RecordingFile.ToReplay(server.Description, server.Bundles, recorded.Info.EndTick);
        var replay = ReplayFormat.Read(file);
        var played = SkirmishReplay.Play(replay, data.Templates, data.Fingerprint, recorded.Terrain, recorded.Map.Objects, audit: false);
        TestHarness.Assert(played.EndTick == recorded.Info.EndTick && played.FinalSessionHash == recorded.Info.FinalSessionHash && played.FinalWorldHash == recorded.Info.FinalWorldHash,
            $"the server's file plays to the client's end: tick {played.EndTick}, hash {played.FinalSessionHash:X16} against {recorded.Info.FinalSessionHash:X16}");
        var client = ReplayFormat.Read(MatchRecordingFormat.Read(recorded.File).Replay);
        var humans = client.Commands.Where(command => command.Player.Value == recorded.Live.Human.Value).Select(ReplayFormat.EncodeCommand).ToArray();
        var relayed = replay.Commands.Select(ReplayFormat.EncodeCommand).ToArray();
        TestHarness.Assert(humans.Length == relayed.Length && humans.Zip(relayed).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)),
            $"the human's commands of the client's replay are the commands of the server's file ({humans.Length} against {relayed.Length})");
    }

    /// <summary>The recorded file with its match description (inside the simulation's replay) and its header changed: what another build, or a lying header, would have written.</summary>
    private static byte[] Forge(byte[] file, Func<MatchDescription, MatchDescription> description, Func<MatchRecordingInfo, MatchRecordingInfo> header)
    {
        var recording = MatchRecordingFormat.Read(file);
        var replay = ReplayFormat.Read(recording.Replay);
        var changed = description(SkirmishReplay.DescriptionOf(replay.Header)!);
        var inner = ReplayFormat.Write(replay with { Header = replay.Header with { MatchDescription = Convert.ToBase64String(changed.Encode()) } });
        return MatchRecordingFormat.Write(header(recording.Info), inner);
    }

    private static void Refusals(GameData data, string contentDir, Recorded recorded)
    {
        ReplayRefusedException? Open(byte[] file, GameData? on = null, ReplayMachine? machine = null) => Refusal(() => ReplayPlayback.Open(on ?? data, file, Viewport, machine: machine));

        // Another build: the description inside says so, and the header with it.
        var otherBuild = Forge(recorded.File, description => description with { SimulationId = OtherBuild }, info => info with { SimulationId = OtherBuild });
        var build = Open(otherBuild);
        TestHarness.Assert(build is { Kind: ReplayCompatibilityKind.OtherBuild } && build.Message.Contains("fedcba987654", StringComparison.Ordinal), $"a file of another build is refused with its reason ({build?.Message})");
        // Another rule profile: the same file in a game that runs the original with the fixes of the community.
        var fixes = GameData.Load(contentDir, rate: data.Rate, rules: CnCNew.Simulation.Rules.RuleProfile.Parse("fixes"));
        var rules = Open(recorded.File, fixes);
        TestHarness.Assert(fixes.Fingerprint != data.Fingerprint && rules is { Kind: ReplayCompatibilityKind.OtherRules } && rules.Message.Contains("'original'", StringComparison.Ordinal)
            && rules.Message.Contains("fixes)", StringComparison.Ordinal) && rules.Message.Contains("--rules original", StringComparison.Ordinal),
            $"a file of another rule profile is refused with its reason ({rules?.Message})");
        // Damaged: one changed byte in the middle of the match.
        var damaged = (byte[])recorded.File.Clone();
        damaged[damaged.Length / 2] ^= 0x01;
        TestHarness.Assert(Open(damaged) is { Kind: ReplayCompatibilityKind.Damaged } broken && broken.Message.Contains("damaged", StringComparison.Ordinal), "a changed byte is refused as damaged");
        // Another rate, a missing map, another map of the same name.
        var otherRate = data.Rate.TicksPerSecond == 60 ? 30 : 60;
        TestHarness.Assert(Open(recorded.File, machine: ReplayMachine.Of(data) with { TickRate = otherRate }) is { Kind: ReplayCompatibilityKind.OtherRate } rate && rate.Message.Contains($"--tick-rate {data.Rate.TicksPerSecond}", StringComparison.Ordinal),
            "a file of another rate is refused with the option that plays it");
        TestHarness.Assert(Open(recorded.File, machine: ReplayMachine.Of(data) with { HasMap = _ => false }) is { Kind: ReplayCompatibilityKind.MapMissing }, "a file whose map this game lacks is refused");
        var otherMap = Forge(recorded.File, description => description with { TerrainFingerprint = "0000" }, info => info with { TerrainFingerprint = "0000" });
        TestHarness.Assert(Open(otherMap) is { Kind: ReplayCompatibilityKind.OtherMap }, "a file of another map of the same name is refused");
        // A header that does not describe its match: the list would show one thing and the playback play another.
        var lying = MatchRecordingFormat.Write(recorded.Info with { EndTick = recorded.Info.EndTick + 1 }, MatchRecordingFormat.Read(recorded.File).Replay);
        TestHarness.Assert(Open(lying) is { Kind: ReplayCompatibilityKind.Damaged }, "a header that does not describe its match is refused as damaged");
        var hidden = Forge(recorded.File, description => description with { SimulationId = OtherBuild }, info => info);
        TestHarness.Assert(Open(hidden) is { Kind: ReplayCompatibilityKind.Damaged }, "a header that hides another build is refused as damaged");
        // The list says the same as the playback.
        var store = new ReplayStore(RunDirectory("refusals"));
        store.Save("build", otherBuild);
        store.Save("damaged", damaged);
        store.Save("good", recorded.File);
        var listed = store.List(ReplayMachine.Of(data)).ToDictionary(entry => entry.Name);
        TestHarness.Assert(listed["build"].Compatibility.Kind == ReplayCompatibilityKind.OtherBuild && listed["damaged"].Compatibility.Kind == ReplayCompatibilityKind.Damaged
            && listed["good"].Compatibility.IsPlayable, "the list judges each file as the playback does");
        TestHarness.Assert(store.List(ReplayMachine.Of(fixes)).Single(entry => entry.Name == "good").Compatibility.Kind == ReplayCompatibilityKind.OtherRules,
            "in a game of another profile the list shows the file as one of other rules");
        Directory.Delete(store.Directory, recursive: true);
    }

    // ------------------------------------------------------------------------------------------------- the pictures

    private static void Screens(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        TestHarness.Assert(ReplayMenuScreen.DetailsLayout.Windows.Length == 1 && ReplayMenuScreen.DetailsLayout.Diagnostics.IsEmpty,
            $"the details window of this project's own is one window the parser has nothing to say about ({string.Join("; ", ReplayMenuScreen.DetailsLayout.Diagnostics)})");
        var directory = RunDirectory("screens");
        var shell = NewShell(data, directory);
        var rate = data.Rate.TicksPerSecond;
        shell.ReplayMachine = new ReplayMachine(data.Fingerprint, rate, data.Rules.Name, TestBuild, data.Content.Exists);
        OpenList(shell);
        TestHarness.Assert(shell.CurrentScreen is ReplayMenuScreen { Entries.IsEmpty: true }, "the list opens empty");
        Capture(shell, "Replays-Empty", baselines, updating);
        ShellTestInput.Click(shell, "ReplayMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is MainMenuScreen, "Back leads to the main menu");

        // Four files of fixed content: a win, a desync, one of another build and one that is damaged. Their rate is this game's, so the pictures are the same at every rate.
        var store = shell.Replays!;
        var fingerprint = data.Fingerprint.ToUpperInvariant();
        store.Save("Evening match", MatchRecordingFormat.Write(SyntheticInfo(tickRate: rate, fingerprint: fingerprint, winner: 1, seconds: 754, recorded: "2026-10-16T19:05:00Z"), [1]));
        store.Save("Out of sync", MatchRecordingFormat.Write(SyntheticInfo(tickRate: rate, fingerprint: fingerprint, ending: ReplayEndingKind.Desync, seconds: 312, recorded: "2026-10-15T11:40:00Z"), [2]));
        store.Save("Old build", MatchRecordingFormat.Write(SyntheticInfo(tickRate: rate, fingerprint: fingerprint, simulationId: OtherBuild, winner: 2, seconds: 1_400, recorded: "2026-10-01T08:00:00Z"), [3]));
        File.WriteAllBytes(store.PathOf("Broken"), [7, 7, 7]);
        OpenList(shell);
        var list = (ReplayMenuScreen)shell.CurrentScreen;
        TestHarness.Assert(list.Entries.Select(entry => entry.Name).SequenceEqual(["Evening match", "Out of sync", "Old build", "Broken"]) && list.Selected == 0, "the newest first, the first one chosen");
        TestHarness.Assert(DetailsText(shell).Contains("America won", StringComparison.Ordinal) && DetailsText(shell).Contains("12:34", StringComparison.Ordinal), $"the details of the chosen file ({DetailsText(shell)})");
        Capture(shell, "Replays-List", baselines, updating);
        Pick(shell, 2);
        TestHarness.Assert(DetailsText(shell).Contains("build fedcba987654", StringComparison.Ordinal), "a file that does not play says why beside the list");
        Capture(shell, "Replays-OtherBuild", baselines, updating);
        ShellTestInput.Click(shell, "ReplayMenu.wnd:ButtonLoadReplay");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Title: ReplayText.CannotPlayTitle } refusal && refusal.Message.Contains("fedcba987654", StringComparison.Ordinal) && shell.Phase == ShellPhase.Menus,
            "Load Replay on such a file is refused with its reason, and nothing starts");
        Capture(shell, "Replays-CannotPlay", baselines, updating);
        ShellTestInput.Click(shell, "MessageBox.wnd:ButtonOk");
        Pick(shell, 3);
        Capture(shell, "Replays-Damaged", baselines, updating);

        Pick(shell, 1);
        ShellTestInput.Click(shell, "ReplayMenu.wnd:ButtonCopyReplay");
        TestHarness.Assert(shell.CurrentScreen is ReplayRenameScreen { Name: "Out of sync" }, "the third button opens the rename popup for the chosen file");
        Capture(shell, "Replays-Rename", baselines, updating);
        TypeName(shell, "Evening match");
        ShellTestInput.Click(shell, "PopupReplay.wnd:ButtonSave");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Title: ReplayText.CannotRenameTitle } taken && taken.Message.Contains("already", StringComparison.Ordinal), "a name that is taken is refused over the popup");
        Capture(shell, "Replays-RenameTaken", baselines, updating);
        ShellTestInput.Click(shell, "MessageBox.wnd:ButtonOk");
        TypeName(shell, "Desync on Alpine");
        ShellTestInput.Click(shell, "PopupReplay.wnd:ButtonSave");
        TestHarness.Assert(shell.CurrentScreen is ReplayMenuScreen renamed && renamed.Entries[renamed.Selected].Name == "Desync on Alpine" && store.Exists("Desync on Alpine"), "the file is renamed and stays chosen");

        ShellTestInput.Click(shell, "ReplayMenu.wnd:ButtonDeleteReplay");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Buttons: ShellMessageBoxButtons.YesNo }, "Delete Replay asks first");
        Capture(shell, "Replays-Delete", baselines, updating);
        ShellTestInput.Click(shell, "MessageBox.wnd:ButtonNo");
        TestHarness.Assert(store.Exists("Desync on Alpine"), "No keeps the file");
        ShellTestInput.Click(shell, "ReplayMenu.wnd:ButtonDeleteReplay");
        ShellTestInput.Click(shell, "MessageBox.wnd:ButtonYes");
        TestHarness.Assert(!store.Exists("Desync on Alpine") && shell.CurrentScreen is ReplayMenuScreen { Entries.Length: 3 }, "Yes deletes it and the list shows the rest");

        // The score screen of a playback, with the note of what it found.
        shell.ReplayFinished(new MatchResult([], 0), "The playback reached the recorded end: America won.");
        Capture(shell, "Replays-PlaybackScore", baselines, updating);
        ShellTestInput.Click(shell, "MessageBox.wnd:ButtonOk");
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.CurrentScreen is ReplayMenuScreen && shell.Phase == ShellPhase.Menus, "after the score of a playback the way back leads to the replay list");
        Directory.Delete(directory, recursive: true);
    }

    private static GameShell NewShell(GameData data, string directory)
        => GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));

    private static void OpenList(GameShell shell)
    {
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonReplay");
    }

    private static string DetailsText(GameShell shell) => shell.CurrentScreen.Tree.FindByName(ReplayMenuScreen.DetailsWindow)?.Text?.Value ?? string.Empty;

    private static void Pick(GameShell shell, int row)
    {
        var list = shell.CurrentScreen.Tree.FindByName("ReplayMenu.wnd:ListboxReplayFiles")!;
        var content = CnCNew.Ui.Runtime.UiGadgetGeometry.ListContent(list);
        var height = CnCNew.Ui.Runtime.UiGadgetGeometry.RowHeight(list);
        ShellTestInput.ClickAt(shell, content.X + 20, content.Y + row * height + height / 2);
        TestHarness.Assert(list.ListBox!.SelectedIndex == row, $"row {row} is chosen");
    }

    private static void TypeName(GameShell shell, string name)
    {
        var field = shell.CurrentScreen.Tree.FindByName("PopupReplay.wnd:TextEntryReplayName")!;
        field.SetEntryText(name);
    }

    private static void Capture(GameShell shell, string name, Dictionary<string, string> baselines, bool updating)
    {
        shell.CurrentScreen.Tree.PointerLeft();
        foreach (var (width, height) in Resolutions)
        {
            shell.Resize(width, height);
            var list = shell.BuildDrawList();
            TestHarness.Assert(list.Statistics.UnresolvedLabels == 0, $"{name}@{width}x{height}: {list.Statistics.UnresolvedLabels} unresolved label(s)");
            var key = $"{name}@{width}x{height}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(list.Dump())));
            if (updating) baselines[key] = hash;
            else
            {
                TestHarness.Assert(baselines.TryGetValue(key, out var expected), $"{key}: no baseline recorded (run with CNCNEW_UPDATE_BASELINES=1 once, then review the diff)");
                TestHarness.Assert(expected == hash, $"{key}: draw list hash changed ({expected} -> {hash})");
            }
        }
        shell.Resize(NavWidth, NavHeight);
        foreach (var screen in shell.Stack)
            TestHarness.Assert(screen.Tree.UnregisteredCallbacks.Count == 0, $"{name}: unregistered callback(s) {string.Join(", ", screen.Tree.UnregisteredCallbacks.Keys)}");
    }

    private static Dictionary<string, string> LoadBaselines()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(BaselinePath)) return result;
        foreach (var line in File.ReadAllLines(BaselinePath))
        {
            var equals = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith('#') || equals < 0) continue;
            result[line[..equals]] = line[(equals + 1)..];
        }
        return result;
    }

    private static void SaveBaselines(Dictionary<string, string> baselines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, string.Concat(baselines.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}\n")));
        Console.WriteLine($"Wrote {baselines.Count} baseline hash(es) to {BaselinePath}.");
    }

    private static string RunDirectory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-replay-tests-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // ------------------------------------------------------------------------------------------------- a measurement

    private static void Measure(GameData data, int minutes)
    {
        var mapPath = data.Maps.First(path => path.Contains("Twilight Flame", StringComparison.OrdinalIgnoreCase));
        var options = SkirmishMatchOptions.StandardAt(data.Rate) with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(minutes * 60L) };
        string[] factions = ["FactionAmerica", "FactionChina", "FactionGLA", "FactionAmericaAirForceGeneral", "FactionChinaTankGeneral", "FactionGLAToxinGeneral", "FactionAmericaLaserGeneral", "FactionChinaNukeGeneral"];
        var setup = new MatchSetup(mapPath, factions[0], [.. factions[1..]], options) { Seed = 11, Spectator = true };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var live = LiveMatch.Create(data, setup, Viewport);
        var recorder = MatchRecorder.Attach(live)!;
        while (!live.IsOver) live.Advance(0.25);
        var recordSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart();
        var bytes = recorder.Build(MatchRecorder.EndingOf(live, false), RecordedAt);
        var buildMs = watch.Elapsed.TotalMilliseconds;
        var commands = ReplayFormat.Read(MatchRecordingFormat.Read(bytes).Replay).Commands.Length;
        watch.Restart();
        var playback = ReplayPlayback.Open(data, bytes, Viewport);
        var openMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        PlayToEnd(playback);
        var playSeconds = watch.Elapsed.TotalSeconds;
        TestHarness.Assert(playback.FinalHashMatches && playback.HashMismatches == 0, $"the playback of the measured match reaches its recorded end ({playback.HashMismatches} of {playback.HashesChecked} differ)");
        var seeks = new List<string>();
        var interval = playback.CheckpointInterval;
        var last = (playback.EndTick - 1) / interval * interval;
        foreach (var target in new[] { last / 2 / interval * interval, interval, last, last / 2 / interval * interval + data.Rate.Time.WholeSecondsToTicks(20) })
        {
            watch.Restart();
            var seek = playback.SeekTo(target);
            seeks.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seek.Tick / data.Rate.TicksPerSecond} s {(seek.Restored ? "restored" : "onwards")} +{seek.SteppedTicks} ticks {watch.Elapsed.TotalMilliseconds:0} ms {(seek.HashMatches ? "same" : "DIFFERENT")}"));
            TestHarness.Assert(seek.HashMatches, $"the seek to tick {target} lands at the recorded hash");
        }
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"       measured: {minutes} min, 8 players, {data.Rate.TicksPerSecond} Hz, {live.Tick} ticks; recorded in {recordSeconds:0.0} s (with the presentation), file {bytes.Length:N0} bytes, {commands:N0} commands, written in {buildMs:0} ms"));
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"       measured: opened in {openMs:0} ms, played to the end in {playSeconds:0.0} s, {playback.CheckpointTicks.Count} checkpoints in {playback.CheckpointBytes:N0} bytes, {playback.HashesChecked} recorded hashes, {playback.HashMismatches} differ"));
        Console.WriteLine($"       measured seeks: {string.Join("; ", seeks)}");
    }

    // ------------------------------------------------------------------------------------------------- a person at the menus

    /// <summary>What the client does around the shell, without a window: the match with its recorder, the playback with its spectator and its seeks.</summary>
    private sealed class Host(GameData data, GameShell shell, ShellScript script)
    {
        private readonly List<InputEvent> _matchInput = [];
        private LiveMatch? _match;
        private MatchRecorder? _recorder;
        private SpectatorController? _spectator;
        public ReplayPlayback? Playback { get; private set; }
        public string? Recorded { get; private set; }
        public List<ReplaySeek> Seeks { get; } = [];
        public List<string> Endings { get; } = [];

        public void Attach()
        {
            script.MatchInput = _matchInput.Add;
            script.MatchProbe = name => (Playback?.Match ?? _match) is not { } game ? null : name switch
            {
                "tick" => game.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "over" => game.IsOver ? "yes" : "no",
                "seeks" => Seeks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        public void Frame(double seconds)
        {
            script.Advance(shell, seconds);
            if (_match is { } match)
            {
                match.Advance(seconds);
                if (!match.IsOver) return;
                Recorded = _recorder!.Finish(shell.Replays!, MatchRecorder.EndingOf(match, leftByChoice: false), RecordedAt);
                shell.MatchFinished(MatchResult.From(match.Players, match.Winner, match.World, match.Tick, _recorder.Statistics), null);
                _match = null;
                return;
            }
            if (Playback is { } playback)
            {
                foreach (var input in _matchInput) _spectator!.Process(input);
                _matchInput.Clear();
                playback.Match.Advance(seconds);
                _spectator!.Update(seconds);
                _ = _spectator.BuildDrawList(NavWidth, NavHeight);
                if (_spectator.TakeSeekRequest() is { } target)
                {
                    Seeks.Add(playback.SeekTo(target));
                    var previous = _spectator;
                    _spectator = new SpectatorController(playback.Match, new MatchController(playback.Match, CnCNew.Ui.Settings.GameSettings.Default));
                    _spectator.TakeViewFrom(previous);
                    return;
                }
                if (_spectator.QuitRequested) { Playback = null; shell.ReplayStopped(); return; }
                if (!playback.IsOver) return;
                var same = playback.ResultMatchesRecorded(out _) && playback.FinalHashMatches && playback.HashMismatches == 0;
                Endings.Add($"{playback.Match.Tick} {(same ? "same" : "different")}");
                shell.ReplayFinished(playback.Result(), same ? "The playback reached the recorded end." : "The playback did not reach the recorded match.");
                Playback = null;
                return;
            }
            shell.Update(seconds);
            _ = shell.BuildDrawList();
            foreach (var output in shell.DrainOutputs())
            {
                if (output is StartMatchOutput start)
                {
                    _match = LiveMatch.Create(data, start.Setup, Viewport);
                    _recorder = MatchRecorder.Attach(_match);
                }
                else if (output is StartReplayOutput replay)
                {
                    try
                    {
                        Playback = ReplayPlayback.Open(data, replay.File, Viewport, new ReplayPlaybackOptions(CheckpointSeconds));
                        _spectator = new SpectatorController(Playback.Match, new MatchController(Playback.Match, CnCNew.Ui.Settings.GameSettings.Default));
                    }
                    catch (ReplayRefusedException refusal)
                    {
                        shell.ReplayFailed(refusal.Message);
                    }
                }
            }
        }
    }

    private static void PersonAtTheMenus(GameData data)
    {
        var directory = RunDirectory("person");
        var shell = NewShell(data, directory);
        shell.SkirmishMatchSeconds = 45;
        var script = ShellScript.Parse(
        [
            "wait screen MainMenuScreen",
            "click MainMenu.wnd:ButtonSinglePlayer",
            "click MainMenu.wnd:ButtonSkirmish",
            "wait screen SkirmishSetupScreen",
            "click SkirmishGameOptionsMenu.wnd:ButtonStart",
            "limit 200",
            "wait phase InMatch",
            "wait phase Results",
            "limit 20",
            "wait screen ScoreScreenScreen",
            "click ScoreScreen.wnd:ButtonOk",
            "wait screen MainMenuScreen",
            "click MainMenu.wnd:ButtonLoadReplay",
            "click MainMenu.wnd:ButtonReplay",
            "wait screen ReplayMenuScreen",
            "wait list ReplayMenu.wnd:ListboxReplayFiles contains Playable",
            "expect text ReplayDetails.wnd:StaticTextDetails contains No winner (time limit)",
            "expect text ReplayDetails.wnd:StaticTextDetails contains Length 0:45",
            "click ReplayMenu.wnd:ButtonLoadReplay",
            "limit 200",
            "wait phase InMatch",
            $"wait match tick atleast {35 * data.Rate.TicksPerSecond}",
            "key PageUp",
            "wait match seeks equals 1",
            "key PageUp",
            "wait match seeks equals 2",
            "key PageDown",
            "wait match seeks equals 3",
            "key D8",
            "wait phase Results",
            "limit 20",
            "wait screen MessageBoxScreen",
            "expect text MessageBox.wnd:StaticTextMessage contains reached the recorded end",
            "click MessageBox.wnd:ButtonOk",
            "click ScoreScreen.wnd:ButtonOk",
            "wait screen ReplayMenuScreen",
            "click ReplayMenu.wnd:ButtonCopyReplay",
            "wait screen ReplayRenameScreen",
            "type PopupReplay.wnd:TextEntryReplayName Watched twice",
            "click PopupReplay.wnd:ButtonSave",
            "wait screen ReplayMenuScreen",
            "wait list ReplayMenu.wnd:ListboxReplayFiles contains Watched twice",
            "click ReplayMenu.wnd:ButtonDeleteReplay",
            "wait screen MessageBoxScreen",
            "click MessageBox.wnd:ButtonYes",
            "wait screen ReplayMenuScreen",
            "wait list ReplayMenu.wnd:ListboxReplayFiles lacks Watched twice",
            "expect text ReplayDetails.wnd:StaticTextDetails contains No replays yet",
            "click ReplayMenu.wnd:ButtonBack",
            "wait screen MainMenuScreen",
        ], "replay-person");
        var host = new Host(data, shell, script);
        host.Attach();
        for (var frame = 0; frame < 20_000 && !script.IsFinished; frame++) host.Frame(0.25);
        foreach (var line in script.Report().TakeLast(4)) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount})");
        TestHarness.Assert(host.Recorded is not null, "the skirmish of the menus was recorded");
        TestHarness.Assert(host.Seeks.Count == 3 && host.Seeks.All(seek => seek.HashMatches && seek.RecordedHash is not null), $"three seeks, each at the hash the recording kept ({string.Join(", ", host.Seeks.Select(seek => $"{seek.Tick}:{(seek.HashMatches ? "same" : "different")}"))})");
        TestHarness.Assert(host.Seeks.Count == 3 && host.Seeks[1].Tick < host.Seeks[0].Tick && host.Seeks[2].Tick > host.Seeks[1].Tick && host.Seeks[1].Restored,
            $"back, back again from a checkpoint, then forward ({string.Join(", ", host.Seeks.Select(seek => seek.Tick))})");
        TestHarness.Assert(host.Endings.Count == 1 && host.Endings[0].EndsWith("same", StringComparison.Ordinal), $"the playback ended where the recording ended, with its result and hash ({string.Join(", ", host.Endings)})");
        Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
    }
}
