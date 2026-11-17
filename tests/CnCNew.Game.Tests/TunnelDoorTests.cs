using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Real data (#428): units leave a GLA tunnel by its door. In the original an exit order or an evacuation sends a rider through <c>OpenContain::exitObjectViaDoor</c>
/// (<c>AIExitState::update</c>; <c>TunnelContain::scatterToNearbyPosition</c> is never called): it is set on the bone <c>ExitStart</c> of the tunnel's model (<c>UBUndTunn</c>) with the
/// tunnel's heading and walks to <c>ExitEnd</c>, then on to the tunnel's rally point. Before, the port put it at a random point 1 to 1.5 bounding radii away.
/// </summary>
internal static class TunnelDoorTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(TunnelDoorTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP TunnelDoorTests (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data (#428): riders leave a GLA tunnel by its door and walk to its end, and on to the rally point (#417), at 30, 60 and 120 Hz", () => ByTheDoor(data));
        harness.Run("Real data (#435): the exit button on tunnel B's bar lets out a rebel that entered tunnel A by B's door, at 30, 60 and 120 Hz", () => ByTheSelectedTunnel(data));
        return harness.Failures;
    }

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "GLA"), new(new PlayerId(2), "America")];

    private static void Assert(bool condition, string message) => TestHarness.Assert(condition, message);

    private static Fixed Distance(Fixed x, Fixed y, (Fixed X, Fixed Y) point) => Fixed.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y));

    /// <summary>
    /// #435: <c>GUI_COMMAND_EXIT_CONTAINER</c> sends <c>MSG_EXIT</c> with the rider, and the logic takes the container from the selection (<c>getSingleObjectFromSelection</c>): the bar of tunnel B
    /// shows the network's riders (<c>TunnelContain</c> iterates the <c>TunnelTracker</c>), and pressing one lets it out by B's door, whichever tunnel it entered. Before, the command named only
    /// the rider and it left by the tunnel it entered. Pressed through the command bar and the controller.
    /// </summary>
    private static void ByTheSelectedTunnel(GameData data)
    {
        var door = data.Templates.Geometry.ExitPaths["GLATunnelNetwork"][0];
        var buttons = CommandButtonCatalog.Build(data.Graph);
        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var templates = TemplateCatalog.Build(data.Graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, data.Templates.Geometry, data.Rules);
            var terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(120, 120, 1), "tunnel-bar");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, Players, "tunnel-bar", "tunnel-bar", terrain.Fingerprint), templates, terrain);
            ulong sequence = 0;
            void Submit(SimCommand command, string what) => Assert(world.Submit(command).Accepted, $"{rate}: {what}");
            void Spawn(string template, int x, int y, Fixed angle)
                => Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, angle), $"{template} spawns");
            Spawn("GLATunnelNetwork", 300, 300, Fixed.Zero);
            Spawn("GLATunnelNetwork", 700, 700, FixedTrig.Pi / Fixed.FromInt(3));
            Spawn("GLAInfantryRebel", 400, 300, Fixed.Zero);
            for (var step = 0; step < world.Time.OriginalFrames(30); step++) world.Step();
            var tunnels = world.Entities.Where(value => value.Template.Name == "GLATunnelNetwork").OrderBy(value => value.Id.Value).ToArray();
            var (a, b) = (tunnels[0], tunnels[1]);
            var rebel = world.Entities.Single(value => value.Template.Name == "GLAInfantryRebel");
            Submit(new EnterContainerCommand(new PlayerId(1), ++sequence, world.Tick, rebel.Id, a.Id), "the rebel enters tunnel A");
            for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
            Assert(rebel.ContainedBy == a.Id, $"{rate}: the rebel is in the network, by tunnel A");

            var selection = new SelectionModel(new PlayerId(1));
            selection.Replace([b.Id]);
            var bar = new CommandBarModel(buttons);
            var controller = new CommandModeController(new PlayerId(1), new WorldSink(world), selection);
            var view = bar.Build(world, selection);
            var place = view.Slots.FirstOrDefault(button => button is { Button.Kind: GuiCommandKind.ExitContainer } && button.Rider == rebel.Id);
            Assert(place is { IsEnabled: true } && place.Container == b.Id, $"{rate}: tunnel B's bar shows the rebel in a place of its own, for tunnel B ({place?.Slot}, {place?.Container})");
            Assert(bar.Activate(world, view, place!.Slot, controller) == ActivationResult.Issued, $"{rate}: the press is issued");
            var result = world.Step();
            Assert(result.Events.Any(value => value.Kind == SimEventKind.EntityUncontained && value.Entity == rebel.Id && value.Other == b.Id), $"{rate}: the rebel leaves through tunnel B");
            var (cos, sin) = (FixedTrig.Cos(b.Angle), FixedTrig.Sin(b.Angle));
            (Fixed X, Fixed Y) OnB(BonePose pose) => (b.X + pose.X * cos - pose.Y * sin, b.Y + pose.X * sin + pose.Y * cos);
            var start = OnB(door.Start!.Value);
            Assert(!rebel.ContainedBy.IsValid && Distance(rebel.X, rebel.Y, start) < Fixed.FromInt(2), $"{rate}: it comes out at tunnel B's ExitStart {start} ({rebel.X}, {rebel.Y}), not at tunnel A ({a.X}, {a.Y})");
            for (var step = 0; step < world.Time.OriginalFrames(30 * 10); step++) world.Step();
            var line = $"{rebel.X.Raw},{rebel.Y.Raw}";
            Console.WriteLine($"       {rate}: the rebel stands at ({rebel.X}, {rebel.Y})");
            reference ??= line;
            Assert(line == reference, $"{rate}: the same place as at 30 Hz");
        }
    }

    private sealed class WorldSink(SimulationWorld world) : ICommandSink
    {
        private ulong _sequence = 1000;

        public CommandReceipt Issue(Func<ulong, long, SimCommand> make)
        {
            var command = make(++_sequence, world.Tick);
            return new CommandReceipt((long)_sequence, world.Submit(command));
        }
    }

    private static void ByTheDoor(GameData data)
    {
        var exits = data.Templates.Geometry.ExitPaths;
        foreach (var name in new[] { "GLATunnelNetwork", "GLATunnelNetworkNoSpawn", "Chem_GLATunnelNetwork", "Demo_GLATunnelNetwork", "Slth_GLATunnelNetwork", "Boss_TunnelNetwork" })
            Assert(exits.TryGetValue(name, out var paths) && paths.Length == 1 && paths[0].Start is not null && paths[0].End is not null, $"{name} has the exit bones ExitStart and ExitEnd");
        var door = exits["GLATunnelNetwork"][0];
        Console.WriteLine($"       GLATunnelNetwork: ExitStart {door.Start}, ExitEnd {door.End}");

        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var templates = TemplateCatalog.Build(data.Graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, data.Templates.Geometry, data.Rules);
            var terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(120, 120, 1), "tunnel-door");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, Players, "tunnel-door", "tunnel-door", terrain.Fingerprint), templates, terrain);
            ulong sequence = 0;
            void Submit(SimCommand command, string what) => Assert(world.Submit(command).Accepted, $"{rate}: {what}");
            void Spawn(string template, int x, int y, Fixed angle)
                => Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, angle), $"{template} spawns");

            // Tunnel B is turned: the door turns with it.
            Spawn("GLATunnelNetwork", 300, 300, Fixed.Zero);
            Spawn("GLATunnelNetwork", 700, 700, FixedTrig.Pi / Fixed.FromInt(3));
            Spawn("GLAInfantryRebel", 400, 300, Fixed.Zero);
            Spawn("GLAInfantryRebel", 400, 330, Fixed.Zero);
            for (var step = 0; step < world.Time.OriginalFrames(30); step++) world.Step();
            var tunnels = world.Entities.Where(value => value.Template.Name == "GLATunnelNetwork").OrderBy(value => value.Id.Value).ToArray();
            var (a, b) = (tunnels[0], tunnels[1]);
            var rebels = world.Entities.Where(value => value.Template.Name == "GLAInfantryRebel").OrderBy(value => value.Id.Value).ToArray();
            foreach (var rebel in rebels) Submit(new EnterContainerCommand(new PlayerId(1), ++sequence, world.Tick, rebel.Id, a.Id), $"rebel {rebel.Id.Value} enters tunnel A");
            for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
            Assert(rebels.All(rebel => rebel.ContainedBy.IsValid), $"{rate}: both rebels are in the network");

            var (cos, sin) = (FixedTrig.Cos(b.Angle), FixedTrig.Sin(b.Angle));
            (Fixed X, Fixed Y) OnB(BonePose pose) => (b.X + pose.X * cos - pose.Y * sin, b.Y + pose.X * sin + pose.Y * cos);
            var start = OnB(door.Start!.Value);
            var end = OnB(door.End!.Value);
            Submit(new EvacuateCommand(new PlayerId(1), ++sequence, world.Tick, b.Id, false, Fixed.Zero, Fixed.Zero), "tunnel B is evacuated");
            var result = world.Step();
            Assert(result.Events.Count(value => value.Kind == SimEventKind.EntityUncontained && value.Other == b.Id) == 2, $"{rate}: both leave through tunnel B");
            var first = rebels[0];
            // It took its first step towards the end in the tick it left.
            Assert(Distance(first.X, first.Y, start) < Fixed.FromInt(2), $"{rate}: the first rebel comes out at ExitStart {start} ({first.X}, {first.Y})");
            Assert(first.HasMoveTarget && Distance(first.MoveTargetX, first.MoveTargetY, end) < Fixed.FromDecimal(0.01m), $"{rate}: it walks to ExitEnd {end} ({first.MoveTargetX}, {first.MoveTargetY})");
            Assert(rebels[1].HasMoveTarget && Distance(rebels[1].MoveTargetX, rebels[1].MoveTargetY, end) < Fixed.FromInt(30), $"{rate}: the second walks to a place near ExitEnd ({rebels[1].MoveTargetX}, {rebels[1].MoveTargetY})");
            for (var step = 0; step < world.Time.OriginalFrames(30 * 10); step++) world.Step();
            foreach (var rebel in rebels)
                Assert(!rebel.HasMoveTarget && Distance(rebel.X, rebel.Y, end) < Fixed.FromInt(30), $"{rate}: rebel {rebel.Id.Value} stands at the door's end ({rebel.X}, {rebel.Y}; {Distance(rebel.X, rebel.Y, end)} from it)");
            var stood = string.Join(";", rebels.Select(rebel => $"{rebel.X.Raw},{rebel.Y.Raw}"));


            // #417 with real data: the GLA tunnel network has a ProductionUpdate (its upgrades) and no production exit, so its rally point is its contain module's
            // (Object::getObjectExitInterface), and the rebel that leaves by the door walks on to it.
            Submit(new SetRallyPointCommand(new PlayerId(1), ++sequence, world.Tick, b.Id, Fixed.FromInt(700), Fixed.FromInt(1000)), "tunnel B takes a rally point");
            foreach (var rebel in rebels) Submit(new EnterContainerCommand(new PlayerId(1), ++sequence, world.Tick, rebel.Id, b.Id), $"rebel {rebel.Id.Value} enters tunnel B");
            for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
            Submit(new ExitContainerCommand(new PlayerId(1), ++sequence, world.Tick, rebels[0].Id), "one rebel leaves tunnel B");
            for (var step = 0; step < world.Time.OriginalFrames(30 * 20); step++) world.Step();
            Assert(!rebels[0].ContainedBy.IsValid && Distance(rebels[0].X, rebels[0].Y, (Fixed.FromInt(700), Fixed.FromInt(1000))) < Fixed.FromInt(25),
                $"{rate}: the rebel that left by the door walked on to the rally point ({rebels[0].X}, {rebels[0].Y})");
            var line = $"{stood}|{rebels[0].X.Raw},{rebels[0].Y.Raw}";
            Console.WriteLine($"       {rate}: stood at the door's end {stood}; at the rally point ({rebels[0].X}, {rebels[0].Y})");
            reference ??= line;
            Assert(line == reference, $"{rate}: the same places as at 30 Hz");
        }
    }
}
