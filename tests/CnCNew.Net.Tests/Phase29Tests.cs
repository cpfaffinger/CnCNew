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
        oldConnection.Send(MessageCodec.Encode(new RoomListRequestMessage(4)));
        Pump(directory, old);
        Check.That(old.Rejection is { Reason: RejectReason.ProtocolVersion } rejected && rejected.Text.Contains('5') && rejected.Text.Contains('4'), "a request of another protocol version is answered with both versions");

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
}
