using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// Guarding as a state of the simulation (F-58, docs/GUARD-STATE.md; <c>AIGuardMachine</c>, <c>AI/AIGuard.cpp</c>): a unit guards a position or an object. In IDLE it looks for the
/// closest enemy within its inner range of the guarded centre every <c>GuardEnemyScanRate</c>; INNER attacks it while it stays within that range, OUTER follows it within the outer
/// range and gives up when it has not come back into the inner range for <c>GuardChaseUnitsDuration</c>; RETURN walks back to the centre, looking every
/// <c>GuardEnemyReturnScanRate</c>; an attacker that hurts the unit in INNER, RETURN or IDLE is answered (ATTACK_AGGRESSOR). The guard lasts until another order. It decides, as the
/// unit's own acquisition does, in the first tick of the unit's own logic frame of the original, and reads positions where frames end.
/// </summary>
public sealed partial class SimulationWorld
{
    // AIData.ini is not a typed definition yet (docs/SIMULATION-CORE.md §33.5): the values of the shipped file.

    /// <summary><c>GuardInnerModifierHuman</c> / <c>GuardOuterModifierHuman</c> (AIData.ini 1.8 / 2.2).</summary>
    private static readonly Fixed GuardInnerFactorHuman = Fixed.FromDecimal(1.8m);
    private static readonly Fixed GuardOuterFactorHuman = Fixed.FromDecimal(2.2m);

    /// <summary><c>GuardInnerModifierAI</c> (AIData.ini 1.1); the outer factor is <see cref="AcquireVisionFactorComputer"/>.</summary>
    private static readonly Fixed GuardInnerFactorComputer = Fixed.FromDecimal(1.1m);

    /// <summary><c>GuardChaseUnitsDuration</c> (10,000 ms), <c>GuardEnemyScanRate</c> (500 ms), <c>GuardEnemyReturnScanRate</c> (1,000 ms) in logic frames of the original.</summary>
    private const long GuardChaseFrames = 300;
    private const long GuardScanFrames = 15;
    private const long GuardReturnScanFrames = 30;

    /// <summary><c>MaxRetaliationDistance</c> (AIData.ini 200) and <c>RetaliationFriendsRadius</c> (120).</summary>
    private static readonly Fixed MaxRetaliationDistance = Fixed.FromInt(200);
    private static readonly Fixed RetaliationFriendsRadius = Fixed.FromInt(120);

    /// <summary>The retaliation's radii: ATTACK_AGGRESSOR outer + inner range, INNER 1.5 x the inner range, OUTER 0.67 x (outer + inner) (<c>AIGuardRetaliate.cpp</c>).</summary>
    private static readonly Fixed RetaliateInnerFactor = Fixed.FromDecimal(1.5m);
    private static readonly Fixed RetaliateOuterFactor = Fixed.FromDecimal(0.67m);

    private static readonly int UsingAbilityStatus = Modules.ObjectStatusBits.Index("USING_ABILITY");

    /// <summary>IDLE's "the guarded object moved": more than two pathfinding cells along x or along y (<c>4 * PATHFIND_CELL_SIZE_F^2</c> per axis).</summary>
    private static readonly Fixed GuardeeMoveLimitSquared = Fixed.FromInt(4 * 10 * 10);

    /// <summary>The trigger areas a guard of an area finds by name (the mission's script program); null in a world without them. Not part of the snapshot: the host sets it again.</summary>
    public Func<string, ScriptArea?>? GuardAreas { get; set; }

    /// <summary>The unit guards (<c>AI_GUARD</c>): it is not idle, and its own acquisition is the guard's.</summary>
    public bool IsGuarding(Entity entity) => entity.Guard is not null;

    /// <summary>A new order of the unit ends its guard (<c>AIGuardState::onExit</c>); the attack it runs ends with the order's own <c>EndAttack</c>.</summary>
    public void EndGuard(Entity entity)
    {
        if (entity.Guard is null) return;
        entity.Guard = null;
        MarkDirty(entity);
    }

    /// <summary>A hit on a human player's object noted for the retaliation (<see cref="RetaliateFor"/>): the victim as it was hit, and its attacker.</summary>
    private readonly record struct RetaliationCall(EntityId Victim, PlayerId Owner, Fixed X, Fixed Y, Fixed Radius, EntityId Damager);

