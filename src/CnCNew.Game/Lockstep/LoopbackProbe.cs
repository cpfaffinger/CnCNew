using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Lockstep;

/// <param name="Ticks">How many ticks every client plays.</param>
/// <param name="ToServer">Delay and jitter of every client's way to the server; <paramref name="ToClient"/> of the way back.</param>
/// <param name="NetworkSeed">Seed of the loopback's jitter (its own generator, never a stream of the simulation).</param>
/// <param name="PlaybackBufferTicks">The fixed playback buffer of every client (server-paced).</param>
/// <param name="ClockStepMs">How far the probe's clock moves per round, in milliseconds.</param>
/// <param name="GeneratorSeedSalt">Makes the command generators decide differently from run to run without touching the match.</param>
public sealed record LoopbackProbeOptions(long Ticks, LinkQuality ToServer = default, LinkQuality ToClient = default, uint NetworkSeed = 1, int PlaybackBufferTicks = 0,
    int ClockStepMs = 5, ushort HashInterval = 1, uint ServerSeed = 0x25C0FFEE, uint GeneratorSeedSalt = 0);

public sealed record LoopbackProbeResult(bool Ok, string Problem, int Clients, Pacing Pacing, long TicksPlayed, ulong FinalSessionHash, long LastAgreedTick, long IntentsRelayed, long BundleCommands,
    long HashReports, long Stalls, long ServerDiscarded, long ClientDiscarded, long MessagesSent, long BytesSent, long FirstDifferingTick, ulong ReplayedSessionHash, int ReplayedHumanCommands,
    long MaxIntentLatencyTicks, double MeanIntentLatencyTicks, long Rounds);

/// <summary>
/// The multi-client probe (docs/NETWORK-PROTOCOL.md section 8): H clients, each with a world of its own, against one relay over the loopback, in one process and one thread.
/// A "human" is a command generator - a <see cref="SkirmishAi"/> that decides on its own client only and sends its commands as intents through the relay, which puts load on
/// the real command path; the computer slots of the description run in every client's simulation as in any match. After every tick the session hashes of all worlds are
/// compared; at the end the relay's recording is played again and must end in the same session hash.
/// </summary>
public sealed class LoopbackProbe
{
    private sealed class Member
    {
        public required LockstepClient Client { get; init; }
        public SkirmishMatchSession Session { get; set; } = null!;
        public CommandPath Path { get; set; } = null!;
        public LockstepDriver Driver { get; set; } = null!;
        public SkirmishAi? Generator { get; set; }
        public long PreparedFor { get; set; } = -1;
        public List<ulong> Hashes { get; } = [];
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
        var clock = new ManualNetClock();
        var network = new LoopbackNetwork(clock, options.NetworkSeed);
        var seed = options.ServerSeed;
        var server = new RelayServer(clock, new RelayOptions(AllowModifiedBuilds: true, HashInterval: options.HashInterval, SeedSource: () => seed = unchecked(seed * 1664525u + 1013904223u)));
        var members = new List<Member>();
        foreach (var _ in seats)
        {
            var (clientEnd, serverEnd) = network.Connect(options.ToServer, options.ToClient);
            server.Attach(serverEnd);
            members.Add(new Member { Client = new LockstepClient(clientEnd, $"client {members.Count + 1}", _fingerprint) });
        }
        var rounds = 0L;
        string? Wait(Func<bool> done, string what)
        {
            for (var guard = 0; guard < 20_000; guard++)
            {
                rounds++;
                clock.Advance(options.ClockStepMs);
                server.Pump();
                foreach (var member in members) member.Client.Pump();
                if (done()) return null;
                if (members.FirstOrDefault(member => member.Client.Rejection is not null) is { } rejected) return $"rejected: {rejected.Client.Rejection!.Text}";
                if (members.FirstOrDefault(member => member.Client.LastRefusal is not null) is { } refused) return $"refused while trying to {what}: {refused.Client.LastRefusal!.Text}";
            }
            return $"the room did not {what} (server discarded {server.Discarded}: {server.LastDiscard})";
        }
        LoopbackProbeResult Fail(string problem, long ticks = 0, long differing = -1)
            => new(false, problem, members.Count, server.Pacing, ticks, 0, server.LastAgreedTick, server.IntentsRelayed, server.Recording.Bundles.Sum(bundle => (long)bundle.Commands.Length), server.HashReports,
                members.Sum(member => member.Driver?.Stalls ?? 0), server.Discarded, members.Sum(member => member.Client.Discarded), network.MessagesSent, network.BytesSent, differing, 0, 0, 0, 0, rounds);

