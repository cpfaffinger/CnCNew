using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// The containment subsystem: <c>GarrisonContain</c> (167 objects unlocked alone, the largest single blocker by
/// measured impact) and <c>TransportContain</c> (2 objects alone — small, but a real second implementor of
/// <see cref="ContainModuleBase"/>, which is why the shared surface was worth extracting now rather than left
/// duplicated). Deliberately not a full port of the reference's <c>OpenContain</c> base (firepoints/exit
/// paths/weapon-bonus passthrough/capture handling/collision-triggered entry — all need infrastructure phase 5 does
/// not have): capacity, <c>KindOf</c>/relationship gating, death ejection, and periodic healing/regen are modeled;
/// everything else is typed (Opaque where genuinely unmodeled) and documented at each schema entry.
/// </summary>
internal static class ContainModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<GarrisonContainData>("GarrisonContain", ModuleOwnerKind.Object, (configuration, _) => GarrisonContainData.Parse(configuration), data => new GarrisonContain(data)),
        new ModuleBinding<TransportContainData>("TransportContain", ModuleOwnerKind.Object, (configuration, _) => TransportContainData.Parse(configuration), data => new TransportContain(data)),
        new ModuleBinding<HelixContainData>("HelixContain", ModuleOwnerKind.Object, (configuration, _) => HelixContainData.Parse(configuration), data => new HelixContain(data)),
        new ModuleBinding<TunnelContainData>("TunnelContain", ModuleOwnerKind.Object, (configuration, _) => TunnelContainData.Parse(configuration), data => new TunnelContain(data)),
    ];
}

/// <summary>
/// <c>OpenContainModuleData</c>'s shared fields (<c>OpenContain.cpp</c>), reused by <see cref="GarrisonContainData"/>
/// and <see cref="TransportContainData"/>. <see cref="AllowInsideKindOf"/> has no engine-wide default (unlike
/// <c>OpenContain</c>'s own "everything allowed"): both current subtypes narrow it to <c>INFANTRY</c> in their own
/// constructors, so callers pass that default in explicitly.
/// </summary>
public sealed record OpenContainData(
    DieMuxData DieMux, Fixed DamagePercentToUnits,
    ImmutableArray<string> AllowInsideKindOf, ImmutableArray<string> ForbidInsideKindOf,
    bool AllowAlliesInside, bool AllowEnemiesInside, bool AllowNeutralInside)
{
    public static OpenContainData Parse(ModuleConfiguration configuration, ImmutableArray<string> defaultAllowInsideKindOf) => new(
        DieMuxData.Parse(configuration),
        ModuleConfigReader.Real(configuration, "DamagePercentToUnits", Fixed.Zero),
        ModuleConfigReader.Flags(configuration, "AllowInsideKindOf")?.Names ?? defaultAllowInsideKindOf,
        ModuleConfigReader.Flags(configuration, "ForbidInsideKindOf")?.Names ?? [],
        ModuleConfigReader.Bool(configuration, "AllowAlliesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowEnemiesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowNeutralInside", true));
}

/// <summary>
/// Shared <c>OpenContain</c> behavior: <c>KindOf</c>/relationship gating (<c>Relationship</c> of the container's owner towards the candidate's), entering/leaving (position snap
/// to the container, clearing a pending move order), and death handling. <c>OpenContain::onDie</c> applies
/// <c>DamagePercentToUnits</c> to each occupant's current health, then calls <c>removeAllContained</c> — never
/// <c>killAllContained</c> — for every container whose <c>killRidersWhoAreNotFreeToExit</c> ends up a no-op in this
/// engine: <c>GarrisonContain</c>'s is the base class's own no-op, and <c>TransportContain</c>'s real
/// terrain/pathfinding/airborne-locomotor checks in <c>isSpecificRiderFreeToExit</c> have no equivalent here (no
/// terrain/pathfinding at all), so riders are always "free to exit". Capacity and the periodic <see cref="Update"/>
/// behavior are subtype-specific.
/// </summary>
public abstract class ContainModuleBase(OpenContainData data) : SimModule, IUpdateModule, IDieModule, IContainerModule
{
    protected readonly List<EntityId> Riders = [];
    private long _nextWake = -1;

    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public abstract int ContainMax { get; }
    public int ContainCount => Riders.Count;
    public IReadOnlyList<EntityId> Contained => Riders;