    /// <summary>The hits of this tick; decided at its end (<see cref="ProcessRetaliations"/>) and empty between ticks, so never saved.</summary>
    private readonly List<RetaliationCall> _retaliations = [];

    /// <inheritdoc cref="IWorldServices.RetaliateFor"/>
    /// <remarks>
    /// The player's logical retaliation mode is on for every human player: the original turns it on with a message its client sends in the first frame divisible by 30 in which the
    /// option (on by default) differs, so from the match's first second; the option to turn it off is not built. The hit is noted here and decided at the end of the tick
    /// (<see cref="ProcessRetaliations"/>): the original decides inside <c>attemptDamage</c>, but the retaliation's machine runs only from the unit's next decision either way, and the
    /// team relations a decision asks for are not to be read in the middle of the combat phase.
    /// </remarks>
    public void RetaliateFor(Entity victim, EntityId damagerId)
    {
        if (victim.Owner.IsSystem || IsComputerPlayer(victim.Owner) || !_players.ContainsKey(victim.Owner.Value) || !damagerId.IsValid || damagerId == victim.Id) return;
        if (victim.Template.IsKindOf("DRONE")) return;
        var (vx, vy, _) = victim.FrameEnd(_tick);
        _retaliations.Add(new RetaliationCall(victim.Id, victim.Owner, vx, vy, victim.Template.BoundingCircleRadius, damagerId));
        // A hit from outside a step (a host's or a test's direct damage) is decided at once: between ticks nothing may stay pending that a snapshot would not carry.
        if (!_stepping) ProcessRetaliations();
    }

    /// <summary>A step of the world is running (<see cref="Step"/>).</summary>
    private bool _stepping;

    /// <summary>
    /// The decisions of <c>ActiveBody::attemptDamage</c>'s retaliation for the hits of this tick, in their order. <c>shouldRetaliateAgainstAggressor</c>: the attacker exists, is no airborne
    /// target, is the victim's enemy, within 200 between the bounding circles. Then every object of the player and its allies within 120 + the victim's radius that would retaliate
    /// (<c>shouldRetaliate</c>: not <c>CANNOT_RETALIATE</c>, not immobile, no drone, an AI that is idle, not stealthed and undetected, not using an ability) and could attack the attacker
    /// here or after moving enters the retaliation; its machine runs from the unit's next decision.
    /// </summary>
    private void ProcessRetaliations()
    {
        if (_retaliations.Count == 0) return;
        foreach (var call in _retaliations)
        {
            if (FindEntity(call.Damager) is not { DestroyRequested: false, EffectivelyDead: false } damager || IsSignificantlyAboveTerrainAtFrameEnd(damager)) continue;
            var relation = FindEntity(call.Victim) is { } victim ? RelationshipBetween(damager, victim) : RelationshipOf(damager.Owner, call.Owner);
            if (relation != Relationship.Enemies) continue;
            var (dx0, dy0, _) = damager.FrameEnd(_tick);
            var ax = dx0 - call.X;
            var ay = dy0 - call.Y;
            if (!SquaredReach.Of(MaxRetaliationDistance + call.Radius + damager.Template.BoundingCircleRadius).Contains(ax * ax + ay * ay)) continue;
            var friends = SquaredReach.Of(RetaliationFriendsRadius + call.Radius);
            foreach (var player in Configuration.Players)
            {
                if (player.Id != call.Owner && RelationshipOf(call.Owner, player.Id) != Relationship.Allies) continue;
                foreach (var friend in EntitiesOwnedBy(player.Id).AsSpan())
                {
                    if (friend.DestroyRequested || friend.EffectivelyDead || friend.ContainedBy.IsValid || AiOf(friend) is not { } ai) continue;
                    var (fx, fy, _) = friend.FrameEnd(_tick);
                    var fdx = fx - call.X;
                    var fdy = fy - call.Y;
                    if (!friends.Contains(fdx * fdx + fdy * fdy)) continue;
                    if (friend.Template.IsKindOf("CANNOT_RETALIATE") || friend.Template.IsKindOf("IMMOBILE") || friend.Template.IsKindOf("DRONE") || !ai.IsIdle) continue;
                    if ((friend.HasStatus(StealthedStatus) && !friend.HasStatus(DetectedStatus)) || friend.HasStatus(UsingAbilityStatus)) continue;
                    if (!MayAct(friend, _tick) || !TurretsLetAttack(friend) || ActiveWeaponSet(friend) is null) continue;
                    UpdateWeaponSet(friend);
                    if (!CanAttack(friend, damager, false, out _)) continue;
                    // privateGuardRetaliate: the post is where the unit stands, the nemesis the attacker; the machine's first state is ATTACK_AGGRESSOR.
                    friend.Guard = new GuardState { X = fx, Y = fy, Retaliate = true, Starting = true, Phase = GuardPhase.Aggressor, Nemesis = damager.Id };
                    _acquirers.Add(friend.Id.Value);
                    MarkDirty(friend);
                }
            }
        }
        _retaliations.Clear();
    }

