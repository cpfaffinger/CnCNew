using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>One computer player run by the original skirmish AI.</summary>
/// <param name="Side">The side of its player template (<c>America</c>, <c>ChinaTankGeneral</c>): the key of its <c>SideInfo</c> and <c>SkirmishBuildList</c>.</param>
/// <param name="StartIndex">Zero-based start position (<c>Player::getMpStartIndex</c>): names the perimeter areas and approach paths of this player.</param>
public sealed record ScriptAiPlayerSetup(PlayerId Player, ScriptDifficulty Difficulty, string Side, int StartIndex, Fixed StartX, Fixed StartY);

/// <summary>The original skirmish AI of a session: its data and the players it runs. Without it the engine behaves exactly as it does for a mission.</summary>
public sealed record ScriptAiSetup(AiData Data, ImmutableArray<ScriptAiPlayerSetup> Players);

/// <summary>
/// The C++ half of the original skirmish AI (<c>AISkirmishPlayer</c> on top of <c>AIPlayer</c>, GameLogic/AI/AISkirmishPlayer.cpp and AIPlayer.cpp), rewritten as part of the script engine so that
/// its whole state is in <see cref="Describe"/>, <see cref="Load"/> and <see cref="StateHash"/> from the start (docs/SKIRMISH-AI-ORIGINAL.md). The scripts of <c>SkirmishScripts.scb</c> decide
/// <em>what</em> is built and where teams go; this part does what they ask for: the build list of the side laid out around the start position, structures by dozer, rebuilding, power first,
/// dozers and gatherers, which team to train next, skill points, the current enemy and the base defences on the approach paths.
/// <para>
/// Everything the AI does is a normal player command (<see cref="ScriptCommandIssuer"/>); its one direct world write is the command centre standing at the start (<c>buildStructureNow</c>).
/// It thinks on ticks that begin a logic frame of the original and its timers are deadlines in ticks made from frames and seconds by the time service, so they run in the same game time at
/// every tick rate.
/// Its random draws (<c>GameLogicRandomValue</c> in the reference) come from <see cref="SimulationWorld.LogicRandom"/>.
/// </para>
/// </summary>
public sealed partial class ScriptEngine
{
    private const string ThisPlayerEnemyName = "<This Player's Enemy>";
    private const string MyInnerPerimeter = "[Skirmish]MyInnerPerimeter";
    private const string MyOuterPerimeter = "[Skirmish]MyOuterPerimeter";
    private const string EnemyInnerPerimeter = "[Skirmish]EnemyInnerPerimeter";
    private const string EnemyOuterPerimeter = "[Skirmish]EnemyOuterPerimeter";
    private const string CenterPath = "Center";
    private const string FlankPath = "Flank";
    private const string BackdoorPath = "Backdoor";

    /// <summary><c>doBaseBuilding</c>/<c>doTeamBuilding</c>: "check again in 2 seconds".</summary>
    private const long AiRecheckFrames = 2 * SimulationRate.OriginalFramesPerSecond;
    /// <summary>The two "ready" timers are capped at three seconds.</summary>
    private const long AiTimerCapFrames = 3 * SimulationRate.OriginalFramesPerSecond;
    /// <summary><c>AISkirmishPlayer::getAiEnemy</c>: the enemy is reconsidered every five seconds.</summary>
    private const long AiEnemyCheckFrames = 5 * SimulationRate.OriginalFramesPerSecond;
    /// <summary>How long a build list entry waits for the site of an accepted construction order to appear (own bookkeeping: the reference gets the object back from <c>buildObjectNow</c>).</summary>
    private const long AiSitePendingFrames = 2 * SimulationRate.OriginalFramesPerSecond;
    /// <summary><c>buildStructureWithDozer</c>: a skirmish AI looks for another place up to 120 pathfind cells away, in steps of four cells outwards and two cells along a side.</summary>
    private const int AiPlacementLimitCells = 120;
    /// <summary><c>doUpgradesAndSkills</c>: nothing is bought in the first two frames.</summary>
    private const long AiSkillStartFrames = 2;
    private const int AiNoSkillSet = -1;

    private sealed class AiBuildEntry
    {
        public string Template = string.Empty;
        public Fixed X;
        public Fixed Y;
        public Fixed Angle;
        public int Rebuilds;
        public bool Automatic;
        public bool Priority;
        public uint ObjectId;
        /// <summary>0: never built or ready to rebuild; otherwise the tick after which the rebuild delay counts.</summary>
        public long Timestamp;
        public bool UnderConstruction;
        public long PendingUntil;
        public bool IsSupply;
        public int DesiredGatherers;
        /// <summary><c>BuildListInfo::getCurrentGatherers</c>: -1 until the first one, the one a supply centre comes with, is accounted for.</summary>
        public int CurrentGatherers;
    }

    /// <summary>
    /// A unit the AI orders for itself: <c>queueDozer</c> and <c>queueSupplyTruck</c> put a <c>TeamInQueue</c> with one <c>WorkOrder</c> for the default team at the head of the queue, with
    /// a busy factory good enough. <see cref="Factory"/> is <c>m_factoryID</c>: the unit is on its way there; the supply centre's first gatherer needs no training, only the order that takes it.
    /// </summary>
    private sealed class AiUnitOrder
    {
        public string Template = string.Empty;
        public bool Gatherer;
        public uint Factory;
    }

    private sealed class AiPlayer(ScriptAiPlayerSetup setup)
    {
        public readonly ScriptAiPlayerSetup Setup = setup;
        public readonly List<AiBuildEntry> BuildList = [];
        public bool Started;
        public bool ReadyTeam;
        public bool ReadyStructure;
        /// <summary>The tick the team (structure) timer of the reference runs out, and the tick of the next look at the queues: deadlines instead of the reference's counters.</summary>
        public long TeamReadyTick;
        public long StructureReadyTick;
        public long BuildCheckTick;
        public long TeamCheckTick;
        public Fixed BaseX;
        public Fixed BaseY;
        public Fixed BaseRadius;
        public int Enemy = -1;
        public long EnemyCheckTick;
        public int SkillSet = AiNoSkillSet;
        /// <summary>The orders of <see cref="AiUnitOrder"/>, newest first.</summary>
        public readonly List<AiUnitOrder> UnitOrders = [];
        /// <summary>Gatherers just produced and the centres they are sent to: a unit appears at any tick of a frame, the order goes out where the AI thinks, at the start of the next one.</summary>
        public readonly List<(uint Unit, uint Dock)> GathererDocks = [];
        public int FrontDefense;
        public int FlankDefense;
        // m_curFrontLeft/Right, m_curLeftFlankLeft/Right, m_curRightFlankLeft/Right defence angles.
        public readonly Fixed[] DefenseAngles = new Fixed[6];
        // The side info of the data once it has been looked up; not state.
        public AiSideInfo? Side;
        public bool SideKnown;
    }

    private readonly List<AiPlayer> _ai = [];
    private readonly List<ScriptTeam> _genericTeams = [];
    // Per team prototype the scripts of its generic hooks, looked up once (a missing one stays null and is diagnosed when it is tried). Derived from the program, not state.
    private readonly Dictionary<string, ScriptUnit?[]> _genericUnits = new(StringComparer.Ordinal);
    private ScriptDifficulty[] _listDifficulty = [];
    private readonly SortedDictionary<string, long> _productionNextTick = new(StringComparer.Ordinal);
    /// <summary>Per team instance the generic script hooks that are done (bit per hook): <c>Team::m_shouldAttemptGenericScript</c>, inverted so that an untouched team stores nothing.</summary>
    private readonly SortedDictionary<int, int> _genericDone = [];
    private AiPlayer? _aiCurrent;
    // The dozer and gatherer types in catalog order, looked up once; not state.
    private ImmutableArray<ObjectTemplate> _aiDozerTemplates;
    private ImmutableArray<ObjectTemplate> _aiHarvesterTemplates;

    /// <summary>Whether this engine runs the original skirmish AI for any player.</summary>
    public bool HasAiPlayers => _ai.Count > 0;

    /// <summary>The player an AI currently treats as its enemy (<c>Player::getCurrentEnemy</c>), or <see langword="null"/>.</summary>
    public PlayerId? AiEnemyOf(PlayerId player) => AiOfPlayer(player) is { Enemy: >= 0 } state ? new PlayerId(state.Enemy) : null;

    /// <summary>Build list of an AI player as (template, built object or 0, priority) in list order; for tools and tests.</summary>
    public ImmutableArray<(string Template, uint ObjectId, bool Priority, Fixed X, Fixed Y)> AiBuildListOf(PlayerId player)
        => AiOfPlayer(player) is { } state ? [..state.BuildList.Select(entry => (entry.Template, entry.ObjectId, entry.Priority, entry.X, entry.Y))] : [];

