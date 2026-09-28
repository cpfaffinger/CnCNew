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
/// The containment subsystem's first module: <c>GarrisonContain</c> (167 objects unlocked alone, the largest single
/// blocker by measured impact — see <c>docs/HANDOFF-PHASE5.md</c>). Deliberately not a full port of the reference's
/// <c>OpenContain</c> base (firepoints/exit paths/weapon-bonus passthrough/capture handling/collision-triggered entry —
/// all need infrastructure phase 5 does not have): capacity, <c>KindOf</c>/relationship gating, death and damage-state
/// ejection, and periodic healing are modeled; everything else is typed (Opaque where genuinely unmodeled) and
/// documented at the schema entry. <c>TransportContain</c> (2 objects alone) can reuse <see cref="IContainerModule"/>
/// once needed; the shared surface was kept intentionally small until a second implementor exists.
/// </summary>
internal static class ContainModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<GarrisonContainData>("GarrisonContain", ModuleOwnerKind.Object, (configuration, _) => GarrisonContainData.Parse(configuration), data => new GarrisonContain(data)),
    ];
}

/// <summary>
/// <c>OpenContainModuleData</c> (shared contain fields) plus <c>GarrisonContainModuleData</c>'s own two mechanically
/// relevant fields. <see cref="AllowInsideKindOf"/> defaults to <c>INFANTRY</c> when unset — <c>GarrisonContainModuleData</c>'s
/// own constructor narrows the generic <c>OpenContain</c> default (which allows everything) to infantry only.
/// </summary>
public sealed record GarrisonContainData(
    DieMuxData DieMux, int ContainMax, Fixed DamagePercentToUnits,
    ImmutableArray<string> AllowInsideKindOf, ImmutableArray<string> ForbidInsideKindOf,
    bool AllowAlliesInside, bool AllowEnemiesInside, bool AllowNeutralInside,
    bool HealObjects, long TimeForFullHealTicks) : ModuleData
{
    public static GarrisonContainData Parse(ModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "ContainMax", -1)),
        ModuleConfigReader.Real(configuration, "DamagePercentToUnits", Fixed.Zero),
        ModuleConfigReader.Flags(configuration, "AllowInsideKindOf")?.Names ?? ["INFANTRY"],
        ModuleConfigReader.Flags(configuration, "ForbidInsideKindOf")?.Names ?? [],
        ModuleConfigReader.Bool(configuration, "AllowAlliesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowEnemiesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowNeutralInside", true),
        ModuleConfigReader.Bool(configuration, "HealObjects", false),
        ModuleConfigReader.Ticks(configuration, "TimeForFullHeal", 0));
}

/// <summary>
/// <c>GarrisonContain.cpp</c>. <see cref="IsValidContainerFor"/> extends <c>OpenContain</c>'s KindOf/relationship check
/// with the Garrison-specific rule: not valid while <c>REALLYDAMAGED</c> (exactly that state — <c>RUBBLE</c> buildings
/// are already dead) unless <c>KindOf = GARRISONABLE_UNTIL_DESTROYED</c>. The same rule ejects everyone (not kills them —
/// <c>killRidersWhoAreNotFreeToExit</c> is a <c>GarrisonContain</c>/base-class no-op; only <c>TransportContain</c>
/// overrides it) the moment the building's damage state transitions into <c>REALLYDAMAGED</c>
/// (<see cref="IDamageModule.OnBodyDamageStateChange"/>). Dying applies <see cref="GarrisonContainData.DamagePercentToUnits"/>
/// of each occupant's current health, then ejects survivors — mirrors <c>OpenContain::onDie</c> exactly (it calls
/// <c>removeAllContained</c>, never <c>killAllContained</c>, for a container whose <c>killRidersWhoAreNotFreeToExit</c>
/// is a no-op). Healing (<see cref="GarrisonContainData.HealObjects"/>) follows <c>healSingleObject</c>: a sliver of
/// <c>MaxHealth / TimeForFullHeal</c> per tick until the occupant has been inside for the full duration, then a hard
/// snap to full. Deliberately not modeled: <c>InitialRoster</c> (needs synchronous nested spawning, a bigger pipeline
/// change than this first cut justifies), firing from inside, garrison points/positions (art-derived, not INI data),
/// the apparent-controlling-player/team-swap system (no team system), capture handling (no capture system).
/// </summary>
public sealed class GarrisonContain(GarrisonContainData data) : SimModule, IUpdateModule, IDieModule, IDamageModule, IContainerModule
{
    private readonly List<EntityId> _contained = [];
    private readonly Dictionary<uint, long> _containedSinceTick = [];
    private long _nextWake = -1;

