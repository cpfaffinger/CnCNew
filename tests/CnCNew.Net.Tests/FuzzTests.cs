using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Tests;

// Phase 28, the suite "Fuzz" (docs/DEDICATED-SERVER.md section 9.4): everything that reads bytes from somebody else is fed bytes nobody would send - random ones, and
// real ones changed in every way a generator from a seed can think of. No exception may leave a parser, nothing may change because of what was refused, memory must
// not grow. Every input that ever broke something is kept in the corpus below and tried first, on every run.
//
//   dotnet run --project tests/CnCNew.Net.Tests -- --only Fuzz [--fuzz-seed N] [--fuzz-cases N]
//
// The default is a few seconds per target; a long run names more cases. A failure prints the seed, the case and the input in hexadecimal - which goes into the corpus.
static partial class NetTests
{
    private static long Argument(string name, long fallback)
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? long.Parse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture) : fallback;
    }

    private static ulong FuzzSeed => (ulong)Argument("--fuzz-seed", 28_2026_10_12);
    private static int FuzzCases(int byDefault) => (int)Math.Min(int.MaxValue, Argument("--fuzz-cases", byDefault));

    /// <summary>The generator of the suite: its own, from a seed, the same on every machine.</summary>
    private sealed class Fuzzer(ulong seed)
    {
        private ulong _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

        public ulong Next()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        public int Below(int bound) => bound <= 0 ? 0 : (int)(Next() % (ulong)bound);

        public byte[] Bytes(int length)
        {
            var bytes = new byte[length];
            for (var index = 0; index < length; index++) bytes[index] = (byte)Next();
            return bytes;
        }

        private static readonly ulong[] Extremes =
            [0, 1, 0xFF, 0x7F, 0x80, 0xFFFF, 0x7FFF, 0x8000, 0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, ulong.MaxValue, 0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0x0000020000000000, 0xFFFFFE0000000000];

        /// <summary>One change of a real input: bits, bytes, a number of the kind length fields are made of, a cut, a tail, a splice with another input, or all of it at once.</summary>
        public byte[] Mutate(byte[] source, byte[] other)
        {
            var bytes = (byte[])source.Clone();
            for (var rounds = 1 + Below(3); rounds > 0; rounds--)
            {
                switch (Below(9))
                {
                    case 0 when bytes.Length > 0:
                        bytes[Below(bytes.Length)] ^= (byte)(1 << Below(8));
                        break;
                    case 1 when bytes.Length > 0:
                        bytes[Below(bytes.Length)] = (byte)Next();
                        break;
                    case 2 when bytes.Length >= 2:
                    {
                        // An extreme where a field may be: one, two, four or eight bytes wide.
                        var width = 1 << Below(4);
                        var at = Below(Math.Max(1, bytes.Length - width + 1));
                        var value = Extremes[Below(Extremes.Length)];
                        for (var index = 0; index < width && at + index < bytes.Length; index++) bytes[at + index] = (byte)(value >> (8 * index));
                        break;
                    }
                    case 3:
                        bytes = bytes[..Below(bytes.Length + 1)];
                        break;
                    case 4:
                        bytes = [..bytes, ..Bytes(1 + Below(40))];
                        break;
                    case 5 when other.Length > 0:
                    {
                        var cut = Below(bytes.Length + 1);
                        bytes = [..bytes.AsSpan(0, cut), ..other.AsSpan(Below(other.Length))];
                        break;
                    }
                    case 6 when bytes.Length >= 2 && other.Length >= 2:
                        // The type of another message in front of this body.
                        bytes[0] = other[0];
                        bytes[1] = other[1];
                        break;
                    case 7 when bytes.Length > 4:
                    {
                        var from = Below(bytes.Length - 2);
                        var count = 1 + Below(Math.Min(16, bytes.Length - from - 1));
                        bytes = [..bytes.AsSpan(0, from + count), ..bytes.AsSpan(from, count), ..bytes.AsSpan(from + count)];
                        break;
                    }
                    case 8:
                        return Bytes(Below(600));
                }
            }
            return bytes;
        }
    }

    private static readonly bool Trace = Environment.GetEnvironmentVariable("CNCNEW_FUZZ_TRACE") == "1";

    private static string Hex(ReadOnlySpan<byte> bytes) => bytes.Length <= 4_096 ? Convert.ToHexString(bytes) : Convert.ToHexString(bytes[..4_096]) + $"... ({bytes.Length} bytes)";

    private static void Guard(string target, ulong seed, int index, ReadOnlySpan<byte> input, Action attempt)
    {
        // CNCNEW_FUZZ_TRACE=1 writes every input before it is tried: the last line of a run that hangs is the input that hung it.
        if (Trace) Console.Error.WriteLine($"{target} {index} {Hex(input)}");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            attempt();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{target}: {exception.GetType().Name} left the parser ({exception.Message}) - seed {seed}, case {index}, input {Hex(input)}", exception);
        }
        // One input must not cost seconds either: work without a bound is a way to stop a server or every client of a match.
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        Check.That(seconds < 5, $"{target}: one input took {seconds:F1} s - seed {seed}, case {index}, input {Hex(input)}");
        // Nothing an input announces may be believed before it is checked: a lying length must not allocate what it claims.
        var grown = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check.That(grown < 8 * 1024 * 1024 + 64L * input.Length, $"{target}: one input of {input.Length} bytes allocated {grown} bytes - seed {seed}, case {index}, input {Hex(input)}");
    }

    // ------------------------------------------------------------------------------------------------------------ corpus

    /// <summary>Inputs that broke something once (target, hexadecimal), and inputs written by hand for the places a generator finds slowly. Tried first on every run.</summary>
    private static readonly (string Target, string Hex)[] FuzzCorpus =
    [
        // A message of every length class that is only a type.
        ("message", ""), ("message", "01"), ("message", "0100"), ("message", "FFFF"), ("message", "0000"),
        // A hello of this version that ends after the version; a text whose length says more than there is; a byte block of four gigabytes.
        ("message", "01000400"), ("message", "010004000A00"), ("message", "0900FFFFFFFF"), ("message", "2100010000000100000000000000000000000000000000FFFFFFFF"),
        // A bundle that announces 65,535 commands and holds none; a desync report with more parts than connections can have.
        ("message", "12000100000000000000FFFF"), ("message", "1F000100000000000000FFFF"),
        // Datagrams: only a header of every kind; a data header and a section that is cut; a fragment count of zero.
        ("datagram", "CEC20201"), ("datagram", "CEC20203"), ("datagram", "CEC20204"), ("datagram", "CEC20205"), ("datagram", "CEC20206"), ("datagram", "CEC20207"),
        ("plain", "CEC20205" + "0100000000000000" + "00000000" + "00000000" + "FFFF" + "00000000" + "00000000" + "00000000" + "01"),
        ("plain", "CEC20205" + "0100000000000000" + "00000000" + "00000000" + "FFFF" + "00000000" + "00000000" + "00000000" + "0200000000" + "0000" + "0000" + "0100" + "AA"),
        // A command that is only its header, and one with a type that does not exist.
        ("command", "0000000000000000" + "0B00" + "01000000" + "0000000000000000"), ("command", "0000000000000000" + "FFFF" + "01000000" + "0000000000000000"),
    ];

    private static IEnumerable<byte[]> Corpus(string target) => FuzzCorpus.Where(entry => entry.Target == target).Select(entry => Convert.FromHexString(entry.Hex));

    // ---------------------------------------------------------------------------------------------------------- messages

    public static void FuzzMessages()
    {
        var seed = FuzzSeed;
        var fuzzer = new Fuzzer(seed);
        var samples = EveryMessage().Select(MessageCodec.Encode).ToArray();
        var cases = FuzzCases(200_000);
        long decoded = 0, refused = 0;
        void Try(int index, byte[] input)
        {
            Guard("message", seed, index, input, () =>
            {
                if (!MessageCodec.TryDecode(input, out var message, out var error))
                {
                    Check.That(error.Length > 0, "a refusal names its reason");
                    refused++;
                    return;
                }
                decoded++;
                // What decodes is a message this build could have sent: it encodes again, and to the same bytes.
                var again = MessageCodec.Encode(message!);
                Check.That(MessageCodec.TryDecode(again, out var second, out _) && second!.Type == message!.Type && MessageCodec.Encode(second).AsSpan().SequenceEqual(again), "a decoded message survives its own bytes");
            });
        }
        var number = 0;
        foreach (var input in Corpus("message")) Try(number++, input);
        for (var index = 0; index < cases; index++) Try(number++, fuzzer.Mutate(samples[fuzzer.Below(samples.Length)], samples[fuzzer.Below(samples.Length)]));
        Check.That(decoded > cases / 50 && refused > cases / 3, $"the generator reached both sides: {decoded} decoded, {refused} refused of {number}");
        Console.WriteLine($"       messages: {number} inputs from seed {seed}: {decoded} decoded and encoded again, {refused} refused with a reason, no exception");
    }

    // --------------------------------------------------------------------------------------------------------- transport

    public static void FuzzTransport()
    {
        var seed = FuzzSeed + 1;
        var fuzzer = new Fuzzer(seed);
        var cases = FuzzCases(100_000);

        // A real conversation on the tape: handshake, data in both directions, a close. The plain datagrams of a link beside it.
        var wire = new UdpTests.FuzzWire();
        var tape = wire.Record();
        var plain = new List<byte[]>();
        var source = new UdpLink(UdpOptions.Default, 0);
        source.Establish(0x1122334455667788, 0);
        for (var index = 0; index < 30; index++)
        {
            source.Send(fuzzer.Bytes(1 + fuzzer.Below(3_000)));
            source.SendUrgent(fuzzer.Bytes(1 + fuzzer.Below(2_500)));
            source.Flush(index * 30, datagram => plain.Add(datagram.ToArray()));
        }

        // ---- The format readers and a link: whatever arrives, nothing throws, and a link keeps no more than its window.
        var victim = new UdpLink(UdpOptions.Default, 0);
        victim.Establish(0x1122334455667788, 0);
        var number = 0;
        var before = GC.GetTotalMemory(true);
        void Plain(byte[] input)
        {
            Guard("plain", seed, number++, input, () =>
            {
                UdpFormat.TryReadKind(input, out _);
                UdpFormat.TryReadRequest(input, out _);
                UdpFormat.TryReadCookie(input, out _, out _, out _);
                UdpFormat.TryReadConnect(input, out _, out _, out _, out _);
                UdpFormat.TryReadAccept(input, out _, out _, out _, out _, out _);
                UdpFormat.TryReadRefuse(input, out _, out _);
                UdpFormat.TryReadClose(input, out _);
                UdpFormat.TryReadSealedToken(input, out _);
                if (UdpFormat.TryReadDataHeader(input, out _)) UdpFormat.SectionsAreValid(input);
                if (input.Length <= UdpFormat.MaxPlainBytes)
                {
                    if (!victim.IsOpen)
                    {
                        victim = new UdpLink(UdpOptions.Default, number);
                        victim.Establish(0x1122334455667788, number);
                    }
                    victim.Receive(input, number);
                    while (victim.TryReceive(out _)) { }
                    victim.Flush(number, _ => { });
                }
            });
        }
        foreach (var input in Corpus("plain").Concat(Corpus("datagram"))) Plain(input);
        for (var index = 0; index < cases; index++) Plain(fuzzer.Mutate(plain[fuzzer.Below(plain.Count)], plain[fuzzer.Below(plain.Count)]));
        var grown = GC.GetTotalMemory(true) - before;
        Check.That(grown < 64 * 1024 * 1024, $"a link fed {cases} changed datagrams holds on to {grown / 1024} KB");

        // ---- A server endpoint with a connected client: a stranger and somebody at the client's own address send changed copies of everything that was ever on the
        // line. Nothing throws, no connection appears, and the real client is still heard afterwards.
        var connections = wire.Server.Connections;
        var received = 0;
        for (var index = 0; index < cases / 4; index++)
        {
            var input = fuzzer.Mutate(tape[fuzzer.Below(tape.Count)], tape[fuzzer.Below(tape.Count)]);
            Guard("datagram", seed, number++, input, () =>
            {
                (index % 2 == 0 ? wire.Stranger : wire.ClientChannel).Send(input, UdpTests.FuzzWire.ServerAddress);
                if (index % 16 == 0) received += wire.Step();
            });
        }
        received += wire.Step(20);
        Check.That(wire.Server.Connections <= connections + 1 && wire.ServerLink.IsOpen && wire.Client.Connection.IsOpen, $"the connection stands ({wire.Server.Connections} connections; server link {wire.ServerLink.CloseReason})");
        Check.That(received == 0, $"nothing of {cases / 4} changed datagrams was delivered as a message ({received})");
        wire.Client.Connection.Send([1, 2, 3]);
        wire.ServerLink.SendUrgent([4, 5, 6]);
        var (up, down) = (false, false);
        for (var step = 0; step < 500 && !(up && down); step++)
        {
            wire.Step();
            up |= wire.ServerLink.TryReceive(out var message) && message.AsSpan().SequenceEqual<byte>([1, 2, 3]);
            down |= wire.Client.Connection.TryReceive(out var answer) && answer.AsSpan().SequenceEqual<byte>([4, 5, 6]);
        }
        Check.That(up && down, "the real client and the server still hear each other");
        Console.WriteLine($"       transport: {number} inputs from seed {seed}: no exception; forged {wire.Server.Forged}, repeated {wire.Server.Replayed}, malformed {wire.Server.Malformed}, of no connection {wire.Server.UnknownPeer}, cookies refused {wire.Server.CookiesRejected}");
    }

    // ------------------------------------------------------------------------------------------------------------- relay

    public static void FuzzRelay()
    {
        var seed = FuzzSeed + 2;
        var fuzzer = new Fuzzer(seed);
        var cases = FuzzCases(60_000);
        var samples = EveryMessage().Select(MessageCodec.Encode).ToArray();
        // A room with a match of two honest clients; a third member - admitted, so everything it sends reaches the room's handler - sends changed messages. The room
        // has no bound on messages here: the handler is what is tested, not the bound that would close the connection after the first few thousand.
        var net = new Net(Bare() with { MessagesPerSecond = 0, HashReportLimitMs = 0, AllowLateWatchers = true, SeatHoldMs = 5_000 });
        net.Join();
        net.StartMatch(2);
        var hostile = net.Clients[2];
        var raw = net.Raw();
        var entered = 0;
        var number = 0;
        var before = GC.GetTotalMemory(true);
        for (var index = 0; index < cases; index++)
        {
            var input = fuzzer.Mutate(samples[fuzzer.Below(samples.Length)], samples[fuzzer.Below(samples.Length)]);
            Guard("relay", seed, number++, input, () =>
            {
                // As a member that watches, as a connection that never said hello, and - a few - as the host and as a player.
                var sender = fuzzer.Below(10);
                if (sender < 6) hostile.SendRaw(input);
                else if (sender < 8) raw.Send(input);
                else if (MessageCodec.TryDecode(input, out var message, out _) && message is not (LeaveMessage or StartRequestMessage or RoomConfigMessage or KickMessage or EndReportMessage or HashReportMessage
                             or PaceRequestMessage or LoadedMessage))
                    net.Clients[sender - 8].SendRaw(input);
                if (index % 8 == 0) Play(net, 5, 0, 1);
            });
            if (net.Server.Phase != RoomPhase.Match) break;
            if (hostile.IsOpen) continue;
            // The room dropped it - it said it leaves, or its hash was not the players'. The next one comes to the running match.
            var (hostileEnd, hostileServerEnd) = net.Network.Connect();
            net.Server.Attach(hostileServerEnd);
            hostile = new LockstepClient(hostileEnd, $"hostile {++entered}", Definitions, Released, watch: true);
            Play(net, 20, 0, 1);
            hostile.Pump();
        }
        Check.That(entered > 3, $"the hostile member was dropped and came again ({entered} times)");
        Play(net, 500, 0, 1);
        var grown = GC.GetTotalMemory(true) - before;
        Check.That(net.Server.Phase == RoomPhase.Match && net.Server.Desync is null && net.Clients[0].IsOpen && net.Clients[1].IsOpen, $"the match of the honest clients goes on ({net.Server.Phase}; {net.Server.LastDiscard})");
        Check.That(net.Server.LastAgreedTick >= net.Server.NextTick - 30, $"and every tick is agreed on ({net.Server.LastAgreedTick} of {net.Server.NextTick})");
        Check.That(net.Server.Discarded > number / 4, $"what did not fit was counted ({net.Server.Discarded} of {number})");
        Check.That(grown < 96 * 1024 * 1024, $"the room holds on to {grown / 1024} KB after {number} messages");

        // ---- The other direction: a client is sent changed messages by a server; it counts and survives.
        var (clientEnd, serverEnd) = new LoopbackNetwork(new ManualNetClock()).Connect();
        var client = new LockstepClient(clientEnd, "victim", Definitions, Released);
        var replies = 0;
        for (var index = 0; index < cases; index++)
        {
            var input = fuzzer.Mutate(samples[fuzzer.Below(samples.Length)], samples[fuzzer.Below(samples.Length)]);
            Guard("client", seed, number++, input, () =>
            {
                serverEnd.Send(input);
                client.Pump();
                while (client.HasBundle(client.NewestBundleTick) && replies < 1_000) { client.TakeBundle(client.NewestBundleTick); replies++; }
            });
        }
        Console.WriteLine($"       relay: {number} inputs from seed {seed}: no exception; the room discarded {net.Server.Discarded}, the client {client.Discarded}; the match of the honest clients agreed through tick {net.Server.LastAgreedTick}");
    }

    // ---------------------------------------------------------------------------------------------------------- commands

    public static void FuzzDirectory()
    {
        // The front of a server (docs/NETWORK-PROTOCOL.md section 22.1): whatever a connection sends before it is a member - a request for the list of rooms, a
        // hello, or neither - is answered, turned away or routed to a room, and nothing of it throws, holds a room for ever or disturbs the people in one.
        var seed = FuzzSeed + 5;
        var fuzzer = new Fuzzer(seed);
        var cases = FuzzCases(40_000);
        var samples = EveryMessage().Select(MessageCodec.Encode).ToArray();
        var clock = new ManualNetClock();
        clock.Advance(ClockStartMs);
        var network = new LoopbackNetwork(clock);
        var seeds = 1u;
        var directory = new RoomDirectory(clock, () => ++seeds, new DirectoryOptions(MaxRooms: 6, HelloTimeoutMs: 200, Room: Bare()) { ListIntervalMs = 5, ListsPerConnection = 4 });
        var (honestEnd, honestServerEnd) = network.Connect();
        directory.Attach(honestServerEnd, "10.0.0.1");
        var honest = new LockstepClient(honestEnd, "honest", Definitions, Released, "the honest room");
        for (var round = 0; round < 8; round++) { directory.Pump(); honest.Pump(); }
        Check.That(honest.Phase == ClientPhase.InRoom && directory.RoomCount == 1, "an honest member sits in its room");
        var before = GC.GetTotalMemory(true);
        var number = 0;
        var mostRooms = 0;
        IConnection? stranger = null;
        for (var index = 0; index < cases; index++)
        {
            var input = fuzzer.Mutate(samples[fuzzer.Below(samples.Length)], samples[fuzzer.Below(samples.Length)]);
            Guard("directory", seed, number++, input, () =>
            {
                // Most strangers send one thing and go; some stay and send more on the same connection (a list, then something else).
                if (stranger is null || !stranger.IsOpen || fuzzer.Below(4) != 0)
                {
                    stranger?.Close();
                    var (end, serverEnd) = network.Connect();
                    directory.Attach(serverEnd, $"10.0.{fuzzer.Below(4)}.{fuzzer.Below(200)}");
                    stranger = end;
                }
                stranger.Send(input);
                if (index % 4 == 0) clock.Advance(3);
                directory.Pump();
                honest.Pump();
                while (stranger.TryReceive(out _)) { }
            });
            mostRooms = Math.Max(mostRooms, directory.RoomCount);
        }
        stranger?.Close();
        for (var round = 0; round < 400; round++) { clock.Advance(1); directory.Pump(); honest.Pump(); }
        var grown = GC.GetTotalMemory(true) - before;
        Check.That(honest.Phase == ClientPhase.InRoom && honest.IsOpen, $"the honest member still sits in its room ({honest.Phase})");
        Check.That(mostRooms <= 6 && directory.RoomCount == 1 && directory.PendingConnections == 0, $"never more rooms than the server holds ({mostRooms}); the strangers' rooms and connections are gone ({directory.RoomCount} room, {directory.PendingConnections} pending)");
        Check.That(directory.TurnedAway + directory.ListsServed + directory.ListsRefused + directory.RoomsOpened > number / 2,
            $"what arrived was answered, turned away or became a room, and counted ({directory.TurnedAway} turned away, {directory.ListsServed} lists, {directory.ListsRefused} refused, {directory.RoomsOpened} rooms of {number})");
        Check.That(grown < 64 * 1024 * 1024, $"the directory holds on to {grown / 1024} KB after {number} inputs");

        // ---- The other direction: somebody who asked for the list is sent changed messages; it counts them and survives.
        var (browserEnd, hostileEnd) = new LoopbackNetwork(new ManualNetClock()).Connect();
        var browser = new RoomBrowser(browserEnd);
        var lists = 0;
        for (var index = 0; index < cases; index++)
        {
            var input = fuzzer.Mutate(samples[fuzzer.Below(samples.Length)], samples[fuzzer.Below(samples.Length)]);
            Guard("browser", seed, number++, input, () =>
            {
                hostileEnd.Send(input);
                browser.Pump();
                if (browser.List is { } list)
                {
                    lists++;
                    if (list.Rooms.Length > Protocol.MaxListedRooms) throw new InvalidOperationException($"A list of {list.Rooms.Length} rooms was taken.");
                }
            });
        }
        Check.That(browser.Discarded > 0 && lists > 0, $"a browser takes lists and discards the rest ({browser.Discarded} discarded)");
        Console.WriteLine($"       directory: {number} inputs from seed {seed}: no exception; {directory.TurnedAway} turned away, {directory.ListsServed} lists served, {directory.ListsRefused} refused, {directory.RoomsOpened} rooms opened and closed again; the browser discarded {browser.Discarded}");
    }

    public static void FuzzCommands()
    {
        var seed = FuzzSeed + 3;
        var fuzzer = new Fuzzer(seed);
        var cases = FuzzCases(40_000);
        var fixture = Fixture.Create();
        // A match of three computer players gives the orders: every kind of order a match has, with names and ids that exist at the tick they were given.
        var source = SkirmishMatchSession.Start(fixture.Settings(0, 3, 30_000).ToDescription(string.Empty, fixture.Synthetic.Fingerprint.ToUpperInvariant(), 7, 9), fixture.Synthetic.Templates,
            fixture.Terrain, fixture.Map.Objects);
        var length = TestRate.Frames(30 * 60);
        for (var tick = 0; tick < length && !source.IsOver; tick++) source.Step();
        var orders = source.RecordedCommands.Where(command => CommandTypes.IsPlayerOrder(command.TypeCode)).OrderBy(command => command.Tick).ToList();
        var encoded = orders.Select(ReplayFormat.EncodeCommand).ToList();
        // The computer players give no guard (F-58): a guard of the units of their moves, in each mode, is changed too.
        var guards = 0;
        foreach (var move in orders.OfType<SetMoveTargetCommand>().Take(30))
            encoded.Add(ReplayFormat.EncodeCommand(new GuardCommand(move.Player, move.Sequence, move.Tick, [move.Target], move.X, move.Y,
                CnCNew.Simulation.Ids.EntityId.Invalid, (GuardMode)(guards++ % 3))));
        // Nor an attack on a place (F-64): one at the target of their moves, at their own place and with a slot, is changed too.
        var places = 0;
        foreach (var move in orders.OfType<SetMoveTargetCommand>().Skip(30).Take(30))
            encoded.Add(ReplayFormat.EncodeCommand(new AttackPositionCommand(move.Player, move.Sequence, move.Tick, [move.Target], move.X, move.Y, places % 3 == 1, places++ % 3 - 1, places % 4)));
        var kinds = orders.Select(command => command.TypeCode).Distinct().Count();
        Check.That(orders.Count > 100 && kinds >= 5, $"the match gave orders to change ({orders.Count} of {kinds} kinds)");
        // The same match with three seats for players: the recorded orders are given as the players' own at their ticks, so the world lives as it did - and between
        // them come the changed ones, each a change of an order of about that time, given as any of the three.
        var humans = fixture.Settings(3, 0, 30_000).ToDescription(string.Empty, fixture.Synthetic.Fingerprint.ToUpperInvariant(), 7, 9);
        long accepted = 0, rejected = 0, undecodable = 0, ticks = 0;
        var number = 0;
        SkirmishMatchSession session = null!;
        void Try(byte[] input)
        {
            Guard("command", seed, number++, input, () =>
            {
                if (input.Length < ReplayFormat.CommandHeaderLength)
                {
                    Check.That(!CommandWire.IsPlausible(input, out _), "a command shorter than its header is not plausible");
                    undecodable++;
                    return;
                }
                // As the relay would hand it on: whatever player the bytes name, the bundle's entry says whose command it is.
                var player = fuzzer.Below(4) == 0 ? 1 + fuzzer.Below(3) : Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(ReplayFormat.CommandPlayerOffset)), 1, 3);
                var entry = new BundleCommand(player, 1, 1, 1, CommandWire.Stamp(input, player, session.World.Tick));
                if (!CommandWire.IsPlausible(input, out _) || !CommandWire.TryDecodeIntent(entry, out var intent, out _))
                {
                    undecodable++;
                    return;
                }
                // The pre-check and the submission, as a client's session does both; the simulation then lives with whatever was accepted.
                session.World.Check(intent(0, 0));
                if (session.Issue(new CnCNew.Simulation.Ids.PlayerId(player), intent).Accepted) accepted++;
                else rejected++;
            });
        }
        var perTick = Math.Max(1, (int)Math.Ceiling(cases / (double)length));
        var remaining = cases;
        while (remaining > 0)
        {
            session = SkirmishMatchSession.Start(humans, fixture.Synthetic.Templates, fixture.Terrain, fixture.Map.Objects);
            if (number == 0)
                foreach (var input in Corpus("command")) Try(input);
            var next = 0;
            for (long tick = 0; tick < length && !session.IsOver && remaining > 0; tick++)
            {
                for (; next < orders.Count && orders[next].Tick <= tick; next++)
                {
                    var order = orders[next];
                    session.Issue(order.Player, (sequence, at) => order with { Sequence = sequence, Tick = at });
                }
                for (var index = 0; index < perTick && remaining > 0; index++, remaining--)
                {
                    // An order of about this time: what it names is likely to exist now.
                    var near = Math.Clamp(next - 20 + fuzzer.Below(40), 0, encoded.Count - 1);
                    // Half of them with one changed byte behind the header - an order that is still one, with another unit, place or name -, half changed freely.
                    if (fuzzer.Below(2) == 0 && encoded[near].Length > ReplayFormat.CommandHeaderLength)
                    {
                        var one = (byte[])encoded[near].Clone();
                        one[ReplayFormat.CommandHeaderLength + fuzzer.Below(one.Length - ReplayFormat.CommandHeaderLength)] ^= (byte)(1 << fuzzer.Below(8));
                        Try(one);
                    }
                    else Try(fuzzer.Mutate(encoded[near], encoded[fuzzer.Below(encoded.Count)]));
                }
                Guard("tick", seed, number, [], () => session.Step());
                ticks++;
            }
        }
        Check.That(accepted > cases / 100 && rejected > cases / 20 && undecodable > cases / 20, $"the generator reached every side: {accepted} accepted, {rejected} rejected, {undecodable} not commands");
        Console.WriteLine($"       commands: {number} inputs from seed {seed}: {accepted} accepted and simulated, {rejected} rejected by the simulation, {undecodable} not commands; no exception in {ticks} ticks");
    }

    // ------------------------------------------------------------------------------------------------------------ states

    public static void FuzzStates()
    {
        var seed = FuzzSeed + 4;
        var fuzzer = new Fuzzer(seed);
        var cases = FuzzCases(300);
        var fixture = Fixture.Create();
        var settings = fixture.Settings(1, 2, 30_000);
        var session = SkirmishMatchSession.Start(settings.ToDescription(string.Empty, fixture.Synthetic.Fingerprint.ToUpperInvariant(), 7, 9), fixture.Synthetic.Templates, fixture.Terrain, fixture.Map.Objects);
        for (var tick = 0; tick < settings.TickRate * 30; tick++) session.Step();
        var state = session.Save();
        var packed = SessionTransfer.Pack(state);
        Check.That(SkirmishMatchSession.Restore(SessionTransfer.Unpack(packed), fixture.Synthetic.Templates, fixture.Synthetic.Fingerprint, fixture.Terrain).StateHash() == session.StateHash(), "the state as it is restores");
        long refused = 0, restored = 0, same = 0;
        var number = 0;
        // A joiner is handed a state by another member; the relay passes it on by a hash that member states. What arrives is read by code that expects a session -
        // and must say "not a session" in its own words, whatever it is given.
        void Try(byte[] input)
        {
            Guard("state", seed, number++, input, () =>
            {
                try
                {
                    var restoredSession = SkirmishMatchSession.Restore(SessionTransfer.Unpack(input), fixture.Synthetic.Templates, fixture.Synthetic.Fingerprint, fixture.Terrain);
                    restored++;
                    if (restoredSession.StateHash() == session.StateHash()) same++;
                }
                catch (Exception exception) when (exception is InvalidDataException or SnapshotFormatException or ArgumentException or IOException)
                {
                    // The words a client understands (HeadlessClient.Build, LiveMatch.Join): the state is turned away, the seat held again.
                    refused++;
                }
            });
        }
        // Damage to the packed form, and a state that is only a few bytes.
        for (var index = 0; index < cases; index++) Try(fuzzer.Mutate(packed, packed));
        Try([]);
        Try("CNZ1"u8.ToArray());
        Try([.. "CNZ1"u8, .. fuzzer.Bytes(2_000)]);
        // Damage that is still a document: a value changed, removed, of another kind - what somebody would write who knows the format.
        var text = System.Text.Encoding.UTF8.GetString(state);
        for (var index = 0; index < cases; index++)
        {
            var root = JsonNode.Parse(text)!;
            for (var changes = 1 + fuzzer.Below(3); changes > 0; changes--) ChangeSomewhere(root, fuzzer);
            Try(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        }
        Check.That(refused > cases && same <= restored, $"damaged states are refused in the client's words ({refused} refused, {restored} restored, {same} of them to the very same state)");
        Console.WriteLine($"       states: {number} inputs from seed {seed}: {refused} refused as 'not a session', {restored} restored ({same} unchanged in what the hash covers), no other exception");
    }

    private static void ChangeSomewhere(JsonNode root, Fuzzer fuzzer)
    {
        // Walk down a random path, then change what is there.
        var node = root;
        for (var depth = 0; depth < 12; depth++)
        {
            JsonNode? next = node switch
            {
                JsonObject { Count: > 0 } o => o.ElementAt(fuzzer.Below(o.Count)).Value,
                JsonArray { Count: > 0 } a => a[fuzzer.Below(a.Count)],
                _ => null,
            };
            if (next is null || (next is JsonValue && fuzzer.Below(3) > 0) || fuzzer.Below(12) == 0) break;
            node = next;
        }
        JsonNode? replacement = fuzzer.Below(9) switch
        {
            0 => null,
            1 => JsonValue.Create(long.MaxValue),
            2 => JsonValue.Create(long.MinValue),
            3 => JsonValue.Create(-1),
            4 => JsonValue.Create("\u0000not a value"),
            5 => new JsonArray(),
            6 => new JsonObject(),
            7 => JsonValue.Create(fuzzer.Next().ToString("X16", CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(1e300),
        };
        switch (node)
        {
            case JsonObject { Count: > 0 } o:
            {
                var key = o.ElementAt(fuzzer.Below(o.Count)).Key;
                if (fuzzer.Below(4) == 0) o.Remove(key);
                else o[key] = replacement;
                break;
            }
            case JsonArray { Count: > 0 } a:
            {
                var at = fuzzer.Below(a.Count);
                if (fuzzer.Below(4) == 0) a.RemoveAt(at);
                else a[at] = replacement;
                break;
            }
        }
    }
}
