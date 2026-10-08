using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The last nine modules the shipped objects needed (docs/SIMULATION-CORE.md): the salvage and unit crates, the fake-building and Internet-Center saboteurs, the MOAB's
/// <c>SmartBombTargetHomingUpdate</c>, the supply warehouse's <c>SupplyWarehouseCripplingBehavior</c> and the ferry (<c>RailedTransportAIUpdate</c>, <c>RailedTransportContain</c>,
/// <c>RailedTransportDockUpdate</c>, in <c>FerryModules.cs</c>). With them every object of the retail base data is instantiable.
/// </summary>
internal static class LastBlockerModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SalvageCrateData>("SalvageCrateCollide", ModuleOwnerKind.Object, (configuration, _) => SalvageCrateData.Parse(configuration), data => new SalvageCrateCollide(data)),
        new ModuleBinding<UnitCrateData>("UnitCrateCollide", ModuleOwnerKind.Object, (configuration, _) => UnitCrateData.Parse(configuration), data => new UnitCrateCollide(data)),
        new ModuleBinding<CrateCollideData>("SabotageFakeBuildingCrateCollide", ModuleOwnerKind.Object, (configuration, _) => CrateCollideData.Parse(configuration), data => new SabotageFakeBuildingCrateCollide(data)),
        new ModuleBinding<SabotageDurationData>("SabotageInternetCenterCrateCollide", ModuleOwnerKind.Object, (configuration, _) => SabotageDurationData.Parse(configuration, "SabotageDuration"), data => new SabotageInternetCenterCrateCollide(data)),
        new ModuleBinding<SmartBombHomingData>("SmartBombTargetHomingUpdate", ModuleOwnerKind.Object, (configuration, _) => SmartBombHomingData.Parse(configuration), data => new SmartBombTargetHomingUpdate(data)),
        new ModuleBinding<WarehouseCripplingData>("SupplyWarehouseCripplingBehavior", ModuleOwnerKind.Object, (configuration, _) => WarehouseCripplingData.Parse(configuration), data => new SupplyWarehouseCripplingBehavior(data)),
        new ModuleBinding<RailedTransportAiData>("RailedTransportAIUpdate", ModuleOwnerKind.Object, (configuration, _) => RailedTransportAiData.Parse(configuration), data => new RailedTransportAIUpdate(data)),
        new ModuleBinding<TransportContainData>("RailedTransportContain", ModuleOwnerKind.Object, (configuration, _) => TransportContainData.Parse(configuration), data => new RailedTransportContain(data)),
        new ModuleBinding<RailedTransportDockData>("RailedTransportDockUpdate", ModuleOwnerKind.Object, (configuration, _) => RailedTransportDockData.Parse(configuration), data => new RailedTransportDockUpdate(data)),
    ];
}

// ============================================================================================================ SalvageCrateCollide

/// <summary>
/// <c>SalvageCrateCollideModuleData</c>: <c>WeaponChance</c> (default 100 %), <c>LevelChance</c> (25 %), <c>MoneyChance</c> (75 %; parsed and never read, the money branch is the "else"),
/// <c>MinMoney</c> (25) and <c>MaxMoney</c> (75).
/// </summary>
public sealed record SalvageCrateData(CrateCollideData Crate, Fixed WeaponChance, Fixed LevelChance, Fixed MoneyChance, int MinMoney, int MaxMoney) : ModuleData
{
    public static SalvageCrateData Parse(RatedModuleConfiguration configuration) => new(
        CrateCollideData.Parse(configuration),
        ModuleConfigReader.Real(configuration, "WeaponChance", Fixed.One),
        ModuleConfigReader.Real(configuration, "LevelChance", Fixed.FromDecimal(0.25m)),
        ModuleConfigReader.Real(configuration, "MoneyChance", Fixed.FromDecimal(0.75m)),
        checked((int)ModuleConfigReader.Int(configuration, "MinMoney", 25)),
        checked((int)ModuleConfigReader.Int(configuration, "MaxMoney", 75)));
}

