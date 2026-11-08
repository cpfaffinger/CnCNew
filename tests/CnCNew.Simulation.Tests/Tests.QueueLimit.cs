using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data, the owner's playtest of 7 November 2026: a barracks took orders without limit and withdrew the money for every one, but showed 9 at most. The data name no
    /// <c>MaxQueueEntries</c> for them, and the original's module data starts at 9 (<c>ProductionUpdateModuleData</c>); the port read "no key" as "no limit". An order beyond the
    /// limit is refused (<c>CANMAKE_QUEUE_FULL</c>, <c>GUI:ProductionQueueFull</c>) and costs nothing; cancelling refunds as before.
    /// </summary>
    public static void RealProductionQueueHasTheOriginalsLimit(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-queue", fingerprint), catalog);
        ulong sequence = 0;
        SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
        var player = new PlayerId(1);
        Check.That(Send(next => new SpawnCommand(player, next, world.Tick, "AmericaBarracks", player, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The barracks spawns.");
        world.Step();
        Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, 1, world.Tick, player, Fixed.FromInt(100000))).Accepted, "Money is granted.");
        world.Step();
        var barracks = world.Entities.First(value => value.Template.Name == "AmericaBarracks");
        var production = barracks.Modules.OfType<ProductionUpdate>().First();
        var cost = world.Templates.TryGet("AmericaInfantryRanger", out var ranger) ? ranger.BuildCost : Fixed.Zero;
        Check.That(cost > Fixed.Zero, "A Ranger costs money.");
        var start = world.MoneyOf(player);
        var accepted = 0;
        for (var index = 0; index < 15; index++)
        {
            var result = Send(next => new QueueProductionCommand(player, next, world.Tick, barracks.Id, "AmericaInfantryRanger"));
            if (!result.Accepted) { Check.That(result.Reason == CommandRejection.QueueFull, $"A refused order says the queue is full ({result.Reason})."); continue; }
            accepted++;
            world.Step();
        }
        Check.That(accepted == 9, $"A barracks takes the original's 9 queued units, no more ({accepted}).");
        Check.That(production.Queue.Length == 9, $"Its queue holds 9 ({production.Queue.Length}).");
        Check.That(start - world.MoneyOf(player) == cost * 9, $"Only the nine queued units were paid for ({(start - world.MoneyOf(player)).ToDecimal()} against {(cost * 9).ToDecimal()}).");
        Check.That(!production.HasRoom, "The module says it has no room.");
        var before = world.MoneyOf(player);
        var tenth = world.Submit(new QueueProductionCommand(player, ++sequence, world.Tick, barracks.Id, "AmericaInfantryRanger"));
        world.Step();
        Check.That(!tenth.Accepted && tenth.Reason == CommandRejection.QueueFull, "The tenth order is refused by the pre-check.");
        Check.That(world.MoneyOf(player) == before, "A refused order withdraws no money.");
        Check.That(Send(next => new CancelProductionCommand(player, next, world.Tick, barracks.Id, 0)).Accepted, "Cancelling the head is accepted.");
        world.Step();
        Check.That(world.MoneyOf(player) == before + cost && production.Queue.Length == 8 && production.HasRoom, "The refund is intact and a place is free again.");
    }
}
