using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// The planner behind <see cref="MissionObjectiveDriver"/>: for each ending script it computes, from the engine's current state, which world-state leaves still have to be
/// made true. A script fires when it is enabled (and called, for a subroutine) and one OR-branch of its conditions holds; a timer/counter/flag condition needs a script that
/// starts/changes it, which has to fire in turn; everything else is a leaf the world can satisfy or a <em>blocker</em> the driver has no intervention for. Alternatives (several
/// enablers, setters, OR-branches) are ranked by blockers, then leaves, so the cheapest feasible way wins; a cycle blocks that way only.
/// </summary>
internal sealed partial class ObjectiveDriver
{
    private const int MaxDepth = 28;
    private const int IdleRoundsBeforeHarm = 40;
    private const int ExhaustiveRoundsBeforeUnion = 20;

    private int _idleRounds;
    private bool _allowHarm;
    private bool Exhaustive => _idleRounds >= ExhaustiveRoundsBeforeUnion;
    private List<string>? _trace;
    private List<Leaf>? _defense;

    /// <summary>One world intervention. <paramref name="Edge"/> leaves matter only at the instant their script evaluates (entering an area), so they are held back until the script is live.</summary>
    internal sealed class Leaf(string key, string label, int maxApplications, int cooldown, Func<int, bool> apply, bool edge = false)
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        public int MaxApplications { get; } = maxApplications;
        public int Cooldown { get; } = cooldown;
        public Func<int, bool> Apply { get; } = apply;
        public bool Edge { get; } = edge;
        public bool Deferred { get; set; }
        /// <summary>Applying it destroys the side the run wants to win (see <see cref="ObjectiveDriver.WouldHarm"/>): held back until nothing else has made progress for a while.</summary>
        public bool Harm { get; set; }
        /// <summary>What applying it is: an order a player could give, a UI signal, or a direct write to the world (<c>docs/SCRIPT-ENGINE.md</c> §9, commands-only mode).</summary>
        public LeafKind Kind { get; set; } = LeafKind.World;
    }

    internal sealed class Plan
    {
        public readonly List<Leaf> Leaves = [];
        public readonly SortedSet<string> Blockers = new(StringComparer.Ordinal);
        public bool Feasible => Blockers.Count == 0;
        public int Cost => Blockers.Count * 1000 + Leaves.Count(leaf => leaf.Harm) * 100 + Leaves.Count;

        public static Plan Blocked(string reason)
        {
            var plan = new Plan();
            plan.Blockers.Add(reason);
            return plan;
        }

        public static Plan Of(Leaf leaf)
        {
            var plan = new Plan();
            plan.Leaves.Add(leaf);
            return plan;
        }

        public void Merge(Plan other)
        {
            foreach (var leaf in other.Leaves)
            {
                var index = Leaves.FindIndex(existing => existing.Key == leaf.Key);
                if (index < 0) Leaves.Add(leaf);
                else if (Leaves[index].Deferred && !leaf.Deferred) Leaves[index] = leaf;
            }
            Blockers.UnionWith(other.Blockers);
        }
    }

    private readonly record struct Producer(ScriptUnit Unit, bool OnFalse);

    private readonly MissionSession _session;
    private readonly SimulationWorld _world;
    private readonly ScriptEngine _engine;
    private readonly ScriptProgram _program;
    private readonly PlayerId _local;
    private readonly bool _victory;
    private readonly bool _commandsOnly;
    private readonly SortedSet<string> _declined = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<Producer>> _timers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(Producer Producer, char Kind, int Amount)>> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Producer>> _flagsTrue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Producer>> _flagsFalse = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Producer>> _enablers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Producer>> _callers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Team, string Kind)>> _teamEvents = new(StringComparer.Ordinal);
    private readonly List<ScriptUnit> _endings = [];
    private readonly HashSet<int> _tainted = [];

    private readonly Dictionary<int, Plan> _memo = [];
    private readonly HashSet<int> _stack = [];
    private int _cycleBlocks;
    private readonly Dictionary<string, int> _applications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastApplied = new(StringComparer.Ordinal);
    private readonly List<string> _log = [];

    public ObjectiveDriver(MissionSession session, bool victory, bool commandsOnly = false, int agentThreshold = 0)
    {
        _session = session;
        _world = session.World;
        _engine = session.Engine;
        _program = session.Program;
        _local = MissionSession.LocalPlayerOf(_program, session.Options.HumanSideName);
        _victory = victory;
        _commandsOnly = commandsOnly;
        _agentThreshold = agentThreshold;
        if (commandsOnly) _agent = CreateAgent();
        Index();
    }


    private ulong _lastSignature;

    /// <summary>
    /// A fingerprint of the script state that counts as progress: which scripts and groups are on, the flags, the counters (not the timers' countdown, only which
    /// timers have started). While it keeps changing the scripts are working through something (intros, wave sequences) and the driver waits; once it stands still
    /// and no intervention applies, the run has stalled.
    /// </summary>
    private ulong Signature()
    {
        var hash = 14695981039346656037UL;
        void Mix(long value) => hash = unchecked((hash ^ (ulong)value) * 1099511628211UL);
        foreach (var unit in _program.Units) Mix(_engine.IsScriptActive(unit.Name) ? 1 : 0);
        foreach (var group in _program.Groups) Mix(_engine.IsGroupActive(group.Name) ? 3 : 2);
        foreach (var name in _flagsTrue.Keys.Concat(_flagsFalse.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal)) Mix(_engine.FlagValue(name) ? 5 : 4);
        foreach (var name in _counters.Keys.Concat(_timers.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
            Mix(_engine.CounterIsTimer(name) ? _engine.CounterValue(name) < 1 ? 7 : 6 : _engine.CounterValue(name) + 11);
        return hash;
    }
    public int Interventions { get; private set; }
    /// <summary>Commands issued through the session (orders, productions); the player-equivalent part of the run.</summary>
    public int Commands { get; private set; }
    /// <summary>Leaf applications that wrote to the world directly (kills, placements, money, ownership, spawns).</summary>
    public int WorldWrites { get; private set; }
    /// <summary>The direct world writes a commands-only run declined to make (distinct labels).</summary>
    public ImmutableArray<string> Declined => [.._declined];
    public ImmutableArray<string> Log => [.._log];

    private bool Allowed(ScriptUnit unit) => _session.Options.Difficulty switch
    {
        ScriptDifficulty.Easy => unit.Script.Easy,
        ScriptDifficulty.Hard => unit.Script.Hard,
        _ => unit.Script.Normal,
    };

    private static string Name(MapScriptAction action, int index) => index < action.Parameters.Length ? action.Parameters[index].Text : string.Empty;

    private static void Add(Dictionary<string, List<Producer>> map, string key, Producer producer)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(producer)) list.Add(producer);
    }

    private void AddCounter(string name, Producer producer, char kind, int amount)
    {
        if (name.Length == 0) return;
        if (!_counters.TryGetValue(name, out var list)) _counters[name] = list = [];
        if (!list.Contains((producer, kind, amount))) list.Add((producer, kind, amount));
    }

    private void Index()
    {
        foreach (var team in _program.Teams)
        {
            AddEvent(team.OnCreateScript, team.Name, "create");
            AddEvent(team.OnIdleScript, team.Name, "idle");
            AddEvent(team.EnemySightedScript, team.Name, "sighted");
            AddEvent(team.AllClearScript, team.Name, "clear");
            AddEvent(team.OnDestroyedScript, team.Name, "destroyed");
            AddEvent(team.OnUnitDestroyedScript, team.Name, "unitDestroyed");
        }
        var endAction = _victory ? new[] { "VICTORY", "QUICKVICTORY" } : new[] { "DEFEAT" };
        foreach (var unit in _program.Units)
        {
            if (!Allowed(unit)) continue;
            if (unit.Script.Actions.Concat(unit.Script.FalseActions).Any(action => endAction.Contains(action.TypeName))) _endings.Add(unit);
            foreach (var (actions, onFalse) in new[] { (unit.Script.Actions, false), (unit.Script.FalseActions, true) })
            {
                if (actions.IsDefaultOrEmpty) continue;
                var producer = new Producer(unit, onFalse);
                foreach (var action in actions)
                {
                    switch (action.TypeName)
                    {
                        case "SET_FLAG":
                            Add(action.Parameters.Length > 1 && action.Parameters[1].Integer != 0 ? _flagsTrue : _flagsFalse, Name(action, 0), producer);
                            break;
                        case "SET_COUNTER": AddCounter(Name(action, 0), producer, 'S', action.Parameters.Length > 1 ? action.Parameters[1].Integer : 0); break;
                        case "INCREMENT_COUNTER":
                        case "DECREMENT_COUNTER": AddCounter(Name(action, 1), producer, action.TypeName == "INCREMENT_COUNTER" ? '+' : '-', action.Parameters.Length > 0 ? action.Parameters[0].Integer : 0); break;
                        case "SET_TIMER":
                        case "SET_RANDOM_TIMER":
                        case "SET_MILLISECOND_TIMER":
                        case "SET_RANDOM_MSEC_TIMER":
                        case "RESTART_TIMER": Add(_timers, Name(action, 0), producer); break;
                        case "ADD_TO_MSEC_TIMER": Add(_timers, Name(action, 1), producer); break;
                        case "ENABLE_SCRIPT":
                            foreach (var parameter in action.Parameters)
                                if (parameter.Type is MapScriptParameterType.Script or MapScriptParameterType.ScriptSubroutine) Add(_enablers, parameter.Text, producer);
                            break;
                        case "CALL_SUBROUTINE":
                        case "TEAM_EXECUTE_SEQUENTIAL_SCRIPT":
                        case "TEAM_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING":
                        case "UNIT_EXECUTE_SEQUENTIAL_SCRIPT":
                        case "UNIT_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING":
                            foreach (var parameter in action.Parameters)
                                if (parameter.Type is MapScriptParameterType.Script or MapScriptParameterType.ScriptSubroutine) Add(_callers, parameter.Text, producer);
                            break;
                    }
                }
            }
        }

        // Scripts that lead to the opposite ending (they execute it, or enable/call a script that does): firing one in a run that wants the other ending is a dead end, so the
        // planner never routes through them. The win and lose sequences of a map share timers ("Wait 1 Second"); this is what tells their producers apart.
        var opposite = _victory ? new[] { "DEFEAT" } : new[] { "VICTORY", "QUICKVICTORY" };
        foreach (var unit in _program.Units)
            if (Allowed(unit) && !_endings.Contains(unit) && unit.Script.Actions.Concat(unit.Script.FalseActions).Any(action => opposite.Contains(action.TypeName))) _tainted.Add(unit.Index);
        // What each script switches on or calls (a group stands for all its scripts). A dispatcher that enables the win condition and the lose condition together
        // ("Win_Loss_Enable - USA") leads to both endings and is not a defeat-only script; only a script all of whose targets lead to defeat is one.
        var targetsOf = new Dictionary<int, HashSet<int>>();
        foreach (var (name, producers) in _enablers.Concat(_callers))
        {
            var targets = _program.FindScript(name) is { } target ? [target.Index] : _program.FindGroup(name) is { } group ? group.ScriptIndices.ToArray() : [];
            foreach (var producer in producers)
            {
                if (!targetsOf.TryGetValue(producer.Unit.Index, out var set)) targetsOf[producer.Unit.Index] = set = [];
                set.UnionWith(targets);
            }
        }
        // A script that only sets timers, counters or flags which nothing but defeat-leading scripts read belongs to the defeat sequence as well ("Losing Condition 1" starts the
        // timer "Start Lose Condition", which only the lose sequence reads).
        var consumers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var unit in _program.Units)
        {
            if (!Allowed(unit)) continue;
            foreach (var or in unit.Script.OrConditions)
                foreach (var condition in or.AndConditions)
                    if (condition.TypeName is "TIMER_EXPIRED" or "COUNTER" or "FLAG" && condition.Parameters.Length > 0)
                    {
                        if (!consumers.TryGetValue(condition.Parameters[0].Text, out var list)) consumers[condition.Parameters[0].Text] = list = [];
                        if (!list.Contains(unit.Index)) list.Add(unit.Index);
                    }
        }
        var produced = new Dictionary<int, HashSet<string>>();
        foreach (var map in new[] { _timers, _flagsTrue, _flagsFalse })
            foreach (var (name, producers) in map)
                foreach (var producer in producers)
                {
                    if (!produced.TryGetValue(producer.Unit.Index, out var names)) produced[producer.Unit.Index] = names = new HashSet<string>(StringComparer.Ordinal);
                    names.Add(name);
                }
        foreach (var (name, movers) in _counters)
            foreach (var mover in movers)
            {
                if (!produced.TryGetValue(mover.Producer.Unit.Index, out var names)) produced[mover.Producer.Unit.Index] = names = new HashSet<string>(StringComparer.Ordinal);
                names.Add(name);
            }
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (index, targets) in targetsOf)
            {
                if (_endings.Any(ending => ending.Index == index) || _tainted.Contains(index) || targets.Count == 0 || !targets.All(_tainted.Contains)) continue;
                _tainted.Add(index);
                changed = true;
            }
            foreach (var unit in _program.Units)
            {
                if (!Allowed(unit) || _endings.Contains(unit) || _tainted.Contains(unit.Index) || !produced.TryGetValue(unit.Index, out var names) || names.Count == 0) continue;
                if (names.All(name => consumers.TryGetValue(name, out var readers) && readers.All(reader => reader != unit.Index && _tainted.Contains(reader)))) { _tainted.Add(unit.Index); changed = true; }
            }
        }

        void AddEvent(string script, string team, string kind)
        {
            if (script.Length == 0) return;
            if (!_teamEvents.TryGetValue(script, out var list)) _teamEvents[script] = list = [];
            list.Add((team, kind));
        }
    }

    // ------------------------------------------------------------------------------------------------ one round

    /// <summary>Plans every ending and applies the leaves that are due. Call between ticks.</summary>
    public void Round()
    {
        _memo.Clear();
        var plans = PlanEndings();
        var feasible = plans.Where(plan => plan.Feasible).ToList();
        // Harmful leaves (destroying the viewer in a victory run) only when nothing else has made progress for a while and no harmless leaf is left anywhere in the plans.
        var allowHarm = _allowHarm = _idleRounds >= IdleRoundsBeforeHarm && !feasible.Any(plan => plan.Leaves.Any(leaf => !leaf.Harm));
        var chosen = feasible.Count > 0 && feasible.Any(plan => plan.Leaves.Any(leaf => !leaf.Harm && !leaf.Deferred)) ? feasible : plans.OrderBy(plan => plan.Cost).Take(1).Concat(feasible).ToList();
        var tick = _world.Tick;
        var before = Interventions;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in chosen)
            foreach (var leaf in plan.Leaves)
            {
                if (leaf.Deferred || (leaf.Harm && !allowHarm) || !seen.Add(leaf.Key)) continue;
                if (_commandsOnly && leaf.Kind == LeafKind.World) { _declined.Add(leaf.Label); continue; }
                var count = _applications.GetValueOrDefault(leaf.Key);
                if (count >= leaf.MaxApplications) continue;
                if (_lastApplied.TryGetValue(leaf.Key, out var last) && tick - last < leaf.Cooldown) continue;
                if (!leaf.Apply(count)) continue;
                _applications[leaf.Key] = count + 1;
                _lastApplied[leaf.Key] = tick;
                Interventions++;
                if (leaf.Kind == LeafKind.World) WorldWrites++;
                _log.Add($"{tick}: {leaf.Label}");
            }
        var signature = Signature();
        if (_victory && !_commandsOnly)
            foreach (var leaf in _defense ??= BuildDefense())
            {
                if (_lastApplied.TryGetValue(leaf.Key, out var lastDefence) && tick - lastDefence < leaf.Cooldown) continue;
                if (!leaf.Apply(0)) continue;
                _lastApplied[leaf.Key] = tick;
                var times = _applications[leaf.Key] = _applications.GetValueOrDefault(leaf.Key) + 1;
                WorldWrites++;
                if (times <= 3) _log.Add($"{tick}: {leaf.Label}");
            }
        _idleRounds = Interventions == before && signature == _lastSignature ? _idleRounds + 1 : 0;
        _lastSignature = signature;
    }

    private List<Plan> PlanEndings()
    {
        var plans = new List<Plan>();
        foreach (var ending in _endings) plans.Add(PlanFire(ending, false, 0));
        return plans;
    }

    // ------------------------------------------------------------------------------------------------ planning

    /// <summary>What has to happen for <paramref name="unit"/> to run its actions (or, with <paramref name="onFalse"/>, its else-actions).</summary>
    private Plan PlanFire(ScriptUnit unit, bool onFalse, int depth)
    {
        var key = unit.Index * 2 + (onFalse ? 1 : 0);
        if (_memo.TryGetValue(key, out var known)) return known;
        if (depth > MaxDepth || !_stack.Add(key))
        {
            _cycleBlocks++;
            return Plan.Blocked($"cycle at '{unit.Name}'");
        }
        var cyclesBefore = _cycleBlocks;
        var plan = new Plan();
        try { ComputeFire(unit, onFalse, depth, plan); }
        finally { _stack.Remove(key); }
        if (_cycleBlocks == cyclesBefore) _memo[key] = plan;
        return plan;
    }

    private void ComputeFire(ScriptUnit unit, bool onFalse, int depth, Plan plan)
    {
        if (!Allowed(unit))
        {
            plan.Blockers.Add($"script '{unit.Name}' is off on this difficulty");
            return;
        }
        if (_tainted.Contains(unit.Index))
        {
            plan.Blockers.Add($"script '{unit.Name}' leads to the {(_victory ? "defeat" : "victory")} ending");
            return;
        }
        var script = unit.Script;
        var group = unit.GroupIndex >= 0 ? _program.Groups[unit.GroupIndex] : null;
        var active = _engine.IsScriptActive(unit.Name);
        var groupOk = group is null || _engine.IsGroupActive(group.Name);
        var needsCaller = unit.IsSubroutine || group is { Group.IsSubroutine: true };
        var indent = new string(' ', Math.Min(depth, 12) * 2);
        _trace?.Add($"{indent}{unit.Name}{(onFalse ? " (else)" : "")}: {(active ? "active" : "inactive")}{(groupOk ? "" : $", group '{group!.Name}' inactive")}{(needsCaller ? ", subroutine" : "")}{(unit.Script.DelayEvaluationSeconds > 0 ? $", delay {unit.Script.DelayEvaluationSeconds} s" : "")}");

        if (!active || !groupOk)
        {
            var producers = Lookup(_enablers, unit.Name).Concat(group is null ? [] : Lookup(_enablers, group.Name)).Where(producer => producer.Unit != unit).ToList();
            var best = BestOf(producers, depth);
            if (best is null) { plan.Blockers.Add($"script '{unit.Name}' is never enabled"); return; }
            plan.Merge(best);
        }
        if (needsCaller)
        {
            var alternatives = new List<Plan>();
            foreach (var producer in Lookup(_callers, unit.Name).Concat(group is null ? [] : Lookup(_callers, group.Name)).Where(producer => producer.Unit != unit))
                alternatives.Add(PlanFire(producer.Unit, producer.OnFalse, depth + 1));
            foreach (var name in group is null ? new[] { unit.Name } : [unit.Name, group.Name])
                if (_teamEvents.TryGetValue(name, out var events))
                    alternatives.Add(TeamEventPlan(events));
            if (alternatives.Count == 0) { plan.Blockers.Add($"subroutine '{unit.Name}' is never called"); return; }
            plan.Merge(Choose(alternatives));
        }
        var ready = active && groupOk && !needsCaller;

        var branches = script.OrConditions.Where(or => !or.AndConditions.IsDefaultOrEmpty).ToList();
        if (branches.Count == 0) return;
        var results = _engine.ExplainScript(unit.Name);
        _trace?.Add($"{indent}  conditions: {string.Join(", ", results.Select(item => $"{item.Label[(item.Label.IndexOf(' ', StringComparison.Ordinal) + 1)..]}={(item.Result ? 'T' : 'F')}"))}");
        var position = 0;
        var branchResults = new List<(MapOrCondition Branch, bool[] Values)>();
        foreach (var or in script.OrConditions)
        {
            if (or.AndConditions.IsDefaultOrEmpty) continue;
            var values = new bool[or.AndConditions.Length];
            for (var index = 0; index < values.Length; index++) values[index] = position < results.Length && results[position++].Result;
            branchResults.Add((or, values));
        }
        if (onFalse)
        {
            if (branchResults.Any(item => item.Values.All(value => value))) plan.Blockers.Add($"else-branch of '{unit.Name}' needs its conditions false");
            return;
        }
        var branchPlans = new List<Plan>();
        foreach (var (branch, values) in branchResults)
        {
            var branchPlan = new Plan();
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index]) continue;
                branchPlan.Merge(NeedCondition(branch.AndConditions[index], ready, depth));
            }
            branchPlans.Add(branchPlan);
            if (branchPlan.Cost == 0 && !Exhaustive) break;
        }
        if (branchPlans.Count > 0) plan.Merge(Choose(branchPlans));
    }

    private static IEnumerable<Producer> Lookup(Dictionary<string, List<Producer>> map, string name) => map.TryGetValue(name, out var list) ? list : [];

    /// <summary>The cheapest of the producers' own plans; <see langword="null"/> when there are none.</summary>
    private Plan? BestOf(IEnumerable<Producer> producers, int depth)
    {
        var plans = producers.Select(producer => PlanFire(producer.Unit, producer.OnFalse, depth + 1)).ToList();
        return plans.Count == 0 ? null : Choose(plans);
    }

    /// <summary>
    /// The cheapest of several alternative ways. Once the run has stalled (<see cref="ExhaustiveRoundsBeforeUnion"/> rounds without an intervention) the cheapest way is
    /// evidently not the way the scripts really need, so every feasible alternative contributes its leaves instead.
    /// </summary>
    private Plan Choose(List<Plan> alternatives)
    {
        var cheapest = alternatives.OrderBy(candidate => candidate.Cost).First();
        if (!Exhaustive || alternatives.Count == 1) return cheapest;
        var union = new Plan();
        var feasible = alternatives.Where(candidate => candidate.Feasible).ToList();
        if (feasible.Count == 0) return cheapest;
        foreach (var candidate in feasible) union.Merge(candidate);
        return union;
    }

    /// <summary>What a false condition needs: a timer waited out (or started by some script), a counter/flag set by some script, or a world-state leaf.</summary>
    private Plan NeedCondition(MapCondition condition, bool ready, int depth)
    {
        var p = condition.Parameters;
        string Text(int index) => index < p.Length ? p[index].Text : string.Empty;
        switch (condition.TypeName)
        {
            case "TIMER_EXPIRED":
            {
                if (_engine.CounterIsTimer(Text(0))) return new Plan();
                return BestOf(Lookup(_timers, Text(0)), depth) ?? Plan.Blocked($"timer '{Text(0)}' is never started");
            }
            case "COUNTER":
            {
                // Which way the counter has to move: up for >/>=, down for </<=, towards the value for ==; a setter counts when the value it stores satisfies the comparison.
                var name = Text(0);
                var comparison = p.Length > 1 ? p[1].Integer : 0;
                var target = p.Length > 2 ? p[2].Integer : 0;
                var wantUp = comparison switch { 3 or 4 => true, 0 or 1 => false, 2 => _engine.CounterValue(name) < target, _ => true };
                var candidates = _counters.TryGetValue(name, out var movers)
                    ? movers.Where(mover => mover.Kind == 'S' ? Compare(comparison, mover.Amount, target) : (mover.Kind == '+') == (mover.Amount > 0) == wantUp).Select(mover => mover.Producer).ToList()
                    : [];
                return BestOf(candidates, depth) ?? Plan.Blocked($"no script moves counter '{name}' {(wantUp ? "up" : "down")} (needs {comparison} {target})");
            }
            case "FLAG":
            {
                var wantsTrue = p.Length < 2 || p[1].Integer != 0;
                var setters = Lookup(wantsTrue ? _flagsTrue : _flagsFalse, Text(0)).ToList();
                if (setters.Count == 0)
                    // A flag nobody sets is one the UI sets (a button the player presses): the engine accepts the interaction as the flag.
                    return wantsTrue ? Plan.Of(new Leaf("ui:" + Text(0), $"press the UI control behind flag '{Text(0)}'", 3, 60, _ => { _engine.SignalUiInteraction(Text(0)); return true; })) : new Plan();
                return BestOf(setters, depth) ?? Plan.Blocked($"flag '{Text(0)}' is never set");
            }
            default:
                return LeafFor(condition, ready);
        }
    }

    /// <summary>
    /// What makes the engine call a team event script: the script is shared by every team prototype that names it (one <c>Wave 6 Team Member Destroyed</c> serves a dozen teams), and
    /// whichever of them has a live member will do, so the leaf works on the first such team instead of insisting on one that may never exist.
    /// </summary>
    private Plan TeamEventPlan(IReadOnlyList<(string Team, string Kind)> events)
    {
        var plan = new Plan();
        foreach (var kind in events.Select(item => item.Kind).Distinct(StringComparer.Ordinal))
        {
            var teams = events.Where(item => item.Kind == kind).Select(item => item.Team).Distinct(StringComparer.Ordinal).ToArray();
            switch (kind)
            {
                case "unitDestroyed":
                    plan.Merge(Plan.Of(new Leaf("teamdie:" + string.Join("|", teams), $"destroy a member of team '{teams[0]}'{(teams.Length > 1 ? $" or {teams.Length - 1} other(s)" : "")}", 400, 24, _ =>
                    {
                        var member = teams.SelectMany(LiveTeamMembers).FirstOrDefault();
                        if (member is null) return false;
                        _world.Kill(member.Id);
                        return true;
                    })));
                    break;
                case "destroyed":
                    plan.Merge(Plan.Of(new Leaf("killteams:" + string.Join("|", teams), $"destroy team '{teams[0]}'{(teams.Length > 1 ? $" and {teams.Length - 1} other(s)" : "")}", 40, 30, _ =>
                    {
                        var members = teams.SelectMany(LiveTeamMembers).ToArray();
                        if (members.Length == 0) return false;
                        foreach (var member in members) _world.Kill(member.Id);
                        return true;
                    })));
                    break;
            }
        }
        return plan;
    }

    // ------------------------------------------------------------------------------------------------ the diagnosis of a run that did not end

    /// <summary>For every special power a script asks about (<c>SKIRMISH_SPECIAL_POWER_READY</c>): who carries it and whether it is ready or paused — why a power condition stays false.</summary>
    public ImmutableArray<string> PowerCarriers()
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        var powers = _program.Units.SelectMany(unit => unit.Script.OrConditions.SelectMany(or => or.AndConditions)).Where(condition => condition.TypeName == "SKIRMISH_SPECIAL_POWER_READY" && condition.Parameters.Length > 1)
            .Select(condition => condition.Parameters[1].Text).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var name in powers)
        {
            if (!_world.SpecialPowers.TryGet(name, out var power)) { lines.Add($"power '{name}': unknown"); continue; }
            var before = lines.Count;
            foreach (var entity in _world.Entities.OrderBy(value => value.Id.Value))
                if (SpecialPowerRouting.FindModule(entity, power.Index) is { } module)
                    lines.Add($"power '{name}': {entity.Template.Name} #{entity.Id.Value} of {(_program.Players.FirstOrDefault(player => player.Id == entity.Owner)?.Name ?? entity.Owner.Value.ToString())} ready={module.IsReady(_world)} paused={module.IsPaused} dead={entity.EffectivelyDead}");
            if (lines.Count == before) lines.Add($"power '{name}': no live carrier");
        }
        return lines.ToImmutable();
    }

    /// <summary>The live objects each side still has at the end of a run that did not end (template counts, most numerous first): what a kill-everything leaf could not remove.</summary>
    public ImmutableArray<string> Survivors()
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var player in _program.Players.Where(player => !player.Id.IsSystem && player.Name.Length > 0))
        {
            var alive = LiveOf(player.Id).GroupBy(entity => entity.Template.Name).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(6).ToArray();
            if (alive.Length > 0) lines.Add($"alive of '{player.Name}': {string.Join(", ", alive.Select(group => $"{group.Key} x{group.Count()}"))}");
        }
        return lines.ToImmutable();
    }

    /// <summary>For each ending script that stayed open: what the planner still saw missing, then the trace of the scripts it walked (state, subroutine/delay, conditions).</summary>
    public ImmutableArray<string> Unmet()
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var unit in _endings.Take(6))
        {
            _memo.Clear();
            _trace = [];
            var plan = PlanFire(unit, false, 0);
            lines.Add($"{unit.Name}{(_engine.IsScriptActive(unit.Name) ? "" : " [inactive]")}: " +
                      (plan.Blockers.Count > 0 ? "BLOCKED by " + string.Join("; ", plan.Blockers.Take(6))
                          : plan.Leaves.Count > 0 ? "waiting after leaves " + string.Join("; ", plan.Leaves.Take(6).Select(leaf => leaf.Label + $" (x{_applications.GetValueOrDefault(leaf.Key)})"))
                          : "nothing left to do (timers still running or script not evaluated)") +
                      (plan.Leaves.Any(leaf => leaf.Harm) ? " [harmful leaves held back: " + string.Join("; ", plan.Leaves.Where(leaf => leaf.Harm).Take(3).Select(leaf => leaf.Label)) + "]" : ""));
            foreach (var line in _trace.Take(36)) lines.Add("  " + line);
            _trace = null;
        }
        return lines.ToImmutable();
    }
}
