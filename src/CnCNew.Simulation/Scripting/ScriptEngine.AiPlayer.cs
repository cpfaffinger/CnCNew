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
        /// <summary>A dozer or gatherer is wanted and could not be paid: team units wait (the reference puts such an order at the head of the build queue as a priority build).</summary>
        public bool HoldUnitOrders;
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

    private bool AiHoldsUnitOrders(PlayerId player) => _ai.Count > 0 && AiOfPlayer(player) is { HoldUnitOrders: true };

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

    /// <summary><c>AIPlayer::onUnitProduced</c> / <c>onStructureProduced</c>: something of an AI player came into the world, so its queues are looked at again at once.</summary>
    private void AiObserveSpawn(Entity entity)
    {
        if (_ai.Count == 0 || AiOfPlayer(entity.Owner) is not { } state) return;
        state.TeamCheckTick = _tick;
        if (entity.Template.IsKindOf("STRUCTURE")) return;
        // A unit that no team asked for belongs to the player's default team (every object of the reference has a team; recruiting takes from there).
        if (FirstTeamOf(entity.Id.Value) is not null) return;
        if (PlayerById(state.Setup.Player) is { } owner && FindTeamInstance("team" + owner.Name) is { } defaultTeam) defaultTeam.Members.Add(entity.Id.Value);
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
    /// <c>AISkirmishPlayer::processBaseBuilding</c>: one structure per round. A priority entry (one a script asked for) comes first, a power plant when power is short (or the list builds it
    /// automatically) overrides it, an automatic entry with rebuilds left comes last. A destroyed or captured structure waits <c>RebuildDelayTimeSeconds</c>; a site whose dozer is gone gets another.
    /// <para>
    /// Deviation: the reference takes the first unbuilt priority entry and builds it whatever its prerequisites (<c>buildObjectNow</c> does not test them). Here a construction is a player
    /// command with the rules of one, so the first priority entry the rules allow now is taken; one that still lacks a prerequisite waits without blocking the ones behind it.
    /// </para>
    /// </summary>
    private void ProcessBaseBuilding(AiPlayer state)
    {
        var player = state.Setup.Player;
        var data = _options.Ai!.Data;
        BindSites(state);
        var underPowered = _world.EnergyProductionOf(player) < 0;
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
                if (building is null || building.DestroyRequested || building.EffectivelyDead)
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
                    building = null;
                    entry.ObjectId = 0;
                    entry.UnderConstruction = false;
                    entry.Timestamp = _tick + TickOrder.Next;
                }
                else if (building.HasStatus(UnderConstructionBit))
                {
                    entry.UnderConstruction = true;
                    if (template.IsKindOf("FS_POWER") && !template.IsKindOf("CASH_GENERATOR")) powerUnderConstruction = true;
                    var builder = _world.FindEntity(building.BuilderId);
                    if (builder is null || builder.EffectivelyDead || builder.Owner != player || AiOf(builder) is not { IsBuilding: true })
                    {
                        if (FindDozer(state, building.X, building.Y) is { } helper)
                        {
                            var site = building.Id;
                            var helperId = helper.Id;
                            _issue(player, (sequence, tick) => new ResumeConstructCommand(player, sequence, tick, helperId, site));
                        }
                    }
                }
                else if (entry.UnderConstruction)
                {
                    // onStructureProduced: the structure is finished.
                    entry.UnderConstruction = false;
                    state.TeamCheckTick = _tick;
                    CheckForSupplyCenter(state, entry, building);
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
            if (entry.Priority && !chosenIsPriority && RulesAllow(state, entry))
            {
                chosen = entry;
                chosenIsPriority = true;
            }
            if (template.IsKindOf("FS_POWER") && power is null && !template.IsKindOf("CASH_GENERATOR") && (underPowered || entry.Automatic)) power = entry;
            if (!entry.Automatic) continue;
            if (FindDozer(state, entry.X, entry.Y) is null)
            {
                if (underPowered) QueueDozer(state);
                continue;
            }
            if ((entry.Rebuilds > 0 || entry.Rebuilds < 0) && chosen is null) chosen = entry;
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

    /// <summary>Whether a construction order for the entry would pass everything but money and the exact place.</summary>
    private bool RulesAllow(AiPlayer state, AiBuildEntry entry)
    {
        if (FindDozer(state, entry.X, entry.Y) is not { } dozer) return true;
        var player = state.Setup.Player;
        return _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, dozer.Id, entry.Template, entry.X, entry.Y, entry.Angle))
            is 0 or CommandRejection.InsufficientFunds or CommandRejection.InvalidPlacement;
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
    /// <c>AIPlayer::buildStructureWithDozer</c>: a dozer, the money, and the place of the entry - or, when that place is not legal, the nearest legal one on growing squares around it.
    /// </summary>
    private bool BuildStructureWithDozer(AiPlayer state, AiBuildEntry entry)
    {
        var player = state.Setup.Player;
        if (FindDozer(state, entry.X, entry.Y) is not { } dozer) return false;
        if (!_world.Templates.TryGet(entry.Template, out var template)) return false;
        var rejection = _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, dozer.Id, entry.Template, entry.X, entry.Y, entry.Angle));
        var (x, y) = (entry.X, entry.Y);
        if (rejection == CommandRejection.InvalidPlacement && _world.Navigation is { } navigation)
        {
            var cell = Fixed.FromDecimal(TerrainMap.CellSize);
            var found = false;
            // posOffset grows by four cells a round; along a side the candidates are two cells apart (the loops of the reference with the skirmish increments).
            for (var ring = 4; ring < AiPlacementLimitCells && !found; ring += 4)
            {
                var half = cell * Fixed.FromInt(ring) / Fixed.FromInt(2);
                var full = cell * Fixed.FromInt(ring);
                for (var step = 0; step <= ring && !found; step += 2)
                {
                    var along = cell * Fixed.FromInt(step);
                    foreach (var (cx, cy) in new[]
                             {
                                 (entry.X - half + along, entry.Y - half), (entry.X - half + along, entry.Y - half + full),
                                 (entry.X - half, entry.Y - half + along), (entry.X - half + full, entry.Y - half + along),
                             })
                    {
                        if (!navigation.IsFootprintLegal(template, cx, cy, entry.Angle)) continue;
                        (x, y) = (cx, cy);
                        found = true;
                        break;
                    }
                }
            }
            if (!found) return false;
            rejection = _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, dozer.Id, entry.Template, x, y, entry.Angle));
        }
        if (rejection != 0) return false;
        var builder = dozer.Id;
        var name = entry.Template;
        var angle = entry.Angle;
        if (!_issue(player, (sequence, tick) => new ConstructCommand(player, sequence, tick, builder, name, x, y, angle)).Accepted) return false;
        entry.X = x;
        entry.Y = y;
        entry.PendingUntil = _tick + _world.Time.OriginalFrames(AiSitePendingFrames);
        _busyThisTick.Add(builder.Value);
        return true;
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
    /// <c>AIPlayer::findDozer</c>: a dozer of the player that is not building and - a worker - not carrying supplies; an idle one first, the nearest of those. When the player has no dozer
    /// at all one is ordered (<c>queueDozer</c>).
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
            // "If it is gathering supplies, don't steal it": a worker that carries boxes or is on its way to or at a dock.
            if (!ai.IsBuilding && ai is ISupplyCarrier carrier && (carrier.Boxes > 0 || ai.IsDocking)) continue;
            needDozer = false;
            if (ai.IsBuilding || _busyThisTick.Contains(entity.Id.Value)) continue;
            var idle = IsIdle(entity);
            if (idle || any is null) any = entity;
            if (!idle) continue;
            var distance = Distance2(entity, x, y);
            if (closest is null || distance < closestDistance) { closest = entity; closestDistance = distance; }
        }
        if (needDozer) QueueDozer(state);
        return closest ?? any;
    }

    /// <summary><c>AIPlayer::queueDozer</c>: unless one is in production already, the first dozer type a factory of the player can make is ordered.</summary>
    private void QueueDozer(AiPlayer state) => QueueFirstOfKind(state, "DOZER");

    private bool QueueFirstOfKind(AiPlayer state, string kind)
    {
        var player = state.Setup.Player;
        var factories = new List<(Entity Entity, ProductionUpdate Production)>();
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!Counts(entity) || entity.HasStatus(UnderConstructionBit) || ProductionOf(entity) is not { } production) continue;
            foreach (var item in production.Queue)
                if (_world.Templates.Templates[item.TemplateIndex].IsKindOf(kind)) return false;
            if (production.HasRoom) factories.Add((entity, production));
        }
        // The reference walks every template and asks each factory whether it can make it (isPossibleToMakeUnit: a button of its command set); here the factories are asked for
        // what their command sets build, in the order of the buttons.
        foreach (var (factory, _) in factories)
            foreach (var set in factory.Template.CommandSets)
                foreach (var name in _world.Templates.Commands.BuildsOf(set))
                {
                    if (!_world.Templates.TryGet(name, out var template) || !template.IsKindOf(kind) || template.IsKindOf("STRUCTURE") || !template.IsInstantiable) continue;
                    if (!_world.CanQueueProductionTemplate(player, factory.Id, template.Name)) continue;
                    if (_world.MoneyOf(player) < _world.ProductionCostOf(player, template))
                    {
                        // A priority order of the reference waits at the head of the queue; here the team orders wait until this one is paid.
                        state.HoldUnitOrders = true;
                        return false;
                    }
                    var producer = factory.Id;
                    var unit = template.Name;
                    // The reference turns unit production on for this one order even when a script has switched it off.
                    state.HoldUnitOrders = false;
                    return _issue(player, (sequence, tick) => new QueueProductionCommand(player, sequence, tick, producer, unit)).Accepted;
                }
        return false;
    }

    // ------------------------------------------------------------------------------------------------ supplies

    /// <summary><c>AIPlayer::checkForSupplyCenter</c>: a finished supply centre wants the gatherers of its side for the player's difficulty, plus the one it comes with.</summary>
    private void CheckForSupplyCenter(AiPlayer state, AiBuildEntry entry, Entity building)
    {
        if (ModuleOf<SupplyCenterDockUpdate>(building) is null) return;
        entry.IsSupply = true;
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
    /// <c>AIPlayer::queueSupplyTruck</c>, reduced to its effect: every finished supply centre of the player that still has a stocked, non-hostile warehouse within <c>SUPPLY_CENTER_CLOSE_DIST</c>
    /// wants its gatherers; while the player has fewer than all centres want and none is in production, one is ordered. A gatherer that stands idle is sent to the nearest stocked warehouse
    /// (the reference re-docks it at its centre, which starts the same loop). Centres are found in the world, not only in the build list: the scripts place them by the supplies
    /// (<c>AI_PLAYER_BUILD_SUPPLY_CENTER</c>).
    /// </summary>
    private void QueueSupplyTruck(AiPlayer state)
    {
        var player = state.Setup.Player;
        var close = Fixed.FromDecimal(SupplyCenterCloseDistance);
        var warehouses = new List<Entity>();
        foreach (var entity in _world.Entities)
        {
            if (entity.EffectivelyDead || entity.DestroyRequested || ModuleOf<SupplyWarehouseDockUpdate>(entity) is not { Boxes: > 0 }) continue;
            if (!entity.Owner.IsSystem && _world.AreEnemies(player, entity.Owner)) continue;
            warehouses.Add(entity);
        }
        var wanted = 0;
        var harvesters = new List<Entity>();
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!Counts(entity) || entity.HasStatus(UnderConstructionBit)) continue;
            if (entity.Template.IsKindOf("HARVESTER") && AiOf(entity) is ISupplyCarrier) harvesters.Add(entity);
            if (ModuleOf<SupplyCenterDockUpdate>(entity) is null || entity.Template.IsKindOf("REBUILD_HOLE")) continue;
            var reach = SquaredReach.Of(close + entity.Template.BoundingCircleRadius);
            if (warehouses.Any(warehouse => reach.Contains(Distance2(warehouse, entity.X, entity.Y)))) wanted += GatherersWanted(state) + 1;
        }
        if (warehouses.Count == 0) return;
        foreach (var harvester in harvesters)
        {
            if (!IsIdle(harvester) || AiOf(harvester) is not { } ai || ai.IsBuilding || ai.IsDocking) continue;
            Entity? nearest = null;
            var best = Fixed.MaxValue;
            foreach (var warehouse in warehouses)
            {
                var distance = Distance2(warehouse, harvester.X, harvester.Y);
                if (distance < best) { best = distance; nearest = warehouse; }
            }
            if (nearest is null) continue;
            var actor = harvester.Id;
            var dock = nearest.Id;
            if (_issue(player, (sequence, tick) => new DockCommand(player, sequence, tick, actor, dock)).Accepted) _busyThisTick.Add(actor.Value);
        }
        if (harvesters.Count < wanted) QueueFirstOfKind(state, "HARVESTER");
    }

    // ------------------------------------------------------------------------------------------------ teams

    /// <summary><c>AISkirmishPlayer::doTeamBuilding</c>.</summary>
    private void DoTeamBuilding(AiPlayer state)
    {
        if (!_world.CanBuildUnits(state.Setup.Player)) return;
        if (_tick >= state.TeamCheckTick) state.HoldUnitOrders = false;
        if (!state.ReadyTeam && _tick >= state.TeamReadyTick)
        {
            state.ReadyTeam = true;
            state.TeamCheckTick = _tick;
        }
        if (_tick < state.TeamCheckTick) return;
        QueueSupplyTruck(state);
        if (state.ReadyTeam) SelectTeamToBuild(state);
        state.TeamCheckTick = _tick + _world.Time.OriginalFrames(AiRecheckFrames);
    }

    /// <summary>
    /// <c>AIPlayer::selectTeamToBuild</c>: of the player's team prototypes that are a good idea now (<c>isAGoodIdeaToBuildTeam</c>) the ones with the highest production priority are the
    /// candidates, and one of them is drawn from the logic random stream (<c>GameLogicRandomValue(0, count - 1)</c>). It is built like a scripted <c>BUILD_TEAM</c> at low priority and the
    /// team timer starts again (<c>TeamSeconds</c>, longer when poor, shorter when wealthy).
    /// <para>Not modelled: <c>selectTeamToReinforce</c> (<c>teamAutoReinforce</c>, 26 teams of the shipped library); a team that asks for it is counted in <see cref="Diagnostics"/> when it is chosen.</para>
    /// </summary>
    private void SelectTeamToBuild(AiPlayer state)
    {
        if (PlayerById(state.Setup.Player) is not { } owner) return;
        var data = _options.Ai!.Data;
        List<ScriptTeamPrototype>? candidates = null;
        var highest = int.MinValue;
        foreach (var prototype in _program.Teams)
        {
            if (prototype.Owner != owner.Name || !IsGoodIdeaToBuildTeam(state, prototype)) continue;
            (candidates ??= []).Add(prototype);
            if (prototype.ProductionPriority > highest) highest = prototype.ProductionPriority;
        }
        if (candidates is null) return;
        candidates.RemoveAll(prototype => prototype.ProductionPriority != highest);
        var pick = candidates[_world.LogicRandom.Range(0, candidates.Count - 1)];
        if (pick.AutoReinforce) Diagnose("AI: automatic team reinforcement is not modelled");
        BuildTeam(pick.Name, idleOnly: true);
        state.ReadyTeam = false;
        state.TeamReadyTick = _tick + ScaledTimer(data.TeamSeconds, _world.MoneyOf(state.Setup.Player), data, data.TeamsPoorRate, data.TeamsWealthyRate);
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
    /// <c>AIPlayer::isPossibleToBuildTeam</c> with an idle factory required: a factory for every unit type, at least one of them idle, and cash for <c>TeamResourcesToStart</c> of the team's
    /// mean cost.
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
            var factory = false;
            foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            {
                if (!Counts(entity) || entity.HasStatus(UnderConstructionBit) || ProductionOf(entity) is not { } production) continue;
                if (!_world.CanQueueProductionTemplate(player, entity.Id, unit.Type)) continue;
                factory = true;
                if (production.Queue.Length == 0) { anyIdle = true; break; }
            }
            if (!factory) return false;
            cost += _world.ProductionCostOf(player, template) * Fixed.FromInt(unit.MaxCount + unit.MinCount) / Fixed.FromInt(2);
        }
        if (!any) return false;
        return _world.MoneyOf(player) >= cost * _options.Ai!.Data.TeamResourcesToStart && anyIdle;
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
            if (!navigation.IsFootprintLegal(template, x, y, Fixed.Zero)) continue;
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
        if (navigation.IsFootprintLegal(template, x, y, Fixed.Zero)) return;
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
                    if (!navigation.IsFootprintLegal(template, cx, cy, Fixed.Zero)) continue;
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
                    if (!navigation.IsFootprintLegal(template, cx, cy, Fixed.Zero)) continue;
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
            writer.Bool("holdUnitOrders", state.HoldUnitOrders);
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
            state.HoldUnitOrders = item.Bool("holdUnitOrders");
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
                    DesiredGatherers = checked((int)entry.Int("desiredGatherers")),
                });
        }
        _productionNextTick.Clear();
        foreach (var item in reader.Array("aiProductionNext")) _productionNextTick[item.Text("team")] = item.Int("tick");
        _genericDone.Clear();
        foreach (var item in reader.Array("aiGenericDone")) _genericDone[checked((int)item.Int("team"))] = checked((int)item.Int("mask"));
        LoadConditionMemory(reader);
    }
}
