using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>World mutations the mission script engine needs that no player command covers (<c>Scripts.h</c> actions act on the world directly, not as a player).</summary>
public sealed partial class SimulationWorld
{
    /// <summary>
    /// Creates every object requested so far right now instead of at the end of the next step. The map loader and the script actions create objects
    /// synchronously in the reference (<c>TheThingFactory->newObject</c>), so a script that runs later in the same frame already sees them.
    /// </summary>
    public void MaterializePendingSpawns() => ProcessSpawns(endOfTick: false);

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

    /// <summary><c>Player::setCanBuildBase</c> / <c>setCanBuildUnits</c> (<c>PLAYER_ENABLE/DISABLE_BASE/UNIT_CONSTRUCTION</c>): structures cannot be placed / units cannot be queued while off.</summary>
    public void SetCanBuild(PlayerId player, bool structures, bool allowed)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        if (structures) state.CanBuildBase = allowed;
        else state.CanBuildUnits = allowed;
    }

    /// <summary>The boxes a supply warehouse still holds (<c>SupplyWarehouseDockUpdate::getBoxesStored</c>); <see langword="null"/> for an object that is not a warehouse.</summary>
    public int? WarehouseBoxes(EntityId id)
        => FindEntity(id)?.ModuleArray.OfType<Modules.BuiltIn.SupplyWarehouseDockUpdate>().FirstOrDefault()?.Boxes;

    public bool CanBuildStructures(PlayerId player) => !_players.TryGetValue(player.Value, out var state) || state.CanBuildBase;
    public bool CanBuildUnits(PlayerId player) => !_players.TryGetValue(player.Value, out var state) || state.CanBuildUnits;

    /// <summary>
    /// Map reveal/shroud for one player (<c>ScriptActions::doRevealMapEntirePermanently</c>, <c>doRevealMapEntire</c>, <c>doRevealMapAtWaypoint</c>,
    /// <c>doShroudMapEntire</c>, <c>doShroudMapAtWaypoint</c>). No-ops in a world without a terrain (everything is visible there anyway).
    /// </summary>
    public void RevealMapPermanently(PlayerId player, bool revealed) => ChangeVision(player, vision => vision.SetPermanentRevealAll(SlotOf(player), revealed));
    public void ExploreMap(PlayerId player) => ChangeVision(player, vision => vision.ExploreAll(SlotOf(player)));
    public void RevealAreaPermanently(PlayerId player, Fixed x, Fixed y, Fixed radius, bool revealed) => ChangeVision(player, vision => vision.SetPermanentRevealCircle(SlotOf(player), x, y, radius, revealed));
    public void ShroudMap(PlayerId player) => ChangeVision(player, vision => vision.ShroudAll(SlotOf(player)));
    public void ExploreArea(PlayerId player, Fixed x, Fixed y, Fixed radius) => ChangeVision(player, vision => vision.ExploreCircle(SlotOf(player), x, y, radius));
    public void ShroudArea(PlayerId player, Fixed x, Fixed y, Fixed radius) => ChangeVision(player, vision => vision.ShroudCircle(SlotOf(player), x, y, radius));

    private void ChangeVision(PlayerId player, Action<Terrain.VisionMap> change)
    {
        if (_vision is null || SlotOf(player) < 0) return;
        change(_vision);
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
            ApplyTemplateEnergy(entity.Owner, entity.Template.EnergyProduction, false);
            ApplyTemplateEnergy(newOwner, entity.Template.EnergyProduction, true);
        }
        RemoveBattlePlanFromEntity(entity);
        NotifyOwnerChange(entity, newOwner);
        SetOwner(entity, newOwner);
        ApplyBattlePlanToEntity(entity);
        MarkDirty(entity);
        return true;
    }

    /// <summary><see cref="RequestSpawn(int, PlayerId, Fixed, Fixed, Fixed, Fixed)"/> with a map object's initial health in percent (<c>objectInitialHealth</c>).</summary>
    /// <inheritdoc cref="IWorldServices.DisabledFlagsOf"/>
    public DisabledMask DisabledFlagsOf(Entity entity)
    {
        var mask = entity.DisabledFlags(_tick);
        if (entity.ContainedBy.IsValid && FindEntity(entity.ContainedBy)?.Container is { HoldsRiders: true }) mask |= DisabledMask.Held;
        return mask;
    }

    /// <summary><c>Object::setDisabledUntil(DISABLED_EMP, until)</c> as <c>EMPUpdate</c> calls it (the scripts' and tests' way to it).</summary>
    public void DisableByEmpUntil(Entity victim, long until) => Modules.BuiltIn.AirWeaponModules.DisableUntil(this, victim, until);

    /// <summary>Sets or clears one script disable bit of an entity (<see cref="Entity.ScriptHeld"/> = <c>DISABLED_HELD</c>, <see cref="Entity.ScriptDisabled"/> = <c>OBJECT_STATUS_SCRIPT_DISABLED</c>).</summary>
    public void SetScriptDisable(Entity entity, byte bit, bool on)
    {
        var mask = on ? (byte)(entity.ScriptDisableMask | bit) : (byte)(entity.ScriptDisableMask & ~bit);
        if (mask == entity.ScriptDisableMask) return;
        entity.ScriptDisableMask = mask;
        MarkDirty(entity);
    }

    public EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle, int healthPercent)
    {
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new ArgumentOutOfRangeException(nameof(templateIndex));
        var id = Ids.AllocateEntity();
        var setup = new SpawnSetup(healthPercent is > 0 and < 100, healthPercent, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid);
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, owner, x, y, z, angle, setup));
        return id;
    }
}
