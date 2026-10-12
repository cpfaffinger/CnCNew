using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Tests;

// Phase 25: the protocol, the loopback transport, the room and relay, the client, and a match of several clients - all in one process.
TestRateOption.Announce();
var run = new Runner();
run.Test("Build identity: the commit and the modified flag are read from the assembly", NetTests.BuildIdentityIsRead);
run.Test("Messages: every message survives its bytes", NetTests.MessagesRoundTrip);
run.Test("Messages: unknown type, over the limit, truncated and trailing bytes are refused with a reason, never thrown", NetTests.MalformedMessagesAreRefused);
run.Test("Command wire: the stamp writes player and tick where the simulation's codec reads them", NetTests.CommandStamp);
run.Test("Loopback: ordered per connection under delay and jitter, overtaking between connections, the same from the same seed", NetTests.LoopbackOrder);
run.Test("Handshake: protocol, formats, identity, definitions and modified builds", NetTests.Handshake);
run.Test("Room: host, seats, refusals, start conditions, and the server chooses the seeds", NetTests.Room);
run.Test("Garbage: what does not decode or does not fit is counted and dropped on both ends", NetTests.GarbageIsCounted);
run.Test("Relay, client-paced: a tick closes when the one client asks, an order is in the bundle of the tick it was sent for", NetTests.ClientPaced);
run.Test("Relay, server-paced: ticks close by the injected clock at tick rate times speed, and wait for nobody", NetTests.ServerPaced);
run.Test("Relay: the player is stamped from the connection, intents beyond the limit wait, a flood is dropped", NetTests.StampAndLimits);
run.Test("Relay: equal hash reports agree on a tick, different ones are a desync sent to everybody", NetTests.HashReports);
run.Test("Match of one client and one computer player over the relay equals the session stepped directly (no added delay)", NetTests.OneClientEqualsDirect);
run.Test("Match of two and three clients with a computer player: session hashes equal in every tick, with and without delay and jitter, the recording replays", NetTests.SeveralClients);
run.Test("A client whose world differs is found: at loading, before the first tick", NetTests.DifferingWorldIsFound);
run.Test("Local relay: the room of a local game, and a saved state whose hash does not match is refused", NetTests.LocalRelayRoom);
run.Test("UDP layout: every datagram kind survives its bytes; wrong magic, version, kind, length, padding and sections are refused", UdpTests.Layout);
run.Test("UDP handshake: requests create no state and are answered in their own size; only a returned cookie connects; repeat, replace, expiry, full, timeout", UdpTests.Handshake);
run.Test("UDP reliable channel: every message once and in order under delay, jitter, loss, duplication and reordering; the same from the same seed", UdpTests.ReliableChannel);
run.Test("UDP urgent channel: a lost datagram costs no retransmission time; once and in order under loss; fragments; beside the reliable channel", UdpTests.UrgentChannel);
run.Test("UDP time limits: keep-alive, measured round trip, timeout of a cut line, clean close", UdpTests.TimeLimits);
run.Test("UDP garbage: noise, damaged datagrams, a lying length prefix and a wrong token are counted and change nothing", UdpTests.Garbage);
run.Test("UDP on real sockets of the loopback interface with the real clock, bounded to ten seconds", UdpTests.RealSockets);
run.Test("Real clock: monotonic in micro- and milliseconds; the fine timer", UdpTests.RealClock);
run.Test("Clock synchronisation: the median of ping samples gives the server's game time; a lopsided exchange moves nothing; a change of speed empties the window", NetTests.ClockSynchronisation);
run.Test("Playback control: the delay follows way, jitter and loss in milliseconds; a skewed clock and a worsening line are followed by rate, never by a jump", NetTests.PlaybackControlFollowsTheServer);
run.Test("Members that fall behind: shown from 500 ms, the game slowed above 2 s, dropped after 10 s slowed; the leave is a command of the next bundle; a watcher slows nobody", NetTests.MembersThatFallBehind);
run.Test("Leaving: by choice an ally inherits, a lost connection kills; a leave is never a client's intent; ping and pong", NetTests.LeavingAndPings);
run.Test("Match over lossy UDP: bundles arrive in any order and at any time, the session hashes stay equal in every tick, the recording replays", NetTests.MatchOverLossyUdp);
run.Test("Match with members that leave, lose their line, freeze or are too slow: the others stay in step and are slowed no longer than the rule says", NetTests.MatchWithMembersThatVanish);
run.Test("Rooms of one server: a connection belongs to the room its hello names; full server, no hello, two matches at once, an empty room is removed", NetTests.RoomsOfOneServer);
run.Test("Match over real sockets: server endpoint, directory and three clients on the loopback interface by the real clock, bounded to twenty seconds", NetTests.MatchOverRealSockets);
run.Test("Dedicated server: references the protocol and the room only; no game data, no game layer, no renderer", NetTests.DedicatedServerReferences);
run.Test("Rejoin: a lost client takes its held seat again with the state of a member and plays on in step; too late, the seat is given up; no seat held unless told", NetTests.ReconnectAndTakeTheSeatAgain);
run.Test("Rejoin: a damaged state is turned away by the joiner and the relay; a state with another hash than the agreed one goes nowhere; a made-up token opens no seat", NetTests.DamagedSnapshotIsTurnedAway);
run.Test("Watchers: sent the bundles on a delay, a late one plays the match from its start, their orders are discarded, a watcher that diverged is found", NetTests.WatchersOnADelayAndLateOnes);
run.Test("Desync diagnosis: the relay names the tick and the part, the packages name the object and the field; the hash interval against the time to notice", NetTests.DesyncIsNamedDownToTheField);
run.Test("Recording as a replay file; the rules of the pace; the recovery time of the lag rule; a close after the end", NetTests.RecordingAsAFileAndTheRulesOfThePace);
run.Test("Sealed transport: nothing readable on the line, a number per datagram, repetitions and forgeries refused, a changed or unknown server key refused, the key file", UdpTests.SealedTransport);
run.Test("Transport limits: handshakes per address and overall, connections per address, a ban, bytes per connection, the backlog of a peer that does not acknowledge", UdpTests.TransportLimits);
run.Test("Relay limits: a flood of messages, a silent connection, a member that never loads or never reports a hash, match length, state size, joins per minute, the recording, an idle lobby", NetTests.RelayLimits);
run.Test("Room password and kick: the opener sets a verifier, everybody else answers a challenge; the host removes a member, which does not come back from its address", NetTests.PasswordAndKick);
run.Test("Hostile members: garbage commands are left out by every client alike, system commands never pass the relay; a flood, withheld hashes and a kick leave the others in step", NetTests.HostileMembers);
run.Test("Secrets stay off the wire: no rejoin token, name, room, password, verifier or chat is readable in any datagram of a room on the sealed transport", NetTests.SecretsStayOffTheWire);
run.Test("Fuzz, messages: changed and random messages never throw, a refusal names its reason, what decodes encodes again to the same bytes", NetTests.FuzzMessages);
run.Test("Fuzz, transport: changed datagrams never throw in the format, a link or a server endpoint, open no connection, deliver nothing, and the real client is still heard", NetTests.FuzzTransport);
run.Test("Fuzz, relay: changed messages of a member, a stranger and a player never throw in the room and leave the match of the honest clients in step; a client survives a hostile server", NetTests.FuzzRelay);
run.Test("Fuzz, commands: real orders with changed bytes are refused by the wire, rejected by the simulation or simulated - no exception in the pre-check, the submission or the ticks after", NetTests.FuzzCommands);
run.Test("Fuzz, states: a damaged or rewritten session state is refused in the words a client understands, never with another exception", NetTests.FuzzStates);
run.Test("Layer rules: project references, no game data in the server, no clock but the injected one", NetTests.LayerRules);
return run.Finish();

