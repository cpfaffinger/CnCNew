using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using CnCNew.Net;
using CnCNew.Net.Udp;

// Phase 26: the UDP transport - layout, cookie handshake, the reliable and the urgent channel, time limits - on a network in memory with a clock set by hand, and once on
// real sockets of the loopback interface.
static partial class UdpTests
{
    private static readonly byte[] Secret = [..Enumerable.Range(1, 32).Select(value => (byte)value)];
    private static readonly IPEndPoint ServerAddress = new(IPAddress.Parse("10.0.0.1"), 4000);

    /// <summary>The transport as it is, without the bounds on what one address may ask for: these tests put many clients behind one address and send in bursts.</summary>
    internal static readonly UdpOptions Unbounded = new() { HandshakeBurst = 1_000_000, HandshakesPerAddressPerSecond = 1_000_000, HandshakesPerSecond = 1_000_000, MaxConnectionsPerAddress = 256, KeyExchangesPerSecond = 0 };

    /// <summary>A key of an exchange, as a client would send it.</summary>
    private static readonly byte[] SomeKey = new UdpKeyExchange().PublicKey;

    private sealed class Wire
    {
        public ManualNetClock Clock { get; } = new();
        public MemoryDatagramNetwork Network { get; }
        public UdpServerEndpoint Server { get; }
        public List<UdpClientEndpoint> Clients { get; } = [];
        public List<UdpLink> Accepted { get; } = [];
        public List<IPEndPoint> Addresses { get; } = [];
        /// <summary>The channel of every client: a test that plays an attacker at a client's address sends through it.</summary>
        public List<IDatagramChannel> Channels { get; } = [];
        private readonly UdpOptions _options;

        public Wire(Impairment impairment = default, ulong seed = 1, UdpOptions? options = null, UdpIdentity? identity = null, Func<IPAddress, bool>? isBanned = null)
        {
            _options = options ?? Unbounded;
            Network = new MemoryDatagramNetwork(Clock, seed) { Default = impairment };
            Server = new UdpServerEndpoint(Network.Open(ServerAddress), Clock, Secret, _options, identity, isBanned);
        }

        public UdpClientEndpoint Connect(ulong nonce = 0, IPEndPoint? address = null, Func<byte[], bool>? trust = null, Func<IDatagramChannel, IDatagramChannel>? wrap = null)
        {
            address ??= new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5000 + Clients.Count);
            Addresses.Add(address);
            var channel = Network.Open(address);
            Channels.Add(channel);
            var client = new UdpClientEndpoint(wrap is null ? channel : wrap(channel), ServerAddress, Clock, nonce == 0 ? 0xABCD0000UL + (ulong)Clients.Count : nonce, _options, trust);
            Clients.Add(client);
            return client;
        }

        public void Step(int milliseconds = 1)
        {
            for (var index = 0; index < milliseconds; index++)
            {
                Clock.Advance(1);
                foreach (var client in Clients) client.Pump();
                Server.Pump();
                while (Server.TryAccept(out var link, out _)) Accepted.Add(link);
                foreach (var client in Clients) client.Flush();
                Server.Flush();
            }
        }

