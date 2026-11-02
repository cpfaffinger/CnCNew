using System.Collections.Immutable;
using CnCNew.Game.Analysis;
using CnCNew.Game.Replays;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The analysis of a match (docs/MATCH-ANALYSIS.md) as a screen: after a match from the score screen, and from the replay list. It lies on the original's
/// <c>ScoreScreen.wnd</c> - its backdrop, its header line (the page's title) and its buttons: the save-replay button is "Previous page", the buddy button "Next page", OK
/// leads back - and draws the charts of the report (<see cref="AnalysisCharts"/>) inside the backdrop itself, because the original has no layout for them. Six pages: economy,
/// army, spending and losses, composition and exchange, the map, activity. Left and Right turn the pages, Escape leads back.
/// <para>
/// From the replay list the analysis is computed first: the replay is played again without a picture (<see cref="ReplayAnalysis"/>), <see cref="TicksPerUpdate"/> ticks a
/// frame, while the header shows how far it has come. A replay that leaves its recorded match is refused with the reason.
/// </para>
/// </summary>
public sealed class MatchAnalysisScreen : ShellScreen
{
    /// <summary>The pages in their order.</summary>
    public static readonly ImmutableArray<string> PageTitles = ["Economy", "Army", "Spending and losses", "Composition and exchange", "Map", "Activity"];

    /// <summary>How many ticks of a replay the screen plays again per frame while it computes the analysis.</summary>
    public static int TicksPerUpdate { get; set; } = 600;

    private const string HeaderWindow = "ChallengeWinLossText";
    private static readonly WndColor Ink = new(230, 230, 230, 255);
    private static readonly WndColor Muted = new(160, 160, 160, 255);
    private static readonly WndColor Grid = new(80, 80, 80, 255);
    private static readonly WndColor Plate = new(0, 0, 0, 150);
    private static readonly WndColor Shadow = new(0, 0, 0, 255);
    private const string FontName = "Arial";

    private readonly ReplayAnalysis? _job;
    private MatchAnalysis? _analysis;
    private int _page;
    private int _eventScroll;

    /// <summary>The screen of an analysis that is complete (after a live match).</summary>
    public MatchAnalysisScreen(GameShell shell, MatchAnalysis analysis) : this(shell, analysis, null) { }

    /// <summary>The screen of a replay whose analysis is computed first.</summary>
    public MatchAnalysisScreen(GameShell shell, ReplayAnalysis job) : this(shell, null, job) { }

    private MatchAnalysisScreen(GameShell shell, MatchAnalysis? analysis, ReplayAnalysis? job) : base(shell, ShellLayoutNames.ScoreScreen)
    {
        _analysis = analysis;
        _job = job;
        Tree.Register("ScoreScreenSystem", OnSystem);
        Tree.Register("ScoreScreenInput", OnInput);
        string[] keep = ["ParentScoreScreen", "MainBackdrop", HeaderWindow, "ButtonOk", "ButtonBuddy", "ButtonSaveReplay"];
        var kept = keep.Select(name => W(name)).ToHashSet();
        var needed = new HashSet<UiWindow>();
        foreach (var window in kept)
            for (var at = window; at is not null; at = at.Parent) needed.Add(at);
        foreach (var window in Tree.AllWindows)
            if (!needed.Contains(window) && window.Parent is { } parent && needed.Contains(parent)) window.SetHidden(true);
        W("ButtonSaveReplay").SetText("Previous page");
        W("ButtonBuddy").SetText("Next page");
        // The layout hides the header line (ScoreScreen.wnd shows it only in the Generals' Challenge); the analysis writes its page, map and winner there.
        W(HeaderWindow).SetHidden(false);
        Refresh();
    }

    /// <summary>The analysis, once it is complete.</summary>
    public MatchAnalysis? Analysis => _analysis;

    /// <summary>The page shown (0-based).</summary>
    public int Page => _page;

    /// <summary>The text of the header line: what a person reads first on every page.</summary>
    public string Header => TextOfHeader();

