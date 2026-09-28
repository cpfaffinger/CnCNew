using CnCNew.Data.Definitions;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules;

/// <summary>Typed, immutable per-template configuration of a module, produced by a binding from a <c>ModuleConfiguration</c>.</summary>
public abstract record ModuleData;

/// <summary>
/// What modules may use of the world. Deliberately narrow: modules read the tick, draw from the <c>logic</c> stream, find
/// entities, and mutate the world only through requests (events, destroy, damage, wake), never by touching each other.
/// </summary>
public interface IWorldServices
{
    long Tick { get; }
    SageRandom LogicRandom { get; }
    Entity? FindEntity(EntityId id);

    /// <summary>Every live entity (registered, not yet destroyed). Used by area-effect modules (<c>AutoHealBehavior</c>); O(n), fine at fixture scale.</summary>
    IReadOnlyCollection<Entity> Entities { get; }
    void Emit(SimEventKind kind, EntityId entity, EntityId other, string text, long value);

    /// <summary>Requests removal at the end of the tick. The entity receives no further updates, but its events are still delivered.</summary>
    void RequestDestroy(EntityId id);

    DamageOutcome Damage(EntityId target, in DamageInfo info);

    /// <summary><c>Object::kill</c>: unresistable damage with the kill flag, so that die modules run.</summary>
    void Kill(EntityId target);

    /// <summary>Called by a body when its entity died; emits the event and runs the die modules in module order.</summary>
    void OnEntityDied(Entity entity, in DamageInfo info);

    /// <summary><c>friend_awakenUpdateModule</c>: moves the module's next wake tick.</summary>
    void WakeAt(Entity entity, IUpdateModule module, long tick);

    // ------------------------------------------------------------------------------------ phase 5: economy, upgrades

    UpgradeCatalog Upgrades { get; }
    WeaponCatalog Weapons { get; }

    /// <summary>The controlling player's current balance (0 for the system player or an unknown player).</summary>
    Fixed MoneyOf(PlayerId player);
    void AddMoney(PlayerId player, Fixed amount);

    /// <summary><c>Money::withdraw</c>: clamps to the available balance and returns the amount actually withdrawn.</summary>
    Fixed WithdrawMoney(PlayerId player, Fixed amount);

    /// <summary>Player-scoped completed-upgrade mask (<c>Player::getCompletedUpgradeMask</c>).</summary>
    UpgradeMask PlayerUpgrades(PlayerId player);

    /// <summary>Union of the entity's own and its controlling player's completed-upgrade masks — the reference's dispatch key.</summary>
    UpgradeMask UpgradeKeyMaskOf(Entity entity);

    /// <summary>
    /// Completes an upgrade for a player (<see cref="UpgradeScope.Player"/>) or one entity (<see cref="UpgradeScope.Object"/>),
    /// then re-evaluates every <see cref="IUpgradeModule"/> the change can affect (<c>Player::onUpgradeCompleted</c>).
    /// </summary>
    void CompleteUpgrade(PlayerId player, EntityId? objectScope, string upgradeName);

    void RemoveUpgradeByName(Entity entity, string upgradeName);

    /// <summary>Reserves and queues the creation of a fresh entity outside the command/validation path (engine-internal spawns: production, OCLs).</summary>
    EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle);

    // ------------------------------------------------------------------------------------------- phase 5: sciences

    ScienceCatalog Sciences { get; }
    ScienceMask PlayerSciences(PlayerId player);

    /// <summary><c>GlobalData::m_unitDamagedThresh</c>/<c>m_unitReallyDamagedThresh</c> (<c>GameData.ini</c>): health-ratio thresholds <see cref="BuiltIn.BodyDamageStates"/> is computed from.</summary>
    Fixed UnitDamagedThreshold { get; }
    Fixed UnitReallyDamagedThreshold { get; }

    /// <summary>"Science Purchase Points" earned via rank/experience in the reference (not modeled); a scenario/system command credits them directly instead — see <c>docs/GAME-RULES.md</c>.</summary>
    int SciencePointsOf(PlayerId player);
    void AddSciencePoints(PlayerId player, int amount);
}

