using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Terrain;

namespace CnCNew.MapTool;

/// <summary>
/// <c>MapTool script-library &lt;content&gt;</c>: the inventory of the shipped script libraries (<c>Data/Scripts/SkirmishScripts.scb</c>, <c>MultiplayerScripts.scb</c>) -
/// sizes, the template players and their teams, every condition and action type with its use count and how the script engine treats it
/// (<see cref="ScriptCoverage"/>), and what the skirmish maps provide for those scripts (named waypoints, waypoint path labels and trigger areas).
/// docs/SKIRMISH-AI-ORIGINAL.md quotes this output.
/// </summary>
internal static class ScriptLibraryReport
{
    private static readonly string[] Libraries = ["Data/Scripts/SkirmishScripts.scb", "Data/Scripts/MultiplayerScripts.scb"];

    public static int Run(string contentDirectory, bool verbose)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        var referencedAreas = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var referencedWaypoints = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var referencedPaths = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in Libraries)
        {
            MapScriptLibrary library;
            using (var stream = content.OpenRead(path)) library = SageMapDecoder.DecodeScriptLibrary(stream);
            Describe(path, library, verbose, referencedAreas, referencedWaypoints, referencedPaths);
        }
        MapCensus(content, referencedAreas, referencedWaypoints, referencedPaths);
        return 0;
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static void Describe(string path, MapScriptLibrary library, bool verbose, SortedDictionary<string, int> areas, SortedDictionary<string, int> waypoints, SortedDictionary<string, int> paths)
    {
        Console.WriteLine($"== {path}: container {library.Container}, {library.Players.Length} template players, {library.ScriptLists.Length} script lists, {library.Teams.Length} team prototypes");
        foreach (var skipped in library.SkippedChunks) Console.WriteLine($"  skipped chunk {skipped.Path} v{skipped.Version} {skipped.Bytes} bytes");

        var conditionTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var actionTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var teamKeys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long scripts = 0, groups = 0, subroutineScripts = 0, conditions = 0, actions = 0, falseActions = 0, delayed = 0, oneShot = 0, inactive = 0;
        var difficulty = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var team in library.Teams)
            foreach (var item in team.Items) teamKeys[item.Key] = teamKeys.GetValueOrDefault(item.Key) + 1;

        for (var index = 0; index < library.ScriptLists.Length; index++)
        {
            var list = library.ScriptLists[index];
            var player = index < library.Players.Length ? library.Players[index] : new MapScriptLibraryPlayer("(no player entry)", MapProperties.Empty);
            var all = list.Scripts.Concat(list.Groups.SelectMany(group => group.Scripts)).ToArray();
            var teams = library.Teams.Count(team => team.GetText("teamOwner") == player.Name);
            var perDifficulty = new int[3];
            foreach (var script in all)
            {
                if (script.Easy) perDifficulty[0]++;
                if (script.Normal) perDifficulty[1]++;
                if (script.Hard) perDifficulty[2]++;
            }
            Console.WriteLine($"  list {index}: player '{player.Name}' faction '{player.Properties.GetText("playerFaction")}': {all.Length} scripts ({list.Scripts.Length} top level, {list.Groups.Length} groups), " +
                              $"{teams} teams; scripts enabled on easy {perDifficulty[0]}, normal {perDifficulty[1]}, hard {perDifficulty[2]}");
            if (verbose)
                foreach (var group in list.Groups)
                    Console.WriteLine($"    group '{group.Name}' active={group.IsActive} subroutine={group.IsSubroutine}: {group.Scripts.Length} scripts");
            groups += list.Groups.Length;
            foreach (var script in all)
            {
                scripts++;
                if (script.IsSubroutine) subroutineScripts++;
                if (script.DelayEvaluationSeconds > 0) delayed++;
                if (script.IsOneShot) oneShot++;
                if (!script.IsActive) inactive++;
                var key = (script.Easy ? "E" : "-") + (script.Normal ? "N" : "-") + (script.Hard ? "H" : "-");
                difficulty[key] = difficulty.GetValueOrDefault(key) + 1;
                foreach (var or in script.OrConditions)
                    foreach (var condition in or.AndConditions)
                    {
                        conditions++;
                        conditionTypes[condition.TypeName] = conditionTypes.GetValueOrDefault(condition.TypeName) + 1;
                        Collect(condition.Parameters, areas, waypoints, paths);
                    }
                foreach (var action in script.Actions.Concat(script.FalseActions))
                {
                    actionTypes[action.TypeName] = actionTypes.GetValueOrDefault(action.TypeName) + 1;
                    Collect(action.Parameters, areas, waypoints, paths);
                }
                actions += script.Actions.Length;
                falseActions += script.FalseActions.Length;
            }
        }

        Console.WriteLine($"  totals: {N(scripts)} scripts ({N(subroutineScripts)} subroutines, {N(oneShot)} one-shot, {N(inactive)} initially inactive, {N(delayed)} with an evaluation delay), {N(groups)} groups, " +
                          $"{N(conditions)} conditions of {conditionTypes.Count} types, {N(actions)} true actions and {N(falseActions)} false actions of {actionTypes.Count} types");
        Console.WriteLine($"  scripts by difficulty flags (E/N/H): {string.Join(", ", difficulty.Select(pair => $"{pair.Key}={N(pair.Value)}"))}");

        Console.WriteLine("  conditions by type (uses, support):");
        foreach (var (name, count) in conditionTypes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {name}: {N(count)} {ScriptCoverage.ConditionSupport(name)}");
        Console.WriteLine("  actions by type (uses, support):");
        foreach (var (name, count) in actionTypes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"    {name}: {N(count)} {ScriptCoverage.ActionSupport(name)}");

        Console.WriteLine("  coverage of the script engine:");
        foreach (var support in new[] { ScriptSupport.Executed, ScriptSupport.Forwarded, ScriptSupport.Recorded, ScriptSupport.Missing })
        {
            var types = actionTypes.Where(pair => ScriptCoverage.ActionSupport(pair.Key) == support).ToArray();
            Console.WriteLine($"    actions {support}: {types.Length} types, {N(types.Sum(pair => (long)pair.Value))} uses");
        }
        foreach (var support in new[] { ScriptSupport.Executed, ScriptSupport.Missing })
        {
            var types = conditionTypes.Where(pair => ScriptCoverage.ConditionSupport(pair.Key) == support).ToArray();
            Console.WriteLine($"    conditions {support}: {types.Length} types, {N(types.Sum(pair => (long)pair.Value))} uses");
        }
        var gaps = actionTypes.Where(pair => ScriptCoverage.ActionSupport(pair.Key) is ScriptSupport.Missing or ScriptSupport.Recorded or ScriptSupport.Forwarded)
            .OrderBy(pair => ScriptCoverage.ActionSupport(pair.Key)).ThenByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        Console.WriteLine($"  action types the engine does not execute: {gaps.Length}");
        foreach (var (name, count) in gaps) Console.WriteLine($"    {ScriptCoverage.ActionSupport(name)} {name}: {N(count)}");

        if (library.Teams.Length == 0) return;
        Console.WriteLine($"  team prototype keys ({teamKeys.Count}): {string.Join(", ", teamKeys.Select(pair => $"{pair.Key}={pair.Value}"))}");
        int Count(Func<MapProperties, bool> test) => library.Teams.Count(test);
        Console.WriteLine($"  teams: singleton {Count(team => team.GetBool("teamIsSingleton"))}, AI recruitable {Count(team => team.GetBool("teamIsAIRecruitable"))}, " +
                          $"with production condition {Count(team => !string.IsNullOrEmpty(team.GetText("teamProductionCondition")))}, executes actions on create {Count(team => team.GetBool("teamExecutesActionsOnCreate"))}, " +
                          $"auto reinforce {Count(team => team.GetBool("teamAutoReinforce"))}, with home waypoint {Count(team => !string.IsNullOrEmpty(team.GetText("teamHome")))}, " +
                          $"with a transport {Count(team => !string.IsNullOrEmpty(team.GetText("teamTransport")))}");
        var events = new[] { "teamOnCreateScript", "teamOnIdleScript", "teamEnemySightedScript", "teamAllClearScript", "teamOnDestroyedScript", "teamOnUnitDestroyedScript" };
        Console.WriteLine($"  teams with event scripts: {string.Join(", ", events.Select(key => $"{key}={Count(team => team.GetText(key) is { Length: > 0 } name && name is not ("<none>" or "None"))}"))}");
        var generic = library.Teams.Sum(team => team.Items.Count(item => item.Key.StartsWith("teamGenericScriptHook", StringComparison.Ordinal) && item.Text.Length > 0));
        Console.WriteLine($"  generic script hooks set: {generic}");
        var homes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var team in library.Teams)
            if (team.GetText("teamHome") is { Length: > 0 } home) homes[home] = homes.GetValueOrDefault(home) + 1;
        Console.WriteLine($"  team home waypoints: {string.Join(", ", homes.Select(pair => $"{pair.Key}={pair.Value}"))}");
        foreach (var home in homes) waypoints[home.Key] = waypoints.GetValueOrDefault(home.Key) + home.Value;
    }

    private static void Collect(ImmutableArray<MapScriptParameter> parameters, SortedDictionary<string, int> areas, SortedDictionary<string, int> waypoints, SortedDictionary<string, int> paths)
    {
        foreach (var parameter in parameters)
        {
            var target = parameter.Type switch
            {
                MapScriptParameterType.TriggerArea => areas,
                MapScriptParameterType.Waypoint => waypoints,
                MapScriptParameterType.WaypointPath or MapScriptParameterType.SkirmishWaypointPath => paths,
                _ => null,
            };
            if (target is not null && parameter.Text.Length > 0) target[parameter.Text] = target.GetValueOrDefault(parameter.Text) + 1;
        }
    }

    /// <summary>What the skirmish maps (two or more <c>Player_N_Start</c> waypoints) provide: own scripts, and the names the library refers to.</summary>
    private static void MapCensus(ContentFileSystem content, SortedDictionary<string, int> areas, SortedDictionary<string, int> waypoints, SortedDictionary<string, int> paths)
    {
        var maps = 0;
        var withOwnScripts = 0;
        var civilianScripts = 0;
        var areaMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var waypointMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var pathMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var perStartComplete = new SortedDictionary<string, int>(StringComparer.Ordinal);
        string[] perStart = ["InnerPerimeter", "OuterPerimeter", "CombatZone"];
        string[] perStartPaths = ["Center", "Flank", "Backdoor"];
        foreach (var path in content.EnumerateFiles().Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            MapData map;
            using (var stream = content.OpenRead(path)) map = SageMapDecoder.Decode(stream);
            int starts;
            try { starts = TerrainMap.Build(map, path).StartPositions.Length; }
            catch (InvalidDataException) { continue; }
            if (starts < 2) continue;
            maps++;
            var ownScripts = false;
            for (var side = 0; side < map.Sides.Length && side < map.PlayerScripts.Length; side++)
            {
                var has = map.PlayerScripts[side].Scripts.Length + map.PlayerScripts[side].Groups.Length > 0;
                if (!has) continue;
                if (map.Sides[side].Properties.GetText("playerFaction") == "FactionCivilian") civilianScripts++;
                else ownScripts = true;
            }
            if (ownScripts) withOwnScripts++;
            var areaNames = map.Triggers.Where(trigger => !trigger.IsWater && !trigger.IsRiver).Select(trigger => trigger.Name).ToHashSet(StringComparer.Ordinal);
            var waypointNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in map.Objects.Where(item => item.IsWaypoint))
            {
                if (item.Properties.GetText("waypointName") is { Length: > 0 } name) waypointNames.Add(name);
                for (var slot = 1; slot <= 3; slot++)
                    if (item.Properties.GetText($"waypointPathLabel{slot}") is { Length: > 0 } label) pathNames.Add(label);
            }
            foreach (var name in areas.Keys) if (areaNames.Contains(name)) areaMaps[name] = areaMaps.GetValueOrDefault(name) + 1;
            foreach (var name in waypoints.Keys) if (waypointNames.Contains(name)) waypointMaps[name] = waypointMaps.GetValueOrDefault(name) + 1;
            foreach (var name in paths.Keys) if (pathNames.Contains(name)) pathMaps[name] = pathMaps.GetValueOrDefault(name) + 1;
            // The per-start names the engine builds itself: <Area><n> for the areas, <Label><n> for the approach paths (n = start position 1..N).
            foreach (var prefix in perStart)
                if (Enumerable.Range(1, starts).All(n => areaNames.Contains(prefix + n.ToString(CultureInfo.InvariantCulture)))) perStartComplete["area " + prefix + "<n>"] = perStartComplete.GetValueOrDefault("area " + prefix + "<n>") + 1;
            foreach (var prefix in perStartPaths)
                if (Enumerable.Range(1, starts).All(n => pathNames.Contains(prefix + n.ToString(CultureInfo.InvariantCulture)))) perStartComplete["path " + prefix + "<n>"] = perStartComplete.GetValueOrDefault("path " + prefix + "<n>") + 1;
        }
        Console.WriteLine($"== skirmish maps (two or more start positions): {maps}; with scripts of a non-civilian side (the library is NOT attached there): {withOwnScripts}; civilian script lists: {civilianScripts}");
        Console.WriteLine($"  complete for every start position: {string.Join(", ", perStartComplete.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine("  trigger areas the libraries name (uses in the libraries, maps that have it):");
        foreach (var (name, uses) in areas) Console.WriteLine($"    {name}: {N(uses)} uses, {areaMaps.GetValueOrDefault(name)} maps");
        Console.WriteLine("  waypoints the libraries name:");
        foreach (var (name, uses) in waypoints) Console.WriteLine($"    {name}: {N(uses)} uses, {waypointMaps.GetValueOrDefault(name)} maps");
        Console.WriteLine("  waypoint paths the libraries name (skirmish approach paths get the enemy's start number appended):");
        foreach (var (name, uses) in paths) Console.WriteLine($"    {name}: {N(uses)} uses, {pathMaps.GetValueOrDefault(name)} maps");
    }
}
