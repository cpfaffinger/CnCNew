using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The phase-5 module family: the upgrade system (<c>StealthUpdate</c>, <c>AutoHealBehavior</c> and the eleven
/// <c>*Upgrade</c> modules, Block 0.2 of docs/handoffs/HANDOFF-PHASE5.md) plus simple straight-line movement
/// (<c>AIUpdateInterface</c>) and unit production (<c>ProductionUpdate</c>). See docs/GAME-RULES.md for the rules these
/// modules implement and the simplifications each one documents inline.
/// </summary>
internal static class Phase5Modules
{
    private static ModuleBinding<TData> Bind<TData>(string name, ModuleOwnerKind owner, Func<RatedModuleConfiguration, TData> parse, Func<TData, SimModule> create)
        where TData : ModuleData
        => new(name, owner, (configuration, _) => parse(configuration), create);

    private static ModuleBinding<TData> BindUpgrade<TData>(string name, ModuleOwnerKind owner, Func<RatedModuleConfiguration, UpgradeCatalog, TData> parse, Func<TData, SimModule> create)
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
        BindUpgrade<StatusBitsUpgradeData>("StatusBitsUpgrade", ModuleOwnerKind.Object, StatusBitsUpgradeData.Parse, data => new StatusBitsUpgrade(data)),
        BindUpgrade<MaxHealthUpgradeData>("MaxHealthUpgrade", ModuleOwnerKind.Object, MaxHealthUpgradeData.Parse, data => new MaxHealthUpgrade(data)),
        BindUpgrade<RadarUpgradeData>("RadarUpgrade", ModuleOwnerKind.Object, RadarUpgradeData.Parse, data => new RadarUpgrade(data)),
        BindUpgrade<UpgradeMuxOnlyData>("PowerPlantUpgrade", ModuleOwnerKind.Object, UpgradeMuxOnlyData.Parse, data => new PowerPlantUpgrade(data)),
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
    public static StealthData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "StealthDelay", 0),
        // Read raw and compared with the speed per tick: a distance per logic frame of the original (the reference parses a velocity per second here; audit, side findings).
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "MoveThresholdSpeed", Fixed.Zero)),
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
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private static readonly int CanStealthBit = ObjectStatusBits.Index("CAN_STEALTH");
    private long _nextWake = -1;
    private long _readyTick = long.MaxValue;
    private long _detectedUntilTick = -1;

    /// <summary><c>OBJECT_STATUS_SCRIPT_UNSTEALTHED</c> (<c>NAMED_SET_STEALTH_ENABLED</c> false): a script keeps the object from stealthing; saved only while set.</summary>
    public bool ScriptUnstealthed { get; set; }

    public override string ModuleName => "StealthUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        if (data.InnateStealth) Entity.Status |= 1UL << CanStealthBit;
        // The first update comes one logic frame of the original after the object appeared (at 30 Hz the next tick), so the stealth delay starts at the same game time at every rate.
        _nextWake = Math.Min(world.Tick + world.Time.TicksPerOriginalFrame, SimulationTiming.SleepForever);
    }

    private long _framesGranted;

    /// <summary>
    /// <c>StealthUpdate::receiveGrant(active, frames)</c> (<c>GrantStealthBehavior</c>, the GPS scrambler): switches stealth on (<c>CAN_STEALTH</c> and <c>STEALTHED</c> at once, for
    /// <paramref name="frames"/> ticks, 0 = until revoked) or off. Not modelled: the disguise exception (<c>canDisguise</c>: there is no disguise module) and the rider of a
    /// <c>RiderChangeContain</c> inheriting the grant.
    /// </summary>
    public void ReceiveGrant(IWorldServices world, bool active, long frames)
    {
        if (active)
        {
            Entity.Status |= (1UL << CanStealthBit) | (1UL << StealthedBit);
            _readyTick = world.Tick;
            _framesGranted = frames;
            world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        }
        else
        {
            Entity.Status &= ~((1UL << CanStealthBit) | (1UL << StealthedBit));
            _readyTick = long.MaxValue;
            _framesGranted = 0;
        }
        world.MarkDirty(Entity);
    }

    /// <summary><c>StealthUpdate::markAsDetected</c>: extends (never shortens) how long <c>DETECTED</c> stays set. Called by <c>StealthDetectorUpdate</c>.</summary>
    public void MarkDetected(IWorldServices world, long ticks)
    {
        var until = world.Tick + world.Time.AtLeastOneFrame(ticks);
        if (until > _detectedUntilTick) _detectedUntilTick = until;
        Entity.Status |= 1UL << DetectedBit;
        // Called from the detector's update, not this entity's: the change needs its own mark (finding D8).
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (Entity.HasStatus(DetectedBit) && world.Tick >= _detectedUntilTick) Entity.Status &= ~(1UL << DetectedBit);
        // StealthUpdate::update: without CAN_STEALTH (innate, or granted by StealthUpgrade) the module does nothing.
        if (!Entity.HasStatus(CanStealthBit)) return SimulationTiming.SleepNone;
        // "Deal with temporary stealth": a grant with a duration ends when it runs out or as soon as the player gives the unit an order ("No exploits").
        if (_framesGranted > 0)
        {
            _framesGranted--;
            if (_framesGranted == 0 || CrateModules.AiOf(Entity) is { LastCommandFromPlayer: true }) ReceiveGrant(world, false, 0);
            if (!Entity.HasStatus(CanStealthBit)) return SimulationTiming.SleepNone;
        }
        if (!Satisfies(world, data.RequiredStatus, data.ForbiddenStatus) || AnyForbiddenCondition(world) || ScriptUnstealthed)
        {
            _readyTick = long.MaxValue;
            Entity.Status &= ~(1UL << StealthedBit);
        }
        else if (!Entity.HasStatus(StealthedBit))
        {
            if (_readyTick == long.MaxValue) _readyTick = world.Tick + world.Time.AtLeastOneFrame(data.StealthDelayTicks);
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

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("readyTick", _readyTick == long.MaxValue ? -1 : _readyTick);
        writer.Int("detectedUntilTick", _detectedUntilTick);
        if (ScriptUnstealthed) writer.Bool("scriptUnstealthed", true);
        if (_framesGranted > 0) writer.Int("framesGranted", _framesGranted);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var value = reader.Int("readyTick");
        _readyTick = value < 0 ? long.MaxValue : value;
        _detectedUntilTick = reader.Int("detectedUntilTick");
        ScriptUnstealthed = reader.Has("scriptUnstealthed") && reader.Bool("scriptUnstealthed");
        _framesGranted = reader.Has("framesGranted") ? reader.Int("framesGranted") : 0;
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
    public static AutoHealData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog) => new(
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
        _nextWake = _active ? Math.Min(world.Tick + world.Time.AtLeastOneFrame(data.StartHealingDelayTicks), SimulationTiming.SleepForever) : SimulationTiming.SleepForever;
    }

    public void TryActivate(IWorldServices world, UpgradeMask keyMask)
    {
        if (!_mux.AttemptUpgrade(world, Entity, keyMask, _ => _active = true)) return;
        world.WakeAt(Entity, this, world.Tick + world.Time.AtLeastOneFrame(data.StartHealingDelayTicks));
    }

    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    public void TryReset(IWorldServices world, UpgradeMask keyMask)
    {
        if (_mux.ResetUpgrade(keyMask)) _active = false;
    }

    /// <summary><c>AutoHealBehavior::stopHealing</c> (called by <c>MinefieldBehavior</c> once the mine's creator is gone): the aura goes off and the module sleeps for good.</summary>
    public void StopHealing(IWorldServices world)
    {
        _active = false;
        world.WakeAt(Entity, this, SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        if (!_active) return SimulationTiming.SleepForever;
        foreach (var candidate in world.Entities)
            if (Matches(candidate)) world.Damage(candidate.Id, new DamageInfo(Entity.Id, data.HealingAmount, DamageTypes.Healing, DeathTypes.None, false));
        if (data.SingleBurst) { _active = false; return SimulationTiming.SleepForever; }
        // A missing or zero HealingDelay heals once per logic frame of the original, at every rate.
        return world.Time.AtLeastOneFrame(data.HealingDelayTicks);
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
        return SquaredReach.Of(data.Radius).Contains(dx * dx + dy * dy);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("active", _active);
        _mux.Describe(writer);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _active = reader.Bool("active");
        _mux.Load(reader);
    }
}

// ==================================================================================== the eleven upgrade-only modules

/// <summary>Data shared by every module whose entire job is the <see cref="UpgradeMuxState"/> state machine (no fields of its own).</summary>
public sealed record UpgradeMuxOnlyData(UpgradeMuxData Mux) : ModuleData
{
    public static UpgradeMuxOnlyData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog));
}

/// <summary>Base of a module that is purely a named <see cref="UpgradeMuxState"/> with one activation effect and its inverse.</summary>
public abstract class UpgradeMuxModule(UpgradeMuxData data) : SimModule, IUpgradeModule
{
    protected UpgradeMuxState Mux { get; } = new(data);

    /// <summary>Public read of the mux state (`docs/PRESENTATION-INPUT.md` §10 point 2): whether <see cref="Activate"/> has already run.</summary>
    public bool Executed => Mux.Executed;

    public void TryActivate(IWorldServices world, UpgradeMask keyMask) => Mux.AttemptUpgrade(world, Entity, keyMask, Activate);
    public void TryReset(IWorldServices world, UpgradeMask keyMask) { if (Mux.ResetUpgrade(keyMask)) Deactivate(world); }
    public bool WouldUpgrade(UpgradeMask keyMask) => Mux.WouldUpgrade(keyMask);

    protected abstract void Activate(IWorldServices world);
    protected virtual void Deactivate(IWorldServices world) => Deactivate();
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

/// <summary><c>WeaponSetUpgrade</c>: raises the <c>PLAYER_UPGRADE</c> weapon set flag, which selects the object's <c>PLAYER_UPGRADE</c> <c>WeaponSet</c>, and loads its weapons.</summary>
public sealed class WeaponSetUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "WeaponSetUpgrade";

    protected override void Activate(IWorldServices world)
    {
        Entity.HasPlayerUpgradeWeapon = true;
        world.WeaponSetFlagsChanged(Entity);
    }

    protected override void Deactivate() => Entity.HasPlayerUpgradeWeapon = false;
}

/// <summary>
/// <c>WeaponBonusUpgrade.cpp upgradeImplementation</c>: sets the <c>PLAYER_UPGRADE</c> weapon bonus condition, which the object's
/// weapons then apply through their <c>WeaponBonus PLAYER_UPGRADE ...</c> lines. Like the reference it never clears the condition again.
/// </summary>
public sealed class WeaponBonusUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "WeaponBonusUpgrade";
    protected override void Activate(IWorldServices world) => Entity.WeaponBonusConditions |= 1u << WeaponBonusTable.PlayerUpgradeCondition;
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

/// <summary><c>StatusBitsUpgradeModuleData</c>: <c>StatusToSet</c> and <c>StatusToClear</c>, two masks of object status bits (default empty).</summary>
public sealed record StatusBitsUpgradeData(UpgradeMuxData Mux, ulong StatusToSet, ulong StatusToClear) : ModuleData
{
    public static StatusBitsUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "StatusToSet"), NameTables.ObjectStatus),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "StatusToClear"), NameTables.ObjectStatus));
}

/// <summary>
/// <c>StatusBitsUpgrade::upgradeImplementation</c>: when its upgrade arrives the object's status gets <c>StatusToSet</c> and loses <c>StatusToClear</c> (set first, then clear, as the
/// reference does). Nothing is undone when the upgrade is removed - the reference has no downgrade. The shipped data never uses the module; the community patch does (Phase 32).
/// </summary>
public sealed class StatusBitsUpgrade(StatusBitsUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "StatusBitsUpgrade";
    protected override void Activate(IWorldServices world)
    {
        Entity.Status |= data.StatusToSet;
        Entity.Status &= ~data.StatusToClear;
        world.MarkDirty(Entity);
    }
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
    public static MaxHealthUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog)
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
        if (Entity.Body is IMaxHealthAdjustable adjustable) adjustable.AdjustMaxHealth(world, data.AddMaxHealth, data.ChangeType);
    }
}

/// <summary>
/// <c>RadarUpgrade.cpp</c>: the headless simulation has no radar/shroud/minimap (a presentation/UI concern), so this module
/// only runs the upgrade state machine; <see cref="DisableProof"/> is compiled but has no consumer yet.
/// </summary>
public sealed record RadarUpgradeData(UpgradeMuxData Mux, bool DisableProof) : ModuleData
{
    public static RadarUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Bool(configuration, "DisableProof", false));
}

public sealed class RadarUpgrade(RadarUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "RadarUpgrade";
    protected override void Activate(IWorldServices world) => Entity.Modules.OfType<RadarUpdate>().FirstOrDefault()?.ExtendRadar(world);
}

/// <summary>
/// <c>PowerPlantUpgrade.cpp</c>: <c>upgradeImplementation</c> adds the template's <c>EnergyBonus</c> to the owner (<c>Player::addPowerBonus</c>) and
/// extends the rods; <c>onDelete</c> takes the bonus away again. Not modeled: <c>onCapture</c> (no capture); un-upgrading (<c>TryReset</c>) is not in
/// the reference either, but here it also removes the bonus so that re-activation cannot count it twice.
/// </summary>
public sealed class PowerPlantUpgrade(UpgradeMuxOnlyData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "PowerPlantUpgrade";

    protected override void Activate(IWorldServices world)
    {
        world.AddEnergyProduction(Entity.Owner, Entity.Template.EnergyBonus);
        Entity.Modules.OfType<PowerPlantUpdate>().FirstOrDefault()?.ExtendRods(world, true);
    }

    protected override void Deactivate(IWorldServices world)
    {
        world.AddEnergyProduction(Entity.Owner, -Entity.Template.EnergyBonus);
        Entity.Modules.OfType<PowerPlantUpdate>().FirstOrDefault()?.ExtendRods(world, false);
    }

    public override void OnRemove(IWorldServices world)
    {
        if (Mux.Executed) world.AddEnergyProduction(Entity.Owner, -Entity.Template.EnergyBonus);
    }
}

/// <summary><c>CommandSetUpgrade.cpp</c>: swaps which <c>CommandSet</c> (UI button layout) is active — a presentation concern the headless simulation does not model; upgrade state machine only.</summary>
public sealed record CommandSetUpgradeData(UpgradeMuxData Mux, string CommandSet, string CommandSetAlt, string TriggerAlt) : ModuleData
{
    public static CommandSetUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog),
        ModuleConfigReader.Text(configuration, "CommandSet"), ModuleConfigReader.Text(configuration, "CommandSetAlt"), ModuleConfigReader.Text(configuration, "TriggerAlt"));
}

public sealed class CommandSetUpgrade(CommandSetUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "CommandSetUpgrade";
    protected override void Activate(IWorldServices world) { }
}

/// <summary><c>ExperienceScalarUpgrade.cpp upgradeImplementation</c>: adds <c>AddXPScalar</c> to the object's experience gain scalar (never undone, like the reference).</summary>
public sealed record ExperienceScalarUpgradeData(UpgradeMuxData Mux, Fixed AddXpScalar) : ModuleData
{
    public static ExperienceScalarUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Real(configuration, "AddXPScalar", Fixed.Zero));
}

