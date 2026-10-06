using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The turret model (<c>TurretAI</c> in the reference, docs/SIM-TURRET-AIM.md): every turret of an object has an angle and a pitch that move at the turret's own rates toward the victim
/// of the unit's attack (an accepted fire order or a pursuit; the aim persists until the victim is gone or the unit is ordered elsewhere); a weapon on a turret-controlled slot fires only
/// once its turret points at the victim; an idle turret turns back to its natural angle and, if its data say so, glances around at random. Objects without a turret, with a turret that
/// cannot turn or starts disabled, and weapons on slots no turret controls fire as before.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>REL_THRESH</c> of <c>TurretAIAimTurretState::update</c>: "about 2 degrees" (0.035 rad) — how closely the turret has to point at the victim before the weapon may fire.</summary>
    private static readonly Fixed TurretAlignThreshold = Fixed.FromDecimal(0.035m);

    /// <summary>
    /// A one-off aim (a module's or script's shot, not part of an attack order) keeps its aim at least this long after the latest request (and past the weapon's reload): logic frames
    /// of the original, 0.5 s. An own value of this simulation.
    /// </summary>
    private const long TurretAimGraceFrames = 15;

    /// <summary>The longest reload that extends the hold (a weapon that reloads at an airfield reports an endless wait): 15 s in logic frames of the original. An own value of this simulation.</summary>
    private const long TurretReloadHoldCapFrames = 15L * SimulationRate.OriginalFramesPerSecond;

    /// <summary><c>ENABLE_SWEEP_FRAME_COUNT</c> (TurretAI.cpp, <c>m_enableSweepUntil = now + ENABLE_SWEEP_FRAME_COUNT</c>): the sweep of <c>TurretFireAngleSweep</c> stays on this many logic frames of the original after a shot.</summary>
    private const long TurretSweepEnableFrames = 3;

    /// <summary>
    /// How long an attacker that stands still keeps its turrets on a victim out of reach of every weapon before it gives up (the reference's attack state ends when it may not chase):
    /// logic frames of the original, 1.5 s. An own value of this simulation.
    /// </summary>
    private const long TurretLoseInterestFrames = 45;

    // The four above in ticks of this world, resolved once when the world is created.
    private readonly long _turretAimGraceTicks;
    private readonly long _turretReloadHoldCapTicks;
    private readonly long _turretSweepEnableTicks;
    private readonly long _turretLoseInterestTicks;

    /// <summary><c>friend_turnTowardsAngle(…, 0.5f, 0.0f)</c> of the recenter and idle-scan states.</summary>
    private static readonly Fixed TurretRecenterRateFactor = Fixed.FromDecimal(0.5m);

    /// <summary>Ids (ascending) of the entities with a turret away from rest or with an idle scan; <see cref="StepTurrets"/> visits exactly these.</summary>
    private readonly SortedSet<uint> _activeTurrets = [];

    private long _turretWaits;

    /// <summary>How many fire orders were held back because a turret did not yet point at its victim (statistics; part of the hash once nonzero).</summary>
    public long TurretWaits => _turretWaits;

    private void InitializeTurrets(Entity entity)
    {
        if (entity.Template.Turrets is not { } set) return;
        var states = new TurretState?[TurretSet.MaxTurrets];
        for (var index = 0; index < states.Length; index++)
            if (set[index] is { } info) states[index] = new TurretState(info);
        entity.Turrets = states;
    }

    /// <summary>Entities restored from a snapshot or just created: registers the ones with a turret away from rest, or with an idle scan, for stepping.</summary>
    private void RegisterTurrets(Entity entity)
    {
        if (entity.Turrets is not { } states || entity.Template.Turrets is not { } set) return;
        for (var index = 0; index < states.Length; index++)
            if (states[index] is { } state && set[index] is { } info && NeedsStep(info, state)) { _activeTurrets.Add(entity.Id.Value); return; }
    }

    /// <summary>A turret needs stepping while it is away from rest, and always when its data make it glance around (the idle timer runs while it rests).</summary>
    private static bool NeedsStep(TurretInfo info, TurretState state) => !state.IsPristine(info) || info.CanScan;

    private void LoadTurrets(Entity entity, IStateReader reader)
    {
        if (!reader.Has("turrets")) return;
        if (entity.Turrets is not { } states) throw new InvalidDataException($"Entity {entity.Id} ('{entity.Template.Name}') carries turret state its template does not have.");
        foreach (var item in reader.Array("turrets"))
        {
            var index = checked((int)item.Int("index"));
            if (index < 0 || index >= states.Length || states[index] is not { } state) throw new InvalidDataException($"Entity {entity.Id} has no turret {index}.");
            state.Load(item);
        }
    }

    // ------------------------------------------------------------------------------------------------------ the fire gate

    /// <summary>
    /// A fire order for <paramref name="slot"/> against <paramref name="victim"/> reaches the turrets that control the slot (every turret of a <c>TurretsLinked</c> object): each is told
    /// to aim at the victim (<c>AIUpdateInterface::setTurretTargetObject</c>, called by <c>AIAttackAimAtTargetState</c>) and the result says whether all of them already point at it
    /// (<c>TurretAIAimTurretState::update</c> returning success, after which <c>TURRETAI_FIRE</c> fires). Always true for an object without a turret that takes part in aiming.
    /// An attack order starts (or continues) the unit's attack, which keeps the aim until it ends.
    /// </summary>
    private bool AimTurretsAt(Entity source, int slot, Entity victim, TurretAimOrder order = TurretAimOrder.Shot)
    {
        if (source.Turrets is not { } states || source.Template.Turrets is not { } set) return true;
        var aligned = true;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { Gates: true } info) continue;
            if (!set.Linked && !info.Controls(slot)) continue;
            RequestAim(source, info, state, victim, slot, order);
            if (!IsAligned(source, info, state, victim, slot)) aligned = false;
        }

        return aligned;
    }

    /// <summary>How a turret is told to aim: a one-off shot (module, script button), an accepted fire order of a unit that attacks, or a pursuit (the unit is on its way to attack).</summary>
    private enum TurretAimOrder { Shot, Attack, Pursuit }

    /// <summary>
    /// Points <paramref name="state"/> at <paramref name="victim"/>. An attack or pursuit makes the aim persistent: the turret tracks until the attack ends (<see cref="EndAttack"/>, the
    /// victim's death, lost interest). A <see cref="TurretAimOrder.Shot"/> is a one-off (a module, a script button): the aim lasts the reload plus <c>RecenterTime</c> after the latest
    /// request, and never replaces a persistent aim at the same victim.
    /// </summary>
    private void RequestAim(Entity source, TurretInfo info, TurretState state, Entity victim, int slot, TurretAimOrder order)
    {
        if (order != TurretAimOrder.Shot)
        {
            var pursuing = order == TurretAimOrder.Pursuit;
            var samePursuit = state.Persistent && state.Target == victim.Id && state.Pursuing;
            if (state.Persistent && state.Target == victim.Id && state.Slot == slot && (state.Pursuing || !pursuing)) return;
            state.Target = victim.Id;
            state.Slot = slot;
            state.Persistent = true;
            state.Pursuing = pursuing || samePursuit;
            state.LoseInterestAt = 0;
            state.HoldUntil = 0;
            state.Scanning = false;
            state.ScanAngle = Fixed.Zero;
            state.NextScan = 0;
            _activeTurrets.Add(source.Id.Value);
            MarkDirty(source);
            return;
        }

        if (state.Persistent && state.Target == victim.Id) return;
        var holdUntil = Math.Max(state.HoldUntil, _tick + Math.Max(info.RecenterTicks, _turretAimGraceTicks));
        if (!state.Persistent && state.Target == victim.Id && state.Slot == slot && state.HoldUntil == holdUntil) return;
        state.Target = victim.Id;
        state.Slot = slot;
        state.HoldUntil = holdUntil;
        state.Persistent = false;
        state.Pursuing = false;
        state.LoseInterestAt = 0;
        state.Scanning = false;
        state.ScanAngle = Fixed.Zero;
        state.NextScan = 0;
        _activeTurrets.Add(source.Id.Value);
        MarkDirty(source);
    }

    // ------------------------------------------------------------------------------------------------------ the attack intent

    /// <summary>
    /// <c>AIAttackApproachTargetState::onEnter</c> ("If we have a turret, start aiming"): the unit is on its way to attack <paramref name="victim"/>, so the turrets that carry the weapon it
    /// would use start tracking now, long before a shot is possible. The weapon is the best one that can hurt the victim, whatever its range or reload (<see cref="AttackSlotIgnoringRange"/>).
    /// </summary>
    private void BeginPursuit(Entity unit, Entity victim) => AimTurretsForAttack(unit, victim, TurretAimOrder.Pursuit);

    /// <summary>The turrets that carry the weapon the unit would use against <paramref name="victim"/> start tracking it as the victim of an attack (<paramref name="order"/>: a pursuit never gives up for distance).</summary>
    private void AimTurretsForAttack(Entity unit, Entity victim, TurretAimOrder order)
    {
        if (unit.Turrets is not { } states || unit.Template.Turrets is not { } set) return;
        UpdateWeaponSet(unit);
        var slot = AttackSlotIgnoringRange(unit, victim);
        if (slot < 0) return;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { Gates: true } info) continue;
            if (!set.Linked && !info.Controls(slot)) continue;
            RequestAim(unit, info, state, victim, slot, order);
        }
    }

    /// <summary>
    /// <c>AIUpdateInterface::setTurretTargetObject(…, NULL)</c> for every turret, as <c>AIAttackState::onExit</c> does when the unit gets another order or the attack is over: the turrets stop
    /// tracking, hold their angle for <c>RecenterTime</c> (<c>TurretAIHoldTurretState</c>) and then recenter.
    /// </summary>
    public void EndAttack(Entity entity)
    {
        ClearAcquisition(entity);
        EndPreAttack(entity);
        StopTurrets(entity);
    }

    /// <summary>Every turret of the object drops its victim and starts its recentre hold.</summary>
    private void StopTurrets(Entity entity)
    {
        if (entity.Turrets is not { } states || entity.Template.Turrets is not { } set) return;
        var changed = false;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { } info || !state.Target.IsValid) continue;
            StopAiming(info, state, _tick);
            changed = true;
        }

        if (!changed) return;
        _activeTurrets.Add(entity.Id.Value);
        MarkDirty(entity);
    }

    /// <summary>
    /// The smallest value of <see cref="TurretState.HoldUntil"/> and <see cref="TurretState.NextScan"/> that means "set": 0 stands for "none" in both, so a deadline computed in
    /// tick 0 is lifted to 1. A sentinel of the state encoding, not a duration — never scaled with the tick rate.
    /// </summary>
    private const long TurretDeadlineSet = 1;

    /// <summary>The target is dropped; the angle is held for the recentre time, then the turret turns back.</summary>
    private static void StopAiming(TurretInfo info, TurretState state, long tick)
    {
        state.Target = EntityId.Invalid;
        state.Persistent = false;
        state.Pursuing = false;
        state.LoseInterestAt = 0;
        state.HoldUntil = Math.Max(TurretDeadlineSet, tick + info.RecenterTicks);
    }

    /// <summary>The object has a turret that takes part in aiming (<see cref="TurretInfo.Gates"/>): an attack order is visible in its turrets.</summary>
    public bool HasAimingTurret(Entity entity)
    {
        if (entity.Template.Turrets is not { } set) return false;
        for (var index = 0; index < TurretSet.MaxTurrets; index++)
            if (set[index] is { Gates: true }) return true;
        return false;
    }

    /// <summary>The victim of a unit's current attack (the target of its first turret that holds an attack), or invalid when none of its turrets does.</summary>
    public EntityId AttackTargetOf(Entity entity)
    {
        if (entity.Turrets is { } states)
            foreach (var state in states)
                if (state is { Persistent: true }) return state.Target;
        return EntityId.Invalid;
    }

    /// <summary>
    /// The slot the unit would fire at <paramref name="victim"/> if range and reload did not matter: a pinned slot, else the qualifying slot with the most damage, a preferred one first —
    /// <see cref="ChooseWeapon"/> without its range and readiness filters. -1 when no weapon can hurt the victim.
    /// </summary>
    private int AttackSlotIgnoringRange(Entity source, Entity victim)
    {
        var set = ActiveWeaponSet(source);
        if (set is null || set.WeaponCount == 0) return -1;
        var state = source.WeaponSlots;
        if (state is not null && state.Lock != WeaponLockKind.None && state.SetIndex == set.Index) return set.HasWeapon(state.Current) ? state.Current : -1;
        var victimMask = VictimAntiMask(victim);
        var found = -1;
        var foundDamage = Fixed.Zero;
        for (var candidate = WeaponSlot.Count - 1; candidate >= 0; candidate--)
        {
            if (!set.HasWeapon(candidate)) continue;
            var sources = set.AutoChoose[candidate];
            if ((sources & (1u << (int)FireOrigin.Player)) == 0 && (sources & CommandSourceBits.FromAi) == 0) continue;
            var weapon = _templates.Weapons[set.Weapons[candidate]];
            if (!CanTarget(source, weapon, victim, victimMask)) continue;
            var damage = IsPreferredAgainst(set, candidate, victim) ? Fixed.MaxValue : EstimateDamage(source, weapon, victim);
            if (damage < foundDamage) continue;
            foundDamage = damage;
            found = candidate;
        }

        return found;
    }

    /// <summary>
    /// Whether a persistent aim goes on this tick. The victim has to stay attackable for its attacker's player (<c>TurretAIAimTurretState::update</c> fails the aim when
    /// <c>getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET, …)</c> does: a victim in the fog, stealthed or garrisoned) and, unless the unit pursues the victim, an attack on a victim
    /// that no weapon reaches is given up after <see cref="TurretLoseInterestFrames"/> (the reference's attack state fails there: <c>AIAttackApproachTargetState::onEnter</c> returns failure
    /// for a unit that may not chase, which is every unit the AI sent after a target it acquired itself).
    /// </summary>
    private bool KeepsAttackIntent(Entity attacker, Entity victim, TurretState state, long tick)
    {
        if (!CanPlayerSee(attacker.Owner, victim)) return false;
        if (state.Pursuing) { state.LoseInterestAt = 0; return true; }
        var range = MaxWeaponRange(attacker);
        var dx = victim.X - attacker.X;
        var dy = victim.Y - attacker.Y;
        if (SquaredReach.Of(range).Contains(dx * dx + dy * dy)) { state.LoseInterestAt = 0; return true; }
        if (state.LoseInterestAt == 0) state.LoseInterestAt = tick + _turretLoseInterestTicks;
        return tick < state.LoseInterestAt;
    }

    /// <summary>
    /// A shot left <paramref name="slot"/>: the turrets that carry it keep their aim until the weapon is ready again plus the recentre time (a one-off aim continues across the reload; an
    /// attack needs no such extension), and the sweep of a sweeping weapon switches on (<c>TurretAI::m_didFire</c> → <c>m_enableSweepUntil</c>).
    /// </summary>
    private void NoteTurretShot(Entity source, int slot)
    {
        if (source.Turrets is not { } states || source.Template.Turrets is not { } set) return;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { Gates: true } info) continue;
            if (!set.Linked && !info.Controls(slot)) continue;
            if (!state.Persistent)
            {
                var ready = Math.Min(source.ReadyTickOf(slot), _tick + _turretReloadHoldCapTicks);
                state.HoldUntil = Math.Max(state.HoldUntil, Math.Max(_tick, ready) + Math.Max(info.RecenterTicks, _turretAimGraceTicks));
            }

            if (info.SweepOf(slot) > Fixed.Zero) state.SweepUntil = _tick + _turretSweepEnableTicks;
            MarkDirty(source);
        }
    }

    /// <summary>
    /// The turret points at the victim: the relative angle is within 2 degrees (<c>REL_THRESH</c>) — or, for a weapon that sweeps (<c>TurretFireAngleSweep</c>), within the sweep angle — and a
    /// pitching turret has reached the pitch it fires at. Judged against the victim's current position, not the one the turret last moved toward.
    /// </summary>
    private bool IsAligned(Entity source, TurretInfo info, TurretState state, Entity victim, int slot)
    {
        var desired = DesiredTurretAngle(source, victim);
        var difference = FixedTrig.AngleDifference(state.Angle, desired).Abs();
        var sweep = info.SweepOf(slot);
        if (sweep > Fixed.Zero ? difference >= sweep : difference > TurretAlignThreshold) return false;
        return !info.AllowsPitch || info.PitchRate <= Fixed.Zero || (state.Pitch - DesiredTurretPitch(source, info, victim, slot)).Abs() <= TurretAlignThreshold;
    }

    /// <summary><c>getRelativeAngle2D(obj, enemyPosition)</c>: the bearing to the victim minus the body's heading, wrapped.</summary>
    private static Fixed DesiredTurretAngle(Entity source, Entity victim)
        => FixedTrig.Normalize(FixedTrig.Atan2(victim.Y - source.Y, victim.X - source.X) - source.Angle);

    /// <summary>
    /// The pitch <c>TurretAIAimTurretState::update</c> turns to: the turret's natural pitch when it cannot pitch; <c>FirePitch</c> when set; else the elevation to the victim from the top of the
    /// firing object (the reference lifts "our side" by the object's height), never below <c>MinPhysicalPitch</c>; with <c>GroundUnitPitch</c> an arc that grows with the distance to a
    /// ground target is added (immobile victims and moving ground units only: the reference also adjusts for ground positions, which have no victim here).
    /// </summary>
    private Fixed DesiredTurretPitch(Entity source, TurretInfo info, Entity victim, int slot)
    {
        if (!info.AllowsPitch) return info.NaturalPitch;
        if (info.FirePitch > Fixed.Zero) return info.FirePitch;
        var dx = victim.X - source.X;
        var dy = victim.Y - source.Y;
        var dz = victim.Z - source.Z - source.Template.GeometryHeight;
        var horizontal = Fixed.Sqrt(dx * dx + dy * dy);
        var actual = FixedTrig.Atan2(dz, horizontal);
        var desired = Fixed.Max(actual, info.MinPitch);
        if (info.GroundUnitPitch > Fixed.Zero && (victim.Template.IsKindOf("IMMOBILE") || (victim.HasMoveTarget && !IsSignificantlyAboveTerrain(victim))))
        {
            var weaponIndex = WeaponIndexIn(source, slot);
            var range = weaponIndex >= 0 ? _templates.Weapons[weaponIndex].RangeWith(_templates.GlobalWeaponBonus, source.WeaponBonusConditions) : Fixed.One;
            if (range < Fixed.One) range = Fixed.One;
            var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
            desired = Fixed.Max(actual + info.GroundUnitPitch * (distance / range), info.MinPitch);
        }

        return desired;
    }

    // ------------------------------------------------------------------------------------------------------ the per-tick step

    /// <summary>
    /// <c>TurretAI::updateTurretAI</c> for every turret away from rest or with an idle scan, once per tick after the objects have moved (so the angle is relative to this tick's heading). In id order.
    /// </summary>
    private void StepTurrets(long tick)
    {
        if (_activeTurrets.Count == 0) return;
        List<uint>? finished = null;
        foreach (var id in _activeTurrets)
        {
            if (!_entities.TryGetValue(id, out var entity) || entity.Turrets is not { } states || entity.Template.Turrets is not { } set || entity.DestroyRequested || entity.EffectivelyDead)
            {
                (finished ??= []).Add(id);
                continue;
            }

            var changed = false;
            var resting = true;
            for (var index = 0; index < states.Length; index++)
            {
                if (states[index] is not { } state || set[index] is not { } info) continue;
                if (NeedsStep(info, state)) changed |= StepTurret(entity, info, state, tick);
                if (NeedsStep(info, state)) resting = false;
            }

            if (changed) MarkDirty(entity);
            if (resting) (finished ??= []).Add(id);
        }

        if (finished is null) return;
        foreach (var id in finished) _activeTurrets.Remove(id);
    }

    /// <summary>
    /// One tick of one turret. A persistent attack aims at its victim until it ends (the victim is gone, no longer attackable or out of reach for good); a one-off aim tracks until
    /// <see cref="TurretState.HoldUntil"/>; with no target the angle is held for <c>RecenterTime</c>, then the turret recentres and returns to rest, where a scanning turret waits for its next
    /// glance. True when the state changed.
    /// </summary>
    private bool StepTurret(Entity entity, TurretInfo info, TurretState state, long tick)
    {
        var before = TurretStamp.Of(state);

        Entity? victim = null;
        if (state.Target.IsValid)
        {
            // TurretAI::friend_getTurretTarget: a dead or vanished victim clears the target. For an attack that is the end of AIAttackState, which starts the hold.
            victim = FindEntity(state.Target);
            if (victim is null || victim.DestroyRequested || victim.EffectivelyDead || victim.Body is null)
            {
                victim = null;
                if (state.Persistent) StopAiming(info, state, tick);
                else state.Target = EntityId.Invalid;
            }
            else if (state.Persistent && !KeepsAttackIntent(entity, victim, state, tick))
            {
                victim = null;
                StopAiming(info, state, tick);
            }
        }

        var holding = state.HoldUntil > 0 && tick <= state.HoldUntil;
        if (victim is not null && (state.Persistent || holding)) AimStep(entity, info, state, victim, tick);
        else if (holding) state.Rotating = false;
        else if (state.Scanning) ScanStep(entity, info, state, tick);
        else
        {
            state.Target = EntityId.Invalid;
            // TurretAIIdleState / TurretAIRecenterTurretState::update: a turret under construction neither recentres nor scans.
            var frozen = entity.HasStatus(ObjectStatusBits.UnderConstruction);
            if (state.HoldUntil == 0 && state.Angle == info.NaturalAngle && state.Pitch == info.NaturalPitch)
            {
                state.Rotating = false;
                if (info.CanScan && !frozen) IdleScanStep(entity, info, state, tick);
            }
            else if (!frozen && !RecenterStep(info, state))
            {
                state.ResetToRest(info);
                if (info.CanScan) ScheduleScan(entity, info, state, tick);
            }
        }

        return TurretStamp.Of(state) != before;
    }

    /// <summary><c>TurretAIAimTurretState::update</c>: turn toward the victim (offset by the sweep while it is on), then pitch.</summary>
    private void AimStep(Entity entity, TurretInfo info, TurretState state, Entity victim, long tick)
    {
        var relative = DesiredTurretAngle(entity, victim);
        var aim = relative;
        var rate = info.TurnRate;
        var sweep = info.SweepOf(state.Slot);
        var sweeping = sweep > Fixed.Zero;
        if (sweeping && tick < state.SweepUntil)
        {
            aim = FixedTrig.Normalize(aim + (state.PositiveSweep ? sweep : -sweep));
            rate *= info.SweepSpeedOf(state.Slot);
        }

        TurnAngle(state, aim, rate);
        // friend_turnTowardsAngle's result: within REL_THRESH of the aim; a sweeping turret then swings to the other side.
        if (sweeping && FixedTrig.AngleDifference(state.Angle, aim).Abs() <= TurretAlignThreshold) state.PositiveSweep = !state.PositiveSweep;
        if (info.AllowsPitch) TurnPitch(state, DesiredTurretPitch(entity, info, victim, state.Slot), info.PitchRate);
    }

    /// <summary><c>TurretAIRecenterTurretState::update</c>: back to the natural angle and pitch at half speed. False once both are there.</summary>
    private static bool RecenterStep(TurretInfo info, TurretState state)
    {
        TurnAngle(state, info.NaturalAngle, info.TurnRate * TurretRecenterRateFactor);
        if (info.AllowsPitch) TurnPitch(state, info.NaturalPitch, info.PitchRate * TurretRecenterRateFactor);
        return state.Angle != info.NaturalAngle || state.Pitch != info.NaturalPitch;
    }

    // ------------------------------------------------------------------------------------------------------ the idle scan

    /// <summary>
    /// <c>TurretAIIdleState::update</c>: the turret rests at its natural angle until the scan timer runs out, then <c>TurretAIIdleScanState</c> picks the angle. The timer starts when the
    /// turret comes to rest (<c>onEnter</c> → <c>resetIdleScan</c>); here that is the first step of a turret that has none.
    /// </summary>
    private void IdleScanStep(Entity entity, TurretInfo info, TurretState state, long tick)
    {
        if (state.NextScan == 0)
        {
            ScheduleScan(entity, info, state, tick);
            return;
        }

        if (tick < state.NextScan) return;
        // TurretAIIdleScanState::onEnter: an angle off the natural angle between the minimum and the maximum, on a random side.
        var spread = Fixed.Max(info.MaxScanAngle - info.MinScanAngle, Fixed.Zero);
        var angle = info.MinScanAngle + spread * DrawScanFraction(entity, info, state);
        if (DrawScan(entity, info, state, 0, 1) == 0) angle = -angle;
        state.Scanning = true;
        state.ScanAngle = angle;
        state.NextScan = 0;
        ScanStep(entity, info, state, tick);
    }

    /// <summary>
    /// <c>TurretAIIdleScanState::update</c>: turn to the natural angle plus the scan angle (and pitch to the natural pitch) at half speed; once there the machine continues in the hold state,
    /// which waits <c>RecenterTime</c> and recentres.
    /// </summary>
    private static void ScanStep(Entity entity, TurretInfo info, TurretState state, long tick)
    {
        if (entity.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        var angleReached = TurnAngle(state, info.NaturalAngle + state.ScanAngle, info.TurnRate * TurretRecenterRateFactor);
        var pitchReached = true;
        if (info.AllowsPitch)
        {
            TurnPitch(state, info.NaturalPitch, info.PitchRate * TurretRecenterRateFactor);
            pitchReached = state.Pitch == info.NaturalPitch;
        }

        if (!angleReached || !pitchReached) return;
        state.Scanning = false;
        state.ScanAngle = Fixed.Zero;
        state.HoldUntil = Math.Max(TurretDeadlineSet, tick + info.RecenterTicks);
    }

    /// <summary>
    /// <c>TurretAIIdleState::resetIdleScan</c>: the next scan starts a random interval from now. The interval is drawn in logic frames of the original
    /// (<c>GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval())</c> draws frames) and only then scaled to ticks, so it stays on the original's grid at every tick rate.
    /// </summary>
    private void ScheduleScan(Entity entity, TurretInfo info, TurretState state, long tick)
        => state.NextScan = Math.Max(TurretDeadlineSet,
            tick + Time.OriginalFrames(DrawScan(entity, info, state, Time.ToOriginalFrames(info.MinScanIntervalTicks), Time.ToOriginalFrames(info.MaxScanIntervalTicks))));

    // The reference draws the scan decisions from the shared logic stream (GameLogicRandomValue), so every idle turret in the world shifts every other user of it. This simulation instead hashes
    // (world seed, entity, turret, draw counter): the same inputs always give the same values, replays and snapshot continuations agree, and idle turrets do not disturb LogicRandom.

    private ulong NextScanBits(Entity entity, TurretInfo info, TurretState state)
    {
        var draw = state.ScanDraws++;
        unchecked
        {
            var z = (((ulong)Streams.Seed << 32) | entity.Id.Value) + 0x9E3779B97F4A7C15UL * (ulong)(((long)draw << 1) + info.Index + 1);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary><c>GameLogicRandomValue(min, max)</c>: an integer in [min, max].</summary>
    private long DrawScan(Entity entity, TurretInfo info, TurretState state, long min, long max)
    {
        var bits = NextScanBits(entity, info, state);
        return max <= min ? min : min + (long)(bits % (ulong)(max - min + 1));
    }

    /// <summary>The bits of a scan draw that make the head of the fraction: a property of how the draw is read, not of the fixed-point resolution.</summary>
    private const int ScanFractionHeadBits = 16;

    /// <summary>
    /// <c>GameLogicRandomValueReal(0, 1)</c>: a fraction in [0, 1). The lowest sixteen bits of the draw are its sixteen leading binary places; a finer
    /// resolution takes its further places from the bits above them. A draw is therefore the same number at every resolution, to the places that one has
    /// (and at sixteen fraction bits exactly the value it always was).
    /// </summary>
    private Fixed DrawScanFraction(Entity entity, TurretInfo info, TurretState state)
    {
        const int tailBits = Fixed.FractionBits - ScanFractionHeadBits;
        var bits = NextScanBits(entity, info, state);
        var head = (long)(bits & ((1UL << ScanFractionHeadBits) - 1));
        return Fixed.FromRaw((head << tailBits) | (long)((bits >> ScanFractionHeadBits) & ((1UL << tailBits) - 1)));
    }

    // ------------------------------------------------------------------------------------------------------ movement primitives

    /// <summary>
    /// <c>TurretAI::friend_turnTowardsAngle</c>: a turret closer to the goal than one step snaps to it (and stops rotating), otherwise it turns by one step the short way round and
    /// reports <c>TURRET_ROTATE</c>. True when the goal is reached.
    /// </summary>
    private static bool TurnAngle(TurretState state, Fixed desired, Fixed rate)
    {
        desired = FixedTrig.Normalize(desired);
        var difference = FixedTrig.AngleDifference(state.Angle, desired);
        if (difference.Abs() < rate)
        {
            state.Angle = desired;
            state.Rotating = false;
            return true;
        }

        state.Angle = FixedTrig.Normalize(state.Angle + (difference.IsNegative ? -rate : rate));
        state.Rotating = true;
        return false;
    }

    /// <summary><c>TurretAI::friend_turnTowardsPitch</c>.</summary>
    private static void TurnPitch(TurretState state, Fixed desired, Fixed rate)
    {
        var difference = FixedTrig.AngleDifference(state.Pitch, desired);
        state.Pitch = difference.Abs() < rate ? FixedTrig.Normalize(desired) : FixedTrig.Normalize(state.Pitch + (difference.IsNegative ? -rate : rate));
    }
}
