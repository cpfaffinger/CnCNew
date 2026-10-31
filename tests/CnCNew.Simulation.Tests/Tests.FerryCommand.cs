using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;

/// <summary><c>MSG_EXECUTE_RAILED_TRANSPORT</c> (F-19): the button of the ferry is a command of a player, the one order besides the evacuation that <c>RailedTransportAIUpdate::aiDoCommand</c> takes.</summary>
static partial class Tests
{
    public static void FerryCommandSendsTheFerry()
    {
        var world = LbWorld(out var feeder, out _, FerryTerrain());
        var ferry = CsSpawn(world, feeder, "LbFerry", 1, 390, 110);
        var ai = ferry.Modules.OfType<RailedTransportAIUpdate>().Single();
        var other = CsSpawn(world, feeder, "LbPassenger", 1, 300, 300);
        world.Step();
        CsRun(world, 40, () => !ai.InTransit);
        Check.That(!ai.InTransit && ai.CurrentPath == 0, "The ferry has arrived.");
        var player = new PlayerId(1);
        Check.That(world.Submit(new ExecuteRailedTransportCommand(player, 5_000, world.Tick, [ferry.Id, other.Id])).Accepted, "The order of a selection is accepted.");
        Check.That(!world.Submit(new ExecuteRailedTransportCommand(new PlayerId(2), 5_000, world.Tick, [ferry.Id])).Accepted, "A foreign ferry takes no order.");
        world.Step();
        Check.That(ai.InTransit && !ai.DockIsOpen && ai.CurrentPath == 1, "The ferry sets off on its next path with its dock closed.");
        Check.That(!other.HasMoveTarget, "Another unit ignores the order.");
        ExecuteRailedTransportCommand command = new(player, 7, 9, [ferry.Id, other.Id]);
        Check.That(CommandTypes.IsPlayerOrder(CommandTypes.ExecuteRailedTransport) && ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(command)).Equals(command), "The order is a player's and round-trips in the replay format.");
    }
}
