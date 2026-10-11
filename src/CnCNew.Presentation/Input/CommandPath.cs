using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>
/// What the one who gave an order gets back at once: a number to recognise the final answer by, and the result of the local pre-check against the world as it is now
/// (<see cref="SimulationWorld.Check"/>). The pre-check is advice - the order is executed later, at the tick its scheduler gives it, and only the answer there counts
/// (<see cref="CommandResolution"/>).
/// </summary>
/// <param name="Number">Unique per command path, counted from 1; 0 for an order that was refused without being scheduled (a spectator's) and for a command that came from
/// another machine in a bundle.</param>
public readonly record struct CommandReceipt(long Number, SubmitResult PreCheck)
{
    public bool Accepted => PreCheck.Accepted;
    public CommandRejection Reason => PreCheck.Reason;

    /// <summary>The pre-check as a result, for the callers that only ask "did it go through so far".</summary>
    public static implicit operator SubmitResult(CommandReceipt receipt) => receipt.PreCheck;
}

/// <summary>The final answer to an order: the command as it was submitted (with its tick and sequence number) and what the simulation said at that moment.</summary>
public readonly record struct CommandResolution(long Receipt, PlayerId Player, SimCommand Command, SubmitResult PreCheck, SubmitResult Result, long IssuedTick)
{
    /// <summary>The simulation answered differently at execution than the pre-check did when the order was given.</summary>
    public bool Deviates => PreCheck.Accepted != Result.Accepted || PreCheck.Reason != Result.Reason;
}

/// <summary>An order on its way: the intent (a command without tick and sequence number, which whoever submits it fills in), its receipt and when it was given.</summary>
public readonly record struct ScheduledCommand(long Receipt, PlayerId Player, Func<ulong, long, SimCommand> Intent, SubmitResult PreCheck, long IssuedTick);

/// <summary>
/// Decides at which tick an order is executed. Locally that is "this tick" (or this tick plus an artificial delay); on the network the server decides. The commands of a tick
/// form its bundle; a tick may only run when its bundle is present, and the bundle is submitted immediately before the step of its tick, in the order <see cref="Take"/>
/// returns - by player, then by receipt number -, so that every machine submits the same commands at the same tick boundary in the same order.
/// </summary>
public interface ICommandScheduler
{
    /// <summary>How many ticks lie between an order and its execution as far as the scheduler knows (0: this tick). The presentation uses it to wait for its own orders.</summary>
    int DelayTicks { get; }

    /// <summary>Takes an order given while the world stood at <paramref name="now"/>.</summary>
    void Schedule(ScheduledCommand command, long now);

    /// <summary>Whether the bundle of <paramref name="tick"/> is complete, i.e. the tick may run.</summary>
    bool IsPresent(long tick);

    /// <summary>Removes and returns the bundle of <paramref name="tick"/> (and anything that is overdue), in canonical order.</summary>
    IReadOnlyList<ScheduledCommand> Take(long tick);

    /// <summary>Orders that are scheduled and not yet taken.</summary>
    int Pending { get; }
}

/// <summary>
/// The local scheduler: an order given at tick T is executed at tick T + <see cref="DelayTicks"/>. With a delay of 0 that is the tick the order was given in - the behaviour
/// the game always had. A delay above 0 is the lockstep probe: the game is played as if every order travelled to a server and back.
/// </summary>
public sealed class LocalCommandScheduler(int delayTicks = 0) : ICommandScheduler
{
    private readonly SortedDictionary<long, List<ScheduledCommand>> _byTick = [];

    public int DelayTicks { get; } = delayTicks >= 0 ? delayTicks : throw new ArgumentOutOfRangeException(nameof(delayTicks));
    public int Pending { get; private set; }

    public void Schedule(ScheduledCommand command, long now)
    {
        var tick = now + DelayTicks;
        if (!_byTick.TryGetValue(tick, out var list)) _byTick[tick] = list = [];
        list.Add(command);
        Pending++;
    }

    /// <summary>Locally nothing can still arrive for a tick that is about to run: every order for it was given at least <see cref="DelayTicks"/> ticks ago.</summary>
    public bool IsPresent(long tick) => true;

    public IReadOnlyList<ScheduledCommand> Take(long tick)
    {
        if (_byTick.Count == 0) return [];
        var due = new List<ScheduledCommand>();
        while (_byTick.Count > 0)
        {
            var (first, list) = _byTick.First();
            if (first > tick) break;
            due.AddRange(list);
            _byTick.Remove(first);
        }
        Pending -= due.Count;
        // Canonical order: by player, then in the order the orders were given (the receipt number).
        due.Sort(static (a, b) =>
        {
            var byPlayer = a.Player.Value.CompareTo(b.Player.Value);
            return byPlayer != 0 ? byPlayer : a.Receipt.CompareTo(b.Receipt);
        });
        return due;
    }
}

