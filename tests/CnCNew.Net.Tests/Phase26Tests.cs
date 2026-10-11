using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Server;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Tests;

// Phase 26: clock synchronisation, playback control, the rule for members that fall behind, a player leaving, and whole matches over the UDP transport on a lossy network.
static partial class NetTests
{
    // ------------------------------------------------------------------------------------------------- clock and playback

    public static void ClockSynchronisation()
    {
        // The server's game time is 5 s ahead of the local clock; a symmetric way gives the exact value.
        var sync = new ClockSync(9);
        Check.That(!sync.HasEstimate && sync.RoundTripMicros == -1, "no estimate without samples");
        long Server(long local) => local + 5_000_000;
        for (var index = 0; index < 9; index++)
        {
            var sent = 1_000_000 + index * 200_000L;
            sync.Take(sent, sent + 60_000, Server(sent + 30_000), 100);
        }
        Check.That(sync.HasEstimate && sync.GameMicrosAt(3_000_000) == Server(3_000_000) && sync.RoundTripMicros == 60_000, $"a symmetric way gives the server's clock exactly ({sync.GameMicrosAt(3_000_000)})");
        // One exchange whose way back took 400 ms longer: the median does not move.
        sync.Take(3_000_000, 3_460_000, Server(3_030_000), 100);
        Check.That(sync.GameMicrosAt(4_000_000) == Server(4_000_000) && sync.RoundTripMicros == 60_000, "one lopsided exchange moves nothing");
        // Half speed: the server's clock advances half as fast, and samples taken at full speed say nothing about it.
        sync.Take(5_000_000, 5_060_000, 20_000_000, 50);
        Check.That(!sync.HasEstimate && sync.SpeedPercent == 50, "a change of speed empties the window");
        sync.Take(5_200_000, 5_260_000, 20_100_000, 50);
        sync.Take(5_400_000, 5_460_000, 20_200_000, 50);
        Check.That(sync.HasEstimate && sync.GameMicrosAt(7_030_000) == 21_000_000, $"at half speed the estimate advances half as fast ({sync.GameMicrosAt(7_030_000)})");
        sync.Take(6_000_000, 6_060_000, 20_500_000, 0);
        Check.That(!sync.HasEstimate, "a standing server clock gives no estimate");
    }

    /// <summary>A simulated line: a server closing ticks by its clock, bundles arriving after a way with jitter, a client clock that may run fast.</summary>
    private static (long Stalls, long Played, double DelayMs, int MaxAdjust, long Resyncs, long SignChanges, double MeanBufferMs, long Jumps) Play(int wayMs, int jitterMs, int lossPerMille, int skewPerMille, int seconds,
        Func<long, int>? jitterAt = null, PlaybackOptions? options = null)
    {
        const long Tick = 16_666;
        options ??= PlaybackOptions.Default;
        var sync = new ClockSync(options.SyncSamples);
        var playback = new PlaybackControl(options, Tick);
        var random = 99UL;
        int Draw(int bound)
        {
            random ^= random << 13;
            random ^= random >> 7;
            random ^= random << 17;
            return (int)((random >> 33) % (ulong)Math.Max(1, bound));
        }
        var arrivals = new SortedDictionary<long, List<long>>();
        var pongs = new SortedDictionary<long, List<(long Sent, long Game)>>();
        var have = new HashSet<long>();
        long newest = -1, next = 0, closed = 0, stalls = 0, stalled = -1, signChanges = 0, jumps = 0, lastSign = 0, steady = 0;
        var maxAdjust = 0;
        double delaySum = 0, bufferSum = 0;
        long lastPosition = 0, lastLocal = 0;
        // True time in steps of 1 ms; the local clock runs faster by the skew. The server's game time is the true time.
        for (long t = 0; t < seconds * 1_000_000L; t += 1_000)
        {
            var local = t + t * skewPerMille / 1000 + 7_000_000;
            var jitter = jitterAt?.Invoke(t) ?? jitterMs;
            while ((closed + 1) * Tick <= t)
            {
                // The bundle of a lost datagram arrives with the next one, one tick later.
                var lost = Draw(1000) < lossPerMille;
                var at = t + wayMs * 1000L + Draw(jitter * 1000 + 1) + (lost ? Tick : 0);
                if (!arrivals.TryGetValue(at, out var list)) arrivals[at] = list = [];
                list.Add(closed++);
            }
            if (t % 200_000 == 0)
            {
                // Ping: up, the server reads its clock, down.
                var up = wayMs * 1000L + Draw(jitter * 1000 + 1);
                var down = wayMs * 1000L + Draw(jitter * 1000 + 1);
                if (!pongs.TryGetValue(t + up + down, out var list)) pongs[t + up + down] = list = [];
                list.Add((local, t + up));
            }
            while (pongs.Count > 0 && pongs.First().Key <= t)
            {
                foreach (var (sent, game) in pongs.First().Value) sync.Take(sent, local, game, 100);
                pongs.Remove(pongs.First().Key);
            }
            while (arrivals.Count > 0 && arrivals.First().Key <= t)
            {
                foreach (var tick in arrivals.First().Value)
                {
                    have.Add(tick);
                    newest = Math.Max(newest, tick);
                    playback.Arrived(tick, local, sync);
                }
                arrivals.Remove(arrivals.First().Key);
            }
            var wasSynced = playback.Synced;
            playback.Advance(local, sync);
            if (wasSynced && playback.Synced && local > lastLocal)
            {
                // Never a jump: per step the position moves by the elapsed time, a few percent more or less.
                var moved = playback.PositionMicros - lastPosition;
                var elapsed = local - lastLocal;
                if (moved < elapsed * 94 / 100 - 1 || moved > elapsed * 106 / 100 + 1) jumps++;
            }
            lastPosition = playback.PositionMicros;
            lastLocal = local;
            while (playback.Synced && playback.TimeReached(next))
            {
                if (!have.Remove(next))
                {
                    if (stalled != next)
                    {
                        stalled = next;
                        if (t > 15_000_000) stalls++;
                    }
                    break;
                }
                next++;
            }
            if (t > 15_000_000)
            {
                // The second half: the control has settled.
                maxAdjust = Math.Max(maxAdjust, Math.Abs(playback.RateAdjustPerMille));
                var sign = Math.Sign(playback.RateAdjustPerMille);
                if (sign != 0 && lastSign != 0 && sign != lastSign) signChanges++;
                if (sign != 0) lastSign = sign;
                delaySum += playback.DelayMicros / 1000.0;
                bufferSum += playback.BufferMs(newest);
                steady++;
            }
        }
        return (stalls, next, delaySum / Math.Max(1, steady), maxAdjust, playback.Resyncs, signChanges, bufferSum / Math.Max(1, steady), jumps - playback.Resyncs);
    }

