using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary><c>StateReturnType</c> of the dock machine; the exit targets (<c>EXIT_MACHINE_WITH_SUCCESS/FAILURE</c>) surface as <see cref="Success"/>/<see cref="Failure"/>.</summary>
internal enum DockStatus { Continue, Success, Failure }

/// <summary>The states of <c>AIDockMachine</c>; <see cref="None"/> is the machine after it exited.</summary>
internal enum DockState { None, Approach, WaitForClearance, AdvancePosition, MoveToEntry, MoveToDock, ProcessDock, MoveToExit, MoveToRally }

/// <summary>
/// <c>AIDockMachine</c> (<c>AIDock.cpp</c>) on top of the <c>StateMachine</c> transition semantics (<c>StateMachine.cpp</c>:
/// <c>internalSetState</c>/<c>friend_checkForTransitions</c>): entering a state that answers success or failure at once transitions on
/// within the same call, up to 20 levels deep. The dock positions carry no geometry (no bones, no terrain): entry, dock, exit and rally
/// are the docker's own position, so those four states answer success when they are entered; the approach position is the only real move.
/// The reference's machine lock during the dock move is not modelled (nothing sets a state from outside).
/// </summary>
internal sealed class DockMachine(EntityId dockId)
{
    private const int MaxTransitionDepth = 20;
    private const long ClearanceTimeoutTicks = 30L * SimulationTiming.LogicFramesPerSecond;

    private DockState _state;
    private int _approachPosition = -1;
    private long _enterTick;
    private long _nextActionTick;

    public EntityId DockId { get; } = dockId;

    /// <summary><c>StateMachine::initDefaultState</c>: enters the first state and reports its raw answer, without resolving transitions.</summary>
    public DockStatus Enter(IWorldServices world, AIUpdateInterface ai)
    {
        _state = DockState.Approach;
        return EnterState(_state, world, ai);
    }

    /// <summary><c>StateMachine::updateStateMachine</c>.</summary>
    public DockStatus Update(IWorldServices world, AIUpdateInterface ai)
    {
        var status = UpdateState(_state, world, ai);
        return CheckTransitions(status, 0, world, ai);
    }

    /// <summary><c>AIDockMachine::halt</c>: tell the dock goodbye, then leave the current state as a reset.</summary>
    public void Halt(IWorldServices world, AIUpdateInterface ai)
    {
        DockOf(world)?.CancelDock(ai.Entity.Id);
        ExitState(_state, true, world, ai);
        _state = DockState.None;
    }

    private DockStatus CheckTransitions(DockStatus status, int depth, IWorldServices world, AIUpdateInterface ai)
    {
        if (_state == DockState.None) return status;
        if (status == DockStatus.Continue)
        {
            if (_state == DockState.WaitForClearance && AbleToAdvance(world, ai)) return SetState(DockState.AdvancePosition, depth, world, ai);
            return DockStatus.Continue;
        }
        var target = Target(_state, status == DockStatus.Success);
        if (target == DockState.None)
        {
            ExitState(_state, false, world, ai);
            _state = DockState.None;
            return status;
        }
        return SetState(target, depth, world, ai);
    }

    private DockStatus SetState(DockState target, int depth, IWorldServices world, AIUpdateInterface ai)
    {
        if (depth >= MaxTransitionDepth) return DockStatus.Failure;
        ExitState(_state, false, world, ai);
        _state = target;
        var status = EnterState(target, world, ai);
        return CheckTransitions(status, depth + 1, world, ai);
    }

    private static DockState Target(DockState state, bool success) => state switch
    {
        DockState.Approach => success ? DockState.WaitForClearance : DockState.None,
        DockState.WaitForClearance => success ? DockState.MoveToEntry : DockState.None,
        DockState.AdvancePosition => success ? DockState.WaitForClearance : DockState.None,
        DockState.MoveToEntry => success ? DockState.MoveToDock : DockState.MoveToExit,
        DockState.MoveToDock => success ? DockState.ProcessDock : DockState.MoveToExit,
        DockState.ProcessDock => DockState.MoveToExit,
        DockState.MoveToExit => success ? DockState.MoveToRally : DockState.None,
        _ => DockState.None,
    };

    private IDockModule? DockOf(IWorldServices world)
    {
        var entity = world.FindEntity(DockId);
        return entity is null || entity.DestroyRequested ? null : DockModules.DockOf(entity);
    }

    private bool AbleToAdvance(IWorldServices world, AIUpdateInterface ai)
        => DockOf(world) is { } dock && dock.IsClearToAdvance(ai.Entity.Id, _approachPosition);

