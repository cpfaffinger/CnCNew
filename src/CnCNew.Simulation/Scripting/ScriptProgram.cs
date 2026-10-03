using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Scripting;

/// <summary>A map side as the script engine sees it: the player behind a <c>Side</c> parameter (<c>SidesList</c>, <c>playerName</c> key).</summary>
/// <param name="SideIndex">Index into <see cref="MapData.Sides"/>; side 0 is the unnamed neutral side (<see cref="PlayerId.System"/>), every other side is player <c>SideIndex</c>.</param>
public sealed record ScriptPlayer(int SideIndex, PlayerId Id, string Name, string DisplayName, string Faction, bool IsHuman, ImmutableArray<string> Allies, ImmutableArray<string> Enemies, int StartIndex = 0);

/// <summary>A <c>TeamPrototype</c> of the map's <c>TeamsList</c> (<c>teamName</c>, <c>teamOwner</c>, <c>teamIsSingleton</c>, <c>teamMaxInstances</c>).</summary>
public sealed record ScriptTeamPrototype(string Name, string Owner, bool IsSingleton, int MaxInstances)
{
    /// <summary>The team's unit composition (<c>teamUnitType1..7</c> with <c>teamUnitMaxCount</c>): what a reinforcement call creates.</summary>
    public ImmutableArray<ScriptTeamUnit> Units { get; init; } = [];

    /// <summary><c>teamTransport</c>: the transport a reinforcement arrives in; empty when the units arrive by themselves.</summary>
    public string Transport { get; init; } = string.Empty;

    /// <summary>The team event scripts (<c>teamOnCreateScript</c>, <c>teamOnIdleScript</c>, <c>teamEnemySightedScript</c>, <c>teamAllClearScript</c>, <c>teamOnDestroyedScript</c>, <c>teamOnUnitDestroyedScript</c>); empty when absent.</summary>
    public string OnCreateScript { get; init; } = string.Empty;
    public string OnIdleScript { get; init; } = string.Empty;
    public string EnemySightedScript { get; init; } = string.Empty;
    public string AllClearScript { get; init; } = string.Empty;
    public string OnDestroyedScript { get; init; } = string.Empty;
    public string OnUnitDestroyedScript { get; init; } = string.Empty;

    /// <summary><c>teamDestroyedThreshold</c>: the share of the team that has to be lost before <see cref="OnDestroyedScript"/> runs (0: all of it).</summary>
    public Fixed DestroyedThreshold { get; init; }

    /// <summary>Whether the prototype has any event script, i.e. whether its instances carry <see cref="ScriptTeam"/> event state.</summary>
    public bool HasEventScripts => OnCreateScript.Length + OnIdleScript.Length + EnemySightedScript.Length + AllClearScript.Length + OnDestroyedScript.Length + OnUnitDestroyedScript.Length > 0;

    /// <summary><c>teamHome</c>: the waypoint recruited units walk to (<c>RECRUIT_TEAM</c>); empty when the team has none.</summary>
    public string Home { get; init; } = string.Empty;

    /// <summary><c>teamProductionPriority</c>: a team may only recruit from teams of a lower priority.</summary>
    public int ProductionPriority { get; init; }

    /// <summary><c>teamProductionPrioritySuccessIncrease</c> / <c>teamProductionPriorityFailureDecrease</c>: what <c>TEAM_INCREASE_PRIORITY</c> / <c>TEAM_DECREASE_PRIORITY</c> add to or take from the priority.</summary>
    public int PrioritySuccessIncrease { get; init; }
    public int PriorityFailureDecrease { get; init; }

    /// <summary><c>teamIsAIRecruitable</c>: the team's units may be taken by <c>RECRUIT_TEAM</c> without <c>TEAM_AVAILABLE_FOR_RECRUITMENT</c>.</summary>
    public bool AiRecruitable { get; init; }

    /// <summary><c>teamReinforcementOrigin</c>: the waypoint a reinforcement starts from; empty: it starts at its destination.</summary>
    public string ReinforcementOrigin { get; init; } = string.Empty;