    public static void PlaybackControlFollowsTheServer()
    {
        // A quiet line: the delay is the way plus the margin, nothing stalls, the rate stays at the game speed.
        var quiet = Play(wayMs: 40, jitterMs: 0, lossPerMille: 0, skewPerMille: 0, seconds: 60);
        Check.That(quiet.Stalls == 0 && quiet.Resyncs == 0 && quiet.Jumps == 0 && quiet.Played > 3_400, $"a quiet line plays every tick in time ({quiet})");
        Check.That(quiet.DelayMs is >= 43 and <= 47 && quiet.MaxAdjust <= 2, $"the delay is the way of 40 ms plus the margin, in milliseconds - not rounded up to ticks ({quiet.DelayMs:F1} ms, adjust {quiet.MaxAdjust})");

        // Jitter: the delay covers the 99 % quantile of the arrivals; a stall is the rare exception.
        var jittery = Play(wayMs: 40, jitterMs: 30, lossPerMille: 0, skewPerMille: 0, seconds: 60);
        Check.That(jittery.DelayMs is >= 66 and <= 80, $"with 30 ms of jitter the delay covers almost all of it ({jittery.DelayMs:F1} ms)");
        Check.That(jittery.Stalls <= 45 && jittery.Jumps == 0 && jittery.Resyncs == 0, $"at most about one tick in a hundred has to wait ({jittery.Stalls} of 2,700)");

        // Loss of 3 %: the bundle of a lost datagram comes one tick later; the quantile takes that in.
        var lossy = Play(wayMs: 40, jitterMs: 10, lossPerMille: 30, skewPerMille: 0, seconds: 60);
        Check.That(lossy.DelayMs is >= 60 and <= 76 && lossy.Stalls <= 45, $"with 3 % loss the delay grows by about a tick ({lossy.DelayMs:F1} ms, {lossy.Stalls} waits)");

        // A clock that runs 1 % fast or slow (a hundred times a bad crystal): followed without a jump and without swinging.
        foreach (var skew in new[] { 10, -10 })
        {
            var drift = Play(wayMs: 40, jitterMs: 10, lossPerMille: 0, skewPerMille: skew, seconds: 120);
            Check.That(drift.Resyncs == 0 && drift.Jumps == 0 && drift.Stalls <= 100, $"a clock skewed by {skew} per mille is followed without a jump ({drift})");
            Check.That(drift.MaxAdjust <= 30 && drift.SignChanges <= 4, $"the rate settles at the skew instead of swinging around it (adjust up to {drift.MaxAdjust} per mille, {drift.SignChanges} changes of sign)");
        }

        // The line gets worse in the middle of the match: the delay follows by rate, not by a jump.
        var worse = Play(wayMs: 40, jitterMs: 0, lossPerMille: 0, skewPerMille: 0, seconds: 60, jitterAt: t => t < 20_000_000 ? 0 : 40);
        Check.That(worse.Jumps == 0 && worse.Resyncs == 0 && worse.DelayMs >= 75, $"a line that gets worse is followed by rate ({worse})");

        // The way changes by two seconds at once (beyond the resync limit): set right at once, and counted.
        var sync = new ClockSync(3);
        var playback = new PlaybackControl(PlaybackOptions.Default, 16_666);
        for (var index = 0; index < 3; index++) sync.Take(index * 1_000, index * 1_000 + 100, 50_000_000 + index * 1_000, 100);
        playback.Advance(10_000, sync);
        Check.That(!playback.Synced, "an estimate of the clock alone does not set the position: no arrival has been measured yet");
        for (var tick = 0; tick < 8; tick++) playback.Arrived(3_000 + tick, 10_000 + tick, sync);
        playback.Advance(10_010, sync);
        Check.That(playback.Synced && playback.Resyncs == 0, "the first measured arrivals set the position");
        var before = playback.PositionMicros;
        for (var index = 0; index < 3; index++) sync.Take(20_000 + index * 1_000, 20_100 + index * 1_000, 52_500_000, 100);
        playback.Advance(30_000, sync);
        Check.That(playback.Resyncs == 1 && playback.PositionMicros - before > 2_000_000, "a deviation beyond the limit is a resync");
        Check.That(new PlaybackControl(PlaybackOptions.Default, 16_666).TimeReached(1_000_000), "without an estimate every tick is due when its bundle is there");
        // Bundles the client took in after standing still itself are played, not measured: its own hitch is not the network's jitter.
        var measured = playback.LatenessSamples;
        playback.Arrived(3_000, 31_000, sync, sinceLastLookMicros: 400_000);
        Check.That(playback.LatenessSamples == measured, "an arrival after a hitch of the client is not a sample");
        playback.Arrived(3_000, 31_000, sync, sinceLastLookMicros: 4_000);
        Check.That(playback.LatenessSamples == measured + 1 && playback.TypicalLookMicros == 4_000, "an arrival in a normal frame is, and the frame is remembered");

        // A long tick of the client's own simulation (30 ms where its frame is 4) must not raise the delay: what exceeds twice the usual frame is taken off the lateness.
        (double Delay, long Stalls) Frames(bool longFrames)
        {
            var clockSync = new ClockSync(9);
            var control = new PlaybackControl(PlaybackOptions.Default, 16_666);
            long local = 0, last = 0, nextBundle = 0, nextTick = 0, stalls = 0, stalled = -1;
            var held = new HashSet<long>();
            for (var frame = 0; local < 40_000_000; frame++)
            {
                // The line is perfect: 40 ms each way, no jitter. Every 50th frame takes 30 ms instead of 4.
                local += longFrames && frame % 50 == 49 ? 30_000 : 4_000;
                if (frame % 50 == 0) clockSync.Take(local - 80_000, local, local - 40_000, 100);
                for (; (nextBundle + 1) * 16_666 + 40_000 <= local; nextBundle++)
                {
                    held.Add(nextBundle);
                    control.Arrived(nextBundle, local, clockSync, local - last);
                }
                last = local;
                control.Advance(local, clockSync);
                while (control.Synced && control.TimeReached(nextTick))
                {
                    if (!held.Remove(nextTick))
                    {
                        if (stalled != nextTick && local > 15_000_000) stalls++;
                        stalled = nextTick;
                        break;
                    }
                    nextTick++;
                }
            }
            return (control.DelayMicros / 1000.0, stalls);
        }
        var even = Frames(false);
        var uneven = Frames(true);
        Check.That(even.Delay is >= 44 and <= 50 && even.Stalls == 0, $"frames of 4 ms on a perfect line: the delay is the way, the frame and the margin ({even.Delay:F1} ms, {even.Stalls} waits)");
        Check.That(uneven.Delay <= even.Delay + 6 && uneven.Stalls <= 2, $"a long frame now and then does not raise the delay and makes no tick wait for its bundle ({uneven.Delay:F1} ms, {uneven.Stalls} waits)");
    }

