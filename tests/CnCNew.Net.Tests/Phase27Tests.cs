using System.Collections.Immutable;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Tests;

// Phase 27: a seat that is held and taken again, a state that is handed over and checked, watchers on a delay and late ones, the diagnosis of a desync, the server's
// recording as a file, the rules of a pause.
static partial class NetTests
{
    private static string Scratch(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-net-tests", name + "-" + Environment.ProcessId);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return directory;
    }

    // ------------------------------------------------------------------------------------------------------------ rejoin

    public static void ReconnectAndTakeTheSeatAgain()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 1_500);
        var ticks = settings.MaxTicks;
        var rate = settings.TickRate;
        var hold = new RelayOptions(SeatHoldMs: 20_000);
        foreach (var transport in new[] { ProbeTransport.Loopback, ProbeTransport.Udp })
        {
            // The line of client 2 is gone after 5 game seconds; 2 s later the same client connects again with its token.
            var options = new LoopbackProbeOptions(ticks, Transport: transport, Impairment: transport == ProbeTransport.Udp ? new Impairment(DelayMs: 20, JitterMs: 10, LossPerMille: 10) : default,
                Disturbances: [new ProbeDisturbance(1, rate * 5, ProbeDisturbanceKind.Reconnect, 400)], Relay: hold, Udp: new UdpOptions { TimeoutMs = 1_000 });
            var result = fixture.Probe().Run(settings, options);
            Check.That(result.Ok, $"{transport}: a client comes back: {result.Problem}");
            Check.That(result.LateJoins == 1 && result.SnapshotsRelayed == 1 && result.LateJoinsRefused == 0 && result.SnapshotStateBytes > 10_000 && result.SnapshotPackedBytes < result.SnapshotStateBytes / 3,
                $"{transport}: it was handed the state of a member, packed ({result.SnapshotStateBytes} -> {result.SnapshotPackedBytes} bytes)");
            Check.That(result.JoinedAt.Length == 1 && result.JoinedAt[0].Member == 1 && result.JoinedAt[0].Tick > rate * 5,
                $"{transport}: it joined at a tick after the one it was lost at and played on from there in step ({string.Join(", ", result.JoinedAt)})");
            var story = string.Join(' ', result.ServerEvents.Where(name => name is "seat-held" or "seat-returned" or "snapshot-relayed" or "joined-late" or "seat-given-up" or "caught-up" or "throttled"));
            Check.That(story == "seat-held seat-returned snapshot-relayed joined-late caught-up", $"{transport}: the seat was held, taken again, and nobody was slowed meanwhile ({story})");
            Check.That(result.ThrottledMs == 0 && result.ReplayedSessionHash == result.FinalSessionHash, $"{transport}: the recording replays; the player never left the match");
            Check.That(result.Resyncs == 0, $"{transport}: the backlog of the joiner was not taken for jitter ({result.Resyncs} resyncs)");
        }

        // A server that asks for a hash only every fourth tick: the state is handed over at a tick whose hash was agreed on, not at any tick.
        var sparse = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Disturbances: [new ProbeDisturbance(1, rate * 5 + 1, ProbeDisturbanceKind.Reconnect, 400)], Relay: hold with { HashInterval = 4 }));
        Check.That(sparse.Ok && sparse.LateJoins == 1 && sparse.SnapshotsRelayed == 1 && sparse.SnapshotsRefused == 0 && sparse.JoinedAt.Length == 1,
            $"with a hash every fourth tick the state of a reported tick is handed over ({sparse.Problem}; relayed {sparse.SnapshotsRelayed}, refused {sparse.SnapshotsRefused}, at {string.Join(", ", sparse.JoinedAt)})");

        // Too late: the seat is given up after its time, the player leaves the match by a command, and the client that comes back then is told so.
        var late = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Disturbances: [new ProbeDisturbance(1, rate * 5, ProbeDisturbanceKind.Reconnect, 800)], Relay: new RelayOptions(SeatHoldMs: 1_500)));
        Check.That(late.Ok, $"a client comes back too late: {late.Problem}");
        Check.That(late.LateJoins == 0 && late.ServerEvents.Contains("seat-given-up") && late.ServerEvents.Count(name => name == "rejected") == 1 && late.JoinedAt.IsEmpty,
            $"the seat was given up and the late client rejected ({string.Join(' ', late.ServerEvents.Where(name => name.StartsWith("seat", StringComparison.Ordinal) || name == "rejected"))})");

        // Without a held seat (the rule of phase 26) a lost connection is a player that leaves at once.
        var none = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Disturbances: [new ProbeDisturbance(1, rate * 5, ProbeDisturbanceKind.Cut)]));
        Check.That(none.Ok && !none.ServerEvents.Contains("seat-held") && none.Dropped.Length == 1, $"no seat is held unless the server is told to: {none.Problem}");
    }

    public static void DamagedSnapshotIsTurnedAway()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 1_500);
        var rate = settings.TickRate;
        // The first member the relay asks hands over a state that was damaged on its way. The joiner cannot use it; the relay turns the joiner away, holds the seat again,
        // and gives it up when its time is over. Nobody plays on a state that does not hash to the match.
        var result = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Relay: new RelayOptions(SeatHoldMs: 6_000),
            Disturbances: [new ProbeDisturbance(0, 10, ProbeDisturbanceKind.DamageSnapshot), new ProbeDisturbance(1, rate * 5, ProbeDisturbanceKind.Reconnect, 200)]));
        Check.That(result.Ok, $"the others play on: {result.Problem}");
        var story = string.Join(' ', result.ServerEvents.Where(name => name.StartsWith("seat", StringComparison.Ordinal) || name.StartsWith("snapshot", StringComparison.Ordinal) || name is "join-refused" or "joined-late"));
        Check.That(result.LateJoinsRefused == 1 && result.LateJoins == 0 && story == "seat-held seat-returned snapshot-relayed join-refused seat-held seat-given-up",
            $"a damaged state is refused by the one who loads it and by the relay ({story})");

        // The relay itself refuses a state whose stated hash is not what the clients agreed on for that tick, and asks the next member.
        var net = new Net(new RelayOptions(SeedSource: () => 9, SeatHoldMs: 60_000, LagReportMs: 0, LagThrottleMs: 0));
        net.Join();
        net.StartMatch(2, TwoSeats());
        var token = net.Clients[1].RejoinToken;
        Check.That(token != 0 && net.Clients[0].RejoinToken != 0 && token != net.Clients[0].RejoinToken, "every member of a server that holds seats gets a token of its own");
        Play(net, 500, 0, 1, 2);
        net.Clients[1].Close();
        Play(net, 200, 0, 2);
        Check.That(net.Server.HeldSeats.SequenceEqual([2]) && net.Clients[0].HeldSeats.ContainsKey(2) && net.Server.Recording.Bundles.All(bundle => bundle.Commands.IsEmpty),
            "the seat of the lost player is held, everybody is told, and the player has not left the match");
        uint asked = 0;
        net.Clients[0].SnapshotRequested = request => asked = request;
        uint askedOfWatcher = 0;
        net.Clients[2].SnapshotRequested = request => askedOfWatcher = request;
        var (end, serverEnd) = net.Network.Connect();
        net.Server.Attach(serverEnd);
        var back = new LockstepClient(end, "back", Definitions, Released, rejoinToken: token);
        net.Clients.Add(back);
        Play(net, 100, 0, 2);
        Check.That(back.Phase == ClientPhase.InRoom && asked != 0 && net.Server.HeldSeats.Count == 0, "the client with the token is admitted to the running match, and a member is asked for its state");
        // The member answers with a state whose hash is not the agreed one (7 in this fixture).
        var tick = net.Server.LastAgreedTick + 1;
        net.Clients[0].OfferSnapshot(asked, new ResumeState(tick, 8, [], [1, 2, 3]));
        Play(net, 100, 0, 2);
        Check.That(net.Server.SnapshotsRefused == 1 && back.Start is null && askedOfWatcher != 0, "a state with another hash than the agreed one goes nowhere; the next member is asked");
        net.Clients[2].OfferSnapshot(askedOfWatcher, new ResumeState(net.Server.LastAgreedTick + 1, 7, [], [4, 5, 6]));
        Play(net, 100, 0, 2);
        Check.That(net.Server.SnapshotsRelayed == 1 && back.Phase == ClientPhase.Loading && back.Start!.Resume!.State.SequenceEqual(new byte[] { 4, 5, 6 }) && back.Player == 2,
            "a state with the agreed hash is passed on, with the seat's player number");
        // The joiner built something that hashes differently: turned away, the seat held again for the rest of its time.
        back.ReportLoaded(99);
        Play(net, 100, 0, 2);
        Check.That(!back.IsOpen && net.Server.LateJoinsRefused == 1 && net.Server.HeldSeats.SequenceEqual([2]), "a joiner whose state does not hash to the match is turned away and its seat held again");
        // Somebody with a made-up token.
        var (strangerEnd, strangerServerEnd) = net.Network.Connect();
        net.Server.Attach(strangerServerEnd);
        var stranger = new LockstepClient(strangerEnd, "stranger", Definitions, Released, rejoinToken: token ^ 1);
        net.Clients.Add(stranger);
        Play(net, 100, 0, 2);
        Check.That(stranger.Phase == ClientPhase.Rejected && stranger.Rejection!.Reason == RejectReason.SeatNotHeld, "a token nobody was given opens no seat");
        // A player that is back before the relay noticed its old connection is dead: the token is the proof, the old connection gives way.
        var (againEnd, againServerEnd) = net.Network.Connect();
        net.Server.Attach(againServerEnd);
        var again = new LockstepClient(againEnd, "again", Definitions, Released, rejoinToken: net.Clients[0].RejoinToken);
        net.Clients.Add(again);
        Play(net, 100, 2, 5);
        Check.That(again.Phase == ClientPhase.InRoom && !net.Clients[0].IsOpen && net.Server.HeldSeats.SequenceEqual([2]),
            $"the token of a connection that still stands takes its seat over ({again.Phase}, {again.Rejection?.Reason})");
    }

    // ---------------------------------------------------------------------------------------------------------- watchers

    public static void WatchersOnADelayAndLateOnes()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(2, 1, 1_500);
        var rate = settings.TickRate;
        // One watcher from the start and one that comes after 6 game seconds; both are sent the bundles 2 s behind the players.
        var result = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, Watchers: 1, LateWatchers: [rate * 6], Transport: ProbeTransport.Udp, Impairment: new Impairment(DelayMs: 15, JitterMs: 5),
            Relay: new RelayOptions(SpectatorDelayMs: 2_000, AllowLateWatchers: true)));
        Check.That(result.Ok, $"watchers on a delay: {result.Problem}");
        Check.That(result.Clients == 4 && result.LateJoins == 1 && result.SnapshotsRelayed == 0 && result.ThrottledMs == 0,
            $"the late watcher played the match from its start - it is not handed the present - and slowed nobody ({result.LateJoins} late, {result.SnapshotsRelayed} snapshots)");
        Check.That(result.ReplayedSessionHash == result.FinalSessionHash, "everybody ends in the state of the replay");

        // The delay itself, at the relay: a watcher's newest bundle is the players' newest less the delay; its report is compared with what the players agreed on.
        var net = new Net(new RelayOptions(SeedSource: () => 3, SpectatorDelayMs: 1_000, AllowLateWatchers: true, LagReportMs: 0, LagThrottleMs: 0));
        net.Join();
        net.StartMatch(2, TwoSeats());
        var watcher = net.Clients[2];
        Play(net, 3_000, 0, 1, 2);
        Check.That(Math.Abs(net.Clients[0].NewestBundleTick - watcher.NewestBundleTick - 60) <= 1, $"a watcher is sent the bundles one second behind the players ({net.Clients[0].NewestBundleTick} against {watcher.NewestBundleTick})");
        Check.That(net.Server.LastAgreedTick >= net.Clients[0].NewestBundleTick - 2 && net.Server.Desync is null, "the players' ticks are agreed without waiting for the watcher");
        watcher.SendIntent(1, MoveIntent());
        Play(net, 100, 0, 1);
        Check.That(net.Server.Discarded == 1 && net.Server.IntentsRelayed == 0, "an order of somebody who watches is discarded and counted");
        // A watcher whose state differs is found by its late report.
        watcher.ReportHash(watcher.NewestBundleTick, 1234);
        Play(net, 50, 0, 1);
        Check.That(watcher.Desync is { } found && found.Reports.Any(report => report.ConnectionId == 0 && report.SessionHash == 7), "a watcher that diverged is found against the hash the players agreed on");
        // Its divergence is its own (version 4, section 20.6): it is told and dropped, and the match of the players goes on.
        Play(net, 200, 0, 1);
        Check.That(net.Server.WatcherDesyncs == 1 && net.Server.Desync is null && net.Server.Phase == RoomPhase.Match && !watcher.IsOpen,
            $"and the players' match goes on without it ({net.Server.WatcherDesyncs}; {net.Server.Phase})");

        // A server that admits nobody to a running match says so.
        var closed = new Net(new RelayOptions(SeedSource: () => 3, LagReportMs: 0, LagThrottleMs: 0));
        closed.StartMatch(2, TwoSeats());
        var (end, serverEnd) = closed.Network.Connect();
        closed.Server.Attach(serverEnd);
        var refused = new LockstepClient(end, "late", Definitions, Released, watch: true);
        closed.Clients.Add(refused);
        closed.Pump(4, 10);
        Check.That(refused.Phase == ClientPhase.Rejected && refused.Rejection!.Reason == RejectReason.MatchRunning, "without the server's leave nobody joins a running match");
    }

    // ----------------------------------------------------------------------------------------------------------- desync

    public static void DesyncIsNamedDownToTheField()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 1_500);
        var rate = settings.TickRate;
        var directory = Scratch("desync");
        try
        {
            // The test switch: on client 2 one object takes a point of damage that nobody else's takes.
            var result = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, DiagnosticsDirectory: directory, Disturbances: [new ProbeDisturbance(1, rate * 4, ProbeDisturbanceKind.Corrupt)]));
            Check.That(!result.Ok && result.DesyncTick == rate * 4 + 1 && result.Corrupted.Contains($"at tick {rate * 4 + 1}", StringComparison.Ordinal),
                $"the relay reports the desync at the tick of the change ({result.DesyncTick}; {result.Corrupted}; {result.Problem})");
            // The damage is in the object; that the world counted it is in the rest of the world.
            Check.That(result.DesyncParts.Split("; ").Contains("world.objects") && !result.DesyncParts.Contains("ai.", StringComparison.Ordinal) && !result.DesyncParts.Contains("world.timers", StringComparison.Ordinal),
                $"the relay names the parts of the hash the clients differ in ({result.DesyncParts})");
            Check.That(result.Packages.Length == 3, $"every client wrote its package ({result.Packages.Length})");
            var corrupted = uint.Parse(result.Corrupted.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
            var map = new MatchMap(fixture.Terrain, fixture.Map.Objects);
            var diff = DesyncPackageDiff.Compare(Path.Combine(directory, "client-1"), Path.Combine(directory, "client-2"), fixture.Synthetic.Templates, fixture.Synthetic.Fingerprint, _ => map);
            Check.That(diff.FirstDifferingTick == result.DesyncTick, $"the tool finds the tick ({diff.FirstDifferingTick})");
            Check.That(diff.Parts.Contains("world.objects") && diff.Objects.SequenceEqual([corrupted]), $"and the object ({string.Join(", ", diff.Parts)}; {string.Join(", ", diff.Objects)}; expected {corrupted})");
            Check.That(diff.FieldsA.IsEmpty, $"the client that is in step differs in nothing from the match played again ({string.Join("; ", diff.FieldsA.Take(3))})");
            Check.That(diff.FieldsB.Length is >= 1 and <= 8 && diff.FieldsB.Any(field => field.Contains($"[id={corrupted}]", StringComparison.Ordinal) && field.Contains("ealth", StringComparison.Ordinal)),
                $"and the field, on the client that diverged ({string.Join("; ", diff.FieldsB.Take(5))})");
            // Two clients that are in step: nothing differs.
            var same = DesyncPackageDiff.Compare(Path.Combine(directory, "client-1"), Path.Combine(directory, "client-3"), fixture.Synthetic.Templates, fixture.Synthetic.Fingerprint, _ => map);
            Check.That(same.FirstDifferingTick == -1 && same.FieldsA.IsEmpty && same.FieldsB.IsEmpty, "two clients in step have nothing to tell apart");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        // The hash interval: with a report every 30 ticks the same change is noticed at the next report - later, for a thirtieth of the reports.
        var sparse = fixture.Probe().Run(settings, new LoopbackProbeOptions(settings.MaxTicks, HashInterval: 30, Disturbances: [new ProbeDisturbance(1, rate * 4, ProbeDisturbanceKind.Corrupt)]));
        Check.That(!sparse.Ok && sparse.DesyncTick > rate * 4 && sparse.DesyncTick <= rate * 4 + 30 && (sparse.DesyncTick + 1) % 30 == 0, $"with an interval of 30 the desync is found at the next report ({sparse.DesyncTick})");
    }

    // ------------------------------------------------------------------------------------------------- recording and pace

    public static void RecordingAsAFileAndTheRulesOfThePace()
    {
        // The relay's recording as a replay file: played, it ends in the state the clients agreed on.
        var fixture = Fixture.Create();
        var settings = fixture.Settings(2, 1, 900);
        var probe = fixture.Probe();
        var result = probe.Run(settings, new LoopbackProbeOptions(settings.MaxTicks));
        Check.That(result.Ok && result.ReplayedSessionHash == result.FinalSessionHash && result.ReplayedHumanCommands > 10, $"the recording as a file replays into the clients' state: {result.Problem}");

        // The pace of a server-paced match.
        var net = new Net(new RelayOptions(SeedSource: () => 4, LagReportMs: 0, LagThrottleMs: 0, PausesPerPlayer: 2, MaxPauseMs: 3_000));
        net.Join();
        net.StartMatch(2, TwoSeats());
        var (host, guest, watcher) = (net.Clients[0], net.Clients[1], net.Clients[2]);
        watcher.RequestPace(true, 100);
        net.Pump(4, 10);
        Check.That(!net.Server.Paused && watcher.LastRefusal is not null, "somebody who watches does not pause the match");
        guest.RequestPace(false, 200);
        net.Pump(4, 10);
        Check.That(net.Server.SpeedPercent == 100 && guest.LastRefusal!.Reason == RejectReason.NotHost, "the speed is the host's to set");
        guest.RequestPace(true, 100);
        net.Pump(4, 10);
        Check.That(net.Server.Paused && host.Paused, "a player pauses the match");
        var before = net.Server.NextTick;
        net.Pump(100, 10);
        Check.That(net.Server.NextTick == before, "a paused match closes no tick");
        host.RequestPace(false, 100);
        net.Pump(4, 10);
        Check.That(!net.Server.Paused, "anybody who plays ends a pause");
        guest.RequestPace(true, 100);
        net.Pump(4, 10);
        // The second pause is left alone: after 3 s the match goes on by itself.
        net.Pump(280, 10);
        Check.That(net.Server.Paused, "a pause lasts until somebody ends it - or until its limit");
        net.Pump(40, 10);
        Check.That(!net.Server.Paused && !host.Paused && net.Server.NextTick > before, "a pause that reaches its limit ends by itself, and everybody is told");
        guest.RequestPace(true, 100);
        net.Pump(4, 10);
        Check.That(!net.Server.Paused, "a player has two pauses on this server, not three");

        // The count of slowed time starts again only after the recovery time (the weakness measured in phase 26): a member that catches up while the game is slowed and
        // falls behind again at once is one incident.
        var rule = new Net(new RelayOptions(SeedSource: () => 4, LagRecoveryMs: 30_000));
        rule.StartMatch(2, TwoSeats());
        Play(rule, 3_000, 0, 1);
        Play(rule, 4_500, 0);
        Check.That(rule.Server.Throttling, "slowed for the member that stopped");
        Play(rule, 600, 0, 1);
        Check.That(!rule.Server.Throttling && rule.Clients[1].IsOpen, "it caught up");
        var first = rule.Server.ThrottledMicros;
        Play(rule, 2_100, 0);
        Play(rule, 10_000 - (int)(first / 1000) + 300, 0);
        Check.That(rule.Server.DroppedForLag == 1 && rule.Server.ThrottledMicros is >= 9_900_000 and <= 10_500_000,
            $"the two incidents together are the limit of 10 s, not 10 s each ({rule.Server.ThrottledMicros / 1000} ms slowed in all)");

        // After the end a connection that closes is a client that went home.
        var ended = new Net();
        ended.StartMatch(2, TwoSeats());
        ended.Pump(4, 10);
        foreach (var client in ended.Clients) client.ReportEnd(5, 1);
        ended.Pump(4, 10);
        ended.Clients[1].Close();
        ended.Pump(4, 10);
        var events = new List<RelayEvent>();
        while (ended.Server.TryTakeEvent(out var relayEvent)) events.Add(relayEvent);
        var left = events.Single(relayEvent => relayEvent.Name == "left");
        Check.That(left.Details.Any(detail => detail is ("reason", "Closed")) && ended.Clients[0].Dropped.Single().Reason == DropReason.Left, "a close after the end is logged as closed, not as a lost connection");
        Check.That(events.Single(relayEvent => relayEvent.Name == "match-ended").Details.Any(detail => detail.Key == "lastAgreedHash"), "the end names the last agreed hash");
    }
}
