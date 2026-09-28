using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>Scheduler node of one update module; the heap orders nodes by (wake tick, entity id, module index).</summary>
internal sealed class UpdateNode(Entity entity, SimModule module, IUpdateModule update)
{
    public Entity Entity { get; } = entity;
    public SimModule Module { get; } = module;
    public IUpdateModule Update { get; } = update;
    public int HeapIndex { get; set; } = -1;
}

/// <summary>
/// A live object. All mutable state is fixed-point or integer. Modules mutate the entity only while the world dispatches a
/// call to them; the world marks the entity dirty at every dispatch, which drives the incremental state hash.
/// </summary>
public sealed class Entity
{
    internal Entity(EntityId id, ObjectTemplate template, PlayerId owner, long spawnTick)
    {
        Id = id;
        Template = template;
        Owner = owner;
        SpawnTick = spawnTick;
    }

    public EntityId Id { get; }
    public ObjectTemplate Template { get; }
    public PlayerId Owner { get; internal set; }
    public Fixed X { get; internal set; }
    public Fixed Y { get; internal set; }
    public Fixed Z { get; internal set; }

    /// <summary>Heading in radians.</summary>
    public Fixed Angle { get; internal set; }

    /// <summary>Bits of the object status table (<see cref="ObjectStatusBits"/>).</summary>
    public ulong Status { get; internal set; }

    /// <summary>0 regular, 1 veteran, 2 elite, 3 heroic.</summary>
    public int VeterancyLevel { get; internal set; }

    /// <summary><c>EFFECTIVELY_DEAD</c>: set by bodies when health reaches zero (always for inactive bodies).</summary>
    public bool EffectivelyDead { get; set; }

    public bool DestroyRequested { get; internal set; }
    public long SpawnTick { get; }

    public IReadOnlyList<SimModule> Modules => ModuleArray;
    internal SimModule[] ModuleArray { get; set; } = [];
    public IBodyModule? Body { get; internal set; }
    internal IDieModule[] Dies { get; set; } = [];
    internal IEventReceiver[] Receivers { get; set; } = [];
    internal UpdateNode[] Updates { get; set; } = [];
    internal List<Timers.SimTimer>? Timers { get; set; }
    internal bool Registered { get; set; }
    internal bool Dirty { get; set; }
    internal ulong CachedHash { get; set; }

    public bool HasStatus(int bit) => (Status & (1UL << bit)) != 0;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.UInt("id", Id.Value);
        writer.Int("template", Template.Index);
        writer.Text("templateName", Template.Name);
        writer.Int("owner", Owner.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        writer.Fixed("z", Z);
        writer.Fixed("angle", Angle);
        writer.UInt("status", Status);
        writer.Int("veterancy", VeterancyLevel);
        writer.Bool("dead", EffectivelyDead);
        writer.Bool("destroyRequested", DestroyRequested);
        writer.Int("spawnTick", SpawnTick);
        writer.BeginArray("modules");
        foreach (var module in ModuleArray)
        {
            writer.BeginObject(null);
            writer.Int("index", module.Index);
            writer.Text("name", module.ModuleName);
            writer.Text("tag", module.Tag);
            writer.BeginObject("state");
            module.Describe(writer);
            writer.EndObject();
            writer.EndObject();
        }
        writer.EndArray();
        writer.EndObject();
    }
}
