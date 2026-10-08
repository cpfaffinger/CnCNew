using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The second batch of modules that keep retail structures and units from being instantiable: the production exits, the
/// healing barracks contain, pilot ejection, veterancy on creation, the radar, and the GLA rebuild hole pair.
/// Whatever part of a module's behaviour needs a subsystem that does not exist yet (AI, radar, preorders) is stated at the module.
/// </summary>
internal static class RetailMatchModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<ProductionExitData>("DefaultProductionExitUpdate", ModuleOwnerKind.Object, (configuration, _) => ProductionExitData.Parse(configuration), data => new DefaultProductionExitUpdate(data)),
        new ModuleBinding<ProductionExitData>("SupplyCenterProductionExitUpdate", ModuleOwnerKind.Object, (configuration, _) => ProductionExitData.Parse(configuration), data => new SupplyCenterProductionExitUpdate(data)),
        new ModuleBinding<ProductionExitData>("QueueProductionExitUpdate", ModuleOwnerKind.Object, (configuration, _) => ProductionExitData.Parse(configuration), data => new QueueProductionExitUpdate(data)),
        new ModuleBinding<HealContainData>("HealContain", ModuleOwnerKind.Object, (configuration, _) => HealContainData.Parse(configuration), data => new HealContain(data)),
        new ModuleBinding<EjectPilotDieData>("EjectPilotDie", ModuleOwnerKind.Object, (configuration, _) => EjectPilotDieData.Parse(configuration), data => new EjectPilotDie(data)),
        new ModuleBinding<VeterancyGainCreateData>("VeterancyGainCreate", ModuleOwnerKind.Object, (configuration, _) => VeterancyGainCreateData.Parse(configuration), data => new VeterancyGainCreate(data)),
        new ModuleBinding<NoFieldsData>("PreorderCreate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new PreorderCreate()),
        new ModuleBinding<CommandButtonHuntData>("CommandButtonHuntUpdate", ModuleOwnerKind.Object, (configuration, _) => CommandButtonHuntData.Parse(configuration), data => new CommandButtonHuntUpdate(data)),
        new ModuleBinding<LockWeaponCreateData>("LockWeaponCreate", ModuleOwnerKind.Object, (configuration, _) => LockWeaponCreateData.Parse(configuration), data => new LockWeaponCreate(data)),
        new ModuleBinding<RadarUpdateData>("RadarUpdate", ModuleOwnerKind.Object, (configuration, _) => RadarUpdateData.Parse(configuration), data => new RadarUpdate(data)),
        new ModuleBinding<PowerPlantUpdateData>("PowerPlantUpdate", ModuleOwnerKind.Object, (configuration, _) => PowerPlantUpdateData.Parse(configuration), data => new PowerPlantUpdate(data)),
        new ModuleBinding<OverchargeData>("OverchargeBehavior", ModuleOwnerKind.Object, (configuration, _) => OverchargeData.Parse(configuration), data => new OverchargeBehavior(data)),
        new ModuleBinding<RebuildHoleExposeDieData>("RebuildHoleExposeDie", ModuleOwnerKind.Object, (configuration, _) => RebuildHoleExposeDieData.Parse(configuration), data => new RebuildHoleExposeDie(data)),
        new ModuleBinding<RebuildHoleBehaviorData>("RebuildHoleBehavior", ModuleOwnerKind.Object, (configuration, _) => RebuildHoleBehaviorData.Parse(configuration), data => new RebuildHoleBehavior(data)),
    ];
}

// ============================================================================================================ HealContain

/// <summary><c>OpenContainModuleData</c> plus the two <c>HealContainModuleData</c> keys. <c>ContainMax</c> below zero means unlimited, as in <see cref="GarrisonContain"/>.</summary>
public sealed record HealContainData(OpenContainData Base, int ContainMax, long TimeForFullHealTicks) : ModuleData
{
    public static HealContainData Parse(RatedModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, []),
        checked((int)ModuleConfigReader.Int(configuration, "ContainMax", -1)),
        ModuleConfigReader.Ticks(configuration, "TimeForFullHeal", 0));
}

/// <summary>
/// <c>HealContain.cpp</c>: every logic frame of the original each occupant is healed <c>MaxHealth / TimeForFullHeal</c> (the duration in frames); once it
/// has been inside for the whole duration it is set to full health and leaves the building. The frames are counted from the tick the occupant came in,
/// so its health is that of 30 Hz at every frame boundary (30 Hz: every tick). The reference lets the occupant walk out through the building's own exit
/// interface; without AI it is placed at the container (<c>Remove(positionAtContainer)</c>).
/// </summary>
public sealed class HealContain(HealContainData data) : ContainModuleBase(data.Base)
{
    private readonly Dictionary<uint, long> _containedSinceTick = [];

