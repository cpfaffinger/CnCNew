using System.Net;
using System.Net.Sockets;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;

namespace CnCNew.Game.Shell;

/// <summary>A server this game hosts for others ("Host game"): where it listens and which key it shows.</summary>
public interface IShellHost : IDisposable
{
    IPEndPoint LocalEndPoint { get; }
    string Fingerprint { get; }
    bool IsRunning { get; }
}

/// <summary>
/// What the multiplayer screens of the shell need from the machine (docs/GAME-SHELL.md section 15): a lobby to a server, a server of its own, the addresses and names
/// a person typed before. The game's implementation is <see cref="ShellNetwork"/>; the tests of the screens hand in a scripted one, so that every state a server can
/// put a client in has a picture without a socket.
/// </summary>
public interface IShellNetwork
{
    /// <summary>The UDP port a server listens on when nobody says otherwise, and the one a typed address without a port means.</summary>
    int DefaultPort { get; }
    /// <summary>The addresses of this machine as the direct-connect screen shows them to somebody who hosts.</summary>
    string LocalAddresses { get; }
    string PlayerName { get; }
    IReadOnlyList<string> RecentServers { get; }
    void Remember(string playerName, string? server);
    /// <summary>Turns what a person typed into an address; null when it is none (the screen says so).</summary>
    IPEndPoint? Resolve(string text);
    INetLobby CreateLobby(string playerName);
    /// <summary>Starts a server in this game. Throws <see cref="RemoteRelayException"/> with a message fit to show.</summary>
    IShellHost Host();
    /// <summary>The length of a match a host of this game sets, in seconds of game time; 0 is the standard match (one hour). A check plays shorter ones.</summary>
    int MatchSeconds { get; }
    /// <summary>The fingerprint of a map's terrain as the simulation computes it - what a room states so that every member builds the same ground; null when the map cannot be read.</summary>
    string? TerrainFingerprint(CnCNew.Game.Data.GameData data, string mapPath);
}

/// <summary>One visit to a server: the lobby, the server this game hosts for it (if it does), and who the person is there. The shell pumps it once a frame.</summary>
public sealed class ShellNetSession(INetLobby lobby, IShellHost? host, string playerName, IPEndPoint server) : IDisposable
{
    public INetLobby Lobby { get; } = lobby;
    public IShellHost? Host { get; } = host;
    public string PlayerName { get; } = playerName;
    public IPEndPoint Server { get; } = server;
    /// <summary>The room's name and password as this client entered it, for the way back after a match.</summary>
    public string Room { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>The relay of the running match while the game owns it; the shell reads the end of the match from it.</summary>
    public RemoteRelay? Relay { get; set; }

    public void Dispose()
    {
        Lobby.Disconnect();
        (Lobby as IDisposable)?.Dispose();
        Relay?.Dispose();
        Host?.Dispose();
    }
}

/// <summary>
/// The game's own <see cref="IShellNetwork"/>: lobbies over UDP, a hosted server, and four small files in one directory of the person's settings - the servers met
/// (<c>known-servers.txt</c>), the player's key (<c>player-key.pem</c>), the key this game shows when it hosts (<c>host-key.pem</c>) and the name and addresses typed
/// before (<c>recent.txt</c>). A check hands in a directory of its run; nothing here touches another place.
/// </summary>
public sealed class ShellNetwork : IShellNetwork
{
    public const int StandardPort = 27_960;
    private const int MaxRecent = 8;

    private readonly string _definitions;
    private readonly string _directory;
    private readonly KnownServers _known;
    private readonly List<string> _recent = [];
    private UdpIdentity? _playerKey;

