using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Lockstep;

/// <summary>The map of a match as a client needs it to build its world.</summary>
public sealed record MatchMap(TerrainMap Terrain, ImmutableArray<MapObject> Objects);

/// <summary>
/// A client without a window (docs/NETWORK-PROTOCOL.md section 8): the protocol client, its own world, the lockstep driver and the command path - everything a playing
/// client has between the connection and the simulation. Its "human" is a command generator: a <see cref="SkirmishAi"/> that decides on this client only and sends what it
/// orders as intents through the relay, which loads the real command path; a client without a seat watches. Whoever hosts it - the multi-client probe in one process, a test,
/// <c>CnCNew.NetTool client</c> as a process of its own - calls <see cref="Update"/> in its loop and reads what it measured.
/// </summary>
public sealed class HeadlessClient
{
    private readonly TemplateCatalog _catalog;
    private readonly string _fingerprint;
    private readonly Func<string, MatchMap> _loadMap;
    private readonly uint _generatorSalt;
    private readonly Dictionary<long, (long Tick, long Micros)> _issued = [];
    private SkirmishAi? _generator;
    private long _preparedFor = -1;
    private long _slowCounter;

    /// <param name="loadMap">Hands out the map of a path; a client learns the path from the match description.</param>
    /// <param name="clock">The local clock: clock synchronisation and the latency in real time. Null in a client-paced room (the local game).</param>
    public HeadlessClient(IConnection connection, string name, TemplateCatalog catalog, string definitionFingerprint, Func<string, MatchMap> loadMap, string room = "", INetClock? clock = null,
        PlaybackOptions? playback = null, uint generatorSalt = 0, BuildIdentity? identity = null, ulong rejoinToken = 0, bool watch = false)
    {
        _catalog = catalog;
        _fingerprint = definitionFingerprint;
        _loadMap = loadMap;
        _generatorSalt = generatorSalt;
        Clock = clock;
        Client = new LockstepClient(connection, name, definitionFingerprint, identity, room, clock, playback, rejoinToken, watch);
        Client.SnapshotRequested = request => _snapshotRequest = request;
        Client.DesyncReceived = OnDesync;
    }

    private uint _snapshotRequest;

    /// <summary>What this client remembers of its session for the diagnosis of a desync; null: nothing (the hash of a tick is then all it can say).</summary>
    public SessionDiagnostics? Diagnostics { get; set; } = new();

    /// <summary>Where this client writes its package when the server reports a desync (docs/NETWORK-DIAGNOSTICS.md); null: nowhere.</summary>
    public string? DiagnosticsDirectory { get; set; }
    public string? PackageWritten { get; private set; }

    /// <summary>The test switch of the diagnosis: before this tick one object of this client's world takes a point of damage nobody else's takes (-1: never).</summary>
    public long CorruptAtTick { get; set; } = -1;
    public string Corrupted { get; private set; } = string.Empty;

    /// <summary>The test switch of the rejoin: the next state this client hands over is damaged on its way (one byte of the packed state).</summary>
    public bool DamageNextSnapshot { get; set; }

    /// <summary>Snapshots this client handed over, and what the last one cost: bytes of the state, bytes packed, microseconds to write and pack it.</summary>
    public int SnapshotsOffered { get; private set; }
    public (int StateBytes, int PackedBytes, long Micros) LastSnapshot { get; private set; }

    /// <summary>The tick this client's session began at: 0, or the tick of the state it joined a running match with.</summary>
    public long JoinedAtTick { get; private set; }

    private void OnDesync(DesyncMessage desync)
    {
        if (Session is null) return;
        Client.SendDesyncDetail(desync.Tick, Diagnostics is not null && Diagnostics.TryGetParts(desync.Tick, out var parts) ? parts : []);
        if (Diagnostics is null || DiagnosticsDirectory is null) return;
        try
        {
            Diagnostics.WritePackage(DiagnosticsDirectory, Session, Client, desync);
            PackageWritten = DiagnosticsDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Problem = $"the diagnostics package could not be written: {exception.Message}";
        }
    }