    public override string ModuleName => "GarrisonContain";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public int ContainMax => data.ContainMax;
    public int ContainCount => _contained.Count;
    public IReadOnlyList<EntityId> Contained => _contained;

    public override void OnSpawn(IWorldServices world)
        => _nextWake = data.HealObjects ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public bool IsValidContainerFor(Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead) return false;
        if (!data.AllowInsideKindOf.IsEmpty && !data.AllowInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        if (data.ForbidInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        // No team/diplomacy system yet: same owner = ally, no neutral concept (same approximation as StealthDetectorUpdate).
        var isAlly = candidate.Owner == Entity.Owner;
        if (isAlly ? !data.AllowAlliesInside : !data.AllowEnemiesInside) return false;
        if (Entity.Body is ActiveBody body && body.DamageState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED"))
            return false;
        if (checkCapacity && data.ContainMax >= 0 && _contained.Count >= data.ContainMax) return false;
        return true;
    }

    public void Add(IWorldServices world, Entity candidate)
    {
        _contained.Add(candidate.Id);
        _containedSinceTick[candidate.Id.Value] = world.Tick;
        candidate.ContainedBy = Entity.Id;
        candidate.HasMoveTarget = false;
        candidate.X = Entity.X;
        candidate.Y = Entity.Y;
        if (data.HealObjects) world.WakeAt(Entity, this, world.Tick);
    }

    public void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        _contained.Remove(candidate.Id);
        _containedSinceTick.Remove(candidate.Id.Value);
        candidate.ContainedBy = EntityId.Invalid;
        if (positionAtContainer) { candidate.X = Entity.X; candidate.Y = Entity.Y; }
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.DieMux.IsApplicable(Entity, info)) return;
        if (data.DamagePercentToUnits > Fixed.Zero)
            foreach (var riderId in _contained.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is null) continue;
                world.Damage(rider.Id, new DamageInfo(info.Source, rider.Body.Health * data.DamagePercentToUnits, DamageTypes.Unresistable, DeathTypes.Normal, false));
            }
        EjectAll(world);
    }

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        _ = info;
        _ = oldState;
        // Edge-triggered (the dispatcher only calls this when the state actually changed): entering REALLYDAMAGED
        // ejects everyone, unless the building is meant to stay garrisonable until destroyed.
        if (newState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED") && _contained.Count > 0)
            EjectAll(world);
    }

    private void EjectAll(IWorldServices world)
    {
        foreach (var riderId in _contained.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null) continue;
            Remove(world, rider, true);
            world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        }
    }

    public long Update(IWorldServices world)
    {
        if (_contained.Count > 0)
            foreach (var riderId in _contained.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider is null || rider.EffectivelyDead)
                {
                    _contained.Remove(riderId);
                    _containedSinceTick.Remove(riderId.Value);
                    continue;
                }
                if (data.HealObjects) HealOne(world, rider);
            }
        return data.HealObjects ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    /// <summary><c>GarrisonContain::healSingleObject</c>: a sliver of <c>MaxHealth / TimeForFullHeal</c> per tick, or a hard snap to full once the occupant has been inside long enough.</summary>
    private void HealOne(IWorldServices world, Entity rider)
    {
        if (rider.Body is null) return;
        var since = _containedSinceTick.GetValueOrDefault(rider.Id.Value, world.Tick);
        var full = data.TimeForFullHealTicks > 0 && world.Tick - since >= data.TimeForFullHealTicks;
        var amount = full || data.TimeForFullHealTicks <= 0 ? rider.Body.MaxHealth : rider.Body.MaxHealth / Fixed.FromInt(data.TimeForFullHealTicks);
        world.Damage(rider.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.BeginArray("contained");
        foreach (var riderId in _contained)
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
        _nextWake = reader.Int("nextWake");
        _contained.Clear();
        _containedSinceTick.Clear();
        foreach (var entry in reader.Array("contained"))
        {
            var id = new EntityId(checked((uint)entry.UInt("id")));
            _contained.Add(id);
            _containedSinceTick[id.Value] = entry.Int("since");
        }
    }
}
