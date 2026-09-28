using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Ids;

/// <summary>Identifies a world instance. Assigned by whoever creates the world (for example the match setup).</summary>
public readonly record struct WorldId(uint Value);

/// <summary>Entity ids start at 1; 0 is the invalid id (<c>INVALID_ID</c>, <c>GameLogic::allocateObjectID</c>).</summary>
public readonly record struct EntityId(uint Value)
{
    public static EntityId Invalid => default;
    public bool IsValid => Value != 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Player 0 is the system authority (scripts, tools, tests); real players start at 1.</summary>
public readonly record struct PlayerId(int Value)
{
    public static PlayerId System => new(0);
    public bool IsSystem => Value == 0;
    public override string ToString() => Value.ToString();
}

/// <summary>Monotone id of an accepted command.</summary>
public readonly record struct CommandId(ulong Value);

public readonly record struct EventId(ulong Value);
public readonly record struct TimerId(ulong Value);

/// <summary>
/// The monotone counters of a world. Every counter starts at 1 and only ever increases, so ids are unique for the whole
/// life of a world and identical in every run that performs the same operations in the same order.
/// </summary>
public sealed class IdCounters
{
    public uint NextEntity { get; private set; } = 1;
    public ulong NextEvent { get; private set; } = 1;
    public ulong NextTimer { get; private set; } = 1;
    public ulong NextCommand { get; private set; } = 1;

    public EntityId AllocateEntity()
    {
        if (NextEntity == uint.MaxValue) throw new InvalidOperationException("Entity id space exhausted.");
        return new EntityId(NextEntity++);
    }

    public EventId AllocateEvent() => new(NextEvent++);
    public TimerId AllocateTimer() => new(NextTimer++);
    public CommandId AllocateCommand() => new(NextCommand++);

    public void Describe(IStateWriter writer)
    {
        writer.UInt("nextEntity", NextEntity);
        writer.UInt("nextEvent", NextEvent);
        writer.UInt("nextTimer", NextTimer);
        writer.UInt("nextCommand", NextCommand);
    }

    public void Load(IStateReader reader)
    {
        NextEntity = checked((uint)reader.UInt("nextEntity"));
        NextEvent = reader.UInt("nextEvent");
        NextTimer = reader.UInt("nextTimer");
        NextCommand = reader.UInt("nextCommand");
        if (NextEntity == 0 || NextEvent == 0 || NextTimer == 0 || NextCommand == 0)
            throw new InvalidDataException("Id counters must start at 1.");
    }
}
