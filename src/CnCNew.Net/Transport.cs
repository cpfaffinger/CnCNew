namespace CnCNew.Net;

/// <summary>
/// The only clock of the network layers: milliseconds that only ever grow. Nothing in <c>CnCNew.Net</c> or <c>CnCNew.Server</c> reads wall-clock time by itself (a source
/// test enforces it); a host injects a clock, a test advances one by hand. The clock paces - it never enters the simulation.
/// </summary>
public interface INetClock
{
    long Milliseconds { get; }
}

/// <summary>A clock that stands until it is told to move: tests, tools and the embedded server of a local game.</summary>
public sealed class ManualNetClock : INetClock
{
    public long Milliseconds { get; private set; }

    public void Advance(long milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        Milliseconds += milliseconds;
    }
}

/// <summary>One end of a connection: whole messages, delivered reliably and in the order they were sent on this connection.</summary>
public interface IConnection
{
    bool IsOpen { get; }
    void Send(ReadOnlySpan<byte> message);

    /// <summary>The next message that has arrived, if any. A closed connection still hands out what arrived before it closed.</summary>
    bool TryReceive(out byte[] message);

    void Close();
}

/// <summary>How a loopback connection delays what it carries, in milliseconds of the network's clock.</summary>
/// <param name="DelayMs">Fixed delay of every message in this direction.</param>
/// <param name="JitterMs">Additional delay, drawn per message between 0 and this value.</param>
public readonly record struct LinkQuality(int DelayMs = 0, int JitterMs = 0)
{
    public static LinkQuality Instant => default;
}

/// <summary>
/// Connections inside one process. Every connection is reliable and ordered in itself; <b>between</b> connections, delay and jitter make messages overtake each other, as on a
/// real network. The jitter is drawn from the network's own generator, seeded by the test - never from a stream of the simulation. With <see cref="LinkQuality.Instant"/> a
/// message is there as soon as it is sent.
/// </summary>
public sealed class LoopbackNetwork(INetClock clock, uint seed = 1)
{
    private ulong _random = seed == 0 ? 1UL : seed;
    private long _sent;

    public long MessagesSent => _sent;
    public long BytesSent { get; private set; }

    /// <summary>A new connection; the first end is the client's, the second the server's. <paramref name="toServer"/> and <paramref name="toClient"/> delay their direction.</summary>
    public (IConnection Client, IConnection Server) Connect(LinkQuality toServer = default, LinkQuality toClient = default)
    {
        var client = new End(this, toServer);
        var server = new End(this, toClient);
        client.Peer = server;
        server.Peer = client;
        return (client, server);
    }

    private int Jitter(int max)
    {
        if (max <= 0) return 0;
        // xorshift64*: a generator of this class's own, so that nothing here can touch a stream of the simulation.
        _random ^= _random >> 12;
        _random ^= _random << 25;
        _random ^= _random >> 27;
        return (int)((_random * 0x2545F4914F6CDD1DUL >> 33) % (ulong)(max + 1));
    }

    private sealed class End(LoopbackNetwork network, LinkQuality outgoing) : IConnection
    {
        private readonly Queue<(long DueMs, byte[] Message)> _incoming = new();
        private long _lastDue;
        private bool _closed;

        public End Peer { get; set; } = null!;
        public bool IsOpen => !_closed;

        public void Send(ReadOnlySpan<byte> message)
        {
            if (_closed || Peer._closed) return;
            // Ordered per connection: a message is never due before the one sent before it.
            var due = Math.Max(Peer._lastDue, network.Clock + outgoing.DelayMs + network.Jitter(outgoing.JitterMs));
            Peer._lastDue = due;
            Peer._incoming.Enqueue((due, message.ToArray()));
            network._sent++;
            network.BytesSent += message.Length;
        }

        public bool TryReceive(out byte[] message)
        {
            if (_incoming.Count > 0 && _incoming.Peek().DueMs <= network.Clock)
            {
                message = _incoming.Dequeue().Message;
                return true;
            }
            message = [];
            return false;
        }

        public void Close()
        {
            _closed = true;
            Peer._closed = true;
        }
    }

    private long Clock => clock.Milliseconds;
}
