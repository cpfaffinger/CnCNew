using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary><c>AIUpdateInterface::isAircraftThatAdjustsDestination</c>: a flyer whose locomotor hovers or has wings ("For now, we are only doing HOVER, and WINGS").</summary>
    internal static bool AdjustsAsAircraft(Entity unit)
        => unit.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) != 0 && (locomotor.IsHover || locomotor.IsWings);

    /// <inheritdoc cref="Modules.IWorldServices.AdjustAircraftDestination"/>
    public void AdjustAircraftDestination(Entity mover, ref Fixed x, ref Fixed y)
    {
        // A drone (the Hellfire drone) is left out, a named difference: the original adjusts its moves as well, but here a drone's moves - following its master, ending an
        // attack - do not all fall in the same logic frame of the original at every rate, and what one drone saw of another's goal then differed between the rates.
        if (!AdjustsAsAircraft(mover) || mover.Template.IsKindOf("DRONE")) return;
        // The world frame the tick lies in: a decision a 30 Hz world makes in its step of frame f is made above 30 Hz in one of the ticks of that frame.
        var frame = Time.ToOriginalFrames(_tick);
        Search(mover, frame, ref x, ref y);
        // updateAircraftGoal: the aircraft holds the cell it was given from now on.
        mover.Goal = AircraftGoal.Take(mover.Goal, frame, x, y);
        MarkDirty(mover);
    }

    private void Search(Entity mover, long frame, ref Fixed x, ref Fixed y)
    {
        var (radius, center) = GoalCellRadius(mover);
        // adjustDestination: the cell of the point (a box without a centre cell is anchored half a cell on), then the counter-clockwise spiral around it.
        var half = Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(2);
        var i = TerrainMap.CellOf(center ? x : x + half);
        var j = TerrainMap.CellOf(center ? y : y + half);
        if (TryAircraftCell(mover, frame, i, j, radius, center, ref x, ref y)) return;
        var limit = MaxCellsToTry;
        var segment = 1;
        while (limit > 0)
        {
            for (var leg = 0; leg < 4; leg++)
            {
                for (var count = segment; count > 0; count--)
                {
                    switch (leg)
                    {
                        case 0: i++; break;
                        case 1: j++; break;
                        case 2: i--; break;
                        default: j--; break;
                    }
                    limit--;
                    if (TryAircraftCell(mover, frame, i, j, radius, center, ref x, ref y)) return;
                }
                if (leg % 2 == 1) segment++;
            }
        }
        // No free cell: the point stays as ordered (snapClosestGoalPosition keeps a hovering aircraft's point).
    }

    /// <summary>
    /// <c>checkDestination</c> for an aircraft that adjusts its destination: every cell of its goal box is on the map and no other such aircraft holds it as its goal
    /// (<c>isAircraftGoal</c>, <c>getGoalAircraft</c>); then <c>adjustCoordToCell</c>. Another aircraft's goal is the one it held before <paramref name="frame"/> (<see cref="AircraftGoal"/>).
    /// </summary>
    private bool TryAircraftCell(Entity mover, long frame, int i, int j, int radius, bool center, ref Fixed x, ref Fixed y)
    {
        var above = center ? radius + 1 : radius;
        if (Navigation is { } navigation && (!navigation.Terrain.IsInside(i - radius, j - radius) || !navigation.Terrain.IsInside(i + above - 1, j + above - 1))) return false;
        foreach (var other in _mobiles)
        {
            if (other.Id == mover.Id || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid || !AdjustsAsAircraft(other) || other.Goal is null) continue;
            if (other.Goal?.HeldBefore(frame) is not { } held) continue;
            var (goalX, goalY) = held;
            var (otherRadius, otherCenter) = GoalCellRadius(other);
            var (gi, gj) = GoalCellOf(goalX, goalY, otherCenter);
            var otherAbove = otherCenter ? otherRadius + 1 : otherRadius;
            // Two boxes of cells [lo, hi) overlap.
            if (i - radius < gi + otherAbove && gi - otherRadius < i + above && j - radius < gj + otherAbove && gj - otherRadius < j + above) return false;
        }
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        x = Fixed.FromInt(i) * cell + (center ? cell / Fixed.FromInt(2) : Fixed.Zero);
        y = Fixed.FromInt(j) * cell + (center ? cell / Fixed.FromInt(2) : Fixed.Zero);
        return true;
    }

    /// <summary><c>updateAircraftGoal</c>: the goal cell of a point (a box with a centre cell is the cell the point lies in, one without the nearest corner).</summary>
    private static (int I, int J) GoalCellOf(Fixed x, Fixed y, bool center)
    {
        var half = Fixed.FromInt(TerrainMap.CellSize) / Fixed.FromInt(2);
        return center ? (TerrainMap.CellOf(x), TerrainMap.CellOf(y)) : (TerrainMap.CellOf(x + half), TerrainMap.CellOf(y + half));
    }

    /// <summary>
    /// <c>Pathfinder::getRadiusAndCenter</c>: the half width of an object's box of goal cells from its bounding circle (a diameter between one and two cells counts as two;
    /// at most two cells), and whether the box has a centre cell.
    /// </summary>
    private static (int Radius, bool Center) GoalCellRadius(Entity entity)
    {
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        var diameter = entity.Template.BoundingCircleRadius * Fixed.FromInt(2);
        if (diameter > cell && diameter < cell * Fixed.FromInt(2)) diameter = cell * Fixed.FromInt(2);
        // REAL_TO_INT_FLOOR(diameter / PATHFIND_CELL_SIZE_F + 0.3f)
        var radius = (diameter / cell + Fixed.FromInt(3) / Fixed.FromInt(10)).Floor();
        var cells = checked((int)radius);
        if (cells == 0) cells = 1;
        var center = (cells & 1) != 0;
        cells /= 2;
        if (cells > 2) return (2, true);
        return (cells, center);
    }
}

