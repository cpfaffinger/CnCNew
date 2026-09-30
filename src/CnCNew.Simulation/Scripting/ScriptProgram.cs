using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Scripting;

/// <summary>A map side as the script engine sees it: the player behind a <c>Side</c> parameter (<c>SidesList</c>, <c>playerName</c> key).</summary>
/// <param name="SideIndex">Index into <see cref="MapData.Sides"/>; side 0 is the unnamed neutral side (<see cref="PlayerId.System"/>), every other side is player <c>SideIndex</c>.</param>
public sealed record ScriptPlayer(int SideIndex, PlayerId Id, string Name, string DisplayName, string Faction, bool IsHuman, ImmutableArray<string> Allies, ImmutableArray<string> Enemies);

/// <summary>A <c>TeamPrototype</c> of the map's <c>TeamsList</c> (<c>teamName</c>, <c>teamOwner</c>, <c>teamIsSingleton</c>, <c>teamMaxInstances</c>).</summary>
public sealed record ScriptTeamPrototype(string Name, string Owner, bool IsSingleton, int MaxInstances);

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
        ImmutableArray<ScriptUnit> units, ImmutableArray<ScriptGroupUnit> groups, int listCount)
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
        Fingerprint = ComputeFingerprint();
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
                SplitNames(properties.GetText("playerAllies")), SplitNames(properties.GetText("playerEnemies"))));
        }

        var teams = ImmutableArray.CreateBuilder<ScriptTeamPrototype>();
        foreach (var team in map.Teams)
        {
            var name = team.GetText("teamName");
            if (string.IsNullOrEmpty(name)) continue;
            teams.Add(new ScriptTeamPrototype(name, team.GetText("teamOwner") ?? string.Empty, team.GetBool("teamIsSingleton"),
                team.TryGetInt("teamMaxInstances", out var max) ? max : 0));
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
