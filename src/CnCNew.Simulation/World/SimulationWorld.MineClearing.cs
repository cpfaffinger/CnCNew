using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// Mine clearing (F-69): the bored builder that finds nothing to repair looks for a mine to clear (<c>DozerPrimaryIdleState::update</c>, <c>findMine</c>) and attacks it as the AI's own
/// order (<c>aiAttackObject(mine, 1, CMD_FROM_DOZER)</c>).
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>findMine</c>: the closest object of kind <c>MINE</c> within <paramref name="range"/> of the builder (between the centres, on the ground; ties by id) of an enemy or a neutral
    /// player (<c>PartitionFilterRelationship</c>: "clearing allied mines (ie, OURS) is really dumb"), that the builder may attack (<c>PartitionFilterPossibleToAttack</c>: seen,
    /// not stealthed undetected, and a weapon of its active set can hit it).
    /// </summary>
    public Entity? FindMineToClear(Entity builder, Fixed range)
    {
        Entity? best = null;
        var bestDistance = Fixed.Zero;
        var reach = SquaredReach.Of(range);
        foreach (var mine in EntitiesOfKind("MINE").AsSpan())
        {
            if (mine.Owner == builder.Owner || RelationshipOf(builder.Owner, mine.Owner) == Relationship.Allies) continue;
            if (!IsGuardVictim(builder, mine) && !IsNeutralVictim(builder, mine)) continue;
            if (!CanPlayerSee(builder.Owner, mine) || !CanAttack(builder, mine, false, out _)) continue;
            var dx = mine.X - builder.X;
            var dy = mine.Y - builder.Y;
            var distance = dx * dx + dy * dy;
            if (!reach.Contains(distance)) continue;
            if (best is null || distance < bestDistance)
            {
                best = mine;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>The builder walks at <paramref name="victim"/> and attacks it until it is gone: a forced attack in the world (as a slave's, F-65) that may be on a neutral object.</summary>
    public void BuilderAttacks(Entity builder, Entity victim)
    {
        if (AiOf(builder) is not { } ai) return;
        ai.OrderIdle(this);
        var state = builder.Acquisition ??= new AcquisitionState();
        state.Victim = victim.Id;
        state.Forced = true;
        state.Neutral = RelationshipOf(builder.Owner, victim.Owner) == Relationship.Neutral;
        AimTurretsForAttack(builder, victim, TurretAimOrder.Attack);
        _acquirers.Add(builder.Id.Value);
        MarkDirty(builder);
        // The order is given where the builder's frame ends. At 30 Hz the units' own acquisition, after the updates of this tick, takes it at once; above 30 Hz the acquisition took
        // its turn in the frame's first tick already, and the attack begins here, as it would have.
        if (_firstFrameTicks != TickOrder.Next && (_tick - builder.SpawnTick) % _firstFrameTicks != 0 && MayAct(builder, _tick)) GuardEngage(builder, ai, state, victim, _tick);
    }

    private CommandRejection CheckSetMineClearing(PlayerId issuer, SetMineClearingCommand order)
    {
        if (order.Units.IsDefaultOrEmpty || order.Units.Length > GroupMoveCommand.MaxUnits) return CommandRejection.UnknownEntity;
        foreach (var id in order.Units)
        {
            if (!_entities.TryGetValue(id.Value, out var member) || member.DestroyRequested) return CommandRejection.UnknownEntity;
            if (member.Owner != issuer) return CommandRejection.NotPermitted;
        }
        return 0;
    }

    /// <summary><c>AIGroup::setMineClearingDetail(true)</c>: every member carries the flag (a builder at work drops it again in its next update, as the original's does).</summary>
    private void ExecuteSetMineClearing(SetMineClearingCommand order)
    {
        foreach (var id in order.Units)
            if (FindEntity(id) is { DestroyRequested: false } unit && unit.Owner == order.Player && !unit.MineClearingDetail)
            {
                unit.MineClearingDetail = true;
                MarkDirty(unit);
            }
    }

    /// <summary>
    /// <c>aiAttackPosition</c> with a weapon that has a <c>ContinueAttackRange</c> ("ick... rather grody hack for disarming stuff"): the closest object within that range of the place that the
    /// unit may attack - stealthed or not (<c>OBJECT_STATUS_IGNORING_STEALTH</c>) - of an enemy or a neutral player (between the centres, on the ground; ties by id); null when there is
    /// none, and the unit then fires one shot at the place.
    /// </summary>
    private Entity? ClearingVictimNear(Entity unit, Fixed x, Fixed y)
    {
        var range = ContinueRangeOf(unit);
        if (range <= Fixed.Zero) return null;
        Entity? best = null;
        var bestDistance = Fixed.Zero;
        var reach = SquaredReach.Of(range);
        foreach (var candidate in Entities)
        {
            if (!IsClearingVictim(unit, candidate) || !CanAttack(unit, candidate, false, out _)) continue;
            var dx = candidate.X - x;
            var dy = candidate.Y - y;
            var distance = dx * dx + dy * dy;
            if (!reach.Contains(distance)) continue;
            if (best is null || distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// A victim of an attack with a weapon that has a continue range: an enemy's or a neutral object, alive, not inside a container; its stealth does not hide it - the original sets
    /// <c>OBJECT_STATUS_IGNORING_STEALTH</c> on a unit that attacks with such a weapon (<c>AIAttackState::onEnter</c>).
    /// </summary>
    private bool IsClearingVictim(Entity entity, Entity victim)
        => !victim.DestroyRequested && !victim.EffectivelyDead && victim.Body is not null && !victim.ContainedBy.IsValid && victim.Owner != entity.Owner
           && RelationshipOf(entity.Owner, victim.Owner) is Relationship.Enemies or Relationship.Neutral && ContinueRangeOf(entity) > Fixed.Zero;

    /// <summary>A neutral object that may be attacked as a forced attack's victim: the guard's test, with neutral in place of enemies.</summary>
    private bool IsNeutralVictim(Entity entity, Entity victim)
        => !victim.DestroyRequested && !victim.EffectivelyDead && victim.Body is not null && !victim.ContainedBy.IsValid && RelationshipOf(entity.Owner, victim.Owner) == Relationship.Neutral
           && !(victim.HasStatus(StealthedStatus) && !victim.HasStatus(DetectedStatus));
}