    private CommandRejection CheckGuard(PlayerId issuer, GuardCommand order)
    {
        if (order.Units.IsDefaultOrEmpty || order.Units.Length > GroupMoveCommand.MaxUnits) return CommandRejection.UnknownEntity;
        if (!Enum.IsDefined(order.Mode)) return CommandRejection.InvalidArguments;
        if (order.Object.IsValid && (FindEntity(order.Object) is not { DestroyRequested: false })) return CommandRejection.UnknownEntity;
        foreach (var id in order.Units)
        {
            if (!_entities.TryGetValue(id.Value, out var member) || member.DestroyRequested) return CommandRejection.UnknownEntity;
            if (member.Owner != issuer) return CommandRejection.NotPermitted;
            if (!CanMove(member)) return CommandRejection.CannotMove;
        }
        return 0;
    }

    /// <summary>
    /// <c>AIGroup::groupGuardPosition</c> / <c>groupGuardObject</c>: every unit guards the same point or object (<c>privateGuardPosition</c>): an immobile object or a projectile ignores the
    /// order; the old state is cleared; the machine starts in RETURN (its <c>onEnter</c> runs INNER, OUTER and GET_CRATE through at once, none of which has a nemesis).
    /// </summary>
    private void ExecuteGuard(GuardCommand order)
    {
        var guarded = order.Object.IsValid ? FindEntity(order.Object) : null;
        var any = false;
        foreach (var id in order.Units)
        {
            if (FindEntity(id) is not { DestroyRequested: false } unit || unit.Owner != order.Player || AiOf(unit) is not { } ai) continue;
            if (unit.Template.Locomotor is null || unit.Template.IsKindOf("IMMOBILE") || unit.Template.IsKindOf("PROJECTILE")) continue;
            ai.OrderIdle(this, true);
            var (x, y) = guarded is not null ? (guarded.X, guarded.Y) : (order.X, order.Y);
            unit.Guard = new GuardState { X = x, Y = y, Object = guarded?.Id ?? EntityId.Invalid, Mode = order.Mode };
            if (guarded is not null) (unit.Guard.GuardeeX, unit.Guard.GuardeeY) = (guarded.X, guarded.Y);
            _acquirers.Add(unit.Id.Value);
            EnterGuardReturn(unit, ai, unit.Guard, _tick);
            MarkDirty(unit);
            any = true;
        }
        if (!any) RecordRejection(order, CommandRejection.UnknownEntity);
    }

    /// <summary>
    /// <c>aiGuardArea</c> of a script (<c>TEAM_GUARD_AREA</c>, F-58 step 3): the units guard the trigger area - its bounds centre is the guarded position, the scans look inside the polygon
    /// out to the area's radius (<see cref="AreaGeometry"/>).
    /// </summary>
    internal void GuardArea(IReadOnlyList<Entity> units, ScriptArea area)
    {
        var (cx, cy, _) = AreaGeometry(area);
        foreach (var unit in units)
        {
            if (unit.DestroyRequested || unit.Owner.IsSystem || AiOf(unit) is not { } ai) continue;
            if (unit.Template.Locomotor is null || unit.Template.IsKindOf("IMMOBILE") || unit.Template.IsKindOf("PROJECTILE")) continue;
            ai.OrderIdle(this, true);
            unit.Guard = new GuardState { X = cx, Y = cy, Area = area.Name };
            _acquirers.Add(unit.Id.Value);
            EnterGuardReturn(unit, ai, unit.Guard, _tick);
            MarkDirty(unit);
        }
    }

