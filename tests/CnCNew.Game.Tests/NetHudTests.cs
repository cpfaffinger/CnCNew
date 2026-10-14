using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Shell;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using LayoutScaleMode = CnCNew.Ui.Layout.UiScaleMode;

namespace CnCNew.Game.Tests;

/// <summary>
/// The indicators of a match of a server (docs/GAME-HUD.md section 21) from a scripted source: every state they can show is put together, its words are read, and -
/// with game data, for the font and the style of the message list - drawn in six resolutions and pinned by a hash in <c>baselines/net-hud.txt</c> (checksums, no
/// picture). Every reason a server has to drop a player is a line of the message list with its own words and its own hash.
/// </summary>
internal static class NetHudTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/net-hud.txt";

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(NetHudTests));
        harness.Run("The connection line: round trip, playback delay, catching up, and a quality by fixed limits", ConnectionLine);
        harness.Run("Waiting for players: who is behind, by how much, and when the server drops it; a held seat counts down between two messages", Waiting);
        harness.Run("Pause with its requester, half speed, a changed speed", Banners);
        harness.Run("What happens once is a notice: every drop reason in its own words, a return, a seat given up, a desync with the package's place", Notices);
        harness.Run("The chat line: Enter to all, Backspace to the allies, the keys are letters while it is open, Escape drops it", Chat);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP NetHudTests pictures (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var baselines = LoadBaselines();
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("Every state of the indicators and every drop reason has a picture", () => Pictures(data, baselines, updating));
        harness.Run("No two states share a picture", () => TestHarness.Assert(baselines.Count >= 6 * 15 && baselines.Values.Distinct(StringComparer.Ordinal).Count() == baselines.Count,
            $"{baselines.Count} hashes, {baselines.Values.Distinct(StringComparer.Ordinal).Count()} distinct"));
        if (updating) SaveBaselines(baselines);
        return harness.Failures;
    }

    private sealed class Source : INetMatchSource
    {
        public bool Watching { get; set; }
        public uint OwnConnection { get; set; } = 7;
        public int RoundTripMs { get; set; } = 42;
        public int DelayMs { get; set; } = 18;
        public int RateAdjustPerMille { get; set; }
        public Dictionary<uint, LagStateMessage> LagStates { get; } = [];
        public IReadOnlyDictionary<uint, LagStateMessage> Lag => LagStates;
        public List<PlayerDroppedMessage> DroppedList { get; } = [];
        public IReadOnlyList<PlayerDroppedMessage> Dropped => DroppedList;
        /// <summary>Told like the protocol client is told: a held seat stands in the table, an ended hold leaves it, and every change is an event.</summary>
        public SeatSetter Seats => new(this);
        public Dictionary<int, SeatStateMessage> Held { get; } = [];
        public IReadOnlyDictionary<int, SeatStateMessage> HeldSeats => Held;
        public List<SeatStateMessage> SeatChanges { get; } = [];
        public IReadOnlyList<SeatStateMessage> SeatEvents => SeatChanges;

        public sealed class SeatSetter(Source source)
        {
            public SeatStateMessage this[int player]
            {
                set
                {
                    source.SeatChanges.Add(value);
                    if (value.State == SeatHold.Held) source.Held[player] = value;
                    else source.Held.Remove(player);
                }
            }
        }
        public bool Paused { get; set; }
        public uint PausedBy { get; set; }
        public int SpeedPercent { get; set; } = 100;
        public DesyncMessage? Desync { get; set; }
        public List<ChatMessage> ChatLines { get; } = [];
        public IReadOnlyList<ChatMessage> Chat => ChatLines;
        public long NowMs { get; set; } = 3 * 60 * 60 * 1000;
        public List<(string Text, bool Allies)> Sent { get; } = [];
        public string? NameOf(uint connectionId) => connectionId switch { 7 => "Alice", 8 => "Bob", 9 => "Carol", _ => null };
        public string? NameOfPlayer(int player) => player switch { 1 => "Alice", 2 => "Bob", 3 => "Carol", _ => null };
        public void SendChat(string text, bool alliesOnly) => Sent.Add((text, alliesOnly));
    }

    private static (NetHud Hud, Source Source, List<string> Notices) New()
    {
        var source = new Source();
        var notices = new List<string>();
        return (new NetHud(source, notices.Add), source, notices);
    }

    private static void ConnectionLine()
    {
        var (hud, source, _) = New();
        source.RoundTripMs = -1;
        hud.Update();
        TestHarness.Assert(hud.View is { Connection: "Connecting ...", Quality: NetQuality.Unknown }, "before the clock has samples the line says so");
        source.RoundTripMs = 42;
        hud.Update();
        TestHarness.Assert(hud.View is { Connection: "Ping 42 ms, buffer 18 ms", Quality: NetQuality.Good, Waiting.Length: 0, Banner: null, Watching: false, ChatEntry: null }, $"a good line ({hud.View.Connection})");
        source.RoundTripMs = NetHud.FairRoundTripMs;
        hud.Update();
        TestHarness.Assert(hud.View.Quality == NetQuality.Fair, "fair from 120 ms");
        source.RoundTripMs = NetHud.PoorRoundTripMs;
        source.RateAdjustPerMille = 50;
        hud.Update();
        TestHarness.Assert(hud.View is { Quality: NetQuality.Poor } && hud.View.Connection.EndsWith(", catching up", StringComparison.Ordinal), $"poor from 250 ms; a game that runs faster to get to its place says so ({hud.View.Connection})");
        source.RateAdjustPerMille = -50;
        hud.Update();
        TestHarness.Assert(hud.View.Connection.EndsWith(", slowing down", StringComparison.Ordinal), "and one that runs slower");
        source.RoundTripMs = 20;
        source.RateAdjustPerMille = 0;
        source.LagStates[7] = new LagStateMessage(7, 800, false);
        hud.Update();
        TestHarness.Assert(hud.View.Quality == NetQuality.Poor && hud.View.Waiting[0].Text == "This game is 0.8 s behind", $"a game the server waits for is poor whatever its ping ({hud.View.Waiting[0].Text})");
        source.Watching = true;
        hud.Update();
        TestHarness.Assert(hud.View.Watching && hud.StatusLine().Contains("watching yes", StringComparison.Ordinal), "a game that only watches says so, on the screen and in the status line");
    }

    private static void Waiting()
    {
        var (hud, source, _) = New();
        source.LagStates[8] = new LagStateMessage(8, 1_200, false);
        hud.Update();
        TestHarness.Assert(hud.View.Waiting.Select(row => row.Text).SequenceEqual(["Bob is 1.2 s behind"]) && hud.View.Banner is null, "behind, the game not slowed: the name and how far");
        source.LagStates[8] = new LagStateMessage(8, 2_600, true, 7_400);
        hud.Update();
        TestHarness.Assert(hud.View.Waiting[0].Text == "Bob is 2.6 s behind - dropped in 8 s" && hud.View.Banner == "The game runs at half speed until everybody has caught up",
            $"slowed for it: the deadline the server named, rounded up, and why the game is slow ({hud.View.Waiting[0].Text})");
        // Between two messages of the server the deadline runs on this game's clock.
        source.NowMs += 3_000;
        hud.Update();
        TestHarness.Assert(hud.View.Waiting[0].Text == "Bob is 2.6 s behind - dropped in 5 s", $"three seconds later ({hud.View.Waiting[0].Text})");
        source.NowMs += 60_000;
        hud.Update();
        TestHarness.Assert(hud.View.Waiting[0].Text.EndsWith("dropped in 0 s", StringComparison.Ordinal), "and never below zero");
        source.LagStates[8] = new LagStateMessage(8, 0, false);
        hud.Update();
        TestHarness.Assert(hud.View.Waiting.Length == 0 && hud.View.Banner is null, "caught up: nothing is shown");

        // A lost connection whose seat the server holds.
        source.Seats[2] = new SeatStateMessage(2, SeatHold.Held, 300_000);
        source.LagStates[9] = new LagStateMessage(9, 900, false);
        hud.Update();
        TestHarness.Assert(hud.View.Waiting.Select(row => row.Text).SequenceEqual(["Carol is 0.9 s behind", "Bob lost the connection - the seat is held for 300 s"]),
            $"who is behind first, then whose seat is held ({string.Join(" | ", hud.View.Waiting.Select(row => row.Text))})");
        source.NowMs += 45_500;
        hud.Update();
        TestHarness.Assert(hud.View.Waiting[1].Text.EndsWith("held for 255 s", StringComparison.Ordinal), $"the hold counts down ({hud.View.Waiting[1].Text})");
    }

    private static void Banners()
    {
        var (hud, source, _) = New();
        source.Paused = true;
        source.PausedBy = 8;
        hud.Update();
        TestHarness.Assert(hud.View.Banner == "Paused by Bob", "a pause names who asked for it");
        source.PausedBy = 7;
        hud.Update();
        TestHarness.Assert(hud.View.Banner == "Paused by you", "one's own pause");
        source.PausedBy = 0;
        hud.Update();
        TestHarness.Assert(hud.View.Banner == "Paused", "a pause nobody is named for");
        source.Paused = false;
        source.SpeedPercent = 150;
        hud.Update();
        TestHarness.Assert(hud.View.Banner == "Game speed 150 %", "a speed that is not the normal one");
        source.LagStates[8] = new LagStateMessage(8, 3_000, true, 5_000);
        hud.Update();
        TestHarness.Assert(hud.View.Banner!.StartsWith("The game runs at half speed", StringComparison.Ordinal), "the slowing comes before the speed");
    }

    private static void Notices()
    {
        var (hud, source, notices) = New();
        foreach (var reason in Enum.GetValues<DropReason>()) source.DroppedList.Add(new PlayerDroppedMessage(2, 8, reason));
        hud.Update();
        TestHarness.Assert(notices.SequenceEqual(Enum.GetValues<DropReason>().Select(reason => $"Bob {NetText.Drop(reason)}.")) && notices.Distinct().Count() == notices.Count,
            $"every drop reason is a line with the player's name and its own words ({string.Join(" | ", notices)})");
        hud.Update();
        TestHarness.Assert(notices.Count == Enum.GetValues<DropReason>().Length, "a drop is told once");
        notices.Clear();
        // Somebody the room never named, and a watcher (no player): the line still says who.
        source.DroppedList.Add(new PlayerDroppedMessage(0, 9, DropReason.Left));
        source.DroppedList.Add(new PlayerDroppedMessage(6, 44, DropReason.ConnectionLost));
        hud.Update();
        TestHarness.Assert(notices.SequenceEqual(["Carol left the match.", "Player 6 lost the connection."]), $"a watcher by its name, an unknown seat by its number ({string.Join(" | ", notices)})");
        notices.Clear();
        source.Seats[2] = new SeatStateMessage(2, SeatHold.Held, 60_000);
        hud.Update();
        TestHarness.Assert(notices.Count == 0, "a held seat stands in the panel, not in the list");
        source.Seats[2] = new SeatStateMessage(2, SeatHold.Returned, 0);
        hud.Update();
        source.Seats[3] = new SeatStateMessage(3, SeatHold.GivenUp, 0);
        hud.Update();
        TestHarness.Assert(notices.SequenceEqual(["Bob is back.", "Carol did not come back in time."]) && hud.View.Waiting.Length == 0, $"the two ways a hold ends ({string.Join(" | ", notices)})");
        notices.Clear();
        source.Desync = new DesyncMessage(4_711, []);
        hud.DiagnosticsDirectory = "C:/run/diagnostics";
        hud.Update();
        hud.Update();
        TestHarness.Assert(notices.SequenceEqual(["The games are out of sync since tick 4711. A diagnostics package is written to C:/run/diagnostics"]), $"a desync, once, with where the package goes ({string.Join(" | ", notices)})");
        source.ChatLines.Add(new ChatMessage(8, "gl hf"));
        source.ChatLines.Add(new ChatMessage(9, "north", true));
        notices.Clear();
        hud.Update();
        TestHarness.Assert(notices.SequenceEqual(["Bob: gl hf", "Carol (to allies): north"]), $"chat lines with their names; a line to the allies says so ({string.Join(" | ", notices)})");
    }

    private static long _clock;
    private static KeyEvent Down(KeyCode key) => new(++_clock, key, IsDown: true);

    private static void Chat()
    {
        var (hud, source, _) = New();
        TestHarness.Assert(!hud.HandleInput(Down(KeyCode.A), false) && !hud.HandleInput(new TextInputEvent(++_clock, 'a'), false) && !hud.ChatOpen, "while no line is open the keys are the game's");
        TestHarness.Assert(!hud.HandleInput(Down(KeyCode.Enter), menuOpen: true) && !hud.ChatOpen, "the in-game menu keeps its keys");
        TestHarness.Assert(hud.HandleInput(Down(KeyCode.Enter), false) && hud.ChatOpen && !hud.ChatToAllies, "Enter opens a line to all");
        foreach (var character in "hello") hud.HandleInput(new TextInputEvent(++_clock, character), false);
        TestHarness.Assert(hud.HandleInput(Down(KeyCode.A), false) && hud.HandleInput(Down(KeyCode.Space), false), "while a line is open a key is a letter, not a hotkey: it is consumed");
        hud.HandleInput(new TextInputEvent(++_clock, '!'), false);
        hud.HandleInput(Down(KeyCode.Backspace), false);
        hud.Update();
        TestHarness.Assert(hud.View.ChatEntry == "To all: hello_", $"the line shows what is typed ({hud.View.ChatEntry})");
        hud.HandleInput(Down(KeyCode.Enter), false);
        TestHarness.Assert(!hud.ChatOpen && source.Sent.SequenceEqual([("hello", false)]) && hud.ChatSent == 1, "Enter sends it to all and closes the line");
        TestHarness.Assert(hud.HandleInput(Down(KeyCode.Backspace), false) && hud.ChatOpen && hud.ChatToAllies, "Backspace opens a line to the allies");
        foreach (var character in " north ") hud.HandleInput(new TextInputEvent(++_clock, character), false);
        hud.Update();
        TestHarness.Assert(hud.View.ChatEntry == "To allies:  north _", "and says whom it is for");
        hud.HandleInput(Down(KeyCode.Enter), false);
        TestHarness.Assert(source.Sent[^1] == ("north", true), "it is sent trimmed, to the allies");
        hud.HandleInput(Down(KeyCode.Enter), false);
        hud.HandleInput(new TextInputEvent(++_clock, 'x'), false);
        hud.HandleInput(Down(KeyCode.Escape), false);
        hud.HandleInput(Down(KeyCode.Enter), false);
        hud.HandleInput(Down(KeyCode.Enter), false);
        TestHarness.Assert(!hud.ChatOpen && source.Sent.Count == 2, "Escape drops a line; an empty line is not sent");
    }

    private static void Pictures(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var style = InGameUiStyle.FromGraph(data.Graph);
        var options = UiScaleOptions.Original with { Mode = LayoutScaleMode.Uniform };
        var measure = new MonospaceTextMeasure();
        void Capture(string name, Action<Source, NetHud> arrange)
        {
            var source = new Source();
            var log = new UiMessageLog(style.Settings);
            var hud = new NetHud(source, line => log.Add(line, 0));
            arrange(source, hud);
            hud.Update();
            foreach (var (width, height) in Resolutions)
            {
                var commands = hud.Build(width, height, measure).AddRange(MissionTextOverlay.Messages(log, style, options, measure, width, height));
                var list = new UiDrawList(commands, new UiDrawStatistics(0, 0, 0, 0, ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal)));
                var key = $"{name}@{width}x{height}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(list.Dump())));
                if (updating) baselines[key] = hash;
                else
                {
                    TestHarness.Assert(baselines.TryGetValue(key, out var expected), $"{key}: no baseline recorded (run with CNCNEW_UPDATE_BASELINES=1 once, then review the diff)");
                    TestHarness.Assert(expected == hash, $"{key}: draw list hash changed ({expected} -> {hash})");
                }
            }
        }
        Capture("NetHud-Good", (_, _) => { });
        Capture("NetHud-Connecting", (source, _) => source.RoundTripMs = -1);
        Capture("NetHud-Fair", (source, _) => source.RoundTripMs = 150);
        Capture("NetHud-Poor-CatchingUp", (source, _) => { source.RoundTripMs = 300; source.RateAdjustPerMille = 50; });
        Capture("NetHud-Waiting", (source, _) => source.LagStates[8] = new LagStateMessage(8, 1_200, false));
        Capture("NetHud-Waiting-Deadline", (source, _) => { source.LagStates[8] = new LagStateMessage(8, 2_600, true, 7_400); source.LagStates[9] = new LagStateMessage(9, 700, false); });
        Capture("NetHud-SeatHeld", (source, _) => source.Seats[2] = new SeatStateMessage(2, SeatHold.Held, 300_000));
        Capture("NetHud-PausedBy", (source, _) => { source.Paused = true; source.PausedBy = 8; });
        Capture("NetHud-Speed", (source, _) => source.SpeedPercent = 150);
        Capture("NetHud-Watching", (source, _) => source.Watching = true);
        Capture("NetHud-ChatToAll", (_, hud) => { hud.HandleInput(Down(KeyCode.Enter), false); foreach (var character in "hello there") hud.HandleInput(new TextInputEvent(++_clock, character), false); });
        Capture("NetHud-ChatToAllies", (_, hud) => { hud.HandleInput(Down(KeyCode.Backspace), false); foreach (var character in "north") hud.HandleInput(new TextInputEvent(++_clock, character), false); });
        Capture("NetHud-ChatLines", (source, _) => { source.ChatLines.Add(new ChatMessage(8, "gl hf")); source.ChatLines.Add(new ChatMessage(9, "north", true)); });
        Capture("NetHud-Desync", (source, hud) => { source.Desync = new DesyncMessage(4_711, []); hud.DiagnosticsDirectory = "diagnostics"; });
        Capture("NetHud-Returned", (source, hud) => { source.Seats[2] = new SeatStateMessage(2, SeatHold.Held, 60_000); hud.Update(); source.Seats[2] = new SeatStateMessage(2, SeatHold.Returned, 0); });
        Capture("NetHud-GivenUp", (source, hud) => { source.Seats[2] = new SeatStateMessage(2, SeatHold.Held, 60_000); hud.Update(); source.Seats[2] = new SeatStateMessage(2, SeatHold.GivenUp, 0); });
        foreach (var reason in Enum.GetValues<DropReason>())
            Capture($"NetHud-Drop-{reason}", (source, _) => source.DroppedList.Add(new PlayerDroppedMessage(2, 8, reason)));
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
        File.WriteAllText(BaselinePath, string.Concat(baselines.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}\n")));
        Console.WriteLine($"Wrote {baselines.Count} baseline hash(es) to {BaselinePath}.");
    }
}
