using System.Buffers.Binary;
using System.Net;
using System.Text;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Commands;

// Phase 28: what a member may cost a room (docs/NETWORK-PROTOCOL.md section 20.4), who may be in it (20.5), what a hostile member can do to a match (20.6), and that
// what proves a seat is never readable on the line (20.3).
static partial class NetTests
{
    private static RelayOptions Bare(uint seed = 5) => new(SeedSource: () => seed, LagReportMs: 0, LagThrottleMs: 0);

    private static LockstepClient Enter(Net net, string name, string password = "", string origin = "", ulong token = 0, bool watch = false)
    {
        var (client, server) = net.Network.Connect();
        net.Server.Attach(server, origin);
        var joined = new LockstepClient(client, name, Definitions, Released, rejoinToken: token, watch: watch, roomPassword: password);
        net.Clients.Add(joined);
        return joined;
    }

    public static void RelayLimits()
    {
        // ---- A member that sends more messages than a connection may is dropped; the others are told why and play on.
        var net = new Net(Bare() with { MessagesPerSecond = 50, MessageBurst = 100, HashReportLimitMs = 0 });
        net.StartMatch(2);
        for (var index = 0; index < 40; index++) net.Clients[0].SendChat("within the allowance");
        net.Pump();
        Check.That(net.Server.FloodDrops == 0 && net.Clients[0].IsOpen, "a burst within the allowance is talk");
        for (var index = 0; index < 150; index++) net.Clients[1].SendChat("flood");
        Play(net, 100, 0);
        Check.That(net.Server.FloodDrops == 1 && !net.Clients[1].IsOpen && net.Clients[0].Dropped.Any(drop => drop.Reason == DropReason.Flood) && net.Clients[0].IsOpen && net.Server.Phase == RoomPhase.Match,
            $"the flooding member is dropped and the room goes on ({net.Server.FloodDrops}, {net.Server.Phase})");
        Check.That(net.Server.Recording.Bundles.SelectMany(bundle => bundle.Commands).Count(command => command.Origin == 0 && command.Player == 2) == 1, "its player leaves the match by a command, like anybody who is dropped");
        // The allowance refills: a member that talks at its rate for a minute is never dropped.
        for (var second = 0; second < 60; second++)
        {
            for (var index = 0; index < 40; index++) net.Clients[0].SendChat("steady");
            Play(net, 1_000);
        }
        Check.That(net.Server.FloodDrops == 1 && net.Clients[0].IsOpen, "forty messages a second for a minute are within fifty a second");

        // ---- A connection that says nothing is closed.
        var quiet = new Net(Bare());
        quiet.Join();
        quiet.Raw();
        quiet.Pump();
        Check.That(quiet.Server.Connections == 2, "a silent connection holds a place");
        Play(quiet, 10_500);
        Check.That(quiet.Server.HelloTimeouts == 1 && quiet.Server.Connections == 1 && quiet.Clients[0].IsOpen, "for ten seconds");

        // ---- A member that never finishes loading does not keep the others in front of their loading screens.
        var loading = new Net(Bare() with { LoadLimitMs = 3_000 });
        loading.Join();
        loading.Join();
        loading.Pump();
        loading.Clients[0].Configure(TwoSeats());
        loading.Pump();
        loading.Clients[0].RequestSlot(0, "Alpha", startPosition: 0);
        loading.Clients[1].RequestSlot(1, "Beta", startPosition: 1);
        loading.Clients[1].SetReady(true);
        loading.Pump();
        loading.Clients[0].RequestStart();
        loading.Pump();
        loading.Clients[0].ReportLoaded(42);
        Play(loading, 2_500);
        Check.That(loading.Server.Phase == RoomPhase.Loading && loading.Server.LoadTimeouts == 0, "within the limit the room waits");
        Play(loading, 1_000);
        Check.That(loading.Server.LoadTimeouts == 1 && loading.Server.Phase == RoomPhase.Match && loading.Clients[0].Phase == ClientPhase.InMatch
                   && loading.Clients[0].Dropped.Any(drop => drop.Reason == DropReason.TooSlow), $"after it the member is dropped and the match begins ({loading.Server.Phase})");

        // ---- A member that plays along and reports no hash is not waited for.
        var stalled = new Net(Bare() with { HashReportLimitMs = 5_000 });
        stalled.StartMatch(2);
        Play(stalled, 4_000, 0);
        Check.That(stalled.Server.HashStallDrops == 0 && stalled.Server.LastAgreedTick < 0, "while a member withholds its hashes nothing is agreed on");
        Play(stalled, 2_000, 0);
        Check.That(stalled.Server.HashStallDrops == 1 && stalled.Clients[0].Dropped.Any(drop => drop.Reason == DropReason.ProtocolViolation) && stalled.Server.LastAgreedTick > 300,
            $"after the limit it is dropped, and the ticks it held up are agreed on ({stalled.Server.LastAgreedTick})");

        // ---- A host cannot set a match longer than the server plays.
        var bounded = new Net(Bare() with { MaxMatchSeconds = 60 });
        bounded.Join();
        bounded.Pump();
        bounded.Clients[0].Configure(TwoSeats());
        bounded.Pump();
        Check.That(bounded.Clients[0].LastRefusal is { Reason: RejectReason.InvalidRequest } && bounded.Server.Settings.Slots.IsEmpty, "a match of 166 s on a server that plays 60 s is refused");
        bounded.Clients[0].Configure(TwoSeats() with { MaxTicks = 3_600 });
        bounded.Pump();
        Check.That(bounded.Server.Settings.MaxTicks == 3_600, "one of 60 s is set");

        // ---- A session state larger than the server takes: at the start from a saved match ...
        var small = new Net(Bare(9) with { SeatHoldMs = 60_000, MaxStateBytes = 1_000 });
        small.Join();
        small.Join();
        small.Join();
        small.Pump();
        small.Clients[0].Configure(TwoSeats());
        small.Pump();
        small.Clients[0].RequestSlot(0, "Alpha", startPosition: 0);
        small.Clients[1].RequestSlot(1, "Beta", startPosition: 1);
        small.Clients[1].SetReady(true);
        small.Pump();
        small.Clients[0].RequestStart(new ResumeState(100, 7, [], new byte[2_000]));
        small.Pump();
        Check.That(small.Server.StatesRefusedForSize == 1 && small.Server.Phase == RoomPhase.Room && small.Clients[0].LastRefusal is { Reason: RejectReason.InvalidRequest }, "a saved match beyond the limit is not started from");
        // ... and from a member, for somebody who joins.
        small.Clients[0].RequestStart();
        small.Pump();
        foreach (var client in small.Clients) client.ReportLoaded(42);
        small.Pump();
        var token = small.Clients[1].RejoinToken;
        Play(small, 500, 0, 1, 2);
        small.Clients[1].Close();
        Play(small, 200, 0, 2);
        uint asked = 0, askedNext = 0;
        small.Clients[0].SnapshotRequested = request => asked = request;
        small.Clients[2].SnapshotRequested = request => askedNext = request;
        var back = Enter(small, "back", token: token);
        Play(small, 100, 0, 2);
        Check.That(asked != 0, "a member is asked for its state");
        small.Clients[0].OfferSnapshot(asked, new ResumeState(small.Server.LastAgreedTick + 1, 7, [], new byte[2_000]));
        Play(small, 100, 0, 2);
        Check.That(small.Server.StatesRefusedForSize == 2 && small.Server.SnapshotsRefused == 1 && back.Start is null && askedNext != 0, "a state beyond the limit goes nowhere, and the next member is asked");

        // ---- Not more than so many joiners a minute are handed the state of a member.
        var busy = new Net(Bare() with { AllowLateWatchers = true, SnapshotsPerMinute = 2 });
        busy.StartMatch(2);
        Play(busy, 300, 0, 1);
        var watchers = Enumerable.Range(0, 3).Select(index => Enter(busy, $"watcher {index}", watch: true)).ToList();
        Play(busy, 100, 0, 1);
        Check.That(watchers[0].Phase == ClientPhase.InRoom && watchers[1].Phase == ClientPhase.InRoom && watchers[2].Rejection is { Reason: RejectReason.CannotCatchUp } && busy.Server.JoinsRateLimited == 1,
            "the third joiner of a minute is asked to come back later");
        Play(busy, 60_000, 0, 1);
        var later = Enter(busy, "later", watch: true);
        Play(busy, 100, 0, 1);
        Check.That(later.Phase == ClientPhase.InRoom, "a minute later there is room again");

        // ---- The recording of a match is bounded: a match that is fed beyond it is ended, for everybody, with the reason.
        var fed = new Net(Bare() with { MaxRecordingBytes = 20_000 });
        fed.StartMatch(2);
        for (var round = 0; round < 40 && fed.Server.Phase == RoomPhase.Match; round++)
        {
            for (var index = 0; index < 30; index++) fed.Clients[0].SendIntent((ulong)(round * 100 + index + 1), MoveIntent(x: index));
            Play(fed, 100, 0, 1);
        }
        Check.That(fed.Server.End is { Reason: EndReason.ServerLimit } && fed.Server.RecordingBytes is > 20_000 and < 30_000 && fed.Clients.All(client => client.End is { Reason: EndReason.ServerLimit }),
            $"a recording beyond its bound ends the match ({fed.Server.RecordingBytes} bytes, {fed.Server.End?.Reason})");

        // ---- A lobby that never starts is closed after its time.
        var idle = new Net(Bare() with { RoomIdleLimitMs = 30_000 });
        idle.Join();
        idle.Join();
        Play(idle, 29_000);
        Check.That(idle.Clients.All(client => client.IsOpen), "a lobby may stand for a while");
        Play(idle, 1_500);
        Check.That(idle.Clients.All(client => !client.IsOpen) && idle.Server.Connections == 0, "and then everybody is sent home");
    }