    public override string ModuleName => "HealContain";
    public override int ContainMax => data.ContainMax;

    public override void OnSpawn(IWorldServices world) => NextWake = SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
        => !Entity.EffectivelyDead && PassesKindOfAndRelationship(world, candidate) && (!checkCapacity || data.ContainMax < 0 || ContainCount < data.ContainMax);

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        _containedSinceTick[candidate.Id.Value] = world.Tick;
        world.WakeAt(Entity, this, world.Tick);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        base.Remove(world, candidate, positionAtContainer);
        _containedSinceTick.Remove(candidate.Id.Value);
    }

    public override long Update(IWorldServices world)
    {
        PruneDead(world);
        foreach (var riderId in Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider?.Body is null) continue;
            var since = _containedSinceTick.GetValueOrDefault(riderId.Value, world.Tick);
            var ticks = world.Time.TicksPerOriginalFrame;
            if (ticks != TickOrder.Next && (world.Tick - since) % ticks != 0) continue;
            var done = data.TimeForFullHealTicks <= 0 || world.Tick - since >= data.TimeForFullHealTicks;
            var amount = done ? rider.Body.MaxHealth : world.Time.SharePerTick(rider.Body.MaxHealth, Fixed.FromInt(world.Time.ToOriginalFrames(data.TimeForFullHealTicks)));
            world.Damage(rider.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
            if (done) Remove(world, rider, true);
        }
        return ContainCount > 0 ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", NextWake);
        writer.BeginArray("contained");
        foreach (var riderId in Riders)
        {
            writer.BeginObject(null);
            writer.UInt("id", riderId.Value);
            writer.Int("since", _containedSinceTick.GetValueOrDefault(riderId.Value));
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        NextWake = reader.Int("nextWake");
        Riders.Clear();
        _containedSinceTick.Clear();
        foreach (var entry in reader.Array("contained"))
        {
            var id = new EntityId(checked((uint)entry.UInt("id")));
            Riders.Add(id);
            _containedSinceTick[id.Value] = entry.Int("since");
        }
    }
}

// ============================================================================================================ EjectPilotDie

/// <summary>
/// <c>InvulnerableTime</c> is parsed by the reference but its <c>EjectPilotDie::onDie</c> never reads it, so it is typed and not used.
/// </summary>
public sealed record EjectPilotDieData(DieMuxData Mux, string GroundCreationList, string AirCreationList, long InvulnerableTicks) : DieData(Mux)
{
    public static EjectPilotDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "GroundCreationList"), ModuleConfigReader.Text(configuration, "AirCreationList"),
        ModuleConfigReader.Ticks(configuration, "InvulnerableTime", 0));
}

/// <summary><c>EjectPilotDie.cpp</c>: on death runs the air list when the vehicle is significantly above the terrain, the ground list otherwise.</summary>
public sealed class EjectPilotDie(EjectPilotDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "EjectPilotDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info)) return;
        var list = world.IsSignificantlyAboveTerrain(Entity) ? data.AirCreationList : data.GroundCreationList;
        if (list.Length > 0) world.CreateFromList(Entity, list, info.Source, false);
    }
}

// ============================================================================================================ VeterancyGainCreate

public sealed record VeterancyGainCreateData(int StartingLevel, string ScienceRequired) : ModuleData
{
    public static VeterancyGainCreateData Parse(RatedModuleConfiguration configuration)
    {
        var level = ModuleConfigReader.Find(configuration, "StartingLevel")?.Value.Value is EnumValue rank && NameTables.Veterancy.TryIndexOf(rank.Name, out var index) ? index : 0;
        return new(level, ModuleConfigReader.Text(configuration, "ScienceRequired"));
    }
}

/// <summary>
/// <c>VeterancyGainCreate::onCreate</c>: an owner that holds the required science (or none is required) gets a trainable object at
/// <c>StartingLevel</c>. The reference stores it as the object's <em>minimum</em> level (<c>setMinVeterancyLevel</c>): experience is set to
/// that level's requirement, so later experience can only raise it.
/// </summary>
public sealed class VeterancyGainCreate(VeterancyGainCreateData data) : SimModule
{
    public override string ModuleName => "VeterancyGainCreate";

