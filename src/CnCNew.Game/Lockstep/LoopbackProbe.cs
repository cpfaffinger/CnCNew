using System.Collections.Immutable;
using System.Net;
using CnCNew.Data.World;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Lockstep;

public enum ProbeTransport
{
    /// <summary>The loopback connections of <see cref="LoopbackNetwork"/>: whole messages, delay and jitter.</summary>
    Loopback,
    /// <summary>The UDP transport over a datagram network in memory: datagrams are delayed, lost, duplicated and reordered.</summary>
    Udp,
}

public enum ProbeDisturbanceKind
{
    /// <summary>The client says it leaves (<see cref="LockstepClient.Leave"/>).</summary>
    Leave,
    /// <summary>The line is cut: nothing arrives in either direction any more; the client's process goes on.</summary>
    Cut,
    /// <summary>The process stands still: it neither plays nor answers.</summary>
    Freeze,
    /// <summary>From here on the client plays at most one tick every <see cref="ProbeDisturbance.SlowEvery"/> rounds.</summary>
    Slow,
    /// <summary>The client plays nothing for <see cref="ProbeDisturbance.SlowEvery"/> rounds (a long frame, a garbage collection) and then goes on; its connection stays alive.</summary>
    Hitch,
    /// <summary>
    /// The connection is lost and, <see cref="ProbeDisturbance.SlowEvery"/> rounds later, the same client connects again with its token: it takes its held seat back, is
    /// handed the state of a member and plays what it missed (docs/NETWORK-PROTOCOL.md section 18.2).
    /// </summary>
    Reconnect,
    /// <summary>The test switch of the diagnosis: one object of this client's world takes a point of damage nobody else's takes (docs/NETWORK-DIAGNOSTICS.md).</summary>
    Corrupt,
    /// <summary>The next state this client hands over to a joiner is damaged on its way.</summary>
    DamageSnapshot,
}

/// <param name="Member">Which client, in slot order of the seats (watchers after them).</param>
/// <param name="AtTick">When that client has played this many ticks.</param>
public sealed record ProbeDisturbance(int Member, long AtTick, ProbeDisturbanceKind Kind, int SlowEvery = 4);

/// <param name="Ticks">How many ticks every client plays.</param>
/// <param name="ToServer">Loopback: delay and jitter of every client's way to the server; <paramref name="ToClient"/> of the way back.</param>
/// <param name="NetworkSeed">Seed of the network's jitter and loss (its own generator, never a stream of the simulation).</param>
/// <param name="ClockStepMs">How far the probe's clock moves per round, in milliseconds.</param>
/// <param name="GeneratorSeedSalt">Makes the command generators decide differently from run to run without touching the match.</param>
/// <param name="Transport">What carries the messages; with <see cref="ProbeTransport.Udp"/>, <paramref name="Impairment"/> applies to both directions.</param>
/// <param name="Watchers">Clients without a seat, in addition to one per human slot.</param>
/// <param name="Disturbances">What happens to which client during the match.</param>
/// <param name="Relay">The relay's options; seeds, modified builds and the hash interval are set by the probe.</param>
/// <param name="LateWatchers">Server ticks at which a further client without a seat connects to the running match.</param>
/// <param name="DiagnosticsDirectory">Where the clients write their packages when the server reports a desync (one directory per client below it).</param>
public sealed record LoopbackProbeOptions(long Ticks, LinkQuality ToServer = default, LinkQuality ToClient = default, uint NetworkSeed = 1,
    int ClockStepMs = 5, ushort HashInterval = 1, uint ServerSeed = 0x25C0FFEE, uint GeneratorSeedSalt = 0, ProbeTransport Transport = ProbeTransport.Loopback, Impairment Impairment = default,
    int Watchers = 0, ImmutableArray<ProbeDisturbance> Disturbances = default, RelayOptions? Relay = null, PlaybackOptions? Playback = null, UdpOptions? Udp = null,
    ImmutableArray<long> LateWatchers = default, string? DiagnosticsDirectory = null);

