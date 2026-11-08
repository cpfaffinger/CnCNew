using System;
using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Numerics;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SpiralRadiusFiles() => new Dictionary<string, string>(SpiralFiles())
    {
        // The Comanche's numbers (AmericaAir.ini): Mass 50, SpiralOrbitTurnRate 140, SpiralOrbitForwardSpeed 350, damping .9999; no self spin, so the heading stays the spawn's.
        ["Data/INI/Object/RateFlightSpiralRadius.ini"] = """
        Object Crasher
          KindOf = VEHICLE AIRCRAFT
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 50.0
          End
          Behavior = HelicopterSlowDeathBehavior Death01
            DestructionDelay = 99999999
            SpiralOrbitTurnRate = 140.0
            SpiralOrbitForwardSpeed = 350.0
            SpiralOrbitForwardSpeedDamping = .9999
            MinSelfSpin = 0.0
            MaxSelfSpin = 0.0
            SelfSpinUpdateDelay = 0
            SelfSpinUpdateAmount = 0.0
            FallHowFast = 5%
            MinBladeFlyOffDelay = 99990
            MaxBladeFlyOffDelay = 99999
            DelayFromGroundToFinalDeath = 500
          End
        End
        """,
    };

    /// <summary>
    /// The owner's playtest of 8 November 2026: a shot-down Comanche's crash spiral was far too wide. <c>HelicopterSlowDeathBehavior::update</c> does not move the wreck by the orbit speed: it
    /// pushes it with <c>applyMotiveForce</c> (the speed turned along the orbit), so the force over the <c>Mass</c> (50 for the Comanche) is an acceleration of the physics step, and the
    /// 2D friction of the helicopter locomotors takes the velocity across the heading. Hand calculation per logic frame from the data: v += F/m - 0.15 * lateral; p += v. The wreck
    /// follows it at 30, 60 and 120 Hz, and the extent of its spiral is the original's (about 40 units, not the 150 of a plain orbit at the force's speed).
    /// </summary>
    public static void HelicopterSpiralRadiusFollowsThePhysics()
    {
        const int Frames = 50;
        foreach (var rate in FlightRates)
        {
            var (world, feeder) = FlightRateWorld(rate, SpiralRadiusFiles(), terrain: false);
            feeder.Spawn(1, "Crasher", x: 5000, y: 5000, z: 4000);
            world.Step();
            StepFrames(world, 3);
            var wreck = Only(world, "Crasher");
            var (startX, startY) = ((double)wreck.X.ToDecimal(), (double)wreck.Y.ToDecimal());
            var heading = (double)wreck.Angle.ToDecimal();
            feeder.Kill(wreck.Id.Value);
            StepFrames(world, 1);
            // The reference: frame k's physics step uses the force of the update of frame k - 1.
            double px = 0, py = 0, vx = 0, vy = 0, ax = 0, ay = 0, fa = heading, fs = 350.0 / 30.0;
            var turn = 140.0 * Math.PI / 180.0 / 30.0;
            double extent = 0;
            for (var frame = 1; frame <= Frames; frame++)
            {
                var lateral = vx * -Math.Sin(heading) + vy * Math.Cos(heading);
                ax -= 0.15 * lateral * -Math.Sin(heading);
                ay -= 0.15 * lateral * Math.Cos(heading);
                vx += ax;
                vy += ay;
                px += vx;
                py += vy;
                extent = Math.Max(extent, Math.Sqrt(px * px + py * py));
                ax = Math.Cos(fa) * fs / 50.0;
                ay = Math.Sin(fa) * fs / 50.0;
                fa += turn;
                fs *= 0.9999;
                StepFrames(world, 1);
                var (dx, dy) = ((double)wreck.X.ToDecimal() - startX, (double)wreck.Y.ToDecimal() - startY);
                Check.That(Math.Abs(dx - px) < 0.2 && Math.Abs(dy - py) < 0.2, $"{rate.TicksPerSecond} Hz, frame {frame}: the wreck is at ({dx:0.##}, {dy:0.##}), the hand calculation says ({px:0.##}, {py:0.##}).");
            }
            Check.That(extent < 90.0, $"The spiral spans {extent:0.#} units from the start after {Frames} frames (a plain orbit at the force's speed would reach 287).");
        }
    }
}
