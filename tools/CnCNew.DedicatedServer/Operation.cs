using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CnCNew.Net.Udp;

// What an operator of the server works with (docs/DEDICATED-SERVER.md section 10): the ban list, the log files and how long they are kept, the server's key.

/// <summary>
/// The addresses that get no answer from this server: a text file, one address or network per line (<c>203.0.113.7</c>, <c>203.0.113.0/24</c>, <c>2001:db8::/32</c>),
/// <c>#</c> starts a comment. The file is read again when it changes, so an operator bans without stopping the server. A line that is neither an address nor a
/// network is an error of the file, named with its number - never a silent default.
/// </summary>
sealed class BanList
{
    private readonly record struct Network(byte[] Address, int Bits);

    private readonly string _file;
    private List<Network> _networks = [];
    private HashSet<string> _keys = new(StringComparer.Ordinal);
    private DateTime _stamp = DateTime.MinValue;

    public BanList(string file)
    {
        _file = file;
        Reload(out var problem);
        Problem = problem;
    }

    /// <summary>What was wrong with the file when the server started; empty when nothing was.</summary>
    public string Problem { get; }

    public int Count => _networks.Count + _keys.Count;

    /// <summary>Whether a player key (its fingerprint, 64 hexadecimal digits) is on the list: a line "key:&lt;fingerprint&gt;".</summary>
    public bool ContainsKey(string fingerprint) => _keys.Contains(fingerprint);

    public static bool TryParse(string text, out byte[] address, out int bits)
    {
        address = [];
        bits = 0;
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (!IPAddress.TryParse(slash < 0 ? text : text[..slash], out var parsed)) return false;
        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        address = parsed.GetAddressBytes();
        bits = address.Length * 8;
        return slash < 0 || (int.TryParse(text[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out bits) && bits >= 0 && bits <= address.Length * 8);
    }

    /// <summary>Reads the file if it changed since the last look. True when the list is another one now; throws <see cref="FormatException"/> for a line that is no entry.</summary>
    public bool Reload(out string problem)
    {
        problem = string.Empty;
        if (_file.Length == 0) return false;
        if (!File.Exists(_file))
        {
            var had = Count > 0;
            _networks = [];
            _keys = new HashSet<string>(StringComparer.Ordinal);
            _stamp = DateTime.MinValue;
            return had;
        }
        var stamp = File.GetLastWriteTimeUtc(_file);
        if (stamp == _stamp) return false;
        var networks = new List<Network>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        try
        {
            foreach (var raw in File.ReadLines(_file))
            {
                number++;
                var hash = raw.IndexOf('#', StringComparison.Ordinal);
                var line = (hash < 0 ? raw : raw[..hash]).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("key:", StringComparison.OrdinalIgnoreCase))
                {
                    var fingerprint = line[4..].Trim().ToUpperInvariant();
                    if (fingerprint.Length != 64 || fingerprint.AsSpan().ContainsAnyExcept("0123456789ABCDEF"))
                    {
                        problem = $"line {number} of {_file} is not a player key (key: and 64 hexadecimal digits): '{line}'";
                        _stamp = stamp;
                        return false;
                    }
                    keys.Add(fingerprint);
                    continue;
                }
                if (!TryParse(line, out var address, out var bits))
                {
                    // The list as it was stays in force: a typing error must not open the door to everybody on it.
                    problem = $"line {number} of {_file} is neither an address nor a network: '{line}'";
                    _stamp = stamp;
                    return false;
                }
                networks.Add(new Network(address, bits));
            }
        }
        catch (IOException exception)
        {
            problem = exception.Message;
            return false;
        }
        _stamp = stamp;
        _networks = networks;
        _keys = keys;
        return true;
    }

    public bool Contains(IPAddress address)
    {
        if (_networks.Count == 0) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var written)) return false;
        foreach (var network in _networks)
        {
            if (network.Address.Length != written) continue;
            var whole = network.Bits / 8;
            if (!bytes[..whole].SequenceEqual(network.Address.AsSpan(0, whole))) continue;
            var rest = network.Bits % 8;
            if (rest == 0) return true;
            var mask = (byte)(0xFF00 >> rest);
            if ((bytes[whole] & mask) == (network.Address[whole] & mask)) return true;
        }
        return false;
    }
}

/// <summary>
/// The server's lines, on standard output and - with a log directory - in one file per day (UTC), each line with the time of day in front; files older than the
/// retention are deleted at the start and at every change of day. An address in a line is personal data: unless the operator asks for full addresses it is written
/// as its network (the last byte of IPv4, the last 80 bits of IPv6 removed) and without its port.
/// </summary>
sealed class ServerLog : IDisposable
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly bool _fullAddresses;
    private StreamWriter? _file;
    private DateTime _day;

    public ServerLog(string directory, int retentionDays, bool fullAddresses)
    {
        _directory = directory;
        _retentionDays = retentionDays;
        _fullAddresses = fullAddresses;
        if (directory.Length > 0) Directory.CreateDirectory(directory);
    }

    public long FilesDeleted { get; private set; }

    /// <summary>An address as the log shows it.</summary>
    public string Shown(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (_fullAddresses) return address.ToString();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            bytes[3] = 0;
            return new IPAddress(bytes) + "/24";
        }
        Array.Clear(bytes, 6, 10);
        return new IPAddress(bytes) + "/48";
    }

    public string Shown(IPEndPoint endpoint) => _fullAddresses ? endpoint.ToString() : Shown(endpoint.Address);

    public void Write(string line)
    {
        Console.WriteLine(line);
        if (_directory.Length == 0) return;
        var now = DateTime.UtcNow;
        if (_file is null || now.Date != _day)
        {
            _file?.Dispose();
            _day = now.Date;
            _file = new StreamWriter(Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"server-{_day:yyyyMMdd}.log")), append: true) { AutoFlush = true };
            Expire(_directory, "server-*.log", _retentionDays);
        }
        _file.Write(now.ToString("HH:mm:ss.fff'Z '", CultureInfo.InvariantCulture));
        _file.WriteLine(line);
    }

    /// <summary>Deletes the files of a directory that match the pattern and were last written more than so many days ago; 0 keeps everything.</summary>
    public int Expire(string directory, string pattern, int days)
    {
        if (days <= 0 || !Directory.Exists(directory)) return 0;
        var deleted = 0;
        var limit = DateTime.UtcNow.AddDays(-days);
        foreach (var file in Directory.EnumerateFiles(directory, pattern))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= limit) continue;
                File.Delete(file);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file somebody holds open is tried again the next day.
            }
        }
        FilesDeleted += deleted;
        return deleted;
    }

    public void Dispose() => _file?.Dispose();
}

static class ServerKey
{
    /// <summary>
    /// The permanent key of the server from its file - made and written on the first start. Without a file name the key lasts as long as the process: every client
    /// that remembered the old one will refuse the server after a restart, which is what a test wants and an operator does not.
    /// </summary>
    public static UdpIdentity Load(string file, out bool created)
    {
        created = false;
        if (file.Length == 0) return UdpIdentity.Create();
        if (File.Exists(file))
        {
            try
            {
                return UdpIdentity.ImportPem(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                throw new ArgumentException($"The key file {file} does not hold a P-256 private key in PEM: {exception.Message}");
            }
        }
        var identity = UdpIdentity.Create();
        var directory = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(file, identity.ExportPem());
        // Whoever reads this file is the server: on a system with file modes nobody but its owner may.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        created = true;
        return identity;
    }
}
