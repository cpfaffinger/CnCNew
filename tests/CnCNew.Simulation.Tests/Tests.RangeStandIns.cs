using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
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
}
