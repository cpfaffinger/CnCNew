using System.Net;

namespace CnCNew.Net.Udp;

/// <summary>What a network does to the datagrams of one direction. Rates are in thousandths; everything is drawn from the impairer's own generator.</summary>
/// <param name="DelayMs">Fixed delay.</param>
/// <param name="JitterMs">Additional delay, drawn per datagram between 0 and this value (so datagrams overtake each other).</param>
/// <param name="LossPerMille">How many of a thousand datagrams are lost.</param>
/// <param name="DuplicatePerMille">How many of a thousand are delivered twice.</param>
/// <param name="ReorderPerMille">How many of a thousand are held back by <paramref name="ReorderExtraMs"/> on top.</param>
/// <param name="RateKbps">A narrow line: so many kilobits per second pass, one datagram after the other; 0 is no limit.</param>
/// <param name="QueueBytes">What waits before the narrow line at most; a datagram that finds the queue full is dropped, as a router does.</param>
public readonly record struct Impairment(int DelayMs = 0, int JitterMs = 0, int LossPerMille = 0, int DuplicatePerMille = 0, int ReorderPerMille = 0, int ReorderExtraMs = 50,
    int RateKbps = 0, int QueueBytes = 64_000)
{
    public static Impairment None => default;
    public bool IsNone => DelayMs == 0 && JitterMs == 0 && LossPerMille == 0 && DuplicatePerMille == 0 && ReorderPerMille == 0 && RateKbps == 0;
}

/// <summary>
/// Delays, loses, duplicates and reorders datagrams, reproducibly from a seed: the same datagrams offered at the same clock readings leave in the same way. Used by the
/// in-memory network of the tests and by the UDP proxy of <c>CnCNew.NetTool</c>, so both disturb in the same manner. <typeparamref name="T"/> is where a datagram goes.
/// </summary>
public sealed class DatagramImpairer<T>(INetClock clock, Impairment impairment, ulong seed)
{
    private readonly PriorityQueue<(byte[] Datagram, T Tag), (long DueMs, long Order)> _queue = new();
    private ulong _random = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    private long _order;

    public long Offered { get; private set; }
    public long Lost { get; private set; }
    public long Duplicated { get; private set; }
    public long Reordered { get; private set; }

    /// <summary>Datagrams dropped because the queue before the narrow line was full.</summary>
    public long Overflowed { get; private set; }
    public int Queued => _queue.Count;
    private long _lineFreeMicros;

    private int Draw(int bound)
    {
        // xorshift64*: this class's own generator.
        _random ^= _random >> 12;
        _random ^= _random << 25;
        _random ^= _random >> 27;
        return (int)((_random * 0x2545F4914F6CDD1DUL >> 33) % (ulong)bound);
    }

    public void Offer(ReadOnlySpan<byte> datagram, T tag)
    {
        Offered++;
        // Every datagram draws the same number of values, whatever happens to it: one datagram's fate does not shift the next one's.
        var loss = Draw(1000);
        var duplicate = Draw(1000);
        var reorder = Draw(1000);
        var jitter = Draw(impairment.JitterMs + 1);
        var jitterOfCopy = Draw(impairment.JitterMs + 1);
        if (loss < impairment.LossPerMille)
        {
            Lost++;
            return;
        }
        var bytes = datagram.ToArray();
        var due = clock.Milliseconds + impairment.DelayMs + jitter;
        if (impairment.RateKbps > 0)
        {
            // The line sends one datagram after the other; what is offered while it is busy waits, and what would wait behind a full queue is dropped.
            var now = clock.Microseconds;
            var free = Math.Max(_lineFreeMicros, now);
            if ((free - now) * impairment.RateKbps / 8_000 > impairment.QueueBytes)
            {
                Overflowed++;
                return;
            }
            _lineFreeMicros = free + bytes.Length * 8_000L / impairment.RateKbps;
            due += (_lineFreeMicros - now) / 1000;
        }
        if (reorder < impairment.ReorderPerMille)
        {
            Reordered++;
            due += impairment.ReorderExtraMs;
        }
        _queue.Enqueue((bytes, tag), (due, _order++));
        if (duplicate < impairment.DuplicatePerMille)
        {
            Duplicated++;
            _queue.Enqueue((bytes, tag), (clock.Milliseconds + impairment.DelayMs + jitterOfCopy, _order++));
        }
    }