    /// <summary><c>teamStartsFull</c>: the team's units are loaded into the transports of the team before it moves (<c>doCreateReinforcements</c>).</summary>
    public bool StartsFull { get; init; }

    /// <summary><c>teamTransportsExit</c>: the transport leaves the map after it unloaded instead of staying (<c>aiMoveToAndEvacuateAndExit</c>).</summary>
    public bool TransportsExit { get; init; }

    /// <summary><c>teamProductionCondition</c> + <c>teamExecutesActionsOnCreate</c>: when the flag is set, creating the team also runs the true-actions of the named script (<c>TeamFactory::createInactiveTeam</c>).</summary>
    public string ProductionCondition { get; init; } = string.Empty;
    public bool ExecutesActionsOnCreate { get; init; }

    /// <summary><c>teamInitialIdleFrames</c>: how long the AI may take to build the team before it is disbanded (0: unlimited).</summary>
    public int InitialIdleFrames { get; init; }
}

/// <summary>One <c>teamUnitTypeN</c> line of a team: the object type and how many of it the team has at most.</summary>
public sealed record ScriptTeamUnit(string Type, int MinCount, int MaxCount);

/// <summary>A named trigger area (<c>PolygonTrigger</c>); point-in-polygon follows <c>PolygonTrigger::pointInTrigger</c> (even-odd ray cast on the integer outline).</summary>
public sealed record ScriptArea(string Name, ImmutableArray<(int X, int Y)> Points)
{
    public bool Contains(Fixed x, Fixed y)
    {
        if (Points.Length < 3) return false;
        var px = x.Floor();
        var py = y.Floor();
        var inside = false;
        for (int index = 0, previous = Points.Length - 1; index < Points.Length; previous = index++)
        {
            var (ax, ay) = Points[index];
            var (bx, by) = Points[previous];
            if ((ay > py) == (by > py)) continue;
            // x of the edge at py, compared without division: px < bx + (py - by) * (ax - bx) / (ay - by)
            var lhs = (px - bx) * (ay - by);
            var rhs = (py - by) * (ax - bx);
            if (ay - by > 0 ? lhs < rhs : lhs > rhs) inside = !inside;
        }
        return inside;
    }
}

public sealed record ScriptWaypoint(string Name, Fixed X, Fixed Y, Fixed Z);

/// <summary>A labelled waypoint path (<c>waypointPathLabel1..3</c>): the first waypoint carrying the label, then the chain of first outgoing links.</summary>
public sealed record ScriptWaypointPath(string Label, ImmutableArray<ScriptWaypoint> Waypoints, bool Loops);

/// <summary>A pre-placed object of the map (<c>ObjectsList</c>), not a waypoint.</summary>
/// <param name="ScriptName">The <c>objectName</c> key (what <c>Unit</c> script parameters refer to); empty for anonymous objects.</param>
/// <param name="TeamName">The <c>originalOwner</c> team prototype name; empty when the object belongs to no team.</param>
public sealed record ScriptMapObject(string Template, string ScriptName, string TeamName, Fixed X, Fixed Y, Fixed Z, Fixed Angle, int HealthPercent, bool Enabled);

/// <summary>One script with the coordinates the engine needs to address it: which side's list, which group, in which order.</summary>
public sealed class ScriptUnit(int index, int listIndex, int groupIndex, MapScript script, bool isSubroutine)
{
    public int Index { get; } = index;
    public int ListIndex { get; } = listIndex;

    /// <summary>Index into <see cref="ScriptProgram.Groups"/>, or -1 for a script outside any group.</summary>
    public int GroupIndex { get; } = groupIndex;

    public MapScript Script { get; } = script;
    public bool IsSubroutine { get; } = isSubroutine;
    public string Name => Script.Name;
}

public sealed class ScriptGroupUnit(int index, int listIndex, MapScriptGroup group, ImmutableArray<int> scriptIndices)
{
    public int Index { get; } = index;
    public int ListIndex { get; } = listIndex;
    public MapScriptGroup Group { get; } = group;
    public ImmutableArray<int> ScriptIndices { get; } = scriptIndices;
    public string Name => Group.Name;
}

