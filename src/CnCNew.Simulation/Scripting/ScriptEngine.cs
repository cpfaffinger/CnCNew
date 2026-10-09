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
/// <param name="PresentationLatencyTicks">
/// How long a speech/sound/movie/camera move counts as still playing for <c>HAS_FINISHED_*</c> and <c>CAMERA_MOVEMENT_FINISHED</c>; a live client may instead end them with
/// <see cref="ScriptEngine.CompletePresentation"/>. 0: finished as soon as started (headless). <b>Unit: logic frames of the original (1/30 s)</b>, whatever the tick rate of the world
/// is - the engine scales the value when it uses it, so a host states the same number at every rate (the name is kept because <c>MissionSnapshot</c> stores the option under it).
/// </param>
public sealed record ScriptEngineOptions(ScriptDifficulty Difficulty, PlayerId LocalPlayer, int PresentationLatencyTicks = 0, int MissionAttempts = 1)
{
    public static ScriptEngineOptions Default { get; } = new(ScriptDifficulty.Normal, new PlayerId(1));

    /// <summary>The original skirmish AI and the players it runs (<see cref="ScriptEngine.HasAiPlayers"/>); <see langword="null"/> for a mission.</summary>
    public ScriptAiSetup? Ai { get; init; }
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
    public TeamMembers Members { get; } = new();

    /// <summary><c>Team::isCreated</c>: the team has been activated (here: it has ever had a member).</summary>
    public bool Created { get; set; }

    /// <summary><c>Team::setAttackPriorityName</c>: the attack priority set members get, new ones included.</summary>
    public string AttackPriority { get; set; } = string.Empty;

    /// <summary><c>Team::m_isRecruitablitySet</c> / <c>m_isRecruitable</c> (<c>TEAM_AVAILABLE_FOR_RECRUITMENT</c>): when set, overrides the prototype's own recruitability.</summary>
    public bool RecruitableSet { get; set; }

    // ---- state of the team event scripts (Team::updateState); only saved for a team whose prototype has any event script
    internal bool CreateHandled { get; set; }
    internal int CurUnits { get; set; }
    internal int DestroyThreshold { get; set; } = -1;
    internal bool SeeEnemy { get; set; }
    internal bool WasIdle { get; set; }

    public bool Recruitable { get; set; }

    /// <summary><c>Team::m_commonAttackTarget</c>: the victim the team attacks together, 0 for none. Only a team of a player the original skirmish AI runs ever has one.</summary>
    internal uint CommonTarget;

    /// <summary>The victim the team attacks together (<c>Team::getTeamTargetObject</c> without its validity test), or an invalid id.</summary>
    public EntityId CommonAttackTarget => new(CommonTarget);

    // The prototype of this instance once the engine has looked it up (ScriptEngine.PrototypeOf); derived from the program, not state.
    internal ScriptTeamPrototype? Info;
    internal bool InfoKnown;
}

