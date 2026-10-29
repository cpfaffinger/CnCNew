using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>Supply-gathering AI modules (<c>SupplyTruckAIUpdate</c>, <c>WorkerAIUpdate</c>) and the dozer stub; see docs/GAME-RULES.md 6e.</summary>
internal static class SupplyModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SupplyCarrierData>("SupplyTruckAIUpdate", ModuleOwnerKind.Object, (configuration, _) => SupplyCarrierData.Parse(configuration), data => new SupplyTruckAIUpdate(data)),
        new ModuleBinding<SupplyCarrierData>("WorkerAIUpdate", ModuleOwnerKind.Object, (configuration, _) => SupplyCarrierData.Parse(configuration), data => new WorkerAIUpdate(data)),
        new ModuleBinding<DozerAIData>("DozerAIUpdate", ModuleOwnerKind.Object, (configuration, _) => DozerAIData.Parse(configuration), data => new DozerAIUpdate(data)),
    ];
}

/// <summary>What a dock and the supply network need from a supply-carrying unit (<c>SupplyTruckAIInterface</c>).</summary>
internal interface ISupplyCarrier
{
    int Boxes { get; }
    Fixed WarehouseScanDistance { get; }

    /// <summary>The dock a player order last named (<c>m_preferredDock</c>); one memory for warehouse and centre.</summary>
    EntityId PreferredDock { get; }

    /// <summary><c>gainOneBox</c>: false when the carrier is full.</summary>
    bool GainOneBox(int remainingInWarehouse);

    /// <summary><c>loseOneBox</c>: false when the carrier is empty.</summary>
    bool LoseOneBox();

    /// <summary><c>getUpgradedSupplyBoost</c>: extra money per deposit.</summary>
    long UpgradedSupplyBoost(IWorldServices world);

    /// <summary><c>getMaxBoxes</c>.</summary>
    int MaxBoxes { get; }

    /// <summary><c>getActionDelayForDock</c>.</summary>
    long ActionDelayFor(IDockModule dock);

    /// <summary>Spawn behaviour of a supply-centre produced unit: go to work at once instead of idling.</summary>
    void ForceWanting(IWorldServices world);
}

public sealed record SupplyCarrierData(AIUpdateData Ai, int MaxBoxes, long CenterDelayTicks, long WarehouseDelayTicks, Fixed ScanDistance, int UpgradedSupplyBoost) : ModuleData
{
    public static SupplyCarrierData Parse(RatedModuleConfiguration configuration) => new(
        AIUpdateData.Parse(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "MaxBoxes", 0)),
        ModuleConfigReader.Ticks(configuration, "SupplyCenterActionDelay", 0),
        ModuleConfigReader.Ticks(configuration, "SupplyWarehouseActionDelay", 0),
        ModuleConfigReader.Real(configuration, "SupplyWarehouseScanDistance", Fixed.FromInt(100)),
        checked((int)ModuleConfigReader.Int(configuration, "UpgradedSupplyBoost", 0)));
}

/// <summary><c>DozerAIUpdateModuleData</c>: the AI's fields, <c>BoredTime</c> (parsed as a duration) and <c>BoredRange</c>.</summary>
public sealed record DozerAIData(AIUpdateData Ai, long BoredTicks, Fixed BoredRange) : ModuleData
{
    public static DozerAIData Parse(RatedModuleConfiguration configuration) => new(AIUpdateData.Parse(configuration),
        ModuleConfigReader.Ticks(configuration, "BoredTime", 0), ModuleConfigReader.Real(configuration, "BoredRange", Fixed.Zero));
}

/// <summary>
/// <c>DozerAIUpdate</c>: builds and repairs structures (see <see cref="AIUpdateInterface.OrderConstruct"/>), and gets bored (F-67, <c>DozerPrimaryIdleState::update</c>): a dozer that has
/// stood idle without a task for more than <c>BoredTime</c> repairs the closest structure of its player within <c>BoredRange</c> that it may repair (<c>findObjectToRepair</c>), and
/// looks again every <c>BoredTime</c> while it stays idle. Mine clearing (the other half of boredom) is not modelled.
/// </summary>
public sealed class DozerAIUpdate(DozerAIData data) : AIUpdateInterface(data.Ai)
{
    // m_idleTooLongTimestamp: the last tick of the dozer's own frame in which it was not idle, had a task, or was bored. The idle state is entered at the dozer's creation.
    private long _boredSince;

