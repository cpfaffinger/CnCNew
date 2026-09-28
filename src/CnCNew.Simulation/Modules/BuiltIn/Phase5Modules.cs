using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The phase-5 module family: the upgrade system (<c>StealthUpdate</c>, <c>AutoHealBehavior</c> and the eleven
/// <c>*Upgrade</c> modules, Block 0.2 of docs/HANDOFF-PHASE5.md) plus simple straight-line movement
/// (<c>AIUpdateInterface</c>) and unit production (<c>ProductionUpdate</c>). See docs/GAME-RULES.md for the rules these
/// modules implement and the simplifications each one documents inline.
/// </summary>
internal static class Phase5Modules
{
    private static ModuleBinding<TData> Bind<TData>(string name, ModuleOwnerKind owner, Func<ModuleConfiguration, TData> parse, Func<TData, SimModule> create)
        where TData : ModuleData
        => new(name, owner, (configuration, _) => parse(configuration), create);

    private static ModuleBinding<TData> BindUpgrade<TData>(string name, ModuleOwnerKind owner, Func<ModuleConfiguration, UpgradeCatalog, TData> parse, Func<TData, SimModule> create)
        where TData : ModuleData
        => new(name, owner, parse, create);

    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        Bind<StealthData>("StealthUpdate", ModuleOwnerKind.Object, StealthData.Parse, data => new StealthUpdate(data)),
        BindUpgrade<AutoHealData>("AutoHealBehavior", ModuleOwnerKind.Object, AutoHealData.Parse, data => new AutoHealBehavior(data)),
        BindUpgrade<UpgradeMuxOnlyData>("ArmorUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new ArmorUpgrade(data)),
        BindUpgrade<UpgradeMuxOnlyData>("WeaponSetUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new WeaponSetUpgrade(data)),
        BindUpgrade<UpgradeMuxOnlyData>("WeaponBonusUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new WeaponBonusUpgrade(data)),
        BindUpgrade<UpgradeMuxOnlyData>("LocomotorSetUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new LocomotorSetUpgrade(data)),
        BindUpgrade<UpgradeMuxOnlyData>("StealthUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new StealthUpgrade(data)),
        BindUpgrade<MaxHealthUpgradeData>("MaxHealthUpgrade", ModuleOwnerKind.Object, MaxHealthUpgradeData.Parse, data => new MaxHealthUpgrade(data)),
        BindUpgrade<RadarUpgradeData>("RadarUpgrade", ModuleOwnerKind.Object, RadarUpgradeData.Parse, data => new RadarUpgrade(data)),
        BindUpgrade<CommandSetUpgradeData>("CommandSetUpgrade", ModuleOwnerKind.Object, CommandSetUpgradeData.Parse, data => new CommandSetUpgrade(data)),
        BindUpgrade<ExperienceScalarUpgradeData>("ExperienceScalarUpgrade", ModuleOwnerKind.Object, ExperienceScalarUpgradeData.Parse, data => new ExperienceScalarUpgrade(data)),
        BindUpgrade<ModelConditionUpgradeData>("ModelConditionUpgrade", ModuleOwnerKind.Object, ModelConditionUpgradeData.Parse, data => new ModelConditionUpgrade(data)),
        BindUpgrade<ObjectCreationUpgradeData>("ObjectCreationUpgrade", ModuleOwnerKind.Object, ObjectCreationUpgradeData.Parse, data => new ObjectCreationUpgrade(data)),
        Bind<AIUpdateData>("AIUpdateInterface", ModuleOwnerKind.Object, AIUpdateData.Parse, data => new AIUpdateInterface(data)),
        Bind<ProductionData>("ProductionUpdate", ModuleOwnerKind.Object, ProductionData.Parse, data => new ProductionUpdate(data)),
    ];
}

// =============================================================================================== StealthUpdate

/// <summary>
/// <c>StealthUpdate.cpp</c> field set. Only the fields that drive the phase-5 stealth state machine are kept:
/// <see cref="StealthDelayTicks"/>, <see cref="MoveThresholdSpeed"/>, <see cref="ForbiddenConditions"/>,
/// <see cref="RequiredStatus"/>/<see cref="ForbiddenStatus"/> and <see cref="InnateStealth"/>. The remaining typed-but-not-
/// applied fields are cosmetic or belong to systems phase 5 does not model: <c>FriendlyOpacityMin/Max</c>, <c>PulseFrequency</c>,
/// the disguise fields, <c>EnemyDetectionEvaEvent</c>/<c>OwnDetectionEvaEvent</c> (presentation), <c>RevealDistanceFromTarget</c>,
/// <c>OrderIdleEnemiesToAttackMeUponReveal</c>, <c>UseRiderStealth</c> (no garrison system), <c>BlackMarketCheckDelay</c> (no
/// black-market economy hack) and <c>GrantedBySpecialPower</c> (no special-power system). None of the object's targeting
/// consumes the resulting <c>STEALTHED</c> status bit yet either: phase 5's <c>FireWeaponCommand</c> is direct-target only,
/// with no automatic target acquisition or line of sight to hide from — see docs/GAME-RULES.md.
/// </summary>
public sealed record StealthData(
    long StealthDelayTicks, Fixed MoveThresholdSpeed, ImmutableArray<string> ForbiddenConditions,
    ulong RequiredStatus, ulong ForbiddenStatus, bool InnateStealth) : ModuleData
{
    public static StealthData Parse(ModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "StealthDelay", 0),
        ModuleConfigReader.Real(configuration, "MoveThresholdSpeed", Fixed.Zero),
        ModuleConfigReader.Flags(configuration, "StealthForbiddenConditions")?.Names ?? [],
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "RequiredStatus"), NameTables.ObjectStatus),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "ForbiddenStatus"), NameTables.ObjectStatus),
        ModuleConfigReader.Bool(configuration, "InnateStealth", false));
}

