using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// The two actions that weigh enemies by what they cost (F-22): <c>ScriptActions::doSkirmishAttackNearestGroupWithValue</c>, for which the value of the partition cells is computed when it is
/// asked for (the original keeps it up to date, <c>Object::handleValueMap</c>), and <c>ScriptActions::doSkirmishCommandButtonOnMostValuable</c>.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary><c>GlobalData::m_partitionCellSize</c> (<c>PartitionCellSize</c> in GameData.ini, 40): the key is not typed; named here as the constants of AIData.ini are.</summary>
    private static readonly Fixed PartitionCellSize = Fixed.FromInt(40);

    /// <summary>
    /// <c>doSkirmishAttackNearestGroupWithValue</c>: for the comparisons <c>GREATER</c> and <c>GREATER_EQUAL</c> the group attack-moves to the first cell that holds more than the value, walking
    /// out from its centre. The original then moves to an unset place when there is no such cell or another comparison was asked for; here the group stays.
    /// </summary>
    private void AttackNearestGroupWithValue(ScriptTeam team, int comparison, int value)
    {
        var members = LiveMembers(team).ToArray();
        if (members.Length == 0 || comparison is not (ComparisonGreaterEqual or ComparisonGreater)) return;
        var (centreX, centreY) = CentreOf(members);
        if (NearestCellWithValue(team.Owner, centreX, centreY, value) is { } place) MoveGroup(members, place.X, place.Y, attackMove: true);
    }

    /// <summary><c>Parameter::GREATER_EQUAL</c> and <c>Parameter::GREATER</c>.</summary>
    private const int ComparisonGreaterEqual = 3, ComparisonGreater = 4;

    /// <summary><c>AIGroup::getCenter</c>: the mean of the members' places.</summary>
    private static (Fixed X, Fixed Y) CentreOf(Entity[] members)
    {
        var (sumX, sumY) = (Fixed.Zero, Fixed.Zero);
        foreach (var member in members) (sumX, sumY) = (sumX + member.X, sumY + member.Y);
        var count = Fixed.FromInt(members.Length);
        return (sumX / count, sumY / count);
    }

    /// <summary>
    /// <c>PartitionManager::getNearestGroupWithValue</c>: the cells of the enemies' cash value, breadth first from the start cell. <c>greaterThan</c> is set from the value there
    /// (<c>parms.greaterThan = valueRequired</c>), so the value 0 finds nothing. The place is the corner of the cell, as the original gives it.
    /// </summary>
    private (Fixed X, Fixed Y)? NearestCellWithValue(PlayerId player, Fixed x, Fixed y, int required)
    {
        if (required == 0 || _world.Navigation is not { } navigation) return null;
        var cellsX = (navigation.Terrain.ExtentX / PartitionCellSize).Ceiling();
        var cellsY = (navigation.Terrain.ExtentY / PartitionCellSize).Ceiling();
        var values = EnemyCashValues(player, (int)cellsX, (int)cellsY);
        var done = new bool[values.Length];
        var pending = new Queue<int>();
        var currentX = (int)Math.Clamp((x / PartitionCellSize).Floor(), 0, cellsX - 1);
        var currentY = (int)Math.Clamp((y / PartitionCellSize).Floor(), 0, cellsY - 1);
        var start = currentY * (int)cellsX + currentX;
        done[start] = true;
        pending.Enqueue(start);
        // iterateCellsBreadthFirst: the neighbours of the cell looked at last go in first - left, up, right, down -, then the oldest cell is looked at.
        ReadOnlySpan<(int X, int Y)> steps = [(-1, 0), (0, -1), (1, 0), (0, 1)];
        while (pending.Count > 0)
        {
            foreach (var (dx, dy) in steps)
            {
                var (nextX, nextY) = (currentX + dx, currentY + dy);
                if (nextX < 0 || nextY < 0 || nextX >= cellsX || nextY >= cellsY) continue;
                var next = nextY * (int)cellsX + nextX;
                if (done[next]) continue;
                done[next] = true;
                pending.Enqueue(next);
            }
            var cell = pending.Dequeue();
            (currentX, currentY) = (cell % (int)cellsX, cell / (int)cellsX);
            if (values[cell] > required) return (Fixed.FromInt(currentX) * PartitionCellSize, Fixed.FromInt(currentY) * PartitionCellSize);
        }
        return null;
    }

    /// <summary>
    /// <c>Object::addValue</c>, <c>PartitionManager::doValueAffect</c> and <c>hLineAddValue</c> for the players the given one has as enemies: an object out of construction that clears shroud is
    /// worth its build cost in its cell and less the further away, out to its vision. The disc is the one of the plain distance, not of <c>DiscreteCircle</c>.
    /// </summary>
    private long[] EnemyCashValues(PlayerId player, int cellsX, int cellsY)
    {
        var values = new long[cellsX * cellsY];
        foreach (var enemy in _world.Configuration.Players)
        {
            if (!_world.AreEnemies(player, enemy.Id)) continue;
            foreach (var entity in _world.EntitiesOwnedBy(enemy.Id).AsSpan())
            {
                if (entity.EffectivelyDead || entity.DestroyRequested || entity.ContainedBy.IsValid || entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
                if (entity.Template.ShroudClearingRange <= Fixed.Zero) continue;
                var cost = entity.Template.BuildCost;
                var centreX = (entity.X / PartitionCellSize).Floor();
                var centreY = (entity.Y / PartitionCellSize).Floor();
                var radius = Math.Max(1, (entity.Template.VisionRange / PartitionCellSize).Ceiling());
                var falloff = Fixed.FromInt(radius + 1);
                for (var dy = -radius; dy <= radius; dy++)
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var (cellX, cellY) = (centreX + dx, centreY + dy);
                        if (dx * dx + dy * dy > radius * radius || cellX < 0 || cellY < 0 || cellX >= cellsX || cellY >= cellsY) continue;
                        var share = Fixed.One - Fixed.Sqrt(Fixed.FromInt(dx * dx + dy * dy)) / falloff;
                        if (share > Fixed.Zero) values[cellY * cellsX + cellX] += (cost * share).Floor();
                    }
            }
        }
        return values;
    }

    /// <summary>
    /// <c>doSkirmishCommandButtonOnMostValuable</c>: the enemy objects within the range of the group's centre on which the button may be used, the most expensive first
    /// (<c>ITER_SORTED_EXPENSIVE_TO_CHEAP</c>); the group uses it on the first of them. Whether the button may be used is the world's answer for the first carrier, on the eight most
    /// expensive candidates, as for <see cref="UseOnNearest"/>.
    /// </summary>
    private void CommandButtonOnMostValuable(ScriptTeam team, string ability, Fixed range)
    {
        var members = LiveMembers(team).ToArray();
        var carriers = Carriers(members, ability).ToArray();
        if (carriers.Length == 0) return;
        var (centreX, centreY) = CentreOf(members);
        var limit = SquaredReach.Of(range);
        var candidates = _world.Entities
            .Where(candidate => CountsAsObject(candidate) && !IsHiddenByStealth(candidate) && _world.RelationshipOf(team.Owner, candidate.Owner) == Relationship.Enemies && limit.Contains(Distance2(candidate, centreX, centreY)))
            .OrderByDescending(candidate => _world.ProductionCostOf(candidate.Owner, candidate.Template)).ThenBy(candidate => candidate.Id.Value).Take(8).ToArray();
        foreach (var target in candidates)
        {
            if (!UseCommandButtonAbility(carriers[0], ability, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero)) continue;
            foreach (var other in carriers.Skip(1)) UseCommandButtonAbility(other, ability, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero);
            return;
        }
    }
}
