using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// The Chinook's combat drop (<c>AIGroup::groupCombatDrop</c>, <c>ChinookAIUpdate::privateCombatDrop</c>, <c>AIRappelState</c>): the order, what may be dropped into, and what a rappeller does
/// when it reaches the top of a building. The flight and the ropes are <see cref="ChinookAIUpdate"/>'s, the way down is <see cref="AIUpdateInterface"/>'s.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>How many of the enemies in a building a rappeller takes with it; with that many gone, it dies too (<c>MAX_TO_KILL</c>).</summary>
    private const int CombatDropMaxKills = 2;

    private CommandRejection CheckCombatDrop(PlayerId issuer, CombatDropCommand order)
    {
        if (order.Units.IsDefaultOrEmpty || order.Units.Length > GroupMoveCommand.MaxUnits) return CommandRejection.UnknownEntity;
        var target = order.Target.IsValid ? FindEntity(order.Target) : null;
        if (order.Target.IsValid && target is not { DestroyRequested: false }) return CommandRejection.UnknownEntity;
        foreach (var id in order.Units)
        {
            if (!_entities.TryGetValue(id.Value, out var member) || member.DestroyRequested) return CommandRejection.UnknownEntity;
            if (member.Owner != issuer) return CommandRejection.NotPermitted;
            if (AiOf(member) is not ChinookAIUpdate) return CommandRejection.CannotMove;
            if (target is not null && !CanCombatDropInto(member, target)) return CommandRejection.CannotEnterContainer;
        }
        return 0;
    }

    private void ExecuteCombatDrop(CombatDropCommand order)
    {
        var target = order.Target.IsValid ? FindEntity(order.Target) : null;
        foreach (var id in order.Units)
            if (FindEntity(id) is { DestroyRequested: false } unit && AiOf(unit) is ChinookAIUpdate chinook) chinook.OrderCombatDrop(this, target, order.X, order.Y);
    }

    /// <summary>
    /// <c>ActionManager::canEnterObject</c> with <c>COMBATDROP_INTO</c>: a building that stands and has room for riders in general; a faction's own structures are closed, and whether there is a free
    /// place is only asked when the rappeller arrives.
    /// </summary>
    public bool CanCombatDropInto(Entity unit, Entity target)
    {
        if (unit == target || target.EffectivelyDead) return false;
        if (!IsPositionExploredBy(unit.Owner, target.X, target.Y)) return false;
        if (unit.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(ObjectStatusBits.UnderConstruction) || target.HasStatus(SoldStatus)) return false;
        if (unit.Template.IsKindOf("IGNORED_IN_GUI") || unit.Template.IsKindOf("MOB_NEXUS") || target.Template.IsKindOf("IGNORED_IN_GUI")) return false;
        if (target.Subdued) return false;
        if (target.Container is not { } contain) return false;
        if (contain is HealContain && unit.Body is { } body && body.Health == body.MaxHealth) return false;
        return !AirWeaponModules.IsFactionStructure(target);
    }

    /// <inheritdoc cref="IWorldServices.FinishCombatDrop"/>
    public void FinishCombatDrop(Entity rappeller, Entity building)
    {
        var killed = 0;
        while (killed < CombatDropMaxKills && EnemyInside(rappeller, building) is { } enemy)
        {
            // removeFromContain first: the building is free for the rappeller in this frame ("killer->scoreTheKill( enemy )" is the source of the kill).
            if (building.Container is ContainModuleBase held) held.Remove(this, enemy, true);
            Damage(enemy.Id, new DamageInfo(rappeller.Id, Fixed.Zero, DamageTypes.Unresistable, DeathTypes.Normal, true));
            killed++;
        }
        if (killed == CombatDropMaxKills)
        {
            Kill(rappeller.Id);
            return;
        }
        if (building.Container is { } contain && contain.IsValidContainerFor(this, rappeller, true) && TryEnterContainer(rappeller, building)) return;
        // "This can legitimately happen if you drop into a full (or nearly full) building. Let's just place the guy on the ground nearby."
        var offset = Fixed.Min(rappeller.Template.BoundingCircleRadius, building.Template.BoundingCircleRadius);
        var angle = LogicRandom.Range(FixedTrig.Pi, FixedTrig.TwoPi);
        rappeller.X = building.X + offset * FixedTrig.Cos(angle);
        rappeller.Y = building.Y + offset * FixedTrig.Sin(angle);
        rappeller.Z = GroundHeightAt(rappeller.X, rappeller.Y);
        rappeller.Angle = building.Angle;
        MarkDirty(rappeller);
        AiOf(rappeller)?.OrderMove(this, rappeller.X, rappeller.Y - building.Template.BoundingCircleRadius, fromPlayer: false);
    }

    /// <summary><c>findEnemyInContainer</c>: "we want to know if I consider it to be an enemy, not vice versa".</summary>
    private Entity? EnemyInside(Entity killer, Entity building)
    {
        if (building.Container is not { } contain) return null;
        foreach (var id in contain.Contained)
            if (FindEntity(id) is { EffectivelyDead: false } rider && AreEnemies(killer.Owner, rider.Owner)) return rider;
        return null;
    }
}