/// <summary>
/// The two-stage command path. Stage one (<see cref="Issue"/>): the presentation hands over an intent and gets the pre-check and a receipt at once. Stage two
/// (<see cref="SubmitDue"/>): immediately before the step of a tick the commands scheduled for it are submitted to the session, which assigns tick and sequence number
/// (per player, in the order of submission - so a player's numbers rise strictly whoever else submits in between); the answer is the final one and is reported with the
/// receipt (<see cref="Resolved"/>). Whether an order is executed in the tick it was given or later is the scheduler's business; nothing else changes.
/// </summary>
public sealed class CommandPath
{
    private readonly SimulationWorld _world;
    private readonly Func<PlayerId, Func<ulong, long, SimCommand>, SubmitResult> _submit;
    private long _receipts;

    /// <param name="submit">The session's submission: it builds the command of a player at the current tick with that player's next sequence number, submits and records it
    /// (<c>SkirmishMatchSession.Issue</c>, <c>MissionSession.Issue</c>).</param>
    public CommandPath(SimulationWorld world, Func<PlayerId, Func<ulong, long, SimCommand>, SubmitResult> submit, ICommandScheduler? scheduler = null)
    {
        _world = world;
        _submit = submit;
        Scheduler = scheduler ?? new LocalCommandScheduler();
    }

    /// <summary>
    /// The sink of one player on this path: what the input layer of that player is given. There is no sink that goes around the path (the direct sinks of a skirmish and a
    /// mission session were removed in phase 25): a headless driver that wants an order executed in the tick it gives it uses a path with the local scheduler and calls
    /// <see cref="SubmitDue"/> before its step, or submits to the session itself.
    /// </summary>
    public ICommandSink For(PlayerId player) => new PlayerSink(this, player);

    private sealed class PlayerSink(CommandPath path, PlayerId player) : ICommandSink
    {
        public CommandReceipt Issue(Func<ulong, long, SimCommand> make) => path.Issue(player, make);
        public int DelayTicks => path.Scheduler.DelayTicks;
    }

    public ICommandScheduler Scheduler { get; private set; }

    /// <summary>Orders handed over, final answers given, and how many of those differed from their pre-check.</summary>
    public long Issued { get; private set; }
    public long ResolvedCount { get; private set; }
    public long Deviations { get; private set; }

    /// <summary>Raised for every order when it is given, with the command as the pre-check saw it (no sequence number yet) and the pre-check's answer.</summary>
    public event Action<SimCommand, CommandReceipt>? PreChecked;

    /// <summary>Raised for every order when it is submitted, with the final answer.</summary>
    public event Action<CommandResolution>? Resolved;

    /// <summary>Replaces the scheduler; only while nothing is on its way.</summary>
    public void UseScheduler(ICommandScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        if (Scheduler.Pending > 0) throw new InvalidOperationException("The scheduler cannot be replaced while orders are on their way.");
        Scheduler = scheduler;
    }

    public CommandReceipt Issue(PlayerId player, Func<ulong, long, SimCommand> intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var now = _world.Tick;
        // The command as it would be if it were submitted now; its sequence number is not assigned yet (0) and is not part of the pre-check.
        var probe = intent(0, now);
        var receipt = new CommandReceipt(++_receipts, _world.Check(probe));
        Issued++;
        Scheduler.Schedule(new ScheduledCommand(receipt.Number, player, intent, receipt.PreCheck, now), now);
        PreChecked?.Invoke(probe, receipt);
        return receipt;
    }

    /// <summary>Whether the bundle of the world's current tick is present, i.e. whether the tick may run.</summary>
    public bool CanStep => Scheduler.IsPresent(_world.Tick);

    /// <summary>Submits the bundle of the world's current tick; call immediately before the step of that tick, and only then.</summary>
    public void SubmitDue()
    {
        var due = Scheduler.Take(_world.Tick);
        for (var index = 0; index < due.Count; index++)
        {
            var scheduled = due[index];
            SimCommand? made = null;
            var result = _submit(scheduled.Player, (sequence, tick) => made = scheduled.Intent(sequence, tick));
            ResolvedCount++;
            if (made is null) continue;
            var resolution = new CommandResolution(scheduled.Receipt, scheduled.Player, made, scheduled.PreCheck, result, scheduled.IssuedTick);
            // A command of another machine (receipt 0) has no pre-check here to deviate from.
            if (resolution.Deviates && scheduled.Receipt != 0) Deviations++;
            Resolved?.Invoke(resolution);
        }
    }
}
