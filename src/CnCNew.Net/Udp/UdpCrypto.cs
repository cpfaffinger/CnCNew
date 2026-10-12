using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CnCNew.Net.Udp;

/// <summary>
/// The permanent key pair of a server (docs/NETWORK-PROTOCOL.md section 20.2): ECDSA on P-256. The server signs every key exchange with it; a client remembers the
/// public half the first time it meets a server and refuses another one later (trust on first use - the host of the client keeps the list, this layer opens no file).
/// </summary>
public sealed class UdpIdentity : IDisposable
{
    public const int PublicKeyBytes = 64;
    public const int SignatureBytes = 64;

    private readonly ECDsa _key;

    private UdpIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = UdpCrypto.ExportPublic(key.ExportParameters(false));
        Fingerprint = UdpCrypto.FingerprintOf(PublicKey);
    }

    /// <summary>The public key as X and Y, 32 bytes each.</summary>
    public byte[] PublicKey { get; }

    /// <summary>SHA-256 of the public key in hexadecimal: what an operator publishes and a client shows.</summary>
    public string Fingerprint { get; }

    public static UdpIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>From the PKCS#8 bytes <see cref="ExportPrivateKey"/> wrote. Throws <see cref="CryptographicException"/> for anything else.</summary>
    public static UdpIdentity Import(ReadOnlySpan<byte> pkcs8)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out var read);
            if (read != pkcs8.Length || key.KeySize != 256) throw new CryptographicException("The key is not a P-256 private key in PKCS#8.");
            return new UdpIdentity(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public byte[] ExportPrivateKey() => _key.ExportPkcs8PrivateKey();

    /// <summary>The private key as PEM text (PKCS#8), for a host that keeps it in a file.</summary>
    public string ExportPem() => _key.ExportPkcs8PrivateKeyPem();

    /// <summary>From the text <see cref="ExportPem"/> wrote. Throws <see cref="CryptographicException"/> or <see cref="ArgumentException"/> for anything else.</summary>
    public static UdpIdentity ImportPem(string pem)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
            if (key.KeySize != 256) throw new CryptographicException("The key is not a P-256 key.");
            return new UdpIdentity(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    internal void Sign(ReadOnlySpan<byte> transcript, Span<byte> signature)
    {
        if (!_key.TrySignData(transcript, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation, out var written) || written != SignatureBytes)
            throw new CryptographicException("The signature has an unexpected length.");
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>The cryptography of the UDP transport, BCL only (docs/NETWORK-PROTOCOL.md section 20.2): P-256 for the exchange and the signature, HKDF-SHA-256, AES-256-GCM.</summary>
public static class UdpCrypto
{
    private static readonly byte[] Label = "CnCNew transport 2"u8.ToArray();

    internal static byte[] ExportPublic(ECParameters parameters)
    {
        var bytes = new byte[UdpIdentity.PublicKeyBytes];
        // The BCL returns both coordinates in the full width of the field.
        parameters.Q.X!.CopyTo(bytes, 32 - parameters.Q.X.Length);
        parameters.Q.Y!.CopyTo(bytes, 64 - parameters.Q.Y.Length);
        return bytes;
    }

    private static ECParameters ImportPublic(ReadOnlySpan<byte> key)
        => new() { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[..32].ToArray(), Y = key[32..64].ToArray() } };

    public static string FingerprintOf(ReadOnlySpan<byte> publicKey) => Convert.ToHexString(SHA256.HashData(publicKey));

    /// <summary>
    /// The cryptography of one handshake, both ends in one place: two exchange keys, the server's signature over the transcript, the client's check of it, and the
    /// keys of both directions. For measuring what a connection costs (NetTool seal-cost) and for tests; the endpoints do the same steps over the wire.
    /// </summary>
    public static (UdpCipher Client, UdpCipher Server) Exchange(UdpIdentity identity, ulong nonce, ulong token)
    {
        using var client = new UdpKeyExchange();
        using var server = new UdpKeyExchange();
        var transcript = Transcript(nonce, token, client.PublicKey, server.PublicKey, identity.PublicKey);
        Span<byte> signature = stackalloc byte[UdpIdentity.SignatureBytes];
        identity.Sign(transcript, signature);
        if (!Verify(identity.PublicKey, transcript, signature)) throw new CryptographicException("The signature of the transcript does not verify.");
        var ofClient = Derive(client, server.PublicKey, transcript, isClient: true);
        var ofServer = Derive(server, client.PublicKey, transcript, isClient: false);
        return ofClient is null || ofServer is null ? throw new CryptographicException("The exchange keys are not points of the curve.") : (ofClient, ofServer);
    }

    /// <summary>What the server signs and both ends derive their keys from: everything the two said to each other in the handshake.</summary>
    internal static byte[] Transcript(ulong nonce, ulong token, ReadOnlySpan<byte> clientKey, ReadOnlySpan<byte> serverKey, ReadOnlySpan<byte> serverIdentity)
    {
        var bytes = new byte[Label.Length + 16 + 3 * UdpIdentity.PublicKeyBytes];
        Label.CopyTo(bytes, 0);
        var position = Label.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(position), nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(position + 8), token);
        clientKey.CopyTo(bytes.AsSpan(position + 16));
        serverKey.CopyTo(bytes.AsSpan(position + 16 + UdpIdentity.PublicKeyBytes));
        serverIdentity.CopyTo(bytes.AsSpan(position + 16 + 2 * UdpIdentity.PublicKeyBytes));
        return bytes;
    }

    /// <summary>Whether the signature over the transcript is the one of the identity key. False for a key that is not a point of the curve, too.</summary>
    internal static bool Verify(ReadOnlySpan<byte> identity, ReadOnlySpan<byte> transcript, ReadOnlySpan<byte> signature)
    {
        try
        {
            using var key = ECDsa.Create(ImportPublic(identity));
            return key.VerifyData(transcript, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// The two keys of a connection from the exchange: one per direction, so the two ends never encrypt under the same key and number. Null when the peer's key is not a
    /// point of the curve.
    /// </summary>
    internal static UdpCipher? Derive(UdpKeyExchange own, ReadOnlySpan<byte> peerKey, ReadOnlySpan<byte> transcript, bool isClient)
    {
        byte[] secret;
        try
        {
            using var peer = ECDiffieHellman.Create(ImportPublic(peerKey));
            secret = own.Key.DeriveRawSecretAgreement(peer.PublicKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
        Span<byte> keys = stackalloc byte[64];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, keys, SHA256.HashData(transcript), Label);
        CryptographicOperations.ZeroMemory(secret);
        var cipher = isClient ? new UdpCipher(keys[..32], keys[32..]) : new UdpCipher(keys[32..], keys[..32]);
        CryptographicOperations.ZeroMemory(keys);
        return cipher;
    }
}

/// <summary>One end's key of a single exchange: made for one connection attempt and forgotten with it, so a key that leaks later opens no earlier match.</summary>
public sealed class UdpKeyExchange : IDisposable
{
    internal ECDiffieHellman Key { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    public byte[] PublicKey { get; }

    public UdpKeyExchange() => PublicKey = UdpCrypto.ExportPublic(Key.ExportParameters(false));

    public void Dispose() => Key.Dispose();
}

/// <summary>
/// Seals and opens the datagrams of one connection (docs/NETWORK-PROTOCOL.md section 20.3): AES-256-GCM, a key per direction, and a number per datagram that is
/// never used twice - a repetition of the same content is a new datagram with a new number. The header up to the number is authenticated, the rest encrypted. A
/// datagram whose number was seen, or is older than the window, is a replay and is dropped.
/// </summary>
public sealed class UdpCipher : IDisposable
{
    public const int WindowDatagrams = 1_024;
    private const int NonceBytes = 12;

    private readonly AesGcm _seal;
    private readonly AesGcm _open;
    private readonly ulong[] _seen = new ulong[WindowDatagrams / 64];
    private ulong _sent;
    private ulong _highest;
    private bool _any;

    internal UdpCipher(ReadOnlySpan<byte> sendKey, ReadOnlySpan<byte> receiveKey)
    {
        _seal = new AesGcm(sendKey, UdpFormat.TagBytes);
        _open = new AesGcm(receiveKey, UdpFormat.TagBytes);
    }

    /// <summary>Datagrams that did not authenticate (damaged, forged, of another connection) and datagrams whose number was already taken.</summary>
    public long Forged { get; private set; }
    public long Replayed { get; private set; }
    public ulong Sent => _sent;

    /// <summary>The wire form of a plain datagram (header, token, body): header, token, number, encrypted body, tag. Returns its length.</summary>
    public int Seal(ReadOnlySpan<byte> plain, Span<byte> wire)
    {
        var body = plain[UdpFormat.ClearBytes..];
        plain[..UdpFormat.ClearBytes].CopyTo(wire);
        var number = ++_sent;
        BinaryPrimitives.WriteUInt64LittleEndian(wire[UdpFormat.ClearBytes..], number);
        Span<byte> nonce = stackalloc byte[NonceBytes];
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], number);
        var associated = UdpFormat.ClearBytes + 8;
        _seal.Encrypt(nonce, body, wire.Slice(associated, body.Length), wire.Slice(associated + body.Length, UdpFormat.TagBytes), wire[..associated]);
        return plain.Length + UdpFormat.SealBytes;
    }

    /// <summary>The plain datagram of a wire datagram, or false: too short, not authentic, or a number that was already taken. Nothing of a refused datagram is used.</summary>
    public bool TryOpen(ReadOnlySpan<byte> wire, Span<byte> plain, out int length)
    {
        length = 0;
        var associated = UdpFormat.ClearBytes + 8;
        if (wire.Length < associated + UdpFormat.TagBytes) return false;
        var number = BinaryPrimitives.ReadUInt64LittleEndian(wire[UdpFormat.ClearBytes..]);
        if (number == 0 || (_any && number + WindowDatagrams <= _highest) || (_any && number <= _highest && Seen(number)))
        {
            Replayed++;
            return false;
        }
        var bodyLength = wire.Length - associated - UdpFormat.TagBytes;
        Span<byte> nonce = stackalloc byte[NonceBytes];
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], number);
        try
        {
            _open.Decrypt(nonce, wire.Slice(associated, bodyLength), wire.Slice(associated + bodyLength, UdpFormat.TagBytes), plain.Slice(UdpFormat.ClearBytes, bodyLength), wire[..associated]);
        }
        catch (CryptographicException)
        {
            Forged++;
            return false;
        }
        // Only an authentic datagram moves the window: a forged number cannot make real ones look old.
        Mark(number);
        wire[..UdpFormat.ClearBytes].CopyTo(plain);
        length = UdpFormat.ClearBytes + bodyLength;
        return true;
    }

    private bool Seen(ulong number) => (_seen[number % WindowDatagrams / 64] & (1UL << (int)(number % 64))) != 0;

    private void Mark(ulong number)
    {
        if (!_any || number > _highest)
        {
            // Everything between the old and the new highest number has not been seen (yet); numbers a whole window apart share a bit.
            var from = _any ? _highest + 1 : number;
            if (number - from >= WindowDatagrams) Array.Clear(_seen);
            else for (var clear = from; clear < number; clear++) _seen[clear % WindowDatagrams / 64] &= ~(1UL << (int)(clear % 64));
            _highest = number;
            _any = true;
        }
        _seen[number % WindowDatagrams / 64] |= 1UL << (int)(number % 64);
    }

    public void Dispose()
    {
        _seal.Dispose();
        _open.Dispose();
    }
}