    // ----------------------------------------------------------------------------------------------------------- the rule

    private static RelayOptions Rule(uint seed = 5) => new(SeedSource: () => seed);

    /// <summary>Lets the clock run; every listed client reports each tick it has a bundle for as completed (with one hash for all).</summary>
    private static void Play(Net net, int ms, params int[] reporting)
    {
        var reported = net.Reported;
        for (var elapsed = 0; elapsed < ms; elapsed += 5)
        {
            net.Clock.Advance(5);
            net.Server.Pump();
            for (var index = 0; index < net.Clients.Count; index++)
            {
                var client = net.Clients[index];
                client.Pump();
                if (!reporting.Contains(index) || !client.IsOpen) continue;
                reported.TryGetValue(index, out var done);
                for (var tick = Math.Max(done, client.StartTick); tick <= client.NewestBundleTick; tick++) client.ReportHash(tick, 7);
                reported[index] = client.NewestBundleTick + 1;
            }
        }
    }

    public static void MembersThatFallBehind()
    {
        var net = new Net(Rule());
        net.StartMatch(2, TwoSeats());
        var slow = net.Clients[1];
        var other = net.Clients[0];
        Play(net, 3_000, 0, 1);
        Check.That(!net.Server.Throttling && other.Lag.Count == 0 && Math.Abs(net.Server.NextTick - 180) <= 2, $"two clients that keep up: full speed, nobody is behind ({net.Server.NextTick} ticks in 3 s)");

        // Client 2 stops playing. Up to 2 s behind it is only shown.
        Play(net, 1_000, 0);
        Check.That(other.Lag.TryGetValue(slow.ConnectionId, out var shown) && shown.LagMs >= 500 && !shown.Throttling && !net.Server.Throttling && other.SpeedPercent == 100,
            $"a lag from 500 ms on is told to everybody, and nothing else happens ({(other.Lag.TryGetValue(slow.ConnectionId, out var lag) ? lag.LagMs : -1)} ms)");
        var before = net.Server.NextTick;
        Play(net, 900, 0);
        Check.That(!net.Server.Throttling && Math.Abs(net.Server.NextTick - before - 54) <= 2, "below 2 s the server closes its ticks at full speed");

        // Beyond 2 s the game runs at half speed for everybody - told as the pace.
        Play(net, 300, 0);
        Check.That(net.Server.Throttling && other.SpeedPercent == 50 && other.Lag[slow.ConnectionId].Throttling, $"above 2 s the game is slowed (server {net.Server.Throttling}, pace {other.SpeedPercent})");
        before = net.Server.NextTick;
        Play(net, 2_000, 0);
        Check.That(Math.Abs(net.Server.NextTick - before - 60) <= 2, $"slowed, two seconds close the ticks of one ({net.Server.NextTick - before})");

        // It catches up: back to full speed once it is below half the limit, and the lag notice is withdrawn.
        Play(net, 400, 0, 1);
        Check.That(!net.Server.Throttling && other.SpeedPercent == 100 && other.Lag.Count == 0 && slow.IsOpen, "a member that caught up ends the slowing; the notice is withdrawn");
        var throttled = net.Server.ThrottledMicros;
        Check.That(throttled is >= 2_000_000 and <= 2_700_000, $"the game ran slowed for as long as it was above the limit ({throttled / 1000} ms)");

        // It stops for good: 2 s shown, 10 s slowed, then it is dropped - and its leave is a command in the next bundle.
        Play(net, 2_100, 0);
        Check.That(net.Server.Throttling, "slowed again");
        Play(net, 9_800, 0);
        Check.That(slow.IsOpen && net.Server.DroppedForLag == 0, "not dropped before the limit");
        Play(net, 400, 0);
        Check.That(net.Server.DroppedForLag == 1 && !slow.IsOpen && !net.Server.Throttling && other.SpeedPercent == 100, "after 10 s slowed the member is dropped and the game runs at full speed again");
        Check.That(other.Dropped.Count == 1 && other.Dropped[0].Reason == DropReason.TooSlow && other.Dropped[0].Player == 2 && other.Lag.Count == 0, "everybody is told who was dropped and why");
        Check.That(net.Server.ThrottledMicros - throttled is >= 9_900_000 and <= 10_300_000, $"the others were slowed for 10 s, not longer ({(net.Server.ThrottledMicros - throttled) / 1000} ms)");
        var leave = net.Server.Recording.Bundles.SelectMany(bundle => bundle.Commands.Select(command => (bundle.Tick, Command: command))).Single(entry => entry.Command.Origin == 0);
        var decoded = ReplayFormat.DecodeCommand(leave.Command.Command);
        Check.That(decoded is PlayerLeaveCommand { TransferToAlly: false } && decoded.Player == new PlayerId(2) && decoded.Tick == leave.Tick && leave.Command.Player == 2 && leave.Command.Order == 1,
            $"the leave of the dropped player is a command of the next bundle ({decoded})");
        Check.That(other.HasBundle(leave.Tick) && CommandWire.DecodeIntent(other.TakeBundle(leave.Tick).Commands.Single())(1, leave.Tick) is PlayerLeaveCommand, "a client takes the leave out of the bundle like any command");
        before = net.Server.NextTick;
        Play(net, 1_000, 0);
        Check.That(Math.Abs(net.Server.NextTick - before - 60) <= 2 && net.Server.Phase == RoomPhase.Match, "the match goes on for the others");

        // A watcher that falls behind slows nobody; it is only dropped.
        var watched = new Net(Rule());
        watched.Join();
        watched.StartMatch(2, TwoSeats());
        var third = watched.Clients[2];
        Check.That(watched.Server.Phase == RoomPhase.Match && third.Player == 0 && watched.Clients[0].Player == 1, "a third member watches");
        Play(watched, 11_900, 0, 1);
        Check.That(!watched.Server.Throttling && watched.Server.ThrottledMicros == 0 && third.IsOpen, "a watcher that is behind slows nobody");
        Play(watched, 400, 0, 1);
        Check.That(watched.Server.DroppedForLag == 1 && !third.IsOpen && watched.Server.Recording.Bundles.All(bundle => bundle.Commands.IsEmpty), "it is dropped after the same time, and no leave is needed for somebody who does not play");

        // The rule is configuration: off, the server neither tells, slows nor drops.
        var off = new Net(Rule() with { LagReportMs = 0, LagThrottleMs = 0 });
        off.StartMatch(2, TwoSeats());
        Play(off, 20_000, 0);
        Check.That(!off.Server.Throttling && off.Server.DroppedForLag == 0 && off.Clients[0].Lag.Count == 0 && Math.Abs(off.Server.NextTick - 1_200) <= 2, "with the rule off the server only closes ticks");
        // And every event is in the room's log.
        var names = new List<string>();
        while (net.Server.TryTakeEvent(out var relayEvent)) names.Add(relayEvent.Name);
        Check.That(string.Join(' ', names) == "joined joined match-started match-running behind behind throttled caught-up full-speed behind behind throttled left full-speed",
            $"the room's log tells the story ({string.Join(' ', names)})");
    }

