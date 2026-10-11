using System.Net;
using System.Net.Sockets;

namespace CnCNew.Net.Udp;

/// <summary>Where datagrams go out and come in: a socket, or a network in memory for a test. Never blocks.</summary>
public interface IDatagramChannel
{
    bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from);
    void Send(ReadOnlySpan<byte> datagram, IPEndPoint to);
}

public enum UdpClientState
{
    Connecting,
    Connected,
    /// <summary>No answer within the connect timeout, or the server refused.</summary>
    Failed,
    Closed,
}

/// <summary>
/// The client's end of the UDP transport: the cookie handshake (docs/NETWORK-PROTOCOL.md section 12.3) and one connection. <see cref="Connection"/> exists from the start;
/// what is sent on it before the handshake is through waits in its reliable channel. The host calls <see cref="Pump"/> (take in what arrived) and <see cref="Flush"/> (send
/// what is due) in its loop.
/// </summary>
public sealed class UdpClientEndpoint
{
    private readonly IDatagramChannel _channel;
    private readonly IPEndPoint _server;
    private readonly INetClock _clock;
    private readonly UdpOptions _options;
    private readonly ulong _nonce;
    private readonly UdpLink _link;
    private readonly byte[] _buffer = new byte[UdpFormat.MaxDatagramBytes + 1];
    private readonly long _startedMs;
    private byte[]? _connect;
    private long _lastHandshakeMs = long.MinValue;

    /// <param name="nonce">A number this client chose for this connection attempt; the host draws it (a test names it). Not 0.</param>
    public UdpClientEndpoint(IDatagramChannel channel, IPEndPoint server, INetClock clock, ulong nonce, UdpOptions? options = null)
    {
        _channel = channel;
        _server = server;
        _clock = clock;
        _options = options ?? UdpOptions.Default;
        _nonce = nonce == 0 ? 1 : nonce;
        _startedMs = clock.Milliseconds;
        _link = new UdpLink(_options, _startedMs);
    }

    public IConnection Connection => _link;
    public UdpLink Link => _link;
    public UdpClientState State { get; private set; } = UdpClientState.Connecting;

    /// <summary>Datagrams that were not ours, not from the server, or did not follow the layout.</summary>
    public long Malformed { get; private set; }

    public void Pump()
    {
        var now = _clock.Milliseconds;
        while (_channel.TryReceive(_buffer, out var length, out var from))
        {
            var datagram = _buffer.AsSpan(0, length);
            if (!from.Equals(_server) || !UdpFormat.TryReadKind(datagram, out var kind))
            {
                Malformed++;
                continue;
            }
            switch (kind)
            {
                case UdpKind.Challenge when State == UdpClientState.Connecting && UdpFormat.TryReadCookie(datagram, out var nonce, out var slot, out var cookie) && nonce == _nonce:
                    _connect = UdpFormat.Cookie(UdpKind.Connect, _nonce, slot, cookie);
                    _lastHandshakeMs = long.MinValue;
                    break;
                case UdpKind.Accept when State == UdpClientState.Connecting && UdpFormat.TryReadAccept(datagram, out var nonce, out var token) && nonce == _nonce:
                    _link.Establish(token, now);
                    State = UdpClientState.Connected;
                    break;
                case UdpKind.Refuse when State == UdpClientState.Connecting && UdpFormat.TryReadRefuse(datagram, out var nonce, out _) && nonce == _nonce:
                    _link.CloseWith(UdpCloseReason.Refused);
                    State = UdpClientState.Failed;
                    break;
                // A closed connection still takes the acknowledgements of what it sent before it closed, and ignores the rest: datagrams on their way are not a violation.
                case UdpKind.Data when State is UdpClientState.Connected or UdpClientState.Closed && UdpFormat.TryReadDataHeader(datagram, out var header) && header.Token == _link.Token:
                    _link.Receive(datagram, now);
                    break;
                case UdpKind.Close when State is UdpClientState.Connected or UdpClientState.Closed && UdpFormat.TryReadClose(datagram, out var token) && token == _link.Token:
                    _link.CloseWith(UdpCloseReason.Peer);
                    break;
                // A repeated challenge or accept after the handshake went on is the network's doing, not a violation.
                case UdpKind.Challenge or UdpKind.Accept when State != UdpClientState.Connecting:
                    break;
                default:
                    Malformed++;
                    break;
            }
        }
        if (State == UdpClientState.Connected && !_link.IsOpen) State = UdpClientState.Closed;
    }

