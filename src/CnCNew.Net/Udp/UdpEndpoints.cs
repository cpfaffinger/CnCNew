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
    /// <summary>No answer within the connect timeout, the server refused, or its identity is not to be trusted.</summary>
    Failed,
    Closed,
}

/// <summary>
/// The client's end of the UDP transport: the cookie handshake with its key exchange (docs/NETWORK-PROTOCOL.md sections 12.3 and 20.2) and one connection.
/// <see cref="Connection"/> exists from the start; what is sent on it before the handshake is through waits in its reliable channel. The host calls <see cref="Pump"/>
/// (take in what arrived) and <see cref="Flush"/> (send what is due) in its loop. Everything of the connection travels sealed (<see cref="UdpCipher"/>).
/// </summary>
public sealed class UdpClientEndpoint : IDisposable
{
    private readonly IDatagramChannel _channel;
    private readonly IPEndPoint _server;
    private readonly INetClock _clock;
    private readonly UdpOptions _options;
    private readonly ulong _nonce;
    private readonly UdpLink _link;
    private readonly UdpKeyExchange _exchange = new();
    private readonly Func<byte[], bool>? _trust;
    private readonly byte[] _buffer = new byte[UdpFormat.MaxDatagramBytes + 1];
    private readonly byte[] _plain = new byte[UdpFormat.MaxDatagramBytes];
    private readonly byte[] _wire = new byte[UdpFormat.MaxDatagramBytes];
    private readonly long _startedMs;
    private UdpCipher? _cipher;
    private byte[]? _connect;
    private long _lastHandshakeMs = long.MinValue;

    /// <param name="nonce">A number this client chose for this connection attempt; the host draws it (a test names it). Not 0.</param>
    /// <param name="trust">
    /// Asked once with the identity key the server proved to hold: false ends the attempt (<see cref="UdpCloseReason.Untrusted"/>). The host of a client keeps the
    /// keys it has met (trust on first use); without the question every server that signs its own exchange is taken.
    /// </param>
    public UdpClientEndpoint(IDatagramChannel channel, IPEndPoint server, INetClock clock, ulong nonce, UdpOptions? options = null, Func<byte[], bool>? trust = null)
    {
        _channel = channel;
        _server = server;
        _clock = clock;
        _options = options ?? UdpOptions.Default;
        _nonce = nonce == 0 ? 1 : nonce;
        _trust = trust;
        _startedMs = clock.Milliseconds;
        _link = new UdpLink(_options, _startedMs);
    }

    public IConnection Connection => _link;
    public UdpLink Link => _link;
    public UdpClientState State { get; private set; } = UdpClientState.Connecting;

    /// <summary>The identity key the server proved to hold in the handshake (X and Y, 32 bytes each); null before the accept.</summary>
    public byte[]? ServerKey { get; private set; }
    public string ServerFingerprint => ServerKey is null ? string.Empty : UdpCrypto.FingerprintOf(ServerKey);

    /// <summary>Datagrams that were not ours, not from the server, or did not follow the layout.</summary>
    public long Malformed { get; private set; }

    /// <summary>Datagrams that were in order but out of place in time: a handshake answer repeated, a datagram of the connection ahead of its accept.</summary>
    public long Early { get; private set; }

    /// <summary>Datagrams of the connection that did not authenticate, and datagrams whose number was already taken (docs/NETWORK-PROTOCOL.md section 20.3).</summary>
    public long Forged => _cipher?.Forged ?? 0;
    public long Replayed => _cipher?.Replayed ?? 0;

