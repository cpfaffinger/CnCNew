using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>The two AI updates of the troop-carrying and money-making infantry of the retail data: <c>HackInternetAIUpdate</c> and <c>AssaultTransportAIUpdate</c>.</summary>
internal static class HackerAndAssaultModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<AssaultTransportData>("AssaultTransportAIUpdate", ModuleOwnerKind.Object, (configuration, _) => AssaultTransportData.Parse(configuration), data => new AssaultTransportAIUpdate(data)),
        new ModuleBinding<HackInternetData>("HackInternetAIUpdate", ModuleOwnerKind.Object, (configuration, _) => HackInternetData.Parse(configuration), data => new HackInternetAIUpdate(data)),
    ];
}

// ================================================================================================================ HackInternetAIUpdate

/// <summary>
/// <c>HackInternetAIUpdateModuleData</c> (<c>HackInternetAIUpdate.h</c>): the unpack/pack times (frames), the variation of both, the cash delay (on the ground and the faster
/// one inside a container), the cash per veterancy level (a level without an amount falls back to the next lower one, and to 1 when all are missing) and the experience per payout.
/// </summary>
public sealed record HackInternetData(AIUpdateData Base, long UnpackTicks, long PackTicks, Fixed VariationFactor, long CashDelayTicks, long CashDelayFastTicks,
    int RegularCash, int VeteranCash, int EliteCash, int HeroicCash, int XpPerCash) : ModuleData
{
    public static HackInternetData Parse(RatedModuleConfiguration configuration) => new(
        AIUpdateData.Parse(configuration),
        ModuleConfigReader.Ticks(configuration, "UnpackTime", 0), ModuleConfigReader.Ticks(configuration, "PackTime", 0),
        ModuleConfigReader.Real(configuration, "PackUnpackVariationFactor", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "CashUpdateDelay", 0), ModuleConfigReader.Ticks(configuration, "CashUpdateDelayFast", 0),
        checked((int)ModuleConfigReader.Int(configuration, "RegularCashAmount", 0)), checked((int)ModuleConfigReader.Int(configuration, "VeteranCashAmount", 0)),
        checked((int)ModuleConfigReader.Int(configuration, "EliteCashAmount", 0)), checked((int)ModuleConfigReader.Int(configuration, "HeroicCashAmount", 0)),
        checked((int)ModuleConfigReader.Int(configuration, "XpPerCashUpdate", 0)));
}

/// <summary>
/// <c>HackInternetAIUpdate.cpp</c>: the hacker's state machine. <see cref="StartHacking"/> (<c>aiHackInternet</c>, given by <c>InternetHackContain</c> to a rider entering the Internet
/// Center, by the <c>HACK_INTERNET</c> command button and by the skirmish AI) cancels whatever the unit was doing and unpacks (<c>UnpackTime</c> times a random factor in
/// [1 − v, 1 + v], one logic draw), then hacks: every <c>CashUpdateDelay</c> frames (<c>CashUpdateDelayFast</c> inside a container) the owner gets the cash of the hacker's veterancy
/// level and the hacker the experience <c>XpPerCashUpdate</c>. A move order given while hacking is held back until the hacker has packed up (<c>PackTime</c>, none inside a container),
/// then carried out. Any other order replaces the hacking. There is no command in this simulation that starts hacking from outside a container yet — only
/// <c>InternetHackContain</c> and the <see cref="StartHacking"/> call do. The cash ping, floating text and the model conditions are presentation. A hacker that
/// <c>SabotageInternetCenterCrateCollide</c> disabled (<see cref="DisableUntil"/>, <c>DISABLED_HACKED</c>) does nothing at all until the tick: no state advances and no cash is paid.
/// </summary>
public sealed class HackInternetAIUpdate(HackInternetData data) : AIUpdateInterface(data.Base)
{
    private enum HackState : byte { None, Unpacking, Hacking, Packing }

    private HackState _state;
    private long _remaining;
    private long _hackedUntil;
    private bool _hasPending;
    private bool _pendingFromPlayer;
    private Fixed _pendingX;
    private Fixed _pendingY;

    public override string ModuleName => "HackInternetAIUpdate";

