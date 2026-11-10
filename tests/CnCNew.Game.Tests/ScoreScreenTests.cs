using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The end screen of a skirmish (issues 288 and 289): the panel "Tactical Analysis" scrolls by the wheel and by its bar, and nothing is cut off, at every window size and text
/// size, in both skins.
/// </summary>
internal static class ScoreScreenTests
{
    private static long _clock;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(ScoreScreenTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP ScoreScreenTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var root = Path.Combine(Path.GetTempPath(), "cncnew-score-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            harness.Run("The advice panel scrolls by the wheel and by its bar and its last line can be reached, in both skins at four sizes and three text sizes", () => AdviceScrolls(data, root));
            harness.Run("The modern end screen fits at four window sizes and three text sizes for two to eight players: nothing is cut off or outside its panel, the rows are equally high, the buttons stand in a fixed place", () => ModernLayoutFits(data, root));
            harness.Run("The headline says what the match came to for the viewer, and the line under it the map, the time and who won", () => HeadlineSaysIt(data, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        return harness.Failures;
    }

    internal static GameShell Scored(GameData data, ProductSurveyShell.Config config, string root, MatchResult result)
    {
        var shell = ProductSurveyShell.NewShell(data, config, root);
        shell.MatchFinished(result);
        for (var step = 0; step < 90; step++) shell.Update(1.0 / 30);
        return shell;
    }

    private static UiWindow Window(GameShell shell, string local) => shell.CurrentScreen.Tree.FindByName("ScoreScreen.wnd:" + local) ?? throw new InvalidOperationException($"no window {local}");

    private static void ModernLayoutFits(GameData data, string root)
    {
        var measured = ProductSurveyShell.Measure is not null;
        foreach (var (width, height) in ProductSurveyShell.Sizes)
            foreach (var percent in new[] { 100, 125, 150 })
                foreach (var players in new[] { 2, 3, 5, 8 })
                {
                    var config = new ProductSurveyShell.Config(UiSkin.Modern, width, height, percent);
                    var shell = Scored(data, config, root, ScoreScreenFixture.Result(players, viewerWon: players % 2 == 0));
                    var label = $"{config} with {players} players";
                    var panel = Window(shell, "MainBackdrop").Rect;
                    static bool Inside(UiRect rect, UiRect outer) => rect.X >= outer.X && rect.Y >= outer.Y && rect.Right <= outer.Right && rect.Bottom <= outer.Bottom;
                    foreach (var window in shell.CurrentScreen.Tree.AllWindows.Where(window => window.IsVisible && !window.Rect.IsEmpty && window.Name != "ScoreScreen.wnd:ParentScoreScreen"))
                        TestHarness.Assert(Inside(window.Rect, new UiRect(0, 0, width, height)), $"{label}: {window.Name} lies on the screen ({window.Rect})");
                    foreach (var local in new[] { "StaticTextWarSchool", "ListboxWarschoolAdvice", "ButtonSaveReplay", "ButtonContinue", "ButtonOk", "Headline", "MatchLine", "GadgetParent" })
                        TestHarness.Assert(Inside(Window(shell, local).Rect, panel), $"{label}: {local} lies in the panel");
                    var previousBottom = Window(shell, "GadgetParent").Rect.Y;
                    for (var row = 0; row < players; row++)
                    {
                        var cell = Window(shell, $"StaticTextPlayer{row}");
                        TestHarness.Assert(cell.IsVisible && cell.Rect.Y >= previousBottom && cell.Rect.Height > 0, $"{label}: row {row} is shown below the one before it");
                        previousBottom = cell.Rect.Bottom;
                        foreach (var column in new[] { "UnitsBuilt", "UnitsLost", "UnitsDestroyed", "BuildingsBuilt", "BuildingsLost", "BuildingsDestroyed", "Resources" })
                        {
                            var number = Window(shell, $"StaticText{column}{row}");
                            TestHarness.Assert(number.IsVisible && number.Rect.Y == cell.Rect.Y && number.Rect.Height == cell.Rect.Height && number.Rect.X >= cell.Rect.Right, $"{label}: row {row} {column} stands in the row's line");
                        }
                    }
                    for (var row = players; row < 8; row++) TestHarness.Assert(!Window(shell, $"StaticTextPlayer{row}").IsVisible, $"{label}: row {row} has no player");
                    TestHarness.Assert(Window(shell, "GadgetParent").Rect.Bottom >= previousBottom, $"{label}: the last row lies in the table");
                    var advice = Window(shell, "ListboxWarschoolAdvice").Rect;
                    var heading = Window(shell, "StaticTextWarSchool").Rect;
                    TestHarness.Assert(heading.Y >= previousBottom && advice.Y >= heading.Bottom, $"{label}: the panel's heading and list lie under the table");
                    var buttons = new[] { "ButtonSaveReplay", "ButtonContinue", "ButtonOk" }.Select(local => Window(shell, local).Rect).ToArray();
                    TestHarness.Assert(buttons.All(button => button.Y >= advice.Bottom && button.Y == buttons[0].Y && button.Height == buttons[0].Height), $"{label}: the buttons stand in one line under the advice");
                    TestHarness.Assert(buttons[0].Right <= buttons[1].X && buttons[1].Right <= buttons[2].X, $"{label}: the buttons do not touch");
                    // The buttons and the panel stand in the same place for every number of players.
                    if (players > 2)
                    {
                        var two = Scored(data, config, root, ScoreScreenFixture.Result(2));
                        TestHarness.Assert(Window(two, "ButtonOk").Rect == Window(shell, "ButtonOk").Rect && Window(two, "MainBackdrop").Rect == panel, $"{label}: the buttons and the panel stand where they stand with two players");
                    }
                    if (measured)
                    {
                        var report = UiReadability.Audit(shell.CurrentScreen.Tree, shell.BuildDrawList());
                        TestHarness.Assert(report.Overflows.IsEmpty, $"{label}: {string.Join("; ", report.Overflows.Take(3).Select(overflow => overflow.Window + " " + overflow.Measure))}");
                    }
                    var cells = Enumerable.Range(0, players).Select(row => Window(shell, $"StaticTextPlayer{row}")).ToArray();
                    TestHarness.Assert(cells.Max(cell => cell.Rect.Height) - cells.Min(cell => cell.Rect.Height) <= 1, $"{label}: the rows are equally high (to a pixel)");
                }
    }

    private static void HeadlineSaysIt(GameData data, string root)
    {
        var config = new ProductSurveyShell.Config(UiSkin.Modern, 1280, 720, 100);
        string Headline(MatchResult result) => Window(Scored(data, config, root, result), "Headline").Text!.Value;
        string Line(MatchResult result) => Window(Scored(data, config, root, result), "MatchLine").Text!.Value;
        TestHarness.Assert(Headline(ScoreScreenFixture.Result(2, viewerWon: true)) == ScoreScreenScreen.Victory, "the winner reads Victory");
        TestHarness.Assert(Headline(ScoreScreenFixture.Result(3, viewerWon: false)) == ScoreScreenScreen.Defeat, "the loser reads Defeat");
        var decided = ScoreScreenFixture.Result(2);
        var undecided = decided with { Rows = [.. decided.Rows.Select(row => row with { IsWinner = false, IsDefeated = false })] };
        TestHarness.Assert(Headline(undecided) == ScoreScreenScreen.NoWinner, "a match nobody won reads No winner");
        var watched = decided with { Rows = [.. decided.Rows.Select(row => row with { Player = row.Player with { IsComputer = true } })] };
        TestHarness.Assert(Headline(watched) == ScoreScreenScreen.MatchOver, "somebody who only watched reads Match over");
        var line = Line(ScoreScreenFixture.Result(3, viewerWon: false, endSeconds: 754));
        TestHarness.Assert(line.Contains("Alpine Assault", StringComparison.Ordinal) && line.Contains("12:34", StringComparison.Ordinal) && line.Contains("3 players", StringComparison.Ordinal) && line.Contains("won by Computer 1", StringComparison.Ordinal), $"the line names the map, the time, the players and the winner ({line})");
        TestHarness.Assert(Line(ScoreScreenFixture.Result(2, withAnalysis: false)).Contains("0:27", StringComparison.Ordinal), "without an analysis the time comes from the match's end tick");
    }

    private static void Wheel(GameShell shell, UiWindow at, int notches)
        => shell.HandleInput(new MouseWheelEvent(++_clock, notches, new Vector2(at.Rect.CenterX, at.Rect.CenterY)));

    private static void AdviceScrolls(GameData data, string root)
    {
        var scrolled = 0;
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in ProductSurveyShell.Sizes)
                foreach (var percent in new[] { 100, 125, 150 })
                {
                    var config = new ProductSurveyShell.Config(skin, width, height, percent);
                    var shell = Scored(data, config, root, ScoreScreenFixture.Result(2));
                    var list = shell.CurrentScreen.Tree.FindByName(ScoreScreenLayoutNames.Advice) ?? throw new InvalidOperationException($"{config}: no advice list");
                    var state = list.ListBox!;
                    TestHarness.Assert(state.Data.ScrollBar && (list.Status & CnCNew.Ui.Wnd.WndStatus.NoInput) == 0, $"{config}: the advice list has a bar and takes input");
                    TestHarness.Assert(state.Rows.Count >= 4, $"{config}: the advice has {state.Rows.Count} rows");
                    var visible = UiGadgetGeometry.VisibleRows(list);
                    var max = UiGadgetGeometry.MaxTopIndex(list);
                    TestHarness.Assert(state.TopIndex == 0, $"{config}: the advice opens at its first line");
                    if (max == 0) continue;
                    scrolled++;
                    Wheel(shell, list, -1);
                    TestHarness.Assert(state.TopIndex == 1, $"{config}: one notch down scrolls one row ({state.TopIndex})");
                    Wheel(shell, list, 1);
                    TestHarness.Assert(state.TopIndex == 0, $"{config}: one notch up goes back");
                    Wheel(shell, list, -50);
                    TestHarness.Assert(state.TopIndex == max && state.TopIndex + visible >= state.Rows.Count, $"{config}: the wheel reaches the last row ({state.TopIndex} of {max})");
                    var last = state.Rows[^1].Cells[0];
                    var drawn = shell.BuildDrawList().Commands.OfType<UiTextDraw>().FirstOrDefault(text => text.Text == last);
                    TestHarness.Assert(drawn is not null && drawn.Y >= list.Rect.Y && drawn.Y + shell.CurrentScreen.Tree.Services.TextMeasure.LineHeight(drawn.Font) <= list.Rect.Bottom + 1, $"{config}: the last line is drawn inside the panel");
                    // The bar: its up arrow, its down arrow, a click on the track and the thumb's drag.
                    var up = UiGadgetGeometry.UpButton(list);
                    ShellTestInput.ClickAt(shell, up.CenterX, up.CenterY);
                    TestHarness.Assert(state.TopIndex == max - 1, $"{config}: the up arrow scrolls one row ({state.TopIndex})");
                    var down = UiGadgetGeometry.DownButton(list);
                    ShellTestInput.ClickAt(shell, down.CenterX, down.CenterY);
                    TestHarness.Assert(state.TopIndex == max, $"{config}: the down arrow scrolls one row");
                    var track = UiGadgetGeometry.ScrollTrack(list);
                    ShellTestInput.ClickAt(shell, track.CenterX, track.Y + 1);
                    TestHarness.Assert(state.TopIndex == 0, $"{config}: a click at the top of the track goes to the first row ({state.TopIndex})");
                    shell.HandleInput(new MouseMoveEvent(++_clock, new Vector2(track.CenterX, track.Y + 1)));
                    shell.HandleInput(new MouseButtonEvent(++_clock, MouseButton.Left, true, new Vector2(track.CenterX, track.Y + 1)));
                    shell.HandleInput(new MouseMoveEvent(++_clock, new Vector2(track.CenterX, track.Bottom - 1)));
                    shell.HandleInput(new MouseButtonEvent(++_clock, MouseButton.Left, false, new Vector2(track.CenterX, track.Bottom - 1)));
                    TestHarness.Assert(state.TopIndex == max, $"{config}: dragging the thumb to the bottom of the track reaches the last row ({state.TopIndex} of {max})");
                    ShellTestInput.ClickAt(shell, list.Rect.X + 20, list.Rect.CenterY);
                    TestHarness.Assert(state.SelectedIndex < 0, $"{config}: a click on a line selects nothing");
                }
        TestHarness.Assert(scrolled >= 12, $"half of the configurations have more advice than the panel holds ({scrolled} of 24)");
    }
}

/// <summary>The decorated names of the score screen's windows that tests look for.</summary>
internal static class ScoreScreenLayoutNames
{
    public const string Advice = "ScoreScreen.wnd:ListboxWarschoolAdvice";
}
