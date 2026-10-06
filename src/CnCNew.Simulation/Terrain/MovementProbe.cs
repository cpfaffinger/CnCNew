using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Terrain;

/// <summary>The pieces of ground movement a tool can measure through <see cref="IMovementProbe"/>.</summary>
public enum MovementWork
{
    /// <summary><see cref="NavigationGrid.FindPath(Numerics.Fixed, Numerics.Fixed, Numerics.Fixed, Numerics.Fixed, LocomotorSurfaces)"/> (including the variant with temporary blockers).</summary>
    PathSearch,
    /// <summary><c>SimulationWorld.StationaryBlockersAhead</c>.</summary>
    StationaryBlockers,
    /// <summary><c>SimulationWorld.AvoidLocalBlockers</c>.</summary>
    LocalAvoidance,
    /// <summary><c>SimulationWorld.ResolveFootprintCollisions</c> (crush, topple and the collide modules).</summary>
    FootprintCollisions,
}

/// <summary>
/// What one path search did: deterministic counters, the same in every run of the same inputs. A search answered from the grid's memory (<see cref="Cached"/>) reports the counters
/// of the search that produced the answer; it did none of that work itself. A search that was stopped because the goal was proved unreachable (<see cref="ProvedUnreachable"/>,
/// <see cref="NavigationGrid.FindPathToGoal"/>) reports the cells it expanded until then and the cells of the flood from the goal.
/// </summary>
public readonly record struct PathSearchInfo(
    int StartCell, int GoalCell, LocomotorSurfaces Surfaces, PathStatus Status, int Expansions, int Pushes, int EscapeCells, int TemporaryCells, int PathCells, int Waypoints, bool Cached,
    bool ProvedUnreachable = false, int ReverseCells = 0);

/// <summary>
/// A tool's measuring hook for movement and path search, the counterpart of <c>ITickPhaseObserver</c> for what happens inside a movement update. The simulation has no clock: it only
/// reports where a piece of work begins and ends and how much it did; the tool reads the clock. <see cref="NavigationGrid.Probe"/> is <see langword="null"/> unless a tool installs one, is
/// not part of the state (neither hashed nor saved) and must not change anything.
/// </summary>
public interface IMovementProbe
{
    void Started(MovementWork work);
    void Ended(MovementWork work);
    void PathSearched(in PathSearchInfo info);
}