        public bool Until(Func<bool> done, int limitMs = 60_000)
        {
            for (var index = 0; index < limitMs; index++)
            {
                if (done()) return true;
                Step();
            }
            return done();
        }
    }

    private static byte[] Payload(int index, int length)
    {
        var bytes = new byte[length];
        for (var position = 0; position < length; position++) bytes[position] = (byte)(index * 31 + position * 7);
        if (length >= 4) BinaryPrimitives.WriteInt32LittleEndian(bytes, index);
        return bytes;
    }

    // ----------------------------------------------------------------------------------------------------------- layout

    public static void Layout()
    {
        var request = UdpFormat.Request(77);
        Check.That(request.Length == 32 && UdpFormat.TryReadKind(request, out var kind) && kind == UdpKind.Request && UdpFormat.TryReadRequest(request, out var nonce) && nonce == 77, "a request survives its bytes");
        var cookie = Enumerable.Range(0, 16).Select(value => (byte)(value + 3)).ToArray();
        var challenge = UdpFormat.Cookie(UdpKind.Challenge, 77, 9, cookie);
        Check.That(challenge.Length == request.Length, "a challenge is as long as the request it answers (no amplification)");
        Check.That(UdpFormat.TryReadCookie(challenge, out nonce, out var slot, out var read) && nonce == 77 && slot == 9 && read.SequenceEqual(cookie), "a challenge survives its bytes");
        var (keyA, keyB, signed) = (Enumerable.Range(0, 64).Select(value => (byte)value).ToArray(), Enumerable.Range(0, 64).Select(value => (byte)(value + 64)).ToArray(), Enumerable.Range(0, 64).Select(value => (byte)(value + 128)).ToArray());
        Check.That(UdpFormat.TryReadAccept(UdpFormat.Accept(77, 0x1122334455667788, keyA, keyB, signed), out nonce, out var token, out var readA, out var readB, out var readSigned) && nonce == 77
                   && token == 0x1122334455667788 && readA.SequenceEqual(keyA) && readB.SequenceEqual(keyB) && readSigned.SequenceEqual(signed), "an accept survives its bytes");
        var connect = UdpFormat.Connect(77, 9, cookie, keyA);
        Check.That(connect.Length == 96 && UdpFormat.TryReadConnect(connect, out nonce, out slot, out read, out readA) && nonce == 77 && slot == 9 && read.SequenceEqual(cookie) && readA.SequenceEqual(keyA)
                   && !UdpFormat.TryReadConnect(challenge, out _, out _, out _, out _), "a connect carries the cookie and the client's key of the exchange");
        Check.That(UdpFormat.TryReadClose(UdpFormat.Close(5), out token) && token == 5 && UdpFormat.TryReadRefuse(UdpFormat.Refuse(77, UdpFormat.RefuseFull), out nonce, out var reason) && reason == 1, "close and refuse survive their bytes");

        // What does not follow the layout is refused: magic, version, kind, length, padding.
        byte[] Changed(byte[] source, int offset, byte value) { var copy = (byte[])source.Clone(); copy[offset] = value; return copy; }
        Check.That(!UdpFormat.TryReadKind(Changed(request, 0, 0), out _) && !UdpFormat.TryReadKind(Changed(request, 2, 1), out _) && !UdpFormat.TryReadKind(Changed(request, 3, 0), out _)
                   && !UdpFormat.TryReadKind(Changed(request, 3, 8), out _) && !UdpFormat.TryReadKind(request.AsSpan(0, 3), out _) && !UdpFormat.TryReadKind(new byte[1201], out _),
            "wrong magic, version, kind and size are not datagrams of this transport");
        Check.That(!UdpFormat.TryReadRequest(Changed(request, 20, 1), out _) && !UdpFormat.TryReadRequest(request.AsSpan(0, 31), out _) && !UdpFormat.TryReadRequest([..request, 0], out _),
            "a request with padding that is not zero, or of another length, is malformed");
        Check.That(!UdpFormat.TryReadAccept(UdpFormat.Accept(1, 0, keyA, keyB, signed), out _, out _, out _, out _, out _) && !UdpFormat.TryReadRefuse(UdpFormat.Refuse(1, 9), out _, out _)
                   && UdpFormat.TryReadRefuse(UdpFormat.Refuse(1, UdpFormat.RefuseAddressFull), out _, out _), "a token of 0 and an unknown refusal are malformed");

        // Sections: every length and range is checked before anything is applied.
        var datagram = new byte[UdpFormat.DataHeaderBytes + 64];
        UdpFormat.WriteDataHeader(datagram, new UdpDataHeader(1, 2, 3, 4, 5, 6, 7));
        Check.That(UdpFormat.TryReadDataHeader(datagram, out var header) && header == new UdpDataHeader(1, 2, 3, 4, 5, 6, 7), "a data header survives its bytes");
        byte[] WithSections(params byte[] sections) => [..datagram.AsSpan(0, UdpFormat.DataHeaderBytes), ..sections];
        Check.That(UdpFormat.SectionsAreValid(WithSections()), "a datagram without sections is an acknowledgement");
        Check.That(UdpFormat.SectionsAreValid(WithSections(1, 0, 0, 0, 0, 2, 0, 9, 9)), "a segment of two bytes");
        Check.That(UdpFormat.SectionsAreValid(WithSections(2, 0, 0, 0, 0, 1, 0, 3, 0, 1, 0, 9)), "the second of three urgent fragments");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(1, 0, 0, 0, 0, 0, 0)), "a segment of length 0");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(1, 0, 0, 0, 0, 3, 0, 9, 9)), "a segment longer than the datagram");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(1, 0, 0, 0, 0, 1, 0, 9, 9)), "trailing bytes after a segment");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(3, 0, 0, 0, 0, 1, 0, 9)), "an unknown section kind");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(2, 0, 0, 0, 0, 3, 0, 3, 0, 1, 0, 9)), "a fragment index that is not below the count");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(2, 0, 0, 0, 0, 0, 0, 65, 0, 1, 0, 9)), "more fragments than a message may have");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(2, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 9)), "a fragment count of 0");
        Check.That(!UdpFormat.SectionsAreValid(WithSections(1, 0, 0)), "a truncated section header");
        Check.That(UdpFormat.MaxSegmentBytes == 1135 && UdpFormat.MaxUrgentFragmentBytes == 1131 && UdpFormat.MaxDatagramBytes == 1200 && UdpFormat.MaxPlainBytes == 1176 && UdpFormat.SealBytes == 24
                   && UdpFormat.AcceptBytes == 212 && UdpFormat.Version == 2, "the sizes are the documented ones");
    }

    // -------------------------------------------------------------------------------------------------------- handshake

    public static void Handshake()
    {
        var wire = new Wire();
        var channel = wire.Network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.9"), 9));
        var forged = new IPEndPoint(IPAddress.Parse("10.0.0.9"), 9);
        var buffer = new byte[2048];

        // A thousand requests: a thousand answers of the same size, and nothing kept.
        for (var index = 0; index < 1000; index++) channel.Send(UdpFormat.Request((ulong)index + 1), ServerAddress);
        wire.Step(2);
        var answers = 0;
        while (channel.TryReceive(buffer, out var length, out _))
        {
            answers++;
            Check.That(length == 32 && buffer[3] == (byte)UdpKind.Challenge, "a request is answered with a challenge of its own size");
        }
        Check.That(answers == 1000 && wire.Server.Requests == 1000 && wire.Server.Connections == 0 && wire.Accepted.Count == 0, $"requests create no state ({answers} answers, {wire.Server.Connections} connections)");

        // A connect with a made-up cookie, and one with a cookie issued to another address: no connection, a fresh challenge.
        channel.Send(UdpFormat.Connect(5, 0, new byte[16], SomeKey), ServerAddress);
        Span<byte> foreign = stackalloc byte[16];
        UdpFormat.Mac(Secret, 1, new IPEndPoint(IPAddress.Parse("10.0.0.8"), 9), 5, 0, foreign);
        channel.Send(UdpFormat.Connect(5, 0, foreign, SomeKey), ServerAddress);
        wire.Step(2);
        Check.That(wire.Server.CookiesRejected == 2 && wire.Server.Connections == 0, "a cookie that is not this sender's creates no connection");
        while (channel.TryReceive(buffer, out _, out _)) { }

        // The real thing.
        var client = wire.Connect();
        Check.That(wire.Until(() => client.State == UdpClientState.Connected, 2_000), "the client connects");
        Check.That(wire.Accepted.Count == 1 && wire.Accepted[0].Token == client.Link.Token && client.Link.Token != 0, "both ends hold the same token");

        // The same connect again (its accept was lost): the same token, no second connection.
        Span<byte> cookie = stackalloc byte[16];
        var slot = unchecked((uint)(wire.Clock.Milliseconds / UdpOptions.Default.CookieLifetimeMs));
        UdpFormat.Mac(Secret, 1, forged, 42, slot, cookie);
        channel.Send(UdpFormat.Connect(42, slot, cookie, SomeKey), ServerAddress);
        wire.Step(2);
        ulong NextAccept()
        {
            // The connection that now exists sends its own datagrams too; the accept is among them.
            while (channel.TryReceive(buffer, out var size, out _))
                if (buffer[3] == (byte)UdpKind.Accept && UdpFormat.TryReadAccept(buffer.AsSpan(0, size), out _, out var accepted, out var exchangeKey, out _, out _))
                    return accepted ^ BinaryPrimitives.ReadUInt64LittleEndian(exchangeKey);
            return 0;
        }
        var first = NextAccept();
        Check.That(first != 0, "a valid cookie is accepted");
        channel.Send(UdpFormat.Connect(42, slot, cookie, SomeKey), ServerAddress);
        wire.Step(2);
        Check.That(NextAccept() == first && wire.Server.Connections == 2,
            "a repeated connect gets the same answer - the same token and the same key of the exchange - and no second connection");
        // Another nonce from the same address replaces the connection.
        UdpFormat.Mac(Secret, 1, forged, 43, slot, cookie);
        channel.Send(UdpFormat.Connect(43, slot, cookie, SomeKey), ServerAddress);
        wire.Step(2);
        Check.That(wire.Server.Connections == 2 && wire.Accepted.Count == 3 && wire.Accepted[1].CloseReason == UdpCloseReason.Replaced, "a new attempt from the same address replaces the old connection");

        // An expired cookie: two lifetimes later it is worth a new challenge only.
        UdpFormat.Mac(Secret, 1, forged, 44, slot, cookie);
        var expired = UdpFormat.Connect(44, slot, cookie, SomeKey);
        wire.Step(2 * UdpOptions.Default.CookieLifetimeMs);
        var rejectedBefore = wire.Server.CookiesRejected;
        channel.Send(expired, ServerAddress);
        wire.Step(2);
        Check.That(wire.Server.CookiesRejected == rejectedBefore + 1, "an expired cookie is rejected");

        // A full server refuses - only somebody who returned a cookie.
        var small = new Wire(options: Unbounded with { MaxConnections = 1 });
        var one = small.Connect();
        var two = small.Connect();
        Check.That(small.Until(() => one.State == UdpClientState.Connected && two.State == UdpClientState.Failed, 2_000) && small.Server.Refused >= 1 && !two.Connection.IsOpen, "the second client of a server for one is refused");

        // Nobody answers: the client gives up after the connect timeout.
        var silent = new Wire(new Impairment(LossPerMille: 1000));
        var lonely = silent.Connect();
        silent.Step(UdpOptions.Default.ConnectTimeoutMs - 10);
        Check.That(lonely.State == UdpClientState.Connecting, "the client keeps trying until the timeout");
        silent.Step(20);
        Check.That(lonely.State == UdpClientState.Failed && !lonely.Connection.IsOpen && lonely.Link.CloseReason == UdpCloseReason.Timeout, "the client gives up after the connect timeout");
        Check.That(silent.Network.Datagrams is >= 19 and <= 21, $"it repeated its request every 250 ms ({silent.Network.Datagrams})");
    }

    // --------------------------------------------------------------------------------------------------------- channels

    private static (long Retransmitted, long Datagrams, long Lost, long Duplicates) Exchange(Impairment impairment, ulong seed, int messages)
    {
        var wire = new Wire(impairment, seed);
        var client = wire.Connect();
        Check.That(wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 20_000), "the client connects through the impaired network");
        var server = wire.Accepted[0];
        var sizes = new int[messages];
        for (var index = 0; index < messages; index++) sizes[index] = index % 97 == 5 ? 20_000 + index : index % 13 == 0 ? 0 : 1 + index * 37 % 1500;
        sizes[messages / 2] = 300_000;
        int sentUp = 0, sentDown = 0, gotUp = 0, gotDown = 0;
        var done = wire.Until(() =>
        {
            // A few per millisecond in both directions, while the exchange runs.
            for (var burst = 0; burst < 3 && sentUp < messages; burst++, sentUp++) client.Connection.Send(Payload(sentUp, sizes[sentUp]));
            for (var burst = 0; burst < 2 && sentDown < messages; burst++, sentDown++) server.Send(Payload(sentDown + 7, sizes[sentDown]));
            while (server.TryReceive(out var message))
            {
                Check.That(message.AsSpan().SequenceEqual(Payload(gotUp, sizes[gotUp])), $"message {gotUp} to the server arrives unchanged, once and in order");
                gotUp++;
            }
            while (client.Connection.TryReceive(out var message))
            {
                Check.That(message.AsSpan().SequenceEqual(Payload(gotDown + 7, sizes[gotDown])), $"message {gotDown} to the client arrives unchanged, once and in order");
                gotDown++;
            }
            return gotUp == messages && gotDown == messages;
        }, 600_000);
        Check.That(done, $"every message arrives ({gotUp} and {gotDown} of {messages})");
        wire.Step(3_000);
        Check.That(!server.TryReceive(out _) && !client.Connection.TryReceive(out _), "nothing arrives twice");
        Check.That(client.Link.ReliableBacklogBytes == 0 && server.ReliableBacklogBytes == 0, "everything is acknowledged in the end");
        return (client.Link.Statistics.SegmentsRetransmitted + server.Statistics.SegmentsRetransmitted, wire.Network.Datagrams, wire.Network.Lost,
            client.Link.Statistics.DuplicatesReceived + server.Statistics.DuplicatesReceived);
    }

    public static void ReliableChannel()
    {
        var clean = Exchange(default, 1, 400);
        Check.That(clean.Retransmitted == 0 && clean.Lost == 0, $"a clean network needs no repetition ({clean.Retransmitted})");
        var bad = new Impairment(DelayMs: 40, JitterMs: 30, LossPerMille: 200, DuplicatePerMille: 50, ReorderPerMille: 50);
        var first = Exchange(bad, 5, 400);
        Check.That(first.Lost > 0 && first.Retransmitted > 0 && first.Duplicates > 0, $"loss, duplication and reordering were really there ({first})");
        Check.That(Exchange(bad, 5, 400) == first, "the same seed gives the same run");
        // Jitter lets the server's first datagram overtake its accept: that is the network's doing, counted as such, and nothing is lost by it.
        long early = 0, malformed = 0;
        for (var seed = 1UL; seed <= 40; seed++)
        {
            var wire = new Wire(new Impairment(DelayMs: 5, JitterMs: 40), seed);
            var client = wire.Connect();
            Check.That(wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 5_000), "connected under jitter");
            wire.Accepted[0].Send(Payload(1, 50));
            Check.That(wire.Until(() => client.Connection.TryReceive(out _), 5_000), "the first message arrives whatever overtook what");
            early += client.Early;
            malformed += client.Malformed;
        }
        Check.That(early > 0 && malformed == 0, $"a datagram ahead of its accept is counted as early, not as malformed ({early}, {malformed})");
        Check.That(Exchange(bad, 6, 400) != first, "another seed gives another run");
    }

    public static void UrgentChannel()
    {
        // One lost datagram costs no waiting: the next urgent message brings the missing one along.
        var wire = new Wire();
        var client = wire.Connect();
        Check.That(wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 2_000), "connected");
        var server = wire.Accepted[0];
        wire.Step(50);
        var received = new List<(int Index, long AtMs)>();
        void Drain()
        {
            while (client.Connection.TryReceive(out var message)) received.Add((BinaryPrimitives.ReadInt32LittleEndian(message), wire.Clock.Milliseconds));
        }
        for (var index = 0; index < 60; index++)
        {
            // Every 16 ms one message; the datagram of message 20 is lost.
            if (index == 20) wire.Network.Impair(ServerAddress, wire.Addresses[0], new Impairment(LossPerMille: 1000));
            server.SendUrgent(Payload(index, 40));
            wire.Step(1);
            if (index == 20) wire.Network.Impair(ServerAddress, wire.Addresses[0], default);
            for (var ms = 0; ms < 15; ms++)
            {
                wire.Step(1);
                Drain();
            }
        }
        Drain();
        Check.That(received.Select(item => item.Index).SequenceEqual(Enumerable.Range(0, 60)), "every urgent message arrives once and in order");
        var gap = received[20].AtMs - received[19].AtMs;
        Check.That(gap is > 16 and <= 34 && received[21].AtMs == received[20].AtMs, $"the lost message arrives with the next one, one interval later ({gap} ms), not after a retransmission time");
        Check.That(server.Statistics.UrgentRepeated >= 1 && server.Statistics.SegmentsRetransmitted == 0, "it came as a repetition on the urgent channel");
        Check.That(wire.Until(() => server.UrgentBacklog == 0, 1_000), "every urgent message is acknowledged");

        // On a long line a message rides along three more times, not for the whole round trip: the datagrams stay small.
        var far = new Wire(new Impairment(DelayMs: 150));
        var distant = far.Connect();
        Check.That(far.Until(() => distant.State == UdpClientState.Connected && far.Accepted.Count == 1, 5_000), "connected over the long line");
        far.Step(1_000);
        var sentBefore = far.Accepted[0].Statistics.BytesSent;
        var got = 0;
        for (var index = 0; index < 120; index++)
        {
            far.Accepted[0].SendUrgent(Payload(index, 30));
            far.Step(16);
            while (distant.Connection.TryReceive(out _)) got++;
        }
        far.Until(() => { while (distant.Connection.TryReceive(out _)) got++; return got == 120; }, 2_000);
        var perMessage = (far.Accepted[0].Statistics.BytesSent - sentBefore) / 120.0;
        Check.That(got == 120 && far.Accepted[0].Statistics.UrgentRepeated is >= 340 and <= 380, $"every message was sent four times ({far.Accepted[0].Statistics.UrgentRepeated} repetitions of 120)");
        Check.That(perMessage < 34 + 24 + 4 * 41 + 20, $"a datagram carries four messages, not the eighteen of a round trip ({perMessage:F0} bytes per message)");

        // The last message is lost and nothing follows: the retransmission time brings it.
        wire.Network.Impair(ServerAddress, wire.Addresses[0], new Impairment(LossPerMille: 1000));
        server.SendUrgent(Payload(60, 40));
        wire.Step(2);
        wire.Network.Impair(ServerAddress, wire.Addresses[0], default);
        Check.That(wire.Until(() => { Drain(); return received.Count == 61; }, 1_000), "a lost last message is repeated by the timer");

        // Under heavy loss, with reordering and duplication: once, in order, large ones in fragments - and both channels side by side.
        var rough = new Wire(new Impairment(DelayMs: 30, JitterMs: 25, LossPerMille: 150, DuplicatePerMille: 50, ReorderPerMille: 50), 3);
        var roughClient = rough.Connect();
        Check.That(rough.Until(() => roughClient.State == UdpClientState.Connected && rough.Accepted.Count == 1, 20_000), "connected through the rough network");
        var roughServer = rough.Accepted[0];
        int sent = 0, urgentGot = 0, reliableGot = 0;
        const int Count = 500;
        int Size(int index) => index % 50 == 7 ? 5_000 : index % 9 == 0 ? UdpFormat.MaxUrgentFragmentBytes : 20 + index % 300;
        var finished = rough.Until(() =>
        {
            if (sent < Count && rough.Clock.Milliseconds % 8 == 0)
            {
                roughServer.SendUrgent(Payload(sent, Size(sent)));
                roughServer.Send(Payload(sent + 100_000, 30));
                sent++;
            }
            while (roughClient.Connection.TryReceive(out var message))
            {
                var index = BinaryPrimitives.ReadInt32LittleEndian(message);
                if (index >= 100_000)
                {
                    Check.That(index == 100_000 + reliableGot, "the reliable messages keep their order");
                    reliableGot++;
                }
                else
                {
                    Check.That(index == urgentGot && message.AsSpan().SequenceEqual(Payload(index, Size(index))), $"urgent message {urgentGot} arrives unchanged, once and in order");
                    urgentGot++;
                }
            }
            return urgentGot == Count && reliableGot == Count;
        }, 120_000);
        Check.That(finished, $"all urgent and reliable messages arrive ({urgentGot}, {reliableGot} of {Count})");
        Check.That(roughServer.Statistics.UrgentRepeated > 0 && roughServer.Statistics.UrgentOmitted > 0, "repetitions rode along, and what did not fit was counted");
        // An urgent message beyond 64 fragments goes the reliable way rather than being lost.
        roughServer.SendUrgent(new byte[80_000]);
        Check.That(rough.Until(() => roughClient.Connection.TryReceive(out var big) && big.Length == 80_000, 60_000), "an over-large urgent message still arrives");
    }

    public static void TimeLimits()
    {
        var wire = new Wire();
        var client = wire.Connect();
        Check.That(wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 2_000), "connected");
        var server = wire.Accepted[0];
        var before = wire.Network.Datagrams;
        wire.Step(60_000);
        var perSecond = (wire.Network.Datagrams - before) / 60.0;
        Check.That(client.Connection.IsOpen && server.IsOpen, "an idle connection stays open");
        Check.That(perSecond is >= 1.9 and <= 4.1, $"kept alive by about one datagram per second and direction, each acknowledged ({perSecond:F1} per second)");
        Check.That(client.Link.Statistics.RoundTripMs is >= 0 and <= 3, $"the round trip of an instant network is measured as such ({client.Link.Statistics.RoundTripMs} ms)");

        // A measured round trip.
        var slow = new Wire(new Impairment(DelayMs: 80));
        var far = slow.Connect();
        slow.Until(() => far.State == UdpClientState.Connected && slow.Accepted.Count == 1, 5_000);
        for (var index = 0; index < 50; index++)
        {
            far.Connection.Send(Payload(index, 10));
            slow.Step(100);
        }
        Check.That(far.Link.Statistics.RoundTripMs is >= 158 and <= 166 && slow.Accepted[0].Statistics.RoundTripMs is >= 158 and <= 166,
            $"80 ms each way are measured as a round trip of 160 ms ({far.Link.Statistics.RoundTripMs}, {slow.Accepted[0].Statistics.RoundTripMs})");

        // The line is cut: both ends close after the timeout, not before.
        wire.Network.Cut(ServerAddress, wire.Addresses[0]);
        wire.Step(UdpOptions.Default.TimeoutMs - 1_100);
        Check.That(client.Connection.IsOpen && server.IsOpen, "a silent line is not closed before the timeout");
        wire.Step(1_200);
        Check.That(!client.Connection.IsOpen && !server.IsOpen && client.Link.CloseReason == UdpCloseReason.Timeout && server.CloseReason == UdpCloseReason.Timeout && client.State == UdpClientState.Closed,
            "both ends close after the timeout");
        wire.Step(10);
        Check.That(wire.Server.Connections == 0, "the server forgets a closed connection");

        // A clean close reaches the peer at once, and what was sent before it still arrives.
        var polite = new Wire(new Impairment(DelayMs: 5));
        var leaving = polite.Connect();
        polite.Until(() => leaving.State == UdpClientState.Connected && polite.Accepted.Count == 1, 2_000);
        leaving.Connection.Send(Payload(1, 100));
        polite.Step(20);
        leaving.Connection.Close();
        polite.Step(20);
        Check.That(!polite.Accepted[0].IsOpen && polite.Accepted[0].CloseReason == UdpCloseReason.Peer, "the peer learns of a close at once");
        Check.That(polite.Accepted[0].TryReceive(out var last) && last.Length == 100, "a closed connection still hands out what arrived before");
        Check.That(polite.Server.Connections == 0 && leaving.State == UdpClientState.Closed, "both ends are done");

        // What was sent immediately before a close still arrives - a rejection is of no use otherwise - even when the network loses every third datagram.
        var lossy = new Wire(new Impairment(DelayMs: 10, LossPerMille: 330), 4);
        var turnedAway = lossy.Connect();
        Check.That(lossy.Until(() => turnedAway.State == UdpClientState.Connected && lossy.Accepted.Count == 1, 20_000), "connected through the lossy network");
        lossy.Accepted[0].Send(Payload(5, 3_000));
        lossy.Accepted[0].Close();
        Check.That(!lossy.Accepted[0].IsOpen, "the closing end is closed for its owner at once");
        var got = Array.Empty<byte>();
        Check.That(lossy.Until(() => turnedAway.Connection.TryReceive(out got) || turnedAway.State == UdpClientState.Closed, 5_000) && got.AsSpan().SequenceEqual(Payload(5, 3_000)),
            "the last message before a close arrives");
        Check.That(lossy.Until(() => turnedAway.State == UdpClientState.Closed && lossy.Server.Connections == 0, 5_000), "and then the close, and the server forgets the connection");
        // A peer that never acknowledges does not keep a closed connection alive beyond the linger time.
        var mute = new Wire();
        var deaf = mute.Connect();
        mute.Until(() => deaf.State == UdpClientState.Connected && mute.Accepted.Count == 1, 2_000);
        mute.Network.Cut(ServerAddress, mute.Addresses[0]);
        mute.Accepted[0].Send(Payload(1, 100));
        mute.Accepted[0].Close();
        mute.Step(UdpOptions.Default.LingerMs + 50);
        Check.That(mute.Server.Connections == 0, "a closed connection is given up after the linger time");
    }

    public static void Garbage()
    {
        var wire = new Wire();
        var client = wire.Connect();
        wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 2_000);
        var server = wire.Accepted[0];
        var stranger = wire.Network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.7"), 7));
        var random = 0x1234567UL;
        byte Next()
        {
            random ^= random << 13;
            random ^= random >> 7;
            random ^= random << 17;
            return (byte)(random >> 32);
        }
        // Noise from a stranger: random bytes, and random bodies behind a correct header of every kind.
        for (var index = 0; index < 5_000; index++)
        {
            var datagram = new byte[1 + Next() * 5 % 1300];
            for (var position = 0; position < datagram.Length; position++) datagram[position] = Next();
            if (index % 2 == 0 && datagram.Length >= 4) UdpFormat.WriteHeader(datagram, (UdpKind)(1 + index / 2 % 7));
            stranger.Send(datagram, ServerAddress);
            if (index % 50 == 0) wire.Step(1);
        }
        wire.Step(5);
        Check.That(wire.Server.Connections == 1 && wire.Server.Malformed + wire.Server.UnknownPeer + wire.Server.CookiesRejected + wire.Server.Requests >= 4_000, "noise is counted and creates nothing");

        // Real datagrams of the connection with one byte changed, sent from the client's own address: taken apart only when every field still holds.
        var tap = new List<byte[]>();
        var link = new UdpLink(UdpOptions.Default, 0);
        link.Establish(client.Link.Token, 0);
        for (var index = 0; index < 40; index++) link.Send(Payload(index, 30 + index * 20));
        for (var index = 0; index < 10; index++) link.SendUrgent(Payload(index, 200));
        link.Flush(1, datagram => tap.Add(datagram.ToArray()));
        Check.That(tap.Count >= 5, "there are datagrams to damage");
        var victim = new UdpLink(UdpOptions.Default, 0);
        victim.Establish(client.Link.Token, 0);
        var applied = 0;
        foreach (var original in tap)
            for (var position = 0; position < original.Length; position += 3)
            {
                var damaged = (byte[])original.Clone();
                damaged[position] ^= (byte)(1 + Next() % 255);
                var fresh = new UdpLink(UdpOptions.Default, 0);
                fresh.Establish(client.Link.Token, 0);
                fresh.Receive(damaged, 1);
                if (fresh.Statistics.Malformed == 0) applied++;
                while (fresh.TryReceive(out _)) { }
            }
        Check.That(applied > 0, "a changed payload byte is the transport's to carry, not to judge");

        // A length prefix beyond the limit closes the connection - before anything of that size is allocated.
        var liar = new UdpLink(UdpOptions.Default, 0);
        liar.Establish(9, 0);
        var lie = new byte[UdpFormat.DataHeaderBytes + UdpFormat.SegmentOverhead + 4];
        UdpFormat.WriteDataHeader(lie, new UdpDataHeader(9, 0, 0, UdpFormat.NoEcho, 0, 0, 0));
        lie[UdpFormat.DataHeaderBytes] = UdpFormat.SectionSegment;
        BinaryPrimitives.WriteUInt16LittleEndian(lie.AsSpan(UdpFormat.DataHeaderBytes + 5), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(lie.AsSpan(UdpFormat.DataHeaderBytes + 7), 0xFFFFFFF0);
        var memory = GC.GetAllocatedBytesForCurrentThread();
        liar.Receive(lie, 1);
        Check.That(!liar.IsOpen && liar.CloseReason == UdpCloseReason.Violation && GC.GetAllocatedBytesForCurrentThread() - memory < 4_096, "an announced message beyond the limit closes the connection and allocates nothing");

        // A datagram for the connection with the wrong token, from the right address, is not the connection's.
        var before = server.Statistics.DatagramsReceived;
        var wrong = new byte[UdpFormat.DataHeaderBytes + UdpFormat.SealBytes];
        UdpFormat.WriteDataHeader(wrong, new UdpDataHeader(client.Link.Token ^ 1, 0, 0, UdpFormat.NoEcho, 0, 0, 0));
        var unknownBefore = wire.Server.UnknownPeer;
        wire.Network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.6"), 6)).Send(wrong, ServerAddress);
        wire.Step(2);
        Check.That(wire.Server.UnknownPeer == unknownBefore + 1 && client.Connection.IsOpen && server.IsOpen, "a wrong token is counted and touches nothing");
        _ = before;
    }

    // ---------------------------------------------------------------------------------------------------------- sockets

    public static void RealSockets()
    {
        // The same transport on two real UDP sockets of the loopback interface, with the real clock - bounded to ten seconds.
        var clock = new SystemNetClock();
        using var serverSocket = new UdpSocketChannel(new IPEndPoint(IPAddress.Loopback, 0));
        using var clientSocket = new UdpSocketChannel(new IPEndPoint(IPAddress.Loopback, 0));
        using var server = new UdpServerEndpoint(serverSocket, clock, Secret);
        using var client = new UdpClientEndpoint(clientSocket, serverSocket.LocalEndPoint, clock, 0x5EED);
        UdpLink? accepted = null;
        const int Count = 300;
        int sent = 0, gotUp = 0, gotDown = 0, gotUrgent = 0;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 10 && (gotUp < Count || gotDown < Count || gotUrgent < Count))
        {
            client.Pump();
            server.Pump();
            if (accepted is null && server.TryAccept(out var link, out _)) accepted = link;
            if (accepted is not null && sent < Count)
            {
                client.Connection.Send(Payload(sent, sent % 7 == 0 ? 9_000 : 64));
                accepted.Send(Payload(sent + 1_000, 128));
                accepted.SendUrgent(Payload(sent + 2_000, 48));
                sent++;
            }
            while (accepted is not null && accepted.TryReceive(out var up))
            {
                Check.That(BinaryPrimitives.ReadInt32LittleEndian(up) == gotUp, "the server receives in order");
                gotUp++;
            }
            while (client.Connection.TryReceive(out var down))
            {
                var index = BinaryPrimitives.ReadInt32LittleEndian(down);
                if (index >= 2_000) { Check.That(index == 2_000 + gotUrgent, "urgent messages in order"); gotUrgent++; }
                else { Check.That(index == 1_000 + gotDown, "reliable messages in order"); gotDown++; }
            }
            client.Flush();
            server.Flush();
            serverSocket.WaitReadable(200);
        }
        Check.That(gotUp == Count && gotDown == Count && gotUrgent == Count, $"everything arrives over real sockets within ten seconds ({gotUp}, {gotDown}, {gotUrgent} of {Count}; client {client.State})");
        client.Connection.Close();
        client.Flush();
        var closing = Stopwatch.StartNew();
        while (closing.Elapsed.TotalSeconds < 4 && accepted!.IsOpen)
        {
            // The closing end goes on until what it sent before is acknowledged, then says goodbye.
            client.Pump();
            client.Flush();
            server.Pump();
            server.Flush();
            serverSocket.WaitReadable(1_000);
        }
        Check.That(!accepted!.IsOpen, "the close arrives over real sockets");
    }

    public static void RealClock()
    {
        var clock = new SystemNetClock();
        var a = clock.Microseconds;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 30) { }
        var b = clock.Microseconds;
        Check.That(b - a is >= 29_000 and < 200_000 && clock.Milliseconds >= 29 && clock.Milliseconds * 1000 <= clock.Microseconds, $"the real clock runs in micro- and milliseconds ({b - a} us)");
        using var timer = new FineTimer();
        Check.That(timer.Held == OperatingSystem.IsWindows() || !OperatingSystem.IsWindows(), "the fine timer is asked for on Windows only");
    }
}