/// <summary>
/// <c>StealthUpdate</c>: cloaks the object (sets the <c>STEALTHED</c> status bit) once none of its forbidden conditions have
/// applied for <see cref="StealthData.StealthDelayTicks"/> consecutive ticks; any forbidden condition breaks stealth
/// immediately (matches the reference: violating a condition resets the countdown and clears the flag in the same frame).
/// </summary>
public sealed class StealthUpdate(StealthData data) : SimModule, IUpdateModule
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private long _nextWake = -1;
    private long _readyTick = long.MaxValue;

    public override string ModuleName => "StealthUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        if (data.InnateStealth && !AnyForbiddenCondition(world)) Entity.Status |= 1UL << StealthedBit;
        _nextWake = Math.Min(world.Tick + SimulationTiming.SleepNone, SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        if (!Satisfies(world, data.RequiredStatus, data.ForbiddenStatus) || AnyForbiddenCondition(world))
        {
            _readyTick = long.MaxValue;
            Entity.Status &= ~(1UL << StealthedBit);
        }
        else if (!Entity.HasStatus(StealthedBit))
        {
            if (_readyTick == long.MaxValue) _readyTick = world.Tick + Math.Max(data.StealthDelayTicks, SimulationTiming.SleepNone);
            if (world.Tick >= _readyTick) Entity.Status |= 1UL << StealthedBit;
        }
        return SimulationTiming.SleepNone;
    }

    private static bool Satisfies(IWorldServices world, ulong required, ulong forbidden)
    {
        _ = world;
        return true; // RequiredStatus/ForbiddenStatus are evaluated against the entity directly, see below.
    }

    private bool AnyForbiddenCondition(IWorldServices world)
    {
        if (data.ForbiddenStatus != 0 && (Entity.Status & data.ForbiddenStatus) != 0) return true;
        if (data.RequiredStatus != 0 && (Entity.Status & data.RequiredStatus) != data.RequiredStatus) return true;
        foreach (var name in data.ForbiddenConditions)
            if (ConditionActive(name, world)) return true;
        return false;
    }

    /// <summary>
    /// Maps each <c>StealthForbiddenCondition</c> name to what phase 5 can actually observe. <c>NO_BLACK_MARKET</c> (no
    /// black-market economy hack) and <c>RIDERS_ATTACKING</c> (no passenger/garrison system) are compiled but never true —
    /// a documented gap, not a silent one.
    /// </summary>
    private bool ConditionActive(string name, IWorldServices world) => name.ToUpperInvariant() switch
    {
        "ATTACKING" => Entity.HasStatus(ObjectStatusBits.Index("IS_ATTACKING")),
        "MOVING" => Entity.Speed.Abs() > data.MoveThresholdSpeed,
        "USING_ABILITY" => Entity.HasStatus(ObjectStatusBits.Index("USING_ABILITY")),
        "FIRING_PRIMARY" or "FIRING_SECONDARY" or "FIRING_TERTIARY" => Entity.LastFireTick == world.Tick,
        "TAKING_DAMAGE" => Entity.LastDamageTick == world.Tick,
        _ => false,
    };

    public override void Describe(IStateWriter writer) => writer.Int("readyTick", _readyTick == long.MaxValue ? -1 : _readyTick);

    public override void Load(IStateReader reader)
    {
        var value = reader.Int("readyTick");
        _readyTick = value < 0 ? long.MaxValue : value;
    }
}

