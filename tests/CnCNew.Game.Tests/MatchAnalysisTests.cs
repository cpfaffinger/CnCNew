using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CnCNew.Game.Analysis;
using CnCNew.Game.Data;
using CnCNew.Game.Diagnostics;
using CnCNew.Game.Live;
using CnCNew.Game.Replays;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using MatchResult = CnCNew.Game.Shell.MatchResult;

namespace CnCNew.Game.Tests;

/// <summary>
/// The match analysis (docs/MATCH-ANALYSIS.md) without a person: a skirmish is analysed while it runs and again from its replay, and the two analyses are the same; the export
/// of a replay is the same bytes on two runs (and under another culture); every sample lies on the original's frame grid; a replay that leaves its recorded match gives no
/// analysis; the analysis screen is drawn in six resolutions and pinned by hashes in <c>baselines/analysis-screens.txt</c> (<c>CNCNEW_UPDATE_BASELINES=1</c> records them);
/// a <see cref="ShellScript"/> opens the analysis after a match and from the replay list. The shared field comparison of the diagnosis tools is checked on its own.
/// What only a person can judge - whether the report is readable and shows the course of the match the person remembers - is not judged here.
/// </summary>
internal static class MatchAnalysisTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const int NavWidth = 1280, NavHeight = 720;
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/analysis-screens.txt";
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly Vector2 Viewport = new(NavWidth, NavHeight);

    /// <summary>The match of these tests: a human (America) who gives group orders against a computer player (China, rush tactics) on Alpine Assault.</summary>
    private const int MatchSeconds = 240;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(MatchAnalysisTests));
        harness.Run("The shared field comparison: paths from a description and from a saved document, elements by id, rules of equality and of missing paths", FieldComparison);
        harness.Run("The charts' numbers: nice axis tops, axis steps, numbers and times as the report writes them", ChartNumbers);
        harness.Run("The export of an analysis: the schema, the tables, the same bytes under another culture", ExportFormat);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP MatchAnalysisTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        Played? played = null;
        harness.Run($"A skirmish of {MatchSeconds} s analysed while it runs and from its replay: the same analysis, the export the same bytes twice, every sample on the frame grid",
            () => played = LiveAndReplay(data));
        if (played is null) return harness.Failures;
        harness.Run("What the analysis holds agrees with the match: money, income, the score counters, losses and kills, the regions, the heat maps, the commands", () => Contents(data, played));
        harness.Run("A replay that leaves its recorded match gives no analysis; a file that does not play here is refused with its reason", () => Refusals(data, played));
        harness.Run("The graphical report: one HTML page of the analysis, the same bytes twice, every chart and both heat maps in it, nothing loaded from elsewhere", () => Report(played));
        var baselines = LoadBaselines();
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("The analysis screen in six resolutions: every page, the computing state, the score screen's button", () => Screens(data, played, baselines, updating));
        harness.Run("No two states of the analysis screen share a picture", () => TestHarness.Assert(baselines.Count >= 48 && baselines.Values.Distinct(StringComparer.Ordinal).Count() == baselines.Count,
            $"{baselines.Count} hashes, {baselines.Values.Distinct(StringComparer.Ordinal).Count()} distinct"));
        if (updating) SaveBaselines(baselines);
        harness.Run("A person opens the analysis after a match from the score screen and from the replay list, and turns its pages", () => PersonOpensTheAnalysis(data));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- without game data

    private sealed class Sample
    {
        public void Describe(CnCNew.Simulation.State.IStateWriter writer, int health, bool extra)
        {
            writer.BeginObject(null);
            writer.Int("tick", 7);
            writer.BeginArray("objects");
            foreach (var id in extra ? new[] { 3, 5, 9 } : [3, 9])
            {
                writer.BeginObject(null);
                writer.UInt("id", (ulong)id);
                writer.Text("template", "Tank");
                writer.Fixed("health", Fixed.FromInt(id == 9 ? health : 100));
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
    }

    private static void FieldComparison()
    {
        var sample = new Sample();
        var left = StateFields.Of(writer => sample.Describe(writer, 100, false));
        var right = StateFields.Of(writer => sample.Describe(writer, 80, true));
        TestHarness.Assert(left.ContainsKey(".tick") && left.Keys.Any(path => path.StartsWith("objects[id=9]", StringComparison.Ordinal)), $"paths from a description, elements by their id ({string.Join(", ", left.Keys)})");
        var differences = StateFields.Compare(left, right);
        TestHarness.Assert(differences.Any(item => item.Path == "objects[id=9].health" && item.Left is { Kind: StateFieldKind.Fixed } && item.Right is { Kind: StateFieldKind.Fixed }),
            $"a changed value is found at its element ({string.Join("; ", differences)})");
        TestHarness.Assert(differences.Count(item => item.Path.StartsWith("objects[id=5]", StringComparison.Ordinal) && item.Left is null) == 3,
            "an element only one side has is three missing paths - and the elements after it are not shifted");
        TestHarness.Assert(!differences.Any(item => item.Path.StartsWith("objects[id=3]", StringComparison.Ordinal)), "an equal element is no difference");
        TestHarness.Assert(StateFields.Compare(left, right, new StateFieldRules(RightOnly: false)).All(item => item.Left is not null), "paths only the right side has can be left out");
        TestHarness.Assert(StateFields.Compare(left, right, new StateFieldRules(Ignore: path => path.EndsWith(".health", StringComparison.Ordinal), RightOnly: false)).IsEmpty, "ignored paths are not compared");
        TestHarness.Assert(StateFields.Compare(left, right, new StateFieldRules(Equal: (x, y) => true, RightOnly: false)).IsEmpty, "a rule of equality decides what is equal");
        TestHarness.Assert(StateFields.Compare(left, right, limit: 2).Length == 2, "at most the limit");
        TestHarness.Assert(differences.Select(item => item.Path).SequenceEqual(differences.Select(item => item.Path).Order(StringComparer.Ordinal)), "in ordinal order of the paths");

        using var a = JsonDocument.Parse("""{"world":{"objects":[{"id":3,"health":100},{"id":9,"health":100}]},"recording":"AAAA","list":[1,2]}""");
        using var b = JsonDocument.Parse("""{"world":{"objects":[{"id":9,"health":99},{"id":3,"health":100}]},"recording":"BBBB","list":[1,3]}""");
        var fromJson = StateFields.Compare(StateFields.OfJson(a.RootElement, name => name == "recording"), StateFields.OfJson(b.RootElement, name => name == "recording"));
        TestHarness.Assert(fromJson.Select(item => item.ToString()).SequenceEqual(["list[1]: 2 -> 3", "world.objects[id=9].health: 100 -> 99"]),
            $"a saved document: elements by id whatever their order, others by position, a skipped property nowhere ({string.Join("; ", fromJson)})");
    }

    private static void ChartNumbers()
    {
        foreach (var (value, nice) in new (long, long)[] { (0, 1), (1, 1), (2, 2), (3, 5), (7, 10), (11, 20), (21, 25), (26, 50), (51, 100), (199, 200), (240, 250), (2_600, 5_000), (12_345, 20_000) })
            TestHarness.Assert(AnalysisCharts.NiceCeiling(value) == nice, $"the top of an axis for {value} is {nice} ({AnalysisCharts.NiceCeiling(value)})");
        TestHarness.Assert(AnalysisCharts.AxisSteps(20_000).SequenceEqual([0L, 5_000, 10_000, 15_000, 20_000]) && AnalysisCharts.AxisSteps(25).SequenceEqual([0L, 5, 10, 15, 20, 25]), "the axis lines lie on round values");
        TestHarness.Assert(AnalysisCharts.Number(1_234_567) == "1 234 567" && AnalysisCharts.Number(-950) == "-950" && AnalysisCharts.Time(754) == "12:34" && AnalysisCharts.Time(3_725) == "1:02:05",
            "numbers and times as the report writes them");
        TestHarness.Assert(AnalysisPalette.Rgb(0x12, 0x34, 0x56) == 0x123456 && AnalysisPalette.Red(0x123456) == 0x12 && AnalysisPalette.Shade(0x606060, 2) == 0xAFAFAF, "colours by bytes");
    }

    private static MatchAnalysis SyntheticAnalysis()
        => new("Maps/Alpine Assault/Alpine Assault.map", 60, "original", 0, 300, 1, "completed", "synthetic", 4_000, 3_000,
            [new AnalysisPlayer(1, "Ann \"the\"\ttab", "FactionAmerica", 0, false, string.Empty, 0x285AE6, true, false, 5_000, 4_200, 3, 1_800,
                [new AnalysisCount("AmericaTankCrusader", 2, 1_800)], [], [new AnalysisCount("AmericaPowerPlant", 1, 800)], [], 0, 1, 0, 0, 0, 12, [8, 4]),
             new AnalysisPlayer(2, string.Empty, "FactionChina", 0, true, "builtin/rush", 0xDC2828, false, true, 3_000, 3_500, 0, 0,
                [], [new AnalysisCount("ChinaTankBattleMaster", 3, 1_800)], [], [], 0, 0, 0, 1, 0, 30, [20, 10])],
            [new AnalysisSample(0, 1, 10_000, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0), new AnalysisSample(150, 1, 9_000, 500, 500, 1_000, 900, 1, 0, 900, 0, 1, 1, 5, 2, 4, 0, 0, 1)],
            [new AnalysisEvent(150, "unit-lost", 2, 1, "ChinaTankBattleMaster", 600, 1_234, 567), new AnalysisEvent(200, "special-power", 2, 0, "SuperweaponCashHack", 0, -1, -1)],
            [new AnalysisHeat("fights", 0, [.. new int[MatchAnalysis.HeatGridSize * MatchAnalysis.HeatGridSize]])], 0);

    private static void ExportFormat()
    {
        var analysis = SyntheticAnalysis();
        var json = MatchAnalysisFormat.ToJson(analysis);
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            TestHarness.Assert(root.GetProperty("format").GetString() == MatchAnalysis.Format && root.GetProperty("schemaVersion").GetInt32() == MatchAnalysis.SchemaVersion,
                "the JSON names its format and schema version");
            TestHarness.Assert(root.GetProperty("players")[0].GetProperty("name").GetString() == "Ann \"the\"\ttab" && root.GetProperty("players")[1].GetProperty("color").GetString() == "DC2828",
                "texts come back as they were; colours as six hex digits");
            TestHarness.Assert(root.GetProperty("events").GetArrayLength() == 2 && root.GetProperty("samples")[1].GetProperty("frame").GetInt64() == 150, "events and samples in their order");
        }
        TestHarness.Assert(!json.Contains((byte)'\r') && json[^1] == (byte)'\n', "\\n line ends, a line end at the end");
        var players = MatchAnalysisFormat.PlayersTsv(analysis).Split('\n');
        TestHarness.Assert(players[0].StartsWith("player\tname\tfaction", StringComparison.Ordinal) && players[1].Split('\t').Length == players[0].Split('\t').Length && players[1].Contains("Ann \"the\" tab", StringComparison.Ordinal),
            "the players' table: a header, a row per player with as many cells, a tab in a name written as a space");
        TestHarness.Assert(MatchAnalysisFormat.SamplesTsv(analysis).Split('\n')[2].StartsWith("150\t5\t1\t9000", StringComparison.Ordinal), "the samples' table: frame, second, player, money");
        TestHarness.Assert(MatchAnalysisFormat.EventsTsv(analysis).Contains("150\t5\tunit-lost\t2\t1\tChinaTankBattleMaster\t600\t1234\t567\n", StringComparison.Ordinal), "the events' table");
        var culture = CultureInfo.CurrentCulture;
        byte[][] Export() => [.. MatchAnalysisFormat.Files(analysis).Select(file => file.Bytes).Append(Encoding.UTF8.GetBytes(MatchReport.ToHtml(analysis, "A title")))];
        var invariant = Export();
        try
        {
            foreach (var name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var other = Export();
                TestHarness.Assert(other.Zip(invariant).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)), $"the same bytes under the culture {name}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // ------------------------------------------------------------------------------------------------- a played match

    private sealed record Played(byte[] File, MatchRecordingInfo Info, MatchAnalysis Live, MatchAnalysis FromReplay, LiveMatch Match);

    private static Played LiveAndReplay(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var options = SkirmishMatchOptions.StandardAt(data.Rate) with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(MatchSeconds) };
        var setup = new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], options) { Seed = 5, Ai = [new MatchAi(SkirmishAiKind.Builtin), new MatchAi(SkirmishAiKind.Builtin, CnCNew.Simulation.Scripting.ScriptDifficulty.Normal, AiTactics.Rush)] };
        var live = LiveMatch.Create(data, setup, Viewport);
        var recorder = MatchRecorder.Attach(live) ?? throw new InvalidOperationException("the match cannot be recorded");
        var analysis = LiveMatchAnalysis.Attach(live) ?? throw new InvalidOperationException("the match cannot be analysed");
        var nextOrder = data.Rate.Time.WholeSecondsToTicks(8);
        var orders = 0;
        while (!live.IsOver)
        {
            if (live.Tick >= nextOrder)
            {
                nextOrder += data.Rate.Time.WholeSecondsToTicks(9);
                var units = live.World.Entities.Where(entity => entity.Owner == live.Human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead).Select(entity => entity.Id).Take(6).ToArray();
                if (units.Length > 0)
                {
                    var home = live.Terrain.StartPositions[live.Match.Slots[0].StartPosition];
                    var offset = Fixed.FromInt(orders % 2 == 0 ? 160 : -110);
                    live.Commands.Issue((sequence, tick) => new GroupMoveCommand(live.Human, sequence, tick, [.. units], home.X + offset, home.Y + offset, false));
                    orders++;
                }
            }
            live.Advance(0.25);
        }
        var ending = MatchRecorder.EndingOf(live, leftByChoice: false);
        var bytes = recorder.Build(ending, RecordedAt);
        var liveAnalysis = analysis.Finish(MatchRecordingFormat.KindName(ending.Kind));
        TestHarness.Assert(ReferenceEquals(liveAnalysis, analysis.Finish("other")), "a finished analysis stays the one it was");
        var info = MatchRecordingFormat.Read(bytes).Info;
        var first = ReplayAnalysis.Run(data, bytes, source: "test.cncreplay");
        var second = ReplayAnalysis.Run(data, bytes, source: "test.cncreplay");
        var firstFiles = MatchAnalysisFormat.Files(first);
        var secondFiles = MatchAnalysisFormat.Files(second);
        TestHarness.Assert(firstFiles.Zip(secondFiles).All(pair => pair.First.Name == pair.Second.Name && pair.First.Bytes.AsSpan().SequenceEqual(pair.Second.Bytes)),
            "two analyses of the same replay are the same bytes in every file");
        var liveJson = MatchAnalysisFormat.ToJson(liveAnalysis with { Source = first.Source });
        TestHarness.Assert(liveJson.AsSpan().SequenceEqual(firstFiles[0].Bytes), "the analysis of the running match is the analysis of its replay: one core, the same numbers" + FirstDifference(liveJson, firstFiles[0].Bytes));
        var grid = (long)CnCNew.Data.OriginalFrame.PerSecond * MatchAnalysis.SampleSeconds;
        var endFrame = first.EndFrame;
        TestHarness.Assert(first.Samples.All(sample => sample.Frame % grid == 0 || sample.Frame == endFrame), "every sample lies on the frame grid of five seconds (or is the one at the end)");
        TestHarness.Assert(first.Samples.Select(sample => sample.Frame).Distinct().Count() == first.EndFrame / grid + 1 + (first.EndFrame % grid == 0 ? 0 : 1)
            && first.EndFrame == live.Tick / data.Rate.TicksPerOriginalFrame,
            $"a sample every five seconds from the start to the end ({first.Samples.Select(sample => sample.Frame).Distinct().Count()}, end frame {first.EndFrame})");
        TestHarness.Assert(first.Source == "test.cncreplay" && liveAnalysis.Source == "live" && first.TickRate == data.Rate.TicksPerSecond && first.Rules == data.Rules.Name, "source, rate and rules are named");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"       analysed {first.EndFrame} frames: {first.Samples.Length} samples, {first.Events.Length} events, JSON {firstFiles[0].Bytes.Length} bytes, {orders} orders of the human; SHA-256 {Convert.ToHexString(SHA256.HashData(firstFiles[0].Bytes))[..16]}"));
        return new Played(bytes, info, liveAnalysis, first, live);
    }

    private static string FirstDifference(byte[] a, byte[] b)
    {
        if (a.AsSpan().SequenceEqual(b)) return string.Empty;
        var lines = (Encoding.UTF8.GetString(a).Split('\n'), Encoding.UTF8.GetString(b).Split('\n'));
        for (var line = 0; line < Math.Min(lines.Item1.Length, lines.Item2.Length); line++)
            if (lines.Item1[line] != lines.Item2[line]) return $" (line {line + 1}: '{lines.Item1[line].Trim()}' against '{lines.Item2[line].Trim()}')";
        return $" ({lines.Item1.Length} against {lines.Item2.Length} lines)";
    }

    private static void Contents(GameData data, Played played)
    {
        var analysis = played.FromReplay;
        var match = played.Match;
        TestHarness.Assert(analysis.Players.Select(player => player.Player).SequenceEqual(played.Info.Players.Select(player => player.Player)) && analysis.Players[1].IsComputer && analysis.Players[1].Ai == "builtin/rush",
            "the players of the header, the computer player with its AI");
        foreach (var player in analysis.Players)
        {
            var row = played.Info.Result.First(result => result.Player == player.Player);
            TestHarness.Assert(player.Collected == row.Resources, $"player {player.Player}: the credits collected are the score screen's ({player.Collected} against {row.Resources})");
            // The score screen counts as the original's ScoreKeeper does - only the kinds SCORE, SCORE_CREATE, SCORE_DESTROY (F-42) -, the analysis every unit and building.
            TestHarness.Assert(player.UnitsBuiltCount >= row.UnitsBuilt && player.StructuresBuilt.Sum(item => item.Count) >= row.BuildingsBuilt
                && player.UnitsLostCount >= row.UnitsLost && player.StructuresLostCount >= row.BuildingsLost && player.Kills >= row.UnitsDestroyed,
                $"player {player.Player}: the analysis counts at least what the score screen counts ({player.UnitsBuiltCount}/{row.UnitsBuilt}, {player.UnitsLostCount}/{row.UnitsLost}, {player.StructuresLostCount}/{row.BuildingsLost}, {player.Kills}/{row.UnitsDestroyed})");
            var last = analysis.SamplesOf(player.Player).Last();
            TestHarness.Assert(last.Money == match.World.MoneyOf(new CnCNew.Simulation.Ids.PlayerId(player.Player)).Floor(), $"player {player.Player}: the money of the last sample is the world's");
            TestHarness.Assert(last.Collected == player.Collected && last.Spent == player.Spent && last.Kills == player.Kills, $"player {player.Player}: the last sample and the totals agree");
            var minute = 60L * data.Rate.TicksPerSecond;
            TestHarness.Assert(player.CommandsPerMinute.Length == (match.World.Tick + minute - 1) / minute, $"player {player.Player}: a count of commands for every minute begun");
            // The human of this match only moves its units; the computer player builds its economy and its army.
            if (player.IsComputer) TestHarness.Assert(player.Spent > 0 && player.Collected > 0 && player.UnitsBuiltCount > 0, $"player {player.Player}: the computer player spent, collected and built");
            TestHarness.Assert(analysis.SamplesOf(player.Player).Zip(analysis.SamplesOf(player.Player).Skip(1)).All(pair => pair.Second.Collected >= pair.First.Collected && pair.Second.UnitsLost >= pair.First.UnitsLost),
                $"player {player.Player}: what is counted only grows");
        }
        var humanCommands = ReplayFormatCommands(played.File).Count(command => command.Player.Value == analysis.Players[0].Player && !command.RequiresSystem && command.Tick < match.World.Tick);
        TestHarness.Assert(analysis.Players[0].Commands == humanCommands && humanCommands > 0, $"the human's commands are the replay's ({analysis.Players[0].Commands} against {humanCommands})");
        var lost = analysis.Players.Sum(player => player.UnitsLostCount + player.StructuresLostCount);
        TestHarness.Assert(analysis.Events.Count(item => item.Kind is "unit-lost" or "structure-lost") == lost, "every loss is an event");
        TestHarness.Assert(analysis.Players.Sum(player => player.Kills) <= lost, "no more kills than losses");
        TestHarness.Assert(analysis.HeatOf("fights", 0)!.Cells.Sum() <= lost && analysis.Events.Where(item => item.Kind is "unit-lost" or "structure-lost").All(item => item.X >= -1),
            "the fights' heat map counts losses with a place");
        foreach (var player in analysis.Players)
            TestHarness.Assert(analysis.HeatOf("movement", player.Player)!.Cells.Sum() == analysis.SamplesOf(player.Player).Sum(sample => sample.ArmyUnits),
                $"player {player.Player}: the movement map counts every army unit of every sample");
        TestHarness.Assert(analysis.Samples.GroupBy(sample => sample.Frame).All(group => group.Sum(sample => sample.Regions) <= MatchAnalysis.RegionsPerSide * MatchAnalysis.RegionsPerSide)
            && analysis.Samples.Any(sample => sample.Regions > 0), "the regions held: never more than the map has, and somebody holds some");
        TestHarness.Assert(analysis.Events.Select(item => item.Frame).Zip(analysis.Events.Select(item => item.Frame).Skip(1)).All(pair => pair.Second >= pair.First), "the events in time order");
        TestHarness.Assert(analysis.UnresolvedEvents == 0, $"every event's object was known ({analysis.UnresolvedEvents} were not)");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"       {string.Join("; ", analysis.Players.Select(player => $"player {player.Player}: collected {player.Collected}, spent {player.Spent}, built {player.UnitsBuiltCount}, lost {player.UnitsLostCount + player.StructuresLostCount}, kills {player.Kills}, commands {player.Commands}"))}"));
    }

    private static ImmutableArray<SimCommand> ReplayFormatCommands(byte[] file) => CnCNew.Simulation.Replay.ReplayFormat.Read(MatchRecordingFormat.Read(file).Replay).Commands;

    private static void Refusals(GameData data, Played played)
    {
        var info = played.Info;
        var forged = MatchRecordingFormat.Write(info with { Hashes = [.. info.Hashes.Select((item, index) => index == 3 ? (item.Tick, item.Hash ^ 1) : item)] }, MatchRecordingFormat.Read(played.File).Replay);
        ReplayRefusedException? refused = null;
        try { ReplayAnalysis.Run(data, forged); }
        catch (ReplayRefusedException exception) { refused = exception; }
        TestHarness.Assert(refused is { Kind: ReplayCompatibilityKind.Damaged } && refused.Message.Contains("left the recorded match", StringComparison.Ordinal),
            $"a recorded hash the match does not reach: no analysis ({refused?.Message})");
        refused = null;
        try { ReplayAnalysis.Run(data, played.File, machine: new ReplayMachine(data.Fingerprint, data.Rate.TicksPerSecond, data.Rules.Name, "fedcba9876543210fedcba9876543210fedcba98", data.Content.Exists)); }
        catch (ReplayRefusedException exception) { refused = exception; }
        TestHarness.Assert(refused is { Kind: ReplayCompatibilityKind.OtherBuild }, "a replay of another build is refused with that reason, as the playback refuses it");
    }

    private static void Report(Played played)
    {
        var html = MatchReport.ToHtml(played.FromReplay, "Test match");
        TestHarness.Assert(html == MatchReport.ToHtml(played.FromReplay, "Test match"), "the same analysis gives the same page");
        TestHarness.Assert(html.StartsWith("<!doctype html>", StringComparison.Ordinal) && !html.Contains('\r') && !html.Contains("<script", StringComparison.Ordinal)
            && !html.Contains("http://", StringComparison.Ordinal) && !html.Contains("https://", StringComparison.Ordinal), "one page of its own: no script, nothing loaded from elsewhere");
        foreach (var chart in AnalysisCharts.Timelines(played.FromReplay)) TestHarness.Assert(html.Contains($"<h3>{System.Net.WebUtility.HtmlEncode(chart.Title)}</h3>", StringComparison.Ordinal), $"the chart '{chart.Title}' is in it");
        TestHarness.Assert(html.Contains("aria-label=\"Movement\"", StringComparison.Ordinal) && html.Contains("aria-label=\"Fights\"", StringComparison.Ordinal) && html.Contains("Loss exchange", StringComparison.Ordinal)
            && html.Contains("Army composition", StringComparison.Ordinal), "both heat maps, the composition and the loss exchange");
        TestHarness.Assert(html.Split("<polyline").Length - 1 == AnalysisCharts.Timelines(played.FromReplay).Length * played.FromReplay.Players.Length, "a line per player in every timeline");
    }

    // ------------------------------------------------------------------------------------------------- the screens

    private static GameShell NewShell(GameData data, string directory)
        => GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(NavWidth, NavHeight));

    private static void Screens(GameData data, Played played, Dictionary<string, string> baselines, bool updating)
    {
        var directory = RunDirectory("screens");
        var shell = NewShell(data, directory);
        shell.MatchFinished(MatchResult.From(played.Match.Players, played.Match.Winner, played.Match.World, played.Match.Tick, null) with { Analysis = played.Live }, null);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:ButtonSaveReplay") is { IsVisible: true, IsEffectivelyEnabled: true } button && button.Text?.Value == "Analysis",
            "the score screen of an analysed match offers the analysis");
        Capture(shell, "Score-WithAnalysis", baselines, updating);
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonSaveReplay");
        var screen = shell.CurrentScreen as MatchAnalysisScreen ?? throw new InvalidOperationException("the analysis did not open");
        shell.CurrentScreen.Tree.PointerLeft();
        var livePage1 = shell.BuildDrawList().Dump();
        for (var page = 0; page < MatchAnalysisScreen.PageTitles.Length; page++)
        {
            TestHarness.Assert(screen.Page == page && screen.Header.Contains(MatchAnalysisScreen.PageTitles[page], StringComparison.Ordinal), $"page {page + 1}: {screen.Header}");
            Capture(shell, $"Analysis-Page{page + 1}", baselines, updating);
            ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonBuddy");
        }
        TestHarness.Assert(screen.Page == 0, "the pages wrap around");
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonSaveReplay");
        TestHarness.Assert(screen.Page == MatchAnalysisScreen.PageTitles.Length - 1, "Previous page from the first is the last");
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.CurrentScreen is ScoreScreenScreen, "OK leads back to the score screen");

        // From the replay list: the analysis is computed first, a piece every frame, with its progress in the header.
        shell.Replays!.Save("Analysed", played.File);
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonOk");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonLoadReplay");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonReplay");
        Capture(shell, "Replays-AnalysisButton", baselines, updating);
        var ticks = MatchAnalysisScreen.TicksPerUpdate;
        try
        {
            MatchAnalysisScreen.TicksPerUpdate = (int)(played.Match.World.Tick / 3) + 1;
            ShellTestInput.Click(shell, ReplayMenuScreen.AnalysisButton);
            var computing = shell.CurrentScreen as MatchAnalysisScreen ?? throw new InvalidOperationException("the analysis of the replay did not open");
            TestHarness.Assert(computing.IsComputing && computing.Header.StartsWith("Analysing the replay", StringComparison.Ordinal), "it opens computing");
            shell.Update(0.016);
            TestHarness.Assert(computing.IsComputing && computing.Header.Contains("33 %", StringComparison.Ordinal), $"and shows how far it has come ({computing.Header})");
            Capture(shell, "Analysis-Computing", baselines, updating);
            for (var frame = 0; frame < 10 && computing.IsComputing; frame++) shell.Update(0.016);
            TestHarness.Assert(!computing.IsComputing && computing.Analysis is { } done && MatchAnalysisFormat.ToJson(done with { Source = "x" }).AsSpan().SequenceEqual(MatchAnalysisFormat.ToJson(played.FromReplay with { Source = "x" })),
                "then it shows the analysis of the replay");
            // The analysis of the replay is the analysis of the match: the same picture as the one opened from the score screen.
            shell.Resize(NavWidth, NavHeight);
            TestHarness.Assert(shell.BuildDrawList().Dump() == livePage1, "the analysis of the replay draws the same first page as the analysis of the match");
        }
        finally
        {
            MatchAnalysisScreen.TicksPerUpdate = ticks;
        }
        ShellTestInput.Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.CurrentScreen is ReplayMenuScreen, "OK leads back to the replay list");
        Directory.Delete(directory, recursive: true);
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
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-analysis-tests-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    // ------------------------------------------------------------------------------------------------- a person at the menus

    private static void PersonOpensTheAnalysis(GameData data)
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
            "expect text ScoreScreen.wnd:ButtonSaveReplay equals Analysis",
            "click ScoreScreen.wnd:ButtonSaveReplay",
            "wait screen MatchAnalysisScreen",
            "expect text ScoreScreen.wnd:ChallengeWinLossText contains page 1 of 6: Economy",
            "click ScoreScreen.wnd:ButtonBuddy",
            "expect text ScoreScreen.wnd:ChallengeWinLossText contains Army",
            "key Right",
            "expect text ScoreScreen.wnd:ChallengeWinLossText contains Spending and losses",
            "key Left",
            "key Left",
            "expect text ScoreScreen.wnd:ChallengeWinLossText contains Economy",
            "click ScoreScreen.wnd:ButtonOk",
            "wait screen ScoreScreenScreen",
            "click ScoreScreen.wnd:ButtonOk",
            "wait screen MainMenuScreen",
            "click MainMenu.wnd:ButtonLoadReplay",
            "click MainMenu.wnd:ButtonReplay",
            "wait screen ReplayMenuScreen",
            "wait list ReplayMenu.wnd:ListboxReplayFiles contains Playable",
            "pick ReplayMenu.wnd:ListboxReplayFiles Playable",
            $"click {ReplayMenuScreen.AnalysisButton}",
            "wait screen MatchAnalysisScreen",
            "limit 120",
            "wait text ScoreScreen.wnd:ChallengeWinLossText contains page 1 of 6",
            "limit 20",
            "key Right",
            "key Right",
            "key Right",
            "key Right",
            "expect text ScoreScreen.wnd:ChallengeWinLossText contains Map",
            "key Escape",
            "wait screen ReplayMenuScreen",
            // The same row chosen again after the analysis: one click, not a double click with the one before the analysis (the list's clock stood still meanwhile).
            "pick ReplayMenu.wnd:ListboxReplayFiles Playable",
            "expect phase Menus",
            "expect screen ReplayMenuScreen",
            "click ReplayMenu.wnd:ButtonBack",
            "wait screen MainMenuScreen",
        ], "analysis-person");
        var host = new ReplayBrowserTests.Host(data, shell, script);
        host.Attach();
        for (var frame = 0; frame < 20_000 && !script.IsFinished; frame++) host.Frame(0.25);
        foreach (var line in script.Report().TakeLast(4)) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount})");
        TestHarness.Assert(host.LastAnalysis is { } analysis && host.Recorded is not null
            && MatchAnalysisFormat.ToJson(analysis with { Source = "x" }).AsSpan().SequenceEqual(MatchAnalysisFormat.ToJson(ReplayAnalysis.Run(data, shell.Replays!.Read(host.Recorded)) with { Source = "x" })),
            "the analysis after the match is the analysis of its replay");
        Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
    }
}
