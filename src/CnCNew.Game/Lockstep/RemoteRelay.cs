using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CnCNew.Net;
using CnCNew.Net.Udp;

namespace CnCNew.Game.Lockstep;

/// <summary>A room on a server could not be reached, entered or started; the message is fit to show.</summary>
public sealed class RemoteRelayException(string message) : InvalidOperationException(message);

/// <summary>
/// The connection of a game to a room on a server somewhere else (docs/NETWORK-PROTOCOL.md section 18.6): a UDP socket, the transport on it and the protocol client -
/// what <see cref="LocalRelay"/> is for the embedded server. The steps before a match - enter the room, take a seat, wait for the host's start - block the caller for
/// at most their time limit and move the connection meanwhile; they are what a lobby screen will do one message at a time. Once the match runs, the game's lockstep
/// driver pumps the connection every frame (<see cref="Pump"/>) and the game flushes what the frame produced (<see cref="Flush"/>).
/// </summary>
public sealed class RemoteRelay : IDisposable
{
    private readonly UdpSocketChannel _socket;
    private readonly UdpClientEndpoint _endpoint;

    private RemoteRelay(UdpSocketChannel socket, UdpClientEndpoint endpoint, LockstepClient client, SystemNetClock clock, IPEndPoint server)
    {
        _socket = socket;
        _endpoint = endpoint;
        Client = client;
        Clock = clock;
        Server = server;
    }

    public LockstepClient Client { get; }
    public SystemNetClock Clock { get; }
    public IPEndPoint Server { get; }

    /// <summary>The fingerprint of the key the server proved to hold, and what this client made of it (docs/NETWORK-PROTOCOL.md section 20.2).</summary>
    public string ServerFingerprint => _endpoint.ServerFingerprint;
    public ServerTrust Trust { get; private set; } = ServerTrust.Known;
    private string _expected = string.Empty;
    public UdpLinkStatistics Statistics => _endpoint.Link.Statistics;

    /// <summary>One round: what arrived on the socket is taken in and handed to the protocol client, what is due is sent.</summary>
    public void Pump()
    {
        _endpoint.Pump();
        Client.Pump();
        _endpoint.Flush();
    }

    /// <summary>Sends what the game produced since the last pump - orders, hash reports - without waiting for the next frame.</summary>
    public void Flush() => _endpoint.Flush();

    private void PumpUntil(Func<bool> done, int timeoutMs, string what)
    {
        var until = Clock.Milliseconds + timeoutMs;
        while (true)
        {
            Pump();
            if (done()) return;
            if (Client.Rejection is { } rejection) throw new RemoteRelayException($"The server {Server} turned this client away ({rejection.Reason}): {rejection.Text}");
            if (_endpoint.State == UdpClientState.Failed && _endpoint.Link.CloseReason == UdpCloseReason.Untrusted)
                throw new RemoteRelayException($"The server at {Server} shows the key {ServerFingerprint}, not the key {_expected} this client knows for it. Either the server has a new key - then "
                                               + "its operator can say so, and the entry in the list of known servers is to be removed - or somebody stands between this client and the server.");
            if (_endpoint.State == UdpClientState.Failed && _endpoint.Link.CloseReason == UdpCloseReason.Refused) throw new RemoteRelayException($"The server at {Server} is full, or holds as many connections of this address as it allows.");
            if (_endpoint.State == UdpClientState.Failed) throw new RemoteRelayException($"No server answers at {Server}.");
            if (_endpoint.State == UdpClientState.Closed) throw new RemoteRelayException($"The server {Server} closed the connection ({_endpoint.Link.CloseReason}).");
            if (Clock.Milliseconds >= until) throw new RemoteRelayException($"The server {Server} did not {what} within {timeoutMs / 1000.0:F0} s.");
            _socket.WaitReadable(2_000);
        }
    }

