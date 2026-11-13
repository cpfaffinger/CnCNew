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

    /// <summary>
    /// #318: "A drone that can repair its unit may only repair its own unit; they also repair other units." <c>SlavedUpdate::doRepairLogic</c> heals only the drone's master. Two Crusaders
    /// of one player stand side by side, one with a battle drone; the other is hurt and the drone's master is whole: nothing heals the hurt one. Then the master is hurt too and the drone
    /// heals the master. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealBattleDroneRepairsOnlyItsMaster(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            var tanks = SpawnReal(world, ("AmericaTankCrusader", 1, 400, 400), ("AmericaTankCrusader", 1, 425, 400));
            var (master, other) = (tanks[0], tanks[1]);
            world.CompleteUpgrade(new PlayerId(1), master.Id, "Upgrade_AmericaBattleDrone");
            Entity? drone = null;
            for (var step = 0; step < world.Frames(30) && drone is null; step++)
            {
                world.Step();
                drone = world.Entities.FirstOrDefault(value => value.Template.Name == "AmericaVehicleBattleDrone");
            }
            Check.That(drone is not null && drone.Modules.OfType<SlavedUpdate>().Single().Slaver == master.Id, $"{rate}: the master's drone is there.");
            Check.That(world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, other.Id, other.Body!.MaxHealth / Fixed.FromInt(2), DamageTypes.Explosion, 0)).Accepted, $"{rate}: the other tank is hurt.");
            world.Step();
            var hurt = other.Body!.Health;
            for (var step = 0; step < world.Frames(30 * 30); step++) world.Step();
            Check.That(other.Body!.Health == hurt, $"{rate}: the drone does not repair a tank that is not its master ({hurt} -> {other.Body!.Health}).");

            Check.That(world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, master.Id, master.Body!.MaxHealth / Fixed.FromInt(2), DamageTypes.Explosion, 0)).Accepted, $"{rate}: the master is hurt.");
            world.Step();
            var masterHurt = master.Body!.Health;
            for (var step = 0; step < world.Frames(30 * 30); step++) world.Step();
            Check.That(master.Body!.Health > masterHurt, $"{rate}: the drone repairs its master ({masterHurt} -> {master.Body!.Health}).");
            Check.That(other.Body!.Health == hurt, $"{rate}: and still not the other tank ({hurt} -> {other.Body!.Health}).");
        }
    }
}
