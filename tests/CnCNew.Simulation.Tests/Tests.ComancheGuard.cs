using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's playtest (issue 298): three Comanches ordered to guard a far place attacked only the rocket troopers that shot at them and none of the tanks they flew past. That build had
// the original's guard walk, which looks for enemies around the post only; rule fix guard-engages-on-the-way (28) came after it. This pins the Comanche on real data: what it fires, and at
// whom, on the first walk to the post.
static partial class Tests
{
    private sealed record ComancheGuardRun(int CannonAtTanks, int MissilesAtTanks, int CannonAtTroopers, int MissilesAtTroopers, int TanksDead, bool TrooperDead, Fixed WorstEndDistance, bool Lost);

    private static ComancheGuardRun RunComancheGuard(RealTargets real, SimulationRate rate, RuleProfile rules, bool troopers, bool guardAnObject)
    {
        var world = real.WorldWith(rate, rules);
        var specs = new List<(string, int, int, int)>
        {
            ("AmericaVehicleComanche", 1, 200, 200), ("AmericaVehicleComanche", 1, 215, 200), ("AmericaVehicleComanche", 1, 200, 215),
            ("ChinaTankBattleMaster", 2, 500, 240), ("ChinaTankBattleMaster", 2, 560, 160), ("ChinaTankBattleMaster", 2, 620, 240),
        };
        if (troopers) specs.Add(("ChinaInfantryTankHunter", 2, 540, 240));
        if (guardAnObject) specs.Add(("AmericaInfantryRanger", 1, 1300, 200));
        var made = SpawnReal(world, specs.ToArray());
        var helicopters = made.Where(value => value.Template.Name == "AmericaVehicleComanche").ToArray();
        var post = made.FirstOrDefault(value => value.Template.Name == "AmericaInfantryRanger");
        var order = new GuardCommand(new PlayerId(1), ++_sequence, world.Tick, [..helicopters.Select(value => value.Id)], Fixed.FromInt(1300), Fixed.FromInt(200), post?.Id ?? EntityId.Invalid, GuardMode.Normal);
        Check.That(world.Submit(order).Accepted, "guard");
        int cannonTanks = 0, missileTanks = 0, cannonTroopers = 0, missileTroopers = 0;
        for (var step = 0; step < world.Frames(30 * 60); step++)
        {
            foreach (var item in world.Step().Events)
            {
                if (item.Kind != SimEventKind.WeaponFired || helicopters.All(value => value.Id != item.Entity) || world.FindEntity(item.Other) is not { } victim) continue;
                var cannon = item.Text.Contains("Cannon", StringComparison.Ordinal);
                if (victim.Template.Name == "ChinaTankBattleMaster") { if (cannon) cannonTanks++; else missileTanks++; }
                else if (cannon) cannonTroopers++;
                else missileTroopers++;
            }
        }

        var tanksDead = made.Count(value => value.Template.Name == "ChinaTankBattleMaster" && value.EffectivelyDead);
        var trooperDead = made.Any(value => value.Template.Name == "ChinaInfantryTankHunter" && value.EffectivelyDead);
        var worst = helicopters.Where(value => !value.EffectivelyDead).Select(value => GdDistance(value, 1300, 200)).DefaultIfEmpty(Fixed.Zero).Max();
        return new ComancheGuardRun(cannonTanks, missileTanks, cannonTroopers, missileTroopers, tanksDead, trooperDead, worst, helicopters.Any(value => value.EffectivelyDead));
    }

    /// <summary>
    /// Three Comanches at (200, 200) ordered to guard (1300, 200) - a place, then a friendly object standing there - past three Battle Masters 40 beside the path, with and without a Tank
    /// Hunter among them. Under the product rules without guard-engages-on-the-way (the owner's build) the Comanches fire at nothing and reach the post; with it they fire at the tanks - the
    /// rocket pods first and the cannon while those reload - and at the Tank Hunter with the cannon, the weapon the data prefers against infantry, and then stand at the post. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealComancheGuardEngagesOnTheWay(string directory)
    {
        var real = new RealTargets(directory);
        var withoutFix = RuleProfile.Parse(string.Join('+', RuleProfile.Catalogue.Where(info => info.Fix != GameFix.GuardEngagesOnTheWay).Select(info => info.Name)) + "+movement-budget");
        foreach (var rate in TargetRates)
            foreach (var (troopers, guardAnObject) in new[] { (false, false), (true, false), (true, true) })
            {
                var label = $"{rate}, troopers {troopers}, object {guardAnObject}";
                var control = RunComancheGuard(real, rate, withoutFix, troopers, guardAnObject);
                Check.That(control.CannonAtTanks + control.MissilesAtTanks == 0, $"{label}: without the fix the Comanches fire at no tank on the way ({control.CannonAtTanks} + {control.MissilesAtTanks} shots).");
                var run = RunComancheGuard(real, rate, RuleProfile.Product, troopers, guardAnObject);
                Check.That(run.MissilesAtTanks > 0, $"{label}: with the fix the rocket pods are fired at the tanks.");
                Check.That(run.CannonAtTanks > 0, $"{label}: and the cannon fires at them while the pods reload ({run.CannonAtTanks} shots).");
                Check.That(run.TanksDead == 3, $"{label}: the three tanks are dead, {run.TanksDead}.");
                if (troopers) Check.That(run.TrooperDead && run.CannonAtTroopers > 0 && run.MissilesAtTroopers == 0, $"{label}: the Tank Hunter is shot with the cannon ({run.CannonAtTroopers} cannon, {run.MissilesAtTroopers} rocket shots), dead {run.TrooperDead}.");
                Check.That(!run.Lost && run.WorstEndDistance < Fixed.FromInt(40), $"{label}: and the Comanches end at the post ({run.WorstEndDistance} away, lost {run.Lost}).");
            }
    }
}