    public static void PasswordAndKick()
    {
        // ---- Whoever opens the room sets its password; everybody else is challenged.
        var net = new Net(Bare());
        var opener = Enter(net, "opener", "sesam");
        net.Pump();
        Check.That(opener.Phase == ClientPhase.InRoom && opener.IsHost && net.Server.HasPassword, "the opener is in, and the room has a password");
        var right = Enter(net, "right", "sesam");
        var wrong = Enter(net, "wrong", "Sesam");
        var none = Enter(net, "none");
        net.Pump(8);
        Check.That(right.Phase == ClientPhase.InRoom && wrong.Rejection is { Reason: RejectReason.BadPassword } && none.Rejection is { Reason: RejectReason.BadPassword } && net.Server.BadPasswords == 2
                   && net.Server.Members == 2, $"the right password opens, a wrong one and none do not ({right.Phase}, {wrong.Rejection?.Reason}, {none.Rejection?.Reason})");
        // The password itself is never sent: what goes over the line is a verifier, bound to the room's name, and an answer to a nonce that never repeats.
        var verifier = RoomPassword.Verifier("sesam", "");
        Check.That(verifier.Length == 64 && verifier != RoomPassword.Verifier("sesam", "other room") && verifier != RoomPassword.Verifier("Sesam", "") && RoomPassword.Verifier("", "") == string.Empty,
            "the verifier depends on password and room");
        var (nonceA, nonceB) = ("00112233445566778899AABBCCDDEEFF", "FFEEDDCCBBAA99887766554433221100");
        Check.That(RoomPassword.Matches(verifier, nonceA, RoomPassword.Answer(verifier, nonceA)) && !RoomPassword.Matches(verifier, nonceB, RoomPassword.Answer(verifier, nonceA))
                   && !RoomPassword.Matches(verifier, nonceA, RoomPassword.Answer(string.Empty, nonceA)), "an answer fits one nonce only, and no answer fits without the verifier");
        // A connection that is challenged and does not answer is closed like one that says nothing.
        var mute = net.Raw();
        mute.Send(MessageCodec.Encode(new HelloMessage(Protocol.Version, Released.Id, CommandTypes.Version, CnCNew.Simulation.Replay.ReplayFormat.CurrentVersion,
            CnCNew.Simulation.Snapshots.WorldSnapshot.Version, CnCNew.Simulation.Skirmish.MatchDescription.EncodingVersion, Definitions, "mute")));
        net.Pump();
        Check.That(mute.TryReceive(out var challenge) && MessageCodec.TryDecode(challenge, out var decoded, out _) && decoded is PasswordChallengeMessage { Nonce.Length: 32 }, "a hello to a locked room is answered with a challenge");
        mute.Send(MessageCodec.Encode(new HelloMessage(Protocol.Version, Released.Id, CommandTypes.Version, CnCNew.Simulation.Replay.ReplayFormat.CurrentVersion,
            CnCNew.Simulation.Snapshots.WorldSnapshot.Version, CnCNew.Simulation.Skirmish.MatchDescription.EncodingVersion, Definitions, "mute again")));
        net.Pump();
        Check.That(net.Server.Members == 2 && net.Server.Discarded >= 1, "a second hello instead of the answer is not an answer");
        Play(net, 10_500);
        Check.That(net.Server.HelloTimeouts == 1, "and after ten seconds the connection is closed");
        // A room without a password asks nobody, whatever a client brings along.
        var open = new Net(Bare());
        Enter(open, "a");
        var guest = Enter(open, "b", "some password");
        open.Pump(8);
        Check.That(guest.Phase == ClientPhase.InRoom && !open.Server.HasPassword, "a room that was opened without a password has none");

        // ---- The host removes a member; it may not come back from where it came.
        var room = new Net(Bare());
        var host = Enter(room, "host", origin: "10.1.1.1");
        room.Pump();
        var guestA = Enter(room, "guest a", origin: "10.2.2.2");
        var guestB = Enter(room, "guest b", origin: "10.3.3.3");
        room.Pump();
        guestA.Kick(host.ConnectionId);
        host.Kick(host.ConnectionId);
        host.Kick(999);
        room.Pump();
        Check.That(guestA.LastRefusal is { Reason: RejectReason.NotHost } && host.LastRefusal is { Reason: RejectReason.InvalidRequest } && room.Server.Kicks == 0 && room.Server.Members == 3,
            "only the host removes, and only somebody else who is there");
        host.Kick(guestA.ConnectionId);
        room.Pump();
        Check.That(room.Server.Kicks == 1 && !guestA.IsOpen && guestA.LastRefusal is { Reason: RejectReason.Kicked } && guestB.Dropped.Any(drop => drop.Reason == DropReason.Kicked && drop.ConnectionId == guestA.ConnectionId)
                   && room.Server.Members == 2, "the member is told, removed, and the others see why it is gone");
        var again = Enter(room, "guest a again", origin: "10.2.2.2");
        var friend = Enter(room, "friend", origin: "10.9.9.9");
        room.Pump();
        Check.That(again.Rejection is { Reason: RejectReason.Kicked } && friend.Phase == ClientPhase.InRoom && room.Server.KickedOrigins == 1, "from the same address it does not come back; somebody else does");
    }