        if (Wait(() => members.All(member => member.Client.Phase == ClientPhase.InRoom), "admit the clients") is { } admit) return Fail(admit);
        var host = members.First(member => member.Client.IsHost);
        host.Client.Configure(settings);
        if (Wait(() => members.All(member => member.Client.Room is { } room && room.Settings.Slots.Length == settings.Slots.Length), "set the room") is { } set) return Fail(set);
        for (var index = 0; index < members.Count; index++)
        {
            var seat = settings.Slots[seats[index]];
            members[index].Client.RequestSlot(seats[index], seat.Faction, seat.Team, seat.Color, seat.StartPosition);
            members[index].Client.SetReady(true);
        }
        if (Wait(() => host.Client.Room!.Members.Count(member => member.Slot >= 0 && (member.Ready || member.IsHost)) == members.Count, "seat the clients") is { } seated) return Fail(seated);
        host.Client.RequestStart();
        if (Wait(() => members.All(member => member.Client.Phase == ClientPhase.Loading), "start the match") is { } started) return Fail(started);

        var identity = BuildIdentity.Current.Id;
        foreach (var member in members)
        {
            var description = member.Client.Description!;
            var session = SkirmishMatchSession.Start(description, _catalog, _terrain, _mapObjects, 0, _fingerprint, identity);
            member.Session = session;
            // The relay is pumped by the probe's rounds; a client-paced room (one client) needs it inside the request for a tick.
            Action pump = members.Count == 1 ? () => { server.Pump(); member.Client.Pump(); } : member.Client.Pump;
            member.Driver = new LockstepDriver(member.Client, 1.0 / description.TickRate, pump, session.StateHash, description.MaxTicks - 1, options.PlaybackBufferTicks);
            member.Path = new CommandPath(session.World, session.Issue, member.Driver);
            var slot = session.Match.Slots.First(resolved => resolved.Player.Value == member.Client.Player);
            var generatorOptions = SkirmishMatchSession.AiOptionsOf(session.Match, slot, _terrain);
            generatorOptions = generatorOptions with { Seed = unchecked(generatorOptions.Seed ^ 0x9E3779B9u ^ options.GeneratorSeedSalt) };
            var player = slot.Player;
            var path = member.Path;
            member.Generator = new SkirmishAi(session.World, _catalog, generatorOptions, command => path.Issue(player, (sequence, tick) => command with { Sequence = sequence, Tick = tick }));
        }
        AfterStart?.Invoke([..members.Select(member => member.Session)]);
        foreach (var member in members) member.Client.ReportLoaded(member.Session.StateHash());
        if (Wait(() => members.All(member => member.Client.Phase is ClientPhase.InMatch or ClientPhase.Ended), "begin the match") is { } begun) return Fail(begun);
        if (members.Any(member => member.Client.Phase == ClientPhase.Ended)) return Fail("the states after loading differ: the server ended the match before it began", 0, 0);