    public void Flush()
    {
        var now = _clock.Milliseconds;
        if (State == UdpClientState.Connecting)
        {
            if (!_link.IsOpen)
            {
                State = UdpClientState.Closed;
            }
            else if (now - _startedMs >= _options.ConnectTimeoutMs)
            {
                _link.CloseWith(UdpCloseReason.Timeout);
                State = UdpClientState.Failed;
            }
            else if (_lastHandshakeMs == long.MinValue || now - _lastHandshakeMs >= _options.ConnectRetryMs)
            {
                _lastHandshakeMs = now;
                _channel.Send(_connect ?? UdpFormat.Request(_nonce), _server);
            }
            return;
        }
        _link.Flush(now, datagram => _channel.Send(datagram, _server));
        if (State == UdpClientState.Connected && !_link.IsOpen) State = UdpClientState.Closed;
    }
}

/// <summary>
/// The server's end of the UDP transport: one channel (one port), many connections. A request costs it nothing but an answer of the same size; state exists only for a client
/// that returned its cookie (docs/NETWORK-PROTOCOL.md section 12.3). New connections are handed out by <see cref="TryAccept"/>.
/// </summary>
public sealed class UdpServerEndpoint
{
    private readonly IDatagramChannel _channel;
    private readonly INetClock _clock;
    private readonly UdpOptions _options;
    private readonly byte[] _secret;
    private readonly Dictionary<IPEndPoint, (UdpLink Link, ulong Nonce)> _links = [];
    private readonly Queue<(UdpLink Link, IPEndPoint Address)> _accepted = new();
    private readonly List<IPEndPoint> _finished = [];
    private readonly Queue<(UdpLink Link, IPEndPoint Address)> _closed = new();
    private readonly byte[] _buffer = new byte[UdpFormat.MaxDatagramBytes + 1];

    /// <param name="secret">The key of the cookies: at least 16 bytes only this server knows, drawn by the host from a cryptographic source (a test names them).</param>
    public UdpServerEndpoint(IDatagramChannel channel, INetClock clock, byte[] secret, UdpOptions? options = null)
    {
        if (secret.Length < 16) throw new ArgumentException("The cookie secret has at least 16 bytes.", nameof(secret));
        _channel = channel;
        _clock = clock;
        _secret = [..secret];
        _options = options ?? UdpOptions.Default;
    }

    public int Connections => _links.Count;
    public long Requests { get; private set; }
    public long ConnectsAccepted { get; private set; }
    public long CookiesRejected { get; private set; }
    public long Refused { get; private set; }
    public long UnknownPeer { get; private set; }
    public long Malformed { get; private set; }

    public bool TryAccept(out UdpLink link, out IPEndPoint address)
    {
        if (_accepted.Count > 0)
        {
            (link, address) = _accepted.Dequeue();
            return true;
        }
        link = null!;
        address = null!;
        return false;
    }

    /// <summary>A connection this endpoint has forgotten (closed and done), with its counters, for a host's log; only the newest are kept.</summary>
    public bool TryTakeClosed(out UdpLink link, out IPEndPoint address)
    {
        if (_closed.Count > 0)
        {
            (link, address) = _closed.Dequeue();
            return true;
        }
        link = null!;
        address = null!;
        return false;
    }

    private uint Slot(long nowMs) => unchecked((uint)(nowMs / _options.CookieLifetimeMs));

