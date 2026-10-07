using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package turrets (docs/RATE-INVARIANCE-TURRETS.md): turrets, the fire gate, the firing tracker and the units' own target acquisition are the same game at 30, 60 and
/// 120 Hz. Each test runs one small scene of the synthetic fixture at the three rates and compares game time and angles; 30 Hz is the reference.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// The turret decides its step once per logic frame of the original exactly as at 30 Hz and walks it in equal parts: at the end of every frame its angle is the 30 Hz angle to the
    /// bit, in between it is on the way, and the shot is released in the same frame.
    /// </summary>
    public static void RateInvarianceTurretStepsAreDecidedPerFrame()
    {
        const int Frames = 40;
        var runs = new List<(SimulationRate Rate, int Factor, int Shot, List<Fixed> Angles)>();
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            feeder.Spawn(1, "TurretTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "TurretTank");
            var target = IdOf(world, "TurretTarget");
            var angles = new List<Fixed>();
            var shot = -1;
            for (var step = 0; step < Frames * factor; step++)
            {
                feeder.FireWeapon(1, tank.Id.Value, target);
                var result = world.Step();
                if (shot < 0 && result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shot = step;
                angles.Add(tank.TurretOf(0)!.Value.Angle);
            }

            runs.Add((rate, factor, shot, angles));
        }

        var reference = runs[0];
        Check.That(reference.Shot == 15, $"30 Hz: 90 degrees at 6 degrees per frame release the shot 15 frames after the first order (got {reference.Shot}).");
        foreach (var (rate, factor, shot, angles) in runs.Skip(1))
        {
            Check.That(shot == reference.Shot * factor, $"{rate}: the shot is released in the same frame as at 30 Hz (step {shot}, expected {reference.Shot * factor}).");
            for (var frame = 0; frame < Frames; frame++)
            {
                var atEnd = angles[(frame + 1) * factor - 1];
                Check.That(atEnd == reference.Angles[frame], $"{rate}: at the end of frame {frame} the turret is where the 30 Hz turret is ({atEnd.Raw} raw, 30 Hz {reference.Angles[frame].Raw}).");
                if (frame >= 14) continue;
                // Still turning: every tick inside the frame moves the turret further toward that angle.
                for (var part = 0; part < factor - 1; part++)
                {
                    var inside = angles[frame * factor + part];
                    var before = frame == 0 ? Fixed.Zero : reference.Angles[frame - 1];
                    Check.That(inside > before && inside < atEnd && (part == 0 || inside > angles[frame * factor + part - 1]), $"{rate}: frame {frame}, tick {part}: the turret is on its way ({inside.Raw} raw between {before.Raw} and {atEnd.Raw}).");
                }
            }
        }
    }
}