/// <summary>
/// <c>SalvageCrateCollide.cpp</c> (the GLA's salvage crate, left by a unit a salvager killed): only a <c>SALVAGER</c> picks it up, and what it gets is decided in this order — an
/// <c>ARMOR_SALVAGER</c> that is not fully armored takes the next armor-set level (<c>ARMORSET_CRATE_UPGRADE_ONE</c>, then <c>_TWO</c>, always); else a <c>WEAPON_SALVAGER</c> not fully
/// armed takes the next weapon-set level with <c>WeaponChance</c> (one logic draw unless the chance is exactly 100 %); else a trainable object below the heroic rank gains one
/// veterancy level with <c>LevelChance</c> (one draw unless exactly 100 %); otherwise the player is paid a random amount from <c>MinMoney</c> to <c>MaxMoney</c> (one draw unless both are equal).
/// The two crate levels live on the entity (<see cref="Entity.CrateWeaponLevel"/>, <see cref="Entity.CrateArmorLevel"/>) and select the template's <c>CRATEUPGRADE_ONE/TWO</c> weapon set and
/// <c>CRATE_UPGRADE_ONE/TWO</c> armor set (<see cref="Templates.ObjectTemplate.CrateUpgradeWeapon"/>); the combinations with <c>PLAYER_UPGRADE</c> or <c>SECOND_LIFE</c> are not modeled
/// (the single-condition set applies). The pickup sounds, the floating cash text and the academy statistic are presentation. <c>CrateCollected</c> carries the money paid, the level
/// gained, or the new set level.
/// </summary>
public sealed class SalvageCrateCollide(SalvageCrateData data) : CrateCollideBase(data.Crate)
{
    public override string ModuleName => "SalvageCrateCollide";

    protected override bool IsValidToExecute(IWorldServices world, Entity other)
        => base.IsValidToExecute(world, other) && other.Template.IsKindOf("SALVAGER");

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        long value;
        if (EligibleForArmorSet(other))
        {
            other.CrateArmorLevel = other.CrateArmorLevel == 1 ? 2 : 1;
            value = other.CrateArmorLevel;
            world.MarkDirty(other);
        }
        else if (EligibleForWeaponSet(other) && Chance(world, data.WeaponChance))
        {
            other.CrateWeaponLevel = other.CrateWeaponLevel == 1 ? 2 : 1;
            value = other.CrateWeaponLevel;
            world.WeaponSetFlagsChanged(other);
        }
        else if (EligibleForLevel(other) && Chance(world, data.LevelChance))
        {
            world.GainExperienceForLevels(other, 1, true);
            value = 1;
        }
        else
        {
            var money = data.MinMoney != data.MaxMoney ? world.LogicRandom.Range(data.MinMoney, data.MaxMoney) : data.MinMoney;
            if (money > 0) world.AddMoney(other.Owner, Fixed.FromInt(money));
            value = money;
        }
        world.Emit(SimEventKind.CrateCollected, Entity.Id, other.Id, ModuleName, value);
        return true;
    }

    private static bool EligibleForWeaponSet(Entity other) => other.Template.IsKindOf("WEAPON_SALVAGER") && other.CrateWeaponLevel < 2;

    private static bool EligibleForArmorSet(Entity other) => other.Template.IsKindOf("ARMOR_SALVAGER") && other.CrateArmorLevel < 2;

    private static bool EligibleForLevel(Entity other) => other.VeterancyLevel < 3 && other.Template.IsTrainable;

    /// <summary><c>testWeaponChance</c>/<c>testLevelChance</c>: "don't waste a random number for a 100%".</summary>
    private static bool Chance(IWorldServices world, Fixed chance) => chance == Fixed.One || world.LogicRandom.Range(Fixed.Zero, Fixed.One) < chance;
}

// ============================================================================================================ UnitCrateCollide

/// <summary><c>UnitCrateCollideModuleData</c>: <c>UnitCount</c> (default 0) units of <c>UnitName</c> (default none).</summary>
public sealed record UnitCrateData(CrateCollideData Crate, int UnitCount, string UnitName) : ModuleData
{
    public static UnitCrateData Parse(RatedModuleConfiguration configuration) => new(
        CrateCollideData.Parse(configuration), checked((int)ModuleConfigReader.Int(configuration, "UnitCount", 0)), ModuleConfigReader.Text(configuration, "UnitName"));
}

