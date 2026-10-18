using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Replays;
using CnCNew.Game.Shell;
using CnCNew.Game.Spectator;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The observer features of S3-M2 (docs/SPECTATOR-MODE.md §14) without a person: the event ticker of a match of two computer players is the same on two runs, the same
/// at 30, 60 and 120 Hz up to a frame, and the same in the replay of that match however the viewer seeks; the view of every player and of everything is chosen by key and
/// kept across a seek; the camera goes to the place of a ticker line by key and by click; the research panel shows what the world holds. The observer HUD is drawn in six
/// resolutions and pinned by hashes in <c>baselines/observer-screens.txt</c> (per rate: the match is the same game at every rate, not the same bits;
/// <c>CNCNEW_UPDATE_BASELINES=1</c> records them), and a <see cref="ShellScript"/> person opens a replay and uses all of it.
/// What only a person can judge - whether one names the players' intentions from the display alone - is not judged here.
/// </summary>
internal static class ObserverTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/observer-screens.txt";
    private static readonly Vector2 Viewport = new(1280, 720);
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The match of these tests: two computer players with the rush profile on Alpine Assault, seed 11, for this long.</summary>
    private const int MatchSeconds = 420;
    private const int Seed = 11;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ObserverTests));
        harness.Run("Every new action has a key, and the views and the research tab have buttons", Keys);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ObserverTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var contentDir = args[index + 1];
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        Watched? watched = null;
        harness.Run($"The ticker of a {MatchSeconds} s match of two computer players: a first blood and the moments after it, with places; two runs give the same lines", () => watched = LiveTicker(data));
        if (watched is null) return harness.Failures;
        harness.Run("The ticker is a function of the match: the same lines at 30, 60 and 120 Hz, each within one frame of the original", () => AcrossRates(contentDir, watched));
        harness.Run("The replay of the match has the same ticker; seeking back and forth shows the same lines up to the frame the playback stands at", () => ReplayTicker(data, watched));
        harness.Run("Views by key and button: every player's and everything; a view, the tab and the ticker are kept across a seek", () => ViewsAcrossSeek(data, watched));
        harness.Run("The camera goes to the place of a ticker line by key and by a click on the line", () => GoToEvents(data, watched));
        harness.Run("The research panel shows the upgrades and sciences the world holds for every player", () => Research(watched));
        var baselines = LoadBaselines();
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("The observer HUD in six resolutions: every tab, a player's view, the ticker shown and hidden, after a jump to an event", () => Screens(data, watched, baselines, updating));
        harness.Run("No two states of the observer HUD share a picture", () =>
        {
            var rate = $"@{data.Rate.TicksPerSecond}Hz";
            var mine = baselines.Where(pair => pair.Key.EndsWith(rate, StringComparison.Ordinal)).Select(pair => pair.Value).ToArray();
            TestHarness.Assert(mine.Length >= 48 && mine.Distinct(StringComparer.Ordinal).Count() == mine.Length, $"{mine.Length} hashes at this rate, {mine.Distinct(StringComparer.Ordinal).Count()} distinct");
        });
        if (updating) SaveBaselines(baselines);
        harness.Run("A person opens a replay, chooses a view and the research tab, goes to an event and seeks; the view stays", () => PersonWatchesAReplay(data, watched));
        return harness.Failures;
    }

    private static void Keys()
    {
        TestHarness.Assert(SpectatorController.ActionOf(KeyCode.R) == SpectatorAction.TabResearch && SpectatorController.ActionOf(KeyCode.F4) == SpectatorAction.ViewAll
            && SpectatorController.ActionOf(KeyCode.F5) == SpectatorAction.ViewPlayer1 && SpectatorController.ActionOf(KeyCode.F12) == SpectatorAction.ViewPlayer8
            && SpectatorController.ActionOf(KeyCode.T) == SpectatorAction.ToggleTicker && SpectatorController.ActionOf(KeyCode.G) == SpectatorAction.GoToEvent, "the documented keys");
        foreach (var tab in Enum.GetValues<SpectatorTab>())
            TestHarness.Assert(SpectatorController.KeyHint(SpectatorController.ActionOfTab(tab)).Length > 0, $"the tab {tab} has its key");
    }

    // ------------------------------------------------------------------------------------------------- the watched match

    private sealed record Watched(byte[] File, string[] Lines, LiveMatch Match, SpectatorController Spectator);

    private static MatchSetup Setup(GameData data, SimulationRate rate)
    {
        var map = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var options = SkirmishMatchOptions.StandardAt(rate) with { MaxTicks = rate.Time.WholeSecondsToTicks(MatchSeconds) };
        var rush = new MatchAi(SkirmishAiKind.Builtin, CnCNew.Simulation.Scripting.ScriptDifficulty.Normal, AiTactics.Rush);
        return new MatchSetup(map, "FactionAmerica", ["FactionChina"], options) { Seed = Seed, Spectator = true, Ai = [rush, rush] };
    }

    private static (LiveMatch Match, SpectatorController Spectator, MatchRecorder Recorder) Play(GameData data)
    {
        var match = LiveMatch.Create(data, Setup(data, data.Rate), Viewport);
        var recorder = MatchRecorder.Attach(match) ?? throw new InvalidOperationException("the match cannot be recorded");
        var spectator = new SpectatorController(match, new MatchController(match, GameSettings.Default));
        while (!match.IsOver)
        {
            match.Advance(0.25);
            spectator.Update(0.25);
        }
        return (match, spectator, recorder);
    }

    private static Watched LiveTicker(GameData data)
    {
        var (match, spectator, recorder) = Play(data);
        var lines = spectator.Ticker.Lines.Select(line => line.ToString()).ToArray();
        foreach (var line in lines) Console.WriteLine($"       {line}");
        TestHarness.Assert(lines.Length >= 2 && spectator.Ticker.Lines[0].Kind == "first-blood" && spectator.Ticker.Lines.Count(line => line.Kind == "first-blood") == 1,
            $"the ticker begins with the one first blood ({lines.Length} lines)");
        TestHarness.Assert(spectator.Ticker.Lines.Where(line => line.Kind != "defeated").All(line => line.HasPlace), "every line but a defeat has a place");
        TestHarness.Assert(spectator.Ticker.Lines.Select(line => line.Frame).Zip(spectator.Ticker.Lines.Select(line => line.Frame).Skip(1)).All(pair => pair.Second >= pair.First), "the lines in time order");
        TestHarness.Assert(spectator.TickerShown.Count == Math.Min(SpectatorController.TickerRows, lines.Length), "the HUD shows the latest lines");
        var again = Play(data);
        TestHarness.Assert(again.Spectator.Ticker.Lines.Select(line => line.ToString()).SequenceEqual(lines), "a second run of the same match gives the same lines");
        var file = recorder.Build(MatchRecorder.EndingOf(match, leftByChoice: false), RecordedAt);
        return new Watched(file, lines, match, spectator);
    }

    private static void AcrossRates(string contentDir, Watched watched)
    {
        var lines = new Dictionary<int, TickerLine[]>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var data = GameData.Load(contentDir, modelGeometry: false, rate: rate);
            var (_, spectator, _) = Play(data);
            lines[rate.TicksPerSecond] = [.. spectator.Ticker.Lines];
            Console.WriteLine($"       {rate.TicksPerSecond} Hz: {lines[rate.TicksPerSecond].Length} lines, last at frame {(lines[rate.TicksPerSecond].Length > 0 ? lines[rate.TicksPerSecond][^1].Frame : -1)}");
        }
        foreach (var rate in new[] { 30, 120 })
        {
            var a = lines[60];
            var b = lines[rate];
            TestHarness.Assert(a.Length == b.Length, $"{rate} Hz has as many lines as 60 Hz ({b.Length} against {a.Length})");
            for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
            {
                TestHarness.Assert(a[index].Kind == b[index].Kind && a[index].Player == b[index].Player && a[index].Text == b[index].Text, $"{rate} Hz, line {index}: the same event ('{b[index]}' against '{a[index]}')");
                TestHarness.Assert(Math.Abs(a[index].Frame - b[index].Frame) <= 1, $"{rate} Hz, line {index}: within one frame of the original ({b[index].Frame} against {a[index].Frame})");
            }
        }
    }

    private static ReplayPlayback Open(GameData data, Watched watched) => ReplayPlayback.Open(data, watched.File, Viewport, new ReplayPlaybackOptions(30));

    private static SpectatorController Observe(ReplayPlayback playback) => new(playback.Match, new MatchController(playback.Match, GameSettings.Default));

    private static void PlayToEnd(ReplayPlayback playback, SpectatorController spectator)
    {
        for (var frame = 0; frame < 200_000 && !playback.IsOver; frame++)
        {
            playback.Match.Advance(0.25);
            spectator.Update(0.25);
        }
    }

    private static void ReplayTicker(GameData data, Watched watched)
    {
        var playback = Open(data, watched);
        var spectator = Observe(playback);
        TestHarness.Assert(ReferenceEquals(spectator.Ticker, playback.Ticker), "the observer of a playback reads the playback's ticker");
        PlayToEnd(playback, spectator);
        TestHarness.Assert(playback.Ticker.Lines.Select(line => line.ToString()).SequenceEqual(watched.Lines), "the replay's ticker is the ticker of the match");
        // Seeking: the lines up to the frame the playback stands at, whatever was sought before.
        foreach (var seconds in new long[] { 300, 60, 390, 30, 0, 210 })
        {
            var target = data.Rate.Time.WholeSecondsToTicks(seconds);
            var seek = playback.SeekTo(target);
            var frame = playback.Ticker.FrameOf(seek.Tick);
            var shown = playback.Ticker.UpTo(frame).Select(line => line.ToString()).ToArray();
            var expected = watched.Lines.Where((_, at) => playback.Ticker.Lines[at].Frame <= frame).ToArray();
            TestHarness.Assert(seek.HashMatches && shown.SequenceEqual(expected), $"after a seek to {seconds} s: {shown.Length} lines, as the straight playback had them");
        }
        TestHarness.Assert(playback.Ticker.Lines.Count == watched.Lines.Length, "seeking added no line twice");
    }

    private static void ViewsAcrossSeek(GameData data, Watched watched)
    {
        var playback = Open(data, watched);
        var spectator = Observe(playback);
        spectator.Process(new KeyEvent(0, KeyCode.F6, IsDown: true));
        TestHarness.Assert(spectator.FogViewer == playback.Match.Players[1].Id && spectator.FogLabel == "Player 2", "F6 shows the view of player 2");
        spectator.Process(new KeyEvent(0, KeyCode.F12, IsDown: true));
        TestHarness.Assert(spectator.FogViewer == playback.Match.Players[1].Id, "the view of a player the match does not have changes nothing");
        spectator.Process(new KeyEvent(0, KeyCode.F4, IsDown: true));
        TestHarness.Assert(spectator.FogViewer is null, "F4 shows everything");
        var all = spectator.ButtonRect(SpectatorAction.ViewPlayer1, 1280, 720) ?? throw new InvalidOperationException("the HUD has no view button for player 1");
        spectator.Process(new MouseButtonEvent(0, MouseButton.Left, IsDown: true, new Vector2(all.CenterX, all.CenterY)));
        TestHarness.Assert(spectator.FogViewer == playback.Match.Players[0].Id, "the button P1 shows the view of player 1");
        spectator.Process(new KeyEvent(0, KeyCode.R, IsDown: true));
        spectator.Process(new KeyEvent(0, KeyCode.T, IsDown: true));
        TestHarness.Assert(spectator.Tab == SpectatorTab.Research && !spectator.TickerVisible, "R the research tab, T the ticker off");
        playback.SeekTo(data.Rate.Time.WholeSecondsToTicks(120));
        var after = Observe(playback);
        after.TakeViewFrom(spectator);
        TestHarness.Assert(after.FogViewer == playback.Match.Players[0].Id && playback.Match.FogViewer == playback.Match.Players[0].Id, "the view of player 1 is kept across the seek");
        TestHarness.Assert(after.Tab == SpectatorTab.Research && !after.TickerVisible, "the tab and the ticker's switch are kept across the seek");
    }

    private static void GoToEvents(GameData data, Watched watched)
    {
        var playback = Open(data, watched);
        var spectator = Observe(playback);
        PlayToEnd(playback, spectator);
        var latest = spectator.TickerShown.Last(line => line.HasPlace);
        spectator.Process(new KeyEvent(0, KeyCode.G, IsDown: true));
        var pivot = playback.Match.Camera.Pivot;
        TestHarness.Assert(spectator.WentTo == latest && Vector2.Distance(pivot, new Vector2(latest.X, latest.Y)) < 1f && spectator.Followed is null,
            $"G goes to the latest line with a place ({latest}; the camera at {pivot.X:F0},{pivot.Y:F0})");
        var first = spectator.TickerShown.First(line => line.HasPlace);
        var rect = spectator.TickerLineRect(0, 1280, 720) ?? throw new InvalidOperationException("the ticker shows no line with a place");
        spectator.Process(new MouseButtonEvent(0, MouseButton.Left, IsDown: true, new Vector2(rect.CenterX, rect.CenterY)));
        TestHarness.Assert(spectator.WentTo == first && spectator.TickerJumps == 2 && Vector2.Distance(playback.Match.Camera.Pivot, new Vector2(first.X, first.Y)) < 1f,
            $"a click on the first line with a place goes there ({first})");
    }

    private static void Research(Watched watched)
    {
        var panels = watched.Spectator.Panels;
        TestHarness.Assert(panels.Players.Any(player => player.Sciences.Length > 0), $"some player has sciences ({string.Join("; ", panels.Players.Select(player => player.Sciences.Length))})");
        foreach (var player in panels.Players)
        {
            var world = watched.Match.World;
            var count = world.Upgrades.Upgrades.Count(info => !info.IsEngine && info.Scope == CnCNew.Simulation.Templates.UpgradeScope.Player && world.PlayerUpgrades(player.Player).Has(info.Index));
            TestHarness.Assert(player.Upgrades.Length == count, $"player {player.Player.Value}: the panel's upgrades are the world's ({player.Upgrades.Length} against {count})");
            Console.WriteLine($"       player {player.Player.Value}: upgrades {string.Join(", ", player.Upgrades)}; object upgrades {player.ObjectUpgrades}; sciences {string.Join(", ", player.Sciences)}");
        }
        TestHarness.Assert(watched.Spectator.Report().Any(line => line.Contains("research: upgrades", StringComparison.Ordinal)), "the report of a headless run has the research line");
    }

    // ------------------------------------------------------------------------------------------------- the pictures

    private static void Screens(GameData data, Watched watched, Dictionary<string, string> baselines, bool updating)
    {
        var playback = Open(data, watched);
        var spectator = Observe(playback);
        playback.SeekTo(data.Rate.Time.WholeSecondsToTicks(390));
        spectator = new SpectatorController(playback.Match, new MatchController(playback.Match, GameSettings.Default));
        spectator.Update(0.0);
        void Key(KeyCode key) => spectator.Process(new KeyEvent(0, key, IsDown: true));
        Capture(spectator, data, "Observer-Production", baselines, updating);
        foreach (var (key, name) in new[] { (KeyCode.U, "Units"), (KeyCode.L, "Losses"), (KeyCode.E, "Economy"), (KeyCode.R, "Research") })
        {
            Key(key);
            Capture(spectator, data, $"Observer-{name}", baselines, updating);
        }
        Key(KeyCode.F5);
        Capture(spectator, data, "Observer-ViewPlayer1", baselines, updating);
        Key(KeyCode.F4);
        Key(KeyCode.G);
        spectator.Update(0.0);
        Capture(spectator, data, "Observer-AtEvent", baselines, updating);
        Key(KeyCode.T);
        Capture(spectator, data, "Observer-TickerOff", baselines, updating);
    }

    private static void Capture(SpectatorController spectator, GameData data, string name, Dictionary<string, string> baselines, bool updating)
    {
        foreach (var (width, height) in Resolutions)
        {
            var key = $"{name}@{width}x{height}@{data.Rate.TicksPerSecond}Hz";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(spectator.BuildDrawList(width, height).Dump())));
            if (updating) baselines[key] = hash;
            else
            {
                TestHarness.Assert(baselines.TryGetValue(key, out var expected), $"{key}: no baseline recorded (run with CNCNEW_UPDATE_BASELINES=1 once, then review the diff)");
                TestHarness.Assert(expected == hash, $"{key}: draw list hash changed ({expected} -> {hash})");
            }
        }
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
        // The other rates' hashes stay: a run at one rate records only its own.
        var merged = LoadBaselines();
        foreach (var (key, value) in baselines) merged[key] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, string.Concat(merged.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}\n")));
        Console.WriteLine($"Wrote {merged.Count} baseline hash(es) to {BaselinePath}.");
    }

    // ------------------------------------------------------------------------------------------------- a person

    private static void PersonWatchesAReplay(GameData data, Watched watched)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-observer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var shell = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(1280, 720));
        shell.Replays!.Save("Rush against rush", watched.File);
        var lastFive = watched.Lines.Length;
        var script = ShellScript.Parse(
        [
            "wait screen MainMenuScreen",
            "click MainMenu.wnd:ButtonLoadReplay",
            "click MainMenu.wnd:ButtonReplay",
            "wait screen ReplayMenuScreen",
            "click ReplayMenu.wnd:ButtonLoadReplay",
            "limit 120",
            "wait phase InMatch",
            "key F5",
            "wait match view equals 1",
            "key R",
            "wait match tab equals Research",
            "key D8",
            // A frame of 0.25 s plays at most eight original frames: the second line (3:22 of game time) comes after some 760 seconds of the script.
            "limit 1200",
            "wait match ticker atleast 2",
            "limit 120",
            "key G",
            "wait match jumps equals 1",
            "key PageUp",
            "wait match seeks equals 1",
            "wait match view equals 1",
            "wait match tab equals Research",
            "key F4",
            "wait match view equals all",
            "key Escape",
            "limit 20",
            "wait screen ReplayMenuScreen",
            "click ReplayMenu.wnd:ButtonBack",
            "wait screen MainMenuScreen",
        ], "observer-person");
        var host = new ReplayBrowserTests.Host(data, shell, script);
        host.Attach();
        for (var frame = 0; frame < 40_000 && !script.IsFinished; frame++) host.Frame(0.25);
        foreach (var line in script.Report().TakeLast(4)) Console.WriteLine($"       {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script runs through ({script.StepsDone}/{script.StepCount}; the match has {lastFive} ticker lines)");
        Directory.Delete(directory, recursive: true);
    }
}
