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
    /// The owner's playtest (#347): an airfield with two rows and two columns of spaces takes four aircraft, not three. On the real data every airfield (China's, America's and the
    /// generals' variants) queues one aircraft per space, refuses the next, and produces and parks all of them.
    /// </summary>
    public static void RealAirfieldsTakeAsManyAircraftAsTheyHaveSpaces(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var airfields = catalog.Templates.Where(value => value.IsInstantiable && value.Modules.Any(module => module.Name == "ParkingPlaceBehavior")).Select(value => value.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Check.That(airfields.Length == 9, $"Nine airfields ({airfields.Length}).");
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var name in airfields)
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-places", fingerprint), catalog);
            ulong sequence = 0;
            SubmitResult Send(Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, name, new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
            world.Step();
            Send(next => new GiveMoneyCommand(PlayerId.System, next, world.Tick, new PlayerId(1), Fixed.FromInt(50000)));
            world.Step();
            var airfield = world.Entities.First(value => value.Template.Name == name);
            var parking = airfield.Modules.OfType<ParkingPlaceBehavior>().Single();
            var spaces = parking.SpaceCount;
            Check.That(spaces == 4, $"{name} has four spaces ({spaces}).");
            var template = airfield.Template.Name.Contains("China", StringComparison.Ordinal) || name.StartsWith("Boss", StringComparison.Ordinal)
                ? catalog.Templates.First(value => value.Name == (name.Replace("ChinaAirfield", "ChinaJetMIG").Replace("Boss_Airfield", "Boss_JetMIG"))).Name
                : catalog.Templates.First(value => value.Name == name.Replace("AmericaAirfield", "AmericaJetRaptor")).Name;
            var accepted = 0;
            for (var i = 0; i < spaces + 1; i++) if (Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, template)).Accepted) accepted++;
            world.Step();
            Check.That(world.RejectionCounts[(int)CommandRejection.ParkingPlacesFull] == 1 && world.RejectionCounts.Sum() == 1, $"{name}: {spaces + 1} orders, one refused ({string.Join(",", world.RejectionCounts)}).");
            for (var tick = 0; tick < world.Frames(6000) && world.Entities.Count(value => value.Template.Name == template) < spaces; tick++) world.Step();
            var parked = world.Entities.Count(value => value.Template.Name == template && value.Modules.OfType<JetAIUpdate>().Any(jet => jet.Airfield == airfield.Id));
            Check.That(parked == spaces, $"{name}: {parked} of {spaces} {template} are produced and parked.");
            // One order at a time, each waited for: the fourth must still find its space.
            var world2 = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-places-2", fingerprint), catalog);
            ulong sequence2 = 0;
            SubmitResult Send2(Func<ulong, SimCommand> make) => world2.Submit(make(++sequence2));
            Send2(next => new SpawnCommand(new PlayerId(1), next, world2.Tick, name, new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
            world2.Step();
            Send2(next => new GiveMoneyCommand(PlayerId.System, next, world2.Tick, new PlayerId(1), Fixed.FromInt(50000)));
            world2.Step();
            var airfield2 = world2.Entities.First(value => value.Template.Name == name);
            for (var i = 1; i <= spaces; i++)
            {
                var queued = Send2(next => new QueueProductionCommand(new PlayerId(1), next, world2.Tick, airfield2.Id, template)).Accepted;
                Check.That(queued, $"{name}: aircraft {i} of {spaces} may be queued one by one.");
                for (var tick = 0; tick < world2.Frames(6000) && world2.Entities.Count(value => value.Template.Name == template) < i; tick++) world2.Step();
                Check.That(world2.Entities.Count(value => value.Template.Name == template) == i, $"{name}: aircraft {i} of {spaces} is produced one by one.");
            }
        }
    }
}
