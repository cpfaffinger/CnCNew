using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Commands;

/// <summary>
/// Versioned command type table. A command type code is part of the replay format; codes are never reused or changed within
/// a table version. Adding a type increases <see cref="Version"/>.
/// </summary>
public static class CommandTypes
{
    public const int Version = 12;
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

    // --------------------------------------------------------------------------------------------------- teams and diplomacy
    public const ushort SetRelationship = 19;

    // --------------------------------------------------------------------------------------------------- construction
    public const ushort Construct = 20;
    public const ushort Sell = 21;
    public const ushort SetRallyPoint = 22;
    public const ushort ResumeConstruct = 23;
    public const ushort CancelConstruct = 24;
    public const ushort Repair = 25;

    // --------------------------------------------------------------------------------------------------- transports and tunnels (v9)
    public const ushort MoveAndEnter = 26;
    public const ushort Evacuate = 27;

    // --------------------------------------------------------------------------------------------------- build/rule limits (v10)
    /// <summary><c>GUI_COMMAND_TOGGLE_OVERCHARGE</c> (<c>CommandButton.ini</c> <c>Command = TOGGLE_OVERCHARGE</c>): flips an <c>OverchargeBehavior</c>.</summary>
    public const ushort Overcharge = 28;
    /// <summary>
    /// Direct capture of a structure (<c>Object::onCapture</c>'s ownership change plus its power-pool effect). Retail gates capturing behind a
    /// channeled <c>SpecialAbilityUpdate</c> ability (<c>hasSpecialPower(SPECIAL_*_CAPTURE_BUILDING)</c>, <c>ActionManager::canCaptureBuilding</c>);
    /// that module is a 64-object blocker spanning unrelated channeled abilities (disguise, hijack, hack, paradrop, capture) and is out of scope
    /// here. This command approximates only the capture <em>outcome</em> for any of the issuer's own mobile units already standing next to the
    /// target, without the ability's channel timer, science gate or stealth-detection requirement — a documented simplification, not a port.
    /// A unit that carries the capture ability itself (<c>SpecialAbilityUpdate</c> of <c>SPECIAL_INFANTRY_CAPTURE_BUILDING</c> /
    /// <c>SPECIAL_BLACKLOTUS_CAPTURE_BUILDING</c>) takes the staged path instead: it walks to the target and unpacks, raises the flag and packs
    /// (<c>docs/SIM-STAGED-CAPTURE.md</c>); the command then needs that power ready, not the unit in reach.
    /// </summary>
    public const ushort Capture = 29;

    // --------------------------------------------------------------------------------------------------- weapon slots (v11)
    /// <summary><c>Object::setWeaponLock</c>/<c>releaseWeaponLock</c> (<c>GUI_COMMAND_SWITCH_WEAPON</c>): pins or releases the weapon slot an object fires.</summary>
    public const ushort SetWeaponLock = 30;

