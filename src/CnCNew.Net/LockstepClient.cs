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
/// The client's end of the protocol: handshake, room, and in a match the collecting of tick bundles. It owns no simulation and no clock - whoever drives a match asks it
/// whether the bundle of a tick is there, takes it, and tells it the hashes (the lockstep driver of the game layer). Everything it receives passes
/// <see cref="MessageCodec.TryDecode"/>; what does not decode, or does not fit the phase, is counted in <see cref="Discarded"/> and dropped.
/// </summary>
public sealed class LockstepClient
{
    private readonly IConnection _connection;
    private readonly SortedDictionary<long, TickBundleMessage> _bundles = [];
    private readonly List<ChatMessage> _chat = [];
    private readonly List<PlayerDroppedMessage> _dropped = [];

    public LockstepClient(IConnection connection, string name, string definitionFingerprint, BuildIdentity? identity = null)
    {
        _connection = connection;
        Identity = identity ?? BuildIdentity.Current;
        Send(new HelloMessage(Protocol.Version, Identity.Id, CommandTypes.Version, ReplayFormat.CurrentVersion, WorldSnapshot.Version, MatchDescription.EncodingVersion,
            definitionFingerprint.ToUpperInvariant(), name));
    }

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

    private void Discard(string reason)
    {
        Discarded++;
        LastDiscard = reason;
    }

    /// <summary>Takes in everything that has arrived.</summary>
    public void Pump()
    {
        while (_connection.TryReceive(out var bytes))
        {
            if (!MessageCodec.TryDecode(bytes, out var message, out var error))
            {
                Discard(error);
                continue;
            }
            Handle(message!);
        }
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
                break;
            case PaceStateMessage pace:
                Paused = pace.Paused;
                SpeedPercent = pace.SpeedPercent;
                break;
            case PlayerDroppedMessage dropped:
                _dropped.Add(dropped);
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
        Send(new IntentMessage(receipt, command));
        IntentsSent++;
    }

    /// <summary>Client-paced rooms: asks the server to close <paramref name="tick"/>.</summary>
    public void RequestTick(long tick) => Send(new TickRequestMessage(tick));

    public bool HasBundle(long tick) => _bundles.ContainsKey(tick);

    /// <summary>Removes and returns the bundle of <paramref name="tick"/>; it must be there.</summary>
    public TickBundleMessage TakeBundle(long tick)
        => _bundles.Remove(tick, out var bundle) ? bundle : throw new InvalidOperationException($"The bundle of tick {tick} has not arrived.");

    public void ReportHash(long tick, ulong sessionHash) => Send(new HashReportMessage(tick, sessionHash));
    public void RequestPace(bool paused, int speedPercent) => Send(new PaceRequestMessage(paused, checked((ushort)Math.Clamp(speedPercent, Protocol.MinSpeedPercent, Protocol.MaxSpeedPercent))));
    public void ReportEnd(long tick, int winner) => Send(new EndReportMessage(tick, winner));

    public void Close() => _connection.Close();
}
