using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// What the reference remembers per condition <em>instance</em> to look at it less often (<c>Condition::m_customData</c>/<c>m_customFrame</c> and the integer of a parameter, ScriptConditions.cpp),
/// as far as it changes when an answer is given, for a session of the original skirmish AI:
/// <list type="bullet">
/// <item><c>evaluateSkirmishSpecialPowerIsReady</c>: a power that was not ready is not looked at again before the frame its nearest module becomes ready, at most ten seconds later
/// (<c>pPower->friend_setInt(nextFrame)</c>).</item>
/// <item><c>evaluateSkirmishSupplySourceSafe</c>: the answer is kept for two seconds (<c>setCustomFrame(frame + 2*LOGICFRAMES_PER_SECOND)</c>).</item>
/// </list>
/// The other caches of that file (<c>getFrameObjectCountChanged</c>) only avoid recounting while nothing changed and do not alter an answer; the engine counts every time.
/// A mission keeps evaluating both conditions on every look, as it always did here: its reference values are fixed, and the memory would be new state in its snapshot.
/// The memory is state of the engine (saved and hashed), keyed by the position of the condition in the program.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary><c>evaluateSkirmishSpecialPowerIsReady</c>: "nextFrame = frame + 10 * LOGICFRAMES_PER_SECOND".</summary>
    private const long PowerRecheckFrames = 10 * SimulationRate.OriginalFramesPerSecond;
    /// <summary><c>evaluateSkirmishSupplySourceSafe</c>: "Trigger every 2*LOGICFRAMES_PER_SECOND".</summary>
    private const long SupplySafeRecheckFrames = 2 * SimulationRate.OriginalFramesPerSecond;

    /// <summary>Per script the ordinal of its first condition: the conditions of the program numbered in program order.</summary>
    private int[] _conditionBase = [];
    /// <summary>The ordinal of the condition being evaluated, or -1 outside <see cref="EvaluateConditions"/>.</summary>
    private int _conditionOrdinal = -1;
    /// <summary>Condition ordinal -> (tick the answer is valid until or the next look happens at, remembered answer: 0 none, 1 true, -1 false).</summary>
    private readonly SortedDictionary<int, (long Tick, int Answer)> _conditionMemory = [];

    private void InitializeConditionOrdinals()
    {
        _conditionBase = new int[_program.Units.Length];
        var next = 0;
        for (var index = 0; index < _program.Units.Length; index++)
        {
            _conditionBase[index] = next;
            foreach (var or in _program.Units[index].Script.OrConditions) next += or.AndConditions.IsDefault ? 0 : or.AndConditions.Length;
        }
    }

    /// <summary><c>evaluateSkirmishSpecialPowerIsReady</c> with the reference's "do not look again before" frame.</summary>
    private bool SkirmishSpecialPowerReadyRemembered(PlayerId player, string powerName)
    {
        var ordinal = _conditionOrdinal;
        if (_ai.Count == 0 || ordinal < 0) return SkirmishSpecialPowerReady(player, powerName);
        if (_conditionMemory.TryGetValue(ordinal, out var memory) && memory.Tick > _tick) return false;
        if (!_world.SpecialPowers.TryGet(powerName, out var info))
        {
            Diagnose(UnknownMessage(_unknownPowerMessages, "special power", powerName));
            return false;
        }
        var next = _tick + _world.Time.OriginalFrames(PowerRecheckFrames);
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!Counts(entity) || entity.HasStatus(UnderConstructionBit)) continue;
            if (SpecialPowerRouting.FindModule(entity, info.Index) is not { } module) continue;
            if (info.RequiredScience >= 0 && !_world.PlayerSciences(player).Has(info.RequiredScience)) continue;
            if (module.IsReady(_world)) return true;
            if (module is SpecialPowerModuleBase timed && timed.AvailableOnFrame(_world) is var ready && ready < next) next = ready;
        }
        _conditionMemory[ordinal] = (next, 0);
        return false;
    }

    /// <summary><c>evaluateSkirmishSupplySourceSafe</c> with the reference's two seconds of remembered answer.</summary>
    private bool SupplySourceSafeRemembered(PlayerId player, int minimumSupplies)
    {
        var ordinal = _conditionOrdinal;
        if (_ai.Count == 0 || ordinal < 0) return SupplySourceSafe(player, minimumSupplies);
        // "anyChanges = frame > customFrame": the remembered answer holds up to and including that frame.
        if (_conditionMemory.TryGetValue(ordinal, out var memory) && memory.Answer != 0 && _tick <= memory.Tick) return memory.Answer > 0;
        var safe = SupplySourceSafe(player, minimumSupplies);
        _conditionMemory[ordinal] = (_tick + _world.Time.OriginalFrames(SupplySafeRecheckFrames), safe ? 1 : -1);
        return safe;
    }

    private void DescribeConditionMemory(IStateWriter writer)
    {
        if (_conditionMemory.Count == 0) return;
        writer.BeginArray("aiConditionMemory");
        foreach (var (ordinal, memory) in _conditionMemory)
        {
            writer.BeginObject(null);
            writer.Int("condition", ordinal);
            writer.Int("tick", memory.Tick);
            writer.Int("answer", memory.Answer);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadConditionMemory(IStateReader reader)
    {
        _conditionMemory.Clear();
        if (!reader.Has("aiConditionMemory")) return;
        foreach (var item in reader.Array("aiConditionMemory")) _conditionMemory[checked((int)item.Int("condition"))] = (item.Int("tick"), checked((int)item.Int("answer")));
    }
}