    /// <summary>
    /// The priority entries of an AI player's build list that do not stand, each with what a construction order would be answered now; a diagnostic for tools (reads only).
    /// </summary>
    public ImmutableArray<string> AiPendingBuilds(PlayerId player)
    {
        if (AiOfPlayer(player) is not { } state) return [];
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in state.BuildList)
        {
            if (!entry.Priority || entry.ObjectId != 0) continue;
            var dozer = _world.EntitiesOwnedBy(player).AsSpan().ToArray().FirstOrDefault(entity => Counts(entity) && entity.Template.IsKindOf("DOZER") && AiOf(entity) is { CanBuild: true });
            var answer = dozer is null ? "no dozer" : _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, dozer.Id, entry.Template, entry.X, entry.Y, entry.Angle)) is var rejection and not 0 ? Enum.GetName(rejection) ?? "rejected" : "ready";
            result.Add(InvariantText.Format($"{entry.Template}: {answer}"));
        }
        return result.ToImmutable();
    }

    /// <summary>Each supply centre of an AI player's build list: its object, the gatherers it wants and the ones it has; for tools and tests.</summary>
    public ImmutableArray<(uint ObjectId, int Desired, int Current)> AiSupplyCentersOf(PlayerId player)
        => AiOfPlayer(player) is { } state ? [..state.BuildList.Where(entry => entry.IsSupply).Select(entry => (entry.ObjectId, entry.DesiredGatherers, entry.CurrentGatherers))] : [];

    /// <summary>The dock a supply gatherer remembers (<c>getPreferredDockID</c>), if the object is one; for tools and tests.</summary>
    public static EntityId? PreferredDockOf(Entity entity) => AiOf(entity) is ISupplyCarrier carrier ? carrier.PreferredDock : null;

    private void InitializeAi()
    {
        if (_options.Ai is { } setup)
            foreach (var player in setup.Players.OrderBy(player => player.Player.Value)) _ai.Add(new AiPlayer(player));
        // The difficulty a script list is gated with is fixed by the options: looked up once per list instead of once per script and frame.
        _listDifficulty = new ScriptDifficulty[_program.ListCount];
        for (var list = 0; list < _listDifficulty.Length; list++) _listDifficulty[list] = DifficultyOfList(list);
    }

    private AiPlayer? AiOfPlayer(PlayerId player)
    {
        foreach (var state in _ai)
            if (state.Setup.Player == player) return state;
        return null;
    }

    /// <summary>The player a script runs for (<c>ScriptEngine::m_currentPlayer</c>): the AI that is thinking, else the owner of the team the script acts for, else the owner of the script list.</summary>
    private ScriptPlayer? CurrentScriptPlayer()
    {
        if (_ai.Count == 0) return _currentList < _program.Players.Length ? _program.Players[_currentList] : null;
        if (_aiCurrent is { } thinking) return PlayerById(thinking.Setup.Player);
        // evaluateConditions / runScript: "if (thisTeam) player = thisTeam->getControllingPlayer()".
        if (ThisTeam is { } team && !team.Owner.IsSystem) return PlayerById(team.Owner);
        return _currentList < _program.Players.Length ? _program.Players[_currentList] : null;
    }

    private ScriptDifficulty DifficultyOfList(int list)
    {
        if (_ai.Count > 0 && list < _program.Players.Length && AiOfPlayer(_program.Players[list].Id) is { } state) return state.Setup.Difficulty;
        return _options.Difficulty;
    }

    private static bool AllowedOn(MapScript script, ScriptDifficulty difficulty) => difficulty switch
    {
        ScriptDifficulty.Easy => script.Easy,
        ScriptDifficulty.Hard => script.Hard,
        _ => script.Normal,
    };

    /// <summary>
    /// <c>ScriptEngine::getQualifiedTriggerAreaByName</c>: <c>[Skirmish]MyInnerPerimeter</c> and its three siblings name the perimeter areas of the current player's or of its enemy's start
    /// position (<c>InnerPerimeter&lt;n&gt;</c>, <c>OuterPerimeter&lt;n&gt;</c>). Without an AI the name is looked up as it is, as before.
    /// </summary>
    private ScriptArea? FindAreaQualified(string name)
    {
        if (_ai.Count == 0 || name.Length == 0 || name[0] != '[') return _program.FindArea(name);
        var mine = name is MyInnerPerimeter or MyOuterPerimeter;
        if (!mine && name is not (EnemyInnerPerimeter or EnemyOuterPerimeter)) return _program.FindArea(name);
        var inner = name is MyInnerPerimeter or EnemyInnerPerimeter;
        var index = -1;
        if (CurrentScriptPlayer() is { } current)
        {
            if (mine) index = current.StartIndex + 1;
            else if (AiOfPlayer(current.Id) is { Enemy: >= 0 } state && PlayerById(new PlayerId(state.Enemy)) is { } enemy) index = enemy.StartIndex + 1;
        }
        else if (mine) return null;
        return _program.FindArea(InvariantText.Format($"{(inner ? "InnerPerimeter" : "OuterPerimeter")}{index}"));
    }

    // ------------------------------------------------------------------------------------------------ start

    /// <summary>
    /// <c>AISkirmishPlayer::newMap</c> for every AI player: the build list of its side, moved and turned to its start position (<c>adjustBuildList</c>), the centre and radius of that base, and
    /// the initially built entry - the command centre - standing. Called once by the session after the start objects are placed; a restored session carries the result in its state.
    /// </summary>
    public void StartAiPlayers(Fixed extentX, Fixed extentY)
    {
        if (_options.Ai is not { } setup) return;
        foreach (var state in _ai)
        {
            if (state.Started) continue;
            state.Started = true;
            if (!setup.Data.BuildLists.TryGetValue(state.Setup.Side, out var list)) { Diagnose($"AI: no skirmish build list for side '{state.Setup.Side}'"); continue; }
            // The command centre of the list goes to the start position; every entry keeps its place relative to it, turned by 135 degrees (plus the turn of RotateSkirmishBases).
            var anchor = list.LastOrDefault(entry => _world.Templates.TryGet(entry.Template, out var template) && template.IsKindOf("COMMANDCENTER"));
            // adjustBuildList tests the template of the list's first entry for every entry: with a list that does not begin with its command centre nothing is moved.
            var move = anchor is not null && list.Length > 0 && _world.Templates.TryGet(list[0].Template, out var first) && first.IsKindOf("COMMANDCENTER");
            var angle = Fixed.FromInt(3) * FixedTrig.Pi / Fixed.FromInt(4);
            if (setup.Data.RotateSkirmishBases) angle += GridAngle(state.Setup.StartX, state.Setup.StartY, extentX, extentY);
            var sin = FixedTrig.Sin(angle);
            var cos = FixedTrig.Cos(angle);
            foreach (var entry in list)
            {
                var x = entry.X;
                var y = entry.Y;
                if (move)
                {
                    var dx = entry.X - anchor!.X;
                    var dy = entry.Y - anchor.Y;
                    x = dx * cos - dy * sin + state.Setup.StartX;
                    y = dy * cos + dx * sin + state.Setup.StartY;
                }
                var initially = entry.InitiallyBuilt || ReferenceEquals(entry, anchor);
                state.BuildList.Add(new AiBuildEntry
                {
                    Template = entry.Template, X = x, Y = y, Angle = entry.AngleDegrees * FixedTrig.Pi / Fixed.FromInt(180),
                    // newMap: "the initial build in the normal build list consumes a rebuild, so add one" (not for an unlimited entry, which the data write as a negative number).
                    Rebuilds = initially || entry.Rebuilds < 0 ? entry.Rebuilds : entry.Rebuilds + 1,
                    Automatic = entry.AutomaticallyBuild,
                });
            }
            ComputeBase(state);
            for (var index = 0; index < state.BuildList.Count; index++)
            {
                var source = list[index];
                if (!(source.InitiallyBuilt || ReferenceEquals(source, anchor))) continue;
                var entry = state.BuildList[index];
                if (!_world.Templates.TryGet(entry.Template, out var template) || !template.IsInstantiable) { Diagnose($"AI: build list template '{entry.Template}' cannot be created"); continue; }
                // buildStructureNow: the structure stands finished.
                var id = _world.RequestSpawn(template.Index, state.Setup.Player, entry.X, entry.Y, Fixed.Zero, entry.Angle, 100);
                entry.ObjectId = id.Value;
                if (_program.FindPlayer(PlayerById(state.Setup.Player)?.Name ?? string.Empty) is { } owner) AssignToTeam("team" + owner.Name, id);
            }
        }
        _world.MaterializePendingSpawns();
    }

    /// <summary>The nine sectors of the map (<c>adjustBuildList</c>) and the turn of a base in each.</summary>
    private static Fixed GridAngle(Fixed x, Fixed y, Fixed extentX, Fixed extentY)
    {
        var three = Fixed.FromInt(3);
        var index = 0;
        if (x > extentX / three) index++;
        if (x > Fixed.FromInt(2) * extentX / three) index++;
        if (y > extentY / three) index += 3;
        if (y > Fixed.FromInt(2) * extentY / three) index += 3;
        var quarter = FixedTrig.Pi / Fixed.FromInt(4);
        return index switch
        {
            1 => quarter,
            2 => quarter * Fixed.FromInt(2),
            3 => -quarter,
            5 => quarter * three,
            6 => -quarter * Fixed.FromInt(2),
            7 => -quarter * three,
            8 => FixedTrig.Pi,
            _ => Fixed.Zero,
        };
    }

    /// <summary><c>AIPlayer::computeCenterAndRadiusOfBase</c>: the mean of the build list locations and the farthest corner (location plus 0.4 of the structure's radius on both axes).</summary>
    private void ComputeBase(AiPlayer state)
    {
        Fixed sumX = Fixed.Zero, sumY = Fixed.Zero;
        var count = 0;
        foreach (var entry in state.BuildList)
        {
            if (!_world.Templates.TryGet(entry.Template, out _)) continue;
            sumX += entry.X;
            sumY += entry.Y;
            count++;
        }
        if (count == 0) { state.BaseX = state.Setup.StartX; state.BaseY = state.Setup.StartY; return; }
        state.BaseX = sumX / Fixed.FromInt(count);
        state.BaseY = sumY / Fixed.FromInt(count);
        var radius = Fixed.Zero;
        foreach (var entry in state.BuildList)
        {
            if (!_world.Templates.TryGet(entry.Template, out var template)) continue;
            var extra = template.BoundingCircleRadius * Fixed.FromDecimal(0.4m);
            var dx = Fixed.Max(entry.X - state.BaseX, state.BaseX - entry.X) + extra;
            var dy = Fixed.Max(entry.Y - state.BaseY, state.BaseY - entry.Y) + extra;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            if (distance > radius) radius = distance;
        }
        state.BaseRadius = radius;
    }

    // ------------------------------------------------------------------------------------------------ update

    /// <summary><c>Player::update</c> for the AI players: <c>AIPlayer::update</c>, then the generic scripts of every team of the player.</summary>
    private void UpdateAiPlayers()
    {
        if (_ai.Count == 0 || !_world.Time.IsOriginalFrameBoundary(_tick)) return;
        foreach (var state in _ai)
        {
            if (!state.Started || !_world.HasPlayer(state.Setup.Player) || !PlayerHasAnyObject(state.Setup.Player)) continue;
            _aiCurrent = state;
            try
            {
                SendProducedGatherers(state);
                if (Observer is { } observer) UpdateAiPlayerObserved(observer, state);
                else
                {
                    DoBaseBuilding(state);
                    DoTeamBuilding(state);
                    DoUpgradesAndSkills(state);
                    UpdateGenericScripts(state);
                }
            }
            finally { _aiCurrent = null; }
        }
    }

    private void SendProducedGatherers(AiPlayer state)
    {
        if (state.GathererDocks.Count == 0) return;
        foreach (var (unit, dock) in state.GathererDocks)
            if (_world.FindEntity(new EntityId(unit)) is { } gatherer && _world.FindEntity(new EntityId(dock)) is { } center) DockAt(state.Setup.Player, gatherer, center);
        state.GathererDocks.Clear();
    }

    /// <summary>The same four calls in the same order, each reported to the observer (the section of the AI players is suspended meanwhile: sections do not nest).</summary>
    private void UpdateAiPlayerObserved(IScriptObserver observer, AiPlayer state)
    {
        observer.SectionEnded(ScriptSection.AiPlayers);
        observer.SectionStarted(ScriptSection.AiBaseBuilding);
        DoBaseBuilding(state);
        observer.SectionEnded(ScriptSection.AiBaseBuilding);
        observer.SectionStarted(ScriptSection.AiTeamBuilding);
        DoTeamBuilding(state);
        observer.SectionEnded(ScriptSection.AiTeamBuilding);
        observer.SectionStarted(ScriptSection.AiSkills);
        DoUpgradesAndSkills(state);
        observer.SectionEnded(ScriptSection.AiSkills);
        observer.SectionStarted(ScriptSection.AiTeamScripts);
        UpdateGenericScripts(state);
        observer.SectionEnded(ScriptSection.AiTeamScripts);
        observer.SectionStarted(ScriptSection.AiPlayers);
    }

    /// <summary><c>AIPlayer::onStructureProduced</c>: a structure of an AI player is finished: the queues and the build list are looked at again at once, and a supply centre starts its count.</summary>
    private void AiObserveStructureFinished(Entity building)
    {
        if (_ai.Count == 0 || AiOfPlayer(building.Owner) is not { } state) return;
        state.TeamCheckTick = _tick;
        state.BuildCheckTick = _tick;
        BindSites(state);
        foreach (var entry in state.BuildList)
        {
            if (entry.ObjectId != building.Id.Value) continue;
            entry.UnderConstruction = false;
            CheckForSupplyCenter(state, entry, building);
            return;
        }
    }

    /// <summary><c>AIPlayer::onUnitProduced</c> / <c>onStructureProduced</c>: something of an AI player came into the world, so its queues are looked at again at once.</summary>
    private void AiObserveSpawn(Entity entity)
    {
        if (_ai.Count == 0 || AiOfPlayer(entity.Owner) is not { } state) return;
        state.TeamCheckTick = _tick;
        if (entity.Template.IsKindOf("STRUCTURE")) return;
        // "factory could be null at the start of the game."
        if (entity.SpawnTick > 0) AiUnitProduced(state, entity);
        // A unit that no team asked for belongs to the player's default team (every object of the reference has a team; recruiting takes from there).
        if (FirstTeamOf(entity.Id.Value) is not null) return;
        if (PlayerById(state.Setup.Player) is { } owner && FindTeamInstance("team" + owner.Name) is { } defaultTeam) defaultTeam.Members.Add(entity.Id.Value);
    }

    /// <summary>
    /// The half of <c>AIPlayer::onUnitProduced</c> for the orders of <see cref="AiUnitOrder"/>. A gatherer gets to work and is sent to every centre that lacks one, so it ends at the last of them. A
    /// dozer no gatherer order asked for looks at the base at once. The reference matches an order by factory and type; an object here does not say where it comes from, so by type.
    /// </summary>
    private void AiUnitProduced(AiPlayer state, Entity unit)
    {
        var gatherer = false;
        for (var index = 0; index < state.UnitOrders.Count; index++)
        {
            var order = state.UnitOrders[index];
            if (order.Factory == 0 || !string.Equals(order.Template, unit.Template.Name, StringComparison.Ordinal)) continue;
            gatherer = order.Gatherer;
            state.UnitOrders.RemoveAt(index);
            if (AiOf(unit) is ISupplyCarrier carrier)
            {
                carrier.SetForceWanting(_world, gatherer);
                if (gatherer)
                    foreach (var entry in state.BuildList)
                    {
                        if (!entry.IsSupply || entry.DesiredGatherers <= 0 || entry.DesiredGatherers <= entry.CurrentGatherers) continue;
                        if (entry.ObjectId == 0 || _world.FindEntity(new EntityId(entry.ObjectId)) is not { } center) continue;
                        entry.CurrentGatherers++;
                        state.GathererDocks.Add((unit.Id.Value, center.Id.Value));
                    }
            }
            break;
        }
        if (gatherer || !unit.Template.IsKindOf("DOZER")) return;
        state.BuildCheckTick = _tick;
        state.StructureReadyTick = _tick;
    }

    // ------------------------------------------------------------------------------------------------ base building

    /// <summary><c>AISkirmishPlayer::doBaseBuilding</c>.</summary>
    private void DoBaseBuilding(AiPlayer state)
    {
        if (!_world.CanBuildStructures(state.Setup.Player)) return;
        if (!state.ReadyStructure && _tick >= state.StructureReadyTick)
        {
            state.ReadyStructure = true;
            state.BuildCheckTick = _tick;
        }
        if (_tick < state.BuildCheckTick) return;
        if (state.ReadyStructure) ProcessBaseBuilding(state);
        if (state.BuildCheckTick <= _tick) state.BuildCheckTick = _tick + _world.Time.OriginalFrames(AiRecheckFrames);
    }

    /// <summary>
    /// <c>AISkirmishPlayer::processBaseBuilding</c>: one structure per round. The first open priority entry (one a script asked for) is taken whatever the rules say about it, and a power plant
    /// when power is short (or the list builds it automatically) overrides it. <c>buildObjectNow</c> then tests the prerequisites (<c>isPossibleToMakeUnit</c>): an entry that lacks one is
    /// not built, and the entries behind it wait with it. A destroyed or captured structure waits <c>RebuildDelayTimeSeconds</c>; a site whose dozer is gone gets another.
    /// <para>
    /// The branch for automatic entries never takes one: the reference asks <c>canMakeUnit(dozer, bldgPlan)</c> of the plan chosen so far, which refuses a null plan, and only a null plan
    /// would be replaced. What it does is look for a dozer, which orders one when power is short.
    /// </para>
    /// </summary>
    private void ProcessBaseBuilding(AiPlayer state)
    {
        var player = state.Setup.Player;
        var data = _options.Ai!.Data;
        BindSites(state);
        var underPowered = !_world.HasSufficientPower(player);
        var powerUnderConstruction = false;
        AiBuildEntry? chosen = null;
        var chosenIsPriority = false;
        AiBuildEntry? power = null;
        foreach (var entry in state.BuildList)
        {
            if (!_world.Templates.TryGet(entry.Template, out var template) || !template.IsInstantiable) continue;
            Entity? building = null;
            if (entry.ObjectId != 0)
            {
                building = _world.FindEntity(new EntityId(entry.ObjectId));
                if (building is null || building.DestroyRequested)
                {
                    // Destroyed. (A GLA structure leaves a hole that rebuilds it by itself; the hole is not followed here and the entry is rebuilt by a worker after the delay if the hole failed.)
                    building = null;
                    entry.ObjectId = 0;
                    entry.UnderConstruction = false;
                    entry.Timestamp = _tick + TickOrder.Next;
                }
                else if (building.Owner != player)
                {
                    // Captured.
                    entry.ObjectId = 0;
                    entry.UnderConstruction = false;
                    entry.Timestamp = _tick + TickOrder.Next;
                }
                else if (building.HasStatus(UnderConstructionBit))
                {
                    if (template.IsKindOf("FS_POWER") && !template.IsKindOf("CASH_GENERATOR")) powerUnderConstruction = true;
                    var builder = _world.FindEntity(building.BuilderId);
                    if (builder is not null && (builder.Owner != player || builder.EffectivelyDead)) builder = null;
                    if (builder is null)
                    {
                        // "AI's Dozer got killed (or captured). Find another dozer."
                        QueueDozer(state);
                        builder = FindDozer(state, building.X, building.Y);
                    }
                    if (builder is not null && AiOf(builder) is not { IsBuilding: true })
                    {
                        var site = building.Id;
                        var helper = builder.Id;
                        _issue(player, (sequence, tick) => new ResumeConstructCommand(player, sequence, tick, helper, site));
                    }
                }
            }
            if (entry.ObjectId == 0 && entry.Timestamp > 0)
            {
                if (entry.Timestamp + _world.Time.WholeSecondsToTicks(data.RebuildDelaySeconds) > _tick) continue;
                entry.Timestamp = 0;
            }
            if (building is not null || entry.PendingUntil > _tick) continue;
            var wanted = entry.Priority || entry.Automatic || (template.IsKindOf("FS_POWER") && !template.IsKindOf("CASH_GENERATOR") && underPowered);
            if (!wanted || !IsLocationSafe(player, entry.X, entry.Y, template)) continue;
            if (entry.Priority && !chosenIsPriority)
            {
                chosen = entry;
                chosenIsPriority = true;
            }
            if (template.IsKindOf("FS_POWER") && power is null && !template.IsKindOf("CASH_GENERATOR") && (underPowered || entry.Automatic)) power = entry;
            if (!entry.Automatic) continue;
            if (FindDozer(state, entry.X, entry.Y) is null && underPowered) QueueDozer(state);
        }
        if (power is not null && (chosen is null || !string.Equals(power.Template, chosen.Template, StringComparison.Ordinal)) && !powerUnderConstruction) chosen = power;
        if (chosen is null || !BuildStructureWithDozer(state, chosen)) return;
        if (chosen.Rebuilds > 0) chosen.Rebuilds--;
        state.ReadyStructure = false;
        state.StructureReadyTick = _tick + ScaledTimer(data.StructureSeconds, _world.MoneyOf(player), data, data.StructuresPoorRate, data.StructuresWealthyRate);
    }

    /// <summary>
    /// The timer of the next team or structure in ticks: the seconds of the data in frames, divided by the poor or wealthy rate when the cash is below <c>Poor</c> or above <c>Wealthy</c>,
    /// and never more than three seconds (the reference caps the running timer).
    /// </summary>
    private long ScaledTimer(Fixed seconds, Fixed money, AiData data, Fixed poorRate, Fixed wealthyRate)
    {
        var frames = seconds * Fixed.FromInt(SimulationRate.OriginalFramesPerSecond);
        if (money < Fixed.FromInt(data.Poor) && poorRate > Fixed.Zero) frames /= poorRate;
        else if (money > Fixed.FromInt(data.Wealthy) && wealthyRate > Fixed.Zero) frames /= wealthyRate;
        return Math.Min(_world.Time.OriginalFrames(frames.Floor()), _world.Time.OriginalFrames(AiTimerCapFrames));
    }

    /// <summary>An accepted construction order creates its site in the world step; the entry takes the site that appeared at the ordered place.</summary>
    private void BindSites(AiPlayer state)
    {
        foreach (var entry in state.BuildList)
        {
            if (entry.ObjectId != 0 || entry.PendingUntil == 0) continue;
            foreach (var entity in _world.EntitiesOwnedBy(state.Setup.Player).AsSpan())
            {
                if (entity.X != entry.X || entity.Y != entry.Y || entity.EffectivelyDead || !string.Equals(entity.Template.Name, entry.Template, StringComparison.Ordinal)) continue;
                if (state.BuildList.Any(other => other.ObjectId == entity.Id.Value)) continue;
                entry.ObjectId = entity.Id.Value;
                entry.UnderConstruction = entity.HasStatus(UnderConstructionBit);
                entry.Timestamp = _tick + TickOrder.Next;
                break;
            }
            if (entry.ObjectId != 0 || entry.PendingUntil <= _tick) entry.PendingUntil = 0;
        }
    }

    /// <summary>
    /// <c>AIPlayer::buildStructureWithDozer</c>: a dozer, the money, no enemy on the place of the entry, and the place itself - or, when the dozer cannot use it, the first one
    /// <see cref="FindPlaceNear"/> finds. Where none is found the entry's own place is built on anyway ("the ai will validate, or cheat"), and a dozer that cannot reach it stands there at once:
    /// "Dozer unable to reach building. Teleporting." What the rules still refuse (a prerequisite, the limit of the type) is <c>buildObjectNow</c>'s refusal: nothing is built.
    /// </summary>
    private bool BuildStructureWithDozer(AiPlayer state, AiBuildEntry entry)
    {
        var player = state.Setup.Player;
        if (FindDozer(state, entry.X, entry.Y) is not { } dozer) return false;
        if (!_world.Templates.TryGet(entry.Template, out var template)) return false;
        if (_world.MoneyOf(player) < _world.ProductionCostOf(player, template)) return false;
        var (x, y) = (entry.X, entry.Y);
        var angle = entry.Angle;
        if (_world.EnemyObjectsInTheWay(dozer, template, x, y, angle)) return false;
        if (_world.Navigation is { } navigation && _world.CheckDozerSite(dozer, template, x, y, angle) != LegalBuildCode.Ok)
        {
            var reach = navigation.ReachableCells(dozer.X, dozer.Y, dozer.Template.Locomotor?.Surfaces ?? LocomotorSurfaces.Ground);
            if (!FindPlaceNear(dozer, template, angle, reach, ref x, ref y) && !navigation.Reaches(reach, x, y)) _world.Teleport(dozer, x, y);
        }
        var builder = dozer.Id;
        var name = entry.Template;
        if (!_issue(player, (sequence, tick) => new ConstructCommand(player, sequence, tick, builder, name, x, y, angle)).Accepted) return false;
        entry.X = x;
        entry.Y = y;
        entry.PendingUntil = _tick + _world.Time.OriginalFrames(AiSitePendingFrames);
        _busyThisTick.Add(builder.Value);
        return true;
    }

    /// <summary>
    /// The search of <c>buildStructureWithDozer</c> around a place that cannot be used, with the skirmish AI's increments: squares of growing size, up to 120 cells, walked along their sides.
    /// The second test of a walk along a side answers only for that side's last step: the next step overwrites it, as in the reference, where only the first test leaves the loop.
    /// </summary>
    private bool FindPlaceNear(Entity dozer, ObjectTemplate template, Fixed angle, bool[] reach, ref Fixed x, ref Fixed y)
    {
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        var (originX, originY) = (x, y);
        var (newX, newY) = (x, y);
        var valid = false;
        var limit = cell * Fixed.FromInt(AiPlacementLimitCells);
        for (var posOffset = Fixed.Zero; posOffset < limit && !valid; posOffset += cell * Fixed.FromInt(2))
        {
            posOffset += cell * Fixed.FromInt(2);
            var offset = posOffset / Fixed.FromInt(2);
            var yPos = originY - offset;
            for (var xPos = originX - offset; xPos <= originX + offset; xPos += cell)
            {
                xPos += cell;
                (newX, newY) = (xPos, yPos);
                valid = _world.CheckDozerSite(dozer, template, newX, newY, angle, reach) == LegalBuildCode.Ok;
                if (valid) break;
                newY = yPos + posOffset;
                valid = _world.CheckDozerSite(dozer, template, newX, newY, angle, reach) == LegalBuildCode.Ok;
            }
            if (valid) break;
            var xSide = originX - offset;
            for (var ySide = originY - offset; ySide <= originY + offset; ySide += cell)
            {
                ySide += cell;
                (newX, newY) = (xSide, ySide);
                valid = _world.CheckDozerSite(dozer, template, newX, newY, angle, reach) == LegalBuildCode.Ok;
                if (valid) break;
                newX = xSide + posOffset;
                valid = _world.CheckDozerSite(dozer, template, newX, newY, angle, reach) == LegalBuildCode.Ok;
            }
        }
        if (valid) (x, y) = (newX, newY);
        return valid;
    }

    /// <summary>
    /// <c>AIPlayer::isLocationSafe</c>: no living, visible enemy object - harvesters, dozers and insignificant buildings aside - within <c>SupplyCenterSafeRadius</c> plus the structure's radius.
    /// </summary>
    private bool IsLocationSafe(PlayerId player, Fixed x, Fixed y, ObjectTemplate template)
    {
        var radius = _options.Ai!.Data.SupplyCenterSafeRadius + template.BoundingCircleRadius;
        foreach (var other in _world.PlayerIds)
        {
            if (other == player || !_world.AreEnemies(player, other)) continue;
            foreach (var entity in _world.EntitiesOwnedBy(other).AsSpan())
            {
                if (!Counts(entity) || entity.Template.IsKindOf("HARVESTER") || entity.Template.IsKindOf("DOZER") || IsHiddenByStealth(entity)) continue;
                // PartitionFilterInsignificantBuildings: a building counts when it matters for victory or can shoot.
                if (entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("MP_COUNT_FOR_VICTORY") && SimulationWorld.ActiveWeaponSet(entity) is not { WeaponCount: > 0 }) continue;
                if (SquaredReach.Of(radius + entity.Template.BoundingCircleRadius).Contains(Distance2(entity, x, y))) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// <c>AIPlayer::findDozer</c>: a dozer of the player that is not constructing and - a worker without a task - not gathering; an idle one first, the nearest of those, else one with another
    /// task. When the player has no dozer at all one is ordered (<c>queueDozer</c>).
    /// </summary>
    private Entity? FindDozer(AiPlayer state, Fixed x, Fixed y)
    {
        Entity? any = null;
        Entity? closest = null;
        var closestDistance = Fixed.MaxValue;
        var needDozer = true;
        foreach (var entity in _world.EntitiesOwnedBy(state.Setup.Player).AsSpan())
        {
            if (!Counts(entity) || !entity.Template.IsKindOf("DOZER") || entity.HasStatus(UnderConstructionBit) || AiOf(entity) is not { CanBuild: true } ai) continue;
            // "If it is gathering supplies, don't steal it."
            if (!ai.HasBuilderTask && ai is ISupplyCarrier carrier && (carrier.IsFerrying || carrier.IsForcedWanting)) continue;
            needDozer = false;
            if (ai.IsConstructing || _busyThisTick.Contains(entity.Id.Value)) continue;
            var idle = !ai.HasBuilderTask;
            if (idle || any is null) any = entity;
            if (!idle) continue;
            var distance = Distance2(entity, x, y);
            if (closest is null || distance < closestDistance) { closest = entity; closestDistance = distance; }
        }
        if (needDozer) QueueDozer(state);
        return closest ?? any;
    }

    /// <summary>
    /// <c>AIPlayer::queueDozer</c>: unless a dozer is ordered already, the first dozer type with a factory goes to the head of the queue. (The reference also turns unit production on for this
    /// one order when a script has switched it off; the world's command does not know that.)
    /// </summary>
    private void QueueDozer(AiPlayer state)
    {
        if (DozerInQueue(state)) return;
        foreach (var template in TemplatesOfKind(ref _aiDozerTemplates, "DOZER"))
        {
            if (FindFactory(state, template, busyOk: true) is null) continue;
            var order = new AiUnitOrder { Template = template.Name };
            state.UnitOrders.Insert(0, order);
            state.TeamCheckTick = _tick;
            StartTraining(state, order, busyOk: true);
            return;
        }
    }

    /// <summary><c>AIPlayer::dozerInQueue</c>, <c>TeamInQueue::includesADozer</c>: GLA workers are gatherers too, and an order for a gatherer is no dozer.</summary>
    private bool DozerInQueue(AiPlayer state)
    {
        foreach (var order in state.UnitOrders)
            if (!order.Gatherer && _world.Templates.TryGet(order.Template, out var template) && template.IsKindOf("DOZER")) return true;
        foreach (var order in _teamBuilds)
            if (order.Joined < order.Total && _teams.TryGetValue(order.TeamId, out var team) && team.Owner == state.Setup.Player && _world.Templates.TryGet(order.Type, out var unit) && unit.IsKindOf("DOZER")) return true;
        return false;
    }

    /// <summary>The unit types of a kind in the order of the catalog, looked up once (the catalog never changes): the reference walks every template and asks for a factory.</summary>
    private ImmutableArray<ObjectTemplate> TemplatesOfKind(ref ImmutableArray<ObjectTemplate> known, string kind)
    {
        if (!known.IsDefault) return known;
        known = [.._world.Templates.Templates.Where(template => template.IsInstantiable && template.IsKindOf(kind) && !template.IsKindOf("STRUCTURE"))];
        return known;
    }

    /// <summary>
    /// <c>AIPlayer::findFactory</c>: only what the build list holds is a factory of this AI. The first of its finished, unsold production buildings that can make the unit and is doing nothing,
    /// else - when a busy one will do - the last busy one. An object that changed hands leaves the list.
    /// </summary>
    private Entity? FindFactory(AiPlayer state, ObjectTemplate thing, bool busyOk)
    {
        var player = state.Setup.Player;
        Entity? busy = null;
        foreach (var entry in state.BuildList)
        {
            if (entry.ObjectId == 0 || _world.FindEntity(new EntityId(entry.ObjectId)) is not { } factory) continue;
            if (factory.Owner != player)
            {
                entry.ObjectId = 0;
                continue;
            }
            if (factory.HasStatus(UnderConstructionBit) || factory.HasStatus(SoldBit) || ProductionOf(factory) is not { } production) continue;
            // isPossibleToMakeUnit: a button of its command set, and the player may build it.
            if (!_world.Templates.OffersBuild(factory.Template, thing.Name) || !_world.CanQueueProductionTemplate(player, factory.Id, thing.Name)) continue;
            if (production.Queue.Length == 0) return factory;
            if (busyOk) busy = factory;
        }
        return busy;
    }

    /// <summary><c>AIPlayer::startTraining</c>: a factory is found and the unit queued, which the money can refuse.</summary>
    private bool StartTraining(AiPlayer state, AiUnitOrder order, bool busyOk)
    {
        if (!_world.Templates.TryGet(order.Template, out var template) || FindFactory(state, template, busyOk) is not { } factory) return false;
        var player = state.Setup.Player;
        var producer = factory.Id;
        var unit = template.Name;
        if (!_issue(player, (sequence, tick) => new QueueProductionCommand(player, sequence, tick, producer, unit)).Accepted) return false;
        order.Factory = producer.Value;
        return true;
    }

    /// <summary>
    /// <c>AIPlayer::queueUnits</c> at its pace (every two seconds, at once when something was produced): first what the AI wants for itself, then the teams being built. A team's order takes a
    /// recruit if there is one, trains one unit at a time, at an idle factory unless a script asked for the team, and a factory that is gone is forgotten (<c>validateFactory</c>).
    /// </summary>
    private void QueueUnits(AiPlayer state)
    {
        QueueSupplyTruck(state);
        var player = state.Setup.Player;
        foreach (var order in state.UnitOrders.ToArray())
        {
            if (order.Factory != 0 && !OwnsFactory(player, order.Factory)) order.Factory = 0;
            if (order.Factory == 0) StartTraining(state, order, busyOk: true);
        }
        foreach (var order in _teamBuilds)
        {
            if (!_teams.TryGetValue(order.TeamId, out var team) || team.Owner != player) continue;
            if (order.Factory != 0 && !OwnsFactory(player, order.Factory))
            {
                order.Factory = 0;
                order.Queued = order.Joined;
            }
            if (order.Queued >= order.Total) continue;
            // A reinforcement looked for a recruit when it was chosen (around the team, not around the base); what is left is to train the one unit.
            if (!order.Reinforcement && AiRecruitsFor(order, team)) continue;
            if (!_world.Templates.TryGet(order.Type, out var template)) continue;
            if (FindFactory(state, template, busyOk: !order.IdleOnly) is not { } factory)
            {
                if (!order.IdleOnly && !order.FactoryMissing) { order.FactoryMissing = true; Diagnose($"BUILD_TEAM: no factory for '{order.Type}'"); }
                continue;
            }
            order.FactoryMissing = false;
            var producer = factory.Id;
            var unit = template.Name;
            if (!_issue(player, (sequence, tick) => new QueueProductionCommand(player, sequence, tick, producer, unit)).Accepted) continue;
            order.Queued++;
            order.Factory = producer.Value;
        }
    }

    private bool OwnsFactory(PlayerId player, uint factory) => _world.FindEntity(new EntityId(factory)) is { } found && found.Owner == player;

    // ------------------------------------------------------------------------------------------------ supplies

    /// <summary><c>AIPlayer::checkForSupplyCenter</c>: a finished supply centre wants the gatherers of its side for the player's difficulty, plus the one it comes with.</summary>
    private void CheckForSupplyCenter(AiPlayer state, AiBuildEntry entry, Entity building)
    {
        if (ModuleOf<SupplyCenterDockUpdate>(building) is null) return;
        entry.IsSupply = true;
        entry.CurrentGatherers = -1;
        // "Get a freebie with the supply depots."
        entry.DesiredGatherers = GatherersWanted(state) + 1;
    }

    private int GatherersWanted(AiPlayer state)
    {
        if (SideOf(state) is not { } side) return 0;
        return state.Setup.Difficulty switch
        {
            ScriptDifficulty.Easy => side.GatherersEasy,
            ScriptDifficulty.Hard => side.GatherersHard,
            _ => side.GatherersNormal,
        };
    }

    /// <summary>
    /// <c>AIPlayer::queueSupplyTruck</c>, for every supply centre of the build list. One that has its gatherers looks whether supplies are left beside it: if so its count starts over from what
    /// the gatherers remember as their dock, and one of them that stands around is sent back. One that lacks a gatherer takes a loose one (its dock was destroyed), else orders one, unless
    /// the player has three times what the centre wants; the first order takes the gatherer the centre comes with and trains nothing. One order at a time.
    /// </summary>
    private void QueueSupplyTruck(AiPlayer state)
    {
        foreach (var order in state.UnitOrders)
            if (order.Gatherer) return;
        var player = state.Setup.Player;
        var harvesters = new List<(Entity Unit, ISupplyCarrier Carrier)>();
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity) && entity.Template.IsKindOf("HARVESTER") && AiOf(entity) is ISupplyCarrier carrier) harvesters.Add((entity, carrier));
        foreach (var entry in state.BuildList)
        {
            if (!entry.IsSupply) continue;
            var center = entry.ObjectId != 0 ? _world.FindEntity(new EntityId(entry.ObjectId)) : null;
            if (entry.CurrentGatherers >= entry.DesiredGatherers)
            {
                if (center is null || center.Template.IsKindOf("REBUILD_HOLE") || !HasSuppliesBeside(player, center)) continue;
                CheckForSupplyCenter(state, entry, center);
                var current = 0;
                foreach (var (unit, carrier) in harvesters)
                {
                    if (carrier.PreferredDock != center.Id) continue;
                    current++;
                    if (!carrier.IsFerrying) DockAt(player, unit, center);
                }
                entry.CurrentGatherers = current;
                continue;
            }
            foreach (var (unit, carrier) in harvesters)
            {
                if (carrier.PreferredDock.IsValid && _world.FindEntity(carrier.PreferredDock) is not null) continue;
                if ((carrier.IsFerrying || carrier.IsForcedWanting) && center is not null)
                {
                    // "Re-attaching supply truck to supply center."
                    entry.CurrentGatherers++;
                    DockAt(player, unit, center);
                    return;
                }
            }
            if (harvesters.Count >= entry.DesiredGatherers * 3) continue;
            foreach (var template in TemplatesOfKind(ref _aiHarvesterTemplates, "HARVESTER"))
            {
                if (FindFactory(state, template, busyOk: false) is not { } factory) continue;
                var order = new AiUnitOrder { Template = template.Name, Gatherer = true };
                state.UnitOrders.Insert(0, order);
                state.TeamCheckTick = _tick;
                if (entry.CurrentGatherers == -1)
                {
                    // "First one is automatic."
                    order.Factory = factory.Id.Value;
                    entry.CurrentGatherers = 0;
                }
                else StartTraining(state, order, busyOk: true);
                break;
            }
        }
    }

    /// <summary>The closest source of supplies beside a centre (<c>SUPPLY_CENTER_CLOSE_DIST</c> from its circle to the source's, not one of the player's own) has boxes and is not an enemy's.</summary>
    private bool HasSuppliesBeside(PlayerId player, Entity center)
    {
        var reach = Fixed.FromDecimal(SupplyCenterCloseDistance) + center.Template.BoundingCircleRadius;
        Entity? closest = null;
        var nearest = Fixed.MaxValue;
        foreach (var source in _world.EntitiesOfKind("SUPPLY_SOURCE").AsSpan())
        {
            if (source.Owner == player || source.EffectivelyDead || source.DestroyRequested) continue;
            var distance = Distance2(source, center.X, center.Y);
            if (!SquaredReach.Of(reach + source.Template.BoundingCircleRadius).Contains(distance)) continue;
            var gap = Fixed.Sqrt(distance) - source.Template.BoundingCircleRadius;
            if (gap >= nearest) continue;
            nearest = gap;
            closest = source;
        }
        if (closest is null) return false;
        if (ModuleOf<SupplyWarehouseDockUpdate>(closest) is not { } warehouse) return true;
        return warehouse.Boxes > 0 && !(!closest.Owner.IsSystem && _world.AreEnemies(player, closest.Owner));
    }

    /// <summary>"Although this is the ai, we are sending in CMD_FROM_PLAYER": the dock is the one the gatherer remembers from now on.</summary>
    private void DockAt(PlayerId player, Entity unit, Entity dock)
    {
        var actor = unit.Id;
        var target = dock.Id;
        if (_issue(player, (sequence, tick) => new DockCommand(player, sequence, tick, actor, target)).Accepted) _busyThisTick.Add(actor.Value);
    }

    // ------------------------------------------------------------------------------------------------ teams

    /// <summary><c>AISkirmishPlayer::doTeamBuilding</c>.</summary>
    private void DoTeamBuilding(AiPlayer state)
    {
        if (!_world.CanBuildUnits(state.Setup.Player)) return;
        if (!state.ReadyTeam && _tick >= state.TeamReadyTick)
        {
            state.ReadyTeam = true;
            state.TeamCheckTick = _tick;
        }
        if (_tick < state.TeamCheckTick) return;
        QueueUnits(state);
        if (state.ReadyTeam && SelectTeamToBuild(state)) QueueUnits(state);
        state.TeamCheckTick = _tick + _world.Time.OriginalFrames(AiRecheckFrames);
    }

    /// <summary>
    /// <c>AIPlayer::selectTeamToBuild</c>: of the player's team prototypes that are a good idea now (<c>isAGoodIdeaToBuildTeam</c>) the ones with the highest production priority are the
    /// candidates, and one of them is drawn from the logic random stream (<c>GameLogicRandomValue(0, count - 1)</c>). It is built like a scripted <c>BUILD_TEAM</c> at low priority and the
    /// team timer starts again (<c>TeamSeconds</c>, longer when poor, shorter when wealthy).
    /// <para>Before that <see cref="SelectTeamToReinforce"/> gets its turn, as in the reference.</para>
    /// </summary>
    private bool SelectTeamToBuild(AiPlayer state)
    {
        if (PlayerById(state.Setup.Player) is not { } owner) return false;
        var data = _options.Ai!.Data;
        List<ScriptTeamPrototype>? candidates = null;
        var highest = NoTeamPriority;
        foreach (var prototype in _program.Teams)
        {
            if (prototype.Owner != owner.Name || !IsGoodIdeaToBuildTeam(state, prototype)) continue;
            (candidates ??= []).Add(prototype);
            if (prototype.ProductionPriority > highest) highest = prototype.ProductionPriority;
        }
        // "if (selectTeamToReinforce(hiPri)) return true": a team in the field that wants a unit comes before a new team of lower or equal priority.
        DropExpiredReinforcements();
        if (SelectTeamToReinforce(state, owner, candidates is null ? NoTeamPriority : highest)) return true;
        if (candidates is null) return false;
        candidates.RemoveAll(prototype => prototype.ProductionPriority != highest);
        var pick = candidates[_world.LogicRandom.Range(0, candidates.Count - 1)];
        BuildTeam(pick.Name, idleOnly: true);
        state.ReadyTeam = false;
        state.TeamReadyTick = _tick + ScaledTimer(data.TeamSeconds, _world.MoneyOf(state.Setup.Player), data, data.TeamsPoorRate, data.TeamsWealthyRate);
        return true;
    }

    /// <summary><c>AISkirmishPlayer::isAGoodIdeaToBuildTeam</c>: the production condition holds, fewer instances than <c>teamMaxInstances</c> exist, none is being built, and it is possible.</summary>
    private bool IsGoodIdeaToBuildTeam(AiPlayer state, ScriptTeamPrototype prototype)
    {
        if (!EvaluateProductionCondition(state, prototype)) return false;
        var instances = 0;
        foreach (var team in _teams.InstancesOf(prototype.Name))
        {
            var queued = false;
            foreach (var entry in _teamQueue)
            {
                if (entry.TeamId != team.Id) continue;
                if (!entry.Ready) return false;
                queued = true;
            }
            // An instance exists while it has members or is on its way through the queues; the engine keeps the empty shell of a single-instance prototype.
            if (queued || HasCountingMember(team)) instances++;
        }
        if (instances >= prototype.MaxInstances) return false;
        return IsPossibleToBuildTeam(state, prototype);
    }

    /// <summary>
    /// <c>TeamPrototype::evaluateProductionCondition</c>: the conditions of the script named by <c>teamProductionCondition</c>, with the player as current player and no team; a script that is not
    /// meant for the player's difficulty never holds; a script with an evaluation delay is looked at only that often.
    /// </summary>
    private bool EvaluateProductionCondition(AiPlayer state, ScriptTeamPrototype prototype)
    {
        if (prototype.ProductionCondition.Length == 0 || _program.FindScript(prototype.ProductionCondition) is not { } unit) return false;
        if (!AllowedOn(unit.Script, state.Setup.Difficulty)) return false;
        if (unit.Script.DelayEvaluationSeconds > 0)
        {
            if (_productionNextTick.TryGetValue(prototype.Name, out var next) && _tick < next) return false;
            _productionNextTick[prototype.Name] = _tick + _world.Time.WholeSecondsToTicks((long)unit.Script.DelayEvaluationSeconds);
        }
        var previous = _callingTeam;
        _callingTeam = null;
        try { return EvaluateConditions(unit); }
        finally { _callingTeam = previous; }
    }

    /// <summary>
    /// <c>AIPlayer::isPossibleToBuildTeam</c> with an idle factory required: a factory of the build list for every unit type, at least one of them idle, and cash for
    /// <c>TeamResourcesToStart</c> of the team's mean cost, in whole credits as the reference's integers have it.
    /// </summary>
    private bool IsPossibleToBuildTeam(AiPlayer state, ScriptTeamPrototype prototype)
    {
        var player = state.Setup.Player;
        var anyIdle = false;
        var cost = Fixed.Zero;
        var any = false;
        foreach (var unit in prototype.Units)
        {
            if (!_world.Templates.TryGet(unit.Type, out var template)) continue;
            any = true;
            if (FindFactory(state, template, busyOk: true) is null) return false;
            if (FindFactory(state, template, busyOk: false) is not null) anyIdle = true;
            cost = Fixed.FromInt((cost + _world.ProductionCostOf(player, template) * Fixed.FromInt(unit.MaxCount + unit.MinCount) / Fixed.FromInt(2)).Floor());
        }
        if (!any) return false;
        return _world.MoneyOf(player) >= Fixed.FromInt((cost * _options.Ai!.Data.TeamResourcesToStart).Floor()) && anyIdle;
    }

    /// <summary>
    /// <c>Team::updateGenericScripts</c>: every instance of every team of the player tries its generic scripts (<c>teamGenericScriptHook0..15</c>) each frame - conditions with the team as
    /// <c>&lt;This Team&gt;</c>, then the true-actions; a one-shot script is not tried again for that instance.
    /// </summary>
    private void UpdateGenericScripts(AiPlayer state)
    {
        // The teams as they are before the first script runs (a script may create or remove teams); the list is this method's own and nothing else touches it meanwhile.
        var teams = _genericTeams;
        teams.Clear();
        foreach (var team in _teams.Values)
        {
            if (team.Owner != state.Setup.Player || !team.Created) continue;
            if (PrototypeOf(team) is not { GenericScripts.IsDefaultOrEmpty: false }) continue;
            teams.Add(team);
        }
        for (var index = 0; index < teams.Count; index++)
        {
            var team = teams[index];
            if (!_teams.TryGetValue(team.Id, out _) || PrototypeOf(team) is not { } prototype || !HasLiveMember(team)) continue;
            var done = _genericDone.GetValueOrDefault(team.Id);
            var units = GenericUnitsOf(prototype);
            for (var hook = 0; hook < prototype.GenericScripts.Length; hook++)
            {
                if ((done & (1 << hook)) != 0) continue;
                if (units[hook] is not { } unit)
                {
                    Diagnose($"AI: generic script '{prototype.GenericScripts[hook]}' of team '{prototype.Name}' is missing");
                    done |= 1 << hook;
                    continue;
                }
                var previous = _callingTeam;
                _callingTeam = team;
                try
                {
                    if (!EvaluateConditions(unit)) continue;
                    if (unit.Script.IsOneShot) done |= 1 << hook;
                    _executedScripts++;
                    ExecuteActions(unit.Script.Actions);
                }
                finally { _callingTeam = previous; }
            }
            if (done != 0) _genericDone[team.Id] = done;
        }
    }

    private ScriptUnit?[] GenericUnitsOf(ScriptTeamPrototype prototype)
    {
        if (_genericUnits.TryGetValue(prototype.Name, out var units)) return units;
        units = new ScriptUnit?[prototype.GenericScripts.Length];
        for (var hook = 0; hook < units.Length; hook++) units[hook] = _program.FindScript(prototype.GenericScripts[hook]);
        _genericUnits[prototype.Name] = units;
        return units;
    }

    /// <summary>The prototype of a team instance, looked up once per instance (the program never changes).</summary>
    private ScriptTeamPrototype? PrototypeOf(ScriptTeam team)
    {
        if (!team.InfoKnown)
        {
            team.Info = _program.FindTeam(team.Prototype);
            team.InfoKnown = true;
        }
        return team.Info;
    }

    /// <summary>Whether some member of <see cref="LiveMembers"/> counts as an object (<see cref="CountsAsObject"/>).</summary>
    private bool HasCountingMember(ScriptTeam team)
    {
        foreach (var id in team.Members)
            if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false } member && CountsAsObject(member)) return true;
        return false;
    }

    /// <summary>Whether <see cref="LiveMembers"/> yields anything.</summary>
    private bool HasLiveMember(ScriptTeam team)
    {
        foreach (var id in team.Members)
            if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false }) return true;
        return false;
    }

    /// <summary>
    /// <c>AIPlayer::queueUnits</c> for one work order of a team of an AI player: before anything is trained, units of that type are taken from the player's recruitable teams of lower
    /// priority - above all its default team, where the units of disbanded teams wait - within <c>MaxRecruitRadius</c> of the base centre (<c>Team::tryToRecruit</c>; the library's teams
    /// have no home position); a recruit walks to the base centre. An order trains one unit at a time (<c>WorkOrder::isWaitingToBuild</c>). Returns whether the order has nothing to train now.
    /// </summary>
    private bool AiRecruitsFor(TeamBuildOrder order, ScriptTeam team)
    {
        if (_ai.Count == 0 || AiOfPlayer(team.Owner) is not { } state || _program.FindTeam(team.Prototype) is not { } prototype) return false;
        var home = new ScriptWaypoint(string.Empty, state.BaseX, state.BaseY, Fixed.Zero);
        var reach = SquaredReach.Of(_options.Ai!.Data.MaxRecruitRadius);
        while (order.Queued < order.Total)
        {
            if (FindRecruit(team.Owner, order.Type, prototype, team, home, reach) is not { } recruit) break;
            foreach (var other in _teams.Values) other.Members.Remove(recruit.Id.Value);
            team.Members.Add(recruit.Id.Value);
            if (team.AttackPriority.Length > 0) _prioritySetOf[recruit.Id.Value] = team.AttackPriority;
            if (recruit.Template.Locomotor is not null) MoveTo(recruit, home.X, home.Y);
            order.Queued++;
            order.Joined++;
        }
        return order.Queued >= order.Total || order.Queued > order.Joined;
    }

    /// <summary>A team of an AI player whose orders are all met (by recruits) has nothing left in the order list; the queue entry then moves it on.</summary>
    private void AiDropFinishedTeamBuilds()
    {
        if (_ai.Count == 0) return;
        for (var index = 0; index < _teamBuilds.Count; index++)
        {
            var teamId = _teamBuilds[index].TeamId;
            if (!_teams.TryGetValue(teamId, out var team) || AiOfPlayer(team.Owner) is null) continue;
            var finished = true;
            foreach (var order in _teamBuilds)
                if (order.TeamId == teamId && order.Joined < order.Total) { finished = false; break; }
            if (!finished) continue;
            _teamBuilds.RemoveAll(order => order.TeamId == teamId);
            index = -1;
        }
    }

    /// <summary>A team that is built again starts over: its create script runs again and its generic scripts are tried again (the reference makes a new <c>Team</c>).</summary>
    private void AiResetTeamForRebuild(ScriptTeam team)
    {
        if (_ai.Count == 0) return;
        team.CreateHandled = false;
        team.CurUnits = 0;
        team.DestroyThreshold = -1;
        team.SeeEnemy = false;
        team.WasIdle = false;
        _genericDone.Remove(team.Id);
    }

    // ------------------------------------------------------------------------------------------------ skills and the enemy

    /// <summary>
    /// <c>AIPlayer::doUpgradesAndSkills</c>: with science purchase points to spend, the skill set is drawn once among the side's non-empty sets (<c>GameLogicRandomValue(0, limit)</c>) and
    /// every science of it that can be bought is bought, in the order of the set.
    /// </summary>
    private void DoUpgradesAndSkills(AiPlayer state)
    {
        var player = state.Setup.Player;
        if (_tick < _world.Time.OriginalFrames(AiSkillStartFrames) || _world.SciencePointsOf(player) <= 0 || SideOf(state) is not { } side) return;
        if (state.SkillSet == AiNoSkillSet)
        {
            var limit = 0;
            while (limit + 1 < side.SkillSets.Length && side.SkillSets[limit + 1].Length > 0) limit++;
            state.SkillSet = _world.LogicRandom.Range(0, limit);
        }
        foreach (var science in side.SkillSets[state.SkillSet])
            if (_world.CanPurchaseScienceNow(player, science)) PurchaseScience(player, science);
    }

    /// <summary><c>AIPlayer::buildUpgrade</c>: with the money for it, at the first finished, unsold building of the build list whose command set offers the upgrade and that takes the order.</summary>
    private void AiBuildUpgrade(AiPlayer state, UpgradeInfo upgrade)
    {
        var player = state.Setup.Player;
        if (_world.MoneyOf(player) < upgrade.BuildCost) return;
        foreach (var entry in state.BuildList)
        {
            if (entry.ObjectId == 0 || _world.FindEntity(new EntityId(entry.ObjectId)) is not { } factory) continue;
            if (factory.HasStatus(UnderConstructionBit) || factory.HasStatus(SoldBit) || !_world.OffersUpgrade(factory, upgrade) || ProductionOf(factory) is null) continue;
            var producer = factory.Id;
            var name = upgrade.Name;
            if (_issue(player, (sequence, tick) => new PurchaseUpgradeCommand(player, sequence, tick, name, producer)).Accepted) return;
        }
        Diagnose($"AI_PLAYER_BUILD_UPGRADE: player {player.Value} lacks a factory for '{upgrade.Name}'");
    }

    private void PurchaseScience(PlayerId player, string science) => _issue(player, (sequence, tick) => new PurchaseScienceCommand(player, sequence, tick, science));

    /// <summary>The <c>SideInfo</c> of the player's side, looked up once (the data never change).</summary>
    private AiSideInfo? SideOf(AiPlayer state)
    {
        if (!state.SideKnown)
        {
            state.Side = _options.Ai!.Data.FindSide(state.Setup.Side);
            state.SideKnown = true;
        }
        return state.Side;
    }

    /// <summary>The first module of that type on an object, if it has one.</summary>
    private static TModule? ModuleOf<TModule>(Entity entity) where TModule : class
    {
        foreach (var module in entity.ModuleArray)
            if (module is TModule found) return found;
        return null;
    }

    /// <summary><c>AISkirmishPlayer::getAiEnemy</c>: the current enemy, reconsidered every five seconds.</summary>
    private ScriptPlayer? AiEnemy(AiPlayer state)
    {
        if (_tick >= state.EnemyCheckTick)
        {
            state.EnemyCheckTick = _tick + _world.Time.OriginalFrames(AiEnemyCheckFrames);
            AcquireEnemy(state);
        }
        return state.Enemy >= 0 ? PlayerById(new PlayerId(state.Enemy)) : null;
    }

    /// <summary>
    /// <c>AISkirmishPlayer::acquireEnemy</c>: the enemy stays while it has units and a build facility. Otherwise the nearest enemy base (centre of its structures against the own base centre)
    /// wins; one that another skirmish AI already attacks counts 500 farther, one that attacks this player 25 nearer, and one without units or without a build facility comes last.
    /// </summary>
    private void AcquireEnemy(AiPlayer state)
    {
        var player = state.Setup.Player;
        if (state.Enemy >= 0 && !InBadShape(new PlayerId(state.Enemy))) return;
        var best = -1;
        var bestDistance = long.MaxValue;
        foreach (var candidate in _world.PlayerIds)
        {
            if (candidate == player || !_world.AreEnemies(player, candidate) || !PlayerHasAnyObject(candidate)) continue;
            long distance;
            if (InBadShape(candidate)) distance = long.MaxValue / 4;
            else
            {
                var centre = StructureBoundsCentre(candidate) ?? (state.BaseX, state.BaseY);
                var dx = (centre.X - state.BaseX).Floor();
                var dy = (centre.Y - state.BaseY).Floor();
                distance = dx * dx + dy * dy;
            }
            foreach (var other in _ai)
            {
                if (other.Setup.Player == candidate) continue;
                if (other.Enemy == candidate.Value) distance += 500L * 500L;
                if (other.Enemy == player.Value) distance = Math.Max(0, distance - 25L * 25L);
            }
            if (distance >= bestDistance) continue;
            best = candidate.Value;
            bestDistance = distance;
        }
        if (best >= 0) state.Enemy = best;
    }

    private bool InBadShape(PlayerId player)
    {
        var units = false;
        var facility = false;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!Counts(entity)) continue;
            if (!entity.Template.IsKindOf("STRUCTURE")) units = true;
            if (entity.Template.IsKindOf("DOZER") || (entity.Template.IsKindOf("STRUCTURE") && ProductionOf(entity) is not null)) facility = true;
            if (units && facility) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------------------------------------ script actions of the skirmish AI

    private bool TryExecuteAiAction(MapScriptAction action)
    {
        if (_ai.Count == 0) return false;
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            // doBuildBuilding -> AISkirmishPlayer::buildSpecificAIBuilding.
            case "SKIRMISH_BUILD_BUILDING":
                if (Has(type, p, 1) && CurrentAi(type) is { } building) BuildSpecificBuilding(building, Str(p, 0));
                return true;
            // doBuildBaseDefense -> AISkirmishPlayer::buildAIBaseDefense: the side's BaseDefenseStructure1.
            case "SKIRMISH_BUILD_BASE_DEFENSE_FRONT":
            case "SKIRMISH_BUILD_BASE_DEFENSE_FLANK":
                if (CurrentAi(type) is { } defender)
                {
                    if (_options.Ai!.Data.FindSide(defender.Setup.Side) is { BaseDefenseStructure.Length: > 0 } side) BuildBaseDefenseStructure(defender, side.BaseDefenseStructure, type.EndsWith("FLANK", StringComparison.Ordinal));
                    else Diagnose($"AI: side '{defender.Setup.Side}' has no base defence structure");
                }
                return true;
            // doBuildBaseStructure -> AISkirmishPlayer::buildAIBaseDefenseStructure.
            case "SKIRMISH_BUILD_STRUCTURE_FRONT":
            case "SKIRMISH_BUILD_STRUCTURE_FLANK":
                if (Has(type, p, 1) && CurrentAi(type) is { } placer) BuildBaseDefenseStructure(placer, Str(p, 0), type.EndsWith("FLANK", StringComparison.Ordinal));
                return true;
            // doBuildObjectNearestTeam -> AIPlayer::buildSpecificBuildingNearestTeam.
            case "AI_PLAYER_BUILD_TYPE_NEAREST_TEAM":
                if (Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } owner)
                {
                    if (AiOfPlayer(owner.Id) is not { } near) Diagnose($"{type}: player '{owner.Name}' is not run by the skirmish AI");
                    else if (FindTeam(Str(p, 2)) is { } team) BuildNearestTeam(near, Str(p, 1), team);
                }
                return true;
            default:
                return false;
        }
    }

    private AiPlayer? CurrentAi(string type)
    {
        if (CurrentScriptPlayer() is { } current && AiOfPlayer(current.Id) is { } state) return state;
        Diagnose($"{type}: the current player is not run by the skirmish AI");
        return null;
    }

    /// <summary><c>AISkirmishPlayer::buildSpecificAIBuilding</c>: the first entry of that type in the build list that is neither built nor asked for becomes a priority build.</summary>
    private void BuildSpecificBuilding(AiPlayer state, string templateName)
    {
        var found = false;
        foreach (var entry in state.BuildList)
        {
            if (!string.Equals(entry.Template, templateName, StringComparison.Ordinal)) continue;
            found = true;
            if (entry.ObjectId != 0 && _world.FindEntity(new EntityId(entry.ObjectId)) is not null) continue;
            if (entry.Priority) continue;
            entry.Priority = true;
            state.BuildCheckTick = _tick;
            return;
        }
        if (!found) Diagnose($"AI: building '{templateName}' is not in the build list of side '{state.Setup.Side}'");
    }

    /// <summary>
    /// <c>AISkirmishPlayer::buildAIBaseDefenseStructure</c>: defences stand on the circle of the base radius plus <c>SkirmishBaseDefenseExtraDistance</c>. A front defence looks along the
    /// <c>Center</c> approach path of the player's start position, flank defences alternate between <c>Backdoor</c> and <c>Flank</c>; the first stands on the line to the path, the next ones
    /// alternately to its left and right, four structure radii apart, up to 60 degrees to either side. The first legal place becomes a priority entry of the build list.
    /// </summary>
    private void BuildBaseDefenseStructure(AiPlayer state, string templateName, bool flank)
    {
        if (!_world.Templates.TryGet(templateName, out var template) || !template.IsInstantiable) { Diagnose($"AI: unknown base defence structure '{templateName}'"); return; }
        if (_world.Navigation is not { } navigation) return;
        var data = _options.Ai!.Data;
        var start = (state.Setup.StartIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var limit = FixedTrig.Pi / Fixed.FromInt(3);
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var label = (flank ? (state.FlankDefense & 1) != 0 ? FlankPath : BackdoorPath : CenterPath) + start;
            Fixed goalX, goalY;
            if (_program.FindPath(label) is { Waypoints.IsDefaultOrEmpty: false } path)
            {
                // getClosestWaypointOnPath: the waypoint of the path nearest to the base centre.
                var nearest = path.Waypoints[0];
                var best = Fixed.MaxValue;
                foreach (var waypoint in path.Waypoints)
                {
                    var dx = waypoint.X - state.BaseX;
                    var dy = waypoint.Y - state.BaseY;
                    var distance = dx * dx + dy * dy;
                    if (distance < best) { best = distance; nearest = waypoint; }
                }
                (goalX, goalY) = (nearest.X, nearest.Y);
            }
            else
            {
                if (flank) return;
                var enemy = AiEnemy(state);
                if (enemy is null || StructureBoundsCentre(enemy.Id) is not { } centre) return;
                (goalX, goalY) = centre;
            }
            var offsetX = goalX - state.BaseX;
            var offsetY = goalY - state.BaseY;
            var length = Fixed.Sqrt(offsetX * offsetX + offsetY * offsetY);
            if (length <= Fixed.Zero) return;
            var distanceOut = state.BaseRadius + data.SkirmishBaseDefenseExtraDistance;
            offsetX = offsetX / length * distanceOut;
            offsetY = offsetY / length * distanceOut;
            // 2 pi * (4 r / circumference) = 4 r / distance.
            var angleOffset = template.BoundingCircleRadius * Fixed.FromInt(4) / distanceOut;
            Fixed angle;
            if (flank)
            {
                var selector = state.FlankDefense >> 1;
                var left = (state.FlankDefense & 1) != 0 ? 2 : 4;
                if ((selector & 1) != 0) { state.DefenseAngles[left + 1] -= angleOffset; angle = state.DefenseAngles[left + 1]; }
                else { angle = state.DefenseAngles[left]; state.DefenseAngles[left] += angleOffset; }
            }
            else if ((state.FrontDefense & 1) != 0) { state.DefenseAngles[1] -= angleOffset; angle = state.DefenseAngles[1]; }
            else { angle = state.DefenseAngles[0]; state.DefenseAngles[0] += angleOffset; }
            if (angle > limit) return;
            var sin = FixedTrig.Sin(angle);
            var cos = FixedTrig.Cos(angle);
            var x = state.BaseX + offsetX * cos - offsetY * sin;
            var y = state.BaseY + offsetY * cos + offsetX * sin;
            if (flank) state.FlankDefense++;
            else state.FrontDefense++;
            if (_world.CheckBuildGround(template, x, y, Fixed.Zero) != LegalBuildCode.Ok) continue;
            AddPriorityBuild(state, templateName, x, y);
            return;
        }
    }

    /// <summary>
    /// <c>AIPlayer::buildSpecificBuildingNearestTeam</c>: a place near the team's position on growing squares (up to <c>2 * SUPPLY_CENTER_CLOSE_DIST</c>). As in the reference, a team position
    /// that is itself a legal place yields nothing: only the adjustment branch sets its result.
    /// </summary>
    private void BuildNearestTeam(AiPlayer state, string templateName, ScriptTeam team)
    {
        if (!_world.Templates.TryGet(templateName, out var template) || !template.IsInstantiable) { Diagnose($"AI: unknown template '{templateName}'"); return; }
        if (_world.Navigation is not { } navigation) return;
        var members = LiveMembers(team).ToArray();
        if (members.Length == 0) return;
        long sumX = 0, sumY = 0;
        foreach (var member in members) { sumX += member.X.Floor(); sumY += member.Y.Floor(); }
        var x = Fixed.FromInt(sumX / members.Length);
        var y = Fixed.FromInt(sumY / members.Length);
        if (_world.CheckBuildGround(template, x, y, Fixed.Zero) == LegalBuildCode.Ok) return;
        var cell = Fixed.FromDecimal(TerrainMap.CellSize);
        var rings = (int)(2m * SupplyCenterCloseDistance / TerrainMap.CellSize);
        for (var ring = 0; ring < rings; ring += 2)
        {
            var half = cell * Fixed.FromInt(ring) / Fixed.FromInt(2);
            var full = cell * Fixed.FromInt(ring);
            for (var step = 0; step <= ring; step++)
            {
                var along = cell * Fixed.FromInt(step);
                foreach (var (cx, cy) in new[] { (x - half + along, y - half), (x - half + along, y - half + full), (x - half, y - half + along), (x - half + full, y - half + along) })
                {
                    if (_world.CheckBuildGround(template, cx, cy, Fixed.Zero) != LegalBuildCode.Ok) continue;
                    AddPriorityBuild(state, templateName, cx, cy);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The placement half of <c>AIPlayer::buildBySupplies</c> for an AI player: the wanted place, or the first legal one on growing squares around it (up to
    /// <c>2 * SUPPLY_CENTER_CLOSE_DIST</c>), becomes a priority entry of the build list.
    /// </summary>
    private void AiBuildBySupplies(AiPlayer state, ObjectTemplate template, Fixed x, Fixed y)
    {
        if (_world.Navigation is not { } navigation) return;
        var cell = Fixed.FromDecimal(TerrainMap.CellSize);
        var rings = (int)(2m * SupplyCenterCloseDistance / TerrainMap.CellSize);
        for (var ring = 0; ring < rings; ring += 2)
        {
            var half = cell * Fixed.FromInt(ring) / Fixed.FromInt(2);
            var full = cell * Fixed.FromInt(ring);
            for (var step = 0; step <= ring; step++)
            {
                var along = cell * Fixed.FromInt(step);
                foreach (var (cx, cy) in new[] { (x - half + along, y - half), (x - half + along, y - half + full), (x - half, y - half + along), (x - half + full, y - half + along) })
                {
                    if (_world.CheckBuildGround(template, cx, cy, Fixed.Zero) != LegalBuildCode.Ok) continue;
                    AddPriorityBuild(state, template.Name, cx, cy);
                    return;
                }
            }
        }
        Diagnose($"AI: no legal place for '{template.Name}' near the supplies");
    }

    /// <summary><c>Player::addToPriorityBuildList</c>: a new entry at the head of the build list, to be built once.</summary>
    private void AddPriorityBuild(AiPlayer state, string templateName, Fixed x, Fixed y)
    {
        state.BuildList.Insert(0, new AiBuildEntry { Template = templateName, X = x, Y = y, Angle = Fixed.Zero, Rebuilds = 1, Priority = true });
        state.BuildCheckTick = _tick;
    }

    // ------------------------------------------------------------------------------------------------ state

    private void DescribeAi(IStateWriter writer)
    {
        if (_ai.Count == 0) return;
        writer.BeginArray("aiPlayers");
        foreach (var state in _ai)
        {
            writer.BeginObject(null);
            writer.Int("player", state.Setup.Player.Value);
            writer.Bool("started", state.Started);
            writer.Bool("readyTeam", state.ReadyTeam);
            writer.Bool("readyStructure", state.ReadyStructure);
            writer.Int("teamReadyTick", state.TeamReadyTick);
            writer.Int("structureReadyTick", state.StructureReadyTick);
            writer.Int("buildCheckTick", state.BuildCheckTick);
            writer.Int("teamCheckTick", state.TeamCheckTick);
            writer.Fixed("baseX", state.BaseX);
            writer.Fixed("baseY", state.BaseY);
            writer.Fixed("baseRadius", state.BaseRadius);
            writer.Int("enemy", state.Enemy);
            writer.Int("enemyCheckTick", state.EnemyCheckTick);
            writer.Int("skillSet", state.SkillSet);
            writer.BeginArray("gathererDocks");
            foreach (var (unit, dock) in state.GathererDocks)
            {
                writer.BeginObject(null);
                writer.UInt("unit", unit);
                writer.UInt("dock", dock);
                writer.EndObject();
            }
            writer.EndArray();
            writer.BeginArray("unitOrders");
            foreach (var order in state.UnitOrders)
            {
                writer.BeginObject(null);
                writer.Text("template", order.Template);
                writer.Bool("gatherer", order.Gatherer);
                writer.UInt("factory", order.Factory);
                writer.EndObject();
            }
            writer.EndArray();
            writer.Int("frontDefense", state.FrontDefense);
            writer.Int("flankDefense", state.FlankDefense);
            writer.BeginArray("defenseAngles");
            foreach (var angle in state.DefenseAngles)
            {
                writer.BeginObject(null);
                writer.Fixed("angle", angle);
                writer.EndObject();
            }
            writer.EndArray();
            writer.BeginArray("buildList");
            foreach (var entry in state.BuildList)
            {
                writer.BeginObject(null);
                writer.Text("template", entry.Template);
                writer.Fixed("x", entry.X);
                writer.Fixed("y", entry.Y);
                writer.Fixed("angle", entry.Angle);
                writer.Int("rebuilds", entry.Rebuilds);
                writer.Bool("automatic", entry.Automatic);
                writer.Bool("priority", entry.Priority);
                writer.UInt("object", entry.ObjectId);
                writer.Int("timestamp", entry.Timestamp);
                writer.Bool("underConstruction", entry.UnderConstruction);
                writer.Int("pendingUntil", entry.PendingUntil);
                writer.Bool("supply", entry.IsSupply);
                writer.Int("desiredGatherers", entry.DesiredGatherers);
                writer.Int("currentGatherers", entry.CurrentGatherers);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("aiProductionNext");
        foreach (var (name, next) in _productionNextTick)
        {
            writer.BeginObject(null);
            writer.Text("team", name);
            writer.Int("tick", next);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("aiGenericDone");
        foreach (var (team, mask) in _genericDone)
        {
            writer.BeginObject(null);
            writer.Int("team", team);
            writer.Int("mask", mask);
            writer.EndObject();
        }
        writer.EndArray();
        DescribeConditionMemory(writer);
    }

    private void LoadAi(IStateReader reader)
    {
        if (_ai.Count == 0) return;
        foreach (var item in reader.Array("aiPlayers"))
        {
            var state = AiOfPlayer(new PlayerId(checked((int)item.Int("player")))) ?? throw new SnapshotFormatException("The state names an AI player the session does not have.");
            state.Started = item.Bool("started");
            state.ReadyTeam = item.Bool("readyTeam");
            state.ReadyStructure = item.Bool("readyStructure");
            state.TeamReadyTick = item.Int("teamReadyTick");
            state.StructureReadyTick = item.Int("structureReadyTick");
            state.BuildCheckTick = item.Int("buildCheckTick");
            state.TeamCheckTick = item.Int("teamCheckTick");
            state.BaseX = item.Fixed("baseX");
            state.BaseY = item.Fixed("baseY");
            state.BaseRadius = item.Fixed("baseRadius");
            state.Enemy = checked((int)item.Int("enemy"));
            state.EnemyCheckTick = item.Int("enemyCheckTick");
            state.SkillSet = checked((int)item.Int("skillSet"));
            state.GathererDocks.Clear();
            foreach (var dock in item.Array("gathererDocks")) state.GathererDocks.Add((checked((uint)dock.UInt("unit")), checked((uint)dock.UInt("dock"))));
            state.UnitOrders.Clear();
            foreach (var order in item.Array("unitOrders"))
                state.UnitOrders.Add(new AiUnitOrder { Template = order.Text("template"), Gatherer = order.Bool("gatherer"), Factory = checked((uint)order.UInt("factory")) });
            state.FrontDefense = checked((int)item.Int("frontDefense"));
            state.FlankDefense = checked((int)item.Int("flankDefense"));
            var index = 0;
            foreach (var angle in item.Array("defenseAngles")) state.DefenseAngles[index++] = angle.Fixed("angle");
            state.BuildList.Clear();
            foreach (var entry in item.Array("buildList"))
                state.BuildList.Add(new AiBuildEntry
                {
                    Template = entry.Text("template"), X = entry.Fixed("x"), Y = entry.Fixed("y"), Angle = entry.Fixed("angle"), Rebuilds = checked((int)entry.Int("rebuilds")),
                    Automatic = entry.Bool("automatic"), Priority = entry.Bool("priority"), ObjectId = checked((uint)entry.UInt("object")), Timestamp = entry.Int("timestamp"),
                    UnderConstruction = entry.Bool("underConstruction"), PendingUntil = entry.Int("pendingUntil"), IsSupply = entry.Bool("supply"),
                    DesiredGatherers = checked((int)entry.Int("desiredGatherers")), CurrentGatherers = checked((int)entry.Int("currentGatherers")),
                });
        }
        _productionNextTick.Clear();
        foreach (var item in reader.Array("aiProductionNext")) _productionNextTick[item.Text("team")] = item.Int("tick");
        _genericDone.Clear();
        foreach (var item in reader.Array("aiGenericDone")) _genericDone[checked((int)item.Int("team"))] = checked((int)item.Int("mask"));
        LoadConditionMemory(reader);
    }
}
