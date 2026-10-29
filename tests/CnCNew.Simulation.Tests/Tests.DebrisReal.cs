using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, F-36: a Crusader's slow death runs <c>OCL_GenericTankDeathEffect</c>, whose <c>CreateDebris</c> nuggets throw pieces of <c>GenericDebris</c> with <c>SEND_IT_FLYING</c>: they fly,
/// bounce, die when they rest on the ground and their slow death removes them. Before, every such nugget was counted as not simulated.
/// </summary>
static partial class Tests
{
    public static void RealDebrisFlies(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        (int Pieces, long Gone, Fixed Highest) Run(SimulationRate rate)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate);
            Check.That(catalog.TryGet("GenericDebris", out var debris) && debris.IsInstantiable, "GenericDebris is instantiable: " + string.Join(", ", debris?.MissingModules ?? []));
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(80, 80, 1, (_, _) => 0), "debris");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "debris", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "AmericaTankCrusader", owner: 1, x: 300, y: 300).Accepted, "The Crusader spawns.");
            for (var step = 0; step < world.Frames(1); step++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "AmericaTankCrusader");
            feeder.Kill(tank.Id.Value);
            var pieces = new HashSet<uint>();
            var highest = Fixed.Zero;
            var start = world.Tick;
            long gone = -1;
            for (var step = 0; step < world.Frames(900); step++)
            {
                world.Step();
                foreach (var piece in world.Entities.Where(value => value.Template.Name == "GenericDebris"))
                {
                    pieces.Add(piece.Id.Value);
                    highest = Fixed.Max(highest, piece.Z);
                }
                if (pieces.Count > 0 && gone < 0 && world.Entities.All(value => value.Template.Name != "GenericDebris"))
                    gone = (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(pieces.Count >= 3 && highest > Fixed.FromInt(5), $"{rate}: the dying Crusader throws its pieces ({pieces.Count}, as high as {highest}).");
            Check.That(gone > 0, $"{rate}: the pieces come to rest and are removed.");
            return (pieces.Count, gone, highest);
        }

        var reference = Run(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate);
            Check.That(other.Pieces == reference.Pieces && other.Gone == reference.Gone, $"{rate}: the same pieces, gone in the same frame ({other.Pieces}/{other.Gone} against {reference.Pieces}/{reference.Gone}).");
        }
    }
}