/// <summary>
/// The static, immutable script content of one map, reshaped for execution: scripts and groups flattened in the order the engine walks them
/// (<c>ScriptEngine::update</c>: per side the top-level scripts, then the groups in file order), name indexes with the reference's
/// first-match rule (<c>findScript</c>/<c>findGroup</c>), and the map facts conditions and actions read (players, teams, waypoints, areas,
/// placed objects). Built once per map; carries no mutable state, so one program serves any number of engines.
/// </summary>
public sealed class ScriptProgram
{
    private readonly Dictionary<string, int> _scriptByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _groupByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptPlayer> _playerByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptTeamPrototype> _teamByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptWaypoint> _waypointByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScriptArea> _areaByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptWaypointPath> _pathByLabel = new(StringComparer.OrdinalIgnoreCase);

    private ScriptProgram(ImmutableArray<ScriptPlayer> players, ImmutableArray<ScriptTeamPrototype> teams, ImmutableArray<ScriptWaypoint> waypoints,
        ImmutableArray<ScriptArea> areas, ImmutableArray<ScriptWaypointPath> paths, ImmutableArray<ScriptMapObject> objects,
        ImmutableArray<ScriptUnit> units, ImmutableArray<ScriptGroupUnit> groups, int listCount, string? fingerprint = null)
    {
        Players = players;
        Teams = teams;
        Waypoints = waypoints;
        Areas = areas;
        Paths = paths;
        Objects = objects;
        Units = units;
        Groups = groups;
        ListCount = listCount;
        foreach (var player in players) _playerByName.TryAdd(player.Name, player);
        foreach (var team in teams) _teamByName.TryAdd(team.Name, team);
        foreach (var waypoint in waypoints) _waypointByName.TryAdd(waypoint.Name, waypoint);
        foreach (var area in areas) _areaByName.TryAdd(area.Name, area);
        foreach (var path in paths) _pathByLabel.TryAdd(path.Label, path);
        // ScriptEngine::findScript: side by side, top-level scripts first, then the scripts of each group; the first name wins.
        // ScriptEngine::findGroup: the same walk over groups.
        for (var listIndex = 0; listIndex < listCount; listIndex++)
        {
            foreach (var unit in units)
                if (unit.ListIndex == listIndex && unit.GroupIndex < 0) _scriptByName.TryAdd(unit.Name, unit.Index);
            foreach (var group in groups)
            {
                if (group.ListIndex != listIndex) continue;
                foreach (var scriptIndex in group.ScriptIndices) _scriptByName.TryAdd(units[scriptIndex].Name, scriptIndex);
            }
        }
        foreach (var group in groups) _groupByName.TryAdd(group.Name, group.Index);
        Fingerprint = fingerprint ?? ComputeFingerprint();
    }

    public ImmutableArray<ScriptPlayer> Players { get; }
    public ImmutableArray<ScriptTeamPrototype> Teams { get; }
    public ImmutableArray<ScriptWaypoint> Waypoints { get; }
    public ImmutableArray<ScriptArea> Areas { get; }
    public ImmutableArray<ScriptWaypointPath> Paths { get; }
    public ImmutableArray<ScriptMapObject> Objects { get; }

    /// <summary>Every script in execution order (list by list; within a list the top-level scripts, then each group's scripts).</summary>
    public ImmutableArray<ScriptUnit> Units { get; }

    public ImmutableArray<ScriptGroupUnit> Groups { get; }
    public int ListCount { get; }

    /// <summary>Structural hash (names, sizes, flags, action/condition types); a savegame refuses a program with another fingerprint.</summary>
    public string Fingerprint { get; }

