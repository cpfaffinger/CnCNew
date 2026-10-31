using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Where a skirmish AI aims a superweapon: <c>ScriptActions::doSkirmishFireSpecialPowerAtMostCost</c> with <c>AISkirmishPlayer::computeSuperweaponTarget</c> and
/// <c>AIPlayer::computeSuperweaponTarget</c>, <c>getPlayerSuperweaponValue</c>, <c>getPlayerStructureBounds</c> and <c>calcClosestConstructionZoneLocation</c>.
/// </summary>
public sealed partial class ScriptEngine
{
    private const string ClusterMines = "SPECIAL_CLUSTER_MINES";
    private const string NukeClusterMines = "NUKE_SPECIAL_CLUSTER_MINES";
    private const string SneakAttack = "SPECIAL_SNEAK_ATTACK";

    /// <summary>
    /// <c>doSkirmishFireSpecialPowerAtMostCost</c>: against the current enemy, one holder that is ready per team of the player fires (a structure is on the default team). A place that comes out
    /// as the map's origin is no place.
    /// </summary>
    private void AiFireAtMostCost(PlayerId player, SpecialPowerInfo info)
    {
        if (SkirmishEnemy() is not { } enemy) return;
        var radius = Fixed.Max(info.RadiusCursorRadius, Fixed.FromInt(50));
        var state = AiOfPlayer(player);
        var fired = new SortedSet<int>();
        foreach (var holder in PlayerEntities(player))
        {
            if (holder.HasStatus(UnderConstructionBit) || SpecialPowerRouting.FindModule(holder, info.Index) is not { } module || !module.IsReady(_world)) continue;
            if (!fired.Add(FirstTeamOf(holder.Id.Value)?.Id ?? -1)) continue;
            if (state is null) continue;
            if (!ComputeSuperweaponTarget(state, info, enemy.Id, radius, out var x, out var y)) continue;
            // calcClosestConstructionZoneLocation works on what the module builds; a module that names nothing leaves the place as it is.
            if (info.Type == SneakAttack && _options.Ai!.Data.FindReferenceObject(holder.Template.Name, info.Name) is { } placed && !ClosestConstructionZone(placed, ref x, ref y)) continue;
            if (x == Fixed.Zero && y == Fixed.Zero) continue;
            FireSpecialPower(holder, info.Name, SpecialPowerTargetKind.Location, EntityId.Invalid, x, y);
        }
    }

    /// <summary>
    /// <c>AISkirmishPlayer::computeSuperweaponTarget</c>: cluster mines go on the player's own entrances, one of <c>Center</c>, <c>Flank</c> and <c>Backdoor</c> drawn from the logic stream, on the
    /// edge of the base towards the waypoint of that path nearest to the base centre. Everything else takes the place of most value on the enemy.
    /// </summary>
    private bool ComputeSuperweaponTarget(AiPlayer state, SpecialPowerInfo power, PlayerId enemy, Fixed weaponRadius, out Fixed x, out Fixed y)
    {
        if (power.Type is not (ClusterMines or NukeClusterMines)) return ComputeTargetOfMostValue(power, enemy, weaponRadius, out x, out y);
        var mode = _world.LogicRandom.Range(0, 2);
        var label = (mode == 1 ? FlankPath : mode == 2 ? BackdoorPath : CenterPath) + (state.Setup.StartIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (goalX, goalY) = (state.BaseX, state.BaseY);
        if (_program.FindPath(label) is { Waypoints.IsDefaultOrEmpty: false } path)
        {
            var best = Fixed.MaxValue;
            foreach (var waypoint in path.Waypoints)
            {
                var (dx, dy) = (waypoint.X - state.BaseX, waypoint.Y - state.BaseY);
                var distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; (goalX, goalY) = (waypoint.X, waypoint.Y); }
            }
        }
        else if (state.Enemy >= 0)
        {
            var (loX, loY, hiX, hiY) = StructureBounds(new PlayerId(state.Enemy));
            (goalX, goalY) = ((loX + hiX) / Fixed.FromInt(2), (loY + hiY) / Fixed.FromInt(2));
        }
        var (offsetX, offsetY) = (goalX - state.BaseX, goalY - state.BaseY);
        var length = Fixed.Sqrt(offsetX * offsetX + offsetY * offsetY);
        // Coord2D::normalize leaves a zero vector alone.
        x = length > Fixed.Zero ? state.BaseX + offsetX / length * state.BaseRadius : state.BaseX;
        y = length > Fixed.Zero ? state.BaseY + offsetY / length * state.BaseRadius : state.BaseY;
        return true;
    }