public sealed class ExperienceScalarUpgrade(ExperienceScalarUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ExperienceScalarUpgrade";
    protected override void Activate(IWorldServices world) => Entity.ExperienceScalar += data.AddXpScalar;
}

/// <summary><c>ModelConditionUpgrade.cpp</c>: sets a draw-time model condition flag — pure presentation, not modeled in the simulation.</summary>
public sealed record ModelConditionUpgradeData(UpgradeMuxData Mux) : ModuleData
{
    public static ModelConditionUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog));
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
    public static ObjectCreationUpgradeData Parse(RatedModuleConfiguration configuration, UpgradeCatalog catalog)
        => new(UpgradeMuxData.Parse(configuration, catalog), ModuleConfigReader.Text(configuration, "UpgradeObject"));
}

public sealed class ObjectCreationUpgrade(ObjectCreationUpgradeData data) : UpgradeMuxModule(data.Mux)
{
    public override string ModuleName => "ObjectCreationUpgrade";

    protected override void Activate(IWorldServices world)
    {
        if (data.UpgradeObject.Length > 0) world.CreateFromList(Entity, data.UpgradeObject, EntityId.Invalid, false);
    }
}

// ========================================================================================== AIUpdateInterface (movement)

/// <summary>
/// <c>AIUpdate.cpp</c> field set. Phase 5 scope is straight-line movement only: the fields that drive it live on
/// <see cref="Templates.LocomotorInfo"/>/<see cref="World.Entity"/> (set by <c>SetMoveTargetCommand</c>), not here.
/// <c>AutoAcquireEnemiesWhenIdle</c> (the <c>AAS_*</c> bits) and <c>MoodAttackCheckRate</c> drive the unit's own target acquisition
/// (<see cref="World.SimulationWorld"/>, docs/SIMULATION-CORE.md §33); <c>ForbidPlayerCommands</c> is typed but not consumed.
/// </summary>
public sealed record AIUpdateData(AutoAcquire AutoAcquire, Fixed RepairRatePerSecond, long MoodCheckTicks) : ModuleData
{
    /// <summary><c>AIUpdateModuleData::AIUpdateModuleData</c>: <c>m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2</c>.</summary>
    public const long DefaultMoodCheckFrames = 2 * SimulationRate.OriginalFramesPerSecond;

    public static AIUpdateData Parse(RatedModuleConfiguration configuration) => new(
        ParseAutoAcquire(configuration),
        ModuleConfigReader.Real(configuration, "RepairHealthPercentPerSecond", Fixed.Zero),
        configuration.Time.AtLeastOneFrame(ModuleConfigReader.Ticks(configuration, "MoodAttackCheckRate", DefaultMoodCheckFrames)));

    private static AutoAcquire ParseAutoAcquire(RatedModuleConfiguration configuration)
    {
        if (ModuleConfigReader.Flags(configuration, "AutoAcquireEnemiesWhenIdle") is not { } flags) return AutoAcquire.None;
        var result = AutoAcquire.None;
        foreach (var name in flags.Names)
            result |= name switch
            {
                "YES" => AutoAcquire.Idle,
                "STEALTHED" => AutoAcquire.Stealthed,
                "NO" => AutoAcquire.No,
                "NOTWHILEATTACKING" => AutoAcquire.NotWhileAttacking,
                "ATTACK_BUILDINGS" => AutoAcquire.AttackBuildings,
                _ => throw new InvalidDataException($"Module '{configuration.Name}': unknown AutoAcquireEnemiesWhenIdle flag '{name}'."),
            };
        return result;
    }
}

/// <summary><c>AutoAcquireStates</c> (AIUpdate.h): the bits of <c>AutoAcquireEnemiesWhenIdle</c>, same values as the reference.</summary>
[Flags]
public enum AutoAcquire : byte
{
    None = 0,
    /// <summary><c>AAS_Idle</c>: the idle unit (and its idle turret) looks for a victim by itself.</summary>
    Idle = 0x01,
    /// <summary><c>AAS_Idle_Stealthed</c>: it also does so while stealthed (giving its position away).</summary>
    Stealthed = 0x02,
    /// <summary><c>AAS_Idle_No</c>: the token <c>No</c>; a bit nothing tests.</summary>
    No = 0x04,
    /// <summary><c>AAS_Idle_Not_While_Attacking</c>: no target of opportunity while the unit attacks.</summary>
    NotWhileAttacking = 0x08,
    /// <summary><c>AAS_Idle_Attack_Buildings</c>: structures are victims too.</summary>
    AttackBuildings = 0x10,
}

