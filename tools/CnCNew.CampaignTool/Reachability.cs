using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Scripting;

/// <summary>
/// A static answer to "could the mission's own scripts end the mission, given what the VM implements?": for every script that executes <c>VICTORY</c>/<c>DEFEAT</c>,
/// walk the chain that has to run before it can fire — the flags, counters and timers its conditions read (each needs a script that sets them), the scripts
/// that enable it or call it, and the conditions of those scripts in turn — and report which chains touch an unimplemented condition or action type.
/// <list type="bullet">
/// <item><b>clean</b>: every script on the (best) chain uses only implemented condition and action types;</item>
/// <item><b>degraded</b>: the chain can fire, but at least one of its scripts executes an action the VM does not implement (the missing effect may or may not matter);</item>
/// <item><b>blocked</b>: some script on every chain has a condition that is not implemented (always false) or reads a flag/counter/timer nobody sets.</item>
/// </list>
/// This is an upper bound on what the engine could reach, not proof that a player can win: state conditions (teams destroyed, objects dead, areas) are leaves and
/// assumed satisfiable. It replaces "everything of the enemy was killed" as the evidence for "the mission ends on its own script path".
/// </summary>
sealed class Reachability(ScriptProgram program, ScriptDifficulty difficulty)
{
    public enum Kind { Clean, Degraded, Blocked }

    public sealed record Verdict(Kind Kind, ImmutableSortedSet<string> Blockers);

    private static readonly Verdict Unreachable = new(Kind.Blocked, ImmutableSortedSet<string>.Empty);
    private readonly Dictionary<string, List<ScriptUnit>> _flagSetters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ScriptUnit>> _counterSetters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ScriptUnit>> _timerSetters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ScriptUnit>> _enablers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Verdict> _memo = [];
    private readonly HashSet<int> _stack = [];

    public ImmutableArray<(ScriptUnit Script, string Ending, Verdict Verdict)> Endings { get; private set; } = [];

    public static Reachability Analyze(ScriptProgram program, ScriptDifficulty difficulty)
    {
        var analysis = new Reachability(program, difficulty);
        analysis.Index();
        var endings = ImmutableArray.CreateBuilder<(ScriptUnit, string, Verdict)>();
        foreach (var unit in program.Units)
        {
            if (!analysis.Allowed(unit)) continue;
            foreach (var ending in unit.Script.Actions.Concat(unit.Script.FalseActions).Select(action => action.TypeName).Where(name => name is "VICTORY" or "QUICKVICTORY" or "DEFEAT").Distinct())
                endings.Add((unit, ending == "DEFEAT" ? "defeat" : "victory", analysis.Of(unit)));
        }
        analysis.Endings = endings.ToImmutable();
        return analysis;
    }

    /// <summary>Per unimplemented action type: in how many scripts of the program it appears (statically, whether or not they ever run).</summary>
    public static IEnumerable<(string Action, int Scripts)> MissingActionUsage(ScriptProgram program)
        => program.Units.SelectMany(unit => unit.Script.Actions.Concat(unit.Script.FalseActions).Select(action => action.TypeName).Where(name => ScriptCoverage.ActionSupport(name) == ScriptSupport.Missing).Distinct())
            .GroupBy(name => name, StringComparer.Ordinal).Select(group => (group.Key, group.Count()));

    private bool Allowed(ScriptUnit unit) => difficulty switch
    {
        ScriptDifficulty.Easy => unit.Script.Easy,
        ScriptDifficulty.Hard => unit.Script.Hard,
        _ => unit.Script.Normal,
    };

    private static string Name(MapScriptAction action, int index) => index < action.Parameters.Length ? action.Parameters[index].Text : string.Empty;
    private static string Name(MapCondition condition, int index) => index < condition.Parameters.Length ? condition.Parameters[index].Text : string.Empty;