    /// <summary><c>Object::setDisabledUntil(DISABLED_HACKED, frame)</c> as the Internet-Center sabotage hands it to every hacker inside: the module's update does not run before <paramref name="tick"/>.</summary>
    public void DisableUntil(IWorldServices world, long tick)
    {
        _hackedUntil = tick;
        Entity.Speed = Fixed.Zero;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    /// <summary><c>isHacking</c>: in the cash-making state.</summary>
    public bool IsHacking => _state == HackState.Hacking;

    /// <summary><c>UnpackingState</c>: the reference keeps <c>MODELCONDITION_UNPACKING</c> set while it runs.</summary>
    public bool IsUnpacking => _state == HackState.Unpacking;

    /// <summary><c>PackingState</c>: the reference shows <c>MODELCONDITION_PACKING</c> while it runs.</summary>
    public bool IsPacking => _state == HackState.Packing;

    /// <summary><c>isHackingPackingOrUnpacking</c>.</summary>
    public bool IsHackingPackingOrUnpacking => _state != HackState.None;

    /// <summary><c>AIUpdateInterface::privateHackInternet</c> / <c>HackInternetAIUpdate::hackInternet</c>.</summary>
    public void StartHacking(IWorldServices world)
    {
        StopInternalMove();
        _hasPending = false;
        _state = HackState.Unpacking;
        _remaining = Varied(world, data.UnpackTicks);
        world.WakeAt(Entity, this, world.Tick);
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>m_framesRemaining = getUnpackTime() * variation</c> (<c>HackInternetAIUpdate.cpp</c>, <c>UnpackingState::onEnter</c>/<c>PackingState::onEnter</c>): varied and floored in
    /// logic frames of the original, then turned into the reload of the per-tick countdown (every phase lasts its frames plus one, like the reference's
    /// "if (m_framesRemaining &gt; 0) m_framesRemaining--; else …").
    /// </summary>
    private long Varied(IWorldServices world, long ticks)
    {
        var variation = world.LogicRandom.Range(Fixed.One - data.VariationFactor, Fixed.One + data.VariationFactor);
        var frames = (Fixed.FromInt(world.Time.ToOriginalFrames(ticks)) * variation).Floor();
        return world.Time.CountdownReload(world.Time.OriginalFrames(frames));
    }

    private long CashDelay(IWorldServices world) => world.Time.CountdownReload(Entity.ContainedBy.IsValid ? data.CashDelayFastTicks : data.CashDelayTicks);

    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (_state is HackState.Hacking or HackState.Packing)
        {
            // "nuke any existing pending cmd": hold it until the gear is packed.
            _hasPending = true;
            _pendingX = x;
            _pendingY = y;
            _pendingFromPlayer = fromPlayer;
            if (_state == HackState.Hacking)
            {
                _state = HackState.Packing;
                _remaining = Entity.ContainedBy.IsValid ? world.Time.CountdownReload(0) : Varied(world, data.PackTicks);
            }
            world.WakeAt(Entity, this, world.Tick);
            world.MarkDirty(Entity);
            return;
        }
        _hasPending = false;
        _state = HackState.None;
        base.OrderMove(world, x, y, fromPlayer, attackMove);
    }

    public override long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        if (world.Tick < _hackedUntil) return SimulationTiming.SleepNone;
        // The parent's idle test, not ours: a hacker that holds a pending order is never idle.
        if (_state == HackState.None && _hasPending && IsIdle)
        {
            _hasPending = false;
            base.OrderMove(world, _pendingX, _pendingY, _pendingFromPlayer);
        }
        var sleep = base.Update(world);
        switch (_state)
        {
            case HackState.Unpacking:
                if (_remaining > 0) _remaining--;
                else
                {
                    _state = HackState.Hacking;
                    _remaining = CashDelay(world);
                }
                break;
            case HackState.Hacking:
                if (_remaining > 0) _remaining--;
                else
                {
                    Pay(world);
                    _remaining = CashDelay(world);
                }
                break;
            case HackState.Packing:
                if (_remaining > 0) _remaining--;
                else _state = HackState.None;
                break;
        }
        return _state != HackState.None || _hasPending ? SimulationTiming.SleepNone : sleep;
    }

    /// <summary>The payout of <c>HackInternetState::update</c>: the amount of the unit's level, else of the next lower level that has one, else 1.</summary>
    private void Pay(IWorldServices world)
    {
        int[] amounts = [data.RegularCash, data.VeteranCash, data.EliteCash, data.HeroicCash];
        var amount = 1;
        for (var level = Math.Clamp(Entity.VeterancyLevel, 0, 3); level >= 0; level--)
            if (amounts[level] != 0)
            {
                amount = amounts[level];
                break;
            }
        world.AddMoney(Entity.Owner, Fixed.FromInt(amount));
        if (data.XpPerCash > 0) world.AddExperiencePoints(Entity, data.XpPerCash);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Int("hackState", (int)_state);
        writer.Int("hackRemaining", _remaining);
        if (_hackedUntil > 0) writer.Int("hackedUntil", _hackedUntil);
        if (_hasPending)
        {
            writer.Bool("hackPending", true);
            writer.Bool("hackPendingFromPlayer", _pendingFromPlayer);
            writer.Fixed("hackPendingX", _pendingX);
            writer.Fixed("hackPendingY", _pendingY);
        }
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        var state = reader.Int("hackState");
        if (state < 0 || state > (int)HackState.Packing) throw new InvalidDataException("Hack state out of range.");
        _state = (HackState)state;
        _remaining = reader.Int("hackRemaining");
        _hackedUntil = reader.Has("hackedUntil") ? reader.Int("hackedUntil") : 0;
        _hasPending = reader.Has("hackPending") && reader.Bool("hackPending");
        _pendingFromPlayer = _hasPending && reader.Bool("hackPendingFromPlayer");
        _pendingX = _hasPending ? reader.Fixed("hackPendingX") : Fixed.Zero;
        _pendingY = _hasPending ? reader.Fixed("hackPendingY") : Fixed.Zero;
    }
}