    /// <summary>
    /// <c>AIPlayer::computeSuperweaponTarget</c>: a grid of at most ten by ten over the enemy's structures, walked in a drawn direction, valued with twice the weapon's radius; then eleven places
    /// around the best one - on its diagonal, as the reference computes y from x - valued with the radius itself, and places of equal value are averaged.
    /// </summary>
    private bool ComputeTargetOfMostValue(SpecialPowerInfo power, PlayerId enemy, Fixed weaponRadius, out Fixed x, out Fixed y)
    {
        (x, y) = (Fixed.Zero, Fixed.Zero);
        var (loX, loY, hiX, hiY) = StructureBounds(enemy);
        if (loX == Fixed.Zero && loY == Fixed.Zero && hiX == Fixed.Zero && hiY == Fixed.Zero && _world.Navigation is { } navigation)
        {
            // "Degenerate bounds because he has no buildings. Don't give up, scan the whole map for the leftovers."
            (hiX, hiY) = (navigation.Terrain.ExtentX, navigation.Terrain.ExtentY);
        }
        var radius = Fixed.Max(weaponRadius, Fixed.One);
        loX += radius;
        hiX -= radius;
        if (hiX < loX) hiX = loX = (hiX + loX) / Fixed.FromInt(2);
        if (hiY < loY) hiY = loY = (hiY + loY) / Fixed.FromInt(2);
        var (width, height) = (hiX - loX, hiY - loY);
        var xCount = Math.Min((width / radius).Ceiling() + 1, 10);
        var yCount = Math.Min((height / radius).Ceiling() + 1, 10);
        var military = power.Type != SneakAttack;
        var (xDelta, yDelta, xStart, yStart) = _world.LogicRandom.Range(1, 4) switch
        {
            1 => (1L, 1L, 0L, 0L),
            2 => (-1L, 1L, xCount, 0L),
            3 => (1L, -1L, 0L, yCount),
            _ => (-1L, -1L, xCount, yCount),
        };
        var found = false;
        var cash = -1L;
        var (bestX, bestY) = (Fixed.Zero, Fixed.Zero);
        var xIndex = xStart;
        for (var column = 0; column < xCount; column++, xIndex += xDelta)
        {
            var yIndex = yStart;
            for (var row = 0; row < yCount; row++, yIndex += yDelta)
            {
                var (placeX, placeY) = (loX + width * Fixed.FromInt(xIndex) / Fixed.FromInt(xCount), loY + height * Fixed.FromInt(yIndex) / Fixed.FromInt(yCount));
                var value = SuperweaponValue(placeX, placeY, enemy, radius * Fixed.FromInt(2), military);
                if (value <= cash) continue;
                cash = value;
                (bestX, bestY) = (placeX, placeY);
                found = true;
            }
        }
        if (!found) return false;

        cash = -1L;
        var count = 0;
        var (veryBestX, veryBestY) = (Fixed.Zero, Fixed.Zero);
        var step = radius / Fixed.FromInt(10);
        for (var column = 0; column < 11; column++)
            for (var row = 0; row < 11; row++)
            {
                var (placeX, placeY) = (bestX + Fixed.FromInt(column - 5) * step, bestY + Fixed.FromInt(column - 5) * step);
                var value = SuperweaponValue(placeX, placeY, enemy, radius, military);
                if (value > cash)
                {
                    cash = value;
                    (veryBestX, veryBestY) = (placeX, placeY);
                    count = 1;
                }
                else if (value == cash)
                {
                    veryBestX += placeX;
                    veryBestY += placeY;
                    count++;
                }
            }
        if (cash <= -1L) return false;
        if (count > 1) (veryBestX, veryBestY) = (veryBestX / Fixed.FromInt(count), veryBestY / Fixed.FromInt(count));
        (x, y) = (veryBestX, veryBestY);
        return true;
    }

