using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ScoreScreen.wnd</c>: fills the 8 result rows of <c>GadgetParent</c> from a <see cref="MatchResult"/>. A row whose
/// player has no <see cref="PlayerTally"/> (the host never fed a <see cref="MatchStatistics"/> tracker while the match
/// ran) shows <c>-</c> in every numeric cell rather than a fabricated 0. Multiplayer-only controls
/// (<c>ButtonBuddy</c>, <c>ButtonSaveReplay</c>, the chat entry/list) are disabled/hidden; observers are not modeled by
/// <c>LiveMatch</c> yet, so <c>StaticTextObserverN</c> stays hidden for every row (documented request to the simulation
/// layer in <c>docs/GAME-SHELL.md</c>).
/// </summary>
public sealed class ScoreScreenScreen : ShellScreen
{
    public ScoreScreenScreen(GameShell shell, MatchResult result) : base(shell, ShellLayoutNames.ScoreScreen)
    {
        Tree.Register("ScoreScreenSystem", OnSystem);
        Tree.Register("ScoreScreenInput", OnInput);

        W("ButtonBuddy").SetEnabled(false);
        W("ButtonSaveReplay").SetEnabled(false);
        W("TextEntryChat").SetHidden(true);
        W("ButtonEmote").SetHidden(true);
        W("ListboxChatWindowScoreScreen").SetHidden(true);

        for (var row = 0; row < 8; row++)
        {
            var hasPlayer = row < result.Rows.Length;
            SetRowHidden(row, !hasPlayer);
            if (!hasPlayer) continue;
            var entry = result.Rows[row];
            W($"StaticTextPlayer{row}").SetText(entry.Player.Name);
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
        // ScoreScreenInit (skirmish): the rows appear one after the other and the numbers count up. Started last, because COUNTUP reads the numbers set above.
        Transitions?.SetGroup("ScoreScreenShow");
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
            case "ScoreScreen.wnd:ButtonOk" or "ScoreScreen.wnd:ButtonContinue":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.ReturnToMainMenuFromResults();
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e) { }
}
