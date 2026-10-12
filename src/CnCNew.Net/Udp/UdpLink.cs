using System.Buffers.Binary;
using System.Numerics;

namespace CnCNew.Net.Udp;

/// <summary>What a connection counted (docs/NETWORK-PROTOCOL.md section 12.9).</summary>
public sealed class UdpLinkStatistics
{
    public long DatagramsSent { get; internal set; }
    public long DatagramsReceived { get; internal set; }
    public long BytesSent { get; internal set; }
    public long BytesReceived { get; internal set; }
    public long SegmentsSent { get; internal set; }
    public long SegmentsRetransmitted { get; internal set; }
    public long UrgentSent { get; internal set; }
    public long UrgentRepeated { get; internal set; }
    public long UrgentOmitted { get; internal set; }
    public long DuplicatesReceived { get; internal set; }
    public long Malformed { get; internal set; }
    /// <summary>Datagrams dropped because the peer sent more than a connection may receive (docs/NETWORK-PROTOCOL.md section 20.4).</summary>
    public long Policed { get; internal set; }
    /// <summary>The smoothed round-trip time in milliseconds; -1 before the first measurement.</summary>
    public int RoundTripMs { get; internal set; } = -1;
}

public enum UdpCloseReason
{
    None,
    Local,
    Peer,
    Timeout,
    Violation,
    Refused,
    Replaced,
    /// <summary>The server's identity key is not the one this client knows for it, or its signature is wrong.</summary>
    Untrusted,
    /// <summary>The peer sent more than a connection may, or left too much unacknowledged, for too long.</summary>
    Flood,
}

/// <summary>
/// One connection of the UDP transport as a state machine without a socket: it is told what arrived and when (<see cref="Receive"/>), and asked what to send now
/// (<see cref="Flush"/>). The two channels of <see cref="IConnection"/> are the reliable byte stream of framed messages and the urgent messages that ride along in every
/// datagram until acknowledged (docs/NETWORK-PROTOCOL.md sections 12.4 to 12.7). Single-threaded, like everything in this layer.
/// </summary>
public sealed class UdpLink : IConnection
{
    private sealed class Segment(uint number, byte[] bytes)
    {
        public uint Number { get; } = number;
        public byte[] Bytes { get; } = bytes;
        public long LastSentMs { get; set; }
        public int Sends { get; set; }
        public bool Acknowledged { get; set; }
        public bool FastRetransmitted { get; set; }
        /// <summary>Send again at the next flush, whatever the retransmission time says.</summary>
        public bool Resend { get; set; }
    }

    private sealed class Urgent(uint number, byte[][] fragments)
    {
        public uint Number { get; } = number;
        public byte[][] Fragments { get; } = fragments;
        public long LastSentMs { get; set; }
        public int Sends { get; set; }
    }

    private readonly UdpOptions _options;
    private readonly ByteQueue _outgoing = new();
    private readonly List<Segment> _inFlight = [];
    private readonly List<Urgent> _urgentOut = [];
    private readonly SortedDictionary<uint, byte[]> _heldSegments = [];
    private readonly SortedDictionary<uint, byte[]?[]> _heldUrgent = [];
    private readonly ByteQueue _stream = new();
    private readonly Queue<(long Order, byte[] Message)> _delivered = new();
    private readonly byte[] _datagram = new byte[UdpFormat.MaxPlainBytes];
    private long _allowance;
    private long _allowanceAtMs;
    private long _policedSinceMs = long.MinValue;
    private uint _nextSegment;
    private uint _nextUrgent;
    private uint _expectedSegment;
    private uint _expectedUrgent;
    private long _lastSentMs;
    private long _lastReceivedMs;
    private bool _ackPending;
    private long _ackPendingSinceMs;
    private bool _urgentNew;
    private uint _echoTime;
    private long _echoReceivedMs = long.MinValue;
    private int _srtt = -1;
    private int _rttVar;
    private int _closesToSend;
    private long _order;
    private bool _draining;
    private long _drainUntilMs = long.MinValue;

