using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Lockstep;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Ui.Layout;

namespace CnCNew.Game.Tests;

/// <summary>
/// The multiplayer screens with a person at them (docs/GAME-SHELL.md sections 15 and 16): two shells in one process - one hosts, one joins - are walked by
/// <see cref="ShellScript"/> from the main menu through the address screen, the question about the server's key, the list of rooms, the room and the shared load
/// screen into a match, to the score screen and back into the room. Every step waits for what a person would see and acts through input events; nothing calls a
/// screen. The sockets are the loopback's, the port is the system's, every step has a time limit, and every file the visit writes lies in a directory of the run.
/// </summary>
internal static class NetShellTests
{
    private const int Width = 1280, Height = 720;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(NetShellTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP NetShellTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Host game and join: main menu, address, the server's key, the rooms, the room, the load screen, a match, the score and back into the room", () => HostAndJoin(data));
        harness.Run("In the match: the connection line, a chat line to all and one to the allies, a pause that names who asked - through the HUD of both games", () => InMatch(data));
        // --endurance-minutes N (scripts/check-net-endurance.ps1): everything of this phase that has a time limit, for at least N minutes of one hosted server.
        var minutesAt = Array.IndexOf(args, "--endurance-minutes");
        if (minutesAt >= 0 && minutesAt + 1 < args.Length && int.TryParse(args[minutesAt + 1], out var minutes) && minutes > 0)
            harness.Run($"Endurance: one hosted server for {minutes} minutes - a guest comes, waits in front of the rooms, enters, plays, returns and leaves, again and again", () => Endurance(data, minutes));
        return harness.Failures;
    }

    /// <summary>One game at the menus: a shell, the script that is the person, and what the client's runtime does for a match of a server.</summary>
    private sealed class Player(GameData data, string name, string directory, int port, IEnumerable<string> script, int matchSeconds = 12) : IDisposable
    {
        public GameShell Shell { get; } = GameShell.Create(data, new FixedShellEnvironment(directory), viewport: new UiViewport(Width, Height),
            network: new ShellNetwork(data.Fingerprint, Path.Combine(directory, "network"), port) { MatchSeconds = matchSeconds, HostOptions = new HostedServerOptions(Address: "127.0.0.1") });
        public ShellScript Script { get; } = ShellScript.Parse(script, name);
        public string Name => name;
        public LiveMatch? Match { get; private set; }
        public ulong LastHash { get; private set; }
        public long LastTick { get; private set; }
        public string? EndNote { get; private set; }
        public int Matches { get; private set; }
        public List<(long Tick, ulong Hash, string? Note)> Played { get; } = [];
        /// <summary>The HUD of the running match, as the client attaches it: its indicators read the connection, its message list takes their notices.</summary>
        public CnCNew.Game.Hud.MatchController? Hud { get; private set; }
        /// <summary>Called every frame of a running match, after the HUD's update: the test is the person in the match.</summary>
        public Action<Player>? InMatch { get; set; }
        public bool WithHud { get; init; }
        private NetMatchLoad? _load;

        public void Key(CnCNew.Presentation.Input.KeyCode key) => Hud!.Process(new CnCNew.Presentation.Input.KeyEvent(++_inputClock, key, IsDown: true));
        public void Type(string text) { foreach (var character in text) Hud!.Process(new CnCNew.Presentation.Input.TextInputEvent(++_inputClock, character)); }
        private long _inputClock;

