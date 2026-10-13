using System.Collections.Immutable;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Lockstep;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Layout;

namespace CnCNew.Game.Tests;

/// <summary>
/// Every picture the multiplayer screens can show (docs/GAME-SHELL.md section 15), without a socket: the shell is given a scripted network whose lobby is put into
/// each state a server can put a client in - connecting, the question about a new key, the rooms, a room as its host and as a member, the load screen, and every
/// reason a server, a room or a connection has to say no. Each state is drawn in six resolutions and its draw list pinned by a hash in
/// <c>baselines/net-screens.txt</c> (set <c>CNCNEW_UPDATE_BASELINES=1</c> to record; the file holds checksums, no picture). The words are checked too: every value of
/// every reason of the protocol has a text of its own, and the screen shows exactly that text.
/// </summary>
internal static class NetScreenTests
{
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];
    private const int NavWidth = 1280, NavHeight = 720;
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/net-screens.txt";
    private const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string OtherFingerprint = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(NetScreenTests));
        harness.Run("Every reason of the protocol has words of its own: reject, drop, end, close, failure", EveryReasonHasText);
        harness.Run("An address is an address, a name or either with a port - and nothing else", Addresses);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP NetScreenTests screens (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var baselines = LoadBaselines();
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        harness.Run("The way to a room: submenu, address, connecting, the new server's key, the rooms, the popups", () => WayToRoom(data, baselines, updating));
        harness.Run("A room as its host and as a member, the map picker, the shared load screen", () => RoomAndLoading(data, baselines, updating));
        harness.Run("Every way a server, a room or a connection says no has a screen with its text", () => Refusals(data, baselines, updating));
        harness.Run("A match that did not end by its own rules says why over the score screen", () => MatchEnds(data, baselines, updating));
        harness.Run("With window transitions the same way is walked by a script: every group the screens ask for exists, and nothing is clicked while one plays", () => AnimatedWalk(data));
        // A picture that shows a reason shows that reason: no two states of these screens draw the same list in any resolution.
        harness.Run("No two states share a picture", () => TestHarness.Assert(baselines.Count >= 336 && baselines.Values.Distinct(StringComparer.Ordinal).Count() == baselines.Count,
            $"{baselines.Count} hashes, {baselines.Values.Distinct(StringComparer.Ordinal).Count()} distinct"));
        if (updating) SaveBaselines(baselines);
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------- the words

    private static void EveryReasonHasText()
    {
        var texts = new List<string>();
        foreach (var reason in Enum.GetValues<RejectReason>()) texts.Add(NetText.Reject(reason));
        foreach (var reason in Enum.GetValues<DropReason>()) texts.Add(NetText.Drop(reason));
        foreach (var reason in Enum.GetValues<EndReason>()) texts.Add(NetText.End(reason));
        foreach (var reason in Enum.GetValues<UdpCloseReason>()) texts.Add(NetText.Close(reason));
        var server = new IPEndPoint(IPAddress.Parse("203.0.113.5"), 27_960);
        foreach (var kind in Enum.GetValues<LobbyFailureKind>())
        {
            var (title, message) = NetText.Failure(new LobbyFailure(kind, RejectReason.RoomFull, UdpCloseReason.Timeout, string.Empty, Fingerprint, OtherFingerprint), server);
            TestHarness.Assert(title.Length > 0 && message.Length > 10, $"{kind} has a title and words");
        }
        TestHarness.Assert(texts.All(text => text.Length > 10), "every reason has words, not a blank or a number");
        TestHarness.Assert(texts.Distinct(StringComparer.Ordinal).Count() == texts.Count, "no two reasons share their words");
        TestHarness.Throws<ArgumentOutOfRangeException>(() => NetText.Reject((RejectReason)999), "a reject reason without a text is an error");
        TestHarness.Throws<ArgumentOutOfRangeException>(() => NetText.Drop((DropReason)99), "a drop reason without a text is an error");
        TestHarness.Throws<ArgumentOutOfRangeException>(() => NetText.End((EndReason)99), "an end reason without a text is an error");
        TestHarness.Throws<ArgumentOutOfRangeException>(() => NetText.Close((UdpCloseReason)99), "a close reason without a text is an error");
        var changed = NetText.Failure(new LobbyFailure(LobbyFailureKind.KeyChanged, Shown: Fingerprint, Expected: OtherFingerprint), server).Message;
        TestHarness.Assert(changed.Contains("0123 4567 89AB CDEF", StringComparison.Ordinal) && changed.Contains("FEDC BA98 7654 3210", StringComparison.Ordinal), "a changed key shows both fingerprints, in groups of four");
        TestHarness.Assert(NetText.NewServer(server, Fingerprint).Contains("0123 4567", StringComparison.Ordinal) && NetText.NewServer(server, Fingerprint).Contains("203.0.113.5:27960", StringComparison.Ordinal),
            "a new server is shown with its address and its key");
    }

    private static void Addresses()
    {
        TestHarness.Assert(ShellNetwork.ParseAddress("203.0.113.5", 27_960) is { Port: 27_960 } plain && plain.Address.ToString() == "203.0.113.5", "an address without a port takes the default port");
        TestHarness.Assert(ShellNetwork.ParseAddress(" 203.0.113.5:4000 ", 27_960) is { Port: 4_000 }, "an address with a port");
        TestHarness.Assert(ShellNetwork.ParseAddress("[2001:db8::1]:4000", 27_960) is { Port: 4_000, AddressFamily: System.Net.Sockets.AddressFamily.InterNetworkV6 }, "an IPv6 address with a port");
        TestHarness.Assert(ShellNetwork.ParseAddress("localhost:4000", 27_960) is { Port: 4_000 }, "a name with a port");
        foreach (var bad in new[] { "", "   ", "203.0.113.5:port", "203.0.113.5:70000", "not an address", "a:b:c" })
            TestHarness.Assert(ShellNetwork.ParseAddress(bad, 27_960) is null, $"'{bad}' is not an address");
    }

    // ------------------------------------------------------------------------------------------------- the scripted network

    private sealed class ScriptedLobby : INetLobby
    {
        public List<string> Calls { get; } = [];
        public LobbyStage Stage { get; set; }
        public IPEndPoint? Server { get; set; }
        public string ServerFingerprint { get; set; } = Fingerprint;
        public LobbyFailure? Failure { get; set; }
        public RoomListMessage? Rooms { get; set; }
        public bool ListingRefused { get; set; }
        public string RoomName { get; set; } = string.Empty;
        public RoomStateMessage? Room { get; set; }
        public uint ConnectionId { get; set; }
        public bool IsHost { get; set; }
        public List<ChatMessage> ChatLines { get; } = [];
        public IReadOnlyList<ChatMessage> Chat => ChatLines;
        public IReadOnlyDictionary<uint, byte> LoadProgress { get; } = new Dictionary<uint, byte>();
        public RejectMessage? LastRefusal { get; set; }

        public void Connect(IPEndPoint server, string serverKey = "") { Calls.Add($"Connect {server} {serverKey}"); Server = server; Failure = null; Stage = LobbyStage.Connecting; }
        public void AcceptServer() { Calls.Add("AcceptServer"); Stage = LobbyStage.Browsing; }
        public void RefreshRooms() => Calls.Add("RefreshRooms");
        public void Enter(string room, string password = "", bool watch = false) { Calls.Add($"Enter '{room}' '{password}'"); RoomName = room; Stage = LobbyStage.Entering; }
        public void Configure(RoomSettings settings) => Calls.Add($"Configure {string.Join(",", settings.Slots.Select(slot => slot.Kind))} {settings.StartMoney} {settings.MapPath}");
        public void RequestSlot(int slot, string faction, int team, int color, int startPosition) => Calls.Add($"RequestSlot {slot} '{faction}' {team} {color} {startPosition}");
        public void LeaveSlot() => Calls.Add("LeaveSlot");
        public void SetReady(bool ready) => Calls.Add($"SetReady {ready}");
        public void RequestStart() => Calls.Add("RequestStart");
        public void SendChat(string text) => Calls.Add($"SendChat {text}");
        public void Kick(uint connectionId) => Calls.Add($"Kick {connectionId}");
        public void LeaveRoom() { Calls.Add("LeaveRoom"); Failure = null; Room = null; Stage = LobbyStage.Browsing; }
        public void Disconnect() { Calls.Add("Disconnect"); Stage = LobbyStage.Idle; }
        public void Pump() { }
        public RemoteRelay? TakeRelay() { Calls.Add("TakeRelay"); Stage = LobbyStage.Idle; return null; }
        public void ReturnToRoom() { Calls.Add("ReturnToRoom"); Stage = LobbyStage.Entering; }
    }

    private sealed class ScriptedHost : IShellHost
    {
        public IPEndPoint LocalEndPoint { get; } = new(IPAddress.Any, 27_960);
        public string Fingerprint => NetScreenTests.Fingerprint;
        public bool IsRunning => true;
        public void Dispose() { }
    }

    private sealed class ScriptedNetwork : IShellNetwork
    {
        public ScriptedLobby Lobby { get; private set; } = new();
        public List<string> Remembered { get; } = [];
        public int DefaultPort => 27_960;
        public string LocalAddresses => "192.0.2.10";
        public string PlayerName => string.Empty;
        public IReadOnlyList<string> RecentServers { get; } = ["203.0.113.5:27960", "198.51.100.7"];
        public int MatchSeconds => 0;
        public void Remember(string playerName, string? server) => Remembered.Add($"{playerName}|{server}");
        public IPEndPoint? Resolve(string text) => ShellNetwork.ParseAddress(text, DefaultPort);
        public INetLobby CreateLobby(string playerName) => Lobby = new ScriptedLobby();
        public IShellHost Host() => new ScriptedHost();
        public string? TerrainFingerprint(GameData data, string mapPath) => "TERRAIN-OF-" + Path.GetFileNameWithoutExtension(mapPath);
    }

    private static (GameShell Shell, ScriptedNetwork Network) NewShell(GameData data)
    {
        var network = new ScriptedNetwork();
        var environment = new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-net-screen-tests-" + Guid.NewGuid().ToString("N")));
        return (GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight), network: network), network);
    }

    /// <summary>Main menu, Multiplayer, Network, Join with the remembered address: the shell stands on the server's front with a lobby that is connecting.</summary>
    private static ScriptedLobby GoToLobby(GameShell shell, ScriptedNetwork network)
    {
        if (shell.CurrentScreen is MainMenuScreen)
        {
            Click(shell, "MainMenu.wnd:ButtonMultiplayer");
            Click(shell, "MainMenu.wnd:ButtonNetwork");
        }
        TestHarness.Assert(shell.CurrentScreen is NetworkDirectConnectScreen, $"the address screen is the top ({shell.CurrentScreen.GetType().Name})");
        Click(shell, "NetworkDirectConnect.wnd:ButtonJoin");
        TestHarness.Assert(shell.CurrentScreen is NetLobbyScreen, $"Join opens the server's front ({shell.CurrentScreen.GetType().Name})");
        return network.Lobby;
    }

    private static RoomSettings FourSeats(GameShell shell, out string mapPath)
    {
        mapPath = shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer && entry.StartPositionCount >= 4).Select(entry => entry.VirtualPath).Order(StringComparer.Ordinal).First();
        var factions = shell.Data.Factions.Where(p => p.Faction.PlayableSide).Select(p => p.Faction.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new RoomSettings(shell.Data.Rate.TicksPerSecond, mapPath, "TERRAIN", 1_000, 10_000, 0, 0,
        [
            new MatchSlotDescription(MatchSlotKind.Human, factions[0], 1, 0, 0),
            new MatchSlotDescription(MatchSlotKind.Human, factions[1], 1, 1, 1),
            new MatchSlotDescription(MatchSlotKind.Human, CnCNew.Game.Live.MatchSetup.RandomFaction, 0, 2, 2),
            new MatchSlotDescription(MatchSlotKind.Computer, factions[2], 2, 3, 3),
        ]);
    }

    private static readonly ImmutableArray<RoomMember> TwoMembers = [new RoomMember(7, "Alice", 0, false, true), new RoomMember(8, "Bob", 1, true, false), new RoomMember(9, "Carol", -1, false, false)];

    // ------------------------------------------------------------------------------------------------- the walks

    private static void WayToRoom(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var (shell, network) = NewShell(data);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonMultiplayer") is { IsEffectivelyEnabled: true }, "with a network the Multiplayer button can be pressed");
        Click(shell, "MainMenu.wnd:ButtonMultiplayer");
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonNetwork") is { IsVisible: true } && shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonOnline") is { IsVisible: true, IsEffectivelyEnabled: false },
            "the submenu shows Network, and Online inert");
        Capture(shell, "Net-MainMenu-Multiplayer", baselines, updating);
        Key(shell, KeyCode.Escape);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonNetwork") is { IsVisible: false }, "Escape leaves the submenu");
        Click(shell, "MainMenu.wnd:ButtonMultiplayer");
        Click(shell, "MainMenu.wnd:ButtonNetwork");
        TestHarness.Assert(shell.CurrentScreen is NetworkDirectConnectScreen, "Network opens the address screen");
        var tree = shell.CurrentScreen.Tree;
        TestHarness.Assert(tree.FindByName("NetworkDirectConnect.wnd:StaticLocalIP")!.Text!.Value == "192.0.2.10" && tree.FindByName("NetworkDirectConnect.wnd:ComboboxRemoteIP")!.ComboBox!.Edit.Text == "203.0.113.5:27960",
            "it shows this machine's address and the server typed last");
        Capture(shell, "Net-DirectConnect", baselines, updating);

        // An address that is none, and a name that is empty, are messages - nothing is connected.
        tree.FindByName("NetworkDirectConnect.wnd:EditPlayerName")!.SetEntryText("   ");
        Click(shell, "NetworkDirectConnect.wnd:ButtonJoin");
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Message: "Enter a player name first." }, "a blank name is asked for");
        Capture(shell, "Net-DirectConnect-NoName", baselines, updating);
        Click(shell, "MessageBox.wnd:ButtonOk");
        tree.FindByName("NetworkDirectConnect.wnd:EditPlayerName")!.SetEntryText("Alice");

        var lobby = GoToLobby(shell, network);
        TestHarness.Assert(lobby.Calls.SequenceEqual(["Connect 203.0.113.5:27960 "]) && network.Remembered.SequenceEqual(["Alice|203.0.113.5:27960"]), $"Join connects to the address and remembers name and address ({string.Join("; ", lobby.Calls)})");
        Step(shell);
        Capture(shell, "Net-Lobby-Connecting", baselines, updating);

        // ServerTrust.New: a question with the key; No ends the visit, Yes goes on.
        lobby.Stage = LobbyStage.TrustQuestion;
        Step(shell);
        TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Title: NetText.TitleNewServer, Buttons: ShellMessageBoxButtons.YesNo } question && question.Message == NetText.NewServer(lobby.Server!, Fingerprint),
            "a server never met is a question with its address and key");
        Capture(shell, "Net-Trust-NewServer", baselines, updating);
        Click(shell, "MessageBox.wnd:ButtonNo");
        TestHarness.Assert(shell.CurrentScreen is NetworkDirectConnectScreen && shell.Net is null && lobby.Calls.Contains("Disconnect") && !lobby.Calls.Contains("AcceptServer"), "No ends the visit without a word to the server");
        lobby = GoToLobby(shell, network);
        lobby.Stage = LobbyStage.TrustQuestion;
        Step(shell);
        Click(shell, "MessageBox.wnd:ButtonYes");
        TestHarness.Assert(shell.CurrentScreen is NetLobbyScreen && lobby.Calls.Contains("AcceptServer"), "Yes accepts the server");

        // The rooms.
        lobby.Rooms = new RoomListMessage(0, 16, []);
        Step(shell);
        Capture(shell, "Net-Lobby-NoRooms", baselines, updating);
        var settings = FourSeats(shell, out var mapPath);
        // The pictures do not depend on the rate this test runs at: the rooms of the hashed list run at this game's rate (what a room at another rate says is checked below).
        var ownRate = shell.Data.Rate.TicksPerSecond;
        RoomListMessage ThreeRooms(int tournamentRate) => new(3, 16,
        [
            new ListedRoom("Friday night", RoomPhase.Room, 2, 2, 4, false, mapPath, ownRate, BuildIdentity.Current.Id),
            new ListedRoom("Locked", RoomPhase.Room, 1, 0, 0, true, string.Empty, 0, "another-build"),
            new ListedRoom("Tournament", RoomPhase.Match, 8, 6, 8, true, "Maps/Unknown Map/Unknown Map.map", tournamentRate, "another-build"),
        ]);
        lobby.Rooms = ThreeRooms(ownRate);
        Step(shell);
        var games = shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:ListboxGames")!;
        TestHarness.Assert(games.ListBox!.Rows.Count == 3 && games.ListBox.Rows[1].Cells[0].StartsWith("[locked] Locked", StringComparison.Ordinal) && games.ListBox.Rows[2].Cells[0].Contains("match running", StringComparison.Ordinal),
            "the list shows the three rooms, the locked one marked, the running one named so");
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:ButtonJoin") is { IsEffectivelyEnabled: false }, "nothing chosen: Join cannot be pressed");
        Capture(shell, "Net-Lobby-Rooms", baselines, updating);
        ClickRow(shell, "LanLobbyMenu.wnd:ListboxGames", 2);
        var info = shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:StaticTextGameInfo")!.Text!.Value;
        TestHarness.Assert(info.Contains("Unknown Map", StringComparison.Ordinal) && info.Contains("Needs a password", StringComparison.Ordinal) && !info.Contains(" Hz", StringComparison.Ordinal)
                           && info.Contains("another build", StringComparison.Ordinal), $"a chosen room says what would keep this game out ({info.Replace('\n', '|')})");
        Capture(shell, "Net-Lobby-RoomChosen", baselines, updating);
        lobby.Rooms = ThreeRooms(ownRate * 2);
        Step(shell);
        info = shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:StaticTextGameInfo")!.Text!.Value;
        TestHarness.Assert(info.Contains($"Runs at {ownRate * 2} Hz - this game runs at {ownRate} Hz", StringComparison.Ordinal), $"a room at another rate says both rates ({info.Replace('\n', '|')})");
        lobby.Rooms = ThreeRooms(ownRate);
        Step(shell);

        // A locked room asks for its password; Enter in the field enters.
        ClickRow(shell, "LanLobbyMenu.wnd:ListboxGames", 1);
        Click(shell, "LanLobbyMenu.wnd:ButtonJoin");
        TestHarness.Assert(shell.CurrentScreen is NetJoinPopupScreen, "a locked room opens the password popup");
        Capture(shell, "Net-JoinPopup", baselines, updating);
        foreach (var character in "sesam") shell.HandleInput(new TextInputEvent(++_clockMs, character));
        Key(shell, KeyCode.Enter);
        TestHarness.Assert(shell.CurrentScreen is NetLobbyScreen && lobby.Calls.Contains("Enter 'Locked' 'sesam'"), $"Enter in the field enters with the password ({lobby.Calls[^1]})");
        Step(shell);
        Capture(shell, "Net-Lobby-Entering", baselines, updating);
        lobby.Stage = LobbyStage.Browsing;
        Step(shell);

        // An open room is entered at once; Host asks for a name.
        ClickRow(shell, "LanLobbyMenu.wnd:ListboxGames", 0);
        Click(shell, "LanLobbyMenu.wnd:ButtonJoin");
        TestHarness.Assert(lobby.Calls[^1] == "Enter 'Friday night' ''", "an open room is entered without a question");
        lobby.Stage = LobbyStage.Browsing;
        Step(shell);
        Click(shell, "LanLobbyMenu.wnd:ButtonHost");
        TestHarness.Assert(shell.CurrentScreen is NetHostPopupScreen, "Host opens the popup for the room's name");
        Capture(shell, "Net-HostPopup", baselines, updating);
        Click(shell, "PopupHostGame.wnd:ButtonCancel");
        TestHarness.Assert(shell.CurrentScreen is NetLobbyScreen, "Cancel closes it");

        // A server that lists nothing.
        lobby.ListingRefused = true;
        lobby.Rooms = null;
        lobby.Stage = LobbyStage.Connecting;
        Step(shell);
        lobby.Stage = LobbyStage.Browsing;
        Step(shell);
        TestHarness.Assert(shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:StaticTextGameInfo")!.Text!.Value.StartsWith("This server does not list its rooms.", StringComparison.Ordinal)
                           && shell.CurrentScreen.Tree.FindByName("LanLobbyMenu.wnd:ButtonHost") is { IsEffectivelyEnabled: true }, "a server without a list says so, and a room is entered by its name");
        Capture(shell, "Net-Lobby-NoList", baselines, updating);
        Click(shell, "LanLobbyMenu.wnd:ButtonBack");
        TestHarness.Assert(shell.CurrentScreen is NetworkDirectConnectScreen && shell.Net is null && lobby.Calls[^1] == "Disconnect", "Back leaves the server");
        _ = settings;
    }

    private static void RoomAndLoading(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var (shell, network) = NewShell(data);
        var lobby = GoToLobby(shell, network);
        var settings = FourSeats(shell, out _);
        lobby.ConnectionId = 7;
        lobby.IsHost = true;
        lobby.RoomName = "Friday night";
        lobby.Room = new RoomStateMessage(RoomPhase.Room, settings, TwoMembers);
        lobby.ChatLines.Add(new ChatMessage(8, "hello Alice"));
        lobby.ChatLines.Add(new ChatMessage(7, "hello Bob"));
        lobby.LastRefusal = new RejectMessage(RejectReason.NotReady, "Not every player is ready.");
        lobby.Stage = LobbyStage.InRoom;
        Step(shell);
        TestHarness.Assert(shell.CurrentScreen is NetRoomScreen, "a lobby in a room shows the room");
        Step(shell);
        var tree = shell.CurrentScreen.Tree;
        string Shown(string name) => tree.FindByName($"LanGameOptionsMenu.wnd:{name}")!.ComboBox is { SelectedIndex: >= 0 } combo ? combo.Entries[combo.SelectedIndex] : string.Empty;
        bool Enabled(string name) => tree.FindByName($"LanGameOptionsMenu.wnd:{name}") is { IsVisible: true, IsEffectivelyEnabled: true };
        bool Visible(string name) => tree.FindByName($"LanGameOptionsMenu.wnd:{name}") is { IsVisible: true };
        TestHarness.Assert(Shown("ComboBoxPlayer0") == "Alice" && Shown("ComboBoxPlayer1") == "Bob" && Shown("ComboBoxPlayer2") == "Open" && Shown("ComboBoxPlayer3") == "Medium Army" && !Visible("ComboBoxPlayer4"),
            $"four rows: the host, a member, an open seat, a computer army ({Shown("ComboBoxPlayer0")}, {Shown("ComboBoxPlayer1")}, {Shown("ComboBoxPlayer2")}, {Shown("ComboBoxPlayer3")})");
        TestHarness.Assert(!Visible("ButtonAccept0") && Visible("ButtonAccept1") && !Visible("ButtonAccept2"), "the lamp of the member that accepted is lit, the host has none");
        TestHarness.Assert(Enabled("ComboBoxPlayerTemplate0") && !Enabled("ComboBoxPlayerTemplate1") && !Enabled("ComboBoxPlayerTemplate2") && Enabled("ComboBoxPlayerTemplate3"),
            "the host sets its own army and the computer's, nobody else's");
        var chat = tree.FindByName("LanGameOptionsMenu.wnd:ListboxChatWindowLanGame")!.ListBox!.Rows.Select(row => row.Cells[0]).ToArray();
        TestHarness.Assert(chat.SequenceEqual(["Bob: hello Alice", "Alice: hello Bob", "* The room is not ready to start. (Not every player is ready.)"]), $"chat lines carry their names, a refusal the server's reason ({string.Join(" | ", chat)})");
        Capture(shell, "Net-Room-Host", baselines, updating);

        // The host's start closes the seat nobody took, then asks.
        lobby.Calls.Clear();
        Click(shell, "LanGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(lobby.Calls.Count == 2 && lobby.Calls[0].StartsWith("Configure Human,Human,Closed,Computer", StringComparison.Ordinal) && lobby.Calls[1] == "RequestStart", $"Start closes the open seat and asks the server ({string.Join("; ", lobby.Calls)})");

        // The map picker lies over the room and reports to it.
        Click(shell, "LanGameOptionsMenu.wnd:ButtonSelectMap");
        TestHarness.Assert(shell.CurrentScreen is SkirmishMapSelectScreen { LayoutName: ShellLayoutNames.LanMapSelectMenu }, "the host picks a map in the picker of the network layouts");
        Capture(shell, "Net-Room-MapSelect", baselines, updating);
        lobby.Calls.Clear();
        ClickRow(shell, "LanMapSelectMenu.wnd:ListboxMap", 0);
        Click(shell, "LanMapSelectMenu.wnd:ButtonOK");
        TestHarness.Assert(shell.CurrentScreen is NetRoomScreen && lobby.Calls.Count == 1 && lobby.Calls[0].StartsWith("Configure ", StringComparison.Ordinal) && lobby.Calls[0].Contains("Maps/", StringComparison.Ordinal),
            $"a picked map is a request to the server with as many slots as the map has places ({string.Join("; ", lobby.Calls)})");

        // The same room seen by the member that sits in it, and by one that only watches.
        lobby.ConnectionId = 8;
        lobby.IsHost = false;
        lobby.LastRefusal = null;
        lobby.Room = new RoomStateMessage(RoomPhase.Room, settings, TwoMembers);
        Step(shell);
        TestHarness.Assert(!Enabled("ComboBoxPlayer0") && !Enabled("ComboBoxPlayer1") && Enabled("ComboBoxPlayer2") && !Enabled("ComboBoxPlayer3") && Enabled("ComboBoxPlayerTemplate1") && !Enabled("ComboBoxPlayerTemplate3")
                           && !Enabled("ButtonSelectMap") && !Enabled("ComboBoxStartingCash"), "a member sets its own seat, may take the open one, and nothing else");
        TestHarness.Assert(tree.FindByName("LanGameOptionsMenu.wnd:ButtonStart")!.Text is { IsLabel: true, Value: "GUI:Accept" } && Enabled("ButtonStart"), "the member's button is Accept");
        Capture(shell, "Net-Room-Member", baselines, updating);
        lobby.Calls.Clear();
        Click(shell, "LanGameOptionsMenu.wnd:ButtonStart");
        TestHarness.Assert(lobby.Calls.SequenceEqual(["SetReady False"]), $"Accept toggles this member's word ({string.Join("; ", lobby.Calls)})");
        lobby.ConnectionId = 9;
        lobby.Calls.Clear();
        lobby.Room = new RoomStateMessage(RoomPhase.Room, settings, TwoMembers);
        Step(shell);
        TestHarness.Assert(lobby.Calls.Count == 1 && lobby.Calls[0].StartsWith("RequestSlot 2 ", StringComparison.Ordinal), $"a member without a seat asks for the first open one ({string.Join("; ", lobby.Calls)})");
        TestHarness.Assert(tree.FindByName("LanGameOptionsMenu.wnd:ButtonStart") is { IsEffectivelyEnabled: false }, "and has nothing to accept until it sits");
        Capture(shell, "Net-Room-Watcher", baselines, updating);

        // The start: the shared load screen, one row per participant.
        lobby.ConnectionId = 8;
        lobby.Stage = LobbyStage.Starting;
        Step(shell);
        TestHarness.Assert(shell.Phase == ShellPhase.MatchLoading && shell.CurrentScreen is NetLoadingScreen && lobby.Calls.Contains("TakeRelay"), "the start shows the shared load screen and takes the connection");
        var loading = (NetLoadingScreen)shell.CurrentScreen;
        string Row(string name) => loading.Tree.FindByName($"MultiplayerLoadScreen.wnd:{name}") is { IsVisible: true } window ? window.Text?.Value ?? string.Empty : "<hidden>";
        TestHarness.Assert(Row("StaticTextPlayer0") == "Alice" && Row("StaticTextPlayer1") == "Bob" && Row("StaticTextPlayer2") == "Medium Army" && Row("StaticTextPlayer3") == "<hidden>",
            $"three participants: the two players and the computer army; the seat nobody took has no row ({Row("StaticTextPlayer0")}, {Row("StaticTextPlayer1")}, {Row("StaticTextPlayer2")}, {Row("StaticTextPlayer3")})");
        Capture(shell, "Net-Loading-Begin", baselines, updating);
        loading.SetProgress(new Dictionary<uint, byte> { [7] = 60, [8] = 100 });
        TestHarness.Assert(loading.Tree.FindByName("MultiplayerLoadScreen.wnd:ProgressLoad0")!.ProgressPercent == 60 && loading.Tree.FindByName("MultiplayerLoadScreen.wnd:ProgressLoad1")!.ProgressPercent == 100
                           && loading.Tree.FindByName("MultiplayerLoadScreen.wnd:ProgressLoad2")!.ProgressPercent == 100, "every bar shows what that member's game reported; a computer player has nothing to load");
        Capture(shell, "Net-Loading-Progress", baselines, updating);
        Step(shell);
        TestHarness.Assert(shell.DrainOutputs().OfType<StartNetMatchOutput>().Count() == 1, "after the screen was drawn the host of the shell is asked to build the match, once");

        // The match cannot be built here: back in front of the server, with the reason.
        shell.NetMatchFailed("The match is played on 'Maps/X/X.map', which this game does not have.");
        TestHarness.Assert(shell.Phase == ShellPhase.Menus && shell.CurrentScreen is MessageBoxScreen { Title: NetText.TitleCannotEnter } && lobby.Calls.Contains("ReturnToRoom"), "a match that cannot be built is a message, and the lobby goes back to the room");
        Capture(shell, "Net-Loading-Failed", baselines, updating);
        Click(shell, "MessageBox.wnd:ButtonOk");
        TestHarness.Assert(shell.CurrentScreen is NetLobbyScreen, "behind the message stands the server's front");
    }

    private static void Refusals(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var (shell, network) = NewShell(data);
        var lobby = GoToLobby(shell, network);
        void Show(LobbyFailure failure, string name, bool endsVisit)
        {
            if (shell.CurrentScreen is NetworkDirectConnectScreen) lobby = GoToLobby(shell, network);
            lobby.Stage = LobbyStage.Browsing;
            Step(shell);
            lobby.Failure = failure;
            lobby.Stage = LobbyStage.Failed;
            Step(shell);
            var (title, message) = NetText.Failure(failure, lobby.Server!);
            TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen box && box.Title == title && box.Message == message && box.Buttons == ShellMessageBoxButtons.OkOnly, $"{name}: the failure is a message with its own words");
            Capture(shell, name, baselines, updating);
            Click(shell, "MessageBox.wnd:ButtonOk");
            Step(shell);
            TestHarness.Assert(endsVisit ? shell.CurrentScreen is NetworkDirectConnectScreen && shell.Net is null : shell.CurrentScreen is NetLobbyScreen && lobby.Stage == LobbyStage.Browsing,
                $"{name}: OK {(endsVisit ? "ends the visit" : "leads back to the server's rooms")} ({shell.CurrentScreen.GetType().Name})");
        }
        // Every reason a room or a server gives.
        foreach (var reason in Enum.GetValues<RejectReason>())
            Show(new LobbyFailure(LobbyFailureKind.Rejected, reason, Detail: reason == RejectReason.ProtocolVersion ? "This server speaks protocol version 5, the client 4." : string.Empty), $"Net-Reject-{reason}",
                endsVisit: reason is RejectReason.ProtocolVersion or RejectReason.Banned or RejectReason.BadIdentity or RejectReason.ServerFull);
        // Every way a connection is not made or ends. KeyChanged is ServerTrust.Changed: both fingerprints, and nothing was said to the server.
        Show(new LobbyFailure(LobbyFailureKind.NoAnswer), "Net-Failure-NoAnswer", true);
        Show(new LobbyFailure(LobbyFailureKind.ServerFull), "Net-Failure-ServerFull", true);
        Show(new LobbyFailure(LobbyFailureKind.AddressFull), "Net-Failure-AddressFull", true);
        Show(new LobbyFailure(LobbyFailureKind.KeyChanged, Shown: Fingerprint, Expected: OtherFingerprint), "Net-Trust-KeyChanged", true);
        foreach (var reason in Enum.GetValues<UdpCloseReason>())
            Show(new LobbyFailure(LobbyFailureKind.ConnectionClosed, Close: reason), $"Net-Closed-{reason}", true);

        // In a room: the host removes this member (back to the rooms), or the connection ends (the visit is over).
        void InRoom(LobbyFailure failure, string name, bool endsVisit)
        {
            if (shell.CurrentScreen is NetworkDirectConnectScreen) lobby = GoToLobby(shell, network);
            lobby.ConnectionId = 8;
            lobby.Room = new RoomStateMessage(RoomPhase.Room, FourSeats(shell, out _), TwoMembers);
            lobby.Stage = LobbyStage.InRoom;
            Step(shell);
            Step(shell);
            TestHarness.Assert(shell.CurrentScreen is NetRoomScreen, $"{name}: in the room");
            lobby.Failure = failure;
            lobby.Stage = LobbyStage.Failed;
            Step(shell);
            var (title, message) = NetText.Failure(failure, lobby.Server!);
            TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen box && box.Title == title && box.Message == message, $"{name}: the failure is a message over the room");
            Capture(shell, name, baselines, updating);
            Click(shell, "MessageBox.wnd:ButtonOk");
            Step(shell);
            TestHarness.Assert(endsVisit ? shell.CurrentScreen is NetworkDirectConnectScreen && shell.Net is null : shell.CurrentScreen is NetLobbyScreen,
                $"{name}: OK {(endsVisit ? "ends the visit" : "leads back to the server's rooms")} ({shell.CurrentScreen.GetType().Name})");
        }
        InRoom(new LobbyFailure(LobbyFailureKind.Rejected, RejectReason.Kicked, Detail: "The host removed you from the room."), "Net-Room-Kicked", false);
        InRoom(new LobbyFailure(LobbyFailureKind.ConnectionClosed, Close: UdpCloseReason.Timeout), "Net-Room-ConnectionLost", true);

        // Hosting that cannot start is a message on the address screen.
        var failing = GameShell.Create(data, new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-net-screen-tests-" + Guid.NewGuid().ToString("N"))), viewport: new UiViewport(NavWidth, NavHeight),
            network: new FailingHostNetwork());
        Click(failing, "MainMenu.wnd:ButtonMultiplayer");
        Click(failing, "MainMenu.wnd:ButtonNetwork");
        Click(failing, "NetworkDirectConnect.wnd:ButtonHost");
        TestHarness.Assert(failing.CurrentScreen is MessageBoxScreen { Title: NetText.TitleCannotHost } cannot && cannot.Message.StartsWith("This game cannot host:", StringComparison.Ordinal) && failing.Net is null, "a server that cannot start is a message");
        Capture(failing, "Net-Host-CannotHost", baselines, updating);
    }

    private sealed class FailingHostNetwork : IShellNetwork
    {
        public int DefaultPort => 27_960;
        public string LocalAddresses => "192.0.2.10";
        public string PlayerName => "Alice";
        public IReadOnlyList<string> RecentServers { get; } = [];
        public int MatchSeconds => 0;
        public void Remember(string playerName, string? server) { }
        public IPEndPoint? Resolve(string text) => null;
        public INetLobby CreateLobby(string playerName) => throw new InvalidOperationException("No lobby without a server.");
        public IShellHost Host() => throw new RemoteRelayException("This game cannot host: the port 27960 is in use.");
        public string? TerrainFingerprint(GameData data, string mapPath) => null;
    }

    private static void MatchEnds(GameData data, Dictionary<string, string> baselines, bool updating)
    {
        var (shell, network) = NewShell(data);
        var lobby = GoToLobby(shell, network);
        foreach (var reason in Enum.GetValues<EndReason>())
        {
            var note = reason == EndReason.Finished ? null : NetText.End(reason);
            shell.MatchFinished(new MatchResult([], 0), note);
            if (note is null)
            {
                TestHarness.Assert(shell.CurrentScreen is ScoreScreenScreen, "a match that ended by its rules shows the score and nothing over it");
                continue;
            }
            TestHarness.Assert(shell.CurrentScreen is MessageBoxScreen { Title: NetText.TitleMatchEnded } box && box.Message == note && shell.Stack[^2] is ScoreScreenScreen, $"{reason}: its words stand over the score screen");
            Capture(shell, $"Net-End-{reason}", baselines, updating);
            Click(shell, "MessageBox.wnd:ButtonOk");
            TestHarness.Assert(shell.CurrentScreen is ScoreScreenScreen, $"{reason}: OK leaves the score screen");
        }
        // A connection that ended in the middle of a match.
        shell.MatchFinished(new MatchResult([], 0), NetText.Close(UdpCloseReason.Timeout));
        Capture(shell, "Net-End-ConnectionLost", baselines, updating);
        Click(shell, "MessageBox.wnd:ButtonOk");
        // From the score screen of a server's match the way leads back into its room.
        lobby.Calls.Clear();
        Click(shell, "ScoreScreen.wnd:ButtonOk");
        TestHarness.Assert(shell.Phase == ShellPhase.Menus && shell.CurrentScreen is NetLobbyScreen && lobby.Calls.SequenceEqual(["ReturnToRoom"]) && shell.Stack.Count == 3,
            $"OK on the score screen returns to the server's room ({shell.CurrentScreen.GetType().Name}; {string.Join("; ", lobby.Calls)})");
    }

    private static void AnimatedWalk(GameData data)
    {
        var network = new ScriptedNetwork();
        var environment = new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-net-screen-tests-" + Guid.NewGuid().ToString("N")));
        var shell = GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight), animateTransitions: true, network: network);
        // The script is the person; the test is the server: it moves the scripted lobby on when the person has reached the screen that waits for it.
        var script = ShellScript.Parse(
        [
            "wait screen MainMenuScreen",
            "click MainMenu.wnd:ButtonMultiplayer",
            "click MainMenu.wnd:ButtonNetwork",
            "wait screen NetworkDirectConnectScreen",
            "type NetworkDirectConnect.wnd:EditPlayerName Alice",
            "click NetworkDirectConnect.wnd:ButtonJoin",
            "wait screen NetLobbyScreen",
            "wait enabled LanLobbyMenu.wnd:ButtonHost",
            "click LanLobbyMenu.wnd:ButtonHost",
            "wait screen NetHostPopupScreen",
            "click PopupHostGame.wnd:ButtonCreateGame",
            "wait screen NetRoomScreen",
            "wait combo LanGameOptionsMenu.wnd:ComboBoxPlayer0 shows Alice",
            "click LanGameOptionsMenu.wnd:ButtonBack",
            "wait screen NetLobbyScreen",
            "click LanLobbyMenu.wnd:ButtonBack",
            "wait screen NetworkDirectConnectScreen",
            "click NetworkDirectConnect.wnd:ButtonBack",
            "wait screen MainMenuScreen",
            "wait enabled MainMenu.wnd:ButtonMultiplayer",
        ]);
        for (var frame = 0; frame < 30 * 120 && !script.IsFinished; frame++)
        {
            script.Advance(shell, CnCNew.Tests.TestTime.FrameSeconds);
            shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
            _ = shell.BuildDrawList();
            var lobby = network.Lobby;
            if (lobby.Stage == LobbyStage.Connecting) lobby.Stage = LobbyStage.Browsing;
            else if (lobby.Stage == LobbyStage.Entering)
            {
                lobby.ConnectionId = 7;
                lobby.IsHost = true;
                lobby.Room = new RoomStateMessage(RoomPhase.Room, FourSeats(shell, out _), TwoMembers);
                lobby.Stage = LobbyStage.InRoom;
            }
        }
        foreach (var line in script.Report().TakeLast(script.Failed || !script.IsFinished ? 4 : 0)) Console.WriteLine($"  {line}");
        TestHarness.Assert(script.IsFinished && !script.Failed, $"the script walks the animated screens ({script.StepsDone}/{script.StepCount})");
        TestHarness.Assert(network.Lobby.Calls.Contains("Enter 'Alice's game' ''") && network.Lobby.Calls.Contains("LeaveRoom") && network.Lobby.Calls[^1] == "Disconnect", $"and did what a person would have ({string.Join("; ", network.Lobby.Calls)})");
    }

    // ------------------------------------------------------------------------------------------------- helpers

    private static long _clockMs;

    private static void Step(GameShell shell, int frames = 2)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            shell.Update(CnCNew.Tests.TestTime.FrameSeconds);
            _ = shell.BuildDrawList();
        }
    }

    private static void Click(GameShell shell, string decoratedName)
    {
        var window = shell.CurrentScreen.Tree.FindByName(decoratedName) ?? throw new InvalidOperationException($"Window '{decoratedName}' not found on {shell.CurrentScreen.GetType().Name}.");
        TestHarness.Assert(window.IsVisible && window.IsEffectivelyEnabled, $"'{decoratedName}' can be clicked");
        ClickAt(shell, window.Rect.CenterX, window.Rect.CenterY);
    }

    private static void ClickRow(GameShell shell, string decoratedName, int row)
    {
        var window = shell.CurrentScreen.Tree.FindByName(decoratedName) ?? throw new InvalidOperationException($"Window '{decoratedName}' not found on {shell.CurrentScreen.GetType().Name}.");
        var content = CnCNew.Ui.Runtime.UiGadgetGeometry.ListContent(window);
        var height = CnCNew.Ui.Runtime.UiGadgetGeometry.RowHeight(window);
        ClickAt(shell, content.X + 20, content.Y + row * height + height / 2);
        TestHarness.Assert(window.ListBox!.SelectedIndex == row, $"row {row} of '{decoratedName}' is selected ({window.ListBox.SelectedIndex})");
    }

    private static void ClickAt(GameShell shell, int x, int y)
    {
        shell.HandleInput(new MouseMoveEvent(++_clockMs, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, true, new Vector2(x, y)));
        shell.HandleInput(new MouseButtonEvent(++_clockMs, MouseButton.Left, false, new Vector2(x, y)));
    }

    private static void Key(GameShell shell, KeyCode key) => shell.HandleInput(new KeyEvent(++_clockMs, key, IsDown: true));

    private static void Capture(GameShell shell, string name, Dictionary<string, string> baselines, bool updating)
    {
        // The pointer of the last click would hilite whatever lies under it in one resolution and not in the next.
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
}
