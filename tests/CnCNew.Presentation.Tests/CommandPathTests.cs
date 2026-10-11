using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Phase 24, steps 6 to 8: the two-stage command path. An order is an intent that is pre-checked at once and submitted immediately before the step of the tick its scheduler
/// gave it; with no delay that is the game as it always was, with a delay it is the lockstep probe.
/// </summary>
internal static class CommandPathTests
{
    private static readonly PlayerId Me = InputTests.Me;
    private static readonly PlayerId Enemy = InputTests.Enemy;

    public static int Run(string[] args)
    {
        var run = new PathRun();
        run.Test("Command path: a pre-check changes nothing (hash and snapshot before and after 10,000 of them)", PreCheckHasNoSideEffect);
        run.Test("Command path: with no delay the path records the bytes of immediate submission", NoDelayIsTheOldPath);
        run.Test("Command path: the order of two players' commands inside a tick is the bundle's, whoever gave his order first", OrderInsideATickIsCanonical);
        run.Test("Command path: a delayed order is executed d ticks later and answered with its receipt; a changed answer is counted", DelayedOrdersAndDeviations);
        run.Test("Command path: sequence numbers rise strictly per player whatever the sources and the delay", SequenceNumbersRisePerPlayer);
        run.Test("Command path: standing orders give no command twice while one is on its way (delay 4 and 12)", StandingOrdersWithDelay);
        run.Test("Command path: a tick whose bundle is missing does not run", MissingBundleHoldsTheTick);
        return run.Finish();
    }

    private sealed class PathRun
    {
        private int _failed;
        private int _passed;

        public void Test(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine($"  ok   {name}");
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
                Console.Error.WriteLine(exception.StackTrace);
            }
        }