/// <summary>A module driven by the <see cref="UpgradeMux"/> state machine (<c>UpgradeModule</c> / any <c>UpgradeMux</c>-mixin module).</summary>
public interface IUpgradeModule
{
    void TryActivate(IWorldServices world, UpgradeMask keyMask);
    void TryReset(IWorldServices world, UpgradeMask keyMask);
}

/// <summary>A body that can change its maximum health at run time (<c>MaxHealthUpgrade.cpp</c>). Not every body supports this (<c>InactiveBody</c> has no health).</summary>
public interface IMaxHealthAdjustable
{
    void AdjustMaxHealth(IWorldServices world, Fixed delta, string changeType);
}

public abstract class SimModule
{
    public Entity Entity { get; internal set; } = null!;
    public int Index { get; internal set; }
    public string Tag { get; internal set; } = string.Empty;
    public abstract string ModuleName { get; }

    /// <summary>Runs once when the entity is created (not when restored from a snapshot). May schedule updates and draw random numbers.</summary>
    public virtual void OnSpawn(IWorldServices world) { }

    /// <summary>Runs in the destroy phase, before the entity is removed.</summary>
    public virtual void OnRemove(IWorldServices world) { }

    public abstract void Describe(IStateWriter writer);
    public abstract void Load(IStateReader reader);
}

public interface IUpdateModule
{
    /// <summary>Absolute tick of the next call; <see cref="Time.SimulationTiming.SleepForever"/> or more means "not scheduled".</summary>
    long NextWake { get; set; }

    /// <summary>Returns the number of ticks to sleep (at least 1) or <see cref="Time.SimulationTiming.SleepForever"/>.</summary>
    long Update(IWorldServices world);
}

public interface IDieModule
{
    void OnDie(IWorldServices world, in DamageInfo info);
}

public interface IBodyModule
{
    Fixed Health { get; }
    Fixed MaxHealth { get; }
    DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info);
}

public interface IEventReceiver
{
    void OnEvent(IWorldServices world, in SimEvent simEvent);
}

/// <summary>
/// <c>DamageModuleInterface</c>: called for every damage application that actually lowers health, before the
/// fear-sound check and before the death check (so both run even on a killing blow). Either callback may be left at
/// its no-op default (the reference's own <c>DamageModuleInterface</c> subclasses routinely implement only one).
/// </summary>
public interface IDamageModule
{
    void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt) { }

    /// <summary><c>oldState</c>/<c>newState</c> are <see cref="BuiltIn.BodyDamageStates"/> values; <paramref name="info"/> is the damage that caused the transition (matches <c>getLastDamageInfo()</c> in a synchronous, single-threaded model).</summary>
    void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState) { }

    /// <summary><c>ActiveBody::attemptHealing</c>: fires only when health actually rose (mirrors <see cref="OnDamage"/>'s "health actually dropped" gate).</summary>
    void OnHealing(IWorldServices world, in DamageInfo info) { }
}

/// <summary>Bit positions of the object status table (<c>ObjectStatusMaskType::s_bitNameList</c>).</summary>
public static class ObjectStatusBits
{
    public static int Index(string name) => NameTables.ObjectStatus.TryIndexOf(name, out var index)
        ? index
        : throw new ArgumentException($"'{name}' is not an object status.", nameof(name));

    public static int Destroyed { get; } = Index("DESTROYED");
    public static int UnderConstruction { get; } = Index("UNDER_CONSTRUCTION");

    public static ulong Mask(FlagsValue? flags, NameTable table)
    {
        if (flags is null) return 0;
        ulong mask = 0;
        foreach (var name in flags.Names) mask |= 1UL << (table.TryIndexOf(name, out var index) ? index : throw new InvalidDataException($"Unknown flag '{name}'."));
        return mask;
    }
}
