using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// The turret model (<c>TurretAI</c> in the reference, docs/SIM-TURRET-AIM.md): every turret of an object has an angle and a pitch that move at the turret's own rates toward the victim
/// of the latest attack order; a weapon on a turret-controlled slot fires only once its turret points at the victim; an idle turret turns back to its natural angle. Objects without a
/// turret, with a turret that cannot turn or starts disabled, and weapons on slots no turret controls fire as before.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>REL_THRESH</c> of <c>TurretAIAimTurretState::update</c>: "about 2 degrees" (0.035 rad) — how closely the turret has to point at the victim before the weapon may fire.</summary>
    private static readonly Fixed TurretAlignThreshold = Fixed.FromDecimal(0.035m);

    /// <summary>The recentre hold of the reference starts when the AI ends the attack; here an attack is a stream of orders, so the turret keeps its aim at least this long after the latest order (and past the weapon's reload).</summary>
    private const long TurretAimGraceTicks = 15;

    /// <summary>The longest reload that extends the hold (a weapon that reloads at an airfield reports an endless wait).</summary>
    private const long TurretReloadHoldCapTicks = 450;

    /// <summary><c>ENABLE_SWEEP_FRAME_COUNT</c>: the sweep of <c>TurretFireAngleSweep</c> stays on this many ticks after a shot.</summary>
    private const long TurretSweepEnableTicks = 3;

    /// <summary><c>friend_turnTowardsAngle(…, 0.5f, 0.0f)</c> of the recenter state.</summary>
    private static readonly Fixed TurretRecenterRateFactor = Fixed.FromDecimal(0.5m);

    /// <summary>Ids (ascending) of the entities with a turret away from rest; <see cref="StepTurrets"/> visits exactly these.</summary>
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

    /// <summary>Entities restored from a snapshot or just created: registers the ones with a turret away from rest for stepping.</summary>
    private void RegisterTurrets(Entity entity)
    {
        if (entity.Turrets is not { } states || entity.Template.Turrets is not { } set) return;
        for (var index = 0; index < states.Length; index++)
            if (states[index] is { } state && set[index] is { } info && !state.IsPristine(info)) { _activeTurrets.Add(entity.Id.Value); return; }
    }

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
    /// </summary>
    private bool AimTurretsAt(Entity source, int slot, Entity victim)
    {
        if (source.Turrets is not { } states || source.Template.Turrets is not { } set) return true;
        var aligned = true;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { Gates: true } info) continue;
            if (!set.Linked && !info.Controls(slot)) continue;
            RequestAim(source, info, state, victim, slot);
            if (!IsAligned(source, info, state, victim, slot)) aligned = false;
        }

        return aligned;
    }

    private void RequestAim(Entity source, TurretInfo info, TurretState state, Entity victim, int slot)
    {
        var holdUntil = Math.Max(state.HoldUntil, _tick + Math.Max(info.RecenterTicks, TurretAimGraceTicks));
        if (state.Target == victim.Id && state.Slot == slot && state.HoldUntil == holdUntil) return;
        state.Target = victim.Id;
        state.Slot = slot;
        state.HoldUntil = holdUntil;
        _activeTurrets.Add(source.Id.Value);
        MarkDirty(source);
    }

    /// <summary>
    /// A shot left <paramref name="slot"/>: the turrets that carry it keep their aim until the weapon is ready again plus the recentre time (the attack continues across the reload in the
    /// reference), and the sweep of a sweeping weapon switches on (<c>TurretAI::m_didFire</c> → <c>m_enableSweepUntil</c>).
    /// </summary>
    private void NoteTurretShot(Entity source, int slot)
    {
        if (source.Turrets is not { } states || source.Template.Turrets is not { } set) return;
        for (var index = 0; index < states.Length; index++)
        {
            if (states[index] is not { } state || set[index] is not { Gates: true } info) continue;
            if (!set.Linked && !info.Controls(slot)) continue;
            var ready = Math.Min(source.ReadyTickOf(slot), _tick + TurretReloadHoldCapTicks);
            state.HoldUntil = Math.Max(state.HoldUntil, Math.Max(_tick, ready) + Math.Max(info.RecenterTicks, TurretAimGraceTicks));
            if (info.SweepOf(slot) > Fixed.Zero) state.SweepUntil = _tick + TurretSweepEnableTicks;
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
    /// <c>TurretAI::updateTurretAI</c> for every turret away from rest, once per tick after the objects have moved (so the angle is relative to this tick's heading). In id order.
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
                if (!state.IsPristine(info)) changed |= StepTurret(entity, info, state, tick);
                if (!state.IsPristine(info)) resting = false;
            }

            if (changed) MarkDirty(entity);
            if (resting) (finished ??= []).Add(id);
        }

        if (finished is null) return;
        foreach (var id in finished) _activeTurrets.Remove(id);
    }

    /// <summary>One tick of one turret: aim at the victim while the order holds, hold the angle once the victim is gone, then recentre. True when the state changed.</summary>
    private bool StepTurret(Entity entity, TurretInfo info, TurretState state, long tick)
    {
        var angle = state.Angle;
        var pitch = state.Pitch;
        var rotating = state.Rotating;
        var target = state.Target;
        var sweepUntil = state.SweepUntil;
        var positive = state.PositiveSweep;
        var holdUntil = state.HoldUntil;

        Entity? victim = null;
        if (target.IsValid)
        {
            // TurretAI::friend_getTurretTarget: a dead or vanished victim clears the target (the turret then holds its angle).
            victim = FindEntity(target);
            if (victim is null || victim.DestroyRequested || victim.EffectivelyDead || victim.Body is null)
            {
                victim = null;
                state.Target = EntityId.Invalid;
            }
        }

        if (holdUntil > 0 && tick <= holdUntil)
        {
            if (victim is not null) AimStep(entity, info, state, victim, tick);
            else state.Rotating = false;
        }
        else
        {
            state.Target = EntityId.Invalid;
            if (!RecenterStep(info, state)) state.ResetToRest(info);
        }

        return state.Angle != angle || state.Pitch != pitch || state.Rotating != rotating || state.Target != target || state.SweepUntil != sweepUntil
               || state.PositiveSweep != positive || state.HoldUntil != holdUntil;
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