    public abstract bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity);

    protected bool PassesKindOfAndRelationship(IWorldServices world, Entity candidate)
    {
        if (!data.AllowInsideKindOf.IsEmpty && !data.AllowInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        if (data.ForbidInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        return world.RelationshipOf(Entity.Owner, candidate.Owner) switch
        {
            Relationship.Allies => data.AllowAlliesInside,
            Relationship.Enemies => data.AllowEnemiesInside,
            _ => data.AllowNeutralInside,
        };
    }

    public virtual void Add(IWorldServices world, Entity candidate)
    {
        _ = world;
        Riders.Add(candidate.Id);
        candidate.ContainedBy = Entity.Id;
        candidate.HasMoveTarget = false;
        candidate.X = Entity.X;
        candidate.Y = Entity.Y;
    }

    public virtual void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        _ = world;
        Riders.Remove(candidate.Id);
        candidate.ContainedBy = EntityId.Invalid;
        if (positionAtContainer) { candidate.X = Entity.X; candidate.Y = Entity.Y; }
    }

    public virtual void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.DieMux.IsApplicable(Entity, info)) return;
        if (data.DamagePercentToUnits > Fixed.Zero)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is null) continue;
                world.Damage(rider.Id, new DamageInfo(info.Source, rider.Body.Health * data.DamagePercentToUnits, DamageTypes.Unresistable, DeathTypes.Normal, false));
            }
        EjectAll(world);
    }

    protected void EjectAll(IWorldServices world)
    {
        foreach (var riderId in Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null) continue;
            Remove(world, rider, true);
            world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        }
    }

    /// <summary>Removes anything that died while contained (both reference <c>update()</c>s do this first).</summary>
    protected void PruneDead(IWorldServices world)
    {
        foreach (var riderId in Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null || rider.EffectivelyDead) Riders.Remove(riderId);
        }
    }

    public abstract long Update(IWorldServices world);

    protected void DescribeContained(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.BeginArray("contained");
        foreach (var riderId in Riders)
        {
            writer.BeginObject(null);
            writer.UInt("id", riderId.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    protected void LoadContained(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        Riders.Clear();
        foreach (var entry in reader.Array("contained")) Riders.Add(new EntityId(checked((uint)entry.UInt("id"))));
    }
}

// ==================================================================================================== GarrisonContain

/// <summary>
/// <c>OpenContainModuleData</c> plus <c>GarrisonContainModuleData</c>'s own two mechanically relevant fields.
/// <see cref="Base"/>'s <c>AllowInsideKindOf</c> defaults to <c>INFANTRY</c> — <c>GarrisonContainModuleData</c>'s own
/// constructor narrows the generic <c>OpenContain</c> default (which allows everything) to infantry only.
/// </summary>
public sealed record GarrisonContainData(OpenContainData Base, int ContainMax, bool HealObjects, long TimeForFullHealTicks) : ModuleData
{
    public static GarrisonContainData Parse(ModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, ["INFANTRY"]),
        checked((int)ModuleConfigReader.Int(configuration, "ContainMax", -1)),
        ModuleConfigReader.Bool(configuration, "HealObjects", false),
        ModuleConfigReader.Ticks(configuration, "TimeForFullHeal", 0));
}

/// <summary>
/// <c>GarrisonContain.cpp</c>. <see cref="IsValidContainerFor"/> extends the shared gating with the Garrison-specific
/// rule: not valid while <c>REALLYDAMAGED</c> (exactly that state — <c>RUBBLE</c> buildings are already dead) unless
/// <c>KindOf = GARRISONABLE_UNTIL_DESTROYED</c>. The same rule ejects everyone the moment the building's damage state
/// transitions into <c>REALLYDAMAGED</c> (<see cref="IDamageModule.OnBodyDamageStateChange"/>). Healing
/// (<see cref="GarrisonContainData.HealObjects"/>) follows <c>healSingleObject</c>: a sliver of
/// <c>MaxHealth / TimeForFullHeal</c> per tick until the occupant has been inside for the full duration, then a hard
/// snap to full. Deliberately not modeled: <c>InitialRoster</c> (needs synchronous nested spawning, a bigger pipeline
/// change than this first cut justifies), firing from inside, garrison points/positions (art-derived, not INI data),
/// the apparent-controlling-player/team-swap system (no team system), capture handling (no capture system).
/// </summary>
public sealed class GarrisonContain(GarrisonContainData data) : ContainModuleBase(data.Base), IDamageModule
{
    private readonly Dictionary<uint, long> _containedSinceTick = [];

    public override string ModuleName => "GarrisonContain";
    public override int ContainMax => data.ContainMax;

    public override void OnSpawn(IWorldServices world)
        => NextWake = data.HealObjects ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead || !PassesKindOfAndRelationship(world, candidate)) return false;
        if (Entity.Body is ActiveBody body && body.DamageState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED"))
            return false;
        return !checkCapacity || data.ContainMax < 0 || ContainCount < data.ContainMax;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        _containedSinceTick[candidate.Id.Value] = world.Tick;
        if (data.HealObjects) world.WakeAt(Entity, this, world.Tick);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        base.Remove(world, candidate, positionAtContainer);
        _containedSinceTick.Remove(candidate.Id.Value);
    }

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        _ = info;
        _ = oldState;
        // Edge-triggered (the dispatcher only calls this when the state actually changed): entering REALLYDAMAGED
        // ejects everyone, unless the building is meant to stay garrisonable until destroyed.
        if (newState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED") && ContainCount > 0)
            EjectAll(world);
    }

    public override long Update(IWorldServices world)
    {
        PruneDead(world);
        if (data.HealObjects)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is not null) HealOne(world, rider);
            }
        return data.HealObjects ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    /// <summary><c>GarrisonContain::healSingleObject</c>: a sliver of <c>MaxHealth / TimeForFullHeal</c> per tick, or a hard snap to full once the occupant has been inside long enough.</summary>
    private void HealOne(IWorldServices world, Entity rider)
    {
        var since = _containedSinceTick.GetValueOrDefault(rider.Id.Value, world.Tick);
        var full = data.TimeForFullHealTicks > 0 && world.Tick - since >= data.TimeForFullHealTicks;
        var amount = full || data.TimeForFullHealTicks <= 0 ? rider.Body!.MaxHealth : rider.Body!.MaxHealth / Fixed.FromInt(data.TimeForFullHealTicks);
        world.Damage(rider.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
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

// =================================================================================================== TransportContain

/// <summary>
/// <c>OpenContainModuleData</c> plus <c>TransportContainModuleData</c>'s two mechanically relevant fields: the INI key
/// is literally <c>Slots</c> (not <c>ContainMax</c> — the inherited <c>ContainMax</c> field is parseable but unused,
/// since <c>TransportContain::getContainMax</c> only ever reads <c>m_slotCapacity</c>), and <c>HealthRegen%PerSec</c>
/// (a raw percent-per-second real, not <c>FieldType.Percent</c> — the reference divides by 100 itself in the formula).
/// </summary>
public sealed record TransportContainData(OpenContainData Base, int Slots, Fixed HealthRegenPercentPerSecond) : ModuleData
{
    public static TransportContainData Parse(ModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, ["INFANTRY"]),
        checked((int)ModuleConfigReader.Int(configuration, "Slots", 0)),
        ModuleConfigReader.Real(configuration, "HealthRegen%PerSec", Fixed.Zero));
}

/// <summary>
/// <c>TransportContain.cpp</c>. Capacity is weighted by <see cref="Templates.ObjectTemplate.TransportSlotCount"/>
/// (<c>rider->getTransportSlotCount()</c>), not a simple occupant count: a candidate with 0 slots is not transportable
/// at all, and the sum of contained occupants' slot costs plus the candidate's own must not exceed
/// <see cref="TransportContainData.Slots"/>. <c>HealthRegen%PerSec</c> heals every occupant continuously (unlike
/// <c>GarrisonContain</c>'s ramp-to-full-over-a-fixed-duration): <c>MaxHealth * percent / 100</c> per second, applied
/// as a per-tick sliver. Deliberately not modeled: <c>InitialPayload</c> (same reason as <c>GarrisonContain</c>'s
/// <c>InitialRoster</c>), firing from inside, exit paths/bones/pitch/delay/scatter (all presentation or need real
/// movement/positioning), capture handling (no capture system), <c>ArmedRidersUpgradeMyWeaponSet</c> (no WeaponBonus
/// system at all, project-wide).
/// </summary>
public class TransportContain(TransportContainData data) : ContainModuleBase(data.Base)
{
    /// <summary>Sum of <see cref="Templates.ObjectTemplate.TransportSlotCount"/> of everyone currently aboard, maintained incrementally (<see cref="Add"/>/<see cref="Remove"/>) so capacity checks never need to resolve <see cref="ContainModuleBase.Riders"/> back to entities.</summary>
    private int _usedSlots;

    public override string ModuleName => "TransportContain";
    public override int ContainMax => data.Slots;

    public override void OnSpawn(IWorldServices world)
        => NextWake = data.HealthRegenPercentPerSecond > Fixed.Zero ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead || candidate.Template.TransportSlotCount <= 0 || !PassesKindOfAndRelationship(world, candidate)) return false;
        return !checkCapacity || _usedSlots + candidate.Template.TransportSlotCount <= data.Slots;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        _usedSlots += candidate.Template.TransportSlotCount;
    }

    /// <summary>
    /// <c>DestroyRidersWhoAreNotFreeToExit</c> (<c>TransportContain::killRidersWhoAreNotFreeToExit</c>), as far as this simulation has a gate: a payload plane
    /// (<see cref="DeliverPayloadAIUpdate"/>) whose doors are shut when it dies takes its riders (and what they carry) with it — they are destroyed, not killed.
    /// </summary>
    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (data.Base.DieMux.IsApplicable(Entity, info) && Entity.ModuleArray.OfType<DeliverPayloadAIUpdate>().FirstOrDefault() is { FreeToExit: false })
            foreach (var riderId in Riders.ToArray())
                if (world.FindEntity(riderId) is { } rider)
                {
                    Remove(world, rider, false);
                    DestroyWithContents(world, rider);
                }
        base.OnDie(world, info);
    }

    private static void DestroyWithContents(IWorldServices world, Entity entity)
    {
        if (entity.Container is { } inside)
            foreach (var id in inside.Contained.ToArray())
                if (world.FindEntity(id) is { } passenger) DestroyWithContents(world, passenger);
        world.RequestDestroy(entity.Id);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        base.Remove(world, candidate, positionAtContainer);
        _usedSlots -= candidate.Template.TransportSlotCount;
    }

    public override long Update(IWorldServices world)
    {
        PruneDead(world);
        if (data.HealthRegenPercentPerSecond > Fixed.Zero)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is null || rider.Body.Health >= rider.Body.MaxHealth) continue;
                var perSecond = rider.Body.MaxHealth * data.HealthRegenPercentPerSecond / Fixed.FromInt(100);
                var perTick = perSecond / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
                world.Damage(rider.Id, new DamageInfo(Entity.Id, perTick, DamageTypes.Healing, DeathTypes.None, false));
            }
        return data.HealthRegenPercentPerSecond > Fixed.Zero ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        DescribeContained(writer);
        writer.Int("usedSlots", _usedSlots);
    }

    public override void Load(IStateReader reader)
    {
        LoadContained(reader);
        _usedSlots = checked((int)reader.Int("usedSlots"));
    }
}
