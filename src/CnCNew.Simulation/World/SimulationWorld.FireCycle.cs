using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The firing half of the original's attack machine (F-140, docs/SIMULATION-CORE.md). <c>AttackStateMachine</c> (<c>AIStates.cpp</c>) has <c>AIM_AT_TARGET</c> succeed into
/// <c>FIRE_WEAPON</c>, and <c>FIRE_WEAPON</c> go back to <c>AIM_AT_TARGET</c> on success and failure alike; <c>TurretStateMachine</c> (<c>TurretAI.cpp</c>) does the same with
/// <c>TURRETAI_AIM</c> and <c>TURRETAI_FIRE</c>. <c>StateMachine::updateStateMachine</c> runs one state's update per logic frame and enters the next state at its end
/// (<c>internalSetState</c>), so the update of the entered state comes in the next frame. <c>AIAttackFireWeaponState::update</c> waits while the weapon winds up, fires when it is
/// <c>READY_TO_FIRE</c> and fails otherwise; its <c>onEnter</c> starts the wind-up (<c>Object::preFireCurrentWeapon</c>) only when the weapon will be ready by the next frame. A weapon the
/// attack machine fires therefore fires on every second frame at the most, and after a wait of D frames on the first even frame from D on (retail's fire cycle; Zero Hour Reforged's
/// <c>holdToRetailFireCycle</c> describes the same and keeps it at its higher rate).
/// <para>
/// Here a machine steps in the frames of its unit, at the first tick that reaches it in a frame: the unit's decision in <see cref="StepAcquisition"/>, or an order's tick. The ready ticks of
/// the weapons are unchanged. A weapon on a turret that turns has the turret's machine (<see cref="TurretState.Cycle"/>), every other one the body's (the attack on an object's
/// <see cref="AcquisitionState.Cycle"/>, the attack on a place's <see cref="AttackPlaceState.Cycle"/>). Paths the original fires outside the attack machine keep their own timing.
/// </para>
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// A fire order of <paramref name="unit"/> at <paramref name="victim"/>, given within the last two logic frames of the original, waits for its attack machine's shot (F-140):
    /// another order adds nothing to it, so an order giver that orders while the weapon is ready waits instead of sending an order in every tick until the machine fires. After two
    /// frames - the longest an aligned machine takes from an order to its shot - the giver orders again, which keeps an attack whose turret still turns from its grace's end.
    /// </summary>
    public bool IsShotPending(Entity unit, EntityId victim)
        => unit.Acquisition is { FireOrdered: true, ShotPending: true } state && state.Victim == victim && _tick - state.OrderedTick < Time.OriginalFrames(FireOrderRepeatFrames);

    private const long FireOrderRepeatFrames = 2;

    /// <summary>The first tick of <paramref name="entity"/>'s own logic frame of the original that holds <paramref name="tick"/> (the tick itself at 30 Hz).</summary>
    private long OwnFrameStart(Entity entity, long tick)
    {
        if (_firstFrameTicks == TickOrder.Next) return tick;
        var into = (tick - entity.SpawnTick) % _firstFrameTicks;
        return into < 0 ? tick - into - _firstFrameTicks : tick - into;
    }

    /// <summary>
    /// The turret whose machine fires the weapon of <paramref name="slot"/>: the first turret that turns and carries it (every turret of a <c>TurretsLinked</c> object), -1 when the body
    /// aims it (no turret, a "fake" turret that cannot turn, a slot no turret controls).
    /// </summary>
    private static int CycleTurretOf(Entity entity, int slot)
    {
        if (entity.Turrets is not { } states || entity.Template.Turrets is not { } set) return -1;
        for (var index = 0; index < TurretSet.MaxTurrets; index++)
            if (set[index] is { Gates: true } info && (set.Linked || info.Controls(slot)) && states[index] is not null) return index;
        return -1;
    }

    /// <summary>
    /// A new attack machine for the body, in its default state <c>AIM_AT_TARGET</c>. Entered by an order (<paramref name="ordered"/>: <c>processCommandList</c> runs before the
    /// objects' updates) its update runs in this frame; entered from the unit's own decision (<c>AIIdleState::update</c> calls <c>aiAttackObject</c>, an approach that reached its range
    /// succeeds into it), in the next.
    /// </summary>
    private FireCycle NewBodyCycle(Entity source, EntityId victim, bool ordered)
    {
        var frame = OwnFrameStart(source, _tick);
        return new FireCycle { Phase = FireCyclePhase.Aim, Due = ordered ? frame : frame + _firstFrameTicks, Victim = victim };
    }

    /// <summary>
    /// One frame of the attack machine that fires <paramref name="slot"/> at <paramref name="victim"/> (or at the place <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>
    /// when it is null), which is in range: true when the shot goes out now - the caller fires and calls <see cref="AfterCycleShot"/>. <paramref name="ordered"/>: an order begins
    /// this attack in this tick. The weapon is <c>READY_TO_FIRE</c> when its ready tick falls in this frame of the unit: the machine decides once per frame, and a shot an order gave
    /// in a later tick of a frame is the frame's shot all the same. <paramref name="byOrder"/>: an order steps the machine; a turret that is not aligned yet is counted as a wait
    /// (<see cref="TurretWaits"/>) by the unit's own step of the frame only, once per frame.
    /// <list type="bullet">
    /// <item><description>The body: <c>AIM_AT_TARGET</c>'s update turns the unit until it faces its target (<see cref="AimBodyAt"/>) and then succeeds; a contact weapon does not aim
    /// (<c>AIAttackAimAtTargetState::onEnter</c>: "Contact weapons don't aim. They just go boom.").</description></item>
    /// <item><description>A turret: its machine runs in <c>TurretAI::updateTurretAI</c>, after the unit's AI and with the turn of its frame (<c>TurretAIAimTurretState::update</c> turns,
    /// then succeeds when aligned and in range). The turret here turns after this decision (<see cref="StepTurrets"/>), so the angle it points with now is the one the frame before
    /// ended on: aligned now, its aim state succeeded in that frame, and the fire state's update is this frame's.</description></item>
    /// </list>
    /// </summary>
    private bool AttackCycleFires(Entity source, ref FireCycle? body, int slot, Entity? victim, Fixed x, Fixed y, Fixed z, bool ordered, bool byOrder = false)
    {
        var set = ActiveWeaponSet(source)!;
        var weapon = _templates.Weapons[set.Weapons[slot]];
        var frame = OwnFrameStart(source, _tick);
        var ready = EffectiveReadyTick(source, slot, set) < frame + _firstFrameTicks;
        var victimId = victim?.Id ?? EntityId.Invalid;
        var turretIndex = CycleTurretOf(source, slot);
        if (turretIndex >= 0)
        {
            // setTurretTargetObject / setTurretTargetPosition, given by the body's aim state in every frame of the attack.
            var aligned = victim is not null ? AimTurretsAt(source, slot, victim, TurretAimOrder.Attack) : AimTurretsAtPlace(source, slot, x, y, z);
            var turret = source.Turrets![turretIndex]!;
            if (turret.Cycle is null)
            {
                // The turret enters TURRETAI_AIM when the attack gives it its target, and its update of that frame comes after the AI's.
                turret.Cycle = new FireCycle { Phase = FireCyclePhase.Aim, Due = frame };
                MarkDirty(source);
            }
            var cycle = turret.Cycle;
            if (cycle.Phase == FireCyclePhase.Aim)
            {
                if (!aligned)
                {
                    if (!byOrder) NoteTurretWait();
                    return false;
                }
                if (frame - _firstFrameTicks < cycle.Due) return false;
                EnterFire(source, cycle, frame - _firstFrameTicks, frame, slot, weapon, victimId);
            }
            return FireUpdate(source, cycle, frame, slot, ready, contact: false, weapon, victimId);
        }

        var contact = IsContactWeapon(weapon);
        if (body is { } running && victim is not null && running.Victim != victimId)
        {
            // Another victim is another attack: AIAttackState::onExit and onEnter build a new machine (and FIRE's onExit drops a wind-up).
            if (running.Phase == FireCyclePhase.Fire) EndPreAttack(source);
            body = null;
        }
        if (body is null)
        {
            body = NewBodyCycle(source, victimId, ordered);
            if (contact) EnterFire(source, body, frame, body.Due, slot, weapon, victimId);
            MarkDirty(source);
        }
        var own = body;
        if (own.Phase == FireCyclePhase.Aim)
        {
            if (frame < own.Due) return false;
            var (aimX, aimY) = (x, y);
            if (victim is not null) (aimX, aimY, _) = victim.FrameEnd(_tick);
            if (!AimBodyAt(source, slot, aimX, aimY)) return false;
            EnterFire(source, own, frame, frame + _firstFrameTicks, slot, weapon, victimId);
            return false;
        }
        return FireUpdate(source, own, frame, slot, ready, contact, weapon, victimId);
    }

    /// <summary>
    /// <c>AIAttackFireWeaponState::update</c>: a wind-up still running waits (<c>PRE_ATTACK</c>: continue), a ready weapon fires (success), any other goes back to aiming (failure).
    /// </summary>
    private bool FireUpdate(Entity source, FireCycle cycle, long frame, int slot, bool ready, bool contact, WeaponInfo weapon, EntityId victimId)
    {
        if (frame < cycle.Due) return false;
        if (source.Firing is { PreSlot: >= 0 } firing && firing.PreSlot == slot && frame < firing.PreFinish) return false;
        if (ready) return true;
        EnterAim(source, cycle, frame, slot, contact, weapon, victimId);
        return false;
    }

    /// <summary>
    /// The machine enters its aim state at the end of <paramref name="frame"/>: its update comes in the next frame. A contact weapon on the body succeeds out of it at once
    /// (<c>AIAttackAimAtTargetState::onEnter</c> returns success, and <c>internalSetState</c> enters the fire state in the same frame).
    /// </summary>
    private void EnterAim(Entity source, FireCycle cycle, long frame, int slot, bool contact, WeaponInfo weapon, EntityId victimId)
    {
        cycle.Phase = FireCyclePhase.Aim;
        cycle.Due = frame + _firstFrameTicks;
        if (contact) EnterFire(source, cycle, frame, frame + _firstFrameTicks, slot, weapon, victimId);
        MarkDirty(source);
    }

    /// <summary>
    /// <c>AIAttackFireWeaponState::onEnter</c> in the frame <paramref name="at"/>, with the update from <paramref name="due"/>: <c>Object::preFireCurrentWeapon</c> starts the wind-up -
    /// "If we are going to be capable of firing our weapon NEXT frame, set the pre-attack up now" - when the weapon is ready by the next frame and <c>Weapon::getPreAttackDelay</c> finds no
    /// reason to return zero.
    /// </summary>
    private void EnterFire(Entity source, FireCycle cycle, long at, long due, int slot, WeaponInfo weapon, EntityId victimId)
    {
        cycle.Phase = FireCyclePhase.Fire;
        cycle.Due = due;
        MarkDirty(source);
        if (weapon.PreAttackDelayTicks == 0 || EffectiveReadyTick(source, slot, ActiveWeaponSet(source)!) >= at + _firstFrameTicks + _firstFrameTicks) return;
        if (weapon.PreAttackType == WeaponPrefire.PerClip && weapon.ClipSize > 0 && source.ClipOf(slot) is var clip && clip != 0 && clip < weapon.ClipSize) return;
        if (weapon.PreAttackType == WeaponPrefire.PerAttack && source.Firing is { } counted && counted.Victim == victimId && counted.Shots > 0) return;
        var delay = weapon.PreAttackDelayWith(Time, _templates.GlobalWeaponBonus, WeaponBonusOf(source));
        if (delay <= 0) return;
        var state = source.Firing ??= new FiringState();
        state.PreSlot = slot;
        state.PreVictim = victimId;
        state.PreFinish = at + delay;
    }

    /// <summary>The shot of <paramref name="slot"/> succeeded the fire state: the machine aims again (<c>FIRE_WEAPON</c> succeeds into <c>AIM_AT_TARGET</c>).</summary>
    private void AfterCycleShot(Entity source, ref FireCycle? body, int slot)
    {
        var frame = OwnFrameStart(source, _tick);
        var turretIndex = CycleTurretOf(source, slot);
        var cycle = turretIndex >= 0 ? source.Turrets![turretIndex]!.Cycle : body;
        if (cycle is null || ActiveWeaponSet(source) is not { } set || !set.HasWeapon(slot)) return;
        var weapon = _templates.Weapons[set.Weapons[slot]];
        EnterAim(source, cycle, frame, slot, turretIndex < 0 && IsContactWeapon(weapon), weapon, cycle.Victim);
    }

    /// <summary>
    /// The target is out of the weapon's range in this frame (<c>outOfWeaponRangeObject</c> / <c>outOfWeaponRangePosition</c>): the body's machine leaves for its chase and starts in its
    /// default state again once the target is in range; a turret's fire state goes back to its aim state (<c>TurretStateMachine</c>'s <c>fireConditions</c>), whose update cannot
    /// succeed before the next frame. FIRE's <c>onExit</c> drops a wind-up.
    /// </summary>
    private void AttackCycleOutOfRange(Entity source, ref FireCycle? body, bool turrets)
    {
        if (body is { } running)
        {
            if (running.Phase == FireCyclePhase.Fire) EndPreAttack(source);
            body = null;
            MarkDirty(source);
        }
        if (!turrets || source.Turrets is not { } states) return;
        var next = OwnFrameStart(source, _tick) + _firstFrameTicks;
        foreach (var turret in states)
        {
            if (turret?.Cycle is not { } cycle || (cycle.Phase == FireCyclePhase.Aim && cycle.Due >= next)) continue;
            if (cycle.Phase == FireCyclePhase.Fire) EndPreAttack(source);
            cycle.Phase = FireCyclePhase.Aim;
            cycle.Due = Math.Max(cycle.Due, next);
            MarkDirty(source);
        }
    }

    /// <summary>
    /// An order begins a new attack (<c>aiAttackObject</c>: the old <c>AIAttackState</c>'s <c>onExit</c> clears the turrets' targets, the new one's <c>onEnter</c> builds its machine
    /// and gives the turrets the victim again): every machine of the unit starts in its aim state.
    /// </summary>
    private void RestartAttackCycles(Entity entity)
    {
        if (entity.Acquisition is { Cycle: { } body } attack)
        {
            if (body.Phase == FireCyclePhase.Fire) EndPreAttack(entity);
            attack.Cycle = null;
            MarkDirty(entity);
        }
        if (entity.Turrets is not { } states) return;
        foreach (var turret in states)
        {
            if (turret?.Cycle is not { } cycle) continue;
            if (cycle.Phase == FireCyclePhase.Fire) EndPreAttack(entity);
            turret.Cycle = null;
            MarkDirty(entity);
        }
    }

    /// <summary>
    /// An attack order of a unit with an AI at <paramref name="victim"/> (<c>aiAttackObject</c>): the unit does not pick victims by itself while such orders keep coming, and the attack's
    /// machine fires (F-140) - in this tick when it is in its fire state, else in a later frame of the unit, from the order stream or from the unit's own step. The order comes from a
    /// player's command giver, a script's command button (<c>Object::doCommandButtonAtObject</c> ends in <c>aiAttackObject</c> as well) or a module of the object
    /// (<c>CleanupHazardUpdate</c>, <c>AssistedTargetingUpdate</c>). <paramref name="lockSlot"/> of a command button pins its weapon slot until the clip empties
    /// (<c>setWeaponLock(LOCKED_TEMPORARILY)</c> before the attack); -1 pins nothing.
    /// </summary>
    private void OrderFireAttack(Entity source, Entity victim, WeaponChoice choice, FireOrigin origin, int lockSlot, PlayerId emitter, int shots = 0)
    {
        var begins = source.Acquisition is not { Ordered: true } running || running.Victim != victim.Id;
        NoteOrderedAttack(source, victim, origin);
        if (begins) RestartAttackCycles(source);
        if (lockSlot >= 0) SetWeaponLock(source, lockSlot, WeaponLockKind.Temporary);
        var attack = source.Acquisition!;
        if (shots > 0) attack.ShotsLeft = shots;
        if (AttackCycleFires(source, ref attack.Cycle, choice.Slot, victim, Fixed.Zero, Fixed.Zero, Fixed.Zero, ordered: begins, byOrder: true))
        {
            attack.ShotPending = false;
            FireCycleShot(source, ref attack.Cycle, choice.Slot, victim, emitter);
        }
    }

    /// <summary>
    /// An attack a module of <paramref name="source"/> orders at <paramref name="victim"/> with the weapon of <paramref name="slot"/> (<c>AIUpdateInterface::aiAttackObject</c> with
    /// <c>CMD_FROM_AI</c>: <c>CleanupHazardUpdate::fireWhenReady</c>, <c>AssistedTargetingUpdate::assistAttack</c>), with <paramref name="shots"/> shots at most (0: no limit). The attack
    /// machine fires it (F-140): the module orders again when the weapon is ready and no order waits for its shot, as a player's command giver does. False when the unit has no AI or no shot
    /// can be ordered now (no weapon, not ready, out of range, a victim it cannot hit).
    /// </summary>
    public bool OrderModuleAttack(Entity source, int slot, Entity victim, int shots = 0)
    {
        if (source.DestroyRequested || source.EffectivelyDead || victim.DestroyRequested || victim.Body is null || AiOf(source) is null) return false;
        UpdateWeaponSet(source);
        if (!TurretsLetAttack(source)) return false;
        if (IsShotPending(source, victim.Id)) return true;
        var choice = ChooseWeaponInView(source, victim, FireOrigin.Ai, slot);
        if (choice.Rejection != 0 || !GateAllowsFire(source, victim)) return false;
        OrderFireAttack(source, victim, choice, FireOrigin.Ai, slot, source.Owner, shots);
        return true;
    }

    /// <summary>The shot of an attack machine at <paramref name="victim"/>: the weapon's bookkeeping, the shot, the turret's sweep, the event, and the machine aims again.</summary>
    private void FireCycleShot(Entity source, ref FireCycle? body, int slot, Entity victim, PlayerId emitter)
    {
        var weapon = ConsumeShot(source, slot, victim.Id);
        var damage = FireWeaponAt(source, weapon, victim, victim.X, victim.Y, victim.Z);
        NoteTurretShot(source, slot);
        Emit(SimEventKind.WeaponFired, source.Id, victim.Id, weapon.Name, damage.Raw, emitter);
        AfterCycleShot(source, ref body, slot);
        // Weapon::m_maxShotCount: AIAttackState::update fails once the weapon has no shots left to fire, and once the weapon the attack entered with is no longer the current one - the shot
        // that empties a clip releases the temporary lock (Object::fireCurrentWeapon), which is how a limit larger than the clip ends the attack.
        if (source.Acquisition is { ShotsLeft: > 0 } limited && (--limited.ShotsLeft == 0 || source.WeaponLock == WeaponLockKind.None))
        {
            limited.ClearVictim();
            ReleaseWeaponLock(source, WeaponLockKind.Temporary);
        }
        else if (source.Acquisition is { ShotsLeft: > 0 } going)
        {
            going.ShotPending = true;
            going.OrderedTick = _tick;
        }
        MarkDirty(source);
    }

    /// <summary>
    /// The frame of an ordered attack (F-140): the original's attack runs its machine in every frame of the unit, so the world steps a fire order's attack itself - also in a frame no
    /// order arrives in (the stream of a command giver orders a shot while the weapon is ready, which is not when the machine fires). It fires the shot an order is pending for when the
    /// machine's fire state is due; without a pending order the fire state waits for the next order. An order that arrives later in the frame finds the step done.
    /// </summary>
    private void StepOrderedFire(Entity entity, AcquisitionState state, Entity victim)
    {
        if (!TurretsLetAttack(entity)) return;
        // AIAttackState::update: "if all of our weapons are out of ammo, can't attack" (a jet whose weapons reload only at its airfield) - the attack fails.
        if (IsOutOfAmmo(entity) && !entity.Template.IsKindOf("PROJECTILE"))
        {
            AttackCycleOutOfRange(entity, ref state.Cycle, turrets: true);
            state.ClearVictim();
            return;
        }
        UpdateWeaponSet(entity);
        var choice = ChooseWeaponInView(entity, victim, state.OrderOrigin);
        if (choice.Slot < 0 || choice.Rejection == CommandRejection.OutOfRange)
        {
            if (choice.Rejection is CommandRejection.OutOfRange or CommandRejection.WeaponNotReady) AttackCycleOutOfRange(entity, ref state.Cycle, turrets: true);
            return;
        }
        if (choice.Rejection is not (0 or CommandRejection.WeaponNotReady) || !GateAllowsFire(entity, victim)) return;
        if (!AttackCycleFires(entity, ref state.Cycle, choice.Slot, victim, Fixed.Zero, Fixed.Zero, Fixed.Zero, ordered: false) || !state.ShotPending) return;
        state.ShotPending = false;
        FireCycleShot(entity, ref state.Cycle, choice.Slot, victim, entity.Owner);
    }
}
