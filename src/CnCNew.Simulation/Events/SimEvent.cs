using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Events;

public enum SimEventKind : byte
{
    EntitySpawned = 1,
    EntityDamaged = 2,
    EntityDied = 3,
    EntityDestroyed = 4,
    TimerFired = 5,
    /// <summary>A module asked for a visual/audio effect list; executed by presentation, never by the simulation.</summary>
    FxRequested = 6,
    /// <summary>A module asked for an object creation list; executed by the rules layer (phase 5).</summary>
    CreationListRequested = 7,
    /// <summary>A module asked for a weapon to be fired (phase 5).</summary>
    WeaponFireRequested = 8,
    CommandRejected = 9,
}

/// <summary>
/// An immutable world event. Field meaning per kind: <c>Entity</c> is the subject, <c>Other</c> the source/target,
/// <c>Text</c> a template, effect or reason name, <c>Value</c> an amount (raw fixed-point), timer tag or reject code.
/// </summary>
public readonly record struct SimEvent(EventId Id, long Tick, SimEventKind Kind, EntityId Entity, EntityId Other, PlayerId Player, string Text, long Value)
{
    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.UInt("id", Id.Value);
        writer.Int("tick", Tick);
        writer.Int("kind", (int)Kind);
        writer.UInt("entity", Entity.Value);
        writer.UInt("other", Other.Value);
        writer.Int("player", Player.Value);
        writer.Text("text", Text);
        writer.Int("value", Value);
        writer.EndObject();
    }

    public static SimEvent Load(IStateReader reader)
    {
        var kind = reader.Int("kind");
        if (!Enum.IsDefined(typeof(SimEventKind), (byte)kind)) throw new InvalidDataException($"Unknown event kind {kind}.");
        return new SimEvent(new EventId(reader.UInt("id")), reader.Int("tick"), (SimEventKind)kind,
            new EntityId(checked((uint)reader.UInt("entity"))), new EntityId(checked((uint)reader.UInt("other"))),
            new PlayerId(checked((int)reader.Int("player"))), reader.Text("text"), reader.Int("value"));
    }
}