    /// <summary>
    /// <c>PolygonTrigger::getCenterPoint</c> and <c>getRadius</c>: the centre of the outline's bounds, and the radius as the original computes it - <c>halfHeight = (hi.y + lo.y) / 2</c>, a
    /// plus where a minus belongs (<c>PolygonTrigger.cpp</c>), so the radius grows with the area's y position. Kept: the guard of an area reaches as far as the original's.
    /// </summary>
    private static (Fixed X, Fixed Y, Fixed Radius) AreaGeometry(ScriptArea area)
    {
        int loX = int.MaxValue, loY = int.MaxValue, hiX = int.MinValue, hiY = int.MinValue;
        foreach (var (x, y) in area.Points)
        {
            loX = Math.Min(loX, x);
            loY = Math.Min(loY, y);
            hiX = Math.Max(hiX, x);
            hiY = Math.Max(hiY, y);
        }
        if (area.Points.IsDefaultOrEmpty) return (Fixed.Zero, Fixed.Zero, Fixed.Zero);
        var two = Fixed.FromInt(2);
        var halfWidth = Fixed.FromInt(hiX - loX) / two;
        var halfHeight = Fixed.FromInt(hiY + loY) / two;
        return (Fixed.FromInt(loX + hiX) / two, Fixed.FromInt(loY + hiY) / two, AirModules.Distance2D(Fixed.Zero, Fixed.Zero, halfWidth, halfHeight));
    }

    private ScriptArea? GuardedArea(GuardState guard) => guard.Area.Length > 0 ? GuardAreas?.Invoke(guard.Area) : null;

    /// <summary>The guarded centre: the guarded object where its begun frame ends, else the guarded position.</summary>
    private (Fixed X, Fixed Y) GuardCentre(GuardState guard)
    {
        if (guard.Object.IsValid && FindEntity(guard.Object) is { DestroyRequested: false } guarded)
        {
            var (x, y, _) = guarded.FrameEnd(_tick);
            return (x, y);
        }
        return (guard.X, guard.Y);
    }

    /// <summary>
    /// <c>AI::getAdjustedVisionRangeForObject(OWNERTYPE | MOOD [| GUARDINNER])</c>: the vision range times the guard factor of a human (1.8 inner, 2.2 outer) or a computer player
    /// (1.1, 1.333), for a computer player's unit also times its team's mood; a contained unit's largest weapon range instead.
    /// </summary>
    private Fixed GuardRange(Entity entity, bool inner)
    {
        if (entity.ContainedBy.IsValid) return MaxWeaponRange(entity);
        var computer = IsComputerPlayer(entity.Owner);
        var factor = computer ? (inner ? GuardInnerFactorComputer : AcquireVisionFactorComputer) : (inner ? GuardInnerFactorHuman : GuardOuterFactorHuman);
        var range = entity.Template.VisionRange * factor;
        if (!computer) return range;
        return (AcquisitionAdvisor?.AttitudeOf(entity) ?? Attitude.Normal) switch
        {
            Attitude.Sleep => Fixed.Zero,
            Attitude.Alert => range * AlertRangeFactor,
            Attitude.Aggressive => range * AggressiveRangeFactor,
            _ => range,
        };
    }

    /// <summary>
    /// <c>GameLogicRandomValue(0, frames)</c> of the guard's first scan, drawn as <see cref="MoodCheckOffset"/> draws (a hash of the world seed, the unit and the frame, in frames of the
    /// original) - the original draws from the shared logic stream, which this does not shift. <paramref name="salt"/> keeps the RETURN and IDLE draws apart.
    /// </summary>
    private long GuardScanOffset(Entity entity, long tick, long frames, ulong salt)
    {
        unchecked
        {
            var z = (((ulong)Streams.Seed << 32) | entity.Id.Value) + 0x9E3779B97F4A7C15UL * (ulong)(Time.FramesIn(tick) + 1) + salt * 0xD1B54A32D192ED03UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return Time.OriginalFrames((long)(z % (ulong)(frames + 1)));
        }
    }

    /// <summary>The radius of an attacking state: INNER and ATTACK_AGGRESSOR the inner range, OUTER the outer one; for the retaliation its own (<see cref="RetaliateInnerFactor"/>).</summary>
    private Fixed AttackRadius(Entity entity, GuardState guard, GuardPhase phase)
    {
        if (!guard.Retaliate) return phase == GuardPhase.Outer ? OuterGuardRange(entity, guard) : GuardRange(entity, true);
        var inner = GuardRange(entity, true);
        var outer = GuardRange(entity, false);
        return phase switch
        {
            GuardPhase.Aggressor => outer + inner,
            GuardPhase.Inner => inner * RetaliateInnerFactor,
            _ => (outer + inner) * RetaliateOuterFactor,
        };
    }