        public void Frame(double seconds)
        {
            Script.Advance(Shell, seconds);
            if (Match is { } match)
            {
                match.Advance(seconds);
                if (Hud is { } hud)
                {
                    hud.Update(seconds);
                    _ = hud.BuildDrawList(Width, Height);
                    InMatch?.Invoke(this);
                }
                var relay = match.Remote!;
                if (!match.IsOver && relay.Client.End is null && relay.Client.IsOpen) return;
                Hud = null;
                Played.Add((match.World.Tick, match.Session.StateHash(), NetText.MatchEndNote(relay, match.IsOver)));
                LastHash = match.Session.StateHash();
                LastTick = match.World.Tick;
                EndNote = NetText.MatchEndNote(relay, match.IsOver);
                relay.Close();
                relay.Dispose();
                Match = null;
                Matches++;
                Shell.MatchFinished(MatchResult.From(match.Players, match.Winner, match.World, match.EndTick, null), EndNote);
                return;
            }
            Shell.Update(seconds);
            _ = Shell.BuildDrawList();
            foreach (var output in Shell.DrainOutputs())
                if (output is StartNetMatchOutput start) _load = new NetMatchLoad(data, start.Session.Relay!, new Vector2(Width, Height));
            if (_load is not { } load) return;
            try
            {
                if (!load.Step()) return;
                Match = load.Match;
                if (WithHud) Hud = new CnCNew.Game.Hud.MatchController(load.Match!, CnCNew.Ui.Settings.GameSettings.Default);
                Shell.NetMatchStarted();
            }
            catch (RemoteRelayException exception)
            {
                Shell.NetMatchFailed(exception.Message);
            }
            _load = null;
        }

        public void Dispose()
        {
            Match?.Remote?.Dispose();
            Shell.Net?.Dispose();
        }
    }

