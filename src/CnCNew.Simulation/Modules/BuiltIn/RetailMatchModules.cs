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
        new ModuleBinding<RadarUpdateData>("RadarUpdate", ModuleOwnerKind.Object, (configuration, _) => RadarUpdateData.Parse(configuration), data => new RadarUpdate(data)),
        new ModuleBinding<RebuildHoleExposeDieData>("RebuildHoleExposeDie", ModuleOwnerKind.Object, (configuration, _) => RebuildHoleExposeDieData.Parse(configuration), data => new RebuildHoleExposeDie(data)),
        new ModuleBinding<RebuildHoleBehaviorData>("RebuildHoleBehavior", ModuleOwnerKind.Object, (configuration, _) => RebuildHoleBehaviorData.Parse(configuration), data => new RebuildHoleBehavior(data)),
    ];
}

// ============================================================================================================ HealContain

/// <summary><c>OpenContainModuleData</c> plus the two <c>HealContainModuleData</c> keys. <c>ContainMax</c> below zero means unlimited, as in <see cref="GarrisonContain"/>.</summary>
public sealed record HealContainData(OpenContainData Base, int ContainMax, long TimeForFullHealTicks) : ModuleData
{
    public static HealContainData Parse(ModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, []),
        checked((int)ModuleConfigReader.Int(configuration, "ContainMax", -1)),
        ModuleConfigReader.Ticks(configuration, "TimeForFullHeal", 0));
}

/// <summary>
/// <c>HealContain.cpp</c>: every tick each occupant is healed <c>MaxHealth / TimeForFullHeal</c>; once it has been inside for the whole
/// duration it is set to full health and leaves the building. The reference lets the occupant walk out through the building's own exit
/// interface; without AI it is placed at the container (<c>Remove(positionAtContainer)</c>).
/// </summary>
public sealed class HealContain(HealContainData data) : ContainModuleBase(data.Base)
{
    private readonly Dictionary<uint, long> _containedSinceTick = [];

    public override string ModuleName => "HealContain";
    public override int ContainMax => data.ContainMax;

    public override void OnSpawn(IWorldServices world) => NextWake = SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(Entity candidate, bool checkCapacity)
        => !Entity.EffectivelyDead && PassesKindOfAndRelationship(candidate) && (!checkCapacity || data.ContainMax < 0 || ContainCount < data.ContainMax);

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
            var done = data.TimeForFullHealTicks <= 0 || world.Tick - since >= data.TimeForFullHealTicks;
            var amount = done ? rider.Body.MaxHealth : rider.Body.MaxHealth / Fixed.FromInt(data.TimeForFullHealTicks);
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
    public static EjectPilotDieData Parse(ModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
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
    public static VeterancyGainCreateData Parse(ModuleConfiguration configuration)
    {
        var level = ModuleConfigReader.Find(configuration, "StartingLevel")?.Value.Value is EnumValue rank && NameTables.Veterancy.TryIndexOf(rank.Name, out var index) ? index : 0;
        return new(level, ModuleConfigReader.Text(configuration, "ScienceRequired"));
    }
}

/// <summary>
/// <c>VeterancyGainCreate::onCreate</c>: an owner that holds the required science (or none is required) gets a trainable object at
/// <c>StartingLevel</c>. The reference stores it as the object's <em>minimum</em> level, so later experience cannot lower it; the
/// experience system is not modeled, so the level is set once at creation.
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
        Entity.VeterancyLevel = Math.Max(Entity.VeterancyLevel, data.StartingLevel);
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

// ============================================================================================================ RadarUpdate

public sealed record RadarUpdateData(long ExtendTicks) : ModuleData
{
    public static RadarUpdateData Parse(ModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "RadarExtendTime", 0));
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

// ============================================================================================================ GLA rebuild hole

public sealed record RebuildHoleExposeDieData(DieMuxData Mux, string HoleName, Fixed HoleMaxHealth, bool TransferAttackers) : DieData(Mux)
{
    public static RebuildHoleExposeDieData Parse(ModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
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
    public static RebuildHoleBehaviorData Parse(ModuleConfiguration configuration) => new(
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
            var amount = body.MaxHealth * data.HealthRegenPercentPerSecond / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
            world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
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