    private void OfferSnapshot()
    {
        var request = _snapshotRequest;
        _snapshotRequest = 0;
        var session = Session!;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var state = session.Save();
        var packed = SessionTransfer.Pack(state);
        LastSnapshot = (state.Length, packed.Length, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        SnapshotsOffered++;
        if (DamageNextSnapshot)
        {
            DamageNextSnapshot = false;
            packed[packed.Length / 2] ^= 0x5A;
        }
        // Between two ticks: the state after the last step, with nothing of the next bundle in it yet.
        Client.OfferSnapshot(request, new ResumeState(session.World.Tick, session.StateHash(), session.HumanCommandCounts, packed));
    }


    public LockstepClient Client { get; }
    public INetClock? Clock { get; }
    public SkirmishMatchSession? Session { get; private set; }
    public LockstepDriver? Driver { get; private set; }
    public CommandPath? Path { get; private set; }

    /// <summary>Client-paced rooms: moves the relay inside a request for a tick (the embedded server); null with a remote relay.</summary>
    public Action? PumpRelay { get; set; }

    /// <summary>Called once the match is built, before its hash is reported.</summary>
    public Action<SkirmishMatchSession>? AfterBuild { get; set; }

    /// <summary>Whether the built match is reported to the relay only when the host says so (<see cref="ReportLoaded"/>) instead of at once.</summary>
    public bool HoldLoaded { get; set; }
    private bool _loadedReported;

    public void ReportLoaded()
    {
        if (_loadedReported || Session is null) return;
        _loadedReported = true;
        Client.ReportLoaded(Session.StateHash());
    }

    /// <summary>Whether the session hash of every tick is kept (<see cref="Hashes"/>) or only chained (<see cref="HashChain"/>).</summary>
    public bool KeepHashes { get; set; } = true;
    public List<ulong> Hashes { get; } = [];

    /// <summary>All session hashes so far folded into one value: two clients that played the same ticks to the same states agree in it.</summary>
    public ulong HashChain { get; private set; } = 0xCBF29CE484222325UL;
    public long TicksPlayed { get; private set; }

    /// <summary>Plays at most one tick every this many updates (1: no limit): a client that does not keep up with the tick rate.</summary>
    public int SlowEvery { get; set; } = 1;

    /// <summary>Plays nothing for this many updates, then goes on: a long frame. The connection is served meanwhile.</summary>
    public int SkipUpdates { get; set; }

    /// <summary>Stops playing and pumping: a frozen process.</summary>
    public bool Frozen { get; set; }

    /// <summary>Order-to-execution of this client's own orders: in ticks, and in microseconds of the local clock (empty without one).</summary>
    public List<int> LatencyTicks { get; } = [];
    public List<long> LatencyMicros { get; } = [];

    /// <summary>The first thing that went wrong on this client, if anything.</summary>
    public string Problem { get; private set; } = string.Empty;

    public long Tick => Session?.World.Tick ?? Client.StartTick;

    private void Build()
    {
        var description = Client.Description!;
        MatchMap map;
        try
        {
            map = _loadMap(description.MapPath);
            // A running match is joined at the state a member handed over (or a saved match is continued); otherwise the match begins.
            Session = Client.Start!.Resume is { } resume
                ? SkirmishMatchSession.Restore(SessionTransfer.Unpack(resume.State), _catalog, _fingerprint, map.Terrain)
                : SkirmishMatchSession.Start(description, _catalog, map.Terrain, map.Objects, 0, _fingerprint, Client.Identity.Id);
            JoinedAtTick = Session.World.Tick;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or CnCNew.Simulation.Snapshots.SnapshotFormatException)
        {
            Problem = $"the match cannot be built here: {exception.Message}";
            // A state that was handed over and cannot be read is told to the relay as a state that does not match: it turns this client away and holds the seat again.
            if (Client.Start!.Resume is not null) Client.ReportLoaded(0);
            return;
        }
        var session = Session;
        Action pump = PumpRelay is { } relay ? () => { relay(); Client.Pump(); } : Client.Pump;
        Driver = new LockstepDriver(Client, 1.0 / description.TickRate, pump, session.StateHash, description.MaxTicks - 1);
        var path = Path = new CommandPath(session.World, session.Issue, Driver);
        path.PreChecked += (_, receipt) => _issued[receipt.Number] = (session.World.Tick, Clock?.Microseconds ?? 0);
        path.Resolved += resolution =>
        {
            if (resolution.Receipt == 0 || !_issued.Remove(resolution.Receipt, out var at)) return;
            LatencyTicks.Add((int)(resolution.Command.Tick - at.Tick));
            if (Clock is not null) LatencyMicros.Add(Clock.Microseconds - at.Micros);
        };
        if (Client.Player > 0)
        {
            var slot = session.Match.Slots.First(resolved => resolved.Player.Value == Client.Player);
            var options = SkirmishMatchSession.AiOptionsOf(session.Match, slot, map.Terrain);
            // Another seed than the slot's computer player would have: the generator is this client's "human", not a participant of the description.
            options = options with { Seed = unchecked(options.Seed ^ 0x9E3779B9u ^ _generatorSalt) };
            var player = slot.Player;
            _generator = new SkirmishAi(session.World, _catalog, options, command => path.Issue(player, (sequence, tick) => command with { Sequence = sequence, Tick = tick }));
        }
        AfterBuild?.Invoke(session);
        if (!HoldLoaded) ReportLoaded();
    }

    /// <summary>
    /// One round of this client: takes in what arrived, builds the match when its description is there, and plays every tick that is due up to
    /// <paramref name="targetTick"/> (at most <paramref name="maxTicks"/> of them: the limit of a frame). Returns how many ticks it played.
    /// </summary>
    /// <param name="seconds">The time since the last update, for a client-paced room.</param>
    public int Update(double seconds, long targetTick, int maxTicks = int.MaxValue)
    {
        if (Frozen || Problem.Length > 0) return 0;
        Client.Pump();
        if (Session is null)
        {
            if (Client.Phase == ClientPhase.Loading && Client.Description is not null) Build();
            return 0;
        }
        var session = Session;
        var driver = Driver!;
        if (_snapshotRequest != 0 && Client.Phase == ClientPhase.InMatch) OfferSnapshot();
        driver.AddTime(seconds);
        if (SlowEvery > 1 && ++_slowCounter % SlowEvery != 0) return 0;
        if (SkipUpdates > 0)
        {
            SkipUpdates--;
            return 0;
        }
        var played = 0;
        var limit = SlowEvery > 1 ? 1 : maxTicks;
        while (played < limit && session.World.Tick < targetTick && driver.IsDue(session.World.Tick))
        {
            var tick = session.World.Tick;
            // The generator decides once per tick, before the tick is closed - where the HUD's per-tick bookkeeping stands in a game.
            if (_preparedFor != tick)
            {
                _preparedFor = tick;
                _generator?.Update();
            }
            if (!driver.TryReleaseTick(tick, Path!.CanStep)) break;
            if (tick == CorruptAtTick)
            {
                var victim = session.World.Entities.Where(entity => entity.Body is not null && !entity.EffectivelyDead && !entity.DestroyRequested).OrderBy(entity => entity.Id.Value).First();
                session.World.Damage(victim.Id, new CnCNew.Simulation.World.DamageInfo(CnCNew.Simulation.Ids.EntityId.Invalid, CnCNew.Simulation.Numerics.Fixed.One,
                    CnCNew.Simulation.World.DamageTypes.Unresistable, CnCNew.Simulation.World.DeathTypes.Normal, false));
                Corrupted = FormattableString.Invariant($"object {victim.Id.Value} ({victim.Template.Name}) at tick {tick}");
            }
            try
            {
                Path.SubmitDue();
            }
            catch (ProtocolException exception)
            {
                Problem = $"the bundle of tick {tick} violates the protocol: {exception.Message}";
                return played;
            }
            session.Step();
            driver.TickCompleted(tick);
            var hash = session.StateHash();
            Diagnostics?.Record(tick, session, hash);
            if (KeepHashes) Hashes.Add(hash);
            HashChain = unchecked((HashChain ^ hash) * 0x100000001B3UL);
            TicksPlayed++;
            played++;
        }
        return played;
    }
}
