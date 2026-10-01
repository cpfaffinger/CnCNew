using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

public enum ScriptDifficulty { Easy = 0, Normal = 1, Hard = 2 }

public enum MissionOutcome { None = 0, Victory = 1, Defeat = 2 }

/// <param name="Difficulty">The game difficulty scripts are gated with (<c>Script::isEasy/isNormal/isHard</c>; the reference uses the current player's difficulty, which for every player of a campaign is the chosen one).</param>
/// <param name="LocalPlayer">What <c>&lt;Local Player&gt;</c> and the multiplayer victory/defeat conditions refer to: the human viewer.</param>
/// <param name="MissionAttempts">Start count of this mission in the campaign profile, for <c>MISSION_ATTEMPTS</c>.</param>
/// <param name="PresentationLatencyTicks">How long a speech/sound/movie/camera move counts as still playing for <c>HAS_FINISHED_*</c> and <c>CAMERA_MOVEMENT_FINISHED</c>; a live client may instead end them with <see cref="ScriptEngine.CompletePresentation"/>. 0: finished as soon as started (headless).</param>
public sealed record ScriptEngineOptions(ScriptDifficulty Difficulty, PlayerId LocalPlayer, int PresentationLatencyTicks = 0, int MissionAttempts = 1)
{
    public static ScriptEngineOptions Default { get; } = new(ScriptDifficulty.Normal, new PlayerId(1));
}

/// <summary>A presentation effect a script asked for (speech, movie, camera, text ...); the client executes it, the simulation only records that it happened.</summary>
public readonly record struct ScriptOutput(long Tick, string Action, ImmutableArray<string> Arguments);

/// <summary>Submits a player command with the session's single per-player sequence counter (sequences must increase over every source of commands).</summary>
public delegate SubmitResult ScriptCommandIssuer(PlayerId player, Func<ulong, long, SimCommand> make);

/// <summary>One team instance (<c>Team</c>): the members the script engine tracks for a <see cref="ScriptTeamPrototype"/>.</summary>
public sealed class ScriptTeam(int id, string prototype, PlayerId owner)
{
    public int Id { get; } = id;
    public string Prototype { get; } = prototype;
    public PlayerId Owner { get; set; } = owner;
    public List<uint> Members { get; } = [];

    /// <summary><c>Team::isCreated</c>: the team has been activated (here: it has ever had a member).</summary>
    public bool Created { get; set; }

    /// <summary><c>Team::setAttackPriorityName</c>: the attack priority set members get, new ones included.</summary>
    public string AttackPriority { get; set; } = string.Empty;
}

