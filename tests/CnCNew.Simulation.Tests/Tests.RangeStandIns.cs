using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #433: modules that measured a weapon's range with a stand-in between the centres after #422 made every attack measure between the bounding circles (Weapon::isWithinAttackRange,
// FROM_BOUNDINGSPHERE_2D, the range a quarter cell short): each now asks the world's own test, on the shipped data, at 30, 60 and 120 Hz.
static partial class Tests
{
    /// <summary>
    /// <c>CleanupHazardUpdate::fireWhenReady</c> orders <c>aiAttackObject</c>, and the attack fires where <c>Weapon::isWithinAttackRange</c> says so - between the two bounding circles. A Medic
    /// 99 units from the centre of a large poison field (radius 70) found it within its <c>ScanRange</c> of 100 and reaches it from where it stands; the port walked it to three quarters of
    /// the weapon's range from the field's centre first, because 99 is beyond the range of 100 less a quarter cell between the centres.
    /// </summary>
    public static void RealCleanupHazardFiresWhereItsWeaponReaches(string directory)
    {
        var data = OpenRealFireData(directory);
        var wrong = new List<string>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var world = NewRealFireWorld(data, rate, computer: false);
            ulong sequence = 0;
            var field = SpawnReal(world, ref sequence, "PoisonFieldLarge", 2, 499, 400);
            var medic = SpawnReal(world, ref sequence, "AmericaVehicleMedic", 1, 400, 400);
            var (startX, startY) = (medic.X, medic.Y);
            var start = world.Tick;
            var moved = false;
            var events = new List<SimEvent>();
            for (var tick = 0; tick < world.Frames(90); tick++)
            {
                events.AddRange(world.Step().Events);
                if (medic.HasMoveTarget || medic.X != startX || medic.Y != startY) moved = true;
            }
            var frames = ShotFrames(world, start, events, null, medic.Id);
            System.Console.WriteLine($"       {rate}: {frames.Count} cleaning shots, first at frame {(frames.Count > 0 ? frames[0] : -1)}; moved {moved}, now at {medic.X.ToDecimal():0.#}");
            if (frames.Count == 0) wrong.Add($"{rate}: the Medic did not clean the field it found (field health {(field.Body is { } body ? body.Health.ToDecimal() : 0)})");
            if (moved) wrong.Add($"{rate}: it walked to {medic.X.ToDecimal():0.#} instead of firing from where it stands, where its weapon reaches the field between the bounding circles");
        }
        Check.That(wrong.Count == 0, string.Join("; ", wrong) + ".");
    }

    /// <summary>
    /// <c>SlavedUpdate::update</c> takes the master's <c>getCurrentVictim</c> and, with an <c>AttackRange</c>, flies its drone over it (<c>doAttackLogic</c>: up to 75 from the master). A Crusader
    /// ordered at a Chinese command centre 220 from its centre fires at it - its gun of 150 reaches the building's circle - and its battle drone flies toward the building. The port took the
    /// nearest enemy within the gun's data range of the master's centre for the victim, found none, and kept the drone guarding beside its master.
    /// </summary>
    public static void RealDroneFliesOverItsMastersVictim(string directory)
    {
        var real = new RealTargets(directory);
        var wrong = new List<string>();
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            world.AddMoney(new PlayerId(1), Fixed.FromInt(20000));
            var master = SpawnReal(world, ("AmericaTankCrusader", 1, 300, 400))[0];
            Check.That(world.Submit(new PurchaseUpgradeCommand(new PlayerId(1), ++_sequence, world.Tick, "Upgrade_AmericaBattleDrone", master.Id)).Accepted, $"{rate}: the drone is bought.");
            for (var step = 0; step < world.Frames(30 * 30) && world.Entities.All(value => value.Template.Name != "AmericaVehicleBattleDrone"); step++) world.Step();
            var drone = world.Entities.Single(value => value.Template.Name == "AmericaVehicleBattleDrone");
            for (var step = 0; step < world.Frames(30 * 5); step++) world.Step();
            var building = SpawnReal(world, ("ChinaCommandCenter", 2, 520, 400))[0];
            world.SetAttitude(building, (int)Attitude.Sleep);
            for (var step = 0; step < world.Frames(30 * 2); step++) world.Step();
            var shots = 0;
            var closest = Fixed.MaxValue;
            for (var step = 0; step < world.Frames(30 * 10); step++)
            {
                if (step % world.Frames(30) == 0) world.Submit(new FireWeaponCommand(new PlayerId(1), ++_sequence, world.Tick, master.Id, building.Id));
                shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == master.Id);
                var (dx, dy) = (drone.X - building.X, drone.Y - building.Y);
                closest = Fixed.Min(closest, Fixed.Sqrt(dx * dx + dy * dy));
            }
            System.Console.WriteLine($"       {rate}: the Crusader fired {shots} shots from {master.X.ToDecimal():0.#}; its drone came within {closest.ToDecimal():0.#} of the building's centre");
            if (shots == 0) wrong.Add($"{rate}: the Crusader did not fire at the building");
            // Guarding, the drone stays 35 from its master (GuardMaxRange), at most 255 from the building; over the victim it flies up to 75 from the master toward it.
            if (closest > Fixed.FromInt(170)) wrong.Add($"{rate}: the drone stayed by its master ({closest.ToDecimal():0.#} from the building) instead of flying over the master's victim");
        }
        Check.That(wrong.Count == 0, string.Join("; ", wrong) + ".");
    }
}
