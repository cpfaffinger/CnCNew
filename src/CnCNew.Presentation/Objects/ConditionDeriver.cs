using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>Inputs of <see cref="ConditionDeriver"/> that are not entity state: time of day, weather, and the construction work one tick earlier.</summary>
public readonly record struct ConditionContext(bool Night = false, bool Snow = false, int? PreviousConstructionWork = null);

/// <summary>
/// Derives the model condition bits of an entity from simulation state. The reference sets these bits imperatively from many places
/// (<c>Drawable::onDamageStateChanged</c>, <c>AIStates.cpp</c>, <c>WeaponSet::getModelConditionForWeaponSlot</c>, <c>DozerAIUpdate</c>,
/// <c>GarrisonContain</c>, <c>TransportContain</c>, <c>DockUpdate</c>, <c>FlammableUpdate</c>, <c>Object::onVeterancyLevelChanged</c>,
/// <c>RadarUpdate</c>, <c>PowerPlantUpdate</c>, <c>ModelConditionUpgrade</c>); here each bit is a pure function of the state the simulation
/// keeps. Bits whose driving state the simulation does not model yet are never set (listed in docs/PRESENTATION-OBJECTS.md).
/// </summary>
public static class ConditionDeriver
{
    private static readonly int ParachutingStatus = ObjectStatusBits.Index("PARACHUTING");
    private static readonly int AflameStatus = ObjectStatusBits.Index("AFLAME");
    private static readonly int BurnedStatus = ObjectStatusBits.Index("BURNED");
    private static readonly int AttackingStatus = ObjectStatusBits.Index("IS_ATTACKING");
    private static readonly int DeployedStatus = ObjectStatusBits.Index("DEPLOYED");
    private static readonly int DisguisedStatus = ObjectStatusBits.Index("DISGUISED");
    private static readonly int Rider1Status = ObjectStatusBits.Index("STATUS_RIDER1");

    public static ModelConditionSet Derive(Entity entity, SimulationWorld world, ObjectVisual? visual, in ConditionContext context)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(world);
        var set = ModelConditionSet.Empty;
        if (context.Night) set = set.With(ModelConditions.Night);
        if (context.Snow) set = set.With(ModelConditions.Snow);

        // Drawable::onDamageStateChanged: exactly one of DAMAGED/REALLYDAMAGED/RUBBLE per BodyDamageType.
        set = entity.DamageState() switch
        {
            BodyDamageStates.Damaged => set.With(ModelConditions.Damaged),
            BodyDamageStates.ReallyDamaged => set.With(ModelConditions.ReallyDamaged),
            BodyDamageStates.Rubble => set.With(ModelConditions.Rubble),
            _ => set,
        };
        if (entity.EffectivelyDead && entity.Body is not null and not InactiveBody && !entity.DestroyRequested) set = set.With(ModelConditions.Dying);

        if (entity.Speed != Fixed.Zero || entity.HasMoveTarget) set = set.With(ModelConditions.Moving);
        set = Weapon(entity, world, set);
        if (entity.HasStatus(AttackingStatus)) set = set.With(ModelConditions.Attacking);

        set = Construction(entity, world, set, context);
        if (entity.HasStatus(ObjectStatusBits.Sold)) set = set.With(ModelConditions.Sold);
        if (entity.HasStatus(ParachutingStatus)) set = set.With(ModelConditions.Parachuting);
        if (entity.HasStatus(AflameStatus)) set = set.With(ModelConditions.Aflame);
        if (entity.HasStatus(BurnedStatus)) set = set.With(ModelConditions.Burned);
        if (entity.HasStatus(DeployedStatus)) set = set.With(ModelConditions.Deployed);
        if (entity.HasStatus(DisguisedStatus)) set = set.With(ModelConditions.Disguised);
        for (var rider = 0; rider < 8; rider++)
            if (entity.HasStatus(Rider1Status + rider)) set = set.With(new ModelConditionFlag(ModelConditions.Rider1.Id + rider));

        // Object::onVeterancyLevelChanged -> setWeaponSetFlag -> TheWeaponSetTypeToModelConditionTypeMap.
        set = entity.VeterancyLevel switch
        {
            1 => set.With(ModelConditions.WeaponSetVeteran),
            2 => set.With(ModelConditions.WeaponSetElite),
            3 => set.With(ModelConditions.WeaponSetHero),
            _ => set,
        };
        if (entity.HasPlayerUpgradeWeapon) set = set.With(ModelConditions.WeaponSetPlayerUpgrade);

