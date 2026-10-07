using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

/// <summary>The staged demonstrations (<see cref="DemoScenarios"/>) run on real data: every object is placed and tagged, the orders do what they say, and two runs are identical.</summary>
internal static class ScenarioTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ScenarioTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ScenarioTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(args[index + 1]);
        foreach (var scenario in DemoScenarios.All)
        {
            harness.Run($"Scenario {scenario.Name} places its objects, runs the orders and is deterministic", () =>
            {
                (ulong Hash, Dictionary<string, int> Facts) Play(long ticks)
                {
                    var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
                    var facts = new Dictionary<string, int>();
                    live.TickStepped += result =>
                    {
                        foreach (var simEvent in result.Events)
                        {
                            var key = simEvent.Kind.ToString();
                            facts[key] = facts.GetValueOrDefault(key) + 1;
                        }
                    };
                    TestHarness.Assert(scenario.Units.All(unit => live.Tags.ContainsKey(unit.Tag)), "every placed object is tagged");
                    while (live.Clock.OriginalFrames < ticks) live.Advance(CnCNew.Tests.TestTime.FrameSeconds + 1e-6); // the budget counts original frames
                    return (live.World.StateHash, facts);
                }

                var (first, facts) = Play(Math.Min(scenario.Frames, 1300));
                var (second, _) = Play(Math.Min(scenario.Frames, 1300));
                TestHarness.Assert(first == second, "two runs end in the same state hash");
                switch (scenario.Name)
                {
                    case "tank-duel":
                        TestHarness.Assert(facts.GetValueOrDefault(nameof(SimEventKind.WeaponFired)) >= 4, "both tanks fire");
                        break;
                    case "jet-strafe":
                        TestHarness.Assert(facts.GetValueOrDefault(nameof(SimEventKind.WeaponFired)) >= 4, "the Raptors and the Missile Defenders shoot");
                        break;
                    case "capture-building":
                        TestHarness.Assert(facts.GetValueOrDefault(nameof(SimEventKind.StructureCaptured)) == 1, "the barracks is captured");
                        break;
                    default:
                        if (FactoryExits.SitesOf(scenario) is { IsEmpty: false } sites)
                            TestHarness.Assert(facts.GetValueOrDefault(nameof(SimEventKind.ProductionCompleted)) >= sites.Length, $"every building of the scene finishes a unit ({facts.GetValueOrDefault(nameof(SimEventKind.ProductionCompleted))} for {sites.Length})");
                        break;
                }
            });
        }

        return harness.Failures;
    }
}
