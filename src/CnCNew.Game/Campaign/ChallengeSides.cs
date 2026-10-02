using CnCNew.Data.World;
using CnCNew.Simulation.Scripting;

namespace CnCNew.Game.Campaign;

/// <summary>
/// Which side of a Generals' Challenge map the human plays. A map that marks its own human side (<c>playerIsHuman</c>, e.g. <c>ThePlayer</c>) decides itself and the
/// answer is empty. A map that marks none (<c>GC_LaserGeneral</c> defines only its opponents: <c>PlyrAmericaLaserGeneral</c> is the enemy whose build facilities the win
/// script asks to destroy) gets a human side of its own: <see cref="SyntheticHumanSide"/>, created by <see cref="ScriptProgram.WithHumanSide"/> with the persona's faction.
/// </summary>
public static class ChallengeSides
{
    /// <summary>The name of the human side the mission session adds to a challenge map that has none.</summary>
    public const string SyntheticHumanSide = "ThePlayer";

    public static string HumanSideFor(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return ScriptProgram.Build(map).Players.Any(player => player.IsHuman && !player.Id.IsSystem) ? string.Empty : SyntheticHumanSide;
    }
}
