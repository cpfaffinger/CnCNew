using CnCNew.Data.Definitions;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
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
