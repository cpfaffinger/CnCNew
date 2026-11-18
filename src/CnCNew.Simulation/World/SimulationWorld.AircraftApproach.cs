using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary><c>AIAttackApproachTargetState</c>'s <c>MIN_RECOMPUTE_TIME</c>: the approach is computed again at most every ten logic frames of the original.</summary>
    private const long ApproachRecomputeFrames = 10;

    /// <summary>
    /// <c>AIUpdateInterface::computeAttackPath</c>: "for now, airborne objects don't pathfind" - an attacker whose locomotors include the air (not <c>landBound</c>) and that is above the
    /// terrain flies straight to <see cref="AircraftApproachTarget"/>. Read where the unit's frame ends.
    /// </summary>
    internal bool ApproachesInFlight(Entity unit)
    {
        if (unit.Template.Locomotor is not { } locomotor || (locomotor.Surfaces & LocomotorSurfaces.Air) == 0) return false;
        var (x, y, z) = unit.FrameEnd(_tick);
        // Thing::isAboveTerrain: getHeightAboveTerrain() > 0.
        return z > LayerHeightAt(x, y, unit.Layer);
    }

    /// <summary>
    /// The weapon an approach of <paramref name="unit"/> to <paramref name="victim"/> measures with (<c>getCurrentWeapon</c> after <c>AIAttackState::onEnter</c> chose the best weapon for the
    /// victim, ignoring range): null when no weapon can hurt it.
    /// </summary>
    private WeaponInfo? ApproachWeapon(Entity unit, Entity victim)
    {
        var slot = AttackSlotIgnoringRange(unit, victim);
        return slot >= 0 && ActiveWeaponSet(unit) is { } set && set.HasWeapon(slot) ? _templates.Weapons[set.Weapons[slot]] : null;
    }

    /// <summary>
    /// <c>Weapon::computeApproachTarget</c> for an airborne attacker (#451), measured between the two bounding circles (<c>ATTACK_RANGE_CALC_TYPE</c>) and read where both frames end:
    /// <list type="bullet">
    /// <item>inside a minimum range above a cell, the point halfway between minimum and maximum range from the victim, on the attacker's own side of it, or on the far side when it
    /// heads toward the victim ("Don't do a 180 degree turn");</item>
    /// <item>circles that touch: where the attacker is ("we're close enough!");</item>
    /// <item>a contact weapon: the victim's place;</item>
    /// <item>else 0.9 of the weapon's range from the victim (<c>ATTACK_RANGE_APPROACH_FUDGE</c>), on the line from the victim to the attacker; an aircraft that adjusts its destination
    /// takes the nearest box of goal cells along the spiral that no other aircraft holds and from which the weapon reaches (<c>Pathfinder::adjustTargetDestination</c>,
    /// <c>checkForTarget</c>, <c>isGoalPosWithinAttackRange</c>) - none found, the point stays.</item>
    /// </list>
    /// The aircraft then holds the box of the point (<c>doPathfind</c>: <c>updateGoal</c> of the path's last node). Not modelled: <c>clipToTerrainExtent</c> of the first point.
    /// </summary>
    internal (Fixed X, Fixed Y) AircraftApproachTarget(Entity source, Entity victim, WeaponInfo weapon)
    {
        var (ownX, ownY, _) = source.FrameEnd(_tick);
        var (victimX, victimY, _) = victim.FrameEnd(_tick);
        var radii = CircleRadius(source) + CircleRadius(victim);
        var minimum = weapon.MinimumRange();
        var (dx, dy) = (ownX - victimX, ownY - victimY);
        var centres = Fixed.Sqrt(dx * dx + dy * dy);
        var (goalX, goalY) = (victimX, victimY);
        var adjust = false;
        if (minimum > Fixed.FromInt(TerrainMap.CellSize) && centres - radii < minimum)
        {
            var heading = source.FrameEndAngle(_tick);
            var (dirX, dirY) = centres > Fixed.Zero ? (dx / centres, dy / centres) : (-FixedTrig.Cos(heading), -FixedTrig.Sin(heading));
            if (FixedTrig.AngleDifference(heading, FixedTrig.Atan2(-dirY, -dirX)).Abs() < FixedTrig.HalfPi) (dirX, dirY) = (-dirX, -dirY);
            var distance = (AttackRangeOf(source, weapon) + minimum) / Fixed.FromInt(2) + radii;
            (goalX, goalY) = (victimX + dirX * distance, victimY + dirY * distance);
        }
        else if (centres <= radii) (goalX, goalY) = (ownX, ownY);
        else if (!IsContactWeapon(weapon))
        {
            var distance = AttackRangeOf(source, weapon) * ApproachRangeFudge;
            (goalX, goalY) = (victimX + dx * distance / centres, victimY + dy * distance / centres);
            adjust = true;
        }

        if (!TakesAircraftGoals(source)) return (goalX, goalY);
        var frame = Time.ToOriginalFrames(_tick);
        if (adjust)
        {
            var far = SquaredReach.Of(AttackRangeOf(source, weapon) - GoalRangeMargin + radii);
            var near = SquaredReach.Of(minimum + GoalRangeMargin + radii);
            Search(source, frame, ref goalX, ref goalY, (x, y) =>
            {
                var (offX, offY) = (victimX - x, victimY - y);
                var squared = offX * offX + offY * offY;
                return far.Contains(squared) && !near.ContainsStrictly(squared);
            });
        }
        TakeAircraftGoal(source, frame, goalX, goalY);
        return (goalX, goalY);
    }

    /// <summary><c>ATTACK_RANGE_APPROACH_FUDGE</c>: "select a spot along the line between us, in range of our weapon".</summary>
    private static readonly Fixed ApproachRangeFudge = Fixed.FromDecimal(0.9m);

    /// <summary>
    /// <c>AIAttackApproachTargetState::computePath</c> of an airborne attacker on an attack that is under way (<paramref name="state"/>): the approach is computed when the unit has no move
    /// (<c>forceRepath</c>: no path), or when the state has just been entered; otherwise at most every <see cref="ApproachRecomputeFrames"/> frames, and only when the victim moved by more
    /// than a tenth of its distance since the last time (<c>isSamePosition</c>). Null when it stays.
    /// </summary>
    private (Fixed X, Fixed Y)? RecomputeApproach(Entity entity, AcquisitionState state, Entity victim, WeaponInfo weapon)
    {
        var force = !entity.HasMoveTarget || !state.Approaching;
        if (!force && _tick - state.ApproachTick < Time.OriginalFrames(ApproachRecomputeFrames)) return null;
        state.Approaching = true;
        state.ApproachTick = _tick;
        var (victimX, victimY, _) = victim.FrameEnd(_tick);
        if (!force)
        {
            var (ownX, ownY, _) = entity.FrameEnd(_tick);
            var (movedX, movedY) = (victimX - state.ApproachVictimX, victimY - state.ApproachVictimY);
            var (toX, toY) = (victimX - ownX, victimY - ownY);
            // Tolerance is (dist/10) squared.
            if (movedX * movedX + movedY * movedY <= (toX * toX + toY * toY) / Fixed.FromInt(100)) return null;
        }
        (state.ApproachVictimX, state.ApproachVictimY) = (victimX, victimY);
        return AircraftApproachTarget(entity, victim, weapon);
    }
}
