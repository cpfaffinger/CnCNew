using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
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

    /// <summary>A commands-only run orders nothing but the viewer's own objects (a player commands no other side); the refusal is reported.</summary>
    private bool MayOrder(Entity entity)
    {
        if (!_commandsOnly || entity.Owner == _local) return true;
        _declined.Add($"order '{entity.Template.Name}' of another side");
        return false;
    }

    /// <summary>Whether the viewer can still grow an army (an own structure that produces): only then is it worth waiting for the agent to mass one.</summary>
    private bool CanReinforce() => LiveOf(_local).Any(entity => entity.Template.IsKindOf("STRUCTURE") && entity.Template.Modules.Any(module => module.Name == "ProductionUpdate"));

    /// <summary>The viewer's attack orders: unit id to victim id. Like the client's standing orders the driver carries an attack on a named victim tick by tick (<see cref="DriveAssaults"/>).</summary>
    private readonly SortedDictionary<uint, uint> _assaults = [];

    private readonly List<(Fixed X, Fixed Y)> _objectivePoints = [];
    private bool _objectivesFresh;
    private long _objectivesAt = long.MinValue;

    /// <summary>
    /// Orders the army at <paramref name="victims"/>: where the agent leads the army the victims become its objective (one army, one goal, instead of two command givers pulling at
    /// the same units). Every armed unit of the viewer gets its nearest victim as its attack order (<see cref="DriveAssaults"/> fires at it once in reach - a player's units do not
    /// pick buildings or neutral objects by themselves); units the agent does not lead also get an attack-move toward it. False when no order could be given.
    /// </summary>
    private bool Assault(IReadOnlyList<Entity> victims)
    {
        if (victims.Any(victim => victim.Owner == _local || _world.AreAllies(_local, victim.Owner)))
        {
            _declined.Add("destroy objects of the viewer's own side");
            victims = [..victims.Where(victim => victim.Owner != _local && !_world.AreAllies(_local, victim.Owner))];
        }
        if (victims.Count == 0) return false;
        if (!_objectivesFresh) { _objectivePoints.Clear(); _objectivesFresh = true; }
        foreach (var victim in victims) _objectivePoints.Add((victim.X, victim.Y));
        _objectivesAt = _world.Tick;
        // Units trickling in one by one die one by one: while the viewer can still produce, the driver waits until the agent's army masses and attacks (the agent sets the mass).
        if (_agent is { Attacking: false } && CanReinforce()) return false;
        var any = _agent is not null;
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
            if (nearest is null) continue;
            _assaults[unit.Id.Value] = nearest.Id.Value;
            if (best <= range * range || (_agent is not null && _agent.LeadsUnit(unit))) continue;
            any |= SendAttackMove(unit, (nearest.X, nearest.Y));
        }
        return any;
    }

    /// <summary>Hands the agent this round's objectives (or none, once the leaves have stopped naming any for a while).</summary>
    private void PublishObjectives()
    {
        if (_agent is null) return;
        if (_world.Tick - _objectivesAt > 150) _objectivePoints.Clear();
        // Only toward a named objective: without one a fixed force that charges at whatever it knows of loses the units the mission needs.
        _agent.CommitEverything = _objectivePoints.Count > 0 && !CanReinforce();
        _agent.Objectives = [.._objectivePoints];
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

    /// <summary>
    /// "Make this structure ours" as a player does it: a capture order from one of the viewer's units that carry the capture ability (it walks there itself). While the ability is
    /// still paused the upgrade that releases it is bought first, a capturer is produced when there is none, and a structure the viewer has not seen yet gets a scout. The reduced
    /// instant capture the simulation grants any unit standing next to a structure is not used: only data that carries the ability exists in the real units, the fixture's units
    /// (which have none) fall back to it.
    /// </summary>
    private bool Capture(Entity target)
    {
        var candidates = LiveOf(_local).Where(entity => entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE") && !entity.ContainedBy.IsValid)
            .OrderBy(entity => ((entity.X - target.X) * (entity.X - target.X) + (entity.Y - target.Y) * (entity.Y - target.Y)).Raw).ThenBy(entity => entity.Id.Value).ToArray();
        var capturers = candidates.Where(entity => _world.StagedCaptureOf(entity) is not null).ToArray();
        var anyAbility = _world.Templates.Templates.Any(CapturesByData);
        var victim = target.Id;
        foreach (var unit in (anyAbility ? capturers : candidates).Take(4))
        {
            var actor = unit.Id;
            if (!_session.Issue(_local, (sequence, tick) => new CaptureCommand(_local, sequence, tick, actor, victim)).Accepted) continue;
            Commands++;
            return true;
        }
        if (!anyAbility) return candidates.FirstOrDefault() is { } walker && Send(walker, (target.X, target.Y));
        if (capturers.Length == 0)
        {
            // Nobody who could: train one (the cheapest unit with the ability that an own producer offers).
            foreach (var template in _world.Templates.Templates.Where(template => CapturesByData(template) && template.Locomotor is not null).OrderBy(template => template.BuildCost.Raw).ThenBy(template => template.Name, StringComparer.Ordinal))
                if (Produce(_local, template.Name)) return true;
            _declined.Add("capture a structure (no unit with the capture ability and no producer that offers one)");
            return false;
        }
        // The ability is paused until its upgrade is bought (a player buys it at the barracks, so only a side that can produce may).
        if (CanReinforce())
            foreach (var name in capturers[0].Template.Modules.Select(module => module.Data).OfType<UnpauseSpecialPowerUpgradeData>().SelectMany(data => data.Mux.TriggerNames).Distinct(StringComparer.Ordinal))
            {
                if (!_world.Templates.Upgrades.TryGet(name, out var upgrade)) continue;
                EntityId? scope = upgrade.Scope == Templates.UpgradeScope.Object ? capturers[0].Id : null;
                if (_session.Issue(_local, (sequence, tick) => new PurchaseUpgradeCommand(_local, sequence, tick, name, scope)).Accepted)
                {
                    Commands++;
                    if (_agent is not null) _agent.ObjectiveReserve = Fixed.Zero;
                    return true;
                }
                // Not affordable yet: the agent stops spending below the price until it is bought.
                if (_agent is not null && _world.MoneyOf(_local) < upgrade.BuildCost && !capturers[0].CompletedUpgrades.Has(upgrade.Index) && !_world.PlayerUpgrades(_local).Has(upgrade.Index)) { _agent.ObjectiveReserve = upgrade.BuildCost; return true; }
            }
        return !_world.CanPlayerSee(_local, target) && Send(capturers[0], (target.X, target.Y));
    }

    /// <summary>The template carries one of the two staged capture abilities (the Ranger's flag, Black Lotus' hack).</summary>
    private bool CapturesByData(Templates.ObjectTemplate template)
        => template.Modules.Any(module => module.Data is SpecialAbilityUpdateData ability && _world.SpecialPowers.TryGet(ability.Base.PowerName, out var power) && power.Type.Contains("CAPTURE_BUILDING", StringComparison.OrdinalIgnoreCase));

    /// <summary>Sends the viewer's unit that is nearest to <paramref name="spot"/> there to have a look (what a player does about "discover X").</summary>
    private bool Scout((Fixed X, Fixed Y) spot)
    {
        var scout = LiveOf(_local).Where(entity => entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE") && !entity.ContainedBy.IsValid && ScriptEngine.CountsAsObject(entity))
            .OrderBy(entity => ((entity.X - spot.X) * (entity.X - spot.X) + (entity.Y - spot.Y) * (entity.Y - spot.Y)).Raw).ThenBy(entity => entity.Id.Value).FirstOrDefault();
        return scout is not null && Send(scout, spot);
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
            CnCNew.Simulation.Skirmish.SkirmishMatchSession.FamilyPrefix(faction), hints, _agentThreshold, _agentThreshold * 3, EconomyFirst: true);
        return new CnCNew.Simulation.Skirmish.SkirmishAi(_world, catalog, options, command => _session.Issue(command.Player, (sequence, tick) => command with { Sequence = sequence, Tick = tick }));
    }

    /// <summary>The agent's turn for the current tick (call once per tick, before the session steps); nothing without an agent.</summary>
    public void Act()
    {
        _agent?.Update();
        DriveAssaults();
    }

    private bool SendAttackMove(Entity entity, (Fixed X, Fixed Y) spot)
    {
        if (entity.Owner.IsSystem || entity.Template.Locomotor is null || !MayOrder(entity)) return false;
        var owner = entity.Owner;
        var id = entity.Id;
        if (!_session.Issue(owner, (sequence, tick) => new AttackMoveCommand(owner, sequence, tick, id, spot.X, spot.Y)).Accepted) return false;
        Commands++;
        return true;
    }

    /// <summary>The fire half of the attack orders (<c>aiAttackObject</c> by the player): a unit whose weapon reaches its victim and is ready fires at it, whatever the unit would pick by itself.</summary>
    private void DriveAssaults()
    {
        if (_assaults.Count == 0) return;
        foreach (var (unitId, victimId) in _assaults.ToArray())
        {
            var unit = _world.FindEntity(new EntityId(unitId));
            var victim = _world.FindEntity(new EntityId(victimId));
            if (unit is null || victim is null || unit.DestroyRequested || unit.EffectivelyDead || victim.DestroyRequested || victim.EffectivelyDead || victim.Body is null)
            {
                _assaults.Remove(unitId);
                continue;
            }
            if (!_world.ChooseWeapon(unit, victim, CnCNew.Simulation.Templates.FireOrigin.Player).CanFire) continue;
            var owner = unit.Owner;
            if (_session.Issue(owner, (sequence, tick) => new FireWeaponCommand(owner, sequence, tick, unit.Id, victim.Id)).Accepted) Commands++;
        }
    }
}

internal sealed partial class ObjectiveDriver
{
    /// <summary>One line about what the agent did (empty without an agent).</summary>
    public string AgentSummary()
    {
        if (_agent is null) return string.Empty;
        var stats = _agent.Stats;
        var army = Army().Count();
        return $"agent: {stats.Commands} commands ({stats.Rejected} rejected), {stats.Constructions} constructions, {stats.Productions} productions, {stats.Waves} attack waves, attacking {stats.AttackTicks} ticks; money {_world.MoneyOf(_local)}, army now {army}; {_agent.DescribeState()}";
    }
}
