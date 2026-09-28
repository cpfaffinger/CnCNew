using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Timers;

public enum TimerActionKind : byte
{
    /// <summary>Only emits a <c>TimerFired</c> event.</summary>
    Notify = 1,
    /// <summary>Emits the event and damages the owner by <c>Amount</c>.</summary>
    Damage = 2,
    /// <summary>Emits the event and kills the owner (die modules run).</summary>
    Kill = 3,
    /// <summary>Emits the event and destroys the owner (removal).</summary>
    Destroy = 4,
}

/// <summary>A scheduled world timer. Timers fire in (<see cref="DueTick"/>, <see cref="Id"/>) order.</summary>
public sealed class SimTimer
{
    public TimerId Id { get; init; }
    public long DueTick { get; set; }
    public EntityId Owner { get; init; }
    public long RepeatTicks { get; init; }
    public TimerActionKind Action { get; init; }
    public Fixed Amount { get; init; }
    public int DamageType { get; init; }
    public int Tag { get; init; }
    internal int HeapIndex { get; set; } = -1;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.UInt("id", Id.Value);
        writer.Int("due", DueTick);
        writer.UInt("owner", Owner.Value);
        writer.Int("repeat", RepeatTicks);
        writer.Int("action", (int)Action);
        writer.Fixed("amount", Amount);
        writer.Int("damageType", DamageType);
        writer.Int("tag", Tag);
        writer.EndObject();
    }

    public static SimTimer Load(IStateReader reader)
    {
        var action = reader.Int("action");
        if (!Enum.IsDefined(typeof(TimerActionKind), (byte)action)) throw new InvalidDataException($"Unknown timer action {action}.");
        return new SimTimer
        {
            Id = new TimerId(reader.UInt("id")),
            DueTick = reader.Int("due"),
            Owner = new EntityId(checked((uint)reader.UInt("owner"))),
            RepeatTicks = reader.Int("repeat"),
            Action = (TimerActionKind)action,
            Amount = reader.Fixed("amount"),
            DamageType = checked((int)reader.Int("damageType")),
            Tag = checked((int)reader.Int("tag")),
        };
    }
}