    /// <summary>Connects to the server and enters the room; a room nobody is in yet is opened by this, and this client is its host.</summary>
    /// <param name="rejoinToken">The token of an earlier connection to a match that runs: asks for the seat held for it. 0 otherwise.</param>
    /// <param name="watch">Ask to watch a match that already runs (the server must admit latecomers).</param>
    /// <param name="known">The servers this client has met: a new one is remembered, one whose key changed is refused. Null: every server is taken as new.</param>
    /// <param name="serverKey">The fingerprint the operator published: any other key is refused, whatever the list says. Empty: the list decides.</param>
    /// <param name="roomPassword">The password of the room: set by whoever opens the room, answered by everybody else. Empty: none.</param>
    public static RemoteRelay Connect(IPEndPoint server, string definitionFingerprint, string name, string room, int timeoutMs = 10_000, ulong rejoinToken = 0, bool watch = false,
        KnownServers? known = null, string serverKey = "", string roomPassword = "")
    {
        var clock = new SystemNetClock();
        var socket = new UdpSocketChannel(new IPEndPoint(server.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
        RemoteRelay? relay = null;
        var trust = ServerTrust.New;
        var expected = serverKey.ToUpperInvariant();
        bool Judge(byte[] key)
        {
            var fingerprint = UdpCrypto.FingerprintOf(key);
            if (expected.Length > 0) trust = fingerprint == expected ? ServerTrust.Known : ServerTrust.Changed;
            else if (known is not null)
            {
                expected = known.FingerprintOf(server) ?? string.Empty;
                trust = known.Check(server, fingerprint);
            }
            return trust != ServerTrust.Changed;
        }
        var endpoint = new UdpClientEndpoint(socket, server, clock, BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)), trust: Judge);
        relay = new RemoteRelay(socket, endpoint, new LockstepClient(endpoint.Connection, name, definitionFingerprint, room: room, clock: clock, rejoinToken: rejoinToken, watch: watch,
            roomPassword: roomPassword), clock, server);
        try
        {
            // Somebody who joins a running match is not shown the room first: the next thing it gets is the start of the match, with the state.
            try
            {
                relay.PumpUntil(() => relay.Client.Phase is ClientPhase.InRoom or ClientPhase.Loading, timeoutMs, "admit this client");
            }
            finally
            {
                relay._expected = expected;
                relay.Trust = trust;
            }
        }
        catch (RemoteRelayException) when (trust == ServerTrust.Changed)
        {
            // Said again with the fingerprints, which were not known when the first message was written.
            var shown = endpoint.ServerFingerprint;
            relay.Dispose();
            throw new RemoteRelayException($"The server at {server} shows the key {shown}, not the key {expected} this client knows for it. Either the server has a new key - then its operator "
                                           + "can say so, and the entry in the list of known servers is to be removed - or somebody stands between this client and the server.");
        }
        catch
        {
            relay.Dispose();
            throw;
        }
        return relay;
    }

    /// <summary>Takes a seat of the room as its host set it (the seat's faction, team, colour and place stay the host's choice) and says "ready".</summary>
    public void TakeSeat(int slot, int timeoutMs = 10_000)
    {
        PumpUntil(() => Client.Room is { } room && room.Settings.Slots.Length > slot, timeoutMs, "show a room with that seat");
        var seat = Client.Room!.Settings.Slots[slot];
        Client.RequestSlot(slot, seat.Faction, seat.Team, seat.Color, seat.StartPosition);
        Client.SetReady(true);
        PumpUntil(() => Client.Room!.Members.Any(member => member.ConnectionId == Client.ConnectionId && member.Slot == slot && member.Ready), timeoutMs, "give this client the seat");
    }

    /// <summary>Waits for the match to be started (by the host of the room) - or, for somebody who joins a running match, for the state of a member.</summary>
    public void WaitForStart(int timeoutMs = 120_000) => PumpUntil(() => Client.Phase == ClientPhase.Loading && Client.Start is not null, timeoutMs, "start the match");

    /// <summary>The match is built here: reports its session hash and waits for the server's go.</summary>
    public void ReportLoaded(ulong sessionHash, int timeoutMs = 120_000)
    {
        Client.ReportLoaded(sessionHash);
        PumpUntil(() => Client.Phase is ClientPhase.InMatch or ClientPhase.Ended, timeoutMs, "begin the match");
        if (Client.Phase == ClientPhase.Ended) throw new RemoteRelayException("The server ended the match before it began here (the state built from what it sent did not match).");
    }

    /// <summary>Says goodbye and waits briefly for the close to be delivered, so the server sees a client that left and not a lost connection.</summary>
    public void Close(int timeoutMs = 1_500)
    {
        if (!_endpoint.Connection.IsOpen) return;
        _endpoint.Connection.Close();
        var until = Clock.Milliseconds + timeoutMs;
        while (!_endpoint.Link.IsFinished && Clock.Milliseconds < until)
        {
            _endpoint.Pump();
            _endpoint.Flush();
            _socket.WaitReadable(2_000);
        }
    }

    public void Dispose()
    {
        _endpoint.Dispose();
        _socket.Dispose();
    }
}