    /// <summary>One decision of the guard, in the first tick of the unit's own logic frame of the original (<see cref="StepAcquisition"/>).</summary>
    private void StepGuard(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        var state = entity.Acquisition;
        if (guard.Starting)
        {
            // The retaliation's machine starts in ATTACK_AGGRESSOR; with nothing to attack it goes on to RETURN.
            guard.Starting = false;
            EnterGuardAggressor(entity, ai, guard, tick);
            return;
        }
        switch (guard.Phase)
        {
            case GuardPhase.Inner or GuardPhase.Outer or GuardPhase.Aggressor:
            {
                // INNER's condition (the guard's, not the retaliation's): an attacker in reach is answered first - not with the rule fix guard-answers-only-when-idle.
                if (guard.Phase == GuardPhase.Inner && !guard.Retaliate && !_templates.Rules.Has(Rules.GameFix.GuardAnswersOnlyWhenIdle) && AttackedAndCanAnswer(entity, guard))
                {
                    EndGuardAttack(entity, ai);
                    EnterGuardAggressor(entity, ai, guard, tick);
                    return;
                }
                if (GuardAttackGoesOn(entity, ai, guard, tick)) return;
                EndGuardAttack(entity, ai);
                // INNER -> OUTER; OUTER -> GET_CRATE -> RETURN; ATTACK_AGGRESSOR -> INNER, the retaliation's -> RETURN (success and failure alike).
                if (guard.Phase == GuardPhase.Inner) EnterGuardOuter(entity, ai, guard, tick);
                else if (guard.Phase == GuardPhase.Outer || guard.Retaliate) EnterGuardReturn(entity, ai, guard, tick);
                else EnterGuardInner(entity, ai, guard, tick);
                return;
            }
            case GuardPhase.Return:
                // RETURN's condition, which the rule fix guard-answers-only-when-idle removes (the attack is then answered in IDLE, its record not read before).
                if (!_templates.Rules.Has(Rules.GameFix.GuardAnswersOnlyWhenIdle) && AttackedAndCanAnswer(entity, guard))
                {
                    if (state is { Chasing: true } || entity.HasMoveTarget) ai.StopInternalMoveAtFrameEnd(this);
                    EnterGuardAggressor(entity, ai, guard, tick);
                    return;
                }
                if (tick >= guard.NextScanTick)
                {
                    guard.NextScanTick = tick + Time.OriginalFrames(GuardReturnScanFrames);
                    if (LookForGuardTarget(entity, guard) is not null)
                    {
                        ai.StopInternalMoveAtFrameEnd(this);
                        EnterGuardInner(entity, ai, guard, tick);
                        return;
                    }
                }
                if (!entity.HasMoveTarget) EnterGuardIdle(entity, guard, tick);
                return;
            case GuardPhase.Idle:
                if (AttackedAndCanAnswer(entity, guard))
                {
                    EnterGuardAggressor(entity, ai, guard, tick);
                    return;
                }
                if (tick < guard.NextScanTick) return;
                guard.NextScanTick = tick + Time.OriginalFrames(GuardScanFrames);
                if (LookForGuardTarget(entity, guard) is not null)
                {
                    EnterGuardInner(entity, ai, guard, tick);
                    return;
                }
                // The retaliation's IDLE looks once: nothing found, the machine is over and the unit idle again.
                if (guard.Retaliate)
                {
                    entity.Guard = null;
                    return;
                }
                if (guard.Object.IsValid && FindEntity(guard.Object) is { DestroyRequested: false } guarded)
                {
                    var (gx, gy, _) = guarded.FrameEnd(tick);
                    var dx = guard.GuardeeX - gx;
                    var dy = guard.GuardeeY - gy;
                    if (dx * dx > GuardeeMoveLimitSquared || dy * dy > GuardeeMoveLimitSquared)
                    {
                        (guard.GuardeeX, guard.GuardeeY) = (gx, gy);
                        EnterGuardReturn(entity, ai, guard, tick);
                    }
                }
                return;
        }
    }

