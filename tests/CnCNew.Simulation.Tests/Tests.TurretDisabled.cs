using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// F-153: a disable that comes after a turret's decision - the Gatling cannon's power gone in a later phase of the tick, an EMP - stops the turret from the next frame on, not in the
    /// middle of the frame's step. The 30 Hz turret has taken its whole step in the tick it decided it; above 30 Hz it walks the step in shares and was left standing between two angles
    /// (Death Valley America against China, frame 39,626: 0.0256 against 0).
    /// </summary>
    public static void ADisableDoesNotStopATurretInTheMiddleOfItsFrame()
    {
        List<long>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            feeder.Spawn(1, "TurretTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "TurretTank");
            var target = IdOf(world, "TurretTarget");
            var angles = new List<long>();
            for (var frame = 0; frame < 12; frame++)
            {
                for (var tick = 0; tick < factor; tick++)
                {
                    feeder.FireWeapon(1, tank.Id.Value, target);
                    world.Step();
                    // The power fails (the EMP lands) after the first tick of frame 4: the turret is in the middle of a step it has decided.
                    if (frame == 4 && tick == 0) world.DisableByEmpUntil(tank, world.Tick + world.Frames(300));
                }
                angles.Add(tank.TurretOf(0)!.Value.Angle.Raw);
            }
            Check.That(angles[3] != angles[2] && angles[4] != angles[3], $"{rate}: the turret is turning when the disable comes ({string.Join(", ", angles)}).");
            Check.That(angles[5] == angles[4] && angles[11] == angles[4], $"{rate}: the disabled turret stands from the next frame on ({string.Join(", ", angles)}).");
            reference ??= angles;
            SameSeries(reference, angles, $"The angle of the disabled turret at the frame ends at {rate}");
        }
    }
}