    public ScriptUnit? FindScript(string name) => _scriptByName.TryGetValue(name, out var index) ? Units[index] : null;
    public ScriptGroupUnit? FindGroup(string name) => _groupByName.TryGetValue(name, out var index) ? Groups[index] : null;
    public ScriptPlayer? FindPlayer(string name) => _playerByName.GetValueOrDefault(name);
    public ScriptTeamPrototype? FindTeam(string name) => _teamByName.GetValueOrDefault(name);
    public ScriptWaypoint? FindWaypoint(string name) => _waypointByName.GetValueOrDefault(name);
    public ScriptArea? FindArea(string name) => _areaByName.GetValueOrDefault(name);
    public ScriptWaypointPath? FindPath(string label) => _pathByLabel.GetValueOrDefault(label);

    /// <summary>
    /// The program for a mission whose human side is a chosen persona (<c>MissionOptions.HumanFaction</c>, the Generals Challenge general): the human side — the one named
    /// <paramref name="sideName"/>, else the one the map marks human — gets <paramref name="faction"/>; when the map has no such side (<c>GC_LaserGeneral</c> defines only its
    /// opponents) a new human side is appended, named <paramref name="sideName"/> (default <c>ThePlayer</c>), enemy of every non-civilian side of the map and enemy to it in
    /// return. The fingerprint stays the map's, so a savegame still recognises the program; the options say how it was adapted.
    /// </summary>
    public ScriptProgram WithHumanSide(string sideName, string faction)
    {
        if (faction.Length == 0) return this;
        // A named side that exists is the human side; a name that does not exist asks for a new side; no name means the side the map marks human.
        var existing = sideName.Length > 0
            ? Players.FirstOrDefault(player => !player.Id.IsSystem && player.Name == sideName)
            : Players.FirstOrDefault(player => !player.Id.IsSystem && player.IsHuman);
        ImmutableArray<ScriptPlayer> players;
        if (existing is not null)
        {
            if (existing.Faction == faction && existing.IsHuman) return this;
            players = Players.Replace(existing, existing with { Faction = faction, IsHuman = true });
        }
        else
        {
            var name = sideName.Length > 0 ? sideName : "ThePlayer";
            static bool Hostile(ScriptPlayer player) => !player.Id.IsSystem && player.Name.Length > 0 && !player.Faction.Contains("Civilian", StringComparison.Ordinal);
            var builder = ImmutableArray.CreateBuilder<ScriptPlayer>();
            foreach (var player in Players) builder.Add(Hostile(player) ? player with { Enemies = player.Enemies.Contains(name) ? player.Enemies : [..player.Enemies, name] } : player);
            builder.Add(new ScriptPlayer(Players.Length, new PlayerId(Players.Length), name, name, faction, true, [], [..Players.Where(Hostile).Select(player => player.Name)]));
            players = builder.ToImmutable();
        }
        return new ScriptProgram(players, Teams, Waypoints, Areas, Paths, Objects, Units, Groups, ListCount, Fingerprint);
    }