    /// <summary>
    /// <c>AIGuardMachine::lookForInnerTarget</c>: none for a unit that cannot attack; the team's common target if it has one; otherwise the closest enemy within the inner range of the
    /// centre that the unit could attack, here or after moving (<c>PartitionFilterPossibleToAttack(ATTACK_NEW_TARGET, CMD_FROM_AI)</c>; no fog filter, as in the original), only flying
    /// ones in <see cref="GuardMode.FlyingUnitsOnly"/>. The find becomes the nemesis. Not modelled: the enter guard of templates with <c>isEnterGuard</c> (none shipped acts on it here).
    /// </summary>
    private Entity? LookForGuardTarget(Entity entity, GuardState guard)
    {
        if (!MayAct(entity, _tick) || !TurretsLetAttack(entity) || ActiveWeaponSet(entity) is null) return null;
        UpdateWeaponSet(entity);
        if (AcquisitionAdvisor is { } teams && teams.CommonTargetOf(entity) is { } common && IsGuardVictim(entity, common) && CanAttack(entity, common, false, out _))
        {
            guard.Nemesis = common.Id;
            return common;
        }
        var (cx, cy) = GuardCentre(guard);
        var limit = SquaredReach.Of(GuardRange(entity, true));
        // An area: its radius from its centre, and only what stands inside the polygon (PartitionFilterPolygonTrigger). Not modelled: the original looks only when some object
        // entered or left a trigger area, or one was made or destroyed, within the last scan period (updateObjectsChangedTriggerAreas) - here it looks every scan.
        var area = GuardedArea(guard);
        if (area is not null) limit = SquaredReach.Of(AreaGeometry(area).Radius);
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var player in Configuration.Players)
        {
            if (!AreEnemies(entity.Owner, player.Id)) continue;
            foreach (var candidate in EntitiesOwnedBy(player.Id).AsSpan())
            {
                if (candidate.DestroyRequested || candidate.EffectivelyDead || candidate.Body is null || candidate.ContainedBy.IsValid || !AreEnemies(entity, candidate)) continue;
                if (candidate.Template.IsKindOf("PROJECTILE") || candidate.Template.IsKindOf("INERT")) continue;
                // The retaliation's look adds PartitionFilterRejectBuildings: ordinary buildings are no target.
                if (guard.Retaliate && !AcquiresStructure(entity, candidate, false, IsComputerPlayer(entity.Owner))) continue;
                if (candidate.HasStatus(StealthedStatus) && !candidate.HasStatus(DetectedStatus)) continue;
                var (x, y, _) = candidate.FrameEnd(_tick);
                var dx = x - cx;
                var dy = y - cy;
                var distance = dx * dx + dy * dy;
                if (!limit.Contains(distance) || distance > bestDistance || (distance == bestDistance && best is not null && candidate.Id.Value > best.Id.Value)) continue;
                if (guard.Mode == GuardMode.FlyingUnitsOnly && !IsSignificantlyAboveTerrainAtFrameEnd(candidate)) continue;
                if (area is not null && !area.Contains(x, y)) continue;
                if (!CanAttack(entity, candidate, false, out _)) continue;
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best is not null) guard.Nemesis = best.Id;
        return best;
    }

    /// <summary>
    /// <c>AIGuardMachine</c>'s condition <c>hasAttackedMeAndICanReturnFire</c>: the body's last attacker unless it was read before (<c>getClearableLastAttacker</c>, cleared by the read), an
    /// enemy, alive, and one the unit can attack here or after moving.
    /// </summary>
    private bool AttackedAndCanAnswer(Entity entity, GuardState guard)
    {
        if (entity.LastDamageTick < 0 || entity.LastDamageTick == guard.ClearedDamageTick) return false;
        guard.ClearedDamageTick = entity.LastDamageTick;
        if (entity.LastDamageHealing || FindEntity(entity.LastDamageSource) is not { DestroyRequested: false, EffectivelyDead: false } attacker) return false;
        if (!AreEnemies(entity, attacker) || !MayAct(entity, _tick) || !TurretsLetAttack(entity) || ActiveWeaponSet(entity) is null) return false;
        UpdateWeaponSet(entity);
        return CanAttack(entity, attacker, false, out _);
    }

    private void EnterGuardInner(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        guard.Phase = GuardPhase.Inner;
        guard.GiveUpTick = 0;
        if (StartGuardAttack(entity, ai, guard, tick, AttackRadius(entity, guard, GuardPhase.Inner))) return;
        EnterGuardOuter(entity, ai, guard, tick);
    }

