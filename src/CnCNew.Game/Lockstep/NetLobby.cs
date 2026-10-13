using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CnCNew.Net;
using CnCNew.Net.Udp;

namespace CnCNew.Game.Lockstep;

/// <summary>Where the way from "an address" to "a running match" stands (docs/GAME-SHELL.md section 15).</summary>
public enum LobbyStage
{
    Idle,
    /// <summary>The handshake with the server runs.</summary>
    Connecting,
    /// <summary>A server this client has never met showed its key: a person decides (<see cref="INetLobby.AcceptServer"/>) before a word is said to it.</summary>
    TrustQuestion,
    /// <summary>Connected, no room entered: the list of rooms is asked for every few seconds.</summary>
    Browsing,
    /// <summary>The hello is on its way; a password challenge is answered meanwhile.</summary>
    Entering,
    InRoom,
    /// <summary>The host started the match: the description is here, the game builds it (<see cref="NetLobby.TakeRelay"/>).</summary>
    Starting,
    /// <summary>Something ended the way; <see cref="INetLobby.Failure"/> says what, in a form a screen can put into words.</summary>
    Failed,
}

/// <summary>Why the way ended. Every kind - and every reject and drop reason behind it - has a text of its own in the shell; none falls back to another.</summary>
public enum LobbyFailureKind
{
    /// <summary>Nobody answered at the address within the connect time.</summary>
    NoAnswer,
    /// <summary>The server holds as many connections as it may.</summary>
    ServerFull,
    /// <summary>The server holds as many connections from this address as it may.</summary>
    AddressFull,
    /// <summary>The server shows another key than the one this client knows or was told to expect.</summary>
    KeyChanged,
    /// <summary>The server or the room said no: <see cref="LobbyFailure.Reject"/> is the reason.</summary>
    Rejected,
    /// <summary>The connection ended while in a room or on the way into one: <see cref="LobbyFailure.Close"/> is how.</summary>
    ConnectionClosed,
}

/// <param name="Shown">The fingerprint the server showed (<see cref="LobbyFailureKind.KeyChanged"/>).</param>
/// <param name="Expected">The fingerprint this client knew or was told to expect.</param>
/// <param name="Detail">The server's own words where it gave any (they name versions and numbers); a screen shows them under its own text.</param>
public sealed record LobbyFailure(LobbyFailureKind Kind, RejectReason Reject = 0, UdpCloseReason Close = UdpCloseReason.None, string Detail = "", string Shown = "", string Expected = "");

/// <summary>
/// What a lobby screen needs of the network: state to show and requests to make, one message at a time, nothing that blocks. <see cref="NetLobby"/> is the real one;
/// a test of the screens puts a scripted one in its place.
/// </summary>
public interface INetLobby
{
    LobbyStage Stage { get; }
    IPEndPoint? Server { get; }
    /// <summary>The fingerprint of the key the server proved to hold; empty before it did.</summary>
    string ServerFingerprint { get; }
    LobbyFailure? Failure { get; }
    RoomListMessage? Rooms { get; }
    /// <summary>The server lists no rooms: a room is entered by its name.</summary>
    bool ListingRefused { get; }
    string RoomName { get; }
    RoomStateMessage? Room { get; }
    uint ConnectionId { get; }
    bool IsHost { get; }
    IReadOnlyList<ChatMessage> Chat { get; }
    IReadOnlyDictionary<uint, byte> LoadProgress { get; }
    /// <summary>The last request the room refused (somebody else's seat, a start that is not the host's, settings the server does not take).</summary>
    RejectMessage? LastRefusal { get; }

    void Connect(IPEndPoint server);
    void AcceptServer();
    void RefreshRooms();
    void Enter(string room, string password = "", bool watch = false);
    void Configure(RoomSettings settings);
    void RequestSlot(int slot, string faction, int team, int color, int startPosition);
    void LeaveSlot();
    void SetReady(bool ready);
    void RequestStart();
    void SendChat(string text);
    void Kick(uint connectionId);
    /// <summary>Leaves the room (or gives up the way into one) and stands in front of the server's rooms again.</summary>
    void LeaveRoom();
    void Disconnect();
    void Pump();
}