    public static bool IsKnown(ushort code) => code is >= Spawn and <= SetWeaponLock;
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
            CommandTypes.FireWeapon => new FireWeaponCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("source"))), new EntityId(checked((uint)reader.UInt("target"))),
                reader.Has("slot") ? checked((int)reader.Int("slot")) : WeaponSlot.Best, reader.Has("origin") ? (FireOrigin)checked((byte)reader.Int("origin")) : FireOrigin.Player),
            CommandTypes.GiveSciencePoints => new GiveSciencePointsCommand(player, sequence, tick, new PlayerId(checked((int)reader.Int("target"))), checked((int)reader.Int("amount"))),
            CommandTypes.PurchaseScience => new PurchaseScienceCommand(player, sequence, tick, reader.Text("science")),
            CommandTypes.EnterContainer => new EnterContainerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("container")))),
            CommandTypes.ExitContainer => new ExitContainerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor")))),
            CommandTypes.UseSpecialPower => new UseSpecialPowerCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("source"))), reader.Text("power"),
                (SpecialPowerTargetKind)checked((byte)reader.Int("targetKind")), new EntityId(checked((uint)reader.UInt("target"))), reader.Fixed("x"), reader.Fixed("y"), reader.Has("option") ? checked((int)reader.Int("option")) : 0),
            CommandTypes.SetRelationship => new SetRelationshipCommand(player, sequence, tick, new PlayerId(checked((int)reader.Int("from"))), new PlayerId(checked((int)reader.Int("to"))),
                (Relationship)checked((int)reader.Int("relationship"))),
            CommandTypes.Dock => new DockCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("dock")))),
            CommandTypes.Construct => new ConstructCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("builder"))), reader.Text("template"),
                reader.Fixed("x"), reader.Fixed("y"), reader.Fixed("angle")),
            CommandTypes.Sell => new SellCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.SetRallyPoint => new SetRallyPointCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("producer"))), reader.Fixed("x"), reader.Fixed("y")),
            CommandTypes.ResumeConstruct => new ResumeConstructCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("builder"))), new EntityId(checked((uint)reader.UInt("site")))),
            CommandTypes.CancelConstruct => new CancelConstructCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("site")))),
            CommandTypes.Repair => new RepairCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("builder"))), new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.MoveAndEnter => new MoveAndEnterCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("container")))),
            CommandTypes.Evacuate => new EvacuateCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("container"))), reader.Bool("hasPosition"), reader.Fixed("x"), reader.Fixed("y")),
            CommandTypes.Overcharge => new OverchargeCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.Capture => new CaptureCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("actor"))), new EntityId(checked((uint)reader.UInt("target")))),
            CommandTypes.SetWeaponLock => new SetWeaponLockCommand(player, sequence, tick, new EntityId(checked((uint)reader.UInt("source"))), checked((int)reader.Int("slot")),
                (WeaponLockKind)checked((byte)reader.Int("lock"))),
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

/// <summary>
/// Direct-target weapon fire (no auto-acquire, no line of sight, no projectile travel time or splash — see <see cref="Templates.WeaponInfo"/>). One shot from
/// one weapon slot: with <see cref="Slot"/> = <see cref="WeaponSlot.Best"/> the world chooses among the slots like <c>WeaponSet::chooseBestWeaponForTarget</c>
/// (<see cref="Origin"/> decides which slots the unit may pick by itself, <c>AutoChooseSources</c>); an explicit slot is a <c>FIRE_WEAPON</c> command button's
/// <c>WeaponSlot</c> and also pins the slot (<c>setWeaponLock(LOCKED_TEMPORARILY)</c>) until its clip empties or the unit is ordered to move.
/// </summary>
public sealed record FireWeaponCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Source, EntityId Target, int Slot = WeaponSlot.Best, FireOrigin Origin = FireOrigin.Player)
    : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.FireWeapon;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("source", Source.Value);
        writer.UInt("target", Target.Value);
        // Written only when they differ from the defaults (best weapon, a player's order), so an ordinary fire command describes itself exactly as before the slot model.
        if (Slot != WeaponSlot.Best) writer.Int("slot", Slot);
        if (Origin != FireOrigin.Player) writer.Int("origin", (long)Origin);
    }
}

/// <summary>
/// <c>Object::setWeaponLock</c> (<paramref name="Lock"/> temporary or permanent: the slot becomes the current weapon and automatic weapon choice leaves it alone) or
/// <c>releaseWeaponLock</c> (<see cref="WeaponLockKind.None"/>: every lock is released, <paramref name="Slot"/> is ignored). A temporary lock never overrides a permanent one.
/// </summary>
public sealed record SetWeaponLockCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Source, int Slot, WeaponLockKind Lock) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.SetWeaponLock;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("source", Source.Value);
        writer.Int("slot", Slot);
        writer.Int("lock", (long)Lock);
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
/// <summary>The target of a special power; <paramref name="Option"/> is the command button's option (0 none, 1-3 OPTION_ONE..OPTION_THREE: the strategy center's battle plan).</summary>
public readonly record struct SpecialPowerTarget(SpecialPowerTargetKind Kind, EntityId Entity, Fixed X, Fixed Y, int Option = 0);

