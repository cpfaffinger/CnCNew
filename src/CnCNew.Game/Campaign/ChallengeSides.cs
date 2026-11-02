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

    /// <summary>
    /// Who plays a map that is started by its name (<c>--mission</c>), as the menus would have it: the side as <see cref="HumanSideFor"/> says, empty when the map marks its own, and
    /// the faction of the challenge. Without them <c>GC_LaserGeneral</c>, which marks nobody, would make its first player the viewer, an enemy. Empty for a map no challenge names.
    /// </summary>
    public static (string Side, string Faction) OfDirectStart(CnCNew.Game.Data.GameData data, string mapPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        const string campaigns = "Data/INI/Campaign.ini", personas = "Data/INI/ChallengeMode.ini";
        if (!data.Content.Exists(campaigns)) return (string.Empty, string.Empty);
        CampaignCatalog catalog;
        using (var stream = data.Content.OpenRead(campaigns)) catalog = CampaignCatalog.Parse(stream);
        if (data.Content.Exists(personas))
        {
            using var reader = new StreamReader(data.Content.OpenRead(personas));
            catalog = catalog.WithPersonas(reader.ReadToEnd());
        }
        var campaign = catalog.Campaigns.FirstOrDefault(item => item.IsChallenge && item.Sequence.Any(mission => mission.MapPath.Equals(mapPath, StringComparison.OrdinalIgnoreCase)));
        if (campaign is null) return (string.Empty, string.Empty);
        using var map = data.Content.OpenRead(mapPath);
        return (HumanSideFor(CnCNew.Formats.SageMapDecoder.Decode(map)), campaign.PlayerFaction);
    }
}