/// <summary>
/// <c>UnitCrateCollide.cpp</c> (the free-units crate): the toucher's player receives <c>UnitCount</c> new objects of <c>UnitName</c>, each facing the way the toucher faces. A name that is
/// no object (or no instantiable one) leaves the crate where it is, as in the reference, but reports the skipped creation instead of staying silent. Approximation: the reference
/// asks the partition manager for a free spot within 20 units of the toucher (<c>findPositionAround</c>); there is no collision search here, so the units stand on a ring around the
/// toucher whose radius is the two bounding radii added, evenly spread, the first one in front of the toucher's right side. The pickup sound is presentation.
/// </summary>
public sealed class UnitCrateCollide(UnitCrateData data) : CrateCollideBase(data.Crate)
{
    public override string ModuleName => "UnitCrateCollide";

    protected override bool ExecuteCrateBehavior(IWorldServices world, Entity other)
    {
        if (!world.Templates.TryGet(data.UnitName, out var template))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.UnitName, CreationSkipReasons.UnknownTemplate);
            return false;
        }
        if (!template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.UnitName, CreationSkipReasons.TemplateNotInstantiable);
            return false;
        }
        var radius = other.Template.BoundingCircleRadius + template.BoundingCircleRadius;
        for (var index = 0; index < data.UnitCount; index++)
        {
            var angle = FixedTrig.Normalize(other.Angle + FixedTrig.Pi / Fixed.FromInt(2) + FixedTrig.TwoPi * Fixed.FromInt(index) / Fixed.FromInt(data.UnitCount));
            var x = other.X + radius * FixedTrig.Cos(angle);
            var y = other.Y + radius * FixedTrig.Sin(angle);
            world.RequestSpawn(template.Index, other.Owner, x, y, world.GroundHeightAt(x, y), other.Angle);
        }
        Collected(world, other, data.UnitCount);
        return true;
    }
}

// ============================================================================================================ SabotageFakeBuildingCrateCollide

/// <summary>
/// <c>SabotageFakeBuildingCrateCollide.cpp</c> (the GLA saboteur against a decoy): an enemy <c>FS_FAKE</c> structure takes unresistable damage equal to its maximum health
/// (<c>DEATH_DETONATED</c>, credited to the saboteur), so it blows up at once. The infiltration blip, EVA line and sabotage flash are presentation.
/// </summary>
public sealed class SabotageFakeBuildingCrateCollide(CrateCollideData crate) : SabotageCollideBase(crate)
{
    public override string ModuleName => "SabotageFakeBuildingCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("FS_FAKE");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        if (building.Body is { } body)
            world.Damage(building.Id, new DamageInfo(Entity.Id, body.MaxHealth, DamageTypes.Unresistable, DeathTypes.Detonated, false));
        return 0;
    }
}

// ============================================================================================================ SabotageInternetCenterCrateCollide

/// <summary>
/// <c>SabotageInternetCenterCrateCollide.cpp</c>: an enemy <c>FS_INTERNET_CENTER</c> is sabotaged for <c>SabotageDuration</c>. Every Internet Center of the victim's player has its
/// <see cref="SpyVisionUpdate"/> switched off until then (they stack, and the spying only resumes after its self-powered interval), and every hacker inside the sabotaged center is
/// disabled (<see cref="HackInternetAIUpdate.DisableUntil"/>: no hacking and no cash until the tick). The reference also marks the center itself <c>DISABLED_HACKED</c>; the
/// simulation has no module of an Internet Center that a general disabled state would stop (its spy vision and its hackers are handled above), so that mark is not stored. The
/// infiltration blip, EVA line and sabotage flash are presentation.
/// </summary>
public sealed class SabotageInternetCenterCrateCollide(SabotageDurationData data) : SabotageCollideBase(data.Crate)
{
    public override string ModuleName => "SabotageInternetCenterCrateCollide";
    protected override bool IsTargetKind(Entity building) => building.Template.IsKindOf("FS_INTERNET_CENTER");

    protected override long Sabotage(IWorldServices world, Entity building)
    {
        var until = world.Tick + data.DurationTicks;
        foreach (var center in world.Entities)
        {
            if (center.Owner != building.Owner || center.DestroyRequested || !center.Template.IsKindOf("FS_INTERNET_CENTER")) continue;
            foreach (var module in center.Modules)
                if (module is SpyVisionUpdate spy) spy.SetDisabledUntilFrame(world, until);
        }
        if (building.Container is { } contain)
            foreach (var id in contain.Contained)
            {
                if (world.FindEntity(id) is not { } hacker) continue;
                foreach (var module in hacker.Modules)
                    if (module is HackInternetAIUpdate update) update.DisableUntil(world, until);
            }
        return data.DurationTicks;
    }
}

