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
/// within the same call, up to 20 levels deep. The approach and, at a dock whose model has a <c>DockStart</c> bone, the moves into, at and out of the dock
/// (<c>DockStart</c>, <c>DockAction</c>, <c>DockEnd</c>) are internal moves that end when the docker arrives; at a boneless dock the docker is "fine where it is" and those three states
/// answer success when they are entered (a flyer is recentred over the dock on its way in). There is no rally point after a dock. The reference's machine lock during the dock
/// move is not modelled (nothing sets a state from outside).
/// </summary>
internal sealed class DockMachine(EntityId dockId)
{
    private const int MaxTransitionDepth = 20;
    private const long ClearanceTimeoutFrames = 30L * SimulationRate.OriginalFramesPerSecond;

    private DockState _state;
    private int _approachPosition = -1;
    private long _enterTick;
    private long _nextActionTick;

    public EntityId DockId { get; } = dockId;

    /// <summary>
    /// The docker is between the dock's entry and exit (<c>DockUpdate::onEnterReached</c> .. <c>onExitReached</c>), where the reference sets <c>MODELCONDITION_DOCKING</c> on dock and docker:
    /// not during the approach, the wait for clearance or the move to the rally point.
    /// </summary>
    public bool Inside => _state is DockState.MoveToDock or DockState.ProcessDock or DockState.MoveToExit;

    /// <summary>
    /// The docker has reached the dock point and the dock works on it (<c>DockUpdate::onDockReached</c> .. <c>onExitReached</c>, where the reference sets <c>MODELCONDITION_DOCKING_ACTIVE</c>): <see cref="DockState.ProcessDock"/>.
    /// The entry, dock and exit points coincide here, so <see cref="DockState.MoveToDock"/> and <see cref="DockState.MoveToExit"/> pass within one tick and the stretch before the dock point
    /// (<c>MODELCONDITION_DOCKING_BEGINNING</c>) has no duration that could be observed.
    /// </summary>
    public bool Active => _state == DockState.ProcessDock;

    /// <summary>
    /// <c>ignoreObstacle(dock)</c> of the moves into, at and out of a dock that <c>AllowsPassthrough</c>: the docker does not steer around the dock on that way; the approach ignores nothing
    /// (<c>ignoreObstacle(NULL)</c>).
    /// </summary>
    public bool IgnoresDock => _ignoresDock && _state is DockState.MoveToEntry or DockState.MoveToDock or DockState.ProcessDock or DockState.MoveToExit;