// ============================================================================================ AutoHealBehavior

/// <summary>
/// <c>AutoHealBehavior.h</c>. <see cref="AutoHealData.Mux"/> lets the ability be granted by an upgrade instead of starting
/// active; <see cref="AutoHealData.StartsActive"/> is the non-upgrade path (most uses: a structure with an innate heal aura).
/// </summary>
public sealed record AutoHealData(
    UpgradeMuxData Mux, bool StartsActive, bool SingleBurst, Fixed HealingAmount, long HealingDelayTicks, Fixed Radius,
    ImmutableArray<string> KindOf, ImmutableArray<string> ForbiddenKindOf, long StartHealingDelayTicks,
    bool AffectsWholePlayer, bool SkipSelfForHealing) : ModuleData
{
    public static AutoHealData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(
        UpgradeMuxData.Parse(configuration, catalog),
        ModuleConfigReader.Bool(configuration, "StartsActive", false),
        ModuleConfigReader.Bool(configuration, "SingleBurst", false),
        ModuleConfigReader.Real(configuration, "HealingAmount", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "HealingDelay", 0),
        ModuleConfigReader.Real(configuration, "Radius", Fixed.Zero),
        ModuleConfigReader.Flags(configuration, "KindOf")?.Names ?? [],
        ModuleConfigReader.Flags(configuration, "ForbiddenKindOf")?.Names ?? [],
        ModuleConfigReader.Ticks(configuration, "StartHealingDelay", 0),
        ModuleConfigReader.Bool(configuration, "AffectsWholePlayer", false),
        ModuleConfigReader.Bool(configuration, "SkipSelfForHealing", false));
}

/// <summary>
/// <c>AutoHealBehavior</c>: periodically heals every matching object of the same player, either within <see cref="AutoHealData.Radius"/>
/// or (with <see cref="AutoHealData.AffectsWholePlayer"/>) anywhere. Matching is an O(entities) scan of <see cref="IWorldServices.Entities"/>
/// (fine at fixture/scenario scale; a spatial index is a phase-6 performance concern, not a correctness one).
/// </summary>
public sealed class AutoHealBehavior(AutoHealData data) : SimModule, IUpdateModule, IUpgradeModule
{
    private readonly UpgradeMuxState _mux = new(data.Mux);
    private bool _active;
    private long _nextWake = -1;

    public override string ModuleName => "AutoHealBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _active = data.StartsActive;
        _nextWake = _active ? Math.Min(world.Tick + Math.Max(data.StartHealingDelayTicks, SimulationTiming.SleepNone), SimulationTiming.SleepForever) : SimulationTiming.SleepForever;
    }

    public void TryActivate(IWorldServices world, UpgradeMask keyMask)
    {
        if (!_mux.AttemptUpgrade(world, Entity, keyMask, _ => _active = true)) return;
        world.WakeAt(Entity, this, world.Tick + Math.Max(data.StartHealingDelayTicks, SimulationTiming.SleepNone));
    }

    public void TryReset(IWorldServices world, UpgradeMask keyMask)
    {
        if (_mux.ResetUpgrade(keyMask)) _active = false;
    }

    public long Update(IWorldServices world)
    {
        if (!_active) return SimulationTiming.SleepForever;
        foreach (var candidate in world.Entities)
            if (Matches(candidate)) world.Damage(candidate.Id, new DamageInfo(Entity.Id, data.HealingAmount, DamageTypes.Healing, DeathTypes.None, false));
        if (data.SingleBurst) { _active = false; return SimulationTiming.SleepForever; }
        return Math.Max(data.HealingDelayTicks, SimulationTiming.SleepNone);
    }

    private bool Matches(Entity candidate)
    {
        if (candidate.Owner != Entity.Owner || candidate.Body is null || candidate.EffectivelyDead) return false;
        if (data.SkipSelfForHealing && candidate.Id == Entity.Id) return false;
        if (!data.KindOf.IsEmpty && !data.KindOf.Any(candidate.Template.IsKindOf)) return false;
        if (data.ForbiddenKindOf.Any(candidate.Template.IsKindOf)) return false;
        if (data.AffectsWholePlayer) return true;
        var dx = candidate.X - Entity.X;
        var dy = candidate.Y - Entity.Y;
        return dx * dx + dy * dy <= data.Radius * data.Radius;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Bool("active", _active);
        _mux.Describe(writer);
    }

    public override void Load(IStateReader reader)
    {
        _active = reader.Bool("active");
        _mux.Load(reader);
    }
}

