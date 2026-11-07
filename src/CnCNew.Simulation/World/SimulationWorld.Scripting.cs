using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

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

    /// <inheritdoc cref="Modules.IWorldServices.AllowedToBuild"/>
    public bool AllowedToBuild(PlayerId player, ObjectTemplate template) => template.IsKindOf("STRUCTURE") ? CanBuildStructures(player) : CanBuildUnits(player);

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
            ApplyObjectEnergy(entity, entity.Owner, false);
            ApplyObjectEnergy(entity, newOwner, true);
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
    public DisabledMask DisabledFlagsOf(Entity entity) => DisabledFlagsAt(entity, _tick);

    private DisabledMask DisabledFlagsAt(Entity entity, long tick)
    {
        var mask = entity.DisabledFlags(tick);
        if (entity.ContainedBy.IsValid && FindEntity(entity.ContainedBy)?.Container is { HoldsRiders: true }) mask |= DisabledMask.Held;
        return mask;
    }

    /// <summary><c>Object::setDisabledUntil(DISABLED_EMP, until)</c> as <c>EMPUpdate</c> calls it (the scripts' and tests' way to it).</summary>
    public void DisableByEmpUntil(Entity victim, long until) => Modules.BuiltIn.AirWeaponModules.DisableUntil(this, victim, until);

    /// <summary>
    /// Sets or clears one script disable bit of an entity (<see cref="Entity.ScriptHeld"/> = <c>DISABLED_HELD</c>, <see cref="Entity.ScriptDisabled"/> = <c>OBJECT_STATUS_SCRIPT_DISABLED</c>,
    /// <see cref="Entity.ScriptUnpowered"/> = <c>OBJECT_STATUS_SCRIPT_UNPOWERED</c>, <see cref="Entity.ScriptUnsellable"/>, <see cref="Entity.ScriptTargetable"/>). The two script statuses that
    /// disable (<c>Object::setScriptStatus</c>: <c>DISABLED_SCRIPT_DISABLED</c>, <c>DISABLED_SCRIPT_UNDERPOWERED</c>) reach the disable model through <see cref="Entity.DisabledFlags"/>; the edge
    /// and the pause of the special powers follow at the end of the tick.
    /// </summary>
    public void SetScriptDisable(Entity entity, byte bit, bool on)
    {
        var mask = on ? (byte)(entity.ScriptDisableMask | bit) : (byte)(entity.ScriptDisableMask & ~bit);
        if (mask == entity.ScriptDisableMask) return;
        entity.ScriptDisableMask = mask;
        MarkDirty(entity);
    }

    /// <summary><c>ActiveBody::setIndestructible</c> (the panel flag "Indestructible"): the body takes no damage, and a bridge gives the same to its towers. Nothing for a body that is no <c>ActiveBody</c>.</summary>
    public void SetIndestructible(Entity entity, bool indestructible)
    {
        if (entity.Body is not Modules.BuiltIn.ActiveBody body) return;
        body.Indestructible = indestructible;
        MarkDirty(entity);
        if (!entity.Template.IsKindOf("BRIDGE") || Array.Find(entity.ModuleArray, module => module is Modules.BuiltIn.BridgeBehavior) is not Modules.BuiltIn.BridgeBehavior bridge) return;
        for (var type = 0; type < Modules.BuiltIn.BridgeBehavior.MaxTowers; type++)
            if (FindEntity(bridge.TowerId(type)) is { } tower && tower.Body is Modules.BuiltIn.ActiveBody towerBody)
            {
                towerBody.Indestructible = indestructible;
                MarkDirty(tower);
            }
    }

    /// <summary>
    /// <c>Object::updateObjValuesFromMapProperties</c> for what a placed map object brings besides its health: the body takes no damage (<c>objectIndestructible</c>), the script statuses
    /// (<c>objectEnabled</c>, <c>objectPowered</c>, <c>objectUnsellable</c>, <c>objectTargetable</c>) and the attitude of its AI (<c>objectAggressiveness</c>).
    /// </summary>
    private void ApplyMapProperties(Entity entity, Commands.MapObjectFlags flags, int attitude)
    {
        if ((flags & Commands.MapObjectFlags.Indestructible) != 0 && entity.Body is Modules.BuiltIn.ActiveBody body) body.Indestructible = true;
        byte bits = 0;
        if ((flags & Commands.MapObjectFlags.Disabled) != 0) bits |= Entity.ScriptDisabled;
        if ((flags & Commands.MapObjectFlags.Unpowered) != 0) bits |= Entity.ScriptUnpowered;
        if ((flags & Commands.MapObjectFlags.Unsellable) != 0) bits |= Entity.ScriptUnsellable;
        if ((flags & Commands.MapObjectFlags.Targetable) != 0) bits |= Entity.ScriptTargetable;
        // setIsRecruitable acts only on an object with an AI; setSelectable only when the value differs from isSelectable (an ALWAYS_SELECTABLE template stays selectable either way).
        if ((flags & Commands.MapObjectFlags.Unrecruitable) != 0 && AiOf(entity) is not null) bits |= Entity.ScriptUnrecruitable;
        var selectableNow = entity.Template.IsKindOf("ALWAYS_SELECTABLE") || entity.Template.IsKindOf("SELECTABLE");
        if ((flags & Commands.MapObjectFlags.Selectable) != 0 && !selectableNow) bits |= Entity.ScriptSelectable;
        if ((flags & Commands.MapObjectFlags.Unselectable) != 0 && selectableNow) bits |= Entity.ScriptUnselectable;
        if (bits != 0) entity.ScriptDisableMask |= bits;
        if (attitude != 0 && AiOf(entity) is not null) SetAttitude(entity, attitude);
    }

    /// <param name="flags">A mission's map object brings its yes-or-no properties (<c>Object::updateObjValuesFromMapProperties</c>, as <see cref="ApplyMapProperties"/> sets them).</param>
    public EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle, int healthPercent, Commands.MapObjectFlags flags = Commands.MapObjectFlags.None)
    {
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new ArgumentOutOfRangeException(nameof(templateIndex));
        var id = Ids.AllocateEntity();
        var setup = new SpawnSetup(healthPercent is > 0 and < 100, healthPercent, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid, MapFlags: flags);
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, owner, x, y, z, angle, setup));
        return id;
    }
}
