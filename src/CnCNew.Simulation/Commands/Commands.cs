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
    public const int Version = 2;
    public const ushort Spawn = 1;
    public const ushort Destroy = 2;
    public const ushort Damage = 3;
    public const ushort Kill = 4;
    public const ushort SetTimer = 5;
    public const ushort CancelTimer = 6;

    // --------------------------------------------------------------------------------------------------- phase 5
    public const ushort GiveMoney = 7;
    public const ushort PurchaseUpgrade = 8;
    public const ushort QueueProduction = 9;
    public const ushort CancelProduction = 10;
    public const ushort SetMoveTarget = 11;
    public const ushort FireWeapon = 12;

    public static bool IsKnown(ushort code) => code is >= Spawn and <= FireWeapon;
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
            CommandTypes.GiveMoney => new GiveMoneyCommand(player, sequence, tick, new PlayerId(checked((int)reader.Int("target"))), reader.Fixed("amount")),
            CommandTypes.PurchaseUpgrade => new PurchaseUpgradeCommand(player, sequence, tick, reader.Text("upgrade"),
                reader.UInt("target") == 0 ? null : new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.QueueProduction => new QueueProductionCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("producer"))), reader.Text("template")),
            CommandTypes.CancelProduction => new CancelProductionCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("producer"))), checked((int)reader.Int("slot"))),
            CommandTypes.SetMoveTarget => new SetMoveTargetCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target"))), reader.Fixed("x"), reader.Fixed("y")),
            CommandTypes.FireWeapon => new FireWeaponCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("source"))), new EntityId(checked((uint)reader.UInt("target")))),
            _ => throw new InvalidDataException($"Unknown command type {code}."),
        };
    }
}

// ------------------------------------------------------------------------------------------------------- phase 5

/// <summary>Credits a player's balance directly (fixture/scenario setup and scripted income; not a purchase).</summary>
public sealed record GiveMoneyCommand(PlayerId Player, ulong Sequence, long Tick, PlayerId Target, Fixed Amount) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.GiveMoney;
    public override bool RequiresSystem => true;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.Int("target", Target.Value);
        writer.Fixed("amount", Amount);
    }
}

/// <summary>
/// Buys an upgrade (<c>Player::onUpgradeCompleted</c> path): withdraws <see cref="Templates.UpgradeInfo.BuildCost"/> from the
/// issuing player and completes the upgrade immediately (build time is a phase-6 production-queue integration; phase 5
/// treats an upgrade purchase as instantaneous once affordable, matching how it treats structure production as the only
/// timed acquisition). <see cref="Target"/> is required for a <see cref="Templates.UpgradeScope.Object"/> upgrade and must
/// name an object the player owns; it is ignored for a <see cref="Templates.UpgradeScope.Player"/> upgrade.
/// </summary>
public sealed record PurchaseUpgradeCommand(PlayerId Player, ulong Sequence, long Tick, string Upgrade, EntityId? Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.PurchaseUpgrade;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.Text("upgrade", Upgrade);
        writer.UInt("target", Target?.Value ?? 0);
    }
}

/// <summary>Appends one unit template to a producer's build queue (<c>ProductionUpdate::queueCreateUnit</c>).</summary>
public sealed record QueueProductionCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Producer, string Template) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.QueueProduction;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("producer", Producer.Value);
        writer.Text("template", Template);
    }
}

/// <summary>Cancels one queue slot (0-based, head of queue first) and refunds its cost in full (<c>ProductionUpdate::cancelUnitCreate</c>).</summary>
public sealed record CancelProductionCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Producer, int Slot) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.CancelProduction;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("producer", Producer.Value);
        writer.Int("slot", Slot);
    }
}

/// <summary>Orders an object to move in a straight line to (X, Y) (phase 5 "einfache Bewegung"; see <see cref="Templates.LocomotorInfo"/>).</summary>
public sealed record SetMoveTargetCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target, Fixed X, Fixed Y) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.SetMoveTarget;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("target", Target.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
    }
}

/// <summary>Direct-target weapon fire (no auto-acquire, no line of sight, no projectile travel time or splash — see <see cref="Templates.WeaponInfo"/>).</summary>
public sealed record FireWeaponCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Source, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.FireWeapon;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("source", Source.Value);
        writer.UInt("target", Target.Value);
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

    // --------------------------------------------------------------------------------------------------- phase 5
    InsufficientFunds = 10,
    AlreadyOwned = 11,
    MissingPrerequisite = 12,
    UnknownUpgrade = 13,
    NoWeapon = 14,
    WeaponNotReady = 15,
    OutOfRange = 16,
    QueueFull = 17,
    CannotMove = 18,
    CannotProduce = 19,
}

public readonly record struct SubmitResult(bool Accepted, CommandRejection Reason)
{
    public static SubmitResult Ok => new(true, 0);
    public static SubmitResult Reject(CommandRejection reason) => new(false, reason);
}