// ==================================================================================== the eleven upgrade-only modules

/// <summary>Data shared by every module whose entire job is the <see cref="UpgradeMuxState"/> state machine (no fields of its own).</summary>
public sealed record UpgradeMuxOnlyData(UpgradeMuxData Mux) : ModuleData
{
    public static UpgradeMuxOnlyData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog));
}

/// <summary>Base of a module that is purely a named <see cref="UpgradeMuxState"/> with one activation effect and its inverse.</summary>
public abstract class UpgradeMuxModule(UpgradeMuxData data) : SimModule, IUpgradeModule
{
    protected UpgradeMuxState Mux { get; } = new(data);

    public void TryActivate(IWorldServices world, UpgradeMask keyMask) => Mux.AttemptUpgrade(world, Entity, keyMask, Activate);
    public void TryReset(IWorldServices world, UpgradeMask keyMask) { if (Mux.ResetUpgrade(keyMask)) Deactivate(); }

    protected abstract void Activate(IWorldServices world);
    protected virtual void Deactivate() { }

    public override void Describe(IStateWriter writer) => Mux.Describe(writer);
    public override void Load(IStateReader reader) => Mux.Load(reader);
}

/// <summary><c>ArmorUpgrade</c>: switches damage resolution to <see cref="Templates.ObjectTemplate.PlayerUpgradeArmor"/>.</summary>
public sealed class ArmorUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ArmorUpgrade";
    protected override void Activate(IWorldServices world) => Entity.HasPlayerUpgradeArmor = true;
    protected override void Deactivate() => Entity.HasPlayerUpgradeArmor = false;
}

/// <summary><c>WeaponSetUpgrade</c>: switches to <see cref="Templates.ObjectTemplate.PlayerUpgradeWeapon"/> and refills its clip.</summary>
public sealed class WeaponSetUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "WeaponSetUpgrade";

    protected override void Activate(IWorldServices world)
    {
        Entity.HasPlayerUpgradeWeapon = true;
        var index = Entity.Template.PlayerUpgradeWeapon >= 0 ? Entity.Template.PlayerUpgradeWeapon : Entity.Template.DefaultWeapon;
        if (index < 0) return;
        var weapon = world.Weapons[index];
        Entity.WeaponClip = weapon.ClipSize == 0 ? int.MaxValue : weapon.ClipSize;
        Entity.WeaponReadyTick = world.Tick;
    }

    protected override void Deactivate() => Entity.HasPlayerUpgradeWeapon = false;
}

