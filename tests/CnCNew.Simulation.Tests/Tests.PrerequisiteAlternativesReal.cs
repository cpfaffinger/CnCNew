using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #465: <c>ThingTemplate::parsePrerequisites</c> reads every token of an <c>Object</c> line of a <c>Prerequisites</c> block as an alternative (<c>parsePrerequisiteUnit</c>,
/// <c>addUnitPrereq(..., orUnitWithPrevious)</c>), and <c>ProductionPrerequisite::isSatisfied</c> lumps them together: the Strategy Center's <c>Object = AmericaWarFactory AmericaAirfield</c>
/// is met by either. Before, the definition kept the last name only, and the Strategy Center needed an airfield.
/// </summary>
static partial class Tests
{
    public static void RealStrategyCenterNeedsAWarFactoryOrAnAirfield(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            Check.That(catalog.TryGet("AmericaStrategyCenter", out var center), "The Strategy Center is in the catalog.");
            var line = center!.Prerequisites.Single(value => !value.IsScience);
            Check.That(line.Alternatives.SequenceEqual(["AmericaWarFactory", "AmericaAirfield"]), $"{rate}: its one Object line names both alternatives ({string.Join(" ", line.Alternatives)}).");

            foreach (var (owned, expected) in new (string? Owned, bool Expected)[] { ("AmericaWarFactory", true), ("AmericaAirfield", true), (null, false) })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "prerequisites");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "prerequisites", fingerprint, terrain.Fingerprint), catalog, terrain);
                var feeder = new Feeder(world);
                Check.That(feeder.Spawn(1, "AmericaVehicleDozer", owner: 1, x: 700, y: 700).Accepted, "The dozer spawns.");
                if (owned is not null) Check.That(feeder.Spawn(1, owned, owner: 1, x: 300, y: 300).Accepted, $"The {owned} spawns.");
                for (var step = 0; step < world.Frames(30); step++) world.Step();
                Check.That(feeder.GiveMoney(1, 100000).Accepted, "Money is granted.");
                world.Step();
                var what = owned ?? "nothing";
                Check.That(world.PlayerHasPrerequisitesFor(new PlayerId(1), "AmericaStrategyCenter") == expected,
                    $"{rate}: with {what} the player {(expected ? "may" : "may not")} build a Strategy Center.");

                var dozer = world.Entities.Single(value => value.Template.Name == "AmericaVehicleDozer");
                var result = feeder.Construct(1, dozer.Id.Value, "AmericaStrategyCenter", 800, 700);
                if (!expected)
                {
                    Check.That(!result.Accepted && result.Reason == CommandRejection.MissingPrerequisite, $"{rate}: without either the order is refused for its prerequisite ({result.Reason}).");
                    continue;
                }
                Check.That(result.Accepted, $"{rate}: with {what} the order to build a Strategy Center is accepted ({result.Reason}).");
                for (var step = 0; step < world.Frames(30 * 10); step++) world.Step();
                Check.That(world.Entities.Any(value => value.Template.Name == "AmericaStrategyCenter" && value.Owner == new PlayerId(1)),
                    $"{rate}: with {what} the dozer has begun the Strategy Center.");
            }
        }
    }
}