/// <summary>
/// Fires the special power named <see cref="SpecialPower"/> (a <c>SpecialPower</c> definition) from the module of <see cref="Source"/> that
/// carries it (<c>ActionManager::canDoSpecialPower</c> + <c>SpecialPowerModule::doSpecialPower*</c>). The power must be ready, unpaused, its
/// required science owned, and — for powers whose effect lives in an update module (<c>UpdateModuleStartsAttack</c>) — a handler must exist.
/// </summary>
public sealed record UseSpecialPowerCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Source, string SpecialPower, SpecialPowerTargetKind TargetKind,
    EntityId Target, Fixed X, Fixed Y, int Option = 0) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.UseSpecialPower;

    public SpecialPowerTarget AsTarget => new(TargetKind, Target, X, Y, Option);

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("source", Source.Value);
        writer.Text("power", SpecialPower);
        writer.Int("targetKind", (byte)TargetKind);
        writer.UInt("target", Target.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        if (Option != 0) writer.Int("option", Option);
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

// ------------------------------------------------------------------------------------------------------- construction

/// <summary>
/// <c>MSG_DOZER_CONSTRUCT</c>: <see cref="Builder"/> (a dozer or worker) places a new structure of <see cref="Template"/> at (X, Y) with <see cref="Angle"/>
/// (<c>Player::canBuild</c>, <c>BuildAssistant::isLocationLegalToBuild</c>, <c>DozerAIUpdate::construct</c>): the cost is paid at placement, the site appears
/// at once and the builder walks over and builds it.
/// </summary>
public sealed record ConstructCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Builder, string Template, Fixed X, Fixed Y, Fixed Angle) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Construct;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("builder", Builder.Value);
        writer.Text("template", Template);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
        writer.Fixed("angle", Angle);
    }
}

/// <summary><c>MSG_SELL</c>: starts the selling of an owned structure (<c>BuildAssistant::sellObject</c>).</summary>
public sealed record SellCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Sell;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("target", Target.Value);
}

/// <summary><c>MSG_SET_RALLY_POINT</c>: units the producer builds from now on walk to (X, Y) after leaving it.</summary>
public sealed record SetRallyPointCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Producer, Fixed X, Fixed Y) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.SetRallyPoint;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("producer", Producer.Value);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
    }
}

/// <summary><c>DozerAIUpdate::resumeConstruction</c> (<c>MSG_RESUME_CONSTRUCTION</c>): <see cref="Builder"/> continues an interrupted construction site.</summary>
public sealed record ResumeConstructCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Builder, EntityId Site) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.ResumeConstruct;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("builder", Builder.Value);
        writer.UInt("site", Site.Value);
    }
}

/// <summary><c>MSG_DOZER_CANCEL_CONSTRUCT</c>: cancels an unfinished structure, refunds its full cost and destroys the site.</summary>
public sealed record CancelConstructCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Site) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.CancelConstruct;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("site", Site.Value);
}

/// <summary><c>MSG_REPAIR</c> (<c>DozerAIUpdate::newTask(DOZER_TASK_REPAIR)</c>): <see cref="Builder"/> walks to a damaged owned structure and repairs it.</summary>
public sealed record RepairCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Builder, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Repair;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("builder", Builder.Value);
        writer.UInt("target", Target.Value);
    }
}

// ------------------------------------------------------------------------------------------------------- build/rule limits (v10)

/// <summary><c>TOGGLE_OVERCHARGE</c>: flips the <c>OverchargeBehavior</c> of <see cref="Target"/> (idempotent only in effect, never rejected by current state).</summary>
public sealed record OverchargeCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Overcharge;
    protected override void DescribeBody(IStateWriter writer) => writer.UInt("target", Target.Value);
}

/// <summary>Direct capture: <see cref="Actor"/> (owned by the issuer) takes <see cref="Target"/> for the issuer. See <see cref="CommandTypes.Capture"/>.</summary>
public sealed record CaptureCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Actor, EntityId Target) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Capture;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("actor", Actor.Value);
        writer.UInt("target", Target.Value);
    }
}

// ------------------------------------------------------------------------------------------------------- transports and tunnels

/// <summary>
/// <c>aiEnter</c> (<c>MSG_ENTER</c>): <see cref="Actor"/> walks to <see cref="Container"/> (a transport, a tunnel, a garrisonable building) and gets in on
/// arrival if the container still takes it then (capacity is checked on arrival, the entry rules at submission and on arrival). Unlike the
/// instant <see cref="EnterContainerCommand"/> this is the order a player gives.
/// </summary>
public sealed record MoveAndEnterCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Actor, EntityId Container) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.MoveAndEnter;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("actor", Actor.Value);
        writer.UInt("container", Container.Value);
    }
}