/// <param name="Dropped">The connections the relay dropped, with the reason and the tick the leave was put into.</param>
/// <param name="ThrottledMs">How long the relay ran the game slowed, in milliseconds of its clock.</param>
/// <param name="ClockMs">How long the match took on the probe's clock, from the first tick to the last.</param>
/// <param name="LateJoins">Members that joined the running match, joins refused, snapshots passed on and refused, and the size of the last snapshot (state, packed).</param>
/// <param name="DesyncTick">The tick of a desync the server reported (-1: none), the parts it named, and what the test switch changed.</param>
/// <param name="JoinedAt">For every client that joined the running match: its index and the tick its state began at.</param>
public sealed record LoopbackProbeResult(bool Ok, string Problem, int Clients, Pacing Pacing, long TicksPlayed, ulong FinalSessionHash, long LastAgreedTick, long IntentsRelayed, long BundleCommands,
    long HashReports, long Stalls, long ServerDiscarded, long ClientDiscarded, long MessagesSent, long BytesSent, long FirstDifferingTick, ulong ReplayedSessionHash, int ReplayedHumanCommands,
    long MaxIntentLatencyTicks, double MeanIntentLatencyTicks, long Rounds, ImmutableArray<(int Member, int Player, DropReason Reason)> Dropped = default, long ThrottledMs = 0, long ClockMs = 0,
    long Retransmissions = 0, long DatagramsLost = 0, long Resyncs = 0, double MeanPlaybackDelayMs = 0, long LateJoins = 0, long LateJoinsRefused = 0, long SnapshotsRelayed = 0,
    long SnapshotsRefused = 0, int SnapshotStateBytes = 0, int SnapshotPackedBytes = 0, long DesyncTick = -1, string DesyncParts = "", string Corrupted = "",
    ImmutableArray<(int Member, long Tick)> JoinedAt = default, ImmutableArray<string> ServerEvents = default, ImmutableArray<string> Packages = default);

/// <summary>
/// The multi-client probe (docs/NETWORK-PROTOCOL.md section 8): H clients (<see cref="HeadlessClient"/>), each with a world of its own, against one relay, in one process and
/// one thread, on a clock set by hand. The computer slots of the description run in every client's simulation as in any match. After every tick the session hashes of all
/// worlds are compared; at the end the relay's recording is played again and must end in the same session hash. The messages travel over the loopback connections or over
/// the UDP transport on a datagram network in memory - with loss, duplication and reordering - and a client can be made to leave, lose its line, freeze, fall behind, come
/// back to its held seat, or diverge; further clients can come to watch the running match.
/// </summary>
public sealed class LoopbackProbe
{
    private static readonly IPEndPoint ServerAddress = new(IPAddress.Parse("10.0.0.1"), 4000);
    private static readonly byte[] CookieSecret = [..Enumerable.Range(40, 32).Select(value => (byte)value)];

    private sealed class Member
    {
        public required HeadlessClient Client { get; set; }
        public UdpClientEndpoint? Endpoint { get; set; }
        public IPEndPoint? Address { get; set; }
        public bool Gone { get; set; }
        public bool Late { get; init; }
        public long ReconnectAtRound { get; set; } = -1;
        public HashSet<int> Done { get; } = [];
        /// <summary>The session hashes of the ticks this member played before it lost its connection (a member that came back begins a new list at the tick it joined at).</summary>
        public List<(long FirstTick, List<ulong> Hashes)> Earlier { get; } = [];

        public bool TryGetHash(long tick, out ulong hash)
        {
            var index = tick - Client.JoinedAtTick;
            if (Client.Session is not null && index >= 0 && index < Client.Hashes.Count)
            {
                hash = Client.Hashes[(int)index];
                return true;
            }
            foreach (var (first, hashes) in Earlier)
                if (tick >= first && tick - first < hashes.Count)
                {
                    hash = hashes[(int)(tick - first)];
                    return true;
                }
            hash = 0;
            return false;
        }
    }

    private readonly TemplateCatalog _catalog;
    private readonly string _fingerprint;
    private readonly TerrainMap _terrain;
    private readonly ImmutableArray<MapObject> _mapObjects;

    public LoopbackProbe(TemplateCatalog catalog, string definitionFingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects)
    {
        _catalog = catalog;
        _fingerprint = definitionFingerprint;
        _terrain = terrain;
        _mapObjects = mapObjects;
    }

    /// <summary>A hook for tests: called once every client has built its match, with each client's session, to disturb one of them.</summary>
    public Action<IReadOnlyList<SkirmishMatchSession>>? AfterStart { get; set; }

