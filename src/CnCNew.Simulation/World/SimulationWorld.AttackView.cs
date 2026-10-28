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
        if (IsContactWeapon(weapon)) return false;
        // isDoingGroundMovement, IMMOBILE, "the brutal special case for stinger soldiers"; a rider of a structure or of a container that is no airborne target is on the ground (F-60).
        var container = FiringContainerOf(attacker);
        var onGround = container is not null
            ? container.Template.IsKindOf("STRUCTURE") || !IsSignificantlyAboveTerrainAtFrameEnd(container)
            : (attacker.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) == 0)
              || attacker.Template.IsKindOf("IMMOBILE") || attacker.Template.IsKindOf("SPAWNS_ARE_THE_WEAPONS");
        if (!onGround || IsSignificantlyAboveTerrainAtFrameEnd(victim)) return false;
        return IsAttackViewBlocked(attacker, victim);
    }

    private static readonly Fixed ContactRangeFudge = Fixed.FromDecimal(1.05m);

    /// <summary><c>WeaponTemplate::isContactWeapon</c>: "fudge this a little to account for pathfinding roundoff & such" - a range a twentieth longer still short of a cell.</summary>
    private static bool IsContactWeapon(WeaponInfo weapon) => weapon.AttackRange * ContactRangeFudge < Fixed.FromInt(global::CnCNew.Simulation.Terrain.TerrainMap.CellSize);

    /// <summary><c>Object::getCurrentWeapon</c>: the slot last chosen or locked in the active set, else its primary.</summary>
    private WeaponInfo? CurrentWeapon(Entity entity)
    {
        if (ActiveWeaponSet(entity) is not { } set) return null;
        var slot = entity.WeaponSlots is { } slots && slots.SetIndex == set.Index ? slots.Current : WeaponSlot.Primary;
        return set.HasWeapon(slot) ? _templates.Weapons[set.Weapons[slot]] : null;
    }

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
        // A contained rider looks out from where it stands (ShooterPosition, F-60).
        var (attackerX, attackerY, attackerZ) = ShooterPosition(attacker, frameEnd: true);
        return IsAttackViewBlockedFrom(attacker, attackerX, attackerY, attackerZ, victim);
    }

    /// <summary><see cref="IsAttackViewBlocked"/> with the attacker standing at (<paramref name="attackerX"/>, <paramref name="attackerY"/>, <paramref name="attackerZ"/>).</summary>
    private bool IsAttackViewBlockedFrom(Entity attacker, Fixed attackerX, Fixed attackerY, Fixed attackerZ, Entity victim)
    {
        if (!AttackUsesLineOfSight || Navigation is null || Terrain is null || !attacker.Template.IsKindOf(NeedsLineOfSightKind)) return false;
        // The rule fix contact-weapon-not-view-blocked (the community source): an attacker with a current weapon (none for an IMMOBILE one) that is a contact weapon is never blocked.
        if (_templates.Rules.Has(Rules.GameFix.ContactWeaponNotViewBlocked) && !attacker.Template.IsKindOf("IMMOBILE") && CurrentWeapon(attacker) is { } current && IsContactWeapon(current))
            return false;
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
        var (ownX, ownY, ownZ) = ShooterPosition(entity, frameEnd: true);
        var (otherX, otherY, otherZ) = candidate.FrameEnd(_tick);
        if (!Terrain.IsClearLineOfSight(ownX, ownY, ownZ + MaxHeightAbovePosition(entity.Template), otherX, otherY, otherZ + MaxHeightAbovePosition(candidate.Template))) return true;
        return !IsSignificantlyAboveTerrainAtFrameEnd(candidate) && IsAttackViewBlocked(entity, candidate);
    }

    /// <summary><c>isGoalPosWithinAttackRange</c>: a goal lies a quarter cell inside the weapon's range (and outside its minimum range).</summary>
    private static readonly Fixed GoalRangeMargin = Fixed.FromDecimal(2.5m);

    private static readonly Fixed HalfCell = Fixed.FromInt(global::CnCNew.Simulation.Terrain.TerrainMap.CellSize / 2);

    /// <summary><c>ATTACK_CELL_LIMIT</c>: the cells the search looks at before it gives up.</summary>
    private const int AttackCellLimit = 2500;

    /// <summary><c>COST_ORTHOGONAL</c> and <c>COST_DIAGONAL</c> of the original's search, in world units of a cell of 10.</summary>
    private const long OrthogonalStepCost = 10;
    private const long DiagonalStepCost = 14;

    /// <summary>The priority of a cell in the search: the estimate first, then the cell index (a map has fewer cells than this).</summary>
    private const long CellKeySpan = 33_554_432;

    private int[]? _attackSearchStamp;
    private long[]? _attackSearchCost;
    private int _attackSearchRun;
    private readonly PriorityQueue<int, long> _attackSearchOpen = new();

    /// <summary>
    /// <c>Pathfinder::findAttackPath</c> for a ground unit of the kind <c>ATTACK_NEEDS_LINE_OF_SIGHT</c>: the place it moves to so that it can fire at the victim - first up to nine
    /// points half a cell apart straight toward the victim, then a search outward from the unit toward the victim over at most 2,500 cells; the first that is passable for the
    /// unit, within the weapon's range less a quarter cell (and outside its minimum range plus as much) and has a clear view is the goal. Null when the search finds none:
    /// the unit then walks at the victim and stops where it can fire (the fallback of <c>doPathfind</c>). Not modelled: the test of <c>checkDestination</c> that no other unit
    /// has its goal there, and the vehicles' strip-back past cells other units block.
    /// </summary>
    public (Fixed X, Fixed Y)? FindAttackPosition(Entity unit, Entity victim)
    {
        if (!AttackUsesLineOfSight || Navigation is not { } grid || Terrain is not { } terrain || !unit.Template.IsKindOf(NeedsLineOfSightKind)) return null;
        if (unit.Template.Locomotor is not { } locomotor || (locomotor.Surfaces & LocomotorSurfaces.Air) != 0) return null;
        var choice = ChooseWeapon(unit, victim, FireOrigin.Ai);
        if (choice.Slot < 0 || ActiveWeaponSet(unit) is not { } set || !set.HasWeapon(choice.Slot)) return null;
        var weapon = _templates.Weapons[set.Weapons[choice.Slot]];
        var range = weapon.RangeWith(_templates.GlobalWeaponBonus, unit.WeaponBonusConditions);
        var far = SquaredReach.Of(range - GoalRangeMargin);
        var hasNear = weapon.MinimumAttackRange > Fixed.Zero;
        var near = SquaredReach.Of(weapon.MinimumAttackRange + GoalRangeMargin);
        var (unitX, unitY, _) = unit.FrameEnd(_tick);
        var (victimX, victimY, _) = victim.FrameEnd(_tick);

        bool Fits(Fixed x, Fixed y)
        {
            if (!grid.IsPassable(global::CnCNew.Simulation.Terrain.TerrainMap.CellOf(x), global::CnCNew.Simulation.Terrain.TerrainMap.CellOf(y), locomotor.Surfaces)) return false;
            var dx = victimX - x;
            var dy = victimY - y;
            var distance = dx * dx + dy * dy;
            if (!far.Contains(distance) || (hasNear && near.ContainsStrictly(distance))) return false;
            return !IsAttackViewBlockedFrom(unit, x, y, terrain.HeightAt(x, y), victim);
        }

        var towardX = victimX - unitX;
        var towardY = victimY - unitY;
        var length = Fixed.Sqrt(towardX * towardX + towardY * towardY);
        if (length > Fixed.Zero)
            for (var step = 1; step <= 9; step++)
            {
                var along = HalfCell * Fixed.FromInt(step);
                if (along > length) break;
                var x = unitX + towardX * along / length;
                var y = unitY + towardY * along / length;
                if (Fits(x, y)) return (x, y);
            }

        // The search: the cost so far plus the distance to the victim less half the attack distance (the range and three cells), the cheaper first, then the lower cell index.
        var cellsX = terrain.CellsX;
        var cells = cellsX * terrain.CellsY;
        if (_attackSearchStamp is null || _attackSearchStamp.Length != cells)
        {
            _attackSearchStamp = new int[cells];
            _attackSearchCost = new long[cells];
            _attackSearchRun = 0;
        }
        if (++_attackSearchRun == int.MaxValue)
        {
            Array.Clear(_attackSearchStamp);
            _attackSearchRun = 1;
        }
        var stamp = _attackSearchStamp;
        var cost = _attackSearchCost!;
        var run = _attackSearchRun;
        var halfAttack = (range + Fixed.FromInt(3 * global::CnCNew.Simulation.Terrain.TerrainMap.CellSize)) / Fixed.FromInt(2);
        long Heuristic(int cx, int cy)
        {
            var dx = global::CnCNew.Simulation.Terrain.TerrainMap.CellCenter(cx) - victimX;
            var dy = global::CnCNew.Simulation.Terrain.TerrainMap.CellCenter(cy) - victimY;
            var left = Fixed.Sqrt(dx * dx + dy * dy) - halfAttack;
            return left > Fixed.Zero ? left.Floor() : 0;
        }
        var startX = Math.Clamp(global::CnCNew.Simulation.Terrain.TerrainMap.CellOf(unitX), 0, cellsX - 1);
        var startY = Math.Clamp(global::CnCNew.Simulation.Terrain.TerrainMap.CellOf(unitY), 0, terrain.CellsY - 1);
        var open = _attackSearchOpen;
        open.Clear();
        var start = startY * cellsX + startX;
        stamp[start] = run;
        cost[start] = 0;
        open.Enqueue(start, Key(Heuristic(startX, startY), start));
        var examined = 0;
        while (examined < AttackCellLimit && open.TryDequeue(out var index, out var key))
        {
            var cx = index % cellsX;
            var cy = index / cellsX;
            // A cell queued again at a lower cost leaves its older entry behind.
            if (key != Key(cost[index] + Heuristic(cx, cy), index)) continue;
            examined++;
            var centerX = global::CnCNew.Simulation.Terrain.TerrainMap.CellCenter(cx);
            var centerY = global::CnCNew.Simulation.Terrain.TerrainMap.CellCenter(cy);
            var offX = centerX - unitX;
            var offY = centerY - unitY;
            if (index != start && !SquaredReach.Of(HalfCell).Contains(offX * offX + offY * offY) && Fits(centerX, centerY)) return (centerX, centerY);
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (!grid.IsPassable(nx, ny, locomotor.Surfaces)) continue;
                    // A diagonal step only past two open corners.
                    if (dx != 0 && dy != 0 && (!grid.IsPassable(cx + dx, cy, locomotor.Surfaces) || !grid.IsPassable(cx, cy + dy, locomotor.Surfaces))) continue;
                    var next = ny * cellsX + nx;
                    var through = cost[index] + (dx != 0 && dy != 0 ? DiagonalStepCost : OrthogonalStepCost);
                    if (stamp[next] == run && cost[next] <= through) continue;
                    stamp[next] = run;
                    cost[next] = through;
                    open.Enqueue(next, Key(through + Heuristic(nx, ny), next));
                }
        }
        return null;

        static long Key(long estimate, int cell) => estimate * CellKeySpan + cell;
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
