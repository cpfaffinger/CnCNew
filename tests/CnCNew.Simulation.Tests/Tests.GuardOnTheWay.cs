using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's wish (playtest of 8 November 2026): a unit sent to guard a place fights the enemies it meets on its way there. Real data: a Ranger, a Red Guard standing beside its path.
static partial class Tests
{
    /// <summary>
    /// A Ranger at (400, 400) is ordered to guard (800, 400); a Red Guard of the other side stands at (550, 450), 50 beside its path and 253 from the post - beyond the human guard's
    /// inner (180) and outer (220) range of the post. Under the product rules without the fix the walk looks only around the post and the Ranger never fires on the way; with the fix
    /// it shoots the Red Guard while still far from the post, and afterwards stands at the post. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealGuardEngagesOnTheWay(string directory)
    {
        var real = new RealTargets(directory);
        var withoutFix = RuleProfile.Parse(string.Join('+', RuleProfile.Catalogue.Where(info => info.Fix != GameFix.GuardEngagesOnTheWay).Select(info => info.Name)) + "+movement-budget");
        foreach (var rate in TargetRates)
        {
            (bool Hit, Fixed DistanceAtFirstHit, Fixed EndDistance, bool Dead) Run(RuleProfile rules)
            {
                var world = real.WorldWith(rate, rules);
                var made = SpawnReal(world, ("AmericaInfantryRanger", 1, 400, 400), ("ChinaInfantryRedguard", 2, 550, 450));
                var ranger = made.First(value => value.Template.Name == "AmericaInfantryRanger");
                var enemy = made.First(value => value.Template.Name == "ChinaInfantryRedguard");
                var maxHealth = enemy.Body!.MaxHealth;
                Check.That(world.Submit(new GuardCommand(new PlayerId(1), ++_sequence, world.Tick, [ranger.Id], Fixed.FromInt(800), Fixed.FromInt(400), EntityId.Invalid, GuardMode.Normal)).Accepted, "guard");
                var distanceAtFirstHit = Fixed.Zero;
                var hit = false;
                for (var step = 0; step < world.Frames(30 * 60); step++)
                {
                    world.Step();
                    if (!hit && (enemy.EffectivelyDead || enemy.Body!.Health < maxHealth))
                    {
                        hit = true;
                        distanceAtFirstHit = GdDistance(ranger, 800, 400);
                    }
                }

                return (hit, distanceAtFirstHit, GdDistance(ranger, 800, 400), ranger.EffectivelyDead);
            }

            var control = Run(withoutFix);
            Check.That(!(control.Hit && control.DistanceAtFirstHit > Fixed.FromInt(200)), $"{rate}: without the fix the Ranger does not fire on its way ({control.DistanceAtFirstHit} from the post at the first hit).");
            var fixedRun = Run(RuleProfile.Product);
            Check.That(fixedRun.Hit && fixedRun.DistanceAtFirstHit > Fixed.FromInt(250), $"{rate}: with the fix the Ranger hurts the Red Guard on the way, {fixedRun.DistanceAtFirstHit} from the post.");
            Check.That(!fixedRun.Dead && fixedRun.EndDistance < Fixed.FromInt(40), $"{rate}: and it ends at the post ({fixedRun.EndDistance} away).");
        }
    }
}
