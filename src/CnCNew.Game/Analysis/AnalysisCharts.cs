using System.Collections.Immutable;

namespace CnCNew.Game.Analysis;

/// <summary>One player's line of a chart: points (second of game time, value), in time order.</summary>
public sealed record ChartSeries(int Player, string Label, int ColorRgb, ImmutableArray<(long X, long Y)> Points);

/// <summary>
/// A timeline of the report: one line per player over the seconds of game time. <paramref name="MaxY"/> is the top of the value axis, rounded up to a "nice" number so that
/// the axis lines fall on round values (<see cref="Ticks"/>). Both the HTML report and the screen in the client draw from these numbers, so they show the same chart.
/// </summary>
public sealed record TimelineChart(string Key, string Title, string Unit, long MaxX, long MaxY, ImmutableArray<ChartSeries> Series)
{
    /// <summary>The values the horizontal axis lines lie at, from 0 to <see cref="MaxY"/>.</summary>
    public ImmutableArray<long> Ticks => AnalysisCharts.AxisSteps(MaxY);
}

/// <summary>A player's army by kind of unit at one moment: values at list price.</summary>
public sealed record CompositionBar(long Second, long Infantry, long Vehicles, long Aircraft);

/// <summary>One player's army composition over the match: a bar per sample.</summary>
public sealed record CompositionChart(int Player, string Label, int ColorRgb, long MaxValue, ImmutableArray<CompositionBar> Bars);

/// <summary>A row of the loss exchange: what a player lost and what it destroyed of the others, at list price.</summary>
public sealed record ExchangeRow(int Player, string Label, int ColorRgb, int UnitsLost, long UnitsLostValue, int StructuresLost, long StructuresLostValue, int Kills, long KillsValue)
{
    public long LostValue => UnitsLostValue + StructuresLostValue;
}

/// <summary>
/// The charts of the report, computed from a <see cref="MatchAnalysis"/> alone (docs/MATCH-ANALYSIS.md): timelines (money, income per minute, spent, army value, regions held,
/// units lost, commands per minute), the army composition of every player, the loss exchange and the heat maps. Integer arithmetic only, players in the analysis' order.
/// </summary>
public static class AnalysisCharts
{
    /// <summary>The timelines of the report in their order.</summary>
    public static ImmutableArray<TimelineChart> Timelines(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return
        [
            Timeline(analysis, "money", "Money", "credits", sample => sample.Money),
            Timeline(analysis, "income", "Income per minute", "credits", sample => sample.IncomePerMinute),
            Timeline(analysis, "spent", "Spent (list price, cumulative)", "credits", sample => sample.Spent),
            Timeline(analysis, "army", "Army value", "credits", sample => sample.ArmyValue),
            Timeline(analysis, "regions", $"Regions held (of {MatchAnalysis.RegionsPerSide * MatchAnalysis.RegionsPerSide})", "regions", sample => sample.Regions),
            Timeline(analysis, "lost", "Units and buildings lost (cumulative)", "objects", sample => sample.UnitsLost + sample.StructuresLost),
            CommandsPerMinute(analysis),
        ];
    }

    public static string LabelOf(AnalysisPlayer player)
    {
        var who = player.Name.Length > 0 ? player.Name : player.IsComputer ? "Computer" : $"Player {player.Player}";
        return $"{who} ({FactionShort(player.Faction)})";
    }

    /// <summary>A faction's name without the prefix the definitions give it (<c>FactionAmericaAirForceGeneral</c> is "AmericaAirForceGeneral").</summary>
    public static string FactionShort(string faction) => faction.StartsWith("Faction", StringComparison.Ordinal) ? faction["Faction".Length..] : faction;

    private static TimelineChart Timeline(MatchAnalysis analysis, string key, string title, string unit, Func<AnalysisSample, long> value)
    {
        var series = ImmutableArray.CreateBuilder<ChartSeries>();
        long maxY = 0, maxX = Math.Max(1, analysis.EndFrame / CnCNew.Data.OriginalFrame.PerSecond);
        foreach (var player in analysis.Players)
        {
            var points = analysis.SamplesOf(player.Player).Select(sample => (sample.Second, value(sample))).ToImmutableArray();
            foreach (var (_, y) in points) maxY = Math.Max(maxY, y);
            series.Add(new ChartSeries(player.Player, LabelOf(player), player.ColorRgb, points));
        }
        return new TimelineChart(key, title, unit, maxX, NiceCeiling(maxY), series.ToImmutable());
    }

    private static TimelineChart CommandsPerMinute(MatchAnalysis analysis)
    {
        var series = ImmutableArray.CreateBuilder<ChartSeries>();
        long maxY = 0, maxX = Math.Max(1, analysis.EndFrame / CnCNew.Data.OriginalFrame.PerSecond);
        foreach (var player in analysis.Players)
        {
            // A point in the middle of every minute; the last minute may be shorter than 60 s and is shown where it lies.
            var points = player.CommandsPerMinute.Select((count, minute) => ((long)Math.Min(minute * 60 + 30, maxX), (long)count)).ToImmutableArray();
            foreach (var (_, y) in points) maxY = Math.Max(maxY, y);
            series.Add(new ChartSeries(player.Player, LabelOf(player), player.ColorRgb, points));
        }
        return new TimelineChart("commands", "Commands per minute", "commands", maxX, NiceCeiling(maxY), series.ToImmutable());
    }

