using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace CnCNew.Net.Udp;

/// <summary>The time limits and sizes of the UDP transport (docs/NETWORK-PROTOCOL.md section 12.8); every number is configuration with the documented default.</summary>
public sealed record UdpOptions
{
    public int ConnectRetryMs { get; init; } = 250;
    public int ConnectTimeoutMs { get; init; } = 5_000;
    public int CookieLifetimeMs { get; init; } = 10_000;
    public int KeepAliveMs { get; init; } = 1_000;
    public int TimeoutMs { get; init; } = 10_000;
    /// <summary>How long a closed connection goes on delivering what was sent before the close (a rejection, the end of a match) before it says goodbye.</summary>
    public int LingerMs { get; init; } = 2_000;
    public int InitialRetransmitMs { get; init; } = 200;
    public int MinRetransmitMs { get; init; } = 30;
    public int MaxRetransmitMs { get; init; } = 2_000;
    public int WindowSegments { get; init; } = 256;
    public int UrgentRedundancyBytes { get; init; } = UdpFormat.MaxUrgentFragmentBytes;
    /// <summary>
    /// How many times an unacknowledged urgent message rides along with newer ones before only the retransmission time repeats it. Three copies after the first survive
    /// any loss a playable line has (at 3 % loss all four are lost once in a million); without the bound a long round trip makes every datagram carry everything sent
    /// during it.
    /// </summary>
    public int UrgentRedundantSends { get; init; } = 3;
    /// <summary>The least time between two copies of the same urgent message: copies sent in one breath share the fate of a burst of loss.</summary>
    public int UrgentRepeatSpacingMs { get; init; } = 8;
    /// <summary>
    /// How long an acknowledgement may wait for a datagram that goes out anyway before it gets one of its own. An end that sends every tick acknowledges for free; the
    /// delay halves the datagrams of the other.
    /// </summary>
    public int AckDelayMs { get; init; } = 20;
    public int UrgentWindow { get; init; } = 1_024;
    public int MaxMessageBytes { get; init; } = Protocol.MaxResumeBytes + Protocol.MaxMessageBytes;
    public int MaxConnections { get; init; } = 256;

    // Phase 28 (docs/NETWORK-PROTOCOL.md section 20.4): what a stranger may cost a server, and what a connection may take.

    /// <summary>Connections one address may hold at a time (people behind one router share theirs); one more is refused.</summary>
    public int MaxConnectionsPerAddress { get; init; } = 8;
    /// <summary>Handshake datagrams (requests and connects) answered per address and second, and how many may come at once; what is beyond is dropped unanswered.</summary>
    public int HandshakesPerAddressPerSecond { get; init; } = 10;
    public int HandshakeBurst { get; init; } = 20;
    /// <summary>Handshake datagrams answered per second whatever their sender says it is: the bound for forged senders.</summary>
    public int HandshakesPerSecond { get; init; } = 2_000;
    /// <summary>Addresses whose handshake rate is remembered; when the table is full it starts anew (the bound for all of them still holds).</summary>
    public int MaxTrackedAddresses { get; init; } = 4_096;
    /// <summary>
    /// What a connection may receive: bytes per second in the long run, and how much at once (a session state is sent as fast as the line carries it). A datagram
    /// beyond is dropped as the network would drop it; a peer that keeps it up for <see cref="PoliceLimitMs"/> is closed.
    /// </summary>
    public int InboundBytesPerSecond { get; init; } = 256 * 1024;
    public int InboundBurstBytes { get; init; } = 8 * 1024 * 1024;
    public int PoliceLimitMs { get; init; } = 10_000;
    /// <summary>The most that may wait in the reliable channel for a peer that does not acknowledge; beyond it the connection is closed instead of growing.</summary>
    public long MaxReliableBacklogBytes { get; init; } = 2L * (Protocol.MaxResumeBytes + Protocol.MaxMessageBytes);

    public static UdpOptions Default { get; } = new();
}