/// <summary>
/// <c>MSG_EVACUATE</c> / <c>aiMoveToAndEvacuate</c>: every passenger of <see cref="Container"/> leaves it — a mobile container first moves to (X, Y) when
/// <see cref="HasPosition"/> is set (a Chinook lands before anybody leaves), a building or tunnel lets them out at once; evacuating a tunnel lets the whole
/// network out through that tunnel. Only the container's owner may order it.
/// </summary>
public sealed record EvacuateCommand(PlayerId Player, ulong Sequence, long Tick, EntityId Container, bool HasPosition, Fixed X, Fixed Y) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.Evacuate;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.UInt("container", Container.Value);
        writer.Bool("hasPosition", HasPosition);
        writer.Fixed("x", X);
        writer.Fixed("y", Y);
    }
}

/// <summary>
/// <c>Player::setPlayerRelationship</c> (the script action behind "set relationship"): <see cref="From"/> sees <see cref="To"/> as
/// <see cref="Relationship"/> from now on. Directed — the reverse relation is a separate command. System-only, both players must be real and distinct.
/// </summary>
public sealed record SetRelationshipCommand(PlayerId Player, ulong Sequence, long Tick, PlayerId From, PlayerId To, Relationship Relationship) : SimCommand(Player, Sequence, Tick)
{
    public override ushort TypeCode => CommandTypes.SetRelationship;
    public override bool RequiresSystem => true;

    protected override void DescribeBody(IStateWriter writer)
    {
        writer.Int("from", From.Value);
        writer.Int("to", To.Value);
        writer.Int("relationship", (int)Relationship);
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

    // --------------------------------------------------------------------------------------------------- phase 6
    /// <summary>The target lies in a cell the issuing player cannot see and is not an ally's object, or it is stealthed and undetected (<c>Object::getShroudedStatus</c>, <c>ActionManager::canAttack</c>).</summary>
    TargetNotVisible = 31,
    /// <summary>A structure placement failed: off the map, on a cliff or water, overlapping another footprint, or inside the shroud.</summary>
    InvalidPlacement = 32,
    /// <summary>The builder is not a dozer/worker that can build the template, or the template is not a buildable structure.</summary>
    CannotConstruct = 33,
    /// <summary>The structure cannot be sold: not a structure, still under construction, or already being sold (<c>BuildAssistant::sellObject</c>).</summary>
    CannotSell = 34,

    // --------------------------------------------------------------------------------------------------- phase 10: build/rule limits
    /// <summary>
    /// <c>Player::canBuild</c>/<c>canProduceObject</c> for a unit queued at a producer: the template is a <c>STRUCTURE</c> (must be placed via
    /// <c>ConstructCommand</c> instead), <c>Buildable</c> is <c>No</c>, <c>Only_By_AI</c> for a human player, or its <c>Side</c> does not match the
    /// producer's.
    /// </summary>
    CannotProduceTemplate = 35,
    /// <summary><c>Player::canBuildMoreOfType</c>: the template's <c>MaxSimultaneousOfType</c> live-or-queued count is already reached.</summary>
    ProductionLimitReached = 36,
    /// <summary>
    /// <c>ActionManager::canCaptureBuilding</c> (reduced, see <see cref="CommandTypes.Capture"/>): the actor has no AI, is not the issuer's,
    /// or the target is not a capturable structure (dead, under construction, sold, owned by an ally, or not <c>KINDOF_CAPTURABLE</c> while not an
    /// enemy's), is out of reach, or is not visible to the issuer.
    /// </summary>
    CannotCapture = 37,
    /// <summary>The target has no <c>OverchargeBehavior</c> module, is not the issuer's, is dead, or is still under construction.</summary>
    CannotToggleOvercharge = 38,

    // --------------------------------------------------------------------------------------------------- weapon slots
    /// <summary>
    /// No weapon the object may fire at this target: every candidate weapon either cannot target its kind (<c>WeaponAntiMaskType</c> against
    /// <c>getVictimAntiMask</c>) or would do no damage through the victim's armor (<c>estimateWeaponDamage</c>, unresistable weapons excepted).
    /// </summary>
    InvalidWeaponTarget = 39,
}

public readonly record struct SubmitResult(bool Accepted, CommandRejection Reason)
{
    public static SubmitResult Ok => new(true, 0);
    public static SubmitResult Reject(CommandRejection reason) => new(false, reason);
}
