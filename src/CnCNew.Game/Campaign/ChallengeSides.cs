using CnCNew.Data.World;
using CnCNew.Simulation.Scripting;

namespace CnCNew.Game.Campaign;

/// <summary>
/// Which side of a Generals' Challenge map the human plays. A map that marks its own human side (<c>playerIsHuman</c>, e.g. <c>ThePlayer</c>) decides
/// itself and the answer is empty; the maps that mark none (<c>GC_LaserGeneral</c>) model the chosen general as an ordinary named general side
/// (<c>PlyrAmericaLaserGeneral</c>): the first side that is not neutral and not civilian and whose faction is a <c>*General</c> faction. The persona's
/// own <c>PlayerTemplate</c> (<c>ChallengeMode.ini</c>) is not substituted for the map's faction: the human plays the faction the map gives that side.
/// </summary>
public static class ChallengeSides
{
    public static string HumanSideFor(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var players = ScriptProgram.Build(map).Players;
        if (players.Any(player => player.IsHuman)) return string.Empty;
        var general = players.FirstOrDefault(player => !player.Id.IsSystem && player.Name.Length > 0
            && !player.Faction.Contains("Civilian", StringComparison.Ordinal) && player.Faction.EndsWith("General", StringComparison.Ordinal));
        return general?.Name ?? string.Empty;
    }
}
