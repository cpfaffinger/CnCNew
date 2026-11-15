using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #384 (the owner's playtest of 15 November 2026): five GLA workers sent together to a supply dock (the group's right click, <c>AIGroup::groupDock</c>: one
    /// <see cref="DockCommand"/> per member) all gather - each takes its approach place (<c>DockUpdate::reserveApproachPosition</c>), docks when the dock lets it in and carries its boxes
    /// to the stash. At the civilian warehouse with its nine <c>DockWaiting</c> bones and at the boneless supply dock of the skirmish maps ("infinite can approach"), every one of the five
    /// goes to and fro within three game minutes, at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealGlaWorkersGatherTogether(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "GLA"), new PlayerConfiguration(new PlayerId(2), "Other"));
        foreach (var (rate, _) in WorldRates)
        foreach (var dockName in new[] { "SupplyWarehouse", "SupplyDock" })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "worker-group-dock");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "worker-group-dock", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            var gla = new PlayerId(1);
            void Spawn(string template, PlayerId owner, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(owner, ++sequence, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("GLASupplyStash", gla, 400, 800);
            Spawn(dockName, PlayerId.System, 800, 800);
            for (var i = 0; i < 5; i++) Spawn("GLAInfantryWorker", gla, 520, 600 + i * 30);
            for (var step = 0; step < world.Frames(2); step++) world.Step();
            var dock = world.Entities.Single(entity => entity.Template.Name == dockName);
            // The five placed workers; the stash's own free worker (SpawnBehavior) gathers by itself and is not counted.
            var workers = world.Entities.Where(entity => entity.Template.Name == "GLAInfantryWorker" && entity.X == Fixed.FromInt(520)).OrderBy(entity => entity.Id.Value).ToList();
            Check.That(workers.Count == 5, $"{rate} {dockName}: five workers stand ready ({workers.Count}).");
            foreach (var worker in workers)
                Check.That(world.Submit(new DockCommand(gla, ++sequence, world.Tick, worker.Id, dock.Id)).Accepted, $"{rate} {dockName}: the group's click orders worker {worker.Id.Value} to dock.");
            var deliveries = workers.ToDictionary(worker => worker.Id, _ => 0);
            var finish = world.Frames(30 * 180);
            for (var step = 0; step < finish; step++)
                foreach (var ev in world.Step().Events)
                    if (ev.Kind == SimEventKind.SupplyDeposited && deliveries.ContainsKey(ev.Entity)) deliveries[ev.Entity]++;
            Console.WriteLine($"       {rate} {dockName}: deliveries {string.Join(", ", deliveries.Select(pair => $"{pair.Key.Value}:{pair.Value}"))}");
            Check.That(deliveries.Values.All(count => count >= 3), $"{rate} {dockName}: every one of the five workers went to and fro within three game minutes ({string.Join(", ", deliveries.Values)}).");
        }
    }
}