// ============================================================================================================ SmartBombTargetHomingUpdate

/// <summary><c>SmartBombTargetHomingUpdateModuleData</c>: <c>CourseCorrectionScalar</c> (default 0.99; 1 = no homing, 0 = snap to the target).</summary>
public sealed record SmartBombHomingData(Fixed CourseCorrectionScalar) : ModuleData
{
    public static SmartBombHomingData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Real(configuration, "CourseCorrectionScalar", Fixed.FromDecimal(0.99m)));
}

/// <summary>
/// <c>SmartBombTargetHomingUpdate.cpp</c> (the MOAB): once a delivering plane told it where its payload is headed (<see cref="SetTargetPosition"/>, <c>DeliverPayloadAIUpdate</c>), every tick
/// it spends well above the terrain it moves its x and y to <c>target · (1 − s) + position · s</c> with <c>s</c> the course-correction scalar clamped to [0, 1] (the height is left to the
/// fall), so a falling bomb drifts onto its target and lands on it. A bomb that was never told a target (or told the point (0, 0, 0), which the reference refuses too) does nothing.
/// The module updates every tick for the bomb's whole life, as the reference does.
/// <para>
/// The scalar is a mixing factor per logic frame of the original: the mix is taken once per frame, from where the bomb is then, and the way it moves the bomb is flown in equal
/// shares over the ticks of that frame, so the bomb is where 30 Hz has it when the frame is over (docs/RATE-INVARIANCE-PROJECTILES.md). At 30 Hz a frame is a tick.
/// </para>
/// </summary>
public sealed class SmartBombTargetHomingUpdate(SmartBombHomingData data) : SimModule, IUpdateModule
{
    private long _nextWake = SimulationTiming.SleepForever;
    private bool _targetReceived;
    private Fixed _targetX;
    private Fixed _targetY;
    private Fixed _targetZ;
    // The way the mix of the current frame moves the bomb and the ticks of it already flown (0: this tick takes a mix). Always 0 at 30 Hz (and then not written).
    private Fixed _frameStepX;
    private Fixed _frameStepY;
    private int _subStep;

    public override string ModuleName => "SmartBombTargetHomingUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool TargetReceived => _targetReceived;
    public Fixed TargetX => _targetX;
    public Fixed TargetY => _targetY;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary><c>SetTargetPosition</c>: a zero point ("received a zero coord") is not a real-world location and is refused.</summary>
    public void SetTargetPosition(IWorldServices world, Fixed x, Fixed y, Fixed z)
    {
        if (x == Fixed.Zero && y == Fixed.Zero && z == Fixed.Zero) return;
        _targetX = x;
        _targetY = y;
        _targetZ = z;
        _targetReceived = true;
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        var time = world.Time;
        if (_subStep != 0)
        {
            Entity.X += time.FrameShare(_frameStepX, _subStep);
            Entity.Y += time.FrameShare(_frameStepY, _subStep);
            _subStep = time.NextSubStep(_subStep);
            world.MarkDirty(Entity);
            return SimulationTiming.SleepNone;
        }
        if (!_targetReceived || !world.IsSignificantlyAboveTerrain(Entity)) return SimulationTiming.SleepNone;
        var status = Fixed.Max(Fixed.Zero, Fixed.Min(Fixed.One, data.CourseCorrectionScalar));
        var target = Fixed.One - status;
        var x = _targetX * target + Entity.X * status;
        var y = _targetY * target + Entity.Y * status;
        if (time.TicksPerOriginalFrame == 1)
        {
            Entity.X = x;
            Entity.Y = y;
        }
        else
        {
            _frameStepX = x - Entity.X;
            _frameStepY = y - Entity.Y;
            Entity.X += time.FrameShare(_frameStepX, 0);
            Entity.Y += time.FrameShare(_frameStepY, 0);
            _subStep = time.NextSubStep(0);
        }
        world.MarkDirty(Entity);
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (!_targetReceived) return;
        writer.Bool("targetReceived", true);
        writer.Fixed("targetX", _targetX);
        writer.Fixed("targetY", _targetY);
        writer.Fixed("targetZ", _targetZ);
        if (_subStep == 0) return;
        writer.Int("subStep", _subStep);
        writer.Fixed("frameStepX", _frameStepX);
        writer.Fixed("frameStepY", _frameStepY);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _targetReceived = reader.Has("targetReceived") && reader.Bool("targetReceived");
        if (!_targetReceived) return;
        _targetX = reader.Fixed("targetX");
        _targetY = reader.Fixed("targetY");
        _targetZ = reader.Fixed("targetZ");
        _subStep = reader.Has("subStep") ? checked((int)reader.Int("subStep")) : 0;
        _frameStepX = _subStep != 0 ? reader.Fixed("frameStepX") : Fixed.Zero;
        _frameStepY = _subStep != 0 ? reader.Fixed("frameStepY") : Fixed.Zero;
    }
}