/// <summary>
/// The deterministic World Builder script VM (<c>ScriptEngine::update</c>, <c>ScriptEngine.cpp</c>). One engine runs the scripts of one map against one
/// <see cref="SimulationWorld"/>: call <see cref="Update"/> once per tick <em>before</em> <see cref="SimulationWorld.Step"/>; the engine itself runs on the ticks that begin a
/// logic frame of the original (its clock is the frame, whatever the tick rate of the world - see <see cref="Update"/>). It holds counters, flags,
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
    /// <summary>
    /// <c>FRAMES_TO_SHOW_WIN_LOSE_MESSAGE</c> (<c>ScriptEngine.cpp</c>, <c>ScriptEngine::startEndGameTimer</c>): how long scripts keep running after a victory or defeat before the
    /// game ends, in logic frames of the original (4 s). The countdown runs in updates of the engine, one per frame of the original.
    /// </summary>
    public const int EndGameDelayFrames = 120;

    /// <summary><see cref="EndGameDelayFrames"/> in ticks of this engine's world: how many world ticks pass between the outcome and <see cref="IsEnded"/>.</summary>
    public long EndGameDelay => _world.Time.OriginalFrames(EndGameDelayFrames);

    /// <summary><c>ATTACK_PRIORITY_DEFAULT</c> (<c>ScriptEngine.cpp</c>).</summary>
    public const int DefaultAttackPriority = 1;

    private const int MaxSubroutineDepth = 32;
    private const string ThisPlayerName = "<This Player>";
    private const string LocalPlayerName = "<Local Player>";
    private const string ThisTeamName = "<This Team>";

    /// <summary>
    /// A counter or countdown timer (<c>ScriptEngine::m_counters</c>). A timer's value is in <b>logic frames of the original</b> (1/30 s) at every tick rate, as the map
    /// states it and compares it (<c>COUNTER</c> against a literal, <c>INCREMENT_COUNTER</c> on a timer): the engine counts it down once per update, and it updates once per frame.
    /// </summary>
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
        /// <summary><c>m_moveAsGroup</c> (<c>groupFollowWaypointPathAsTeam</c>): the member keeps its offset from the group centre and follows the team's waypoint.</summary>
        public bool AsTeam;
        public int Team;
        public Fixed OffsetX;
        public Fixed OffsetY;
        /// <summary>The team waypoint the current move order was given for (-1: none yet).</summary>
        public int GoalIndex = -1;
    }

    /// <summary><c>Team::getCurrentWaypoint</c> of the teams that follow a path as a team: the index the first member to arrive advances.</summary>
    private readonly SortedDictionary<int, int> _teamWaypoints = [];

    private readonly ScriptProgram _program;
    private readonly SimulationWorld _world;
    private readonly ScriptEngineOptions _options;
    private readonly ScriptCommandIssuer _issue;

    private readonly bool[] _scriptActive;
    private readonly long[] _nextEvaluation;
    private readonly bool[] _groupActive;
    private readonly string?[] _conditionTeam;

    // What the update walks per script list, in program order: the scripts outside groups that are no subroutines, and the groups that are no subroutine
    // groups. All of it is fixed by the program; whether a script or a group is active is asked when it is reached, as before.
    private readonly ScriptUnit[][] _listScripts;
    private readonly ScriptGroupUnit[][] _listGroups;

    private readonly NamedTable<Counter> _counters = new();
    private readonly NamedTable<bool> _flags = new();
    private readonly SortedDictionary<string, uint> _named = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _everNamed = new(StringComparer.Ordinal);

    // The names of _named that are not in _everNamed yet: the only ones the sweep of PruneTeams has to look at. Derived from the two tables, not state.
    private readonly SortedSet<string> _namedPending = new(StringComparer.Ordinal);
    private readonly List<string> _nameScratch = [];
    private readonly List<uint> _idScratch = [];
    private Predicate<uint>? _memberIsGone;
    private readonly SortedDictionary<string, SortedSet<string>> _objectLists = new(StringComparer.Ordinal);
    private readonly MembershipStamp _membership = new();
    private readonly TeamTable _teams;
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
    // Calls of Update since the last frame began while the world is frozen (BeginsFrame); 0 whenever the world runs, and always at 30 Hz.
    private long _frozenPhase;
    private int _currentList;
    private ScriptTeam? _callingTeam;

    /// <summary>
    /// <c>ScriptEngine::m_conditionTeam</c>: the instance a script's conditions are evaluated for, kept apart from the team a script was called for (<c>m_callingTeam</c>) as the
    /// reference keeps it - in a session of the original skirmish AI. There <c>executeScript</c> leaves the calling team alone, so a team's own script (<c>runScript(name, team)</c>:
    /// on create, on idle, on destroyed ...) still has its team as <c>&lt;This Team&gt;</c> when its conditions name no team. A mission keeps the one variable it always had (and its
    /// reference values): there this field stays <see langword="null"/> and a called script without a condition team has no <c>&lt;This Team&gt;</c>.
    /// </summary>
    private ScriptTeam? _conditionInstance;

    /// <summary><c>getTeamNamed(THIS_TEAM)</c>: "if (m_callingTeam) return m_callingTeam; return m_conditionTeam".</summary>
    private ScriptTeam? ThisTeam => _callingTeam ?? _conditionInstance;

    /// <summary><c>getTeamNamed</c>: the calling team, else the condition team, when it is an instance of the named prototype.</summary>
    private ScriptTeam? ContextInstance(string prototype)
        => _callingTeam is { } calling && calling.Prototype == prototype ? calling : _conditionInstance is { } condition && condition.Prototype == prototype ? condition : null;
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
        _teams = new TeamTable(_membership);
        _scriptActive = new bool[program.Units.Length];
        _nextEvaluation = new long[program.Units.Length];
        _groupActive = new bool[program.Groups.Length];
        _conditionTeam = new string?[program.Units.Length];
        InitializeConditionOrdinals();
        for (var index = 0; index < program.Units.Length; index++)
        {
            var script = program.Units[index].Script;
            _scriptActive[index] = script.IsActive;
            // ScriptEngine::checkConditionsForTeamNames draws the first evaluation frame of a delayed script from the logic random stream; a
            // name-derived offset in the same 0..2 s range keeps the spread without consuming a stream that a restored world would not replay.
            _nextEvaluation[index] = script.DelayEvaluationSeconds > 0 ? _world.Time.OriginalFrames((long)(NameHash(script.Name) % (2 * SimulationRate.OriginalFramesPerSecond + 1))) : 0;
            _conditionTeam[index] = FindConditionTeam(script);
        }
        for (var index = 0; index < program.Groups.Length; index++) _groupActive[index] = program.Groups[index].Group.IsActive;
        _listScripts = new ScriptUnit[program.ListCount][];
        _listGroups = new ScriptGroupUnit[program.ListCount][];
        for (var list = 0; list < program.ListCount; list++)
        {
            var scripts = new List<ScriptUnit>();
            foreach (var unit in program.Units)
                if (unit.ListIndex == list && unit.GroupIndex < 0 && !unit.IsSubroutine) scripts.Add(unit);
            var groups = new List<ScriptGroupUnit>();
            foreach (var group in program.Groups)
                if (group.ListIndex == list && !group.Group.IsSubroutine) groups.Add(group);
            _listScripts[list] = [..scripts];
            _listGroups[list] = [..groups];
        }
        foreach (var prototype in program.Teams)
            if (prototype.IsSingleton || prototype.MaxInstances < 2) CreateTeam(prototype.Name, runStartActions: false);
        InitializeAi();
    }

    public ScriptProgram Program => _program;

    /// <summary>A tool's measuring hook (<see cref="IScriptObserver"/>); <see langword="null"/> unless a tool installs one. Not part of the state: neither hashed nor saved.</summary>
    public IScriptObserver? Observer { get; set; }
    public MissionOutcome Outcome { get; private set; }

    /// <summary>The tick the outcome was decided (<c>VICTORY</c>/<c>DEFEAT</c> executed), or -1.</summary>
    public long OutcomeTick { get; private set; } = -1;

    /// <summary>The script whose <c>VICTORY</c>/<c>DEFEAT</c> action decided the mission (empty when none did).</summary>
    public string OutcomeScript { get; private set; } = string.Empty;

    /// <summary>The end-game delay is over: the script engine no longer runs and the client should leave the mission.</summary>
    public bool IsEnded { get; private set; }

    /// <summary>
    /// <c>FREEZE_TIME</c> ran and <c>UNFREEZE_TIME</c> has not (<c>ScriptEngine::m_freezeByScript</c>). <c>GameLogic::update</c> runs the scripts and then returns while it is set: objects, combat and the frame counter
    /// stand still, script timers and sequential scripts go on. <see cref="CnCNew.Simulation.Mission.MissionSession.Step"/> does the same; the end of the mission lifts it
    /// (<c>forceUnfreezeTime</c> on <c>MSG_CLEAR_GAME_DATA</c>).
    /// </summary>
    public bool TimeFrozen { get; private set; }

    public long ExecutedScripts => _executedScripts;
    public long ExecutedActions => _executedActions;
    public long ForwardedActions => _forwardedActions;

    /// <summary>Executions of condition types without implementation, by type name (the condition evaluated to false each time).</summary>
    public IReadOnlyDictionary<string, long> MissingConditionHits => _missingConditions;

    /// <summary>Executions of action types without implementation, by type name (nothing happened each time).</summary>
    public IReadOnlyDictionary<string, long> MissingActionHits => _missingActions;

    /// <summary>Unresolvable references met at runtime (unknown player/team/waypoint/area/template, malformed parameter lists, recursion limit); never empty by accident.</summary>
    public IReadOnlyDictionary<string, long> Diagnostics => _diagnostics;

    /// <summary>The value of a counter; for a countdown timer (<see cref="CounterIsTimer"/>) the remaining logic frames of the original (1/30 s) at every tick rate, -1 once it has run out.</summary>
    public long CounterValue(string name) => _counters.TryGetValue(name, out var counter) ? counter.Value : 0;
    public bool CounterIsTimer(string name) => _counters.TryGetValue(name, out var counter) && counter.IsTimer;
    public bool FlagValue(string name) => _flags.GetValueOrDefault(name);
    /// <summary>The templates of an object-type list built by <c>OBJECTLIST_ADDOBJECTTYPE</c>, or empty when the name is no list (then the parameter is a single template name).</summary>
    public IReadOnlyCollection<string> ObjectListTypes(string name) => _objectLists.TryGetValue(name, out var list) ? list : [];
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
        if (_world.FindEntity(id) is not null)
        {
            _everNamed.Add(name);
            _namedPending.Remove(name);
        }
        else if (!_everNamed.Contains(name)) _namedPending.Add(name);
    }

    /// <summary>Puts an object into a team instance (map objects join the singleton team of their <c>originalOwner</c>).</summary>
    public void AssignToTeam(string prototype, EntityId id)
    {
        var team = FindTeam(prototype) ?? CreateTeam(prototype, runStartActions: false);
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

    /// <summary>Whether the client reported the object with this script name as selected (what <c>NAMED_SELECTED</c> reads).</summary>
    public bool IsNamedSelected(string name) => _named.TryGetValue(name, out var id) && _selected.Contains(id);

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

    /// <summary>
    /// <c>ScriptEngine::update</c>: countdown timers, then every side's scripts in list order, then the followers. Call once per tick before the world step.
    /// <para>
    /// The reference runs this once per logic frame (<c>GameLogic::update</c>), and maps are written against that: timers and frame counts are compared with literals, scripts
    /// count their own evaluations, edge conditions remember the previous evaluation. None of that can be rescaled to another step, so the engine keeps the frame of the
    /// original as its clock: it does its work on the ticks that begin one (every tick at 30 Hz, every second at 60 Hz, every fourth at 120 Hz) and nothing on the ticks
    /// between. What the world does between two updates reaches the engine through <see cref="Observe"/>, which the session calls after every world step.
    /// </para>
    /// </summary>
    public void Update(long tick)
    {
        _tick = tick;
        _busyThisTick.Clear();
        if (!BeginsFrame(tick))
        {
            // The one thing that cannot wait for the frame: an object that appears and vanishes between two updates still has existed (didUnitExist).
            NoteNamedObjectsThatExist();
            return;
        }
        if (_endTimer > 0)
        {
            _endTimer--;
            if (_endTimer < 1) IsEnded = true;
        }
        if (_endTimer >= 0) return;
        var observer = Observer;
        observer?.SectionStarted(ScriptSection.Housekeeping);
        if (_pendingPresentation.Count > 0)
            foreach (var name in _pendingPresentation.Where(pair => pair.Value <= tick).Select(pair => pair.Key).ToArray()) _pendingPresentation.Remove(name);

        PruneTeams();
        // Every timer is counted down once; the order does not matter, so the walk takes the cheap one.
        foreach (var counter in _counters.Unordered)
            if (counter.IsTimer && counter.Value >= 0) counter.Value--;
        observer?.SectionEnded(ScriptSection.Housekeeping);

        observer?.SectionStarted(ScriptSection.Scripts);
        for (var list = 0; list < _program.ListCount; list++)
        {
            _currentList = list;
            foreach (var unit in _listScripts[list]) ExecuteScript(unit);
            foreach (var group in _listGroups[list])
            {
                if (!_groupActive[group.Index]) continue;
                foreach (var index in group.ScriptIndices)
                    if (!_program.Units[index].IsSubroutine) ExecuteScript(_program.Units[index]);
            }
        }
        observer?.SectionEnded(ScriptSection.Scripts);
        observer?.SectionStarted(ScriptSection.AiPlayers);
        UpdateAiPlayers();
        observer?.SectionEnded(ScriptSection.AiPlayers);
        _uiInteractions.Clear();
        if (observer is null)
        {
            UpdateFollowers();
            UpdateButtonHunts();
            ProcessTeamEvents();
            ProcessSequences();
            ProcessTeamBuilds();
            UpdateTeamQueues();
            UpdatePhase13(tick);
        }
        else UpdateTailObserved(observer, tick);
        _pruneBelow = _world.Ids.NextEntity;
    }

    /// <summary>
    /// Whether this call of <see cref="Update"/> begins a logic frame of the original. While the world runs that is a question of its tick. Under <c>FREEZE_TIME</c> the tick stands
    /// still and the host goes on calling at the tick rate (<see cref="CnCNew.Simulation.Mission.MissionSession.Step"/>), so the engine counts those calls itself: script timers
    /// keep their length in game time while the world is frozen. The freeze begins and ends in an update, hence on a frame; at 30 Hz the phase is always 0.
    /// </summary>
    private bool BeginsFrame(long tick)
    {
        if (!TimeFrozen) return _world.Time.IsOriginalFrameBoundary(tick);
        _frozenPhase = (_frozenPhase + TickOrder.Next) % _world.Time.TicksPerOriginalFrame;
        return _frozenPhase == 0;
    }

    /// <summary>The same calls in the same order as the tail of <see cref="Update"/>, each reported to the observer.</summary>
    private void UpdateTailObserved(IScriptObserver observer, long tick)
    {
        observer.SectionStarted(ScriptSection.Followers);
        UpdateFollowers();
        observer.SectionEnded(ScriptSection.Followers);
        observer.SectionStarted(ScriptSection.ButtonHunts);
        UpdateButtonHunts();
        observer.SectionEnded(ScriptSection.ButtonHunts);
        observer.SectionStarted(ScriptSection.TeamEvents);
        ProcessTeamEvents();
        observer.SectionEnded(ScriptSection.TeamEvents);
        observer.SectionStarted(ScriptSection.Sequences);
        ProcessSequences();
        observer.SectionEnded(ScriptSection.Sequences);
        observer.SectionStarted(ScriptSection.TeamBuilds);
        ProcessTeamBuilds();
        observer.SectionEnded(ScriptSection.TeamBuilds);
        observer.SectionStarted(ScriptSection.TeamQueues);
        UpdateTeamQueues();
        observer.SectionEnded(ScriptSection.TeamQueues);
        observer.SectionStarted(ScriptSection.WorldOrders);
        UpdatePhase13(tick);
        observer.SectionEnded(ScriptSection.WorldOrders);
    }

    private void ExecuteScript(ScriptUnit unit)
    {
        // The three tests the body begins with, asked before anything is set up: most scripts of a frame end here.
        if (!_scriptActive[unit.Index] || _tick < _nextEvaluation[unit.Index]) return;
        var script = unit.Script;
        if (!AllowedOn(script, _listDifficulty[unit.ListIndex])) return;
        var outerScript = _runningScript;
        _runningScript = script.Name;
        try { ExecuteScriptBody(unit, script); }
        finally { _runningScript = outerScript; }
    }

    private void ExecuteScriptBody(ScriptUnit unit, MapScript script)
    {
        if (!_scriptActive[unit.Index]) return;
        if (!AllowedOn(script, _listDifficulty[unit.ListIndex])) return;
        if (_tick < _nextEvaluation[unit.Index]) return;
        if (script.DelayEvaluationSeconds > 0) _nextEvaluation[unit.Index] = _tick + _world.Time.WholeSecondsToTicks((long)script.DelayEvaluationSeconds);

        var previousTeam = _callingTeam;
        var previousCondition = _conditionInstance;
        // The instances of the team the conditions talk about, as they are before the first evaluation (in id order): none, one, or - rarely - several.
        ScriptTeam? onlyInstance = null;
        List<ScriptTeam>? instances = null;
        if (_conditionTeam[unit.Index] is { } conditionTeam)
        {
            var existing = _teams.InstancesOf(conditionTeam);
            if (existing.Count == 1) onlyInstance = existing[0];
            else if (existing.Count > 1) instances = [..existing];
        }
        if (instances is not null)
        {
            // One evaluation per instance; the actions of that round act for that instance.
            foreach (var instance in instances) RunForInstance(unit, script, instance);
        }
        else if (onlyInstance is not null) RunForInstance(unit, script, onlyInstance);
        else
        {
            if (_ai.Count > 0) _conditionInstance = null;
            else _callingTeam = null;
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
        _conditionInstance = previousCondition;
    }

    private void RunForInstance(ScriptUnit unit, MapScript script, ScriptTeam instance)
    {
        if (_ai.Count > 0) _conditionInstance = instance;
        else _callingTeam = instance;
        if (EvaluateConditions(unit))
        {
            _executedScripts++;
            ExecuteActions(script.Actions);
            if (script.IsOneShot) _scriptActive[unit.Index] = false;
        }
        else if (!script.FalseActions.IsDefaultOrEmpty) ExecuteActions(script.FalseActions);
    }

    /// <summary>
    /// Evaluates every condition of a script once, outside the normal schedule, and reports each result (<c>branch.condition</c> labels). A diagnostic for tools
    /// and tests that leaves the engine untouched: the memory edge-triggered conditions keep (entered/exited area, lost object type, a consumed special-power
    /// trigger), the counters a read creates and the diagnostic tallies are restored afterwards, so a run that was explained continues exactly as one that was not.
    /// An edge condition therefore reports its edge only if the engine's own next evaluation would see it.
    /// </summary>
    public ImmutableArray<(string Label, bool Result)> ExplainScript(string name)
    {
        if (_program.FindScript(name) is not { } unit) return [];
        var result = ImmutableArray.CreateBuilder<(string, bool)>();
        var team = _conditionTeam[unit.Index] is { } prototype ? FindTeam(prototype) : null;
        var previous = _callingTeam;
        var areaMemory = new Dictionary<string, bool>(_areaInside, StringComparer.Ordinal);
        var counterMemory = _counters.ToDictionary(pair => pair.Key, pair => (pair.Value.Value, pair.Value.IsTimer), StringComparer.Ordinal);
        var powerMemory = _triggeredPowers.ToArray();
        var completedPowerMemory = _completedPowers.ToArray();
        var completedUpgradeMemory = _completedUpgrades.ToArray();
        var transportMemory = _transportStatus.ToDictionary(pair => pair.Key, pair => pair.Value);
        var techMemory = _techBuildingAnswers.ToDictionary(pair => pair.Key, pair => pair.Value);
        var missingMemory = new Dictionary<string, long>(_missingConditions, StringComparer.Ordinal);
        var diagnosticMemory = new Dictionary<string, long>(_diagnostics, StringComparer.Ordinal);
        var conditionMemory = _conditionMemory.ToArray();
        var outerOrdinal = _conditionOrdinal;
        _callingTeam = team;
        try
        {
            var ordinal = _conditionBase[unit.Index];
            for (var branch = 0; branch < unit.Script.OrConditions.Length; branch++)
                for (var index = 0; index < unit.Script.OrConditions[branch].AndConditions.Length; index++)
                {
                    var condition = unit.Script.OrConditions[branch].AndConditions[index];
                    _conditionOrdinal = ordinal++;
                    result.Add((InvariantText.Format($"{branch}.{index} {condition.TypeName}"), EvaluateCondition(condition)));
                }
        }
        finally
        {
            _callingTeam = previous;
            _conditionOrdinal = outerOrdinal;
            _conditionMemory.Clear();
            foreach (var (key, value) in conditionMemory) _conditionMemory[key] = value;
            Restore(_areaInside, areaMemory);
            Restore(_missingConditions, missingMemory);
            Restore(_diagnostics, diagnosticMemory);
            _counters.Clear();
            foreach (var (key, (value, isTimer)) in counterMemory) _counters[key] = new Counter { Value = value, IsTimer = isTimer };
            _triggeredPowers.Clear();
            _triggeredPowers.AddRange(powerMemory);
            _completedPowers.Clear();
            _completedPowers.AddRange(completedPowerMemory);
            _completedUpgrades.Clear();
            _completedUpgrades.AddRange(completedUpgradeMemory);
            _transportStatus.Clear();
            foreach (var (key, value) in transportMemory) _transportStatus[key] = value;
            _techBuildingAnswers.Clear();
            foreach (var (key, value) in techMemory) _techBuildingAnswers[key] = value;
        }
        return result.ToImmutable();

        static void Restore<TValue>(SortedDictionary<string, TValue> target, Dictionary<string, TValue> saved)
        {
            target.Clear();
            foreach (var (key, value) in saved) target[key] = value;
        }
    }

    private bool EvaluateConditions(ScriptUnit unit)
    {
        var outer = _conditionOrdinal;
        var ordinal = _conditionBase[unit.Index];
        try
        {
            foreach (var or in unit.Script.OrConditions)
            {
                if (or.AndConditions.IsDefaultOrEmpty) continue;
                var all = true;
                for (var index = 0; index < or.AndConditions.Length; index++)
                {
                    _conditionOrdinal = ordinal + index;
                    if (EvaluateCondition(or.AndConditions[index])) continue;
                    all = false;
                    break;
                }
                if (all) return true;
                ordinal += or.AndConditions.Length;
            }
            return false;
        }
        finally { _conditionOrdinal = outer; }
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

    /// <summary>Diagnoses are part of the hashed state: numbers inside them are written with the invariant culture (<see cref="InvariantText"/>).</summary>
    private void Diagnose(InvariantText message) => Diagnose(message.ToStringAndClear());

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
        if (name == ThisPlayerName) return CurrentScriptPlayer();
        if (_ai.Count > 0 && name == ThisPlayerEnemyName) return SkirmishEnemy();
        if (name == LocalPlayerName) return PlayerById(_options.LocalPlayer);
        var player = _program.FindPlayer(name);
        if (player is null) Diagnose(UnknownMessage(_unknownPlayerMessages, "player", name));
        return player;
    }

    private ScriptPlayer? PlayerById(PlayerId id) => id.Value >= 0 && id.Value < _program.Players.Length ? _program.Players[id.Value] : null;

    /// <summary>
    /// <c>ScriptEngine::getTeamNamed</c>: the instance the running script/sequence acts for wins over any other instance of the prototype; a singleton team that is not active yet
    /// (<see cref="ScriptTeam.Created"/>: it is still being built or recruited, or nothing ever joined it) is <see langword="null"/>; for any other prototype the first instance.
    /// </summary>
    private ScriptTeam? FindTeam(string name)
    {
        if (name == ThisTeamName) return ThisTeam;
        if (ContextInstance(name) is { } context) return context;
        var team = FindTeamInstance(name);
        return team is not null && !team.Created && _program.FindTeam(name) is { IsSingleton: true } ? null : team;
    }

    /// <summary>The first instance of a prototype whatever its state: the lookup that creates, builds and recruits into a team (<c>TeamPrototype::getFirstItemIn_TeamInstanceList</c>).</summary>
    private ScriptTeam? FindTeamInstance(string name)
    {
        var instances = _teams.InstancesOf(name);
        return instances.Count > 0 ? instances[0] : null;
    }

    /// <summary>
    /// <c>TeamFactory::createTeam</c>/<c>createInactiveTeam</c>: a new instance of a prototype, or the existing one of a singleton; <see langword="null"/> for an unknown prototype. A prototype with
    /// <c>teamExecutesActionsOnCreate</c> also runs the true-actions of its <c>teamProductionCondition</c> script each time it is created, existing singleton included
    /// (<paramref name="runStartActions"/> is off for the map load, where objects join their teams before any script ran).
    /// </summary>
    private ScriptTeam? CreateTeam(string prototypeName, bool runStartActions = true)
    {
        var prototype = _program.FindTeam(prototypeName);
        if (prototype is null)
        {
            Diagnose($"unknown team '{prototypeName}'");
            return null;
        }
        ScriptTeam team;
        if ((prototype.IsSingleton || prototype.MaxInstances < 2) && FindTeamInstance(prototypeName) is { } existing) team = existing;
        else
        {
            var owner = _program.FindPlayer(prototype.Owner);
            team = new ScriptTeam(_nextTeamId++, prototypeName, owner is null ? PlayerId.System : owner.Id);
            team.Members.Stamp = _membership;
            _teams[team.Id] = team;
        }
        if (runStartActions && prototype.ExecutesActionsOnCreate && prototype.ProductionCondition.Length > 0)
        {
            if (_program.FindScript(prototype.ProductionCondition) is not { } script) Diagnose($"team '{prototypeName}': production condition script '{prototype.ProductionCondition}' is missing");
            else if (_depth >= MaxSubroutineDepth) Diagnose($"subroutine recursion limit at team '{prototypeName}'");
            else
            {
                _depth++;
                try { ExecuteActions(script.Script.Actions); }
                finally { _depth--; }
            }
        }
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
        NoteNamedObjectsThatExist();
        _memberIsGone ??= IsGone;
        foreach (var team in _teams.Values)
            if (team.Members.Count > 0) team.Members.RemoveAll(_memberIsGone);
        PruneGone(_lastDamage);
        PruneGone(_lastEntered);
        PruneGone(_prioritySetOf);
        PruneGone(_completedPath);
    }

    /// <summary>
    /// <c>TheScriptEngine->didUnitExist</c>: a name counts as having existed once its object was alive in the world. A requested spawn registers at the end of the
    /// tick, so the first look after that records it; an object that dies and vanishes later is then "destroyed", one that was never created is not. Looked at on
    /// every tick, also on those between two frames of the original: what the world shows there may be gone by the next update.
    /// </summary>
    private void NoteNamedObjectsThatExist()
    {
        if (_namedPending.Count == 0) return;
        _nameScratch.Clear();
        foreach (var name in _namedPending)
            if (_world.FindEntity(new EntityId(_named[name])) is not null) _nameScratch.Add(name);
        foreach (var name in _nameScratch)
        {
            _everNamed.Add(name);
            _namedPending.Remove(name);
        }
    }

    /// <summary>An id allocated before the previous update whose object is not in the world (any more).</summary>
    private bool IsGone(uint id) => id < _pruneBelow && _world.FindEntity(new EntityId(id)) is null;

    private void PruneGone<TValue>(SortedDictionary<uint, TValue> table)
    {
        if (table.Count == 0) return;
        _idScratch.Clear();
        foreach (var id in table.Keys)
            if (IsGone(id)) _idScratch.Add(id);
        foreach (var id in _idScratch) table.Remove(id);
    }

    internal static bool CountsAsObject(Entity entity)
        => !entity.EffectivelyDead && !entity.DestroyRequested
           && !entity.Template.IsKindOf("PROJECTILE") && !entity.Template.IsKindOf("INERT") && !entity.Template.IsKindOf("MINE");

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
        if (TimeFrozen) writer.Bool("timeFrozen", true);
        if (_frozenPhase != 0) writer.Int("frozenPhase", _frozenPhase);
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
            if (team.RecruitableSet) writer.Bool("recruitable", team.Recruitable);
            if (team.CommonTarget != 0) writer.UInt("commonTarget", team.CommonTarget);
            if (_program.FindTeam(team.Prototype) is { HasEventScripts: true })
            {
                writer.Bool("eventsCreateHandled", team.CreateHandled);
                writer.Int("eventsCurUnits", team.CurUnits);
                writer.Int("eventsThreshold", team.DestroyThreshold);
                writer.Bool("eventsSeeEnemy", team.SeeEnemy);
                writer.Bool("eventsWasIdle", team.WasIdle);
            }
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
            if (follower.AsTeam)
            {
                writer.Bool("asTeam", true);
                writer.Int("team", follower.Team);
                writer.Fixed("offsetX", follower.OffsetX);
                writer.Fixed("offsetY", follower.OffsetY);
                writer.Int("goalIndex", follower.GoalIndex);
            }
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
        DescribeAi(writer);
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
        TimeFrozen = reader.Has("timeFrozen") && reader.Bool("timeFrozen");
        _frozenPhase = reader.Has("frozenPhase") ? reader.Int("frozenPhase") : 0;
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
        _namedPending.Clear();
        foreach (var name in _named.Keys)
            if (!_everNamed.Contains(name)) _namedPending.Add(name);
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
            var team = new ScriptTeam(checked((int)item.Int("id")), item.Text("prototype"), new PlayerId(checked((int)item.Int("owner")))) { Created = item.Bool("created"), AttackPriority = item.Text("attackPriority"), RecruitableSet = item.Has("recruitable"), Recruitable = item.Has("recruitable") && item.Bool("recruitable") };
            if (item.Has("eventsCreateHandled"))
            {
                team.CreateHandled = item.Bool("eventsCreateHandled");
                team.CurUnits = checked((int)item.Int("eventsCurUnits"));
                team.DestroyThreshold = checked((int)item.Int("eventsThreshold"));
                team.SeeEnemy = item.Bool("eventsSeeEnemy");
                team.WasIdle = item.Bool("eventsWasIdle");
            }
            if (item.Has("commonTarget")) team.CommonTarget = checked((uint)item.UInt("commonTarget"));
            team.Members.Stamp = _membership;
            foreach (var member in item.Array("members")) team.Members.Add(checked((uint)member.UInt("id")));
            _teams[team.Id] = team;
        }
        _followers.Clear();
        foreach (var item in reader.Array("followers"))
            _followers[checked((uint)item.UInt("entity"))] = new Follower
            {
                Entity = checked((uint)item.UInt("entity")), Path = item.Text("path"), Index = checked((int)item.Int("index")), AsTeam = item.Has("asTeam"),
                Team = item.Has("asTeam") ? checked((int)item.Int("team")) : 0, OffsetX = item.Has("asTeam") ? item.Fixed("offsetX") : Fixed.Zero,
                OffsetY = item.Has("asTeam") ? item.Fixed("offsetY") : Fixed.Zero, GoalIndex = item.Has("asTeam") ? checked((int)item.Int("goalIndex")) : -1,
            };
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
        LoadAi(reader);
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