    private static int FreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static string RunDirectory(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-net-shell-tests-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Frames of both games until every script is through or one failed; a hard limit in real time on top of the steps' own.</summary>
    private static void Play(int limitSeconds, params Player[] players)
    {
        var watch = Stopwatch.StartNew();
        var last = watch.Elapsed.TotalSeconds;
        while (players.Any(player => !player.Script.IsFinished) && !players.Any(player => player.Script.Failed))
        {
            if (watch.Elapsed.TotalSeconds > limitSeconds) break;
            var now = watch.Elapsed.TotalSeconds;
            var seconds = Math.Min(0.1, now - last);
            last = now;
            foreach (var player in players) player.Frame(seconds);
            Thread.Sleep(4);
        }
        foreach (var player in players)
        {
            if (player.Script.IsFinished && !player.Script.Failed) continue;
            foreach (var line in player.Script.Report().TakeLast(6)) Console.WriteLine($"  [{player.Name}] {line}");
            if (player.Shell.Net?.Lobby is { } lobby)
                Console.WriteLine($"  [{player.Name}] lobby {lobby.Stage}, room {lobby.Room?.Phase}, members [{string.Join(", ", lobby.Room?.Members.Select(member => $"{member.Name}@{member.Slot}{(member.Ready ? " ready" : string.Empty)}{(member.IsHost ? " host" : string.Empty)}") ?? [])}], last refusal {lobby.LastRefusal}, failure {lobby.Failure}");
        }
        TestHarness.Assert(players.All(player => player.Script.IsFinished && !player.Script.Failed),
            $"every script runs through ({string.Join(", ", players.Select(player => $"{player.Name}: {player.Script.StepsDone}/{player.Script.StepCount}{(player.Script.Failed ? " FAILED" : string.Empty)}"))})");
    }

    private static void HostAndJoin(GameData data)
    {
        var port = FreePort();
        string[] toAddress =
        [
            "wait screen MainMenuScreen",
            "expect enabled MainMenu.wnd:ButtonMultiplayer",
            "click MainMenu.wnd:ButtonMultiplayer",
            "expect disabled MainMenu.wnd:ButtonOnline",
            "click MainMenu.wnd:ButtonNetwork",
            "wait screen NetworkDirectConnectScreen",
        ];
        string[] matchAndBack =
        [
            "wait screen NetLoadingScreen",
            "expect text MultiplayerLoadScreen.wnd:StaticTextPlayer0 equals Alice",
            "expect text MultiplayerLoadScreen.wnd:StaticTextPlayer1 equals Bob",
            "expect hidden MultiplayerLoadScreen.wnd:StaticTextPlayer2",
            "limit 60",
            "wait phase InMatch",
            "wait phase Results",
            "wait screen ScoreScreenScreen",
            "click ScoreScreen.wnd:ButtonOk",
            "limit 20",
            "wait screen NetRoomScreen",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice|Bob",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Alice|Bob",
            "wait seconds 2",
        ];
        using var host = new Player(data, "host", RunDirectory("host"), port,
        [
            .. toAddress,
            "type NetworkDirectConnect.wnd:EditPlayerName Alice",
            "click NetworkDirectConnect.wnd:ButtonHost",
            "wait screen NetLobbyScreen",
            "wait text LanLobbyMenu.wnd:StaticTextGameInfo contains No room is open",
            "expect disabled LanLobbyMenu.wnd:ButtonJoin",
            "click LanLobbyMenu.wnd:ButtonHost",
            "wait screen NetHostPopupScreen",
            "expect text PopupHostGame.wnd:TextEntryGameName equals Alice's game",
            "type PopupHostGame.wnd:TextEntryGameName Friday night",
            "click PopupHostGame.wnd:ButtonCreateGame",
            "wait screen NetRoomScreen",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice",
            "expect text LanGameOptionsMenu.wnd:ButtonStart equals PLAY GAME",
            "expect hidden LanGameOptionsMenu.wnd:ButtonAccept0",
            "expect hidden LanGameOptionsMenu.wnd:ButtonAccept1",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob",
            "wait list LanGameOptionsMenu.wnd:ListboxChatWindowLanGame contains Bob: hello Alice",
            "type LanGameOptionsMenu.wnd:TextEntryChat hello Bob",
            "enter LanGameOptionsMenu.wnd:TextEntryChat",
            // A start before the guest accepted is refused, with the server's reason in the chat list.
            "click LanGameOptionsMenu.wnd:ButtonStart",
            "wait list LanGameOptionsMenu.wnd:ListboxChatWindowLanGame contains The room is not ready to start",
            "wait shown LanGameOptionsMenu.wnd:ButtonAccept1",
            "click LanGameOptionsMenu.wnd:ButtonStart",
            .. matchAndBack,
        ]);
        using var guest = new Player(data, "guest", RunDirectory("guest"), port,
        [
            .. toAddress,
            "type NetworkDirectConnect.wnd:EditPlayerName Bob",
            $"type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:{port}",
            // The host needs a moment to be there; an address nobody answers at is a message, and the person tries again.
            "wait seconds 3",
            "click NetworkDirectConnect.wnd:ButtonJoin",
            "wait screen MessageBoxScreen",
            "expect text MessageBox.wnd:StaticTextTitle equals New server",
            "expect text MessageBox.wnd:StaticTextMessage contains You have not met the server",
            "click MessageBox.wnd:ButtonYes",
            "wait screen NetLobbyScreen",
            "pick LanLobbyMenu.wnd:ListboxGames Friday night",
            "wait text LanLobbyMenu.wnd:StaticTextGameInfo contains 1 of 2 seats taken",
            "click LanLobbyMenu.wnd:ButtonJoin",
            "wait screen NetRoomScreen",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob",
            "expect text LanGameOptionsMenu.wnd:ButtonStart equals ACCEPT",
            "expect disabled LanGameOptionsMenu.wnd:ComboBoxPlayer0",
            "expect disabled LanGameOptionsMenu.wnd:ButtonSelectMap",
            "type LanGameOptionsMenu.wnd:TextEntryChat hello Alice",
            "enter LanGameOptionsMenu.wnd:TextEntryChat",
            "wait list LanGameOptionsMenu.wnd:ListboxChatWindowLanGame contains Alice: hello Bob",
            "click LanGameOptionsMenu.wnd:ButtonStart",
            "wait shown LanGameOptionsMenu.wnd:ButtonAccept1",
            .. matchAndBack,
        ]);
        Play(150, host, guest);
        TestHarness.Assert(host.Matches == 1 && guest.Matches == 1 && host.LastTick == guest.LastTick && host.LastTick > 0 && host.LastHash == guest.LastHash,
            $"both games played the same match to the same state (tick {host.LastTick}/{guest.LastTick}, hash {host.LastHash:X16}/{guest.LastHash:X16})");
        TestHarness.Assert(host.EndNote is null && guest.EndNote is null, $"the match ended by its own rules ({host.EndNote}; {guest.EndNote})");
        TestHarness.Assert(host.Shell.Net is { Lobby.Stage: LobbyStage.InRoom } && guest.Shell.Net is { Lobby.Stage: LobbyStage.InRoom }, "both are back in the room");
        TestHarness.Assert(host.Shell.Stack.All(screen => screen.Tree.UnregisteredCallbacks.Count == 0) && guest.Shell.Stack.All(screen => screen.Tree.UnregisteredCallbacks.Count == 0),
            "every callback of the multiplayer layouts is wired");
    }

    private static readonly string[] ToAddress =
    [
        "wait screen MainMenuScreen", "click MainMenu.wnd:ButtonMultiplayer", "click MainMenu.wnd:ButtonNetwork", "wait screen NetworkDirectConnectScreen",
    ];

    private static string[] HostToRoom(string room) =>
    [
        .. ToAddress,
        "type NetworkDirectConnect.wnd:EditPlayerName Alice", "click NetworkDirectConnect.wnd:ButtonHost", "wait screen NetLobbyScreen", "wait enabled LanLobbyMenu.wnd:ButtonHost",
        "click LanLobbyMenu.wnd:ButtonHost", "wait screen NetHostPopupScreen", $"type PopupHostGame.wnd:TextEntryGameName {room}", "click PopupHostGame.wnd:ButtonCreateGame",
        "wait screen NetRoomScreen", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice",
    ];

    private static void InMatch(GameData data)
    {
        var port = FreePort();
        string[] match = ["limit 90", "wait screen NetLoadingScreen", "wait phase InMatch", "wait phase Results", "wait screen ScoreScreenScreen"];
        using var host = new Player(data, "host", RunDirectory("host"), port,
            [.. HostToRoom("Indicators"), "limit 60", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob", "wait shown LanGameOptionsMenu.wnd:ButtonAccept1", "click LanGameOptionsMenu.wnd:ButtonStart", .. match], 25) { WithHud = true };
        using var guest = new Player(data, "guest", RunDirectory("guest"), port,
        [
            .. ToAddress,
            "type NetworkDirectConnect.wnd:EditPlayerName Bob", $"type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:{port}", "wait seconds 3", "click NetworkDirectConnect.wnd:ButtonJoin",
            "wait screen MessageBoxScreen", "click MessageBox.wnd:ButtonYes", "wait screen NetLobbyScreen", "pick LanLobbyMenu.wnd:ListboxGames Indicators", "click LanLobbyMenu.wnd:ButtonJoin",
            "wait screen NetRoomScreen", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob", "click LanGameOptionsMenu.wnd:ButtonStart", .. match,
        ], 25) { WithHud = true };

        // The test is the two people in the match: each step waits for what the other's screen shows.
        var step = 0;
        var seen = new List<string>();
        string Lines(Player player) => string.Join(" | ", player.Hud!.Messages.Log.Lines.Select(line => line.Text));
        bool Says(Player player, string text) => player.Hud!.Messages.Log.Lines.Any(line => line.Text == text);
        void Person(Player player)
        {
            if (host.Hud is null || guest.Hud is null || host.Match is null || host.Match.World.Tick < 120) return;
            switch (step)
            {
                case 0 when player == host:
                    seen.Add($"connection lines: '{host.Hud.Net!.View.Connection}' / '{guest.Hud.Net!.View.Connection}'");
                    TestHarness.Assert(host.Hud.Net.View.Connection.StartsWith("Ping ", StringComparison.Ordinal) && guest.Hud.Net.View.Connection.StartsWith("Ping ", StringComparison.Ordinal)
                                       && host.Hud.Net.View is { Waiting.Length: 0, Banner: null, Watching: false }, $"both show their connection and nothing to wait for ({seen[^1]})");
                    host.Key(CnCNew.Presentation.Input.KeyCode.Enter);
                    host.Type("gl hf");
                    host.Key(CnCNew.Presentation.Input.KeyCode.Enter);
                    step = 1;
                    break;
                case 1 when Says(guest, "Alice: gl hf") && Says(host, "Alice: gl hf"):
                    // Two seats in no team: a line to the allies reaches its sender only.
                    guest.Key(CnCNew.Presentation.Input.KeyCode.Backspace);
                    guest.Type("only me");
                    guest.Key(CnCNew.Presentation.Input.KeyCode.Enter);
                    step = 2;
                    break;
                case 2 when Says(guest, "Bob (to allies): only me"):
                    host.Match.Paused = true;
                    step = 3;
                    break;
                case 3 when guest.Hud.Net!.View.Banner == "Paused by Alice" && host.Hud.Net!.View.Banner == "Paused by you":
                    seen.Add($"host's list: {Lines(host)}");
                    TestHarness.Assert(!Says(host, "Bob (to allies): only me"), $"the line to the allies did not reach the other team ({Lines(host)})");
                    host.Match.Paused = false;
                    step = 4;
                    break;
                case 4 when guest.Hud.Net!.View.Banner is null && host.Hud.Net!.View.Banner is null:
                    step = 5;
                    break;
            }
        }
        host.InMatch = Person;
        guest.InMatch = Person;
        Play(150, host, guest);
        TestHarness.Assert(step == 5, $"the two people got through chat and pause (step {step}; {string.Join("; ", seen)})");
        TestHarness.Assert(host.Played.Count == 1 && guest.Played.Count == 1 && host.Played[0] == guest.Played[0] && host.Played[0].Note is null,
            $"and the match was the same on both to its end ({host.Played.FirstOrDefault()} / {guest.Played.FirstOrDefault()})");
    }

    private static void Endurance(GameData data, int minutes)
    {
        var port = FreePort();
        const int matchSeconds = 15, longBrowseSeconds = 330;
        // One cycle is about 45 s of real time; the script holds more cycles than fit, and the run ends at a cycle's end once the time is up.
        var cycles = Math.Max(3, minutes * 60 / 30);
        var hostScript = new List<string>(HostToRoom("Endurance")) { "limit 900" };
        var guestScript = new List<string>(ToAddress) { "limit 900", "type NetworkDirectConnect.wnd:EditPlayerName Bob", $"type NetworkDirectConnect.wnd:ComboboxRemoteIP 127.0.0.1:{port}", "wait seconds 3" };
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            hostScript.AddRange(
            [
                "wait text LanGameOptionsMenu.wnd:ButtonStart equals PLAY GAME", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice",
                "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob", "wait shown LanGameOptionsMenu.wnd:ButtonAccept1", "click LanGameOptionsMenu.wnd:ButtonStart",
                "wait screen NetLoadingScreen", "wait phase InMatch", "wait phase Results", "wait screen ScoreScreenScreen", "click ScoreScreen.wnd:ButtonOk", "wait screen NetRoomScreen",
                // The guest comes back later, sits, and leaves again: the seat is open before the next cycle begins.
                "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Open", $"note cycle {cycle + 1} done",
            ]);
            guestScript.Add("click NetworkDirectConnect.wnd:ButtonJoin");
            if (cycle == 0) guestScript.AddRange(["wait screen MessageBoxScreen", "expect text MessageBox.wnd:StaticTextTitle equals New server", "click MessageBox.wnd:ButtonYes"]);
            guestScript.AddRange(
            [
                "wait screen NetLobbyScreen", "wait list LanLobbyMenu.wnd:ListboxGames contains Endurance",
                // Once the guest stands in front of the rooms for longer than a server lets a connection browse: the lobby must be there again by itself.
                $"wait seconds {(cycle == 1 ? longBrowseSeconds : 2 + cycle % 5)}",
                "pick LanLobbyMenu.wnd:ListboxGames Endurance", "click LanLobbyMenu.wnd:ButtonJoin", "wait screen NetRoomScreen", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob",
                "click LanGameOptionsMenu.wnd:ButtonStart", "wait screen NetLoadingScreen", "wait phase InMatch", "wait phase Results", "wait screen ScoreScreenScreen",
                // The host is back first and opens the room again; the guest follows, sits, and leaves the server.
                "wait seconds 5", "click ScoreScreen.wnd:ButtonOk", "wait screen NetRoomScreen", "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice",
                "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer1 shows Bob", "click LanGameOptionsMenu.wnd:ButtonBack", "wait screen NetLobbyScreen", "click LanLobbyMenu.wnd:ButtonBack",
                "wait screen NetworkDirectConnectScreen",
            ]);
        }
        using var host = new Player(data, "host", RunDirectory("host"), port, hostScript, matchSeconds);
        using var guest = new Player(data, "guest", RunDirectory("guest"), port, guestScript, matchSeconds);
        var events = new Dictionary<string, int>(StringComparer.Ordinal);
        var watch = Stopwatch.StartNew();
        var last = watch.Elapsed.TotalSeconds;
        var nextReport = 300.0;
        var memoryAtStart = GC.GetTotalMemory(true);
        var done = 0;
        while (!host.Script.Failed && !guest.Script.Failed && !host.Script.IsFinished && !guest.Script.IsFinished)
        {
            var now = watch.Elapsed.TotalSeconds;
            var seconds = Math.Min(0.1, now - last);
            last = now;
            host.Frame(seconds);
            guest.Frame(seconds);
            if (host.Shell.Net?.Host is HostedServer server)
                while (server.TryTakeLine(out var line))
                {
                    var name = line.Split(' ')[0];
                    if (name == "event=left") name += line.Contains("reason=Left", StringComparison.Ordinal) ? " (said goodbye)" : line.Contains("reason=Closed", StringComparison.Ordinal) ? " (closed)" : " (other)";
                    if (name == "event=turned-away") name += " " + line.Split(' ').FirstOrDefault(part => part.StartsWith("reason=", StringComparison.Ordinal));
                    events[name] = events.GetValueOrDefault(name) + 1;
                }
            done = host.Script.Log.Count(line => line.Contains("note: cycle ", StringComparison.Ordinal));
            if (now >= nextReport)
            {
                nextReport += 300;
                Console.WriteLine($"  endurance: {now / 60:F0} min, {done} cycles, {guest.Matches} matches, managed memory {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            }
            // The time is up and the cycle is through: the guest stands on the address screen, the host alone in its room.
            if (now >= minutes * 60 && guest.Shell.CurrentScreen is NetworkDirectConnectScreen && guest.Match is null && host.Match is null && done == guest.Matches) break;
            if (now > minutes * 60 + 1_200) break;
            Thread.Sleep(4);
        }
        var uptime = watch.Elapsed.TotalMinutes;
        foreach (var player in new[] { host, guest })
            if (player.Script.Failed) foreach (var line in player.Script.Report().TakeLast(3)) Console.WriteLine($"  [{player.Name}] {line}");
        Console.WriteLine($"  endurance: server up {uptime:F1} min; {done} cycles; matches host {host.Matches} guest {guest.Matches}; server events: {string.Join(", ", events.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key[6..]}={pair.Value}"))}; managed memory {memoryAtStart / 1024 / 1024} -> {GC.GetTotalMemory(true) / 1024 / 1024} MB");
        TestHarness.Assert(!host.Script.Failed && !guest.Script.Failed, "no step of either script failed");
        TestHarness.Assert(uptime >= minutes, $"the hosted server was up for {uptime:F1} of {minutes} minutes");
        TestHarness.Assert(done >= 3 && host.Matches == done && guest.Matches == done, $"every cycle was a match on both ({done} cycles, {host.Matches} / {guest.Matches} matches)");
        TestHarness.Assert(host.Played.SequenceEqual(guest.Played) && host.Played.All(match => match.Tick == matchSeconds * data.Rate.TicksPerSecond && match.Note is null),
            "every match ended by its rules, at the same tick and in the same state on both");
        TestHarness.Assert(events.GetValueOrDefault("event=desync") == 0 && events.GetValueOrDefault("event=match-ended") == done && events.GetValueOrDefault("event=room-opened") == done + 1,
            $"the server ended {events.GetValueOrDefault("event=match-ended")} matches in {events.GetValueOrDefault("event=room-opened")} rooms and reported no desync");
        TestHarness.Assert(done < 2 || events.Keys.Any(name => name.StartsWith("event=turned-away", StringComparison.Ordinal)), "the long wait in front of the rooms was longer than the server lets a connection browse, and the lobby came back by itself");
    }
}
