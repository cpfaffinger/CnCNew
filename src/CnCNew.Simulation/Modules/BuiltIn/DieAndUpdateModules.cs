using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The shared die filter (<c>DieMuxData</c>, DieModule.cpp): death type and veterancy must be enabled, no exempt status may be
/// set and all required statuses must be set. Defaults (all death types and veterancy levels, no statuses) equal the reference.
/// </summary>
public sealed record DieMuxData(uint DeathTypes, uint VeterancyLevels, ulong ExemptStatus, ulong RequiredStatus)
{
    public static DieMuxData Parse(RatedModuleConfiguration configuration) => new(
        Mask(ModuleConfigReader.Flags(configuration, "DeathTypes"), NameTables.DeathType, uint.MaxValue),
        Mask(ModuleConfigReader.Flags(configuration, "VeterancyLevels"), NameTables.Veterancy, uint.MaxValue),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "ExemptStatus"), NameTables.ObjectStatus),
        ObjectStatusBits.Mask(ModuleConfigReader.Flags(configuration, "RequiredStatus"), NameTables.ObjectStatus));

    private static uint Mask(FlagsValue? flags, NameTable table, uint fallback)
        => flags is null ? fallback : (uint)ObjectStatusBits.Mask(flags, table);

    public bool IsApplicable(Entity entity, in DamageInfo info)
    {
        if (info.DeathType < 0 || info.DeathType >= 32 || (DeathTypes & (1u << info.DeathType)) == 0) return false;
        if ((VeterancyLevels & (1u << entity.VeterancyLevel)) == 0) return false;
        if (ExemptStatus != 0 && (entity.Status & ExemptStatus) != 0) return false;
        if (RequiredStatus != 0 && (entity.Status & RequiredStatus) != RequiredStatus) return false;
        return true;
    }
}

public abstract record DieData(DieMuxData Mux) : ModuleData;

public abstract class DieModuleBase(DieMuxData mux) : SimModule, IDieModule
{
    protected DieMuxData Mux { get; } = mux;
    public abstract void OnDie(IWorldServices world, in DamageInfo info);
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

public sealed record PlainDieData(DieMuxData Mux) : DieData(Mux)
{
    public static PlainDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration));
}

/// <summary><c>DestroyDie</c>: removes the entity at the end of the tick when the die filter applies.</summary>
public sealed class DestroyDie(PlainDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "DestroyDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (Mux.IsApplicable(Entity, info)) world.RequestDestroy(Entity.Id);
    }
}

/// <summary><c>KeepObjectDie</c>: deliberately does nothing, so the corpse stays (rubble structures).</summary>
public sealed class KeepObjectDie(PlainDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "KeepObjectDie";
    public override void OnDie(IWorldServices world, in DamageInfo info) => _ = Mux.IsApplicable(Entity, info);
}

public sealed record CreateObjectDieData(DieMuxData Mux, string CreationList, bool TransferPreviousHealth) : DieData(Mux)
{
    public static CreateObjectDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "CreationList"), ModuleConfigReader.Bool(configuration, "TransferPreviousHealth", false));
}

/// <summary>
/// <c>CreateObjectDie</c>: executes its object creation list (<see cref="IWorldServices.CreateFromList"/>), transferring the previous
/// health to the first created object when configured. An absent list (shipped map overlays use this to disable the effect) does nothing.
/// </summary>
public sealed class CreateObjectDie(CreateObjectDieData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "CreateObjectDie";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info) || data.CreationList.Length == 0) return;
        world.CreateFromList(Entity, data.CreationList, info.Source, data.TransferPreviousHealth);
    }
}

public sealed record FxListDieData(DieMuxData Mux, string DeathFx, bool OrientToObject, bool StartsActive) : DieData(Mux)
{
    public static FxListDieData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Text(configuration, "DeathFX"), ModuleConfigReader.Bool(configuration, "OrientToObject", false),
        // FXListDieModuleData sets m_initiallyActive = TRUE: the reference documents that only one of 847 uses turns it off.
        ModuleConfigReader.Bool(configuration, "StartsActive", true));
}

/// <summary>
/// <c>FXListDie</c>: requests an effect list on death. It is an upgrade module whose activation belongs to the upgrade system
/// (phase 5): with <c>StartsActive = No</c> it stays dormant, and conflicting upgrades cannot exist yet.
/// </summary>
public sealed class FxListDie(FxListDieData data) : DieModuleBase(data.Mux)
{
    private bool _active;

    public override string ModuleName => "FXListDie";
    public override void OnSpawn(IWorldServices world) => _active = data.StartsActive;

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!_active || !Mux.IsApplicable(Entity, info) || data.DeathFx.Length == 0) return;
        world.Emit(SimEventKind.FxRequested, Entity.Id, info.Source, data.DeathFx, data.OrientToObject ? 1 : 0);
    }

    public override void Describe(IStateWriter writer) => writer.Bool("active", _active);
    public override void Load(IStateReader reader) => _active = reader.Bool("active");
}

