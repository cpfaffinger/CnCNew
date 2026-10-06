using CnCNew.Data.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>The parts of <see cref="ScriptEngine.Update"/>, in the order they run.</summary>
public enum ScriptSection
{
    /// <summary>Expired presentation entries, <c>PruneTeams</c> and the countdown of the timers.</summary>
    Housekeeping,
    /// <summary>Every side's scripts and groups in list order (conditions and actions are reported inside it).</summary>
    Scripts,
    Followers,
    ButtonHunts,
    TeamEvents,
    Sequences,
    TeamBuilds,
    TeamQueues,
    /// <summary>Capture orders, pending structures, traps and bridge states.</summary>
    WorldOrders,
}

/// <summary>
/// A measuring hook for tools, the script engine's counterpart of <see cref="World.ITickPhaseObserver"/>: the engine reports where the parts of its update,
/// each condition evaluation and each action execution begin and end; it holds no clock of its own. Conditions and actions nest (an action may call a
/// subroutine whose conditions are evaluated). The observer is not part of the state: neither hashed nor saved, and it must not change the engine or the
/// world. Without an observer the engine runs exactly as before.
/// </summary>
public interface IScriptObserver
{
    void SectionStarted(ScriptSection section);
    void SectionEnded(ScriptSection section);
    void ConditionStarted(MapCondition condition);
    void ConditionEnded(MapCondition condition);
    void ActionStarted(MapScriptAction action);
    void ActionEnded(MapScriptAction action);
}
