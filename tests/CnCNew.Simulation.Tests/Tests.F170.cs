using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// F-170 (#167): a unit whose weapon no turning turret carries aims with its body (<c>AIAttackAimAtTargetState::update</c>). A unit still on a move when it is ordered to attack went on
/// moving and fired at once, unturned: <c>AimBodyAt</c> took any move target for an aim. In the original the attack exits the move state but leaves the locomotor's goal on the path, so
/// a unit that already faces its victim fires on its way; one that does not gets <c>setLocomotorGoalOrientation</c> instead of its path, turns on the spot and fires only when it
/// faces the victim.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A gunner drives east and is ordered, in every tick, to attack a target well to its side: it gives up its move, turns, and its first shot leaves when it faces the target, in the
    /// same frame of the original at 30, 60 and 120 Hz and from the same place. A target straight ahead is fired at on the way, the move going on.
    /// </summary>
    public static void F170AMovingUnitTurnsToItsVictimBeforeItFires()
    {
        foreach (var (name, targetX, targetY, ahead) in new[] { ("to the side", 300, 330, false), ("straight ahead", 560, 100, true) })
        {
            long? referenceFrame = null;
            (Fixed X, Fixed Y)? referencePlace = null;
            foreach (var (rate, factor) in WorldRates)
            {
                var (world, feeder, _) = RatedWorld(rate, F138Files());
                feeder.Spawn(1, "F138Gunner", owner: 1, x: 100, y: 100);
                feeder.Spawn(2, "TurretTarget", owner: 2, x: targetX, y: targetY);
                feeder.SetRelationship(1, 2, Relationship.Enemies);
                world.Step();
                var gunner = Slotted(world, "F138Gunner");
                var victim = IdOf(world, "TurretTarget");
                F138ToFrameStart(world);
                Check.That(feeder.SetMoveTarget(1, gunner.Id.Value, 1500, 100).Accepted, $"{rate}: the move is accepted.");
                for (var tick = 0; tick < 20 * factor; tick++) world.Step();
                F138ToFrameStart(world);
                Check.That(gunner.HasMoveTarget && gunner.Angle.Abs() < Fixed.FromDecimal(0.01m), $"{rate}: the gunner drives east ({gunner.Angle.Raw}).");
                var start = world.Tick;
                long? shotTick = null;
                Fixed angleAtShot = Fixed.Zero, bearingAtShot = Fixed.Zero;
                var movingAtShot = false;
                for (var tick = 0; tick < 120 * factor && shotTick is null; tick++)
                {
                    feeder.FireWeapon(1, gunner.Id.Value, victim);
                    var events = world.Step().Events;
                    if (!events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == gunner.Id)) continue;
                    shotTick = world.Tick - 1;
                    var target = world.FindEntity(new EntityId(victim))!;
                    angleAtShot = gunner.Angle;
                    bearingAtShot = FixedTrig.Atan2(target.Y - gunner.Y, target.X - gunner.X);
                    movingAtShot = gunner.HasMoveTarget;
                }

                Check.That(shotTick is not null, $"{name}, {rate}: the gunner fires.");
                if (shotTick is null) continue;
                var frame = (shotTick.Value - start) / factor;
                var off = FixedTrig.AngleDifference(angleAtShot, bearingAtShot).Abs();
                System.Console.WriteLine($"       {name}, {rate}: first shot in frame {frame}, {off.ToDecimal():0.0000} rad off its bearing, still moving: {movingAtShot}, at ({gunner.X.ToDecimal():0.000}, {gunner.Y.ToDecimal():0.000})");
                Check.That(off <= Fixed.FromDecimal(0.036m), $"{name}, {rate}: the shot leaves when the gunner faces its target ({off.ToDecimal()} rad off).");
                Check.That(movingAtShot == ahead, $"{name}, {rate}: the gunner {(ahead ? "fires on its way" : "has given up its move")} (moving: {movingAtShot}).");
                referenceFrame ??= frame;
                Check.That(frame == referenceFrame, $"{name}, {rate}: the first shot in frame {frame}, at 30 Hz in frame {referenceFrame}.");
                if (!ahead)
                {
                    referencePlace ??= (gunner.X, gunner.Y);
                    Check.That((gunner.X - referencePlace.Value.X).Abs() <= Fixed.FromDecimal(0.001m) && (gunner.Y - referencePlace.Value.Y).Abs() <= Fixed.FromDecimal(0.001m),
                        $"{name}, {rate}: the gunner stopped where it stops at 30 Hz ({gunner.X.ToDecimal()}, {gunner.Y.ToDecimal()} against {referencePlace.Value.X.ToDecimal()}, {referencePlace.Value.Y.ToDecimal()}).");
                }
            }
        }
    }
}
