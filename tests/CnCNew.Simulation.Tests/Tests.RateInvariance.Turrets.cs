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

    /// <summary>Per tick the turret angle of a lone <c>ScanTank</c> (it glances around at random while idle) over <paramref name="frames"/> logic frames after its first frame.</summary>
    private static List<Fixed> IdleScanAngles(SimulationRate rate, int factor, int frames)
    {
        var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
        feeder.Spawn(1, "ScanTank", owner: 1, x: 0, y: 0);
        for (var i = 0; i < factor; i++) world.Step();
        var scanner = Slotted(world, "ScanTank");
        var angles = new List<Fixed>();
        for (var step = 0; step < frames * factor; step++)
        {
            world.Step();
            angles.Add(scanner.TurretOf(0)!.Value.Angle);
        }

        return angles;
    }

    /// <summary>The lengths of the runs of ticks in which the turret stood still away from rest (the holds at the scan angles).</summary>
    private static List<int> HoldRuns(List<Fixed> angles)
    {
        var runs = new List<int>();
        var length = 1;
        for (var index = 1; index < angles.Count; index++)
        {
            if (angles[index] == angles[index - 1]) { length++; continue; }
            if (angles[index - 1] != Fixed.Zero && length > 1) runs.Add(length);
            length = 1;
        }

        return runs;
    }

    /// <summary>
    /// "Hold until and including <c>HoldUntil</c>" is the recentre time plus one step. That step is a logic frame of the original at every rate: a turret that reached its scan angle
    /// stands there RecenterTime + 1 frames (16 at 30 Hz), counted from the end of the frame it arrived in to the start of the frame it leaves in.
    /// </summary>
    public static void RateInvarianceTurretHoldsOneFrameMoreThanTheRecentreTime()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var runs = HoldRuns(IdleScanAngles(rate, factor, 1_500));
            // The turret is at the angle from the last tick of its arrival frame on: factor - 1 ticks of that frame are not part of the run.
            Check.That(runs.Count >= 8 && runs.All(run => run + factor - 1 == 16 * factor),
                $"{rate}: every hold lasts 15 logic frames and one frame more ({string.Join(",", runs.Distinct())} ticks, expected {16 * factor - (factor - 1)}).");
        }
    }

    /// <summary>
    /// The idle scan is the same film at every rate: the first glance starts in the same logic frame after the object appeared (the turret's first step, which sets the timer, is one
    /// frame after the spawn and takes a frame), and from then on the turret is at the 30 Hz angle at the end of every frame, over 50 seconds of glances, holds and returns.
    /// </summary>
    public static void RateInvarianceTurretIdleScanIsTheSameAtEveryFrame()
    {
        const int Frames = 1_500;
        var reference = IdleScanAngles(SimulationRate.Hz30, 1, Frames);
        var firstGlance = reference.FindIndex(angle => angle != Fixed.Zero);
        Check.That(firstGlance > 30 && reference.Distinct().Count() > 50, $"30 Hz: the turret glances around (first glance in frame {firstGlance}).");
        foreach (var (rate, factor) in WorldRates.Skip(1))
        {
            var angles = IdleScanAngles(rate, factor, Frames);
            var first = angles.FindIndex(angle => angle != Fixed.Zero);
            Check.That(first == firstGlance * factor, $"{rate}: the first glance starts in the first tick of the same logic frame as at 30 Hz (tick {first}, expected {firstGlance * factor}).");
            var different = Enumerable.Range(0, Frames).Where(frame => angles[(frame + 1) * factor - 1] != reference[frame]).ToList();
            Check.That(different.Count == 0, $"{rate}: at the end of every logic frame the turret is where the 30 Hz turret is ({different.Count} of {Frames} frames differ, the first is frame {different.FirstOrDefault(-1)}).");
        }
    }
}
