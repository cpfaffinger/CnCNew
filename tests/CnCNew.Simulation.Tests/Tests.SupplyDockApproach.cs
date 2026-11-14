using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (#339): a supply pile has no <c>DockWaiting</c> bones in its model, so <c>DockUpdate::computeApproachPosition</c> takes the pile's centre pushed half its major radius towards the
    /// truck and has <c>PartitionManager::findPositionAround</c> look for a place no object overlaps. A truck that waits to take boxes stands at the pile's outer edge, never on the pile.
    /// </summary>
    public static void RealSupplyTrucksWaitOutsideThePile(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = CnCNew.Simulation.Templates.TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (truckName, centerName) in new[] { ("ChinaVehicleSupplyTruck", "ChinaSupplyCenter"), ("GLAInfantryWorker", "GLASupplyStash") })
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "pile");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-pile", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                void Spawn(string template, PlayerId owner, int x, int y)
                    => Check.That(world.Submit(new SpawnCommand(owner, ++sequence, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                var human = new PlayerId(1);
                Spawn(centerName, human, 300, 300);
                Spawn("SupplyPile", PlayerId.System, 600, 300);
                for (var truck = 0; truck < 4; truck++) Spawn(truckName, human, 330 + truck * 12, 340);
                world.Step();
                var pile = world.Entities.Single(entity => entity.Template.Name == "SupplyPile");
                var trucks = world.Entities.Where(entity => entity.Template.Name == truckName).ToList();
                foreach (var truck in trucks)
                    Check.That(world.Submit(new DockCommand(human, ++sequence, world.Tick, truck.Id, pile.Id)).Accepted, $"{rate}: the click on the pile orders the {truckName} to dock.");
                var inside = 0;
                var waited = 0;
                long deposits = 0;
                var finish = world.Frames(30 * 120);
                for (var step = 0; step < finish; step++)
                {
                    var result = world.Step();
                    deposits += result.Events.Count(ev => ev.Kind == CnCNew.Simulation.Events.SimEventKind.SupplyDeposited);
                    foreach (var truck in trucks)
                    {
                        if (truck.HasMoveTarget || truck.Speed != Fixed.Zero) continue;
                        var (dx, dy) = ((truck.X - pile.X).Abs(), (truck.Y - pile.Y).Abs());
                        if (dx > Fixed.FromInt(120) || dy > Fixed.FromInt(120)) continue;
                        waited++;
                        if (dx < pile.Template.GeometryMajorRadius && dy < pile.Template.GeometryMinorRadius) inside++;
                    }
                }
                Check.That(deposits > 0, $"{rate}: the {truckName}s brought supplies to the center from the pile's edge.");
                Check.That(waited > 0, $"{rate}: a {truckName} stood at the pile.");
                Check.That(inside == 0, $"{rate}: a {truckName} never stands on the pile (#339).");
            }
        }
    }
}
