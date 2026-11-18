using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.World;

/// <summary>Issue #440: dozer repair goes through the one healing claim of an object (<c>Object::attemptHealingFromSoleBenefactor</c>).</summary>
static partial class Tests
{
    /// <summary>
    /// <c>DozerActionDoActionState::update</c> repairs through <c>Object::attemptHealingFromSoleBenefactor</c> for "this frame and the next": a second Dozer sent to a building another
    /// one repairs is refused and completes its task (<c>internalTaskComplete</c>, <c>STATE_FAILURE</c>), so two Dozers repair a Barracks as fast as one. The port let them add up.
    /// At 30, 60 and 120 Hz, on real data.
    /// </summary>
    public static void RealTwoDozersDoNotStackTheirRepair(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            Fixed Run(int dozers, out bool oneGaveUp)
            {
                var world = real.WorldWith(rate, RuleProfile.Product);
                var specs = new List<(string, int, int, int)> { ("AmericaBarracks", 1, 400, 400) };
                for (var index = 0; index < dozers; index++) specs.Add(("AmericaVehicleDozer", 1, 280, 360 + 80 * index));
                var made = SpawnReal(world, [.. specs]);
                var barracks = made.Single(value => value.Template.Name == "AmericaBarracks");
                var builders = made.Where(value => value.Template.Name == "AmericaVehicleDozer").ToArray();
                Issue440Damage(world, barracks, (int)(barracks.Body!.MaxHealth / Fixed.FromInt(2)).Floor());
                world.Step();
                foreach (var builder in builders)
                {
                    var result = world.Submit(new RepairCommand(new PlayerId(1), ++_sequence, world.Tick, builder.Id, barracks.Id));
                    Check.That(result.Accepted, $"{rate}: a Dozer is sent to repair ({result}).");
                }
                var health = barracks.Body.Health;
                for (var step = 0; step < world.Frames(30 * 20) && barracks.Body.Health == health; step++) world.Step();
                Check.That(barracks.Body.Health > health, $"{rate}: the repair begins.");
                // Both Dozers at the building: the last one arrives within a few seconds.
                Issue440Gain(world, barracks, 30 * 4);
                var tasks = builders.Select(builder => builder.Modules.OfType<AIUpdateInterface>().Single()).ToArray();
                oneGaveUp = false;
                var start = barracks.Body.Health;
                for (var step = 0; step < world.Frames(30 * 5); step++)
                {
                    world.Step();
                    if (tasks.Count(task => task.HasBuilderTask) == 1) oneGaveUp = true;
                }
                return (barracks.Body.Health - start) * Fixed.FromInt(100) / barracks.Body.MaxHealth;
            }

            var one = Run(1, out _);
            var two = Run(2, out var gaveUp);
            // 2 % of the maximum health a second, 10 % in five seconds, and the base's own regeneration (GameData's BaseRegenHealthPercentPerSecond, not a claim).
            Check.That(one > Fixed.FromInt(10) && one < Fixed.FromInt(13), $"{rate}: one Dozer repairs 2 % a second ({one} % in 5 s).");
            Check.That(two > Fixed.FromInt(10) && two < one + Fixed.FromDecimal(0.5m), $"{rate}: two Dozers repair as fast as one ({two} % against {one} % in 5 s).");
            Check.That(gaveUp, $"{rate}: the Dozer that is refused completes its task.");
        }
    }
}
