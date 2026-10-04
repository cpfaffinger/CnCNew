using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>What applying a driver leaf is: something a player could do, a UI signal, or a direct write to the world.</summary>
internal enum LeafKind
{
    /// <summary>A direct write (kill, placement, money, ownership, spawn): declined in commands-only mode.</summary>
    World,

    /// <summary>A command issued through the session like a player's (move, production).</summary>
    Order,

    /// <summary>A UI-level signal (selecting an object) with no effect on the simulation.</summary>
    Signal,
}

/// <summary>
/// The player-equivalent part of the objective driver (commands-only mode, <c>docs/SCRIPT-ENGINE.md</c> §9): where a leaf would write to the world, a player would give orders. A
/// "destroy" leaf sends the viewer's armed mobile units toward the victims (the session's auto-acquire combat fires once they are in range), and a "count" leaf queues production at an
/// own producer. Everything the commands cannot do — money, science, ownership, health, spawns, teleports, removing the viewer's own objects — stays declined and is reported.
/// </summary>
internal sealed partial class ObjectiveDriver
{
    /// <summary>Own armed objects that can walk (the viewer's army).</summary>
    private IEnumerable<Entity> Army()
        => LiveOf(_local).Where(entity => SimulationWorld.ActiveWeaponSet(entity) is { WeaponCount: > 0 } && entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE")
                                          && !entity.ContainedBy.IsValid && ScriptEngine.CountsAsObject(entity));

    /// <summary>Orders the army toward the nearest of <paramref name="victims"/> (units already in weapon range are left to the combat loop). False when no order could be given.</summary>
    private bool Assault(IReadOnlyList<Entity> victims)
    {
        if (victims.Count == 0) return false;
        // Units trickling in one by one die one by one: with an agent the driver waits until its army masses and attacks (the agent sets the mass).
        if (_agent is { Attacking: false }) return false;
        var any = false;
        foreach (var unit in Army().ToArray())
        {
            var range = _world.MaxWeaponRange(unit);
            Entity? nearest = null;
            var best = Fixed.MaxValue;
            foreach (var victim in victims)
            {
                var dx = unit.X - victim.X;
                var dy = unit.Y - victim.Y;
                var distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; nearest = victim; }
            }
            if (nearest is null || best <= range * range) continue;
            any |= Send(unit, (nearest.X, nearest.Y));
        }
        return any;
    }

    /// <summary>
    /// A "destroy these" leaf: in the normal mode the victims are killed (a world write), in commands-only mode the army is sent at them (an order, repeated until they are gone).
    /// </summary>
    private Leaf Destroy(string key, string label, int maxApplications, int cooldown, Func<IReadOnlyList<Entity>> victims)
    {
        if (!_commandsOnly)
            return new Leaf(key, label, maxApplications, cooldown, _ =>
            {
                var list = victims();
                if (list.Count == 0) return false;
                foreach (var victim in list) _world.Kill(victim.Id);
                return true;
            });
        return new Leaf(key, label.Replace("destroy", "attack", StringComparison.Ordinal), 100000, 90, _ => Assault(victims())) { Kind = LeafKind.Order };
    }

    /// <summary>Queues one object of the template at an own producer that may build it (false when there is none).</summary>
    private bool Produce(PlayerId player, string template)
    {
        foreach (var producer in LiveOf(player))
        {
            if (!_world.CanQueueProductionTemplate(player, producer.Id, template)) continue;
            var id = producer.Id;
            if (_session.Issue(player, (sequence, tick) => new QueueProductionCommand(player, sequence, tick, id, template)).Accepted) { Commands++; return true; }
        }
        return false;
    }
}

internal sealed partial class ObjectiveDriver
{
    private CnCNew.Simulation.Skirmish.SkirmishAi? _agent;
    private readonly int _agentThreshold;

    /// <summary>The skirmish AI's statistics when the commands-only agent plays for the viewer.</summary>
    public CnCNew.Simulation.Skirmish.SkirmishAiStats? AgentStats => _agent?.Stats;

    /// <summary>
    /// The viewer's economy and army in commands-only mode: the skirmish AI plays for the local player through the session's command counter (it builds, produces and masses an army,
    /// and attacks the enemy structures that were on the map at the start – what a player sees on the minimap), while the leaves still direct units at the objectives. Without a faction
    /// with a base to start from there is no agent and the run has only the leaves.
    /// </summary>
    private CnCNew.Simulation.Skirmish.SkirmishAi? CreateAgent()
    {
        var catalog = _world.Templates;
        var player = _program.Players.FirstOrDefault(value => value.Id == _local);
        if (player is null || !catalog.Factions.TryGet(player.Faction, out var faction) || !catalog.TryGet(faction.StartingBuilding, out var building)) return null;
        var own = LiveOf(_local).FirstOrDefault(entity => entity.Template.IsKindOf("STRUCTURE")) ?? LiveOf(_local).FirstOrDefault();
        if (own is null) return null;
        var hints = _world.Entities.Where(entity => !entity.Owner.IsSystem && entity.Template.IsKindOf("STRUCTURE") && _world.AreEnemies(_local, entity.Owner) && ScriptEngine.CountsAsObject(entity))
            .OrderBy(entity => entity.Id.Value).Select(entity => (entity.X, entity.Y)).ToImmutableArray();
        var options = new CnCNew.Simulation.Skirmish.SkirmishAiOptions(_local, 0x5EEDu, own.X, own.Y, building.Side,
            CnCNew.Simulation.Skirmish.SkirmishMatchSession.FamilyPrefix(faction), hints, _agentThreshold, _agentThreshold * 3);
        return new CnCNew.Simulation.Skirmish.SkirmishAi(_world, catalog, options, command => _session.Issue(command.Player, (sequence, tick) => command with { Sequence = sequence, Tick = tick }));
    }

    /// <summary>The agent's turn for the current tick (call once per tick, before the session steps); nothing without an agent.</summary>
    public void Act() => _agent?.Update();
}

internal sealed partial class ObjectiveDriver
{
    /// <summary>One line about what the agent did (empty without an agent).</summary>
    public string AgentSummary()
    {
        if (_agent is null) return string.Empty;
        var stats = _agent.Stats;
        var army = Army().Count();
        return $"agent: {stats.Commands} commands ({stats.Rejected} rejected), {stats.Constructions} constructions, {stats.Productions} productions, {stats.Waves} attack waves, {stats.Shots} shots, attacking {stats.AttackTicks} ticks; money {_world.MoneyOf(_local)}, army now {army}; {_agent.DescribeState()}";
    }
}