    public UdpLink(UdpOptions options, long nowMs)
    {
        _options = options;
        _lastSentMs = nowMs;
        _lastReceivedMs = nowMs;
        _allowance = options.InboundBurstBytes;
        _allowanceAtMs = nowMs;
    }

    public UdpLinkStatistics Statistics { get; } = new();

    /// <summary>The token of the connection; 0 until the handshake has given one (nothing is sent before).</summary>
    public ulong Token { get; private set; }
    public UdpCloseReason CloseReason { get; private set; }
    public bool IsOpen => CloseReason == UdpCloseReason.None;

    /// <summary>Whether everything this end had to say has gone out (the close datagrams of a closed link included): the owner may forget the link.</summary>
    public bool IsFinished => !IsOpen && !_draining && _closesToSend == 0;

    /// <summary>Bytes of the reliable stream not yet acknowledged or not yet sent, and urgent messages not yet acknowledged.</summary>
    public long ReliableBacklogBytes => _outgoing.Count + _inFlight.Sum(segment => (long)segment.Bytes.Length);
    public int UrgentBacklog => _urgentOut.Count;

    public void Establish(ulong token, long nowMs)
    {
        Token = token;
        _lastReceivedMs = nowMs;
        _lastSentMs = nowMs - _options.KeepAliveMs;
    }

    // ------------------------------------------------------------------------------------------------------ IConnection

