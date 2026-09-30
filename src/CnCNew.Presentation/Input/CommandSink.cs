using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Presentation.Input;

/// <summary>
/// Where the input layer sends its orders. The sink assigns the sequence number and the tick (<see cref="SkirmishMatchSession.Issue"/>), so every
/// order is recorded and replayable; the input layer never mutates the world itself.
/// </summary>
public interface ICommandSink
{
    SubmitResult Issue(Func<ulong, long, SimCommand> make);
}

/// <summary>The live adapter: a human slot of a running skirmish.</summary>
public sealed class SkirmishSessionCommandSink(SkirmishMatchSession session, PlayerId player) : ICommandSink
{
    public SubmitResult Issue(Func<ulong, long, SimCommand> make) => session.Issue(player, make);
}