// ============================================================================================================ AssaultTransportAIUpdate

/// <summary><c>AssaultTransportAIUpdateModuleData</c> (<c>AssaultTransportAIUpdate.h</c>): the life ratio below which a member counts as wounded (default 0) and <c>ClearRangeRequiredToContinueAttackMove</c> (default 50, used by the attack-move order that does not exist here).</summary>
public sealed record AssaultTransportData(AIUpdateData Base, Fixed MembersGetHealedAtLifeRatio, Fixed ClearRangeRequiredToContinueAttackMove) : ModuleData
{
    public static AssaultTransportData Parse(RatedModuleConfiguration configuration) => new(
        AIUpdateData.Parse(configuration),
        ModuleConfigReader.Real(configuration, "MembersGetHealedAtLifeRatio", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "ClearRangeRequiredToContinueAttackMove", Fixed.FromInt(50)));
}

/// <summary>
/// <c>AssaultTransportAIUpdate.cpp</c>: the Troop Crawler's brain. It keeps a list of its members (the passengers; those that boarded after the first pass are "new" and take
/// no part in the current assault; a member that is given a player command leaves the group). When the transport's weapon — the <c>DEPLOY</c> dummy shot — fires at a target
/// (<see cref="BeginAssault"/>), every healthy, full-health member inside gets out and goes for the target; wounded members (health ratio below
/// <c>MembersGetHealedAtLifeRatio</c>) outside walk back into the transport to be healed. A dead transport forgets its members.
/// <para>
/// Differences: the reference starts the assault when the weapon system tells it the target; here the module notices a new shot of a <c>DEPLOY</c> weapon (<c>Entity.LastFireTick</c>)
/// and takes the nearest enemy within the weapon's range as the designated target (shots carry no victim into modules); a shot at a new target also plays the part of the
/// player's attack order, which resets the group, so everybody aboard at that moment may deploy while later arrivals sit the assault out. Members are ordered to <em>move</em> to the target, not to
/// attack it — fire orders do not exist for modules; armed objects acquire targets by themselves (<c>MissionSession.DriveCombat</c>). The order-driven parts (attack-move, attack-object
/// orders, handing the original order to the members when the transport dies, "attack is pointless") have no counterpart because the simulation has no such orders.
/// </para>
/// </summary>
public sealed class AssaultTransportAIUpdate(AssaultTransportData data) : AIUpdateInterface(data.Base)
{
    private static readonly int DeployDamage = NameTables.DamageType.TryIndexOf("DEPLOY", out var deploy) ? deploy : throw new InvalidOperationException("DEPLOY is not a damage type.");

    private readonly List<(EntityId Id, bool Healing, bool IsNew)> _members = [];
    private EntityId _designated;
    private bool _newOccupantsAreNewMembers;
    private long _lastShot = -1;

    public override string ModuleName => "AssaultTransportAIUpdate";

    /// <summary>The reference never lets this update sleep ("someday, make sleepy"): it keeps the group list current every tick.</summary>
    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public int MemberCount => _members.Count;
    public EntityId DesignatedTarget => _designated;

    /// <summary><c>beginAssault</c>: the transport is in range; the healthy troops are to deploy against <paramref name="target"/>.</summary>
    public void BeginAssault(Entity target) => _designated = target.Id;

    private bool IsWounded(Entity member)
        => member.Body is { } body && body.MaxHealth > Fixed.Zero && body.Health / body.MaxHealth < data.MembersGetHealedAtLifeRatio;

    private static bool IsHealthy(Entity member) => member.Body is { } body && body.Health == body.MaxHealth;