    public void Send(ReadOnlySpan<byte> message)
    {
        if (!IsOpen) return;
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)message.Length);
        _outgoing.Append(length);
        _outgoing.Append(message);
        // A peer that does not acknowledge must not make this end keep everything it was ever told.
        if (_outgoing.Count > _options.MaxReliableBacklogBytes) CloseWith(UdpCloseReason.Flood);
    }

    public void SendUrgent(ReadOnlySpan<byte> message)
    {
        if (!IsOpen) return;
        var count = Math.Max(1, (message.Length + UdpFormat.MaxUrgentFragmentBytes - 1) / UdpFormat.MaxUrgentFragmentBytes);
        if (message.Length == 0 || count > UdpFormat.MaxUrgentFragments)
        {
            // Not expressible on this channel: it goes the reliable way rather than being lost. The protocol's urgent messages never get here (a bundle is bounded below).
            Send(message);
            return;
        }
        var fragments = new byte[count][];
        for (var index = 0; index < count; index++)
        {
            var start = index * UdpFormat.MaxUrgentFragmentBytes;
            fragments[index] = message.Slice(start, Math.Min(UdpFormat.MaxUrgentFragmentBytes, message.Length - start)).ToArray();
        }
        _urgentOut.Add(new Urgent(_nextUrgent++, fragments));
        _urgentNew = true;
        if (_urgentOut.Count > _options.UrgentWindow) CloseWith(UdpCloseReason.Timeout);
    }

    public bool TryReceive(out byte[] message)
    {
        if (_delivered.Count > 0)
        {
            message = _delivered.Dequeue().Message;
            return true;
        }
        message = [];
        return false;
    }

    public void Close() => CloseWith(UdpCloseReason.Local);

    internal void CloseWith(UdpCloseReason reason)
    {
        if (!IsOpen) return;
        CloseReason = reason;
        // The peer is told unless it is the one that closed, or there is no connection to speak of.
        _closesToSend = reason is UdpCloseReason.Peer or UdpCloseReason.Refused || Token == 0 ? 0 : 3;
        // Closed by this end: what was sent before the close still goes out (and is repeated until acknowledged) before the goodbye.
        _draining = reason == UdpCloseReason.Local && Token != 0;
    }

    // ---------------------------------------------------------------------------------------------------------- receive

    /// <summary>Takes a data datagram of this connection (magic, version, kind and token already checked by the endpoint).</summary>
    public void Receive(ReadOnlySpan<byte> datagram, long nowMs)
    {
        if (!IsOpen && !_draining) return;
        if (!UdpFormat.TryReadDataHeader(datagram, out var header) || !UdpFormat.SectionsAreValid(datagram))
        {
            Statistics.Malformed++;
            return;
        }
        if (!IsOpen)
        {
            // Draining: only the acknowledgements matter any more.
            Acknowledge(header);
            return;
        }
        // What a connection may receive: the allowance refills at the long-run rate up to the burst; a datagram beyond it is lost like one the network dropped.
        _allowance = Math.Min(_options.InboundBurstBytes, _allowance + Math.Max(0, nowMs - _allowanceAtMs) * _options.InboundBytesPerSecond / 1000);
        _allowanceAtMs = nowMs;
        var cost = datagram.Length + UdpFormat.SealBytes;
        if (_allowance < cost)
        {
            Statistics.Policed++;
            if (_policedSinceMs == long.MinValue) _policedSinceMs = nowMs;
            else if (nowMs - _policedSinceMs >= _options.PoliceLimitMs) CloseWith(UdpCloseReason.Flood);
            return;
        }
        _allowance -= cost;
        if (_allowance >= _options.InboundBurstBytes / 2) _policedSinceMs = long.MinValue;
        Statistics.DatagramsReceived++;
        Statistics.BytesReceived += cost;
        _lastReceivedMs = nowMs;
        if (_echoReceivedMs == long.MinValue || unchecked((int)(header.SendTime - _echoTime)) > 0)
        {
            _echoTime = header.SendTime;
            _echoReceivedMs = nowMs;
        }
        if (header.EchoHold != UdpFormat.NoEcho)
        {
            var sample = unchecked((int)((uint)nowMs - header.EchoTime)) - header.EchoHold;
            if (sample is >= 0 and < 60_000) TakeRoundTrip(sample);
        }
        Acknowledge(header);
        var position = UdpFormat.DataHeaderBytes;
        while (position < datagram.Length)
        {
            if (datagram[position] == UdpFormat.SectionSegment)
            {
                var number = BinaryPrimitives.ReadUInt32LittleEndian(datagram[(position + 1)..]);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 5)..]);
                TakeSegment(number, datagram.Slice(position + UdpFormat.SegmentOverhead, length));
                position += UdpFormat.SegmentOverhead + length;
            }
            else
            {
                var number = BinaryPrimitives.ReadUInt32LittleEndian(datagram[(position + 1)..]);
                var index = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 5)..]);
                var count = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 7)..]);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 9)..]);
                TakeUrgent(number, index, count, datagram.Slice(position + UdpFormat.UrgentOverhead, length));
                position += UdpFormat.UrgentOverhead + length;
            }
            if (!IsOpen) return;
        }
    }

    private void TakeRoundTrip(int sample)
    {
        if (_srtt < 0)
        {
            _srtt = sample;
            _rttVar = sample / 2;
        }
        else
        {
            _rttVar = (3 * _rttVar + Math.Abs(_srtt - sample)) / 4;
            _srtt = (7 * _srtt + sample) / 8;
        }
        Statistics.RoundTripMs = _srtt;
    }

    // The margin above the round trip is never less than a quarter of it or 15 ms: on a line without jitter the variance goes to zero, and a timer that equals the
    // round trip fires just before every acknowledgement.
    private int RetransmitMs => _srtt < 0 ? _options.InitialRetransmitMs : Math.Clamp(_srtt + Math.Max(4 * _rttVar, Math.Max(_srtt / 4, 15)), _options.MinRetransmitMs, _options.MaxRetransmitMs);

    private void Acknowledge(in UdpDataHeader header)
    {
        // Numbers are compared as distances from what this end knows: an acknowledgement for something never sent is ignored.
        var acked = unchecked((int)(header.ReliableAck - (_inFlight.Count > 0 ? _inFlight[0].Number : _nextSegment)));
        if (acked > 0 && acked <= _inFlight.Count) _inFlight.RemoveRange(0, acked);
        if (_inFlight.Count > 0 && _inFlight[0].Number == header.ReliableAck && header.AckMask != 0)
        {
            for (var bit = 0; bit < 32 && bit + 1 < _inFlight.Count; bit++)
                if ((header.AckMask & (1u << bit)) != 0) _inFlight[bit + 1].Acknowledged = true;
            // Two later segments arrived and this one did not: it is lost, not late.
            var first = _inFlight[0];
            if (!first.FastRetransmitted && first.Sends > 0 && BitOperations.PopCount(header.AckMask) >= 2)
            {
                first.FastRetransmitted = true;
                first.Resend = true;
            }
        }
        var urgentAcked = unchecked((int)(header.UrgentAck - (_urgentOut.Count > 0 ? _urgentOut[0].Number : _nextUrgent)));
        if (urgentAcked > 0 && urgentAcked <= _urgentOut.Count) _urgentOut.RemoveRange(0, urgentAcked);
    }

    private void NoteAckPending()
    {
        if (!_ackPending) _ackPendingSinceMs = _lastReceivedMs;
        _ackPending = true;
    }

    private void TakeSegment(uint number, ReadOnlySpan<byte> bytes)
    {
        NoteAckPending();
        var distance = unchecked((int)(number - _expectedSegment));
        if (distance < 0 || _heldSegments.ContainsKey(number))
        {
            Statistics.DuplicatesReceived++;
            return;
        }
        if (distance >= _options.WindowSegments) return;
        if (distance > 0)
        {
            _heldSegments[number] = bytes.ToArray();
            return;
        }
        _stream.Append(bytes);
        _expectedSegment++;
        while (_heldSegments.Remove(_expectedSegment, out var held))
        {
            _stream.Append(held);
            _expectedSegment++;
        }
        Span<byte> prefix = stackalloc byte[4];
        while (_stream.Count >= 4)
        {
            _stream.Peek(prefix);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            if (length > (uint)_options.MaxMessageBytes)
            {
                Statistics.Malformed++;
                CloseWith(UdpCloseReason.Violation);
                return;
            }
            if (_stream.Count < 4 + (long)length) break;
            _stream.Skip(4);
            _delivered.Enqueue((_order++, _stream.Take((int)length)));
        }
    }

    private void TakeUrgent(uint number, int index, int count, ReadOnlySpan<byte> bytes)
    {
        NoteAckPending();
        var distance = unchecked((int)(number - _expectedUrgent));
        if (distance < 0)
        {
            Statistics.DuplicatesReceived++;
            return;
        }
        if (distance >= _options.UrgentWindow) return;
        if (!_heldUrgent.TryGetValue(number, out var fragments)) _heldUrgent[number] = fragments = new byte[]?[count];
        if (fragments.Length != count)
        {
            // The same message cannot have two fragment counts.
            Statistics.Malformed++;
            CloseWith(UdpCloseReason.Violation);
            return;
        }
        if (fragments[index] is not null)
        {
            Statistics.DuplicatesReceived++;
            return;
        }
        fragments[index] = bytes.ToArray();
        while (_heldUrgent.TryGetValue(_expectedUrgent, out var next) && Array.TrueForAll(next, fragment => fragment is not null))
        {
            _heldUrgent.Remove(_expectedUrgent);
            _expectedUrgent++;
            if (next.Length == 1)
            {
                _delivered.Enqueue((_order++, next[0]!));
                continue;
            }
            var whole = new byte[next.Sum(fragment => fragment!.Length)];
            var position = 0;
            foreach (var fragment in next)
            {
                fragment!.CopyTo(whole, position);
                position += fragment.Length;
            }
            _delivered.Enqueue((_order++, whole));
        }
    }

    // ------------------------------------------------------------------------------------------------------------- send

    /// <summary>Sends what is due now: new segments within the window, repetitions, urgent messages with everything unacknowledged, acknowledgements, keep-alive, close.</summary>
    public void Flush(long nowMs, Action<ReadOnlySpan<byte>> send)
    {
        if (Token == 0) return;
        if (!IsOpen)
        {
            if (_drainUntilMs == long.MinValue) _drainUntilMs = nowMs + _options.LingerMs;
            if (!_draining || (_outgoing.Count == 0 && _inFlight.Count == 0) || nowMs >= _drainUntilMs)
            {
                _draining = false;
                if (_closesToSend > 0)
                {
                    _closesToSend--;
                    Emit(UdpFormat.Close(Token), send);
                }
                return;
            }
        }
        else if (nowMs - _lastReceivedMs >= _options.TimeoutMs)
        {
            CloseWith(UdpCloseReason.Timeout);
            return;
        }
        // New segments from the stream, as far as the window allows.
        while (_outgoing.Count > 0 && _inFlight.Count < _options.WindowSegments)
            _inFlight.Add(new Segment(_nextSegment++, _outgoing.Take((int)Math.Min(_outgoing.Count, UdpFormat.MaxSegmentBytes))));

        var retransmit = RetransmitMs;
        var position = 0;
        var began = false;
        void Begin()
        {
            UdpFormat.WriteDataHeader(_datagram, new UdpDataHeader(Token, unchecked((uint)nowMs), _echoTime,
                _echoReceivedMs == long.MinValue ? UdpFormat.NoEcho : (ushort)Math.Min(nowMs - _echoReceivedMs, UdpFormat.NoEcho - 1), _expectedSegment, AckMask(), _expectedUrgent));
            position = UdpFormat.DataHeaderBytes;
            began = true;
        }
        void End()
        {
            Emit(_datagram.AsSpan(0, position), send);
            _lastSentMs = nowMs;
            _ackPending = false;
            began = false;
        }
        void Room(int bytes)
        {
            if (began && position + bytes > UdpFormat.MaxPlainBytes) End();
            if (!began) Begin();
        }

        foreach (var segment in _inFlight)
        {
            if (segment.Acknowledged) continue;
            var wait = (long)retransmit << Math.Min(Math.Max(segment.Sends - 1, 0), 4);
            if (segment.Sends > 0 && !segment.Resend && nowMs - segment.LastSentMs < wait) continue;
            segment.Resend = false;
            Room(UdpFormat.SegmentOverhead + segment.Bytes.Length);
            _datagram[position] = UdpFormat.SectionSegment;
            BinaryPrimitives.WriteUInt32LittleEndian(_datagram.AsSpan(position + 1), segment.Number);
            BinaryPrimitives.WriteUInt16LittleEndian(_datagram.AsSpan(position + 5), (ushort)segment.Bytes.Length);
            segment.Bytes.CopyTo(_datagram, position + UdpFormat.SegmentOverhead);
            position += UdpFormat.SegmentOverhead + segment.Bytes.Length;
            Statistics.SegmentsSent++;
            if (segment.Sends > 0) Statistics.SegmentsRetransmitted++;
            segment.Sends++;
            segment.LastSentMs = nowMs;
        }

        if (!IsOpen)
        {
            if (began) End();
            return;
        }

        // An acknowledgement that has waited long enough gets a datagram of its own; one that has not rides with whatever goes.
        var ackDue = _ackPending && nowMs - _ackPendingSinceMs >= _options.AckDelayMs;
        // Urgent: what is new goes now; what the peer has not acknowledged rides along - with a new urgent message and with any other datagram that goes out anyway -,
        // oldest first, a few times, as far as the redundancy budget reaches; and whatever has waited a retransmission time goes regardless (a pause, the last tick, a
        // message too large to ride along).
        if (_urgentOut.Count > 0)
        {
            var redundancy = _options.UrgentRedundancyBytes;
            var carrying = _urgentNew || began || ackDue;
            foreach (var urgent in _urgentOut)
            {
                if (urgent.Sends > 0 && nowMs - urgent.LastSentMs < retransmit)
                {
                    if (!carrying || urgent.Sends > _options.UrgentRedundantSends || nowMs - urgent.LastSentMs < _options.UrgentRepeatSpacingMs) continue;
                    // A message of several fragments rides along like a small one as long as the budget holds all of it: the bundle of a tick in which a player
                    // sent an army is larger than a datagram, and it is exactly the bundle nobody should wait a retransmission time for.
                    var size = 0;
                    foreach (var fragment in urgent.Fragments) size += fragment.Length + UdpFormat.UrgentOverhead;
                    if (size > redundancy)
                    {
                        Statistics.UrgentOmitted++;
                        continue;
                    }
                    redundancy -= size;
                }
                for (var index = 0; index < urgent.Fragments.Length; index++)
                {
                    var fragment = urgent.Fragments[index];
                    Room(UdpFormat.UrgentOverhead + fragment.Length);
                    _datagram[position] = UdpFormat.SectionUrgent;
                    BinaryPrimitives.WriteUInt32LittleEndian(_datagram.AsSpan(position + 1), urgent.Number);
                    BinaryPrimitives.WriteUInt16LittleEndian(_datagram.AsSpan(position + 5), (ushort)index);
                    BinaryPrimitives.WriteUInt16LittleEndian(_datagram.AsSpan(position + 7), (ushort)urgent.Fragments.Length);
                    BinaryPrimitives.WriteUInt16LittleEndian(_datagram.AsSpan(position + 9), (ushort)fragment.Length);
                    fragment.CopyTo(_datagram, position + UdpFormat.UrgentOverhead);
                    position += UdpFormat.UrgentOverhead + fragment.Length;
                }
                if (urgent.Sends > 0) Statistics.UrgentRepeated++;
                else Statistics.UrgentSent++;
                urgent.Sends++;
                urgent.LastSentMs = nowMs;
            }
            _urgentNew = false;
        }

        if (!began && (ackDue || nowMs - _lastSentMs >= _options.KeepAliveMs)) Begin();
        if (began) End();
    }

    private uint AckMask()
    {
        var mask = 0u;
        foreach (var number in _heldSegments.Keys)
        {
            var bit = unchecked((int)(number - _expectedSegment)) - 1;
            if (bit is >= 0 and < 32) mask |= 1u << bit;
        }
        return mask;
    }

    private void Emit(ReadOnlySpan<byte> datagram, Action<ReadOnlySpan<byte>> send)
    {
        Statistics.DatagramsSent++;
        Statistics.BytesSent += datagram.Length + UdpFormat.SealBytes;
        send(datagram);
    }
}

/// <summary>A growing queue of bytes: appended at the end, taken from the front, without copying the rest on every take.</summary>
internal sealed class ByteQueue
{
    private byte[] _buffer = new byte[256];
    private int _start;
    private int _end;

    public long Count => _end - _start;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (_end + bytes.Length > _buffer.Length)
        {
            var count = _end - _start;
            if (count + bytes.Length > _buffer.Length)
            {
                var grown = new byte[Math.Max(checked(count + bytes.Length), _buffer.Length * 2)];
                _buffer.AsSpan(_start, count).CopyTo(grown);
                _buffer = grown;
            }
            else
            {
                _buffer.AsSpan(_start, count).CopyTo(_buffer);
            }
            _start = 0;
            _end = count;
        }
        bytes.CopyTo(_buffer.AsSpan(_end));
        _end += bytes.Length;
    }

    public void Peek(Span<byte> destination) => _buffer.AsSpan(_start, destination.Length).CopyTo(destination);

    public void Skip(int count)
    {
        _start += count;
        if (_start == _end) _start = _end = 0;
    }

    public byte[] Take(int count)
    {
        var taken = _buffer.AsSpan(_start, count).ToArray();
        Skip(count);
        return taken;
    }
}
