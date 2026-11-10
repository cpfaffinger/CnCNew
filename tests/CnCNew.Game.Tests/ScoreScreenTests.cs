using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
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
