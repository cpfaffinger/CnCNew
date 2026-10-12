using System.Collections.Immutable;
using CnCNew.Net;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Server;

/// <param name="AllowModifiedBuilds">Admit clients whose build identity is a development state. An embedded (loopback) server does; a public one does not unless a developer says so.</param>
/// <param name="HostMayChooseSeeds">Take the seeds the host states in its room settings instead of choosing them here (the embedded server of a local game: a savegame and a test name their seeds).</param>
/// <param name="HashInterval">Every how many ticks a client reports its session hash.</param>
/// <param name="SeedSource">Where the server takes a seed from when it chooses; required unless the host may choose.</param>
/// <param name="MaxConnections">Members a room admits (players and watchers), at most <see cref="Protocol.MaxConnections"/>.</param>
/// <param name="LagReportMs">From this lag on (game time a member is behind the server's ticks) the server tells everybody who is behind; 0: never.</param>
/// <param name="LagThrottleMs">Above this lag of a player the game runs at <paramref name="ThrottleSpeedPercent"/> for everybody; 0: the server neither throttles nor drops for lag.</param>
/// <param name="ThrottleLimitMs">A member that stays above <paramref name="LagThrottleMs"/> for this long (time of the server's clock) is dropped.</param>
/// <param name="ThrottleSpeedPercent">The share of the set game speed while the game is slowed for somebody.</param>
public sealed record RelayOptions(bool AllowModifiedBuilds = false, bool HostMayChooseSeeds = false, ushort HashInterval = 1, Func<uint>? SeedSource = null, int MaxConnections = Protocol.MaxConnections,
    int LagReportMs = 500, int LagThrottleMs = 2_000, int ThrottleLimitMs = 10_000, int ThrottleSpeedPercent = 50, int DefaultTickRate = 60, int LagRecoveryMs = 30_000,
    int SeatHoldMs = 0, int SpectatorDelayMs = 0, bool AllowLateWatchers = false, long CatchUpLimitTicks = 432_000, int CatchUpLimitMs = 300_000, int PausesPerPlayer = 3,
    int MaxPauseMs = 60_000, bool HostSetsSpeed = true)
{
    // Phase 28 (docs/NETWORK-PROTOCOL.md section 20.4): what a member may cost a room. 0 switches a limit off (the embedded server of a local game has none).

    /// <summary>Whether a player key (its fingerprint) is banned from this server; asked when a member has proved its key (docs/NETWORK-PROTOCOL.md section 20.5).</summary>
    public Func<string, bool>? IsKeyBanned { get; init; }
    /// <summary>Admit only clients that state a player key and prove it: without it a ban by key keeps nobody out.</summary>
    public bool RequirePlayerKey { get; init; }

    /// <summary>Messages a member may send per second in the long run, and at once; beyond it the member is dropped (a client-paced room is not measured).</summary>
    public int MessagesPerSecond { get; init; } = 500;
    public int MessageBurst { get; init; } = 4_000;
    /// <summary>The largest session state the room takes: with a start from a saved match and from a member for a joiner.</summary>
    public int MaxStateBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>The longest match a host may set, in seconds of game time.</summary>
    public int MaxMatchSeconds { get; init; } = 7_200;
    /// <summary>The most the commands of a match may add up to in the recording; a match that reaches it is ended.</summary>
    public long MaxRecordingBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>How long a member may take to build the match before it is dropped.</summary>
    public int LoadLimitMs { get; init; } = 180_000;
    /// <summary>How far behind the hash of a tick may stay, in game time, before the members that have not reported it are dropped.</summary>
    public int HashReportLimitMs { get; init; } = 20_000;
    /// <summary>How often per minute the room asks a member for its state on behalf of somebody who joins.</summary>
    public int SnapshotsPerMinute { get; init; } = 6;
    /// <summary>How long a room may stand in its lobby, or after its match, before everybody in it is sent home.</summary>
    public int RoomIdleLimitMs { get; init; } = 3_600_000;
    /// <summary>How long a connection of a room may take to say its hello or to answer the password challenge.</summary>
    public int HelloLimitMs { get; init; } = 10_000;

    // Phase 27 (docs/NETWORK-PROTOCOL.md section 18). LagRecoveryMs: a member's count of slowed time starts again only after it was below the report limit for this long
    // (0: at once, the rule of phase 26). SeatHoldMs: how long the seat of a player that lost its connection is kept free before the player leaves the match (0: not at
    // all). SpectatorDelayMs: how far behind the players a member without a seat is sent the bundles. AllowLateWatchers: admit somebody without a seat to a running match.
    // CatchUpLimitTicks: the longest match a joiner is let play again from its start. CatchUpLimitMs: how long a joiner may take to catch up before it is dropped.
    // PausesPerPlayer, MaxPauseMs, HostSetsSpeed: the rules of the pace in a server-paced match.
}

/// <summary>
/// How evenly a room closed its ticks (docs/NETWORK-PROTOCOL.md section 13.4): the time between two closings on the server's clock, as its deviation from the tick length, in
/// classes. A tick closed in the same pump as the one before it (the host woke late and had more than one to close) counts as a burst.
/// </summary>
public sealed class TickIntervalHistogram
{
    private static readonly int[] LimitsMicros = [250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];
    private readonly long[] _counts = new long[LimitsMicros.Length + 1];

    public long Ticks { get; private set; }
    public long Bursts { get; private set; }
    public long MaxDeviationMicros { get; private set; }

    /// <summary>Counts per class: deviation below 0.25, 0.5, 1, 2, 4, 8, 16 ms, and from 16 ms on.</summary>
    public IReadOnlyList<long> Counts => _counts;

    internal void Add(long intervalMicros, long tickMicros)
    {
        Ticks++;
        if (intervalMicros == 0)
        {
            Bursts++;
            return;
        }
        var deviation = Math.Abs(intervalMicros - tickMicros);
        MaxDeviationMicros = Math.Max(MaxDeviationMicros, deviation);
        var index = 0;
        while (index < LimitsMicros.Length && deviation >= LimitsMicros[index]) index++;
        _counts[index]++;
    }

    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"ticks={Ticks};bursts={Bursts};maxDeviationUs={MaxDeviationMicros};below0.25/0.5/1/2/4/8/16ms/above={string.Join('/', _counts)}");
}

/// <summary>Something that happened in a room, for the log of a host: a name and its details as key and value.</summary>
public sealed record RelayEvent(string Name, IReadOnlyList<(string Key, string Value)> Details);

/// <summary>What the server recorded of a match: the description it started and every bundle it sent, in order. Enough to play the match again (docs/NETWORK-PROTOCOL.md section 6).</summary>
public sealed class RelayRecording
{
    public byte[] Description { get; internal set; } = [];
    public long StartTick { get; internal set; }
    public ResumeState? Resume { get; internal set; }
    public List<TickBundleMessage> Bundles { get; } = [];
}

