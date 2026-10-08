using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// A free place to stop at when the place a unit was sent to is taken (observation C of <c>docs/AI-OBSERVED-2026-10-08.md</c>: units sent to one point orbit the first one
/// that arrived, because the last waypoint lies inside its bounding circle and the local avoidance steers every step around it).
/// <para>
/// The reference gives every moving unit a goal cell of its own: <c>AIInternalMoveToState::onEnter</c> (AIStates.cpp) passes the goal through
/// <c>Pathfinder::adjustDestination</c> (AIPathfind.cpp), which walks a square spiral of cells around the wanted one (right, up, twice left, twice down, three times right …,
/// at most <c>MAX_CELLS_TO_TRY</c> = 400) and takes the first cell <c>checkDestination</c> accepts — passable, not the goal cell of an ally ("Don't usurp your allies goals"),
/// not held by a standing unit it cannot crush — and then reserves it (<c>Pathfinder::updateGoal</c>). Rewritten here without a reservation table: a standing unit claims
/// the place it stands on, a moving unit the place it is heading for, and of several units of friendly sides heading for the same place all but the lowest id give way. The
/// claims are read from the objects (<see cref="Entity.MoveTargetX"/> is state every snapshot already carries), so nothing new is stored.
/// </para>
/// <para>
/// Differences to the reference: the mover asks when it is close to its goal rather than when the order is given (<c>AIUpdateInterface.Move</c>), so a place taken while it
/// was on its way is noticed; the spiral runs around the exact goal point instead of the centre of its cell; a candidate must be in a straight line of passable cells from
/// the goal (the reference asks its zone table whether a path exists); units of an enemy never claim a place (the reference lets a crusher take the cell of a unit it can
/// crush, which is the same for the common case and keeps a tank ordered onto enemy infantry on its way).
/// </para>
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>Pathfinder::adjustDestination</c>: <c>MAX_CELLS_TO_TRY</c>.</summary>
    private const int GoalSpotCellsToTry = 400;

    // The claims around the goal a search is running for: where somebody stands or is heading, with the distance the mover has to keep from that point. Reused, never state.
    private readonly List<(Fixed X, Fixed Y, Fixed Limit)> _goalClaims = [];

    /// <inheritdoc cref="Modules.IWorldServices.TryFreeGoalSpot"/>
    public bool TryFreeGoalSpot(Entity mover, out Fixed x, out Fixed y)
    {
        x = mover.MoveTargetX;
        y = mover.MoveTargetY;
        if (Navigation is not { } navigation || mover.Template.Locomotor is not { } locomotor || (locomotor.Surfaces & LocomotorSurfaces.Air) != 0) return false;
        // A unit closing in on its victim aims at the victim; where it stops is the weapon's business.
        if (mover.Acquisition is { Victim.IsValid: true }) return false;

        var goalX = x;
        var goalY = y;
        var moverRadius = CircleRadius(mover);
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        // The spiral of 400 cells stays within ten cells of the goal; a claim further away than that plus the two widest circles cannot reach any candidate.
        var around = cell * Fixed.FromInt(11) + moverRadius + Fixed.FromRaw(_widestMobile);
        var claims = _goalClaims;
        claims.Clear();
        var mobiles = _mobiles;
        var aboveBaseRate = _firstFrameTicks != TickOrder.Next;
        var goalTaken = false;
        for (var index = 0; index < mobiles.Count; index++)
        {
            var other = mobiles[index];
            if (other.Id == mover.Id || other.EffectivelyDead || other.ContainedBy.IsValid) continue;
            Fixed claimX, claimY;
            var standing = !other.HasMoveTarget ? other.Speed == Fixed.Zero
                // Above 30 Hz a unit covering the last step of its move stands, for the 30 Hz step, on its target already (docs/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3).
                : aboveBaseRate && other.AheadTick + TickOrder.Next >= _tick && AiOf(other) is { IsArrivingWithThisFrame: true };
            if (standing) (claimX, claimY, _) = other.FrameEnd(_tick);
            else if (other.HasMoveTarget) (claimX, claimY) = (other.MoveTargetX, other.MoveTargetY);
            else continue;
            if (FurtherThan(claimX, goalX, around) || FurtherThan(claimY, goalY, around)) continue;
            if ((other.Template.Locomotor!.Surfaces & LocomotorSurfaces.Air) != 0 || AreEnemies(mover, other)) continue;
            var limit = moverRadius + CircleRadius(other);
            claims.Add((claimX, claimY, limit));
            // The mover's own goal is taken by whoever stands on it and, among those heading for it, by a lower id; every other place is taken by anybody's claim. So of several
            // units sent to one point each but the lowest gives way once, to a place nobody is heading for, and stays with it.
            if (goalTaken || (!standing && other.Id.Value > mover.Id.Value) || FurtherThan(claimX, goalX, limit) || FurtherThan(claimY, goalY, limit)) continue;
            var dx = claimX - goalX;
            var dy = claimY - goalY;
            goalTaken = SquaredReach.Of(limit).ContainsStrictly(dx * dx + dy * dy);
        }
        if (!goalTaken) return false;

        // Pathfinder::adjustDestination's walk: one side of a square after the other, each two sides one cell longer.
        var surfaces = locomotor.Surfaces;
        var i = 0;
        var j = 0;
        var left = GoalSpotCellsToTry;
        var side = 1;
        while (left > 0)
        {
            for (var leg = 0; leg < 4; leg++)
            {
                var (stepI, stepJ) = leg switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
                if (leg == 2) side++;
                for (var count = side; count > 0; count--)
                {
                    i += stepI;
                    j += stepJ;
                    left--;
                    var candidateX = goalX + cell * Fixed.FromInt(i);
                    var candidateY = goalY + cell * Fixed.FromInt(j);
                    if (GoalSpotClaimed(candidateX, candidateY) || !GoalSpotInLine(navigation, surfaces, goalX, goalY, i, j)) continue;
                    x = candidateX;
                    y = candidateY;
                    return true;
                }
            }
            side++;
        }
        return false;
    }

    private bool GoalSpotClaimed(Fixed x, Fixed y)
    {
        foreach (var (claimX, claimY, limit) in _goalClaims)
        {
            if (FurtherThan(claimX, x, limit) || FurtherThan(claimY, y, limit)) continue;
            var dx = claimX - x;
            var dy = claimY - y;
            if (SquaredReach.Of(limit).ContainsStrictly(dx * dx + dy * dy)) return true;
        }
        return false;
    }

    /// <summary>The candidate (<paramref name="i"/>, <paramref name="j"/>) cells from the goal and every half cell on the straight line back to the goal lie on cells the mover can stand on.</summary>
    private static bool GoalSpotInLine(NavigationGrid navigation, LocomotorSurfaces surfaces, Fixed goalX, Fixed goalY, int i, int j)
    {
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        var samples = 2 * Math.Max(Math.Abs(i), Math.Abs(j));
        for (var sample = samples; sample >= 1; sample--)
        {
            var x = goalX + cell * Fixed.FromInt(i * sample) / Fixed.FromInt(samples);
            var y = goalY + cell * Fixed.FromInt(j * sample) / Fixed.FromInt(samples);
            if (!navigation.IsPassable(TerrainMap.CellOf(x), TerrainMap.CellOf(y), surfaces)) return false;
        }
        return true;
    }
}
