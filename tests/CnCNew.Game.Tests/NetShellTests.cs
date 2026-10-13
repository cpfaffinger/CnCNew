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
        private NetMatchLoad? _load;

        public void Frame(double seconds)
        {
            Script.Advance(Shell, seconds);
            if (Match is { } match)
            {
                match.Advance(seconds);
                var relay = match.Remote!;
                if (!match.IsOver && relay.Client.End is null && relay.Client.IsOpen) return;
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
}
