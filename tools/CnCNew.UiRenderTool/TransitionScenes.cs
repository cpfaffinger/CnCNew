using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.UiRenderTool;

/// <summary>
/// Window transition sequences (<c>GameWindowTransitions</c>, <c>docs/GAME-SHELL.md</c> §11): the animated shell is stepped at the original's 30 frames
/// per second and the frames of interest are written as <c>transition-NAME-fNN-1280x720.png</c>: the main menu fading in and flashing its buttons, the
/// switch to the single-player submenu, a hovered faction logo, the skirmish setup fading in, and the score screen filling its rows. The sequences are
/// deterministic, so two runs write identical files.
/// </summary>
internal static class TransitionScenes
{
    private const int Width = 1280, Height = 720;
    // Shell transitions are authored in original frames and run on wall time.
    private const double Frame = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;

    public static void Capture(GameData data, IUiTextMeasure textMeasure, string outputDirectory, Action<UiDrawList, int, int, string> capture)
    {
        var environment = new FixedShellEnvironment(Path.Combine(outputDirectory, "shell-settings"), Width, Height);
        var time = 0L;

        GameShell NewShell() => GameShell.Create(data, environment, "English", new UiViewport(Width, Height), textMeasure, animateTransitions: true);
        UiRect Rect(GameShell shell, string name) => (shell.CurrentScreen.Tree.FindByName(name) ?? throw new InvalidDataException($"Window '{name}' is missing.")).Rect;
        void Move(GameShell shell, int x, int y) => shell.HandleInput(new MouseMoveEvent(++time, new Vector2(x, y)));
        void MoveTo(GameShell shell, string name)
        {
            var rect = Rect(shell, name);
            Move(shell, rect.CenterX, rect.CenterY);
        }
        void Click(GameShell shell, string name)
        {
            var rect = Rect(shell, name);
            Move(shell, rect.CenterX, rect.CenterY);
            shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, true, new Vector2(rect.CenterX, rect.CenterY)));
            shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, false, new Vector2(rect.CenterX, rect.CenterY)));
        }
        void Settle(GameShell shell)
        {
            for (var frame = 0; frame < 600 && shell.IsAnimating; frame++) shell.Update(Frame);
        }
        // Writes the frames listed in <paramref name="frames"/> (counted from now) of a running sequence.
        void Film(GameShell shell, string name, params int[] frames)
        {
            var last = frames.Max();
            for (var frame = 0; frame <= last; frame++)
            {
                if (frames.Contains(frame)) capture(shell.BuildDrawList(), Width, Height, $"transition-{name}-f{frame:D2}-{Width}x{Height}");
                shell.Update(Frame);
            }
        }

        // 1. The first main menu of a run: fade in from black, logo, border, the six buttons flash in one after the other.
        var entry = NewShell();
        Film(entry, "entry", 0, 12, 24, 30, 34, 37, 40, 43, 46, 50, 54, 58);
        Settle(entry);
        capture(entry.BuildDrawList(), Width, Height, $"transition-entry-settled-{Width}x{Height}");

        // 2. Single player: the root buttons flash out, the border and the five buttons flash in, the faction logos scale up.
        Click(entry, "MainMenu.wnd:ButtonSinglePlayer");
        Film(entry, "single", 1, 4, 8, 12, 16, 20, 24, 28, 34);
        Settle(entry);
        capture(entry.BuildDrawList(), Width, Height, $"transition-single-settled-{Width}x{Height}");

        // 3. A hovered faction button: its logo grows from the small row.
        MoveTo(entry, "MainMenu.wnd:ButtonUSA");
        Film(entry, "hover-usa", 1, 2, 3, 6);
        Move(entry, 2, 2);
        Film(entry, "hover-usa-away", 1, 2, 6);
        Settle(entry);

        // 4. Skirmish: the menu flashes out, the setup fades in (black ramps up, the content appears at the peak).
        Click(entry, "MainMenu.wnd:ButtonSkirmish");
        Film(entry, "skirmish-out", 2, 6, 10);
        var guard = 0;
        while (entry.CurrentScreen is CnCNew.Game.Shell.Screens.MainMenuScreen && guard++ < 400) entry.Update(Frame);
        Film(entry, "skirmish-in", 1, 3, 4, 5, 6, 8, 10);

        // 5. The skirmish score screen fills its rows and counts its numbers up.
        var setup = new MatchSetup("Maps/Alpine Assault/Alpine Assault.map", "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.Standard with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(30) });
        var live = LiveMatch.Create(data, setup, new Vector2(Width, Height));
        var statistics = new MatchStatistics();
        while (!live.Session.IsOver) statistics.Record(live.Session.Step());
        var score = NewShell();
        Settle(score);
        score.MatchFinished(live.Session, statistics);
        Film(score, "score", 0, 1, 8, 12, 20, 30, 40, 55, 70);
    }
}
