using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Retail data: the winged locomotors are recognised (<c>Appearance = WINGS</c>, <c>CirclingRadius</c> including the integer form), and Raptor, MiG, Stealth Fighter and Aurora fly a square of move
    /// orders on a flat map without ever leaving the range from their minimum speed to their top speed, turning by at most their turn rate per tick, with a continuous track, and circle the last
    /// destination afterwards; helicopters keep stopping, and the B-52 (a <c>DeliverPayloadAIUpdate</c>, also on a WINGS locomotor) is unchanged. Two runs are identical.
    /// </summary>
    public static void RealWingedFlight(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));

        var wings = catalog.Templates.Where(value => value.Locomotor is { IsWings: true } && value.Modules.Any(module => module.Name == "JetAIUpdate")).Select(value => value.Name).Order(StringComparer.Ordinal).ToArray();
        Console.WriteLine($"       real wings: {wings.Length} JetAIUpdate objects with a WINGS locomotor: {string.Join(", ", wings)}");
        Check.That(wings.Length >= 10 && wings.Contains("AmericaJetRaptor") && wings.Contains("ChinaJetMIG") && wings.Contains("AmericaJetAurora") && wings.Contains("AmericaJetStealthFighter"), "The jets of the four fighter types fly with a WINGS locomotor.");
        Check.That(catalog.TryGet("AmericaJetRaptor", out var raptorTemplate) && raptorTemplate.Locomotor is { IsWings: true } raptorLoco
                   && raptorLoco.CirclingRadius == Fixed.FromInt(100) && raptorLoco.MinSpeed == TestRate.Default.Time.PerSecondToPerTick(60m) && raptorLoco.TurnRatePerTick == TestRate.Default.Time.DegreesPerSecondToRadiansPerTick(120m),
            $"RaptorJetLocomotor: wings, circling radius 100, minimum speed 60/s, turn rate 120 deg/s ({raptorTemplate?.Locomotor?.CirclingRadius}).");
        Check.That(catalog.TryGet("AmericaVehicleComanche", out var comancheTemplate) && comancheTemplate.Locomotor is { IsWings: false }, "The Comanche is not a winged aircraft.");
        Check.That(catalog.TryGet("AmericaJetB52", out var bomberTemplate) && bomberTemplate.Locomotor is { IsWings: true } && bomberTemplate.Modules.All(module => module.Name != "JetAIUpdate"), "The B-52 has a WINGS locomotor but no JetAIUpdate.");

        (List<ulong> Hashes, List<string> Log) Run()
        {
            var hashes = new List<ulong>();
            var log = new List<string>();
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-wings", fingerprint), catalog);
            var sequence = new Dictionary<int, ulong>();
            SubmitResult Send(Func<ulong, SimCommand> make) => world.Submit(make(sequence[1] = sequence.GetValueOrDefault(1) + 1));
            var names = new[] { "AmericaJetRaptor", "ChinaJetMIG", "AmericaJetStealthFighter", "AmericaJetAurora" };
            for (var index = 0; index < names.Length; index++)
                Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, names[index], new PlayerId(1), Fixed.FromInt(1000), Fixed.FromInt(1000 + index * 40), Fixed.Zero, Fixed.Zero)).Accepted, $"{names[index]} spawns.");
            Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaVehicleComanche", new PlayerId(1), Fixed.FromInt(500), Fixed.FromInt(500), Fixed.Zero, Fixed.Zero)).Accepted, "The Comanche spawns.");
            Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaJetB52", new PlayerId(1), Fixed.FromInt(500), Fixed.FromInt(2500), Fixed.Zero, Fixed.Zero)).Accepted, "The B-52 spawns.");
            hashes.Add(world.Step().StateHash);
            var jets = names.Select(name => world.Entities.First(value => value.Template.Name == name)).ToArray();
            var heli = world.Entities.First(value => value.Template.Name == "AmericaVehicleComanche");
            var bomber = world.Entities.First(value => value.Template.Name == "AmericaJetB52");
            var corners = new[] { (2200, 1000), (2200, 2200), (1000, 2200), (1000, 1000) };
            var leg = new int[jets.Length];
            var previous = jets.Select(value => (value.X, value.Y, value.Angle)).ToArray();
            var worstTurn = new Fixed[jets.Length];
            var slowest = jets.Select(_ => Fixed.MaxValue).ToArray();
            var fastest = new Fixed[jets.Length];
            var largestStep = new Fixed[jets.Length];
            var smallestStep = jets.Select(_ => Fixed.MaxValue).ToArray();
            var arrivedAt = new long[jets.Length];
            for (var index = 0; index < jets.Length; index++) Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, jets[index].Id, Fixed.FromInt(corners[0].Item1), Fixed.FromInt(corners[0].Item2)));
            Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, heli.Id, Fixed.FromInt(1500), Fixed.FromInt(500)));
            Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, bomber.Id, Fixed.FromInt(1500), Fixed.FromInt(2500)));
            for (var tick = 0; tick < world.Frames(4200); tick++)
            {
                hashes.Add(world.Step().StateHash);
                for (var index = 0; index < jets.Length; index++)
                {
                    var jet = jets[index];
                    if (tick > world.Frames(20))
                    {
                        // 20 ticks of take-off-free climbing to the preferred height; afterwards every tick counts
                        slowest[index] = Fixed.Min(slowest[index], jet.Speed);
                        fastest[index] = Fixed.Max(fastest[index], jet.Speed);
                        var step = Fixed.Sqrt((jet.X - previous[index].X) * (jet.X - previous[index].X) + (jet.Y - previous[index].Y) * (jet.Y - previous[index].Y));
                        smallestStep[index] = Fixed.Min(smallestStep[index], step);
                        largestStep[index] = Fixed.Max(largestStep[index], step);
                        worstTurn[index] = Fixed.Max(worstTurn[index], FixedTrig.AngleDifference(previous[index].Angle, jet.Angle).Abs());
                    }
                    previous[index] = (jet.X, jet.Y, jet.Angle);
                    if (!jet.HasMoveTarget && leg[index] < corners.Length - 1)
                    {
                        leg[index]++;
                        var corner = corners[leg[index]];
                        Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, jet.Id, Fixed.FromInt(corner.Item1), Fixed.FromInt(corner.Item2)));
                    }
                    else if (!jet.HasMoveTarget && arrivedAt[index] == 0) arrivedAt[index] = world.Tick;
                }
            }
            for (var index = 0; index < jets.Length; index++)
            {
                var jet = jets[index];
                var loco = jet.Template.Locomotor!;
                Check.That(leg[index] == corners.Length - 1 && arrivedAt[index] > 0, $"{jet.Template.Name} flew all four legs of the square (leg {leg[index]}, arrived at {arrivedAt[index]}).");
                Check.That(slowest[index] >= loco.MinSpeed - Fixed.FromRaw(16), $"{jet.Template.Name} never fell below its minimum speed ({slowest[index]} vs {loco.MinSpeed}).");
                Check.That(fastest[index] <= loco.Speed + Fixed.FromRaw(16), $"{jet.Template.Name} never exceeded its speed ({fastest[index]} vs {loco.Speed}).");
                Check.That(smallestStep[index] >= loco.MinSpeed - Fixed.FromRaw(64), $"{jet.Template.Name}'s track has no gap: every tick moves at least the minimum speed ({smallestStep[index]}).");
                Check.That(largestStep[index] <= loco.Speed + Fixed.FromRaw(64), $"{jet.Template.Name}'s track has no jump ({largestStep[index]}).");
                Check.That(worstTurn[index] <= loco.TurnRatePerTick + Fixed.FromRaw(4), $"{jet.Template.Name} turns by at most its turn rate per tick ({worstTurn[index]} vs {loco.TurnRatePerTick}).");
                var radius = loco.CirclingRadius.Abs();
                var distance = Fixed.Sqrt((jet.X - Fixed.FromInt(1000)) * (jet.X - Fixed.FromInt(1000)) + (jet.Y - Fixed.FromInt(1000)) * (jet.Y - Fixed.FromInt(1000)));
                Check.That(distance < radius * 2 + Fixed.FromInt(40), $"{jet.Template.Name} circles its last destination ({distance} away, circling radius {radius}).");
                log.Add($"{jet.Template.Name}: min speed {slowest[index]}, max {fastest[index]}, worst turn {worstTurn[index]}, {distance} from the last destination at the end");
            }
            Check.That(!heli.HasMoveTarget && heli.Speed == Fixed.Zero, $"The Comanche stops at the end of its order (speed {heli.Speed}).");
            Check.That(!bomber.HasMoveTarget && bomber.Speed == Fixed.Zero, $"The B-52, a different AI on a WINGS locomotor, keeps its old movement and stops (speed {bomber.Speed}).");
            return (hashes, log);
        }

        var first = Run();
        foreach (var line in first.Log) Console.WriteLine("       real wings: " + line);
        var second = Run();
        Check.That(first.Hashes.SequenceEqual(second.Hashes), "Two runs of the winged flight are identical, tick by tick.");
    }
}