    public bool TryTake(out byte[] datagram, out T tag)
    {
        if (_queue.TryPeek(out var next, out var priority) && priority.DueMs <= clock.Milliseconds)
        {
            _queue.Dequeue();
            (datagram, tag) = next;
            return true;
        }
        datagram = [];
        tag = default!;
        return false;
    }

    /// <summary>Milliseconds until the next datagram is due; -1 when none waits.</summary>
    public long NextDueInMs => _queue.TryPeek(out _, out var priority) ? Math.Max(0, priority.DueMs - clock.Milliseconds) : -1;
}

/// <summary>A datagram network in memory for tests: channels at addresses, every direction with its own impairment, one clock. Nothing leaves the process.</summary>
public sealed class MemoryDatagramNetwork(INetClock clock, ulong seed = 1)
{
    private readonly Dictionary<IPEndPoint, Channel> _channels = [];
    private readonly Dictionary<(IPEndPoint From, IPEndPoint To), DatagramImpairer<IPEndPoint>> _paths = [];
    private readonly Dictionary<(IPEndPoint From, IPEndPoint To), Impairment> _impairments = [];
    private ulong _seed = seed;

    /// <summary>The impairment of every direction that has none of its own.</summary>
    public Impairment Default { get; set; }

    public long Datagrams { get; private set; }
    public long Bytes { get; private set; }

    public IDatagramChannel Open(IPEndPoint address)
    {
        var channel = new Channel(this, address);
        _channels.Add(address, channel);
        return channel;
    }

    /// <summary>Sets the impairment of one direction; from now on, for datagrams not yet sent.</summary>
    public void Impair(IPEndPoint from, IPEndPoint to, Impairment impairment)
    {
        _impairments[(from, to)] = impairment;
        _paths.Remove((from, to));
    }

    /// <summary>Total loss between two addresses in both directions, or its end.</summary>
    public void Cut(IPEndPoint a, IPEndPoint b, bool cut = true)
    {
        Impair(a, b, cut ? new Impairment(LossPerMille: 1000) : Default);
        Impair(b, a, cut ? new Impairment(LossPerMille: 1000) : Default);
    }

    public long Lost => _paths.Values.Sum(path => path.Lost);

    private DatagramImpairer<IPEndPoint> Path(IPEndPoint from, IPEndPoint to)
    {
        if (!_paths.TryGetValue((from, to), out var path))
            _paths[(from, to)] = path = new DatagramImpairer<IPEndPoint>(clock, _impairments.GetValueOrDefault((from, to), Default), _seed = unchecked(_seed * 6364136223846793005UL + 1442695040888963407UL));
        return path;
    }

    private sealed class Channel(MemoryDatagramNetwork network, IPEndPoint address) : IDatagramChannel
    {
        public void Send(ReadOnlySpan<byte> datagram, IPEndPoint to)
        {
            network.Datagrams++;
            network.Bytes += datagram.Length;
            network.Path(address, to).Offer(datagram, address);
        }

        public bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from)
        {
            // Every path that ends here, in a fixed order: the address of the sender.
            foreach (var (key, path) in network._paths.Where(pair => pair.Key.To.Equals(address)).OrderBy(pair => pair.Key.From.Port).ThenBy(pair => pair.Key.From.Address.ToString(), StringComparer.Ordinal))
            {
                if (!path.TryTake(out var datagram, out from)) continue;
                // A datagram longer than the buffer arrives cut off, as a socket hands it over.
                length = Math.Min(datagram.Length, buffer.Length);
                datagram.AsSpan(0, length).CopyTo(buffer);
                return true;
            }
            length = 0;
            from = null!;
            return false;
        }
    }
}
