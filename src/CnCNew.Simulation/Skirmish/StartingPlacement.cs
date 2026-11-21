using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.Skirmish;

/// <summary>
/// Where <c>GameLogic.cpp placeNetworkBuildingsForPlayer</c> looks for a player's starting units (#476): around the map's <c>Player_N_Rally</c> waypoint when the map has one,
/// else around the start (<c>Player_N_Start</c>) moved half the starting building's bounding sphere along -y (<c>pos.y -= getBoundingSphereRadius() / 2</c>), between 0.7 and 1.3
/// times that sphere (<c>FindPositionOptions</c> <c>minRadius</c>, <c>maxRadius</c>). Every starting unit searches around the same centre; the search itself is the world's
/// (<see cref="Commands.PlaceStartingUnitCommand"/>).
/// </summary>
public readonly record struct StartingPlacement(Fixed X, Fixed Y, Fixed MinRadius, Fixed MaxRadius)
{
    /// <summary>The placement of the units of the player at start position <paramref name="startIndex"/> (an index into <see cref="TerrainMap.StartPositions"/>).</summary>
    /// <param name="building">The starting building; without one the units search around the start itself, at no distance.</param>
    public static StartingPlacement Of(TerrainMap terrain, int startIndex, ObjectTemplate? building)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var start = terrain.StartPositions[startIndex];
        var radius = building?.BoundingSphereRadius ?? Fixed.Zero;
        var (x, y) = (start.X, start.Y - radius / Fixed.FromInt(2));
        // findNamedWaypoint compares the names as they are (AsciiString::operator==); the number is that of the start's own waypoint, "start pos waypoints are 1-based".
        if (TerrainMap.TryParseStart(start.Name, out var number))
        {
            var rallyName = "Player_" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_Rally";
            foreach (var waypoint in terrain.Waypoints)
                if (string.Equals(waypoint.Name, rallyName, StringComparison.Ordinal))
                {
                    (x, y) = (waypoint.X, waypoint.Y);
                    break;
                }
        }
        return new StartingPlacement(x, y, radius * Fixed.FromInt(7) / Fixed.FromInt(10), radius * Fixed.FromInt(13) / Fixed.FromInt(10));
    }
}
