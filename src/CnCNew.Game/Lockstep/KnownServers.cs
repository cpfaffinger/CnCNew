using System.Net;
using System.Text.Json;

namespace CnCNew.Game.Lockstep;

/// <summary>What a client decided about the key a server showed.</summary>
public enum ServerTrust
{
    /// <summary>The key this client remembered for the server, or the one it was told to expect.</summary>
    Known,
    /// <summary>The first meeting: the key is remembered from now on.</summary>
    New,
    /// <summary>Another key than the one remembered or expected: the server is not the one it was, or somebody stands in between.</summary>
    Changed,
}

/// <summary>
/// The keys of the servers a client has met (docs/NETWORK-PROTOCOL.md section 20.2): trust on first use. The first time a client connects to an address it remembers
/// the fingerprint of the key the server proved to hold; from then on a server at that address with another key is refused, with both fingerprints named, until the
/// person removes the entry. A file of a few lines of JSON, address to fingerprint; without a file the list lasts as long as the process.
/// <para>What this protects against: somebody who comes between client and server later. What it does not: somebody who was already there at the first meeting -
/// whoever wants to exclude that compares the fingerprint with the one the operator published (<c>--server-key</c>).</para>
/// </summary>
public sealed class KnownServers
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _file;
    private readonly SortedDictionary<string, string> _known = new(StringComparer.Ordinal);

    /// <param name="file">Where the list is kept; null keeps it in memory. A file that cannot be read as such a list is an error, not an empty list.</param>
    public KnownServers(string? file = null)
    {
        _file = file;
        if (file is null || !File.Exists(file)) return;
        try
        {
            foreach (var (address, fingerprint) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? []) _known[address] = fingerprint.ToUpperInvariant();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The list of known servers {file} cannot be read: {exception.Message}");
        }
    }

    public int Count => _known.Count;
    public string? FingerprintOf(IPEndPoint server) => _known.GetValueOrDefault(server.ToString());

    /// <summary>Judges the fingerprint a server showed, and remembers it when the server is new.</summary>
    public ServerTrust Check(IPEndPoint server, string fingerprint)
    {
        fingerprint = fingerprint.ToUpperInvariant();
        if (_known.TryGetValue(server.ToString(), out var known)) return known == fingerprint ? ServerTrust.Known : ServerTrust.Changed;
        _known[server.ToString()] = fingerprint;
        Save();
        return ServerTrust.New;
    }

    /// <summary>Forgets a server: the next key it shows is taken as new. For a person who knows why the key changed.</summary>
    public bool Forget(IPEndPoint server)
    {
        if (!_known.Remove(server.ToString())) return false;
        Save();
        return true;
    }

    private void Save()
    {
        if (_file is null) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(_file));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_file, JsonSerializer.Serialize(_known, Json));
    }
}

/// <summary>
/// The permanent key of the player on this machine (docs/NETWORK-PROTOCOL.md section 20.5): a file, made the first time it is asked for. A server sees its public half
/// and can know the same player again, or keep it out; whoever deletes the file is somebody new to every server.
/// </summary>
public static class PlayerKeyFile
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CnCNew", "player-key.pem");

    public static CnCNew.Net.Udp.UdpIdentity Load(string file)
    {
        if (File.Exists(file)) return CnCNew.Net.Udp.UdpIdentity.ImportPem(File.ReadAllText(file));
        var key = CnCNew.Net.Udp.UdpIdentity.Create();
        if (Path.GetDirectoryName(Path.GetFullPath(file)) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        File.WriteAllText(file, key.ExportPem());
        return key;
    }
}
