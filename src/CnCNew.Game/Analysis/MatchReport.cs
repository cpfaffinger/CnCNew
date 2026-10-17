using System.Globalization;
using System.Net;
using System.Text;

namespace CnCNew.Game.Analysis;

/// <summary>
/// The graphical report of a match as one HTML page (docs/MATCH-ANALYSIS.md): the players, the timelines, the army composition, the loss exchange, the heat maps of movement
/// and fights and the list of the match's events - inline SVG and CSS, no script and nothing loaded from elsewhere. It is a function of the analysis: integer coordinates,
/// invariant numbers, "\n" line ends, so the same analysis gives the same bytes.
/// </summary>
public static class MatchReport
{
    private const int ChartWidth = 720;
    private const int ChartHeight = 200;
    private const int Left = 64;
    private const int Top = 12;
    private const int MapPixels = 320;

    private static string Html(string text) => WebUtility.HtmlEncode(text);
    private static string Hex(int rgb) => "#" + rgb.ToString("X6", CultureInfo.InvariantCulture);
    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string ToHtml(MatchAnalysis analysis, string title)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Html(title)).Append("</title>\n<style>\n");
        html.Append(":root{--bg:#f7f7f4;--fg:#1d1d1b;--muted:#6b6b66;--grid:#d9d9d2;--panel:#ffffff}\n");
        html.Append("@media (prefers-color-scheme: dark){:root{--bg:#161614;--fg:#ecece6;--muted:#9a9a92;--grid:#3a3a35;--panel:#20201d}}\n");
        html.Append("body{background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif;margin:0 auto;max-width:960px;padding:16px}\n");
        html.Append("h1{font-size:22px;margin:8px 0}h2{font-size:17px;margin:28px 0 8px}p.meta{color:var(--muted);margin:0 0 12px}\n");
        html.Append("table{border-collapse:collapse;width:100%;background:var(--panel)}th,td{padding:4px 8px;border-bottom:1px solid var(--grid);text-align:right;white-space:nowrap}\n");
        html.Append("th:first-child,td:first-child{text-align:left}.wrap{overflow-x:auto}svg{display:block;max-width:100%;height:auto;background:var(--panel)}\n");
        html.Append("svg text{fill:var(--muted);font:11px system-ui,sans-serif}.axis{stroke:var(--grid)}.key{display:inline-block;width:10px;height:10px;margin-right:6px;border-radius:2px}\n");
        html.Append(".maps{display:flex;flex-wrap:wrap;gap:16px}.maps figure{margin:0}figcaption{color:var(--muted);font-size:12px;margin-top:4px}\n");
        html.Append("</style>\n</head>\n<body>\n");

        var winner = analysis.PlayerOf(analysis.Winner);
        html.Append("<h1>").Append(Html(title)).Append("</h1>\n");
        html.Append("<p class=\"meta\">").Append(Html(MapName(analysis.MapPath))).Append(" · ").Append(AnalysisCharts.Time(analysis.DurationSeconds)).Append(" of game time · ")
            .Append(winner is null ? "no winner" : "won by " + Html(AnalysisCharts.LabelOf(winner))).Append(" · ended: ").Append(Html(analysis.Ending.Length == 0 ? "-" : analysis.Ending))
            .Append(" · ").Append(I(analysis.TickRate)).Append(" Hz · rules: ").Append(Html(analysis.Rules)).Append("</p>\n");

        Players(html, analysis);
        html.Append("<h2>Course of the match</h2>\n");
        foreach (var chart in AnalysisCharts.Timelines(analysis)) Timeline(html, chart);
        Compositions(html, analysis);
        Exchange(html, analysis);
        Maps(html, analysis);
        Units(html, analysis);
        Events(html, analysis);
        html.Append("<p class=\"meta\">Format ").Append(MatchAnalysis.Format).Append(' ').Append(I(MatchAnalysis.SchemaVersion)).Append(", computed from ").Append(Html(analysis.Source))
            .Append(". Values are list prices (BuildCost); a sample every ").Append(I(MatchAnalysis.SampleSeconds)).Append(" s of game time.</p>\n");
        html.Append("</body>\n</html>\n");
        return html.ToString();
    }

    private static string MapName(string mapPath) => Path.GetFileNameWithoutExtension(mapPath.Replace('\\', '/'));

    private static void Players(StringBuilder html, MatchAnalysis analysis)
    {
        html.Append("<div class=\"wrap\"><table>\n<tr><th>Player</th><th>Team</th><th>Result</th><th>Collected</th><th>Spent</th><th>Units built</th><th>Lost</th><th>Kills</th><th>Commands</th></tr>\n");
        foreach (var player in analysis.Players)
        {
            var result = player.IsWinner ? "won" : player.IsDefeated ? "defeated" : "-";
            html.Append("<tr><td><span class=\"key\" style=\"background:").Append(Hex(player.ColorRgb)).Append("\"></span>").Append(Html(AnalysisCharts.LabelOf(player)))
                .Append(player.IsComputer ? " · " + Html(player.Ai) : string.Empty).Append("</td><td>").Append(player.Team == 0 ? "-" : I(player.Team)).Append("</td><td>").Append(result)
                .Append("</td><td>").Append(AnalysisCharts.Number(player.Collected)).Append("</td><td>").Append(AnalysisCharts.Number(player.Spent))
                .Append("</td><td>").Append(I(player.UnitsBuiltCount)).Append("</td><td>").Append(I(player.UnitsLostCount + player.StructuresLostCount))
                .Append("</td><td>").Append(I(player.Kills)).Append("</td><td>").Append(I(player.Commands)).Append("</td></tr>\n");
        }
        html.Append("</table></div>\n");
    }

    private static void Timeline(StringBuilder html, TimelineChart chart)
    {
        var width = Left + ChartWidth + 16;
        var height = Top + ChartHeight + 28;
        html.Append("<h3>").Append(Html(chart.Title)).Append("</h3>\n");
        html.Append("<svg viewBox=\"0 0 ").Append(I(width)).Append(' ').Append(I(height)).Append("\" width=\"").Append(I(width)).Append("\" height=\"").Append(I(height))
            .Append("\" role=\"img\" aria-label=\"").Append(Html(chart.Title)).Append("\">\n");
        foreach (var step in chart.Ticks)
        {
            var y = Top + ChartHeight - step * ChartHeight / chart.MaxY;
            html.Append("<line class=\"axis\" x1=\"").Append(I(Left)).Append("\" y1=\"").Append(I(y)).Append("\" x2=\"").Append(I(Left + ChartWidth)).Append("\" y2=\"").Append(I(y)).Append("\"/>");
            html.Append("<text x=\"").Append(I(Left - 6)).Append("\" y=\"").Append(I(y + 4)).Append("\" text-anchor=\"end\">").Append(AnalysisCharts.Number(step)).Append("</text>\n");
        }
        foreach (var second in TimeSteps(chart.MaxX))
        {
            var x = Left + second * ChartWidth / chart.MaxX;
            html.Append("<text x=\"").Append(I(x)).Append("\" y=\"").Append(I(Top + ChartHeight + 18)).Append("\" text-anchor=\"middle\">").Append(AnalysisCharts.Time(second)).Append("</text>\n");
        }
        foreach (var series in chart.Series)
        {
            if (series.Points.IsDefaultOrEmpty) continue;
            html.Append("<polyline fill=\"none\" stroke=\"").Append(Hex(series.ColorRgb)).Append("\" stroke-width=\"2\" points=\"");
            var first = true;
            foreach (var (second, value) in series.Points)
            {
                if (!first) html.Append(' ');
                first = false;
                html.Append(I(Left + Math.Min(second, chart.MaxX) * ChartWidth / chart.MaxX)).Append(',').Append(I(Top + ChartHeight - value * ChartHeight / chart.MaxY));
            }
            html.Append("\"><title>").Append(Html(series.Label)).Append("</title></polyline>\n");
        }
        html.Append("</svg>\n");
    }

    /// <summary>Marks on the time axis at whole minutes: every 1, 2, 5, 10, 15 or 30 minutes so that there are at most eight.</summary>
    internal static IEnumerable<long> TimeSteps(long maxSeconds)
    {
        var step = new long[] { 60, 120, 300, 600, 900, 1800, 3600 }.FirstOrDefault(value => maxSeconds / value <= 8, 3600);
        for (var second = 0L; second <= maxSeconds; second += step) yield return second;
    }

    private static void Compositions(StringBuilder html, MatchAnalysis analysis)
    {
        html.Append("<h2>Army composition</h2>\n<p class=\"meta\">The value of each player's army over time: infantry (dark), vehicles (middle), aircraft (light).</p>\n");
        foreach (var chart in AnalysisCharts.Compositions(analysis))
        {
            var width = Left + ChartWidth + 16;
            var height = Top + 120 + 24;
            html.Append("<h3>").Append(Html(chart.Label)).Append("</h3>\n<svg viewBox=\"0 0 ").Append(I(width)).Append(' ').Append(I(height)).Append("\" width=\"").Append(I(width))
                .Append("\" height=\"").Append(I(height)).Append("\" role=\"img\" aria-label=\"Army composition of ").Append(Html(chart.Label)).Append("\">\n");
            var maxX = Math.Max(1, analysis.EndFrame / CnCNew.Data.OriginalFrame.PerSecond);
            var bar = Math.Max(1, ChartWidth / Math.Max(1, chart.Bars.Length));
            foreach (var item in chart.Bars)
            {
                var x = Left + Math.Min(item.Second, maxX) * ChartWidth / maxX;
                var bottom = Top + 120L;
                foreach (var (value, shade) in new[] { (item.Infantry, 0), (item.Vehicles, 1), (item.Aircraft, 2) })
                {
                    if (value <= 0) continue;
                    var height2 = value * 120 / chart.MaxValue;
                    if (height2 <= 0) continue;
                    html.Append("<rect x=\"").Append(I(x)).Append("\" y=\"").Append(I(bottom - height2)).Append("\" width=\"").Append(I(bar)).Append("\" height=\"").Append(I(height2))
                        .Append("\" fill=\"").Append(Hex(AnalysisPalette.Shade(chart.ColorRgb, shade))).Append("\"/>");
                    bottom -= height2;
                }
            }
            html.Append("\n<text x=\"").Append(I(Left - 6)).Append("\" y=\"").Append(I(Top + 8)).Append("\" text-anchor=\"end\">").Append(AnalysisCharts.Number(chart.MaxValue)).Append("</text>\n");
            foreach (var second in TimeSteps(maxX))
                html.Append("<text x=\"").Append(I(Left + second * ChartWidth / maxX)).Append("\" y=\"").Append(I(Top + 120 + 18)).Append("\" text-anchor=\"middle\">").Append(AnalysisCharts.Time(second)).Append("</text>\n");
            html.Append("</svg>\n");
        }
    }

    private static void Exchange(StringBuilder html, MatchAnalysis analysis)
    {
        html.Append("<h2>Loss exchange</h2>\n<div class=\"wrap\"><table>\n<tr><th>Player</th><th>Units lost</th><th>Value</th><th>Buildings lost</th><th>Value</th><th>Kills</th><th>Value</th></tr>\n");
        foreach (var row in AnalysisCharts.Exchange(analysis))
            html.Append("<tr><td><span class=\"key\" style=\"background:").Append(Hex(row.ColorRgb)).Append("\"></span>").Append(Html(row.Label)).Append("</td><td>").Append(I(row.UnitsLost))
                .Append("</td><td>").Append(AnalysisCharts.Number(row.UnitsLostValue)).Append("</td><td>").Append(I(row.StructuresLost)).Append("</td><td>").Append(AnalysisCharts.Number(row.StructuresLostValue))
                .Append("</td><td>").Append(I(row.Kills)).Append("</td><td>").Append(AnalysisCharts.Number(row.KillsValue)).Append("</td></tr>\n");
        html.Append("</table></div>\n");
    }

    private static void Maps(StringBuilder html, MatchAnalysis analysis)
    {
        var size = MatchAnalysis.HeatGridSize;
        var cell = MapPixels / size;
        html.Append("<h2>Where the match was played</h2>\n<div class=\"maps\">\n");
        html.Append("<figure><svg viewBox=\"0 0 ").Append(I(MapPixels)).Append(' ').Append(I(MapPixels)).Append("\" width=\"").Append(I(MapPixels)).Append("\" height=\"").Append(I(MapPixels))
            .Append("\" role=\"img\" aria-label=\"Movement\">\n");
        var movement = AnalysisCharts.MovementMap(analysis);
        for (var index = 0; index < movement.Length; index++)
        {
            var (player, intensity) = movement[index];
            if (intensity == 0) continue;
            var color = analysis.PlayerOf(player)?.ColorRgb ?? 0x808080;
            Cell(html, index, cell, size, color, intensity);
        }
        html.Append("</svg><figcaption>Movement: where each player's army stood, in the colour of the player who stood there most (sampled every ")
            .Append(I(MatchAnalysis.SampleSeconds)).Append(" s; north is up).</figcaption></figure>\n");
        html.Append("<figure><svg viewBox=\"0 0 ").Append(I(MapPixels)).Append(' ').Append(I(MapPixels)).Append("\" width=\"").Append(I(MapPixels)).Append("\" height=\"").Append(I(MapPixels))
            .Append("\" role=\"img\" aria-label=\"Fights\">\n");
        var fights = AnalysisCharts.FightsMap(analysis);
        for (var index = 0; index < fights.Length; index++)
            if (fights[index] > 0) Cell(html, index, cell, size, 0xD03020, fights[index]);
        foreach (var item in analysis.Events.Where(item => item.Kind == "superweapon" && item.X >= 0))
        {
            var (x, y) = MapPoint(analysis, item.X, item.Y);
            html.Append("<circle cx=\"").Append(I(x)).Append("\" cy=\"").Append(I(y)).Append("\" r=\"6\" fill=\"none\" stroke=\"").Append(Hex(analysis.PlayerOf(item.Player)?.ColorRgb ?? 0x808080))
                .Append("\" stroke-width=\"2\"><title>").Append(Html(item.Name)).Append(" at ").Append(AnalysisCharts.Time(item.Second)).Append("</title></circle>\n");
        }
        html.Append("</svg><figcaption>Fights: where units and buildings died; rings mark superweapon strikes.</figcaption></figure>\n</div>\n");
    }

    /// <summary>A heat cell: row 0 of the grid is the map's south edge, drawn at the bottom (north up, as the minimap).</summary>
    private static void Cell(StringBuilder html, int index, int cell, int size, int color, int intensity)
    {
        var column = index % size;
        var row = index / size;
        html.Append("<rect x=\"").Append(I(column * cell)).Append("\" y=\"").Append(I((size - 1 - row) * cell)).Append("\" width=\"").Append(I(cell)).Append("\" height=\"").Append(I(cell))
            .Append("\" fill=\"").Append(Hex(color)).Append("\" fill-opacity=\"").Append(I(intensity * 100 / 255)).Append("%\"/>");
    }

    internal static (long X, long Y) MapPoint(MatchAnalysis analysis, int x, int y)
        => (Math.Clamp((long)x * MapPixels / Math.Max(1, analysis.MapWidth), 0, MapPixels), Math.Clamp(MapPixels - (long)y * MapPixels / Math.Max(1, analysis.MapHeight), 0, MapPixels));

    private static void Units(StringBuilder html, MatchAnalysis analysis)
    {
        html.Append("<h2>Units built and lost</h2>\n<div class=\"wrap\"><table>\n<tr><th>Player</th><th>Most built (count)</th><th>Most lost (count)</th></tr>\n");
        foreach (var player in analysis.Players)
            html.Append("<tr><td><span class=\"key\" style=\"background:").Append(Hex(player.ColorRgb)).Append("\"></span>").Append(Html(AnalysisCharts.LabelOf(player))).Append("</td><td>")
                .Append(Html(string.Join(", ", player.UnitsBuilt.OrderByDescending(item => item.Count).ThenBy(item => item.Template, StringComparer.Ordinal).Take(5).Select(item => $"{item.Template} {I(item.Count)}"))))
                .Append("</td><td>")
                .Append(Html(string.Join(", ", player.UnitsLost.OrderByDescending(item => item.Count).ThenBy(item => item.Template, StringComparer.Ordinal).Take(5).Select(item => $"{item.Template} {I(item.Count)}"))))
                .Append("</td></tr>\n");
        html.Append("</table></div>\n");
    }

    /// <summary>The events of the list: not every death and every building, but upgrades, sciences, powers, captures and defeats.</summary>
    internal static bool Listed(AnalysisEvent item) => item.Kind is "upgrade" or "science" or "special-power" or "superweapon" or "captured" or "defeated";

    private static void Events(StringBuilder html, MatchAnalysis analysis)
    {
        html.Append("<h2>Events</h2>\n<div class=\"wrap\"><table>\n<tr><th>Time</th><th>Player</th><th>Event</th><th>What</th></tr>\n");
        foreach (var item in analysis.Events.Where(Listed))
        {
            var player = analysis.PlayerOf(item.Player);
            html.Append("<tr><td>").Append(AnalysisCharts.Time(item.Second)).Append("</td><td>").Append(player is null ? I(item.Player) : Html(AnalysisCharts.LabelOf(player)))
                .Append("</td><td>").Append(item.Kind).Append("</td><td>").Append(Html(item.Name)).Append("</td></tr>\n");
        }
        html.Append("</table></div>\n");
    }
}

/// <summary>The shades the report draws a player's colour in.</summary>
public static class AnalysisPalette
{
    /// <summary>A colour as 0xRRGGBB (shifted by bytes: a shift by 16 reads as the fixed-point scale to the source rules).</summary>
    public static int Rgb(int r, int g, int b) => (((r << 8) | g) << 8) | b;

    public static int Red(int rgb) => (rgb >> 8 >> 8) & 0xFF;
    public static int Green(int rgb) => (rgb >> 8) & 0xFF;
    public static int Blue(int rgb) => rgb & 0xFF;

    /// <summary>The player's colour darker (0: two thirds), as it is (1) or lighter (2: halfway to white).</summary>
    public static int Shade(int rgb, int shade)
    {
        int r = Red(rgb), g = Green(rgb), b = Blue(rgb);
        (r, g, b) = shade switch
        {
            0 => (r * 2 / 3, g * 2 / 3, b * 2 / 3),
            2 => ((r + 255) / 2, (g + 255) / 2, (b + 255) / 2),
            _ => (r, g, b),
        };
        return Rgb(r, g, b);
    }
}