    public override void OnSpawn(IWorldServices world)
    {
        if (!Entity.Template.IsTrainable) return;
        if (data.ScienceRequired.Length > 0)
        {
            if (!world.Sciences.TryGet(data.ScienceRequired, out var science) || !world.PlayerSciences(Entity.Owner).Has(science.Index)) return;
        }
        world.SetMinVeterancyLevel(Entity, data.StartingLevel);
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ PreorderCreate

/// <summary><c>PreorderCreate.cpp</c> only shows the pre-order decal when the player bought the game early: there is no preorder state to read, so the module is an inert placeholder with no keys.</summary>
public sealed class PreorderCreate : SimModule
{
    public override string ModuleName => "PreorderCreate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ CommandButtonHuntUpdate

public sealed record CommandButtonHuntData(long ScanTicks, Fixed ScanRange) : ModuleData
{
    public static CommandButtonHuntData Parse(RatedModuleConfiguration configuration)
        => new(ModuleConfigReader.Ticks(configuration, "ScanRate", 0), ModuleConfigReader.Real(configuration, "ScanRange", Fixed.Zero));
}

/// <summary>
/// <c>CommandButtonHuntUpdate.cpp</c> only ever acts after a script told the object which command button to hunt with
/// (<c>TEAM_HUNT_WITH_COMMAND_BUTTON</c>, <c>ScriptActions::doTeamHuntWithCommandButton</c>); on its own it does nothing, which is what all 86 uses in the
/// shipped data rely on ("allows use of command button hunt script with this unit"). The module therefore exists so that such objects can be created, and
/// keeps no state: the script actions that would drive it are not implemented and are counted as missing by the script engine, never skipped silently
/// (<c>docs/MAP-SCRIPTS.md</c> §8).
/// </summary>
public sealed class CommandButtonHuntUpdate(CommandButtonHuntData data) : SimModule
{
    public override string ModuleName => "CommandButtonHuntUpdate";
    public CommandButtonHuntData Data => data;
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ LockWeaponCreate

public sealed record LockWeaponCreateData(int Slot) : ModuleData
{
    public static LockWeaponCreateData Parse(RatedModuleConfiguration configuration)
    {
        // LockWeaponCreateModuleData: m_slotToLock defaults to PRIMARY_WEAPON; SlotToLock is a TheWeaponSlotTypeNames lookup.
        var slot = ModuleConfigReader.Text(configuration, "SlotToLock");
        if (slot.Length == 0) return new(Templates.WeaponSlot.Primary);
        return Templates.WeaponSlot.TryParse(slot, out var index) ? new(index) : throw new InvalidDataException($"LockWeaponCreate: unknown SlotToLock '{slot}'.");
    }
}

/// <summary>
/// <c>LockWeaponCreate.cpp</c>: <c>onBuildComplete</c> locks a weapon slot permanently (<c>setWeaponLock(slot, LOCKED_PERMANENTLY)</c>), so automatic weapon choice never leaves it
/// (the data's comment: "prevents indeterminate state plus two unpickable weapons equaling no attack"). The lock is taken when the object spawns, which is when a produced or
/// placed unit is complete; an object spawned under construction (a structure placed by a builder) is not locked. The module keeps no state of its own: the lock lives in the
/// entity's weapon slots, and an object whose weapon sets hold one weapon has no choice to lock (<see cref="IWorldServices.SetWeaponLock"/> accepts it without storing anything).
/// </summary>
public sealed class LockWeaponCreate(LockWeaponCreateData data) : SimModule
{
    public override string ModuleName => "LockWeaponCreate";
    public LockWeaponCreateData Data => data;

    public override void OnSpawn(IWorldServices world)
    {
        if (!Entity.HasStatus(ObjectStatusBits.UnderConstruction)) world.SetWeaponLock(Entity, data.Slot, WeaponLockKind.Permanent);
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ RadarUpdate

public sealed record RadarUpdateData(long ExtendTicks) : ModuleData
{
    public static RadarUpdateData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "RadarExtendTime", 0));
}

/// <summary>
/// <c>RadarUpdate.cpp</c>: passive. <c>RadarUpgrade</c> calls <see cref="ExtendRadar"/>, which starts the extension animation timer;
/// the extension completes <c>RadarExtendTime</c> later. No radar or minimap exists in the simulation, so only this state is kept.
/// </summary>
public sealed class RadarUpdate(RadarUpdateData data) : SimModule
{
    private long _extendDoneTick;
    private bool _radarActive;

    public override string ModuleName => "RadarUpdate";
    public bool RadarActive => _radarActive;
    public bool IsExtendComplete(long tick) => _radarActive && tick > _extendDoneTick;

    public void ExtendRadar(IWorldServices world)
    {
        _extendDoneTick = world.Tick + data.ExtendTicks;
        _radarActive = true;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("extendDoneTick", _extendDoneTick);
        writer.Bool("radarActive", _radarActive);
    }

    public override void Load(IStateReader reader)
    {
        _extendDoneTick = reader.Int("extendDoneTick");
        _radarActive = reader.Bool("radarActive");
    }
}

// ============================================================================================================ PowerPlantUpdate

public sealed record PowerPlantUpdateData(long ExtendTicks) : ModuleData
{
    public static PowerPlantUpdateData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "RodsExtendTime", 0));
}

/// <summary>
/// <c>PowerPlantUpdate.cpp</c>: passive like <see cref="RadarUpdate"/>. <see cref="ExtendRods"/> (called by <c>PowerPlantUpgrade</c>) starts the
/// extension animation timer; <c>update()</c> then only flips the model condition, which the simulation does not have, so only the extended
/// flag is kept. Retracting is instant.
/// </summary>
public sealed class PowerPlantUpdate(PowerPlantUpdateData data) : SimModule
{
    private bool _extended;
    private long _extendDoneTick;

    public override string ModuleName => "PowerPlantUpdate";
    public bool Extended => _extended;
    public bool IsExtendComplete(long tick) => _extended && tick >= _extendDoneTick;

    public void ExtendRods(IWorldServices world, bool extend)
    {
        if (!extend) { _extended = false; return; }
        if (_extended) return;
        _extended = true;
        _extendDoneTick = world.Tick + data.ExtendTicks;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Bool("extended", _extended);
        writer.Int("extendDoneTick", _extendDoneTick);
    }

    public override void Load(IStateReader reader)
    {
        _extended = reader.Bool("extended");
        _extendDoneTick = reader.Int("extendDoneTick");
    }
}

// ============================================================================================================ OverchargeBehavior

public sealed record OverchargeData(Fixed HealthDrainRatioPerSecond, Fixed NotAllowedBelowRatio) : ModuleData
{
    public static OverchargeData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "HealthPercentToDrainPerSecond", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "NotAllowedWhenHealthBelowPercent", Fixed.Zero));
}

