using System.Collections.Immutable;

namespace CnCNew.Game.Analysis;

/// <summary>
/// What the "Tactical Analysis" panel of the score screen says (<c>ScoreScreen.wnd:ListboxWarschoolAdvice</c>). The original fills it from <c>AcademyStats</c>, counters the simulation
/// keeps for the local player in a skirmish, with one tip of the game's <c>ACADEMY:</c> texts drawn at random (<c>AcademyStats::calculateAcademyAdvice</c>). Here the same questions are asked of the
/// analysis of the match, which sees what they need without new state in the simulation: the same match gives the same tips, in the order of the original's tiers, and the panel has
/// room for two. A question the analysis cannot answer (how a selection was dragged, whether a radar was bought) is not asked. A line of what happened comes first.
/// </summary>
public static class TacticalAdvice
{
    /// <summary>How many tips the panel shows.</summary>
    public const int MaxTips = 2;

    /// <summary>The lines for <paramref name="viewer"/> (null: the first player who is not a computer); <paramref name="text"/> resolves a label of the game's string table.</summary>
    public static ImmutableArray<string> For(MatchAnalysis analysis, int? viewer, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(text);
        var player = viewer is { } id ? analysis.PlayerOf(id) : analysis.Players.FirstOrDefault(item => !item.IsComputer);
        var lines = ImmutableArray.CreateBuilder<string>();
        if (player is null) return lines.ToImmutable();
        var samples = analysis.SamplesOf(player.Player).ToArray();
        var peak = samples.Length == 0 ? null : samples.MaxBy(sample => sample.ArmyValue);
        lines.Add(FormattableString.Invariant($"{AnalysisCharts.Time(analysis.DurationSeconds)}: you collected {AnalysisCharts.Number(player.Collected)}, spent {AnalysisCharts.Number(player.Spent)} and destroyed {AnalysisCharts.Number(player.KillsValue)} for {AnalysisCharts.Number(player.UnitsLostValue + player.StructuresLostValue)} of your own.")
            + (peak is { ArmyValue: > 0 } ? FormattableString.Invariant($" Your army was largest at {AnalysisCharts.Time(peak.Second)}: {AnalysisCharts.Number(peak.ArmyValue)}.") : string.Empty));
        foreach (var label in Tips(analysis, player, samples).Take(MaxTips)) lines.Add(text(label));
        return lines.ToImmutable();
    }

    /// <summary>The labels of every tip that applies, most useful first (<c>evaluateTier1Advice</c> and on).</summary>
    private static IEnumerable<string> Tips(MatchAnalysis analysis, AnalysisPlayer player, AnalysisSample[] samples)
    {
        if (player.Collected == 0) yield return "ACADEMY:NoIncome";
        if (samples.Length > 0 && samples.Max(sample => sample.Workers) < 2) yield return "ACADEMY:BuildMorePeons";
        // Without power for more than ten minutes (m_powerOutMaxFrames > 600 seconds).
        if (samples.Count(sample => sample.PowerUsed > sample.PowerProduced) * MatchAnalysis.SampleSeconds > 600) yield return "ACADEMY:RanOutOfPower";
        if (player.Upgrades == 0) yield return "ACADEMY:ResearchUpgrades";
        if (player.Sciences == 0) yield return "ACADEMY:SpendGeneralsPoints";
        if (player.SpecialPowers == 0 && player.Superweapons == 0) yield return "ACADEMY:TryUsingSuperweapons";
        if (!analysis.Events.Any(item => item.Kind == "captured" && item.Player == player.Player)) yield return "ACADEMY:TryCapturingStructures";
    }
}
