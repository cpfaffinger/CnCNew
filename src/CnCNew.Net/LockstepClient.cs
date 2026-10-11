using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;

namespace CnCNew.Net;

public enum ClientPhase
{
    /// <summary>The hello is sent, no answer yet.</summary>
    Connecting,
    Rejected,
    InRoom,
    /// <summary>The match description has arrived; this client builds its match.</summary>
    Loading,
    InMatch,
    Ended,
}

/// <summary>
/// The client's end of the protocol: handshake, room, and in a match the collecting of tick bundles. It owns no simulation - whoever drives a match asks it whether the
/// bundle of a tick is there, takes it, and tells it the hashes (the lockstep driver of the game layer). Given a clock, it also asks the server for its tick clock at an
/// interval and keeps the estimate (<see cref="Sync"/>, docs/NETWORK-PROTOCOL.md section 13); without one (the local game, whose room is client-paced) it asks nothing. Everything it receives passes
/// <see cref="MessageCodec.TryDecode"/>; what does not decode, or does not fit the phase, is counted in <see cref="Discarded"/> and dropped.
/// </summary>
public sealed class LockstepClient
{
    private readonly IConnection _connection;
    private readonly SortedDictionary<long, TickBundleMessage> _bundles = [];
    private readonly List<ChatMessage> _chat = [];
    private readonly List<PlayerDroppedMessage> _dropped = [];

    private readonly SortedDictionary<uint, LagStateMessage> _lag = [];
    private readonly PlaybackOptions _playback;
    private long _lastPingMicros = long.MinValue;

    /// <param name="room">The room of the server this client wants to be in; a server with one room ignores it.</param>
    /// <param name="clock">The local clock for clock synchronisation; null: no pings, no estimate.</param>
    public LockstepClient(IConnection connection, string name, string definitionFingerprint, BuildIdentity? identity = null, string room = "", INetClock? clock = null, PlaybackOptions? playback = null)
    {
        _connection = connection;
        Clock = clock;
        _playback = playback ?? PlaybackOptions.Default;
        Sync = new ClockSync(_playback.SyncSamples);
        Identity = identity ?? BuildIdentity.Current;
        Send(new HelloMessage(Protocol.Version, Identity.Id, CommandTypes.Version, ReplayFormat.CurrentVersion, WorldSnapshot.Version, MatchDescription.EncodingVersion,
            definitionFingerprint.ToUpperInvariant(), name, room));
    }

    public INetClock? Clock { get; }
    public PlaybackOptions Playback => _playback;

    /// <summary>The estimate of the server's tick clock; without a clock it never has one.</summary>
    public ClockSync Sync { get; }

    /// <summary>The last tick this client's simulation has completed; told to the server with every ping. One less than the start tick before the first.</summary>
    public long CompletedTick { get; private set; } = -1;
    public void ReportCompleted(long tick) => CompletedTick = Math.Max(CompletedTick, tick);

    /// <summary>How long ago this client last looked for messages, as of the running <see cref="Pump"/>, in microseconds (0 without a clock): above a frame's length it stood still.</summary>
    public long SinceLastPumpMicros { get; private set; }
    private long _lastPumpMicros = long.MinValue;

    /// <summary>Called for every bundle when it arrives, with its tick and the local clock in microseconds (0 without a clock).</summary>
    public Action<long, long>? BundleArrived { get; set; }

    /// <summary>Who is behind, as the server last said: connection to lag; a member that caught up is removed.</summary>
    public IReadOnlyDictionary<uint, LagStateMessage> Lag => _lag;
    public bool IsOpen => _connection.IsOpen;

    public BuildIdentity Identity { get; }
    public ClientPhase Phase { get; private set; } = ClientPhase.Connecting;
    public uint ConnectionId { get; private set; }
    public bool IsHost { get; private set; }
    public RejectMessage? Rejection { get; private set; }

    /// <summary>The room as the server last said it is; null before the first state arrived.</summary>
    public RoomStateMessage? Room { get; private set; }

    /// <summary>A request the server turned down while the connection stayed (not the host, an invalid slot, ...); the last one.</summary>
    public RejectMessage? LastRefusal { get; private set; }

    public MatchStartMessage? Start { get; private set; }

    /// <summary>The description of the match, decoded from <see cref="Start"/>.</summary>
    public MatchDescription? Description { get; private set; }