public enum UdpKind : byte
{
    Request = 1,
    Challenge = 2,
    Connect = 3,
    Accept = 4,
    Data = 5,
    Close = 6,
    Refuse = 7,
}

/// <summary>The header of a data datagram after magic, version and kind.</summary>
public readonly record struct UdpDataHeader(ulong Token, uint SendTime, uint EchoTime, ushort EchoHold, uint ReliableAck, uint AckMask, uint UrgentAck);

/// <summary>The datagram layout of the UDP transport (docs/NETWORK-PROTOCOL.md sections 12.2 to 12.4). Reading never throws: what does not follow the layout is refused.</summary>
public static class UdpFormat
{
    public const ushort Magic = 0xC2CE;
    public const byte Version = 2;
    public const int MaxDatagramBytes = 1_200;
    public const int HeaderBytes = 4;
    public const int HandshakeBytes = 32;
    /// <summary>A connect: the cookie fields and the client's key of the exchange.</summary>
    public const int ConnectBytes = HandshakeBytes + UdpIdentity.PublicKeyBytes;
    /// <summary>An accept: nonce, token, the server's key of the exchange, its identity key and its signature.</summary>
    public const int AcceptBytes = 20 + 2 * UdpIdentity.PublicKeyBytes + UdpIdentity.SignatureBytes;
    public const int RefuseBytes = 13;
    public const int CloseBytes = 12;
    /// <summary>What of a sealed datagram stays readable: magic, version, kind and the token of the connection.</summary>
    public const int ClearBytes = 12;
    public const int TagBytes = 16;
    /// <summary>What sealing adds to a datagram: its number and its tag.</summary>
    public const int SealBytes = 8 + TagBytes;
    /// <summary>The largest datagram of a connection before it is sealed.</summary>
    public const int MaxPlainBytes = MaxDatagramBytes - SealBytes;
    public const int DataHeaderBytes = 34;
    public const int SegmentOverhead = 7;
    public const int UrgentOverhead = 11;
    public const int MaxSegmentBytes = MaxPlainBytes - DataHeaderBytes - SegmentOverhead;
    public const int MaxUrgentFragmentBytes = MaxPlainBytes - DataHeaderBytes - UrgentOverhead;
    public const int MaxUrgentFragments = 64;
    public const int CookieBytes = 16;
    public const ushort NoEcho = 0xFFFF;
    public const byte SectionSegment = 1;
    public const byte SectionUrgent = 2;
    public const byte RefuseFull = 1;
    /// <summary>The address already holds as many connections as one address may.</summary>
    public const byte RefuseAddressFull = 2;