public sealed record InstantDeathData(DieMuxData Mux, ImmutableArray<string> Fx, ImmutableArray<string> CreationLists, ImmutableArray<string> Weapons) : DieData(Mux)
{
    public static InstantDeathData Parse(RatedModuleConfiguration configuration) => new(DieMuxData.Parse(configuration),
        ModuleConfigReader.Names(configuration, "FX"), ModuleConfigReader.Names(configuration, "OCL"), ModuleConfigReader.Names(configuration, "Weapon"));
}

/// <summary>
/// <c>InstantDeathBehavior</c>: picks one entry of each non-empty list (effect, creation list, weapon; one random draw per list, in that order),
/// then removes the entity. Effects and weapons are requested as events; the creation list is executed.
/// </summary>
public sealed class InstantDeathBehavior(InstantDeathData data) : DieModuleBase(data.Mux)
{
    public override string ModuleName => "InstantDeathBehavior";

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!Mux.IsApplicable(Entity, info)) return;
        if (data.Fx.Length > 0) world.Emit(SimEventKind.FxRequested, Entity.Id, info.Source, data.Fx[world.LogicRandom.Range(0, data.Fx.Length - 1)], 0);
        if (data.CreationLists.Length > 0) world.CreateFromList(Entity, data.CreationLists[world.LogicRandom.Range(0, data.CreationLists.Length - 1)], info.Source, false);
        if (data.Weapons.Length > 0)
        {
            var weapon = data.Weapons[world.LogicRandom.Range(0, data.Weapons.Length - 1)];
            world.Emit(SimEventKind.WeaponFireRequested, Entity.Id, info.Source, weapon, 0);
            world.FireTempWeapon(Entity, weapon, Entity.X, Entity.Y, Entity.Z);
        }
        world.RequestDestroy(Entity.Id);
    }
}

public sealed record LifetimeData(long MinTicks, long MaxTicks) : ModuleData
{
    public static LifetimeData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "MinLifetime", 0), ModuleConfigReader.Ticks(configuration, "MaxLifetime", 0));
}

/// <summary>Shared behaviour of <c>LifetimeUpdate</c> and <c>DeletionUpdate</c>: draw a lifetime once at spawn, act when it expires.</summary>
public abstract class TimedExpiryModule(LifetimeData data) : SimModule, IUpdateModule
{
    private long _nextWake = -1;
    private long _dieTick;
    private long _minTicks = data.MinTicks;
    private long _maxTicks = data.MaxTicks;

    public long NextWake
    {
        get => _nextWake;
        set => _nextWake = value;
    }

    public long DieTick => _dieTick;

    /// <summary><c>LifetimeUpdate::setLifetimeRange</c> before the first draw: an object creation list overrides the object's own range (the reference redraws afterwards, the simulation draws once).</summary>
    internal void OverrideRange(long minTicks, long maxTicks)
    {
        _minTicks = minTicks;
        _maxTicks = maxTicks;
    }

    public override void OnSpawn(IWorldServices world)
    {
        // LifetimeUpdate.cpp, LifetimeUpdate::calcSleepDelay: GameLogicRandomValue(minFrames, maxFrames) inclusive, "if (delay < 1) delay = 1" – drawn in
        // frames of the original and then scaled, so the lifetime is the same game time at every rate; at least one frame.
        var delay = world.Time.AtLeastOneFrame(world.Time.DrawFrames(world.LogicRandom, _minTicks, _maxTicks));
        _dieTick = world.Tick + delay;
        _nextWake = Math.Min(world.Tick + delay, SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        Expire(world);
        return SimulationTiming.SleepForever;
    }

    protected abstract void Expire(IWorldServices world);

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("dieTick", _dieTick);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _dieTick = reader.Int("dieTick");
    }
}

/// <summary><c>LifetimeUpdate</c>: kills (not destroys) the entity when the lifetime is up, so die modules run.</summary>
public sealed class LifetimeUpdate(LifetimeData data) : TimedExpiryModule(data)
{
    public override string ModuleName => "LifetimeUpdate";
    protected override void Expire(IWorldServices world) => world.Kill(Entity.Id);
}

/// <summary><c>DeletionUpdate</c>: destroys (not kills) the entity when the lifetime is up, without running die modules.</summary>
public sealed class DeletionUpdate(LifetimeData data) : TimedExpiryModule(data)
{
    public override string ModuleName => "DeletionUpdate";
    protected override void Expire(IWorldServices world) => world.RequestDestroy(Entity.Id);
}
