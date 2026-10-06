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
    /// <summary>
    /// Demo finding #12 with retail units: a squad of Rangers walks along a corridor-free field and a Dozer stands (working) across their line. They have to find the way round without
    /// standing against the Dozer: per Ranger the ticks with a move order and no movement are counted, and none of them may overlap the Dozer's bounding circle for long.
    /// </summary>
    public static void RealSquadPassesAStandingDozer(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, SimulationRate.Original);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        (long Slowest, int Stalled, int Overlaps) Run()
        {
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "squad");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 7, players, "real-squad", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("AmericaVehicleDozer", 600, 600);
            for (var index = 0; index < 8; index++) Spawn("AmericaInfantryRanger", 380, 560 + index * 12);
            world.Step();
            var rangers = world.Entities.Where(value => value.Template.Name == "AmericaInfantryRanger").OrderBy(value => value.Id.Value).ToArray();
            var dozer = world.Entities.First(value => value.Template.Name == "AmericaVehicleDozer");
            foreach (var ranger in rangers)
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, ranger.Id, Fixed.FromInt(820), Fixed.FromInt(600))).Accepted, "The squad is ordered across the field.");
            var arrived = new HashSet<uint>();
            long last = 0;
            int stalled = 0, overlaps = 0;
            for (var tick = 0; tick < 1800 && arrived.Count < rangers.Length; tick++)
            {
                var result = world.Step();
                foreach (var simEvent in result.Events)
                    if (simEvent.Kind == CnCNew.Simulation.Events.SimEventKind.MoveArrived && rangers.Any(value => value.Id == simEvent.Entity) && arrived.Add(simEvent.Entity.Value)) last = world.Tick;
                foreach (var ranger in rangers)
                {
                    if (ranger.HasMoveTarget && ranger.Speed == Fixed.Zero && tick > 20) stalled++;
                    var dx = ranger.X - dozer.X;
                    var dy = ranger.Y - dozer.Y;
                    var limit = ranger.Template.BoundingCircleRadius + dozer.Template.BoundingCircleRadius;
                    if (dx * dx + dy * dy < limit * limit - Fixed.One) overlaps++;
                }
            }

            return (arrived.Count == rangers.Length ? last : -1, stalled, overlaps);
        }

        var (slowest, stalled, overlaps) = Run();
        Console.WriteLine($"       squad past a standing dozer: all arrived by tick {slowest}, {stalled} unit-ticks with an order and no movement, {overlaps} overlapping unit-ticks");
        Check.That(slowest > 0, "Every Ranger reaches the far side.");
        // Before the fix (path only knew structures, units shoved along the dozer) the same squad overlapped the dozer's circle for 488 unit-ticks.
        Check.That(overlaps == 0, $"No Ranger walks into the standing Dozer ({overlaps} overlapping unit-ticks).");
        Check.That(Run() == (slowest, stalled, overlaps), "The passage is deterministic.");
    }
}
