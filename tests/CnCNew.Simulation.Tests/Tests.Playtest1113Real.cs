using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's playtest of 13 November 2026, on real data.
static partial class Tests
{
    /// <summary>
    /// #312: "Comanches do not use their countermeasures when they are researched." A Comanche that stood when <c>Upgrade_AmericaCountermeasures</c> completed and one built after it
    /// both carry active countermeasures (<c>Object::initObject</c> runs <c>updateUpgradeModules</c>), and the missiles the Tank Hunters fire at the later one (idle, they take what comes into their range) are reported and some of them
    /// diverted to its flares. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealComanchesBuiltAfterTheResearchLaunchFlares(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            var early = SpawnReal(world, ("AmericaVehicleComanche", 1, 300, 300)).Single();
            world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
            var late = SpawnReal(world, ("AmericaVehicleComanche", 1, 300, 400)).Single();
            CountermeasuresBehavior Flares(Entity comanche) => comanche.Modules.OfType<CountermeasuresBehavior>().Single();
            Check.That(Flares(early).IsActive, $"{rate}: a Comanche that stood when the research completed has its countermeasures.");
            Check.That(Flares(late).IsActive, $"{rate}: a Comanche built after the research has its countermeasures.");

            _ = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 560, 380), ("ChinaInfantryTankHunter", 2, 560, 400), ("ChinaInfantryTankHunter", 2, 560, 420),
                ("ChinaInfantryTankHunter", 2, 580, 390), ("ChinaInfantryTankHunter", 2, 580, 410));
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, late.Id, Fixed.FromInt(420), Fixed.FromInt(400))).Accepted, $"{rate}: the Comanche flies out.");
            var flaresSeen = 0;
            var known = world.Entities.Select(value => value.Id).ToHashSet();
            for (var step = 0; step < world.Frames(30 * 15) && !late.EffectivelyDead; step++)
            {
                world.Step();
                foreach (var entity in world.Entities)
                    if (known.Add(entity.Id) && entity.Template.Name == "CountermeasureFlare") flaresSeen++;
            }
            var (incoming, diverted) = Flares(late).MissileCounts;
            Check.That(incoming > 0, $"{rate}: the missiles fired at the Comanche are reported to its countermeasures ({incoming}).");
            Check.That(diverted > 0 && flaresSeen > 0, $"{rate}: some are diverted ({diverted} of {incoming}) and the Comanche launches flares ({flaresSeen}).");
        }
    }
}
