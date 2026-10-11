using System.Collections.Immutable;
using CnCNew.Net;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Server;

/// <param name="AllowModifiedBuilds">Admit clients whose build identity is a development state. An embedded (loopback) server does; a public one does not unless a developer says so.</param>
/// <param name="HostMayChooseSeeds">Take the seeds the host states in its room settings instead of choosing them here (the embedded server of a local game: a savegame and a test name their seeds).</param>
/// <param name="HashInterval">Every how many ticks a client reports its session hash.</param>
/// <param name="SeedSource">Where the server takes a seed from when it chooses; required unless the host may choose.</param>
public sealed record RelayOptions(bool AllowModifiedBuilds = false, bool HostMayChooseSeeds = false, ushort HashInterval = 1, Func<uint>? SeedSource = null);

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
public sealed class RelayServer
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
    }

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
    private long _lastMs;
    private long _tickMicroseconds;
    private long _accumulatedMicroseconds;

    public RelayServer(INetClock clock, RelayOptions? options = null)
    {
        _clock = clock;
        _options = options ?? new RelayOptions();
        if (!_options.HostMayChooseSeeds && _options.SeedSource is null) throw new ArgumentException("A server that chooses the seeds needs a seed source.", nameof(options));
        if (_options.HashInterval == 0) throw new ArgumentException("The hash interval is at least 1.", nameof(options));
        _lastMs = clock.Milliseconds;
    }

    public RoomPhase Phase { get; private set; } = RoomPhase.Room;
    public RoomSettings Settings { get; private set; } = RoomSettings.Empty;
    public Pacing Pacing { get; private set; }
    public RelayRecording Recording { get; } = new();

    /// <summary>The next tick to close.</summary>
    public long NextTick { get; private set; }
    public bool Paused { get; private set; }
    public int SpeedPercent { get; private set; } = 100;
    public int Connections => _peers.Count;
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
    public void Attach(IConnection connection) => _peers.Add(new Peer(++_nextConnection, connection));

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
            while (peer.Connection.TryReceive(out var bytes))
            {
                if (!MessageCodec.TryDecode(bytes, out var message, out var error))
                {
                    Discard(peer, error);
                    continue;
                }
                Handle(peer, message!);
            }
        }
        for (var index = _peers.Count - 1; index >= 0; index--)
            if (!_peers[index].Connection.IsOpen) Drop(_peers[index], DropReason.ConnectionLost);
        AdvanceClock();
    }

    private void Handle(Peer peer, NetMessage message)
    {
        if (peer.Hello is null)
        {
            if (message is HelloMessage hello) Admit(peer, hello);
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
            case LoadedMessage loaded when Phase == RoomPhase.Loading && !peer.Loaded:
                peer.Loaded = true;
                peer.LoadedHash = loaded.SessionHash;
                CheckAllLoaded();
                break;
            case ChatRequestMessage chat:
                Broadcast(new ChatMessage(peer.Id, chat.Text));
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
                Paused = pace.Paused;
                SpeedPercent = pace.SpeedPercent;
                Broadcast(new PaceStateMessage(Paused, pace.SpeedPercent, peer.Id));
                break;
            case EndReportMessage report when Phase == RoomPhase.Match:
                peer.EndReport = (report.Tick, report.Winner);
                CheckEnd();
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
        if (hello.ProtocolVersion != Protocol.Version) reject = new RejectMessage(RejectReason.ProtocolVersion, $"The server speaks protocol {Protocol.Version}, the client {hello.ProtocolVersion}.");
        else if (Phase != RoomPhase.Room) reject = new RejectMessage(RejectReason.MatchRunning, "The match has started.");
        else if (_peers.Count(other => other.Hello is not null) >= Protocol.MaxConnections) reject = new RejectMessage(RejectReason.RoomFull, "The room is full.");
        else if (identity.Modified && !_options.AllowModifiedBuilds) reject = new RejectMessage(RejectReason.ModifiedBuild, "The client is a modified development build; this server admits released builds only.");
        else if (_roomHasIdentity)
        {
            // The first member defines what the room plays with; everybody after it must be the same in every respect, or the match is certain to diverge.
            if (FormatVersions.Of(hello) != _formats) reject = new RejectMessage(RejectReason.FormatVersion, "The client speaks other format versions than the room.");
            else if (!string.Equals(hello.SimulationId, _simulationId, StringComparison.Ordinal)) reject = new RejectMessage(RejectReason.SimulationVersion, $"The room plays simulation {_simulationId}, the client is {hello.SimulationId}.");
            else if (!string.Equals(hello.DefinitionFingerprint, _definitionFingerprint, StringComparison.OrdinalIgnoreCase)) reject = new RejectMessage(RejectReason.Definitions, "The client has other game definitions than the room.");
        }
        if (reject is not null)
        {
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
        }
        peer.Hello = hello;
        Send(peer, new WelcomeMessage(peer.Id, peer.Id == _host));
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
        foreach (var peer in members)
        {
            peer.Player = peer.Slot >= 0 ? playerOfSlot[peer.Slot] : 0;
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
        Phase = RoomPhase.Match;
        _lastMs = _clock.Milliseconds;
        _accumulatedMicroseconds = 0;
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
        var now = _clock.Milliseconds;
        var elapsed = now - _lastMs;
        _lastMs = now;
        if (Phase != RoomPhase.Match || Pacing != Pacing.Server || Paused || elapsed <= 0) return;
        _accumulatedMicroseconds += elapsed * 1000 * SpeedPercent / 100;
        // The server closes its ticks by its clock and waits for nobody.
        while (Phase == RoomPhase.Match && _accumulatedMicroseconds >= _tickMicroseconds && NextTick < Settings.MaxTicks)
        {
            _accumulatedMicroseconds -= _tickMicroseconds;
            CloseTick();
        }
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
        var bundle = new TickBundleMessage(tick, commands.ToImmutable());
        IntentsRelayed += bundle.Commands.Length;
        Recording.Bundles.Add(bundle);
        NextTick = tick + 1;
        // The urgent channel: a bundle must not wait behind a lost packet.
        var bytes = MessageCodec.Encode(bundle);
        foreach (var peer in _peers)
            if (peer.Hello is not null) peer.Connection.SendUrgent(bytes);
    }

    private void TakeHash(Peer peer, HashReportMessage report)
    {
        if (report.Tick < Recording.StartTick || report.Tick >= NextTick || report.Tick <= LastAgreedTick)
        {
            Discard(peer, $"a hash report for tick {report.Tick}");
            return;
        }
        HashReports++;
        if (!_hashReports.TryGetValue(report.Tick, out var reports)) _hashReports[report.Tick] = reports = [];
        reports[peer.Id] = report.SessionHash;
        var members = _peers.Where(other => other.Hello is not null).ToList();
        if (reports.Values.Distinct().Count() > 1)
        {
            ReportDesync(report.Tick, [..reports.Select(pair => (pair.Key, pair.Value))]);
            return;
        }
        if (members.Any(other => !reports.ContainsKey(other.Id))) return;
        _hashReports.Remove(report.Tick);
        if (report.Tick > LastAgreedTick) LastAgreedTick = report.Tick;
    }

    private void ReportDesync(long tick, ImmutableArray<(uint, ulong)> reports)
    {
        Desync = new DesyncMessage(tick, reports);
        Broadcast(Desync);
        Finish(new MatchEndMessage(tick, 0, EndReason.Desync));
    }

    private void CheckEnd()
    {
        var members = _peers.Where(peer => peer.Hello is not null).ToList();
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
        Broadcast(end);
    }

    private void Drop(Peer peer, DropReason reason)
    {
        _peers.Remove(peer);
        if (peer.Hello is null) return;
        Broadcast(new PlayerDroppedMessage(peer.Player, peer.Id, reason));
        var members = _peers.Where(other => other.Hello is not null).ToList();
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
