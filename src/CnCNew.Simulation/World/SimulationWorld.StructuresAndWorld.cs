using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// World support of the structures-and-world module group (<c>docs/SIMULATION-CORE.md</c>): the registry of entities that carry a collide module or a shroud-range override (both
/// kept in id order, so every pass over them is deterministic), the collide dispatch the footprint check feeds, and the player-side lookups <c>OCLUpdate</c> needs.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly SortedDictionary<uint, Entity> _colliders = [];
    private readonly SortedDictionary<uint, IShroudRangeProvider> _shroudProviders = [];

    private void RegisterStructureHooks(Entity entity)
    {
        foreach (var module in entity.ModuleArray)
        {
            if (module is ICollideModule) _colliders[entity.Id.Value] = entity;
            if (module is IShroudRangeProvider provider) _shroudProviders[entity.Id.Value] = provider;
        }
    }

    private void UnregisterStructureHooks(Entity entity)
    {
        _colliders.Remove(entity.Id.Value);
        _shroudProviders.Remove(entity.Id.Value);
    }

    /// <summary>The shroud clearing range an entity's module overrides the template's with, if any (<c>Object::setShroudClearingRange</c>).</summary>
    private bool TryShroudOverride(Entity entity, out Fixed range)
    {
        if (_shroudProviders.Count > 0 && _shroudProviders.TryGetValue(entity.Id.Value, out var provider)) return provider.TryGetShroudRange(out range);
        range = default;
        return false;
    }

    /// <summary>
    /// <c>onCollide</c> for the pair (<paramref name="mover"/>, every collide-module entity whose footprint it overlaps), delivered to both parties like the reference's physics does. Entities with a collide
    /// module are visited in id order; a module's reaction may damage or destroy (deferred) but never spawns synchronously, so the pass is stable.
    /// </summary>
    private void DispatchCollisions(Entity mover)
    {
        var moverCollides = _colliders.ContainsKey(mover.Id.Value);
        if (_colliders.Count == 0 || (_colliders.Count == 1 && moverCollides)) return;
        foreach (var collider in _colliders.Values)
        {
            if (collider.Id == mover.Id || collider.EffectivelyDead || collider.DestroyRequested) continue;
            if (collider.ContainedBy.IsValid || mover.ContainedBy.IsValid || !FootprintsOverlap(mover, collider)) continue;
            foreach (var module in collider.ModuleArray)
                if (module is ICollideModule collide) collide.OnCollide(this, mover);
            if (!moverCollides) continue;
            foreach (var module in mover.ModuleArray)
                if (module is ICollideModule collide) collide.OnCollide(this, collider);
        }
    }

    /// <summary>Asks the <see cref="IFireGate"/> modules of a fire command's source (a deployed-only weapon) whether the shot may go out now.</summary>
    private bool GateAllowsFire(Entity source, Entity victim)
    {
        foreach (var module in source.ModuleArray)
            if (module is IFireGate gate && !gate.AllowsFire(this, victim)) return false;
        return true;
    }

    /// <summary>One <c>KindOfPercentProductionChange</c>: objects with all of the kinds cost <c>1 + Percent</c> times as much; reference counted as the same (kinds, percent) pair can come from several upgrades.</summary>
    private sealed class CostModifier(ImmutableArray<string> kinds, Fixed percent)
    {
        public ImmutableArray<string> Kinds { get; } = kinds;
        public Fixed Percent { get; } = percent;
        public int References { get; set; } = 1;
    }

    /// <inheritdoc cref="IWorldServices.AddProductionCostChange"/>
    public void AddProductionCostChange(PlayerId player, ImmutableArray<string> kinds, Fixed percent)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        var existing = state.CostModifiers.Find(entry => entry.Percent == percent && entry.Kinds.SequenceEqual(kinds, StringComparer.Ordinal));
        if (existing is not null) existing.References++;
        else state.CostModifiers.Add(new CostModifier(kinds, percent));
    }

    /// <inheritdoc cref="IWorldServices.RemoveProductionCostChange"/>
    public void RemoveProductionCostChange(PlayerId player, ImmutableArray<string> kinds, Fixed percent)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        var index = state.CostModifiers.FindIndex(entry => entry.Percent == percent && entry.Kinds.SequenceEqual(kinds, StringComparer.Ordinal));
        if (index < 0) return;
        if (--state.CostModifiers[index].References == 0) state.CostModifiers.RemoveAt(index);
    }

    /// <summary>
    /// <c>ThingTemplate::calcCostToBuild</c> (without the handicap and the per-template changes the simulation does not model): the build cost times the product of <c>1 + percent</c>
    /// of every modifier whose kinds the template has all of (<c>getProductionCostChangeBasedOnKindOf</c>), truncated to whole money. A player with no modifier pays exactly <c>BuildCost</c>.
    /// </summary>
    private static Fixed ProductionCostOf(PlayerState player, ObjectTemplate template)
    {
        if (player.CostModifiers.Count == 0) return template.BuildCost;
        var factor = Fixed.One;
        foreach (var modifier in player.CostModifiers)
            if (modifier.Kinds.All(template.IsKindOf)) factor *= Fixed.One + modifier.Percent;
        return Fixed.FromInt((template.BuildCost * factor).Floor());
    }

    /// <summary>What a player pays for a template now (the template's build cost for an unknown player).</summary>
    public Fixed ProductionCostOf(PlayerId player, ObjectTemplate template) => _players.TryGetValue(player.Value, out var state) ? ProductionCostOf(state, template) : template.BuildCost;

    private static void DescribeCostModifiers(IStateWriter writer, PlayerState player)
    {
        if (player.CostModifiers.Count == 0) return;
        writer.BeginArray("costModifiers");
        foreach (var modifier in player.CostModifiers)
        {
            writer.BeginObject(null);
            writer.Text("kinds", string.Join(' ', modifier.Kinds));
            writer.Fixed("percent", modifier.Percent);
            writer.Int("references", modifier.References);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private static void LoadCostModifiers(IStateReader reader, PlayerState player)
    {
        player.CostModifiers.Clear();
        if (!reader.Has("costModifiers")) return;
        foreach (var item in reader.Array("costModifiers"))
            player.CostModifiers.Add(new CostModifier([..item.Text("kinds").Split(' ', StringSplitOptions.RemoveEmptyEntries)], item.Fixed("percent")) { References = checked((int)item.Int("references")) });
    }

    /// <summary><c>Object::setTeam</c>'s <c>onCapture</c> calls: the modules that care learn of the change before the owner is replaced.</summary>
    private void NotifyOwnerChange(Entity entity, PlayerId newOwner)
    {
        foreach (var module in entity.ModuleArray)
            if (module is IOwnerChangeReceiver receiver) receiver.OnOwnerChanged(this, entity.Owner, newOwner);
    }

    private long _listLifetimeOverride;

    /// <inheritdoc cref="IWorldServices.CreateFromListWithLifetime"/>
    public EntityId CreateFromListWithLifetime(Entity source, string listName, EntityId requester, long lifetimeTicks)
    {
        _listLifetimeOverride = lifetimeTicks;
        try
        {
            return CreateFromList(source, listName, requester, false);
        }
        finally
        {
            _listLifetimeOverride = 0;
        }
    }

    /// <inheritdoc cref="IWorldServices.AdjustDamageByArmor"/>
    public Fixed AdjustDamageByArmor(Entity victim, int damageType, Fixed amount) => ActiveArmor(victim).Adjust(damageType, amount);

    /// <inheritdoc cref="IWorldServices.FactionSideOf"/>
    public string FactionSideOf(PlayerId player) => FactionOf(player)?.Side ?? string.Empty;

    /// <inheritdoc cref="IWorldServices.IsPlayableSide"/>
    public bool IsPlayableSide(PlayerId player) => FactionOf(player)?.PlayableSide ?? false;

    private Templates.FactionInfo? FactionOf(PlayerId player)
    {
        if (player.IsSystem) return null;
        foreach (var configured in Configuration.Players)
            if (configured.Id == player && configured.Faction is { } name && _templates.Factions.TryGet(name, out var faction)) return faction;
        return null;
    }
}
