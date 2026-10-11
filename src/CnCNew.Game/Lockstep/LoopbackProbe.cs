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
public sealed record LoopbackProbeOptions(long Ticks, LinkQuality ToServer = default, LinkQuality ToClient = default, uint NetworkSeed = 1,
    int ClockStepMs = 5, ushort HashInterval = 1, uint ServerSeed = 0x25C0FFEE, uint GeneratorSeedSalt = 0, ProbeTransport Transport = ProbeTransport.Loopback, Impairment Impairment = default,
    int Watchers = 0, ImmutableArray<ProbeDisturbance> Disturbances = default, RelayOptions? Relay = null, PlaybackOptions? Playback = null, UdpOptions? Udp = null);

/// <param name="Dropped">The connections the relay dropped, with the reason and the tick the leave was put into.</param>
/// <param name="ThrottledMs">How long the relay ran the game slowed, in milliseconds of its clock.</param>
/// <param name="ClockMs">How long the match took on the probe's clock, from the first tick to the last.</param>
public sealed record LoopbackProbeResult(bool Ok, string Problem, int Clients, Pacing Pacing, long TicksPlayed, ulong FinalSessionHash, long LastAgreedTick, long IntentsRelayed, long BundleCommands,
    long HashReports, long Stalls, long ServerDiscarded, long ClientDiscarded, long MessagesSent, long BytesSent, long FirstDifferingTick, ulong ReplayedSessionHash, int ReplayedHumanCommands,
    long MaxIntentLatencyTicks, double MeanIntentLatencyTicks, long Rounds, ImmutableArray<(int Member, int Player, DropReason Reason)> Dropped = default, long ThrottledMs = 0, long ClockMs = 0,
    long Retransmissions = 0, long DatagramsLost = 0, long Resyncs = 0, double MeanPlaybackDelayMs = 0);

/// <summary>
/// The multi-client probe (docs/NETWORK-PROTOCOL.md section 8): H clients (<see cref="HeadlessClient"/>), each with a world of its own, against one relay, in one process and
/// one thread, on a clock set by hand. The computer slots of the description run in every client's simulation as in any match. After every tick the session hashes of all
/// worlds are compared; at the end the relay's recording is played again and must end in the same session hash. The messages travel over the loopback connections or over
/// the UDP transport on a datagram network in memory - with loss, duplication and reordering - and a client can be made to leave, lose its line, freeze or fall behind.
/// </summary>
public sealed class LoopbackProbe
{
    private static readonly IPEndPoint ServerAddress = new(IPAddress.Parse("10.0.0.1"), 4000);
    private static readonly byte[] CookieSecret = [..Enumerable.Range(40, 32).Select(value => (byte)value)];

    private sealed class Member
    {
        public required HeadlessClient Client { get; init; }
        public UdpClientEndpoint? Endpoint { get; init; }
        public IPEndPoint? Address { get; init; }
        public bool Gone { get; set; }
        public bool Disturbed { get; set; }
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
        for (var index = 0; index < count; index++)
        {
            IConnection connection;
            UdpClientEndpoint? endpoint = null;
            IPEndPoint? address = null;
            if (udp)
            {
                address = new IPEndPoint(IPAddress.Parse("10.0.1.1"), 5000 + index);
                endpoint = new UdpClientEndpoint(datagrams.Open(address), ServerAddress, clock, 0xC11E0000UL + (ulong)index, options.Udp);
                connection = endpoint.Connection;
            }
            else
            {
                var (clientEnd, serverEnd) = loopback.Connect(options.ToServer, options.ToClient);
                server.Attach(serverEnd);
                connection = clientEnd;
            }
            // With more than one client the room is server-paced: every client synchronises with the probe's clock, as it would with its own over a network.
            var client = new HeadlessClient(connection, $"client {index + 1}", _catalog, _fingerprint, _ => map, clock: serverPaced ? clock : null, playback: options.Playback, generatorSalt: options.GeneratorSeedSalt);
            if (!serverPaced) client.PumpRelay = server.Pump;
            // The world may be disturbed only once every client has built its own: the hook is called with all of them.
            client.AfterBuild = built.Add;
            client.HoldLoaded = true;
            members.Add(new Member { Client = client, Endpoint = endpoint, Address = address });
        }