    private DockStatus EnterState(DockState state, IWorldServices world, AIUpdateInterface ai)
    {
        var dock = DockOf(world);
        switch (state)
        {
            case DockState.Approach:
                if (dock is not null && !dock.IsOpen) return RefuseClosed(dock, ai);
                if (dock is null || !dock.ReserveApproachPosition(world, ai.Entity, out _approachPosition, out var x, out var y)) return DockStatus.Failure;
                return ai.StartInternalMove(x, y) ? DockStatus.Continue : DockStatus.Failure;
            case DockState.AdvancePosition:
                if (dock is not null && !dock.IsOpen) return RefuseClosed(dock, ai);
                if (dock is null || !dock.AdvanceApproachPosition(world, ai.Entity, ref _approachPosition, out x, out y)) return DockStatus.Failure;
                return ai.StartInternalMove(x, y) ? DockStatus.Continue : DockStatus.Failure;
            case DockState.WaitForClearance:
                _enterTick = world.Tick;
                return DockStatus.Continue;
            case DockState.MoveToEntry:
                if (dock is null) return DockStatus.Failure;
                if (!dock.IsOpen) return RefuseClosed(dock, ai);
                _approachPosition = -1;
                return DockStatus.Success;
            case DockState.MoveToDock:
                if (dock is null) return DockStatus.Failure;
                return dock.IsOpen ? DockStatus.Success : RefuseClosed(dock, ai);
            case DockState.MoveToExit:
                return dock is null ? DockStatus.Failure : DockStatus.Success;
            case DockState.ProcessDock:
                if (dock is null) return DockStatus.Failure;
                _nextActionTick = world.Tick + ActionDelay(dock, ai);
                return DockStatus.Continue;
            case DockState.MoveToRally:
                return dock is null ? DockStatus.Failure : DockStatus.Success;
            default:
                return DockStatus.Failure;
        }
    }

    private DockStatus UpdateState(DockState state, IWorldServices world, AIUpdateInterface ai)
    {
        var dock = DockOf(world);
        switch (state)
        {
            case DockState.Approach:
            case DockState.AdvancePosition:
                if (dock is null) return DockStatus.Failure;
                return ai.Entity.HasMoveTarget ? DockStatus.Continue : DockStatus.Success;
            case DockState.WaitForClearance:
                if (dock is null) return DockStatus.Failure;
                if (!dock.IsOpen) return RefuseClosed(dock, ai);
                if (dock.IsClearToEnter(world, ai.Entity.Id)) return DockStatus.Success;
                return _enterTick + ClearanceTimeoutTicks < world.Tick ? DockStatus.Failure : DockStatus.Continue;
            case DockState.ProcessDock:
                if (dock is null) return DockStatus.Failure;
                if (world.Tick < _nextActionTick) return DockStatus.Continue;
                _nextActionTick = world.Tick + ActionDelay(dock, ai);
                // "invoke the dock's action until it tells us it is done or the dock becomes closed" (AIDockProcessDockState::update)
                return dock.IsOpen && dock.Action(world, ai.Entity) ? DockStatus.Continue : DockStatus.Success;
            default:
                return dock is null ? DockStatus.Failure : DockStatus.Continue;
        }
    }

    private void ExitState(DockState state, bool reset, IWorldServices world, AIUpdateInterface ai)
    {
        var dock = DockOf(world);
        var id = ai.Entity.Id;
        switch (state)
        {
            case DockState.Approach:
            case DockState.AdvancePosition:
                if (dock is not null)
                {
                    if (reset || !dock.IsOpen) dock.CancelDock(id);
                    else dock.OnApproachReached(id);
                }
                ai.StopInternalMove();
                break;
            case DockState.WaitForClearance:
                if (dock is not null && (reset || !dock.IsOpen)) dock.CancelDock(id);
                break;
            case DockState.MoveToEntry:
                if (dock is not null)
                {
                    if (reset || !dock.IsOpen) dock.CancelDock(id);
                    else dock.OnEnterReached(id);
                }
                break;
            case DockState.MoveToDock:
                if (dock is not null && (reset || !dock.IsOpen)) dock.CancelDock(id);
                break;
            case DockState.MoveToExit:
                dock?.OnExitReached(id);
                break;
        }
    }

    /// <summary>"Fail if the dock is closed": the docker tells the dock it is not coming and the state fails.</summary>
    private static DockStatus RefuseClosed(IDockModule dock, AIUpdateInterface ai)
    {
        dock.CancelDock(ai.Entity.Id);
        return DockStatus.Failure;
    }

    private static long ActionDelay(IDockModule dock, AIUpdateInterface ai) => ai is ISupplyCarrier carrier ? carrier.ActionDelayFor(dock) : 0;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("dockMachine");
        writer.UInt("dock", DockId.Value);
        writer.Int("state", (int)_state);
        writer.Int("approachPosition", _approachPosition);
        writer.Int("enterTick", _enterTick);
        writer.Int("nextActionTick", _nextActionTick);
        writer.EndObject();
    }

    public static DockMachine Load(IStateReader reader)
    {
        var node = reader.Object("dockMachine");
        var state = node.Int("state");
        if (state < 0 || state > (int)DockState.MoveToRally) throw new InvalidDataException("Dock machine state out of range.");
        return new DockMachine(new EntityId(checked((uint)node.UInt("dock"))))
        {
            _state = (DockState)state,
            _approachPosition = checked((int)node.Int("approachPosition")),
            _enterTick = node.Int("enterTick"),
            _nextActionTick = node.Int("nextActionTick"),
        };
    }
}
