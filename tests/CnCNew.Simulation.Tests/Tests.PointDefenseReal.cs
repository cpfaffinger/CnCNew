using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.World;

// Issues 443 and 444 (the owner's playtest of release 0.0.1): the Paladin's point-defence laser does not shoot down missiles, and the Avenger's may not either.
static partial class Tests
{
    /// <summary>
    /// On real data a tank with a <c>PointDefenseLaserUpdate</c> (the Paladin, the Avenger) stands still while four enemy Missile Defenders fire their missiles at it from beyond the laser's
    /// range. The laser fires at the missiles (<c>scanClosestTarget</c> takes a <c>SMALL_MISSILE</c> in range first), every missile it fires at dies, and missiles die so before they hit. A missile that has detonated
    /// stays alive in <c>KILL_SELF</c> for <c>KillSelfDelay</c> frames, and the original's scan takes it as well (it asks only for an enemy of the kinds that is not dead). At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealPointDefenseLaserShootsDownMissiles(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var (tank, laser) in new[] { ("AmericaTankPaladin", "PaladinPointDefenseLaser"), ("AmericaTankAvenger", "AvengerPointDefenseLaser") })
            foreach (var rate in TargetRates)
            {
                var world = real.WorldWith(rate, RuleProfile.Product);
                var spawned = SpawnReal(world, (tank, 1, 400, 400), ("AmericaInfantryMissileDefender", 2, 560, 400), ("AmericaInfantryMissileDefender", 2, 400, 560),
                    ("AmericaInfantryMissileDefender", 2, 240, 400), ("AmericaInfantryMissileDefender", 2, 400, 240));
                var defender = spawned.Single(value => value.Template.Name == tank);
                var missiles = new HashSet<uint>();
                var lasered = new HashSet<uint>();
                var impacted = new HashSet<uint>();
                var laserShots = 0;
                var missileShots = 0;
                for (var step = 0; step < world.Frames(30 * 20); step++)
                {
                    foreach (var missile in world.Entities.Where(value => value.Template.Name == "MissileDefenderMissile")) missiles.Add(missile.Id.Value);
                    var result = world.Step();
                    foreach (var missile in world.Entities.Where(value => value.Template.Name == "MissileDefenderMissile")) missiles.Add(missile.Id.Value);
                    foreach (var value in result.Events.Where(value => value.Kind == SimEventKind.WeaponImpact && missiles.Contains(value.Entity.Value))) impacted.Add(value.Entity.Value);
                    foreach (var value in result.Events.Where(value => value.Kind == SimEventKind.WeaponFired))
                    {
                        if (value.Entity == defender.Id && value.Text.StartsWith(laser, StringComparison.Ordinal))
                        {
                            if (missiles.Contains(value.Other.Value)) { missileShots++; lasered.Add(value.Other.Value); }
                            laserShots++;
                        }
                    }
                }

                Console.WriteLine($"    {tank} {rate}: {missiles.Count} missiles, {laserShots} laser shots, {missileShots} at missiles, {lasered.Count} lasered, {impacted.Count} hit, health {defender.Body!.Health}/{defender.Body.MaxHealth}");
                Check.That(missiles.Count >= 4, $"{tank}, {rate}: the Missile Defenders fire at the tank ({missiles.Count} missiles).");
                Check.That(missileShots > 0, $"{tank}, {rate}: the laser fires at the missiles ({laserShots} laser shots, {missileShots} at a missile).");
                Check.That(lasered.All(id => world.FindEntity(new CnCNew.Simulation.Ids.EntityId(id)) is null), $"{tank}, {rate}: every missile the laser fired at is gone.");
                Check.That(lasered.Count(id => !impacted.Contains(id)) > 0, $"{tank}, {rate}: missiles are shot down before they hit ({lasered.Count(id => !impacted.Contains(id))} of {lasered.Count}).");
            }
    }
}
