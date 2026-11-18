using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.World;

// #452: a jet in its supersonic locomotor set launches no flares, on real data.
static partial class Tests
{
    /// <summary>
    /// #452, the supersonic set: <c>WeaponTemplate::fireWeaponTemplate</c> reports a small missile to its victim's countermeasures only when the victim has an AI whose locomotor set is not
    /// <c>LOCOMOTORSET_SUPERSONIC</c> ("only allow jets not currently supersonic to launch countermeasures"). The shipped Aurora takes <c>SET_SUPERSONIC</c> as its
    /// <c>AttackLocomotorType</c> after it fired; a Tank Hunter's missile fired at it then is not reported, one fired when it flies on with another set is. Before, both were. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSupersonicJetLaunchesNoFlares(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
            var aurora = SpawnReal(world, ("AmericaJetAurora", 1, 200, 400)).Single();
            var countermeasures = aurora.Modules.OfType<CountermeasuresBehavior>().Single();
            var ai = aurora.Modules.OfType<JetAIUpdate>().Single();
            Check.That(countermeasures.IsActive, $"{rate}: the research gives the Aurora its countermeasures.");
            var hunter = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 1400, 1400)).Single();
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, aurora.Id, Fixed.FromInt(1400), Fixed.FromInt(400))).Accepted, $"{rate}: the Aurora flies out.");
            for (var step = 0; step < world.Frames(30 * 10) && !world.IsAirborneTarget(aurora); step++) world.Step();
            Check.That(world.IsAirborneTarget(aurora) && ai.CurrentLocomotorSetName == "SET_NORMAL", $"{rate}: the Aurora flies with its normal set ({ai.CurrentLocomotorSetName}).");

            // It drops its bomb where it is: its attack set, SET_SUPERSONIC, follows.
            Check.That(world.FireCurrentWeaponAtPosition(aurora, aurora.X, aurora.Y, Fixed.Zero), $"{rate}: the Aurora drops its bomb.");
            world.Step();
            Check.That(ai.CurrentLocomotorSetName == "SET_SUPERSONIC", $"{rate}: the Aurora flies supersonic after its attack ({ai.CurrentLocomotorSetName}).");
            Check.That(world.FireCurrentWeaponAtPosition(hunter, aurora.X, aurora.Y, aurora.Z, aurora), $"{rate}: the Tank Hunter fires at the supersonic Aurora.");
            world.Step();
            Check.That(world.Entities.Any(value => value.Template.Name == "TankHunterMissile"), $"{rate}: its missile flies.");
            Check.That(countermeasures.MissileCounts.Incoming == 0, $"{rate}: a missile fired at a supersonic jet is not reported ({countermeasures.MissileCounts.Incoming}).");

            // Once its attack set is over the next missile is reported.
            for (var step = 0; step < world.Frames(30 * 5) && ai.CurrentLocomotorSetName == "SET_SUPERSONIC"; step++) world.Step();
            Check.That(ai.CurrentLocomotorSetName != "SET_SUPERSONIC", $"{rate}: the attack set ends ({ai.CurrentLocomotorSetName}).");
            var fired = false;
            for (var step = 0; step < world.Frames(30 * 10) && !fired; step++)
            {
                fired = world.FireCurrentWeaponAtPosition(hunter, aurora.X, aurora.Y, aurora.Z, aurora);
                world.Step();
            }
            Check.That(fired, $"{rate}: the Tank Hunter fires again.");
            Check.That(countermeasures.MissileCounts.Incoming == 1, $"{rate}: a missile fired at the jet with its {ai.CurrentLocomotorSetName} set is reported ({countermeasures.MissileCounts.Incoming}).");
        }
    }
}
