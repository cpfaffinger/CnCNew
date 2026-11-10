using System.Collections.Immutable;
using CnCNew.Game.Analysis;
using CnCNew.Game.Shell;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// A finished skirmish of <c>n</c> players for the score screen, made without playing one (the tool that draws the screen and the tests share this file): the tallies are large
/// enough to need the widest cells, the names are of the longest sort a room allows, and the analysis is one in which the viewer's advice has its tips.
/// </summary>
internal static class ScoreScreenFixture
{
    private static readonly string[] Factions = ["FactionAmerica", "FactionChina", "FactionGLA", "FactionAmericaAirForceGeneral", "FactionChinaTankGeneral", "FactionGLAToxinGeneral", "FactionAmericaLaserGeneral", "FactionChinaNukeGeneral"];

    /// <summary>The result of a match of <paramref name="players"/> players (the first one the viewer) which the viewer <paramref name="viewerWon"/> or lost; <paramref name="withTallies"/> false leaves the counters out.</summary>
    public static MatchResult Result(int players, bool viewerWon = true, bool withAnalysis = true, bool withTallies = true, long endSeconds = 27, string mapPath = "Maps/Alpine Assault/Alpine Assault.map")
    {
        var configurations = Enumerable.Range(0, players).Select(index => new PlayerConfiguration(new PlayerId(index + 1), index == 0 ? "Commander With A Long Name" : $"Computer {index}", Factions[index % Factions.Length], index % 2, index != 0)).ToArray();
        var rows = configurations.Select((player, index) => new MatchResultRow(player, viewerWon ? index == 0 : index == 1, !(viewerWon ? index == 0 : index == 1) && index % 2 == (viewerWon ? 1 : 0),
            withTallies ? PlayerTally.Of(120 + index * 111, 99 + index * 7, 1234 + index, 15 + index, 8, 21 + index * 3, 123456 + index * 99999) : null)).ToImmutableArray();
        var colors = new[] { 0x3030FF, 0xFF3030, 0x30FF30, 0xFFFF30, 0xFF30FF, 0x30FFFF, 0xFF8030, 0xFFFFFF };
        var analysisPlayers = configurations.Select((player, index) => new AnalysisPlayer(player.Id.Value, player.Name, player.Faction ?? string.Empty, player.Team, player.IsComputer, player.IsComputer ? "builtin" : string.Empty, colors[index % colors.Length],
            rows[index].IsWinner, rows[index].IsDefeated, 0, 500 + index, 0, 0, [], [], [], [], 0, 0, 0, 0, 0, 3, [3])).ToImmutableArray();
        var winner = rows.FirstOrDefault(row => row.IsWinner)?.Player.Id.Value ?? 0;
        MatchAnalysis? analysis = withAnalysis
            ? new MatchAnalysis(mapPath, 60, "product", 0, endSeconds * 30, winner, "completed", "live", 100, 100, analysisPlayers,
                [new AnalysisSample(0, 1, 10000, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0), new AnalysisSample(Math.Max(30, endSeconds * 15), 1, 9500, 0, 0, 500, 1200, 3, 1200, 0, 0, 1, 2, 5, 0, 1, 0, 0, 0)], [], [], 0)
            : null;
        return new MatchResult(rows, endSeconds * 60) { Analysis = analysis };
    }
}