    public static void LeavingAndPings()
    {
        // By choice: the leave carries the transfer to an ally, and the server closes the connection.
        var net = new Net();
        net.StartMatch(2, TwoSeats());
        net.Pump(4, 10);
        net.Clients[1].Leave();
        net.Pump(4, 10);
        var leave = net.Server.Recording.Bundles.SelectMany(bundle => bundle.Commands).Single(command => command.Origin == 0);
        Check.That(ReplayFormat.DecodeCommand(leave.Command) is PlayerLeaveCommand { TransferToAlly: true } && leave.Player == 2 && !net.Clients[1].IsOpen, "a client that says it leaves hands over to an ally");
        Check.That(net.Clients[0].Dropped.Single().Reason == DropReason.Left && net.Server.Phase == RoomPhase.Match, "the others are told, and play on");

        // A lost connection: everything of the player dies.
        var lost = new Net();
        lost.StartMatch(2, TwoSeats());
        lost.Clients[0].SendIntent(1, MoveIntent());
        lost.Clients[0].Close();
        lost.Pump(4, 10);
        var commands = lost.Server.Recording.Bundles.SelectMany(bundle => bundle.Commands).ToList();
        Check.That(commands.Count(command => command.Origin == 0) == 1 && ReplayFormat.DecodeCommand(commands.Single(command => command.Origin == 0).Command) is PlayerLeaveCommand { TransferToAlly: false },
            "a lost connection is a leave without an heir");
        Check.That(lost.Clients[1].Dropped.Single().Reason == DropReason.ConnectionLost, "the others are told");

        // A client cannot give the leave command itself, for itself or anybody.
        var cheat = new Net();
        cheat.StartMatch(2, TwoSeats());
        cheat.Clients[0].SendIntent(1, CommandWire.Leave(2, 0, true));
        cheat.Pump(4, 10);
        Check.That(cheat.Server.Discarded == 1 && cheat.Server.Recording.Bundles.All(bundle => bundle.Commands.IsEmpty), $"a leave as an intent is refused ({cheat.Server.LastDiscard})");

        // A player that vanishes while the match loads leaves in the first tick.
        var loading = new Net();
        for (var index = 0; index < 2; index++) loading.Join();
        loading.Pump();
        loading.Clients[0].Configure(TwoSeats());
        loading.Pump();
        for (var index = 0; index < 2; index++)
        {
            loading.Clients[index].RequestSlot(index, "Alpha", startPosition: index);
            loading.Clients[index].SetReady(true);
        }
        loading.Pump();
        loading.Clients[0].RequestStart();
        loading.Pump();
        loading.Clients[1].Close();
        loading.Clients[0].ReportLoaded(1);
        loading.Pump(4, 20);
        Check.That(loading.Server.Phase == RoomPhase.Match && loading.Server.Recording.Bundles[0].Commands.Single().Player == 2, "a player lost while loading leaves in the first tick");

        // Ping and pong: the server states its game time and the speed it advances at.
        var timed = new Net();
        var clocked = timed.Join(clocked: true);
        timed.StartMatch(2, TwoSeats());
        timed.Pump(400, 5);
        var now = timed.Clock.Microseconds;
        Check.That(clocked.Sync.SamplesTaken >= 5 && clocked.Sync.HasEstimate && clocked.Sync.SpeedPercent == 100, $"pings are answered with the server's game time ({clocked.Sync.SamplesTaken} samples)");
        Check.That(Math.Abs(clocked.Sync.GameMicrosAt(now) - timed.Server.GameMicros) <= 5_000 && timed.Server.GameMicros / 16_666 == timed.Server.NextTick,
            $"the estimate is the server's tick clock ({clocked.Sync.GameMicrosAt(now)} against {timed.Server.GameMicros})");
        timed.Clients[1].RequestPace(true, 100);
        timed.Pump(100, 5);
        Check.That(!clocked.Sync.HasEstimate && clocked.Sync.SpeedPercent == 0, "a paused server states a standing clock");
    }