    private bool _ignoresDock;

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
                _ignoresDock = false;
                // AIDockApproachState is an AIInternalMoveToState that adjusts its destination: a hovering docker waits in a cell no other flyer holds, not in the one at the dock.
                world.AdjustAircraftDestination(ai.Entity, ref x, ref y);
                return ai.StartInternalMove(x, y) ? DockStatus.Continue : DockStatus.Failure;
            case DockState.AdvancePosition:
                if (dock is not null && !dock.IsOpen) return RefuseClosed(dock, ai);
                if (dock is null || !dock.AdvanceApproachPosition(world, ai.Entity, ref _approachPosition, out x, out y)) return DockStatus.Failure;
                _ignoresDock = false;
                world.AdjustAircraftDestination(ai.Entity, ref x, ref y);
                return ai.StartInternalMove(x, y) ? DockStatus.Continue : DockStatus.Failure;
            case DockState.WaitForClearance:
                _enterTick = world.Tick;
                return DockStatus.Continue;
            case DockState.MoveToEntry:
                if (dock is null) return DockStatus.Failure;
                if (!dock.IsOpen) return RefuseClosed(dock, ai);
                _ignoresDock = dock.AllowsPassthrough;
                _approachPosition = -1;
                return StartDockMove(world, ai, dock, DockPathStage.Enter, adjust: true);
            case DockState.MoveToDock:
                if (dock is null) return DockStatus.Failure;
                return dock.IsOpen ? StartDockMove(world, ai, dock, DockPathStage.Dock, adjust: !dock.AllowsPassthrough) : RefuseClosed(dock, ai);
            case DockState.MoveToExit:
                return dock is null ? DockStatus.Failure : StartDockMove(world, ai, dock, DockPathStage.Exit, adjust: !dock.AllowsPassthrough);
            case DockState.ProcessDock:
                if (dock is null) return DockStatus.Failure;
                // AIDockProcessDockState::onEnter: "it is simply okay to Action right away", which is the state's first update, in the frame after the docker reached the dock - at every
                // rate in the first tick of that frame, so that the dock's own frames (a repair dock heals once per frame from its first action) and the move out after its last action
                // lie on the original's frames.
                _nextActionTick = world.Tick + world.Time.ZeroIsNextFrame(ActionDelay(world, dock, ai));
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
            case DockState.MoveToEntry:
            case DockState.MoveToExit:
                if (dock is null) return DockStatus.Failure;
                return ai.Entity.HasMoveTarget ? DockStatus.Continue : DockStatus.Success;
            case DockState.MoveToDock:
                // AIDockMoveToDockState::update fails when the dock closes; its exit then tells the dock goodbye.
                if (dock is null || !dock.IsOpen) return DockStatus.Failure;
                return ai.Entity.HasMoveTarget ? DockStatus.Continue : DockStatus.Success;
            case DockState.WaitForClearance:
                if (dock is null) return DockStatus.Failure;
                if (!dock.IsOpen) return RefuseClosed(dock, ai);
                if (dock.IsClearToEnter(world, ai.Entity.Id)) return DockStatus.Success;
                return _enterTick + world.Time.OriginalFrames(ClearanceTimeoutFrames) < world.Tick ? DockStatus.Failure : DockStatus.Continue;
            case DockState.ProcessDock:
                // The machine runs every tick while the dock works on its docker, but its states change once per frame (F-79): a dock that is gone - destroyed in a phase after
                // the updates of a frame's first tick - is seen in the next frame, as at 30 Hz, not in that frame's later ticks.
                if (dock is null) return world.Time.IsOriginalFrameBoundary(world.Tick) ? DockStatus.Failure : DockStatus.Continue;
                if (world.Tick < _nextActionTick) return DockStatus.Continue;
                _nextActionTick = world.Tick + ActionDelay(world, dock, ai);
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
                StopDockMove(ai);
                break;
            case DockState.MoveToDock:
                if (dock is not null && (reset || !dock.IsOpen)) dock.CancelDock(id);
                StopDockMove(ai);
                break;
            case DockState.MoveToExit:
                dock?.OnExitReached(id);
                StopDockMove(ai);
                break;
        }
    }

    /// <summary>
    /// The <c>AIInternalMoveToState</c> of the moves into, at and out of the dock, to the dock's point for the stage. A boneless dock has none, and the docker that is fine where it is
    /// passes the state at once. The way in adjusts its destination (an aircraft takes a cell no other flyer holds), the dock and exit points only at a dock without passthrough.
    /// </summary>
    private static DockStatus StartDockMove(IWorldServices world, AIUpdateInterface ai, IDockModule dock, DockPathStage stage, bool adjust)
    {
        if (dock.PathPoint(world, ai.Entity, stage) is not var (x, y)) return DockStatus.Success;
        if (adjust) world.AdjustAircraftDestination(ai.Entity, ref x, ref y);
        return ai.StartInternalMove(x, y) ? DockStatus.Continue : DockStatus.Failure;
    }

    /// <summary><c>AIInternalMoveToState::onExit</c> of a dock move that was cut short; a docker that arrived, or stood where it was, has nothing to stop.</summary>
    private static void StopDockMove(AIUpdateInterface ai)
    {
        if (ai.Entity.HasMoveTarget) ai.StopInternalMove();
    }

    /// <summary>"Fail if the dock is closed": the docker tells the dock it is not coming and the state fails.</summary>
    private static DockStatus RefuseClosed(IDockModule dock, AIUpdateInterface ai)
    {
        dock.CancelDock(ai.Entity.Id);
        return DockStatus.Failure;
    }

    /// <summary>
    /// The wait before the dock's next action. The supply docks act once per call and rely on the carrier's delay; a docker whose docking AI is not its supply module (an object
    /// with two AI modules: the Chinook of the air force general) has none and is served "at the next update", which is one logic frame of the original later at every rate.
    /// The other docks are called every tick and keep their own frame (a repair dock heals once per frame).
    /// </summary>
    private static long ActionDelay(IWorldServices world, IDockModule dock, AIUpdateInterface ai)
    {
        var delay = ai is ISupplyCarrier carrier ? carrier.ActionDelayFor(dock) : 0;
        return dock is SupplyWarehouseDockUpdate or SupplyCenterDockUpdate ? world.Time.ZeroIsNextFrame(delay) : delay;
    }

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("dockMachine");
        writer.UInt("dock", DockId.Value);
        writer.Int("state", (int)_state);
        writer.Int("approachPosition", _approachPosition);
        writer.Int("enterTick", _enterTick);
        writer.Int("nextActionTick", _nextActionTick);
        // Written only when set, so a docker of a dock without passthrough keeps its serialized form.
        if (_ignoresDock) writer.Bool("ignoresDock", true);
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
            _ignoresDock = node.Has("ignoresDock") && node.Bool("ignoresDock"),
        };
    }
}
