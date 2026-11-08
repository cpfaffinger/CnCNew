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
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (the owner's playtest of 7 November 2026, finding 14): a human player's Chinook gathers supplies. The supply center's exit sends the Chinook it makes to the supplies at once
    /// (<c>SupplyCenterProductionExitUpdate::exitObjectViaDoor</c> forces the wanting state; no player kind is asked), and an order a player gives a standing Chinook through the command path
    /// (<c>DockCommand</c>, the click on a supply pile) takes it to the pile, loads it and brings the boxes to the center; both end in a deposit within two and a half minutes at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealHumanChinookGathers(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var variant in new[] { "free", "made", "ordered" })
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "chinook");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-chinook", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                void Spawn(string template, PlayerId owner, int x, int y)
                    => Check.That(world.Submit(new SpawnCommand(owner, ++sequence, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                var human = new PlayerId(1);
                Spawn("AmericaSupplyCenter", human, 300, 300);
                Spawn("SupplyPile", PlayerId.System, 560, 380);
                if (variant == "ordered") Spawn("AmericaVehicleChinook", human, 200, 200);
                Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, human, Fixed.FromInt(5000))).Accepted, "the player has money.");
                for (var step = 0; step < world.Frames(2); step++) world.Step();
                var center = world.Entities.Single(entity => entity.Template.Name == "AmericaSupplyCenter");
                var pile = world.Entities.Single(entity => entity.Template.Name == "SupplyPile");
                // The finished center spawns its own Chinook (SpawnBehavior); "free" is that one, left alone.
                var free = world.Entities.Where(entity => entity.Template.Name == "AmericaVehicleChinook" && entity.Id != default).Select(entity => entity.Id.Value).ToHashSet();
                var watched = new HashSet<uint>();
                if (variant == "ordered")
                {
                    var chinook = world.Entities.Single(entity => entity.Template.Name == "AmericaVehicleChinook" && entity.X < Fixed.FromInt(250) && entity.Y < Fixed.FromInt(250));
                    Check.That(world.Submit(new DockCommand(human, ++sequence, world.Tick, chinook.Id, pile.Id)).Accepted, $"{rate}: the click on the pile orders the Chinook to dock.");
                    watched.Add(chinook.Id.Value);
                    free.Remove(chinook.Id.Value);
                }
                else if (variant == "made")
                    Check.That(world.Submit(new QueueProductionCommand(human, ++sequence, world.Tick, center.Id, "AmericaVehicleChinook")).Accepted, $"{rate}: the center queues a Chinook.");
                else watched.UnionWith(free);
                var finish = world.Frames(30 * 150);
                long deposits = 0;
                for (var step = 0; step < finish && deposits == 0; step++)
                {
                    var result = world.Step();
                    if (variant == "made" && watched.Count == 0)
                        foreach (var entity in world.Entities)
                            if (entity.Template.Name == "AmericaVehicleChinook" && !free.Contains(entity.Id.Value)) watched.Add(entity.Id.Value);
                    deposits += result.Events.Count(ev => ev.Kind == SimEventKind.SupplyDeposited && ev.Entity != ev.Other && watched.Contains(ev.Entity.Value));
                }
                Check.That(deposits > 0, $"{rate}: the human's Chinook ({variant}) brought supplies to the center within 150 s.");
            }
        }
    }
}
