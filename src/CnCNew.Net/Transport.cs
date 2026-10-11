namespace CnCNew.Net;

/// <summary>
/// The only clock of the network layers: milliseconds that only ever grow. Nothing in <c>CnCNew.Net</c> or <c>CnCNew.Server</c> reads wall-clock time by itself (a source
/// test enforces it); a host injects a clock, a test advances one by hand. The clock paces - it never enters the simulation.
/// </summary>
public interface INetClock
{
    long Milliseconds { get; }

    /// <summary>The same clock in microseconds, for whoever measures finer than a tick (clock synchronisation, playback control); a clock that only knows milliseconds scales them.</summary>
    long Microseconds => Milliseconds * 1000;
}

/// <summary>A clock that stands until it is told to move: tests, tools and the embedded server of a local game.</summary>
public sealed class ManualNetClock : INetClock
{
    public long Milliseconds { get; private set; }
    public long Microseconds => Milliseconds * 1000;

    public void Advance(long milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        Milliseconds += milliseconds;
    }
}

/// <summary>
/// One end of a connection: whole messages, delivered reliably, once, and in the order they were sent - per channel. There are two channels (docs/NETWORK-PROTOCOL.md
/// section 12): the ordinary one, and the urgent one for what must not wait behind a lost packet (the tick bundles). Order holds within a channel, not between them.
/// </summary>
public interface IConnection
{
    bool IsOpen { get; }
    void Send(ReadOnlySpan<byte> message);

    /// <summary>Sends on the urgent channel: delivered like any message, but a transport over a lossy network repeats it with everything that follows until it is acknowledged.</summary>
    void SendUrgent(ReadOnlySpan<byte> message);

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
/// Connections inside one process. Every connection is reliable and ordered per channel; <b>between</b> connections, and between the two channels of one, delay and jitter
/// make messages overtake each other, as on a real network. The jitter is drawn from the network's own generator, seeded by the test - never from a stream of the simulation. With <see cref="LinkQuality.Instant"/> a
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
        // One lane per channel; a message is handed out by due time, and among equals in the order of sending - so an instant link keeps the order across both.
        private readonly Queue<(long DueMs, long Order, byte[] Message)>[] _incoming = [new(), new()];
        private readonly long[] _lastDue = new long[2];
        private bool _closed;

        public End Peer { get; set; } = null!;
        public bool IsOpen => !_closed;

        public void Send(ReadOnlySpan<byte> message) => Send(message, 0);
        public void SendUrgent(ReadOnlySpan<byte> message) => Send(message, 1);

        private void Send(ReadOnlySpan<byte> message, int lane)
        {
            if (_closed || Peer._closed) return;
            // Ordered per channel: a message is never due before the one sent before it on its lane.
            var due = Math.Max(Peer._lastDue[lane], network.Clock + outgoing.DelayMs + network.Jitter(outgoing.JitterMs));
            Peer._lastDue[lane] = due;
            Peer._incoming[lane].Enqueue((due, network._sent, message.ToArray()));
            network._sent++;
            network.BytesSent += message.Length;
        }

        public bool TryReceive(out byte[] message)
        {
            var lane = -1;
            for (var index = 0; index < 2; index++)
            {
                if (_incoming[index].Count == 0 || _incoming[index].Peek().DueMs > network.Clock) continue;
                if (lane < 0 || (_incoming[index].Peek().DueMs, _incoming[index].Peek().Order).CompareTo((_incoming[lane].Peek().DueMs, _incoming[lane].Peek().Order)) < 0) lane = index;
            }
            if (lane >= 0)
            {
                message = _incoming[lane].Dequeue().Message;
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
