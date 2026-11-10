using CnCNew.Game.Analysis;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ScoreScreen.wnd</c>: fills the 8 result rows of <c>GadgetParent</c> from a <see cref="MatchResult"/>. A row whose
/// player has no <see cref="PlayerTally"/> (the host never fed a <see cref="MatchStatistics"/> tracker while the match
/// ran) shows <c>-</c> in every numeric cell rather than a fabricated 0. Multiplayer-only controls
/// (<c>ButtonBuddy</c>, <c>ButtonSaveReplay</c>, the chat entry/list) are disabled/hidden; observers are not modeled by
/// <c>LiveMatch</c> yet, so <c>StaticTextObserverN</c> stays hidden for every row (documented request to the simulation
/// layer in <c>docs/GAME-SHELL.md</c>).
/// <para>
/// <c>ButtonSaveReplay</c> - the original saved the replay of the match there, which this game does by itself - says "Analysis" and opens the analysis of the match
/// (docs/MATCH-ANALYSIS.md): the one the host made while the match ran, or, after a playback, the analysis of the replay that was played. Without either it stays disabled.
/// </para>
/// </summary>
public sealed class ScoreScreenScreen : ShellScreen
{
    private readonly MatchResult _result;

    /// <summary>The faction of the first player of this machine; none for a watcher.</summary>
    public string? ViewerFaction => _result.Rows.FirstOrDefault(row => !row.Player.IsComputer)?.Player.Faction;

    public ScoreScreenScreen(GameShell shell, MatchResult result) : base(shell, ShellLayoutNames.ScoreScreen, shell.Layout(ShellLayoutNames.ScoreScreen, $"ScoreScreen.skirmish.{result.Rows.Length}", (layout, modern) => ScoreScreenLayout.Prepare(layout, modern, result.Rows.Length)))
    {
        _result = result;
        W("ListboxWarschoolAdvice").ListBox!.IsReadOnly = true;
        Tree.Register("ScoreScreenSystem", OnSystem);
        Tree.Register("ScoreScreenInput", OnInput);

        W("ButtonBuddy").SetEnabled(false);
        // A skirmish has no buddies (ScoreScreen.cpp hides the button); the modern skin leaves out what cannot be pressed.
        if (shell.Skin.IsModern) W("ButtonBuddy").SetHidden(true);
        W("ButtonSaveReplay").SetText(CnCNew.Game.Replays.ReplayText.AnalysisButton);
        W("ButtonSaveReplay").SetEnabled(result.Analysis is not null || shell.PlayedReplay is not null);
        W("TextEntryChat").SetHidden(true);
        W("ButtonEmote").SetHidden(true);
        W("ListboxChatWindowScoreScreen").SetHidden(true);

        for (var row = 0; row < 8; row++)
        {
            var hasPlayer = row < result.Rows.Length;
            SetRowHidden(row, !hasPlayer);
            if (!hasPlayer) continue;
            var entry = result.Rows[row];
            W($"StaticTextPlayer{row}").SetText(NameCell(entry, result.Analysis?.PlayerOf(entry.Player.Id.Value), row));
            if (shell.Skin.IsModern && result.Analysis?.PlayerOf(entry.Player.Id.Value) is { } colored)
                W($"StaticTextPlayer{row}").SetTextColor(CnCNew.Ui.Skins.ModernLayout.Readable(new CnCNew.Ui.Wnd.WndColor((byte)AnalysisPalette.Red(colored.ColorRgb), (byte)AnalysisPalette.Green(colored.ColorRgb), (byte)AnalysisPalette.Blue(colored.ColorRgb), 255)));
            var tally = entry.Tally;
            W($"StaticTextUnitsBuilt{row}").SetText(tally is null ? "-" : tally.UnitsBuilt.ToString());
            W($"StaticTextUnitsLost{row}").SetText(tally is null ? "-" : tally.UnitsLost.ToString());
            W($"StaticTextUnitsDestroyed{row}").SetText(tally is null ? "-" : tally.UnitsDestroyed.ToString());
            W($"StaticTextBuildingsBuilt{row}").SetText(tally is null ? "-" : tally.BuildingsBuilt.ToString());
            W($"StaticTextBuildingsLost{row}").SetText(tally is null ? "-" : tally.BuildingsLost.ToString());
            W($"StaticTextBuildingsDestroyed{row}").SetText(tally is null ? "-" : tally.BuildingsDestroyed.ToString());
            W($"StaticTextResources{row}").SetText(tally is null ? "-" : tally.Resources.ToString());
            W($"StaticTextObserver{row}").SetHidden(true);
            W($"GameWindowWinner{row}").SetHidden(!entry.IsWinner);
        }
        if (shell.Skin.IsModern) ShowHeadline();
        if (result.Analysis is { } analysis) ShowTacticalAnalysis(analysis);
        // ScoreScreenInit (skirmish): the rows appear one after the other and the numbers count up. Started last, because COUNTUP reads the numbers set above.
        Transitions?.SetGroup("ScoreScreenShow");
    }