        if (entity.Container is { ContainCount: > 0 } container)
            set = set.With(container is GarrisonContain ? ModelConditions.Garrisoned : ModelConditions.Loaded);
        if (entity.CarriedSupplyBoxes() > 0) set = set.With(ModelConditions.Carrying);

        foreach (var module in entity.Modules)
        {
            switch (module)
            {
                case AIUpdateInterface { IsDocking: true }:
                    set = set.With(ModelConditions.Docking);
                    break;
                case AIUpdateInterface { IsBuilding: true } when entity.Speed == Fixed.Zero && !entity.HasMoveTarget:
                    set = set.With(ModelConditions.ActivelyConstructing);
                    break;
                case RadarUpdate { RadarActive: true } radar:
                    set = set.With(radar.IsExtendComplete(world.Tick) ? ModelConditions.RadarUpgraded : ModelConditions.RadarExtending);
                    break;
                case PowerPlantUpdate { Extended: true } plant:
                    set = set.With(plant.IsExtendComplete(world.Tick) ? ModelConditions.PowerPlantUpgraded : ModelConditions.PowerPlantUpgrading);
                    break;
            }
        }

        if (visual is not null && !visual.ConditionUpgrades.IsEmpty)
        {
            var keys = world.UpgradeKeyMaskOf(entity);
            foreach (var upgrade in visual.ConditionUpgrades)
            {
                var activation = world.Upgrades.MaskOf(upgrade.TriggeredBy);
                var conflicts = world.Upgrades.MaskOf(upgrade.ConflictsWith);
                if (!activation.Any() || (keys.Any() && keys.TestForAny(conflicts))) continue;
                if (upgrade.RequiresAllTriggers ? keys.TestForAll(activation) : keys.TestForAny(activation)) set = set.With(upgrade.Flag);
            }
        }
        return set;
    }

    /// <summary>
    /// The current weapon slot only (<c>Object::adjustModelConditionForWeaponStatus</c> sets nothing for a slot that is not the current weapon).
    /// <c>WeaponSet::getModelConditionForWeaponSlot</c> maps the weapon status to FIRING (the shot happened on the last simulated tick),
    /// RELOADING (empty clip, not ready) or BETWEEN_FIRING_SHOTS (not ready), each with USING_WEAPON_A/B/C for the slot.
    /// </summary>
    private static ModelConditionSet Weapon(Entity entity, SimulationWorld world, ModelConditionSet set)
    {
        var slot = entity.CurrentWeaponSlot;
        var clip = entity.ClipOf(slot);
        var lastFire = entity.LastFireTickOf(slot);
        if (clip < 0 || lastFire < 0) return set;
        if (lastFire == world.Tick - 1) return set.With(ModelConditions.Firing[slot]).With(ModelConditions.UsingWeapon[slot]);
        if (world.Tick >= entity.ReadyTickOf(slot)) return set;
        return set.With(clip == 0 ? ModelConditions.Reloading[slot] : ModelConditions.BetweenFiringShots[slot]).With(ModelConditions.UsingWeapon[slot]);
    }

    /// <summary>
    /// <c>DozerAIUpdate</c>/<c>WorkerAIUpdate</c>: AWAITING_CONSTRUCTION until the first work, then PARTIALLY_CONSTRUCTED, plus
    /// ACTIVELY_BEING_CONSTRUCTED while work is added. Without the previous tick's work, a live builder that is building counts as work.
    /// </summary>
    private static ModelConditionSet Construction(Entity entity, SimulationWorld world, ModelConditionSet set, in ConditionContext context)
    {
        if (!entity.HasStatus(ObjectStatusBits.UnderConstruction)) return set;
        if (entity.ConstructionWork == 0) return set.With(ModelConditions.AwaitingConstruction);
        set = set.With(ModelConditions.PartiallyConstructed);
        var active = context.PreviousConstructionWork is { } previous
            ? entity.ConstructionWork > previous
            : world.FindEntity(entity.BuilderId) is { } builder && builder.Modules.OfType<AIUpdateInterface>().Any(ai => ai.IsBuilding);
        return active ? set.With(ModelConditions.ActivelyBeingConstructed) : set;
    }
}
