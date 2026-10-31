using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Shell;

/// <summary>One line of a mission's score screen: a label and what was counted under it.</summary>
public sealed record MissionScoreRow(string Label, PlayerTally Tally);

/// <summary>
/// The score of a mission (<c>grabSinglePlayerInfo</c>): the player's own line under <c>GUI:Player</c>, then the sum of every other player that is listed (a script takes a side out with
/// <c>PLAYER_EXCLUDE_FROM_SCORE_SCREEN</c>), one line per base side, allies before enemies. The order is the original's: USA, China, GLA among the allies, then among the enemies.
/// </summary>
public static class MissionScore
{
    private static readonly ImmutableArray<(string Side, string Prefix)> Sides = [("USA", "America"), ("China", "China"), ("GLA", "GLA")];

    public static ImmutableArray<MissionScoreRow> Rows(LiveMission mission, GameData data, MatchStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(statistics);
        var excluded = mission.Director.ScoreScreenExcluded;
        var players = mission.Session.Program.Players.Where(player => !player.Id.IsSystem).ToArray();
        return Rows(mission.Human, players.Select(player => (player.Id, player.Name, BaseSide(data, player.Faction))).ToArray(), mission.World, statistics, excluded);
    }

    /// <summary>The rows from the players with their base sides; a side that is none of the three has no line, as in the original.</summary>
    public static ImmutableArray<MissionScoreRow> Rows(PlayerId local, IReadOnlyList<(PlayerId Id, string Name, string BaseSide)> players, SimulationWorld world, MatchStatistics statistics, IReadOnlyCollection<string> excluded)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(excluded);
        var rows = ImmutableArray.CreateBuilder<MissionScoreRow>();
        rows.Add(new MissionScoreRow("GUI:Player", statistics.TryGet(local) ?? new PlayerTally()));
        foreach (var friends in new[] { true, false })
            foreach (var (side, _) in Sides)
            {
                PlayerTally? total = null;
                foreach (var player in players)
                {
                    if (player.Id == local || player.BaseSide != side || excluded.Contains(player.Name)) continue;
                    var relation = world.RelationshipOf(local, player.Id);
                    if (relation != (friends ? Relationship.Allies : Relationship.Enemies)) continue;
                    total ??= new PlayerTally();
                    Add(total, statistics.TryGet(player.Id));
                }
                if (total is not null) rows.Add(new MissionScoreRow($"GUI:{side}{(friends ? "Allies" : "Enemies")}", total));
            }
        return rows.ToImmutable();
    }

    private static void Add(PlayerTally total, PlayerTally? tally)
    {
        if (tally is null) return;
        total.UnitsBuilt += tally.UnitsBuilt;
        total.UnitsLost += tally.UnitsLost;
        total.UnitsDestroyed += tally.UnitsDestroyed;
        total.BuildingsBuilt += tally.BuildingsBuilt;
        total.BuildingsLost += tally.BuildingsLost;
        total.BuildingsDestroyed += tally.BuildingsDestroyed;
        total.Resources += tally.Resources;
    }

    /// <summary><c>getBaseSide</c>: the data's <c>BaseSide</c> is not typed, and every playable <c>Side</c> begins with its base side, America standing for USA.</summary>
    private static string BaseSide(GameData data, string faction)
    {
        var side = data.Factions.FirstOrDefault(entry => entry.Faction.Name == faction)?.Faction.Side ?? string.Empty;
        foreach (var (name, prefix) in Sides)
            if (side.StartsWith(prefix, StringComparison.Ordinal)) return name;
        return string.Empty;
    }
}
