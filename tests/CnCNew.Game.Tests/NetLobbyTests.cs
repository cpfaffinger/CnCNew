using System.Diagnostics;
using System.Net;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Game.Tests;

/// <summary>
/// The way from an address to a room (docs/GAME-SHELL.md section 15) on real sockets of the loopback interface: a game hosts (<see cref="HostedServer"/>, the server
/// core on a thread of its own), lobbies connect to it (<see cref="NetLobby"/>). Every wait has a hard limit; ports are the system's. Needs no game data - the match
/// itself is built elsewhere (<c>scripts/check-net-lobby.ps1</c>).
/// </summary>
internal static class NetLobbyTests
{
    private const string Definitions = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(NetLobbyTests));
        harness.Run("A hosted server: the question about a new server's key, the list of its rooms, a room entered, seats, chat, the host's start", HostAndJoin);
        harness.Run("A locked room, a kick, a server whose key changed, a server that lists nothing, one that is not there, one that stops", Refusals);
        harness.Run("A lobby sends at its own pace whatever a person does with its boxes: far below what a server allows, the latest value wins", Pace);
        return harness.Failures;
    }

    private static readonly RelayOptions Room = new(AllowModifiedBuilds: true);

    private static IPEndPoint Local(HostedServer server) => new(IPAddress.Loopback, server.LocalEndPoint.Port);

    private static NetLobby Lobby(string name, KnownServers? known = null, string serverKey = "", UdpIdentity? key = null)
        => new(new NetLobbyOptions(name, Definitions, known ?? new KnownServers(), serverKey, key) { ListEveryMs = 300, ConnectTimeoutMs = 1_500 });

    /// <summary>Pumps the lobbies until the condition holds; false after the limit.</summary>
    private static bool Until(Func<bool> done, int limitMs, params NetLobby[] lobbies)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < limitMs)
        {
            foreach (var lobby in lobbies) lobby.Pump();
            if (done()) return true;
            Thread.Sleep(2);
        }
        return false;
    }

    private static RoomSettings TwoSeats()
        => new(60, "Maps/Test/Test.map", "TERRAIN", 10_000, 10_000, 60, 0,
            [new MatchSlotDescription(MatchSlotKind.Human, "Alpha", StartPosition: 0), new MatchSlotDescription(MatchSlotKind.Human, "Beta", StartPosition: 1)]);

    private static void HostAndJoin()
    {
        using var server = HostedServer.Start(new HostedServerOptions(Address: "127.0.0.1", Room: Room));
        TestHarness.Assert(server.IsRunning && server.LocalEndPoint.Port != 0 && server.Fingerprint.Length == 64, "the server runs on a port of its own and has a key");
        var known = new KnownServers();
        using var host = Lobby("host", known);
        using var guest = Lobby("guest");

        // The first meeting: nothing is said to the server before a person has seen its key.
        host.Connect(Local(server));
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.TrustQuestion, 3_000, host), $"a server never met before is a question ({host.Stage}; {host.Failure})");
        TestHarness.Assert(host.ServerFingerprint == server.Fingerprint && server.RoomsOpened == 0 && known.Count == 0, "the question shows the key the server holds; nothing is remembered and nothing was said yet");
        host.AcceptServer();
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.Browsing && host.Rooms is not null, 3_000, host), $"after the yes the lobby stands in front of the server's rooms ({host.Stage}; {host.Failure})");
        TestHarness.Assert(host.Rooms!.TotalRooms == 0 && known.Peek(Local(server), server.Fingerprint) == ServerTrust.Known, "an empty list, and the key is remembered from now on");

        host.Enter("our room");
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.InRoom && host.IsHost, 3_000, host), $"whoever names a room first opens it and is its host ({host.Stage}; {host.Failure})");
        host.Configure(TwoSeats());
        host.RequestSlot(0, "Alpha", 0, -1, 0);

        // The second player: the question, the yes, and the room is in the list with what it needs to be chosen.
        guest.Connect(Local(server));
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.TrustQuestion, 3_000, host, guest), "the guest is asked too");
        guest.AcceptServer();
        TestHarness.Assert(Until(() => guest.Rooms is { TotalRooms: 1 } list && list.Rooms[0] is { Name: "our room", Seats: 2, Players: 1, Members: 1, HasPassword: false, MapPath: "Maps/Test/Test.map" }, 4_000, host, guest),
            $"the guest's list shows the room, its map and one of two seats taken ({guest.Rooms?.Rooms.FirstOrDefault()})");
        guest.Enter("our room");
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.InRoom && host.Room is { Members.Length: 2 }, 3_000, host, guest), $"the guest is in the room ({guest.Stage}; {guest.Failure})");
        TestHarness.Assert(!guest.IsHost && guest.Room!.Settings.Slots.Length == 2 && guest.ConnectionId != host.ConnectionId, "it sees the room's settings and is not its host");

        guest.RequestSlot(1, "Beta", 0, -1, 1);
        guest.SetReady(true);
        host.SetReady(true);
        guest.SendChat("hello from the guest");
        TestHarness.Assert(Until(() => host.Chat.Any(line => line.Text == "hello from the guest" && line.ConnectionId == guest.ConnectionId)
                               && host.Room!.Members.Count(member => member.Slot >= 0 && member.Ready) == 2, 3_000, host, guest), "seats, ready and a chat line arrive at the other end");
        // Somebody who is not the host asks for the start: refused, and the lobby says so.
        guest.RequestStart();
        TestHarness.Assert(Until(() => guest.LastRefusal is { Reason: RejectReason.NotHost }, 3_000, host, guest), "a start by somebody who is not the host is refused");
        host.RequestStart();
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.Starting && guest.Stage == LobbyStage.Starting, 3_000, host, guest), $"the host's start reaches both ({host.Stage}, {guest.Stage}; {host.LastRefusal?.Text})");
        using var hostRelay = host.TakeRelay();
        using var guestRelay = guest.TakeRelay();
        TestHarness.Assert(hostRelay.Client.Start is { Player: 1 } && guestRelay.Client.Start is { Player: 2 } && hostRelay.Client.Description!.MapPath == "Maps/Test/Test.map"
                   && host.Stage == LobbyStage.Idle && hostRelay.ServerFingerprint == server.Fingerprint, "each hands its connection to the match, with the description and its own player");
    }

    private static void Refusals()
    {
        using var server = HostedServer.Start(new HostedServerOptions(Address: "127.0.0.1", Room: Room));
        var address = Local(server);
        using var host = Lobby("host", serverKey: server.Fingerprint);
        // A pinned key: no question.
        host.Connect(address);
        host.Enter("locked", "sesam");
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.InRoom, 3_000, host), $"with the key it was told to expect the lobby asks nothing, and a room named while connecting is entered ({host.Stage}; {host.Failure})");

        // ---- A wrong password; then the right one; then the host removes the guest, and the guest is told why.
        using var guest = Lobby("guest", serverKey: server.Fingerprint);
        guest.Connect(address);
        TestHarness.Assert(Until(() => guest.Rooms is { TotalRooms: 1 } list && list.Rooms[0].HasPassword, 3_000, host, guest), "the list says the room is locked");
        guest.Enter("locked", "Sesam");
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.Failed, 3_000, host, guest) && guest.Failure is { Kind: LobbyFailureKind.Rejected, Reject: RejectReason.BadPassword },
            $"a wrong password is a failure with its reason ({guest.Failure})");
        guest.LeaveRoom();
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.Browsing, 3_000, host, guest), "from a failure the lobby goes back in front of the rooms");
        guest.Enter("locked", "sesam");
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.InRoom, 3_000, host, guest), $"the right password opens ({guest.Failure})");
        host.Kick(guest.ConnectionId);
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.Failed, 3_000, host, guest) && guest.Failure is { Kind: LobbyFailureKind.Rejected, Reject: RejectReason.Kicked },
            $"a member the host removed is told so ({guest.Failure})");
        guest.LeaveRoom();
        guest.Enter("locked", "sesam");
        TestHarness.Assert(Until(() => guest.Stage == LobbyStage.Failed, 4_000, host, guest) && guest.Failure is { Reject: RejectReason.Kicked }, $"and may not come back to that room ({guest.Stage}; {guest.Failure})");

        // ---- The server stops while the host sits in its room: the lobby knows at once, and how.
        var port = server.LocalEndPoint.Port;
        var oldKey = server.Fingerprint;
        server.Dispose();
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.Failed, 3_000, host) && host.Failure is { Kind: LobbyFailureKind.ConnectionClosed, Close: UdpCloseReason.Peer },
            $"a server that stops tells the lobby ({host.Stage}; {host.Failure})");

        // ---- Nobody at the address.
        using var lost = Lobby("lost");
        lost.Connect(address);
        TestHarness.Assert(Until(() => lost.Stage == LobbyStage.Failed, 4_000, lost) && lost.Failure is { Kind: LobbyFailureKind.NoAnswer }, $"no server at the address is its own failure ({lost.Failure})");

        // ---- Another server at the same address, with another key: refused with both fingerprints, and nothing said to it.
        var known = new KnownServers();
        known.Remember(address, oldKey);
        using var other = HostedServer.Start(new HostedServerOptions(Port: port, Address: "127.0.0.1", Room: Room, ListRooms: false));
        using var wary = Lobby("wary", known);
        wary.Connect(address);
        TestHarness.Assert(Until(() => wary.Stage == LobbyStage.Failed, 3_000, wary) && wary.Failure is { Kind: LobbyFailureKind.KeyChanged } changed && changed.Shown == other.Fingerprint && changed.Expected == oldKey
                   && other.RoomsOpened == 0, $"a server whose key is another one is refused, with both keys named ({wary.Failure})");

        // ---- A server that lists nothing: no failure - a room is entered by its name.
        using var blind = Lobby("blind", serverKey: other.Fingerprint);
        blind.Connect(address);
        TestHarness.Assert(Until(() => blind.ListingRefused, 3_000, blind) && blind.Stage == LobbyStage.Browsing && blind.Rooms is null, $"a server without a list says so and the lobby stays ({blind.Stage}; {blind.Failure})");
        blind.Enter("by name");
        TestHarness.Assert(Until(() => blind.Stage == LobbyStage.InRoom && blind.IsHost, 4_000, blind) && blind.Reconnects == 1, $"and a room named by hand is entered on a fresh connection ({blind.Stage}; {blind.Failure})");
    }

    private static void Pace()
    {
        using var server = HostedServer.Start(new HostedServerOptions(Address: "127.0.0.1", Room: Room));
        using var host = Lobby("host", serverKey: server.Fingerprint);
        host.Connect(Local(server));
        host.Enter("pace");
        TestHarness.Assert(Until(() => host.Stage == LobbyStage.InRoom, 3_000, host), "in the room");
        host.Configure(TwoSeats());
        TestHarness.Assert(Until(() => host.Room is { Settings.Slots.Length: 2 }, 3_000, host), "with settings");
        var sentBefore = host.RequestsSent;
        // A person drags a box for a second: a thousand changes, sixty chat lines.
        var watch = Stopwatch.StartNew();
        var changes = 0;
        while (watch.ElapsedMilliseconds < 1_000)
        {
            host.RequestSlot(0, changes % 2 == 0 ? "Alpha" : "Beta", changes % 5, -1, 0);
            host.SetReady(changes % 2 == 0);
            if (changes % 16 == 0) host.SendChat($"line {changes}");
            changes++;
            host.Pump();
        }
        host.RequestSlot(0, "Beta", 3, -1, 0);
        host.SetReady(true);
        TestHarness.Assert(Until(() => host.Room!.Members.Any(member => member.ConnectionId == host.ConnectionId && member.Slot == 0 && member.Ready)
                               && host.Room.Settings.Slots[0] is { Faction: "Beta", Team: 3 }, 3_000, host), "the last value is what the room holds");
        var sent = host.RequestsSent - sentBefore;
        TestHarness.Assert(changes > 500 && sent <= 60 && sent >= 10, $"{changes} changes in a second became {sent} messages - at most the lobby's pace, far below the 500 a server allows");
        TestHarness.Assert(host.ChatDropped > 0 && host.Stage == LobbyStage.InRoom && host.Failure is null, $"chat beyond a short queue is counted as dropped ({host.ChatDropped}), and the server drops nobody");
    }
}
