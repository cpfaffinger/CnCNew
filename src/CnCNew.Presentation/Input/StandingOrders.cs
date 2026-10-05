using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

public enum StandingOrderKind
{
    /// <summary><c>groupAttackObject</c> / <c>groupForceAttackObject</c>: chase and fire until the target is gone.</summary>
    Attack,
    /// <summary><c>groupAttackMoveToPosition</c>: the simulation's attack-move (<see cref="AttackMoveCommand"/>); the entry only says that the unit is on one, until it arrives.</summary>
    AttackMove,
    /// <summary><c>groupGuardPosition</c> without pursuit: walk to the point; standing there the unit picks its victims itself (the simulation's idle target acquisition).</summary>
    Guard,
    /// <summary><c>MSG_ADD_WAYPOINT</c>: the next point is ordered when the previous one is reached.</summary>
    Waypoints,
}

public sealed record StandingOrder(StandingOrderKind Kind, EntityId Target, Vector2 Destination, ImmutableArray<Vector2> Waypoints, long IssuedTick);

/// <summary>
/// The simulation's commands are single steps (move to a point, fire one shot, dock); the original's AI states (attack an object, attack-move,
/// waypoint paths) span many ticks. This book keeps those multi-tick orders on the client and re-issues the single-step commands each
/// tick. Choosing victims is not its business: an idle unit, a unit on an attack-move and the turret of a marching unit acquire their targets in the
/// simulation (docs/SIMULATION-CORE.md §33); only the attack on a victim the player named is driven from here. Every re-issued command still goes through the sink,
/// so the replay contains it. Orders are processed in ascending unit id.
/// </summary>
public sealed class StandingOrders
{
    /// <summary>A chased target that moved more than this (metres) from the last ordered point gets a fresh move order.</summary>
    private const float ChaseRepathDistance = 10f;

    private readonly SortedDictionary<uint, StandingOrder> _orders = [];
    private readonly Dictionary<uint, Vector2> _chaseGoal = [];

    /// <summary>Units that were told, while reloading inside weapon range, to attack their victim from where they stand (so the order is not repeated every tick).</summary>
    private readonly Dictionary<uint, EntityId> _standing = [];

    /// <summary>The point an attacking aircraft flies out to after it overflew its target: it cannot stop to turn, so it leaves far enough to turn around and come in for the next pass.</summary>
    private readonly Dictionary<uint, Vector2> _egress = [];

    public int Count => _orders.Count;

    public bool TryGet(EntityId unit, out StandingOrder order) => _orders.TryGetValue(unit.Value, out order!);

    public ImmutableArray<(EntityId Unit, StandingOrder Order)> All => [.._orders.Select(pair => (new EntityId(pair.Key), pair.Value))];

    public void Set(EntityId unit, StandingOrder order)
    {
        _orders[unit.Value] = order;
        _chaseGoal.Remove(unit.Value);
        _egress.Remove(unit.Value);
        _standing.Remove(unit.Value);
    }