/// <summary>
/// The room and the relay (docs/NETWORK-PROTOCOL.md): a state machine room, loading, match, end. The server is authoritative for the room; in a match it assigns every
/// command intent to a tick, stamps the player of its connection, and sends each tick's bundle to everybody. It does not simulate and never sees game data - it does not even
/// decode a command. Ticks are closed in one of two ways: <b>server-paced</b> (more than one client) by the injected clock at tick rate times speed, waiting for nobody;
/// <b>client-paced</b> (exactly one client: the local game) when that client asks, so a single-player game has no added delay and keeps pause, speed and fast forward.
/// <para>Single-threaded: <see cref="Attach"/> and <see cref="Pump"/> are called by one driver. Nothing here reads a clock but the injected one.</para>
/// </summary>
public sealed partial class RelayServer
{
    private sealed class Peer(uint id, IConnection connection)
    {
        public uint Id { get; } = id;
        public IConnection Connection { get; } = connection;
        public HelloMessage? Hello { get; set; }
        public int Slot { get; set; } = -1;
        public int Player { get; set; }
        public bool Ready { get; set; }
        public bool Loaded { get; set; }
        public ulong LoadedHash { get; set; }
        public byte LoadPercent { get; set; }
        public (long Tick, int Winner)? EndReport { get; set; }
        public Queue<IntentMessage> Intents { get; } = new();
        public long Discarded { get; set; }
        /// <summary>The last tick this member's simulation has completed, as it said (ping, hash report).</summary>
        public long CompletedTick { get; set; } = -1;
        public long ThrottledMicros { get; set; }
        public int LagReported { get; set; }
        public bool LagThrottling { get; set; }
        /// <summary>Above the throttle limit, and since then not yet back below half of it: the game does not flicker between slow and fast at the limit.</summary>
        public bool LagOver { get; set; }
        /// <summary>The least lag of this member in the current and in the previous window: what its line and its playback delay cost when nothing is wrong.</summary>
        public int LagFloorNow { get; set; } = int.MaxValue;
        public int LagFloorBefore { get; set; } = int.MaxValue;
        public long LagWindowStartMicros { get; set; }
        public long LagReportedAtMicros { get; set; }
        public bool Leaving { get; set; }
        /// <summary>Since when this member has been below the report limit again (0: it is not).</summary>
        public long RecoveredSinceMicros { get; set; }
        /// <summary>What this client presents to take its seat again.</summary>
        public ulong Token { get; set; }
        /// <summary>The next bundle this member has not been sent yet.</summary>
        public long NextBundle { get; set; }
        /// <summary>Admitted to a running match, not yet standing at a state of it: it waits for a snapshot or builds the match.</summary>
        public bool Joining { get; set; }
        /// <summary>Joined a running match and still playing what it missed: the rule for members that fall behind does not apply yet, and no tick waits for its report.</summary>
        public bool CatchingUp { get; set; }
        public long CatchingUpSinceMicros { get; set; }
        /// <summary>A member without a seat on a server that delays watchers.</summary>
        public bool Delayed { get; set; }
        /// <summary>Hashes of somebody who does not decide, for ticks the deciding members have not agreed on yet (bounded; compared when they have).</summary>
        public Dictionary<long, ulong> PendingHashes { get; } = [];
        public long SeatUntilMicros { get; set; }
        public long JoinDeadlineMicros { get; set; }
        public uint SnapshotRequest { get; set; }
        public uint SnapshotDonor { get; set; }
        public long SnapshotDeadlineMicros { get; set; }
        public ResumeState? Offer { get; set; }
        public ulong ExpectedHash { get; set; }
        public HashSet<uint> TriedDonors { get; } = [];
        public int PausesUsed { get; set; }
        /// <summary>The end of the match, kept back until a delayed watcher has been sent all of it.</summary>
        public MatchEndMessage? PendingEnd { get; set; }
        /// <summary>Where the connection comes from, as its host says (an address without its port; empty: unknown): what a kick remembers.</summary>
        public string Origin { get; init; } = string.Empty;
        public long AttachedMicros { get; init; }
        /// <summary>A hello that waits for the answer to the room's password challenge.</summary>
        public HelloMessage? PendingHello { get; set; }
        /// <summary>The fingerprint of the player key this member proved; empty when it stated none.</summary>
        public string Key { get; set; } = string.Empty;
        public bool KeyProved { get; set; }
        public string Nonce { get; set; } = string.Empty;
        public bool PasswordPassed { get; set; }
        public long MessageTokens { get; set; }
        public long MessageAtMicros { get; set; }
        public bool Flooding { get; set; }
    }

    private const int EventLimit = 1_024;
    private const long LagRepeatMicros = 250_000;
    private const long LagWindowMicros = 5_000_000;
    private const int MaxLagFloorMs = 1_000;

    private const int IntentQueueLimit = Protocol.MaxIntentsPerTick * 4;

    private readonly INetClock _clock;
    private readonly RelayOptions _options;
    private readonly List<Peer> _peers = [];
    private readonly SortedDictionary<int, ulong> _orders = [];
    private readonly SortedDictionary<long, SortedDictionary<uint, ulong>> _hashReports = [];
    private uint _nextConnection;
    private uint _host;
    private string _simulationId = string.Empty;
    private string _definitionFingerprint = string.Empty;
    private FormatVersions _formats;
    private bool _roomHasIdentity;
    private readonly List<(int Player, bool TransferToAlly)> _leaves = [];
    private readonly Queue<RelayEvent> _events = new();
    private long _lastMicros;
    private long _lastCloseMicros = long.MinValue;
    private long _tickMicroseconds;
    private long _accumulatedMicroseconds;

    public RelayServer(INetClock clock, RelayOptions? options = null)
    {
        _clock = clock;
        _options = options ?? new RelayOptions();
        if (!_options.HostMayChooseSeeds && _options.SeedSource is null) throw new ArgumentException("A server that chooses the seeds needs a seed source.", nameof(options));
        if (_options.HashInterval == 0) throw new ArgumentException("The hash interval is at least 1.", nameof(options));
        if (_options.MaxConnections is < 1 or > Protocol.MaxConnections) throw new ArgumentException($"A room admits 1 to {Protocol.MaxConnections} members.", nameof(options));
        if (_options.ThrottleSpeedPercent is < 1 or > 100) throw new ArgumentException("The throttle speed is 1 to 100 percent.", nameof(options));
        if (_options.DefaultTickRate is not (30 or 60 or 120)) throw new ArgumentException("The default tick rate is 30, 60 or 120.", nameof(options));
        if (_options.SeatHoldMs < 0 || _options.SpectatorDelayMs < 0 || _options.LagRecoveryMs < 0 || _options.PausesPerPlayer < 0 || _options.MaxPauseMs < 0 || _options.CatchUpLimitTicks < 0)
            throw new ArgumentException("A time or a count of the room's rules is negative.", nameof(options));
        Settings = RoomSettings.Empty with { TickRate = _options.DefaultTickRate };
        _lastMicros = clock.Microseconds;
    }

