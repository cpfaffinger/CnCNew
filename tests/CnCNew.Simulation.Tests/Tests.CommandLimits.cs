using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// What a player's command may state (docs/SIMULATION-CORE.md §49): a point far outside any map made a movement overflow in every simulation of a match - an order one
/// member of a network match could give to end it for all. Such a command is rejected like any other invalid one.
/// </summary>
internal static partial class Tests
{
    public static void CommandCoordinatesHaveALimit()
    {
        var (world, _, _) = PlayerLeaveWorld();
        var player = new PlayerId(1);
        var grunt = world.Entities.First(entity => entity.Template.Name == "Grunt" && entity.Owner == player && !entity.ContainedBy.IsValid);
        var limit = SimulationWorld.CommandCoordinateLimit;
        var beyond = Fixed.FromInt(600_000);
        ulong sequence = 1_000;

        // The order that used to end every simulation: a move to a point 600,000 units away. It is refused at the pre-check and at submission, and nothing happens.
        Check.That(world.Check(new SetMoveTargetCommand(player, 0, 0, grunt.Id, beyond, Fixed.Zero)) is { Accepted: false, Reason: CommandRejection.InvalidArguments },
            "a move beyond the coordinate limit is refused by the pre-check");
        var (x, y) = (grunt.X, grunt.Y);
        Check.That(!world.Submit(new SetMoveTargetCommand(player, ++sequence, world.Tick, grunt.Id, beyond, Fixed.Zero)).Accepted, "and at submission");
        Check.That(!world.Submit(new SetMoveTargetCommand(player, ++sequence, world.Tick, grunt.Id, Fixed.Zero, -beyond)).Accepted, "on either axis, either way");
        for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
        Check.That(grunt.X == x && grunt.Y == y, "the unit has not moved");

        // Every kind of order that names a point.
        Check.That(!world.Check(new AttackMoveCommand(player, 0, 0, grunt.Id, beyond, Fixed.Zero)).Accepted, "an attack-move beyond the limit");
        Check.That(!world.Check(new GroupMoveCommand(player, 0, 0, [grunt.Id], Fixed.Zero, beyond, false)).Accepted, "a group move beyond the limit");
        Check.That(!world.Check(new PursueCommand(player, 0, 0, grunt.Id, grunt.Id, beyond, beyond)).Accepted, "a pursuit beyond the limit");
        Check.That(!world.Check(new SetRallyPointCommand(player, 0, 0, grunt.Id, beyond, Fixed.Zero)).Accepted, "a rally point beyond the limit");
        Check.That(!world.Check(new EvacuateCommand(player, 0, 0, grunt.Id, true, beyond, Fixed.Zero)).Accepted, "an evacuation beyond the limit");
        Check.That(!world.Check(new ConstructCommand(player, 0, 0, grunt.Id, "OilTower", beyond, Fixed.Zero, Fixed.Zero)).Accepted, "a building site beyond the limit");
        Check.That(!world.Check(new ConstructCommand(player, 0, 0, grunt.Id, "OilTower", Fixed.Zero, Fixed.Zero, Fixed.FromInt(100_000))).Accepted, "a building turned by an absurd angle");
        Check.That(!world.Check(new UseSpecialPowerCommand(player, 0, 0, grunt.Id, "None", SpecialPowerTargetKind.Location, EntityId.Invalid, beyond, Fixed.Zero)).Accepted,
            "a special power aimed beyond the limit");
        Check.That(!world.Check(new SpawnCommand(player, 0, 0, "Grunt", player, beyond, Fixed.Zero, Fixed.Zero, Fixed.Zero)).Accepted, "a player's spawn beyond the limit");

        // The limit itself is a point a unit can be sent toward: the order stands, and the simulation steps on without an overflow.
        Check.That(world.Submit(new SetMoveTargetCommand(player, ++sequence, world.Tick, grunt.Id, limit, -limit)).Accepted, "a move to the limit itself is an order like any other");
        Check.That(!world.Check(new SetMoveTargetCommand(player, 0, 0, grunt.Id, Fixed.FromRaw(limit.Raw + 1), Fixed.Zero)).Accepted, "one step beyond it is not");
        for (var tick = 0; tick < world.Frames(300); tick++) world.Step();
        Check.That(grunt.X != x || grunt.Y != y, "the unit is on its way");

        // The system is not a player: fixtures, scripts and tools place things where they like.
        Check.That(world.Check(new SpawnCommand(PlayerId.System, 0, 0, "Grunt", player, beyond, Fixed.Zero, Fixed.Zero, Fixed.Zero)).Accepted, "the limit binds players, not the system");

        // Which commands are orders of a player at all - what a relay lets through by the type alone.
        Check.That(CommandTypes.IsPlayerOrder(CommandTypes.SetMoveTarget) && CommandTypes.IsPlayerOrder(CommandTypes.GroupMove) && CommandTypes.IsPlayerOrder(CommandTypes.Construct)
            && CommandTypes.IsPlayerOrder(CommandTypes.UseSpecialPower), "moves, building and powers are orders of a player");
        ushort[] not = [CommandTypes.Spawn, CommandTypes.Destroy, CommandTypes.Damage, CommandTypes.Kill, CommandTypes.SetTimer, CommandTypes.CancelTimer, CommandTypes.GiveMoney,
            CommandTypes.GiveSciencePoints, CommandTypes.SetRelationship, CommandTypes.PlayerLeave, 0, 999];
        Check.That(not.All(code => !CommandTypes.IsPlayerOrder(code)), "spawning, damage, timers, gifts, relations and the leave are not");
        var orders = Enumerable.Range(0, 100).Count(code => CommandTypes.IsPlayerOrder((ushort)code));
        Check.That(orders == 24, $"24 of the 34 command types are orders of a player ({orders})");
    }
}
