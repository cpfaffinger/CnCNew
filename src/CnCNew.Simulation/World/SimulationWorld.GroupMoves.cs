using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary><c>AIGroup::computeIndividualDestination</c>: a member keeps its offset from the group's centre up to six of its bounding-circle radii.</summary>
    private const int GroupOffsetRadii = 6;

    // While one group order is planned: the members whose place is not planned yet (their old goals claim nothing). Empty outside ExecuteGroupMove; not state.
    private readonly HashSet<uint> _groupPending = [];
    private readonly List<(Entity Unit, Fixed X, Fixed Y, Fixed DistanceSquared)> _groupMembers = [];

    private bool IsGroundMover(Entity unit) => unit.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) == 0;

    /// <summary>
    /// The destination of a single unit's move as the reference chooses it when the move begins (<c>AIInternalMoveToState::onEnter</c>, AIStates.cpp:
    /// <c>Pathfinder::adjustDestination</c>, then <c>updateGoal</c>): the ordered point when a ground mover could stand there, else the nearest free place on the reference's square
    /// spiral (at most 400 cells) - "don't usurp your allies goals". A unit ordered onto the very point it stands on (this simulation's stop order) and an aircraft keep the point.
    /// When no cell is free the point stays as ordered (the reference lets the move fail).
    /// </summary>
    private (Fixed X, Fixed Y) IndividualDestination(Entity unit, Fixed x, Fixed y)
    {
        if (!IsGroundMover(unit)) return (x, y);
        var (standsX, standsY, _) = unit.FrameEnd(_tick);
        if (standsX == x && standsY == y) return (x, y);
        FindDestination(unit, ref x, ref y, true);
        return (x, y);
    }

    /// <summary>
    /// <c>AIGroup::groupMoveToPosition</c> / <c>groupAttackMoveToPosition</c> (AIGroup.cpp), the loop every group move ends in: the members are sorted by their distance from the
    /// goal, nearest first ("works better if you let the near units get the first paths"); the nearest member's position is the centre; each member's destination is the goal plus
    /// its own offset from that centre, shortened to six of its bounding-circle radii (<c>computeIndividualDestination</c>) and - for a ground mover - moved to the nearest free
    /// place (<c>Pathfinder::adjustDestination</c>), which the member then holds as its goal against everybody ordered after it. The goals the members held before count for
    /// nothing (<c>removeGoal</c> for every member first). Every unit then walks one path to a place that is its own from the start.
    /// <para>
    /// Not built: the column moves of far-away infantry and vehicles along one group path (<c>friend_moveInfantryToPos</c>, <c>friend_moveVehicleToPos</c>), the tightening of a
    /// group ordered into its own bounds (<c>groupTightenToPosition</c>), formations the player set (<c>friend_moveFormationToPos</c>), the path-cost test of an adjusted cell
    /// (<c>checkForAdjust</c> with <c>groupDest</c>) and the delayed mood check of stealth units.
    /// </para>
    /// </summary>
    private void ExecuteGroupMove(GroupMoveCommand order)
    {
        var members = _groupMembers;
        members.Clear();
        _groupPending.Clear();
        foreach (var id in order.Units)
        {
            // A member that is gone, changed hands or cannot move any more since the order was accepted takes no part (a dead object leaves its AIGroup); a unit named twice counts once.
            if (FindEntity(id) is not { DestroyRequested: false } unit || unit.Owner != order.Player || !CanMove(unit)) continue;
            if (!_groupPending.Add(id.Value)) continue;
            // Where the unit stands once the logic frame of the original it has begun is over (docs/SIMULATION-CORE.md §43.2, rule 4); 30 Hz: its position.
            var (standsX, standsY, _) = unit.FrameEnd(_tick);
            var dx = standsX - order.X;
            var dy = standsY - order.Y;
            members.Add((unit, standsX, standsY, dx * dx + dy * dy));
        }
        if (members.Count == 0)
        {
            RecordRejection(order, CommandRejection.UnknownEntity);
            return;
        }
        members.Sort(static (a, b) =>
        {
            var byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            return byDistance != 0 ? byDistance : a.Unit.Id.Value.CompareTo(b.Unit.Id.Value);
        });
        var (centreX, centreY) = (members[0].X, members[0].Y);
        foreach (var (unit, standsX, standsY, _) in members)
        {
            var offsetX = standsX - centreX;
            var offsetY = standsY - centreY;
            var length = Fixed.Sqrt(offsetX * offsetX + offsetY * offsetY);
            var longest = unit.Template.BoundingCircleRadius * Fixed.FromInt(GroupOffsetRadii);
            if (length > longest)
            {
                offsetX = offsetX * longest / length;
                offsetY = offsetY * longest / length;
            }
            var x = order.X + offsetX;
            var y = order.Y + offsetY;
            _groupPending.Remove(unit.Id.Value);
            if (IsGroundMover(unit)) FindDestination(unit, ref x, ref y, true);
            // GameLogicDispatch doMoveTo: a move order releases any temporary weapon lock.
            ReleaseWeaponLock(unit, WeaponLockKind.Temporary);
            var ai = AiOf(unit)!;
            ai.OrderMove(this, x, y, true, attackMove: order.Attack);
            // The movement profile's shared group path: the members of one order, named by its player and sequence number.
            if (PathsQueued) ai.MarkGroupOrder(((ulong)(uint)order.Player.Value << 48) ^ order.Sequence ^ 1UL << 63);
        }
        members.Clear();
        _groupPending.Clear();
    }
}
