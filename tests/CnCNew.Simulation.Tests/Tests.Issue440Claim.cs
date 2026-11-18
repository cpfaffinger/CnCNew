using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.World;

/// <summary>Issue #440: the propaganda towers share the one healing claim of an object (<c>Object::attemptHealingFromSoleBenefactor</c>) with the healing auras.</summary>
static partial class Tests
{
    /// <summary>Steps <paramref name="frames"/> frames of the original and returns how much the object's health rose.</summary>
    private static Fixed Issue440Gain(SimulationWorld world, Entity patient, int frames)
    {
        var start = patient.Body!.Health;
        for (var step = 0; step < world.Frames(frames); step++) world.Step();
        return patient.Body.Health - start;
    }

    private static void Issue440Damage(SimulationWorld world, Entity target, int amount)
        => Check.That(world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, target.Id, Fixed.FromInt(amount), DamageTypes.Unresistable, 0)).Accepted, "damage");

    /// <summary>
    /// <c>PropagandaTowerBehavior::effectLogic</c> heals through <c>Object::attemptHealingFromSoleBenefactor</c> for a scan delay, and the Ambulance's aura
    /// (<c>AutoHealBehavior::pulseHealObject</c>) through the same claim for its <c>HealingDelay</c>: the claim is the object's, so a Ranger between a Speaker Tower and an Ambulance
    /// is healed by one of them, never by both - by the Ambulance when it healed first, by the tower when the tower did. The port kept the towers' claims among towers, and the two
    /// added up. A restored world heals the same. At 30, 60 and 120 Hz, on real data.
    /// </summary>
    public static void RealPropagandaTowerAndAmbulanceDoNotStack(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            Fixed Run(bool tower, bool ambulance, bool towerFirst, out SimulationWorld world, out Entity ranger)
            {
                world = real.WorldWith(rate, RuleProfile.Product);
                var made = SpawnReal(world, ("ChinaPowerPlant", 1, 700, 700), ("AmericaInfantryRanger", 1, 400, 430));
                ranger = made.Single(value => value.Template.Name == "AmericaInfantryRanger");
                Issue440Damage(world, ranger, 150);
                if (tower) SpawnReal(world, ("ChinaSpeakerTower", 1, 400, 400));
                if (towerFirst) Issue440Gain(world, ranger, 30 * 4);
                if (ambulance) SpawnReal(world, ("AmericaVehicleMedic", 1, 440, 430));
                // Up to the first heal of each, so that the measured stretch has every healer at work.
                Issue440Gain(world, ranger, 30 * 2);
                return Issue440Gain(world, ranger, 30 * 10);
            }

            var towerAlone = Run(tower: true, ambulance: false, towerFirst: false, out _, out _);
            var ambulanceAlone = Run(tower: false, ambulance: true, towerFirst: false, out _, out _);
            Check.That(towerAlone > Fixed.FromInt(30) && ambulanceAlone > Fixed.FromInt(30), $"{rate}: each heals the Ranger alone (tower {towerAlone}, Ambulance {ambulanceAlone}).");

            var both = Run(tower: true, ambulance: true, towerFirst: false, out var world, out var ranger);
            var holder = world.FindEntity(ranger.SoleHealingBenefactor);
            Check.That(holder?.Template.Name == "AmericaVehicleMedic", $"{rate}: the Ambulance, which healed first, holds the Ranger ({holder?.Template.Name}).");
            Check.That(both <= ambulanceAlone + Fixed.FromInt(4), $"{rate}: tower and Ambulance heal as much as the Ambulance alone ({both} against {ambulanceAlone} and {towerAlone}).");

            var towerFirst = Run(tower: true, ambulance: true, towerFirst: true, out world, out ranger);
            holder = world.FindEntity(ranger.SoleHealingBenefactor);
            Check.That(holder?.Template.Name == "ChinaSpeakerTower", $"{rate}: the tower, which healed first, holds the Ranger ({holder?.Template.Name}).");
            Check.That(towerFirst <= towerAlone + Fixed.FromInt(1), $"{rate}: tower and Ambulance heal as much as the tower alone ({towerFirst} against {towerAlone} and {ambulanceAlone}).");

            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
            var restoredRanger = restored.FindEntity(ranger.Id)!;
            Check.That(restoredRanger.SoleHealingBenefactor == ranger.SoleHealingBenefactor && restoredRanger.SoleHealingBenefactorUntil == ranger.SoleHealingBenefactorUntil, $"{rate}: the claim is kept across a save.");
            var gain = Issue440Gain(world, ranger, 30 * 3);
            var restoredGain = Issue440Gain(restored, restoredRanger, 30 * 3);
            Check.That(gain == restoredGain && restored.StateHash == world.StateHash, $"{rate}: the restored world heals the same ({gain} against {restoredGain}).");
        }
    }
}