    /// <summary>The player number of this client in the match; 0 when it watches.</summary>
    public int Player => Start?.Player ?? 0;
    public Pacing Pacing => Start?.Pacing ?? Pacing.Server;

    /// <summary>The first tick of the match (0, or the tick of the resumed state).</summary>
    public long StartTick => Start?.Resume?.Tick ?? 0;

    public bool Paused { get; private set; }
    public int SpeedPercent { get; private set; } = 100;
    public DesyncMessage? Desync { get; private set; }
    public MatchEndMessage? End { get; private set; }
    public IReadOnlyList<ChatMessage> Chat => _chat;
    public IReadOnlyList<PlayerDroppedMessage> Dropped => _dropped;
    public IReadOnlyDictionary<uint, byte> LoadProgress => _loadProgress;
    private readonly SortedDictionary<uint, byte> _loadProgress = [];

    /// <summary>Messages that were dropped: unknown type, over their limit, malformed, or not fitting the phase. By reason, for a report.</summary>
    public long Discarded { get; private set; }
    public string LastDiscard { get; private set; } = string.Empty;

    /// <summary>The newest tick a bundle has arrived for; one less than the start tick before the first.</summary>
    public long NewestBundleTick { get; private set; } = -1;
    public long BundlesReceived { get; private set; }
    public long IntentsSent { get; private set; }

    private void Send(NetMessage message) => _connection.Send(MessageCodec.Encode(message));

    /// <summary>
    /// What a match must not wait for behind a lost packet goes on the urgent channel: orders, hash reports, pings, the report of the end (docs/NETWORK-PROTOCOL.md
    /// section 12.6). They keep their order among themselves; nothing depends on their order against the room's messages.
    /// </summary>
    private void SendUrgent(NetMessage message) => _connection.SendUrgent(MessageCodec.Encode(message));

    private void Discard(string reason)
    {
        Discarded++;
        LastDiscard = reason;
    }

    /// <summary>Takes in everything that has arrived.</summary>
    public void Pump()
    {
        if (Clock is not null)
        {
            var at = Clock.Microseconds;
            SinceLastPumpMicros = _lastPumpMicros == long.MinValue ? 0 : at - _lastPumpMicros;
            _lastPumpMicros = at;
        }
        while (_connection.TryReceive(out var bytes))
        {
            if (!MessageCodec.TryDecode(bytes, out var message, out var error))
            {
                Discard(error);
                continue;
            }
            Handle(message!);
        }
        if (Clock is null || Phase is ClientPhase.Connecting or ClientPhase.Rejected or ClientPhase.Ended) return;
        // Faster while there is no estimate: the first ticks of a match should not have to wait for it.
        var now = Clock.Microseconds;
        var interval = (Sync.HasEstimate || Phase != ClientPhase.InMatch ? _playback.PingIntervalMs : Math.Max(1, _playback.PingIntervalMs / 4)) * 1000L;
        if (_lastPingMicros != long.MinValue && now - _lastPingMicros < interval) return;
        _lastPingMicros = now;
        SendUrgent(new PingMessage(now, CompletedTick));
    }

