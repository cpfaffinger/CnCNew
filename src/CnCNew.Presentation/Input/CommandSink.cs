using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Presentation.Input;

/// <summary>
/// Where the input layer sends its orders. What it hands over is a command <b>intent</b>: the command without its tick and its sequence number, as a function that builds it
/// once both are known. It gets a <see cref="CommandReceipt"/> back at once - the local pre-check and a number -; when and with which number the command is executed is decided
/// behind the sink (<see cref="CommandPath"/>, <see cref="ICommandScheduler"/>), and the final answer comes with the receipt number when it is. The input layer never mutates
/// the world itself.
/// </summary>
public interface ICommandSink
{
    CommandReceipt Issue(Func<ulong, long, SimCommand> make);

    /// <summary>
    /// How many ticks an order needs before the simulation executes it, as far as the sink knows (0: the tick it is given in). Who gives orders from what the world shows
    /// (<see cref="StandingOrders"/>) waits that long before it judges the world again.
    /// </summary>
    int DelayTicks => 0;
}

/// <summary>
/// A human slot of a skirmish session without a scheduler in between: the order is submitted in the tick it is given, and the receipt's pre-check <i>is</i> the final answer.
/// For headless drivers that step the session themselves (tests, tools); a game goes through <see cref="CommandPath"/>.
/// </summary>
public sealed class SkirmishSessionCommandSink(SkirmishMatchSession session, PlayerId player) : ICommandSink
{
    private long _receipts;
    public CommandReceipt Issue(Func<ulong, long, SimCommand> make) => new(++_receipts, session.Issue(player, make));
}

/// <summary>
/// The human side of a mission session without a scheduler in between (see <see cref="SkirmishSessionCommandSink"/>); the human shares the session's single per-player sequence
/// counter with the scripts.
/// </summary>
public sealed class MissionCommandSink(MissionSession session, PlayerId player) : ICommandSink
{
    private long _receipts;
    public CommandReceipt Issue(Func<ulong, long, SimCommand> make) => new(++_receipts, session.Issue(player, make));
}
