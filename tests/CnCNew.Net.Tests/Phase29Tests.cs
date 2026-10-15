using System.Net;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Skirmish;

// Phase 29 (docs/NETWORK-PROTOCOL.md section 22): the list of a server's rooms, and the server as a library a game can host.
static partial class NetTests
{
    public static void RoomList()
    {
        var clock = new ManualNetClock();
        clock.Advance(ClockStartMs);
        var network = new LoopbackNetwork(clock);
        var seed = 1u;
        RoomDirectory Directory(DirectoryOptions options) => new(clock, () => ++seed, options);
        var directory = Directory(new DirectoryOptions(MaxRooms: 4, HelloTimeoutMs: 1_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)));
        var clients = new List<LockstepClient>();
        (RoomBrowser Browser, IConnection Connection) Browse(RoomDirectory to)
        {
            var (client, server) = network.Connect();
            to.Attach(server, "10.0.0.9");
            return (new RoomBrowser(client), client);
        }
        LockstepClient Join(IConnection? on, string room, string password = "")
        {
            if (on is null)
            {
                var (client, server) = network.Connect();
                directory.Attach(server);
                on = client;
            }
            var joined = new LockstepClient(on, $"{room} {clients.Count + 1}", Definitions, Released, room, roomPassword: password);
            clients.Add(joined);
            return joined;
        }
        void Pump(RoomDirectory on, RoomBrowser? browser = null, int rounds = 4, int ms = 0)
        {
            for (var round = 0; round < rounds; round++)
            {
                if (ms > 0) clock.Advance(ms);
                on.Pump();
                foreach (var client in clients) client.Pump();
                browser?.Pump();
            }
        }

        // ---- An empty server lists nothing; a server with rooms lists them by name, with what somebody needs to choose one.
        var (first, firstConnection) = Browse(directory);
        first.Request();
        Pump(directory, first);
        Check.That(first.List is { TotalRooms: 0, MaxRooms: 4, Rooms.Length: 0 } && directory.ListsServed == 1, "an empty server answers with an empty list");
        var host = Join(null, "zulu");
        Pump(directory);
        host.Configure(TwoSeats());
        Pump(directory);
        host.RequestSlot(0, "Alpha", startPosition: 0);
        var locked = Join(null, "alpha", "sesam");
        Join(null, "alpha", "sesam");
        Pump(directory, rounds: 8);
        clock.Advance(1_000);
        first.Request();
        Pump(directory, first);
        var list = first.List!;
        Check.That(list.TotalRooms == 2 && list.Rooms.Select(room => room.Name).SequenceEqual(["alpha", "zulu"]), $"two rooms, in the order of their names ({string.Join(",", list.Rooms.Select(room => room.Name))})");
        var (alpha, zulu) = (list.Rooms[0], list.Rooms[1]);
        Check.That(alpha is { HasPassword: true, Members: 2, Players: 0, Seats: 0, Phase: RoomPhase.Room, MapPath: "" } && locked.Phase == ClientPhase.InRoom,
            $"a locked lobby whose host has set nothing: two members, no seats ({alpha})");
        Check.That(zulu is { HasPassword: false, Members: 1, Players: 1, Seats: 2, Phase: RoomPhase.Room, MapPath: "Maps/Test/Test.map", TickRate: 60 } && zulu.SimulationId == Released.Id,
            $"a room with settings: its map, its rate, its build, one of two seats taken ({zulu})");
        // The list says nothing about who is there: no member's name is in its bytes.
        var bytes = System.Text.Encoding.UTF8.GetString(MessageCodec.Encode(list));
        Check.That(!bytes.Contains("zulu 1", StringComparison.Ordinal) && !bytes.Contains("alpha 2", StringComparison.Ordinal), "a list names rooms, not people");

        // ---- The connection that browsed says its hello on the same connection and is a member like any other.
        var entered = Join(firstConnection, "zulu");
        Pump(directory, rounds: 8);
        Check.That(entered.Phase == ClientPhase.InRoom && !entered.IsHost && host.Room!.Members.Length == 2, "the connection that asked for the list enters a room it chose");