/// <param name="Known">The servers this client has met; null keeps the list for the life of the lobby.</param>
/// <param name="ServerKey">A fingerprint the person was given: that server and no other, and no question.</param>
public sealed record NetLobbyOptions(string PlayerName, string DefinitionFingerprint, KnownServers? Known = null, string ServerKey = "", UdpIdentity? PlayerKey = null, BuildIdentity? Identity = null)
{
    /// <summary>Every how many milliseconds the list of rooms is asked for while nobody has chosen one (the server answers once a second at most).</summary>
    public int ListEveryMs { get; init; } = 2_500;
    /// <summary>
    /// What this client sends a room per second at most while in the lobby - seat and setting changes, ready, chat. A server drops a member above 500 messages a
    /// second (docs/DEDICATED-SERVER.md section 9.2); a lobby has no reason to come near that, whatever a person does with its boxes.
    /// </summary>
    public int RequestsPerSecond { get; init; } = 20;
    public int ConnectTimeoutMs { get; init; } = 5_000;
}

/// <summary>
/// The client's way to a room on a server (docs/GAME-SHELL.md section 15): connect, judge the server's key, ask for its rooms, enter one, sit, chat, start - and hand
/// the connection to the match when the host starts it. Everything is a request and a state; <see cref="Pump"/> moves it once a frame and never waits. It is the
/// lobby half of what <see cref="RemoteRelay"/> does in one blocking call for a client that is told its room on the command line.
/// </summary>
public sealed class NetLobby : INetLobby, IDisposable
{
    private readonly NetLobbyOptions _options;
    private readonly KnownServers _known;
    private readonly SystemNetClock _clock = new();
    private UdpSocketChannel? _socket;
    private UdpClientEndpoint? _endpoint;
    private RoomBrowser? _browser;
    private LockstepClient? _client;
    private string _accepted = string.Empty;
    private string _expected = string.Empty;
    private bool _keyChanged;
    private bool _question;
    private long _nextListMs;
    private string _password = string.Empty;
    private bool _watch;
    private (string Room, string Password, bool Watch)? _enterWhenConnected;
    // Requests wait here and go out at the lobby's own pace: the latest of each kind is what counts, chat lines in their order.
    private RoomSettings? _pendingSettings;
    private (int Slot, string Faction, int Team, int Color, int Start)? _pendingSlot;
    private bool _pendingLeaveSlot;
    private bool? _pendingReady;
    private bool _pendingStart;
    private readonly Queue<string> _pendingChat = new();
    private readonly Queue<uint> _pendingKicks = new();
    private long _budgetMilli;
    private long _budgetAtMs;

    public NetLobby(NetLobbyOptions options)
    {
        _options = options;
        _known = options.Known ?? new KnownServers();
        _budgetAtMs = _clock.Milliseconds;
        _budgetMilli = options.RequestsPerSecond * 1000L;
    }

    public LobbyStage Stage { get; private set; }
    public IPEndPoint? Server { get; private set; }
    public string ServerFingerprint => _endpoint?.ServerFingerprint is { Length: > 0 } shown ? shown : _shown;
    private string _shown = string.Empty;
    public LobbyFailure? Failure { get; private set; }
    public RoomListMessage? Rooms => _browser?.List ?? _lastRooms;
    private RoomListMessage? _lastRooms;
    public bool ListingRefused { get; private set; }
    public string RoomName { get; private set; } = string.Empty;
    public RoomStateMessage? Room => _client?.Room;
    public uint ConnectionId => _client?.ConnectionId ?? 0;
    public bool IsHost => _client?.IsHost ?? false;
    public IReadOnlyList<ChatMessage> Chat => _client?.Chat ?? [];
    public IReadOnlyDictionary<uint, byte> LoadProgress => _client?.LoadProgress ?? ImmutableDictionary<uint, byte>.Empty;
    public RejectMessage? LastRefusal => _client?.LastRefusal;
    /// <summary>The protocol client while in a room; null before.</summary>
    public LockstepClient? Client => _client;

