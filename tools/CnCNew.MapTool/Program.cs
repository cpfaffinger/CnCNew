using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

if (args is ["info", var infoDirectory, var infoPath])
{
    try
    {
        var content = ContentFileSystem.Open(infoDirectory);
        using var stream = content.OpenRead(infoPath);
        var map = SageMapDecoder.Decode(stream);
        Console.WriteLine($"{infoPath}: container {map.Container}, {map.HeightField.Width}x{map.HeightField.Height} vertices, border {map.HeightField.Border}");
        Console.WriteLine($"  blend v{map.BlendTiles.Version}, {map.Objects.Length} objects ({map.Objects.Count(o => o.IsWaypoint)} waypoints), " +
                          $"{map.Triggers.Length} triggers ({map.Triggers.Count(t => t.IsWater)} water), {map.Sides.Length} sides, {map.Teams.Length} teams");
        var scriptCount = map.PlayerScripts.Sum(list => list.Scripts.Length + list.Groups.Sum(group => group.Scripts.Length));
        Console.WriteLine($"  scripts: {map.PlayerScripts.Length} player lists, {scriptCount} scripts");
        foreach (var skipped in map.SkippedChunks)
            Console.WriteLine($"  skipped {skipped.Path} v{skipped.Version} {skipped.Bytes} bytes");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

if (args is ["dump", var dumpDirectory, var dumpPath])
{
    var content = ContentFileSystem.Open(dumpDirectory);
    using var stream = content.OpenRead(dumpPath);
    var map = SageMapDecoder.Decode(stream);
    string Props(MapProperties properties) => string.Join(", ", properties.Items.Select(item => $"{item.Key}={(item.Type is MapPropertyType.Ascii or MapPropertyType.Unicode ? item.Text : item.Type == MapPropertyType.Real ? item.Real.ToString(CultureInfo.InvariantCulture) : item.Integer.ToString(CultureInfo.InvariantCulture))}"));
    Console.WriteLine($"world: {Props(map.WorldInfo)}");
    for (var index = 0; index < map.Sides.Length; index++) Console.WriteLine($"side {index}: {Props(map.Sides[index].Properties)}");
    foreach (var team in map.Teams) Console.WriteLine($"team: {Props(team)}");
    foreach (var trigger in map.Triggers) Console.WriteLine($"trigger {trigger.Id} '{trigger.Name}' points={trigger.Points.Length} water={trigger.IsWater}");
    foreach (var mapObject in map.Objects.Where(value => !value.IsWaypoint)) Console.WriteLine($"object {mapObject.Name} ({mapObject.X},{mapObject.Y},{mapObject.Z}) a={mapObject.Angle} {Props(mapObject.Properties)}");
    foreach (var mapObject in map.Objects.Where(value => value.IsWaypoint)) Console.WriteLine($"waypoint {mapObject.Name} ({mapObject.X},{mapObject.Y}) {Props(mapObject.Properties)}");
    string Par(MapScriptParameter p) => p.Type switch
    {
        MapScriptParameterType.Coord3D => $"{p.Type}({p.CoordX},{p.CoordY},{p.CoordZ})",
        MapScriptParameterType.Real or MapScriptParameterType.Angle or MapScriptParameterType.Percent => $"{p.Type}({p.Real})",
        _ => $"{p.Type}(i={p.Integer},s='{p.Text}')",
    };
    void Script(string indent, MapScript script)
    {
        Console.WriteLine($"{indent}script '{script.Name}' active={script.IsActive} oneShot={script.IsOneShot} sub={script.IsSubroutine} delay={script.DelayEvaluationSeconds} diff={(script.Easy ? "E" : "")}{(script.Normal ? "N" : "")}{(script.Hard ? "H" : "")}");
        foreach (var or in script.OrConditions)
        {
            Console.WriteLine($"{indent}  OR");
            foreach (var condition in or.AndConditions) Console.WriteLine($"{indent}    IF {condition.TypeName} [{string.Join("; ", condition.Parameters.Select(Par))}]");
        }
        foreach (var action in script.Actions) Console.WriteLine($"{indent}  DO {action.TypeName} [{string.Join("; ", action.Parameters.Select(Par))}]");
        foreach (var action in script.FalseActions) Console.WriteLine($"{indent}  ELSE {action.TypeName} [{string.Join("; ", action.Parameters.Select(Par))}]");
    }
    for (var index = 0; index < map.PlayerScripts.Length; index++)
    {
        Console.WriteLine($"scriptlist {index}");
        foreach (var script in map.PlayerScripts[index].Scripts) Script("  ", script);
        foreach (var group in map.PlayerScripts[index].Groups)
        {
            Console.WriteLine($"  group '{group.Name}' active={group.IsActive} sub={group.IsSubroutine}");
            foreach (var script in group.Scripts) Script("    ", script);
        }
    }
    return 0;
}

if (args is not ["audit", var contentDirectory])
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  CnCNew.MapTool audit <content-directory>");
    Console.Error.WriteLine("  CnCNew.MapTool info <content-directory> <virtual-map-path>");
    return 2;
}

try
{
    var content = ContentFileSystem.Open(contentDirectory);
    var paths = content.EnumerateFiles()
        .Where(path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.Ordinal)
        .ToArray();
    var failures = new List<string>();
    var containers = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var skippedChunks = new SortedDictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);
    var blendVersions = new SortedDictionary<int, int>();
    long vertices = 0, objects = 0, waypoints = 0, links = 0, triggers = 0, waterAreas = 0, sides = 0, teams = 0, buildEntries = 0;
    long cliffCells = 0, cells = 0, mapsWithoutStart = 0, droppedObjects = 0;
    var maxStart = 0;
    long terrainMaps = 0, terrainWaterCells = 0, terrainCliffCells = 0, terrainCells = 0, terrainSimStarts = 0;
    var terrainRejected = new List<string>();
    var terrainFingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
    var maxExtent = 0;
    long pathPairs = 0, pathFound = 0, pathPartial = 0, pathBlocked = 0, pathPoints = 0;
    long scriptLists = 0, scripts = 0, scriptGroups = 0, subroutineGroups = 0, orConditions = 0, conditions = 0, scriptActions = 0, falseActions = 0;
    var conditionTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var actionTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var parameterTypes = new SortedDictionary<MapScriptParameterType, int>();
    var pathDigest = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);

    foreach (var path in paths)
    {
        try
        {
            using var stream = content.OpenRead(path);
            var map = SageMapDecoder.Decode(stream);
            containers[map.Container] = containers.GetValueOrDefault(map.Container) + 1;
            blendVersions[map.BlendTiles.Version] = blendVersions.GetValueOrDefault(map.BlendTiles.Version) + 1;
            vertices += map.HeightField.Heights.Length;
            objects += map.Objects.Length;
            waypoints += map.Objects.Count(o => o.IsWaypoint);
            links += map.WaypointLinks.Length;
            triggers += map.Triggers.Length;
            waterAreas += map.Triggers.Count(t => t.IsWater);
            sides += map.Sides.Length;
            teams += map.Teams.Length;
            buildEntries += map.Sides.Sum(s => s.BuildList.Length);
            for (var y = 0; y < map.HeightField.Height - 1; y++)
                for (var x = 0; x < map.HeightField.Width - 1; x++)
                {
                    cells++;
                    if (map.BlendTiles.IsCliff(x, y)) cliffCells++;
                }
            var starts = map.Objects.Where(o => o.IsWaypoint && (o.Properties.GetText("waypointName") ?? "").StartsWith("Player_", StringComparison.Ordinal)
                                                              && (o.Properties.GetText("waypointName") ?? "").EndsWith("_Start", StringComparison.Ordinal)).Count();
            if (starts == 0) mapsWithoutStart++;
            maxStart = Math.Max(maxStart, starts);
            try
            {
                var terrain = TerrainMap.Build(map, path);
                terrainMaps++;
                terrainCells += (long)terrain.CellsX * terrain.CellsY;
                for (var cy = 0; cy < terrain.CellsY; cy++)
                    for (var cx = 0; cx < terrain.CellsX; cx++)
                    {
                        var flags = terrain.CellAt(cx, cy);
                        if (flags.HasFlag(CellFlags.Water)) terrainWaterCells++;
                        if (flags.HasFlag(CellFlags.Cliff)) terrainCliffCells++;
                    }
                terrainSimStarts += terrain.StartPositions.Length;
                maxExtent = Math.Max(maxExtent, Math.Max(terrain.CellsX, terrain.CellsY));
                terrainFingerprints[path] = terrain.Fingerprint;
                var second = TerrainMap.Build(map, path);
                if (second.Fingerprint != terrain.Fingerprint) terrainRejected.Add($"{path}: fingerprint differs between two builds");
                // Ground paths between every pair of start positions on the bare terrain (no structures), twice to prove determinism.
                var grid = new NavigationGrid(terrain);
                var starts2 = terrain.StartPositions;
                for (var a = 0; a < starts2.Length; a++)
                    for (var b = a + 1; b < starts2.Length; b++)
                    {
                        var first = grid.FindPath(starts2[a].X, starts2[a].Y, starts2[b].X, starts2[b].Y, LocomotorSurfaces.Ground);
                        var repeat = grid.FindPath(starts2[a].X, starts2[a].Y, starts2[b].X, starts2[b].Y, LocomotorSurfaces.Ground);
                        if (first.Status != repeat.Status || !first.Points.SequenceEqual(repeat.Points)) terrainRejected.Add($"{path}: path {a}->{b} is not repeatable");
                        pathPairs++;
                        switch (first.Status)
                        {
                            case PathStatus.Found: pathFound++; break;
                            case PathStatus.Partial: pathPartial++; break;
                            default: pathBlocked++; break;
                        }
                        pathPoints += first.Points.Length;
                        var record = $"{path}|{a}|{b}|{first.Status}|{string.Join(";", first.Points.Select(point => point.X.Raw + "," + point.Y.Raw))}|";
                        pathDigest.AppendData(System.Text.Encoding.UTF8.GetBytes(record));
                    }
            }
            catch (InvalidDataException exception)
            {
                terrainRejected.Add($"{path}: {exception.Message}");
            }
            foreach (var skipped in map.SkippedChunks)
            {
                if (skipped.Path.StartsWith("ObjectsList/Object[", StringComparison.Ordinal)) droppedObjects++;
                var current = skippedChunks.GetValueOrDefault(skipped.Path);
                skippedChunks[skipped.Path] = (current.Count + 1, current.Bytes + skipped.Bytes);
            }
            scriptLists += map.PlayerScripts.Length;
            foreach (var list in map.PlayerScripts)
            {
                CountScripts(list.Scripts);
                scriptGroups += list.Groups.Length;
                foreach (var group in list.Groups)
                {
                    if (group.IsSubroutine) subroutineGroups++;
                    CountScripts(group.Scripts);
                }
            }

            void CountScripts(System.Collections.Immutable.ImmutableArray<MapScript> list)
            {
                foreach (var script in list)
                {
                    scripts++;
                    orConditions += script.OrConditions.Length;
                    foreach (var or in script.OrConditions)
                        foreach (var condition in or.AndConditions)
                        {
                            conditions++;
                            conditionTypes[condition.TypeName] = conditionTypes.GetValueOrDefault(condition.TypeName) + 1;
                            foreach (var parameter in condition.Parameters) parameterTypes[parameter.Type] = parameterTypes.GetValueOrDefault(parameter.Type) + 1;
                        }
                    scriptActions += script.Actions.Length;
                    foreach (var action in script.Actions)
                    {
                        actionTypes[action.TypeName] = actionTypes.GetValueOrDefault(action.TypeName) + 1;
                        foreach (var parameter in action.Parameters) parameterTypes[parameter.Type] = parameterTypes.GetValueOrDefault(parameter.Type) + 1;
                    }
                    falseActions += script.FalseActions.Length;
                    foreach (var action in script.FalseActions)
                    {
                        actionTypes[action.TypeName] = actionTypes.GetValueOrDefault(action.TypeName) + 1;
                        foreach (var parameter in action.Parameters) parameterTypes[parameter.Type] = parameterTypes.GetValueOrDefault(parameter.Type) + 1;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{path}: {exception.Message}");
        }
    }

    string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    Console.WriteLine($"maps: {paths.Length}, decoded {paths.Length - failures.Count}, failed {failures.Count}");
    Console.WriteLine($"containers: {string.Join(", ", containers.Select(pair => $"{pair.Key}={pair.Value}"))}");
    Console.WriteLine($"blend tile versions: {string.Join(", ", blendVersions.Select(pair => $"v{pair.Key}={pair.Value}"))}");
    Console.WriteLine($"height vertices {N(vertices)}, cells {N(cells)}, cliff cells {N(cliffCells)}");
    Console.WriteLine($"objects {N(objects)} (dropped for z range {N(droppedObjects)}), waypoints {N(waypoints)}, waypoint links {N(links)}, max start waypoints {maxStart}, maps without start waypoint {mapsWithoutStart}");
    Console.WriteLine($"polygon triggers {N(triggers)} (water {N(waterAreas)}), sides {N(sides)}, teams {N(teams)}, build list entries {N(buildEntries)}");
    Console.WriteLine($"terrain: {terrainMaps} maps built, {N(terrainCells)} path cells ({N(terrainCliffCells)} cliff, {N(terrainWaterCells)} water), {terrainSimStarts} start waypoints, largest extent {maxExtent} cells, {terrainRejected.Count} rejected");
    var digestInput = string.Join("|", terrainFingerprints.Select(pair => pair.Key + "=" + pair.Value));
    Console.WriteLine("terrain fingerprint digest: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(digestInput))));
    Console.WriteLine($"ground paths between start positions: {pathPairs} pairs, {pathFound} complete, {pathPartial} partial, {pathBlocked} blocked, {pathPoints} waypoints, digest {Convert.ToHexString(pathDigest.GetHashAndReset())}");
    foreach (var rejected in terrainRejected) Console.Error.WriteLine("TERRAIN " + rejected);
    Console.WriteLine($"scripts: {N(scriptLists)} player lists, {N(scripts)} scripts, {N(scriptGroups)} groups ({N(subroutineGroups)} subroutine), " +
                      $"{N(orConditions)} OR-branches, {N(conditions)} conditions, {N(scriptActions)} true actions, {N(falseActions)} false actions, " +
                      $"{conditionTypes.Count} distinct condition types, {actionTypes.Count} distinct action types");
    Console.WriteLine("condition types by usage:");
    foreach (var (name, count) in conditionTypes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {name}: {N(count)}");
    Console.WriteLine("action types by usage:");
    foreach (var (name, count) in actionTypes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {name}: {N(count)}");
    Console.WriteLine("parameter types by usage:");
    foreach (var (type, count) in parameterTypes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        Console.WriteLine($"  {type}: {N(count)}");
    Console.WriteLine("skipped chunks:");
    foreach (var (name, value) in skippedChunks)
        Console.WriteLine($"  {name}: {N(value.Count)} occurrences, {N(value.Bytes)} bytes");
    foreach (var failure in failures) Console.Error.WriteLine("FAILED " + failure);
    return failures.Count == 0 && terrainRejected.Count == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}