    public static void HostileMembers()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 1_500);
        var rate = settings.TickRate;

        // ---- Garbage behind a plausible header passes the relay, which reads no command; every client leaves it out alike. System commands do not pass at all.
        var garbage = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Disturbances: [new ProbeDisturbance(1, rate * 3, ProbeDisturbanceKind.Garbage)]));
        Check.That(garbage.Ok, $"the match goes on in step: {garbage.Problem}");
        Check.That(garbage.UndecodableCommands == 2 && garbage.ServerDiscarded >= 2 && garbage.ReplayedSessionHash == garbage.FinalSessionHash && garbage.Dropped.IsDefaultOrEmpty,
            $"two orders that do not decode were left out by every client and by the replay, a spawn and a gift never left the relay ({garbage.UndecodableCommands}, {garbage.ServerDiscarded})");
        var bytes = CommandWire.EncodeIntent((sequence, tick) => new SpawnCommand(new CnCNew.Simulation.Ids.PlayerId(1), sequence, tick, "Grunt", new CnCNew.Simulation.Ids.PlayerId(1),
            CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero));
        Check.That(!CommandWire.IsPlausible(bytes, out var why) && why.Contains("not an order of a player", StringComparison.Ordinal) && CommandWire.IsPlausible(MoveIntent(), out _),
            "a spawn is not an order of a player, a move is");
        var entry = new BundleCommand(2, 1, 1, 1, CommandWire.Stamp(MoveIntent(), 2, 77).AsSpan(0, 30).ToArray());
        Check.That(!CommandWire.TryDecodeIntent(entry, out _, out var error) && error.Length > 0
                   && !CommandWire.TryDecodeIntent(new BundleCommand(1, 1, 1, 1, CommandWire.Stamp(bytes, 1, 5)), out _, out _)
                   && CommandWire.TryDecodeIntent(new BundleCommand(2, 1, 0, 0, CommandWire.Leave(2, 5, false)), out _, out _)
                   && !CommandWire.TryDecodeIntent(new BundleCommand(2, 1, 4, 0, CommandWire.Leave(2, 5, false)), out _, out _),
            "what does not decode, a spawn, and a leave that is not the relay's are no commands of a bundle; the relay's own leave is");

        // ---- A member that floods is dropped; the three others ... are two, and stay in step.
        var flood = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Disturbances: [new ProbeDisturbance(2, rate * 3, ProbeDisturbanceKind.Flood)]));
        Check.That(flood.Ok && flood.FloodDrops == 1 && flood.Dropped.Any(drop => drop.Member == 2 && drop.Reason == DropReason.Flood) && flood.ReplayedSessionHash == flood.FinalSessionHash,
            $"twenty thousand messages at once: dropped, and the others in step ({flood.Problem}; {flood.FloodDrops})");

        // ---- A member that plays along and withholds its hashes: dropped after the limit, and every tick is agreed on in the end.
        var withheld = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Relay: new RelayOptions { HashReportLimitMs = 3_000 },
            Disturbances: [new ProbeDisturbance(2, rate * 3, ProbeDisturbanceKind.WithholdHashes)]));
        Check.That(withheld.Ok && withheld.HashStallDrops == 1 && withheld.Dropped.Any(drop => drop.Member == 2 && drop.Reason == DropReason.ProtocolViolation) && withheld.LastAgreedTick == settings.MaxTicks - 1,
            $"withheld hashes: dropped, the others agree on every tick ({withheld.Problem}; {withheld.HashStallDrops}; agreed to {withheld.LastAgreedTick})");

        // ---- The host removes a player from the running match: it leaves by a command, like a lost connection.
        var kicked = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Disturbances: [new ProbeDisturbance(1, rate * 4, ProbeDisturbanceKind.Kick)]));
        Check.That(kicked.Ok && kicked.Kicks == 1 && kicked.Dropped.Any(drop => drop.Member == 1 && drop.Reason == DropReason.Kicked) && kicked.ReplayedSessionHash == kicked.FinalSessionHash,
            $"a kick in a match: the others in step, the recording replays ({kicked.Problem})");
    }

    public static void SecretsStayOffTheWire()
    {
        // A room on a server that holds seats, over the sealed transport on a network somebody reads along on: the tokens that prove a seat, the names, the room's
        // name and what is said in it are in no datagram.
        var clock = new ManualNetClock();
        var network = new MemoryDatagramNetwork(clock, 3);
        var tape = new List<byte[]>();
        network.Tap = (_, _, bytes) => tape.Add(bytes);
        var serverAddress = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 4000);
        using var endpoint = new UdpServerEndpoint(network.Open(serverAddress), clock, [..Enumerable.Range(7, 32).Select(value => (byte)value)], UdpTests.Unbounded);
        var seed = 100u;
        var server = new RelayServer(clock, Bare() with { SeedSource = () => seed += 0x9E3779B9, SeatHoldMs = 60_000 });
        var ends = new List<UdpClientEndpoint>();
        var clients = new List<LockstepClient>();
        for (var index = 0; index < 2; index++)
        {
            var end = new UdpClientEndpoint(network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5000 + index)), serverAddress, clock, 77UL + (ulong)index, UdpTests.Unbounded);
            ends.Add(end);
            clients.Add(new LockstepClient(end.Connection, index == 0 ? "Ariadne-of-the-thread" : "Theseus-in-the-maze", Definitions, Released, room: "labyrinth-of-knossos", roomPassword: "minotaur-password"));
        }
        void Step(int ms)
        {
            for (var index = 0; index < ms; index++)
            {
                clock.Advance(1);
                foreach (var end in ends) end.Pump();
                endpoint.Pump();
                while (endpoint.TryAccept(out var link, out var from)) server.Attach(link, from.Address.ToString());
                server.Pump();
                foreach (var client in clients) client.Pump();
                foreach (var end in ends) end.Flush();
                endpoint.Flush();
            }
        }
        Step(300);
        Check.That(clients.All(client => client.Phase == ClientPhase.InRoom && client.RejoinToken != 0) && clients[0].RejoinToken != clients[1].RejoinToken && server.HasPassword,
            $"both are in the locked room and hold a token ({clients[0].Phase}, {clients[1].Phase}; {server.LastDiscard})");
        clients[0].SendChat("meet me at the western gate");
        Step(100);
        var words = new List<byte[]> { Encoding.UTF8.GetBytes("Ariadne-of-the-thread"), Encoding.UTF8.GetBytes("Theseus-in-the-maze"), Encoding.UTF8.GetBytes("labyrinth-of-knossos"),
            Encoding.UTF8.GetBytes("minotaur-password"), Encoding.UTF8.GetBytes("western gate"), Encoding.UTF8.GetBytes(RoomPassword.Verifier("minotaur-password", "labyrinth-of-knossos")),
            Encoding.UTF8.GetBytes(Definitions) };
        foreach (var client in clients)
        {
            var token = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(token, client.RejoinToken);
            words.Add(token);
            BinaryPrimitives.WriteUInt64BigEndian(token = new byte[8], client.RejoinToken);
            words.Add(token);
        }
        Check.That(tape.Count > 20 && words.All(word => tape.All(datagram => datagram.AsSpan().IndexOf(word) < 0)),
            $"nothing that was said is in any of the {tape.Count} datagrams: no token, no name, no room, no password or its verifier, no chat");
        // What an eavesdropper has left is guessing: a token nobody was given opens no seat (the test of the rejoin shows the refusal), and a guess costs a connection each.
        foreach (var end in ends) end.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------- player keys

    public static void PlayerKeys()
    {
        using var alice = CnCNew.Net.Udp.UdpIdentity.Create();
        using var mallory = CnCNew.Net.Udp.UdpIdentity.Create();
        var banned = new HashSet<string>(StringComparer.Ordinal);
        var net = new Net(Bare() with { IsKeyBanned = banned.Contains });
        LockstepClient Come(Net to, string name, CnCNew.Net.Udp.UdpIdentity? key, string origin)
        {
            var (client, server) = to.Network.Connect();
            to.Server.Attach(server, origin);
            var joined = new LockstepClient(client, name, Definitions, Released, playerKey: key);
            to.Clients.Add(joined);
            to.Pump(8);
            return joined;
        }
        HelloMessage Hello(string name, string key) => new(Protocol.Version, Released.Id, CommandTypes.Version, CnCNew.Simulation.Replay.ReplayFormat.CurrentVersion,
            CnCNew.Simulation.Snapshots.WorldSnapshot.Version, CnCNew.Simulation.Skirmish.MatchDescription.EncodingVersion, Definitions, name, PlayerKey: key);
        static NetMessage? Next(IConnection connection) => connection.TryReceive(out var bytes) && MessageCodec.TryDecode(bytes, out var message, out _) ? message : null;

        // ---- A member that states a key proves it; the server knows the player by the key's fingerprint. A member without one is a member like before.
        var host = Come(net, "host", null, "10.0.0.1");
        var member = Come(net, "alice", alice, "10.0.0.2");
        Check.That(host.Phase == ClientPhase.InRoom && member.Phase == ClientPhase.InRoom && net.Server.KeyOf(member.ConnectionId) == alice.Fingerprint && net.Server.KeyOf(host.ConnectionId).Length == 0,
            $"a stated and proved key is the member's ({member.Phase}; {member.Rejection?.Text})");
        Check.That(alice.Fingerprint.Length == 64 && PlayerKey.FingerprintOf(PlayerKey.PublicOf(alice)) == alice.Fingerprint && alice.Fingerprint != mallory.Fingerprint, "the fingerprint is the key's, and another key has another");

        // ---- Somebody states alice's key and signs with its own: turned away.
        var thief = net.Raw();
        thief.Send(MessageCodec.Encode(Hello("mallory", PlayerKey.PublicOf(alice))));
        net.Pump();
        var asked = Next(thief) as PasswordChallengeMessage;
        Check.That(asked is { Nonce.Length: 32 }, "a hello that states a key is answered with a challenge");
        thief.Send(MessageCodec.Encode(new PasswordResponseMessage(new string('0', 64), PlayerKey.Sign(mallory, asked!.Nonce, string.Empty))));
        net.Pump();
        Check.That(Next(thief) is RejectMessage { Reason: RejectReason.BadIdentity } && net.Server.BadIdentities == 1 && net.Server.Members == 2, "a signature of another key does not prove the stated one");

        // ---- Somebody recorded an answer of alice (for one nonce) and gives it to another challenge: turned away. So is an answer without a signature.
        var recorded = PlayerKey.Sign(alice, asked.Nonce, string.Empty);
        var replayer = net.Raw();
        replayer.Send(MessageCodec.Encode(Hello("replay", PlayerKey.PublicOf(alice))));
        net.Pump();
        var second = Next(replayer) as PasswordChallengeMessage;
        Check.That(second is not null && second.Nonce != asked.Nonce, "every challenge has a nonce of its own");
        replayer.Send(MessageCodec.Encode(new PasswordResponseMessage(new string('0', 64), recorded)));
        var silent = net.Raw();
        silent.Send(MessageCodec.Encode(Hello("unsigned", PlayerKey.PublicOf(alice))));
        net.Pump();
        Next(silent);
        silent.Send(MessageCodec.Encode(new PasswordResponseMessage(new string('0', 64))));
        net.Pump();
        Check.That(Next(replayer) is RejectMessage { Reason: RejectReason.BadIdentity } && Next(silent) is RejectMessage { Reason: RejectReason.BadIdentity } && net.Server.BadIdentities == 3,
            "an answer to another nonce is refused, and so is none");
        Check.That(PlayerKey.Verify(PlayerKey.PublicOf(alice), asked.Nonce, string.Empty, recorded) && !PlayerKey.Verify(PlayerKey.PublicOf(alice), asked.Nonce, "another room", recorded)
                   && !PlayerKey.Verify(new string('0', 128), asked.Nonce, string.Empty, recorded), "a signature holds for one nonce in one room, and a key that is no point of the curve proves nothing");

        // ---- The key is banned while its owner is in the room: the member leaves; and it does not get in again, whatever address it comes from.
        banned.Add(alice.Fingerprint);
        Check.That(net.Server.DropWhereKey(banned.Contains) == 1, "the ban finds the member by its key");
        net.Pump(8);
        Check.That(!member.IsOpen && net.Server.Members == 1 && host.Room!.Members.Length == 1, "a member whose key is banned leaves the room");
        var again = Come(net, "alice again", alice, "10.9.9.9");
        Check.That(again.Rejection is { Reason: RejectReason.Banned } && net.Server.KeysBanned == 2 && net.Server.Members == 1, $"and is turned away when it comes again from elsewhere ({again.Rejection?.Reason})");
        var other = Come(net, "bob", mallory, "10.0.0.2");
        Check.That(other.Phase == ClientPhase.InRoom, "another key from the same address is another player");

        // ---- A server that requires a key admits nobody without one.
        var strict = new Net(Bare() with { RequirePlayerKey = true });
        var nameless = Come(strict, "nameless", null, "10.0.0.1");
        var named = Come(strict, "named", alice, "10.0.0.1");
        Check.That(nameless.Rejection is { Reason: RejectReason.BadIdentity } && named.Phase == ClientPhase.InRoom && named.IsHost, "a server that requires a player key turns a client without one away");

        // ---- A kicked member's key may not come back to the room from another address either.
        var room = new Net(Bare());
        var owner = Come(room, "owner", null, "10.1.1.1");
        var guest = Come(room, "guest", alice, "10.2.2.2");
        owner.Kick(guest.ConnectionId);
        room.Pump(8);
        var back = Come(room, "guest again", alice, "10.3.3.3");
        var stranger = Come(room, "stranger", mallory, "10.3.3.3");
        Check.That(back.Rejection is { Reason: RejectReason.Kicked } && stranger.Phase == ClientPhase.InRoom && room.Server.Kicks == 1, "the kicked player's key is refused from a new address; somebody else from there is not");

        // ---- A room with a password and a member with a key: one exchange answers both, and each can fail on its own.
        var locked = new Net(Bare());
        Enter(locked, "opener", "sesam");
        locked.Pump(8);
        LockstepClient Both(string name, string password, CnCNew.Net.Udp.UdpIdentity key)
        {
            var (client, server) = locked.Network.Connect();
            locked.Server.Attach(server, "10.4.4.4");
            var joined = new LockstepClient(client, name, Definitions, Released, roomPassword: password, playerKey: key);
            locked.Clients.Add(joined);
            locked.Pump(8);
            return joined;
        }
        var good = Both("good", "sesam", alice);
        var wrongWord = Both("wrong word", "Sesam", mallory);
        Check.That(good.Phase == ClientPhase.InRoom && locked.Server.KeyOf(good.ConnectionId) == alice.Fingerprint && wrongWord.Rejection is { Reason: RejectReason.BadPassword },
            "password and key are answered together; a proved key does not open a locked room");
    }

    // --------------------------------------------------------------------------------------- just below every limit

    public static void MaliciouslySlowClient()
    {
        // The rule for members that fall behind counts what goes beyond the least a member showed lately, at most a second of it (section 14.1). A member that plays
        // 2.9 s behind for good is therefore 1.9 s "behind" - just below the 2 s that would slow the game. It must cost the others nothing.
        var net = new Net(new RelayOptions(SeedSource: () => 5) { HashReportLimitMs = 20_000 });
        net.StartMatch(2, TwoSeats());
        var (honest, slow) = (net.Clients[0], net.Clients[1]);
        long honestDone = 0, slowDone = 0;
        void Run(Net on, LockstepClient first, LockstepClient second, int ms, long behindTicks, long from = -1, bool claims = false)
        {
            for (var elapsed = 0; elapsed < ms; elapsed += 5)
            {
                on.Clock.Advance(5);
                on.Server.Pump();
                first.Pump();
                second.Pump();
                // "I have played this tick", ten times a second, as a client's ping says it - whatever its hashes say.
                if (claims && second.IsOpen && elapsed % 100 == 0) second.SendRaw(MessageCodec.Encode(new PingMessage(on.Clock.Microseconds, second.NewestBundleTick)));
                for (; honestDone <= first.NewestBundleTick; honestDone++) first.ReportHash(honestDone, 7);
                if (!second.IsOpen || behindTicks < 0) continue;
                // With a start value the distance grows to its end by a tick every 100 ms: slowly enough that the rule never sees it jump.
                var behind = from < 0 ? behindTicks : Math.Min(behindTicks, from + elapsed / 100);
                for (; slowDone <= second.NewestBundleTick - behind; slowDone++) second.ReportHash(slowDone, 7);
            }
        }
        Run(net, honest, slow, 20_000, 174, from: 0);
        Check.That(net.Server.ThrottledMicros == 0 && slow.IsOpen, $"it gets there without ever crossing the limit ({net.Server.ThrottledMicros / 1000} ms slowed)");
        var startedAt = net.Server.NextTick;
        Run(net, honest, slow, 60_000, 174);
        Check.That(net.Server.ThrottledMicros == 0 && !net.Server.Throttling && Math.Abs(net.Server.NextTick - startedAt - 3_600) <= 6 && slow.IsOpen && net.Server.DroppedForLag == 0,
            $"a member 2.9 s behind for a minute: nobody was slowed, nobody dropped ({net.Server.ThrottledMicros / 1000} ms slowed, {net.Server.NextTick} ticks)");
        Check.That(net.Server.LastAgreedTick >= net.Server.NextTick - 174 - 6 && honest.Lag.ContainsKey(slow.ConnectionId), $"the ticks are agreed on as far as it has reported, and everybody is told it is behind ({net.Server.NextTick - net.Server.LastAgreedTick} ticks)");
        // It crosses the line - 3.5 s behind: now the game is slowed for it, for ten seconds in all, and then it is gone and the match runs at full speed.
        Run(net, honest, slow, 25_000, 210);
        var slowed = net.Server.ThrottledMicros;
        Check.That(!slow.IsOpen && net.Server.DroppedForLag == 1 && honest.Dropped.Any(drop => drop.Reason == DropReason.TooSlow) && slowed is >= 9_500_000 and <= 11_500_000 && !net.Server.Throttling,
            $"above the limit it is carried for ten seconds and dropped ({slowed / 1000} ms slowed, dropped {net.Server.DroppedForLag})");
        var before = net.Server.NextTick;
        Run(net, honest, slow, 2_000, 0);
        Check.That(Math.Abs(net.Server.NextTick - before - 120) <= 3 && net.Server.LastAgreedTick >= net.Server.NextTick - 4 && net.Server.ThrottledMicros == slowed, "and the match of the others runs at full speed, agreed tick by tick");

        // The other limit: a member that says it keeps up (its pings name the tick it has played) and reports its hashes as late as the limit allows - 19 of 20 s. The
        // lag rule is off here, as if it were blind to it. The ticks are agreed on that much later and no later, and when it stops reporting it is dropped.
        var late = new Net(Bare() with { HashReportLimitMs = 20_000 });
        late.StartMatch(2, TwoSeats());
        (honestDone, slowDone) = (0, 0);
        Run(late, late.Clients[0], late.Clients[1], 60_000, 1_140, claims: true);
        Check.That(late.Server.HashStallDrops == 0 && late.Clients[1].IsOpen && late.Server.LastAgreedTick >= late.Server.NextTick - 1_140 - 6 && Math.Abs(late.Server.NextTick - 3_600) <= 6,
            $"hashes 19 s late for a minute: the member stays, the ticks are agreed 19 s late and no later ({late.Server.NextTick - late.Server.LastAgreedTick} ticks behind; {late.Server.HashStallDrops} dropped)");
        Run(late, late.Clients[0], late.Clients[1], 22_000, -1, claims: true);
        Check.That(late.Server.HashStallDrops == 1 && !late.Clients[1].IsOpen, $"when it stops reporting it is dropped after the limit ({late.Server.HashStallDrops})");
        Run(late, late.Clients[0], late.Clients[1], 1_000, -1);
        Check.That(late.Server.LastAgreedTick >= late.Server.NextTick - 4 && late.Server.Phase == RoomPhase.Match, "and the others' ticks are agreed at once again");
    }
}
