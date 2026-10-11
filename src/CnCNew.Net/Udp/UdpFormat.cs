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
    public int UrgentWindow { get; init; } = 1_024;
    public int MaxMessageBytes { get; init; } = Protocol.MaxResumeBytes + Protocol.MaxMessageBytes;
    public int MaxConnections { get; init; } = 256;

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
    public const byte Version = 1;
    public const int MaxDatagramBytes = 1_200;
    public const int HeaderBytes = 4;
    public const int HandshakeBytes = 32;
    public const int AcceptBytes = 20;
    public const int RefuseBytes = 13;
    public const int CloseBytes = 12;
    public const int DataHeaderBytes = 34;
    public const int SegmentOverhead = 7;
    public const int UrgentOverhead = 11;
    public const int MaxSegmentBytes = MaxDatagramBytes - DataHeaderBytes - SegmentOverhead;
    public const int MaxUrgentFragmentBytes = MaxDatagramBytes - DataHeaderBytes - UrgentOverhead;
    public const int MaxUrgentFragments = 64;
    public const int CookieBytes = 16;
    public const ushort NoEcho = 0xFFFF;
    public const byte SectionSegment = 1;
    public const byte SectionUrgent = 2;
    public const byte RefuseFull = 1;

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

    public static byte[] Accept(ulong nonce, ulong token)
    {
        var datagram = new byte[AcceptBytes];
        WriteHeader(datagram, UdpKind.Accept);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(4), nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(datagram.AsSpan(12), token);
        return datagram;
    }

    public static bool TryReadAccept(ReadOnlySpan<byte> datagram, out ulong nonce, out ulong token)
    {
        nonce = token = 0;
        if (datagram.Length != AcceptBytes) return false;
        nonce = BinaryPrimitives.ReadUInt64LittleEndian(datagram[4..]);
        token = BinaryPrimitives.ReadUInt64LittleEndian(datagram[12..]);
        return token != 0;
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
        if (datagram.Length != RefuseBytes || datagram[12] != RefuseFull) return false;
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