    public static ScriptProgram Build(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var players = ImmutableArray.CreateBuilder<ScriptPlayer>();
        for (var side = 0; side < map.Sides.Length; side++)
        {
            var properties = map.Sides[side].Properties;
            var name = properties.GetText("playerName") ?? string.Empty;
            players.Add(new ScriptPlayer(side, new PlayerId(side), name, properties.GetText("playerDisplayName") ?? name,
                properties.GetText("playerFaction") ?? string.Empty, properties.GetBool("playerIsHuman"),
                SplitNames(properties.GetText("playerAllies")), SplitNames(properties.GetText("playerEnemies")), properties.TryGetInt("multiplayerStartIndex", out var startIndex) ? startIndex : 0));
        }

        var teams = ImmutableArray.CreateBuilder<ScriptTeamPrototype>();
        foreach (var team in map.Teams)
        {
            var name = team.GetText("teamName");
            if (string.IsNullOrEmpty(name)) continue;
            var teamUnits = ImmutableArray.CreateBuilder<ScriptTeamUnit>();
            for (var slot = 1; slot <= 7; slot++)
            {
                var unitType = team.GetText($"teamUnitType{slot}");
                if (string.IsNullOrEmpty(unitType)) continue;
                teamUnits.Add(new ScriptTeamUnit(unitType, team.TryGetInt($"teamUnitMinCount{slot}", out var min) ? min : 0, team.TryGetInt($"teamUnitMaxCount{slot}", out var maxCount) ? maxCount : 0));
            }
            teams.Add(new ScriptTeamPrototype(name, team.GetText("teamOwner") ?? string.Empty, team.GetBool("teamIsSingleton"), team.TryGetInt("teamMaxInstances", out var max) ? max : 0)
            {
                Units = teamUnits.ToImmutable(),
                Transport = team.GetText("teamTransport") ?? string.Empty,
                ReinforcementOrigin = team.GetText("teamReinforcementOrigin") ?? string.Empty,
                StartsFull = team.GetBool("teamStartsFull"),
                TransportsExit = team.GetBool("teamTransportsExit"),
                ProductionCondition = team.GetText("teamProductionCondition") ?? string.Empty,
                ExecutesActionsOnCreate = team.GetBool("teamExecutesActionsOnCreate"),
                InitialIdleFrames = team.TryGetInt("teamInitialIdleFrames", out var idleFrames) ? idleFrames : 0,
                Home = team.GetText("teamHome") ?? string.Empty,
                OnCreateScript = EventScript(team, "teamOnCreateScript"),
                OnIdleScript = EventScript(team, "teamOnIdleScript"),
                EnemySightedScript = EventScript(team, "teamEnemySightedScript"),
                AllClearScript = EventScript(team, "teamAllClearScript"),
                OnDestroyedScript = EventScript(team, "teamOnDestroyedScript"),
                OnUnitDestroyedScript = EventScript(team, "teamOnUnitDestroyedScript"),
                DestroyedThreshold = team.TryGetReal("teamDestroyedThreshold", out var threshold) ? Fixed.FromDecimal(threshold) : Fixed.Zero,
                ProductionPriority = team.TryGetInt("teamProductionPriority", out var priority) ? priority : 0,
                PrioritySuccessIncrease = team.TryGetInt("teamProductionPrioritySuccessIncrease", out var increase) ? increase : 0,
                PriorityFailureDecrease = team.TryGetInt("teamProductionPriorityFailureDecrease", out var decrease) ? decrease : 0,
                AiRecruitable = team.GetBool("teamIsAIRecruitable"),
            });
        }

        var waypoints = ImmutableArray.CreateBuilder<ScriptWaypoint>();
        var byId = new Dictionary<int, (ScriptWaypoint Waypoint, MapObject Source)>();
        foreach (var obj in map.Objects)
        {
            if (!obj.IsWaypoint) continue;
            var name = obj.Properties.GetText("waypointName");
            if (string.IsNullOrEmpty(name)) continue;
            var waypoint = new ScriptWaypoint(name, Fixed.FromDecimal(obj.X), Fixed.FromDecimal(obj.Y), Fixed.FromDecimal(obj.Z));
            waypoints.Add(waypoint);
            if (obj.Properties.TryGetInt("waypointID", out var id)) byId[id] = (waypoint, obj);
        }
        var links = new Dictionary<int, int>();
        foreach (var link in map.WaypointLinks) links.TryAdd(link.From, link.To);
        var paths = ImmutableArray.CreateBuilder<ScriptWaypointPath>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, entry) in byId.OrderBy(pair => pair.Key))
        {
            for (var slot = 1; slot <= 3; slot++)
            {
                var label = entry.Source.Properties.GetText($"waypointPathLabel{slot}");
                if (string.IsNullOrEmpty(label) || !labels.Add(label)) continue;
                var chain = ImmutableArray.CreateBuilder<ScriptWaypoint>();
                var visited = new HashSet<int>();
                var current = id;
                var loops = false;
                while (byId.TryGetValue(current, out var step) && visited.Add(current))
                {
                    chain.Add(step.Waypoint);
                    if (!links.TryGetValue(current, out var next)) break;
                    if (next == id) { loops = true; break; }
                    current = next;
                }
                paths.Add(new ScriptWaypointPath(label, chain.ToImmutable(), loops));
            }
        }

        var areas = ImmutableArray.CreateBuilder<ScriptArea>();
        foreach (var trigger in map.Triggers)
            if (!trigger.IsWater && !trigger.IsRiver) areas.Add(new ScriptArea(trigger.Name, [..trigger.Points.Select(point => (point.X, point.Y))]));

        var objects = ImmutableArray.CreateBuilder<ScriptMapObject>();
        foreach (var obj in map.Objects)
        {
            if (obj.IsWaypoint) continue;
            var health = obj.Properties.TryGetInt("objectInitialHealth", out var value) ? value : 100;
            var enabled = obj.Properties.Find("objectEnabled") is not { } flag || flag.Bool;
            objects.Add(new ScriptMapObject(obj.Name, obj.Properties.GetText("objectName") ?? string.Empty, obj.Properties.GetText("originalOwner") ?? string.Empty,
                Fixed.FromDecimal(obj.X), Fixed.FromDecimal(obj.Y), Fixed.FromDecimal(obj.Z), Fixed.FromDecimal(obj.Angle), health, enabled));
        }

        var units = ImmutableArray.CreateBuilder<ScriptUnit>();
        var groups = ImmutableArray.CreateBuilder<ScriptGroupUnit>();
        for (var listIndex = 0; listIndex < map.PlayerScripts.Length; listIndex++)
        {
            var list = map.PlayerScripts[listIndex];
            foreach (var script in list.Scripts) units.Add(new ScriptUnit(units.Count, listIndex, -1, script, script.IsSubroutine));
            foreach (var group in list.Groups)
            {
                var indices = ImmutableArray.CreateBuilder<int>();
                var groupIndex = groups.Count;
                foreach (var script in group.Scripts)
                {
                    indices.Add(units.Count);
                    units.Add(new ScriptUnit(units.Count, listIndex, groupIndex, script, script.IsSubroutine));
                }
                groups.Add(new ScriptGroupUnit(groupIndex, listIndex, group, indices.ToImmutable()));
            }
        }
        return new ScriptProgram(players.ToImmutable(), teams.ToImmutable(), waypoints.ToImmutable(), areas.ToImmutable(), paths.ToImmutable(),
            objects.ToImmutable(), units.ToImmutable(), groups.ToImmutable(), map.PlayerScripts.Length);
    }

    /// <summary>A team event script name; the reference treats an empty name and the literal <c>&lt;none&gt;</c>/<c>None</c> entry of the World Builder as "no script" (<c>ScriptEngine::runScript</c>).</summary>
    private static string EventScript(MapProperties team, string key)
    {
        var name = team.GetText(key) ?? string.Empty;
        return name is "<none>" or "None" ? string.Empty : name;
    }

    private static ImmutableArray<string> SplitNames(string? text)
        => string.IsNullOrWhiteSpace(text) ? [] : [..text.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

    private string ComputeFingerprint()
    {
        var hash = StateHash.Seed;
        hash = StateHash.Mix(hash, (ulong)Units.Length);
        hash = StateHash.Mix(hash, (ulong)Groups.Length);
        foreach (var unit in Units)
        {
            hash = StateHash.MixText(hash, unit.Name);
            hash = StateHash.Mix(hash, (ulong)unit.ListIndex);
            hash = StateHash.Mix(hash, (ulong)(unit.Script.OrConditions.Sum(or => or.AndConditions.Length) * 131 + unit.Script.Actions.Length * 17 + unit.Script.FalseActions.Length));
            foreach (var or in unit.Script.OrConditions)
                foreach (var condition in or.AndConditions) hash = StateHash.Mix(hash, (ulong)condition.TypeId);
            foreach (var action in unit.Script.Actions) hash = StateHash.Mix(hash, (ulong)action.TypeId);
            foreach (var action in unit.Script.FalseActions) hash = StateHash.Mix(hash, (ulong)action.TypeId);
        }
        foreach (var group in Groups) hash = StateHash.MixText(hash, group.Name);
        foreach (var player in Players) hash = StateHash.MixText(hash, player.Name);
        foreach (var team in Teams) hash = StateHash.MixText(hash, team.Name);
        hash = StateHash.Mix(hash, (ulong)Objects.Length);
        return hash.ToString("X16");
    }
}