    private void TakeAccept(ReadOnlySpan<byte> datagram, long now)
    {
        if (!UdpFormat.TryReadAccept(datagram, out var nonce, out var token, out var serverKey, out var identity, out var signature) || nonce != _nonce)
        {
            Malformed++;
            return;
        }
        var transcript = UdpCrypto.Transcript(_nonce, token, _exchange.PublicKey, serverKey, identity);
        // Whoever answered must hold the identity key it names: the signature covers both keys of the exchange, so nobody in between can have swapped one.
        if (!UdpCrypto.Verify(identity, transcript, signature) || UdpCrypto.Derive(_exchange, serverKey, transcript, isClient: true) is not { } cipher)
        {
            Malformed++;
            return;
        }
        ServerKey = identity.ToArray();
        if (_trust is not null && !_trust(ServerKey))
        {
            cipher.Dispose();
            _link.CloseWith(UdpCloseReason.Untrusted);
            State = UdpClientState.Failed;
            return;
        }
        _cipher = cipher;
        _link.Establish(token, now);
        State = UdpClientState.Connected;
    }

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
                    _connect = UdpFormat.Connect(_nonce, slot, cookie, _exchange.PublicKey);
                    _lastHandshakeMs = long.MinValue;
                    break;
                case UdpKind.Accept when State == UdpClientState.Connecting:
                    TakeAccept(datagram, now);
                    break;
                case UdpKind.Refuse when State == UdpClientState.Connecting && UdpFormat.TryReadRefuse(datagram, out var nonce, out _) && nonce == _nonce:
                    _link.CloseWith(UdpCloseReason.Refused);
                    State = UdpClientState.Failed;
                    break;
                // A closed connection still takes the acknowledgements of what it sent before it closed, and ignores the rest: datagrams on their way are not a violation.
                case UdpKind.Data or UdpKind.Close when State is UdpClientState.Connected or UdpClientState.Closed && _cipher is not null
                                                        && UdpFormat.TryReadSealedToken(datagram, out var token) && token == _link.Token:
                    // Not authentic or a repetition: counted by the cipher, and nothing of it is used.
                    if (!_cipher.TryOpen(datagram, _plain, out var plainLength)) break;
                    if (kind == UdpKind.Data) _link.Receive(_plain.AsSpan(0, plainLength), now);
                    else if (plainLength == UdpFormat.CloseBytes) _link.CloseWith(UdpCloseReason.Peer);
                    else Malformed++;
                    break;
                // A repeated challenge or accept after the handshake went on is the network's doing, not a violation - and so is a datagram of the connection that
                // overtook the accept: what it carried is repeated.
                case UdpKind.Challenge or UdpKind.Accept when State != UdpClientState.Connecting:
                case UdpKind.Data or UdpKind.Close when State == UdpClientState.Connecting && _connect is not null:
                    Early++;
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
        if (_cipher is { } cipher) _link.Flush(now, datagram => _channel.Send(_wire.AsSpan(0, cipher.Seal(datagram, _wire)), _server));
        if (State == UdpClientState.Connected && !_link.IsOpen) State = UdpClientState.Closed;
    }

    public void Dispose()
    {
        _exchange.Dispose();
        _cipher?.Dispose();
    }
}

/// <summary>
/// The server's end of the UDP transport: one channel (one port), many connections. A request costs it nothing but an answer of the same size; state - and the
/// arithmetic of a key exchange - exists only for a client that returned its cookie, and both are bounded per address and overall (docs/NETWORK-PROTOCOL.md
/// sections 12.3, 20.2 and 20.4). New connections are handed out by <see cref="TryAccept"/>.
/// </summary>
public sealed class UdpServerEndpoint : IDisposable
{
    private sealed class Entry(UdpLink link, UdpCipher cipher, byte[] accept)
    {
        public UdpLink Link { get; } = link;
        public UdpCipher Cipher { get; } = cipher;
        /// <summary>The accept as it was sent: a client whose accept was lost gets the same one again, not a second exchange.</summary>
        public byte[] Accept { get; } = accept;
    }

    private readonly IDatagramChannel _channel;
    private readonly INetClock _clock;
    private readonly UdpOptions _options;
    private readonly byte[] _secret;
    private readonly UdpIdentity _identity;
    private readonly bool _ownsIdentity;
    private readonly Dictionary<IPEndPoint, Entry> _links = [];
    private readonly Dictionary<IPAddress, int> _perAddress = [];
    private readonly Dictionary<IPAddress, (long Tokens, long AtMs)> _handshakes = [];
    private readonly Queue<(UdpLink Link, IPEndPoint Address)> _accepted = new();
    private readonly List<IPEndPoint> _finished = [];
    private readonly Queue<(UdpLink Link, IPEndPoint Address)> _closed = new();
    private readonly byte[] _buffer = new byte[UdpFormat.MaxDatagramBytes + 1];
    private readonly byte[] _plain = new byte[UdpFormat.MaxDatagramBytes];
    private readonly byte[] _wire = new byte[UdpFormat.MaxDatagramBytes];
    private long _globalTokens;
    private long _globalAtMs;

