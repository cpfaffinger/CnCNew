using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>World mutations the mission script engine needs that no player command covers (<c>Scripts.h</c> actions act on the world directly, not as a player).</summary>
public sealed partial class SimulationWorld
{
    /// <summary>Whether <paramref name="player"/> is configured in this world (the system authority, id 0, always is).</summary>
    public bool HasPlayer(PlayerId player) => _players.ContainsKey(player.Value);

    /// <summary>
    /// <c>doSetMoney</c> (<c>ScriptActions.cpp</c>): withdraws everything the player has, then deposits <paramref name="amount"/>. A negative target is
    /// clamped to zero, as <c>Money::deposit</c> ignores non-positive amounts.
    /// </summary>
    public void SetMoney(PlayerId player, Fixed amount)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        state.Money = amount.IsNegative ? Fixed.Zero : amount;
    }

    /// <summary>Sets one directed relation (<c>Player::setPlayerRelationship</c>), as <see cref="Commands.SetRelationshipCommand"/> does.</summary>
    public void SetRelationship(PlayerId from, PlayerId to, Relationship relationship)
    {
        if (from.IsSystem || to.IsSystem || from == to) return;
        if (!_players.TryGetValue(from.Value, out var state) || !_players.ContainsKey(to.Value)) return;
        state.Relations[to.Value] = relationship;
    }

    /// <summary>
    /// <c>Object::setTeam</c> for a controlling-player change (<c>doNamedTransferAssetsToPlayer</c>): the object now belongs to <paramref name="newOwner"/>, and
    /// the power it supplies or consumes moves with it. Returns <see langword="false"/> when nothing changed (unknown object, unknown player, same owner).
    /// </summary>
    public bool TransferOwnership(EntityId id, PlayerId newOwner)
    {
        var entity = FindEntity(id);
        if (entity is null || entity.DestroyRequested || entity.Owner == newOwner) return false;
        if (!newOwner.IsSystem && !_players.ContainsKey(newOwner.Value)) return false;
        if (!entity.HasStatus(ObjectStatusBits.UnderConstruction))
        {
            if (_players.TryGetValue(entity.Owner.Value, out var previous)) previous.EnergyProduction -= entity.Template.EnergyProduction;
            if (_players.TryGetValue(newOwner.Value, out var next)) next.EnergyProduction += entity.Template.EnergyProduction;
        }
        entity.Owner = newOwner;
        MarkDirty(entity);
        return true;
    }

    /// <summary><see cref="RequestSpawn(int, PlayerId, Fixed, Fixed, Fixed, Fixed)"/> with a map object's initial health in percent (<c>objectInitialHealth</c>).</summary>
    public EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle, int healthPercent)
    {
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new ArgumentOutOfRangeException(nameof(templateIndex));
        var id = Ids.AllocateEntity();
        var setup = new SpawnSetup(healthPercent is > 0 and < 100, healthPercent, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid);
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, owner, x, y, z, angle, setup));
        return id;
    }
}