/// <summary>An aircraft's goal (<see cref="Entity.Goal"/>): the point it holds, the logic frame of the original it took it in, and the point it held before that frame (if any).</summary>
internal sealed record AircraftGoal(Fixed X, Fixed Y, long Frame, bool HadBefore, Fixed BeforeX, Fixed BeforeY)
{
    /// <summary>The goal another aircraft deciding in <paramref name="frame"/> sees: the earlier one when this was taken in that frame, else this one.</summary>
    public (Fixed X, Fixed Y)? HeldBefore(long frame) => Frame != frame ? (X, Y) : HadBefore ? (BeforeX, BeforeY) : null;

    /// <summary>A new goal taken in <paramref name="frame"/>; a second one in the same frame keeps what was held before the frame.</summary>
    public static AircraftGoal Take(AircraftGoal? last, long frame, Fixed x, Fixed y)
    {
        if (last is null) return new(x, y, frame, false, Fixed.Zero, Fixed.Zero);
        return last.Frame == frame ? last with { X = x, Y = y } : new(x, y, frame, true, last.X, last.Y);
    }

    public void Describe(CnCNew.Simulation.State.IStateWriter writer)
    {
        writer.Fixed("aircraftGoalX", X);
        writer.Fixed("aircraftGoalY", Y);
        writer.Int("aircraftGoalFrame", Frame);
        if (!HadBefore) return;
        writer.Fixed("aircraftGoalBeforeX", BeforeX);
        writer.Fixed("aircraftGoalBeforeY", BeforeY);
    }

    public static AircraftGoal? Load(CnCNew.Simulation.State.IStateReader reader)
    {
        if (!reader.Has("aircraftGoalX")) return null;
        var before = reader.Has("aircraftGoalBeforeX");
        return new(reader.Fixed("aircraftGoalX"), reader.Fixed("aircraftGoalY"), reader.Int("aircraftGoalFrame"), before,
            before ? reader.Fixed("aircraftGoalBeforeX") : Fixed.Zero, before ? reader.Fixed("aircraftGoalBeforeY") : Fixed.Zero);
    }
}
