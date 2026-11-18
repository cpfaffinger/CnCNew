using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

// #452: a jet in its supersonic locomotor set launches no flares, on real data.
static partial class Tests
{
    /// <summary>
    /// #452, the supersonic set: <c>WeaponTemplate::fireWeaponTemplate</c> reports a small missile to its victim's countermeasures only when the victim has an AI whose locomotor set is not
    /// <c>LOCOMOTORSET_SUPERSONIC</c> ("only allow jets not currently supersonic to launch countermeasures"). The shipped Aurora takes <c>SET_SUPERSONIC</c> as its
    /// <c>AttackLocomotorType</c> from its attack on and keeps it after the attack (#453); a Tank Hunter's missile fired at it then is not reported, one fired when it flies on with another set is. Before, both were. At 30, 60 and 120 Hz.
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

            // It is ordered to attack a place far away (OBJECT_STATUS_IS_ATTACKING): its attack set, SET_SUPERSONIC, follows and stays after the attack is given up for a move (#453).
            Check.That(world.Submit(new AttackPositionCommand(new PlayerId(1), ++_sequence, world.Tick, [aurora.Id], Fixed.FromInt(4400), Fixed.FromInt(400), false, WeaponSlot.Best, 0)).Accepted,
                $"{rate}: the Aurora is ordered to attack a place.");
            // The jet chooses its set where its states decide a logic frame of the original.
            for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
            Check.That(ai.CurrentLocomotorSetName == "SET_SUPERSONIC", $"{rate}: the Aurora flies supersonic in its attack ({ai.CurrentLocomotorSetName}).");
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, aurora.Id, Fixed.FromInt(1400), Fixed.FromInt(600))).Accepted, $"{rate}: the attack is given up.");
            // AttackersMissPersistTime (2 s): until then a shot at the Aurora is aimed behind it at a place, which no countermeasures hear of either.
            for (var step = 0; step < world.Frames(30 * 3); step++) world.Step();
            Check.That(ai.CurrentLocomotorSetName == "SET_SUPERSONIC", $"{rate}: the Aurora still flies supersonic three seconds after its attack ({ai.CurrentLocomotorSetName}).");
            Check.That(world.FireCurrentWeaponAtPosition(hunter, aurora.X, aurora.Y, aurora.Z, aurora), $"{rate}: the Tank Hunter fires at the supersonic Aurora.");
            world.Step();
            Check.That(world.Entities.Any(value => value.Template.Name == "TankHunterMissile"), $"{rate}: its missile flies.");
            Check.That(countermeasures.MissileCounts.Incoming == 0, $"{rate}: a missile fired at a supersonic jet is not reported ({countermeasures.MissileCounts.Incoming}).");

            // It drops its bomb: out of ammunition it returns with its return set, and the next missile is reported.
            Check.That(world.FireCurrentWeaponAtPosition(aurora, aurora.X, aurora.Y, Fixed.Zero), $"{rate}: the Aurora drops its bomb.");
            for (var step = 0; step < world.Frames(30 * 30) && ai.CurrentLocomotorSetName == "SET_SUPERSONIC"; step++) world.Step();
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
