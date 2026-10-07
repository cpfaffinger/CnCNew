using System.Collections.Immutable;
using CnCNew.Data.World;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// Builds small synthetic <see cref="MapData"/> instances (no original map content): the maps of the simulation tests and of the rate-equivalence scenes,
/// which tests and <c>SimTool rate-compare</c> share.
/// </summary>
public static class FlatMap
{
    /// <summary>
    /// A <paramref name="cellsX"/> x <paramref name="cellsY"/> playable map with the given border. <paramref name="height"/> gives the raw height byte per
    /// vertex (x, y in whole-field coordinates, absent: flat at 0); cliff cells are given in height-cell coordinates.
    /// </summary>
    public static MapData Build(int cellsX, int cellsY, int border, Func<int, int, int>? height = null, IEnumerable<(int X, int Y)>? cliffs = null,
        IEnumerable<MapPolygonTrigger>? triggers = null, IEnumerable<(string Name, decimal X, decimal Y, decimal Z)>? waypoints = null)
    {
        var width = cellsX + 2 * border;
        var fieldHeight = cellsY + 2 * border;
        var heights = new byte[width * fieldHeight];
        for (var y = 0; y < fieldHeight; y++)
            for (var x = 0; x < width; x++) heights[y * width + x] = (byte)(height?.Invoke(x, y) ?? 0);
        var stride = (width + 7) / 8;
        var flags = new byte[stride * fieldHeight];
        foreach (var (cx, cy) in cliffs ?? [])
            flags[cy * stride + (cx >> 3)] |= (byte)(1 << (cx & 7));
        var blend = new MapBlendTiles(8, [], [], [], [], [..flags], stride, 0, 0, [], [], [], []);
        var objects = ImmutableArray.CreateBuilder<MapObject>();
        var id = 1;
        foreach (var (name, x, y, z) in waypoints ?? [])
        {
            var properties = new MapProperties([
                new MapProperty("waypointID", MapPropertyType.Int, id++, 0m, string.Empty),
                new MapProperty("waypointName", MapPropertyType.Ascii, 0, 0m, name)]);
            objects.Add(new MapObject("*Waypoints/Waypoint", x, y, z, 0m, 0, properties));
        }
        return new MapData("synthetic", new MapHeightField(width, fieldHeight, border, [new MapBoundary(cellsX, cellsY)], [..heights]), blend,
            MapProperties.Empty, [], [], objects.ToImmutable(), [..triggers ?? []], [], [], []);
    }
}
