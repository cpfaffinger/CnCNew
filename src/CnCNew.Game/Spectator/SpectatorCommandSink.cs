using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;

namespace CnCNew.Game.Spectator;

/// <summary>
/// The command sink of a spectator: it refuses every order without building it, so nothing the viewer does (a click, a hotkey, a stray HUD button) can reach the session,
/// its replay or its hash. <see cref="Refused"/> counts the attempts for the report of a headless check.
/// </summary>
public sealed class SpectatorCommandSink : ICommandSink
{
    public int Refused { get; private set; }

    public SubmitResult Issue(Func<ulong, long, SimCommand> make)
    {
        Refused++;
        return SubmitResult.Reject(CommandRejection.NotPermitted);
    }
}