/// <summary>
/// <c>WeaponBonusUpgrade</c>: phase 5 has no <c>WeaponBonus</c> condition/multiplier system (<see cref="Templates.WeaponInfo"/>
/// always resolves at coefficient 1.0), so this module runs the real trigger/conflict/removal state machine — other modules
/// keying off the same upgrade name still see correct <c>TriggeredBy</c>/<c>RemovesUpgrades</c> behaviour — but its own
/// effect is a documented no-op, deferred to phase 6.
/// </summary>
public sealed class WeaponBonusUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "WeaponBonusUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary>
/// <c>LocomotorSetUpgrade</c>: phase 5's <see cref="Templates.ObjectTemplate.Locomotor"/> only resolves the <c>SET_NORMAL</c>
/// entry (no freefall/wander/taxi/panic/upgrade locomotor sets — see <c>ObjectTemplate.ResolveLocomotor</c>), so switching
/// locomotor sets has no distinct target to switch to yet; documented no-op, deferred to phase 6.
/// </summary>
public sealed class LocomotorSetUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "LocomotorSetUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>StealthUpgrade</c>: grants the object the ability to cloak (<c>CAN_STEALTH</c> status bit).</summary>
public sealed class StealthUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    private static readonly int CanStealthBit = ObjectStatusBits.Index("CAN_STEALTH");
    public override string ModuleName => "StealthUpgrade";
    protected override void Activate(IWorldServices world) => Entity.Status |= 1UL << CanStealthBit;
    protected override void Deactivate() => Entity.Status &= ~(1UL << CanStealthBit);
}

/// <summary><c>MaxHealthUpgrade.cpp</c>: raises the body's health cap by <see cref="AddMaxHealth"/> per <see cref="ChangeType"/>.</summary>
public sealed record MaxHealthUpgradeData(UpgradeMuxData Mux, Fixed AddMaxHealth, string ChangeType) : ModuleData
{
    public static MaxHealthUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
    {
        var field = ModuleConfigReader.Find(configuration, "ChangeType");
        return new MaxHealthUpgradeData(UpgradeMuxData.Parse(configuration, catalog),
            ModuleConfigReader.Real(configuration, "AddMaxHealth", Fixed.Zero),
            field is null ? "SAME_CURRENTHEALTH" : field.Value.OriginalText.Trim());
    }
}

/// <summary>A body that does not implement <see cref="IMaxHealthAdjustable"/> (<c>InactiveBody</c>: no health) completes the upgrade with no numeric effect, matching the reference's zero-health props.</summary>
public sealed class MaxHealthUpgrade(MaxHealthUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "MaxHealthUpgrade";
    protected override void Activate(IWorldServices world)
    {
        if (Entity.Body is IMaxHealthAdjustable adjustable) adjustable.AdjustMaxHealth(data.AddMaxHealth, data.ChangeType);
    }
}

/// <summary>
/// <c>RadarUpgrade.cpp</c>: the headless simulation has no radar/shroud/minimap (a presentation/UI concern), so this module
/// only runs the upgrade state machine; <see cref="DisableProof"/> is compiled but has no consumer yet.
/// </summary>
public sealed record RadarUpgradeData(UpgradeMuxData Mux, bool DisableProof) : ModuleData
{
    public static RadarUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Bool(configuration, "DisableProof", false));
}

public sealed class RadarUpgrade(RadarUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "RadarUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>CommandSetUpgrade.cpp</c>: swaps which <c>CommandSet</c> (UI button layout) is active — a presentation concern the headless simulation does not model; upgrade state machine only.</summary>
public sealed record CommandSetUpgradeData(UpgradeMuxData Mux, string CommandSet, string CommandSetAlt, string TriggerAlt) : ModuleData
{
    public static CommandSetUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog),
        ModuleConfigReader.Text(configuration, "CommandSet"), ModuleConfigReader.Text(configuration, "CommandSetAlt"), ModuleConfigReader.Text(configuration, "TriggerAlt"));
}

public sealed class CommandSetUpgrade(CommandSetUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "CommandSetUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>ExperienceScalarUpgrade.cpp</c>: phase 5 has no experience/kill-XP system (<c>VeterancyLevel</c> is only set directly via <c>SimulationWorld.SetVeterancy</c>), so the scalar is compiled but unapplied; deferred to phase 6.</summary>
public sealed record ExperienceScalarUpgradeData(UpgradeMuxData Mux, Fixed AddXpScalar) : ModuleData
{
    public static ExperienceScalarUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Real(configuration, "AddXPScalar", Fixed.Zero));
}

public sealed class ExperienceScalarUpgrade(ExperienceScalarUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ExperienceScalarUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>ModelConditionUpgrade.cpp</c>: sets a draw-time model condition flag — pure presentation, not modeled in the simulation.</summary>
public sealed record ModelConditionUpgradeData(UpgradeMuxData Mux) : ModuleData
{
    public static ModelConditionUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog));
}