    private static void Add(Dictionary<string, List<ScriptUnit>> map, string key, ScriptUnit unit)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(unit)) list.Add(unit);
    }

    private void Index()
    {
        foreach (var unit in program.Units)
        {
            if (!Allowed(unit)) continue;
            foreach (var action in unit.Script.Actions.Concat(unit.Script.FalseActions))
            {
                switch (action.TypeName)
                {
                    case "SET_FLAG":
                        if (action.Parameters.Length > 1 && action.Parameters[1].Integer != 0) Add(_flagSetters, Name(action, 0), unit);
                        break;
                    case "SET_COUNTER": Add(_counterSetters, Name(action, 0), unit); break;
                    case "INCREMENT_COUNTER":
                    case "DECREMENT_COUNTER": Add(_counterSetters, Name(action, 1), unit); break;
                    case "SET_TIMER":
                    case "SET_RANDOM_TIMER":
                    case "SET_MILLISECOND_TIMER":
                    case "SET_RANDOM_MSEC_TIMER":
                    case "RESTART_TIMER":
                    case "ADD_TO_MSEC_TIMER":
                        Add(_timerSetters, Name(action, action.TypeName is "ADD_TO_MSEC_TIMER" ? 1 : 0), unit);
                        break;
                    case "ENABLE_SCRIPT":
                    case "CALL_SUBROUTINE": Add(_enablers, Name(action, 0), unit); break;
                }
            }
        }
    }

    private Verdict Of(ScriptUnit unit)
    {
        if (_memo.TryGetValue(unit.Index, out var known)) return known;
        if (!_stack.Add(unit.Index)) return Unreachable;
        var verdict = Compute(unit);
        _stack.Remove(unit.Index);
        // A verdict computed while an ancestor was still open depends on the order of the walk; only memoize when nothing is open (deterministic per ending).
        if (_stack.Count == 0) _memo[unit.Index] = verdict;
        return verdict;
    }

    private Verdict Compute(ScriptUnit unit)
    {
        var script = unit.Script;
        var blockers = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var action in script.Actions.Concat(script.FalseActions))
            if (ScriptCoverage.ActionSupport(action.TypeName) == ScriptSupport.Missing) blockers.Add("action " + action.TypeName);

        // Being enabled: active from the start (and its group too), or switched on / called by a reachable script.
        var group = unit.GroupIndex >= 0 ? program.Groups[unit.GroupIndex] : null;
        var startsRunning = script.IsActive && !script.IsSubroutine && (group is null || (group.Group.IsActive && !group.Group.IsSubroutine));
        if (!startsRunning)
        {
            var enablers = new List<ScriptUnit>();
            if (_enablers.TryGetValue(script.Name, out var byScript)) enablers.AddRange(byScript);
            if (group is not null && _enablers.TryGetValue(group.Name, out var byGroup)) enablers.AddRange(byGroup);
            var best = Best(enablers.Where(enabler => enabler != unit));
            if (best is null) return Unreachable;
            foreach (var blocker in best.Blockers) blockers.Add(blocker);
        }

        // Conditions: an OR of ANDs; the first fully satisfiable branch with the fewest blockers wins. A script without conditions is true.
        if (script.OrConditions.All(or => or.AndConditions.IsDefaultOrEmpty)) return Finish(blockers);
        Verdict? bestBranch = null;
        foreach (var or in script.OrConditions)
        {
            if (or.AndConditions.IsDefaultOrEmpty) continue;
            var branch = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            var possible = true;
            foreach (var condition in or.AndConditions)
            {
                var need = Need(condition);
                if (need.Kind == Kind.Blocked) { possible = false; branch.UnionWith(need.Blockers); break; }
                branch.UnionWith(need.Blockers);
            }
            if (!possible) { bestBranch ??= new Verdict(Kind.Blocked, branch.ToImmutable()); continue; }
            var candidate = new Verdict(branch.Count == 0 ? Kind.Clean : Kind.Degraded, branch.ToImmutable());
            if (bestBranch is null || bestBranch.Kind == Kind.Blocked || candidate.Blockers.Count < bestBranch.Blockers.Count) bestBranch = candidate;
        }
        if (bestBranch is null) return Finish(blockers);
        if (bestBranch.Kind == Kind.Blocked) return new Verdict(Kind.Blocked, blockers.ToImmutable().Union(bestBranch.Blockers));
        foreach (var blocker in bestBranch.Blockers) blockers.Add(blocker);
        return Finish(blockers);
    }

    private static Verdict Finish(ImmutableSortedSet<string>.Builder blockers) => new(blockers.Count == 0 ? Kind.Clean : Kind.Degraded, blockers.ToImmutable());

    private Verdict? Best(IEnumerable<ScriptUnit> producers)
    {
        Verdict? best = null;
        foreach (var producer in producers.OrderBy(value => value.Index))
        {
            var verdict = Of(producer);
            if (verdict.Kind == Kind.Blocked) continue;
            if (best is null || verdict.Blockers.Count < best.Blockers.Count) best = verdict;
        }
        return best;
    }

    /// <summary>What a condition asks of the rest of the program: a leaf (state of the world) costs nothing, a flag/counter/timer needs a producer, a missing type blocks.</summary>
    private Verdict Need(MapCondition condition)
    {
        var type = condition.TypeName;
        if (ScriptCoverage.ConditionSupport(type) == ScriptSupport.Missing) return new Verdict(Kind.Blocked, [$"condition {type}"]);
        switch (type)
        {
            case "FLAG":
            {
                // A flag is false from the start (and a UI interaction can also satisfy the condition); only "flag is true" needs a setter.
                var wantsTrue = condition.Parameters.Length < 2 || condition.Parameters[1].Integer != 0;
                if (!wantsTrue) return Ok;
                return _flagSetters.TryGetValue(Name(condition, 0), out var setters) && Best(setters) is { } best ? best : new Verdict(Kind.Blocked, ImmutableSortedSet.Create($"flag '{Name(condition, 0)}' is never set"));
            }
            case "COUNTER":
            {
                // A counter starts at 0: a comparison that holds for 0 needs no producer.
                if (condition.Parameters.Length >= 3)
                {
                    var holdsAtZero = condition.Parameters[1].Integer switch
                    {
                        0 => 0 < condition.Parameters[2].Integer,
                        1 => 0 <= condition.Parameters[2].Integer,
                        2 => 0 == condition.Parameters[2].Integer,
                        3 => 0 >= condition.Parameters[2].Integer,
                        4 => 0 > condition.Parameters[2].Integer,
                        5 => 0 != condition.Parameters[2].Integer,
                        _ => false,
                    };
                    if (holdsAtZero) return Ok;
                }
                return _counterSetters.TryGetValue(Name(condition, 0), out var setters) && Best(setters) is { } best ? best : new Verdict(Kind.Blocked, ImmutableSortedSet.Create($"counter '{Name(condition, 0)}' is never changed"));
            }
            case "TIMER_EXPIRED":
                return _timerSetters.TryGetValue(Name(condition, 0), out var timers) && Best(timers) is { } timer ? timer : new Verdict(Kind.Blocked, ImmutableSortedSet.Create($"timer '{Name(condition, 0)}' is never started"));
            default:
                return Ok;
        }
    }

    private static readonly Verdict Ok = new(Kind.Clean, ImmutableSortedSet<string>.Empty);
}