/// <summary>
/// <c>AIUpdateInterface</c>: moves the object toward <see cref="Entity.MoveTargetX"/>/<see cref="Entity.MoveTargetY"/>. In a world
/// with a terrain it follows an A* path over the <see cref="Terrain.NavigationGrid"/> (recomputed when the set of structure
/// footprints changes), turns at the locomotor's turn rate, slows for sharp corners and the final approach, and stays on the ground.
/// Without a terrain it moves in a straight line and turns instantly (the phase 5 mode). Sleeps while idle; <c>SimulationWorld</c> wakes it the instant
/// a <c>SetMoveTargetCommand</c> is accepted. The dock state (<c>AI_DOCK</c> with its <see cref="DockMachine"/>) is the one other
/// state of the main machine that exists here: an order (<see cref="OrderMove"/>, <see cref="OrderDock"/>) cancels it, exactly as any
/// new AI command replaces the current state in the reference. The supply-truck, worker and dozer modules derive from this class.
/// </summary>
public class AIUpdateInterface(AIUpdateData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private DockMachine? _dock;
    private bool _lastCommandFromPlayer;
    private EntityId _enterTarget;
    private EntityId _goal;
    private readonly List<PathPoint> _path = [];
    private int _pathIndex;
    private bool _pathValid;
    private bool _pathComplete;
    private ulong _pathObstacles;
    // The movement profile (SimulationWorld.PathQueue.cs): what this object asked the world's path queue for and is waiting for - none, a plan, or a detour around blockers.
    private PathWait _pathWait;

    private enum PathWait : byte { None = 0, Plan = 1, Detour = 2 }

    /// <summary>Whether the object waits for an answer of the world's path queue (the movement profile): it keeps the path it has, or stands, and keeps its order.</summary>
    public bool WaitingForPath => _pathWait != PathWait.None;
    private BuilderTask _task;
    private EntityId _taskTarget;
    private bool _approaching;
    private bool _evacuatePending;
    private bool _exitAfterEvacuate;
    private bool _returning;
    private Fixed _exitX;
    private Fixed _exitY;
    private static readonly Fixed MisalignedAngle = FixedTrig.PiFraction(1, 6);
    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");
    private static readonly int NoCollisionsBit = ObjectStatusBits.Index("NO_COLLISIONS");

    public override string ModuleName => "AIUpdateInterface";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    private enum BuilderTask : byte { None, Construct, Repair }

    /// <summary><c>AIUpdateInterface::isIdle</c>: no move order, not docking, no builder task and no evacuation waiting (<c>AI_EVACUATE</c>).</summary>
    public virtual bool IsIdle => _dock is null && !Entity.HasMoveTarget && _task == BuilderTask.None && !_evacuatePending && !_returning;

    /// <summary>The locomotor the object moves with right now (<c>AIUpdateInterface::getCurLocomotor</c>); aircraft switch between their locomotor sets (<c>JetAIUpdate::chooseLocomotorSet</c>).</summary>
    /// <summary>The locomotor set the object moves with right now (a jet picks its taxi, attack or return set).</summary>
    protected virtual LocomotorInfo? SelectedLocomotor => Entity.Template.Locomotor;

    /// <summary>The constants the object moves with: <see cref="SelectedLocomotor"/>, in its damaged form once the body is as damaged as <c>MovementPenaltyDamageState</c> (<c>Locomotor::getMaxSpeedForCondition</c> and its siblings).</summary>
    protected LocomotorInfo? ActiveLocomotor => SelectedLocomotor?.ForCondition(Entity.Body is ActiveBody body ? body.DamageState : BodyDamageStates.Pristine);

    /// <summary>
    /// The object flies <paramref name="locomotor"/> as a fixed-wing aircraft (<c>Appearance = WINGS</c>): <see cref="MoveWinged"/> instead of the ordinary air movement. Only <see cref="JetAIUpdate"/>
    /// answers yes; other aircraft AIs (bombers on a delivery run, gunships, drones, missiles) keep the movement they had (<c>docs/SIM-JET-FLIGHT.md</c>).
    /// </summary>
    protected virtual bool FliesWithWings(LocomotorInfo locomotor) => false;

    /// <summary>A winged aircraft's move order ended at (<paramref name="x"/>, <paramref name="y"/>) without stopping: it has to keep flying, which the derived AI does by circling there.</summary>
    protected virtual void OnWingedArrival(Fixed x, Fixed y) { }

    /// <summary>Dozers and workers can raise structures (<c>DozerAIInterface</c>); every other AI cannot.</summary>
    public virtual bool CanBuild => false;

    /// <summary>The object is busy constructing or repairing (<c>DozerPrimaryStateMachine</c> outside its idle state).</summary>
    public bool IsBuilding => _task != BuilderTask.None;

    public bool CanRepair => CanBuild && data.RepairRatePerSecond > Fixed.Zero;

    /// <summary><c>ActionManager::canRepairObject</c>, reduced to the rules that exist here: a complete, unsold structure that is hurt.</summary>
    public static bool IsRepairable(Entity target)
        => target.Template.IsKindOf("STRUCTURE") && !target.EffectivelyDead && !target.HasStatus(ObjectStatusBits.UnderConstruction)
           && !target.HasStatus(SoldBit) && target.Body is { } body && body.Health < body.MaxHealth;

    /// <summary>The main machine is in <c>AI_DOCK</c>.</summary>
    public bool IsDocking => _dock is not null;

    /// <summary>The object has reached its dock's entry and not yet left through the exit: the span in which the reference shows the <c>DOCKING</c> model condition (the net of a supply Chinook).</summary>
    public bool IsInsideDock => _dock is { Inside: true };

    /// <summary>The dock works on this object (<c>onDockReached</c> .. <c>onExitReached</c>): the span of <c>MODELCONDITION_DOCKING_ACTIVE</c> on the docker and on the dock.</summary>
    public bool IsDockActive => _dock is { Active: true };

    /// <summary>The dock this object is docking at (invalid when it is not between approach and the end of the dock machine).</summary>
    public EntityId DockingAt => _dock?.DockId ?? EntityId.Invalid;

    /// <summary><c>getLastCommandSource() == CMD_FROM_PLAYER</c>.</summary>
    public bool LastCommandFromPlayer => _lastCommandFromPlayer;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    /// <summary>
    /// The object moves, but nobody ordered it to: it drives out of the factory that built it, goes on to the rally point, or makes room for the next unit. It is not idle
    /// (<see cref="IsIdle"/>), yet free for its owner - any order replaces this move. Derived from the order state: the last move was not a command, and nothing else is going on.
    /// </summary>
    public bool IsOnUnorderedMove => Entity.HasMoveTarget && !_lastCommandFromPlayer && _dock is null && _task == BuilderTask.None && !_enterTarget.IsValid && !_goal.IsValid
                                     && !_evacuatePending && !_returning;

    /// <summary>The <c>AutoAcquireEnemiesWhenIdle</c> bits of the module data.</summary>
    public AutoAcquire AutoAcquire => data.AutoAcquire;

    /// <summary><c>MoodAttackCheckRate</c> in ticks: how often the unit looks for a victim by itself.</summary>
    public long MoodCheckTicks => data.MoodCheckTicks;

    /// <summary>
    /// <c>aiMoveToPosition</c>: replaces the current state with a move. With <paramref name="attackMove"/> it is <c>aiAttackMoveToPosition</c> (<c>AI_ATTACK_MOVE_TO</c>): the world
    /// lets the unit engage what it meets on the way and resume the march afterwards (docs/SIMULATION-CORE.md §33). An AI that has to finish something first (a parked jet takes off)
    /// carries the flag along with the stored order.
    /// </summary>
    public virtual void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        // AIUpdateInterface::privateMoveToPosition: "the dead don't listen very well".
        if (Entity.EffectivelyDead && IsGroundMover) return;
        world.EndAttack(Entity);
        if (attackMove) world.BeginAttackMove(Entity, x, y);
        CancelDock(world);
        ClearTask();
        _enterTarget = EntityId.Invalid;
        _goal = EntityId.Invalid;
        _evacuatePending = false;
        _exitAfterEvacuate = false;
        _returning = false;
        _lastCommandFromPlayer = fromPlayer;
        EndExitPath();
        Entity.HasMoveTarget = true;
        Entity.MoveTargetX = x;
        Entity.MoveTargetY = y;
        InvalidatePath();
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    private bool _exitRallyPending;
    private Fixed _exitRallyX;
    private Fixed _exitRallyY;

    // A point the path of the current move has to pass first (the door of the factory a unit leaves); gone once the object has been there or got another order.
    private bool _viaPending;
    private Fixed _viaX;
    private Fixed _viaY;

    /// <summary>
    /// <c>aiFollowExitProductionPath</c> (<c>AI_FOLLOW_EXIT_PRODUCTION_PATH</c>) for a unit its factory just created: a move to the place in front of the factory whose path
    /// first passes the door point, and, when the player set a rally point, a second move there once the first has ended. The path search leaves the footprint the unit
    /// stands in by itself (the reference ignores the factory as an obstacle on this path). Any later order replaces the whole path.
    /// </summary>
    public void OrderExitProduction(IWorldServices world, Fixed doorX, Fixed doorY, Fixed exitX, Fixed exitY, bool hasRally, Fixed rallyX, Fixed rallyY)
    {
        OrderMove(world, exitX, exitY, false);
        _viaPending = true;
        _viaX = doorX;
        _viaY = doorY;
        _exitRallyPending = hasRally;
        _exitRallyX = rallyX;
        _exitRallyY = rallyY;
    }

    /// <summary>
    /// The exit path is one order (<c>AI_FOLLOW_EXIT_PRODUCTION_PATH</c>): whatever replaces or stops its move - another move, a dock, a builder task, an attack the world
    /// drives, a stop - ends it, door point and second leg included. Only the arrival at the end of the first leg goes on to the rally point.
    /// </summary>
    private void EndExitPath()
    {
        _exitRallyPending = false;
        _viaPending = false;
        _exitFrom = EntityId.Invalid;
    }

    // The container the object is walking out of (OpenContain::exitObjectViaDoor: aiFollowPath with the container as the ignored object); gone with the first leg or any other order.
    private EntityId _exitFrom;

    // OpenContain::exitObjectViaDoor, setIgnoreCollisionTime(LOGICFRAMES_PER_SECOND): "the units often come out at the same position, and need to ignore collisions briefly as they
    // move out". The tick up to which the object's steps are not shoved aside by other units; zero when no exit is recent.
    private long _ignoreCollisionsUntil;

    /// <summary><c>AIUpdateInterface::getIgnoreCollisionsUntil</c>: the object left a container less than a second ago and walks through whoever stands at the door.</summary>
    internal bool IgnoresCollisions(long tick) => tick < _ignoreCollisionsUntil;

    /// <summary>
    /// The rider's half of <c>OpenContain::exitObjectViaDoor</c> and of <c>OpenContain::scatterToNearbyPosition</c>: <c>aiFollowPath</c> / <c>aiMoveToPosition</c> to
    /// (<paramref name="x"/>, <paramref name="y"/>) with <paramref name="container"/> as the ignored obstacle, then on to the container's rally point when it has one. With
    /// <paramref name="viaDoor"/> the rider also ignores collisions with other units for one second of game time (<c>setIgnoreCollisionTime</c>), which the scatter of a dead
    /// transport's riders does not ask for. A rider that already stands on the point gets no move (the original skips path points closer than one cell).
    /// </summary>
    public void OrderExitContainer(IWorldServices world, Entity container, Fixed x, Fixed y, bool viaDoor, bool hasRally = false, Fixed rallyX = default, Fixed rallyY = default)
    {
        if (Entity.Template.Locomotor is null) return;
        if (viaDoor) _ignoreCollisionsUntil = world.Tick + world.Time.OriginalFrames(SimulationRate.OriginalFramesPerSecond);
        var dx = x - Entity.X;
        var dy = y - Entity.Y;
        if (dx != Fixed.Zero || dy != Fixed.Zero)
        {
            OrderMove(world, x, y, false);
            _exitFrom = container.Id;
        }
        else world.MarkDirty(Entity);
        _exitRallyPending = hasRally;
        _exitRallyX = rallyX;
        _exitRallyY = rallyY;
        if (hasRally) world.WakeAt(Entity, this, world.Tick);
    }

    /// <summary>The order of <see cref="OrderExitProduction"/> still stands and the unit has not been through the door yet; gives the door point.</summary>
    internal bool TryGetExitOrder(out Fixed doorX, out Fixed doorY)
    {
        doorX = _viaX;
        doorY = _viaY;
        return _viaPending && Entity.HasMoveTarget;
    }

    /// <summary>
    /// Above 30 Hz: the place in front of the factory the standing exit order ends at, chosen at the end of the logic frame of the original the unit appeared in
    /// (<c>SimulationWorld.ProcessExitClaims</c>). The unit has not moved yet; its first step is planned to this goal.
    /// </summary>
    internal void RetargetExit(IWorldServices world, Fixed exitX, Fixed exitY)
    {
        if (Entity.MoveTargetX == exitX && Entity.MoveTargetY == exitY) return;
        Entity.MoveTargetX = exitX;
        Entity.MoveTargetY = exitY;
        InvalidatePath();
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>aiEnter</c> (<c>AI_ENTER</c>): moves to the container and enters it on arrival if it still accepts the object. An immobile object
    /// or a missing container ignores the order, as <c>privateEnter</c> does.
    /// </summary>
    public virtual void OrderEnter(IWorldServices world, Entity container, bool fromPlayer)
    {
        if (Entity.Template.Locomotor is null) return;
        var (containerX, containerY, _) = container.FrameEnd(world.Tick);
        OrderMove(world, containerX, containerY, fromPlayer);
        _enterTarget = container.Id;
        // AIEnterState::onEnter tells the container that somebody wants to enter (onObjectWantsToEnterOrExit): a Chinook lands for its boarders (ChinookAIUpdate.HasBoarders).
        foreach (var module in container.Modules)
            if (module is ChinookAIUpdate chinook) world.WakeAt(container, chinook, world.Tick + SimulationTiming.SleepNone);
    }

    /// <summary>The container this object is on its way into (<c>AIEnterState</c>'s goal object), or none.</summary>
    public EntityId EnterTarget => _enterTarget;

    /// <summary>
    /// <c>AIUpdateInterface::getGoalObject</c>: the object the current order aims at (a hijacker's car, a saboteur's building, a pilot's vehicle).
    /// <c>CrateCollide</c> subclasses with a purpose of their own only fire when the object they touch is this goal, so walking past is harmless.
    /// </summary>
    public EntityId GoalObject => _goal;

    /// <summary>
    /// <c>AIUpdateInterface::getIgnoredObstacleID</c>: the one object this unit's steps and detours do not treat as an obstacle, because the order ends inside it — the goal
    /// object of an approach, or the container of an enter order (<c>AIEnterState::onEnter</c>, AIStates.cpp: "tell the pathfinder to ignore the enterable object").
    /// </summary>
    internal EntityId IgnoredObstacle => _goal.IsValid ? _goal : _enterTarget.IsValid ? _enterTarget : _exitFrom;

    /// <summary>
    /// The approach half of <c>aiEnter</c>/<c>aiHijack</c>-style orders that end in a collision instead of a container entry: moves to <paramref name="goal"/> (following
    /// it when it moves) and, once the footprints overlap or the move ends within reach, lets this object's <see cref="ICollideModule"/>s act on it. An immobile object
    /// ignores the order. Replaced by any later order.
    /// </summary>
    public void OrderApproachGoal(IWorldServices world, Entity goal, bool fromPlayer)
    {
        if (Entity.Template.Locomotor is null) return;
        // The goal is where the frame it has begun ends (rule 3 of docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3); 30 Hz: its position.
        var (goalX, goalY, _) = goal.FrameEnd(world.Tick);
        OrderMove(world, goalX, goalY, fromPlayer);
        _goal = goal.Id;
    }

    /// <summary><c>aiIdle</c>: drops the move order and the goal object; the object stands where it is.</summary>
    public void OrderIdle(IWorldServices world, bool fromPlayer = false)
    {
        world.EndAttack(Entity);
        CancelDock(world);
        ClearTask();
        _enterTarget = EntityId.Invalid;
        _goal = EntityId.Invalid;
        _evacuatePending = false;
        _exitAfterEvacuate = false;
        _returning = false;
        _lastCommandFromPlayer = fromPlayer;
        StopInternalMove();
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>aiGetHealed</c> (<c>privateGetHealed</c>): <c>ActionManager::canGetHealedAt</c> — the object is hurt infantry, the pad is a living, complete,
    /// unsold ally with <c>HEAL_PAD</c> — then <see cref="OrderEnter"/>. The shroud check has no counterpart (no fog of war).
    /// </summary>
    public void OrderGetHealed(IWorldServices world, Entity pad, bool fromPlayer)
    {
        if (world.RelationshipOf(Entity.Owner, pad.Owner) != Relationship.Allies) return;
        if (pad.EffectivelyDead) return;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction) || pad.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        if (pad.HasStatus(SoldBit)) return;
        if (!Entity.Template.IsKindOf("INFANTRY") || !pad.Template.IsKindOf("HEAL_PAD")) return;
        if (Entity.Body is { } body && body.Health == body.MaxHealth) return;
        OrderEnter(world, pad, fromPlayer);
    }

    /// <summary><c>aiIdle</c>: cancels whatever the object is doing (move, dock, builder task, pending evacuation).</summary>
    public virtual void OrderIdle(IWorldServices world)
    {
        world.EndAttack(Entity);
        CancelDock(world);
        ClearTask();
        _enterTarget = EntityId.Invalid;
        _evacuatePending = false;
        _exitAfterEvacuate = false;
        _returning = false;
        StopInternalMove();
        world.MarkDirty(Entity);
    }

    private bool IsGroundMover => Entity.Template.Locomotor is { } moves && (moves.Surfaces & LocomotorSurfaces.Air) == 0;

    /// <summary>
    /// <c>AIUpdateInterface::markAsDead</c> (called by <c>SlowDeathBehavior::onDie</c>) and <c>AIDeadState</c> (AIStates.cpp: <c>setLocomotorGoalNone</c> in every update): a
    /// dying ground object heads nowhere any more - its move, dock, builder task and approach end where the logic frame it has begun ends, and it takes no further order.
    /// What dies in the air is moved by its own death module (helicopter, jet) and is left alone. Not modeled: the coasting to a stop (a vehicle rolls out by its
    /// physics, an infantryman loses a fifth of his speed per frame).
    /// </summary>
    internal void StopAtDeath(IWorldServices world, bool settled)
    {
        if (!IsGroundMover) return;
        if (!Entity.HasMoveTarget && _dock is null && _task == BuilderTask.None && !_enterTarget.IsValid && !_goal.IsValid && !_evacuatePending && !_returning && !_exitRallyPending) return;
        // Where it stops is the same at every tick rate: where the logic frame of the original ends in which it died. Above 30 Hz the world has put an object that was
        // covering a frame's step on that frame's end (settled). An object that has not had its turn in this tick takes the step of this frame first - at 30 Hz the whole
        // step of the tick, above the step of the frame its update begins now - and stops then (Update); one that has had it stops here.
        if (!settled && Entity.HasMoveTarget && _turnTick != world.Tick)
        {
            _dyingSince = world.Tick;
            world.WakeAt(Entity, this, world.Tick);
            world.MarkDirty(Entity);
            return;
        }
        StopForDeath(world);
    }

    // The tick the module last had its turn in (derived within a tick, not state), and the tick of a death whose stop waits for the step of that tick (written while it waits).
    private long _turnTick = -1;
    private long _dyingSince;

    private void StopForDeath(IWorldServices world)
    {
        _dyingSince = 0;
        CancelDock(world);
        ClearTask();
        _enterTarget = EntityId.Invalid;
        _goal = EntityId.Invalid;
        _evacuatePending = false;
        _exitAfterEvacuate = false;
        _returning = false;
        StopInternalMove();
        world.MarkDirty(Entity);
    }

    /// <summary><c>aiDock</c>: replaces the current state with <c>AI_DOCK</c>; a dock that cannot be entered ends at once and leaves the object idle.</summary>
    public virtual void OrderDock(IWorldServices world, Entity dock, bool fromPlayer)
    {
        world.EndAttack(Entity);
        CancelDock(world);
        ClearTask();
        _enterTarget = EntityId.Invalid;
        _goal = EntityId.Invalid;
        _evacuatePending = false;
        _lastCommandFromPlayer = fromPlayer;
        EndExitPath();
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        ClearFrameSpeed();
        InvalidatePath();
        _dock = new DockMachine(dock.Id);
        if (_dock.Enter(world, this) != DockStatus.Continue) EndDock(world);
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    /// <summary><c>DozerAIInterface::construct</c>: walk to the site and add work to it every tick until it is complete.</summary>
    public void OrderConstruct(IWorldServices world, Entity site) => StartTask(world, BuilderTask.Construct, site);

    /// <summary>
    /// <c>aiEvacuate</c> / <c>aiMoveToAndEvacuate</c> (<c>AI_EVACUATE</c>, <c>AI_MOVE_AND_EVACUATE</c>): unload every passenger once the object stands
    /// still (after moving to (x, y) when <paramref name="move"/> is set) and <see cref="ReadyToUnload"/> agrees. Replaces the current state.
    /// </summary>
    public virtual void OrderEvacuate(IWorldServices world, bool move, Fixed x, Fixed y, bool thenExit = false)
    {
        // aiMoveToAndEvacuateAndExit: the place the order was given is where the transport returns to (AIMoveAndEvacuateState::m_origin) and is deleted.
        var originX = Entity.X;
        var originY = Entity.Y;
        if (move && Entity.Template.Locomotor is not null) OrderMove(world, x, y, true);
        else
        {
            CancelDock(world);
            ClearTask();
            _enterTarget = EntityId.Invalid;
            _returning = false;
            StopInternalMove();
        }
        _exitAfterEvacuate = thenExit && Entity.Template.Locomotor is not null;
        _exitX = originX;
        _exitY = originY;
        _evacuatePending = true;
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    /// <summary>An evacuation order waits for the object to stand still and be ready (<see cref="ReadyToUnload"/>).</summary>
    public bool EvacuatePending => _evacuatePending;

    /// <summary><c>getAiFreeToExit</c>: whether passengers may leave right now (a Chinook has to land first).</summary>
    protected virtual bool ReadyToUnload(IWorldServices world) => true;

    /// <summary><c>DozerAIInterface::newTask(DOZER_TASK_REPAIR)</c>: walk to the structure and heal it until it is at full health.</summary>
    public void OrderRepair(IWorldServices world, Entity target) => StartTask(world, BuilderTask.Repair, target);

    private void StartTask(IWorldServices world, BuilderTask task, Entity target)
    {
        world.EndAttack(Entity);
        CancelDock(world);
        _enterTarget = EntityId.Invalid;
        _goal = EntityId.Invalid;
        _evacuatePending = false;
        StopInternalMove();
        InvalidatePath();
        _task = task;
        _taskTarget = target.Id;
        _approaching = false;
        _lastCommandFromPlayer = true;
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    private void ClearTask()
    {
        _task = BuilderTask.None;
        _taskTarget = EntityId.Invalid;
        _approaching = false;
    }

    private void EndTask(IWorldServices world)
    {
        ClearTask();
        StopInternalMove();
        OnBuilderTaskEnded(world);
    }

    /// <summary>Called when a task finishes by itself (never when a new order replaces it).</summary>
    protected virtual void OnBuilderTaskEnded(IWorldServices world) { }

    private void UpdateTask(IWorldServices world)
    {
        var target = world.FindEntity(_taskTarget);
        // Whether the task is still worth doing is looked at once per logic frame of the original of the builder (every tick at 30 Hz): what happened to the target after the
        // builder's turn in the first tick of a frame is seen with the next frame, as the 30 Hz builder sees it with its next step.
        if (world.Time.BeginsFrameOf(Entity, world.Tick))
        {
            if (target is null || target.DestroyRequested || target.EffectivelyDead) { EndTask(world); return; }
            if (_task == BuilderTask.Construct && (!target.HasStatus(ObjectStatusBits.UnderConstruction) || target.BuilderId != Entity.Id)) { EndTask(world); return; }
            if (_task == BuilderTask.Repair && !IsRepairable(target)) { EndTask(world); return; }
        }
        else if (target is null) return;
        // Inside a logic frame's step (above 30 Hz) the builder is between two positions of the 30 Hz walk: it measures its reach again when the step is covered.
        if (_frameTicksLeft > 0 && Entity.HasMoveTarget) return;

        var reach = target.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        if (!SquaredReach.Of(reach).Contains(dx * dx + dy * dy))
        {
            if (Entity.HasMoveTarget) { _approaching = true; return; }
            // The approach ended (blocked or as close as the grid allows) without getting within reach: give up.
            if (_approaching || !StartInternalMove(target.X, target.Y)) { EndTask(world); return; }
            _approaching = true;
            return;
        }
        StopInternalMove();
        _approaching = false;
        if (_task == BuilderTask.Construct)
        {
            if (world.AdvanceConstruction(target)) EndTask(world);
            return;
        }
        var body = target.Body!;
        if (world.Time.TicksPerOriginalFrame != TickOrder.Next)
        {
            // Above 30 Hz the repair heals the slice of a logic frame of the original, once per frame of the builder (the escape "Originalraster" for a heal slice,
            // docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md §3): a hit in the first tick of a frame meets the health the 30 Hz step leaves - with a share per tick it met only
            // one share of that frame's repair, and a threshold of the body (the fear roll at a quarter of the health) was crossed in different frames.
            if (!world.Time.BeginsFrameOf(Entity, world.Tick)) return;
            var slice = body.MaxHealth * data.RepairRatePerSecond / Fixed.FromInt(SimulationRate.OriginalFramesPerSecond);
            world.Damage(target.Id, new DamageInfo(Entity.Id, slice, DamageTypes.Healing, DeathTypes.None, false));
            return;
        }
        var amount = world.Time.PerSecondToPerTick(body.MaxHealth * data.RepairRatePerSecond);
        world.Damage(target.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
    }

    /// <summary>The internal move of a state (approach, advance): false when the object has no locomotor.</summary>
    internal bool StartInternalMove(Fixed x, Fixed y)
    {
        if (Entity.Template.Locomotor is null) return false;
        EndExitPath();
        Entity.HasMoveTarget = true;
        Entity.MoveTargetX = x;
        Entity.MoveTargetY = y;
        InvalidatePath();
        return true;
    }

    internal void StopInternalMove()
    {
        EndExitPath();
        if (!Entity.HasMoveTarget) return;
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        ClearFrameSpeed();
        // A stopped object does not cover the rest of the frame's step it was on.
        _frameTicksLeft = 0;
        InvalidatePath();
    }

    /// <summary>
    /// <see cref="StopInternalMove"/> for an observer that acts after the updates of the tick (the units' own target acquisition). At 30 Hz the object has taken the whole step
    /// of this tick by then and its next step is a tick later. Above 30 Hz it is put where the frame it has begun ends first (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3,
    /// rule 4), and whatever move it is given now starts with its next frame, not with the next tick. 30 Hz: <see cref="StopInternalMove"/>.
    /// </summary>
    internal void StopInternalMoveAtFrameEnd(IWorldServices world)
    {
        var ticks = world.Time.TicksPerOriginalFrame;
        if (ticks > 1 && Entity.HasMoveTarget)
        {
            if (Entity.IsInsideFrameStep || Entity.HasAheadAngle) Entity.CompleteFrame(world.Tick);
            _moveHoldUntil = world.Tick + ticks;
        }
        StopInternalMove();
    }

    /// <summary>
    /// A move given to a standing object by an observer that acts after the updates of the tick (the units' own target acquisition): at 30 Hz its first step is the next
    /// tick's, one logic frame of the original later. Above 30 Hz a module that is awake every tick (a transport, a supply carrier) would take it in the next tick and
    /// count its frames from there, a share of a frame ahead of everything in phase; the first decision waits for the object's next frame. An object that is covering a
    /// frame's step decides when that is covered anyway. Nothing to do at 30 Hz.
    /// </summary>
    internal void BeginMoveWithNextFrame(IWorldServices world)
    {
        var ticks = world.Time.TicksPerOriginalFrame;
        if (ticks > 1 && _frameTicksLeft == 0) _moveHoldUntil = Math.Max(_moveHoldUntil, world.Tick + ticks);
    }

    // Above 30 Hz only: the tick before which the object takes no decision of a new frame (StopInternalMoveAtFrameEnd). Never set at 30 Hz.
    private long _moveHoldUntil;

    private void InvalidatePath()
    {
        // A new order during the last frame of a move: the object still covers that frame's step, but it has not arrived.
        _frameArriving = false;
        _pathValid = false;
        _pathComplete = false;
        _pathIndex = 0;
        _path.Clear();
        _pathWait = PathWait.None;
    }

    private void CancelDock(IWorldServices world)
    {
        if (_dock is not null) EndDock(world);
    }

    private void EndDock(IWorldServices world)
    {
        var machine = _dock!;
        _dock = null;
        machine.Halt(world, this);
    }

    private long _paralyzedUntil;

    /// <summary>
    /// <c>Object::setDisabledUntil(DISABLED_PARALYZED, frame)</c> for an object with AI (<c>BattlePlanUpdate</c> paralyzes the troops of a changing battle plan): until <paramref name="tick"/> the AI does not run
    /// at all (<c>AIUpdateInterface::getDisabledTypesToProcess</c> only lets <c>DISABLED_HELD</c> through), so the object stands still with its order intact and carries on afterwards.
    /// </summary>
    public void ParalyzeUntil(IWorldServices world, long tick)
    {
        _paralyzedUntil = Math.Max(_paralyzedUntil, tick);
        Entity.Speed = Fixed.Zero;
        ClearFrameSpeed();
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public virtual long Update(IWorldServices world)
    {
        // A death whose stop waits for this turn (StopAtDeath): the step of the frame the object died in is taken below, then it stands. A wait that was not
        // answered in the tick of the death (the module did not run) ends without a step.
        var dying = _dyingSince != 0;
        var late = dying && _dyingSince != world.Tick;
        _turnTick = world.Tick;
        var sleep = late ? SimulationTiming.SleepForever : UpdateLiving(world);
        if (!dying) return sleep;
        if (_frameTicksLeft > 0) SettleFrame(world.Tick);
        StopForDeath(world);
        return SimulationTiming.SleepForever;
    }

    private long UpdateLiving(IWorldServices world)
    {
        if (Entity.EmpDisabledUntil > world.Tick || (Entity.ScriptDisableMask & Entity.ScriptHeld) != 0)
        {
            // DISABLED_EMP (EMPUpdate): the object cannot act until the disable runs out; its orders stay.
            Entity.Speed = Fixed.Zero;
            ClearFrameSpeed();
            return SimulationTiming.SleepNone;
        }
        if (world.Tick < _paralyzedUntil) return SimulationTiming.SleepNone;
        // The dock machine is a state machine of the original's logic frame: its states change once per frame, so that "the update after the arrival" is a frame later at every
        // rate. While the dock works on the object (ProcessDock) the machine runs every tick: the docks' actions are written per tick (a repair dock heals a share per tick), and
        // the action delays of the carriers lie on the frame grid by themselves.
        if (_dock is not null && (_dock.Active || world.Time.IsOriginalFrameBoundary(world.Tick)) && _dock.Update(world, this) != DockStatus.Continue) EndDock(world);
        if (_task != BuilderTask.None) UpdateTask(world);
        if (Entity.HasMoveTarget)
        {
            if (ActiveLocomotor is not { } locomotor)
            {
                // No locomotor resolved (e.g. only a non-SET_NORMAL set is defined): drop the stale order rather than spin forever.
                Entity.HasMoveTarget = false;
                InvalidatePath();
            }
            else Move(world, locomotor);
        }
        else if (_frameTicksLeft > 0) Coast(world);
        // Above 30 Hz the second leg is ordered with the last tick of the unit's logic frame of the original, where the 30 Hz step that ends the first leg orders it
        // (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3): an arrival ends there by itself, a first leg that ends in the first tick of a frame (no way out) waits for it.
        if (_exitRallyPending && !Entity.HasMoveTarget && world.Time.BeginsFrameOf(Entity, world.Tick + TickOrder.Next))
        {
            // The first leg of the exit path is done (the unit is out of its factory): on to the rally point.
            _exitRallyPending = false;
            // exitObjectViaDoor adjusts the rally point for the unit (Pathfinder::adjustDestination): the units of a factory do not all end on the same spot.
            var (rallyX, rallyY) = world.FindFreeGoal(Entity, Entity.X, Entity.Y, _exitRallyX, _exitRallyY);
            OrderMove(world, rallyX, rallyY, false);
        }
        if (_enterTarget.IsValid) UpdateEnter(world);
        if (_goal.IsValid) UpdateGoal(world);
        if (_evacuatePending && !Entity.HasMoveTarget && _dock is null && ReadyToUnload(world))
        {
            _evacuatePending = false;
            // The passengers get out where the move ended: above 30 Hz the object is put there first (it is still covering the last step).
            if (_frameTicksLeft > 0) SettleFrame(world.Tick);
            world.EvacuateContainer(Entity);
            if (_exitAfterEvacuate)
            {
                // AIMoveAndDeleteState: back to where the order was given, then deleted.
                _exitAfterEvacuate = false;
                _returning = StartInternalMove(_exitX, _exitY);
                if (!_returning) world.RequestDestroy(Entity.Id);
            }
        }
        else if (_returning && !Entity.HasMoveTarget)
        {
            _returning = false;
            world.RequestDestroy(Entity.Id);
        }
        // AIFollowPathState::update, "we have exited whatever object we are leaving": the leg out of the container is over.
        if (_exitFrom.IsValid && !Entity.HasMoveTarget && !_exitRallyPending)
        {
            _exitFrom = EntityId.Invalid;
            world.MarkDirty(Entity);
        }
        return _dock is not null || Entity.HasMoveTarget || _frameTicksLeft > 0 || _task != BuilderTask.None || _evacuatePending || _returning || _goal.IsValid || _exitRallyPending || _enterTarget.IsValid ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    /// <summary>
    /// The approach to the goal object of <see cref="OrderApproachGoal"/>: gone, dead or destroyed ends the order; a goal that has walked off is followed (the move is
    /// re-aimed once it is two cells away from where the object is heading); when the move has ended close enough the object's collide modules act on the goal once,
    /// the stand-in for the physics collision the reference waits for. The module may end the order itself (<see cref="OrderIdle"/>, destroying the object).
    /// </summary>
    private void UpdateGoal(IWorldServices world)
    {
        var goal = world.FindEntity(_goal);
        if (Entity.DestroyRequested || goal is null || goal.DestroyRequested || goal.EffectivelyDead)
        {
            _goal = EntityId.Invalid;
            StopInternalMove();
            return;
        }
        if (Entity.HasMoveTarget)
        {
            // Re-aimed where the 30 Hz step re-aims: once per logic frame, in the tick that decided the frame's step and after that decision, with the goal where the frame
            // it has begun ends (a goal that has its turn later in the tick is read after its previous step, as at 30 Hz). With the last share instead, the goal had
            // always made its whole step.
            if (_frameTicksLeft != Math.Max(_stride, 1) - 1) return;
            var slack = Fixed.FromInt(2 * TerrainMap.CellSize);
            var (aimX, aimY, _) = goal.FrameEnd(world.Tick);
            var gx = aimX - Entity.MoveTargetX;
            var gy = aimY - Entity.MoveTargetY;
            if (!SquaredReach.Of(slack).Contains(gx * gx + gy * gy)) StartInternalMove(aimX, aimY);
            return;
        }
        var reach = goal.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        // Both where the frame they have begun ends (above 30 Hz the object is still covering the last step of its move); the collision acts on the object standing there.
        var (ownX, ownY, _) = Entity.FrameEnd(world.Tick);
        var (goalX, goalY, _) = goal.FrameEnd(world.Tick);
        var dx = goalX - ownX;
        var dy = goalY - ownY;
        var near = SquaredReach.Of(reach).Contains(dx * dx + dy * dy);
        if (near && _frameTicksLeft > 0) SettleFrame(world.Tick);
        if (near)
            foreach (var module in Entity.Modules)
            {
                if (module is not ICollideModule collide) continue;
                if (Entity.DestroyRequested || goal.DestroyRequested || Entity.HasStatus(NoCollisionsBit)) break;
                collide.OnCollide(world, goal);
            }
        // Whatever happened, a finished approach is over: a module that wanted to keep the order called OrderApproachGoal again itself.
        if (_goal == goal.Id && !Entity.HasMoveTarget) _goal = EntityId.Invalid;
    }

    private void UpdateEnter(IWorldServices world)
    {
        var container = world.FindEntity(_enterTarget);
        // Whether the container is still there is looked at once per logic frame of the original of this object, like every other look at another object: a container that
        // dies in the phases after the updates is found dead by the next 30 Hz step, not by the next tick of the same frame.
        if (Entity.HasMoveTarget && _frameTicksLeft != Math.Max(_stride, 1) - 1) return;
        if (container is null || container.DestroyRequested || container.EffectivelyDead)
        {
            _enterTarget = EntityId.Invalid;
            // The 30 Hz step has made its whole step before it finds the container gone: the object stops where the frame it has begun ends.
            if (Entity.HasMoveTarget && _frameTicksLeft > 0) SettleFrame(world.Tick);
            StopInternalMove();
            return;
        }
        // AIEnterState::update (fidelity finding F-38): a boarder on the ground gives up on a container that is itself carried by something in the air (rangers that were to
        // board a humvee the Chinook has taken up), and waits at the end of its approach while its container is in the air - it goes in only once the container is down.
        var carriedAway = container.ContainedBy.IsValid && AboveTerrain(world, container) && !AboveTerrain(world, Entity);
        // The rule fix enter-needs-vertical-overlap (the community source): the wait is for an overlap in height, not for the container to be on the ground.
        var waitForLanding = world.Templates.Rules.Has(Rules.GameFix.EnterNeedsVerticalOverlap) ? !VerticalOverlap(Entity, container)
            : AboveTerrain(world, container) && !AboveTerrain(world, Entity);
        if (carriedAway)
        {
            _enterTarget = EntityId.Invalid;
            if (Entity.HasMoveTarget && _frameTicksLeft > 0) SettleFrame(world.Tick);
            StopInternalMove();
            return;
        }
        if (Entity.HasMoveTarget)
        {
            // OpenContain::onCollide: an object whose enter target is this container is taken in as soon as the two touch; it does not have to reach the container's centre
            // (which another boarder, or a rider that found no room and stands there, may keep it from). A structure keeps the old rule (the walk ends at its wall): its
            // bounding circle reaches far beyond a box-shaped footprint, and nothing stands inside it.
            // Decided where the 30 Hz step decides it (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md): in the tick that decided a
            // logic frame's step, with both where that frame ends; the boarder is put there and goes in, in the first tick of the frame like a move that ends.
            if (container.Template.IsKindOf("STRUCTURE") || _frameTicksLeft != Math.Max(_stride, 1) - 1) return;
            var touch = container.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius;
            var (endX, endY, _) = Entity.FrameEnd(world.Tick);
            var (insideX, insideY, _) = container.FrameEnd(world.Tick);
            var tx = insideX - endX;
            var ty = insideY - endY;
            if (!SquaredReach.Of(touch).Contains(tx * tx + ty * ty)) return;
            // The partition manager's collision is three-dimensional (PartitionManager.cpp, the height test of the geometries): a container hovering above the boarder is not
            // touched, the approach goes on to its end.
            if (!VerticalOverlap(Entity, container)) return;
            if (_frameTicksLeft > 0) SettleFrame(world.Tick);
            StopInternalMove();
        }
        if (waitForLanding) return;
        _enterTarget = EntityId.Invalid;
        // The approach may end short (a partial path): only an object that actually reached the container gets in.
        var reach = container.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        // Measured from where the last step of the move ends (above 30 Hz the object is still covering it).
        var (ownX, ownY, _) = Entity.FrameEnd(world.Tick);
        var dx = container.X - ownX;
        var dy = container.Y - ownY;
        if (!SquaredReach.Of(reach).Contains(dx * dx + dy * dy)) return;
        world.TryEnterContainer(Entity, container);
    }

    /// <summary><c>Object::isAboveTerrain</c>: the object's position lies above the ground under it.</summary>
    private static bool AboveTerrain(IWorldServices world, Entity entity) => entity.Z > world.GroundHeightAt(entity.X, entity.Y);

    private static bool VerticalOverlap(Entity a, Entity b)
        => a.Z + ToppleAndFallModules.MaxHeightAbovePosition(a.Template) >= b.Z && a.Z <= b.Z + ToppleAndFallModules.MaxHeightAbovePosition(b.Template);

    private void Move(IWorldServices world, LocomotorInfo locomotor)
    {
        if ((locomotor.Surfaces & LocomotorSurfaces.Air) != 0 && FliesWithWings(locomotor))
        {
            MoveWinged(world, locomotor);
            return;
        }
        if (_stride == 0) ResolveBlockerTiming(world.Time);
        if (world.Tick < _moveHoldUntil) return;
        if (_frameTicksLeft > 0)
        {
            // Between two decisions the object only covers the next share of the step its last decision made.
            if (world.Tick == _frameDue)
            {
                AdvanceFrame(world, locomotor);
                return;
            }
            if (_frameDue == world.Tick + TickOrder.Next)
            {
                // The module runs a second time in this tick: an order of a module that had its turn after it woke it "now" (a transport calls a wounded soldier back). At
                // 30 Hz the second run is a second whole step. In the tick that decided the frame the step is completed first and the new one decided from where it ends,
                // as there; later in the frame the object goes on covering its step and decides when it is covered.
                if (_frameTicksLeft != _stride - 1) return;
                if (Entity.IsInsideFrameStep || Entity.HasAheadAngle) Entity.CompleteFrame(world.Tick);
            }
            // Else the object did not move in the tick the share was due in (disabled, stopped, another state of a derived AI): the rest of that step is void.
            _frameTicksLeft = 0;
            _frameArriving = false;
        }
        if (_stride > 1 && !_behindAnotherMover && Entity.AheadTick == world.Tick && (Entity.IsInsideFrameStep || Entity.HasAheadAngle))
        {
            DecideBehindAnotherMover(world, locomotor);
            return;
        }
        if (world.Navigation is not { } navigation)
        {
            MoveStraight(world, locomotor);
            return;
        }
        var air = (locomotor.Surfaces & LocomotorSurfaces.Air) != 0;
        if (_viaPending)
        {
            // Through the door: from here on a new plan goes straight to the goal.
            var viaX = _viaX - Entity.X;
            var viaY = _viaY - Entity.Y;
            if (air || SquaredReach.Of(Fixed.FromInt(TerrainMap.CellSize)).Contains(viaX * viaX + viaY * viaY)) _viaPending = false;
        }
        // Close to a goal somebody else has taken: head for the nearest free place instead (SimulationWorld.ExitGoals.cs, the reference's adjustDestination).
        if (!air && GoalSpotCheckDue(world) && world.TryFreeGoalSpot(Entity, out var freeX, out var freeY))
        {
            Entity.MoveTargetX = freeX;
            Entity.MoveTargetY = freeY;
            InvalidatePath();
        }
        if (!air && !_viaPending && world.PathsQueued)
        {
            if (!QueuedPath(world, navigation, locomotor)) return;
        }
        else if (!_pathValid || (!air && _pathObstacles != navigation.ObstacleSum)) PlanPath(navigation, locomotor, air);
        if (_path.Count == 0)
        {
            // Blocked: no cell next to the object can be entered. The order ends without an arrival.
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
            ClearFrameSpeed();
            InvalidatePath();
            return;
        }

        // Everything from here on is one decision of a logic frame of the original, in that frame's units (docs/rate-invariance/RATE-INVARIANCE-GROUND.md): above 30 Hz the object decides once per
        // frame and covers the step in shares, at 30 Hz the frame is the tick and these are the locomotor's own constants.
        FrameConstants(locomotor, out var topSpeed, out var acceleration, out var braking, out var turnRate, out var minSpeed);
        var arrivalRadius = Fixed.Max(locomotor.CloseEnoughDist, topSpeed);
        while (true)
        {
            var waypoint = _path[_pathIndex];
            var last = _pathIndex == _path.Count - 1;
            var reach = last ? arrivalRadius : topSpeed;
            var toX = waypoint.X - Entity.X;
            var toY = waypoint.Y - Entity.Y;
            if (!SquaredReach.Of(reach).Contains(toX * toX + toY * toY)) break;
            if (!last) { _pathIndex++; continue; }
            if (_stride > 1)
            {
                BeginArrival(world, locomotor, waypoint.X - Entity.X, waypoint.Y - Entity.Y);
                return;
            }
            Entity.X = waypoint.X;
            Entity.Y = waypoint.Y;
            if (!air) Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            var complete = _pathComplete;
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
            ClearFrameSpeed();
            InvalidatePath();
            if (complete) world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
            return;
        }

        var target = _path[_pathIndex];
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        var desired = FixedTrig.Atan2(dy, dx);
        var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
        var heading = Entity.Angle;
        Entity.Angle = turnRate > Fixed.Zero && difference.Abs() > turnRate
            ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turnRate : turnRate))
            : desired;

        var speed = FrameSpeed();
        var brakingDistance = locomotor.Braking > Fixed.Zero ? speed * speed / (braking * Fixed.FromInt(2)) : Fixed.Zero;
        var remaining = distance;
        if (distance <= brakingDistance && _pathIndex < _path.Count - 1) remaining += TailLength();
        var goalSpeed = remaining <= brakingDistance ? minSpeed : CappedFrameSpeed(topSpeed);
        if (FixedTrig.AngleDifference(Entity.Angle, desired).Abs() > MisalignedAngle)
        {
            goalSpeed = Fixed.Min(goalSpeed, minSpeed);
            // Inside the turning circle a moving object would orbit the waypoint: stop and turn on the spot instead.
            if (turnRate > Fixed.Zero && distance < topSpeed / turnRate * Fixed.FromInt(2)) goalSpeed = Fixed.Zero;
        }
        speed = speed < goalSpeed
            ? Fixed.Min(goalSpeed, speed + acceleration)
            : Fixed.Max(goalSpeed, speed - braking);
        SetFrameSpeed(speed);
        // Standing units on the way (a dozer at work, a halted group) are unknown to the static path: plan once around them instead of rubbing along them while the local avoidance
        // below shoves the step aside (docs/DEMO-FINDINGS.md #12). At most once per second; a plan that no longer reaches the goal is dropped and the old path kept.
        if (!air && world.Tick - _blockerReplanTick >= _blockerReplanInterval && speed > Fixed.Zero && _pathWait == PathWait.None)
        {
            // Look-ahead: the travel of about 25 logic frames of the original (a walking infantry sees 17 units, a fast vehicle 60).
            var ahead = world.StationaryBlockersAhead(Entity, FixedTrig.Cos(Entity.Angle) * speed * BlockerLookAhead, FixedTrig.Sin(Entity.Angle) * speed * BlockerLookAhead);
            if (ahead.Count > 0)
            {
                if (world.PathsQueued && !_viaPending)
                {
                    // The movement profile: the detour is a question to the world's path queue; the path the object has stays until the answer comes.
                    _blockerReplanTick = world.Tick;
                    world.RequestPath(Entity, new PathQuery(Entity.X, Entity.Y, Entity.MoveTargetX, Entity.MoveTargetY, locomotor.Surfaces, [..ahead], true));
                    _pathWait = PathWait.Detour;
                    world.MarkDirty(Entity);
                }
                else ReplanAround(navigation, locomotor, ahead, world.Tick);
            }
        }

        // phase 10 (docs/SIMULATION-CORE.md, CnCNew.Simulation/World/SimulationWorld.Collision.cs): bounded local avoidance
        // against another unit's footprint, then the crush/topple collision check — both own-file, additive world services.
        var (stepX, stepY) = air
            ? (FixedTrig.Cos(Entity.Angle) * speed, FixedTrig.Sin(Entity.Angle) * speed)
            : world.AvoidLocalBlockers(Entity, FixedTrig.Cos(Entity.Angle) * speed, FixedTrig.Sin(Entity.Angle) * speed);
        if (_stride > 1)
        {
            BeginFrame(world, locomotor, stepX, stepY, heading);
            return;
        }
        Entity.X += stepX;
        Entity.Y += stepY;
        if (!air)
        {
            Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            world.ResolveFootprintCollisions(Entity, stepX, stepY);
        }
    }

    /// <summary>
    /// The move order of a fixed-wing aircraft (<c>Locomotor::moveTowardsPositionWings</c>, which is <c>moveTowardsPositionOther</c> for the 2D part): fly toward the target, turning by at most the
    /// locomotor's turn rate per logic frame of the original, at a speed that never drops below <see cref="LocomotorInfo.MinSpeed"/>. The order ends without stopping: an aircraft within one frame of travel of the target, or
    /// one whose target lies behind it inside its turning circle (it cannot hit a point there; the reference lets it orbit until it slowed down), has "arrived" and carries on flying —
    /// <see cref="OnWingedArrival"/> lets the derived AI circle. The reference slows an aircraft to its minimum speed once the distance to the goal fits the braking distance; so does this.
    /// Heading and speed are decided once per logic frame of the original and the frame's step is flown in equal shares per tick (<see cref="SteerWinged"/>).
    /// </summary>
    private void MoveWinged(IWorldServices world, LocomotorInfo locomotor)
    {
        if (_wingStep.Continue(Entity)) return;
        var targetX = Entity.MoveTargetX;
        var targetY = Entity.MoveTargetY;
        var distance = AirModules.Distance2D(Entity.X, Entity.Y, targetX, targetY);
        // One decision of a logic frame of the original in that frame's units, with the constants and the speed the 30 Hz step computes with (FlightFrameConstants).
        FlightFrameConstants(world.Time, locomotor, out var topSpeed, out _, out var braking, out var turnRate, out var minSpeed);
        var speed = FrameSpeed();
        var arrival = Fixed.Max(locomotor.CloseEnoughDist, topSpeed);
        var behind = distance > WingEpsilon && FixedTrig.AngleDifference(Entity.Angle, FixedTrig.Atan2(targetY - Entity.Y, targetX - Entity.X)).Abs() > FixedTrig.HalfPi;
        var arrived = distance <= arrival || (behind && distance <= TurnRadius(turnRate, Fixed.Max(speed, minSpeed)) * Fixed.FromInt(2));
        var brakingDistance = Fixed.Zero;
        if (locomotor.Braking > Fixed.Zero && speed > minSpeed)
            brakingDistance = (speed * speed - minSpeed * minSpeed) / (braking * Fixed.FromInt(2));
        SteerWinged(world.Time, locomotor, targetX, targetY, distance <= brakingDistance ? minSpeed : CappedFrameSpeed(topSpeed));
        if (!arrived) return;
        Entity.HasMoveTarget = false;
        InvalidatePath();
        world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
        OnWingedArrival(targetX, targetY);
    }

    private static readonly Fixed WingEpsilon = Fixed.FromFraction(1, 64);

    /// <summary><c>Locomotor::calcMinTurnRadius</c>: the radius of the circle an aircraft flies at <paramref name="speed"/> with its turn rate (a huge radius when it cannot turn).</summary>
    protected static Fixed TurnRadius(LocomotorInfo locomotor, Fixed speed) => TurnRadius(locomotor.TurnRatePerTick, speed);

    /// <summary>The same with speed and turn rate in one unit of time (both per tick or both per logic frame of the original).</summary>
    protected static Fixed TurnRadius(Fixed turnRate, Fixed speed) => turnRate > Fixed.Zero ? speed / turnRate : Fixed.FromInt(1_000_000);

    /// <summary>
    /// The constants of <paramref name="locomotor"/> per logic frame of the original for a decision of flight: at 30 Hz the locomotor's own values, above the 30 Hz conversion of the
    /// same source numbers (<see cref="LocomotorFrames"/>), so that a decision computes with the bits the 30 Hz step computes with and no threshold falls differently at another rate.
    /// </summary>
    protected void FlightFrameConstants(SimulationTime time, LocomotorInfo locomotor, out Fixed topSpeed, out Fixed acceleration, out Fixed braking, out Fixed turnRate, out Fixed minSpeed)
    {
        if (_stride == 0) ResolveBlockerTiming(time);
        FrameConstants(locomotor, out topSpeed, out acceleration, out braking, out turnRate, out minSpeed);
    }

    /// <summary>The vertical constants of <paramref name="locomotor"/> per logic frame of the original (<see cref="FlightFrameConstants"/>): the limit of its own descent and its lift.</summary>
    protected void FlightVerticalConstants(SimulationTime time, LocomotorInfo locomotor, out Fixed speedLimitZ, out Fixed lift)
    {
        FlightFrameConstants(time, locomotor, out _, out _, out _, out _, out _);
        if (_stride == 1)
        {
            speedLimitZ = locomotor.SpeedLimitZ;
            lift = locomotor.Lift;
            return;
        }
        speedLimitZ = _frameConstants!.SpeedLimitZ;
        lift = _frameConstants.Lift;
    }

    /// <summary>The speed per logic frame of the original the object flies or rolls with (<see cref="FrameSpeed"/>).</summary>
    protected Fixed FlightFrameSpeed(SimulationTime time)
    {
        if (_stride == 0) ResolveBlockerTiming(time);
        return FrameSpeed();
    }

    /// <summary>Sets the speed per logic frame of the original; <see cref="Entity.Speed"/> becomes its share of a tick (<see cref="SetFrameSpeed"/>). Zero is "stopped by the module itself".</summary>
    protected void SetFlightFrameSpeed(SimulationTime time, Fixed speed)
    {
        if (_stride == 0) ResolveBlockerTiming(time);
        SetFrameSpeed(speed);
    }

    /// <summary>The frame of winged flight that is being flown (<see cref="SteerWinged"/>); empty at 30 Hz. Kept by the one AI that flies with wings (<see cref="DescribeWingStep"/>).</summary>
    private FlightFrameStep _wingStep;

    /// <summary>A frame of winged flight has been decided and is not flown to its end yet.</summary>
    protected bool WingedStepRunning => _wingStep.TicksLeft > 0;

    /// <summary>Flies the next tick's share of the decided frame of winged flight; false when the next frame has to be decided.</summary>
    protected bool ContinueWingedStep() => _wingStep.Continue(Entity);

    /// <summary>The aircraft stopped flying with its wings (it lands, taxis or is gone): what is left of the decided frame is dropped.</summary>
    protected void CancelWingedStep() => _wingStep.Cancel();

    /// <summary>What is left of the decided frame of winged flight (for the publication of the aircraft's frame end, <see cref="FlightFrameStep.Publish"/>).</summary>
    protected (Fixed X, Fixed Y) WingedStepRest => _wingStep.RestXY;

    /// <summary>The heading the decided frame of winged flight ends with, while the aircraft still turns toward it (for <see cref="Entity.FrameEndAngle"/>).</summary>
    protected bool WingedStepTurnsTo(out Fixed endAngle) => _wingStep.TurnsTo(out endAngle);

    /// <summary>
    /// What is left of the step of ground or helicopter movement the object is covering in shares (<see cref="AdvanceFrame"/>), after this tick's share; zero when no share was
    /// covered in <paramref name="tick"/>. Read by a derived AI that publishes the frame end of an object with a second step (height) of its own.
    /// </summary>
    protected (Fixed X, Fixed Y) GroundStepRest(long tick)
    {
        if (_frameTicksLeft <= 0 || _frameDue != tick + TickOrder.Next) return default;
        long shares = _stride;
        var covered = shares - _frameTicksLeft;
        return (Fixed.FromRaw(_frameStepX.Raw - _frameStepX.Raw * covered / shares), Fixed.FromRaw(_frameStepY.Raw - _frameStepY.Raw * covered / shares));
    }

    protected void DescribeWingStep(IStateWriter writer) => _wingStep.Describe(writer, "wingStep");

    protected void LoadWingStep(IStateReader reader) => _wingStep.Load(reader, "wingStep");

    /// <summary>
    /// One logic frame of the original of winged flight toward (<paramref name="targetX"/>, <paramref name="targetY"/>): the heading turns toward the target by at most the turn rate of a frame (the reference
    /// turns an aircraft at its full turn rate whatever its speed: <c>MinTurnSpeed</c> is a wheeled-vehicle setting), the speed approaches <paramref name="goalSpeed"/> by the locomotor's acceleration or braking
    /// of a frame but never leaves the range from <see cref="LocomotorInfo.MinSpeed"/> up, and the aircraft advances along its new heading by the travel of a frame at that speed. This is the 30 Hz step; at a
    /// higher rate it is decided once per frame and flown in equal shares per tick (<see cref="FlightFrameStep"/>), because a polygon with shorter sides is another curve (another turning circle).
    /// <paramref name="goalSpeed"/> is a speed per logic frame of the original, as are the constants of the step (<see cref="FlightFrameConstants"/>); the module keeps the
    /// speed per frame itself (<see cref="FrameSpeed"/>), <see cref="Entity.Speed"/> is its share of a tick.
    /// </summary>
    protected void SteerWinged(SimulationTime time, LocomotorInfo locomotor, Fixed targetX, Fixed targetY, Fixed goalSpeed)
    {
        FlightFrameConstants(time, locomotor, out _, out var acceleration, out var braking, out var turn, out var minSpeed);
        var dx = targetX - Entity.X;
        var dy = targetY - Entity.Y;
        var heading = Entity.Angle;
        if (dx.Abs() + dy.Abs() > WingEpsilon)
        {
            var desired = FixedTrig.Atan2(dy, dx);
            var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
            Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : desired;
        }
        goalSpeed = Fixed.Max(goalSpeed, minSpeed);
        var speed = Fixed.Max(FrameSpeed(), minSpeed);
        speed = speed < goalSpeed
            ? Fixed.Min(goalSpeed, speed + acceleration)
            : Fixed.Max(goalSpeed, speed - braking);
        SetFrameSpeed(speed);
        var stepX = FixedTrig.Cos(Entity.Angle) * speed;
        var stepY = FixedTrig.Sin(Entity.Angle) * speed;
        Entity.X += stepX;
        Entity.Y += stepY;
        if (time.TicksPerOriginalFrame > 1) _wingStep.Begin(time, Entity, stepX, stepY, Fixed.Zero, FixedTrig.AngleDifference(heading, Entity.Angle));
    }

    /// <summary>Own rhythm: a unit close to its goal asks every 15 logic frames of the original (half a second) whether the goal is still free.</summary>
    private const long GoalSpotCheckFrames = 15;

    /// <summary>Own value: "close to its goal" is six path cells, a little more than the widest ground unit's bounding circle plus the look-ahead of a walking infantryman.</summary>
    private static readonly Fixed GoalSpotLook = Fixed.FromInt(6 * TerrainMap.CellSize);

    /// <summary>
    /// Whether this decision asks <see cref="IWorldServices.TryFreeGoalSpot"/>: only a plain move (no dock, no construction, no container or goal object to walk into, no way
    /// back), only within <see cref="GoalSpotLook"/> of the target, and only every <see cref="GoalSpotCheckFrames"/> frames. The rhythm needs no state: it is the world's frame
    /// number, staggered by the object's id, so it falls on the same frame of the original at every rate.
    /// </summary>
    // Derived from the template's modules on first use, not state.
    private bool? _follows;

    private bool GoalSpotCheckDue(IWorldServices world)
    {
        // Not on the way out of a factory either: the first leg ends at the place the exit path chose, and the rally point is adjusted when the second leg is ordered.
        if (_dock is not null || _task != BuilderTask.None || _enterTarget.IsValid || _goal.IsValid || _returning || _evacuatePending || _exitRallyPending || _viaPending || _exitFrom.IsValid) return false;
        if ((world.Time.FramesIn(world.Tick) + Entity.Id.Value) % GoalSpotCheckFrames != 0) return false;
        // A slave or mob member is sent onto its master's place by its own module, again and again: it keeps the goal it is given.
        _follows ??= Entity.Modules.Any(module => module is ISlavedModule);
        if (_follows.Value) return false;
        var dx = Entity.MoveTargetX - Entity.X;
        var dy = Entity.MoveTargetY - Entity.Y;
        return SquaredReach.Of(GoalSpotLook).Contains(dx * dx + dy * dy);
    }

    /// <summary>Own rhythm: a replan around standing units at most once per 30 logic frames of the original (one second).</summary>
    private const long BlockerReplanFrames = SimulationRate.OriginalFramesPerSecond;

    /// <summary>
    /// Own value: the look-ahead for standing units is 25 times the travel of one logic frame of the original. A factor on a speed per logic frame, not a duration: no time
    /// service converts it.
    /// </summary>
    private static readonly Fixed BlockerLookAhead = Fixed.FromInt(25);

    /// <summary>"Never replanned": further back than any replan interval. A sentinel (also the snapshot's default), not a time.</summary>
    private long _blockerReplanTick = -1000;

    // In ticks of the world's rate, resolved once on the first move (a module is created and restored without a world); derived, not state.
    private long _blockerReplanInterval;

    /// <summary>Ticks per logic frame of the original (1, 2 or 4), resolved with the interval above; 0 until the first move.</summary>
    private int _stride;

    private void ResolveBlockerTiming(SimulationTime time)
    {
        _blockerReplanInterval = time.OriginalFrames(BlockerReplanFrames);
        _stride = time.TicksPerOriginalFrame;
    }

    // The per-frame constants of the locomotor the last decision above 30 Hz moved with (LocomotorFrames); derived, not state.
    private LocomotorInfo? _frameLocomotor;
    private LocomotorFrame? _frameConstants;

    private void FrameConstants(LocomotorInfo locomotor, out Fixed topSpeed, out Fixed acceleration, out Fixed braking, out Fixed turnRate, out Fixed minSpeed)
    {
        if (_stride == 1)
        {
            topSpeed = locomotor.Speed;
            acceleration = locomotor.Acceleration;
            braking = locomotor.Braking;
            turnRate = locomotor.TurnRatePerTick;
            minSpeed = locomotor.MinSpeed;
            return;
        }
        if (!ReferenceEquals(_frameLocomotor, locomotor))
        {
            _frameConstants = LocomotorFrames.Of(locomotor, _stride);
            _frameLocomotor = locomotor;
        }
        var frame = _frameConstants!;
        topSpeed = frame.Speed;
        acceleration = frame.Acceleration;
        braking = frame.Braking;
        turnRate = frame.TurnRate;
        minSpeed = frame.MinSpeed;
    }

    // State above 30 Hz only (at 30 Hz a decision is a whole step and none of this is ever set or written).
    // The speed of the last decision in units per logic frame; Entity.Speed (per tick) is this value divided by the stride.
    private Fixed _frameSpeed;
    // The step of the last decision while it is being covered: the ticks still to come, the tick the next share is due in, the displacement of the whole frame, the heading at its end.
    private int _frameTicksLeft;
    private long _frameDue;
    private Fixed _frameStepX;
    private Fixed _frameStepY;
    private Fixed _frameAngle;
    // The step being covered is the last one of the move: with its last share the object has arrived.
    private bool _frameArriving;

    /// <summary>Above 30 Hz: the object is covering the last step of its move; at the end of this frame it stands on its target, as it does after the single 30 Hz step.</summary>
    internal bool IsArrivingWithThisFrame => _frameArriving && _frameTicksLeft > 0;

    /// <summary>
    /// The speed the object moves with, per logic frame of the original. Above 30 Hz the module keeps it itself, because the 30 Hz value does not survive a division by the stride; an
    /// object stopped or slowed from outside (<see cref="Entity.Speed"/> no longer the published share of it) starts from what it was set to.
    /// </summary>
    private Fixed FrameSpeed()
    {
        if (_stride == 1) return Entity.Speed;
        // A frame speed below one raw step per tick publishes a speed per tick of zero and is still the speed the 30 Hz step goes on with (a braking object passes through
        // such a value); the module clears _frameSpeed wherever it stops the object itself, so a published zero with a larger frame speed means "stopped from outside".
        // What the object itself carries comes first: an object with two AI modules (the Chinook of the air force general) moves with one speed that both of them raise in turn,
        // as they do with Entity.Speed at 30 Hz.
        if (Entity.Speed.Raw == Entity.SpeedPerFrame.Raw / _stride) return Entity.SpeedPerFrame;
        return Entity.Speed.Raw == _frameSpeed.Raw / _stride ? _frameSpeed : Entity.Speed * _stride;
    }

    /// <summary>The module stops the object itself: no speed of a frame is left, neither its own nor the one the object carries.</summary>
    private void ClearFrameSpeed()
    {
        _frameSpeed = Fixed.Zero;
        if (Entity.SpeedPerFrame != Fixed.Zero) Entity.SpeedPerFrame = Fixed.Zero;
    }

    /// <summary>
    /// The speed of the object in units per logic frame of the original, as the movement computes with it (a read access for measurements: above 30 Hz <see cref="Entity.Speed"/> is
    /// this value divided by the ticks per frame and has lost its last bits). <paramref name="ticksPerOriginalFrame"/> is the world's <c>Time.TicksPerOriginalFrame</c>.
    /// </summary>
    public Fixed SpeedPerOriginalFrame(int ticksPerOriginalFrame) => _stride == 0 ? Entity.Speed * ticksPerOriginalFrame : FrameSpeed();

    private void SetFrameSpeed(Fixed speed)
    {
        if (_stride == 1)
        {
            Entity.Speed = speed;
            return;
        }
        _frameSpeed = speed;
        Entity.SpeedPerFrame = speed;
        Entity.Speed = Fixed.FromRaw(speed.Raw / _stride);
    }

    private Fixed CappedFrameSpeed(Fixed topSpeed) => _speedCap > Fixed.Zero ? Fixed.Min(topSpeed, _speedCap * _stride) : topSpeed;

    // True while this module decides a frame behind another mover of the same object (DecideBehindAnotherMover). Not state.
    private bool _behindAnotherMover;

    /// <summary>
    /// Above 30 Hz: the decision of a frame by the second AI module of an object (the Chinook of the air force general carries an AIUpdateInterface and a ChinookAIUpdate, and
    /// both move it). At 30 Hz the second module decides from where the first one's whole step has put the object; here the first has covered only a share of its step by now.
    /// The object is put where the first mover's frame ends while this module decides, and back afterwards; what both have left of the frame stays readable on the object
    /// (<see cref="Entity.FrameEnd"/>). An object the decision takes out of the world stays where it is.
    /// </summary>
    private void DecideBehindAnotherMover(IWorldServices world, LocomotorInfo locomotor)
    {
        var (restX, restY, restZ) = (Entity.AheadX, Entity.AheadY, Entity.AheadZ);
        var before = Entity.Angle;
        var end = Entity.FrameEndAngle(world.Tick);
        var (dead, leaving) = (Entity.EffectivelyDead, Entity.DestroyRequested);
        Entity.X += restX;
        Entity.Y += restY;
        Entity.Angle = end;
        Entity.SetAhead(world.Tick, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        _behindAnotherMover = true;
        Move(world, locomotor);
        _behindAnotherMover = false;
        if (Entity.EffectivelyDead != dead || Entity.DestroyRequested != leaving || Entity.ContainedBy.IsValid) return;
        // The heading this frame ends with: this module's when it has begun a step, else the first mover's.
        var turnsTo = _frameTicksLeft > 0 ? _frameAngle : Entity.Angle;
        Entity.X -= restX;
        Entity.Y -= restY;
        if (end != before) Entity.Angle = FixedTrig.Normalize(Entity.Angle - FixedTrig.AngleDifference(before, end));
        Entity.SetAhead(world.Tick, Entity.AheadX + restX, Entity.AheadY + restY, Entity.AheadZ + restZ);
        if (world.Navigation is not null && (locomotor.Surfaces & LocomotorSurfaces.Air) == 0)
        {
            Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            Entity.SetAheadHeight(world.GroundHeightAt(Entity.X + Entity.AheadX, Entity.Y + Entity.AheadY) - Entity.Z);
        }
        if (turnsTo != Entity.Angle) Entity.SetAheadAngle(turnsTo);
    }

    /// <summary>
    /// Above 30 Hz a decision of <see cref="Move"/>/<see cref="MoveStraight"/> is the step of one logic frame of the original; the object covers it in equal shares, one per tick, and
    /// decides again when it is used up. (<paramref name="stepX"/>, <paramref name="stepY"/>) is the displacement of the frame, <paramref name="heading"/> the angle before the
    /// decision turned the object. After the last share position and angle are what the single 30 Hz step leaves: share i of n is the difference of the raw step's parts i/n and
    /// (i-1)/n, so the shares add up to the step exactly. <see cref="Entity.Speed"/> is the speed of the frame from its first share on.
    /// </summary>
    private void BeginFrame(IWorldServices world, LocomotorInfo locomotor, Fixed stepX, Fixed stepY, Fixed heading)
    {
        _frameStepX = stepX;
        _frameStepY = stepY;
        _frameAngle = Entity.Angle;
        Entity.Angle = heading;
        _frameTicksLeft = _stride;
        AdvanceFrame(world, locomotor);
    }

    /// <summary>
    /// The arrival above 30 Hz. At 30 Hz the step that finds the object within its arrival radius puts it on the target and ends the move; that step is a logic frame of the
    /// original like every other. Here the move ends, with <c>MoveArrived</c>, in the first tick of that frame - the tick of the 30 Hz step, in which every decision of a
    /// frame falls (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 1): whoever looks at the object in this tick finds it without a move order, as at 30 Hz - and the object
    /// covers the rest of the way (<paramref name="dx"/>, <paramref name="dy"/>) in that frame's shares (<see cref="Coast"/>); its position at the frame's end, which
    /// observers read (<see cref="Entity.FrameEnd"/>), is the target.
    /// </summary>
    private void BeginArrival(IWorldServices world, LocomotorInfo locomotor, Fixed dx, Fixed dy)
    {
        _frameStepX = dx;
        _frameStepY = dy;
        _frameAngle = Entity.Angle;
        _frameTicksLeft = _stride;
        _frameArriving = true;
        var complete = world.Navigation is null || _pathComplete;
        AdvanceFrame(world, locomotor);
        // The end of the move, in the order of the 30 Hz arrival (which tests no collision in its step either).
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        ClearFrameSpeed();
        InvalidatePath();
        _frameArriving = true;
        if (complete) world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
    }

    /// <summary>
    /// Above 30 Hz: puts the object where the frame it has begun ends and leaves nothing of that frame's step to cover - for whoever takes the object out of the world in the
    /// tick of its decision (it enters a container): at 30 Hz it has taken its whole step by then. Nothing to do at 30 Hz.
    /// </summary>
    internal void SettleFrame(long tick)
    {
        // Whatever the object does next is decided when the frame it was covering would have ended, not earlier.
        if (_frameTicksLeft > 0 && _frameDue >= tick) _moveHoldUntil = _frameDue + _frameTicksLeft;
        if (Entity.IsInsideFrameStep || Entity.HasAheadAngle) Entity.CompleteFrame(tick);
        _frameTicksLeft = 0;
        _frameArriving = false;
    }

    /// <summary>Above 30 Hz: the shares of the last step of a move that has ended (<see cref="BeginArrival"/>). A share that was not taken in its tick voids the rest.</summary>
    private void Coast(IWorldServices world)
    {
        if (_stride == 0) ResolveBlockerTiming(world.Time);
        if (world.Tick == _frameDue && ActiveLocomotor is { } locomotor)
        {
            AdvanceFrame(world, locomotor);
            return;
        }
        _frameTicksLeft = 0;
        _frameArriving = false;
    }

    private void AdvanceFrame(IWorldServices world, LocomotorInfo locomotor)
    {
        var left = _frameTicksLeft;
        _frameTicksLeft = left - 1;
        _frameDue = world.Tick + TickOrder.Next;
        long shares = _stride;
        var covered = shares - left;
        var stepX = Fixed.FromRaw(_frameStepX.Raw * (covered + 1) / shares - _frameStepX.Raw * covered / shares);
        var stepY = Fixed.FromRaw(_frameStepY.Raw * (covered + 1) / shares - _frameStepY.Raw * covered / shares);
        if (left == 1) Entity.Angle = _frameAngle;
        else
        {
            var turn = FixedTrig.AngleDifference(Entity.Angle, _frameAngle);
            if (turn != Fixed.Zero) Entity.Angle = FixedTrig.Normalize(Entity.Angle + Fixed.FromRaw(turn.Raw / left));
        }
        // The rest of the frame's step stays readable on the object (Entity.FrameEnd): who decides its own frame later in this tick sees this one where its step ends, as at 30 Hz.
        if (covered == 0) Entity.SetAhead(world.Tick, _frameStepX, _frameStepY, Fixed.Zero);
        Entity.CoverShare(world.Tick, stepX, stepY, Fixed.Zero);
        // So does the heading the frame ends with (Entity.FrameEndAngle).
        if (Entity.Angle != _frameAngle) Entity.SetAheadAngle(_frameAngle);
        else Entity.ClearAheadAngle();
        var air = (locomotor.Surfaces & LocomotorSurfaces.Air) != 0;
        if (world.Navigation is not null && !air)
        {
            Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            // The height the frame ends with is that of the ground where it ends: an observer reads it there, and an object put where its frame ends (Entity.CompleteFrame)
            // stands on the ground of that place, as after the 30 Hz step, not on the height of the share it had covered.
            Entity.SetAheadHeight(Entity.AheadX == Fixed.Zero && Entity.AheadY == Fixed.Zero ? Fixed.Zero : world.GroundHeightAt(Entity.X + Entity.AheadX, Entity.Y + Entity.AheadY) - Entity.Z);
        }
        if (_frameArriving)
        {
            // The last step of a move tests no collision, as the 30 Hz arrival does not.
            if (left == 1) _frameArriving = false;
            return;
        }
        // Crush, topple and collide modules are tested where the 30 Hz step tests them: once per logic frame, at the end of the frame's step and with its whole displacement -
        // and in the tick of the 30 Hz step, the frame's first, in which every decision of a frame falls (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rules 1 and 4).
        if (covered == 0 && (world.Navigation is null || !air)) CollideWhereTheFrameEnds(world, air);
    }

    /// <summary>
    /// Above 30 Hz: the collision test of a frame's step, in the frame's first tick. The object stands where its frame ends while the test runs (what it meets reads it there
    /// and is read where its own begun frame ends, <c>SimulationWorld.FootprintsOverlap</c>), as it does after the single 30 Hz step. An object the test takes out of the
    /// world or out of its move (killed, contained, settled) stays there, as rule 4 has it; every other object goes back and covers the rest of its step. Tested with the
    /// last share instead, a crate was collected and a soldier crushed up to a frame's last tick late: after everything else of that frame had decided, drawn its random
    /// numbers and looked at them.
    /// </summary>
    private void CollideWhereTheFrameEnds(IWorldServices world, bool air)
    {
        var (x, y, z, angle) = (Entity.X, Entity.Y, Entity.Z, Entity.Angle);
        var (aheadX, aheadY, aheadZ) = (Entity.AheadX, Entity.AheadY, Entity.AheadZ);
        var pending = _frameTicksLeft;
        var (dead, leaving) = (Entity.EffectivelyDead, Entity.DestroyRequested);
        Entity.X = x + aheadX;
        Entity.Y = y + aheadY;
        Entity.Z = z + aheadZ;
        Entity.Angle = _frameAngle;
        Entity.SetAhead(world.Tick, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        world.ResolveFootprintCollisions(Entity, _frameStepX, _frameStepY);
        if (_frameTicksLeft != pending || Entity.EffectivelyDead != dead || Entity.DestroyRequested != leaving || Entity.ContainedBy.IsValid || Entity.X != x + aheadX || Entity.Y != y + aheadY)
        {
            _frameTicksLeft = 0;
            return;
        }
        Entity.X = x;
        Entity.Y = y;
        Entity.Z = z;
        Entity.Angle = angle;
        Entity.SetAhead(world.Tick, aheadX, aheadY, aheadZ);
        if (angle != _frameAngle) Entity.SetAheadAngle(_frameAngle);
    }

    /// <summary>
    /// The movement profile's planning: asks the world's path queue when the object has no valid path or the footprints changed, takes the answer once it is there, and keeps a
    /// path it still has while it waits. Returns <see langword="false"/> when the object has to stand this tick (it waits and has no path to follow).
    /// </summary>
    private bool QueuedPath(IWorldServices world, NavigationGrid navigation, LocomotorInfo locomotor)
    {
        if (_pathWait != PathWait.None)
        {
            if (world.TryTakePath(Entity, out var answer))
            {
                var detour = _pathWait == PathWait.Detour;
                _pathWait = PathWait.None;
                world.MarkDirty(Entity);
                if (!detour || answer is { Status: PathStatus.Found, Points.IsDefaultOrEmpty: false })
                {
                    _path.Clear();
                    if (answer is not null) _path.AddRange(answer.Points);
                    _pathIndex = 0;
                    _pathValid = true;
                    _pathComplete = detour || answer is { Status: PathStatus.Found };
                    _pathObstacles = navigation.ObstacleSum;
                }
            }
            else if (!world.IsPathPending(Entity))
            {
                // The question is gone (its answer was taken by an order in between): ask again.
                _pathWait = PathWait.None;
                _pathValid = false;
            }
        }
        if (_pathWait == PathWait.None && (!_pathValid || _pathObstacles != navigation.ObstacleSum))
        {
            if (!_pathValid) { _path.Clear(); _pathIndex = 0; }
            world.RequestPath(Entity, new PathQuery(Entity.X, Entity.Y, Entity.MoveTargetX, Entity.MoveTargetY, locomotor.Surfaces, [], false));
            _pathWait = PathWait.Plan;
            _pathValid = true;
            _pathObstacles = navigation.ObstacleSum;
            world.MarkDirty(Entity);
        }
        if (_pathWait != PathWait.None && _path.Count == 0)
        {
            // Waiting without a path: the object stands, and keeps its order.
            Entity.Speed = Fixed.Zero;
            ClearFrameSpeed();
            return false;
        }
        return true;
    }

    private void ReplanAround(NavigationGrid navigation, LocomotorInfo locomotor, IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> blockers, long tick)
    {
        _blockerReplanTick = tick;
        // Only a detour that arrives replaces the path: the grid may stop as soon as it knows there is none.
        var result = navigation.FindPathToGoal(Entity.X, Entity.Y, Entity.MoveTargetX, Entity.MoveTargetY, locomotor.Surfaces, blockers);
        if (result is null || result.Points.IsDefaultOrEmpty) return;
        _path.Clear();
        _path.AddRange(result.Points);
        _pathIndex = 0;
        _pathComplete = true;
    }

    private void PlanPath(NavigationGrid navigation, LocomotorInfo locomotor, bool air)
    {
        InvalidatePath();
        _pathValid = true;
        _pathObstacles = navigation.ObstacleSum;
        if (air)
        {
            _path.Add(new PathPoint(Entity.MoveTargetX, Entity.MoveTargetY));
            _pathComplete = true;
            return;
        }
        if (_viaPending)
        {
            var toDoor = navigation.FindPath(Entity.X, Entity.Y, _viaX, _viaY, locomotor.Surfaces);
            if (toDoor.Status == PathStatus.Found)
            {
                var onward = navigation.FindPath(_viaX, _viaY, Entity.MoveTargetX, Entity.MoveTargetY, locomotor.Surfaces);
                _pathComplete = onward.Status == PathStatus.Found;
                _path.AddRange(toDoor.Points);
                _path.AddRange(onward.Points);
                return;
            }
            // The door point cannot be reached (something was built in front of the door): straight to the goal.
            _viaPending = false;
        }
        var result = navigation.FindPath(Entity.X, Entity.Y, Entity.MoveTargetX, Entity.MoveTargetY, locomotor.Surfaces);
        _pathComplete = result.Status == PathStatus.Found;
        _path.AddRange(result.Points);
    }

    private Fixed TailLength()
    {
        var total = Fixed.Zero;
        for (var index = _pathIndex; index < _path.Count - 1; index++)
        {
            var dx = _path[index + 1].X - _path[index].X;
            var dy = _path[index + 1].Y - _path[index].Y;
            total += Fixed.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }

    private void MoveStraight(IWorldServices world, LocomotorInfo locomotor)
    {
        var dx = Entity.MoveTargetX - Entity.X;
        var dy = Entity.MoveTargetY - Entity.Y;
        var distSq = dx * dx + dy * dy;
        // "Close enough" is at least one tick's travel at full speed, so a fast unit cannot perpetually overshoot and reverse.
        FrameConstants(locomotor, out var topSpeed, out var acceleration, out var braking, out _, out var minSpeed);
        var closeEnough = Fixed.Max(locomotor.CloseEnoughDist, topSpeed);
        if (SquaredReach.Of(closeEnough).Contains(distSq))
        {
            if (_stride > 1)
            {
                BeginArrival(world, locomotor, dx, dy);
                return;
            }
            Entity.X = Entity.MoveTargetX;
            Entity.Y = Entity.MoveTargetY;
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
            ClearFrameSpeed();
            world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
            return;
        }
        var dist = Fixed.Sqrt(distSq);
        // calcSlowDownDist equivalent: v^2 = 2*a*d, so begin braking once the remaining distance fits the stopping distance at the current speed.
        var speed = FrameSpeed();
        var brakingDist = locomotor.Braking > Fixed.Zero ? speed * speed / (braking * Fixed.FromInt(2)) : Fixed.Zero;
        var goalSpeed = dist <= brakingDist ? minSpeed : CappedFrameSpeed(topSpeed);
        speed = speed < goalSpeed
            ? Fixed.Min(goalSpeed, speed + acceleration)
            : Fixed.Max(goalSpeed, speed - braking);
        SetFrameSpeed(speed);
        var step = Fixed.Min(speed, dist);
        var stepX = dx * step / dist;
        var stepY = dy * step / dist;
        if (_stride > 1)
        {
            BeginFrame(world, locomotor, stepX, stepY, Entity.Angle);
            return;
        }
        Entity.X += stepX;
        Entity.Y += stepY;
        // phase 10: no terrain here (fixture mode), so no local avoidance, but the crush/topple check still applies.
        world.ResolveFootprintCollisions(Entity, stepX, stepY);
    }

    private Fixed _speedCap;

    /// <summary>
    /// <c>AIUpdateInterface::setDesiredSpeed</c> (zero: no limit): a group that moves as a team (<c>AIGroup::getSpeed</c>, the slowest member) keeps every member at the group speed. The
    /// locomotor's own speed is never exceeded.
    /// </summary>
    public Fixed SpeedCap { get => _speedCap; set => _speedCap = value; }

    private Fixed CappedSpeed(LocomotorInfo locomotor) => _speedCap > Fixed.Zero ? Fixed.Min(locomotor.Speed, _speedCap) : locomotor.Speed;

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_speedCap > Fixed.Zero) writer.Fixed("speedCap", _speedCap);
        writer.Bool("lastCommandFromPlayer", _lastCommandFromPlayer);
        writer.Bool("docking", _dock is not null);
        writer.UInt("enterTarget", _enterTarget.Value);
        writer.Int("task", (int)_task);
        writer.UInt("taskTarget", _taskTarget.Value);
        writer.Bool("approaching", _approaching);
        if (_evacuatePending) writer.Bool("evacuate", true);
        if (_goal.IsValid) writer.UInt("goal", _goal.Value);
        if (_paralyzedUntil > 0) writer.Int("paralyzedUntil", _paralyzedUntil);
        if (_exitAfterEvacuate || _returning)
        {
            writer.Bool("exitAfterEvacuate", _exitAfterEvacuate);
            writer.Bool("returning", _returning);
            writer.Fixed("exitX", _exitX);
            writer.Fixed("exitY", _exitY);
        }
        if (_blockerReplanTick > -1000) writer.Int("blockerReplanTick", _blockerReplanTick);
        if (_frameSpeed != Fixed.Zero) writer.Fixed("frameSpeed", _frameSpeed);
        if (_moveHoldUntil != 0) writer.Int("moveHoldUntil", _moveHoldUntil);
        if (_frameTicksLeft > 0)
        {
            writer.Int("frameTicksLeft", _frameTicksLeft);
            writer.Int("frameDue", _frameDue);
            writer.Fixed("frameStepX", _frameStepX);
            writer.Fixed("frameStepY", _frameStepY);
            writer.Fixed("frameAngle", _frameAngle);
            if (_frameArriving) writer.Bool("frameArriving", true);
        }
        if (_exitRallyPending)
        {
            writer.Fixed("exitRallyX", _exitRallyX);
            writer.Fixed("exitRallyY", _exitRallyY);
        }
        if (_viaPending)
        {
            writer.Fixed("viaX", _viaX);
            writer.Fixed("viaY", _viaY);
        }
        if (_exitFrom.IsValid) writer.UInt("exitFrom", _exitFrom.Value);
        if (_dyingSince != 0) writer.Int("dyingSince", _dyingSince);
        if (_ignoreCollisionsUntil != 0) writer.Int("ignoreCollisionsUntil", _ignoreCollisionsUntil);
        if (_pathWait != PathWait.None) writer.Int("pathWait", (int)_pathWait);
        if (_pathValid)
        {
            writer.Bool("pathValid", true);
            writer.Bool("pathComplete", _pathComplete);
            writer.Int("pathIndex", _pathIndex);
            writer.UInt("pathObstacles", _pathObstacles);
            writer.BeginArray("path");
            foreach (var point in _path)
            {
                writer.BeginObject(null);
                writer.Fixed("x", point.X);
                writer.Fixed("y", point.Y);
                writer.EndObject();
            }
            writer.EndArray();
        }
        _dock?.Describe(writer);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _speedCap = reader.Has("speedCap") ? reader.Fixed("speedCap") : Fixed.Zero;
        _lastCommandFromPlayer = reader.Bool("lastCommandFromPlayer");
        _enterTarget = new EntityId(checked((uint)reader.UInt("enterTarget")));
        var task = reader.Int("task");
        if (task < 0 || task > (int)BuilderTask.Repair) throw new InvalidDataException("Builder task out of range.");
        _task = (BuilderTask)task;
        _taskTarget = new EntityId(checked((uint)reader.UInt("taskTarget")));
        _approaching = reader.Bool("approaching");
        _evacuatePending = reader.Has("evacuate") && reader.Bool("evacuate");
        _goal = reader.Has("goal") ? new EntityId(checked((uint)reader.UInt("goal"))) : EntityId.Invalid;
        _paralyzedUntil = reader.Has("paralyzedUntil") ? reader.Int("paralyzedUntil") : 0;
        _exitAfterEvacuate = reader.Has("exitAfterEvacuate") && reader.Bool("exitAfterEvacuate");
        _returning = reader.Has("returning") && reader.Bool("returning");
        _exitX = reader.Has("exitX") ? reader.Fixed("exitX") : Fixed.Zero;
        _exitY = reader.Has("exitY") ? reader.Fixed("exitY") : Fixed.Zero;
        _blockerReplanTick = reader.Has("blockerReplanTick") ? reader.Int("blockerReplanTick") : -1000;
        _frameSpeed = reader.Has("frameSpeed") ? reader.Fixed("frameSpeed") : Fixed.Zero;
        _moveHoldUntil = reader.Has("moveHoldUntil") ? reader.Int("moveHoldUntil") : 0;
        _frameTicksLeft = reader.Has("frameTicksLeft") ? checked((int)reader.Int("frameTicksLeft")) : 0;
        _frameDue = reader.Has("frameDue") ? reader.Int("frameDue") : 0;
        _frameStepX = reader.Has("frameStepX") ? reader.Fixed("frameStepX") : Fixed.Zero;
        _frameStepY = reader.Has("frameStepY") ? reader.Fixed("frameStepY") : Fixed.Zero;
        _frameAngle = reader.Has("frameAngle") ? reader.Fixed("frameAngle") : Fixed.Zero;
        _exitRallyPending = reader.Has("exitRallyX");
        _exitRallyX = _exitRallyPending ? reader.Fixed("exitRallyX") : Fixed.Zero;
        _exitRallyY = _exitRallyPending ? reader.Fixed("exitRallyY") : Fixed.Zero;
        _viaPending = reader.Has("viaX");
        _viaX = _viaPending ? reader.Fixed("viaX") : Fixed.Zero;
        _viaY = _viaPending ? reader.Fixed("viaY") : Fixed.Zero;
        _dyingSince = reader.Has("dyingSince") ? reader.Int("dyingSince") : 0;
        _exitFrom = reader.Has("exitFrom") ? new EntityId(checked((uint)reader.UInt("exitFrom"))) : EntityId.Invalid;
        _ignoreCollisionsUntil = reader.Has("ignoreCollisionsUntil") ? reader.Int("ignoreCollisionsUntil") : 0;
        InvalidatePath();
        if (reader.Has("pathValid"))
        {
            _pathValid = true;
            _pathComplete = reader.Bool("pathComplete");
            _pathIndex = checked((int)reader.Int("pathIndex"));
            _pathObstacles = reader.UInt("pathObstacles");
            foreach (var item in reader.Array("path")) _path.Add(new PathPoint(item.Fixed("x"), item.Fixed("y")));
        }
        _pathWait = reader.Has("pathWait") ? (PathWait)checked((int)reader.Int("pathWait")) : PathWait.None;
        _frameArriving = reader.Has("frameArriving") && reader.Bool("frameArriving");
        _dock = reader.Bool("docking") ? DockMachine.Load(reader) : null;
    }
}

// ============================================================================================= ProductionUpdate

/// <summary>
/// <c>ProductionUpdate.h</c>. <c>QuantityModifier</c> is typed but not applied: one unit per completed queue entry, created at the exit module's <c>UnitCreatePoint</c> and sent along its exit path (docs/SIMULATION-CORE.md §42).
/// The door-animation timing fields (<c>NumDoorAnimations</c>, <c>DoorOpeningTime</c>, <c>DoorWaitOpenTime</c>, <c>DoorCloseTime</c>, <c>ConstructionCompleteDuration</c>; milliseconds in the INI, ticks here) change no
/// simulation behaviour — nothing waits for a door — and are kept for the presentation only (docs/MODEL-CONDITIONS-BUILDINGS.md); the catalog carries them as Opaque, so they are read from the original text.
/// </summary>
public sealed record ProductionData(int MaxQueueEntries, int NumDoorAnimations = 0, long DoorOpeningTicks = 0, long DoorWaitOpenTicks = 0, long DoorCloseTicks = 0, long ConstructionCompleteTicks = 0) : ModuleData
{
    public static ProductionData Parse(RatedModuleConfiguration configuration)
    {
        var max = ModuleConfigReader.Int(configuration, "MaxQueueEntries", 0);
        return new ProductionData(
            max <= 0 ? int.MaxValue : checked((int)max),
            checked((int)OpaqueNumber(configuration, "NumDoorAnimations")),
            configuration.Time.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorOpeningTime")),
            configuration.Time.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorWaitOpenTime")),
            configuration.Time.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorCloseTime")),
            configuration.Time.DurationMillisecondsToTicks(OpaqueNumber(configuration, "ConstructionCompleteDuration")));
    }

    /// <summary><c>INI::parseInt</c>/<c>parseDurationUnsignedInt</c> input of a field the catalog keeps Opaque: the first token as a whole number; absent: zero (the constructor default).</summary>
    private static long OpaqueNumber(RatedModuleConfiguration configuration, string name)
    {
        var field = ModuleConfigReader.Find(configuration, name);
        if (field is null) return 0;
        var token = field.Value.OriginalText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return long.TryParse(token, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 0
            ? number
            : throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): ProductionUpdate {name} expects a non-negative whole number, got '{field.Value.OriginalText.Trim()}'.");
    }
}

/// <summary>One queued unit: template, its cost (for a full refund on cancel) and remaining build ticks.</summary>
internal readonly record struct ProductionEntry(int TemplateIndex, Fixed Cost, long RemainingTicks);

/// <summary>Public counterpart of <see cref="ProductionEntry"/> for <see cref="ProductionUpdate.Queue"/>.</summary>
public readonly record struct ProductionQueueItem(int TemplateIndex, Fixed Cost, long RemainingTicks);

/// <summary>
/// <c>ProductionUpdate::update()</c>: only the head-of-queue entry accumulates progress, one tick per call; when it reaches
/// zero the unit spawns at the exit's <c>UnitCreatePoint</c>, is sent along its exit path (<see cref="IProductionExit.TryGetExitPath"/>) and the next entry (if any) starts from a fresh tick count. The full
/// cost is withdrawn at queue time (<c>SimulationWorld</c>'s <c>QueueProductionCommand</c> handler) and refunded in full on
/// cancel, matching <c>Money::withdraw</c>/<c>deposit</c> semantics; there is no per-frame energy-ratio build-time penalty
/// (a documented phase-5 simplification — <c>ProductionUpdate::calcTimeToBuild</c> in the reference).
/// </summary>
public sealed class ProductionUpdate(ProductionData data) : SimModule, IUpdateModule
{
    private readonly List<ProductionEntry> _queue = [];
    private long _nextWake = -1;
    private bool _hasRally;
    private Fixed _rallyX;
    private Fixed _rallyY;

    public override string ModuleName => "ProductionUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool HasRoom => _queue.Count < data.MaxQueueEntries;

    /// <summary>The module's data, for the door timings the presentation reads (<see cref="ProductionData.DoorOpeningTicks"/> and its siblings).</summary>
    public ProductionData Data => data;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    /// <param name="time">The time service of the world: a build time of zero still takes one logic frame of the original, not one tick.</param>
    public void Enqueue(SimulationTime time, int templateIndex, Fixed cost, long totalTicks)
    {
        var ticks = time.AtLeastOneFrame(totalTicks);
        // The entry that starts an empty queue is worked on in the step of its order, and that step stands for a whole logic frame of the original: the unit is done
        // BuildTime less one frame after the order (not less one tick). An entry that waits behind another starts one step after its predecessor left and takes the full time.
        if (_queue.Count == 0) ticks -= time.TicksPerOriginalFrame - TickOrder.Next;
        _queue.Add(new ProductionEntry(templateIndex, cost, ticks));
    }

    public bool HasSlot(int slot) => slot >= 0 && slot < _queue.Count;

    /// <summary>How many entries of <paramref name="templateIndex"/> are currently queued (<c>ProductionUpdateInterface::countUnitTypeInQueue</c>).</summary>
    public int CountInQueue(int templateIndex)
    {
        var count = 0;
        foreach (var entry in _queue) if (entry.TemplateIndex == templateIndex) count++;
        return count;
    }

    /// <summary>
    /// Public, read-only view of the queue (`docs/PRESENTATION-INPUT.md` §10 point 2): template, paid cost (full refund on cancel) and the
    /// remaining ticks of work, head first. Only the head entry (index 0) progresses.
    /// </summary>
    public ImmutableArray<ProductionQueueItem> Queue => [.._queue.Select(entry => new ProductionQueueItem(entry.TemplateIndex, entry.Cost, entry.RemainingTicks))];

    /// <summary>The entry that is being built (the only one that progresses); false for an empty queue. Unlike <see cref="Queue"/> this allocates nothing, for readers that look at every producer every tick.</summary>
    public bool TryGetHead(out ProductionQueueItem head)
    {
        if (_queue.Count == 0)
        {
            head = default;
            return false;
        }
        var entry = _queue[0];
        head = new ProductionQueueItem(entry.TemplateIndex, entry.Cost, entry.RemainingTicks);
        return true;
    }

    public bool TryCancel(int slot, out Fixed refund)
    {
        if (!HasSlot(slot)) { refund = Fixed.Zero; return false; }
        refund = _queue[slot].Cost;
        _queue.RemoveAt(slot);
        return true;
    }

    /// <summary>Sale of the producer: every queued entry is dropped and its cost handed back at once.</summary>
    public void CancelAll(out Fixed refund)
    {
        refund = Fixed.Zero;
        foreach (var entry in _queue) refund += entry.Cost;
        _queue.Clear();
    }

    /// <summary>The rally point, when one is set (<c>ExitInterface::getRallyPoint</c>).</summary>
    public bool TryGetRally(out Fixed x, out Fixed y)
    {
        x = _rallyX;
        y = _rallyY;
        return _hasRally;
    }

    /// <summary>Units leaving the producer walk there.</summary>
    public void SetRally(Fixed x, Fixed y)
    {
        _hasRally = true;
        _rallyX = x;
        _rallyY = y;
    }

    private long _disabledUntil;

    /// <summary><c>Object::setDisabledUntil(DISABLED_HACKED, frame)</c> as <c>SabotageMilitaryFactoryCrateCollide</c> uses it: a disabled producer's update does not run (<c>ProductionUpdateModuleData::m_disabledTypesToProcess</c> is empty by default), so its queue stands still until the tick.</summary>
    public void DisableUntil(IWorldServices world, long tick)
    {
        _disabledUntil = tick;
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (_queue.Count == 0) return SimulationTiming.SleepForever;
        if (world.Tick < _disabledUntil || Entity.ScriptDisableMask != 0) return SimulationTiming.SleepNone;
        var head = _queue[0];
        var exit = Entity.Modules.OfType<IProductionExit>().FirstOrDefault();
        var remaining = head.RemainingTicks - TickOrder.Next;
        if (remaining <= 0 && exit is not null && !exit.IsFreeToExitFor(world.Templates[head.TemplateIndex]))
        {
            // The door is closed (QueueProductionExitUpdate delay): the finished unit waits at the head of the queue.
            _queue[0] = head with { RemainingTicks = TickOrder.Next };
        }
        else if (remaining <= 0)
        {
            _queue.RemoveAt(0);
            var forcesSupplyWanting = exit is { ForcesSupplyWanting: true };
            // exitObjectViaDoor: the unit leaves over the exit point of its factory (NaturalRallyPoint) and goes on to the player's rally point, if one is set.
            Fixed doorX = Fixed.Zero, doorY = Fixed.Zero, exitX = Fixed.Zero, exitY = Fixed.Zero;
            var hasExit = !forcesSupplyWanting && exit is not null && exit.TryGetExitPath(out doorX, out doorY, out exitX, out exitY);
            var extras = forcesSupplyWanting ? new SpawnExtras(Fixed.Zero, ForceSupplyWanting: true)
                : _hasRally || hasExit ? new SpawnExtras(Fixed.Zero, HasRally: _hasRally, RallyX: _rallyX, RallyY: _rallyY, HasExit: hasExit, DoorX: doorX, DoorY: doorY, ExitX: exitX, ExitY: exitY)
                : default(SpawnExtras?);
            var (x, y, z, angle) = exit is null ? OwnPlace(world) : exit.ClaimExit(world, world.Templates[head.TemplateIndex]);
            if (extras is { } value) world.RequestSpawn(head.TemplateIndex, Entity.Owner, x, y, z, angle, value);
            else world.RequestSpawn(head.TemplateIndex, Entity.Owner, x, y, z, angle);
            world.Emit(SimEventKind.ProductionCompleted, Entity.Id, EntityId.Invalid, string.Empty, head.TemplateIndex);
        }
        else _queue[0] = head with { RemainingTicks = remaining };
        return _queue.Count == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
    }

    /// <summary>
    /// A producer without an exit puts the unit where it is itself. One that moves (a worker) has, above 30 Hz, covered only a share of its frame's step when this module looks at
    /// it: the unit appears where the producer's frame ends, which is where the 30 Hz step has put it (the 30 Hz position itself at 30 Hz).
    /// </summary>
    private (Fixed X, Fixed Y, Fixed Z, Fixed Angle) OwnPlace(IWorldServices world)
    {
        var (x, y, z) = Entity.FrameEnd(world.Tick);
        return (x, y, z, Entity.FrameEndAngle(world.Tick));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("hasRally", _hasRally);
        writer.Fixed("rallyX", _rallyX);
        writer.Fixed("rallyY", _rallyY);
        if (_disabledUntil > 0) writer.Int("disabledUntil", _disabledUntil);
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
        _hasRally = reader.Bool("hasRally");
        _rallyX = reader.Fixed("rallyX");
        _rallyY = reader.Fixed("rallyY");
        _disabledUntil = reader.Has("disabledUntil") ? reader.Int("disabledUntil") : 0;
        _queue.Clear();
        foreach (var entry in reader.Array("queue"))
            _queue.Add(new ProductionEntry(checked((int)entry.Int("template")), entry.Fixed("cost"), entry.Int("remaining")));
    }
}