    /// <param name="secret">The key of the cookies: at least 16 bytes only this server knows, drawn by the host from a cryptographic source (a test names them).</param>
    /// <param name="identity">The permanent key pair of the server; without one the endpoint makes a key that lasts as long as it does (a test, an embedded server).</param>
    /// <param name="isBanned">Asked for the address of every handshake datagram: true and it is dropped unanswered (docs/DEDICATED-SERVER.md section 10).</param>
    public UdpServerEndpoint(IDatagramChannel channel, INetClock clock, byte[] secret, UdpOptions? options = null, UdpIdentity? identity = null, Func<IPAddress, bool>? isBanned = null)
    {
        if (secret.Length < 16) throw new ArgumentException("The cookie secret has at least 16 bytes.", nameof(secret));
        _channel = channel;
        _clock = clock;
        _secret = [..secret];
        _options = options ?? UdpOptions.Default;
        _ownsIdentity = identity is null;
        _identity = identity ?? UdpIdentity.Create();
        _isBanned = isBanned;
        _globalTokens = _options.HandshakesPerSecond;
        _globalAtMs = clock.Milliseconds;
    }

    private readonly Func<IPAddress, bool>? _isBanned;

    public UdpIdentity Identity => _identity;
    public int Connections => _links.Count;
    public long Requests { get; private set; }
    public long ConnectsAccepted { get; private set; }
    public long CookiesRejected { get; private set; }
    public long Refused { get; private set; }
    public long UnknownPeer { get; private set; }
    public long Malformed { get; private set; }

    /// <summary>Handshake datagrams dropped unanswered because their sender, or all senders together, sent too many (docs/NETWORK-PROTOCOL.md section 20.4).</summary>
    public long RateLimited { get; private set; }
    /// <summary>Handshake datagrams of banned addresses, dropped unanswered.</summary>
    public long Banned { get; private set; }
    /// <summary>Datagrams of connections that did not authenticate, and repetitions of datagrams already taken, summed over the connections that are gone and those that stand.</summary>
    public long Forged => _forgedGone + _links.Values.Sum(entry => entry.Cipher.Forged);
    public long Replayed => _replayedGone + _links.Values.Sum(entry => entry.Cipher.Replayed);
    private long _forgedGone;
    private long _replayedGone;

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

    /// <summary>Closes every connection whose address the test names (a ban that came into force while they stood). Returns how many.</summary>
    public int CloseWhere(Func<IPAddress, bool> address)
    {
        var closed = 0;
        foreach (var (from, entry) in _links)
        {
            if (!entry.Link.IsOpen || !address(from.Address)) continue;
            entry.Link.Close();
            closed++;
        }
        return closed;
    }

    /// <summary>Whether a handshake datagram of this address is answered now: one token of the address and one of the whole server, both refilled by the clock.</summary>
    private bool HandshakeAllowed(IPAddress address, long now)
    {
        if (_isBanned is not null && _isBanned(address))
        {
            Banned++;
            return false;
        }
        _globalTokens = Math.Min(_options.HandshakesPerSecond, _globalTokens + Math.Max(0, now - _globalAtMs) * _options.HandshakesPerSecond / 1000);
        _globalAtMs = now;
        if (_globalTokens < 1)
        {
            RateLimited++;
            return false;
        }
        var state = _handshakes.TryGetValue(address, out var known) ? known : (Tokens: (long)_options.HandshakeBurst * 1000, AtMs: now);
        // Thousandths of a token, so that a slow clock does not round a refill away.
        var tokens = Math.Min((long)_options.HandshakeBurst * 1000, state.Tokens + Math.Max(0, now - state.AtMs) * _options.HandshakesPerAddressPerSecond);
        if (tokens < 1000)
        {
            _handshakes[address] = (tokens, now);
            RateLimited++;
            return false;
        }
        // Forged senders must not make the table grow without end: full, it starts anew, and the bound for all senders still holds.
        if (_handshakes.Count >= _options.MaxTrackedAddresses && !_handshakes.ContainsKey(address)) _handshakes.Clear();
        _handshakes[address] = (tokens - 1000, now);
        _globalTokens--;
        return true;
    }

    private void Forget(IPEndPoint address, Entry entry)
    {
        _forgedGone += entry.Cipher.Forged;
        _replayedGone += entry.Cipher.Replayed;
        entry.Cipher.Dispose();
        if (_closed.Count >= 1_024) _closed.Dequeue();
        _closed.Enqueue((entry.Link, address));
        _links.Remove(address);
        if (_perAddress.TryGetValue(address.Address, out var count))
        {
            if (count <= 1) _perAddress.Remove(address.Address);
            else _perAddress[address.Address] = count - 1;
        }
    }