sealed class Runner
{
    private int _failed;
    private int _passed;

    /// <summary>--only TEXT: run the tests whose name contains the text (a focused run while working; the tiers run everything).</summary>
    private static readonly string? Only = Array.IndexOf(Environment.GetCommandLineArgs(), "--only") is var at and >= 0 && at + 1 < Environment.GetCommandLineArgs().Length
        ? Environment.GetCommandLineArgs()[at + 1] : null;

    public void Test(string name, Action test)
    {
        if (Only is not null && !name.Contains(Only, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        Console.WriteLine(_failed == 0 ? $"All network tests passed ({_passed})." : $"{_failed} network test(s) failed, {_passed} passed.");
        return _failed == 0 ? 0 : 1;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

static partial class NetTests
{
    private const string Definitions = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly BuildIdentity Released = new("1111111111111111111111111111111111111111", false);

    // ------------------------------------------------------------------------------------------------------- fixtures

    private static RoomSettings TwoSeats(int tickRate = 60, bool withSeed = false)
        => new(tickRate, "Maps/Test/Test.map", "TERRAIN", 10_000, 500, 60, 0,
            [new MatchSlotDescription(MatchSlotKind.Human, "Alpha", StartPosition: 0), new MatchSlotDescription(MatchSlotKind.Human, "Beta", StartPosition: 1),
             new MatchSlotDescription(MatchSlotKind.Computer, "Alpha", StartPosition: 2)], withSeed, 11, 22);

    private sealed class Net
    {
        public ManualNetClock Clock { get; } = new();
        public LoopbackNetwork Network { get; }
        public RelayServer Server { get; }
        public List<LockstepClient> Clients { get; } = [];
        /// <summary>Per client, the first tick it has not reported as completed yet (the tests of the rule for members that fall behind).</summary>
        public Dictionary<int, long> Reported { get; } = [];
        private uint _seed = 77;

        public Net(RelayOptions? options = null, uint networkSeed = 1)
        {
            Network = new LoopbackNetwork(Clock, networkSeed);
            // The clients of this fixture play no ticks: the rule for members that fall behind is off unless a test sets it.
            Server = new RelayServer(Clock, options ?? new RelayOptions(SeedSource: () => ++_seed, LagReportMs: 0, LagThrottleMs: 0));
        }

        public LockstepClient Join(BuildIdentity? identity = null, string definitions = Definitions, LinkQuality toServer = default, LinkQuality toClient = default, bool clocked = false)
        {
            var (client, server) = Network.Connect(toServer, toClient);
            Server.Attach(server);
            var joined = new LockstepClient(client, $"client {Clients.Count + 1}", definitions, identity ?? Released, clock: clocked ? Clock : null);
            Clients.Add(joined);
            return joined;
        }

        public IConnection Raw()
        {
            var (client, server) = Network.Connect();
            Server.Attach(server);
            return client;
        }

        public void Pump(int rounds = 4, int ms = 0)
        {
            for (var round = 0; round < rounds; round++)
            {
                if (ms > 0) Clock.Advance(ms);
                Server.Pump();
                foreach (var client in Clients) client.Pump();
            }
        }

        /// <summary>A room of <paramref name="clients"/> seated clients, started and loaded with equal hashes.</summary>
        public void StartMatch(int clients, RoomSettings? settings = null)
        {
            for (var index = 0; index < clients; index++) Join();
            Pump();
            Clients[0].Configure(settings ?? TwoSeats());
            Pump();
            for (var index = 0; index < clients; index++)
            {
                Clients[index].RequestSlot(index, index == 0 ? "Alpha" : "Beta", startPosition: index);
                Clients[index].SetReady(true);
            }
            Pump();
            Clients[0].RequestStart();
            Pump();
            foreach (var client in Clients) client.ReportLoaded(42);
            Pump();
            Check.That(Server.Phase == RoomPhase.Match && Clients.All(client => client.Phase == ClientPhase.InMatch), $"the match begins (server {Server.Phase}; {Server.LastDiscard}; {Clients[0].LastRefusal?.Text})");
        }
    }

    private static byte[] MoveIntent(int player = 9, uint unit = 5, int x = 100)
        => CommandWire.EncodeIntent((sequence, tick) => new SetMoveTargetCommand(new PlayerId(player), sequence, tick, new EntityId(unit), Fixed.FromInt(x), Fixed.FromInt(50)));

    // ---------------------------------------------------------------------------------------------------------- tests

    public static void BuildIdentityIsRead()
    {
        var identity = BuildIdentity.Current;
        Check.That(identity.Commit == BuildIdentity.UnknownCommit || Regex.IsMatch(identity.Commit, "^[0-9a-f]{40}$"), $"the commit is a full id or unknown ('{identity.Commit}')");
        Check.That(identity.Commit != BuildIdentity.UnknownCommit || identity.Modified, "a build without a commit counts as modified");
        Check.That(BuildIdentity.Parse(identity.Id) == identity && BuildIdentity.Parse("abc+modified") == new BuildIdentity("abc", true) && BuildIdentity.Parse("abc") == new BuildIdentity("abc", false),
            "an identity survives its text");
        Check.That(new BuildIdentity("abc", true).Id != new BuildIdentity("abc", false).Id, "a modified build is another identity than the commit it started from");
    }

    private static NetMessage[] EveryMessage()
    {
        var settings = TwoSeats(withSeed: true);
        var resume = new ResumeState(1234, 0xFEEDFACECAFEBEEF, [(1, 17UL), (2, 3UL)], [1, 2, 3, 4, 5]);
        return
        [
            new HelloMessage(Protocol.Version, "abc+modified", 16, 14, 10, 2, Definitions, "Näme", "a room"),
            new HelloMessage(Protocol.Version, "abc", 17, 15, 10, 2, Definitions, "back", "a room", 0xFEDCBA9876543210, true), new WelcomeMessage(7, false, 0x1122334455667788),
            new DesyncDetailMessage(900, [new HashPart("world.objects", 1), new HashPart("ai.player3", ulong.MaxValue)]), new DesyncDetailMessage(900, []),
            new DesyncReportMessage(900, [new DesyncPart("world.objects", 1, 5), new DesyncPart("world.objects", 2, 6)]), new SnapshotRequestMessage(4),
            new SnapshotOfferMessage(4, new ResumeState(901, 0xAABB, [(1, 12UL), (2, 0UL)], [1, 2, 3, 4, 5])), new SeatStateMessage(3, SeatHold.Held, 300_000),
            new SeatStateMessage(3, SeatHold.GivenUp, 0), new PasswordChallengeMessage("00112233445566778899AABBCCDDEEFF"),
            new PasswordResponseMessage(RoomPassword.Answer(RoomPassword.Verifier("p", "r"), "00112233445566778899AABBCCDDEEFF")), new KickMessage(7),
            new HelloMessage(Protocol.Version, "id", 1, 2, 3, 4, "F", "n", "room", 0, false, RoomPassword.Verifier("secret", "room")),
            new PlayerDroppedMessage(2, 4, DropReason.Kicked), new PlayerDroppedMessage(2, 4, DropReason.Flood), new MatchEndMessage(9, 0, EndReason.ServerLimit),
            new RejectMessage(RejectReason.BadPassword, "no"), new RejectMessage(RejectReason.Kicked, "out"),
            new PingMessage(123_456_789_012, 77), new PongMessage(123_456_789_012, 9_876_543_210, 50), new LagStateMessage(3, 2_500, true), new LeaveMessage(),
            new WelcomeMessage(7, true),
            new RejectMessage(RejectReason.SimulationVersion, "other build"),
            new RoomConfigMessage(settings),
            new SlotRequestMessage(1, "Beta", 2, 3, -1),
            new ReadyMessage(true),
            new RoomStateMessage(RoomPhase.Loading, settings with { HasSeed = false, Seed = 0, AiSeed = 0 }, [new RoomMember(1, "a", 0, true, true), new RoomMember(2, "b", -1, false, false)]),
            new StartRequestMessage(null),
            new StartRequestMessage(resume),
            new MatchStartMessage([9, 8, 7], 2, Pacing.Client, 4, null),
            new MatchStartMessage([9, 8, 7], 0, Pacing.Server, 1, resume),
            new LoadProgressMessage(55),
            new LoadProgressStateMessage(3, 100),
            new LoadedMessage(0x0123456789ABCDEF),
            new AllLoadedMessage(1234),
            new ChatRequestMessage("hello"),
            new ChatMessage(2, "hello"),
            new IntentMessage(99, MoveIntent()),
            new TickRequestMessage(77),
            new TickBundleMessage(77, []),
            new TickBundleMessage(78, [new BundleCommand(1, 5, 2, 99, MoveIntent()), new BundleCommand(2, 1, 3, 1, MoveIntent(x: 7))]),
            new HashReportMessage(77, 0xAAAABBBBCCCCDDDD),
            new PaceRequestMessage(true, 200),
            new PaceStateMessage(false, 100, 2),
            new PlayerDroppedMessage(2, 3, DropReason.ConnectionLost),
            new DesyncMessage(77, [(1u, 1UL), (2u, 2UL)]),
            new EndReportMessage(9000, 1),
            new MatchEndMessage(9000, 1, EndReason.Finished),
        ];
    }

    public static void MessagesRoundTrip()
    {
        var types = new HashSet<MessageType>();
        foreach (var message in EveryMessage())
        {
            var bytes = MessageCodec.Encode(message);
            Check.That(MessageCodec.TryDecode(bytes, out var decoded, out var error), $"{message.Type} decodes ({error})");
            Check.That(decoded!.Type == message.Type && MessageCodec.Encode(decoded).AsSpan().SequenceEqual(bytes), $"{message.Type} survives its bytes");
            types.Add(message.Type);
            var frame = MessageCodec.Frame(bytes);
            Check.That(frame.Length == bytes.Length + 4 && BitConverter.ToUInt32(frame) == bytes.Length, "a frame is the length and the message");
        }
        Check.That(types.SetEquals(Enum.GetValues<MessageType>()), $"every message type of the protocol is covered ({types.Count} of {Enum.GetValues<MessageType>().Length})");
    }

    public static void MalformedMessagesAreRefused()
    {
        Check.That(!MessageCodec.TryDecode([], out _, out var empty) && empty.Length > 0, "an empty message is refused");
        Check.That(!MessageCodec.TryDecode([0xFF, 0x7F, 1, 2, 3], out _, out var unknown) && unknown.Contains("unknown message type", StringComparison.Ordinal), "an unknown type is refused by name");
        var oversized = new byte[Protocol.MaxMessageBytes + 1];
        oversized[0] = (byte)MessageType.Chat;
        Check.That(!MessageCodec.TryDecode(oversized, out _, out var large) && large.Contains("exceeds its limit", StringComparison.Ordinal), "a message over its limit is refused before it is read");
        foreach (var message in EveryMessage())
        {
            var bytes = MessageCodec.Encode(message);
            if (bytes.Length > 2) Check.That(!MessageCodec.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _, out _), $"a truncated {message.Type} is refused");
            Check.That(!MessageCodec.TryDecode([..bytes, 0], out _, out _), $"a {message.Type} with a trailing byte is refused");
        }
        var chat = MessageCodec.Encode(new ChatRequestMessage("ok"));
        chat[2] = 0xFF;
        chat[3] = 0xFF;
        Check.That(!MessageCodec.TryDecode(chat, out _, out _), "a text that claims more bytes than its limit is refused");
        var writer = new WireWriter();
        try
        {
            writer.Text(new string('x', Protocol.MaxChatBytes + 1), Protocol.MaxChatBytes);
            throw new InvalidOperationException("a text over its limit must not be written");
        }
        catch (ProtocolException)
        {
        }
    }

    public static void CommandStamp()
    {
        var units = ImmutableArray.Create(new EntityId(3), new EntityId(4), new EntityId(9));
        SimCommand original = new GroupMoveCommand(new PlayerId(7), 55, 1234, units, Fixed.FromInt(10), Fixed.FromInt(20), true);
        var bytes = ReplayFormat.EncodeCommand(original);
        var decoded = ReplayFormat.DecodeCommand(bytes);
        Check.That(ReplayFormat.EncodeCommand(decoded).AsSpan().SequenceEqual(bytes), "one command survives the simulation's codec");
        var stamped = ReplayFormat.DecodeCommand(CommandWire.Stamp(bytes, 2, 4321));
        Check.That(stamped is GroupMoveCommand move && move.Player == new PlayerId(2) && move.Tick == 4321 && move.Sequence == 0 && move.Units.SequenceEqual(units) && move.Attack,
            "the stamp replaces player and tick, clears the sequence and leaves the rest");
        Check.That(CommandWire.IsPlausible(bytes, out _) && !CommandWire.IsPlausible(bytes.AsSpan(0, 10), out _), "a command shorter than its header is not plausible");
        var unknownType = (byte[])bytes.Clone();
        unknownType[ReplayFormat.CommandTypeOffset] = 0xFF;
        unknownType[ReplayFormat.CommandTypeOffset + 1] = 0xFF;
        Check.That(!CommandWire.IsPlausible(unknownType, out var why) && why.Contains("unknown command type", StringComparison.Ordinal), "a command of an unknown type is not plausible");

        var intent = CommandWire.DecodeIntent(new BundleCommand(2, 1, 1, 1, CommandWire.Stamp(bytes, 2, 4321)));
        Check.That(intent(8, 4321) is GroupMoveCommand submitted && submitted.Sequence == 8 && submitted.Tick == 4321, "a bundle's command gets its sequence number and tick at submission");
        Throws(() => CommandWire.DecodeIntent(new BundleCommand(3, 1, 1, 1, CommandWire.Stamp(bytes, 2, 4321))), "another player than its entry");
        var system = ReplayFormat.EncodeCommand(new GiveMoneyCommand(PlayerId.System, 1, 0, new PlayerId(1), Fixed.FromInt(5)));
        Throws(() => CommandWire.DecodeIntent(new BundleCommand(1, 1, 1, 1, CommandWire.Stamp(system, 1, 5))), "only the system may give");
        Throws(() => CommandWire.DecodeIntent(new BundleCommand(1, 1, 1, 1, [..CommandWire.Stamp(bytes, 1, 5), 0])), "does not decode");
    }

    private static void Throws(Action action, string part)
    {
        try
        {
            action();
        }
        catch (ProtocolException exception) when (exception.Message.Contains(part, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"expected a protocol violation naming '{part}'");
    }

    public static void LoopbackOrder()
    {
        List<(int Connection, int Number, long At)> Deliver(uint seed)
        {
            var clock = new ManualNetClock();
            var network = new LoopbackNetwork(clock, seed);
            var links = Enumerable.Range(0, 3).Select(_ => network.Connect(new LinkQuality(20, 60))).ToArray();
            var arrived = new List<(int, int, long)>();
            for (var step = 0; step < 400; step++)
            {
                if (step < 100)
                    for (var link = 0; link < links.Length; link++) links[link].Client.Send([(byte)link, (byte)step]);
                clock.Advance(1);
                for (var link = 0; link < links.Length; link++)
                    while (links[link].Server.TryReceive(out var message)) arrived.Add((message[0], message[1], clock.Milliseconds));
            }
            return arrived;
        }
        var first = Deliver(5);
        Check.That(first.Count == 300, $"every message arrives ({first.Count} of 300)");
        for (var link = 0; link < 3; link++)
            Check.That(first.Where(entry => entry.Connection == link).Select(entry => entry.Number).SequenceEqual(Enumerable.Range(0, 100)), $"connection {link} delivers in the order sent");
        var overtakes = 0;
        for (var index = 1; index < first.Count; index++)
            if (first[index].Number < first[index - 1].Number && first[index].Connection != first[index - 1].Connection) overtakes++;
        Check.That(overtakes > 10, $"between connections messages overtake each other ({overtakes} times)");
        Check.That(first.All(entry => entry.At >= 20), "nothing arrives before its delay");
        Check.That(Deliver(5).SequenceEqual(first) && !Deliver(6).SequenceEqual(first), "the same seed gives the same network, another seed another one");

        var instantClock = new ManualNetClock();
        var (a, b) = new LoopbackNetwork(instantClock).Connect();
        a.Send([1]);
        Check.That(b.TryReceive(out var now) && now[0] == 1, "without delay a message is there as soon as it is sent");
        a.Close();
        Check.That(!b.IsOpen && !a.IsOpen, "closing one end closes the connection");
    }

    public static void Handshake()
    {
        var net = new Net();
        var first = net.Join();
        net.Pump();
        Check.That(first.Phase == ClientPhase.InRoom && first.IsHost && first.ConnectionId == 1 && first.Room is { Phase: RoomPhase.Room }, "the first client is admitted as host");
        var same = net.Join();
        var otherBuild = net.Join(new BuildIdentity("2222222222222222222222222222222222222222", false));
        var otherData = net.Join(definitions: new string('F', 64));
        var modified = net.Join(Released with { Modified = true });
        net.Pump();
        Check.That(same.Phase == ClientPhase.InRoom && !same.IsHost, "a second client of the same build and data is admitted");
        Check.That(otherBuild.Phase == ClientPhase.Rejected && otherBuild.Rejection!.Reason == RejectReason.SimulationVersion, "another simulation build is rejected by reason");
        Check.That(otherData.Phase == ClientPhase.Rejected && otherData.Rejection!.Reason == RejectReason.Definitions, "other game definitions are rejected by reason");
        Check.That(modified.Phase == ClientPhase.Rejected && modified.Rejection!.Reason == RejectReason.ModifiedBuild, "a modified build is rejected by a server that does not allow it");
        Check.That(net.Server.Connections == 2, "a rejected connection is closed and gone");

        var raw = net.Raw();
        raw.Send(MessageCodec.Encode(new HelloMessage(Protocol.Version + 1, Released.Id, CommandTypes.Version, ReplayFormat.CurrentVersion, 10, MatchDescription.EncodingVersion, Definitions, "old")));
        net.Server.Pump();
        Check.That(raw.TryReceive(out var answer) && MessageCodec.TryDecode(answer, out var reject, out _) && reject is RejectMessage { Reason: RejectReason.ProtocolVersion }, "another protocol version is rejected");
        var formats = net.Raw();
        formats.Send(MessageCodec.Encode(new HelloMessage(Protocol.Version, Released.Id, CommandTypes.Version + 1, ReplayFormat.CurrentVersion, 10, MatchDescription.EncodingVersion, Definitions, "new")));
        net.Server.Pump();
        Check.That(formats.TryReceive(out answer) && MessageCodec.TryDecode(answer, out reject, out _) && reject is RejectMessage { Reason: RejectReason.FormatVersion }, "another command table is rejected");

        var open = new Net(new RelayOptions(AllowModifiedBuilds: true, HostMayChooseSeeds: true));
        var development = open.Join(Released with { Modified = true });
        open.Pump();
        Check.That(development.Phase == ClientPhase.InRoom, "a server that allows it admits a development build");
    }

    public static void Room()
    {
        var net = new Net();
        var host = net.Join();
        var guest = net.Join();
        var watcher = net.Join();
        net.Pump();
        guest.Configure(TwoSeats());
        net.Pump();
        Check.That(guest.LastRefusal is { Reason: RejectReason.NotHost } && net.Server.Settings.Slots.IsEmpty, "only the host sets the room");
        host.Configure(TwoSeats(withSeed: true));
        net.Pump();
        Check.That(host.LastRefusal is { Reason: RejectReason.InvalidRequest } && net.Server.Settings.Slots.IsEmpty, "a host may not state the seeds on a server that chooses them");
        host.Configure(TwoSeats() with { TickRate = 50 });
        net.Pump();
        Check.That(net.Server.Settings.Slots.IsEmpty, "a tick rate the simulation does not support is refused");
        host.Configure(TwoSeats());
        net.Pump();
        Check.That(guest.Room!.Settings.Slots.Length == 3 && !guest.Room.Settings.HasSeed, "everybody sees the room the host set");

        host.RequestSlot(0, "Alpha", startPosition: 0);
        guest.RequestSlot(0, "Beta");
        net.Pump();
        Check.That(guest.LastRefusal!.Text.Contains("taken", StringComparison.Ordinal) && host.Room!.Members.Single(member => member.Slot == 0).ConnectionId == host.ConnectionId, "a taken seat is refused");
        guest.RequestSlot(2);
        net.Pump();
        Check.That(guest.LastRefusal!.Text.Contains("not a seat", StringComparison.Ordinal), "a computer slot is no seat");
        guest.RequestSlot(1, "Beta", team: 2, color: 4, startPosition: 0);
        net.Pump();
        Check.That(guest.LastRefusal!.Text.Contains("start position", StringComparison.Ordinal), "a start position another slot names is refused");
        host.RequestStart();
        net.Pump();
        Check.That(host.LastRefusal is { Reason: RejectReason.NotReady } && net.Server.Phase == RoomPhase.Room, "a match does not start while a seat waits for a player");
        guest.RequestSlot(1, "Beta", team: 2, color: 4, startPosition: 1);
        net.Pump();
        Check.That(host.Room!.Settings.Slots[1] is { Faction: "Beta", Team: 2, Color: 4, StartPosition: 1 }, "a seated player brings faction, team, colour and start position into the room");
        host.RequestStart();
        net.Pump();
        Check.That(host.LastRefusal!.Text.Contains("ready", StringComparison.Ordinal), "a match does not start while a seated player is not ready");
        guest.RequestStart();
        net.Pump();
        Check.That(guest.LastRefusal is { Reason: RejectReason.NotHost }, "only the host starts the match");
        guest.SetReady(true);
        host.SendChat("gl hf");
        net.Pump();
        host.RequestStart();
        net.Pump();
        Check.That(net.Clients.All(client => client.Phase == ClientPhase.Loading), "the match starts for everybody, the watcher included");
        Check.That(host.Player == 1 && guest.Player == 2 && watcher.Player == 0 && host.Pacing == Pacing.Server, "seated clients get their player number in slot order, a watcher none; three clients are server-paced");
        Check.That(host.Description!.Equals(guest.Description) && host.Description.Equals(watcher.Description), "everybody is handed the same description");
        var description = host.Description;
        Check.That(description.SimulationId == Released.Id && description.DefinitionFingerprint == Definitions && description.Seed == 78 && description.AiSeed == 79,
            "the description carries the room's identity and definitions and the seeds the server chose");
        Check.That(description.Slots[1] is { Kind: MatchSlotKind.Human, Faction: "Beta", Team: 2, Color: 4, StartPosition: 1 } && description.Slots[2].Kind == MatchSlotKind.Computer, "the slots travel into the description");
        Check.That(watcher.Chat.Count == 1 && watcher.Chat[0].Text == "gl hf", "chat reaches everybody");
        var late = net.Join();
        net.Pump();
        Check.That(late.Rejection is { Reason: RejectReason.MatchRunning }, "nobody joins a match that has started (rejoin is a later phase)");

        host.ReportLoadProgress(50);
        host.ReportLoaded(1);
        guest.ReportLoaded(1);
        net.Pump();
        Check.That(net.Server.Phase == RoomPhase.Loading && watcher.LoadProgress[host.ConnectionId] == 50, "the match waits for everybody to load and shows the progress");
        watcher.ReportLoaded(1);
        net.Pump();
        Check.That(net.Server.Phase == RoomPhase.Match && net.Clients.Take(3).All(client => client.Phase == ClientPhase.InMatch), "when all have loaded with the same hash the match begins");

        guest.Close();
        net.Pump();
        Check.That(host.Dropped.Count == 1 && host.Dropped[0].Player == 2 && net.Server.Connections == 2, "a player that leaves is announced with its player number");
        host.Close();
        watcher.Close();
        net.Pump();
        Check.That(net.Server.Phase == RoomPhase.Ended && net.Server.End is { Reason: EndReason.Abandoned }, "a match nobody is left in is abandoned");
    }

    public static void GarbageIsCounted()
    {
        var net = new Net();
        var raw = net.Raw();
        raw.Send([1, 2, 3]);
        raw.Send(MessageCodec.Encode(new ReadyMessage(true)));
        raw.Send([0xFF, 0xFF]);
        net.Server.Pump();
        Check.That(net.Server.Discarded == 3 && net.Server.Connections == 1, $"a malformed hello, a message before the hello and an unknown type are counted ({net.Server.Discarded}); the connection is not admitted");
        raw.Send(MessageCodec.Encode(new HelloMessage(Protocol.Version, Released.Id, CommandTypes.Version, ReplayFormat.CurrentVersion, 10, MatchDescription.EncodingVersion, Definitions, "x")));
        raw.Send(MessageCodec.Encode(new TickRequestMessage(0)));
        raw.Send(MessageCodec.Encode(new IntentMessage(1, MoveIntent())));
        raw.Send(new byte[Protocol.MaxMessageBytes + 5]);
        net.Server.Pump();
        Check.That(net.Server.Discarded == 6 && net.Server.Phase == RoomPhase.Room && net.Server.NextTick == 0, "messages that do not fit the phase and an oversized one are counted and change nothing");
        Check.That(net.Server.LastDiscard.Length > 0, "the last reason is kept for the report");

        var (clientEnd, serverEnd) = net.Network.Connect();
        var client = new LockstepClient(clientEnd, "c", Definitions, Released);
        serverEnd.Send([9, 9, 9]);
        serverEnd.Send(MessageCodec.Encode(new TickBundleMessage(0, [])));
        serverEnd.Send(MessageCodec.Encode(new AllLoadedMessage(0)));
        client.Pump();
        Check.That(client.Discarded == 3 && client.Phase == ClientPhase.Connecting && !client.HasBundle(0), "the client counts and drops what it cannot use, too");
    }

    public static void ClientPaced()
    {
        var net = new Net();
        var client = net.Join();
        net.Pump();
        client.Configure(new RoomSettings(60, "m", "t", 1_000, 0, 0, 0, [new MatchSlotDescription(MatchSlotKind.Human, "Alpha"), new MatchSlotDescription(MatchSlotKind.Computer, "Beta")]));
        client.RequestSlot(0, "Alpha");
        net.Pump();
        client.RequestStart();
        net.Pump();
        client.ReportLoaded(5);
        net.Pump();
        Check.That(net.Server.Pacing == Pacing.Client && client.Pacing == Pacing.Client && client.Phase == ClientPhase.InMatch, "a room with one client is client-paced");
        net.Clock.Advance(10_000);
        net.Pump();
        Check.That(net.Server.NextTick == 0 && !client.HasBundle(0), "the clock closes nothing in a client-paced room");
        client.SendIntent(41, MoveIntent());
        client.RequestTick(0);
        net.Pump(1);
        Check.That(client.HasBundle(0) && net.Server.NextTick == 1, "the tick closes when the client asks");
        var bundle = client.TakeBundle(0);
        Check.That(bundle.Commands.Length == 1 && bundle.Commands[0] is { Player: 1, Order: 1, Receipt: 41 } && bundle.Commands[0].Origin == client.ConnectionId,
            "an order sent before the request is in the bundle of that tick, with its receipt and the order number 1");
        client.RequestTick(5);
        client.RequestTick(1);
        net.Pump(1);
        Check.That(net.Server.Discarded == 1 && client.HasBundle(1) && client.TakeBundle(1).Commands.IsEmpty && !client.HasBundle(5), "a request for another tick than the next is dropped; an empty tick has an empty bundle");
        client.ReportEnd(2, 1);
        net.Pump();
        Check.That(net.Server.Phase == RoomPhase.Ended && client.End is { Tick: 2, Winner: 1, Reason: EndReason.Finished }, "the end the one client reports ends the match");
    }

    public static void ServerPaced()
    {
        foreach (var rate in new[] { 30, 60, 120 })
        {
            var net = new Net();
            net.StartMatch(2, TwoSeats(rate));
            var start = net.Server.NextTick;
            net.Pump(100, 10);
            Check.That(Math.Abs(net.Server.NextTick - start - rate) <= 1, $"{rate} Hz: one second of the server's clock closes {rate} ticks ({net.Server.NextTick - start})");
            Check.That(net.Clients.All(client => client.NewestBundleTick == net.Server.NextTick - 1), "every client has every bundle; nobody was waited for");
            var before = net.Server.NextTick;
            net.Clients[1].RequestPace(true, 100);
            net.Pump(1);
            net.Pump(50, 10);
            Check.That(net.Server.Paused && net.Clients[0].Paused && net.Server.NextTick == before, "a pause is granted by the server and told to everybody; a paused server closes nothing");
            net.Clients[0].RequestPace(false, 200);
            net.Pump(1);
            before = net.Server.NextTick;
            net.Pump(100, 10);
            Check.That(Math.Abs(net.Server.NextTick - before - 2 * rate) <= 1 && net.Clients[1].SpeedPercent == 200, $"{rate} Hz: at double speed one second closes {2 * rate} ticks");
            net.Clients[0].RequestPace(false, 10_000);
            net.Pump(1);
            Check.That(net.Server.SpeedPercent == Protocol.MaxSpeedPercent, "a speed is clamped by the client to the protocol's range");
            net.Clients[0].RequestTick(net.Server.NextTick);
            net.Pump(1);
            Check.That(net.Server.Discarded == 1, "a tick request means nothing in a server-paced room");
        }
    }

    public static void StampAndLimits()
    {
        var net = new Net();
        net.StartMatch(2);
        var (first, second) = (net.Clients[0], net.Clients[1]);
        // The second client claims to be player 1 and the system: the server writes the player of the connection.
        second.SendIntent(1, MoveIntent(player: 1));
        second.SendIntent(2, MoveIntent(player: 0));
        first.SendIntent(1, MoveIntent(player: 2));
        net.Pump(1);
        net.Pump(4, 20);
        var commands = new List<(long Tick, BundleCommand Command)>();
        void Collect(LockstepClient client)
        {
            for (var tick = 0L; tick <= client.NewestBundleTick; tick++)
                if (client.HasBundle(tick))
                    foreach (var command in client.TakeBundle(tick).Commands) commands.Add((tick, command));
        }
        Collect(first);
        Check.That(commands.Count == 3 && commands.Select(entry => entry.Command.Player).SequenceEqual([1, 2, 2]), "a bundle lists its commands by player; each carries the player of its connection");
        Check.That(commands.All(entry => ReplayFormat.DecodeCommand(entry.Command.Command) is { } decoded && decoded.Player.Value == entry.Command.Player && decoded.Tick == entry.Tick),
            "the stamp is in the command bytes: nobody can give a command for somebody else");
        Check.That(commands.Where(entry => entry.Command.Player == 2).Select(entry => entry.Command.Order).SequenceEqual([1UL, 2UL]), "the order numbers of a player count from 1");

        commands.Clear();
        for (var index = 0; index < 100; index++) first.SendIntent((ulong)(100 + index), MoveIntent(x: index));
        net.Pump(1);
        net.Pump(6, 20);
        Collect(first);
        var mine = commands.Where(entry => entry.Command.Player == 1).ToList();
        Check.That(mine.Count == 100 && mine.GroupBy(entry => entry.Tick).Select(group => group.Count()).SequenceEqual([Protocol.MaxIntentsPerTick, 100 - Protocol.MaxIntentsPerTick]),
            $"at most {Protocol.MaxIntentsPerTick} intents of a player go into one tick; the rest wait for the next");
        Check.That(mine.Select(entry => entry.Command.Receipt).SequenceEqual(Enumerable.Range(100, 100).Select(value => (ulong)value)) && net.Server.IntentsDeferred > 0, "they keep their order");

        for (var index = 0; index < 1_000; index++) first.SendIntent((ulong)(1_000 + index), MoveIntent());
        net.Pump(1);
        Check.That(net.Server.IntentsDropped == 1_000 - Protocol.MaxIntentsPerTick * 4, $"a flood beyond the queue of a player is dropped and counted ({net.Server.IntentsDropped})");
        var raw = MoveIntent();
        raw[ReplayFormat.CommandTypeOffset] = 0xFF;
        raw[ReplayFormat.CommandTypeOffset + 1] = 0xFF;
        var discarded = net.Server.Discarded;
        second.SendIntent(7, raw);
        net.Pump(1);
        Check.That(net.Server.Discarded == discarded + 1, "an intent whose command type the protocol does not know is dropped");
    }

    public static void HashReports()
    {
        var net = new Net();
        net.StartMatch(2);
        net.Pump(10, 20);
        var (first, second) = (net.Clients[0], net.Clients[1]);
        first.ReportHash(0, 100);
        net.Pump(1);
        Check.That(net.Server.LastAgreedTick == -1, "one report alone agrees on nothing");
        second.ReportHash(0, 100);
        first.ReportHash(1, 101);
        second.ReportHash(1, 101);
        net.Pump(1);
        Check.That(net.Server.LastAgreedTick == 1 && net.Server.Phase == RoomPhase.Match, "equal reports of all clients agree on the tick");
        first.ReportHash(net.Server.NextTick + 50, 1);
        net.Pump(1);
        Check.That(net.Server.Discarded == 1, "a report for a tick the server has not closed is dropped");
        first.ReportHash(2, 102);
        second.ReportHash(2, 999);
        net.Pump(2);
        Check.That(net.Server.Phase == RoomPhase.Ended && net.Server.Desync is { Tick: 2 }, "two different hashes for one tick are a desync");
        Check.That(first.Desync is { Tick: 2 } desync && desync.Reports.Length == 2 && desync.Reports.Any(report => report.SessionHash == 999) && second.End is { Reason: EndReason.Desync },
            "the desync is sent to everybody with every reported hash, and the match ends");
    }

    // --------------------------------------------------------------------------------------- matches on the synthetic fixture

    private sealed record Fixture(SyntheticFixture Synthetic, TerrainMap Terrain, CnCNew.Data.World.MapData Map, string[] Factions)
    {
        public static Fixture Create()
        {
            var synthetic = new SyntheticFixture(TestRate.Default, files: SkirmishFixtureFiles.Files());
            var map = FlatMap.Build(70, 50, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 560m, 200m, 0m), ("Player_3_Start", 330m, 420m, 0m)]);
            return new Fixture(synthetic, TerrainMap.Build(map, "net"), map, [..ReferenceMatchScenario.Participants(synthetic.Templates, out _).Select(participant => participant.Faction.Name)]);
        }

        public RoomSettings Settings(int humans, int computers, long frames)
        {
            var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>();
            for (var index = 0; index < humans + computers; index++)
                slots.Add(new MatchSlotDescription(index < humans ? MatchSlotKind.Human : MatchSlotKind.Computer, Factions[index % Factions.Length], StartPosition: index));
            return new RoomSettings(TestRate.Default.TicksPerSecond, "Maps/Net/Net.map", Terrain.Fingerprint, TestRate.Frames(frames), 500, 60, 0, slots.ToImmutable());
        }

        public LoopbackProbe Probe() => new(Synthetic.Templates, Synthetic.Fingerprint, Terrain, Map.Objects);
    }

    public static void OneClientEqualsDirect()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(1, 1, 1_500);
        var ticks = settings.MaxTicks;
        var result = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks));
        Check.That(result.Ok, $"the match of one client runs: {result.Problem}");
        Check.That(result.Pacing == Pacing.Client && result.Stalls == 0 && result.BundleCommands > 20, $"client-paced, no tick ever waited, the generator's orders went through the relay ({result.BundleCommands})");
        Check.That(result.MaxIntentLatencyTicks == 0, $"every order is executed in the tick it was given (largest distance {result.MaxIntentLatencyTicks} ticks)");
        Check.That(result.ReplayedSessionHash == result.FinalSessionHash && result.ReplayedHumanCommands == result.BundleCommands, "the server's recording played as a replay ends in the clients' session hash");
        var again = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks));
        Check.That(again.Ok && again.FinalSessionHash == result.FinalSessionHash && again.BundleCommands == result.BundleCommands, "the same match again is the same match");
    }

    public static void SeveralClients()
    {
        var fixture = Fixture.Create();
        ulong? reference = null;
        foreach (var (humans, computers) in new[] { (2, 1), (3, 0) })
        {
            var settings = fixture.Settings(humans, computers, 1_200);
            var ticks = settings.MaxTicks;
            var plain = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks));
            Check.That(plain.Ok, $"{humans}+{computers} without delay: {plain.Problem}");
            Check.That(plain.Pacing == Pacing.Server && plain.Clients == humans && plain.LastAgreedTick == ticks - 1 && plain.BundleCommands > 20,
                $"{humans}+{computers}: server-paced, the server agreed on every tick ({plain.LastAgreedTick} of {ticks - 1}), {plain.BundleCommands} commands relayed");
            Check.That(plain.ReplayedSessionHash == plain.FinalSessionHash, "the recording replays into the same session hash");
            var repeated = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks));
            Check.That(repeated.Ok && repeated.FinalSessionHash == plain.FinalSessionHash, $"{humans}+{computers}: the same run twice ends in the same hash");

            // Delay and jitter: bundles reach the clients at different times; a tick still runs only with its bundle, so the hashes stay equal.
            var jitter = new LoopbackProbeOptions(ticks, new LinkQuality(30, 40), new LinkQuality(25, 60), NetworkSeed: 9);
            var delayed = fixture.Probe().Run(settings, jitter);
            Check.That(delayed.Ok, $"{humans}+{computers} with delay and jitter: {delayed.Problem}");
            Check.That(delayed.MaxIntentLatencyTicks >= 3 && delayed.ReplayedSessionHash == delayed.FinalSessionHash, $"orders arrive ticks later (up to {delayed.MaxIntentLatencyTicks}) and the match still replays");
            var delayedAgain = fixture.Probe().Run(settings, jitter);
            Check.That(delayedAgain.Ok && delayedAgain.FinalSessionHash == delayed.FinalSessionHash, "the same network seed gives the same match");
            reference ??= plain.FinalSessionHash;
        }
    }

    public static void DifferingWorldIsFound()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(2, 1, 300);
        var probe = fixture.Probe();
        probe.AfterStart = sessions =>
        {
            // One client's world gets a credit the others do not have.
            var world = sessions[1].World;
            world.Submit(new GiveMoneyCommand(PlayerId.System, 1_000_000, world.Tick, new PlayerId(1), Fixed.FromInt(1)));
            world.Step();
        };
        var result = probe.Run(settings, new LoopbackProbeOptions(settings.MaxTicks));
        Check.That(!result.Ok && result.Problem.Contains("before it began", StringComparison.Ordinal), $"a client that built another world is found when it reports its hash after loading ({result.Problem})");
    }

    public static void LocalRelayRoom()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(1, 1, 600) with { HasSeed = true, Seed = 0xABCD, AiSeed = 0x1234 };
        var relay = LocalRelay.Open(fixture.Synthetic.Fingerprint);
        relay.StartMatch(settings, 0);
        var description = relay.Client.Description!;
        Check.That(description.Seed == 0xABCD && description.AiSeed == 0x1234 && description.SimulationId == BuildIdentity.Current.Id && relay.Client.Player == 1 && relay.Client.Pacing == Pacing.Client,
            "the local server takes the seeds of its host, states its build and seats the one client");
        var session = SkirmishMatchSession.Start(description, fixture.Synthetic.Templates, fixture.Terrain, fixture.Map.Objects, 0, fixture.Synthetic.Fingerprint, BuildIdentity.Current.Id);
        relay.ReportLoaded(session.StateHash());
        Check.That(relay.Client.Phase == ClientPhase.InMatch, "the match begins when the client has reported its hash");

        for (var index = 0; index < 200; index++) session.Step();
        var state = session.Save();
        var watching = LocalRelay.Open(fixture.Synthetic.Fingerprint);
        watching.StartMatch(session.Description.ToRoomSettings(withSeed: true), 0, new ResumeState(session.World.Tick, session.StateHash(), [..session.HumanCommandCounts], state));
        Check.That(watching.Client.Player == 1 && watching.Client.StartTick == 200 && watching.Client.Start!.Resume!.State.AsSpan().SequenceEqual(state), "a room started from a saved state hands the state on and starts at its tick");
        var spectated = LocalRelay.Open(fixture.Synthetic.Fingerprint);
        spectated.StartMatch(fixture.Settings(0, 2, 600) with { HasSeed = true, Seed = 1, AiSeed = 2 }, -1);
        Check.That(spectated.Client.Player == 0 && spectated.Client.Pacing == Pacing.Client, "a viewer without a seat watches a room of computer players, and paces it");
        try
        {
            watching.ReportLoaded(session.StateHash() + 1);
            throw new InvalidOperationException("a state that does not hash to the saved hash must be refused");
        }
        catch (LocalRelayException exception)
        {
            Check.That(exception.Message.Contains("did not match", StringComparison.Ordinal), "the refusal says why");
        }
        try
        {
            LocalRelay.Open(fixture.Synthetic.Fingerprint).StartMatch(settings with { TickRate = 55 }, 0);
            throw new InvalidOperationException("an invalid room must be refused");
        }
        catch (LocalRelayException)
        {
        }
    }

    // ------------------------------------------------------------------------------------------------------ layer rules

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("The repository root (CnCNew.slnx) was not found above the test's directory; run the tests inside the tree.");
    }

    private static string[] ReferencesOf(string root, string project)
        => [..Regex.Matches(File.ReadAllText(Path.Combine(root, "src", project, project + ".csproj")), "<ProjectReference Include=\"[^\"]*?([A-Za-z0-9.]+)\\.csproj\"").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

    private static IEnumerable<(string File, int Line, string Text)> CodeLines(string root, string project)
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var trimmed = lines[index].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*')) continue;
                yield return (relative, index + 1, lines[index]);
            }
        }
    }

    public static void LayerRules()
    {
        var root = RepositoryRoot();
        Check.That(ReferencesOf(root, "CnCNew.Net").SequenceEqual(["CnCNew.Simulation"]), $"CnCNew.Net references CnCNew.Simulation only ({string.Join(", ", ReferencesOf(root, "CnCNew.Net"))})");
        Check.That(ReferencesOf(root, "CnCNew.Server").SequenceEqual(["CnCNew.Net"]), $"CnCNew.Server references CnCNew.Net only ({string.Join(", ", ReferencesOf(root, "CnCNew.Server"))})");
        Check.That(ReferencesOf(root, "CnCNew.Simulation").SequenceEqual(["CnCNew.Data"]), "CnCNew.Simulation still references CnCNew.Data only");
        var game = ReferencesOf(root, "CnCNew.Game");
        Check.That(game.Contains("CnCNew.Net") && game.Contains("CnCNew.Server"), "CnCNew.Game embeds the server");
        foreach (var lower in new[] { "CnCNew.Simulation", "CnCNew.Data", "CnCNew.Presentation", "CnCNew.Content" })
            Check.That(!ReferencesOf(root, lower).Any(reference => reference is "CnCNew.Net" or "CnCNew.Server" or "CnCNew.Game"), $"{lower} does not reference the network layers");

        var problems = new List<string>();
        // The server never loads game data and does not simulate: of the simulation it may name the match description's namespace and nothing else.
        var serverForbidden = new Regex(@"using CnCNew\.(?!Net;|Simulation\.Skirmish;)|\b(TemplateCatalog|SimulationWorld|SkirmishMatchSession|SkirmishAi|TerrainMap|ContentFileSystem|CompiledDefinitionGraph|WorldSnapshot)\b|System\.IO\.|\bFile\.|\bDirectory\.");
        foreach (var (file, line, text) in CodeLines(root, "CnCNew.Server"))
            if (serverForbidden.IsMatch(text)) problems.Add($"{file}({line}): the server names game data or the simulation: {text.Trim()}");
        // Wall-clock time is read through the injected INetClock only.
        var clocks = new Regex(@"\b(DateTime|DateTimeOffset|Stopwatch|TimeProvider|Timer|Thread|Task)\b|Environment\.TickCount|\.Sleep\(|\.Delay\(");
        var lockstep = new Regex(@"System\.Random\b|new Random\(|Random\.Shared|LogicRandom|\.Streams\b|\bSageRandom\b");
        foreach (var project in new[] { "CnCNew.Net", "CnCNew.Server" })
            foreach (var (file, line, text) in CodeLines(root, project))
            {
                // The one file that reads the real clock and asks the operating system for a finer timer.
                if (file.EndsWith("CnCNew.Net/RealTime.cs", StringComparison.Ordinal)) continue;
                if (clocks.IsMatch(text)) problems.Add($"{file}({line}): a clock, a timer or a thread of its own: {text.Trim()}");
                if (lockstep.IsMatch(text)) problems.Add($"{file}({line}): a random source that is not the layer's own: {text.Trim()}");
            }
        // One match path: the client starts and steps no session itself, and in the game layer only LiveMatch (behind the relay) and the lockstep code build one.
        var direct = new Regex(@"\b(SkirmishMatchSession|ScriptedSkirmishSession)\.(Start|Restore)\(|\bSkirmishSessionCommandSink\b|\.Session\.(Step|Issue|Submit)\(|\bnew RelayServer\(");
        foreach (var (file, line, text) in CodeLines(root, "CnCNew.Client.Stride"))
            if (direct.IsMatch(text)) problems.Add($"{file}({line}): the client starts, steps or commands a session around the relay: {text.Trim()}");
        var starts = new Regex(@"\b(SkirmishMatchSession|ScriptedSkirmishSession)\.(Start|Restore)\(|\bSkirmishSessionCommandSink\b");
        foreach (var (file, line, text) in CodeLines(root, "CnCNew.Game"))
            if (starts.IsMatch(text) && !file.EndsWith("/Live/LiveMatch.cs", StringComparison.Ordinal) && !file.Contains("/Lockstep/", StringComparison.Ordinal))
                problems.Add($"{file}({line}): a skirmish session is built outside LiveMatch and the lockstep code: {text.Trim()}");
        Check.That(problems.Count == 0, "the network layers keep their rules: " + string.Join("; ", problems));
        Check.That(direct.IsMatch("var session = SkirmishMatchSession.Start(description, catalog, terrain, objects);") && direct.IsMatch("match.Session.Step();") && starts.IsMatch("new SkirmishSessionCommandSink(session, player)"),
            "the rule of the one match path finds what it forbids");
        Check.That(clocks.IsMatch("var now = DateTime.UtcNow;") && clocks.IsMatch("Thread.Sleep(5);") && serverForbidden.IsMatch("using CnCNew.Simulation.World;") && serverForbidden.IsMatch("var text = File.ReadAllText(path);")
                   && !serverForbidden.IsMatch("using CnCNew.Simulation.Skirmish;") && lockstep.IsMatch("world.LogicRandom.Range(0, 1)"), "the rules find what they forbid");
    }
}
