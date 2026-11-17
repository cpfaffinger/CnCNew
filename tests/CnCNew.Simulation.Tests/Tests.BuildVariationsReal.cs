using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #425: <c>ThingFactory::newObject</c> replaces a template that names <c>BuildVariations</c> by one of them, drawn with <c>GameLogicRandomValue(0, size - 1)</c> from the logic
/// stream before anything else of the object is made. A produced GLA Technical is chassis One, Two or Three - each with a body, a weapon and its own <c>VeterancyGainCreate</c>, so
/// Technical Training makes it a veteran. Before, the port made the bare <c>GLAVehicleTechnical</c>: no body, nothing to train.
/// </summary>
static partial class Tests
{
    public static void RealProducedTechnicalIsOneOfItsBuildVariations(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        string[] chassis = ["GLAVehicleTechnicalChassisOne", "GLAVehicleTechnicalChassisTwo", "GLAVehicleTechnicalChassisThree"];

        SimulationWorld NewWorld(SimulationRate rate, uint seed)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "variations");
            return new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, seed, players, "variations", fingerprint, terrain.Fingerprint), catalog, terrain);
        }

        // Six Technicals produced at an arms dealer: what each became and its veterancy level, in the order they were made.
        (string Name, int Level)[] Produce(SimulationRate rate, bool training)
        {
            var world = NewWorld(rate, 7);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "GLAArmsDealer", owner: 1, x: 600, y: 600).Accepted, "The arms dealer spawns.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            Check.That(feeder.GiveMoney(1, 100000).Accepted, "Money is granted.");
            if (training) Check.That(world.GrantScience(new PlayerId(1), "SCIENCE_TechnicalTraining"), "Technical Training is granted.");
            world.Step();
            var dealer = world.Entities.Single(value => value.Template.Name == "GLAArmsDealer");
            var known = world.Entities.Select(value => value.Id.Value).ToHashSet();
            for (var index = 0; index < 6; index++)
            {
                Check.That(feeder.QueueProduction(1, dealer.Id.Value, "GLAVehicleTechnical").Accepted, $"{rate}: Technical {index + 1} is queued.");
                world.Step();
            }
            for (var step = 0; step < world.Frames(30 * 90); step++) world.Step();
            return world.Entities.Where(value => !known.Contains(value.Id.Value) && value.Owner == new PlayerId(1)).OrderBy(value => value.Id.Value)
                .Select(value =>
                {
                    Check.That(world.Templates.IsOfType(value.Template, "GLAVehicleTechnical"), $"{rate}: {value.Template.Name} is a GLAVehicleTechnical to a script's count and a team's order.");
                    return (value.Template.Name, value.VeterancyLevel);
                }).ToArray();
        }

        foreach (var training in new[] { false, true })
        {
            var at30 = Produce(SimulationRate.Hz30, training);
            var text = string.Join(", ", at30.Select(value => $"{value.Name}:{value.Level}"));
            Check.That(at30.Length == 6, $"30 Hz: six Technicals are made ({text}).");
            Check.That(at30.All(value => chassis.Contains(value.Name)), $"30 Hz: every produced Technical is one of its three chassis ({text}).");
            var level = training ? 1 : 0;
            Check.That(at30.All(value => value.Level == level), $"30 Hz: {(training ? "with" : "without")} Technical Training a produced Technical is at level {level} ({text}).");
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var other = Produce(rate, training);
                Check.That(other.SequenceEqual(at30), $"{rate}: the same chassis at the same levels as at 30 Hz ({string.Join(", ", other.Select(value => $"{value.Name}:{value.Level}"))} against {text}).");
            }
        }

        // The choice is the logic stream's GameLogicRandomValue(0, 2), drawn when the object is made: predicted from the stream's state before the step that makes it.
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var seen = new HashSet<string>();
            for (uint index = 1; index <= 24; index++)
            {
                // The first values of neighbouring seeds lie close together in SAGE's generator: the seeds are spread.
                var seed = unchecked(index * 2654435761u);
                var world = NewWorld(rate, seed);
                var feeder = new Feeder(world);
                Check.That(feeder.Spawn(1, "GLAVehicleTechnical", owner: 1, x: 600, y: 600).Accepted, "A Technical is created.");
                var state = new uint[6];
                world.LogicRandom.CopyState(state);
                var predictor = new SageRandom(0);
                predictor.SetState(state);
                var expected = chassis[predictor.Range(0, chassis.Length - 1)];
                world.Step();
                var made = world.Entities.Single(value => value.Owner == new PlayerId(1));
                Check.That(made.Template.Name == expected, $"{rate}, seed {seed}: the logic stream's draw makes {expected} ({made.Template.Name}).");
                seen.Add(made.Template.Name);
            }
            Check.That(seen.Count == 3, $"{rate}: over 24 seeds every chassis is made ({string.Join(", ", seen.Order())}).");
        }
    }
}