    public override long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead)
        {
            _members.Clear();
            return SimulationTiming.SleepForever;
        }

        // Dead members and members given orders outside this AI leave the group.
        for (var index = 0; index < _members.Count;)
        {
            var member = world.FindEntity(_members[index].Id);
            if (member is null || member.EffectivelyDead || member.DestroyRequested || SlaveSupport.AiOf(member) is not { } memberAi || memberAi.LastCommandFromPlayer) _members.RemoveAt(index);
            else index++;
        }

        // Passengers that are not members yet become members.
        if (Entity.Container is { } contain)
        {
            foreach (var passengerId in contain.Contained.ToArray())
            {
                if (_members.Any(member => member.Id == passengerId) || world.FindEntity(passengerId) is not { } passenger) continue;
                _members.Add((passengerId, IsWounded(passenger), _newOccupantsAreNewMembers));
            }
            _newOccupantsAreNewMembers = true;
        }

        DetectDeployShot(world);

        var target = world.FindEntity(_designated);
        if (target is not null && (target.EffectivelyDead || target.DestroyRequested)) target = null;
        if (target is not null) DriveMembers(world, target);

        base.Update(world);
        return SimulationTiming.SleepNone;
    }

    /// <summary>A new shot of a <c>DEPLOY</c> weapon starts the assault on the nearest enemy in range.</summary>
    private void DetectDeployShot(IWorldServices world)
    {
        if (Entity.LastFireTick < 0 || Entity.LastFireTick == _lastShot) return;
        _lastShot = Entity.LastFireTick;
        if (SimulationWorld.WeaponIndexIn(Entity, Entity.CurrentWeaponSlot) < 0) return;
        var weapon = world.Weapons[SimulationWorld.WeaponIndexIn(Entity, Entity.CurrentWeaponSlot)];
        if (weapon.DamageType != DeployDamage) return;
        var range = weapon.RangeWith(world.Templates.GlobalWeaponBonus, Entity.WeaponBonusConditions);
        Entity? nearest = null;
        var limit = SquaredReach.Of(range);
        var best = Fixed.Zero;
        foreach (var candidate in world.Entities)
        {
            if (candidate.EffectivelyDead || candidate.DestroyRequested || candidate.Body is null || !world.AreEnemies(Entity.Owner, candidate.Owner)) continue;
            var distance = SlaveSupport.CenterDistanceSquared2D(Entity, candidate);
            if (!limit.Contains(distance) || (nearest is not null && (distance > best || (distance == best && candidate.Id.Value > nearest.Id.Value)))) continue;
            nearest = candidate;
            best = distance;
        }
        if (nearest is null) return;
        // A new attack stands in for the player's attack order (<c>aiDoCommand</c> → <c>reset</c>): everybody aboard now may deploy; later arrivals are "new" again.
        if (world.FindEntity(_designated) is not { EffectivelyDead: false, DestroyRequested: false })
        {
            for (var index = 0; index < _members.Count; index++) _members[index] = (_members[index].Id, _members[index].Healing, false);
            _newOccupantsAreNewMembers = false;
        }
        BeginAssault(nearest);
    }

    private void DriveMembers(IWorldServices world, Entity target)
    {
        for (var index = 0; index < _members.Count; index++)
        {
            var (id, healing, isNew) = _members[index];
            if (world.FindEntity(id) is not { } member || SlaveSupport.AiOf(member) is not { } ai) continue;
            var contained = member.ContainedBy.IsValid;
            if (contained && IsHealthy(member) && !isNew)
            {
                // aiExit: the healthy member leaves the transport and starts for the target.
                Entity.Container!.Remove(world, member, true);
                world.Emit(SimEventKind.EntityUncontained, member.Id, Entity.Id, string.Empty, 0);
                _members[index] = (id, false, isNew);
                ai.OrderMove(world, target.X, target.Y, false);
            }
            else if (contained)
            {
                if (healing) _members[index] = (id, false, isNew);
            }
            else if (IsWounded(member))
            {
                // "Order wounded members back to get healed" — once, not every tick (the reference asks the AI state; the flag stands in for it).
                if (healing) continue;
                ai.OrderEnter(world, Entity, false);
                _members[index] = (id, true, isNew);
            }
            else
            {
                if (healing) _members[index] = (id, false, isNew);
                if (!member.HasMoveTarget && SlaveSupport.CenterDistanceSquared2D(member, target) > Fixed.FromInt(4)) ai.OrderMove(world, target.X, target.Y, false);
            }
        }
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.UInt("designated", _designated.Value);
        writer.Bool("newOccupants", _newOccupantsAreNewMembers);
        writer.Int("lastShot", _lastShot);
        writer.BeginArray("members");
        foreach (var (id, healing, isNew) in _members)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.Bool("healing", healing);
            writer.Bool("isNew", isNew);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _designated = new EntityId(checked((uint)reader.UInt("designated")));
        _newOccupantsAreNewMembers = reader.Bool("newOccupants");
        _lastShot = reader.Int("lastShot");
        _members.Clear();
        foreach (var entry in reader.Array("members")) _members.Add((new EntityId(checked((uint)entry.UInt("id"))), entry.Bool("healing"), entry.Bool("isNew")));
    }
}