    public void Pump()
    {
        var now = _clock.Milliseconds;
        Span<byte> cookie = stackalloc byte[UdpFormat.CookieBytes];
        Span<byte> signature = stackalloc byte[UdpIdentity.SignatureBytes];
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
                    if (!HandshakeAllowed(from.Address, now)) break;
                    Requests++;
                    var slot = Slot(now);
                    UdpFormat.Mac(_secret, 1, from, nonce, slot, cookie);
                    _channel.Send(UdpFormat.Cookie(UdpKind.Challenge, nonce, slot, cookie), from);
                    break;
                }
                case UdpKind.Connect when UdpFormat.TryReadConnect(datagram, out var nonce, out var slot, out var offered, out var clientKey):
                {
                    if (!HandshakeAllowed(from.Address, now)) break;
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
                            _channel.Send(known.Accept, from);
                            break;
                        }
                        // Another attempt from the same address: the client started anew.
                        known.Link.CloseWith(UdpCloseReason.Replaced);
                        Forget(from, known);
                    }
                    if (_links.Count >= _options.MaxConnections || _perAddress.GetValueOrDefault(from.Address) >= _options.MaxConnectionsPerAddress)
                    {
                        Refused++;
                        _channel.Send(UdpFormat.Refuse(nonce, _links.Count >= _options.MaxConnections ? UdpFormat.RefuseFull : UdpFormat.RefuseAddressFull), from);
                        break;
                    }
                    // Only now, for an address that answered: a key of this exchange, the signature of the identity over everything said, the keys of the connection.
                    using var exchange = new UdpKeyExchange();
                    var transcript = UdpCrypto.Transcript(nonce, token, clientKey, exchange.PublicKey, _identity.PublicKey);
                    if (UdpCrypto.Derive(exchange, clientKey, transcript, isClient: false) is not { } cipher)
                    {
                        Malformed++;
                        break;
                    }
                    _identity.Sign(transcript, signature);
                    var accept = UdpFormat.Accept(nonce, token, exchange.PublicKey, _identity.PublicKey, signature);
                    var link = new UdpLink(_options, now);
                    link.Establish(token, now);
                    _links[from] = new Entry(link, cipher, accept);
                    _perAddress[from.Address] = _perAddress.GetValueOrDefault(from.Address) + 1;
                    _accepted.Enqueue((link, from));
                    ConnectsAccepted++;
                    _channel.Send(accept, from);
                    break;
                }
                case UdpKind.Data or UdpKind.Close when UdpFormat.TryReadSealedToken(datagram, out var token):
                    if (!_links.TryGetValue(from, out var entry) || entry.Link.Token != token)
                    {
                        UnknownPeer++;
                        break;
                    }
                    // Not authentic or a repetition: counted by the cipher, and nothing of it is used - not even to keep the connection alive.
                    if (!entry.Cipher.TryOpen(datagram, _plain, out var plainLength)) break;
                    if (kind == UdpKind.Data) entry.Link.Receive(_plain.AsSpan(0, plainLength), now);
                    else if (plainLength == UdpFormat.CloseBytes) entry.Link.CloseWith(UdpCloseReason.Peer);
                    else Malformed++;
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
            var cipher = entry.Cipher;
            entry.Link.Flush(now, datagram => _channel.Send(_wire.AsSpan(0, cipher.Seal(datagram, _wire)), to));
            if (entry.Link.IsFinished) _finished.Add(address);
        }
        foreach (var address in _finished) Forget(address, _links[address]);
        _finished.Clear();
    }

    public void Dispose()
    {
        foreach (var entry in _links.Values) entry.Cipher.Dispose();
        if (_ownsIdentity) _identity.Dispose();
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
    /// <summary>What went through this socket, as payload of datagrams (the headers of UDP and IP come on top: 28 bytes a datagram with IPv4).</summary>
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }
    public long DatagramsSent { get; private set; }
    public long DatagramsReceived { get; private set; }

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
                BytesReceived += length;
                DatagramsReceived++;
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
            BytesSent += datagram.Length;
            DatagramsSent++;
        }
        catch (SocketException)
        {
            // A full send buffer or an unreachable host loses a datagram like the network does; the channels above repeat.
            SendFailures++;
        }
    }

    public void Dispose() => _socket.Dispose();
}