    private void Handle(NetMessage message)
    {
        switch (message)
        {
            case WelcomeMessage welcome when Phase == ClientPhase.Connecting:
                ConnectionId = welcome.ConnectionId;
                IsHost = welcome.IsHost;
                Phase = ClientPhase.InRoom;
                break;
            case RejectMessage reject when Phase == ClientPhase.Connecting:
                Rejection = reject;
                Phase = ClientPhase.Rejected;
                break;
            case RejectMessage refusal:
                LastRefusal = refusal;
                break;
            case RoomStateMessage room:
                Room = room;
                IsHost = room.Members.Any(member => member.ConnectionId == ConnectionId && member.IsHost);
                break;
            case MatchStartMessage start when Phase == ClientPhase.InRoom:
                try
                {
                    Description = MatchDescription.Decode(start.Description);
                }
                catch (FormatException exception)
                {
                    Discard($"a match start whose description does not decode: {exception.Message}");
                    break;
                }
                Start = start;
                NewestBundleTick = StartTick - 1;
                CompletedTick = StartTick - 1;
                Phase = ClientPhase.Loading;
                break;
            case LoadProgressStateMessage progress:
                _loadProgress[progress.ConnectionId] = progress.Percent;
                break;
            case AllLoadedMessage when Phase == ClientPhase.Loading:
                Phase = ClientPhase.InMatch;
                break;
            case ChatMessage chat:
                _chat.Add(chat);
                break;
            case TickBundleMessage bundle when Phase is ClientPhase.InMatch or ClientPhase.Loading:
                // Bundles arrive in order on the connection; one that does not continue the sequence is a violation, not something to repair.
                if (bundle.Tick != NewestBundleTick + 1)
                {
                    Discard($"a bundle for tick {bundle.Tick} after tick {NewestBundleTick}");
                    break;
                }
                _bundles[bundle.Tick] = bundle;
                NewestBundleTick = bundle.Tick;
                BundlesReceived++;
                BundleArrived?.Invoke(bundle.Tick, Clock?.Microseconds ?? 0);
                break;
            // The two channels are not ordered against each other: a bundle that was on its way when the end arrived is not a violation.
            case TickBundleMessage when Phase == ClientPhase.Ended:
                break;
            case PongMessage pong:
                if (Clock is not null) Sync.Take(pong.ClientMicros, Clock.Microseconds, pong.GameMicros, pong.SpeedPercent);
                break;
            case LagStateMessage lag:
                if (lag.LagMs == 0 && !lag.Throttling) _lag.Remove(lag.ConnectionId);
                else _lag[lag.ConnectionId] = lag;
                break;
            case PaceStateMessage pace:
                Paused = pace.Paused;
                SpeedPercent = pace.SpeedPercent;
                break;
            case PlayerDroppedMessage dropped:
                _dropped.Add(dropped);
                _lag.Remove(dropped.ConnectionId);
                break;
            case DesyncMessage desync:
                Desync ??= desync;
                break;
            case MatchEndMessage end:
                End = end;
                Phase = ClientPhase.Ended;
                break;
            default:
                Discard($"a {message.Type} message in phase {Phase}");
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------- room

    public void Configure(RoomSettings settings) => Send(new RoomConfigMessage(settings));
    public void RequestSlot(int slot, string faction = "", int team = 0, int color = -1, int startPosition = -1) => Send(new SlotRequestMessage(checked((byte)slot), faction, team, color, startPosition));
    public void LeaveSlot() => Send(new SlotRequestMessage(SlotRequestMessage.Leave, string.Empty, 0, -1, -1));
    public void SetReady(bool ready) => Send(new ReadyMessage(ready));
    public void RequestStart(ResumeState? resume = null) => Send(new StartRequestMessage(resume));
    public void SendChat(string text) => Send(new ChatRequestMessage(text));
    public void ReportLoadProgress(int percent) => Send(new LoadProgressMessage(checked((byte)Math.Clamp(percent, 0, 100))));

    /// <summary>The match is built; <paramref name="sessionHash"/> is its session hash at the start tick.</summary>
    public void ReportLoaded(ulong sessionHash) => Send(new LoadedMessage(sessionHash));

    // ------------------------------------------------------------------------------------------------------------ match

    /// <summary>Sends a command intent (the bytes of <see cref="CommandWire.EncodeIntent"/>) under the client's own receipt number.</summary>
    public void SendIntent(ulong receipt, byte[] command)
    {
        SendUrgent(new IntentMessage(receipt, command));
        IntentsSent++;
    }

    /// <summary>Client-paced rooms: asks the server to close <paramref name="tick"/>.</summary>
    public void RequestTick(long tick) => SendUrgent(new TickRequestMessage(tick));

    public bool HasBundle(long tick) => _bundles.ContainsKey(tick);

    /// <summary>Removes and returns the bundle of <paramref name="tick"/>; it must be there.</summary>
    public TickBundleMessage TakeBundle(long tick)
        => _bundles.Remove(tick, out var bundle) ? bundle : throw new InvalidOperationException($"The bundle of tick {tick} has not arrived.");

    public void ReportHash(long tick, ulong sessionHash) => SendUrgent(new HashReportMessage(tick, sessionHash));

    /// <summary>Leaves the match by choice: the server puts the leave of this client's player into the next bundle (a living ally inherits) and closes the connection.</summary>
    public void Leave() => Send(new LeaveMessage());
    public void RequestPace(bool paused, int speedPercent) => Send(new PaceRequestMessage(paused, checked((ushort)Math.Clamp(speedPercent, Protocol.MinSpeedPercent, Protocol.MaxSpeedPercent))));
    public void ReportEnd(long tick, int winner) => SendUrgent(new EndReportMessage(tick, winner));

    public void Close() => _connection.Close();
}