        var rounds = 0L;
        var tickSeconds = 1.0 / settings.TickRate;
        var target = Math.Min(options.Ticks, settings.MaxTicks);
        var hold = true;

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
        long Retransmitted() => udpLinks.Sum(link => link.Statistics.SegmentsRetransmitted) + members.Sum(member => member.Endpoint?.Link.Statistics.SegmentsRetransmitted ?? 0);
        LoopbackProbeResult Fail(string problem, long ticks = 0, long differing = -1)
            => new(false, problem, members.Count, server.Pacing, ticks, 0, server.LastAgreedTick, server.IntentsRelayed, server.Recording.Bundles.Sum(bundle => (long)bundle.Commands.Length), server.HashReports,
                members.Sum(member => member.Client.Driver?.Stalls ?? 0), server.Discarded, members.Sum(member => member.Client.Client.Discarded), udp ? datagrams.Datagrams : loopback.MessagesSent,
                udp ? datagrams.Bytes : loopback.BytesSent, differing, 0, 0, 0, 0, rounds, dropped.ToImmutable(), server.ThrottledMicros / 1000, 0, Retransmitted(), datagrams.Lost);

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
        long delaySum = 0, delayCount = 0;
        bool Playing(Member member) => !member.Gone;
        while (members.Any(member => Playing(member) && member.Client.Tick < target))
        {
            var progressed = Round(target);
            foreach (var disturbance in disturbances)
            {
                var member = members[disturbance.Member];
                if (member.Disturbed || member.Client.Tick < disturbance.AtTick) continue;
                member.Disturbed = true;
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
                }
            }
            foreach (var member in members)
            {
                // A client the relay dropped plays no further: it has no bundles to play.
                if (!member.Gone && (member.Client.Client.Phase == ClientPhase.Ended || !member.Client.Client.IsOpen) && member.Client.Tick < target) member.Gone = true;
                if (member.Client.Problem.Length > 0) return Fail($"client {members.IndexOf(member) + 1}: {member.Client.Problem}", compared);
                if (Playing(member) && member.Client.Driver is { Playback.Synced: true } driver)
                {
                    delaySum += driver.Playback.DelayMicros;
                    delayCount++;
                }
            }
            // Every tick is compared across the clients that played it, in addition to the server's own comparison of the reports: a client that left was in step until it left.
            var common = members.Where(Playing).Select(member => (long)member.Client.Hashes.Count).DefaultIfEmpty(0).Min();
            for (; compared < common; compared++)
            {
                var reference = members.First(Playing).Client.Hashes[(int)compared];
                if (members.Any(member => member.Client.Hashes.Count > compared && member.Client.Hashes[(int)compared] != reference))
                    return Fail($"the session hashes of the clients differ after tick {compared}", compared, compared);
            }
            if (server.Phase == RoomPhase.Ended && server.Desync is { } desync) return Fail($"the server reported a desync at tick {desync.Tick}", compared, desync.Tick);
            if (server.Phase == RoomPhase.Ended && members.Any(member => Playing(member) && member.Client.Tick < target)) return Fail($"the server ended the match at tick {server.End?.Tick} ({server.End?.Reason})", compared);
            idle = progressed ? 0 : idle + 1;
            if (idle > 200_000)
                return Fail($"the match stalled at tick {members.Where(Playing).Min(member => member.Client.Tick)} (server tick {server.NextTick}, discarded {server.Discarded}: {server.LastDiscard})", compared);
        }
        var clockMs = clock.Milliseconds - startMs;
        if (!members.Any(Playing)) return Fail("no client played to the end", compared);
        // The last hash reports travel to the server.
        var lastReported = target / options.HashInterval * options.HashInterval - 1;
        for (var round = 0; round < 40_000 && server.LastAgreedTick < lastReported && server.Phase == RoomPhase.Match; round++) Round(target);
        foreach (var notice in members.First(Playing).Client.Client.Dropped)
            dropped.Add((members.FindIndex(member => member.Client.Client.ConnectionId == notice.ConnectionId), notice.Player, notice.Reason));
        if (server.Desync is { } late) return Fail($"the server reported a desync at tick {late.Tick}", compared, late.Tick);
        var clientDiscarded = members.Where(Playing).Sum(member => member.Client.Client.Discarded);
        if (server.Discarded > 0 || clientDiscarded > 0)
            return Fail($"messages were discarded (server {server.Discarded}: {server.LastDiscard}; clients {clientDiscarded}: {members.Where(Playing).Select(member => member.Client.Client.LastDiscard).FirstOrDefault(text => text.Length > 0)})", compared);
        if (server.LastAgreedTick < lastReported) return Fail($"the server agreed on the hashes only up to tick {server.LastAgreedTick} of {lastReported}", compared);

        // The relay's recording as a replay: the humans' commands from the bundles - a leave among them is one -, the computer players decide again.
        var first = members.First(Playing).Client.Session!;
        var recorded = ImmutableArray.CreateBuilder<SimCommand>();
        foreach (var bundle in server.Recording.Bundles)
        {
            if (bundle.Tick >= target) break;
            foreach (var entry in bundle.Commands) recorded.Add(CommandWire.DecodeIntent(entry)(entry.Order, bundle.Tick));
        }
        var replay = new ReplayFile(first.ToReplay().Header with { EndTick = target, MatchDescription = Convert.ToBase64String(server.Recording.Description) },
            [..recorded.OrderBy(command => command.Tick).ThenBy(command => command.Player.Value).ThenBy(command => command.Sequence)], []);
        var played = SkirmishReplay.Play(ReplayFormat.Read(ReplayFormat.Write(replay)), _catalog, _fingerprint, _terrain, _mapObjects, audit: false);
        var final = first.StateHash();
        var latencies = members.SelectMany(member => member.Client.LatencyTicks).ToList();
        var result = new LoopbackProbeResult(true, string.Empty, members.Count, server.Pacing, target, final, server.LastAgreedTick, server.IntentsRelayed, recorded.Count, server.HashReports,
            members.Sum(member => member.Client.Driver?.Stalls ?? 0), server.Discarded, clientDiscarded, udp ? datagrams.Datagrams : loopback.MessagesSent, udp ? datagrams.Bytes : loopback.BytesSent, -1,
            played.FinalSessionHash, played.HumanCommands, latencies.Count == 0 ? 0 : latencies.Max(), latencies.Count == 0 ? 0 : latencies.Average(), rounds, dropped.ToImmutable(), server.ThrottledMicros / 1000, clockMs,
            Retransmitted(), datagrams.Lost, members.Sum(member => member.Client.Driver?.Playback.Resyncs ?? 0), delayCount == 0 ? 0 : delaySum / 1000.0 / delayCount);
        if (played.FinalSessionHash != final) return result with { Ok = false, Problem = $"the server's recording played as a replay ends in session hash {played.FinalSessionHash:X16}, the clients in {final:X16}" };
        if (members.Where(Playing).Any(member => member.Client.Session!.StateHash() != final)) return result with { Ok = false, Problem = "the clients end in different session hashes" };
        return result;
    }
}