    /// <param name="definitionFingerprint">The fingerprint of the game data this game runs (<c>GameData.Fingerprint</c>): what a room compares.</param>
    /// <param name="directory">Where the four files live; created when missing.</param>
    public ShellNetwork(string definitionFingerprint, string directory, int port = StandardPort, bool usePlayerKey = true)
    {
        _definitions = definitionFingerprint;
        _directory = directory;
        DefaultPort = port;
        UsePlayerKey = usePlayerKey;
        Directory.CreateDirectory(directory);
        _known = new KnownServers(Path.Combine(directory, "known-servers.txt"));
        var recent = Path.Combine(directory, "recent.txt");
        if (File.Exists(recent))
        {
            var lines = File.ReadAllLines(recent);
            if (lines.Length > 0) PlayerName = lines[0].Trim();
            _recent.AddRange(lines.Skip(1).Select(line => line.Trim()).Where(line => line.Length > 0).Take(MaxRecent));
        }
    }

    public int DefaultPort { get; }
    public bool UsePlayerKey { get; }
    public string PlayerName { get; private set; } = string.Empty;
    public IReadOnlyList<string> RecentServers => _recent;
    /// <summary>Options of the server this game hosts; a check shortens its time limits here.</summary>
    public HostedServerOptions HostOptions { get; init; } = new();
    public NetLobbyOptions? LobbyOptions { get; init; }
    public int MatchSeconds { get; init; }

