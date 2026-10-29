using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// An attack on a place (F-64, docs/ATTACK-POSITION.md; <c>AIUpdateInterface::privateAttackPosition</c>, <c>AIAttackState</c> on a position): the unit walks until the place is within
/// its weapon's range and in view, turns its turret to it and fires at it until its shots are used up or another order comes. It decides, as its own acquisition does, in the first
/// tick of the unit's own logic frame of the original, and reads positions where frames end.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The unit attacks a place (<c>AI_ATTACK_POSITION</c>): it is not idle.</summary>
    public bool IsAttackingPlace(Entity entity) => entity.AttackPlace is not null;

    /// <summary>The place a unit attacks, if it does.</summary>
    public (Fixed X, Fixed Y)? AttackedPlaceOf(Entity entity) => entity.AttackPlace is { } place ? (place.X, place.Y) : null;

    private CommandRejection CheckAttackPosition(PlayerId issuer, AttackPositionCommand order)
    {
        if (order.Units.IsDefaultOrEmpty || order.Units.Length > GroupMoveCommand.MaxUnits) return CommandRejection.UnknownEntity;
        if (order.Slot < WeaponSlot.Best || order.Slot >= WeaponSlot.Count || order.MaxShots < 0) return CommandRejection.InvalidArguments;
        foreach (var id in order.Units)
        {
            if (!_entities.TryGetValue(id.Value, out var member) || member.DestroyRequested) return CommandRejection.UnknownEntity;
            if (member.Owner != issuer) return CommandRejection.NotPermitted;
        }
        return 0;
    }

    /// <summary>
    /// <c>AIGroup::groupAttackPosition</c> after <c>GameLogicDispatch</c>: a <c>FIRE_WEAPON</c> button locks its slot temporarily on every member that has it
    /// (<c>setWeaponLockForGroup</c>; when none has, nothing happens), the force-fire on the ground releases a temporary lock; then every member attacks the place, or its own position,
    /// and so do the riders of a member whose container lets them fire (F-60's attack on a position through the container) and the slaves of a spawner without free will
    /// (<c>orderSlavesToAttackPosition</c>, F-65).
    /// </summary>
    private void ExecuteAttackPosition(AttackPositionCommand order)
    {
        var members = new List<Entity>(order.Units.Length);
        foreach (var id in order.Units)
            if (FindEntity(id) is { DestroyRequested: false } unit && unit.Owner == order.Player) members.Add(unit);
        if (order.Slot >= 0)
        {
            var locked = false;
            foreach (var unit in members) locked |= SetWeaponLock(unit, order.Slot, WeaponLockKind.Temporary);
            if (!locked)
            {
                RecordRejection(order, CommandRejection.NoWeapon);
                return;
            }
        }
        else
            foreach (var unit in members) ReleaseWeaponLock(unit, WeaponLockKind.Temporary);
        var any = false;
        var shots = order.MaxShots > 0 ? order.MaxShots : -1;
        foreach (var unit in members)
        {
            var (x, y) = order.OwnPosition ? FramePosition(unit) : (order.X, order.Y);
            // "This code allows garrisoned buildings to force attack a ground position": the riders of a container that lets them fire attack the same place.
            if (PassesAttackToRiders(unit))
                foreach (var id in unit.Container!.Contained.ToArray())
                    if (FindEntity(id) is { EffectivelyDead: false, DestroyRequested: false } rider && ContainerLetsFire(rider) && PlaceSlot(rider) is not null)
                        any |= AttackPlace(rider, x, y, shots, fromPlayer: true);
            // "Also handle slaves": a spawner whose slaves have no free will sends them to the place (F-65).
            if (PassesAttackToSlaves(unit))
            {
                PassPlaceToSlaves(unit, x, y, shots);
                any = true;
            }
            any |= AttackPlace(unit, x, y, shots, fromPlayer: true);
        }
        if (!any) RecordRejection(order, CommandRejection.UnknownEntity);
    }

    /// <inheritdoc cref="IWorldServices.AttackPlaceFor"/>
    public bool AttackPlaceFor(Entity unit, Fixed x, Fixed y, int shots) => AttackPlace(unit, x, y, shots > 0 ? shots : -1, fromPlayer: false);

    private (Fixed X, Fixed Y) FramePosition(Entity unit)
    {
        var (x, y, _) = unit.FrameEnd(_tick);
        return (x, y);
    }

    /// <summary>
    /// <c>aiAttackPosition</c> / <c>privateAttackPosition</c> and <c>AIAttackState::onEnter</c>: the unit's state is cleared and it attacks (<paramref name="x"/>, <paramref name="y"/>) for
    /// <paramref name="shots"/> shots (-1: no limit). Nothing for a unit without an AI, under construction, out of ammunition or without the weapon to fire. A unit whose active weapons
    /// have a continue range (a builder clearing mines, F-69) attacks the closest object near the place instead, if there is one, and otherwise fires one shot at the place. Not
    /// modelled: a contact weapon's reachable place.
    /// </summary>
    private bool AttackPlace(Entity unit, Fixed x, Fixed y, int shots, bool fromPlayer)
    {
        if (AiOf(unit) is not { } ai || unit.EffectivelyDead) return false;
        if (ClearingVictimNear(unit, x, y) is { } victim)
        {
            BuilderAttacks(unit, victim);
            return true;
        }
        if (ContinueRangeOf(unit) > Fixed.Zero) shots = 1;
        ai.OrderIdle(this, fromPlayer);
        if (unit.HasStatus(Modules.ObjectStatusBits.UnderConstruction) || IsOutOfAmmo(unit) || PlaceSlot(unit) is not { } slot) return false;
        var state = new AttackPlaceState { X = x, Y = y, Z = GroundHeightAt(x, y), ShotsLeft = shots };
        if (unit.WeaponSlots is { Lock: not WeaponLockKind.None }) state.LockedSlot = slot;
        unit.AttackPlace = state;
        _acquirers.Add(unit.Id.Value);
        MarkDirty(unit);
        return true;
    }

    /// <summary>
    /// <c>WeaponSet::chooseBestWeaponForTarget(NULL)</c>: a locked weapon, else the primary one ("make only Primary attack the ground"). Null when the active set has no weapon in that slot.
    /// </summary>
    private int? PlaceSlot(Entity unit)
    {
        if (ActiveWeaponSet(unit) is not { } set) return null;
        UpdateWeaponSet(unit);
        var slot = unit.WeaponSlots is { Lock: not WeaponLockKind.None } state && state.SetIndex == set.Index ? state.Current : WeaponSlot.Primary;
        return set.HasWeapon(slot) ? slot : null;
    }

    /// <summary><c>Object::isOutOfAmmo</c>: every weapon has an empty clip it does not reload by time (the airfield's weapons of an aircraft).</summary>
    private bool IsOutOfAmmo(Entity unit)
    {
        if (ActiveWeaponSet(unit) is not { } set || set.WeaponCount == 0) return false;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
            if (set.HasWeapon(slot) && (unit.ClipOf(slot) != 0 || unit.ReadyTickOf(slot) < SimulationTiming.SleepForever)) return false;
        return true;
    }

    /// <summary>The attack ends (<c>AIAttackState::onExit</c>): the turrets hold, a walk toward the place stops at the end of the frame.</summary>
    private void EndAttackPlace(Entity entity, AIUpdateInterface? ai)
    {
        if (entity.AttackPlace is not { } state) return;
        entity.AttackPlace = null;
        EndPreAttack(entity);
        StopTurrets(entity);
        if (state.Chasing) ai?.StopInternalMoveAtFrameEnd(this);
        MarkDirty(entity);
    }

    /// <summary>
    /// One decision of <c>AIAttackState::update</c> on a place: out of ammunition, the shots used up, no weapon, or another weapon than the one locked at the start - the attack ends;
    /// out of range or out of view (<c>outOfWeaponRangePosition</c>) a mobile unit walks toward the place and one that may not walk gives up; in range it stops, turns its turret,
    /// waits for its weapon and its wind-up and fires at the place.
    /// </summary>
    private void StepAttackPlace(Entity entity, AIUpdateInterface ai, AttackPlaceState state, long tick)
    {
        if (IsOutOfAmmo(entity) || state.ShotsLeft == 0 || PlaceSlot(entity) is not { } slot || (state.LockedSlot >= 0 && slot != state.LockedSlot))
        {
            EndAttackPlace(entity, ai);
            return;
        }
        var weapon = _templates.Weapons[ActiveWeaponSet(entity)!.Weapons[slot]];
        if (!WithinPlaceRange(entity, weapon, state) || IsPlaceViewBlocked(entity, state))
        {
            if (entity.ContainedBy.IsValid || entity.Template.Locomotor is null || entity.Template.IsKindOf("IMMOBILE"))
            {
                EndAttackPlace(entity, ai);
                return;
            }
            // AIAttackApproachTargetState on a position: the unit walks at the place and stops once it is in range; a walk that ended short of it is a failure.
            if (!state.Chasing || !entity.HasMoveTarget)
            {
                if (state.Chasing || !ai.StartInternalMove(state.X, state.Y))
                {
                    EndAttackPlace(entity, ai);
                    return;
                }
                ai.BeginMoveWithNextFrame(this);
                WakeAt(entity, ai, Math.Min(ai.NextWake, tick + _turretFrameTicks));
                state.Chasing = true;
            }
            AimTurretsAtPlace(entity, slot, state.X, state.Y, state.Z);
            return;
        }
        if (state.Chasing)
        {
            ai.StopInternalMoveAtFrameEnd(this);
            state.Chasing = false;
        }
        if (!TurretsLetAttack(entity) || tick < EffectiveReadyTick(entity, slot, ActiveWeaponSet(entity)!)) return;
        if (!AimTurretsAtPlace(entity, slot, state.X, state.Y, state.Z))
        {
            NoteTurretWait();
            return;
        }
        if (!PreAttackDone(entity, weapon, slot, EntityId.Invalid)) return;
        var fired = ConsumeShot(entity, slot, EntityId.Invalid);
        var damage = FireWeaponAt(entity, fired, null, state.X, state.Y, state.Z);
        NoteTurretShot(entity, slot);
        Emit(SimEventKind.WeaponFired, entity.Id, EntityId.Invalid, fired.Name, damage.Raw, entity.Owner);
        if (state.ShotsLeft > 0) state.ShotsLeft--;
    }

    /// <summary><c>Weapon::isWithinAttackRange(source, pos)</c>: the place within the weapon's range of the shooter (its fire point in a garrison), not nearer than the minimum range.</summary>
    private bool WithinPlaceRange(Entity source, WeaponInfo weapon, AttackPlaceState state)
    {
        if (!TryRangeOrigin(source, state.X, state.Y, state.Z, out var sourceX, out var sourceY)) return false;
        var dx = state.X - sourceX;
        var dy = state.Y - sourceY;
        var distanceSquared = dx * dx + dy * dy;
        if (!SquaredReach.Of(weapon.RangeWith(_templates.GlobalWeaponBonus, WeaponBonusOf(source))).Contains(distanceSquared)) return false;
        var minimum = weapon.MinimumAttackRange;
        return minimum <= Fixed.Zero || !SquaredReach.ContainsStrictly(minimum, distanceSquared, Fixed.FromDecimal(0.5m));
    }

    /// <summary>
    /// <c>outOfWeaponRangePosition</c>'s view test: a unit on the ground (not flying, or a rider of a building or of a container on the ground) whose view to the place is blocked
    /// (<c>isAttackViewBlockedByObstacle</c>, the terrain line and the structures between).
    /// </summary>
    private bool IsPlaceViewBlocked(Entity attacker, AttackPlaceState state)
    {
        if (!AttackUsesLineOfSight || Navigation is null || Terrain is null || !attacker.Template.IsKindOf(NeedsLineOfSightKind)) return false;
        if (!attacker.ContainedBy.IsValid && IsSignificantlyAboveTerrainAtFrameEnd(attacker)) return false;
        if (_templates.Rules.Has(Rules.GameFix.ContactWeaponNotViewBlocked) && !attacker.Template.IsKindOf("IMMOBILE") && CurrentWeapon(attacker) is { } current && IsContactWeapon(current))
            return false;
        var (attackerX, attackerY, attackerZ) = ShooterPosition(attacker, frameEnd: true);
        if (!attacker.Template.IsKindOf("IMMOBILE") && !Terrain.IsClearLineOfSight(attackerX, attackerY, attackerZ + MaxHeightAbovePosition(attacker.Template), state.X, state.Y, state.Z))
            return true;
        Span<ulong> excepted = [attacker.Id.Value, attacker.ContainedBy.Value, SlaverOf(attacker).Value];
        return Navigation.IsAttackLineBlocked(attackerX, attackerY, state.X, state.Y, excepted);
    }
}
