using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.World;
using CnCNew.Formats;

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
            foreach (var skipped in map.SkippedChunks)
            {
                if (skipped.Path.StartsWith("ObjectsList/Object[", StringComparison.Ordinal)) droppedObjects++;
                var current = skippedChunks.GetValueOrDefault(skipped.Path);
                skippedChunks[skipped.Path] = (current.Count + 1, current.Bytes + skipped.Bytes);
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
    Console.WriteLine("skipped chunks:");
    foreach (var (name, value) in skippedChunks)
        Console.WriteLine($"  {name}: {N(value.Count)} occurrences, {N(value.Bytes)} bytes");
    foreach (var failure in failures) Console.Error.WriteLine("FAILED " + failure);
    return failures.Count == 0 ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}