    public bool IsComputing => _analysis is null;

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        if (_analysis is not null || _job is null) return;
        try
        {
            if (_job.Advance(TicksPerUpdate)) _analysis = _job.Result;
        }
        catch (ReplayRefusedException exception)
        {
            Shell.CloseTop();
            Shell.ShowError(ReplayText.CannotAnalyseTitle, exception.Message);
            return;
        }
        Refresh();
    }

    /// <summary>Turns to page <paramref name="page"/> (wrapping around).</summary>
    public void ShowPage(int page)
    {
        _page = ((page % PageTitles.Length) + PageTitles.Length) % PageTitles.Length;
        _eventScroll = 0;
        Refresh();
    }

    private string TextOfHeader()
    {
        if (_analysis is not { } analysis)
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Analysing the replay: {(int)((_job?.Progress ?? 0) * 100)} %");
        var winner = analysis.PlayerOf(analysis.Winner);
        return $"Analysis - page {_page + 1} of {PageTitles.Length}: {PageTitles[_page]}\n{ReplayMenuScreen.MapName(Shell, analysis.MapPath)}, {AnalysisCharts.Time(analysis.DurationSeconds)}, "
            + (winner is null ? "no winner" : "won by " + AnalysisCharts.LabelOf(winner));
    }

    private void Refresh()
    {
        W(HeaderWindow).SetText(TextOfHeader());
        W("ButtonSaveReplay").SetEnabled(_analysis is not null);
        W("ButtonBuddy").SetEnabled(_analysis is not null);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "ScoreScreen.wnd:ButtonOk":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case "ScoreScreen.wnd:ButtonSaveReplay":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                ShowPage(_page - 1);
                break;
            case "ScoreScreen.wnd:ButtonBuddy":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                ShowPage(_page + 1);
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.Key) return;
        switch ((UiKey)e.Value)
        {
            case UiKey.Escape: Shell.CloseTop(); break;
            case UiKey.Left when _analysis is not null: ShowPage(_page - 1); break;
            case UiKey.Right when _analysis is not null: ShowPage(_page + 1); break;
            case UiKey.Down when _analysis is not null && _page == 5: _eventScroll++; break;
            case UiKey.Up when _analysis is not null && _page == 5: _eventScroll = Math.Max(0, _eventScroll - 1); break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------ drawing

    public override UiDrawList BuildDrawList()
    {
        var list = base.BuildDrawList();
        if (_analysis is not { } analysis) return list;
        var area = Area();
        var commands = list.Commands.ToBuilder();
        new Painter(commands, area, Tree.Services.TextMeasure, Shell.Skin, Tree.Viewport, Tree.Options.TextScale).Page(analysis, _page, _eventScroll);
        return list with { Commands = commands.ToImmutable() };
    }

    /// <summary>The part of the backdrop below the header line and above the buttons: where the pages are drawn.</summary>
    private UiRect Area()
    {
        var backdrop = W("MainBackdrop").Rect;
        var header = W(HeaderWindow).Rect;
        var top = header.Bottom + backdrop.Height / 60;
        var inset = backdrop.Width / 40;
        return new UiRect(backdrop.X + inset, top, backdrop.Width - 2 * inset, backdrop.Bottom - inset - top);
    }

    /// <summary>
    /// Draws one page into the area, in screen pixels scaled from the layout's 800 x 600. The original skin draws with the colours and fonts the screen always had; the
    /// modern skin with its palette and type scale (docs/UI-MODERN.md sections 4 and 5).
    /// </summary>
    private sealed class Painter(ImmutableArray<UiDrawCommand>.Builder commands, UiRect area, IUiTextMeasure measure, CnCNew.Ui.Skins.UiSkin skin, UiViewport viewport, decimal textScale)
    {
        private readonly int _unit = Math.Max(1, area.Width / 700);
        private int S(int value) => value * area.Width / 700;
        private UiFont Small => skin.IsModern ? skin.Style.Font(CnCNew.Ui.Skins.UiTypeStep.Caption, viewport, textScale) : new(FontName, Math.Max(8, S(10)), false);
        private UiFont Normal => skin.IsModern ? skin.Style.Font(CnCNew.Ui.Skins.UiTypeStep.Body, viewport, textScale) : new(FontName, Math.Max(9, S(12)), false);
        private UiFont Bold => skin.IsModern ? skin.Style.Font(CnCNew.Ui.Skins.UiTypeStep.Control, viewport, textScale) with { Bold = true } : new(FontName, Math.Max(9, S(12)), true);
        private WndColor Ink => skin.IsModern ? skin.Style.Palette.Text : MatchAnalysisScreen.Ink;
        private WndColor Muted => skin.IsModern ? skin.Style.Palette.Muted : MatchAnalysisScreen.Muted;
        private WndColor Grid => skin.IsModern ? skin.Style.Palette.Border : MatchAnalysisScreen.Grid;
        private WndColor Plate => skin.IsModern ? skin.Style.Palette.Panel : MatchAnalysisScreen.Plate;
        private WndColor Shadow => skin.IsModern ? skin.Style.Palette.Shadow : MatchAnalysisScreen.Shadow;
        private WndColor Field => skin.IsModern ? skin.Style.Palette.Canvas : new WndColor(25, 25, 25, 255);
        private bool Modern => skin.IsModern;

        private static WndColor Color(int rgb, int alpha = 255) => new((byte)AnalysisPalette.Red(rgb), (byte)AnalysisPalette.Green(rgb), (byte)AnalysisPalette.Blue(rgb), (byte)alpha);

        private void Fill(UiRect rect, WndColor color) => commands.Add(new UiFillRect(rect, color));
        private void Line(long x0, long y0, long x1, long y1, WndColor color, int width = 1) => commands.Add(new UiLineDraw(x0, y0, x1, y1, Math.Max(1, width * _unit), color));
        private void Text(string text, UiFont font, WndColor color, int x, int y) => commands.Add(new UiTextDraw(text, null, font, color, Shadow, x, y, UiTextAlignment.Left, area));
        private void Right(string text, UiFont font, WndColor color, int right, int y) => Text(text, font, color, right - measure.MeasureWidth(text, font), y);
        private void Centered(string text, UiFont font, WndColor color, int center, int y) => Text(text, font, color, center - measure.MeasureWidth(text, font) / 2, y);

        public void Page(MatchAnalysis analysis, int page, int scroll)
        {
            if (skin.IsModern)
            {
                ModernPage(analysis, page, scroll);
                return;
            }
            Fill(area, Plate);
            var timelines = AnalysisCharts.Timelines(analysis);
            TimelineChart Chart(string key) => timelines.First(chart => chart.Key == key);
            var half = (area.Height - S(24)) / 2;
            var upper = area with { Height = half };
            var lower = new UiRect(area.X, area.Y + half, area.Width, half);
            switch (page)
            {
                case 0:
                    Timeline(Chart("money"), upper);
                    Timeline(Chart("income"), lower);
                    break;
                case 1:
                    Timeline(Chart("army"), upper);
                    Timeline(Chart("regions"), lower);
                    break;
                case 2:
                    Timeline(Chart("spent"), upper);
                    Timeline(Chart("lost"), lower);
                    break;
                case 3:
                    Compositions(analysis, upper);
                    Exchange(analysis, lower);
                    break;
                case 4:
                    Maps(analysis, area);
                    break;
                default:
                    Timeline(Chart("commands"), upper);
                    Events(analysis, lower, scroll);
                    break;
            }
            if (page != 4) Legend(analysis, new UiRect(area.X, area.Bottom - S(22), area.Width, S(22)));
        }

        /// <summary>
        /// A page of the modern skin: the six pages as a row of tabs, the charts on cards of their own, the legend once under them, the lines in colours that read on the dark card
        /// (docs/UI-MODERN.md section 16).
        /// </summary>
        private void ModernPage(MatchAnalysis analysis, int page, int scroll)
        {
            var palette = skin.Style.Palette;
            var tabHeight = measure.LineHeight(Small) + S(10);
            var width = area.Width / PageTitles.Length;
            for (var index = 0; index < PageTitles.Length; index++)
            {
                var tab = new UiRect(area.X + index * width, area.Y, width, tabHeight);
                var active = index == page;
                if (active) Fill(tab, palette.Selected);
                Fill(new UiRect(tab.X, tab.Bottom - Math.Max(1, S(2)), tab.Width, Math.Max(1, S(2))), active ? palette.Accent : palette.Border);
                Centered(PageTitles[index].Split(' ')[0], Small, active ? palette.Text : palette.Muted, tab.X + width / 2, tab.Y + S(4));
            }
            var legendHeight = page == 4 ? 0 : measure.LineHeight(Small) + S(8);
            var gap = S(8);
            var content = UiRect.FromEdges(area.X, area.Y + tabHeight + gap, area.Right, area.Bottom - legendHeight);
            var half = (content.Height - gap) / 2;
            var upper = content with { Height = half };
            var lower = UiRect.FromEdges(content.X, content.Y + half + gap, content.Right, content.Bottom);
            var timelines = AnalysisCharts.Timelines(analysis);
            TimelineChart Chart(string key) => timelines.First(chart => chart.Key == key);
            switch (page)
            {
                case 0:
                    ModernTimeline(Chart("money"), upper);
                    ModernTimeline(Chart("income"), lower);
                    break;
                case 1:
                    ModernTimeline(Chart("army"), upper);
                    ModernTimeline(Chart("regions"), lower);
                    break;
                case 2:
                    ModernTimeline(Chart("spent"), upper);
                    ModernTimeline(Chart("lost"), lower);
                    break;
                case 3:
                    Card(upper);
                    Compositions(analysis, upper.Inset(S(4)));
                    Card(lower);
                    Exchange(analysis, lower.Inset(S(4)));
                    break;
                case 4:
                    Card(content);
                    Maps(analysis, content.Inset(S(8)));
                    break;
                default:
                    ModernTimeline(Chart("commands"), upper);
                    Card(lower);
                    Events(analysis, lower.Inset(S(4)), scroll);
                    break;
            }
            if (page != 4) ModernLegend(analysis, new UiRect(area.X, content.Bottom + S(6), area.Width, measure.LineHeight(Small)));
        }

        private void Card(UiRect rect)
        {
            Fill(rect, CnCNew.Ui.Skins.ModernLayout.Well);
            commands.Add(new UiBorderRect(rect, skin.Style.Palette.Border));
        }

        private WndColor Line(int rgb) => CnCNew.Ui.Skins.ModernLayout.Readable(Color(rgb));

        private void ModernLegend(MatchAnalysis analysis, UiRect row)
        {
            var x = row.X;
            var height = measure.LineHeight(Small);
            foreach (var player in analysis.Players)
            {
                var label = AnalysisCharts.LabelOf(player);
                Fill(new UiRect(x, row.Y + height / 2 - S(1), S(14), Math.Max(2, S(3))), Line(player.ColorRgb));
                Text(label, Small, Ink, x + S(18), row.Y);
                x += S(18) + measure.MeasureWidth(label, Small) + S(18);
                if (x > row.Right - S(60)) break;
            }
        }

        private void ModernTimeline(TimelineChart chart, UiRect box)
        {
            Card(box);
            var palette = skin.Style.Palette;
            var labelHeight = measure.LineHeight(Small);
            var left = box.X + S(60);
            var right = box.Right - S(16);
            var top = box.Y + S(6) + measure.LineHeight(Bold) + labelHeight / 2 + S(2);
            var bottom = box.Bottom - labelHeight - S(8);
            var width = right - left;
            var height = Math.Max(1, bottom - top);
            Text(chart.Title, Bold, Ink, box.X + S(10), box.Y + S(5));
            foreach (var step in chart.Ticks)
            {
                var y = bottom - step * height / chart.MaxY;
                Line(left, y, right, y, step == 0 ? palette.Muted : palette.Border);
                Right(AnalysisCharts.Number(step), Small, palette.Muted, left - S(6), (int)y - labelHeight / 2);
            }
            foreach (var second in MatchReport.TimeSteps(chart.MaxX))
            {
                var x = left + (int)(second * width / chart.MaxX);
                Line(x, bottom, x, bottom + S(3), palette.Muted);
                Centered(AnalysisCharts.Time(second), Small, palette.Muted, x, bottom + S(5));
            }
            foreach (var series in chart.Series)
            {
                var color = Line(series.ColorRgb);
                for (var index = 1; index < series.Points.Length; index++)
                {
                    var (x0, y0) = series.Points[index - 1];
                    var (x1, y1) = series.Points[index];
                    Line(left + Math.Min(x0, chart.MaxX) * width / chart.MaxX, bottom - y0 * height / chart.MaxY,
                        left + Math.Min(x1, chart.MaxX) * width / chart.MaxX, bottom - y1 * height / chart.MaxY, color, 2);
                }
                if (series.Points.IsDefaultOrEmpty) continue;
                // Where a line ends: its last value at a glance.
                var (lastX, lastY) = series.Points[^1];
                var dot = Math.Max(3, S(3));
                Fill(new UiRect((int)(left + Math.Min(lastX, chart.MaxX) * width / chart.MaxX) - dot, (int)(bottom - lastY * height / chart.MaxY) - dot, 2 * dot, 2 * dot), color);
            }
        }

        private void Legend(MatchAnalysis analysis, UiRect row)
        {
            var x = row.X + S(8);
            foreach (var player in analysis.Players)
            {
                var label = AnalysisCharts.LabelOf(player);
                Fill(new UiRect(x, row.Y + S(6), S(10), S(10)), Color(player.ColorRgb));
                Text(label, Small, Ink, x + S(14), row.Y + S(4));
                x += S(14) + measure.MeasureWidth(label, Small) + S(14);
                if (x > row.Right - S(60)) break;
            }
        }

        private void Timeline(TimelineChart chart, UiRect box)
        {
            var left = box.X + S(64);
            // The plot begins below the title line, so that its top axis label (drawn half a line above the top line) does not run into the title.
            var top = box.Y + S(4) + measure.LineHeight(Bold) + S(6);
            var width = box.Right - S(12) - left;
            var height = box.Bottom - S(22) - top;
            Text(chart.Title, Bold, Ink, box.X + S(8), box.Y + S(2));
            foreach (var step in chart.Ticks)
            {
                var y = top + height - step * height / chart.MaxY;
                Line(left, y, left + width, y, Grid);
                Right(AnalysisCharts.Number(step), Small, Muted, left - S(6), (int)y - S(6));
            }
            foreach (var second in MatchReport.TimeSteps(chart.MaxX))
                Centered(AnalysisCharts.Time(second), Small, Muted, left + (int)(second * width / chart.MaxX), top + height + S(4));
            foreach (var series in chart.Series)
            {
                var color = Color(series.ColorRgb);
                for (var index = 1; index < series.Points.Length; index++)
                {
                    var (x0, y0) = series.Points[index - 1];
                    var (x1, y1) = series.Points[index];
                    Line(left + Math.Min(x0, chart.MaxX) * width / chart.MaxX, top + height - y0 * height / chart.MaxY,
                        left + Math.Min(x1, chart.MaxX) * width / chart.MaxX, top + height - y1 * height / chart.MaxY, color, 2);
                }
            }
        }

        private void Compositions(MatchAnalysis analysis, UiRect box)
        {
            Text("Army composition: infantry, vehicles, aircraft", Bold, Ink, box.X + S(8), box.Y + S(2));
            var charts = AnalysisCharts.Compositions(analysis);
            if (charts.IsEmpty) return;
            var rowHeight = Math.Max(S(10), (box.Height - S(22)) / charts.Length);
            var left = box.X + S(150);
            var width = box.Right - S(12) - left;
            var maxX = Math.Max(1, analysis.EndFrame / CnCNew.Data.OriginalFrame.PerSecond);
            for (var index = 0; index < charts.Length; index++)
            {
                var chart = charts[index];
                var top = box.Y + S(20) + index * rowHeight;
                Text(chart.Label, Small, Modern ? Line(chart.ColorRgb) : Color(chart.ColorRgb), box.X + S(8), top + rowHeight / 2 - S(6));
                var bar = Math.Max(1, width / Math.Max(1, chart.Bars.Length));
                foreach (var item in chart.Bars)
                {
                    var x = left + (int)(Math.Min(item.Second, maxX) * width / maxX);
                    var bottom = top + rowHeight - S(2);
                    foreach (var (value, shade) in new[] { (item.Infantry, 0), (item.Vehicles, 1), (item.Aircraft, 2) })
                    {
                        var size = (int)(value * (rowHeight - S(4)) / chart.MaxValue);
                        if (size <= 0) continue;
                        Fill(new UiRect(x, bottom - size, bar, size), Color(AnalysisPalette.Shade(chart.ColorRgb, shade)));
                        bottom -= size;
                    }
                }
            }
        }

        private void Exchange(MatchAnalysis analysis, UiRect box)
        {
            Text("Loss exchange (list price)", Bold, Ink, box.X + S(8), box.Y + S(2));
            string[] headers = ["Units lost", "Value", "Buildings lost", "Value", "Kills", "Value"];
            var columns = new[] { 330, 400, 490, 560, 620, 690 };
            var y = box.Y + S(22);
            for (var index = 0; index < headers.Length; index++) Right(headers[index], Small, Muted, box.X + S(columns[index]), y);
            var rows = AnalysisCharts.Exchange(analysis);
            var height = Math.Max(S(12), Math.Min(S(18), (box.Height - S(62)) / Math.Max(1, rows.Length)));
            foreach (var row in rows)
            {
                y += height;
                Fill(new UiRect(box.X + S(8), y + S(3), S(8), S(8)), Color(row.ColorRgb));
                Text(row.Label, Small, Ink, box.X + S(20), y);
                string[] cells = [row.UnitsLost.ToString(System.Globalization.CultureInfo.InvariantCulture), AnalysisCharts.Number(row.UnitsLostValue),
                    row.StructuresLost.ToString(System.Globalization.CultureInfo.InvariantCulture), AnalysisCharts.Number(row.StructuresLostValue),
                    row.Kills.ToString(System.Globalization.CultureInfo.InvariantCulture), AnalysisCharts.Number(row.KillsValue)];
                for (var index = 0; index < cells.Length; index++) Right(cells[index], Small, Ink, box.X + S(columns[index]), y);
            }
        }

        private void Maps(MatchAnalysis analysis, UiRect box)
        {
            var size = MatchAnalysis.HeatGridSize;
            var side = Math.Min((box.Width - S(48)) / 2, box.Height - S(48)) / size * size;
            var cell = side / size;
            var top = box.Y + S(22);
            var lefts = new[] { box.X + box.Width / 4 - side / 2, box.X + box.Width * 3 / 4 - side / 2 };
            Text("Movement: whose army stood where", Bold, Ink, lefts[0], box.Y + S(2));
            Text("Fights: where units and buildings died", Bold, Ink, lefts[1], box.Y + S(2));
            foreach (var left in lefts) Fill(new UiRect(left, top, side, side), Field);
            var movement = AnalysisCharts.MovementMap(analysis);
            var fights = AnalysisCharts.FightsMap(analysis);
            for (var index = 0; index < size * size; index++)
            {
                var column = index % size;
                var row = size - 1 - index / size;
                var (player, intensity) = movement[index];
                if (intensity > 0) Fill(new UiRect(lefts[0] + column * cell, top + row * cell, cell, cell), Color(analysis.PlayerOf(player)?.ColorRgb ?? 0x808080, intensity));
                if (fights[index] > 0) Fill(new UiRect(lefts[1] + column * cell, top + row * cell, cell, cell), Color(0xD03020, fights[index]));
            }
            foreach (var item in analysis.Events.Where(item => item.Kind == "superweapon" && item.X >= 0))
            {
                var x = lefts[1] + (long)item.X * side / Math.Max(1, analysis.MapWidth);
                var y = top + side - (long)item.Y * side / Math.Max(1, analysis.MapHeight);
                var color = Color(analysis.PlayerOf(item.Player)?.ColorRgb ?? 0x808080);
                var r = S(5);
                Line(x - r, y, x + r, y, color, 2);
                Line(x, y - r, x, y + r, color, 2);
            }
            Text("North is up; a sample every 5 s of game time. A cross marks a superweapon strike.", Small, Muted, box.X + S(8), top + side + S(6));
        }

        private void Events(MatchAnalysis analysis, UiRect box, int scroll)
        {
            var listed = analysis.Events.Where(MatchReport.Listed).ToArray();
            Text($"Events ({listed.Length}; Up and Down scroll)", Bold, Ink, box.X + S(8), box.Y + S(2));
            var line = Math.Max(S(12), S(15));
            var rows = Math.Max(1, (box.Height - S(44)) / line);
            var first = Math.Clamp(scroll, 0, Math.Max(0, listed.Length - rows));
            var y = box.Y + S(20);
            foreach (var item in listed.Skip(first).Take(rows))
            {
                var player = analysis.PlayerOf(item.Player);
                Text(AnalysisCharts.Time(item.Second), Small, Muted, box.X + S(8), y);
                if (player is not null) Fill(new UiRect(box.X + S(56), y + S(3), S(8), S(8)), Color(player.ColorRgb));
                Text(item.Kind, Small, Ink, box.X + S(70), y);
                Text(item.Name, Small, Ink, box.X + S(170), y);
                y += line;
            }
        }
    }
}