/// <summary>
/// <c>OverchargeBehavior.cpp</c>: while enabled the owner gets the object's <c>EnergyBonus</c> (<c>Player::addPowerBonus</c>), the rods of a
/// <see cref="PowerPlantUpdate"/> are extended, and every tick <c>MaxHealth * HealthPercentToDrainPerSecond / 30</c> is taken as
/// <c>DAMAGE_PENALTY</c>; below <c>NotAllowedWhenHealthBelowPercent</c> it switches itself off. Deleting the object removes the bonus.
/// Not modeled: the <c>TOGGLE_OVERCHARGE</c> command (there is no command for it, so <see cref="Toggle"/>/<see cref="Enable"/> are reachable
/// from code and tests only), the local-player message and radar event, and <c>onCapture</c> (no capture).
/// </summary>
public sealed class OverchargeBehavior(OverchargeData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private bool _active;

    public override string ModuleName => "OverchargeBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool Active => _active;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void Toggle(IWorldServices world) => Enable(world, !_active);

    public void Enable(IWorldServices world, bool enable)
    {
        if (enable == _active) return;
        var plant = Entity.Modules.OfType<PowerPlantUpdate>().FirstOrDefault();
        plant?.ExtendRods(world, enable);
        world.AddEnergyProduction(Entity.Owner, enable ? Entity.Template.EnergyBonus : -Entity.Template.EnergyBonus);
        _active = enable;
        world.WakeAt(Entity, this, enable ? world.Tick + SimulationTiming.SleepNone : world.Tick + SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        if (!_active || Entity.Body is not { } body) return SimulationTiming.SleepForever;
        var amount = world.Time.PerSecondToPerTick(body.MaxHealth * data.HealthDrainRatioPerSecond);
        world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Penalty, DeathTypes.Normal, false));
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        if (body.Health < body.MaxHealth * data.NotAllowedBelowRatio)
        {
            Enable(world, false);
            return SimulationTiming.SleepForever;
        }
        return SimulationTiming.SleepNone;
    }

    public override void OnRemove(IWorldServices world)
    {
        if (!_active) return;
        world.AddEnergyProduction(Entity.Owner, -Entity.Template.EnergyBonus);
        _active = false;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("active", _active);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _active = reader.Bool("active");
    }
}

