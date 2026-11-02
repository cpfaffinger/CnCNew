using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary>
/// The columns of a group move: <c>AIGroup::friend_computeGroundPath</c>, <c>friend_moveInfantryToPos</c>, <c>friend_moveVehicleToPos</c>. A group that has far to go, or that is large, walks one
/// wide path found for the group: infantry in three lanes that close up to five at the goal, ground vehicles in two lanes that close up to three, each unit along its own lane. Infantry sorts by
/// <c>GroupMovementPriority</c>, vehicles do not (the original's code for them is <c>#if 0</c>). Whatever the columns do not take walks alone, as before.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>AIGroup.cpp</c>: <c>PATH_DIAMETER_IN_CELLS</c>.</summary>
    private const int GroupPathDiameter = 6;

    private static readonly Fixed ColumnCell = Fixed.FromInt(TerrainMap.CellSize);
    private static readonly Fixed InfantryLaneWidth = Fixed.FromDecimal(2.1m) * ColumnCell;
    private static readonly Fixed InfantryGoalSpacing = Fixed.FromDecimal(2.2m) * ColumnCell;
    private static readonly Fixed VehicleLaneWidth = Fixed.FromDecimal(1.5m) * ColumnCell;
    private static readonly Fixed VehicleGoalSpacing = Fixed.FromDecimal(3.2m) * ColumnCell;

    /// <summary><c>friend_moveVehicleToPos</c>: below this many, three lanes at the goal are not worth it.</summary>
    private const int VehiclesForThreeLanes = 5;

    /// <summary><c>friend_moveInfantryToPos</c>: below this many, five lanes at the goal are not worth it.</summary>
    private const int InfantryForFiveLanes = 16;

    /// <summary><c>friend_computeGroundPath</c>: more than these go as a column however close they are.</summary>
    private const int InfantryAlwaysColumn = 6;
    private const int VehiclesAlwaysColumn = 4;

    private sealed class ColumnUnit(Entity unit, Fixed x, Fixed y)
    {
        public Entity Unit { get; } = unit;
        public Fixed X { get; } = x;
        public Fixed Y { get; } = y;
        public Fixed Key { get; set; }
        public int Lane { get; set; }
        public int GoalLane { get; set; }
    }

    private static (Fixed X, Fixed Y) Normalized(Fixed x, Fixed y)
    {
        var length = Fixed.Sqrt(x * x + y * y);
        return length == Fixed.Zero ? (Fixed.Zero, Fixed.Zero) : (x / length, y / length);
    }

    private static Fixed ColumnSquared(Fixed dx, Fixed dy) => dx * dx + dy * dy;

    private static bool Holds(Entity unit) => unit.ContainedBy.IsValid;

    private static bool IsColumnVehicle(Entity unit) => unit.Template.IsKindOf("VEHICLE") && !unit.Template.IsKindOf("AIRCRAFT");

    /// <summary><c>friend_computeGroundPath</c> and the two column moves. The members that went are taken out of <see cref="_groupMembers"/>.</summary>
    private void MoveColumns(GroupMoveCommand order)
    {
        if (Navigation is not { } navigation) return;
        var (fx, fy) = (Fixed.Zero, Fixed.Zero);
        var (minX, minY) = (_groupMembers[0].X, _groupMembers[0].Y);
        var (maxX, maxY) = (minX, minY);
        foreach (var (_, x, y, _) in _groupMembers)
        {
            (fx, fy) = (fx + x, fy + y);
            (minX, maxX) = (Fixed.Min(minX, x), Fixed.Max(maxX, x));
            (minY, maxY) = (Fixed.Min(minY, y), Fixed.Max(maxY, y));
        }
        var count = Fixed.FromInt(_groupMembers.Count);
        var (centreX, centreY) = (fx / count, fy / count);

        var data = _templates.AiData;
        var requires = SquaredReach.Of(data.DistanceRequiresGroup);
        var furthest = SquaredReach.Of(data.DistanceRequiresGroup * Fixed.FromInt(2));
        Fixed? nearest = null;
        var (infantry, vehicles) = (0, 0);
        Entity? central = null;
        var (centralX, centralY) = (Fixed.Zero, Fixed.Zero);
        var closest = Fixed.Zero;
        foreach (var (unit, x, y, _) in _groupMembers)
        {
            if (unit.Template.IsKindOf("INFANTRY")) infantry++;
            else if (IsColumnVehicle(unit)) vehicles++;
            else continue;
            var (dx, dy) = (x - order.X, y - order.Y);
            var squared = dx * dx + dy * dy;
            if (furthest.ContainsStrictly(squared) && (nearest is null || squared < nearest)) nearest = squared;
            (dx, dy) = (x - centreX, y - centreY);
            if (central is null || dx * dx + dy * dy < closest)
            {
                central = unit;
                (centralX, centralY) = (x, y);
                closest = dx * dx + dy * dy;
            }
        }
        if (central is null) return;
        var (spreadX, spreadY) = (maxX - minX, maxY - minY);
        var spread = spreadX * spreadX + spreadY * spreadY;
        var distance = requires.Contains(spread) ? nearest : spread;
        if (distance is { } known && SquaredReach.Of(data.MinDistanceForGroup).ContainsStrictly(known)) return;
        var far = distance is not { } gone || !requires.Contains(gone) || infantry > InfantryAlwaysColumn || vehicles > VehiclesAlwaysColumn;
        if (!far)
        {
            // "See if all units have an unobstructed path to the center."
            far = true;
            foreach (var (unit, x, y, _) in _groupMembers)
                if (unit.Template.IsKindOf("INFANTRY") && unit.Template.Locomotor is { } locomotor && !navigation.StraightWalkClear(x, y, centralX, centralY, locomotor.Surfaces, unit.Layer)) far = false;
        }
        if (!far) return;

        var path = navigation.FindGroundPath(centralX, centralY, central.Layer, order.X, order.Y, LayerForGoal(order.X, order.Y, LocomotorSurfaces.Ground), GroupPathDiameter);
        if (path.IsDefaultOrEmpty) return;
        var movedInfantry = MoveColumn(order, path, centreX, centreY, true);
        var movedVehicles = MoveColumn(order, path, centreX, centreY, false);
        // groupMoveToPosition: what the columns took walks no second time; a cliff jumper's own way goes on (KINDOF_CLIFF_JUMPER).
        _groupMembers.RemoveAll(member => (movedInfantry && member.Unit.Template.IsKindOf("INFANTRY")) || (movedVehicles && IsColumnVehicle(member.Unit) && !member.Unit.Template.IsKindOf("CLIFF_JUMPER")));
    }

    private bool MoveColumn(GroupMoveCommand order, ImmutableArray<PathPoint> path, Fixed centreX, Fixed centreY, bool infantry)
    {
        var nearby = SquaredReach.Of(Fixed.FromInt(GroupPathDiameter * TerrainMap.CellSize));
        bool Far(Fixed x0, Fixed y0, Fixed x1, Fixed y1) => !nearby.Contains(ColumnSquared(x1 - x0, y1 - y0));

        var (startPoint, endPoint) = (path[0], path[^1]);
        var (startNode, endNode) = (-1, -1);
        for (var node = 0; node < path.Length && startNode < 0; node++)
            if (Far(path[node].X, path[node].Y, startPoint.X, startPoint.Y)) startNode = node;
        for (var node = 0; node < path.Length; node++)
            if (Far(path[node].X, path[node].Y, endPoint.X, endPoint.Y)) endNode = node;
        if (!infantry && endNode == 0) endNode = -1;
        if (startNode < 0 || endNode < 0) return false;

        var startVector = Normalized(path[startNode].X - startPoint.X, path[startNode].Y - startPoint.Y);
        var endVector = Normalized(endPoint.X - path[endNode].X, endPoint.Y - path[endNode].Y);

        var units = new List<ColumnUnit>();
        var useEndVector = false;
        foreach (var (unit, x, y, _) in _groupMembers)
        {
            if (Holds(unit) || !(infantry ? unit.Template.IsKindOf("INFANTRY") : IsColumnVehicle(unit))) continue;
            // "Means I did NOT do a column group pathfind, so the nexus will have a far-away goal position for the mobsters to aim at."
            if (infantry && unit.Template.IsKindOf("MOB_NEXUS")) return false;
            units.Add(new ColumnUnit(unit, x, y));
            if (ColumnSquared(x - startPoint.X, y - startPoint.Y) > ColumnSquared(x - endPoint.X, y - endPoint.Y)) useEndVector = true;
        }
        var data = _templates.AiData;
        if (units.Count < (infantry ? data.MinInfantryForGroup : data.MinVehiclesForGroup)) return false;
        var vector = useEndVector ? endVector : startVector;
        var normal = (X: -vector.Y, Y: vector.X);

        foreach (var unit in units) unit.Key = (unit.X - centreX) * normal.X + (unit.Y - centreY) * normal.Y;
        units.Sort(static (a, b) => b.Key != a.Key ? b.Key.CompareTo(a.Key) : a.Unit.Id.Value.CompareTo(b.Unit.Id.Value));
        for (var index = 0; index < units.Count; index++)
        {
            var (unit, count) = (units[index], units.Count);
            if (infantry)
            {
                unit.Lane = Math.Max(1 - index / Math.Max((count + 1) / 3, 1), -1);
                unit.GoalLane = count < InfantryForFiveLanes ? unit.Lane : Math.Max(2 - index / Math.Max((count + 3) / 5, 1), -2);
            }
            else
            {
                unit.Lane = 1 - index / Math.Max((count + 1) / 2, 1);
                if (unit.Lane == 0) unit.Lane = -1;
                unit.GoalLane = count < VehiclesForThreeLanes ? unit.Lane : Math.Max(1 - index / Math.Max((count + 1) / 3, 1), -1);
            }
            var adjust = Fixed.Zero;
            if (infantry) adjust = -Fixed.FromInt(100 * TerrainMap.CellSize) * Fixed.FromInt((int)LocomotorPriority.Front - (int)PriorityOf(unit.Unit));
            unit.Key = adjust + (unit.X - centreX) * vector.X + (unit.Y - centreY) * vector.Y;
        }
        units.Sort(static (a, b) => b.Key != a.Key ? b.Key.CompareTo(a.Key) : a.Unit.Id.Value.CompareTo(b.Unit.Id.Value));
        EvenLanes(units, infantry);

        Span<int> filled = stackalloc int[5];
        foreach (var unit in units)
        {
            var place = filled[unit.GoalLane + 2]++;
            FollowColumn(order, path, unit, place, startNode, infantry, units.Count, endVector);
        }
        return true;
    }

    private static LocomotorPriority PriorityOf(Entity unit) => unit.Template.Locomotor?.MovePriority ?? LocomotorPriority.Middle;

    /// <summary>"Even out columns by priority": lane by lane the emptiest near the one a unit was given. Vehicles pass three times over all of them, as their priority test is <c>#if 0</c>.</summary>
    private static void EvenLanes(List<ColumnUnit> units, bool infantry)
    {
        Span<int> lanes = stackalloc int[3];
        Span<int> goalLanes = stackalloc int[5];
        var goalMiddle = infantry ? 2 : 1;
        var goalCount = goalMiddle * 2 + 1;
        var laneStep = infantry ? 1 : 2;
        for (var group = (int)LocomotorPriority.Front; group >= (int)LocomotorPriority.Back; group--)
            foreach (var unit in units)
            {
                if (infantry && group != (int)PriorityOf(unit.Unit)) continue;
                var fewest = int.MaxValue;
                for (var lane = 0; lane < 3; lane += laneStep) fewest = Math.Min(fewest, lanes[lane]);
                var (nearest, best) = (int.MaxValue, -1);
                for (var lane = 0; lane < 3; lane += laneStep)
                    if (lanes[lane] == fewest && Math.Abs(1 + unit.Lane - lane) < nearest) (nearest, best) = (Math.Abs(1 + unit.Lane - lane), lane);
                if (best >= 0)
                {
                    lanes[best]++;
                    unit.Lane = best - 1;
                }
                fewest = int.MaxValue;
                for (var lane = 0; lane < goalCount; lane++) fewest = Math.Min(fewest, goalLanes[lane]);
                (nearest, best) = (int.MaxValue, -1);
                for (var lane = 0; lane < goalCount; lane++)
                    if (goalLanes[lane] == fewest && Math.Abs(goalMiddle + unit.GoalLane - lane) < nearest) (nearest, best) = (Math.Abs(goalMiddle + unit.GoalLane - lane), lane);
                if (best >= 0)
                {
                    goalLanes[best]++;
                    unit.GoalLane = best - goalMiddle;
                }
                if (units.Count < (infantry ? InfantryForFiveLanes : VehiclesForThreeLanes)) unit.GoalLane = unit.Lane;
            }
    }

    private (Fixed X, Fixed Y) ClampedToColumnMap(Fixed x, Fixed y)
        => Terrain is { } terrain ? (Fixed.Max(ColumnCell, Fixed.Min(x, terrain.ExtentX - ColumnCell)), Fixed.Max(ColumnCell, Fixed.Min(y, terrain.ExtentY - ColumnCell))) : (x, y);

    private void FollowColumn(GroupMoveCommand order, ImmutableArray<PathPoint> path, ColumnUnit column, int place, int startNode, bool infantry, int count, (Fixed X, Fixed Y) endVector)
    {
        var nearby = SquaredReach.Of(Fixed.FromInt(GroupPathDiameter * TerrainMap.CellSize));
        bool Far(PathPoint a, PathPoint b) => !nearby.Contains(ColumnSquared(a.X - b.X, a.Y - b.Y));
        var half = ColumnCell / Fixed.FromInt(2);
        var side = place % 2 == 1 ? half : -half;

        var points = new List<PathPoint>();
        var (previous, node) = (0, startNode);
        var (fromX, fromY) = (column.X, column.Y);
        var laneOffset = (infantry ? InfantryLaneWidth : VehicleLaneWidth) * Fixed.FromInt(column.Lane) + side;
        while (true)
        {
            var next = -1;
            for (var candidate = node + 1; candidate < path.Length && next < 0; candidate++)
                if (Far(path[candidate], path[node])) next = candidate;
            if (next < 0) break;
            var (cornerX, cornerY) = (path[next].X - path[previous].X, path[next].Y - path[previous].Y);
            var corner = Normalized(-cornerY, cornerX);
            var (x, y) = (path[node].X + laneOffset * corner.X, path[node].Y + laneOffset * corner.Y);
            var (curX, curY) = (x - fromX, y - fromY);
            (x, y) = ClampedToColumnMap(x, y);
            // "Make sure that this dest is going in the same direction as the vector."
            if (cornerX * curX + cornerY * curY > Fixed.Zero)
            {
                points.Add(new PathPoint(x, y, path[node].Layer));
                (fromX, fromY) = (x, y);
            }
            node++;
            for (var between = previous + 1; between < path.Length && between != node; between++)
                if (Far(path[between], path[node])) previous = between;
        }

        var normal = (X: -endVector.Y, Y: endVector.X);
        var spacing = infantry ? InfantryGoalSpacing : count < VehiclesForThreeLanes ? VehicleLaneWidth : VehicleGoalSpacing;
        var across = spacing * Fixed.FromInt(column.GoalLane) + (place % 2 == 1 ? ColumnCell : Fixed.Zero);
        var (destX, destY) = (order.X + across * normal.X, order.Y + across * normal.Y);
        if (infantry)
        {
            var back = ColumnCell * Fixed.FromInt((int)PriorityOf(column.Unit) - (int)LocomotorPriority.Front);
            (destX, destY) = (destX + back * endVector.X, destY + back * endVector.Y);
        }
        var behind = spacing * Fixed.FromInt(place);
        (destX, destY) = (destX - behind * endVector.X, destY - behind * endVector.Y);
        while (points.Count > 0 && endVector.X * (destX - points[^1].X) + endVector.Y * (destY - points[^1].Y) <= Fixed.Zero) points.RemoveAt(points.Count - 1);
        (destX, destY) = ClampedToColumnMap(destX, destY);

        _groupPending.Remove(column.Unit.Id.Value);
        FindDestination(column.Unit, ref destX, ref destY, true);
        ReleaseWeaponLock(column.Unit, WeaponLockKind.Temporary);
        points.Add(new PathPoint(destX, destY));
        AiOf(column.Unit)!.OrderFollowColumn(this, [.. points], order.FromPlayer);
    }
}
