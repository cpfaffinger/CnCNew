using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>Own value: how many rows of places behind each other, and how many places to either side of the middle one, are tried for a free goal.</summary>
    private const int FreeGoalRows = 4;

    private const int FreeGoalColumnsEitherSide = 3;

    /// <summary>A produced unit whose place in front of its factory is chosen in the spawn phase of <see cref="Tick"/>. Above 30 Hz only.</summary>
    private readonly record struct ExitClaim(EntityId Unit, long Tick);

    // Above 30 Hz only: the units that appeared in the logic frame of the original that is running and have not been given their place yet, in the order they appeared.
    private readonly List<ExitClaim> _exitClaims = [];

    // While a claim of _exitClaims is decided: units that appeared in the same tick after the claiming one do not exist for it (at 30 Hz they are created after its claim).
    private Entity? _claiming;

    /// <summary>
    /// Above 30 Hz (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3): at 30 Hz the spawn phase that creates a produced unit chooses its place in front of the factory and asks who
    /// stands there to make room, seeing every object after the whole step of that logic frame of the original. At a higher rate the unit appears with the first tick of that
    /// frame; the same decision is taken in the spawn phase of the frame's last tick, when every object in phase has covered its step, and the unit and whoever makes room for it
    /// begin to move with the next frame - as they do at 30 Hz. A unit that got another order meanwhile, or is gone, keeps what it has.
    /// </summary>
    private void ProcessExitClaims()
    {
        var kept = 0;
        for (var index = 0; index < _exitClaims.Count; index++)
        {
            var claim = _exitClaims[index];
            if (claim.Tick > _tick)
            {
                _exitClaims[kept++] = claim;
                continue;
            }
            if (FindEntity(claim.Unit) is not { DestroyRequested: false, EffectivelyDead: false } unit || unit.ContainedBy.IsValid) continue;
            if (AiOf(unit) is not { } ai || !ai.TryGetExitOrder(out var doorX, out var doorY)) continue;
            _claiming = unit;
            var (exitX, exitY) = ClaimExitPoint(unit, doorX, doorY, unit.MoveTargetX, unit.MoveTargetY);
            _claiming = null;
            ai.RetargetExit(this, exitX, exitY);
        }
        _exitClaims.RemoveRange(kept, _exitClaims.Count - kept);
    }

    private bool AppearsAfterClaim(Entity other) => _claiming is { } unit && other.SpawnTick == unit.SpawnTick && other.Id.Value > unit.Id.Value;

    // Written only while a claim waits, which never happens at 30 Hz: a 30 Hz world keeps its serialized form (and hash).
    private void DescribeExitClaims(IStateWriter writer)
    {
        if (_exitClaims.Count == 0) return;
        writer.BeginArray("exitClaims");
        foreach (var claim in _exitClaims)
        {
            writer.BeginObject(null);
            writer.UInt("unit", claim.Unit.Value);
            writer.Int("tick", claim.Tick);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadExitClaims(IStateReader state)
    {
        if (!state.Has("exitClaims")) return;
        foreach (var item in state.Array("exitClaims")) _exitClaims.Add(new ExitClaim(new EntityId(checked((uint)item.UInt("unit"))), item.Int("tick")));
    }

    /// <inheritdoc cref="IWorldServices.FindFreeGoal"/>
    public (Fixed X, Fixed Y) FindFreeGoal(Entity mover, Fixed fromX, Fixed fromY, Fixed x, Fixed y) => FindFreeGoal(mover, fromX, fromY, x, y, false);

    /// <summary>
    /// The goal of a unit that leaves its factory. When idle units of the same player stand on the exit point they are asked to make room first, the way the reference moves
    /// idle allies off a path (<c>Pathfinder::moveAllies</c>, <c>AIUpdateInterface::aiMoveAwayFromUnit</c>): each steps to the nearest free place beside the exit point, so the
    /// door stays clear and the new unit takes the exit point itself. When they cannot be moved (busy, somebody else's) the new unit takes a free place beside them instead.
    /// </summary>
    private (Fixed X, Fixed Y) ClaimExitPoint(Entity unit, Fixed doorX, Fixed doorY, Fixed exitX, Fixed exitY)
    {
        if (GoalIsFree(unit, exitX, exitY)) return (exitX, exitY);
        var radius = CircleRadius(unit);
        foreach (var other in _mobiles)
        {
            if (other.Id == unit.Id || other.Owner != unit.Owner || other.HasMoveTarget || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid) continue;
            if (AppearsAfterClaim(other)) continue;
            var limit = radius + CircleRadius(other);
            // Where it stands once the logic frame of the original it is in is over (a unit whose move has ended covers the rest of its last step above 30 Hz; at 30 Hz its position).
            var (standsX, standsY, _) = other.FrameEnd(_tick);
            var ox = standsX - exitX;
            var oy = standsY - exitY;
            if (FurtherThan(standsX, exitX, limit) || FurtherThan(standsY, exitY, limit) || !SquaredReach.Of(limit).Contains(ox * ox + oy * oy)) continue;
            if (AiOf(other) is not { IsIdle: true } ai) continue;
            var (asideX, asideY) = FindFreeGoal(other, doorX, doorY, exitX, exitY, true);
            if (asideX != exitX || asideY != exitY) ai.OrderMove(this, asideX, asideY, false);
        }
        return FindFreeGoal(unit, unit.X, unit.Y, exitX, exitY, false);
    }

    /// <summary>Own value: the rows tried for a unit already on its way to a taken goal (83 places): an army gathers on one point, a factory's door sees a few units.</summary>
    private const int GatherGoalRows = 12;

    /// <inheritdoc cref="Modules.IWorldServices.TryFreeGoalSpot"/>
    /// <remarks>
    /// Observation C of docs/AI-OBSERVED-2026-10-08.md: units sent to one point walk circles around the first one that arrived, because the last waypoint lies inside its
    /// bounding circle. The reference gives every moving unit a goal cell of its own (<c>AIInternalMoveToState::onEnter</c>, <c>Pathfinder::adjustDestination</c>,
    /// <c>checkDestination</c>: "Don't usurp your allies goals", "Don't move to an occupied cell", then <c>updateGoal</c>). Here there is no reservation table: a standing unit
    /// claims the place it stands on, a moving one the place it is heading for (<see cref="Entity.MoveTargetX"/>), and of several units heading for one place all but the
    /// lowest id give way. The search itself is the one the exit path uses.
    /// </remarks>
    public bool TryFreeGoalSpot(Entity mover, out Fixed x, out Fixed y)
    {
        x = mover.MoveTargetX;
        y = mover.MoveTargetY;
        if (Navigation is null || mover.Template.Locomotor is not { } locomotor || (locomotor.Surfaces & CnCNew.Simulation.Templates.LocomotorSurfaces.Air) != 0) return false;
        // A unit closing in on its victim aims at the victim; where it stops is the weapon's business.
        if (mover.Acquisition is { Victim.IsValid: true }) return false;
        var radius = CircleRadius(mover);
        var taken = false;
        foreach (var other in _mobiles)
        {
            if (other.Id == mover.Id || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid) continue;
            // The mover's own goal is taken by whoever stands on it and, among those heading for it, by a lower id only: of several units sent to one point each but the
            // lowest gives way once, to a place nobody is heading for (GoalIsFree counts everybody's goal), and stays with it.
            if (other.HasMoveTarget && other.Id.Value > mover.Id.Value) continue;
            var (standsX, standsY, _) = other.FrameEnd(_tick);
            var otherX = other.HasMoveTarget ? other.MoveTargetX : standsX;
            var otherY = other.HasMoveTarget ? other.MoveTargetY : standsY;
            var limit = radius + CircleRadius(other);
            if (FurtherThan(otherX, x, limit) || FurtherThan(otherY, y, limit)) continue;
            var ox = otherX - x;
            var oy = otherY - y;
            if (!SquaredReach.Of(limit).ContainsStrictly(ox * ox + oy * oy)) continue;
            // Units of an enemy never claim (a tank ordered onto enemy infantry drives on), nor does anything in the air.
            if ((other.Template.Locomotor!.Surfaces & CnCNew.Simulation.Templates.LocomotorSurfaces.Air) != 0 || AreEnemies(mover, other)) continue;
            taken = true;
            break;
        }
        if (!taken) return false;
        var (freeX, freeY) = FindFreeGoal(mover, mover.X, mover.Y, x, y, false, GatherGoalRows);
        if (freeX == x && freeY == y) return false;
        x = freeX;
        y = freeY;
        return true;
    }

    private (Fixed X, Fixed Y) FindFreeGoal(Entity mover, Fixed fromX, Fixed fromY, Fixed x, Fixed y, bool besideOnly, int rows = FreeGoalRows)
    {
        var dx = x - fromX;
        var dy = y - fromY;
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        if (length == Fixed.Zero || (!besideOnly && GoalIsFree(mover, x, y))) return (x, y);
        // The places lie in rows across the direction of travel: beside the goal first (a unit standing on the goal is then passed, not run into), further rows behind it.
        dx /= length;
        dy /= length;
        var radius = CircleRadius(mover);
        var spacing = Fixed.Max(Fixed.FromInt(TerrainMap.CellSize), radius * Fixed.FromInt(2) + StandingBlockerMargin);
        for (var row = 0; row < rows; row++)
        {
            var rowX = x + dx * spacing * Fixed.FromInt(row);
            var rowY = y + dy * spacing * Fixed.FromInt(row);
            for (var step = row == 0 ? 1 : 0; step <= 2 * FreeGoalColumnsEitherSide; step++)
            {
                // 0, +1, -1, +2, -2, ... places to the left of the direction of travel.
                var column = step % 2 == 1 ? (step + 1) / 2 : -(step / 2);
                var candidateX = rowX - dy * spacing * Fixed.FromInt(column);
                var candidateY = rowY + dx * spacing * Fixed.FromInt(column);
                if (GoalIsFree(mover, candidateX, candidateY)) return (candidateX, candidateY);
            }
        }
        return (x, y);
    }

    /// <summary>
    /// Whether <paramref name="mover"/> could stand at the point: its cell can be entered with the mover's locomotor and is not covered by a structure, and no other mobile ground
    /// object stands there or is on its way there (its bounding circle around its position, or around its move target while it has one, does not reach the mover's).
    /// </summary>
    private bool GoalIsFree(Entity mover, Fixed x, Fixed y)
    {
        if (Navigation is { } navigation && mover.Template.Locomotor is { } locomotor)
        {
            var cellX = TerrainMap.CellOf(x);
            var cellY = TerrainMap.CellOf(y);
            if (!navigation.IsPassable(cellX, cellY, locomotor.Surfaces) || navigation.IsBlockedByObstacle(cellX, cellY)) return false;
        }
        var radius = CircleRadius(mover);
        foreach (var other in _mobiles)
        {
            if (other.Id == mover.Id || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid) continue;
            if (AppearsAfterClaim(other)) continue;
            // A standing unit counts where it stands once its logic frame of the original is over: above 30 Hz a move ends with the first tick of its last frame and the unit
            // covers the rest of the step afterwards (docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md). At 30 Hz that is its position.
            var (standsX, standsY, _) = other.FrameEnd(_tick);
            // The unit whose claim is being decided has no order yet where the 30 Hz spawn phase decides it (the exit order follows the claim): whoever makes room for it
            // sees it standing where it appeared, not heading for the exit point. Never the case at 30 Hz.
            var heading = other.HasMoveTarget && !ReferenceEquals(other, _claiming);
            var otherX = heading ? other.MoveTargetX : standsX;
            var otherY = heading ? other.MoveTargetY : standsY;
            var limit = radius + CircleRadius(other);
            if (FurtherThan(otherX, x, limit) || FurtherThan(otherY, y, limit)) continue;
            var ox = otherX - x;
            var oy = otherY - y;
            if (SquaredReach.Of(limit).Contains(ox * ox + oy * oy)) return false;
        }
        return true;
    }

    /// <summary><c>MAX_CELLS_TO_TRY</c> of <c>Pathfinder::adjustToPossibleDestination</c> and <c>adjustDestination</c> (AIPathfind.cpp).</summary>
    private const int MaxCellsToTry = 400;

    /// <inheritdoc cref="Modules.IWorldServices.FindPossibleDestination"/>
    public bool FindPossibleDestination(Entity mover, ref Fixed x, ref Fixed y)
    {
        if (GoalIsFree(mover, x, y)) return true;
        var i = TerrainMap.CellOf(x);
        var j = TerrainMap.CellOf(y);
        var limit = MaxCellsToTry;
        var delta = 1;
        while (limit > 0)
        {
            // One round of the reference's spiral: delta cells in +x, delta in +y, then delta + 1 in -x and in -y.
            for (var leg = 0; leg < 4; leg++)
            {
                for (var count = delta; count > 0; count--)
                {
                    switch (leg)
                    {
                        case 0: i++; break;
                        case 1: j++; break;
                        case 2: i--; break;
                        default: j--; break;
                    }
                    limit--;
                    var cellX = TerrainMap.CellCenter(i);
                    var cellY = TerrainMap.CellCenter(j);
                    if (!GoalIsFree(mover, cellX, cellY)) continue;
                    x = cellX;
                    y = cellY;
                    return true;
                }
                if (leg % 2 == 1) delta++;
            }
        }
        return false;
    }
}
