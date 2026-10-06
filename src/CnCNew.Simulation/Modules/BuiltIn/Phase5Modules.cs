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
        _nextWake = Math.Min(world.Tick + SimulationTiming.SleepNone, SimulationTiming.SleepForever);
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
        var until = world.Tick + Math.Max(ticks, SimulationTiming.SleepNone);
        if (until > _detectedUntilTick) _detectedUntilTick = until;
        Entity.Status |= 1UL << DetectedBit;
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
    public static UpgradeMuxOnlyData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog));
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
        if (Entity.Body is IMaxHealthAdjustable adjustable) adjustable.AdjustMaxHealth(world, data.AddMaxHealth, data.ChangeType);
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
    public static CommandSetUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog) => new(UpgradeMuxData.Parse(configuration, catalog),
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
    public static ExperienceScalarUpgradeData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
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
public sealed record AIUpdateData(AutoAcquire AutoAcquire, Fixed RepairRatePerSecond = default, long MoodCheckTicks = AIUpdateData.DefaultMoodCheckTicks) : ModuleData
{
    /// <summary><c>AIUpdateModuleData::AIUpdateModuleData</c>: <c>m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2</c>.</summary>
    public const long DefaultMoodCheckTicks = 2 * SimulationTiming.LogicFramesPerSecond;

    public static AIUpdateData Parse(ModuleConfiguration configuration) => new(
        ParseAutoAcquire(configuration),
        ModuleConfigReader.Real(configuration, "RepairHealthPercentPerSecond", Fixed.Zero),
        Math.Max(1, ModuleConfigReader.Ticks(configuration, "MoodAttackCheckRate", DefaultMoodCheckTicks)));

    private static AutoAcquire ParseAutoAcquire(ModuleConfiguration configuration)
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
    private BuilderTask _task;
    private EntityId _taskTarget;
    private bool _approaching;
    private bool _evacuatePending;
    private bool _exitAfterEvacuate;
    private bool _returning;
    private Fixed _exitX;
    private Fixed _exitY;
    private static readonly Fixed MisalignedAngle = Fixed.FromRaw(34315); // π/6
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
        Entity.HasMoveTarget = true;
        Entity.MoveTargetX = x;
        Entity.MoveTargetY = y;
        InvalidatePath();
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>aiEnter</c> (<c>AI_ENTER</c>): moves to the container and enters it on arrival if it still accepts the object. An immobile object
    /// or a missing container ignores the order, as <c>privateEnter</c> does.
    /// </summary>
    public virtual void OrderEnter(IWorldServices world, Entity container, bool fromPlayer)
    {
        if (Entity.Template.Locomotor is null) return;
        OrderMove(world, container.X, container.Y, fromPlayer);
        _enterTarget = container.Id;
    }

    /// <summary>
    /// <c>AIUpdateInterface::getGoalObject</c>: the object the current order aims at (a hijacker's car, a saboteur's building, a pilot's vehicle).
    /// <c>CrateCollide</c> subclasses with a purpose of their own only fire when the object they touch is this goal, so walking past is harmless.
    /// </summary>
    public EntityId GoalObject => _goal;

    /// <summary>
    /// The approach half of <c>aiEnter</c>/<c>aiHijack</c>-style orders that end in a collision instead of a container entry: moves to <paramref name="goal"/> (following
    /// it when it moves) and, once the footprints overlap or the move ends within reach, lets this object's <see cref="ICollideModule"/>s act on it. An immobile object
    /// ignores the order. Replaced by any later order.
    /// </summary>
    public void OrderApproachGoal(IWorldServices world, Entity goal, bool fromPlayer)
    {
        if (Entity.Template.Locomotor is null) return;
        OrderMove(world, goal.X, goal.Y, fromPlayer);
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
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
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
        if (target is null || target.DestroyRequested || target.EffectivelyDead) { EndTask(world); return; }
        if (_task == BuilderTask.Construct && (!target.HasStatus(ObjectStatusBits.UnderConstruction) || target.BuilderId != Entity.Id)) { EndTask(world); return; }
        if (_task == BuilderTask.Repair && !IsRepairable(target)) { EndTask(world); return; }

        var reach = target.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        if (dx * dx + dy * dy > reach * reach)
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
        var amount = body.MaxHealth * data.RepairRatePerSecond / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
        world.Damage(target.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
    }

    /// <summary>The internal move of a state (approach, advance): false when the object has no locomotor.</summary>
    internal bool StartInternalMove(Fixed x, Fixed y)
    {
        if (Entity.Template.Locomotor is null) return false;
        Entity.HasMoveTarget = true;
        Entity.MoveTargetX = x;
        Entity.MoveTargetY = y;
        InvalidatePath();
        return true;
    }

    internal void StopInternalMove()
    {
        if (!Entity.HasMoveTarget) return;
        Entity.HasMoveTarget = false;
        Entity.Speed = Fixed.Zero;
        InvalidatePath();
    }

    private void InvalidatePath()
    {
        _pathValid = false;
        _pathComplete = false;
        _pathIndex = 0;
        _path.Clear();
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
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public virtual long Update(IWorldServices world)
    {
        if (Entity.EmpDisabledUntil > world.Tick || (Entity.ScriptDisableMask & Entity.ScriptHeld) != 0)
        {
            // DISABLED_EMP (EMPUpdate): the object cannot act until the disable runs out; its orders stay.
            Entity.Speed = Fixed.Zero;
            return SimulationTiming.SleepNone;
        }
        if (world.Tick < _paralyzedUntil) return SimulationTiming.SleepNone;
        if (_dock is not null && _dock.Update(world, this) != DockStatus.Continue) EndDock(world);
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
        if (_enterTarget.IsValid) UpdateEnter(world);
        if (_goal.IsValid) UpdateGoal(world);
        if (_evacuatePending && !Entity.HasMoveTarget && _dock is null && ReadyToUnload(world))
        {
            _evacuatePending = false;
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
        return _dock is not null || Entity.HasMoveTarget || _task != BuilderTask.None || _evacuatePending || _returning || _goal.IsValid ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
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
            var slack = Fixed.FromInt(2 * TerrainMap.CellSize);
            var gx = goal.X - Entity.MoveTargetX;
            var gy = goal.Y - Entity.MoveTargetY;
            if (gx * gx + gy * gy > slack * slack) StartInternalMove(goal.X, goal.Y);
            return;
        }
        var reach = goal.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        var dx = goal.X - Entity.X;
        var dy = goal.Y - Entity.Y;
        var near = dx * dx + dy * dy <= reach * reach;
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
        if (container is null || container.DestroyRequested || container.EffectivelyDead)
        {
            _enterTarget = EntityId.Invalid;
            StopInternalMove();
            return;
        }
        if (Entity.HasMoveTarget) return;
        _enterTarget = EntityId.Invalid;
        // The approach may end short (a partial path): only an object that actually reached the container gets in.
        var reach = container.Template.BoundingCircleRadius + Entity.Template.BoundingCircleRadius + Fixed.FromInt(2 * TerrainMap.CellSize);
        var dx = container.X - Entity.X;
        var dy = container.Y - Entity.Y;
        if (dx * dx + dy * dy > reach * reach) return;
        world.TryEnterContainer(Entity, container);
    }

    private void Move(IWorldServices world, LocomotorInfo locomotor)
    {
        if ((locomotor.Surfaces & LocomotorSurfaces.Air) != 0 && FliesWithWings(locomotor))
        {
            MoveWinged(world, locomotor);
            return;
        }
        if (world.Navigation is not { } navigation)
        {
            MoveStraight(world, locomotor);
            return;
        }
        var air = (locomotor.Surfaces & LocomotorSurfaces.Air) != 0;
        if (!_pathValid || (!air && _pathObstacles != navigation.ObstacleSum)) PlanPath(navigation, locomotor, air);
        if (_path.Count == 0)
        {
            // Blocked: no cell next to the object can be entered. The order ends without an arrival.
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
            InvalidatePath();
            return;
        }

        var arrivalRadius = Fixed.Max(locomotor.CloseEnoughDist, locomotor.Speed);
        while (true)
        {
            var waypoint = _path[_pathIndex];
            var last = _pathIndex == _path.Count - 1;
            var reach = last ? arrivalRadius : locomotor.Speed;
            var toX = waypoint.X - Entity.X;
            var toY = waypoint.Y - Entity.Y;
            if (toX * toX + toY * toY > reach * reach) break;
            if (!last) { _pathIndex++; continue; }
            Entity.X = waypoint.X;
            Entity.Y = waypoint.Y;
            if (!air) Entity.Z = world.GroundHeightAt(Entity.X, Entity.Y);
            var complete = _pathComplete;
            Entity.HasMoveTarget = false;
            Entity.Speed = Fixed.Zero;
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
        var turnRate = locomotor.TurnRatePerTick;
        Entity.Angle = turnRate > Fixed.Zero && difference.Abs() > turnRate
            ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turnRate : turnRate))
            : desired;

        var brakingDistance = locomotor.Braking > Fixed.Zero ? Entity.Speed * Entity.Speed / (locomotor.Braking * Fixed.FromInt(2)) : Fixed.Zero;
        var remaining = distance;
        if (distance <= brakingDistance && _pathIndex < _path.Count - 1) remaining += TailLength();
        var goalSpeed = remaining <= brakingDistance ? locomotor.MinSpeed : CappedSpeed(locomotor);
        if (FixedTrig.AngleDifference(Entity.Angle, desired).Abs() > MisalignedAngle)
        {
            goalSpeed = Fixed.Min(goalSpeed, locomotor.MinSpeed);
            // Inside the turning circle a moving object would orbit the waypoint: stop and turn on the spot instead.
            if (turnRate > Fixed.Zero && distance < locomotor.Speed / turnRate * Fixed.FromInt(2)) goalSpeed = Fixed.Zero;
        }
        Entity.Speed = Entity.Speed < goalSpeed
            ? Fixed.Min(goalSpeed, Entity.Speed + locomotor.Acceleration)
            : Fixed.Max(goalSpeed, Entity.Speed - locomotor.Braking);
        // Standing units on the way (a dozer at work, a halted group) are unknown to the static path: plan once around them instead of rubbing along them while the local avoidance
        // below shoves the step aside (docs/DEMO-FINDINGS.md #12). At most once per 30 ticks; a plan that no longer reaches the goal is dropped and the old path kept.
        if (!air && world.Tick - _blockerReplanTick >= 30 && Entity.Speed > Fixed.Zero)
        {
            // Look-ahead: about 25 ticks of travel (a walking infantry sees 17 units, a fast vehicle 60).
            var ahead = world.StationaryBlockersAhead(Entity, FixedTrig.Cos(Entity.Angle) * Entity.Speed * Fixed.FromInt(25), FixedTrig.Sin(Entity.Angle) * Entity.Speed * Fixed.FromInt(25));
            if (ahead.Count > 0) ReplanAround(navigation, locomotor, ahead, world.Tick);
        }

        // phase 10 (docs/SIMULATION-CORE.md, CnCNew.Simulation/World/SimulationWorld.Collision.cs): bounded local avoidance
        // against another unit's footprint, then the crush/topple collision check — both own-file, additive world services.
        var (stepX, stepY) = air
            ? (FixedTrig.Cos(Entity.Angle) * Entity.Speed, FixedTrig.Sin(Entity.Angle) * Entity.Speed)
            : world.AvoidLocalBlockers(Entity, FixedTrig.Cos(Entity.Angle) * Entity.Speed, FixedTrig.Sin(Entity.Angle) * Entity.Speed);
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
    /// locomotor's turn rate per tick, at a speed that never drops below <see cref="LocomotorInfo.MinSpeed"/>. The order ends without stopping: an aircraft within one tick of travel of the target, or
    /// one whose target lies behind it inside its turning circle (it cannot hit a point there; the reference lets it orbit until it slowed down), has "arrived" and carries on flying —
    /// <see cref="OnWingedArrival"/> lets the derived AI circle. The reference slows an aircraft to its minimum speed once the distance to the goal fits the braking distance; so does this.
    /// </summary>
    private void MoveWinged(IWorldServices world, LocomotorInfo locomotor)
    {
        var targetX = Entity.MoveTargetX;
        var targetY = Entity.MoveTargetY;
        var distance = AirModules.Distance2D(Entity.X, Entity.Y, targetX, targetY);
        var arrival = Fixed.Max(locomotor.CloseEnoughDist, locomotor.Speed);
        var behind = distance > WingEpsilon && FixedTrig.AngleDifference(Entity.Angle, FixedTrig.Atan2(targetY - Entity.Y, targetX - Entity.X)).Abs() > FixedTrig.HalfPi;
        var arrived = distance <= arrival || (behind && distance <= TurnRadius(locomotor, Fixed.Max(Entity.Speed, locomotor.MinSpeed)) * Fixed.FromInt(2));
        var brakingDistance = Fixed.Zero;
        if (locomotor.Braking > Fixed.Zero && Entity.Speed > locomotor.MinSpeed)
            brakingDistance = (Entity.Speed * Entity.Speed - locomotor.MinSpeed * locomotor.MinSpeed) / (locomotor.Braking * Fixed.FromInt(2));
        SteerWinged(locomotor, targetX, targetY, distance <= brakingDistance ? locomotor.MinSpeed : CappedSpeed(locomotor));
        if (!arrived) return;
        Entity.HasMoveTarget = false;
        InvalidatePath();
        world.Emit(SimEventKind.MoveArrived, Entity.Id, EntityId.Invalid, string.Empty, 0);
        OnWingedArrival(targetX, targetY);
    }

    private static readonly Fixed WingEpsilon = Fixed.FromRaw(1024);

    /// <summary><c>Locomotor::calcMinTurnRadius</c>: the radius of the circle an aircraft flies at <paramref name="speed"/> with its turn rate (a huge radius when it cannot turn).</summary>
    protected static Fixed TurnRadius(LocomotorInfo locomotor, Fixed speed)
        => locomotor.TurnRatePerTick > Fixed.Zero ? speed / locomotor.TurnRatePerTick : Fixed.FromInt(1_000_000);

    /// <summary>
    /// One tick of winged flight toward (<paramref name="targetX"/>, <paramref name="targetY"/>): the heading turns toward the target by at most the turn rate (the reference turns an aircraft at its
    /// full turn rate whatever its speed: <c>MinTurnSpeed</c> is a wheeled-vehicle setting), the speed approaches <paramref name="goalSpeed"/> by the locomotor's acceleration or braking but never
    /// leaves the range from <see cref="LocomotorInfo.MinSpeed"/> up, and the aircraft advances along its heading by that speed.
    /// </summary>
    protected void SteerWinged(LocomotorInfo locomotor, Fixed targetX, Fixed targetY, Fixed goalSpeed)
    {
        var dx = targetX - Entity.X;
        var dy = targetY - Entity.Y;
        if (dx.Abs() + dy.Abs() > WingEpsilon)
        {
            var desired = FixedTrig.Atan2(dy, dx);
            var difference = FixedTrig.AngleDifference(Entity.Angle, desired);
            var turn = locomotor.TurnRatePerTick;
            Entity.Angle = turn > Fixed.Zero && difference.Abs() > turn ? FixedTrig.Normalize(Entity.Angle + (difference.IsNegative ? -turn : turn)) : desired;
        }
        goalSpeed = Fixed.Max(goalSpeed, locomotor.MinSpeed);
        var speed = Fixed.Max(Entity.Speed, locomotor.MinSpeed);
        speed = speed < goalSpeed ? Fixed.Min(goalSpeed, speed + locomotor.Acceleration) : Fixed.Max(goalSpeed, speed - locomotor.Braking);
        Entity.Speed = speed;
        Entity.X += FixedTrig.Cos(Entity.Angle) * speed;
        Entity.Y += FixedTrig.Sin(Entity.Angle) * speed;
    }

    private long _blockerReplanTick = -1000;

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
        var goalSpeed = dist <= brakingDist ? locomotor.MinSpeed : CappedSpeed(locomotor);
        Entity.Speed = Entity.Speed < goalSpeed
            ? Fixed.Min(goalSpeed, Entity.Speed + locomotor.Acceleration)
            : Fixed.Max(goalSpeed, Entity.Speed - locomotor.Braking);
        var step = Fixed.Min(Entity.Speed, dist);
        var stepX = dx * step / dist;
        var stepY = dy * step / dist;
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
        InvalidatePath();
        if (reader.Has("pathValid"))
        {
            _pathValid = true;
            _pathComplete = reader.Bool("pathComplete");
            _pathIndex = checked((int)reader.Int("pathIndex"));
            _pathObstacles = reader.UInt("pathObstacles");
            foreach (var item in reader.Array("path")) _path.Add(new PathPoint(item.Fixed("x"), item.Fixed("y")));
        }
        _dock = reader.Bool("docking") ? DockMachine.Load(reader) : null;
    }
}

// ============================================================================================= ProductionUpdate

/// <summary>
/// <c>ProductionUpdate.h</c>. <c>QuantityModifier</c> is typed but not applied: phase 5 spawns one unit per completed queue entry at the producer's own position (no exit-point geometry, no rally points — see docs/GAME-RULES.md).
/// The door-animation timing fields (<c>NumDoorAnimations</c>, <c>DoorOpeningTime</c>, <c>DoorWaitOpenTime</c>, <c>DoorCloseTime</c>, <c>ConstructionCompleteDuration</c>; milliseconds in the INI, ticks here) change no
/// simulation behaviour — nothing waits for a door — and are kept for the presentation only (docs/MODEL-CONDITIONS-BUILDINGS.md); the catalog carries them as Opaque, so they are read from the original text.
/// </summary>
public sealed record ProductionData(int MaxQueueEntries, int NumDoorAnimations = 0, long DoorOpeningTicks = 0, long DoorWaitOpenTicks = 0, long DoorCloseTicks = 0, long ConstructionCompleteTicks = 0) : ModuleData
{
    public static ProductionData Parse(ModuleConfiguration configuration)
    {
        var max = ModuleConfigReader.Int(configuration, "MaxQueueEntries", 0);
        return new ProductionData(
            max <= 0 ? int.MaxValue : checked((int)max),
            checked((int)OpaqueNumber(configuration, "NumDoorAnimations")),
            SimulationTiming.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorOpeningTime")),
            SimulationTiming.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorWaitOpenTime")),
            SimulationTiming.DurationMillisecondsToTicks(OpaqueNumber(configuration, "DoorCloseTime")),
            SimulationTiming.DurationMillisecondsToTicks(OpaqueNumber(configuration, "ConstructionCompleteDuration")));
    }

    /// <summary><c>INI::parseInt</c>/<c>parseDurationUnsignedInt</c> input of a field the catalog keeps Opaque: the first token as a whole number; absent: zero (the constructor default).</summary>
    private static long OpaqueNumber(ModuleConfiguration configuration, string name)
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
/// zero the unit spawns at the producer's own position and the next entry (if any) starts from a fresh tick count. The full
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

    public void Enqueue(int templateIndex, Fixed cost, long totalTicks) => _queue.Add(new ProductionEntry(templateIndex, cost, Math.Max(totalTicks, 1)));

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
        var remaining = head.RemainingTicks - 1;
        if (remaining <= 0 && exit is not null && !exit.IsFreeToExitFor(world.Templates[head.TemplateIndex]))
        {
            // The door is closed (QueueProductionExitUpdate delay): the finished unit waits at the head of the queue.
            _queue[0] = head with { RemainingTicks = 1 };
        }
        else if (remaining <= 0)
        {
            _queue.RemoveAt(0);
            var forcesSupplyWanting = exit is { ForcesSupplyWanting: true };
            var extras = forcesSupplyWanting ? new SpawnExtras(Fixed.Zero, ForceSupplyWanting: true)
                : _hasRally ? new SpawnExtras(Fixed.Zero, HasRally: true, RallyX: _rallyX, RallyY: _rallyY)
                : default(SpawnExtras?);
            var (x, y, z, angle) = exit is null ? (Entity.X, Entity.Y, Entity.Z, Entity.Angle) : exit.ClaimExit(world, world.Templates[head.TemplateIndex]);
            if (extras is { } value) world.RequestSpawn(head.TemplateIndex, Entity.Owner, x, y, z, angle, value);
            else world.RequestSpawn(head.TemplateIndex, Entity.Owner, x, y, z, angle);
            world.Emit(SimEventKind.ProductionCompleted, Entity.Id, EntityId.Invalid, string.Empty, head.TemplateIndex);
        }
        else _queue[0] = head with { RemainingTicks = remaining };
        return _queue.Count == 0 ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
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