    /// <summary>
    /// <c>AIPlayer::getPlayerSuperweaponValue</c>: what the enemy's objects within the radius cost, a full share at the place and half at the edge. A command centre and a superweapon survive any
    /// superweapon and count a tenth; a sneak attack wants them, counts them five times, and shuns the defences and the army with five times their value. Flying aircraft are not worth a shot.
    /// </summary>
    private long SuperweaponValue(Fixed x, Fixed y, PlayerId enemy, Fixed radius, bool includeMilitaryUnits)
    {
        radius = Fixed.Max(radius, Fixed.FromInt(4 * TerrainMap.CellSize));
        var reach = SquaredReach.Of(radius);
        var cash = Fixed.Zero;
        foreach (var entity in _world.EntitiesOwnedBy(enemy).AsSpan())
        {
            if (!CountsAsObject(entity)) continue;
            var template = entity.Template;
            var unwanted = false;
            if (!includeMilitaryUnits)
            {
                if (template.IsKindOf("FS_BASE_DEFENSE") || template.IsKindOf("TECH_BASE_DEFENSE")) unwanted = true;
                else if ((template.IsKindOf("VEHICLE") || template.IsKindOf("INFANTRY")) && !template.IsKindOf("DOZER") && !template.IsKindOf("HARVESTER")) unwanted = true;
            }
            else if (template.IsKindOf("AIRCRAFT") && _world.IsSignificantlyAboveTerrain(entity)) continue;
            var (dx, dy) = (x - entity.X, y - entity.Y);
            var distance = dx * dx + dy * dy;
            if (!reach.ContainsStrictly(distance)) continue;
            var factor = Fixed.One - Fixed.Sqrt(distance) / (radius * Fixed.FromInt(2));
            var value = _world.ProductionCostOf(enemy, template);
            if (template.IsKindOf("COMMANDCENTER")) value = includeMilitaryUnits ? value / Fixed.FromInt(10) : value * Fixed.FromInt(5);
            if (template.IsKindOf("FS_SUPERWEAPON")) value = includeMilitaryUnits ? value / Fixed.FromInt(10) : value * Fixed.FromInt(5);
            if (unwanted) cash -= factor * value * Fixed.FromInt(5);
            else cash += factor * value;
        }
        return cash < Fixed.Zero ? cash.Ceiling() : cash.Floor();
    }

    /// <summary><c>AIPlayer::getPlayerStructureBounds</c>: the box around the player's structures, zero without one.</summary>
    private (Fixed LoX, Fixed LoY, Fixed HiX, Fixed HiY) StructureBounds(PlayerId player)
    {
        Fixed loX = Fixed.Zero, loY = Fixed.Zero, hiX = Fixed.Zero, hiY = Fixed.Zero;
        var first = true;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!CountsAsObject(entity) || !entity.Template.IsKindOf("STRUCTURE")) continue;
            if (first)
            {
                (loX, hiX, loY, hiY) = (entity.X, entity.X, entity.Y, entity.Y);
                first = false;
                continue;
            }
            (loX, hiX) = (Fixed.Min(loX, entity.X), Fixed.Max(hiX, entity.X));
            (loY, hiY) = (Fixed.Min(loY, entity.Y), Fixed.Max(hiY, entity.Y));
        }
        return (loX, loY, hiX, hiY);
    }

    /// <summary>
    /// <c>AIPlayer::calcClosestConstructionZoneLocation</c>: only when objects stand in the way of the tunnel the first legal place around it is looked for. A place that was free from the start
    /// comes out of the reference as no place at all: it sets its answer inside that branch alone, and the power does not fire.
    /// </summary>
    private bool ClosestConstructionZone((string Template, Fixed Angle) placed, ref Fixed x, ref Fixed y)
    {
        var (name, angle) = placed;
        var found = false;
        if (_world.Templates.TryGet(name, out var template) && !_world.IsClearOfObjects(template, x, y, angle))
            found = SearchSquares(Fixed.FromDecimal(2m * SupplyCenterCloseDistance), skirmish: false, (cx, cy) => _world.CheckBuildGround(template, cx, cy, angle) == LegalBuildCode.Ok, ref x, ref y);
        if (!found) (x, y) = (Fixed.Zero, Fixed.Zero);
        return found;
    }
}