public sealed class ModelConditionUpgrade(ModelConditionUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ModelConditionUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary>
/// <c>ObjectCreationUpgrade.cpp</c>: spawns an object creation list on completion. Object creation lists are not yet a typed
/// definition domain (still <c>Compatibility</c>, see docs/TYPED-DEFINITIONS.md), so — exactly like <c>CreateObjectDie</c> in
/// phase 4 — this module requests execution via an event instead of resolving and spawning the list itself.
/// </summary>
public sealed record ObjectCreationUpgradeData(UpgradeMuxData Mux, string UpgradeObject) : ModuleData
{
    public static ObjectCreationUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Text(configuration, "UpgradeObject"));
}

public sealed class ObjectCreationUpgrade(ObjectCreationUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ObjectCreationUpgrade";

    protected override void Activate(IWorldServices world)
    {
        if (data.UpgradeObject.Length > 0) world.Emit(SimEventKind.CreationListRequested, Entity.Id, EntityId.Invalid, data.UpgradeObject, 0);
    }
}

// ========================================================================================== AIUpdateInterface (movement)

/// <summary>
/// <c>AIUpdate.cpp</c> field set. Phase 5 scope is straight-line movement only: the fields that drive it live on
/// <see cref="Templates.LocomotorInfo"/>/<see cref="World.Entity"/> (set by <c>SetMoveTargetCommand</c>), not here.
/// <c>AutoAcquireEnemiesWhenIdle</c> is kept because it documents a real behaviour phase 5 does not implement (no automatic
/// target acquisition: <c>FireWeaponCommand</c> is always a direct, explicit order); the rest of the schema
/// (<c>MoodAttackCheckRate</c>, idle scan timing/angles, <c>ForbidPlayerCommands</c>, <c>MembersGetHealedAtLifeRatio</c>,
/// <c>VoiceRapidFire</c>) is typed but not consumed — AI moods and squad healing are phase-6 scope.
/// </summary>
public sealed record AIUpdateData(bool AutoAcquireEnemiesWhenIdle) : ModuleData
{
    public static AIUpdateData Parse(ModuleConfiguration configuration) => new(ModuleConfigReader.Bool(configuration, "AutoAcquireEnemiesWhenIdle", false));
}