    public static ImmutableArray<CompositionChart> Compositions(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var maximum = Math.Max(1, analysis.Samples.Select(sample => sample.InfantryValue + sample.VehicleValue + sample.AircraftValue).DefaultIfEmpty(0).Max());
        var top = NiceCeiling(maximum);
        return [..analysis.Players.Select(player => new CompositionChart(player.Player, LabelOf(player), player.ColorRgb, top,
            [..analysis.SamplesOf(player.Player).Select(sample => new CompositionBar(sample.Second, sample.InfantryValue, sample.VehicleValue, sample.AircraftValue))]))];
    }

    public static ImmutableArray<ExchangeRow> Exchange(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return [..analysis.Players.Select(player => new ExchangeRow(player.Player, LabelOf(player), player.ColorRgb, player.UnitsLostCount, player.UnitsLostValue,
            player.StructuresLostCount, player.StructuresLostValue, player.Kills, player.KillsValue))];
    }

    /// <summary>
    /// The movement heat map of all players at once: every cell shows the player whose army stood there most often, its intensity the share of the most visited cell
    /// (0..255); 0 where no army stood.
    /// </summary>
    public static ImmutableArray<(int Player, int Intensity)> MovementMap(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var size = MatchAnalysis.HeatGridSize * MatchAnalysis.HeatGridSize;
        var maps = analysis.Heat.Where(heat => heat.Kind == "movement").ToArray();
        var peak = Math.Max(1, maps.Select(heat => heat.Maximum).DefaultIfEmpty(0).Max());
        var cells = ImmutableArray.CreateBuilder<(int, int)>(size);
        for (var cell = 0; cell < size; cell++)
        {
            int best = 0, value = 0;
            foreach (var heat in maps)
                if (heat.Cells[cell] > value) { best = heat.Player; value = heat.Cells[cell]; }
            cells.Add((best, value == 0 ? 0 : Intensity(value, peak)));
        }
        return cells.MoveToImmutable();
    }

    /// <summary>The fights heat map: the intensity of the deaths in every cell (0..255).</summary>
    public static ImmutableArray<int> FightsMap(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var fights = analysis.HeatOf("fights", 0);
        if (fights is null) return [.. new int[MatchAnalysis.HeatGridSize * MatchAnalysis.HeatGridSize]];
        var peak = Math.Max(1, fights.Maximum);
        return [..fights.Cells.Select(value => value == 0 ? 0 : Intensity(value, peak))];
    }

    /// <summary>A share of the peak as 64..255, by the square root so that a few visits still show beside many (integer square root, no floating point).</summary>
    private static int Intensity(int value, int peak)
    {
        var share = (long)value * 1_000_000 / peak;
        return 64 + (int)(IntegerSqrt(share) * 191 / 1000);
    }

    private static long IntegerSqrt(long value)
    {
        if (value <= 0) return 0;
        long root = 0, bit = 1L << 62;
        while (bit > value) bit >>= 2;
        while (bit != 0)
        {
            if (value >= root + bit) { value -= root + bit; root = (root >> 1) + bit; }
            else root >>= 1;
            bit >>= 2;
        }
        return root;
    }

    /// <summary>The smallest of 1, 2, 2.5, 5 times a power of ten that is at least <paramref name="value"/> (at least 1).</summary>
    public static long NiceCeiling(long value)
    {
        if (value <= 1) return 1;
        long power = 1;
        while (power * 10 < value) power *= 10;
        // In tenths of the power: 10, 20, 25, 50, 100.
        foreach (var tenths in new long[] { 10, 20, 25, 50, 100 })
            if (power * tenths / 10 >= value && power * tenths % 10 == 0) return power * tenths / 10;
        return power * 10;
    }

    /// <summary>Four or five axis values from 0 to <paramref name="max"/>.</summary>
    public static ImmutableArray<long> AxisSteps(long max)
    {
        var steps = max % 4 == 0 ? 4 : max % 5 == 0 ? 5 : 2;
        return [..Enumerable.Range(0, steps + 1).Select(index => max * index / steps)];
    }

    /// <summary>A number with thousands grouped by a thin gap the way the report writes it (invariant: "12 500").</summary>
    public static string Number(long value)
    {
        var text = Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var grouped = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 && (text.Length - index) % 3 == 0) grouped.Append(' ');
            grouped.Append(text[index]);
        }
        return (value < 0 ? "-" : string.Empty) + grouped;
    }

    /// <summary>Seconds of game time as m:ss or h:mm:ss.</summary>
    public static string Time(long seconds)
        => seconds >= 3600
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
}