// ============================================================================================================ GLA rebuild hole

public sealed record RebuildHoleExposeDieData(DieMuxData Mux, string HoleName, Fixed HoleMaxHealth, bool TransferAttackers) : DieData(Mux)
{
    public static RebuildHoleExposeDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "HoleName"), ModuleConfigReader.Real(configuration, "HoleMaxHealth", Fixed.Zero),
        // RebuildHoleExposeDieModuleData sets m_transferAttackers = true; the attacker hand-over is AI (phase 6).
        ModuleConfigReader.Bool(configuration, "TransferAttackers", true));
}

/// <summary>
/// <c>RebuildHoleExposeDie::onDie</c>: a dying GLA structure leaves a hole of the same owner, position and orientation with
/// <c>HoleMaxHealth</c> hit points, unless the owner is neutral or the structure was still under construction. Not modeled: the
/// <c>isPlayerActive</c> check (no per-player activity state), copying the geometry to the hole, and <c>TransferAttackers</c>.
/// </summary>
public sealed class RebuildHoleExposeDie(RebuildHoleExposeDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "RebuildHoleExposeDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info) || Entity.Owner.IsSystem || Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        if (!world.Templates.TryGet(data.HoleName, out var hole))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.HoleName, CreationSkipReasons.UnknownTemplate);
            return;
        }
        if (!hole.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.HoleName, CreationSkipReasons.TemplateNotInstantiable);
            return;
        }
        world.RequestSpawn(hole.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle, new SpawnExtras(data.HoleMaxHealth, Entity.Template.Index));
    }
}

public sealed record RebuildHoleBehaviorData(string WorkerName, long WorkerRespawnDelayTicks, Fixed HealthRegenPercentPerSecond) : ModuleData
{
    public static RebuildHoleBehaviorData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Text(configuration, "WorkerObjectName"), ModuleConfigReader.Ticks(configuration, "WorkerRespawnDelay", 0),
        ModuleConfigReader.Real(configuration, "HoleHealthRegen%PerSecond", Fixed.Zero));
}

/// <summary>
/// <c>RebuildHoleBehavior.cpp</c>: the hole regenerates <c>HoleHealthRegen%PerSecond</c> of its maximum health per second and, once
/// <c>WorkerRespawnDelay</c> has passed, spawns a worker that reconstructs the destroyed structure. The worker and its
/// <c>construct</c> order are AI (phase 6), so at that point a <see cref="SimEventKind.CreationSkipped"/> event
/// (<see cref="CreationSkipReasons.SettingNotSimulated"/>) records the missing rebuild and the countdown stops.
/// </summary>
public sealed class RebuildHoleBehavior(RebuildHoleBehaviorData data) : SimModule, IUpdateModule, ISpawnOriginReceiver
{
    private long _nextWake = -1;
    private long _workerWait = -1;
    private int _origin = -1;

    public override string ModuleName => "RebuildHoleBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public void OnSpawnedFrom(IWorldServices world, int originTemplate)
    {
        _ = world;
        _origin = originTemplate;
        _workerWait = data.WorkerRespawnDelayTicks;
    }

    public long Update(IWorldServices world)
    {
        var body = Entity.Body;
        if (body is not null && body.Health < body.MaxHealth && data.HealthRegenPercentPerSecond > Fixed.Zero)
        {
            if (world.Time.TicksPerOriginalFrame == TickOrder.Next)
            {
                var amount = world.Time.PerSecondToPerTick(body.MaxHealth * data.HealthRegenPercentPerSecond);
                world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
            }
            else if (world.Time.BeginsFrameOf(Entity, world.Tick))
            {
                // Above 30 Hz the hole regenerates the slice of a logic frame of the original once per frame (the escape "Originalraster" for a heal slice,
                // docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md §3): a hit in the first tick of a frame meets the health the 30 Hz step leaves, not one share of the frame's slice.
                var slice = body.MaxHealth * data.HealthRegenPercentPerSecond / Fixed.FromInt(SimulationRate.OriginalFramesPerSecond);
                world.Damage(Entity.Id, new DamageInfo(Entity.Id, slice, DamageTypes.Healing, DeathTypes.None, false));
            }
        }

        if (_workerWait > 0 && --_workerWait == 0)
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.WorkerName, CreationSkipReasons.SettingNotSimulated);
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("workerWait", _workerWait);
        writer.Int("origin", _origin);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _workerWait = reader.Int("workerWait");
        _origin = checked((int)reader.Int("origin"));
    }
}
