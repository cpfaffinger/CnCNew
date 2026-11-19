using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

/// <summary>
/// Issue #462: an attack ordered once lasts. <c>aiAttackObject</c> puts the unit into <c>AIAttackState</c>, which approaches, aims and fires until the victim is dead, the attack fails
/// (no weapon, out of reach of a unit that cannot approach, out of ammunition, a victim that is no enemy any more) or the unit gets another order (<c>AIStates.cpp</c>). The port ended an
/// attack 1.5 s after the weapon was ready again unless the command giver ordered it on.
/// </summary>
static partial class Tests
{
    /// <summary>One fire order at a victim in range that a tank's single gun cannot kill: the tank keeps firing on its cycle, the same frames at every rate.</summary>
    public static void OrderedAttack462OneFireOrderLasts()
    {
        long[]? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            world.SelfAcquisition = false;
            feeder.Spawn(1, "PlainTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 0);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "PlainTank");
            var victim = Slotted(world, "TurretTarget");
            var orderTick = world.Tick;
            Check.That(feeder.FireWeapon(1, tank.Id.Value, victim.Id.Value).Accepted, $"{rate}: the fire order is accepted.");
            var shots = new List<long>();
            for (var step = 0; step < 150 * factor; step++)
                foreach (var shot in world.Step().Events)
                    if (shot.Kind == SimEventKind.WeaponFired && shot.Entity == tank.Id) shots.Add((shot.Tick - orderTick) / factor);

            Check.That(world.AcquiredVictimOf(tank) == victim.Id, $"{rate}: five seconds after the one order the tank still attacks its victim.");
            // TurretGun waits 9 frames between shots and the attack machine fires on every second frame at most (F-140): a shot every 10 frames, 15 in 150 frames.
            Check.That(shots.Count >= 14, $"{rate}: the tank fires on without further orders ({shots.Count} shots in 150 frames: {string.Join(", ", shots)}).");
            reference ??= [..shots];
            Check.That(shots.SequenceEqual(reference), $"{rate}: the shots fall in the frames of the 30 Hz run ({string.Join(", ", shots)} against {string.Join(", ", reference)}).");
        }
    }

    /// <summary>
    /// One attack order (the client's <c>PursueCommand</c>) at a victim out of range: the tank drives at it, stops where it can fire, and fires until it is told otherwise - the same frames
    /// at every rate. Another order ends the attack.
    /// </summary>
    public static void OrderedAttack462OnePursueOrderApproachesAndFires()
    {
        long[]? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            world.SelfAcquisition = false;
            feeder.Spawn(1, "ChaseTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 400, y: 0);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "ChaseTank");
            var victim = Slotted(world, "TurretTarget");
            var orderTick = world.Tick;
            Check.That(feeder.Pursue(1, tank.Id.Value, victim.Id.Value, 400, 0).Accepted, $"{rate}: the attack order is accepted.");
            var shots = new List<long>();
            for (var step = 0; step < 300 * factor; step++)
                foreach (var shot in world.Step().Events)
                    if (shot.Kind == SimEventKind.WeaponFired && shot.Entity == tank.Id) shots.Add((shot.Tick - orderTick) / factor);

            Check.That(world.AcquiredVictimOf(tank) == victim.Id, $"{rate}: ten seconds after the one order the tank still attacks its victim.");
            Check.That(!tank.HasMoveTarget, $"{rate}: the tank stopped where it can fire (it drives to ({tank.MoveTargetX}, {tank.MoveTargetY}) from x {tank.X}).");
            Check.That(tank.X.Floor() is > 200 and < 300, $"{rate}: the tank stands within its gun's range of 150 and short of the victim (x {tank.X}).");
            Check.That(shots.Count >= 10, $"{rate}: the tank fires on without further orders ({shots.Count} shots: {string.Join(", ", shots)}).");
            reference ??= [..shots];
            Check.That(shots.SequenceEqual(reference), $"{rate}: the shots fall in the frames of the 30 Hz run ({string.Join(", ", shots)} against {string.Join(", ", reference)}).");

            // Another order is another state: the attack is over.
            Check.That(feeder.SetMoveTarget(1, tank.Id.Value, 0, 0).Accepted, $"{rate}: the move order is accepted.");
            for (var i = 0; i < factor; i++) world.Step();
            Check.That(!world.AcquiredVictimOf(tank).IsValid, $"{rate}: a move order ends the attack.");
        }
    }
}
