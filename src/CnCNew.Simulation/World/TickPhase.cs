using CnCNew.Simulation.Modules;

namespace CnCNew.Simulation.World;

/// <summary>
/// The sections of one logic tick, in the order <see cref="SimulationWorld.Step"/> runs them. The first fifteen are the world's own; the
/// <c>Mission…</c> and <c>Skirmish…</c> entries belong to the sessions that wrap the world step (<see cref="Mission.MissionSession.Step"/>,
/// <see cref="Skirmish.SkirmishMatchSession.Step"/>).
/// </summary>
public enum TickPhase
{
    /// <summary><c>ExecuteCommands</c>: the commands addressed to this tick.</summary>
    Commands,
    /// <summary><c>FireTimers</c>: due timers.</summary>
    Timers,
    /// <summary><c>RunUpdates</c>: the sleepy module updates; movement and its collision delivery happen in here.</summary>
    Updates,
    /// <summary><c>StepAcquisition</c>: target acquisition of idle and attacking objects.</summary>
    Acquisition,
    /// <summary><c>StepTurrets</c>.</summary>
    Turrets,
    /// <summary><c>StepFiringTrackers</c>.</summary>
    FiringTrackers,
    /// <summary>First <c>DeliverEvents</c>: everything raised so far.</summary>
    Events,
    /// <summary><c>ProcessSales</c>.</summary>
    Sales,
    /// <summary><c>ProcessDestroys</c>.</summary>
    Destroys,
    /// <summary><c>ProcessDelayedDamage</c>.</summary>
    DelayedDamage,
    /// <summary><c>ProcessSpawns</c>.</summary>
    Spawns,
    /// <summary>Second <c>DeliverEvents</c>: what destroy, delayed damage and spawn raised.</summary>
    SpawnEvents,
    /// <summary>The reveal part of <c>RefreshVision</c>; empty on ticks between two refreshes and in a world without terrain.</summary>
    Vision,
    /// <summary>The hash over the vision cells that ends <c>RefreshVision</c>; empty whenever <see cref="Vision"/> is.</summary>
    VisionHash,
    /// <summary>The tick counter advances and the incremental state hash is computed.</summary>
    Hash,
    /// <summary><c>ScriptEngine.Update</c>, before the world step of a mission.</summary>
    MissionScripts,
    /// <summary>The session's own tail after the world step: shot counting and <c>ScriptEngine.Observe</c>.</summary>
    MissionEvents,
    /// <summary>The science stand-in and every <c>SkirmishAi.Update</c>, before the world step of a skirmish match (<see cref="Skirmish.SkirmishMatchSession.Step"/>).</summary>
    SkirmishAi,
    /// <summary>The periodic victory check after the world step of a skirmish match.</summary>
    SkirmishVictory,
}

/// <summary>
/// A measuring hook for tools. The world calls it at the boundaries of its tick phases and around every module update; it holds no clock of
/// its own. The observer is not part of the state: it is neither hashed nor saved, and it must not change the world. Without an observer the
/// step runs exactly as before.
/// </summary>
public interface ITickPhaseObserver
{
    void PhaseStarted(TickPhase phase);
    void PhaseEnded(TickPhase phase);

    /// <summary>Called right before and after one module's <c>Update</c> inside <see cref="TickPhase.Updates"/>.</summary>
    void UpdateStarted(SimModule module);
    void UpdateEnded(SimModule module);
}
