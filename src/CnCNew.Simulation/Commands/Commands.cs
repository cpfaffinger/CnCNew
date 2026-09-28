using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Timers;

namespace CnCNew.Simulation.Commands;

/// <summary>
/// Versioned command type table. A command type code is part of the replay format; codes are never reused or changed within
/// a table version. Adding a type increases <see cref="Version"/>.
/// </summary>
public static class CommandTypes
{
    public const int Version = 1;
    public const ushort Spawn = 1;
    public const ushort Destroy = 2;
    public const ushort Damage = 3;
    public const ushort Kill = 4;
    public const ushort SetTimer = 5;
    public const ushort CancelTimer = 6;

    public static bool IsKnown(ushort code) => code is >= Spawn and <= CancelTimer;
}

/// <summary>
/// A typed, tick-addressed input command. <see cref="Sequence"/> is per player, strictly increasing in submission order.
/// Commands with the same tick execute ordered by (<see cref="Player"/>, <see cref="Sequence"/>).
/// </summary>
public abstract record SimCommand(PlayerId Player, ulong Sequence, long Tick)
{
    public abstract ushort TypeCode { get; }

    /// <summary>Only the system authority may issue this command type.</summary>
    public virtual bool RequiresSystem => false;

    protected abstract void DescribeBody(IStateWriter writer);

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject(null);
        writer.Int("type", TypeCode);
        writer.Int("player", Player.Value);
        writer.UInt("sequence", Sequence);
        writer.Int("tick", Tick);
        DescribeBody(writer);
        writer.EndObject();
    }

    public static SimCommand Load(IStateReader reader)
    {
        var code = reader.Int("type");
        var player = new PlayerId(checked((int)reader.Int("player")));
        var sequence = reader.UInt("sequence");
        var tick = reader.Int("tick");
        return code switch
        {
            CommandTypes.Spawn => new SpawnCommand(player, sequence, tick, reader.Text("template"), new PlayerId(checked((int)reader.Int("owner"))),
                reader.Fixed("x"), reader.Fixed("y"), reader.Fixed("z"), reader.Fixed("angle")),
            CommandTypes.Destroy => new DestroyCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.Damage => new DamageCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target"))), reader.Fixed("amount"),
                checked((int)reader.Int("damageType")), checked((int)reader.Int("deathType"))),
            CommandTypes.Kill => new KillCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.SetTimer => new SetTimerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target"))), reader.Int("delay"),
                reader.Int("repeat"), (TimerActionKind)reader.Int("action"), reader.Fixed("amount"), checked((int)reader.Int("damageType")), checked((int)reader.Int("tag"))),
            CommandTypes.CancelTimer => new CancelTimerCommand(player, sequence, tick, new TimerId(reader.UInt("timer"))),
            _ => throw new InvalidDataException($"Unknown command type {code}."),
        };
    }
}

/// <summary>Creates an entity from a template. Owner 0 defaults to the issuing player; only the system may spawn for others.</summary>
public sealed record SpawnCommand(PlayerId Player, ulong Sequence, long Tick, string Template, PlayerId Owner, Fixed X, Fixed Y, Fixed Z, Fixed Angle)
    : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Spawn;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.Text("template", Template);
        writer.Int("owner", Owner.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        writer.Fixed("z", Z);
        writer.Fixed("angle", Angle);
    }
}

/// <summary>Destroys an entity (removal, not death: no die modules run).</summary>
public sealed record DestroyCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Destroy;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("target", Target.Value);
}

/// <summary>Deals raw damage (armor coefficients belong to phase 5).</summary>
public sealed record DamageCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target, Fixed Amount, int DamageType, int DeathType)
    : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Damage;
    public override bool RequiresSystem => true;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("target", Target.Value);
        writer.Fixed("amount", Amount);
        writer.Int("damageType", DamageType);
        writer.Int("deathType", DeathType);
    }
}

/// <summary>Kills an entity the way <c>Object::kill</c> does: unresistable damage with the kill flag, so die modules run.</summary>
public sealed record KillCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Kill;
    public override bool RequiresSystem => true;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("target", Target.Value);
}

/// <summary>Creates a timer owned by an entity (or by the world when the target is invalid).</summary>
public sealed record SetTimerCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target, long DelayTicks, long RepeatTicks,
    TimerActionKind Action, Fixed Amount, int DamageType, int TimerTag) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.SetTimer;
    public override bool RequiresSystem => true;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("target", Target.Value);
        writer.Int("delay", DelayTicks);
        writer.Int("repeat", RepeatTicks);
        writer.Int("action", (int)Action);
        writer.Fixed("amount", Amount);
        writer.Int("damageType", DamageType);
        writer.Int("tag", TimerTag);
    }
}

public sealed record CancelTimerCommand(PlayerId Player, ulong Sequence, long Tick, TimerId Timer) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.CancelTimer;
    public override bool RequiresSystem => true;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("timer", Timer.Value);
}

public enum CommandRejection : byte
{
    /// <summary>The command addresses a tick that has already been executed.</summary>
    Late = 1,
    /// <summary>The (player, sequence) pair was already accepted.</summary>
    Duplicate = 2,
    /// <summary>The sequence is lower than the last accepted sequence of the player.</summary>
    OutOfOrder = 3,
    UnknownPlayer = 4,
    /// <summary>A system-only command was issued by a regular player, or an entity of another player was targeted.</summary>
    NotPermitted = 5,
    InvalidArguments = 6,
    UnknownEntity = 7,
    UnknownTemplate = 8,
    UnknownTimer = 9,
}

public readonly record struct SubmitResult(bool Accepted, CommandRejection Reason)
{
    public static SubmitResult Ok => new(true, 0);
    public static SubmitResult Reject(CommandRejection reason) => new(false, reason);
}