// ============================================================================================================ SupplyWarehouseCripplingBehavior

/// <summary><c>SupplyWarehouseCripplingBehaviorModuleData</c>: <c>SelfHealSupression</c> and <c>SelfHealDelay</c> (ticks, default 0), <c>SelfHealAmount</c> (health, default 0).</summary>
public sealed record WarehouseCripplingData(long SuppressTicks, long DelayTicks, Fixed Amount) : ModuleData
{
    public static WarehouseCripplingData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "SelfHealSupression", 0), ModuleConfigReader.Ticks(configuration, "SelfHealDelay", 0), ModuleConfigReader.Real(configuration, "SelfHealAmount", Fixed.Zero));
}

/// <summary>
/// <c>SupplyWarehouseCripplingBehavior.cpp</c> (a warehouse that is shot at): when the body falls into <c>REALLYDAMAGED</c> the object's dock is crippled
/// (<see cref="IDockModule.SetDockCrippled"/>: trucks may still approach but are never let in, one that is inside is killed), and when it climbs out of it the dock works again.
/// Every hit starts the healing suppression (<c>SelfHealSupression</c> ticks of rest); after that the warehouse heals <c>SelfHealAmount</c> health every <c>SelfHealDelay</c> ticks until
/// it is whole again, the module sleeping in between exactly as the reference does. An object without a dock module is not crippled (the reference would dereference a null dock; the
/// shipped warehouse has one).
/// </summary>
public sealed class SupplyWarehouseCripplingBehavior(WarehouseCripplingData data) : SimModule, IUpdateModule, IDamageModule
{
    private long _nextWake = SimulationTiming.SleepForever;
    private long _suppressedUntil;
    private long _nextHealing;

    public override string ModuleName => "SupplyWarehouseCripplingBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary><c>m_healingSupressedUntilFrame</c>.</summary>
    public long HealingSuppressedUntil => _suppressedUntil;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        // "we got hit, time to get up for work after a quick snooze"
        _suppressedUntil = world.Tick + data.SuppressTicks;
        _nextHealing = _suppressedUntil;
        world.WakeAt(Entity, this, world.Tick + world.Time.AtLeastOneFrame(data.SuppressTicks));
        world.MarkDirty(Entity);
    }

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        if (newState == BodyDamageStates.ReallyDamaged) DockModules.DockOf(Entity)?.SetDockCrippled(world, true);
        else if (oldState == BodyDamageStates.ReallyDamaged) DockModules.DockOf(Entity)?.SetDockCrippled(world, false);
    }

    public long Update(IWorldServices world)
    {
        // Suppression is handled by sleeping the module, so if I am here, I know it is time to heal.
        _nextHealing = world.Tick + data.DelayTicks;
        world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, data.Amount, DamageTypes.Healing, DeathTypes.None, false));
        if (Entity.Body is { } body && body.Health >= body.MaxHealth) return SimulationTiming.SleepForever;
        // A SelfHealDelay of 0 heals once per frame of the original, not once per tick.
        return world.Time.AtLeastOneFrame(_nextHealing - world.Tick);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        if (_suppressedUntil == 0 && _nextHealing == 0) return;
        writer.Int("suppressedUntil", _suppressedUntil);
        writer.Int("nextHealing", _nextHealing);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _suppressedUntil = reader.Has("suppressedUntil") ? reader.Int("suppressedUntil") : 0;
        _nextHealing = reader.Has("nextHealing") ? reader.Int("nextHealing") : 0;
    }
}