    private void EnterGuardOuter(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        guard.Phase = GuardPhase.Outer;
        guard.GiveUpTick = tick + Time.OriginalFrames(GuardChaseFrames);
        if (guard.Mode != GuardMode.WithoutPursuit && StartGuardAttack(entity, ai, guard, tick, AttackRadius(entity, guard, GuardPhase.Outer))) return;
        guard.GiveUpTick = 0;
        EnterGuardReturn(entity, ai, guard, tick);
    }

    private void EnterGuardAggressor(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        guard.Phase = GuardPhase.Aggressor;
        // The guard's aggressor is the body's last attacker; the retaliation's is the attacker it answers (its goal object), else the last attacker that did not heal.
        if (!guard.Retaliate && entity.LastDamageSource.IsValid) guard.Nemesis = entity.LastDamageSource;
        else if (guard.Retaliate && FindEntity(guard.Nemesis) is null && !entity.LastDamageHealing && entity.LastDamageSource.IsValid) guard.Nemesis = entity.LastDamageSource;
        guard.GiveUpTick = tick + Time.OriginalFrames(GuardChaseFrames);
        if (StartGuardAttack(entity, ai, guard, tick, AttackRadius(entity, guard, GuardPhase.Aggressor))) return;
        guard.GiveUpTick = 0;
        if (guard.Retaliate) EnterGuardReturn(entity, ai, guard, tick);
        else EnterGuardInner(entity, ai, guard, tick);
    }

    /// <summary>RETURN (<c>AIGuardReturnState::onEnter</c>): the first scan after up to a scan period, then a walk to the centre; already there, the unit is IDLE.</summary>
    private void EnterGuardReturn(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        guard.Phase = GuardPhase.Return;
        guard.GiveUpTick = 0;
        guard.NextScanTick = tick + GuardScanOffset(entity, tick, GuardReturnScanFrames, 1);
        var (cx, cy) = GuardCentre(guard);
        var (gx, gy) = FindFreeGoal(entity, entity.X, entity.Y, cx, cy);
        if (ai.StartInternalMove(gx, gy))
        {
            ai.BeginMoveWithNextFrame(this);
            WakeAt(entity, ai, Math.Min(ai.NextWake, tick + _turretFrameTicks));
            return;
        }
        EnterGuardIdle(entity, guard, tick);
    }

    private void EnterGuardIdle(Entity entity, GuardState guard, long tick)
    {
        guard.Phase = GuardPhase.Idle;
        guard.NextScanTick = tick + GuardScanOffset(entity, tick, GuardScanFrames, 2);
        _ = entity;
    }

    /// <summary>The attack of INNER, OUTER and ATTACK_AGGRESSOR (<c>AIAttackState</c> on the nemesis): false when there is no nemesis to attack.</summary>
    private bool StartGuardAttack(Entity entity, AIUpdateInterface ai, GuardState guard, long tick, Fixed radius)
    {
        if (FindEntity(guard.Nemesis) is not { } nemesis || !IsGuardVictim(entity, nemesis)) return false;
        var (cx, cy) = GuardCentre(guard);
        if (!WithinGuardRadius(nemesis, cx, cy, radius)) return false;
        var state = entity.Acquisition ??= new AcquisitionState();
        state.Victim = nemesis.Id;
        AimTurretsForAttack(entity, nemesis, TurretAimOrder.Attack);
        if (GuardEngage(entity, ai, state, nemesis, tick)) return true;
        EndGuardAttack(entity, ai);
        return false;
    }