    /// <summary>The next event of this room for a host's log, oldest first; a room nobody listens to keeps the newest only.</summary>
    public bool TryTakeEvent(out RelayEvent relayEvent) => _events.TryDequeue(out relayEvent!);

    private void Log(string name, params (string Key, string Value)[] details)
    {
        if (_events.Count >= EventLimit) _events.Dequeue();
        _events.Enqueue(new RelayEvent(name, details));
    }

    private static string Text(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public RoomPhase Phase { get; private set; } = RoomPhase.Room;
    public RoomSettings Settings { get; private set; } = RoomSettings.Empty;
    public Pacing Pacing { get; private set; }
    public RelayRecording Recording { get; } = new();

    /// <summary>The next tick to close.</summary>
    public long NextTick { get; private set; }
    public bool Paused { get; private set; }
    public int SpeedPercent { get; private set; } = 100;
    public int Connections => _peers.Count;
    public int Members => _peers.Count(peer => peer.Hello is not null);

    /// <summary>Whether the game runs slowed because a player is too far behind (docs/NETWORK-PROTOCOL.md section 14).</summary>
    public bool Throttling { get; private set; }
    /// <summary>The time of the server's clock the game has run slowed so far, in microseconds, and the members dropped for being too slow.</summary>
    public long ThrottledMicros { get; private set; }
    public long DroppedForLag { get; private set; }

    /// <summary>The speed the tick clock advances at, in percent: the set speed, slowed while <see cref="Throttling"/>; 0 while nothing runs.</summary>
    public int EffectiveSpeedPercent => Phase != RoomPhase.Match || Paused || Pacing != Pacing.Server ? 0 : Math.Max(1, SpeedPercent * (Throttling ? _options.ThrottleSpeedPercent : 100) / 100);

    /// <summary>Server-paced matches: how evenly the ticks were closed, at set speed 100 and unslowed (other speeds have another tick length on the clock).</summary>
    public TickIntervalHistogram TickIntervals { get; } = new();

    /// <summary>Microseconds of the clock until the next tick closes; -1 when no tick is closed by the clock now.</summary>
    public long MicrosUntilNextTick
    {
        get
        {
            var speed = EffectiveSpeedPercent;
            return speed <= 0 || NextTick >= Settings.MaxTicks ? -1 : Math.Max(0, (_tickMicroseconds - _accumulatedMicroseconds) * 100 / speed);
        }
    }

    /// <summary>The server's game time in microseconds: ticks closed times the tick length, plus what has accumulated toward the next.</summary>
    public long GameMicros => Phase == RoomPhase.Match ? NextTick * _tickMicroseconds + _accumulatedMicroseconds : 0;
    public MatchEndMessage? End { get; private set; }
    public DesyncMessage? Desync { get; private set; }

    /// <summary>The newest tick for which every client reported the same session hash.</summary>
    public long LastAgreedTick { get; private set; } = -1;
    public long HashReports { get; private set; }

    /// <summary>Messages dropped: unknown, oversized, malformed, not fitting the phase or the sender. Never answered with a guess.</summary>
    public long Discarded { get; private set; }
    public string LastDiscard { get; private set; } = string.Empty;

    /// <summary>Intents that waited for a later tick because their player had more than <see cref="Protocol.MaxIntentsPerTick"/> in one, and intents dropped because the queue was full.</summary>
    public long IntentsDeferred { get; private set; }
    public long IntentsDropped { get; private set; }
    public long IntentsRelayed { get; private set; }

    /// <summary>A new connection; it becomes a member of the room once its hello is accepted.</summary>
    /// <param name="origin">Where the connection comes from, as far as the host wants the room to know (an address without its port): a member the host of the room
    /// kicked cannot come back from there. Empty: unknown.</param>
    public void Attach(IConnection connection, string origin = "")
        => _peers.Add(new Peer(++_nextConnection, connection) { Origin = origin, AttachedMicros = _clock.Microseconds, MessageTokens = _options.MessageBurst * TokenScale, MessageAtMicros = _clock.Microseconds });

    private static void Send(Peer peer, NetMessage message) => peer.Connection.Send(MessageCodec.Encode(message));

    private void Broadcast(NetMessage message)
    {
        var bytes = MessageCodec.Encode(message);
        foreach (var peer in _peers)
            if (peer.Hello is not null) peer.Connection.Send(bytes);
    }

    private void Discard(Peer peer, string reason)
    {
        Discarded++;
        peer.Discarded++;
        LastDiscard = $"connection {peer.Id}: {reason}";
    }

    private void Refuse(Peer peer, RejectReason reason, string text) => Send(peer, new RejectMessage(reason, text));

    /// <summary>Takes in everything that has arrived, drops what left, and closes the ticks that are due.</summary>
    public void Pump()
    {
        for (var index = 0; index < _peers.Count; index++)
        {
            var peer = _peers[index];
            while (!peer.Flooding && peer.Connection.TryReceive(out var bytes))
            {
                if (!MessageAllowed(peer))
                {
                    peer.Flooding = true;
                    break;
                }
                if (!MessageCodec.TryDecode(bytes, out var message, out var error))
                {
                    Discard(peer, error);
                    continue;
                }
                Handle(peer, message!);
            }
        }
        EnforceLimits(_clock.Microseconds);
        for (var index = _peers.Count - 1; index >= 0; index--)
        {
            // A drop can take more than one member with it (a joiner whose donor is gone is asked again, not dropped - but be safe against a list that shrank).
            if (index >= _peers.Count) continue;
            var peer = _peers[index];
            if (peer.Flooding)
            {
                FloodDrops++;
                Log("flood", ("connection", Text(peer.Id)), ("player", Text(peer.Player)));
                peer.Connection.Close();
                Drop(peer, DropReason.Flood);
            }
            else if (peer.Leaving)
            {
                peer.Connection.Close();
                Drop(peer, DropReason.Left);
            }
            else if (!peer.Connection.IsOpen)
            {
                Drop(peer, DropReason.ConnectionLost);
            }
        }
        AdvanceClock();
        if (Phase is RoomPhase.Match or RoomPhase.Ended)
        {
            if (Phase == RoomPhase.Match) DeliverSnapshots(_clock.Microseconds);
            FeedAll();
        }
    }

    private void Handle(Peer peer, NetMessage message)
    {
        if (peer.Hello is null)
        {
            if (message is HelloMessage hello && peer.PendingHello is null) Admit(peer, hello);
            else if (message is PasswordResponseMessage response && peer.PendingHello is { } pending) TakePassword(peer, pending, response);
            else Discard(peer, $"a {message.Type} message before the hello");
            return;
        }
        switch (message)
        {
            case RoomConfigMessage config when Phase == RoomPhase.Room:
                if (peer.Id != _host) { Refuse(peer, RejectReason.NotHost, "Only the host sets the room."); break; }
                Configure(peer, config.Settings);
                break;
            case SlotRequestMessage request when Phase == RoomPhase.Room:
                Seat(peer, request);
                break;
            case ReadyMessage ready when Phase == RoomPhase.Room:
                peer.Ready = ready.Ready;
                BroadcastRoom();
                break;
            case StartRequestMessage start when Phase == RoomPhase.Room:
                if (peer.Id != _host) { Refuse(peer, RejectReason.NotHost, "Only the host starts the match."); break; }
                StartMatch(peer, start.Resume);
                break;
            case LoadProgressMessage progress when Phase == RoomPhase.Loading:
                peer.LoadPercent = progress.Percent;
                Broadcast(new LoadProgressStateMessage(peer.Id, progress.Percent));
                break;
            case LoadedMessage loaded when Phase == RoomPhase.Match && peer.Joining && peer.SnapshotRequest == 0:
                LateLoaded(peer, loaded);
                break;
            case SnapshotOfferMessage offer when Phase == RoomPhase.Match:
                TakeSnapshot(peer, offer);
                break;
            case DesyncDetailMessage detail when Phase == RoomPhase.Ended:
                TakeDetail(peer, detail);
                break;
            // What a client of a finished match still had on its way is not a violation: the two channels are not ordered against each other.
            case HashReportMessage or PingMessage or IntentMessage or EndReportMessage or SnapshotOfferMessage or LoadProgressMessage when Phase == RoomPhase.Ended:
                if (message is PingMessage late) peer.Connection.SendUrgent(MessageCodec.Encode(new PongMessage(late.ClientMicros, 0, 0)));
                break;
            case LoadedMessage loaded when Phase == RoomPhase.Loading && !peer.Loaded:
                peer.Loaded = true;
                peer.LoadedHash = loaded.SessionHash;
                CheckAllLoaded();
                break;
            case ChatRequestMessage chat:
                Broadcast(new ChatMessage(peer.Id, chat.Text));
                break;
            case KickMessage kick:
                Kick(peer, kick.ConnectionId);
                break;
            case PingMessage ping:
                if (Phase == RoomPhase.Match && !peer.Joining) peer.CompletedTick = Math.Max(peer.CompletedTick, Math.Min(ping.CompletedTick, NextTick - 1));
                // On the urgent channel like the bundles: a clock sample that waited behind a lost packet would be a wrong one. A delayed watcher is told the clock of
                // the stream it is sent: the server's, less the delay.
                peer.Connection.SendUrgent(MessageCodec.Encode(new PongMessage(ping.ClientMicros, peer.Delayed ? Math.Max(0, GameMicros - _delayTicks * _tickMicroseconds) : GameMicros,
                    (ushort)EffectiveSpeedPercent)));
                break;
            case LeaveMessage:
                peer.Leaving = true;
                break;
            case IntentMessage intent when Phase == RoomPhase.Match:
                TakeIntent(peer, intent);
                break;
            case TickRequestMessage request when Phase == RoomPhase.Match:
                if (Pacing != Pacing.Client) Discard(peer, "a tick request in a server-paced match");
                else if (request.Tick != NextTick) Discard(peer, $"a request for tick {request.Tick} while tick {NextTick} is next");
                else CloseTick();
                break;
            case HashReportMessage report when Phase == RoomPhase.Match:
                TakeHash(peer, report);
                break;
            case PaceRequestMessage pace when Phase == RoomPhase.Match:
                if (pace.SpeedPercent is < Protocol.MinSpeedPercent or > Protocol.MaxSpeedPercent) { Discard(peer, $"a speed of {pace.SpeedPercent} percent"); break; }
                if (CheckPace(peer, pace) is { } notGranted) { Send(peer, notGranted); break; }
                Paused = pace.Paused;
                SpeedPercent = pace.SpeedPercent;
                // What everybody is told is the speed the ticks really close at (a client-paced room has no clock: there it is the speed as set).
                Broadcast(new PaceStateMessage(Paused, (ushort)(Pacing == Pacing.Server && Throttling ? Math.Max(1, SpeedPercent * _options.ThrottleSpeedPercent / 100) : SpeedPercent), peer.Id));
                break;
            case EndReportMessage report when Phase == RoomPhase.Match:
                peer.EndReport = (report.Tick, report.Winner);
                CheckEnd();
                break;
            // A member that is not yet in the match says nothing that counts.
            case HashReportMessage or IntentMessage or EndReportMessage when peer.Joining:
                break;
            default:
                Discard(peer, $"a {message.Type} message in phase {Phase}");
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------- room

    private void Admit(Peer peer, HelloMessage hello)
    {
        RejectMessage? reject = null;
        var identity = BuildIdentity.Parse(hello.SimulationId);
        // A running match takes in a player that comes back to its held seat, and - where the server allows it - somebody who watches.
        var late = Phase == RoomPhase.Match && Pacing == Pacing.Server && (hello.RejoinToken != 0 || hello.Watch);
        if (hello.ProtocolVersion != Protocol.Version) reject = new RejectMessage(RejectReason.ProtocolVersion, $"The server speaks protocol {Protocol.Version}, the client {hello.ProtocolVersion}.");
        else if (Phase != RoomPhase.Room && !late) reject = new RejectMessage(RejectReason.MatchRunning, "The match has started.");
        else if (_peers.Count(other => other.Hello is not null) >= _options.MaxConnections) reject = new RejectMessage(RejectReason.RoomFull, "The room is full.");
        else if (identity.Modified && !_options.AllowModifiedBuilds) reject = new RejectMessage(RejectReason.ModifiedBuild, "The client is a modified development build; this server admits released builds only.");
        else if (_roomHasIdentity)
        {
            // The first member defines what the room plays with; everybody after it must be the same in every respect, or the match is certain to diverge.
            if (FormatVersions.Of(hello) != _formats) reject = new RejectMessage(RejectReason.FormatVersion, "The client speaks other format versions than the room.");
            else if (!string.Equals(hello.SimulationId, _simulationId, StringComparison.Ordinal)) reject = new RejectMessage(RejectReason.SimulationVersion, $"The room plays simulation {_simulationId}, the client is {hello.SimulationId}.");
            else if (!string.Equals(hello.DefinitionFingerprint, _definitionFingerprint, StringComparison.OrdinalIgnoreCase)) reject = new RejectMessage(RejectReason.Definitions, "The client has other game definitions than the room.");
        }
        if (reject is null && _options.RequirePlayerKey && hello.PlayerKey.Length == 0)
            reject = new RejectMessage(RejectReason.BadIdentity, "This server admits only clients that state a player key.");
        if (reject is null && peer.Origin.Length > 0 && _kicked.Contains(peer.Origin)) reject = new RejectMessage(RejectReason.Kicked, "The host of this room removed this client from it.");
        // A room with a password asks everybody but the one who opened it - and but a player that proves by its token that it was let in before. Whoever states a
        // player key is asked to prove it, in the same exchange.
        if (reject is null && ((AsksPassword(hello, late) && !peer.PasswordPassed) || (hello.PlayerKey.Length > 0 && !peer.KeyProved)))
        {
            peer.PendingHello = hello;
            peer.Nonce = NewNonce();
            Send(peer, new PasswordChallengeMessage(peer.Nonce));
            return;
        }
        if (reject is null && peer.Key.Length > 0)
        {
            if (_kickedKeys.Contains(peer.Key)) reject = new RejectMessage(RejectReason.Kicked, "The host of this room removed this player from it.");
            else if (_options.IsKeyBanned?.Invoke(peer.Key) == true)
            {
                KeysBanned++;
                reject = new RejectMessage(RejectReason.Banned, "This player key is banned from this server.");
            }
        }
        if (reject is null && late) reject = AdmitLate(peer, hello);
        if (reject is null && late) return;
        if (reject is not null)
        {
            Log("rejected", ("connection", Text(peer.Id)), ("reason", reject.Reason.ToString()));
            Send(peer, reject);
            peer.Connection.Close();
            _peers.Remove(peer);
            return;
        }
        if (!_roomHasIdentity)
        {
            _roomHasIdentity = true;
            _formats = FormatVersions.Of(hello);
            _simulationId = hello.SimulationId;
            _definitionFingerprint = hello.DefinitionFingerprint.ToUpperInvariant();
            _host = peer.Id;
            // Whoever opens the room sets its password - as a verifier, the password itself never comes here.
            _verifier = hello.RoomVerifier;
            if (_verifier.Length > 0) Log("room-locked");
        }
        peer.Hello = hello;
        peer.Token = NewToken();
        Log("joined", ("connection", Text(peer.Id)), ("name", hello.Name), ("host", peer.Id == _host ? "true" : "false"), ("build", hello.SimulationId), ("key", peer.Key.Length > 0 ? peer.Key : "none"));
        Send(peer, new WelcomeMessage(peer.Id, peer.Id == _host, peer.Token));
        BroadcastRoom();
    }

    private void Configure(Peer host, RoomSettings settings)
    {
        if (settings.TickRate is not (30 or 60 or 120) || settings.Slots.Length is < 2 or > Protocol.MaxSlots || settings.MaxTicks <= 0 || settings.StartMoney < 0
            || settings.MapPath.Length == 0 || settings.TerrainFingerprint.Length == 0 || settings.GraceFrames < 0 || settings.SciencePointIntervalFrames < 0)
        {
            Refuse(host, RejectReason.InvalidRequest, "The room settings are not valid.");
            return;
        }
        if (_options.MaxMatchSeconds > 0 && settings.MaxTicks > (long)_options.MaxMatchSeconds * settings.TickRate)
        {
            Refuse(host, RejectReason.InvalidRequest, $"This server plays matches of at most {_options.MaxMatchSeconds / 60} minutes of game time.");
            return;
        }
        if (settings.HasSeed && !_options.HostMayChooseSeeds)
        {
            Refuse(host, RejectReason.InvalidRequest, "This server chooses the seeds.");
            return;
        }
        Settings = settings;
        // A new layout empties the seats: what a member sat in may not exist any more.
        foreach (var peer in _peers)
        {
            peer.Slot = -1;
            peer.Ready = false;
        }
        BroadcastRoom();
    }

    private void Seat(Peer peer, SlotRequestMessage request)
    {
        if (request.Slot == SlotRequestMessage.Leave)
        {
            peer.Slot = -1;
            peer.Ready = false;
            BroadcastRoom();
            return;
        }
        if (request.Slot >= Settings.Slots.Length || Settings.Slots[request.Slot].Kind != MatchSlotKind.Human)
        {
            Refuse(peer, RejectReason.InvalidRequest, "That slot is not a seat for a player.");
            return;
        }
        if (_peers.Any(other => other != peer && other.Slot == request.Slot))
        {
            Refuse(peer, RejectReason.InvalidRequest, "That slot is taken.");
            return;
        }
        if (request.StartPosition >= 0 && Settings.Slots.Where((slot, index) => index != request.Slot && slot.TakesPart && slot.StartPosition == request.StartPosition).Any())
        {
            Refuse(peer, RejectReason.InvalidRequest, "That start position is taken.");
            return;
        }
        peer.Slot = request.Slot;
        peer.Ready = false;
        Settings = Settings with
        {
            Slots = Settings.Slots.SetItem(request.Slot, Settings.Slots[request.Slot] with { Faction = request.Faction, Team = request.Team, Color = request.Color, StartPosition = request.StartPosition }),
        };
        BroadcastRoom();
    }

    private void BroadcastRoom()
        => Broadcast(new RoomStateMessage(Phase, Settings with { HasSeed = false, Seed = 0, AiSeed = 0 },
            [.._peers.Where(peer => peer.Hello is not null).Select(peer => new RoomMember(peer.Id, peer.Hello!.Name, peer.Slot, peer.Ready, peer.Id == _host))]));

    private void StartMatch(Peer host, ResumeState? resume)
    {
        var members = _peers.Where(peer => peer.Hello is not null).ToList();
        for (var slot = 0; slot < Settings.Slots.Length; slot++)
            if (Settings.Slots[slot].Kind == MatchSlotKind.Human && members.All(peer => peer.Slot != slot))
            {
                Refuse(host, RejectReason.NotReady, $"Slot {slot} waits for a player.");
                return;
            }
        if (Settings.Slots.Count(slot => slot.TakesPart) < 2)
        {
            Refuse(host, RejectReason.NotReady, "A match needs at least two participants.");
            return;
        }
        if (members.Any(peer => peer != host && peer.Slot >= 0 && !peer.Ready))
        {
            Refuse(host, RejectReason.NotReady, "Not every player is ready.");
            return;
        }
        if (resume is not null && _options.MaxStateBytes > 0 && resume.State.Length > _options.MaxStateBytes)
        {
            StatesRefusedForSize++;
            Refuse(host, RejectReason.InvalidRequest, $"The saved match is larger than the {_options.MaxStateBytes / 1024} KiB this server takes.");
            return;
        }
        var chosen = Settings.HasSeed && _options.HostMayChooseSeeds;
        var seed = chosen ? Settings.Seed : _options.SeedSource!();
        var aiSeed = chosen ? Settings.AiSeed : _options.SeedSource!();
        var description = Settings.ToDescription(_simulationId, _definitionFingerprint, seed, aiSeed).Encode();
        if (description.Length > Protocol.MaxDescriptionBytes)
        {
            Refuse(host, RejectReason.InvalidRequest, "The match description is too large.");
            return;
        }
        // The slots that take part are numbered from player 1 in slot order - the rule MatchDescription.Resolve applies on every client.
        var player = 0;
        var playerOfSlot = new int[Settings.Slots.Length];
        for (var slot = 0; slot < Settings.Slots.Length; slot++)
            if (Settings.Slots[slot].TakesPart) playerOfSlot[slot] = ++player;
        _orders.Clear();
        if (resume is not null)
            foreach (var (resumed, orders) in resume.OrdersSoFar) _orders[resumed] = orders;
        Pacing = members.Count == 1 ? Pacing.Client : Pacing.Server;
        Phase = RoomPhase.Loading;
        NextTick = resume?.Tick ?? 0;
        _tickMicroseconds = 1_000_000L / Settings.TickRate;
        Recording.Description = description;
        Recording.StartTick = NextTick;
        Recording.Resume = resume;
        Recording.Bundles.Clear();
        _leaves.Clear();
        _held.Clear();
        _agreed.Clear();
        _agreedOrder.Clear();
        _details.Clear();
        _delayTicks = (long)_options.SpectatorDelayMs * Settings.TickRate / 1000;
        _pauseUntilMicros = 0;
        Throttling = false;
        RecordingBytes = 0;
        _loadingSinceMicros = _clock.Microseconds;
        Log("match-started", ("map", Settings.MapPath), ("tickRate", Text(Settings.TickRate)), ("participants", Text(Settings.Slots.Count(slot => slot.TakesPart))), ("members", Text(members.Count)),
            ("pacing", Pacing.ToString()), ("startTick", Text(NextTick)));
        foreach (var peer in members)
        {
            peer.CompletedTick = NextTick - 1;
            peer.ThrottledMicros = 0;
            peer.LagReported = 0;
            peer.LagThrottling = false;
            peer.LagOver = false;
            peer.LagFloorNow = int.MaxValue;
            peer.LagFloorBefore = int.MaxValue;
            peer.LagWindowStartMicros = _clock.Microseconds;
            peer.Player = peer.Slot >= 0 ? playerOfSlot[peer.Slot] : 0;
            peer.NextBundle = NextTick;
            peer.Joining = false;
            peer.CatchingUp = false;
            peer.RecoveredSinceMicros = 0;
            peer.PausesUsed = 0;
            peer.PendingEnd = null;
            // Only where ticks are closed by the clock is there a delay to speak of: the one member of a local game watches its own match as it runs.
            peer.Delayed = peer.Player == 0 && _delayTicks > 0 && Pacing == Pacing.Server;
            peer.Loaded = false;
            peer.EndReport = null;
            peer.Intents.Clear();
            Send(peer, new MatchStartMessage(description, peer.Player, Pacing, _options.HashInterval, resume));
        }
        BroadcastRoom();
    }

    private void CheckAllLoaded()
    {
        var members = _peers.Where(peer => peer.Hello is not null).ToList();
        if (members.Count == 0 || members.Any(peer => !peer.Loaded)) return;
        // Everybody built the match from the same bytes; whoever does not stand at the same state now would diverge at once.
        var expected = Recording.Resume?.SessionHash ?? members[0].LoadedHash;
        if (members.Any(peer => peer.LoadedHash != expected))
        {
            ReportDesync(NextTick - 1, [..members.Select(peer => (peer.Id, peer.LoadedHash))]);
            return;
        }
        _startHash = expected;
        Phase = RoomPhase.Match;
        _lastCloseMicros = long.MinValue;
        _lastMicros = _clock.Microseconds;
        _accumulatedMicroseconds = 0;
        Log("match-running", ("members", Text(members.Count)));
        Broadcast(new AllLoadedMessage(NextTick));
        BroadcastRoom();
    }

    // ------------------------------------------------------------------------------------------------------------ match

    private void TakeIntent(Peer peer, IntentMessage intent)
    {
        if (peer.Player == 0)
        {
            Discard(peer, "an intent from a connection that does not play");
            return;
        }
        if (!CommandWire.IsPlausible(intent.Command, out var error))
        {
            Discard(peer, error);
            return;
        }
        if (peer.Intents.Count >= IntentQueueLimit)
        {
            IntentsDropped++;
            Discard(peer, "more intents than the queue of a player holds");
            return;
        }
        peer.Intents.Enqueue(intent);
    }

    private void AdvanceClock()
    {
        var now = _clock.Microseconds;
        var elapsed = now - _lastMicros;
        _lastMicros = now;
        if (Phase != RoomPhase.Match || Pacing != Pacing.Server || elapsed <= 0) return;
        CheckHashStall();
        if (Phase != RoomPhase.Match) return;
        ExpireSeats(now);
        EndOverlongPause(now);
        ApplyLagRule(now, elapsed);
        if (Phase != RoomPhase.Match || Paused) return;
        if (Throttling) ThrottledMicros += elapsed;
        var speed = EffectiveSpeedPercent;
        _accumulatedMicroseconds += elapsed * speed / 100;
        if (_accumulatedMicroseconds >= _tickMicroseconds && NextTick < Settings.MaxTicks)
        {
            // How evenly the host lets this room close its ticks: the first of a pump against the last one before, every further one of the same pump as a burst.
            if (speed == 100 && _lastCloseMicros != long.MinValue)
            {
                TickIntervals.Add(now - _lastCloseMicros, _tickMicroseconds);
                for (var more = _accumulatedMicroseconds / _tickMicroseconds - 1; more > 0; more--) TickIntervals.Add(0, _tickMicroseconds);
            }
            _lastCloseMicros = now;
        }
        // The server closes its ticks by its clock and waits for nobody.
        while (Phase == RoomPhase.Match && _accumulatedMicroseconds >= _tickMicroseconds && NextTick < Settings.MaxTicks)
        {
            _accumulatedMicroseconds -= _tickMicroseconds;
            CloseTick();
        }
    }

    /// <summary>
    /// The rule for members that fall behind (docs/NETWORK-PROTOCOL.md section 14): the server closes its ticks by its clock and waits for nobody; a lag is told to everybody
    /// from <see cref="RelayOptions.LagReportMs"/> on; above <see cref="RelayOptions.LagThrottleMs"/> of a player the game is slowed for everybody; a member that stays above
    /// it for <see cref="RelayOptions.ThrottleLimitMs"/> is dropped. Once slowed, the game stays slowed until the member is back below half the limit, and the time counts on
    /// until it is back below the report limit - a member that hovers at the limit is dropped, not carried. A watcher slows nobody, it is only dropped.
    /// </summary>
    private void ApplyLagRule(long now, long elapsed)
    {
        if (_options.LagReportMs <= 0 && _options.LagThrottleMs <= 0) return;
        var throttle = false;
        for (var index = _peers.Count - 1; index >= 0; index--)
        {
            var peer = _peers[index];
            if (peer.Hello is null || peer.Joining) continue;
            var rawLagMs = (int)Math.Clamp((StreamHead(peer) - peer.CompletedTick) * _tickMicroseconds / 1000, 0, int.MaxValue);
            if (peer.CatchingUp)
            {
                // A member that joined the running match plays what it missed as fast as it can; the rule begins when it has arrived - or it is given up.
                if (peer.CatchingUpSinceMicros == 0) peer.CatchingUpSinceMicros = now;
                if (rawLagMs < 1_000)
                {
                    peer.CatchingUp = false;
                    peer.CatchingUpSinceMicros = 0;
                    peer.LagWindowStartMicros = now;
                    Log("caught-up", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("lagMs", Text(rawLagMs)), ("slowsTheGame", "false"));
                }
                else if (_options.CatchUpLimitMs > 0 && now - peer.CatchingUpSinceMicros > _options.CatchUpLimitMs * 1000L)
                {
                    DroppedForLag++;
                    peer.Connection.Close();
                    Drop(peer, DropReason.TooSlow);
                }
                continue;
            }
            // A member on a long line is "behind" by its way and its playback delay all the time. That is not lag: the rule counts what goes beyond the least it showed
            // in the last windows (at most a second of it - a member that is always further behind than that is behind).
            if (now - peer.LagWindowStartMicros >= LagWindowMicros)
            {
                peer.LagFloorBefore = peer.LagFloorNow;
                peer.LagFloorNow = int.MaxValue;
                peer.LagWindowStartMicros = now;
            }
            peer.LagFloorNow = Math.Min(peer.LagFloorNow, rawLagMs);
            var floor = Math.Min(peer.LagFloorBefore, peer.LagFloorNow);
            var lagMs = rawLagMs - Math.Min(floor, MaxLagFloorMs);
            var over = _options.LagThrottleMs > 0 && (lagMs > _options.LagThrottleMs || (peer.LagOver && lagMs > _options.LagThrottleMs / 2));
            peer.LagOver = over;
            if (over && !Paused)
            {
                peer.ThrottledMicros += elapsed;
                if (peer.ThrottledMicros > _options.ThrottleLimitMs * 1000L)
                {
                    DroppedForLag++;
                    peer.Connection.Close();
                    Drop(peer, DropReason.TooSlow);
                    continue;
                }
            }
            // The count of slowed time starts again only when the member has stayed below the report limit for the recovery time: one that catches up at the slowed
            // speed and falls behind again at full speed is one incident, not many.
            if (lagMs < Math.Max(1, _options.LagReportMs))
            {
                if (peer.RecoveredSinceMicros == 0) peer.RecoveredSinceMicros = now;
                if (now - peer.RecoveredSinceMicros >= _options.LagRecoveryMs * 1000L) peer.ThrottledMicros = 0;
            }
            else
            {
                peer.RecoveredSinceMicros = 0;
            }
            var slows = over && peer.Player > 0;
            throttle |= slows;
            // Told from the report limit on, and withdrawn below half of it: the notice does not flicker at the limit.
            var reported = _options.LagReportMs > 0 && (lagMs >= _options.LagReportMs || (peer.LagReported != 0 && lagMs >= _options.LagReportMs / 2)) ? Math.Max(1, lagMs) : 0;
            if ((reported == 0) != (peer.LagReported == 0) || slows != peer.LagThrottling || (reported != 0 && now - peer.LagReportedAtMicros >= LagRepeatMicros))
            {
                if ((reported == 0) != (peer.LagReported == 0) || slows != peer.LagThrottling)
                    Log(reported == 0 ? "caught-up" : "behind", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("lagMs", Text(lagMs)), ("slowsTheGame", slows ? "true" : "false"));
                peer.LagReported = reported;
                peer.LagThrottling = slows;
                peer.LagReportedAtMicros = now;
                Broadcast(new LagStateMessage(peer.Id, reported, slows));
            }
        }
        if (throttle == Throttling) return;
        Throttling = throttle;
        Log(throttle ? "throttled" : "full-speed", ("tick", Text(NextTick)), ("speedPercent", Text(EffectiveSpeedPercent)));
        Broadcast(new PaceStateMessage(Paused, (ushort)Math.Max(1, SpeedPercent * (Throttling ? _options.ThrottleSpeedPercent : 100) / 100), 0));
    }

    private void CloseTick()
    {
        var tick = NextTick;
        var commands = ImmutableArray.CreateBuilder<BundleCommand>();
        // By player, then in the order of arrival: the order every client submits in.
        foreach (var peer in _peers.Where(peer => peer.Player > 0).OrderBy(peer => peer.Player))
        {
            var taken = 0;
            while (peer.Intents.Count > 0 && taken < Protocol.MaxIntentsPerTick)
            {
                var intent = peer.Intents.Dequeue();
                var order = _orders.GetValueOrDefault(peer.Player) + 1;
                _orders[peer.Player] = order;
                commands.Add(new BundleCommand(peer.Player, order, peer.Id, intent.Receipt, CommandWire.Stamp(intent.Command, peer.Player, tick)));
                taken++;
            }
            IntentsDeferred += peer.Intents.Count;
        }
        if (_leaves.Count > 0)
        {
            // A player that left: its leave is a command of this tick like any other - the last one of that player - so that every simulation executes it in the same tick.
            foreach (var (player, transfer) in _leaves)
            {
                var order = _orders.GetValueOrDefault(player) + 1;
                _orders[player] = order;
                commands.Add(new BundleCommand(player, order, 0, 0, CommandWire.Leave(player, tick, transfer)));
            }
            _leaves.Clear();
            var sorted = commands.OrderBy(command => command.Player).ToList();
            commands.Clear();
            commands.AddRange(sorted);
        }
        var bundle = new TickBundleMessage(tick, commands.ToImmutable());
        IntentsRelayed += bundle.Commands.Length;
        Recording.Bundles.Add(bundle);
        foreach (var command in bundle.Commands) RecordingBytes += command.Command.Length + 32;
        RecordingBytes += 16;
        NextTick = tick + 1;
        if (_options.MaxRecordingBytes > 0 && RecordingBytes > _options.MaxRecordingBytes)
        {
            // The room must not grow without end, whoever feeds it: the match is over, and everybody is told why.
            Log("recording-limit", ("bytes", Text(RecordingBytes)), ("tick", Text(tick)));
            FeedAll();
            Finish(new MatchEndMessage(NextTick, 0, EndReason.ServerLimit));
            return;
        }
        if (NextTick >= Settings.MaxTicks) _lastTickAtMicros = _clock.Microseconds;
        FeedAll();
    }

    private void TakeHash(Peer peer, HashReportMessage report)
    {
        if (report.Tick < Recording.StartTick || report.Tick >= NextTick)
        {
            Discard(peer, $"a hash report for tick {report.Tick}");
            return;
        }
        HashReports++;
        peer.CompletedTick = Math.Max(peer.CompletedTick, report.Tick);
        if (report.Tick <= LastAgreedTick || !Decides(peer))
        {
            // Somebody who plays behind the others - a member that catches up, a delayed watcher - reports ticks the others have long agreed on, and somebody who only
            // watches decides nothing. Its hash is compared with theirs; a tick the server no longer remembers is past comparing.
            if (_agreed.TryGetValue(report.Tick, out var agreed))
            {
                if (agreed != report.SessionHash) Diverged(peer, report.Tick, report.SessionHash, agreed);
                return;
            }
            if (report.Tick <= LastAgreedTick) return;
            if (!Decides(peer))
            {
                // Not agreed yet: kept until it is. Somebody who reports far ahead of the players keeps the newest few thousand.
                if (peer.PendingHashes.Count >= MaxPendingHashes) peer.PendingHashes.Remove(peer.PendingHashes.Keys.Min());
                peer.PendingHashes[report.Tick] = report.SessionHash;
                return;
            }
        }
        if (!_hashReports.TryGetValue(report.Tick, out var reports)) _hashReports[report.Tick] = reports = [];
        reports[peer.Id] = report.SessionHash;
        if (reports.Values.Distinct().Count() > 1)
        {
            ReportDesync(report.Tick, [..reports.Select(pair => (pair.Key, pair.Value))]);
            return;
        }
        if (_peers.Any(other => Decides(other) && !reports.ContainsKey(other.Id))) return;
        _hashReports.Remove(report.Tick);
        Agree(report.Tick, report.SessionHash);
    }

    private void ReportDesync(long tick, ImmutableArray<(uint, ulong)> reports)
    {
        Desync = new DesyncMessage(tick, reports);
        Log("desync", ("tick", Text(tick)), ("reports", string.Join(' ', reports.Select(report => $"{report.Item1}:{report.Item2:X16}"))));
        Broadcast(Desync);
        Finish(new MatchEndMessage(tick, 0, EndReason.Desync));
    }

    private void CheckEnd()
    {
        // The end is what those say who decide: the players that play along in time. A watcher's word is not waited for, and a delayed one gets there later.
        var members = _peers.Where(Decides).ToList();
        if (members.Count == 0 || members.Any(peer => peer.EndReport is null)) return;
        var first = members[0].EndReport!.Value;
        // Clients that disagree about the end disagree about the state; the hash reports will say where.
        if (members.Any(peer => peer.EndReport!.Value != first)) return;
        Finish(new MatchEndMessage(first.Tick, first.Winner, EndReason.Finished));
    }

    private void Finish(MatchEndMessage end)
    {
        if (Phase == RoomPhase.Ended) return;
        End = end;
        Phase = RoomPhase.Ended;
        Throttling = false;
        _endedAtMicros = NextTick >= Settings.MaxTicks && Recording.Bundles.Count > 0 ? Math.Min(_lastTickAtMicros, _clock.Microseconds) : _clock.Microseconds;
        _held.Clear();
        Log("match-ended", ("tick", Text(end.Tick)), ("winner", Text(end.Winner)), ("reason", end.Reason.ToString()), ("lastAgreedTick", Text(LastAgreedTick)),
            ("lastAgreedHash", _agreed.TryGetValue(LastAgreedTick, out var hash) ? hash.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) : "none"), ("throttledMs", Text(ThrottledMicros / 1000)));
        var bytes = MessageCodec.Encode(end);
        foreach (var peer in _peers)
        {
            if (peer.Hello is null) continue;
            // A delayed watcher that has not been sent all of the match yet is told the end when it has (a desync or an abandoned match ends for everybody at once).
            if (peer.Delayed && end.Reason == EndReason.Finished && peer.NextBundle < NextTick) peer.PendingEnd = end;
            else peer.Connection.Send(bytes);
        }
    }

    private void Drop(Peer peer, DropReason reason)
    {
        _peers.Remove(peer);
        if (peer.Hello is null) return;
        // After the end a connection that closes is a client that went home, whatever the transport calls it.
        var said = Phase == RoomPhase.Ended ? DropReason.Left : reason;
        Log("left", ("connection", Text(peer.Id)), ("player", Text(peer.Player)), ("reason", Phase == RoomPhase.Ended ? "Closed" : reason.ToString()), ("phase", Phase.ToString()), ("tick", Text(NextTick)));
        Broadcast(new PlayerDroppedMessage(peer.Player, peer.Id, said));
        if (Phase == RoomPhase.Match && peer.Player > 0 && Pacing == Pacing.Server && peer.Token != 0
            && ((reason == DropReason.ConnectionLost && _options.SeatHoldMs > 0 && !peer.Joining) || (peer.Joining && peer.SeatUntilMicros > _clock.Microseconds)))
        {
            // A lost connection is not yet a player that left: the seat is kept free for a while, and the player's units stand where they are. (A joiner that did not
            // make it gives its seat back for the rest of that time.)
            HoldSeat(peer);
        }
        else if (Phase is RoomPhase.Loading or RoomPhase.Match && peer.Player > 0)
        {
            // What the leave means for the simulation is a command in the next bundle (PlayerLeaveCommand): by choice a living ally inherits, otherwise everything of the player dies.
            _leaves.Add((peer.Player, reason == DropReason.Left));
        }
        // A joiner that waited for this member's snapshot asks the next one.
        foreach (var waiting in _peers.Where(other => other.Joining && other.SnapshotRequest != 0 && other.SnapshotDonor == peer.Id).ToList()) RequestSnapshot(waiting);
        // The hash reports of ticks it never reported must not wait for it.
        foreach (var tick in _hashReports.Keys.ToArray())
        {
            var reports = _hashReports[tick];
            foreach (var id in reports.Keys.Where(id => !_peers.Any(other => other.Id == id && Decides(other))).ToList()) MoveToPending(id, tick, reports);
            if (_peers.Any(other => Decides(other) && !reports.ContainsKey(other.Id))) continue;
            _hashReports.Remove(tick);
            if (reports.Count > 0 && reports.Values.Distinct().Count() <= 1 && tick > LastAgreedTick) Agree(tick, reports.Values.First());
        }
        var members = _peers.Where(other => other.Hello is not null && !other.Joining).ToList();
        if (members.Count == 0)
        {
            if (Phase is RoomPhase.Loading or RoomPhase.Match) Finish(new MatchEndMessage(NextTick, 0, EndReason.Abandoned));
            return;
        }
        if (peer.Id == _host) _host = members[0].Id;
        if (Phase == RoomPhase.Room) BroadcastRoom();
        else if (Phase == RoomPhase.Loading) CheckAllLoaded();
        else if (Phase == RoomPhase.Match) CheckEnd();
    }
}