    /// <summary>
    /// Plays the match of <paramref name="settings"/>: every <see cref="MatchSlotKind.Human"/> slot gets a client with a command generator, in slot order. The room is
    /// client-paced with one client and server-paced with more.
    /// </summary>
    public LoopbackProbeResult Run(RoomSettings settings, LoopbackProbeOptions options)
    {
        var seats = Enumerable.Range(0, settings.Slots.Length).Where(slot => settings.Slots[slot].Kind == MatchSlotKind.Human).ToArray();
        if (seats.Length == 0) throw new ArgumentException("The probe needs at least one human slot.", nameof(settings));
        var count = seats.Length + options.Watchers;
        var clock = new ManualNetClock();
        var loopback = new LoopbackNetwork(clock, options.NetworkSeed);
        var datagrams = new MemoryDatagramNetwork(clock, options.NetworkSeed) { Default = options.Impairment };
        var udp = options.Transport == ProbeTransport.Udp;
        var udpServer = udp ? new UdpServerEndpoint(datagrams.Open(ServerAddress), clock, CookieSecret, options.Udp) : null;
        var udpLinks = new List<UdpLink>();
        var seed = options.ServerSeed;
        var server = new RelayServer(clock, (options.Relay ?? new RelayOptions()) with { AllowModifiedBuilds = true, HashInterval = options.HashInterval, SeedSource = () => seed = unchecked(seed * 1664525u + 1013904223u) });
        var map = new MatchMap(_terrain, _mapObjects);
        var members = new List<Member>();
        var built = new List<SkirmishMatchSession>();
        var serverPaced = count > 1;
        var connections = 0;

        // A connection and the client on it: at the start, for a client that comes back, for somebody who comes to watch.
        (HeadlessClient Client, UdpClientEndpoint? Endpoint, IPEndPoint? Address) Connect(string name, ulong token = 0, bool watch = false)
        {
            IConnection connection;
            UdpClientEndpoint? endpoint = null;
            IPEndPoint? address = null;
            var number = connections++;
            if (udp)
            {
                address = new IPEndPoint(IPAddress.Parse("10.0.1.1"), 5000 + number);
                endpoint = new UdpClientEndpoint(datagrams.Open(address), ServerAddress, clock, 0xC11E0000UL + (ulong)number, options.Udp);
                connection = endpoint.Connection;
            }
            else
            {
                var (clientEnd, serverEnd) = loopback.Connect(options.ToServer, options.ToClient);
                server.Attach(serverEnd);
                connection = clientEnd;
            }
            // With more than one client the room is server-paced: every client synchronises with the probe's clock, as it would with its own over a network.
            var client = new HeadlessClient(connection, name, _catalog, _fingerprint, _ => map, clock: serverPaced ? clock : null, playback: options.Playback, generatorSalt: options.GeneratorSeedSalt,
                rejoinToken: token, watch: watch);
            if (!serverPaced) client.PumpRelay = server.Pump;
            if (options.DiagnosticsDirectory is { } directory) client.DiagnosticsDirectory = System.IO.Path.Combine(directory, name.Replace(' ', '-'));
            return (client, endpoint, address);
        }

        for (var index = 0; index < count; index++)
        {
            var (client, endpoint, address) = Connect($"client {index + 1}");
            // The world may be disturbed only once every client has built its own: the hook is called with all of them.
            client.AfterBuild = built.Add;
            client.HoldLoaded = true;
            members.Add(new Member { Client = client, Endpoint = endpoint, Address = address });
        }

        var rounds = 0L;
        var tickSeconds = 1.0 / settings.TickRate;
        var target = Math.Min(options.Ticks, settings.MaxTicks);
        var hold = true;
        var events = new List<string>();

        // One round: the clock moves, datagrams arrive, the relay works, every client works, datagrams leave.
        bool Round(long playTo)
        {
            rounds++;
            clock.Advance(options.ClockStepMs);
            if (udp)
            {
                udpServer!.Pump();
                while (udpServer.TryAccept(out var link, out _))
                {
                    udpLinks.Add(link);
                    server.Attach(link);
                }
                foreach (var member in members)
                    if (!member.Client.Frozen) member.Endpoint!.Pump();
            }
            server.Pump();
            while (server.TryTakeEvent(out var relayEvent)) events.Add(relayEvent.Name);
            var progressed = false;
            foreach (var member in members)
            {
                if (member.Gone && !member.Client.Client.IsOpen) continue;
                progressed |= member.Client.Update(hold ? 0 : serverPaced ? options.ClockStepMs / 1000.0 : tickSeconds, hold ? 0 : playTo) > 0;
            }
            if (udp)
            {
                foreach (var member in members)
                    if (!member.Client.Frozen) member.Endpoint!.Flush();
                udpServer!.Flush();
            }
            return progressed;
        }

        string? Wait(Func<bool> done, string what)
        {
            for (var guard = 0; guard < 40_000; guard++)
            {
                Round(0);
                if (done()) return null;
                if (members.FirstOrDefault(member => member.Client.Client.Rejection is not null) is { } rejected) return $"rejected: {rejected.Client.Client.Rejection!.Text}";
                if (members.FirstOrDefault(member => member.Client.Client.LastRefusal is not null) is { } refused) return $"refused while trying to {what}: {refused.Client.Client.LastRefusal!.Text}";
                if (members.FirstOrDefault(member => member.Client.Problem.Length > 0) is { } broken) return broken.Client.Problem;
            }
            return $"the room did not {what} (server discarded {server.Discarded}: {server.LastDiscard})";
        }

        var dropped = ImmutableArray.CreateBuilder<(int Member, int Player, DropReason Reason)>();
        var joinedAt = ImmutableArray.CreateBuilder<(int Member, long Tick)>();
        (int State, int Packed) snapshot = (0, 0);
        long Retransmitted() => udpLinks.Sum(link => link.Statistics.SegmentsRetransmitted) + members.Sum(member => member.Endpoint?.Link.Statistics.SegmentsRetransmitted ?? 0);
        LoopbackProbeResult Fail(string problem, long ticks = 0, long differing = -1)
            => new(false, problem, members.Count, server.Pacing, ticks, 0, server.LastAgreedTick, server.IntentsRelayed, server.Recording.Bundles.Sum(bundle => (long)bundle.Commands.Length), server.HashReports,
                members.Sum(member => member.Client.Driver?.Stalls ?? 0), server.Discarded, members.Sum(member => member.Client.Client.Discarded), udp ? datagrams.Datagrams : loopback.MessagesSent,
                udp ? datagrams.Bytes : loopback.BytesSent, differing, 0, 0, 0, 0, rounds, dropped.ToImmutable(), server.ThrottledMicros / 1000, 0, Retransmitted(), datagrams.Lost, 0, 0, server.LateJoins,
                server.LateJoinsRefused, server.SnapshotsRelayed, server.SnapshotsRefused, snapshot.State, snapshot.Packed, server.Desync?.Tick ?? -1,
                server.DesyncParts is { } named ? string.Join("; ", named.Parts.GroupBy(part => part.Name).Select(group => group.Key)) : string.Empty,
                string.Join("; ", members.Select(member => member.Client.Corrupted).Where(text => text.Length > 0)), joinedAt.ToImmutable(), [..events],
                [..members.Select(member => member.Client.PackageWritten).Where(path => path is not null).Select(path => path!)]);

        if (Wait(() => members.All(member => member.Client.Client.Phase == ClientPhase.InRoom), "admit the clients") is { } admit) return Fail(admit);
        var host = members.First(member => member.Client.Client.IsHost).Client.Client;
        host.Configure(settings);
        if (Wait(() => members.All(member => member.Client.Client.Room is { } room && room.Settings.Slots.Length == settings.Slots.Length), "set the room") is { } set) return Fail(set);
        for (var index = 0; index < seats.Length; index++)
        {
            var seat = settings.Slots[seats[index]];
            members[index].Client.Client.RequestSlot(seats[index], seat.Faction, seat.Team, seat.Color, seat.StartPosition);
            members[index].Client.Client.SetReady(true);
        }
        if (Wait(() => host.Room!.Members.Count(member => member.Slot >= 0 && (member.Ready || member.IsHost)) == seats.Length, "seat the clients") is { } seated) return Fail(seated);
        host.RequestStart();
        // Every client builds its match and reports it; the relay starts the match when all have.
        var disturbed = false;
        if (Wait(() =>
            {
                if (!disturbed && built.Count == members.Count)
                {
                    disturbed = true;
                    AfterStart?.Invoke(built);
                    foreach (var member in members) member.Client.ReportLoaded();
                }
                return members.All(member => member.Client.Client.Phase is ClientPhase.InMatch or ClientPhase.Ended);
            }, "begin the match") is { } begun) return Fail(begun);
        if (members.Any(member => member.Client.Client.Phase == ClientPhase.Ended)) return Fail("the states after loading differ: the server ended the match before it began", 0, 0);
        hold = false;

        var startMs = clock.Milliseconds;
        var compared = 0L;
        var idle = 0;
        var disturbances = options.Disturbances.IsDefault ? [] : options.Disturbances;
        var lateWatchers = new Queue<long>((options.LateWatchers.IsDefault ? [] : options.LateWatchers).Order());
        long delaySum = 0, delayCount = 0, mismatch = -1, mismatchRounds = 0;
        bool Playing(Member member) => !member.Gone;
        while (members.Any(member => Playing(member) && member.Client.Tick < target))
        {
            var progressed = Round(target);
            for (var number = 0; number < disturbances.Length; number++)
            {
                var disturbance = disturbances[number];
                var member = members[disturbance.Member];
                if (member.Done.Contains(number) || member.Gone || member.Client.Tick < disturbance.AtTick) continue;
                member.Done.Add(number);
                switch (disturbance.Kind)
                {
                    case ProbeDisturbanceKind.Leave:
                        member.Client.Client.Leave();
                        member.Gone = true;
                        break;
                    case ProbeDisturbanceKind.Cut:
                        if (udp) datagrams.Cut(member.Address!, ServerAddress);
                        else member.Client.Client.Close();
                        member.Gone = true;
                        break;
                    case ProbeDisturbanceKind.Freeze:
                        member.Client.Frozen = true;
                        member.Gone = true;
                        break;
                    case ProbeDisturbanceKind.Slow:
                        member.Client.SlowEvery = disturbance.SlowEvery;
                        break;
                    case ProbeDisturbanceKind.Hitch:
                        member.Client.SkipUpdates = disturbance.SlowEvery;
                        break;
                    case ProbeDisturbanceKind.Reconnect:
                        // The line is gone; the process is still there and will connect again.
                        if (udp) datagrams.Cut(member.Address!, ServerAddress);
                        else member.Client.Client.Close();
                        member.Client.Frozen = true;
                        member.Gone = true;
                        member.ReconnectAtRound = rounds + disturbance.SlowEvery;
                        break;
                    case ProbeDisturbanceKind.Corrupt:
                        member.Client.CorruptAtTick = member.Client.Tick + 1;
                        break;
                    case ProbeDisturbanceKind.DamageSnapshot:
                        member.Client.DamageNextSnapshot = true;
                        break;
                }
            }
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (member.ReconnectAtRound < 0 || rounds < member.ReconnectAtRound) continue;
                member.ReconnectAtRound = -1;
                var old = member.Client;
                member.Earlier.Add((old.JoinedAtTick, old.Hashes));
                var (client, endpoint, address) = Connect($"client {index + 1} again", old.Client.RejoinToken);
                member.Client = client;
                member.Endpoint = endpoint;
                member.Address = address;
                member.Gone = false;
            }
            while (lateWatchers.Count > 0 && server.NextTick >= lateWatchers.Peek())
            {
                lateWatchers.Dequeue();
                var (client, endpoint, address) = Connect($"watcher {members.Count + 1}", watch: true);
                members.Add(new Member { Client = client, Endpoint = endpoint, Address = address, Late = true });
            }
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (member.Client.LastSnapshot.StateBytes > 0) snapshot = (member.Client.LastSnapshot.StateBytes, member.Client.LastSnapshot.PackedBytes);
                if (member.Client.Session is not null && member.Client.JoinedAtTick > 0 && !joinedAt.Any(entry => entry.Member == index && entry.Tick == member.Client.JoinedAtTick))
                    joinedAt.Add((index, member.Client.JoinedAtTick));
                // A client the relay turned away or dropped plays no further: it has no bundles to play.
                if (!member.Gone && (member.Client.Client.Phase is ClientPhase.Ended or ClientPhase.Rejected || !member.Client.Client.IsOpen) && member.Client.Tick < target) member.Gone = true;
                if (member.Client.Problem.Length > 0 && !member.Gone)
                {
                    // A joiner that could not build the state it was handed is turned away by the relay; that is an outcome, not a failure of the probe.
                    if (member.Client.JoinedAtTick == 0 && member.Client.Session is null && (member.Late || member.Earlier.Count > 0)) member.Gone = true;
                    else return Fail($"client {index + 1}: {member.Client.Problem}", compared);
                }
                if (Playing(member) && member.Client.Driver is { Playback.Synced: true } driver)
                {
                    delaySum += driver.Playback.DelayMicros;
                    delayCount++;
                }
            }
            // Every tick is compared across the clients that played it, in addition to the server's own comparison of the reports: a client that left was in step until it
            // left, and one that came back is in step from the tick it joined at.
            for (var limit = Math.Min(target, server.NextTick); compared < limit; compared++)
            {
                // Wait for everybody who is still going to play this tick.
                if (members.Any(member => Playing(member) && !member.TryGetHash(compared, out _) && member.Client.Tick <= compared)) break;
                ulong? reference = null;
                foreach (var member in members)
                {
                    if (!member.TryGetHash(compared, out var hash)) continue;
                    reference ??= hash;
                    if (hash != reference) mismatch = compared;
                }
                if (mismatch >= 0) break;
            }
            if (server.Phase == RoomPhase.Ended && server.Desync is { } desync)
            {
                // The clients send the parts of their hashes; the relay names those they differ in.
                for (var round = 0; round < 400 && server.DesyncParts is null; round++) Round(target);
                return Fail($"the server reported a desync at tick {desync.Tick}", compared, desync.Tick);
            }
            // The clients differ and the relay has not said so within ten seconds of the clock: the probe says it.
            if (mismatch >= 0 && ++mismatchRounds > 10_000 / options.ClockStepMs) return Fail($"the session hashes of the clients differ after tick {mismatch}, and the server did not report it", mismatch, mismatch);
            if (server.Phase == RoomPhase.Ended && members.Any(member => Playing(member) && !member.Client.Client.IsOpen)) return Fail($"the server ended the match at tick {server.End?.Tick} ({server.End?.Reason})", compared);
            idle = progressed ? 0 : idle + 1;
            if (idle > 200_000)
                return Fail($"the match stalled at tick {members.Where(Playing).Select(member => member.Client.Tick).DefaultIfEmpty(-1).Min()} (server tick {server.NextTick}, discarded {server.Discarded}: {server.LastDiscard})", compared);
        }
        var clockMs = clock.Milliseconds - startMs;
        if (!members.Any(Playing)) return Fail("no client played to the end", compared);
        // The last hash reports travel to the server.
        var lastReported = target / options.HashInterval * options.HashInterval - 1;
        for (var round = 0; round < 40_000 && server.LastAgreedTick < lastReported && server.Phase == RoomPhase.Match; round++) Round(target);
        foreach (var notice in members.First(member => Playing(member) && !member.Late).Client.Client.Dropped)
            dropped.Add((members.FindIndex(member => member.Client.Client.ConnectionId == notice.ConnectionId), notice.Player, notice.Reason));
        if (server.Desync is { } late) return Fail($"the server reported a desync at tick {late.Tick}", compared, late.Tick);
        var clientDiscarded = members.Where(Playing).Sum(member => member.Client.Client.Discarded);
        if (server.Discarded > 0 || clientDiscarded > 0)
            return Fail($"messages were discarded (server {server.Discarded}: {server.LastDiscard}; clients {clientDiscarded}: {members.Where(Playing).Select(member => member.Client.Client.LastDiscard).FirstOrDefault(text => text.Length > 0)})", compared);
        if (server.LastAgreedTick < lastReported) return Fail($"the server agreed on the hashes only up to tick {server.LastAgreedTick} of {lastReported}", compared);

        // The relay's recording as a replay: the humans' commands from the bundles - a leave among them is one -, the computer players decide again.
        var first = members.First(Playing).Client.Session!;
        var played = SkirmishReplay.Play(ReplayFormat.Read(RecordingFile.ToReplay(server.Recording.Description, server.Recording.Bundles, target)), _catalog, _fingerprint, _terrain, _mapObjects, audit: false);
        var final = first.StateHash();
        var latencies = members.SelectMany(member => member.Client.LatencyTicks).ToList();
        var result = Fail(string.Empty, target) with
        {
            Ok = true, FinalSessionHash = final, ReplayedSessionHash = played.FinalSessionHash, ReplayedHumanCommands = played.HumanCommands,
            BundleCommands = server.Recording.Bundles.Where(bundle => bundle.Tick < target).Sum(bundle => (long)bundle.Commands.Length), ClientDiscarded = clientDiscarded,
            MaxIntentLatencyTicks = latencies.Count == 0 ? 0 : latencies.Max(), MeanIntentLatencyTicks = latencies.Count == 0 ? 0 : latencies.Average(), ClockMs = clockMs,
            Resyncs = members.Sum(member => member.Client.Driver?.Playback.Resyncs ?? 0), MeanPlaybackDelayMs = delayCount == 0 ? 0 : delaySum / 1000.0 / delayCount, FirstDifferingTick = -1,
        };
        if (played.FinalSessionHash != final) return result with { Ok = false, Problem = $"the server's recording played as a replay ends in session hash {played.FinalSessionHash:X16}, the clients in {final:X16}" };
        if (members.Where(Playing).Any(member => member.Client.Session!.StateHash() != final)) return result with { Ok = false, Problem = "the clients end in different session hashes" };
        return result;
    }
}
