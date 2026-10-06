namespace CnCNew.Presentation;

/// <summary>
/// The parts of the game layer's work around one simulation tick and one rendered frame, named as the code names them (<c>LiveGame.Advance</c>,
/// <c>LiveGame.SyncAfterTick</c>, <c>FrameProjector.Advance</c>). Phases nest: <see cref="SyncAfterTick"/> contains the phases after it up to
/// <see cref="FogUpdate"/>, <see cref="ProjectorAdvance"/> contains the four <c>ProjectorAdvance…</c> parts.
/// </summary>
public enum PresentationPhase
{
    /// <summary>The <c>BeforeTick</c> callback (the HUD's per-tick input bookkeeping; empty without a HUD).</summary>
    BeforeTick,
    /// <summary><c>StepSimulation</c>: the session step (AI or scripts plus the world step).</summary>
    StepSimulation,
    /// <summary>All of <c>SyncAfterTick</c>.</summary>
    SyncAfterTick,
    /// <summary><c>FrameProjector.Observe</c>: the tick's events for the unit conditions and the selection flash.</summary>
    ProjectorObserve,
    /// <summary>All of <c>FrameProjector.Advance</c>.</summary>
    ProjectorAdvance,
    /// <summary><c>SimulationWorld.EntitiesVisibleTo</c> (or all entities without a viewer).</summary>
    ProjectorAdvanceVisibleEntities,
    /// <summary>The capture of every visible entity (conditions, state selection, turrets, suspension).</summary>
    ProjectorAdvanceCapture,
    /// <summary><c>UpdateGhosts</c>: remembered enemy structures.</summary>
    ProjectorAdvanceGhosts,
    /// <summary><c>UpdateSceneryExploration</c>: which scenery the viewer has explored and which is in the fog.</summary>
    ProjectorAdvanceScenery,
    /// <summary><c>SimulationFxWorldView.Refresh</c>: the pose of every entity for the effects.</summary>
    FxViewRefresh,
    /// <summary><c>FxExecutor.ExecuteEvents</c>.</summary>
    FxExecuteEvents,
    /// <summary><c>FxExecutor.Update</c>: particles, tracers, lights, decals.</summary>
    FxUpdate,
    /// <summary><c>FxExecutor.DrainSoundCues</c> into the game's cue list.</summary>
    DrainSoundCues,
    /// <summary><c>FogOfWarGrid.Update</c>, every <c>SimulationWorld.VisionInterval</c> ticks.</summary>
    FogUpdate,
    /// <summary>The <c>TickStepped</c> handlers (statistics, ticker, sound; empty without a HUD).</summary>
    TickStepped,
    /// <summary><c>FogOfWarGrid.Advance</c>, once per frame.</summary>
    FogAdvance,
    /// <summary><c>OnAdvanced</c>, once per frame (a mission's scripted camera).</summary>
    OnAdvanced,
    /// <summary><c>FrameProjector.Project</c>, once per frame.</summary>
    Project,
}

/// <summary>
/// Told where the phases of the game layer begin and end, so that a measuring tool can read a clock there (the presentation itself reads none). Absent in
/// the game; a call costs one null test per phase. It observes only: nothing it does may change what the presentation produces.
/// </summary>
public interface IPresentationPhaseObserver
{
    void PhaseStarted(PresentationPhase phase);
    void PhaseEnded(PresentationPhase phase);
}
