using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// F-55, the attack's view: an object of the kind <c>ATTACK_NEEDS_LINE_OF_SIGHT</c> does not fire through the terrain or a structure. <c>outOfWeaponRangeObject</c> (the
/// condition of the attack state's aim and fire, and of the turret's fire) counts a blocked view as out of range; the test is <c>Pathfinder::isAttackViewBlockedByObstacle</c>.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>TAiData::m_attackUsesLineOfSight</c> (<c>AIData.ini AttackUsesLineOfSight</c>): the reference default and the shipped value are both true.</summary>
    private const bool AttackUsesLineOfSight = true;

    private const string NeedsLineOfSightKind = "ATTACK_NEEDS_LINE_OF_SIGHT";

    /// <summary>
    /// The view part of <c>outOfWeaponRangeObject</c>: tested only for an attacker on the ground (moving on the ground, <c>IMMOBILE</c> or <c>SPAWNS_ARE_THE_WEAPONS</c>), with a
    /// weapon that is not a contact weapon, at a victim that is not significantly above the terrain. Not modelled: a weapon's leech range (the port has none).
    /// </summary>
    private bool AttackViewBlocks(Entity attacker, WeaponInfo weapon, Entity victim)
    {
        if (!AttackUsesLineOfSight || Navigation is null || Terrain is null || !attacker.Template.IsKindOf(NeedsLineOfSightKind)) return false;
        // WeaponTemplate::isContactWeapon: "fudge this a little to account for pathfinding roundoff".
        if (weapon.AttackRange * ContactRangeFudge < Fixed.FromInt(global::CnCNew.Simulation.Terrain.TerrainMap.CellSize)) return false;
        // isDoingGroundMovement, IMMOBILE, "the brutal special case for stinger soldiers"; a rider in a container is on the ground as well, but a contained object attacks nothing here.
        var onGround = (attacker.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) == 0)
            || attacker.Template.IsKindOf("IMMOBILE") || attacker.Template.IsKindOf("SPAWNS_ARE_THE_WEAPONS");
        if (!onGround || IsSignificantlyAboveTerrainAtFrameEnd(victim)) return false;
        return IsAttackViewBlocked(attacker, victim);
    }

    private static readonly Fixed ContactRangeFudge = Fixed.FromDecimal(1.05m);

    /// <summary>
    /// <see cref="ChooseWeapon"/>, then the view of the chosen weapon: the choice of an ordered attack. The original chooses the weapon without looking
    /// (<c>chooseBestWeaponForTarget</c>) and its attack state then counts a blocked view as out of range, so a unit whose view is blocked approaches instead of firing.
    /// </summary>
    public WeaponChoice ChooseWeaponInView(Entity source, Entity victim, FireOrigin origin, int slot = WeaponSlot.Best)
    {
        var choice = ChooseWeapon(source, victim, origin, slot);
        if (choice.Rejection is not (0 or CommandRejection.WeaponNotReady) || choice.Slot < 0) return choice;
        if (ActiveWeaponSet(source) is not { } set || !set.HasWeapon(choice.Slot)) return choice;
        return AttackViewBlocks(source, _templates.Weapons[set.Weapons[choice.Slot]], victim) ? choice with { Rejection = CommandRejection.OutOfRange } : choice;
    }

    /// <summary>Whether the weapon a player's order would choose against the victim is in range but cannot see it: the unit must move to fire.</summary>
    public bool AttackViewBlocksFiring(Entity source, Entity victim)
    {
        var choice = ChooseWeapon(source, victim, FireOrigin.Player);
        if (choice.Slot < 0 || ActiveWeaponSet(source) is not { } set || !set.HasWeapon(choice.Slot)) return false;
        return AttackViewBlocks(source, _templates.Weapons[set.Weapons[choice.Slot]], victim);
    }

    /// <summary>
    /// <c>Pathfinder::isAttackViewBlockedByObstacle</c>: for an attacker of the kind <c>ATTACK_NEEDS_LINE_OF_SIGHT</c>, the terrain between the attacker's top and the victim's
    /// centre (<c>Weapon::isClearGoalFiringLineOfSightTerrain</c>; not for an <c>IMMOBILE</c> attacker, which "can't move around it") and the structures in the pathfind cells
    /// between them. Above 30 Hz both are read where the logic frame of the original they have begun ends, as the range is.
    /// </summary>
    public bool IsAttackViewBlocked(Entity attacker, Entity victim)
    {
        if (!AttackUsesLineOfSight || Navigation is null || Terrain is null || !attacker.Template.IsKindOf(NeedsLineOfSightKind)) return false;
        var (attackerX, attackerY, attackerZ) = attacker.FrameEnd(_tick);
        var (victimX, victimY, victimZ) = victim.FrameEnd(_tick);
        if (!attacker.Template.IsKindOf("IMMOBILE"))
        {
            var top = attackerZ + MaxHeightAbovePosition(attacker.Template);
            if (!Terrain.IsClearLineOfSight(attackerX, attackerY, top, victimX, victimY, victimZ + victim.Template.CenterZDelta)) return true;
        }
        // attackBlockedByObstacleCallback: never the attacker itself, the victim, the victim's slaver, the attacker's container or the attacker's slaver.
        Span<ulong> excepted = [attacker.Id.Value, victim.Id.Value, SlaverOf(victim).Value, attacker.ContainedBy.Value, SlaverOf(attacker).Value];
        return Navigation.IsAttackLineBlocked(attackerX, attackerY, victimX, victimY, excepted);
    }

    /// <summary>
    /// <c>PartitionFilterLineOfSight</c>, the filter <c>getNextMoodTarget</c> adds (<c>AI::CAN_SEE</c>) for an acquirer of the kind <c>ATTACK_NEEDS_LINE_OF_SIGHT</c>: the terrain
    /// between the two tops (<c>isClearLineOfSightTerrain</c> with both objects - for every candidate, an aircraft and an <c>IMMOBILE</c> acquirer too), then, for a candidate
    /// not significantly above the terrain, the attack's view (<c>Pathfinder::isViewBlockedByObstacle</c>). A blocked candidate is passed by, also by a unit that may chase.
    /// </summary>
    private bool AcquisitionViewBlocked(Entity entity, Entity candidate)
    {
        if (!AttackUsesLineOfSight || Terrain is null || !entity.Template.IsKindOf(NeedsLineOfSightKind)) return false;
        var (ownX, ownY, ownZ) = entity.FrameEnd(_tick);
        var (otherX, otherY, otherZ) = candidate.FrameEnd(_tick);
        if (!Terrain.IsClearLineOfSight(ownX, ownY, ownZ + MaxHeightAbovePosition(entity.Template), otherX, otherY, otherZ + MaxHeightAbovePosition(candidate.Template))) return true;
        return !IsSignificantlyAboveTerrainAtFrameEnd(candidate) && IsAttackViewBlocked(entity, candidate);
    }

    /// <summary><c>GeometryInfo::getMaxHeightAbovePosition</c>: a sphere its radius, a box or a cylinder its height.</summary>
    private static Fixed MaxHeightAbovePosition(ObjectTemplate template)
        => template.Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase) ? template.GeometryMajorRadius : template.GeometryHeight;

    private static EntityId SlaverOf(Entity entity)
    {
        foreach (var module in entity.ModuleArray)
            if (module is ISlavedModule slaved && slaved.Slaver.IsValid) return slaved.Slaver;
        return EntityId.Invalid;
    }
}