    public void Pump()
    {
        var now = _clock.Milliseconds;
        Span<byte> cookie = stackalloc byte[UdpFormat.CookieBytes];
        while (_channel.TryReceive(_buffer, out var length, out var from))
        {
            var datagram = _buffer.AsSpan(0, length);
            if (!UdpFormat.TryReadKind(datagram, out var kind))
            {
                Malformed++;
                continue;
            }
            switch (kind)
            {
                case UdpKind.Request when UdpFormat.TryReadRequest(datagram, out var nonce):
                {
                    Requests++;
                    var slot = Slot(now);
                    UdpFormat.Mac(_secret, 1, from, nonce, slot, cookie);
                    _channel.Send(UdpFormat.Cookie(UdpKind.Challenge, nonce, slot, cookie), from);
                    break;
                }
                case UdpKind.Connect when UdpFormat.TryReadCookie(datagram, out var nonce, out var slot, out var offered):
                {
                    var current = Slot(now);
                    UdpFormat.Mac(_secret, 1, from, nonce, slot, cookie);
                    if ((slot != current && slot != current - 1) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(cookie, offered))
                    {
                        // Expired or not ours: the sender may try again with a fresh one. Still no state.
                        CookiesRejected++;
                        UdpFormat.Mac(_secret, 1, from, nonce, current, cookie);
                        _channel.Send(UdpFormat.Cookie(UdpKind.Challenge, nonce, current, cookie), from);
                        break;
                    }
                    var token = UdpFormat.Token(_secret, from, nonce, slot);
                    if (_links.TryGetValue(from, out var known))
                    {
                        if (known.Link.Token == token)
                        {
                            // The accept was lost; the same answer again.
                            _channel.Send(UdpFormat.Accept(nonce, token), from);
                            break;
                        }
                        // Another attempt from the same address: the client started anew.
                        known.Link.CloseWith(UdpCloseReason.Replaced);
                        if (_closed.Count >= 1_024) _closed.Dequeue();
                        _closed.Enqueue((known.Link, from));
                        _links.Remove(from);
                    }
                    if (_links.Count >= _options.MaxConnections)
                    {
                        Refused++;
                        _channel.Send(UdpFormat.Refuse(nonce, UdpFormat.RefuseFull), from);
                        break;
                    }
                    var link = new UdpLink(_options, now);
                    link.Establish(token, now);
                    _links[from] = (link, nonce);
                    _accepted.Enqueue((link, from));
                    ConnectsAccepted++;
                    _channel.Send(UdpFormat.Accept(nonce, token), from);
                    break;
                }
                case UdpKind.Data when UdpFormat.TryReadDataHeader(datagram, out var header):
                    if (_links.TryGetValue(from, out var entry) && entry.Link.Token == header.Token) entry.Link.Receive(datagram, now);
                    else UnknownPeer++;
                    break;
                case UdpKind.Close when UdpFormat.TryReadClose(datagram, out var token):
                    if (_links.TryGetValue(from, out var closing) && closing.Link.Token == token) closing.Link.CloseWith(UdpCloseReason.Peer);
                    else UnknownPeer++;
                    break;
                default:
                    Malformed++;
                    break;
            }
        }
    }

    public void Flush()
    {
        var now = _clock.Milliseconds;
        foreach (var (address, entry) in _links)
        {
            var to = address;
            entry.Link.Flush(now, datagram => _channel.Send(datagram, to));
            if (entry.Link.IsFinished) _finished.Add(address);
        }
        foreach (var address in _finished)
        {
            if (_closed.Count >= 1_024) _closed.Dequeue();
            _closed.Enqueue((_links[address].Link, address));
            _links.Remove(address);
        }
        _finished.Clear();
    }
}

/// <summary>A UDP socket as a datagram channel: non-blocking, IPv4 or IPv6 as the address it is bound to.</summary>
public sealed class UdpSocketChannel : IDatagramChannel, IDisposable
{
    private readonly Socket _socket;
    private EndPoint _from;

    public UdpSocketChannel(IPEndPoint local)
    {
        _socket = new Socket(local.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        if (OperatingSystem.IsWindows())
        {
            // Without this, an ICMP "port unreachable" for something this socket sent makes a later receive fail with a connection reset (SIO_UDP_CONNRESET).
            _socket.IOControl(unchecked((int)0x9800000C), [0, 0, 0, 0], null);
        }
        _socket.ReceiveBufferSize = 1 << 20;
        _socket.SendBufferSize = 1 << 20;
        _socket.Bind(local);
        _from = new IPEndPoint(local.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint!;
    public long SendFailures { get; private set; }

    /// <summary>Waits until a datagram can be read or the time has passed; the pacing of a host loop (docs/NETWORK-PROTOCOL.md section 13.4).</summary>
    public bool WaitReadable(int microseconds) => _socket.Poll(Math.Max(0, microseconds), SelectMode.SelectRead);

    public bool TryReceive(Span<byte> buffer, out int length, out IPEndPoint from)
    {
        length = 0;
        from = null!;
        while (_socket.Available > 0 || _socket.Poll(0, SelectMode.SelectRead))
        {
            try
            {
                length = _socket.ReceiveFrom(buffer, SocketFlags.None, ref _from);
                from = (IPEndPoint)_from;
                return true;
            }
            catch (SocketException exception) when (exception.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset or SocketError.WouldBlock)
            {
                // Larger than a datagram of ours can be, or the echo of a port that is gone: not a datagram to take in. An over-long one is consumed by the failed read.
                if (exception.SocketErrorCode == SocketError.WouldBlock) return false;
            }
        }
        return false;
    }

    public void Send(ReadOnlySpan<byte> datagram, IPEndPoint to)
    {
        try
        {
            _socket.SendTo(datagram, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // A full send buffer or an unreachable host loses a datagram like the network does; the channels above repeat.
            SendFailures++;
        }
    }

    public void Dispose() => _socket.Dispose();
}