        // ---- Asking too soon is not answered and buys no time; asking for ever is not possible; a silent browser is turned away like a silent connection.
        var (hasty, _) = Browse(directory);
        hasty.Request();
        hasty.Request();
        hasty.Request();
        Pump(directory, hasty);
        Check.That(hasty.Lists == 1 && directory.ListsRefused == 2, $"three requests at once: one list ({hasty.Lists}; {directory.ListsRefused} not answered)");
        Pump(directory, hasty, rounds: 1, ms: 900);
        Check.That(hasty.IsOpen, "the connection stays while its hello time runs");
        Pump(directory, hasty, rounds: 2, ms: 200);
        Check.That(!hasty.IsOpen && directory.TurnedAway >= 1, "and is turned away when it neither asks again nor says hello");
        var bounded = Directory(new DirectoryOptions(MaxRooms: 4, HelloTimeoutMs: 1_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)) { ListsPerConnection = 3, ListIntervalMs = 100 });
        var (greedy, _) = Browse(bounded);
        for (var index = 0; index < 5; index++)
        {
            greedy.Request();
            Pump(bounded, greedy, rounds: 2, ms: 150);
        }
        Check.That(greedy.Lists == 3 && greedy.Rejection is { Reason: RejectReason.InvalidRequest } && !greedy.IsOpen, $"a connection gets as many lists as it may, then it is turned away ({greedy.Lists})");

        // ---- A server that lists nothing says so; a client of another protocol version is told the version.
        var silent = Directory(new DirectoryOptions(MaxRooms: 4, HelloTimeoutMs: 1_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)) { ListRooms = false });
        var (refused, _) = Browse(silent);
        refused.Request();
        Pump(silent, refused);
        Check.That(refused.List is null && refused.Rejection is { Reason: RejectReason.InvalidRequest } && silent.ListsRefused == 1 && silent.ListsServed == 0, "a server that lists nothing refuses with a reason");
        var (old, oldConnection) = Browse(directory);
        var older = (ushort)(Protocol.Version - 1);
        oldConnection.Send(MessageCodec.Encode(new RoomListRequestMessage(older)));
        Pump(directory, old);
        Check.That(old.Rejection is { Reason: RejectReason.ProtocolVersion } rejected && rejected.Text.Contains(Protocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                   && rejected.Text.Contains(older.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), "a request of another protocol version is answered with both versions");

        // ---- More rooms than a list names: the first ones by name, and the count of all.
        var crowded = Directory(new DirectoryOptions(MaxRooms: 100, HelloTimeoutMs: 1_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)));
        for (var index = 0; index < 70; index++)
        {
            var (client, server) = network.Connect();
            crowded.Attach(server);
            clients.Add(new LockstepClient(client, "m", Definitions, Released, $"room {index:D3}"));
        }
        Pump(crowded, rounds: 4);
        var (many, _) = Browse(crowded);
        many.Request();
        Pump(crowded, many);
        Check.That(many.List is { TotalRooms: 70, Rooms.Length: Protocol.MaxListedRooms } && many.List.Rooms[^1].Name == "room 063" && MessageCodec.Encode(many.List).Length < Protocol.MaxMessageBytes,
            $"70 rooms: the list names the first {Protocol.MaxListedRooms} and says there are 70");
    }

    public static void ServerCoreHostsRooms()
    {
        // The server as the library a game hosts (docs/DEDICATED-SERVER.md section 14): a datagram channel, a clock, a key, and one round of work. Two clients find its
        // room by the list and enter it through the sealed transport - the same objects the dedicated server program runs.
        var clock = new ManualNetClock();
        clock.Advance(ClockStartMs);
        var network = new MemoryDatagramNetwork(clock, 5);
        var address = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 27_960);
        using var identity = UdpIdentity.Create();
        var seed = 10u;
        using var core = new ServerCore(network.Open(address), clock, new byte[32], identity, () => ++seed, UdpTests.Unbounded,
            new DirectoryOptions(MaxRooms: 2, HelloTimeoutMs: 2_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)));
        var endpoints = new List<UdpClientEndpoint>();
        UdpClientEndpoint Connect(int index)
        {
            var endpoint = new UdpClientEndpoint(network.Open(new IPEndPoint(IPAddress.Parse($"10.0.1.{index}"), 4_000 + index)), address, clock, (ulong)(77 + index), UdpTests.Unbounded,
                key => UdpCrypto.FingerprintOf(key) == identity.Fingerprint);
            endpoints.Add(endpoint);
            return endpoint;
        }
        var clients = new List<LockstepClient>();
        var browsers = new List<RoomBrowser>();
        void Step(int ms)
        {
            for (var elapsed = 0; elapsed < ms; elapsed++)
            {
                clock.Advance(1);
                foreach (var endpoint in endpoints) endpoint.Pump();
                foreach (var browser in browsers) browser.Pump();
                foreach (var client in clients) client.Pump();
                foreach (var endpoint in endpoints) endpoint.Flush();
                core.Pump();
            }
        }
        var hostEnd = Connect(1);
        var host = new LockstepClient(hostEnd.Connection, "host", Definitions, Released, "hosted");
        clients.Add(host);
        Step(50);
        Check.That(host.Phase == ClientPhase.InRoom && host.IsHost && core.Rooms.RoomCount == 1 && core.TryTakeConnected(out var from) && from.Port == 4_001,
            $"the first client opens a room on the hosted server ({host.Phase}); the host is told who connected");
        var guestEnd = Connect(2);
        var browser = new RoomBrowser(guestEnd.Connection);
        browsers.Add(browser);
        browser.Request();
        Step(50);
        Check.That(browser.List is { TotalRooms: 1 } listed && listed.Rooms[0].Name == "hosted" && listed.Rooms[0].Members == 1, "a second client is shown the room");
        browsers.Clear();
        var guest = new LockstepClient(guestEnd.Connection, "guest", Definitions, Released, "hosted");
        clients.Add(guest);
        Step(50);
        host.Configure(TwoSeats());
        Step(20);
        host.RequestSlot(0, "Alpha", startPosition: 0);
        guest.RequestSlot(1, "Beta", startPosition: 1);
        host.SetReady(true);
        guest.SetReady(true);
        Step(20);
        host.RequestStart();
        Step(20);
        host.ReportLoaded(42);
        guest.ReportLoaded(42);
        Step(200);
        Check.That(host.Phase == ClientPhase.InMatch && guest.Phase == ClientPhase.InMatch && guest.NewestBundleTick >= 5 && core.WaitMicros(2_000) is >= 0 and <= 2_000,
            $"host and guest are in a match the hosted server paces ({host.Phase}, {guest.Phase}, tick {guest.NewestBundleTick})");
        // The host stops: everybody is told at once.
        Check.That(core.CloseAll() == 2, "stopping tells both connections");
        Step(300);
        Check.That(!hostEnd.Connection.IsOpen && !guestEnd.Connection.IsOpen, "and both know before any timeout");
    }

    public static void RoomAndMatchIndicators()
    {
        // What the client's indicators are fed by (docs/NETWORK-PROTOCOL.md section 22.3): a seat that survives the host's changes, a chat line for the allies only,
        // a pause that names who asked, and - for a member the game is slowed for - how long the server will still wait.
        var clock = new ManualNetClock();
        clock.Advance(ClockStartMs);
        var network = new LoopbackNetwork(clock);
        var seed = 1u;
        var server = new RelayServer(clock, new RelayOptions(SeedSource: () => ++seed, LagReportMs: 200, LagThrottleMs: 600, ThrottleLimitMs: 3_000, LagRecoveryMs: 30_000));
        var clients = new List<LockstepClient>();
        LockstepClient Join(string name)
        {
            var (client, relay) = network.Connect();
            server.Attach(relay);
            var joined = new LockstepClient(client, name, Definitions, Released);
            clients.Add(joined);
            return joined;
        }
        var keepsUp = new HashSet<LockstepClient>();
        void Step(int ms)
        {
            for (var elapsed = 0; elapsed < ms; elapsed++)
            {
                clock.Advance(1);
                server.Pump();
                foreach (var client in clients)
                {
                    client.Pump();
                    // A member that plays along: it completes every tick it was sent and says the hash everybody says.
                    if (!keepsUp.Contains(client) || client.Phase != ClientPhase.InMatch) continue;
                    while (client.CompletedTick < client.NewestBundleTick)
                    {
                        var tick = client.CompletedTick + 1;
                        if (client.HasBundle(tick)) client.TakeBundle(tick);
                        client.ReportHash(tick, 99);
                        client.ReportCompleted(tick);
                    }
                }
            }
        }
        var (host, ally, enemy, watcher) = (Join("host"), Join("ally"), Join("enemy"), Join("watcher"));
        Step(20);
        var settings = new RoomSettings(60, "Maps/Test/Test.map", "TERRAIN", 100_000, 500, 60, 0,
            [new MatchSlotDescription(MatchSlotKind.Human, "Alpha", Team: 1, StartPosition: 0), new MatchSlotDescription(MatchSlotKind.Human, "Beta", Team: 1, StartPosition: 1),
             new MatchSlotDescription(MatchSlotKind.Human, "Alpha", Team: 2, StartPosition: 2), new MatchSlotDescription(MatchSlotKind.Human, "Beta", Team: 0, StartPosition: 3)]);
        host.Configure(settings);
        Step(10);
        host.RequestSlot(0, "Alpha", 1, -1, 0);
        ally.RequestSlot(1, "Beta", 1, -1, 1);
        enemy.RequestSlot(2, "Alpha", 2, -1, 2);
        ally.SetReady(true);
        enemy.SetReady(true);
        Step(10);
        Check.That(host.Room!.Members.Count(member => member.Slot >= 0) == 3 && host.Room.Members.Count(member => member.Ready) == 2, "three sit, two said ready");

        // ---- The host changes the money: everybody keeps the seat, nobody stays ready. It closes a seat somebody sits in: that member stands up.
        host.Configure(settings with { StartMoney = 20_000 });
        Step(10);
        Check.That(host.Room!.Settings.StartMoney == 20_000 && host.Room.Members.Count(member => member.Slot >= 0) == 3 && !host.Room.Members.Any(member => member.Ready),
            $"a change of the settings keeps the seats and takes back every 'ready' ({string.Join(", ", host.Room.Members.Select(member => $"{member.Name}@{member.Slot}{(member.Ready ? " ready" : string.Empty)}"))})");
        host.Configure(settings with { Slots = settings.Slots.SetItem(2, settings.Slots[2] with { Kind = MatchSlotKind.Closed }) });
        Step(10);
        Check.That(host.Room!.Members.Single(member => member.Name == "enemy").Slot == -1 && host.Room.Members.Count(member => member.Slot >= 0) == 2, "a seat that is closed is left by who sat in it; the others stay");
        host.Configure(settings);
        Step(10);
        enemy.RequestSlot(2, "Alpha", 2, -1, 2);
        ally.SetReady(true);
        enemy.SetReady(true);
        Step(10);

        // ---- Before the match a line "to the allies" goes to everybody: a room has no teams yet.
        ally.SendChat("in the room", alliesOnly: true);
        Step(10);
        Check.That(clients.All(client => client.Chat.Count == 1 && client.Chat[0] is { Text: "in the room", AlliesOnly: false }), "in the room a line reaches everybody and is marked so");

        // The host closes the seat nobody took - what a start does: nobody's word is taken back by that.
        host.Configure(settings with { Slots = settings.Slots.SetItem(3, settings.Slots[3] with { Kind = MatchSlotKind.Closed }) });
        Step(10);
        Check.That(host.Room!.Settings.Slots[3].Kind == MatchSlotKind.Closed && host.Room.Members.Count(member => member.Ready) == 2, "closing a seat nobody sits in leaves everybody ready");
        host.RequestStart();
        Step(10);
        foreach (var client in clients) client.ReportLoaded(42);
        foreach (var client in clients) keepsUp.Add(client);
        Step(200);
        Check.That(clients.All(client => client.Phase == ClientPhase.InMatch), $"the match runs ({string.Join(", ", clients.Select(client => client.Phase))}; {host.LastRefusal})");

        // ---- In the match: to all, to the allies (the team of the seat), and from a watcher to the watchers.
        host.SendChat("to everybody");
        host.SendChat("to my team", alliesOnly: true);
        enemy.SendChat("alone in my team", alliesOnly: true);
        watcher.SendChat("we who watch", alliesOnly: true);
        Step(50);
        string Heard(LockstepClient client) => string.Join(" | ", client.Chat.Skip(1).Select(line => $"{line.Text}{(line.AlliesOnly ? "*" : string.Empty)}"));
        Check.That(Heard(host) == "to everybody | to my team*" && Heard(ally) == "to everybody | to my team*", $"host and ally hear the team's line ({Heard(host)}; {Heard(ally)})");
        Check.That(Heard(enemy) == "to everybody | alone in my team*" && Heard(watcher) == "to everybody | we who watch*", $"the other team and a watcher hear only their own ({Heard(enemy)}; {Heard(watcher)})");
        Check.That(server.AlliedChatLines == 3, "the server counts the lines it did not send to everybody");

        // ---- A pause names who asked for it.
        ally.RequestPace(true, 100);
        Step(50);
        Check.That(clients.All(client => client.Paused && client.PausedBy == ally.ConnectionId), "everybody is told who paused");
        ally.RequestPace(false, 100);
        Step(50);
        Check.That(clients.All(client => !client.Paused), "and that the pause is over");

        // ---- A member that falls behind: everybody is told how far, and - once the game is slowed for it - how long the server will still wait.
        keepsUp.Remove(enemy);
        Step(500);
        Check.That(host.Lag.TryGetValue(enemy.ConnectionId, out var behind) && behind is { LagMs: >= 200, Throttling: false, DropInMs: -1 }, $"behind but not yet slowing: no deadline ({(host.Lag.TryGetValue(enemy.ConnectionId, out var shown) ? shown : null)})");
        Step(700);
        Check.That(host.Lag[enemy.ConnectionId] is { Throttling: true, DropInMs: > 1_500 and <= 3_000 } first, $"slowed for it: the deadline is what is left of the server's limit ({host.Lag[enemy.ConnectionId]})");
        var before = host.Lag[enemy.ConnectionId].DropInMs;
        Step(1_500);
        Check.That(host.Lag[enemy.ConnectionId] is { Throttling: true } later && later.DropInMs < before - 500, $"and it runs down ({before} -> {host.Lag[enemy.ConnectionId].DropInMs})");
        Step(4_000);
        Check.That(host.Dropped.Any(dropped => dropped.ConnectionId == enemy.ConnectionId && dropped.Reason == DropReason.TooSlow) && !host.Lag.ContainsKey(enemy.ConnectionId),
            "when it is over the member is dropped, with the reason, and no longer waited for");
    }
}