/// <summary>
/// The deterministic World Builder script VM (<c>ScriptEngine::update</c>, <c>ScriptEngine.cpp</c>). One engine runs the scripts of one map against one
/// <see cref="SimulationWorld"/>: call <see cref="Update"/> once per tick <em>before</em> <see cref="SimulationWorld.Step"/>. It holds counters, flags,
/// script/group activation and evaluation schedules, named objects, teams, object-type lists, waypoint followers and the mission outcome — all of it saved by
/// <see cref="Describe"/>/<see cref="Load"/> and covered by <see cref="StateHash"/>.
/// <para>
/// Orders to units are ordinary player commands through the <see cref="ScriptCommandIssuer"/> (validated like any other); direct world mutations
/// (spawn, kill, money, ownership) use the world API. A condition or action type without implementation is counted
/// (<see cref="MissingConditionHits"/>/<see cref="MissingActionHits"/>), never skipped silently; <see cref="ScriptCoverage"/> tells which is which.
/// </para>
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary><c>FRAMES_TO_SHOW_WIN_LOSE_MESSAGE</c> (<c>ScriptEngine.cpp</c>): how long scripts keep running after a victory or defeat before the game ends.</summary>
    public const int EndGameDelayTicks = 120;

    /// <summary><c>ATTACK_PRIORITY_DEFAULT</c> (<c>ScriptEngine.cpp</c>).</summary>
    public const int DefaultAttackPriority = 1;

    private const int MaxSubroutineDepth = 32;
    private const string ThisPlayerName = "<This Player>";
    private const string LocalPlayerName = "<Local Player>";
    private const string ThisTeamName = "<This Team>";

    private sealed class Counter
    {
        public long Value;
        public bool IsTimer;
    }

    /// <summary><c>BodyModule::getLastDamageInfo</c>: who hurt an object last (never reset), as far as the script conditions read it.</summary>
    private readonly record struct LastDamage(int Owner, string Template);

    /// <summary><c>AttackPriorityInfo</c>: a named set of target priorities. Rules apply in the order they were given, the last matching one wins (the reference writes kind rules into the same per-type map as type rules).</summary>
    private sealed class PrioritySet
    {
        public int Default = DefaultAttackPriority;
        public readonly List<(bool IsKind, string Name, int Priority)> Rules = [];
    }

    private sealed class Follower
    {
        public uint Entity;
        public string Path = string.Empty;
        public int Index;
    }

    private readonly ScriptProgram _program;
    private readonly SimulationWorld _world;
    private readonly ScriptEngineOptions _options;
    private readonly ScriptCommandIssuer _issue;

    private readonly bool[] _scriptActive;
    private readonly long[] _nextEvaluation;
    private readonly bool[] _groupActive;
    private readonly string?[] _conditionTeam;

    private readonly SortedDictionary<string, Counter> _counters = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, bool> _flags = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, uint> _named = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _everNamed = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, SortedSet<string>> _objectLists = new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, ScriptTeam> _teams = [];
    private readonly SortedDictionary<uint, Follower> _followers = [];
    private readonly SortedDictionary<uint, LastDamage> _lastDamage = [];
    private readonly SortedDictionary<uint, string> _completedPath = [];
    private readonly SortedDictionary<uint, int> _lastEntered = [];
    private readonly SortedDictionary<uint, string> _prioritySetOf = [];
    private readonly SortedDictionary<string, PrioritySet> _prioritySets = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _pendingPresentation = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _uiInteractions = new(StringComparer.Ordinal);
    private readonly SortedSet<uint> _selected = [];
    private readonly SortedDictionary<string, bool> _areaInside = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _missingConditions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _missingActions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _diagnostics = new(StringComparer.Ordinal);
    private readonly List<ScriptOutput> _outputs = [];

    private uint _pruneBelow;
    private int _nextTeamId = 1;
    private long _endTimer = -1;
    private long _tick;
    private int _currentList;
    private ScriptTeam? _callingTeam;
    private int _depth;
    private string _runningScript = string.Empty;
    private long _executedScripts;
    private long _executedActions;
    private long _forwardedActions;

    public ScriptEngine(ScriptProgram program, SimulationWorld world, ScriptEngineOptions options, ScriptCommandIssuer issue)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(issue);
        _program = program;
        _world = world;
        _options = options;
        _issue = issue;
        _scriptActive = new bool[program.Units.Length];
        _nextEvaluation = new long[program.Units.Length];
        _groupActive = new bool[program.Groups.Length];
        _conditionTeam = new string?[program.Units.Length];
        for (var index = 0; index < program.Units.Length; index++)
        {
            var script = program.Units[index].Script;
            _scriptActive[index] = script.IsActive;
            // ScriptEngine::checkConditionsForTeamNames draws the first evaluation frame of a delayed script from the logic random stream; a
            // name-derived offset in the same 0..2 s range keeps the spread without consuming a stream that a restored world would not replay.
            _nextEvaluation[index] = script.DelayEvaluationSeconds > 0 ? (long)(NameHash(script.Name) % (2 * SimulationTiming.LogicFramesPerSecond + 1)) : 0;
            _conditionTeam[index] = FindConditionTeam(script);
        }
        for (var index = 0; index < program.Groups.Length; index++) _groupActive[index] = program.Groups[index].Group.IsActive;
        foreach (var prototype in program.Teams)
            if (prototype.IsSingleton || prototype.MaxInstances < 2) CreateTeam(prototype.Name);
    }

    public ScriptProgram Program => _program;
    public MissionOutcome Outcome { get; private set; }

    /// <summary>The tick the outcome was decided (<c>VICTORY</c>/<c>DEFEAT</c> executed), or -1.</summary>
    public long OutcomeTick { get; private set; } = -1;

    /// <summary>The script whose <c>VICTORY</c>/<c>DEFEAT</c> action decided the mission (empty when none did).</summary>
    public string OutcomeScript { get; private set; } = string.Empty;

    /// <summary>The end-game delay is over: the script engine no longer runs and the client should leave the mission.</summary>
    public bool IsEnded { get; private set; }

    public long ExecutedScripts => _executedScripts;
    public long ExecutedActions => _executedActions;
    public long ForwardedActions => _forwardedActions;

    /// <summary>Executions of condition types without implementation, by type name (the condition evaluated to false each time).</summary>
    public IReadOnlyDictionary<string, long> MissingConditionHits => _missingConditions;

    /// <summary>Executions of action types without implementation, by type name (nothing happened each time).</summary>
    public IReadOnlyDictionary<string, long> MissingActionHits => _missingActions;

    /// <summary>Unresolvable references met at runtime (unknown player/team/waypoint/area/template, malformed parameter lists, recursion limit); never empty by accident.</summary>
    public IReadOnlyDictionary<string, long> Diagnostics => _diagnostics;

    public long CounterValue(string name) => _counters.TryGetValue(name, out var counter) ? counter.Value : 0;
    public bool CounterIsTimer(string name) => _counters.TryGetValue(name, out var counter) && counter.IsTimer;
    public bool FlagValue(string name) => _flags.GetValueOrDefault(name);
    public bool IsScriptActive(string name) => _program.FindScript(name) is { } unit && _scriptActive[unit.Index];
    public bool IsGroupActive(string name) => _program.FindGroup(name) is { } group && _groupActive[group.Index];
    public IEnumerable<KeyValuePair<string, uint>> NamedObjects => _named;
    public IEnumerable<ScriptTeam> TeamInstances => _teams.Values;
    public IReadOnlyCollection<string> PendingPresentation => _pendingPresentation.Keys;

    /// <summary>Hands over (and forgets) the presentation effects requested since the last call, in execution order.</summary>
    public ImmutableArray<ScriptOutput> DrainOutputs()
    {
        var result = _outputs.ToImmutableArray();
        _outputs.Clear();
        return result;
    }

    /// <summary>
    /// <c>AttackPriorityInfo::getPriority</c> for the set an object was given (<c>*_APPLY_ATTACK_PRIORITY_SET</c>): the priority of <paramref name="target"/>'s
    /// type, the set's default when no rule matches, and <see cref="DefaultAttackPriority"/> for an object without a set. The session's combat and the hunt
    /// orders pick the highest priority, nearest first (<c>TheAI->findClosestEnemy</c> with the priority info).
    /// </summary>
    public int AttackPriority(Entity attacker, Entity target)
    {
        if (!_prioritySetOf.TryGetValue(attacker.Id.Value, out var name) || !_prioritySets.TryGetValue(name, out var set)) return DefaultAttackPriority;
        var priority = set.Default;
        foreach (var (isKind, ruleName, value) in set.Rules)
            if (isKind ? target.Template.IsKindOf(ruleName) : string.Equals(target.Template.Name, ruleName, StringComparison.Ordinal)) priority = value;
        return priority;
    }

    // ------------------------------------------------------------------------------------------------ host interface

    /// <summary><c>TheScriptEngine->addObjectToCache</c>: makes an existing object addressable by a script name (map objects with <c>objectName</c>).</summary>
    public void RegisterNamed(string name, EntityId id)
    {
        if (string.IsNullOrEmpty(name)) return;
        _named[name] = id.Value;
        if (_world.FindEntity(id) is not null) _everNamed.Add(name);
    }

    /// <summary>Puts an object into a team instance (map objects join the singleton team of their <c>originalOwner</c>).</summary>
    public void AssignToTeam(string prototype, EntityId id)
    {
        var team = FindTeam(prototype) ?? CreateTeam(prototype);
        if (team is null) return;
        if (!team.Members.Contains(id.Value)) team.Members.Add(id.Value);
        team.Created = true;
    }

    /// <summary>Tells the engine that a UI interaction happened (<c>ScriptEngine::signalUIInteraction</c>): a <c>FLAG</c> condition for that flag name is satisfied for one evaluation round.</summary>
    public void SignalUiInteraction(string flagName) => _uiInteractions.Add(flagName);

    /// <summary>The client selected a named object (<c>NAMED_SELECTED</c>); the selection stays until <see cref="ClearSelection"/>.</summary>
    public void SelectNamed(string name)
    {
        if (_named.TryGetValue(name, out var id)) _selected.Add(id);
    }

    public void ClearSelection() => _selected.Clear();

    /// <summary>A speech, sound, movie or camera move has finished playing on the client.</summary>
    public void CompletePresentation(string name) => _pendingPresentation.Remove(name);

    /// <summary>
    /// Feeds the tick's world events to the engine (call with <see cref="TickResult.Events"/> after every world step): damage events record the last
    /// attacker of each object for the <c>*_ATTACKED_BY_*</c> conditions. The attacker's owner and type are read at this point, after the step, so an
    /// attacker that died in the same tick is recorded with an unknown owner (the reference stores the source player mask at damage time).
    /// </summary>
    public void Observe(IReadOnlyList<CnCNew.Simulation.Events.SimEvent> events)
    {
        foreach (var item in events)
        {
            ObserveExtended(item);
            if (item.Kind == CnCNew.Simulation.Events.SimEventKind.EntityContained && item.Other.IsValid)
            {
                // ContainModuleInterface::getPlayerWhoEntered: the owner of the last object that entered the container.
                if (_world.FindEntity(item.Entity) is { } entered) _lastEntered[item.Other.Value] = entered.Owner.Value;
                continue;
            }
            if (item.Kind != CnCNew.Simulation.Events.SimEventKind.EntityDamaged || !item.Other.IsValid) continue;
            var attacker = _world.FindEntity(item.Other);
            _lastDamage[item.Entity.Value] = attacker is null ? new LastDamage(-1, string.Empty) : new LastDamage(attacker.Owner.Value, attacker.Template.Name);
        }
    }

    // ------------------------------------------------------------------------------------------------ update

    /// <summary><c>ScriptEngine::update</c>: countdown timers, then every side's scripts in list order, then the followers. Call once per tick before the world step.</summary>
    public void Update(long tick)
    {
        _tick = tick;
        _busyThisTick.Clear();
        if (_endTimer > 0)
        {
            _endTimer--;
            if (_endTimer < 1) IsEnded = true;
        }
        if (_endTimer >= 0) return;
        if (_pendingPresentation.Count > 0)
            foreach (var name in _pendingPresentation.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray()) _pendingPresentation.Remove(name);

        PruneTeams();
        foreach (var counter in _counters.Values)
            if (counter.IsTimer && counter.Value >= 0) counter.Value--;

        for (var list = 0; list < _program.ListCount; list++)
        {
            _currentList = list;
            foreach (var unit in _program.Units)
            {
                if (unit.ListIndex != list || unit.GroupIndex >= 0) continue;
                if (!unit.IsSubroutine) ExecuteScript(unit);
            }
            foreach (var group in _program.Groups)
            {
                if (group.ListIndex != list || !_groupActive[group.Index] || group.Group.IsSubroutine) continue;
                foreach (var index in group.ScriptIndices)
                    if (!_program.Units[index].IsSubroutine) ExecuteScript(_program.Units[index]);
            }
        }
        _uiInteractions.Clear();
        UpdateFollowers();
        ProcessSequences();
        ProcessTeamBuilds();
        _pruneBelow = _world.Ids.NextEntity;
    }

    private void ExecuteScript(ScriptUnit unit)
    {
        var script = unit.Script;
        var outerScript = _runningScript;
        _runningScript = script.Name;
        try { ExecuteScriptBody(unit, script); }
        finally { _runningScript = outerScript; }
    }

    private void ExecuteScriptBody(ScriptUnit unit, MapScript script)
    {
        if (!_scriptActive[unit.Index]) return;
        var allowed = _options.Difficulty switch
        {
            ScriptDifficulty.Easy => script.Easy,
            ScriptDifficulty.Hard => script.Hard,
            _ => script.Normal,
        };
        if (!allowed) return;
        if (_tick < _nextEvaluation[unit.Index]) return;
        if (script.DelayEvaluationSeconds > 0) _nextEvaluation[unit.Index] = _tick + (long)script.DelayEvaluationSeconds * SimulationTiming.LogicFramesPerSecond;

        var previousTeam = _callingTeam;
        var conditionTeam = _conditionTeam[unit.Index];
        var instances = conditionTeam is null ? [] : _teams.Values.Where(team => team.Prototype == conditionTeam).ToArray();
        if (instances.Length > 0)
        {
            // One evaluation per instance of the team the conditions talk about; the actions of that round act for that instance.
            foreach (var instance in instances)
            {
                _callingTeam = instance;
                if (EvaluateConditions(unit))
                {
                    _executedScripts++;
                    ExecuteActions(script.Actions);
                    if (script.IsOneShot) _scriptActive[unit.Index] = false;
                }
                else if (!script.FalseActions.IsDefaultOrEmpty) ExecuteActions(script.FalseActions);
            }
        }
        else
        {
            _callingTeam = null;
            if (EvaluateConditions(unit))
            {
                _executedScripts++;
                ExecuteActions(script.Actions);
                if (script.IsOneShot) _scriptActive[unit.Index] = false;
            }
            else if (!script.FalseActions.IsDefaultOrEmpty)
            {
                ExecuteActions(script.FalseActions);
                if (script.IsOneShot) _scriptActive[unit.Index] = false;
            }
        }
        _callingTeam = previousTeam;
    }

    /// <summary>
    /// Evaluates every condition of a script once, outside the normal schedule, and reports each result (<c>branch.condition</c> labels). A diagnostic for tools
    /// and tests: edge-triggered conditions (entered/exited area, lost object type) update their memory, so do not call it on a run you still want to compare.
    /// </summary>
    public ImmutableArray<(string Label, bool Result)> ExplainScript(string name)
    {
        if (_program.FindScript(name) is not { } unit) return [];
        var result = ImmutableArray.CreateBuilder<(string, bool)>();
        var team = _conditionTeam[unit.Index] is { } prototype ? FindTeam(prototype) : null;
        var previous = _callingTeam;
        _callingTeam = team;
        for (var branch = 0; branch < unit.Script.OrConditions.Length; branch++)
            for (var index = 0; index < unit.Script.OrConditions[branch].AndConditions.Length; index++)
            {
                var condition = unit.Script.OrConditions[branch].AndConditions[index];
                result.Add(($"{branch}.{index} {condition.TypeName}", EvaluateCondition(condition)));
            }
        _callingTeam = previous;
        return result.ToImmutable();
    }

    private bool EvaluateConditions(ScriptUnit unit)
    {
        foreach (var or in unit.Script.OrConditions)
        {
            if (or.AndConditions.IsDefaultOrEmpty) continue;
            var all = true;
            foreach (var condition in or.AndConditions)
            {
                if (EvaluateCondition(condition)) continue;
                all = false;
                break;
            }
            if (all) return true;
        }
        return false;
    }

    private void ExecuteActions(ImmutableArray<MapScriptAction> actions)
    {
        if (actions.IsDefaultOrEmpty) return;
        foreach (var action in actions) ExecuteAction(action);
    }

    /// <summary><c>ScriptEngine::callSubroutine</c>: a group runs all its scripts when it is an active subroutine group; a script runs when it is a subroutine script.</summary>
    private void CallSubroutine(string name)
    {
        if (_depth >= MaxSubroutineDepth)
        {
            Diagnose($"subroutine recursion limit at '{name}'");
            return;
        }
        _depth++;
        try
        {
            if (_program.FindGroup(name) is { } group)
            {
                if (!group.Group.IsSubroutine) Diagnose($"call of non-subroutine group '{name}'");
                else if (_groupActive[group.Index])
                    foreach (var index in group.ScriptIndices)
                        if (!_program.Units[index].IsSubroutine) ExecuteScript(_program.Units[index]);
            }
            else if (_program.FindScript(name) is { } script)
            {
                if (script.IsSubroutine) ExecuteScript(script);
                else Diagnose($"call of non-subroutine script '{name}'");
            }
            else Diagnose($"call of undefined script '{name}'");
        }
        finally { _depth--; }
    }

    /// <summary>
    /// <c>ScriptEngine::checkConditionsForTeamNames</c>: the team a script's conditions iterate over — a multi-instance prototype wins over a singleton.
    /// </summary>
    private string? FindConditionTeam(MapScript script)
    {
        string? singleton = null;
        string? multi = null;
        foreach (var or in script.OrConditions)
            foreach (var condition in or.AndConditions)
                foreach (var parameter in condition.Parameters)
                {
                    if (parameter.Type != MapScriptParameterType.Team) continue;
                    var prototype = _program.FindTeam(parameter.Text);
                    if (prototype is null) continue;
                    if (prototype.IsSingleton || prototype.MaxInstances < 2) singleton = parameter.Text;
                    else multi ??= parameter.Text;
                }
        return multi ?? singleton;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private static uint NameHash(string name)
    {
        var hash = 2166136261u;
        foreach (var character in name) hash = unchecked((hash ^ character) * 16777619u);
        return hash;
    }

    private void Diagnose(string message) => _diagnostics[message] = _diagnostics.GetValueOrDefault(message) + 1;

    private void Output(string action, params string[] arguments) => _outputs.Add(new ScriptOutput(_tick, action, [..arguments]));

    private Counter CounterFor(string name)
    {
        if (!_counters.TryGetValue(name, out var counter)) _counters[name] = counter = new Counter();
        return counter;
    }

    private static bool Compare(int comparison, long left, long right) => comparison switch
    {
        0 => left < right,
        1 => left <= right,
        2 => left == right,
        3 => left >= right,
        4 => left > right,
        5 => left != right,
        _ => false,
    };

    private ScriptPlayer? ResolvePlayer(string name)
    {
        if (name == ThisPlayerName) return _currentList < _program.Players.Length ? _program.Players[_currentList] : null;
        if (name == LocalPlayerName) return PlayerById(_options.LocalPlayer);
        var player = _program.FindPlayer(name);
        if (player is null) Diagnose($"unknown player '{name}'");
        return player;
    }

    private ScriptPlayer? PlayerById(PlayerId id) => id.Value >= 0 && id.Value < _program.Players.Length ? _program.Players[id.Value] : null;

    private ScriptTeam? FindTeam(string name)
    {
        if (name == ThisTeamName) return _callingTeam;
        // ScriptEngine::getTeamNamed: the instance the running script/sequence acts for wins over any other instance of the prototype.
        if (_callingTeam is { } calling && calling.Prototype == name) return calling;
        foreach (var team in _teams.Values)
            if (team.Prototype == name) return team;
        return null;
    }

    /// <summary><c>TeamFactory::createTeam</c>: a new instance of a prototype, or the existing one of a singleton; <see langword="null"/> for an unknown prototype.</summary>
    private ScriptTeam? CreateTeam(string prototypeName)
    {
        var prototype = _program.FindTeam(prototypeName);
        if (prototype is null)
        {
            Diagnose($"unknown team '{prototypeName}'");
            return null;
        }
        if ((prototype.IsSingleton || prototype.MaxInstances < 2) && FindTeam(prototypeName) is { } existing) return existing;
        var owner = _program.FindPlayer(prototype.Owner);
        var team = new ScriptTeam(_nextTeamId++, prototypeName, owner is null ? PlayerId.System : owner.Id);
        _teams[team.Id] = team;
        return team;
    }

    private Entity? NamedEntity(string name)
    {
        if (name == ThisObjectName) return _callingObject is { DestroyRequested: false } ? _callingObject : null;
        if (!_named.TryGetValue(name, out var id)) return null;
        var entity = _world.FindEntity(new EntityId(id));
        return entity is null || entity.DestroyRequested ? null : entity;
    }

    /// <summary>The members that exist in the world (objects requested this tick are not registered yet and are skipped, not forgotten; <see cref="PruneTeams"/> forgets the gone ones).</summary>
    private IEnumerable<Entity> LiveMembers(ScriptTeam team)
    {
        foreach (var id in team.Members)
            if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false } entity) yield return entity;
    }

    /// <summary>Forgets team members that no longer exist; only ids allocated before the previous update count, as those have been through a world step and are registered if they exist at all.</summary>
    private void PruneTeams()
    {
        // TheScriptEngine->didUnitExist: a name counts as having existed once its object was alive in the world. A requested spawn registers at the end of the
        // tick, so the first sweep after that records it; an object that dies and vanishes later is then "destroyed", one that was never created is not.
        foreach (var (name, id) in _named)
            if (!_everNamed.Contains(name) && _world.FindEntity(new EntityId(id)) is not null) _everNamed.Add(name);
        foreach (var team in _teams.Values)
            team.Members.RemoveAll(id => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null);
        foreach (var gone in _lastDamage.Keys.Where(id => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null).ToArray()) _lastDamage.Remove(gone);
        foreach (var gone in _lastEntered.Keys.Where(id => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null).ToArray()) _lastEntered.Remove(gone);
        foreach (var gone in _prioritySetOf.Keys.Where(id => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null).ToArray()) _prioritySetOf.Remove(gone);
        foreach (var gone in _completedPath.Keys.Where(id => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null).ToArray()) _completedPath.Remove(gone);
    }

    internal static bool CountsAsObject(Entity entity)
        => !entity.EffectivelyDead && !entity.DestroyRequested
           && !entity.Template.IsKindOf("PROJECTILE") && !entity.Template.IsKindOf("INERT") && !entity.Template.IsKindOf("MINE");

    private IEnumerable<Entity> PlayerEntities(PlayerId player)
        => _world.Entities.Where(entity => entity.Owner == player && CountsAsObject(entity)).OrderBy(entity => entity.Id.Value);

    // ------------------------------------------------------------------------------------------------ state

    private static readonly ImmutableArray<string> Empty = [];

    /// <summary>Writes the complete engine state in canonical order (every collection sorted).</summary>
    public void Describe(IStateWriter writer)
    {
        writer.Text("program", _program.Fingerprint);
        writer.Int("tick", _tick);
        writer.Int("outcome", (int)Outcome);
        writer.Int("outcomeTick", OutcomeTick);
        writer.Text("outcomeScript", OutcomeScript);
        writer.Int("endTimer", _endTimer);
        writer.Bool("ended", IsEnded);
        writer.Int("nextTeamId", _nextTeamId);
        writer.UInt("pruneBelow", _pruneBelow);
        writer.Int("executedScripts", _executedScripts);
        writer.Int("executedActions", _executedActions);
        writer.Int("forwardedActions", _forwardedActions);
        writer.BeginArray("scripts");
        for (var index = 0; index < _scriptActive.Length; index++)
        {
            writer.BeginObject(null);
            writer.Bool("active", _scriptActive[index]);
            writer.Int("next", _nextEvaluation[index]);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("groups");
        foreach (var active in _groupActive)
        {
            writer.BeginObject(null);
            writer.Bool("active", active);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("counters");
        foreach (var (name, counter) in _counters)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("value", counter.Value);
            writer.Bool("timer", counter.IsTimer);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("flags");
        foreach (var (name, value) in _flags)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Bool("value", value);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("named");
        foreach (var (name, id) in _named)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.UInt("id", id);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("everNamed");
        foreach (var name in _everNamed)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("objectLists");
        foreach (var (name, types) in _objectLists)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.BeginArray("types");
            foreach (var type in types)
            {
                writer.BeginObject(null);
                writer.Text("type", type);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("teams");
        foreach (var team in _teams.Values)
        {
            writer.BeginObject(null);
            writer.Int("id", team.Id);
            writer.Text("prototype", team.Prototype);
            writer.Int("owner", team.Owner.Value);
            writer.Bool("created", team.Created);
            writer.Text("attackPriority", team.AttackPriority);
            writer.BeginArray("members");
            foreach (var member in team.Members.Order())
            {
                writer.BeginObject(null);
                writer.UInt("id", member);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("followers");
        foreach (var follower in _followers.Values)
        {
            writer.BeginObject(null);
            writer.UInt("entity", follower.Entity);
            writer.Text("path", follower.Path);
            writer.Int("index", follower.Index);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("lastDamage");
        foreach (var (victim, damage) in _lastDamage)
        {
            writer.BeginObject(null);
            writer.UInt("victim", victim);
            writer.Int("owner", damage.Owner);
            writer.Text("template", damage.Template);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("entered");
        foreach (var (container, player) in _lastEntered)
        {
            writer.BeginObject(null);
            writer.UInt("container", container);
            writer.Int("player", player);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("prioritySets");
        foreach (var (name, set) in _prioritySets)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("default", set.Default);
            writer.BeginArray("rules");
            foreach (var (isKind, ruleName, priority) in set.Rules)
            {
                writer.BeginObject(null);
                writer.Bool("kind", isKind);
                writer.Text("type", ruleName);
                writer.Int("priority", priority);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("prioritySetOf");
        foreach (var (entity, set) in _prioritySetOf)
        {
            writer.BeginObject(null);
            writer.UInt("entity", entity);
            writer.Text("set", set);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("completedPaths");
        foreach (var (entity, path) in _completedPath)
        {
            writer.BeginObject(null);
            writer.UInt("entity", entity);
            writer.Text("path", path);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("presentation");
        foreach (var (name, until) in _pendingPresentation)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("until", until);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("areaState");
        foreach (var (key, inside) in _areaInside)
        {
            writer.BeginObject(null);
            writer.Text("key", key);
            writer.Bool("inside", inside);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("missingConditions");
        foreach (var (name, count) in _missingConditions)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("missingActions");
        foreach (var (name, count) in _missingActions)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
        DescribeExtended(writer);
        writer.BeginArray("diagnostics");
        foreach (var (name, count) in _diagnostics)
        {
            writer.BeginObject(null);
            writer.Text("name", name);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
    }

    /// <summary>Restores the state written by <see cref="Describe"/> into a freshly constructed engine of the same program.</summary>
    public void Load(IStateReader reader)
    {
        if (reader.Text("program") != _program.Fingerprint) throw new InvalidDataException("The saved script state belongs to a different map script program.");
        _tick = reader.Int("tick");
        var outcome = checked((int)reader.Int("outcome"));
        if (!Enum.IsDefined((MissionOutcome)outcome)) throw new InvalidDataException("Unknown mission outcome in script state.");
        Outcome = (MissionOutcome)outcome;
        OutcomeTick = reader.Int("outcomeTick");
        OutcomeScript = reader.Text("outcomeScript");
        _endTimer = reader.Int("endTimer");
        IsEnded = reader.Bool("ended");
        _nextTeamId = checked((int)reader.Int("nextTeamId"));
        _pruneBelow = checked((uint)reader.UInt("pruneBelow"));
        _executedScripts = reader.Int("executedScripts");
        _executedActions = reader.Int("executedActions");
        _forwardedActions = reader.Int("forwardedActions");
        var scripts = reader.Array("scripts");
        if (scripts.Count != _scriptActive.Length) throw new InvalidDataException("Script count mismatch in script state.");
        for (var index = 0; index < scripts.Count; index++)
        {
            _scriptActive[index] = scripts[index].Bool("active");
            _nextEvaluation[index] = scripts[index].Int("next");
        }
        var groups = reader.Array("groups");
        if (groups.Count != _groupActive.Length) throw new InvalidDataException("Group count mismatch in script state.");
        for (var index = 0; index < groups.Count; index++) _groupActive[index] = groups[index].Bool("active");
        _counters.Clear();
        foreach (var item in reader.Array("counters")) _counters[item.Text("name")] = new Counter { Value = item.Int("value"), IsTimer = item.Bool("timer") };
        _flags.Clear();
        foreach (var item in reader.Array("flags")) _flags[item.Text("name")] = item.Bool("value");
        _named.Clear();
        foreach (var item in reader.Array("named")) _named[item.Text("name")] = checked((uint)item.UInt("id"));
        _everNamed.Clear();
        foreach (var item in reader.Array("everNamed")) _everNamed.Add(item.Text("name"));
        _objectLists.Clear();
        foreach (var item in reader.Array("objectLists"))
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in item.Array("types")) types.Add(type.Text("type"));
            _objectLists[item.Text("name")] = types;
        }
        _teams.Clear();
        foreach (var item in reader.Array("teams"))
        {
            var team = new ScriptTeam(checked((int)item.Int("id")), item.Text("prototype"), new PlayerId(checked((int)item.Int("owner")))) { Created = item.Bool("created"), AttackPriority = item.Text("attackPriority") };
            foreach (var member in item.Array("members")) team.Members.Add(checked((uint)member.UInt("id")));
            _teams[team.Id] = team;
        }
        _followers.Clear();
        foreach (var item in reader.Array("followers"))
            _followers[checked((uint)item.UInt("entity"))] = new Follower { Entity = checked((uint)item.UInt("entity")), Path = item.Text("path"), Index = checked((int)item.Int("index")) };
        _lastDamage.Clear();
        foreach (var item in reader.Array("lastDamage")) _lastDamage[checked((uint)item.UInt("victim"))] = new LastDamage(checked((int)item.Int("owner")), item.Text("template"));
        _lastEntered.Clear();
        foreach (var item in reader.Array("entered")) _lastEntered[checked((uint)item.UInt("container"))] = checked((int)item.Int("player"));
        _prioritySets.Clear();
        foreach (var item in reader.Array("prioritySets"))
        {
            var set = new PrioritySet { Default = checked((int)item.Int("default")) };
            foreach (var rule in item.Array("rules")) set.Rules.Add((rule.Bool("kind"), rule.Text("type"), checked((int)rule.Int("priority"))));
            _prioritySets[item.Text("name")] = set;
        }
        _prioritySetOf.Clear();
        foreach (var item in reader.Array("prioritySetOf")) _prioritySetOf[checked((uint)item.UInt("entity"))] = item.Text("set");
        _completedPath.Clear();
        foreach (var item in reader.Array("completedPaths")) _completedPath[checked((uint)item.UInt("entity"))] = item.Text("path");
        _pendingPresentation.Clear();
        foreach (var item in reader.Array("presentation")) _pendingPresentation[item.Text("name")] = item.Int("until");
        _areaInside.Clear();
        foreach (var item in reader.Array("areaState")) _areaInside[item.Text("key")] = item.Bool("inside");
        _missingConditions.Clear();
        foreach (var item in reader.Array("missingConditions")) _missingConditions[item.Text("name")] = item.Int("count");
        _missingActions.Clear();
        foreach (var item in reader.Array("missingActions")) _missingActions[item.Text("name")] = item.Int("count");
        LoadExtended(reader);
        _diagnostics.Clear();
        foreach (var item in reader.Array("diagnostics")) _diagnostics[item.Text("name")] = item.Int("count");
        _outputs.Clear();
        _uiInteractions.Clear();
    }

    /// <summary>A 64-bit hash over <see cref="Describe"/>; two runs that diverge in any script state differ here.</summary>
    public ulong StateHash()
    {
        var hasher = new HashStateWriter();
        Describe(hasher);
        return hasher.Hash;
    }
}