        var target = Math.Min(options.Ticks, settings.MaxTicks);
        var tickSeconds = 1.0 / settings.TickRate;
        var compared = 0L;
        var issuedAt = new Dictionary<(int Member, long Receipt), long>();
        long latencySum = 0, latencyCount = 0, latencyMax = 0;
        for (var index = 0; index < members.Count; index++)
        {
            var memberIndex = index;
            members[index].Path.PreChecked += (_, receipt) => issuedAt[(memberIndex, receipt.Number)] = members[memberIndex].Session.World.Tick;
            members[index].Path.Resolved += resolution =>
            {
                if (resolution.Receipt == 0 || !issuedAt.Remove((memberIndex, resolution.Receipt), out var at)) return;
                var latency = resolution.Command.Tick - at;
                latencySum += latency;
                latencyCount++;
                latencyMax = Math.Max(latencyMax, latency);
            };
        }
        var idle = 0;
        while (members.Any(member => member.Session.World.Tick < target))
        {
            rounds++;
            clock.Advance(options.ClockStepMs);
            server.Pump();
            var progressed = false;
            foreach (var member in members)
            {
                member.Driver.AddTime(members.Count == 1 ? tickSeconds : options.ClockStepMs / 1000.0);
                while (member.Session.World.Tick < target && member.Driver.IsDue(member.Session.World.Tick))
                {
                    var tick = member.Session.World.Tick;
                    // The generator decides once per tick, before the tick is closed - where the HUD's per-tick bookkeeping stands in a game.
                    if (member.PreparedFor != tick)
                    {
                        member.PreparedFor = tick;
                        member.Generator?.Update();
                    }
                    if (!member.Driver.TryReleaseTick(tick, member.Path.CanStep)) break;
                    member.Path.SubmitDue();
                    member.Session.Step();
                    member.Driver.TickCompleted(tick);
                    member.Hashes.Add(member.Session.StateHash());
                    progressed = true;
                }
            }
            // Every tick all clients have played is compared here, in addition to the server's own comparison of the reports.
            var common = members.Min(member => member.Hashes.Count);
            for (; compared < common; compared++)
                if (members.Any(member => member.Hashes[(int)compared] != members[0].Hashes[(int)compared]))
                    return Fail($"the session hashes of the clients differ after tick {compared}", compared, compared);
            if (server.Phase == RoomPhase.Ended && server.Desync is { } desync) return Fail($"the server reported a desync at tick {desync.Tick}", compared, desync.Tick);
            idle = progressed ? 0 : idle + 1;
            if (idle > 200_000) return Fail($"the match stalled at tick {members.Min(member => member.Session.World.Tick)} (server tick {server.NextTick}, discarded {server.Discarded}: {server.LastDiscard})", compared);
        }
        // The last hash reports travel to the server.
        var lastReported = target / options.HashInterval * options.HashInterval - 1;
        for (var round = 0; round < 20_000 && server.LastAgreedTick < lastReported && server.Phase == RoomPhase.Match; round++)
        {
            rounds++;
            clock.Advance(options.ClockStepMs);
            server.Pump();
            foreach (var member in members) member.Client.Pump();
        }
        if (server.Desync is { } late) return Fail($"the server reported a desync at tick {late.Tick}", compared, late.Tick);
        var clientDiscarded = members.Sum(member => member.Client.Discarded);
        if (server.Discarded > 0 || clientDiscarded > 0) return Fail($"messages were discarded (server {server.Discarded}: {server.LastDiscard}; clients {clientDiscarded})", compared);
        if (server.LastAgreedTick < lastReported) return Fail($"the server agreed on the hashes only up to tick {server.LastAgreedTick} of {lastReported}", compared);

        // The relay's recording as a replay: the humans' commands from the bundles, the computer players decide again.
        var first = members[0].Session;
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
        var result = new LoopbackProbeResult(true, string.Empty, members.Count, server.Pacing, target, final, server.LastAgreedTick, server.IntentsRelayed, recorded.Count, server.HashReports,
            members.Sum(member => member.Driver.Stalls), server.Discarded, clientDiscarded, network.MessagesSent, network.BytesSent, -1, played.FinalSessionHash, played.HumanCommands,
            latencyMax, latencyCount == 0 ? 0 : (double)latencySum / latencyCount, rounds);
        if (played.FinalSessionHash != final) return result with { Ok = false, Problem = $"the server's recording played as a replay ends in session hash {played.FinalSessionHash:X16}, the clients in {final:X16}" };
        if (members.Any(member => member.Session.StateHash() != final)) return result with { Ok = false, Problem = "the clients end in different session hashes" };
        return result;
    }
}
