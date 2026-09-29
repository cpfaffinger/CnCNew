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

    // ------------------------------------------------------------------------------------------------- phase 5

    /// <summary>An upgrade module completed (<c>Value</c>: the <see cref="Templates.UpgradeInfo.Index"/>).</summary>
    UpgradeCompleted = 10,
    /// <summary><c>FireWeaponCommand</c> resolved (<c>Other</c>: the target, <c>Value</c>: the adjusted damage dealt).</summary>
    WeaponFired = 11,
    /// <summary>A move order reached its target (<c>AIUpdateInterface</c>).</summary>
    MoveArrived = 12,
    /// <summary><c>ProductionUpdate</c> completed a queue entry and requested a spawn (<c>Value</c>: the template index; the resulting spawn raises its own <see cref="EntitySpawned"/>).</summary>
    ProductionCompleted = 13,
    /// <summary>The last player without any remaining objects was defeated (<c>Player</c>: the defeated player).</summary>
    PlayerDefeated = 14,
    /// <summary><c>ActiveBody::attemptDamage</c>'s fear-sound roll succeeded when crossing the 25% health threshold; presentation-only (the roll itself, win or lose, is already consumed from <c>LogicRandom</c>).</summary>
    VoiceFearRequested = 15,
    /// <summary><c>PurchaseScienceCommand</c> completed (<c>Value</c>: the <see cref="Templates.ScienceInfo.Index"/>).</summary>
    ScienceCompleted = 16,
    /// <summary><c>ContainModuleInterface::addToContain</c> (<c>Entity</c>: the entity that entered, <c>Other</c>: the container).</summary>
    EntityContained = 17,
    /// <summary><c>ContainModuleInterface::removeFromContain</c> (<c>Entity</c>: the entity that left, <c>Other</c>: the container it left).</summary>
    EntityUncontained = 18,
    /// <summary><c>CreateCrateDie</c> chose a crate object (<c>Entity</c>: the dead object, <c>Text</c>: the crate template, <c>Value</c>: the owning player id, 0 = neutral). Raised whether or not the crate template is instantiable yet.</summary>
    CrateRequested = 19,
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