    public string LocalAddresses
    {
        get
        {
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName()).Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    .Select(address => address.ToString()).Order(StringComparer.Ordinal).Take(3).ToArray();
                return addresses.Length == 0 ? IPAddress.Loopback.ToString() : string.Join(", ", addresses);
            }
            catch (SocketException)
            {
                return IPAddress.Loopback.ToString();
            }
        }
    }

    public void Remember(string playerName, string? server)
    {
        PlayerName = playerName;
        if (server is { Length: > 0 })
        {
            _recent.RemoveAll(entry => entry.Equals(server, StringComparison.OrdinalIgnoreCase));
            _recent.Insert(0, server);
            if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        }
        File.WriteAllLines(Path.Combine(_directory, "recent.txt"), [playerName, .. _recent]);
    }

    public IPEndPoint? Resolve(string text) => ParseAddress(text, DefaultPort);

    /// <summary><c>address</c>, <c>address:port</c>, <c>[v6]:port</c> or a host name with or without a port; null when it is none of these or the name is unknown.</summary>
    public static IPEndPoint? ParseAddress(string text, int defaultPort)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (IPEndPoint.TryParse(text, out var literal)) return literal.Port == 0 ? new IPEndPoint(literal.Address, defaultPort) : literal;
        var host = text;
        var port = defaultPort;
        var colon = text.LastIndexOf(':');
        if (colon > 0)
        {
            if (!int.TryParse(text[(colon + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) || port is <= 0 or > 65_535) return null;
            host = text[..colon];
        }
        if (Uri.CheckHostName(host) != UriHostNameType.Dns) return null;
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            return address is null ? null : new IPEndPoint(address, port);
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public INetLobby CreateLobby(string playerName)
    {
        if (UsePlayerKey) _playerKey ??= PlayerKeyFile.Load(Path.Combine(_directory, "player-key.pem"));
        var options = LobbyOptions ?? new NetLobbyOptions(playerName, _definitions);
        return new NetLobby(options with { PlayerName = playerName, DefinitionFingerprint = _definitions, Known = _known, PlayerKey = _playerKey });
    }

    public string? TerrainFingerprint(CnCNew.Game.Data.GameData data, string mapPath)
    {
        if (!data.Content.Exists(mapPath)) return null;
        try
        {
            using var stream = data.Content.OpenRead(mapPath);
            return CnCNew.Simulation.Terrain.TerrainMap.Build(CnCNew.Formats.SageMapDecoder.Decode(stream), mapPath).Fingerprint;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The server of "Host game". It takes the build that hosts: a game built from changed sources may host for games of the same build (a room compares the build of
    /// everybody who enters with its host's), where a dedicated server refuses changed builds unless its operator allows them.
    /// </summary>
    public IShellHost Host() => HostedServer.Start(HostOptions with
    {
        Port = HostOptions.Port == 0 ? DefaultPort : HostOptions.Port,
        KeyFile = HostOptions.KeyFile ?? Path.Combine(_directory, "host-key.pem"),
        Room = HostOptions.Room ?? new CnCNew.Server.RelayOptions(AllowModifiedBuilds: BuildIdentity.Current.Modified),
    });
}

/// <summary>
/// The words of the multiplayer screens for everything a server can say (docs/GAME-SHELL.md section 15.4). They are this project's own English - the original has no
/// text for a relay's reasons - and every value of every reason has one: a value without a text is an error here and in the tests, never a blank line or a number.
/// </summary>
public static class NetText
{
    public const string TitleNewServer = "New server";
    public const string TitleKeyChanged = "This is not the server you know";
    public const string TitleCannotConnect = "Cannot connect";
    public const string TitleCannotEnter = "Cannot enter the room";
    public const string TitleRemoved = "Removed from the room";
    public const string TitleConnectionLost = "Connection lost";
    public const string TitleCannotHost = "Cannot host";
    public const string TitleAddress = "Not an address";
    public const string TitleMatchEnded = "The match has ended";

    /// <summary>A fingerprint in groups of four, on two lines: what a person compares with what the operator published.</summary>
    public static string Fingerprint(string fingerprint)
    {
        var groups = Enumerable.Range(0, (fingerprint.Length + 3) / 4).Select(index => fingerprint.Substring(index * 4, Math.Min(4, fingerprint.Length - index * 4))).ToArray();
        var half = (groups.Length + 1) / 2;
        return groups.Length <= 4 ? string.Join(' ', groups) : string.Join(' ', groups.Take(half)) + "\n" + string.Join(' ', groups.Skip(half));
    }

    public static string NewServer(IPEndPoint server, string fingerprint)
        => $"You have not met the server at {server} before. Its key is\n{Fingerprint(fingerprint)}\nCompare it with what the host or operator told you. Trust this server from now on?";

    public static string KeyChanged(IPEndPoint server, string shown, string expected)
        => $"The server at {server} shows another key than the one you trusted.\nIt shows\n{Fingerprint(shown)}\nYou know\n{Fingerprint(expected)}\nNothing was sent to it. If the operator really changed the key, remove the server from your known servers and connect again.";

    public static string Reject(RejectReason reason) => reason switch
    {
        RejectReason.ProtocolVersion => "The server speaks another version of the network protocol. Both need the same build of the game.",
        RejectReason.SimulationVersion => "The room was opened by another build of the game. Everybody in a room needs the same build.",
        RejectReason.FormatVersion => "The room uses other format versions than this game. Everybody in a room needs the same build.",
        RejectReason.Definitions => "The room plays with other game data than this game has installed.",
        RejectReason.ModifiedBuild => "This server only takes released builds; this game was built from changed sources.",
        RejectReason.RoomFull => "The room is full.",
        RejectReason.MatchRunning => "The match of this room is already running and takes nobody new.",
        RejectReason.NotHost => "Only the host of the room can do that.",
        RejectReason.InvalidRequest => "The server did not take that request.",
        RejectReason.NotReady => "The room is not ready to start.",
        RejectReason.ServerFull => "The server has no room left.",
        RejectReason.SeatNotHeld => "Your seat in that match is no longer held.",
        RejectReason.CannotCatchUp => "The match has run for too long to catch up with it.",
        RejectReason.BadPassword => "That is not the room's password.",
        RejectReason.Kicked => "The host removed you from the room.",
        RejectReason.BadIdentity => "The server could not verify your player key.",
        RejectReason.Banned => "You are banned from this server.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No text for this reject reason."),
    };

    public static string Drop(DropReason reason) => reason switch
    {
        DropReason.Left => "left the match",
        DropReason.ConnectionLost => "lost the connection",
        DropReason.ProtocolViolation => "was dropped: its game sent something the server does not take",
        DropReason.Desync => "was dropped: its game was out of sync",
        DropReason.TooSlow => "was dropped: its game could not keep up",
        DropReason.Kicked => "was removed by the host",
        DropReason.Flood => "was dropped: its game sent too much",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No text for this drop reason."),
    };

    public static string End(EndReason reason) => reason switch
    {
        EndReason.Finished => "The match is over.",
        EndReason.Desync => "The games of the players were no longer in the same state. The match cannot go on.",
        EndReason.Abandoned => "Everybody has left the match.",
        EndReason.ServerLimit => "The server ended the match: it reached a limit of the server.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No text for this end reason."),
    };

    public static string Close(UdpCloseReason reason) => reason switch
    {
        UdpCloseReason.None => "The connection ended.",
        UdpCloseReason.Local => "This game closed the connection.",
        UdpCloseReason.Peer => "The server closed the connection.",
        UdpCloseReason.Timeout => "The server does not answer any more.",
        UdpCloseReason.Refused => "The server refused the connection.",
        UdpCloseReason.Violation => "The connection was closed: something arrived that does not belong to it.",
        UdpCloseReason.Replaced => "The connection was replaced by a newer one from this game.",
        UdpCloseReason.Untrusted => "The connection was not made: the server's key was not trusted.",
        UdpCloseReason.Flood => "The connection was closed: more was sent than the other side takes.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "No text for this close reason."),
    };

    /// <summary>Title and message of a failure on the way to a room or in it.</summary>
    public static (string Title, string Message) Failure(LobbyFailure failure, IPEndPoint server) => failure.Kind switch
    {
        LobbyFailureKind.NoAnswer => (TitleCannotConnect, $"No server answers at {server}."),
        LobbyFailureKind.ServerFull => (TitleCannotConnect, $"The server at {server} takes no more connections."),
        LobbyFailureKind.AddressFull => (TitleCannotConnect, $"The server at {server} takes no more connections from your address."),
        LobbyFailureKind.KeyChanged => (TitleKeyChanged, KeyChanged(server, failure.Shown, failure.Expected)),
        LobbyFailureKind.Rejected => (failure.Reject is RejectReason.Kicked or RejectReason.Banned ? TitleRemoved : TitleCannotEnter,
            failure.Detail.Length > 0 && failure.Reject is RejectReason.ProtocolVersion or RejectReason.SimulationVersion or RejectReason.FormatVersion or RejectReason.InvalidRequest or RejectReason.NotReady
                ? $"{Reject(failure.Reject)}\n({failure.Detail})" : Reject(failure.Reject)),
        LobbyFailureKind.ConnectionClosed => (TitleConnectionLost, Close(failure.Close)),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, "No text for this failure."),
    };

    /// <summary>
    /// What to tell the person when a match of a server is over: nothing when it ended by its own rules, otherwise what the server or the connection said.
    /// </summary>
    public static string? MatchEndNote(RemoteRelay relay, bool sessionOver)
    {
        var client = relay.Client;
        if (client.Desync is not null) return End(EndReason.Desync);
        if (client.End is { Reason: not EndReason.Finished } end) return End(end.Reason);
        if (sessionOver || client.End is not null) return null;
        return client.IsOpen ? null : Close(relay.CloseReason);
    }

    public static string RoomLine(ListedRoom room, string mapName)
    {
        var state = room.Phase switch
        {
            RoomPhase.Room => room.Seats == 0 ? "being set up" : $"{room.Players}/{room.Seats} players",
            RoomPhase.Loading => "loading",
            RoomPhase.Match => "match running",
            RoomPhase.Ended => "match over",
            _ => throw new ArgumentOutOfRangeException(nameof(room), room.Phase, "No text for this room phase."),
        };
        return $"{(room.HasPassword ? "[locked] " : string.Empty)}{room.Name} - {(mapName.Length == 0 ? "no map yet" : mapName)} - {state}";
    }

    public static string RoomInfo(ListedRoom room, string mapName, string ownBuild, int ownRate)
    {
        var lines = new List<string> { room.Name, mapName.Length == 0 ? "No map chosen yet" : mapName, $"{room.Members} connected, {room.Players} of {room.Seats} seats taken" };
        if (room.HasPassword) lines.Add("Needs a password");
        if (room.TickRate != 0 && room.TickRate != ownRate) lines.Add($"Runs at {room.TickRate} Hz - this game runs at {ownRate} Hz");
        if (room.SimulationId.Length > 0 && ownBuild.Length > 0 && room.SimulationId != ownBuild) lines.Add("Opened by another build of the game");
        return string.Join("\n", lines);
    }
}
