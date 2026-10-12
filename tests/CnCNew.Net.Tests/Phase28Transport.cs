using System.Buffers.Binary;
using System.Net;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;

// Phase 28: the sealed transport (docs/NETWORK-PROTOCOL.md sections 20.2 to 20.4) - what somebody who reads along, repeats, forges or stands in between gets out of
// it, and what a stranger may cost a server.
static partial class UdpTests
{
    /// <summary>A channel whose incoming datagrams somebody in between may change.</summary>
    private sealed class Tampered(IDatagramChannel inner, Func<byte[], byte[]> change) : IDatagramChannel
    {
        public void Send(ReadOnlySpan<byte> datagram, IPEndPoint to) => inner.Send(datagram, to);

        public bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from)
        {
            if (!inner.TryReceive(buffer, out length, out from)) return false;
            var changed = change(buffer[..length].ToArray());
            changed.CopyTo(buffer);
            length = changed.Length;
            return true;
        }
    }

    private static bool Holds(byte[] datagram, ReadOnlySpan<byte> marker) => datagram.AsSpan().IndexOf(marker) >= 0;

    /// <summary>
    /// What the fuzz suite needs of this harness: a server with one connected client, a stranger, a way to send from the client's own address, and the tape of a real
    /// conversation to change.
    /// </summary>
    internal sealed class FuzzWire
    {
        internal static IPEndPoint ServerAddress => UdpTests.ServerAddress;
        private readonly Wire _wire = new();
        private readonly List<byte[]> _tape = [];

        public FuzzWire()
        {
            _wire.Network.Tap = (_, to, bytes) => { if (to.Equals(ServerAddress) && _recording) _tape.Add(bytes); };
            Client = _wire.Connect();
            Check.That(_wire.Until(() => Client.State == UdpClientState.Connected && _wire.Accepted.Count == 1, 2_000), "the client of the fuzz wire connects");
            Stranger = _wire.Network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.77"), 77));
        }

        private bool _recording = true;
        public UdpServerEndpoint Server => _wire.Server;
        public UdpClientEndpoint Client { get; }
        public UdpLink ServerLink => _wire.Accepted[0];
        public IDatagramChannel Stranger { get; }
        public IDatagramChannel ClientChannel => _wire.Channels[0];

        /// <summary>Everything a client sent a server in a short conversation: handshake, messages on both channels, acknowledgements.</summary>
        public List<byte[]> Record()
        {
            for (var index = 0; index < 20; index++)
            {
                Client.Connection.Send(Payload(index, 30 + index * 60));
                Client.Connection.SendUrgent(Payload(index, 10 + index * 7));
                ServerLink.Send(Payload(index, 300));
                _wire.Step(8);
                while (ServerLink.TryReceive(out _)) { }
                while (Client.Connection.TryReceive(out _)) { }
            }
            _wire.Step(100);
            while (ServerLink.TryReceive(out _)) { }
            _recording = false;
            return _tape;
        }

        /// <summary>Lets time pass; returns how many messages arrived at the server's end of the connection (a changed datagram must deliver none).</summary>
        public int Step(int milliseconds = 1)
        {
            _wire.Step(milliseconds);
            var count = 0;
            while (_wire.Accepted.Count > 1 && _wire.Accepted[^1].TryReceive(out _)) count++;
            return count;
        }
    }

    public static void SealedTransport()
    {
        // ---- Somebody reads along: every datagram of the connection is on the tape.
        var wire = new Wire();
        var tape = new List<(IPEndPoint From, IPEndPoint To, byte[] Bytes)>();
        wire.Network.Tap = (from, to, bytes) => tape.Add((from, to, bytes));
        byte[]? shown = null;
        var client = wire.Connect(trust: key => { shown = key; return true; });
        Check.That(wire.Until(() => client.State == UdpClientState.Connected && wire.Accepted.Count == 1, 2_000), "the client connects");
        var server = wire.Accepted[0];
        Check.That(shown is not null && shown.AsSpan().SequenceEqual(wire.Server.Identity.PublicKey) && client.ServerFingerprint == wire.Server.Identity.Fingerprint && client.ServerFingerprint.Length == 64,
            "the client is shown the identity key the server holds, and its fingerprint");
        var secret = "the rejoin token and the room's secret words"u8.ToArray();
        for (var index = 0; index < 30; index++)
        {
            client.Connection.Send([..Payload(index, 40), ..secret]);
            client.Connection.SendUrgent([..secret, ..Payload(index, 20)]);
            server.Send([..secret, ..Payload(index, 900)]);
            server.SendUrgent([..Payload(index, 10), ..secret]);
            wire.Step(5);
        }
        wire.Step(200);
        int up = 0, down = 0;
        while (server.TryReceive(out var message)) if (Holds(message, secret)) up++;
        while (client.Connection.TryReceive(out var message)) if (Holds(message, secret)) down++;
        Check.That(up == 60 && down == 60, $"both ends read every message ({up}, {down})");
        var data = tape.Where(entry => entry.Bytes.Length > 3 && entry.Bytes[3] is (byte)UdpKind.Data).ToList();
        Check.That(data.Count >= 60 && tape.All(entry => !Holds(entry.Bytes, secret)), $"nothing of what was said is readable on the line ({data.Count} datagrams)");
        Check.That(data.All(entry => entry.Bytes.Length is <= UdpFormat.MaxDatagramBytes and >= UdpFormat.DataHeaderBytes + UdpFormat.SealBytes), "no datagram is larger than before, seal included");
        // The token of the connection is readable - it says whose datagram it is - and worth nothing without the keys.
        Check.That(data.All(entry => BinaryPrimitives.ReadUInt64LittleEndian(entry.Bytes.AsSpan(4)) == client.Link.Token), "the header stays readable");

        // ---- Every datagram has a number of its own, each direction counts upward, and a repetition is a new datagram.
        foreach (var direction in new[] { wire.Addresses[0], ServerAddress })
        {
            var numbers = data.Where(entry => entry.From.Equals(direction)).Select(entry => BinaryPrimitives.ReadUInt64LittleEndian(entry.Bytes.AsSpan(12))).ToList();
            Check.That(numbers.Count > 0 && numbers.SequenceEqual(numbers.Order()) && numbers.Distinct().Count() == numbers.Count && numbers[0] == 1, "datagram numbers count upward from 1 and never repeat");
        }
        var lossy = new Wire(new Impairment(DelayMs: 20, LossPerMille: 300), 9);
        var lossyTape = new List<byte[]>();
        lossy.Network.Tap = (from, _, bytes) => { if (from.Equals(ServerAddress) && bytes[3] == (byte)UdpKind.Data) lossyTape.Add(bytes); };
        var far = lossy.Connect();
        Check.That(lossy.Until(() => far.State == UdpClientState.Connected && lossy.Accepted.Count == 1, 20_000), "connected through the lossy network");
        lossy.Accepted[0].Send(Payload(1, 20_000));
        var got = Array.Empty<byte>();
        Check.That(lossy.Until(() => far.Connection.TryReceive(out got), 60_000) && got.AsSpan().SequenceEqual(Payload(1, 20_000)), "a large message arrives through loss");
        var sealedNumbers = lossyTape.Select(bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12))).ToList();
        Check.That(lossy.Accepted[0].Statistics.SegmentsRetransmitted > 0 && sealedNumbers.Distinct().Count() == sealedNumbers.Count
                   && lossyTape.Select(Convert.ToHexString).Distinct().Count() == lossyTape.Count,
            $"a segment sent again is a datagram with a new number and other bytes ({lossy.Accepted[0].Statistics.SegmentsRetransmitted} repetitions, {lossyTape.Count} datagrams)");

        // ---- Somebody repeats what it read, from the client's own address: every datagram is refused as a repetition and nothing arrives twice.
        var before = server.Statistics.DatagramsReceived;
        var fromClient = data.Where(entry => entry.From.Equals(wire.Addresses[0])).ToList();
        foreach (var entry in fromClient) wire.Channels[0].Send(entry.Bytes, ServerAddress);
        wire.Step(5);
        Check.That(wire.Server.Replayed == fromClient.Count && !server.TryReceive(out _), $"repeated datagrams are counted and deliver nothing ({wire.Server.Replayed} of {fromClient.Count})");
        // A datagram older than the window is a repetition too, whatever it was.
        Check.That(server.IsOpen && client.Connection.IsOpen, "the connection is untouched");

        // ---- Somebody forges: a datagram with a fresh number and changed content, a body of its own under a real header, a close.
        var forgedBefore = wire.Server.Forged;
        var sample = fromClient[^1].Bytes;
        for (var position = 12; position < sample.Length; position += 5)
        {
            var forged = (byte[])sample.Clone();
            BinaryPrimitives.WriteUInt64LittleEndian(forged.AsSpan(12), 1_000_000UL + (ulong)position);
            forged[Math.Max(20, position)] ^= 0x40;
            wire.Channels[0].Send(forged, ServerAddress);
        }
        var close = new byte[UdpFormat.CloseBytes + UdpFormat.SealBytes];
        sample.AsSpan(0, 12).CopyTo(close);
        close[3] = (byte)UdpKind.Close;
        BinaryPrimitives.WriteUInt64LittleEndian(close.AsSpan(12), 2_000_000);
        wire.Channels[0].Send(close, ServerAddress);
        // The header of a real datagram with its number changed: the header is covered by the seal.
        var renumbered = (byte[])sample.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(renumbered.AsSpan(12), 3_000_000);
        wire.Channels[0].Send(renumbered, ServerAddress);
        wire.Step(5);
        Check.That(wire.Server.Forged - forgedBefore >= (sample.Length - 12 + 4) / 5 + 2 && server.IsOpen && !server.TryReceive(out _),
            $"forged datagrams - changed, renumbered, a close under the connection's token - are counted and do nothing ({wire.Server.Forged - forgedBefore})");
        // A forged number far ahead must not make the real datagrams look old.
        client.Connection.Send(Payload(7, 50));
        Check.That(wire.Until(() => server.TryReceive(out var real) && real.AsSpan().SequenceEqual(Payload(7, 50)), 1_000), "the real client is still heard after the forgeries");

        // ---- Somebody replays the whole conversation from another address - and from the client's own, once the client is gone: no connection it can use.
        var attackerAddress = new IPEndPoint(IPAddress.Parse("10.0.0.99"), 99);
        var attacker = wire.Network.Open(attackerAddress);
        var connectionsBefore = wire.Server.Connections;
        foreach (var entry in tape.Where(entry => entry.To.Equals(ServerAddress)).ToList()) attacker.Send(entry.Bytes, ServerAddress);
        wire.Step(5);
        Check.That(wire.Server.Connections == connectionsBefore && wire.Accepted.Count == 1, "the client's handshake is worth nothing from another address (the cookie is the address's)");
        wire.Network.Cut(ServerAddress, wire.Addresses[0]);
        wire.Step(UdpOptions.Default.TimeoutMs + 200);
        Check.That(wire.Server.Connections == 0, "the client is gone");
        wire.Network.Cut(ServerAddress, wire.Addresses[0], false);
        // From the client's own address the cookie of the tape may still be good: the server answers the repeated connect with a new exchange - whose keys nobody
        // derives without the client's secret half. Everything else on the tape belongs to the old keys. The connection hears nothing and is forgotten.
        var accepted = wire.Accepted.Count;
        foreach (var entry in tape.Where(entry => entry.To.Equals(ServerAddress)).ToList()) wire.Channels[0].Send(entry.Bytes, ServerAddress);
        wire.Step(50);
        Check.That(wire.Accepted.Count <= accepted + 1 && wire.Accepted.Skip(accepted).All(link => !link.TryReceive(out _) && link.Statistics.DatagramsReceived == 0),
            "a repeated handshake gives the one who repeats it a connection it cannot speak on");
        wire.Step(UdpOptions.Default.TimeoutMs + 200);
        Check.That(wire.Server.Connections == 0, "which the server forgets");

        // ---- Somebody stands in between and changes the server's key of the exchange: the signature does not hold, and the client connects to nobody.
        var meddled = new Wire();
        var victim = meddled.Connect(wrap: channel => new Tampered(channel, datagram =>
        {
            if (datagram.Length == UdpFormat.AcceptBytes && datagram[3] == (byte)UdpKind.Accept) datagram[30] ^= 1;
            return datagram;
        }));
        meddled.Step(UdpOptions.Default.ConnectTimeoutMs + 50);
        Check.That(victim.State == UdpClientState.Failed && victim.Malformed > 0 && victim.ServerKey is null, $"an accept whose key was changed on the way is not an accept ({victim.State})");
        // ... or puts its own key and signature in: a valid exchange - with a server the client does not know.
        using var impostor = UdpIdentity.Create();
        var known = new KnownServers();
        Check.That(known.Check(ServerAddress, wire.Server.Identity.Fingerprint) == ServerTrust.New && known.Check(ServerAddress, wire.Server.Identity.Fingerprint) == ServerTrust.Known
                   && known.Check(ServerAddress, impostor.Fingerprint) == ServerTrust.Changed && known.FingerprintOf(ServerAddress) == wire.Server.Identity.Fingerprint,
            "a client remembers the key of a server it meets, and tells a changed one");
        var fake = new Wire(identity: impostor);
        var careful = fake.Connect(trust: key => known.Check(ServerAddress, UdpCrypto.FingerprintOf(key)) != ServerTrust.Changed);
        fake.Step(500);
        Check.That(careful.State == UdpClientState.Failed && careful.Link.CloseReason == UdpCloseReason.Untrusted && careful.ServerFingerprint == impostor.Fingerprint && !careful.Connection.IsOpen,
            "a server with another key than the remembered one is refused, and the client can say which key it showed");
        fake.Step(UdpOptions.Default.TimeoutMs + 200);
        Check.That(fake.Server.Connections == 0, "the server it refused hears nothing more and forgets the connection");

        // ---- The identity survives its file form; another process with the same file is the same server.
        using var original = UdpIdentity.Create();
        using var fromPem = UdpIdentity.ImportPem(original.ExportPem());
        using var fromBytes = UdpIdentity.Import(original.ExportPrivateKey());
        Check.That(fromPem.Fingerprint == original.Fingerprint && fromBytes.Fingerprint == original.Fingerprint && original.Fingerprint != impostor.Fingerprint, "a key read from its file is the same key");
        var failed = false;
        try { UdpIdentity.ImportPem("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----"); }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or ArgumentException) { failed = true; }
        Check.That(failed, "a file that holds no key is refused");
        var restarted = new Wire(identity: fromPem);
        var returning = restarted.Connect(trust: key => UdpCrypto.FingerprintOf(key) == original.Fingerprint);
        Check.That(restarted.Until(() => returning.State == UdpClientState.Connected, 2_000), "a client that pinned the key connects to the restarted server");
    }

    public static void TransportLimits()
    {
        var buffer = new byte[2048];
        int Drain(IDatagramChannel channel)
        {
            var count = 0;
            while (channel.TryReceive(buffer, out _, out _)) count++;
            return count;
        }

        // ---- One address: twenty requests at once are answered, the rest is dropped unanswered; ten more every second.
        var wire = new Wire(options: UdpOptions.Default);
        var stranger = wire.Network.Open(new IPEndPoint(IPAddress.Parse("10.0.0.50"), 50));
        for (var index = 0; index < 500; index++) stranger.Send(UdpFormat.Request((ulong)index + 1), ServerAddress);
        wire.Step(2);
        Check.That(Drain(stranger) == 20 && wire.Server.RateLimited == 480 && wire.Server.Requests == 20, $"of 500 requests at once 20 are answered ({wire.Server.Requests}, {wire.Server.RateLimited} dropped)");
        wire.Step(1_000);
        for (var index = 0; index < 500; index++) stranger.Send(UdpFormat.Request((ulong)index + 1), ServerAddress);
        wire.Step(2);
        Check.That(Drain(stranger) == 10, "a second later ten more");
        // Another address is not held up by the first.
        var other = wire.Connect(address: new IPEndPoint(IPAddress.Parse("10.0.0.51"), 51));
        Check.That(wire.Until(() => other.State == UdpClientState.Connected, 2_000), "somebody else connects while the first address is over its rate");

        // ---- Forged senders: whatever addresses they claim, all together get a bounded number of answers; and the table of addresses does not grow without end.
        var flooded = new Wire(options: UdpOptions.Default with { HandshakesPerSecond = 50, MaxTrackedAddresses = 64 });
        var answers = 0;
        for (var index = 0; index < 400; index++)
        {
            var forged = flooded.Network.Open(new IPEndPoint(new IPAddress([172, 16, (byte)(index / 250), (byte)(index % 250 + 1)]), 7));
            forged.Send(UdpFormat.Request(5), ServerAddress);
            if (index % 100 == 99)
            {
                flooded.Step(1);
                answers = (int)flooded.Server.Requests;
            }
        }
        flooded.Step(1);
        Check.That(flooded.Server.Requests is >= 50 and <= 52 && flooded.Server.RateLimited >= 348 && flooded.Server.Connections == 0,
            $"400 requests from 400 addresses in a few milliseconds: about 50 answers ({flooded.Server.Requests}), no state ({answers})");

        // ---- One address holds as many connections as it may, and not one more.
        var household = new Wire(options: Unbounded with { MaxConnectionsPerAddress = 2 });
        var first = household.Connect();
        var second = household.Connect();
        Check.That(household.Until(() => first.State == UdpClientState.Connected && second.State == UdpClientState.Connected, 2_000), "two connections of one address");
        var third = household.Connect();
        var neighbour = household.Connect(address: new IPEndPoint(IPAddress.Parse("10.0.0.3"), 5));
        Check.That(household.Until(() => third.State == UdpClientState.Failed && neighbour.State == UdpClientState.Connected, 2_000) && third.Link.CloseReason == UdpCloseReason.Refused
                   && household.Server.Refused >= 1, "the third of the same address is refused, the neighbour is not");
        first.Connection.Close();
        household.Step(UdpOptions.Default.LingerMs + 100);
        var again = household.Connect();
        Check.That(household.Until(() => again.State == UdpClientState.Connected, 2_000), "a place that was given up is free again");

        // ---- A banned address gets no answer at all; a ban that comes later closes what stands.
        var banned = new HashSet<IPAddress> { IPAddress.Parse("10.0.0.66") };
        var guarded = new Wire(isBanned: banned.Contains);
        var unwelcome = guarded.Connect(address: new IPEndPoint(IPAddress.Parse("10.0.0.66"), 6));
        var welcome = guarded.Connect();
        guarded.Step(1_000);
        Check.That(unwelcome.State == UdpClientState.Connecting && guarded.Server.Banned >= 3 && guarded.Server.Requests == 1 && welcome.State == UdpClientState.Connected,
            $"a banned address is not answered ({guarded.Server.Banned} datagrams dropped)");
        banned.Add(IPAddress.Parse("10.0.0.2"));
        Check.That(guarded.Server.CloseWhere(banned.Contains) == 1, "a new ban closes the connection that stands");
        guarded.Step(200);
        Check.That(welcome.State == UdpClientState.Closed, "and its client learns of it");

        // ---- A connection may send so much: a burst, then a rate; what is beyond is dropped like loss, and a peer that keeps it up is closed.
        var policed = new Wire(options: Unbounded with { InboundBytesPerSecond = 20_000, InboundBurstBytes = 100_000, PoliceLimitMs = 3_000 });
        var greedy = policed.Connect();
        var modest = policed.Connect();
        Check.That(policed.Until(() => greedy.State == UdpClientState.Connected && modest.State == UdpClientState.Connected && policed.Accepted.Count == 2, 2_000), "connected");
        greedy.Connection.Send(Payload(1, 90_000));
        var burst = Array.Empty<byte>();
        Check.That(policed.Until(() => policed.Accepted[0].TryReceive(out burst), 3_000) && burst.Length == 90_000 && policed.Accepted[0].Statistics.Policed == 0, "a state-sized burst passes at once");
        for (var index = 0; index < 40; index++) greedy.Connection.Send(Payload(index, 50_000));
        policed.Until(() => !policed.Accepted[0].IsOpen, 30_000);
        Check.That(policed.Accepted[0].CloseReason == UdpCloseReason.Flood && policed.Accepted[0].Statistics.Policed > 50, $"a peer that sends far beyond its rate for seconds is closed ({policed.Accepted[0].CloseReason}, {policed.Accepted[0].Statistics.Policed} datagrams dropped)");
        modest.Connection.Send(Payload(3, 500));
        Check.That(policed.Until(() => policed.Accepted[1].TryReceive(out _), 1_000) && policed.Accepted[1].IsOpen, "the connection beside it is untouched");

        // ---- A peer that acknowledges nothing does not make its sender keep everything: the backlog is bounded.
        var deaf = new Wire(options: Unbounded with { MaxReliableBacklogBytes = 200_000 });
        var silent = deaf.Connect();
        deaf.Until(() => silent.State == UdpClientState.Connected && deaf.Accepted.Count == 1, 2_000);
        deaf.Network.Cut(ServerAddress, deaf.Addresses[0]);
        for (var index = 0; index < 20 && deaf.Accepted[0].IsOpen; index++)
        {
            deaf.Accepted[0].Send(Payload(index, 60_000));
            deaf.Step(10);
        }
        Check.That(!deaf.Accepted[0].IsOpen && deaf.Accepted[0].CloseReason == UdpCloseReason.Flood && deaf.Accepted[0].ReliableBacklogBytes < 2_000_000, "a backlog beyond its bound closes the connection");
    }
}
