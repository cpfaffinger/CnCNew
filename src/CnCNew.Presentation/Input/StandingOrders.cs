using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

public enum StandingOrderKind
{
    /// <summary><c>groupAttackObject</c> / <c>groupForceAttackObject</c>: chase and fire until the target is gone.</summary>
    Attack,
    /// <summary><c>groupAttackMoveToPosition</c>: walk to the point, firing at enemies in weapon range on the way.</summary>
    AttackMove,
    /// <summary><c>groupGuardPosition</c> without pursuit: walk to the point and fire at enemies in weapon range there.</summary>
    Guard,
    /// <summary><c>MSG_ADD_WAYPOINT</c>: the next point is ordered when the previous one is reached.</summary>
    Waypoints,
}

public sealed record StandingOrder(StandingOrderKind Kind, EntityId Target, Vector2 Destination, ImmutableArray<Vector2> Waypoints, long IssuedTick);

/// <summary>
/// The simulation's commands are single steps (move to a point, fire one shot, dock); the original's AI states (attack an object, attack-move,
/// guard, waypoint paths) span many ticks. This book keeps those multi-tick orders on the client and re-issues the single-step commands each
/// tick, exactly as <see cref="Simulation.Skirmish.SkirmishAi"/> does for the computer players. Every re-issued command still goes through the sink,
/// so the replay contains it. Orders are processed in ascending unit id.
/// </summary>
public sealed class StandingOrders
{
    /// <summary>A chased target that moved more than this (metres) from the last ordered point gets a fresh move order.</summary>
    private const float ChaseRepathDistance = 10f;

    private readonly SortedDictionary<uint, StandingOrder> _orders = [];
    private readonly Dictionary<uint, Vector2> _chaseGoal = [];

    public int Count => _orders.Count;

    public bool TryGet(EntityId unit, out StandingOrder order) => _orders.TryGetValue(unit.Value, out order!);

    public ImmutableArray<(EntityId Unit, StandingOrder Order)> All => [.._orders.Select(pair => (new EntityId(pair.Key), pair.Value))];

    public void Set(EntityId unit, StandingOrder order)
    {
        _orders[unit.Value] = order;
        _chaseGoal.Remove(unit.Value);
    }

    public void Clear(EntityId unit)
    {
        _orders.Remove(unit.Value);
        _chaseGoal.Remove(unit.Value);
    }

    /// <summary>Appends a waypoint; a unit without a waypoint order starts one (the first point is ordered by the caller).</summary>
    public void AddWaypoint(EntityId unit, Vector2 point, long tick)
    {
        if (_orders.TryGetValue(unit.Value, out var order) && order.Kind == StandingOrderKind.Waypoints)
            _orders[unit.Value] = order with { Waypoints = order.Waypoints.Add(point) };
        else Set(unit, new StandingOrder(StandingOrderKind.Waypoints, EntityId.Invalid, point, [], tick));
    }

    public void Update(SimulationWorld world, PlayerId player, Func<Func<ulong, long, SimCommand>, SubmitResult> issue)
    {
        foreach (var (id, order) in _orders.ToArray())
        {
            var unit = world.FindEntity(new EntityId(id));
            if (unit is null || !SimulationQueries.IsAlive(unit) || unit.Owner != player)
            {
                Clear(new EntityId(id));
                continue;
            }
            // The order's own first move executes in the tick it was issued; before that the unit cannot show a move target yet.
            var settled = world.Tick > order.IssuedTick + 1;
            switch (order.Kind)
            {
                case StandingOrderKind.Attack:
                    UpdateAttack(world, player, unit, order, issue);
                    break;
                case StandingOrderKind.AttackMove:
                    FireAtNearestEnemy(world, player, unit, issue);
                    if (settled && !unit.HasMoveTarget) Clear(unit.Id);
                    break;
                case StandingOrderKind.Guard:
                    FireAtNearestEnemy(world, player, unit, issue);
                    break;
                case StandingOrderKind.Waypoints:
                    if (!settled || unit.HasMoveTarget) break;
                    if (order.Waypoints.IsEmpty) { Clear(unit.Id); break; }
                    var next = order.Waypoints[0];
                    _orders[id] = order with { Destination = next, Waypoints = order.Waypoints.RemoveAt(0), IssuedTick = world.Tick };
                    var (x, y) = (SimulationQueries.ToFixed(next.X), SimulationQueries.ToFixed(next.Y));
                    issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, x, y));
                    break;
            }
        }
    }

    private void UpdateAttack(SimulationWorld world, PlayerId player, Entity unit, StandingOrder order, Func<Func<ulong, long, SimCommand>, SubmitResult> issue)
    {
        var target = world.FindEntity(order.Target);
        if (target is null || !SimulationQueries.IsAlive(target) || target.Body is null || !world.CanPlayerSee(player, target))
        {
            Clear(unit.Id);
            return;
        }
        if (SimulationQueries.InRange(world, unit, target))
        {
            if (_chaseGoal.Remove(unit.Id.Value) && unit.HasMoveTarget)
                issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, unit.X, unit.Y));
            if (SimulationQueries.CanFireAt(world, unit, target))
                issue((sequence, tick) => new FireWeaponCommand(player, sequence, tick, unit.Id, target.Id));
            return;
        }
        if (!SimulationQueries.CanMove(unit)) return;
        var goal = SimulationQueries.GroundOf(target);
        if (_chaseGoal.TryGetValue(unit.Id.Value, out var last) && unit.HasMoveTarget && Vector2.Distance(last, goal) <= ChaseRepathDistance) return;
        _chaseGoal[unit.Id.Value] = goal;
        issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, target.X, target.Y));
    }

    /// <summary>The nearest visible enemy in weapon range (ties: lower id), fired at when the weapon is ready.</summary>
    private static void FireAtNearestEnemy(SimulationWorld world, PlayerId player, Entity unit, Func<Func<ulong, long, SimCommand>, SubmitResult> issue)
    {
        if (SimulationQueries.ActiveWeapon(unit) < 0 || world.Tick < unit.WeaponReadyTick) return;
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var candidate in world.Entities)
        {
            if (!world.AreEnemies(player, candidate.Owner) || !SimulationQueries.IsAlive(candidate) || candidate.Body is null) continue;
            if (!SimulationQueries.CanFireAt(world, unit, candidate)) continue;
            var dx = candidate.X - unit.X;
            var dy = candidate.Y - unit.Y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance == bestDistance && candidate.Id.Value < best!.Id.Value))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best is not null) issue((sequence, tick) => new FireWeaponCommand(player, sequence, tick, unit.Id, best.Id));
    }
}
