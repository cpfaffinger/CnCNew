using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>Real data: a guarding Pathfinder (sniper rifle, vision 200) walks at a rebel 340 away but not at vehicles, a structure or the wreck of a vehicle, which it cannot attack.</summary>
    public static void RealGuardIgnoresWhatItCannotHurt(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var enemy in new[] { "AmericaTankCrusader", "ChinaTankBattleMaster", "GLAVehicleTechnical", "AmericaVehicleHumvee", "AmericaVehicleDozer", "ChinaVehicleSupplyTruck", "AmericaVehicleComanche", "ChinaPowerPlant", "AmericaVehicleHumveeDeadHull", "GLAInfantryRebel" })
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "guard");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-guard", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                void Spawn(string template, int owner, int x, int y)
                    => Check.That(world.Submit(new SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                Spawn("AmericaInfantryPathfinder", 1, 400, 400);
                Spawn(enemy, 2, 740, 400);
                for (var i = 0; i < world.Frames(1); i++) world.Step();
                var sniper = world.Entities.First(value => value.Template.Name == "AmericaInfantryPathfinder");
                Check.That(world.Submit(new GuardCommand(new PlayerId(1), ++sequence, world.Tick, [sniper.Id], Fixed.FromInt(400), Fixed.FromInt(400), EntityId.Invalid, GuardMode.Normal)).Accepted, "guard");
                var furthest = Fixed.Zero;
                for (var step = 0; step < world.Frames(300); step++)
                {
                    world.Step();
                    var dx = sniper.X - Fixed.FromInt(400);
                    furthest = Fixed.Max(furthest, Fixed.Sqrt(dx * dx + (sniper.Y - Fixed.FromInt(400)) * (sniper.Y - Fixed.FromInt(400))));
                }
                if (enemy == "GLAInfantryRebel") Check.That(furthest > Fixed.FromInt(20), $"{rate}: a guarding Pathfinder walks at a rebel it can hurt ({furthest}).");
                else Check.That(furthest < Fixed.FromInt(5), $"{rate}: a guarding Pathfinder stays at its post with {enemy} in its guard area ({furthest}).");
            }
        }
    }
}
