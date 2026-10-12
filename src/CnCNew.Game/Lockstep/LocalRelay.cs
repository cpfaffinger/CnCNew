using CnCNew.Net;
using CnCNew.Server;

namespace CnCNew.Game.Lockstep;

/// <summary>A local room could not be opened or started; the message is the relay's own refusal.</summary>
public sealed class LocalRelayException(string message) : InvalidOperationException(message);

/// <summary>
/// The embedded server of a local game: a <see cref="RelayServer"/>, a loopback network without delay and the one client, all in this process (docs/NETWORK-PROTOCOL.md
/// section 7). The local skirmish goes through the same handshake, room, match start and tick bundles as a network match - there is one match path. With one client the room
/// is client-paced, so nothing here needs a clock that moves: the clock stands, and a tick is closed when the client asks.
/// </summary>
public sealed class LocalRelay
{
    private const int HandshakeRounds = 64;

    private LocalRelay(RelayServer server, LockstepClient client, LoopbackNetwork network)
    {
        Server = server;
        Client = client;
        Network = network;
    }

    public RelayServer Server { get; }
    public LockstepClient Client { get; }
    public LoopbackNetwork Network { get; }

    /// <summary>One round of the network: the server takes in what the client sent and answers, the client takes in the answers.</summary>
    public void Pump()
    {
        Server.Pump();
        Client.Pump();
    }

    private void PumpUntil(Func<bool> done, string what)
    {
        for (var round = 0; round < HandshakeRounds; round++)
        {
            Pump();
            if (done()) return;
            if (Client.Rejection is { } rejection) throw new LocalRelayException($"The local server rejected the client ({rejection.Reason}): {rejection.Text}");
            if (Client.LastRefusal is { } refusal) throw new LocalRelayException($"The local server refused to {what} ({refusal.Reason}): {refusal.Text}");
        }
        throw new LocalRelayException($"The local server did not {what}{(Client.Discarded > 0 ? $"; the client discarded: {Client.LastDiscard}" : Server.Discarded > 0 ? $"; the server discarded: {Server.LastDiscard}" : string.Empty)}.");
    }

    /// <summary>Opens the server and connects the one client; the server admits development builds and takes the seeds the host states (a local game names its own).</summary>
    public static LocalRelay Open(string definitionFingerprint, string name = "Player", ushort hashInterval = 1)
    {
        var clock = new ManualNetClock();
        var network = new LoopbackNetwork(clock);
        var server = new RelayServer(clock, new RelayOptions(AllowModifiedBuilds: true, HostMayChooseSeeds: true, HashInterval: hashInterval)
        {
            // The server of one player on its own machine: nobody to protect it from, and a saved match of any length may be continued.
            MessagesPerSecond = 0, MaxStateBytes = 0, MaxMatchSeconds = 0, MaxRecordingBytes = 0, LoadLimitMs = 0, HashReportLimitMs = 0, RoomIdleLimitMs = 0, HelloLimitMs = 0,
        });
        var (clientEnd, serverEnd) = network.Connect();
        server.Attach(serverEnd);
        var relay = new LocalRelay(server, new LockstepClient(clientEnd, name, definitionFingerprint), network);
        relay.PumpUntil(() => relay.Client.Phase == ClientPhase.InRoom, "admit the client");
        return relay;
    }

    /// <summary>
    /// Sets the room, takes the seat (<paramref name="slot"/>; -1: the viewer only watches) and starts the match, from tick 0 or from a saved state. Returns when the match
    /// description has arrived (<see cref="LockstepClient.Description"/>).
    /// </summary>
    public void StartMatch(RoomSettings settings, int slot, ResumeState? resume = null)
    {
        Client.Configure(settings);
        if (slot >= 0)
        {
            var seat = settings.Slots[slot];
            Client.RequestSlot(slot, seat.Faction, seat.Team, seat.Color, seat.StartPosition);
        }
        PumpUntil(() => Client.Room is { } room && room.Settings.Slots.Length == settings.Slots.Length && (slot < 0 || room.Members.Any(member => member.ConnectionId == Client.ConnectionId && member.Slot == slot)),
            "set the room");
        Client.RequestStart(resume);
        PumpUntil(() => Client.Phase == ClientPhase.Loading, "start the match");
    }

    /// <summary>The match is built: reports its session hash at the start tick and waits for the server's go.</summary>
    public void ReportLoaded(ulong sessionHash)
    {
        Client.ReportLoaded(sessionHash);
        PumpUntil(() => Client.Phase is ClientPhase.InMatch or ClientPhase.Ended, "begin the match");
        if (Client.Phase == ClientPhase.Ended) throw new LocalRelayException("The local server ended the match before it began (the state after loading did not match the saved one).");
    }
}