    public override string ModuleName => "DozerAIUpdate";
    public override bool CanBuild => true;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        _boredSince = world.Tick + world.Time.TicksPerOriginalFrame - TickOrder.Next;
        NextWake = _boredSince + data.BoredTicks + world.Time.TicksPerOriginalFrame;
    }

    /// <summary>
    /// The base's update, then the dozer machine's idle state, as in the original after <c>AIUpdateInterface::update</c>. The state is read where the dozer's frame ends - a move
    /// that arrives in a frame has arrived for the 30 Hz frame, which is read once the whole step is made.
    /// </summary>
    public override long Update(IWorldServices world)
    {
        var hadTask = HasBuilderTask;
        var sleep = base.Update(world);
        if (Entity.EffectivelyDead) return sleep;
        var frame = world.Time.TicksPerOriginalFrame;
        if (world.Time.BeginsFrameOf(Entity, world.Tick + TickOrder.Next))
        {
            // A task that ended in this frame enters the idle state now (its onEnter sets the stamp); a dozer that is not idle resets it.
            if (hadTask || HasBuilderTask || !IsIdle) _boredSince = world.Tick;
            else if (world.Tick - _boredSince >= data.BoredTicks + frame)
            {
                _boredSince = world.Tick;
                if (FindObjectToRepair(world) is { } target) OrderRepair(world, target, fromPlayer: false);
            }
        }
        if (HasBuilderTask || !IsIdle)
        {
            // Something the world drives (a guard) keeps it from being idle without waking it: look again where the next frame ends.
            if (sleep < SimulationTiming.SleepForever) return sleep;
            for (var ahead = TickOrder.Next; ; ahead++)
                if (world.Time.BeginsFrameOf(Entity, world.Tick + ahead + TickOrder.Next)) return ahead;
        }
        // The next look lies after this tick: the stamp is the last frame end that was looked at, and a look is due a whole frame after it at the earliest.
        return Math.Min(sleep, _boredSince + data.BoredTicks + frame - world.Tick);
    }

    /// <summary>
    /// <c>findObjectToRepair</c>: the closest structure of the dozer's player within <c>BoredRange</c> (between the centres, on the ground; ties by id) that
    /// <c>ActionManager::canRepairObject</c> lets it repair.
    /// </summary>
    private Entity? FindObjectToRepair(IWorldServices world)
    {
        Entity? best = null;
        var bestDistance = Fixed.Zero;
        var reach = SquaredReach.Of(data.BoredRange);
        foreach (var candidate in world.EntitiesOwnedBy(Entity.Owner).AsSpan())
        {
            if (candidate == Entity || !IsRepairable(candidate)) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            var distance = dx * dx + dy * dy;
            if (!reach.Contains(distance)) continue;
            if (best is null || distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Int("boredSince", _boredSince);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _boredSince = reader.Has("boredSince") ? reader.Int("boredSince") : 0;
    }
}

internal enum SupplyState { Busy, Idle, Wanting, Regrouping, Docking }

/// <summary>
/// <c>SupplyTruckStateMachine</c> (<c>SupplyTruckAIUpdate.cpp</c>) with the <c>StateMachine</c> transition semantics of <see cref="DockMachine"/>.
/// The reference's <c>forcedBusy</c> condition (set by a player stop command, which does not exist here) is omitted.
/// </summary>
internal sealed class SupplyMachine
{
    private const int MaxTransitionDepth = 20;
    private const long RegroupNearSquared = 15 * 15;

    private SupplyState _state = SupplyState.Busy;
    private bool _forceWanting;

    public SupplyState State => _state;
    public bool Sleeps => _state == SupplyState.Idle && !_forceWanting;

    public void SetForceWanting() => _forceWanting = true;

    public void Update(IWorldServices world, SupplyCarrierAI ai)
    {
        var status = UpdateState(world, ai);
        Transition(status, 0, world, ai);
    }

    private DockStatus Transition(DockStatus status, int depth, IWorldServices world, SupplyCarrierAI ai)
    {
        if (status == DockStatus.Continue)
        {
            var next = Condition(ai);
            return next is { } target ? SetState(target, depth, world, ai) : DockStatus.Continue;
        }
        var success = status == DockStatus.Success;
        var to = _state switch
        {
            SupplyState.Wanting => success ? SupplyState.Busy : SupplyState.Regrouping,
            SupplyState.Regrouping => success ? SupplyState.Wanting : SupplyState.Busy,
            _ => SupplyState.Busy,
        };
        return SetState(to, depth, world, ai);
    }

    private DockStatus SetState(SupplyState target, int depth, IWorldServices world, SupplyCarrierAI ai)
    {
        if (depth >= MaxTransitionDepth) return DockStatus.Failure;
        _state = target;
        var status = EnterState(world, ai);
        return Transition(status, depth + 1, world, ai);
    }

    private SupplyState? Condition(SupplyCarrierAI ai)
    {
        var idle = ai.IsIdle;
        var docking = ai.IsDocking;
        var notDockingOrIdle = !idle && !docking;
        switch (_state)
        {
            case SupplyState.Busy:
                if (idle) return SupplyState.Idle;
                if (docking) return SupplyState.Docking;
                break;
            case SupplyState.Idle:
                if (_forceWanting) return SupplyState.Wanting;
                if (docking) return SupplyState.Docking;
                if (notDockingOrIdle) return SupplyState.Busy;
                break;
            case SupplyState.Wanting:
                if (docking) return SupplyState.Docking;
                if (notDockingOrIdle) return SupplyState.Busy;
                break;
            case SupplyState.Regrouping:
                if (ai.LastCommandFromPlayer) return SupplyState.Busy;
                break;
            case SupplyState.Docking:
                if (idle) return SupplyState.Wanting;
                if (notDockingOrIdle) return SupplyState.Busy;
                break;
        }
        return null;
    }

    private DockStatus EnterState(IWorldServices world, SupplyCarrierAI ai)
    {
        switch (_state)
        {
            case SupplyState.Wanting:
            case SupplyState.Docking:
                _forceWanting = false;
                return DockStatus.Continue;
            case SupplyState.Regrouping:
                return EnterRegroup(world, ai);
            default:
                return DockStatus.Continue;
        }
    }

    private DockStatus UpdateState(IWorldServices world, SupplyCarrierAI ai)
    {
        switch (_state)
        {
            case SupplyState.Wanting:
                var dock = ai.Boxes > 0 ? SupplyNetwork.FindBestCenter(world, ai) : SupplyNetwork.FindBestWarehouse(world, ai);
                if (dock is null) return DockStatus.Failure;
                ai.OrderDock(world, dock, false);
                return DockStatus.Success;
            case SupplyState.Regrouping:
                return ai.IsIdle ? DockStatus.Success : DockStatus.Continue;
            default:
                return DockStatus.Continue;
        }
    }

    /// <summary><c>SupplyTruckRegroupingState::onEnter</c>: walk back to the closest own cash generator, else command centre, else any structure.</summary>
    private static DockStatus EnterRegroup(IWorldServices world, SupplyCarrierAI ai)
    {
        var self = ai.Entity;
        var goal = Closest(world, self, "CASH_GENERATOR") ?? Closest(world, self, "COMMANDCENTER") ?? Closest(world, self, "STRUCTURE");
        if (goal is null) return DockStatus.Failure;
        var dx = goal.X - self.X;
        var dy = goal.Y - self.Y;
        var gap = Fixed.Sqrt(dx * dx + dy * dy) - goal.Template.GeometryMajorRadius - self.Template.GeometryMajorRadius;
        // gap² >= 15²: the square of the gap holds the constant.
        if (gap > Fixed.Zero && SquaredReach.Of(gap).Contains(Fixed.FromInt(RegroupNearSquared))) ai.OrderMove(world, goal.X, goal.Y, false);
        return DockStatus.Continue;
    }

    private static Entity? Closest(IWorldServices world, Entity self, string kind)
    {
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        // The objects of the kind (the world's kind list, in id order like the scan over every object it replaces).
        foreach (var candidate in world.EntitiesOfKind(kind).AsSpan())
        {
            if (candidate.Owner != self.Owner || candidate.EffectivelyDead || candidate.DestroyRequested) continue;
            var dx = candidate.X - self.X;
            var dy = candidate.Y - self.Y;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance == bestDistance && best is not null && candidate.Id.Value < best.Id.Value))
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    public void Describe(IStateWriter writer)
    {
        writer.Int("supplyState", (int)_state);
        writer.Bool("forceWanting", _forceWanting);
    }

    public void Load(IStateReader reader)
    {
        var state = reader.Int("supplyState");
        if (state < 0 || state > (int)SupplyState.Docking) throw new InvalidDataException("Supply machine state out of range.");
        _state = (SupplyState)state;
        _forceWanting = reader.Bool("forceWanting");
    }
}

/// <summary>
/// <c>SupplyTruckAIUpdate::update</c>: the supply machine runs before the base AI, and the sleep is the shorter of the two. An idle truck
/// (machine <see cref="SupplyState.Idle"/>, nothing moving) sleeps until an order or a spawn hook wakes it.
/// </summary>
internal abstract class SupplyCarrierAI(SupplyCarrierData data) : AIUpdateInterface(data.Ai), ISupplyCarrier
{
    private readonly SupplyMachine _machine = new();
    private int _boxes;
    private EntityId _preferredDock;

    public int Boxes => _boxes;

    /// <summary>The supply machine is idle (<c>SupplyTruckAIUpdate::isIdle</c>'s half of the question).</summary>
    protected bool SupplyIdle => _machine.Sleeps;
    public int MaxBoxes => data.MaxBoxes;
    public Fixed WarehouseScanDistance => data.ScanDistance;
    public EntityId PreferredDock => _preferredDock;

    public bool GainOneBox(int remainingInWarehouse)
    {
        if (_boxes >= data.MaxBoxes) return false;
        _boxes++;
        return true;
    }

    public bool LoseOneBox()
    {
        if (_boxes <= 0) return false;
        _boxes--;
        return true;
    }

    public abstract long UpgradedSupplyBoost(IWorldServices world);

    public long ActionDelayFor(IDockModule dock) => dock switch
    {
        SupplyWarehouseDockUpdate => data.WarehouseDelayTicks,
        SupplyCenterDockUpdate => data.CenterDelayTicks,
        _ => 0,
    };

    public void ForceWanting(IWorldServices world)
    {
        _machine.SetForceWanting();
        world.WakeAt(Entity, this, world.Tick);
    }

    protected int BoostIfUpgraded(IWorldServices world, string upgrade)
        => data.UpgradedSupplyBoost > 0 && world.Upgrades.TryGet(upgrade, out var info) && world.PlayerUpgrades(Entity.Owner).Has(info.Index) ? data.UpgradedSupplyBoost : 0;

    public override void OrderDock(IWorldServices world, Entity dock, bool fromPlayer)
    {
        if (fromPlayer) _preferredDock = dock.Id;
        base.OrderDock(world, dock, fromPlayer);
    }

    protected override void OnBuilderTaskEnded(IWorldServices world) => _machine.SetForceWanting();

    public override long Update(IWorldServices world)
    {
        // Like the dock machine (AIUpdateInterface.Update): one step of the supply machine per logic frame of the original.
        if (!IsBuilding && world.Time.IsOriginalFrameBoundary(world.Tick)) _machine.Update(world, this);
        var baseSleep = base.Update(world);
        return _machine.Sleeps ? baseSleep : Math.Min(baseSleep, SimulationTiming.SleepNone);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Int("boxes", _boxes);
        writer.UInt("preferredDock", _preferredDock.Value);
        _machine.Describe(writer);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _boxes = checked((int)reader.Int("boxes"));
        _preferredDock = new EntityId(checked((uint)reader.UInt("preferredDock")));
        _machine.Load(reader);
    }
}

/// <summary><c>SupplyTruckAIUpdate</c>: the truck has no upgrade boost (<c>getUpgradedSupplyBoost</c> returns 0).</summary>
internal sealed class SupplyTruckAIUpdate(SupplyCarrierData data) : SupplyCarrierAI(data)
{
    public override string ModuleName => "SupplyTruckAIUpdate";
    public override long UpgradedSupplyBoost(IWorldServices world) => 0;
}

/// <summary>
/// <c>WorkerAIUpdate</c>: a dozer that also gathers. The reference switches between its dozer and supply brain by the object's task; here
/// the worker always runs the supply brain (construction is not modelled). The boost applies once the player owns <c>Upgrade_GLAWorkerShoes</c>.
/// </summary>
internal sealed class WorkerAIUpdate(SupplyCarrierData data) : SupplyCarrierAI(data)
{
    public override string ModuleName => "WorkerAIUpdate";
    public override bool CanBuild => true;
    public override long UpgradedSupplyBoost(IWorldServices world) => BoostIfUpgraded(world, "Upgrade_GLAWorkerShoes");
}

/// <summary>
/// <c>ResourceGatheringManager</c>: every warehouse and centre is visible to every player; the truck picks by squared 3D distance.
/// Candidates are scanned in entity id order so ties break deterministically (the reference's list order is registration order).
/// </summary>
internal static class SupplyNetwork
{
    public static ISupplyCarrier? CarrierOf(Entity entity)
    {
        foreach (var module in entity.ModuleArray)
            if (module is ISupplyCarrier carrier) return carrier;
        return null;
    }

    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");

    /// <summary><c>canTransfer</c> plus <c>isClearToApproach</c>; the squared 3D distance, or null when the dock is unusable.</summary>
    private static Fixed? Cost(Entity truck, ISupplyCarrier carrier, IWorldServices world, Entity dest, bool warehouse)
    {
        if (dest.EffectivelyDead || dest.DestroyRequested || dest.HasStatus(ObjectStatusBits.UnderConstruction) || dest.HasStatus(SoldBit)) return null;
        var dock = DockModules.DockOf(dest);
        if (dock is null || (warehouse ? dock is not SupplyWarehouseDockUpdate : dock is not SupplyCenterDockUpdate)) return null;
        if (warehouse)
        {
            if (((SupplyWarehouseDockUpdate)dock).Boxes == 0 || world.AreEnemies(truck.Owner, dest.Owner)) return null;
        }
        else if (carrier.Boxes == 0 || truck.Owner != dest.Owner) return null;
        if (!dock.IsClearToApproach(truck.Id)) return null;
        var dx = dest.X - truck.X;
        var dy = dest.Y - truck.Y;
        var dz = dest.Z - truck.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    /// <summary><c>findBestSupplyWarehouse</c>: the remembered dock wins unconditionally, else the closest one inside the scan distance.</summary>
    public static Entity? FindBestWarehouse(IWorldServices world, SupplyCarrierAI truck)
    {
        var self = truck.Entity;
        if (truck.PreferredDock.IsValid && world.FindEntity(truck.PreferredDock) is { } preferred && Cost(self, truck, world, preferred, true) is not null) return preferred;
        Entity? best = null;
        var limit = SquaredReach.Of(truck.WarehouseScanDistance);
        var bestCost = Fixed.MaxValue;
        // Only an object with a dock module has a cost (Cost asks DockOf first), so the objects with one are all the candidates there are; the pick is the
        // minimum of (cost, id) and does not depend on their order.
        foreach (var candidate in world.EntitiesWithModule<IDockModule>().AsSpan())
        {
            if (Cost(self, truck, world, candidate, true) is not { } cost || !limit.ContainsStrictly(cost)) continue;
            if (best is null || cost < bestCost || (cost == bestCost && candidate.Id.Value < best.Id.Value))
            {
                best = candidate;
                bestCost = cost;
            }
        }
        return best;
    }

    /// <summary><c>findBestSupplyCenter</c>: the remembered dock when it is a centre, else the closest one (no distance limit).</summary>
    public static Entity? FindBestCenter(IWorldServices world, SupplyCarrierAI truck)
    {
        var self = truck.Entity;
        if (truck.PreferredDock.IsValid && world.FindEntity(truck.PreferredDock) is { } preferred && Cost(self, truck, world, preferred, false) is not null) return preferred;
        Entity? best = null;
        Fixed bestCost = Fixed.MaxValue;
        foreach (var candidate in world.EntitiesWithModule<IDockModule>().AsSpan())
        {
            if (Cost(self, truck, world, candidate, false) is not { } cost) continue;
            if (best is null || cost < bestCost || (cost == bestCost && candidate.Id.Value < best.Id.Value))
            {
                best = candidate;
                bestCost = cost;
            }
        }
        return best;
    }
}