        public int Finish()
        {
            Console.WriteLine($"  command path: {_passed} passed, {_failed} failed");
            return _failed;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static long Rejections(SimulationWorld world, CommandRejection reason) => world.RejectionCounts[(int)reason];

    /// <summary>The session behind a path in these tests: the world itself, with a sequence counter per player, recording what it submits.</summary>
    private sealed class Session(InputTests.Arena arena)
    {
        private readonly Dictionary<int, ulong> _sequence = [];
        public List<SimCommand> Submitted { get; } = [];

        public SubmitResult Issue(PlayerId player, Func<ulong, long, SimCommand> make)
        {
            var next = _sequence.GetValueOrDefault(player.Value) + 1;
            _sequence[player.Value] = next;
            var command = make(next, arena.World.Tick);
            Submitted.Add(command);
            arena.Log.Add(command);
            return arena.World.Submit(command);
        }
    }

    private static (InputTests.Arena Arena, Session Session, CommandPath Path) NewPath(int delay)
    {
        var arena = new InputTests.Arena();
        var session = new Session(arena);
        return (arena, session, new CommandPath(arena.World, session.Issue, new LocalCommandScheduler(delay)));
    }

    /// <summary>One tick the way a game takes it: the bundle of the tick, then the step.</summary>
    private static void Step(InputTests.Arena arena, CommandPath path, int ticks = 1)
    {
        for (var index = 0; index < ticks; index++)
        {
            Assert(path.CanStep, "the bundle of a local tick is present");
            path.SubmitDue();
            arena.World.Step();
        }
    }

    private static void PreCheckHasNoSideEffect()
    {
        var arena = new InputTests.Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Tank", Me, 60, 40);
        arena.Spawn("Barracks", Me, 150, 150);
        arena.Spawn("Ranger", Enemy, 150, 40);
        arena.StepFrames(4);
        var ranger = arena.Find("Ranger", Me, 40, 40);
        var tank = arena.Find("Tank", Me, 60, 40);
        var barracks = arena.Find("Barracks", Me, 150, 150);
        var enemy = arena.Find("Ranger", Enemy, 150, 40);
        var world = arena.World;
        var tick = world.Tick;
        SimCommand[] probes =
        [
            new SetMoveTargetCommand(Me, 0, tick, ranger.Id, Fixed.FromInt(80), Fixed.FromInt(80)),
            new GroupMoveCommand(Me, 0, tick, [ranger.Id, tank.Id], Fixed.FromInt(90), Fixed.FromInt(60), false),
            new FireWeaponCommand(Me, 0, tick, tank.Id, enemy.Id),
            new QueueProductionCommand(Me, 0, tick, barracks.Id, "Ranger"),
            new SetMoveTargetCommand(Me, 0, tick, enemy.Id, Fixed.FromInt(80), Fixed.FromInt(80)),
            new SetMoveTargetCommand(Me, 0, tick, new EntityId(9_999), Fixed.FromInt(80), Fixed.FromInt(80)),
            new QueueProductionCommand(Me, 0, tick, barracks.Id, "NoSuchTemplate"),
            new SpawnCommand(Me, 0, tick, "Tank", Enemy, Fixed.FromInt(10), Fixed.FromInt(10), Fixed.Zero, Fixed.Zero),
        ];
        var hashBefore = world.StateHash;
        var snapshotBefore = WorldSnapshot.Write(world);
        var accepted = 0;
        var rejected = 0;
        for (var index = 0; index < 10_000; index++)
        {
            if (world.Check(probes[index % probes.Length]).Accepted) accepted++;
            else rejected++;
        }
        Assert(accepted >= 2_500 && rejected >= 2_500, $"the pre-checks cover accepted ({accepted}) and rejected ({rejected}) commands");
        world.VerifyIncrementalHash();
        Assert(world.StateHash == hashBefore, "10,000 pre-checks leave the state hash as it was");
        Assert(WorldSnapshot.Write(world).AsSpan().SequenceEqual(snapshotBefore), "10,000 pre-checks leave the snapshot as it was (no counter, no random stream, no event)");
        Assert(world.Check(probes[0]).Accepted && !world.Check(probes[4]).Accepted && world.Check(probes[4]).Reason == world.Submit(probes[4] with { Sequence = 1 }).Reason,
            "the pre-check answers what the submission answers");
    }

    /// <summary>The same orders once through immediate submission and once through the path with no delay.</summary>
    private static byte[] Record(bool throughPath)
    {
        var (arena, session, path) = NewPath(0);
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Tank", Me, 60, 40);
        arena.Spawn("Tank", Me, 70, 50);
        arena.Spawn("Barracks", Me, 150, 150);
        arena.StepFrames(4);
        var units = arena.World.Entities.Where(entity => entity.Owner == Me && !entity.Template.IsKindOf("STRUCTURE")).Select(entity => entity.Id).ToImmutableArray();
        var barracks = arena.Find("Barracks", Me, 150, 150);
        var sink = throughPath ? path.For(Me) : null;
        void Order(Func<ulong, long, SimCommand> make)
        {
            if (sink is not null) sink.Issue(make);
            else session.Issue(Me, make);
        }
        for (var tick = 0; tick < 400; tick++)
        {
            if (tick % 37 == 0) Order((sequence, at) => new GroupMoveCommand(Me, sequence, at, units, Fixed.FromInt(60 + tick / 4), Fixed.FromInt(90), false));
            if (tick % 53 == 5) Order((sequence, at) => new SetMoveTargetCommand(Me, sequence, at, units[0], Fixed.FromInt(30), Fixed.FromInt(30 + tick / 8)));
            if (tick == 100) Order((sequence, at) => new QueueProductionCommand(Me, sequence, at, barracks.Id, "Ranger"));
            if (tick == 100) Order((sequence, at) => new QueueProductionCommand(Me, sequence, at, barracks.Id, "NoSuchTemplate"));
            if (throughPath) Step(arena, path);
            else arena.Step();
        }
        Assert(session.Submitted.Count >= 20 && session.Submitted.OfType<GroupMoveCommand>().Count() >= 10, "the recording holds group orders and single orders");
        return [..ReplayFormat.Write(arena.Replay(arena.World.Tick)), ..BitConverter.GetBytes(arena.World.StateHash)];
    }

    private static void NoDelayIsTheOldPath()
    {
        var immediate = Record(throughPath: false);
        var path = Record(throughPath: true);
        Assert(immediate.AsSpan().SequenceEqual(path), "the path with no delay yields the replay bytes and the end hash of immediate submission (ticks, sequence numbers, rejected commands included)");
    }

    private static ulong TwoPlayersToOnePoint(bool enemyFirst, int delay)
    {
        var (arena, session, path) = NewPath(delay);
        for (var index = 0; index < 4; index++)
        {
            arena.Spawn("Ranger", Me, 40 + index * 8, 40);
            arena.Spawn("Ranger", Enemy, 40 + index * 8, 160);
        }
        arena.StepFrames(4);
        var mine = arena.World.Entities.Where(entity => entity.Owner == Me).Select(entity => entity.Id).ToImmutableArray();
        var theirs = arena.World.Entities.Where(entity => entity.Owner == Enemy).Select(entity => entity.Id).ToImmutableArray();
        var (x, y) = (Fixed.FromInt(100), Fixed.FromInt(100));
        Func<ulong, long, SimCommand> first = (sequence, tick) => new GroupMoveCommand(Me, sequence, tick, mine, x, y, false);
        Func<ulong, long, SimCommand> second = (sequence, tick) => new GroupMoveCommand(Enemy, sequence, tick, theirs, x, y, false);
        if (enemyFirst)
        {
            path.Issue(Enemy, second);
            path.Issue(Me, first);
        }
        else
        {
            path.Issue(Me, first);
            path.Issue(Enemy, second);
        }
        Step(arena, path, delay + 1);
        var moves = session.Submitted.OfType<GroupMoveCommand>().ToArray();
        Assert(moves.Length == 2 && moves[0].Player == Me && moves[1].Player == Enemy && moves[0].Tick == moves[1].Tick, "both group orders are submitted in one tick, the lower player number first");
        Step(arena, path, 600);
        return arena.World.StateHash;
    }

    private static void OrderInsideATickIsCanonical()
    {
        // A group order takes its free places when it is executed, the next one the next: which of two orders to one point comes first decides where every unit ends up.
        var meFirst = TwoPlayersToOnePoint(enemyFirst: false, delay: 0);
        var enemyFirst = TwoPlayersToOnePoint(enemyFirst: true, delay: 0);
        Assert(meFirst == enemyFirst, "two machines on which the players gave their orders in opposite order end in the same world");
        Assert(TwoPlayersToOnePoint(enemyFirst: false, delay: 4) == TwoPlayersToOnePoint(enemyFirst: true, delay: 4), "the same holds with an input delay of 4 ticks");

        // The control: the order does matter to the simulation - submitted in the other order, the units end up elsewhere.
        ulong Direct(bool enemyFirstSubmitted)
        {
            var arena = new InputTests.Arena();
            var session = new Session(arena);
            for (var index = 0; index < 4; index++)
            {
                arena.Spawn("Ranger", Me, 40 + index * 8, 40);
                arena.Spawn("Ranger", Enemy, 40 + index * 8, 160);
            }
            arena.StepFrames(4);
            var mine = arena.World.Entities.Where(entity => entity.Owner == Me).Select(entity => entity.Id).ToImmutableArray();
            var theirs = arena.World.Entities.Where(entity => entity.Owner == Enemy).Select(entity => entity.Id).ToImmutableArray();
            var (x, y) = (Fixed.FromInt(100), Fixed.FromInt(100));
            // The world executes a tick's commands by player number; to execute the enemy's first, the enemy orders one tick earlier.
            if (enemyFirstSubmitted)
            {
                session.Issue(Enemy, (sequence, tick) => new GroupMoveCommand(Enemy, sequence, tick, theirs, x, y, false));
                arena.Step();
                session.Issue(Me, (sequence, tick) => new GroupMoveCommand(Me, sequence, tick, mine, x, y, false));
                arena.Step(600);
            }
            else
            {
                session.Issue(Me, (sequence, tick) => new GroupMoveCommand(Me, sequence, tick, mine, x, y, false));
                arena.Step();
                session.Issue(Enemy, (sequence, tick) => new GroupMoveCommand(Enemy, sequence, tick, theirs, x, y, false));
                arena.Step(600);
            }
            return arena.World.StateHash;
        }
        Assert(Direct(enemyFirstSubmitted: false) != Direct(enemyFirstSubmitted: true), "executed in the other order, the same two group orders give another world (the order is not free)");
    }

    private static void DelayedOrdersAndDeviations()
    {
        foreach (var delay in new[] { 0, 4, 12 })
        {
            var (arena, session, path) = NewPath(delay);
            arena.Spawn("Ranger", Me, 40, 40);
            arena.Spawn("Tank", Me, 60, 40);
            arena.StepFrames(4);
            var ranger = arena.Find("Ranger", Me, 40, 40);
            var tank = arena.Find("Tank", Me, 60, 40);
            var resolutions = new List<CommandResolution>();
            var preChecks = new List<(SimCommand Command, CommandReceipt Receipt)>();
            path.Resolved += resolutions.Add;
            path.PreChecked += (command, receipt) => preChecks.Add((command, receipt));
            var issuedAt = arena.World.Tick;
            var sink = path.For(Me);
            Assert(sink.DelayTicks == delay, "the sink tells its delay");

            var move = sink.Issue((sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, ranger.Id, Fixed.FromInt(90), Fixed.FromInt(40)));
            var foreign = sink.Issue((sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, new EntityId(9_999), Fixed.FromInt(90), Fixed.FromInt(40)));
            var doomed = sink.Issue((sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, tank.Id, Fixed.FromInt(90), Fixed.FromInt(60)));
            Assert(move.Accepted && move.Number == 1 && !foreign.Accepted && foreign.Number == 2 && doomed.Accepted && doomed.Number == 3, "every order gets its pre-check and its receipt number at once");
            Assert(preChecks.Count == 3 && preChecks[0].Command.Sequence == 0 && preChecks[1].Receipt.Reason == foreign.Reason, "the pre-check is reported with the command as it stands (no sequence number yet)");
            Assert(session.Submitted.Count == 0 && resolutions.Count == 0 && path.Scheduler.Pending == 3, "nothing is submitted when the order is given");

            // The tank dies while its order is on its way (with a delay); with no delay the order is submitted before the kill.
            if (delay > 0)
            {
                Step(arena, path, delay - 1);
                Assert(resolutions.Count == 0, $"with a delay of {delay} nothing is answered before tick +{delay}");
                arena.System(sequence => new DestroyCommand(PlayerId.System, sequence, arena.World.Tick, tank.Id));
                Step(arena, path);
                Assert(arena.World.FindEntity(tank.Id) is not { } gone || !CnCNew.Presentation.Input.SimulationQueries.IsAlive(gone), "the tank is gone before its order arrives");
            }
            Step(arena, path, 2);
            Assert(resolutions.Count == 3 && resolutions.Select(resolution => resolution.Receipt).SequenceEqual([1L, 2L, 3L]), "every order is answered once, by its receipt number, in the order given");
            Assert(resolutions.All(resolution => resolution.Command.Tick == issuedAt + delay && resolution.IssuedTick == issuedAt), $"the orders are executed {delay} ticks after they were given");
            Assert(resolutions[0].Result.Accepted && !resolutions[0].Deviates && !resolutions[1].Result.Accepted && !resolutions[1].Deviates, "an answer that confirms its pre-check does not deviate, accepted or rejected");
            if (delay > 0)
            {
                Assert(!resolutions[2].Result.Accepted && resolutions[2].Deviates && path.Deviations == 1, "an order whose unit died on the way is rejected at execution: counted as a deviation from the pre-check");
            }
            else
            {
                Assert(resolutions[2].Result.Accepted && path.Deviations == 0, "with no delay the pre-check and the answer agree");
            }
            Assert(path.Issued == 3 && path.ResolvedCount == 3 && path.Scheduler.Pending == 0, "the path counts what it took and what it answered");
            Step(arena, path, 300);
            Assert((float)arena.World.FindEntity(ranger.Id)!.X.ToDecimal() > 80f, "the delayed order is carried out");
        }
        Assert(new Func<bool>(() =>
        {
            var (arena, _, path) = NewPath(0);
            path.Issue(Me, (sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, new EntityId(1), Fixed.Zero, Fixed.Zero));
            try { path.UseScheduler(new LocalCommandScheduler(4)); return false; }
            catch (InvalidOperationException) { return true; }
        })(), "the scheduler is not replaced while an order is on its way");
    }

    private static void SequenceNumbersRisePerPlayer()
    {
        foreach (var delay in new[] { 0, 4 })
        {
            var (arena, session, path) = NewPath(delay);
            arena.Spawn("Ranger", Me, 40, 40);
            arena.Spawn("Ranger", Enemy, 150, 150);
            arena.StepFrames(4);
            var mine = arena.Find("Ranger", Me, 40, 40);
            var theirs = arena.Find("Ranger", Enemy, 150, 150);
            for (var tick = 0; tick < 120; tick++)
            {
                // The human through the path (delayed), a computer player and the session's own source directly at this tick - as a session with an AI does it.
                if (tick % 3 == 0) path.Issue(Me, (sequence, at) => new SetMoveTargetCommand(Me, sequence, at, mine.Id, Fixed.FromInt(40 + tick % 30), Fixed.FromInt(40)));
                if (tick % 7 == 0) path.Issue(Me, (sequence, at) => new SetMoveTargetCommand(Me, sequence, at, mine.Id, Fixed.FromInt(40), Fixed.FromInt(40 + tick % 30)));
                path.SubmitDue();
                if (tick % 5 == 0) session.Issue(Enemy, (sequence, at) => new SetMoveTargetCommand(Enemy, sequence, at, theirs.Id, Fixed.FromInt(150 - tick % 30), Fixed.FromInt(150)));
                arena.World.Step();
            }
            foreach (var player in new[] { Me, Enemy })
            {
                var sequences = session.Submitted.Where(command => command.Player == player).Select(command => command.Sequence).ToArray();
                Assert(sequences.Length > 20 && sequences.SequenceEqual(Enumerable.Range(1, sequences.Length).Select(value => (ulong)value)), $"player {player.Value}: the sequence numbers are 1, 2, 3, ... in the order of submission (delay {delay})");
            }
            Assert(Rejections(arena.World, CommandRejection.OutOfOrder) == 0 && Rejections(arena.World, CommandRejection.Duplicate) == 0 && Rejections(arena.World, CommandRejection.Late) == 0,
                "no command is refused for its number or its tick");
        }
    }

    private static void StandingOrdersWithDelay()
    {
        foreach (var delay in new[] { 0, 4, 12 })
        {
            var (arena, session, path) = NewPath(delay);
            arena.Spawn("Tank", Me, 40, 40);
            arena.Spawn("Ranger", Me, 40, 80);
            arena.Spawn("Barracks", Enemy, 110, 40);
            arena.StepFrames(4);
            var tank = arena.Find("Tank", Me, 40, 40);
            var ranger = arena.Find("Ranger", Me, 40, 80);
            var target = arena.Find("Barracks", Enemy, 110, 40);
            var orders = new StandingOrders();
            var sink = path.For(Me);
            orders.Set(tank.Id, new StandingOrder(StandingOrderKind.Attack, target.Id, new Vector2(110, 40), [], arena.World.Tick));
            // A waypoint path: the first point is ordered by the caller, the others by the standing order as each is reached.
            var points = new[] { new Vector2(60, 80), new Vector2(60, 110), new Vector2(90, 110) };
            sink.Issue((sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, ranger.Id, SimulationQueries.ToFixed(points[0].X), SimulationQueries.ToFixed(points[0].Y)));
            foreach (var point in points) orders.AddWaypoint(ranger.Id, point, arena.World.Tick);

            var frameTicks = arena.World.Time.OriginalFrames(1);
            var lastIssue = new Dictionary<(uint Unit, Type Kind), long>();
            var tooSoon = 0;
            path.PreChecked += (command, _) =>
            {
                var unit = command switch { FireWeaponCommand fire => fire.Source.Value, PursueCommand pursue => pursue.Unit.Value, SetMoveTargetCommand move => move.Target.Value, _ => 0u };
                var key = (unit, command.GetType());
                // While a command is on its way (delay ticks) the same unit must not be given the same kind of command again.
                if (delay > 0 && lastIssue.TryGetValue(key, out var last) && arena.World.Tick - last <= delay) tooSoon++;
                lastIssue[key] = arena.World.Tick;
            };
            for (var tick = 0; tick < 1_500 && arena.World.FindEntity(target.Id) is { } alive && SimulationQueries.IsAlive(alive); tick++)
            {
                orders.Update(arena.World, Me, make => sink.Issue(make), sink.DelayTicks);
                Step(arena, path);
            }
            for (var tick = 0; tick < 600; tick++)
            {
                orders.Update(arena.World, Me, make => sink.Issue(make), sink.DelayTicks);
                Step(arena, path);
            }
            Assert(arena.World.FindEntity(target.Id) is not { } left || !SimulationQueries.IsAlive(left), $"delay {delay}: the standing attack destroys its target");
            Assert(tooSoon == 0, $"delay {delay}: no unit is given a command again while its last one of that kind is on its way ({tooSoon} such commands)");
            var waypointMoves = session.Submitted.OfType<SetMoveTargetCommand>().Where(command => command.Target == ranger.Id).ToArray();
            Assert(waypointMoves.Length == points.Length, $"delay {delay}: the first point is ordered by the caller and each further waypoint exactly once ({waypointMoves.Length} moves for {points.Length} points)");
            var end = arena.World.FindEntity(ranger.Id)!;
            Assert(Vector2.Distance(new Vector2((float)end.X.ToDecimal(), (float)end.Y.ToDecimal()), points[^1]) < 12f, $"delay {delay}: the unit arrives at the last waypoint");
            Assert(path.Scheduler.Pending == 0 || delay > 0, "nothing is left on its way without a delay");
            Assert(Rejections(arena.World, CommandRejection.Late) == 0, $"delay {delay}: no command arrives late");
        }
    }

    /// <summary>A scheduler whose bundles arrive only when the test says so: the stand-in for a server that has not sent a tick's commands yet.</summary>
    private sealed class WithholdingScheduler(int delay) : ICommandScheduler
    {
        private readonly LocalCommandScheduler _inner = new(delay);
        public long ArrivedThrough { get; set; } = -1;
        public int DelayTicks => _inner.DelayTicks;
        public int Pending => _inner.Pending;
        public void Schedule(ScheduledCommand command, long now) => _inner.Schedule(command, now);
        public bool IsPresent(long tick) => tick <= ArrivedThrough;
        public IReadOnlyList<ScheduledCommand> Take(long tick) => _inner.Take(tick);
    }

    private static void MissingBundleHoldsTheTick()
    {
        var arena = new InputTests.Arena();
        var session = new Session(arena);
        var scheduler = new WithholdingScheduler(2);
        var path = new CommandPath(arena.World, session.Issue, scheduler);
        arena.Spawn("Ranger", Me, 40, 40);
        scheduler.ArrivedThrough = long.MaxValue;
        for (var index = 0; index < arena.World.Time.OriginalFrames(4); index++) { path.SubmitDue(); arena.World.Step(); }
        var ranger = arena.Find("Ranger", Me, 40, 40);
        var start = arena.World.Tick;
        scheduler.ArrivedThrough = start + 1;
        path.Issue(Me, (sequence, tick) => new SetMoveTargetCommand(Me, sequence, tick, ranger.Id, Fixed.FromInt(90), Fixed.FromInt(40)));
        var stepped = 0;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!path.CanStep) continue;
            path.SubmitDue();
            arena.World.Step();
            stepped++;
        }
        Assert(stepped == 2 && arena.World.Tick == start + 2 && session.Submitted.Count == 0, "the ticks whose bundles arrived run; the tick whose bundle is missing does not, however often it is asked");
        scheduler.ArrivedThrough = start + 2;
        Assert(path.CanStep, "the tick runs once its bundle is there");
        path.SubmitDue();
        arena.World.Step();
        Assert(session.Submitted.Count == 1 && session.Submitted[0].Tick == start + 2, "the order is submitted with the bundle of its tick");
    }
}