    /// <summary>What the lobby did not send because a person asked for more than the lobby's pace: chat lines beyond a short queue. Counted, never silent.</summary>
    public long ChatDropped { get; private set; }
    public long RequestsSent { get; private set; }
    /// <summary>How often the connection to the server was made again while browsing (a server turns a connection away that browses for minutes).</summary>
    public int Reconnects { get; private set; }
    public UdpLinkStatistics? Statistics => _endpoint?.Link.Statistics;

    public void Connect(IPEndPoint server)
    {
        Server = server;
        _accepted = string.Empty;
        _lastRooms = null;
        ListingRefused = false;
        Open();
    }

    private void Open()
    {
        Close();
        Failure = null;
        _keyChanged = false;
        _question = false;
        var server = Server ?? throw new InvalidOperationException("No server was named.");
        var pinned = _options.ServerKey.ToUpperInvariant();
        _socket = new UdpSocketChannel(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
        _endpoint = new UdpClientEndpoint(_socket, server, _clock, BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)), new UdpOptions { ConnectTimeoutMs = _options.ConnectTimeoutMs }, key =>
        {
            // The handshake asks before the connection is used for anything. A key that is not to be trusted yet ends the attempt without a word to the server.
            var shown = UdpCrypto.FingerprintOf(key);
            _shown = shown;
            if (pinned.Length > 0)
            {
                _expected = pinned;
                return !(_keyChanged = shown != pinned);
            }
            if (shown == _accepted) return true;
            switch (_known.Peek(server, shown))
            {
                case ServerTrust.Known: return true;
                case ServerTrust.Changed:
                    _expected = _known.FingerprintOf(server) ?? string.Empty;
                    _keyChanged = true;
                    return false;
                default:
                    _question = true;
                    return false;
            }
        });
        _browser = null;
        _client = null;
        Stage = LobbyStage.Connecting;
    }

    public void AcceptServer()
    {
        if (Stage != LobbyStage.TrustQuestion || Server is null) return;
        _accepted = _shown;
        _known.Remember(Server, _shown);
        Open();
    }

    public void RefreshRooms()
    {
        if (Stage != LobbyStage.Browsing || _browser is null || ListingRefused) return;
        _browser.Request();
        _nextListMs = _clock.Milliseconds + _options.ListEveryMs;
    }

    public void Enter(string room, string password = "", bool watch = false)
    {
        if (Stage is LobbyStage.Connecting)
        {
            _enterWhenConnected = (room, password, watch);
            return;
        }
        if (Stage != LobbyStage.Browsing) return;
        if (_endpoint is null || !_endpoint.Connection.IsOpen || _browser?.Rejection is not null)
        {
            // The server sent this connection home meanwhile (it browsed for long, or the server lists nothing) - its close may still be on the way behind the
            // refusal: a fresh one, and the hello on it.
            _enterWhenConnected = (room, password, watch);
            Reconnects++;
            Open();
            return;
        }
        _lastRooms = _browser?.List ?? _lastRooms;
        _browser = null;
        RoomName = room;
        _password = password;
        _watch = watch;
        _client = new LockstepClient(_endpoint.Connection, _options.PlayerName, _options.DefinitionFingerprint, _options.Identity, room, _clock, watch: watch, roomPassword: password,
            playerKey: _options.PlayerKey);
        Stage = LobbyStage.Entering;
    }

    public void Configure(RoomSettings settings) => _pendingSettings = settings;

    public void RequestSlot(int slot, string faction, int team, int color, int startPosition)
    {
        _pendingSlot = (slot, faction, team, color, startPosition);
        _pendingLeaveSlot = false;
    }

    public void LeaveSlot()
    {
        _pendingSlot = null;
        _pendingLeaveSlot = true;
    }

    public void SetReady(bool ready) => _pendingReady = ready;
    public void RequestStart() => _pendingStart = true;

    public void SendChat(string text)
    {
        if (text.Length == 0) return;
        if (_pendingChat.Count >= 8)
        {
            ChatDropped++;
            return;
        }
        // The protocol bounds a line in bytes; a longer one is cut at a character, not refused whole.
        while (System.Text.Encoding.UTF8.GetByteCount(text) > Protocol.MaxChatBytes) text = text[..^1];
        _pendingChat.Enqueue(text);
    }

    public void Kick(uint connectionId) => _pendingKicks.Enqueue(connectionId);

    public void LeaveRoom()
    {
        if (Server is null) return;
        if (_client is { IsOpen: true } client && Stage is LobbyStage.InRoom or LobbyStage.Entering)
        {
            client.Leave();
            _endpoint?.Flush();
            _endpoint?.Connection.Close();
            _endpoint?.Flush();
        }
        ClearPending();
        Open();
    }

    public void Disconnect()
    {
        if (_client is { IsOpen: true } client)
        {
            client.Leave();
            _endpoint?.Flush();
        }
        if (_endpoint is { Connection.IsOpen: true } endpoint)
        {
            endpoint.Connection.Close();
            endpoint.Flush();
        }
        Close();
        ClearPending();
        Stage = LobbyStage.Idle;
    }

    private void ClearPending()
    {
        _pendingSettings = null;
        _pendingSlot = null;
        _pendingLeaveSlot = false;
        _pendingReady = null;
        _pendingStart = false;
        _pendingChat.Clear();
        _pendingKicks.Clear();
        _enterWhenConnected = null;
    }

    private void Close()
    {
        _endpoint?.Dispose();
        _socket?.Dispose();
        _endpoint = null;
        _socket = null;
    }

    private void Fail(LobbyFailure failure)
    {
        Failure = failure;
        Stage = LobbyStage.Failed;
        ClearPending();
    }

    public void Pump()
    {
        if (_endpoint is null || Stage is LobbyStage.Idle or LobbyStage.Failed or LobbyStage.TrustQuestion or LobbyStage.Starting) return;
        _endpoint.Pump();
        var now = _clock.Milliseconds;
        switch (Stage)
        {
            case LobbyStage.Connecting:
                if (_endpoint.State == UdpClientState.Connected)
                {
                    Stage = LobbyStage.Browsing;
                    _browser = new RoomBrowser(_endpoint.Connection);
                    _nextListMs = now;
                    if (_enterWhenConnected is { } enter)
                    {
                        _enterWhenConnected = null;
                        Enter(enter.Room, enter.Password, enter.Watch);
                    }
                }
                else if (_endpoint.State == UdpClientState.Failed)
                {
                    if (_question)
                    {
                        Close();
                        Stage = LobbyStage.TrustQuestion;
                    }
                    else if (_keyChanged) Fail(new LobbyFailure(LobbyFailureKind.KeyChanged, Shown: _shown, Expected: _expected));
                    else if (_endpoint.Link.CloseReason == UdpCloseReason.Refused) Fail(new LobbyFailure(_endpoint.RefuseReason == UdpFormat.RefuseAddressFull ? LobbyFailureKind.AddressFull : LobbyFailureKind.ServerFull));
                    else Fail(new LobbyFailure(LobbyFailureKind.NoAnswer));
                }
                break;
            case LobbyStage.Browsing:
                _browser!.Pump();
                if (_browser.Rejection is { } refused)
                {
                    // A server that lists nothing, or one of another protocol version. The first is no failure: rooms are entered by name there.
                    if (refused.Reason == RejectReason.ProtocolVersion) Fail(new LobbyFailure(LobbyFailureKind.Rejected, refused.Reason, Detail: refused.Text));
                    else ListingRefused = _browser.Lists == 0;
                }
                else if (_endpoint.Connection.IsOpen && now >= _nextListMs) RefreshRooms();
                break;
            case LobbyStage.Entering:
            case LobbyStage.InRoom:
                _client!.Pump();
                if (_client.Rejection is { } rejection) Fail(new LobbyFailure(LobbyFailureKind.Rejected, rejection.Reason, Detail: rejection.Text));
                else if (!_client.IsOpen)
                {
                    // The room closed the connection: it said why in its last message (a kick, a ban), or the line is gone.
                    var last = _client.LastRefusal;
                    Fail(last is { Reason: RejectReason.Kicked or RejectReason.Banned } ? new LobbyFailure(LobbyFailureKind.Rejected, last.Reason, Detail: last.Text)
                        : new LobbyFailure(LobbyFailureKind.ConnectionClosed, Close: _endpoint.Link.CloseReason));
                }
                else if (_client.Phase == ClientPhase.InRoom && Stage == LobbyStage.Entering) Stage = LobbyStage.InRoom;
                else if (_client.Phase == ClientPhase.Loading && _client.Start is not null) Stage = LobbyStage.Starting;
                if (Stage == LobbyStage.InRoom) SendPending(now);
                break;
        }
        _endpoint?.Flush();
    }

    private void SendPending(long now)
    {
        var full = _options.RequestsPerSecond * 1000L;
        _budgetMilli = Math.Min(full, _budgetMilli + Math.Max(0, now - _budgetAtMs) * _options.RequestsPerSecond);
        _budgetAtMs = now;
        bool Take()
        {
            if (_budgetMilli < 1000) return false;
            _budgetMilli -= 1000;
            RequestsSent++;
            return true;
        }
        var client = _client!;
        if (_pendingSettings is { } settings && Take())
        {
            client.Configure(settings);
            _pendingSettings = null;
        }
        if (_pendingLeaveSlot && Take())
        {
            client.LeaveSlot();
            _pendingLeaveSlot = false;
        }
        if (_pendingSlot is { } seat && Take())
        {
            client.RequestSlot(seat.Slot, seat.Faction, seat.Team, seat.Color, seat.Start);
            _pendingSlot = null;
        }
        if (_pendingReady is { } ready && Take())
        {
            client.SetReady(ready);
            _pendingReady = null;
        }
        while (_pendingKicks.Count > 0 && Take()) client.Kick(_pendingKicks.Dequeue());
        while (_pendingChat.Count > 0 && Take()) client.SendChat(_pendingChat.Dequeue());
        // The start goes last: whatever was set in the same frame is on its way before it.
        if (_pendingStart && _pendingSettings is null && _pendingSlot is null && _pendingReady is null && Take())
        {
            client.RequestStart();
            _pendingStart = false;
        }
    }

    /// <summary>
    /// The host started the match: the connection with its protocol client goes to the game, which builds the match the description names
    /// (<c>LiveMatch.Join</c>). The lobby keeps the server's address and the room's name for the way back.
    /// </summary>
    public RemoteRelay TakeRelay()
    {
        if (Stage != LobbyStage.Starting || _socket is null || _endpoint is null || _client is null) throw new InvalidOperationException("No match has been started.");
        var relay = RemoteRelay.Adopt(_socket, _endpoint, _client, _clock, Server!);
        _socket = null;
        _endpoint = null;
        _client = null;
        Stage = LobbyStage.Idle;
        return relay;
    }

    /// <summary>After a match: the same server, the same room by name (whoever is back first opens it again and is its host).</summary>
    public void ReturnToRoom()
    {
        if (Server is null || RoomName.Length == 0 && Stage != LobbyStage.Idle) return;
        _enterWhenConnected = (RoomName, _password, _watch);
        Open();
    }

    public void Dispose() => Close();
}