    public void Clear(EntityId unit)
    {
        _orders.Remove(unit.Value);
        _chaseGoal.Remove(unit.Value);
        _egress.Remove(unit.Value);
        _standing.Remove(unit.Value);
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
                    // The simulation marches, fights and marches on; the entry ends with the attack-move.
                    if (settled && !world.IsAttackMoving(unit)) Clear(unit.Id);
                    break;
                case StandingOrderKind.Guard:
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
        // An aircraft cannot stop in the air: it keeps flying at the target and fires whenever the weapon may (inside its range, past its minimum range, aimed), as an attack run.
        var flying = unit.Template.IsKindOf("AIRCRAFT");
        if (flying && IsOutOfAirfieldAmmunition(world, unit))
        {
            // Nothing left to shoot until the airfield reloads it: the attack is over and the jet's own AI flies home (JetAIUpdate.IdleLogic) once it is idle, which a move to its own position makes it.
            Clear(unit.Id);
            issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, unit.X, unit.Y));
            return;
        }
        if (SimulationQueries.InRange(world, unit, target))
        {
            // The chase ends where the unit stands; it is still attacking (its turrets stay on the victim while the weapon reloads), so the stop is a pursuit of its own position.
            if (!flying && _chaseGoal.Remove(unit.Id.Value) && unit.HasMoveTarget)
                issue((sequence, tick) => new PursueCommand(player, sequence, tick, unit.Id, target.Id, unit.X, unit.Y));
            if (SimulationQueries.CanFireAt(world, unit, target))
                issue((sequence, tick) => new FireWeaponCommand(player, sequence, tick, unit.Id, target.Id));
            // In reach but not ready to shoot (reloading): the attack has begun all the same, so a turret that has no victim yet takes it now instead of waiting for the next shot.
            else if (!flying && world.HasAimingTurret(unit) && world.AttackTargetOf(unit) != target.Id && !(_standing.TryGetValue(unit.Id.Value, out var told) && told == target.Id))
            {
                _standing[unit.Id.Value] = target.Id;
                issue((sequence, tick) => new PursueCommand(player, sequence, tick, unit.Id, target.Id, unit.X, unit.Y));
            }
            if (!flying) return;
        }
        if (!SimulationQueries.CanMove(unit)) return;
        if (flying && unit.Template.Locomotor is { IsWings: true } && AttackRunEgress(world, player, unit, target, issue)) return;
        var goal = SimulationQueries.GroundOf(target);
        if (_chaseGoal.TryGetValue(unit.Id.Value, out var last) && unit.HasMoveTarget && Vector2.Distance(last, goal) <= ChaseRepathDistance) return;
        _chaseGoal[unit.Id.Value] = goal;
        // The approach of an attack (AIAttackApproachTargetState): the turrets track the victim on the way; aircraft have none and just fly.
        if (flying) issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, target.X, target.Y));
        else issue((sequence, tick) => new PursueCommand(player, sequence, tick, unit.Id, target.Id, target.X, target.Y));
    }

    /// <summary>A jet whose weapons that reload only at an airfield are all empty (<see cref="JetAIUpdate.IsOutOfSpecialReloadAmmo"/>).</summary>
    private static bool IsOutOfAirfieldAmmunition(SimulationWorld world, Entity unit)
        => unit.Modules.OfType<JetAIUpdate>().FirstOrDefault() is { } jet && jet.IsOutOfSpecialReloadAmmo(world);

    /// <summary>
    /// The turn-around of an attack run (a fighter flies over its target and cannot stop): once the aircraft is past the target and still within weapon range it is sent straight ahead
    /// for one weapon range (<see cref="_egress"/>); only when it is that far out does the order chase the target again, so the next pass starts outside the weapon's minimum range and
    /// the aircraft has turned toward the target before it is in range. True while the aircraft is on the way out (the caller issues no approach).
    /// </summary>
    private bool AttackRunEgress(SimulationWorld world, PlayerId player, Entity unit, Entity target, Func<Func<ulong, long, SimCommand>, SubmitResult> issue)
    {
        var reach = (float)SimulationQueries.WeaponRange(world, unit).ToDecimal();
        if (reach <= 0f) return false;
        var toTarget = SimulationQueries.GroundOf(target) - SimulationQueries.GroundOf(unit);
        var angle = (float)unit.Angle.ToDecimal();
        var heading = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var distance = toTarget.Length();
        if (_egress.ContainsKey(unit.Id.Value))
        {
            if (distance >= reach)
            {
                _egress.Remove(unit.Id.Value);
                _chaseGoal.Remove(unit.Id.Value);
                return false;
            }
            if (unit.HasMoveTarget) return true;
        }
        else if (distance >= reach || Vector2.Dot(heading, toTarget) >= 0f) return false;
        var point = SimulationQueries.GroundOf(unit) + heading * reach;
        _egress[unit.Id.Value] = point;
        _chaseGoal.Remove(unit.Id.Value);
        var (x, y) = (SimulationQueries.ToFixed(point.X), SimulationQueries.ToFixed(point.Y));
        issue((sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, unit.Id, x, y));
        return true;
    }
}
