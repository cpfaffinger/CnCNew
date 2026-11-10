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

    public ScoreScreenScreen(GameShell shell, MatchResult result) : base(shell, ShellLayoutNames.ScoreScreen, shell.Layout(ShellLayoutNames.ScoreScreen, "ScoreScreen.skirmish", ScoreScreenLayout.Prepare))
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
            W($"StaticTextPlayer{row}").SetText(result.Analysis?.PlayerOf(entry.Player.Id.Value) is { } named ? AnalysisCharts.LabelOf(named) : entry.Player.Name);
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
        if (result.Analysis is { } analysis) ShowTacticalAnalysis(analysis);
        // ScoreScreenInit (skirmish): the rows appear one after the other and the numbers count up. Started last, because COUNTUP reads the numbers set above.
        Transitions?.SetGroup("ScoreScreenShow");
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