    // ------------------------------------------------------------------------------------------------------ whole matches

    public static void MatchOverLossyUdp()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 900);
        var ticks = settings.MaxTicks;
        var plain = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks));
        Check.That(plain.Ok, $"the reference over the loopback: {plain.Problem}");

        // The same match over the UDP transport: datagrams delayed, 5 % lost, duplicated, reordered - bundles arrive in any order and at any time, and every client still
        // plays the same states in every tick.
        var rough = new Impairment(DelayMs: 40, JitterMs: 30, LossPerMille: 50, DuplicatePerMille: 30, ReorderPerMille: 30);
        var udp = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Transport: ProbeTransport.Udp, Impairment: rough, NetworkSeed: 3));
        Check.That(udp.Ok, $"over lossy UDP: {udp.Problem}");
        Check.That(udp.DatagramsLost > 100 && udp.LastAgreedTick == ticks - 1 && udp.ReplayedSessionHash == udp.FinalSessionHash && udp.Dropped.IsEmpty,
            $"the network really lost datagrams ({udp.DatagramsLost}), the server agreed on every tick and the recording replays");
        Check.That(udp.ThrottledMs == 0 && udp.Resyncs == 0, $"nobody was slowed and no client had to resynchronise ({udp.ThrottledMs} ms, {udp.Resyncs})");
        Check.That(udp.MeanPlaybackDelayMs is > 45 and < 160, $"the playback delay settled near the way plus the jitter ({udp.MeanPlaybackDelayMs:F0} ms)");
        Check.That(Math.Abs(udp.ClockMs - ticks * 1000 / settings.TickRate) <= 500, $"the match took its game time on the clock: nobody slowed it ({udp.ClockMs} ms for {ticks} ticks)");
        var again = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Transport: ProbeTransport.Udp, Impairment: rough, NetworkSeed: 3));
        Check.That(again.Ok && again.FinalSessionHash == udp.FinalSessionHash && again.Retransmissions == udp.Retransmissions, "the same network seed gives the same match and the same repetitions");
        // Without disturbance of the orders' timing the three runs are three different matches (the orders land in other ticks) - but each is one match on all clients.
        var clean = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Transport: ProbeTransport.Udp));
        Check.That(clean.Ok && clean.Retransmissions == 0 && clean.DatagramsLost == 0, $"over clean UDP: {clean.Problem}");
    }

    public static void MatchWithMembersThatVanish()
    {
        var fixture = Fixture.Create();
        var settings = fixture.Settings(3, 0, 1_500);
        var ticks = settings.MaxTicks;
        var rate = settings.TickRate;
        // The rule with short limits, so that a test of a few game seconds sees all of it.
        var rule = new RelayOptions(LagReportMs: 300, LagThrottleMs: 1_000, ThrottleLimitMs: 3_000);
        var udpOptions = new UdpOptions { TimeoutMs = 2_000 };
        LoopbackProbeOptions Options(ProbeDisturbanceKind kind, ProbeTransport transport, int member = 1, int slowEvery = 5)
            => new(ticks, Transport: transport, Impairment: transport == ProbeTransport.Udp ? new Impairment(DelayMs: 20, JitterMs: 10, LossPerMille: 10) : default,
                Disturbances: [new ProbeDisturbance(member, rate * 5, kind, slowEvery)], Relay: rule, Udp: udpOptions);

        // Leaves by choice: no slowing at all.
        var left = fixture.Probe().Run(settings, Options(ProbeDisturbanceKind.Leave, ProbeTransport.Udp));
        Check.That(left.Ok, $"a client leaves: {left.Problem}");
        Check.That(left.Dropped.Length == 1 && left.Dropped[0] is { Member: 1, Player: 2, Reason: DropReason.Left } && left.ThrottledMs == 0 && left.ReplayedSessionHash == left.FinalSessionHash,
            $"the others play on in step, unslowed, and the recording with the leave replays ({string.Join(", ", left.Dropped)}; {left.ThrottledMs} ms)");

        // The line is cut (UDP): the transport's timeout drops it; until then the rule shows, then slows - never longer than its limits.
        var cut = fixture.Probe().Run(settings, Options(ProbeDisturbanceKind.Cut, ProbeTransport.Udp));
        Check.That(cut.Ok, $"a line is cut: {cut.Problem}");
        Check.That(cut.Dropped.Length == 1 && cut.Dropped[0].Reason == DropReason.ConnectionLost && cut.ThrottledMs is > 500 and <= 1_300,
            $"a cut line is dropped by the timeout of 2 s; the game was slowed only between the limit of 1 s and that ({cut.ThrottledMs} ms)");

        // The process freezes (loopback: the connection stays open, nothing times out): the rule alone decides - 1 s shown, 3 s slowed, dropped.
        var frozen = fixture.Probe().Run(settings, Options(ProbeDisturbanceKind.Freeze, ProbeTransport.Loopback, member: 2));
        Check.That(frozen.Ok, $"a client freezes: {frozen.Problem}");
        Check.That(frozen.Dropped.Length == 1 && frozen.Dropped[0] is { Member: 2, Player: 3, Reason: DropReason.TooSlow } && frozen.ThrottledMs is >= 2_900 and <= 3_200,
            $"a frozen client slows the others for the limit of 3 s and no longer ({frozen.ThrottledMs} ms)");
        // Slowed at half speed for 3 s costs the others 1.5 s of game time: the match takes that much longer on the clock.
        var expected = ticks * 1000 / rate;
        Check.That(frozen.ClockMs - expected is >= 1_300 and <= 2_000, $"what the others lost is bounded by the rule ({frozen.ClockMs - expected} ms over {expected} ms)");

        // A client that plays slower than the tick rate (one tick per 25 ms where 16.7 are needed): it falls behind, the game is slowed, it hovers - and is dropped.
        var slow = fixture.Probe().Run(settings, Options(ProbeDisturbanceKind.Slow, ProbeTransport.Udp, member: 0));
        Check.That(slow.Ok, $"a client is too slow: {slow.Problem}");
        Check.That(slow.Dropped.Length == 1 && slow.Dropped[0] is { Member: 0, Reason: DropReason.TooSlow } && slow.ThrottledMs is >= 2_900 and <= 3_300,
            $"a client that cannot keep up is carried for the limit and then dropped ({string.Join(", ", slow.Dropped)}; {slow.ThrottledMs} ms)");

        // A short hitch - half a second of standing still - is nobody's business: the client catches up by playing several ticks per round.
        var hitch = fixture.Probe().Run(settings, new LoopbackProbeOptions(ticks, Transport: ProbeTransport.Udp, Impairment: new Impairment(DelayMs: 20), Relay: rule, Udp: udpOptions,
            Disturbances: [new ProbeDisturbance(1, rate * 5, ProbeDisturbanceKind.Hitch, 120)]));
        Check.That(hitch.Ok && hitch.Dropped.IsEmpty && hitch.ThrottledMs == 0, $"a hitch of 600 ms slows nobody and drops nobody: {hitch.Problem} ({hitch.ThrottledMs} ms)");
    }

    // ---------------------------------------------------------------------------------------------- rooms and the program

    public static void RoomsOfOneServer()
    {
        var clock = new ManualNetClock();
        var network = new LoopbackNetwork(clock);
        var seed = 1u;
        var directory = new RoomDirectory(clock, () => ++seed, new DirectoryOptions(MaxRooms: 2, HelloTimeoutMs: 1_000, Room: new RelayOptions(LagReportMs: 0, LagThrottleMs: 0)));
        var clients = new List<LockstepClient>();
        LockstepClient Join(string room)
        {
            var (client, server) = network.Connect();
            directory.Attach(server);
            var joined = new LockstepClient(client, $"{room} {clients.Count + 1}", Definitions, Released, room);
            clients.Add(joined);
            return joined;
        }
        void Pump(int rounds = 4, int ms = 0)
        {
            for (var round = 0; round < rounds; round++)
            {
                if (ms > 0) clock.Advance(ms);
                directory.Pump();
                foreach (var client in clients) client.Pump();
            }
        }
        // Two rooms by name; whoever names a room first is its host.
        var a1 = Join("alpha");
        var a2 = Join("alpha");
        var b1 = Join("beta");
        var b2 = Join("beta");
        Pump();
        Check.That(directory.RoomCount == 2 && a1.IsHost && !a2.IsHost && b1.IsHost && !b2.IsHost && a1.Room!.Members.Length == 2 && b1.Room!.Members.Length == 2,
            "a connection belongs to the room its hello names; each room has its own host and members");
        Check.That(a1.Room!.Settings.TickRate == 60, "a room nobody has set yet states the server's default tick rate");
        // A third room on a server for two is turned away with a reason.
        var c1 = Join("gamma");
        Pump();
        Check.That(c1.Phase == ClientPhase.Rejected && c1.Rejection!.Reason == RejectReason.ServerFull && !c1.IsOpen && directory.RoomCount == 2 && directory.TurnedAway == 1, "no room left: rejected as 'server full'");
        // Something that says no hello, or something else first.
        var (silent, silentEnd) = network.Connect();
        directory.Attach(silentEnd);
        var (rude, rudeEnd) = network.Connect();
        directory.Attach(rudeEnd);
        rude.Send(MessageCodec.Encode(new ReadyMessage(true)));
        Pump();
        Check.That(!rude.IsOpen && silent.IsOpen && directory.PendingConnections == 1, "a first message that is no hello closes the connection; a silent one may still speak");
        Pump(11, 100);
        Check.That(!silent.IsOpen && directory.PendingConnections == 0 && directory.TurnedAway == 3, "no hello within the time limit: closed");

        // Both rooms play at the same time, each at its own rate, and know nothing of each other.
        void Start(LockstepClient host, LockstepClient guest, int rate)
        {
            host.Configure(TwoSeats(rate));
            Pump();
            host.RequestSlot(0, "Alpha", startPosition: 0);
            guest.RequestSlot(1, "Beta", startPosition: 1);
            guest.SetReady(true);
            Pump();
            host.RequestStart();
            Pump();
            host.ReportLoaded(42);
            guest.ReportLoaded(42);
            Pump();
            Check.That(host.Phase == ClientPhase.InMatch && guest.Phase == ClientPhase.InMatch, $"the match of the room begins ({host.LastRefusal?.Text})");
        }
        Start(a1, a2, 60);
        Start(b1, b2, 30);
        Check.That(a1.Description!.Seed != b1.Description!.Seed, "every room gets seeds of its own from the server");
        a1.SendIntent(1, MoveIntent());
        Pump(100, 10);
        Check.That(Math.Abs(a1.NewestBundleTick - 60) <= 2 && Math.Abs(b1.NewestBundleTick - 30) <= 2, $"one second closes 60 ticks in one room and 30 in the other ({a1.NewestBundleTick}, {b1.NewestBundleTick})");
        var alpha = directory.Rooms.Single(room => room.Name == "alpha").Room;
        var beta = directory.Rooms.Single(room => room.Name == "beta").Room;
        Check.That(alpha.IntentsRelayed == 1 && beta.IntentsRelayed == 0 && directory.MicrosUntilNextTick is >= 0 and <= 16_666, "an order stays in its room; the directory knows when the next tick of any room is due");

        // A room is gone when its last connection is; its name is free again.
        b1.Close();
        b2.Close();
        Pump();
        Check.That(directory.RoomCount == 1 && directory.RoomsClosed == 1, "an empty room is removed");
        var d1 = Join("beta");
        Pump();
        Check.That(d1.Phase == ClientPhase.InRoom && d1.IsHost && directory.RoomCount == 2, "the name of a closed room opens a new one");
        var names = new List<string>();
        while (directory.TryTakeEvent(out var room, out var relayEvent)) names.Add($"{room}:{relayEvent.Name}");
        Check.That(names.Contains("alpha:room-opened") && names.Contains("beta:match-started") && names.Contains("beta:room-closed") && names.Contains(":turned-away"), $"the directory's log names the room of every event ({string.Join(' ', names)})");
    }

    public static void DedicatedServerReferences()
    {
        var root = RepositoryRoot();
        // The program references the protocol and the room, and through them the simulation's types of the wire - never content, the game layer, the presentation or a renderer.
        var direct = ToolReferencesOf(root, "CnCNew.DedicatedServer");
        Check.That(direct.SequenceEqual(["CnCNew.Net", "CnCNew.Server"]), $"the dedicated server references CnCNew.Net and CnCNew.Server only ({string.Join(", ", direct)})");
        var closure = new SortedSet<string>(direct, StringComparer.Ordinal);
        var queue = new Queue<string>(direct);
        while (queue.Count > 0)
            foreach (var reference in ReferencesOf(root, queue.Dequeue()))
                if (closure.Add(reference)) queue.Enqueue(reference);
        Check.That(closure.SetEquals(["CnCNew.Net", "CnCNew.Server", "CnCNew.Simulation", "CnCNew.Data"]), $"and through them nothing but the simulation and its data model ({string.Join(", ", closure)})");
        var project = File.ReadAllText(Path.Combine(root, "tools", "CnCNew.DedicatedServer", "CnCNew.DedicatedServer.csproj"));
        Check.That(!project.Contains("PackageReference", StringComparison.Ordinal), "it has no package of its own");
        var forbidden = new Regex(@"using CnCNew\.(?!Net;|Net\.Udp;|Server;)|\b(GameData|TemplateCatalog|SimulationWorld|SkirmishMatchSession|ContentFileSystem|TerrainMap)\b|Stride");
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "tools", "CnCNew.DedicatedServer"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
                if (!lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal) && forbidden.IsMatch(lines[index])) problems.Add($"{Path.GetFileName(file)}({index + 1}): {lines[index].Trim()}");
        }
        Check.That(problems.Count == 0, "the dedicated server names no game data, no simulation and no renderer: " + string.Join("; ", problems));
        Check.That(forbidden.IsMatch("using CnCNew.Game.Data;") && forbidden.IsMatch("var data = GameData.Load(path);") && !forbidden.IsMatch("using CnCNew.Net.Udp;"), "the rule finds what it forbids");
    }

    private static string[] ToolReferencesOf(string root, string project)
        => [..Regex.Matches(File.ReadAllText(Path.Combine(root, "tools", project, project + ".csproj")), @"ProjectReference Include=""[^""]*?([A-Za-z.]+)\.csproj""").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal)];

    public static void MatchOverRealSockets()
    {
        // The whole stack as the dedicated server runs it - socket, cookie handshake, directory, room - and three clients on sockets of their own, in this process, on the
        // loopback interface, by the real clock; bounded to twenty seconds. Two play, one watches; game time: three seconds.
        var fixture = Fixture.Create();
        var settings = fixture.Settings(2, 1, 180);
        var ticks = settings.MaxTicks;
        var clock = new SystemNetClock();
        using var timer = new FineTimer();
        using var serverSocket = new UdpSocketChannel(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var endpoint = new UdpServerEndpoint(serverSocket, clock, [..Enumerable.Range(7, 32).Select(value => (byte)value)]);
        var seed = 100u;
        var directory = new RoomDirectory(clock, () => ++seed, new DirectoryOptions(Room: new RelayOptions(AllowModifiedBuilds: true)));
        var map = new MatchMap(fixture.Terrain, fixture.Map.Objects);
        var sockets = new List<UdpSocketChannel>();
        var endpoints = new List<UdpClientEndpoint>();
        var clients = new List<HeadlessClient>();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                var socket = new UdpSocketChannel(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
                sockets.Add(socket);
                var clientEndpoint = new UdpClientEndpoint(socket, serverSocket.LocalEndPoint, clock, 0xFEED0000UL + (ulong)index);
                endpoints.Add(clientEndpoint);
                clients.Add(new HeadlessClient(clientEndpoint.Connection, $"socket {index + 1}", fixture.Synthetic.Templates, fixture.Synthetic.Fingerprint, _ => map, "sockets", clock));
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool configured = false, seated = false, started = false;
            while (watch.Elapsed.TotalSeconds < 20 && clients.Any(client => client.Tick < ticks) && clients.All(client => client.Problem.Length == 0))
            {
                endpoint.Pump();
                while (endpoint.TryAccept(out var link, out _)) directory.Attach(link);
                directory.Pump();
                endpoint.Flush();
                for (var index = 0; index < clients.Count; index++)
                {
                    endpoints[index].Pump();
                    clients[index].Update(0.002, ticks, 8);
                    endpoints[index].Flush();
                }
                var host = clients.FirstOrDefault(client => client.Client.IsHost)?.Client;
                if (host is not null && !configured && clients.All(client => client.Client.Phase == ClientPhase.InRoom))
                {
                    configured = true;
                    host.Configure(settings);
                }
                if (configured && !seated && clients.All(client => client.Client.Room is { } room && room.Settings.Slots.Length == settings.Slots.Length))
                {
                    seated = true;
                    for (var index = 0; index < 2; index++)
                    {
                        clients[index].Client.RequestSlot(index, settings.Slots[index].Faction, startPosition: settings.Slots[index].StartPosition);
                        clients[index].Client.SetReady(true);
                    }
                }
                if (seated && !started && host!.Room!.Members.Count(member => member.Slot >= 0 && (member.Ready || member.IsHost)) == 2)
                {
                    started = true;
                    host.RequestStart();
                }
                serverSocket.WaitReadable(1_000);
            }
            Check.That(clients.All(client => client.Problem.Length == 0), "no client has a problem: " + string.Join("; ", clients.Select(client => client.Problem)));
            Check.That(clients.All(client => client.Tick == ticks), $"every client played the match within twenty seconds ({string.Join(", ", clients.Select(client => client.Tick))} of {ticks}; {watch.Elapsed.TotalSeconds:F1} s)");
            Check.That(clients.All(client => client.Hashes.SequenceEqual(clients[0].Hashes)) && clients[0].Hashes.Count == ticks, "the session hashes of all three are equal in every tick");
            Check.That(clients[2].Client.Player == 0 && clients[0].Client.Player == 1 && clients[0].Client.IntentsSent > 0, "two played, one watched");
            Check.That(watch.Elapsed.TotalSeconds >= 2.9, $"the match took its game time: the server paced it by the real clock ({watch.Elapsed.TotalSeconds:F1} s for 3 s)");
            Check.That(clients.All(client => client.Driver!.Playback.Synced && client.Client.Sync.RoundTripMicros is >= 0 and < 50_000), "every client synchronised with the server's clock");
            var room = directory.Rooms.Single().Room;
            for (var round = 0; round < 500 && room.LastAgreedTick < ticks - 1; round++)
            {
                endpoint.Pump();
                directory.Pump();
                endpoint.Flush();
                for (var index = 0; index < clients.Count; index++)
                {
                    endpoints[index].Pump();
                    endpoints[index].Flush();
                }
                serverSocket.WaitReadable(1_000);
            }
            Check.That(room.LastAgreedTick == ticks - 1 && room.Desync is null && room.Discarded == 0, $"the server agreed on every tick ({room.LastAgreedTick} of {ticks - 1})");
        }
        finally
        {
            foreach (var socket in sockets) socket.Dispose();
        }
    }
}
