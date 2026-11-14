using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// Issue 318 (the owner's playtest of 13 November 2026): "a tank was repaired by another tank's drone", with about twenty Paladins and a drone bought for all of them.
static partial class Tests
{
    /// <summary>
    /// Twenty Paladins stand side by side and the player buys the battle drone for each, one purchase per tank - what the command bar sends for a selected group. Every tank gets one drone of its
    /// own whose master it is; eight tanks are hurt below the drones' threshold; in every tick that a tank's health rises its own drone is welding, a tank that is not hurt never changes, the
    /// hurt ones are repaired, and a world saved and restored half way plays on to the live world's hashes. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealDroneGroupRepairsEachOnlyItsOwnMaster(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            world.AddMoney(new PlayerId(1), Fixed.FromInt(20000));
            var specs = Enumerable.Range(0, 20).Select(index => ("AmericaTankPaladin", 1, 200 + 30 * (index % 5), 200 + 30 * (index / 5))).ToArray();
            var tanks = SpawnReal(world, specs);
            Check.That(tanks.Length == 20, $"{rate}: twenty Paladins.");
            foreach (var tank in tanks)
                Check.That(world.Submit(new PurchaseUpgradeCommand(new PlayerId(1), ++_sequence, world.Tick, "Upgrade_AmericaBattleDrone", tank.Id)).Accepted, $"{rate}: the purchase for {tank.Id} is accepted.");
            for (var step = 0; step < world.Frames(30 * 30) && world.Entities.Count(value => value.Template.Name == "AmericaVehicleBattleDrone") < 20; step++) world.Step();
            var drones = world.Entities.Where(value => value.Template.Name == "AmericaVehicleBattleDrone").ToArray();
            Check.That(drones.Length == 20, $"{rate}: twenty drones ({drones.Length}).");
            var masters = drones.Select(drone => drone.Modules.OfType<SlavedUpdate>().Single().Slaver).ToArray();
            Check.That(masters.Distinct().Count() == 20 && tanks.All(tank => masters.Contains(tank.Id)), $"{rate}: every tank is the master of exactly one drone.");

            for (var index = 0; index < 20; index++)
                if (index % 5 == 0 || index % 5 == 2)
                    Check.That(world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, tanks[index].Id, tanks[index].Body!.MaxHealth / Fixed.FromInt(2), DamageTypes.Explosion, 0)).Accepted, $"{rate}: tank {index} is hurt.");
            world.Step();
            var hurt = tanks.Where(tank => tank.Body!.Health < tank.Body.MaxHealth).ToArray();
            Check.That(hurt.Length >= 6, $"{rate}: tanks are hurt ({hurt.Length}).");
            var before = hurt.ToDictionary(tank => tank.Id, tank => tank.Body!.Health);
            var droneOf = drones.ToDictionary(drone => drone.Modules.OfType<SlavedUpdate>().Single().Slaver);
            var last = tanks.ToDictionary(tank => tank.Id, tank => tank.Body!.Health);
            SimulationWorld? restored = null;
            var total = world.Frames(30 * 40);
            for (var step = 0; step < total; step++)
            {
                world.Step();
                if (restored is not null)
                {
                    restored.Step();
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: the restored world agrees at step {step}.");
                }
                if (step == total / 3) restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
                foreach (var tank in tanks)
                {
                    var health = tank.Body!.Health;
                    if (health > last[tank.Id])
                    {
                        var drone = droneOf[tank.Id];
                        Check.That(health - last[tank.Id] <= world.Time.PerSecondToPerTick(Fixed.FromInt(10)) + Fixed.FromDecimal(0.001m), $"{rate}: tank {tank.Id} gained more in one tick than one drone's repair rate gives ({health - last[tank.Id]}).");
                        Check.That(drone.Modules.OfType<SlavedUpdate>().Single().IsRepairing, $"{rate}: tank {tank.Id} rose in step {step} without its own drone welding.");
                    }
                    last[tank.Id] = health;
                }
            }
            foreach (var tank in tanks.Except(hurt))
                Check.That(tank.Body!.Health == tank.Body.MaxHealth, $"{rate}: a tank that was not hurt is as it was.");
            Check.That(hurt.All(tank => tank.Body!.Health > before[tank.Id]), $"{rate}: every hurt tank was repaired by its drone.");
        }
    }
}