    /// <summary>
    /// One decision of a running attack: <c>ExitConditions</c> - the nemesis gone or not attackable, outside the state's radius of the centre (2D), or the give-up time passed - end it; OUTER
    /// refreshes its give-up time while the nemesis is within the inner range of the centre (3D, as the original measures there).
    /// </summary>
    private bool GuardAttackGoesOn(Entity entity, AIUpdateInterface ai, GuardState guard, long tick)
    {
        if (entity.Acquisition is not { Victim.IsValid: true } state || FindEntity(state.Victim) is not { } victim || !IsGuardVictim(entity, victim)) return false;
        var (cx, cy) = GuardCentre(guard);
        var inner = GuardRange(entity, true);
        if (!WithinGuardRadius(victim, cx, cy, AttackRadius(entity, guard, guard.Phase))) return false;
        // The retaliation's exit conditions add the retaliator itself leaving its inner range of the post.
        if (guard.Retaliate && !WithinGuardRadius(entity, cx, cy, inner)) return false;
        if (guard.Phase == GuardPhase.Outer)
        {
            // OUTER refreshes its give-up time while the victim is within the inner range of the centre (the retaliation's ATTACK_AGGRESSOR does not).
            var (vx, vy, _) = victim.FrameEnd(tick);
            var dx = cx - vx;
            var dy = cy - vy;
            if (SquaredReach.Of(inner).Contains(dx * dx + dy * dy)) guard.GiveUpTick = tick + Time.OriginalFrames(GuardChaseFrames);
        }
        if (guard.GiveUpTick != 0 && tick >= guard.GiveUpTick) return false;
        return GuardEngage(entity, ai, state, victim, tick);
    }

    /// <summary>OUTER's radius: the outer range, at least the guarded area's radius (<c>AIGuardOuterState::onEnter</c>).</summary>
    private Fixed OuterGuardRange(Entity entity, GuardState guard)
    {
        var outer = GuardRange(entity, false);
        return GuardedArea(guard) is { } area ? Fixed.Max(outer, AreaGeometry(area).Radius) : outer;
    }

    /// <summary>
    /// A victim of the guard: alive, an enemy, not inside a container, not stealthed and undetected - but, unlike <see cref="IsAttackable"/>, not asked to be visible: the guard's look
    /// has no fog filter (<c>lookForInnerTarget</c>; Reforged adds one) and its attack walks into the fog after what it found.
    /// </summary>
    private bool IsGuardVictim(Entity entity, Entity victim)
        => !victim.DestroyRequested && !victim.EffectivelyDead && victim.Body is not null && !victim.ContainedBy.IsValid && AreEnemies(entity, victim)
           && !(victim.HasStatus(StealthedStatus) && !victim.HasStatus(DetectedStatus));

    private bool WithinGuardRadius(Entity victim, Fixed cx, Fixed cy, Fixed radius)
    {
        var (vx, vy, _) = victim.FrameEnd(_tick);
        var dx = vx - cx;
        var dy = vy - cy;
        return SquaredReach.Of(radius).Contains(dx * dx + dy * dy);
    }

    /// <summary>
    /// The attack state of the guard: in range the unit stops and fires; out of range a mobile unit walks at its victim (a guard order comes from a player or a script, so a human's
    /// unit chases too, <c>AIAttackPursueTargetState</c>); a unit that may not walk gives the victim up.
    /// </summary>
    private bool GuardEngage(Entity entity, AIUpdateInterface ai, AcquisitionState state, Entity victim, long tick)
    {
        if (!CanAttack(entity, victim, false, out var inRange)) return false;
        if (inRange)
        {
            if (state.Chasing)
            {
                ai.StopInternalMoveAtFrameEnd(this);
                state.Chasing = false;
                state.ChaseTick = 0;
            }
            PlaceAtFirePoint(entity, victim);
            AcquisitionAdvisor?.NoteFiring(entity, victim);
            AutoFire(entity, victim, false);
            return true;
        }
        if (entity.ContainedBy.IsValid || entity.Template.Locomotor is null) return false;
        if (!state.Chasing || tick - state.ChaseTick >= _chaseRefreshTicks || !entity.HasMoveTarget)
        {
            var (victimX, victimY, _) = victim.FrameEnd(tick);
            var (goalX, goalY) = FindAttackPosition(entity, victim) ?? (victimX, victimY);
            if (!ai.StartInternalMove(goalX, goalY)) return false;
            ai.BeginMoveWithNextFrame(this);
            WakeAt(entity, ai, Math.Min(ai.NextWake, tick + _turretFrameTicks));
            state.Chasing = true;
            state.ChaseTick = tick;
            AimTurretsForAttack(entity, victim, TurretAimOrder.Pursuit);
        }
        return true;
    }

    private void EndGuardAttack(Entity entity, AIUpdateInterface ai)
    {
        if (entity.Acquisition is not { } state) return;
        if (state.Victim.IsValid) EndEngagement(entity, ai, state);
        if (entity.Acquisition is { IsDefault: true }) entity.Acquisition = null;
    }
}
