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
    public const int Version = 6;
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
    public const ushort GiveSciencePoints = 13;
    public const ushort PurchaseScience = 14;
    public const ushort EnterContainer = 15;
    public const ushort ExitContainer = 16;

    // --------------------------------------------------------------------------------------------------- special powers
    public const ushort UseSpecialPower = 17;

    // --------------------------------------------------------------------------------------------------- supply economy
    public const ushort Dock = 18;

    public static bool IsKnown(ushort code) => code is >= Spawn and <= Dock;
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
            CommandTypes.GiveSciencePoints => new GiveSciencePointsCommand(player, sequence, tick, new PlayerId(checked((int)reader.Int("target"))), checked((int)reader.Int("amount"))),
            CommandTypes.PurchaseScience => new PurchaseScienceCommand(player, sequence, tick, reader.Text("science")),
            CommandTypes.EnterContainer => new EnterContainerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("container")))),
            CommandTypes.ExitContainer => new ExitContainerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor")))),
            CommandTypes.UseSpecialPower => new UseSpecialPowerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("source"))), reader.Text("power"),
                (SpecialPowerTargetKind)checked((byte)reader.Int("targetKind")), new EntityId(checked((uint)reader.UInt("target"))), reader.Fixed("x"), reader.Fixed("y")),
            CommandTypes.Dock => new DockCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("dock")))),
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

/// <summary>Credits "Science Purchase Points" directly (nothing yet earns them automatically — no rank/experience system; see <c>docs/GAME-RULES.md</c>).</summary>
public sealed record GiveSciencePointsCommand(PlayerId Player, ulong Sequence, long Tick, PlayerId Target, int Amount) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.GiveSciencePoints;
    public override bool RequiresSystem => true;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.Int("target", Target.Value);
        writer.Int("amount", Amount);
    }
}

/// <summary><c>Player::attemptToPurchaseScience</c>: always player-scoped, requires all direct prerequisites already owned and enough purchase points; instantaneous (no build time — sciences are not queued anywhere in the reference either).</summary>
public sealed record PurchaseScienceCommand(PlayerId Player, ulong Sequence, long Tick, string Science) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.PurchaseScience;
    protected override void DescribeBody(IStateWriter writer) => writer.Text("science", Science);
}

/// <summary>
/// Places <see cref="Actor"/> inside <see cref="Container"/> (<c>ContainModuleInterface::addToContain</c>). Phase 5 has no
/// pathfinding/movement-to-enter (matches the rest of phase 5's "instant, no real spatial gameplay" economy/production
/// commands): entry succeeds immediately if <c>IsValidContainerFor</c> allows it, regardless of distance.
/// </summary>
public sealed record EnterContainerCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Actor, EntityId Container) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.EnterContainer;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("actor", Actor.Value);
        writer.UInt("container", Container.Value);
    }
}

/// <summary>
/// Removes <see cref="Actor"/> from whatever currently contains it. Either the actor's own owner or the container's
/// owner may order this (the reference's "Evacuate" command belongs to the container's owner; a passenger's own owner
/// can also always leave).
/// </summary>
public sealed record ExitContainerCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Actor) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.ExitContainer;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("actor", Actor.Value);
}

/// <summary>What a special power is aimed at (<c>doSpecialPower</c> / <c>doSpecialPowerAtObject</c> / <c>doSpecialPowerAtLocation</c>).</summary>
public enum SpecialPowerTargetKind : byte
{
    None = 0,
    Object = 1,
    Location = 2,
}

/// <summary>The target of a special power use: <see cref="Entity"/> for <see cref="SpecialPowerTargetKind.Object"/>, (<see cref="X"/>, <see cref="Y"/>) for <see cref="SpecialPowerTargetKind.Location"/>.</summary>
public readonly record struct SpecialPowerTarget(SpecialPowerTargetKind Kind, EntityId Entity, Fixed X, Fixed Y);

/// <summary>
/// Fires the special power named <see cref="SpecialPower"/> (a <c>SpecialPower</c> definition) from the module of <see cref="Source"/> that
/// carries it (<c>ActionManager::canDoSpecialPower</c> + <c>SpecialPowerModule::doSpecialPower*</c>). The power must be ready, unpaused, its
/// required science owned, and — for powers whose effect lives in an update module (<c>UpdateModuleStartsAttack</c>) — a handler must exist.
/// </summary>
public sealed record UseSpecialPowerCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Source, string SpecialPower, SpecialPowerTargetKind TargetKind,
    EntityId Target, Fixed X, Fixed Y) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.UseSpecialPower;

    public SpecialPowerTarget AsTarget => new(TargetKind, Target, X, Y);

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("source", Source.Value);
        writer.Text("power", SpecialPower);
        writer.Int("targetKind", (byte)TargetKind);
        writer.UInt("target", Target.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
    }
}

/// <summary>
/// Orders <see cref="Actor"/> to dock with <see cref="Dock"/> (<c>aiDock</c> from the player: supply trucks and workers drop off or collect,
/// and remember the dock as their preferred one). The dock must carry a dock module.
/// </summary>
public sealed record DockCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Actor, EntityId Dock) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Dock;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("actor", Actor.Value);
        writer.UInt("dock", Dock.Value);
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
    UnknownScience = 20,
    /// <summary><c>Player::isCapableOfPurchasingScience</c>: a purchase cost of 0 means the science is not directly purchasable (only ever granted, e.g. as an intrinsic faction science).</summary>
    ScienceNotPurchasable = 21,
    InsufficientSciencePurchasePoints = 22,
    /// <summary><c>OpenContain::isValidContainerFor</c> failed: KindOf filter, relationship (<c>AllowAlliesInside</c> etc.), capacity, or (<c>GarrisonContain</c>) the container is destroyed, dead or too damaged to garrison.</summary>
    CannotEnterContainer = 23,
    /// <summary>An exit was requested for an entity that is not currently contained (by anything, or not by the named container).</summary>
    NotContained = 24,
    /// <summary>The special power is not defined, or the source entity has no module carrying it.</summary>
    UnknownSpecialPower = 25,
    /// <summary>The special power's <c>RequiredScience</c> is not owned by the issuing player.</summary>
    MissingScience = 26,
    /// <summary>The power is paused (<c>UnpauseSpecialPowerUpgrade</c> not yet applied) or still recharging.</summary>
    SpecialPowerNotReady = 27,
    /// <summary>The target kind does not fit the power (an object power without an object, an OCL power with an object that does not exist).</summary>
    InvalidSpecialPowerTarget = 28,
    /// <summary>The effect of the power lives in an update module (<c>UpdateModuleStartsAttack</c>) that is not implemented; the use is refused instead of silently doing nothing.</summary>
    SpecialPowerHandlerMissing = 29,
    /// <summary>The dock order names an object without a dock module, or an actor that cannot dock (no AI module).</summary>
    CannotDock = 30,
}

public readonly record struct SubmitResult(bool Accepted, CommandRejection Reason)
{
    public static SubmitResult Ok => new(true, 0);
    public static SubmitResult Reject(CommandRejection reason) => new(false, reason);
}