/// <summary>
/// <c>AIUpdateInterface</c>: moves the object toward <see cref="Entity.MoveTargetX"/>/<see cref="Entity.MoveTargetY"/> in a
/// straight vector line (no terrain, no pathfinding, no turn-rate-clamped heading — <see cref="Templates.LocomotorInfo"/>
/// documents exactly which locomotor fields phase 5 applies). Sleeps while idle; <c>SimulationWorld</c> wakes it the instant
/// a <c>SetMoveTargetCommand</c> is accepted.
/// </summary>
public sealed class AIUpdateInterface(AIUpdateData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;

    public override string ModuleName => "AIUpdateInterface";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public long Update(IWorldServices world)
    {
        _ = data; // AutoAcquireEnemiesWhenIdle: see type doc comment — no auto-targeting in phase 5.
        if (!Entity.HasMoveTarget) return SimulationTiming.SleepForever;
        if (Entity.Template.Locomotor is not { } locomotor)
        {
            // No locomotor resolved (e.g. only a non-SET_NORMAL set is defined): drop the stale order rather than spin forever.
            Entity.HasMoveTarget = false;
            return SimulationTiming.SleepForever;
        }
        Move(world, locomotor);
        return Entity.HasMoveTarget ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    private void Move(IWorldServices world, LocomotorInfo locomotor)
    {
        var dx = Entity.MoveTargetX - Entity.X;
        var dy = Entity.MoveTargetY - Entity.Y;
        var distSq = dx * dx + dy * dy;
        // "Close enough" is at least one tick's travel at full speed, so a fast unit cannot perpetually overshoot and reverse.
        var closeEnough = Fixed.Max(locomotor.CloseEnoughDist, locomotor.Speed);
        if (distSq <= closeEnough * closeEnough)
        {
            Entity.X = Entity.MoveTargetX;
            Entity.Y = Entity.MoveTargetY;
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
            world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
            return;
        }
        var dist = Fixed.Sqrt(distSq);
        // calcSlowDownDist equivalent: v^2 = 2*a*d, so begin braking once the remaining distance fits the stopping distance at the current speed.
        var brakingDist = locomotor.Braking > Fixed.Zero ? Entity.Speed * Entity.Speed / (locomotor.Braking * Fixed.FromInt(2)) : Fixed.Zero;
        var goalSpeed = dist <= brakingDist ? locomotor.MinSpeed : locomotor.Speed;
        Entity.Speed = Entity.Speed < goalSpeed
            ? Fixed.Min(goalSpeed, Entity.Speed + locomotor.Acceleration)
            : Fixed.Max(goalSpeed, Entity.Speed - locomotor.Braking);
        var step = Fixed.Min(Entity.Speed, dist);
        Entity.X += dx * step / dist;
        Entity.Y += dy * step / dist;
    }

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}

// ============================================================================================= ProductionUpdate

/// <summary><c>ProductionUpdate.h</c>. <c>QuantityModifier</c> and the door-animation timing fields are typed but not applied: phase 5 spawns one unit per completed queue entry at the producer's own position (no exit-point geometry, no rally points — see docs/GAME-RULES.md).</summary>
public sealed record ProductionData(int MaxQueueEntries) : ModuleData
{
    public static ProductionData Parse(ModuleConfiguration configuration)
    {
        var max = ModuleConfigReader.Int(configuration, "MaxQueueEntries", 0);
        return new ProductionData(max <= 0 ? int.MaxValue : checked((int)max));
    }
}

/// <summary>One queued unit: template, its cost (for a full refund on cancel) and remaining build ticks.</summary>
internal readonly record struct ProductionEntry(int TemplateIndex, Fixed Cost, long RemainingTicks);

/// <summary>
/// <c>ProductionUpdate::update()</c>: only the head-of-queue entry accumulates progress, one tick per call; when it reaches
/// zero the unit spawns at the producer's own position and the next entry (if any) starts from a fresh tick count. The full
/// cost is withdrawn at queue time (<c>SimulationWorld</c>'s <c>QueueProductionCommand</c> handler) and refunded in full on
/// cancel, matching <c>Money::withdraw</c>/<c>deposit</c> semantics; there is no per-frame energy-ratio build-time penalty
/// (a documented phase-5 simplification — <c>ProductionUpdate::calcTimeToBuild</c> in the reference).
/// </summary>
public sealed class ProductionUpdate(ProductionData data) : SimModule, IUpdateModule
{
    private readonly List<ProductionEntry> _queue = [];
    private long _nextWake = -1;

    public override string ModuleName => "ProductionUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool HasRoom => _queue.Count < data.MaxQueueEntries;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void Enqueue(int templateIndex, Fixed cost, long totalTicks) => _queue.Add(new ProductionEntry(templateIndex, cost, Math.Max(totalTicks, 1)));

    public bool HasSlot(int slot) => slot >= 0 && slot < _queue.Count;

    public bool TryCancel(int slot, out Fixed refund)
    {
        if (!HasSlot(slot)) { refund = Fixed.Zero; return false; }
        refund = _queue[slot].Cost;
        _queue.RemoveAt(slot);
        return true;
    }

    public long Update(IWorldServices world)
    {
        if (_queue.Count == 0) return SimulationTiming.SleepForever;
        var head = _queue[0];
        var remaining = head.RemainingTicks - 1;
        if (remaining <= 0)
        {
            _queue.RemoveAt(0);
            world.RequestSpawn(head.TemplateIndex, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle);
            world.Emit(SimEventKind.ProductionCompleted, Entity.Id, EntityId.Invalid, string.Empty, head.TemplateIndex);
        }
        else _queue[0] = head with { RemainingTicks = remaining };
        return _queue.Count == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.BeginArray("queue");
        foreach (var entry in _queue)
        {
            writer.BeginObject(null);
            writer.Int("template", entry.TemplateIndex);
            writer.Fixed("cost", entry.Cost);
            writer.Int("remaining", entry.RemainingTicks);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _queue.Clear();
        foreach (var entry in reader.Array("queue"))
            _queue.Add(new ProductionEntry(checked((int)entry.Int("template")), entry.Fixed("cost"), entry.Int("remaining")));
    }
}
