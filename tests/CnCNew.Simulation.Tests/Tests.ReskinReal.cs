using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #431: <c>ThingTemplate::isEquivalentTo</c> counts an <c>ObjectReskin</c> as the object it reskins. The GLA Technical's second and third chassis are reskins of the first
/// (<c>GLAVehicle.ini</c>), so a script or a team that counts <c>GLAVehicleTechnicalChassisOne</c> counts all three (<c>Team::countObjectsByThingTemplate</c>), as the original does; before,
/// the port counted only the first.
/// </summary>
static partial class Tests
{
    public static void RealReskinCountsAsWhatItReskins(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var reskins = graph.Definitions.OfType<ObjectDefinition>().Where(value => value.ReskinnedFrom is not null).ToArray();
        Check.That(reskins.Length > 0, "The data define reskins.");
        Console.WriteLine($"  {reskins.Length} ObjectReskin templates of {reskins.Select(value => value.ReskinnedFrom).Distinct(StringComparer.Ordinal).Count()} parents");
        Check.That(reskins.Single(value => value.OriginalName == "GLAVehicleTechnicalChassisTwo").ReskinnedFrom == "GLAVehicleTechnicalChassisOne", "Chassis two reskins chassis one.");
        Check.That(reskins.Single(value => value.OriginalName == "GLAHoleBarracks").ReskinnedFrom == "GLAHole", "The barracks' rebuild hole reskins GLAHole.");
        string[] chassis = ["GLAVehicleTechnicalChassisOne", "GLAVehicleTechnicalChassisTwo", "GLAVehicleTechnicalChassisThree"];

        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "reskins");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "reskins", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            for (var index = 0; index < chassis.Length; index++)
                Check.That(feeder.Spawn(1, chassis[index], owner: 1, x: 600 + 40 * index, y: 600).Accepted, $"{rate}: {chassis[index]} spawns.");
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            var made = world.Entities.Where(value => value.Owner == new PlayerId(1) && !value.EffectivelyDead).ToArray();
            Check.That(made.Select(value => value.Template.Name).Order(StringComparer.Ordinal).SequenceEqual(chassis.Order(StringComparer.Ordinal)), $"{rate}: the three chassis stand ({string.Join(", ", made.Select(value => value.Template.Name))}).");
            foreach (var name in chassis)
            {
                var counted = made.Count(value => world.Templates.IsOfType(value.Template, name));
                Check.That(counted == 3, $"{rate}: a count of {name} counts every chassis ({counted}).");
            }
            Check.That(made.All(value => world.Templates.IsOfType(value.Template, "GLAVehicleTechnical")), $"{rate}: every chassis is a GLAVehicleTechnical (its BuildVariations).");
            Check.That(world.Templates.TryGet("GLAHoleBarracks", out var hole) && world.Templates.IsOfType(hole, "GLAHole") && world.Templates.IsOfType(hole, "GLAHoleArmsDealer")
                       && !world.Templates.IsOfType(hole, "GLABarracks"), $"{rate}: a rebuild hole is a GLAHole and its siblings' equal, not the building it rebuilds.");
        }
    }
}