    /// <summary>
    /// A player's cell: the name and the faction on one line. In the modern skin a line that is wider than the cell loses the end of the name with an ellipsis (and, when the name would fall below eight letters, the faction's word General and then the faction), so
    /// that the cell stays one line high at every text size instead of wrapping into the next row.
    /// </summary>
    private string NameCell(MatchResultRow entry, AnalysisPlayer? analysed, int row)
    {
        if (!Shell.Skin.IsModern) return analysed is not null ? AnalysisCharts.LabelOf(analysed) : entry.Player.Name;
        var name = analysed is not null && analysed.Name.Length > 0 ? analysed.Name : entry.Player.Name.Length > 0 ? entry.Player.Name : entry.Player.IsComputer ? "Computer" : $"Player {entry.Player.Id.Value}";
        var faction = AnalysisCharts.FactionShort(analysed?.Faction ?? entry.Player.Faction ?? string.Empty);
        var cell = W($"StaticTextPlayer{row}");
        var measure = Tree.Services.TextMeasure;
        var room = cell.Rect.Width - cell.Transform.X.Length(10) - measure.ExtraOverlap(cell.Font);
        // At a text size above 100 % the label steps its font down to what the cell holds (UiTextFit), so the text is cut only for what the base size does not hold.
        var font = cell.Font with { Size = Math.Min(cell.Font.Size, cell.FontBaseSize) };
        bool Fits(string text) => measure.MeasureWidth(text, font) <= room;
        // The faction as it is, then without its "General", then no faction at all: the name keeps at least eight letters while the faction does not take them.
        var suffixes = new List<string> { faction.Length > 0 ? $" ({faction})" : string.Empty };
        if (faction.EndsWith("General", StringComparison.Ordinal) && faction.Length > "General".Length) suffixes.Add($" ({faction[..^"General".Length]})");
        foreach (var suffix in suffixes)
        {
            if (Fits(name + suffix)) return name + suffix;
            var shown = name;
            while (shown.Length > 8 && !Fits(shown.TrimEnd() + "..." + suffix)) shown = shown[..^1];
            if (Fits(shown.TrimEnd() + "..." + suffix)) return shown.TrimEnd() + "..." + suffix;
        }
        return FitWhole(name, Fits);
    }

    private static string FitWhole(string text, Func<string, bool> fits)
    {
        var shown = text;
        while (shown.Length > 1 && !fits(shown.TrimEnd() + "...")) shown = shown[..^1];
        return shown.TrimEnd() + "...";
    }

    /// <summary>The words of the headline: what the viewer's match came to, and the line under it.</summary>
    public const string Victory = "Victory", Defeat = "Defeat", NoWinner = "No winner", MatchOver = "Match over";

    /// <summary>The headline of the modern skin (<see cref="Victory"/>, <see cref="Defeat"/>, <see cref="NoWinner"/>; <see cref="MatchOver"/> for somebody who only watched) and the line under it (the map, the time and the winner).</summary>
    private void ShowHeadline()
    {
        var viewer = _result.Rows.FirstOrDefault(row => !row.Player.IsComputer);
        var winner = _result.Rows.FirstOrDefault(row => row.IsWinner);
        var (word, color) = viewer is null ? (MatchOver, CnCNew.Ui.Skins.ModernLayout.Palette.Text)
            : viewer.IsWinner ? (Victory, CnCNew.Ui.Skins.ModernLayout.Palette.Good)
            : viewer.IsDefeated || winner is not null ? (Defeat, CnCNew.Ui.Skins.ModernLayout.Palette.Bad)
            : (NoWinner, CnCNew.Ui.Skins.ModernLayout.Palette.Text);
        W("Headline").SetText(word);
        W("Headline").SetTextColor(color);
        var seconds = _result.Analysis?.DurationSeconds ?? Shell.Data.Rate.Time.ToWholeSeconds(_result.EndTick);
        var parts = new List<string>();
        if (_result.Analysis is { } analysis) parts.Add(ReplayMenuScreen.MapName(Shell, analysis.MapPath));
        parts.Add(AnalysisCharts.Time(seconds));
        parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{_result.Rows.Length} players"));
        if (winner is not null && winner != viewer) parts.Add($"won by {winner.Player.Name}");
        else if (winner is null && viewer is not null) parts.Add("nobody won");
        W("MatchLine").SetText(string.Join(" - ", parts));
    }

    /// <summary>The panel "Tactical Analysis" (<c>ListboxWarschoolAdvice</c>, <see cref="TacticalAdvice"/>); the list scrolls (<see cref="ScoreScreenLayout"/>) and lies under the panel's title.</summary>
    private void ShowTacticalAnalysis(MatchAnalysis analysis)
    {
        var list = W("ListboxWarschoolAdvice");
        var measure = Tree.Services.TextMeasure;
        var width = UiGadgetGeometry.ListContent(list).Width - 2 * list.Transform.X.Length(UiGadgetGeometry.TextInset);
        foreach (var advice in TacticalAdvice.For(analysis, null, Text))
            foreach (var line in CnCNew.Ui.Rendering.UiSentenceLayout.Layout(advice, list.Font, measure, width, false).Lines)
                list.AddListRow(new UiListRow(line.Text));
    }

    private static readonly string[] RowFieldPrefixes =
    [
        "StaticTextPlayer", "StaticTextUnitsBuilt", "StaticTextUnitsLost", "StaticTextUnitsDestroyed",
        "StaticTextBuildingsBuilt", "StaticTextBuildingsLost", "StaticTextBuildingsDestroyed", "StaticTextResources",
        "StaticTextObserver", "GameWindowWinner",
    ];

    private void SetRowHidden(int row, bool hidden)
    {
        foreach (var prefix in RowFieldPrefixes) W($"{prefix}{row}").SetHidden(hidden);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "ScoreScreen.wnd:ButtonSaveReplay":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                if (_result.Analysis is { } analysis) Shell.OpenAnalysis(analysis);
                else if (Shell.PlayedReplay is { } replay) Shell.OpenReplayAnalysis(replay);
                break;
            case "ScoreScreen.wnd:ButtonOk" or "ScoreScreen.wnd:ButtonContinue":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.ReturnToMainMenuFromResults();
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e) { }
}