    public static void WriteHeader(Span<byte> datagram, UdpKind kind)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(datagram, Magic);
        datagram[2] = Version;
        datagram[3] = (byte)kind;
    }

    /// <summary>The kind of a datagram, or false when it is not one of this transport and version.</summary>
    public static bool TryReadKind(ReadOnlySpan<byte> datagram, out UdpKind kind)
    {
        kind = 0;
        if (datagram.Length < HeaderBytes || datagram.Length > MaxDatagramBytes) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(datagram) != Magic || datagram[2] != Version) return false;
        if (datagram[3] is < (byte)UdpKind.Request or > (byte)UdpKind.Refuse) return false;
        kind = (UdpKind)datagram[3];
        return true;
    }

    public static byte[] Request(ulong nonce)
    {
        var datagram = new byte[HandshakeBytes];
        WriteHeader(datagram, UdpKind.Request);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        return datagram;
    }

    public static bool TryReadRequest(ReadOnlySpan<byte> datagram, out ulong nonce)
    {
        nonce = 0;
        if (datagram.Length != HandshakeBytes || datagram[12..].ContainsAnyExcept((byte)0)) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        return true;
    }

    /// <summary>A challenge or a connect: the two carry the same fields.</summary>
    public static byte[] Cookie(UdpKind kind, ulong nonce, uint slot, ReadOnlySpan<byte> cookie)
    {
        var datagram = new byte[HandshakeBytes];
        WriteHeader(datagram, kind);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(12), slot);
        cookie.CopyTo(datagram.AsSpan(16));
        return datagram;
    }

    public static bool TryReadCookie(ReadOnlySpan<byte> datagram, out ulong nonce, out uint slot, out ReadOnlySpan<byte> cookie)
    {
        nonce = 0;
        slot = 0;
        cookie = default;
        if (datagram.Length != HandshakeBytes) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        slot = BinaryPrimitives.ReadUInt32LittleEndian(datagram[12..]);
        cookie = datagram.Slice(16, CookieBytes);
        return true;
    }

    public static byte[] Connect(ulong nonce, uint slot, ReadOnlySpan<byte> cookie, ReadOnlySpan<byte> clientKey)
    {
        var datagram = new byte[ConnectBytes];
        WriteHeader(datagram, UdpKind.Connect);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(12), slot);
        cookie.CopyTo(datagram.AsSpan(16));
        clientKey.CopyTo(datagram.AsSpan(HandshakeBytes));
        return datagram;
    }

    public static bool TryReadConnect(ReadOnlySpan<byte> datagram, out ulong nonce, out uint slot, out ReadOnlySpan<byte> cookie, out ReadOnlySpan<byte> clientKey)
    {
        nonce = 0;
        slot = 0;
        cookie = clientKey = default;
        if (datagram.Length != ConnectBytes) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        slot = BinaryPrimitives.ReadUInt32LittleEndian(datagram[12..]);
        cookie = datagram.Slice(16, CookieBytes);
        clientKey = datagram.Slice(HandshakeBytes, UdpIdentity.PublicKeyBytes);
        return true;
    }

    public static byte[] Accept(ulong nonce, ulong token, ReadOnlySpan<byte> serverKey, ReadOnlySpan<byte> identity, ReadOnlySpan<byte> signature)
    {
        var datagram = new byte[AcceptBytes];
        WriteHeader(datagram, UdpKind.Accept);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(12), token);
        serverKey.CopyTo(datagram.AsSpan(20));
        identity.CopyTo(datagram.AsSpan(20 + UdpIdentity.PublicKeyBytes));
        signature.CopyTo(datagram.AsSpan(20 + 2 * UdpIdentity.PublicKeyBytes));
        return datagram;
    }

    public static bool TryReadAccept(ReadOnlySpan<byte> datagram, out ulong nonce, out ulong token, out ReadOnlySpan<byte> serverKey, out ReadOnlySpan<byte> identity,
        out ReadOnlySpan<byte> signature)
    {
        nonce = token = 0;
        serverKey = identity = signature = default;
        if (datagram.Length != AcceptBytes) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        token = BinaryPrimitives.ReadUInt64LittleEndian(datagram[12..]);
        serverKey = datagram.Slice(20, UdpIdentity.PublicKeyBytes);
        identity = datagram.Slice(20 + UdpIdentity.PublicKeyBytes, UdpIdentity.PublicKeyBytes);
        signature = datagram.Slice(20 + 2 * UdpIdentity.PublicKeyBytes, UdpIdentity.SignatureBytes);
        return token != 0;
    }

    /// <summary>The token of a sealed datagram of a connection (data or close), read from what stays in the clear; false when it is too short to be one.</summary>
    public static bool TryReadSealedToken(ReadOnlySpan<byte> datagram, out ulong token)
    {
        token = 0;
        if (datagram.Length < ClearBytes + SealBytes) return false;
        token = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        return true;
    }

    public static byte[] Refuse(ulong nonce, byte reason)
    {
        var datagram = new byte[RefuseBytes];
        WriteHeader(datagram, UdpKind.Refuse);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        datagram[12] = reason;
        return datagram;
    }

    public static bool TryReadRefuse(ReadOnlySpan<byte> datagram, out ulong nonce, out byte reason)
    {
        nonce = 0;
        reason = 0;
        if (datagram.Length != RefuseBytes || datagram[12] is not (RefuseFull or RefuseAddressFull)) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        reason = datagram[12];
        return true;
    }

    public static byte[] Close(ulong token)
    {
        var datagram = new byte[CloseBytes];
        WriteHeader(datagram, UdpKind.Close);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), token);
        return datagram;
    }

    public static bool TryReadClose(ReadOnlySpan<byte> datagram, out ulong token)
    {
        token = 0;
        if (datagram.Length != CloseBytes) return false;
        token = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        return true;
    }

    public static void WriteDataHeader(Span<byte> datagram, in UdpDataHeader header)
    {
        WriteHeader(datagram, UdpKind.Data);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram[4..], header.Token);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram[12..], header.SendTime);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram[16..], header.EchoTime);
        BinaryPrimitives.WriteUInt16LittleEndian(datagram[20..], header.EchoHold);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram[22..], header.ReliableAck);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram[26..], header.AckMask);
        BinaryPrimitives.WriteUInt32LittleEndian(datagram[30..], header.UrgentAck);
    }

    public static bool TryReadDataHeader(ReadOnlySpan<byte> datagram, out UdpDataHeader header)
    {
        header = default;
        if (datagram.Length < DataHeaderBytes) return false;
        header = new UdpDataHeader(BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]), BinaryPrimitives.ReadUInt32LittleEndian(datagram[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[16..]), BinaryPrimitives.ReadUInt16LittleEndian(datagram[20..]), BinaryPrimitives.ReadUInt32LittleEndian(datagram[22..]),
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[26..]), BinaryPrimitives.ReadUInt32LittleEndian(datagram[30..]));
        return true;
    }

    /// <summary>
    /// Checks every section of a data datagram without applying anything: kinds, lengths, value ranges, no trailing bytes. Only a datagram that passes is taken apart.
    /// </summary>
    public static bool SectionsAreValid(ReadOnlySpan<byte> datagram)
    {
        var position = DataHeaderBytes;
        while (position < datagram.Length)
        {
            var kind = datagram[position];
            if (kind == SectionSegment)
            {
                if (datagram.Length - position < SegmentOverhead) return false;
                var length = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 5)..]);
                if (length is 0 or > MaxSegmentBytes || length > datagram.Length - position - SegmentOverhead) return false;
                position += SegmentOverhead + length;
            }
            else if (kind == SectionUrgent)
            {
                if (datagram.Length - position < UrgentOverhead) return false;
                var index = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 5)..]);
                var count = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 7)..]);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(datagram[(position + 9)..]);
                if (count is 0 or > MaxUrgentFragments || index >= count) return false;
                if (length is 0 or > MaxUrgentFragmentBytes || length > datagram.Length - position - UrgentOverhead) return false;
                position += UrgentOverhead + length;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The cookie (prefix 1) or the token material (prefix 2) of a client at an address: HMAC-SHA-256 with the server's secret.</summary>
    public static void Mac(ReadOnlySpan<byte> secret, byte prefix, IPEndPoint address, ulong nonce, uint slot, Span<byte> destination)
    {
        Span<byte> input = stackalloc byte[1 + 16 + 2 + 8 + 4];
        input.Clear();
        input[0] = prefix;
        address.Address.TryWriteBytes(input[1..], out var written);
        var position = 1 + written;
        BinaryPrimitives.WriteUInt16LittleEndian(input[position..], (ushort)address.Port);
        BinaryPrimitives.WriteUInt64LittleEndian(input[(position + 2)..], nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(input[(position + 10)..], slot);
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(secret, input[..(position + 14)], mac);
        mac[..destination.Length].CopyTo(destination);
    }

    public static ulong Token(ReadOnlySpan<byte> secret, IPEndPoint address, ulong nonce, uint slot)
    {
        Span<byte> bytes = stackalloc byte[8];
        Mac(secret, 2, address, nonce, slot, bytes);
        var token = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return token == 0 ? 1 : token;
    }
}
