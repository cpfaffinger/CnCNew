using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.UiRenderTool;

/// <summary>
/// The rest of the screens a player meets between the main menu and a match, driven through the shell with real input
/// like <see cref="ShellScenes"/>: single-player menu, skirmish setup and map picker (and back), the match loading screen, the score
/// screen after a short match, credits, load game, the challenge menu and the USA campaign's difficulty question up to the mission briefing.
/// These are the screens whose text the human compared against the original (docs/TEXT-AUDIT.md).
/// </summary>
internal static class ShellFlowScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1920, 1080)];

    public static void Capture(GameData data, IUiTextMeasure textMeasure, string outputDirectory, Action<UiDrawList, int, int, string> capture)
    {
        foreach (var (width, height) in Sizes)
        {
            var time = 0L;
            GameShell NewShell() => GameShell.Create(data, new FixedShellEnvironment(Path.Combine(outputDirectory, "shell-settings"), width, height), "English", new UiViewport(width, height), textMeasure);
            void Shot(GameShell shell, string name) => capture(shell.BuildDrawList(), width, height, $"flow-{name}-{width}x{height}");
            void Click(GameShell shell, string window)
            {
                var rect = (shell.CurrentScreen.Tree.FindByName(window) ?? throw new InvalidDataException($"Window '{window}' is missing.")).Rect;
                var point = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                shell.HandleInput(new MouseMoveEvent(++time, point));
                shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, true, point));
                shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, false, point));
                shell.HandleInput(new MouseMoveEvent(++time, new Vector2(0, 0)));
            }

            var shell = NewShell();
            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Shot(shell, "single-player");
            var usa = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonUSA")!.Rect;
            shell.HandleInput(new MouseMoveEvent(++time, new Vector2(usa.X + usa.Width / 2, usa.Y + usa.Height / 2)));
            shell.Update(0.9);
            Shot(shell, "tooltip-revealing");
            shell.Update(0.5);
            Shot(shell, "tooltip");
            shell.HandleInput(new MouseMoveEvent(++time, new Vector2(0, 0)));
            Click(shell, "MainMenu.wnd:ButtonSkirmish");
            Shot(shell, "skirmish-setup");
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonSelectMap");
            Shot(shell, "skirmish-map-select");
            var list = shell.CurrentScreen.Tree.FindByName("SkirmishMapSelectMenu.wnd:ListboxMap")!;
            var row = new Vector2(list.Rect.X + 20, list.Rect.Y + 8);
            shell.HandleInput(new MouseMoveEvent(++time, row));
            shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, true, row));
            shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, false, row));
            Shot(shell, "skirmish-map-chosen");
            Click(shell, "SkirmishMapSelectMenu.wnd:ButtonOK");
            Shot(shell, "skirmish-setup-map");
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonStart");
            Shot(shell, "match-loading");
            shell.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
            var start = shell.DrainOutputs().OfType<StartMatchOutput>().Single();
            var setup = start.Setup with { Options = start.Setup.Options with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(20) } };
            var live = LiveMatch.Create(data, setup, new Vector2(width, height));
            var stats = new MatchStatistics();
            while (!live.Session.IsOver) stats.Record(live.Session.Step());
            shell.MatchFinished(live.Session, stats);
            Shot(shell, "score");
            Click(shell, "ScoreScreen.wnd:ButtonOk");

            Click(shell, "MainMenu.wnd:ButtonCredits");
            Shot(shell, "credits");

            // Shell::push hides the screen below: setup -> map picker (an overlay of the setup) -> back -> back must never show the main menu's windows behind them.
            shell = NewShell();
            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Click(shell, "MainMenu.wnd:ButtonSkirmish");
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonSelectMap");
            Click(shell, "SkirmishMapSelectMenu.wnd:ButtonBack");
            Shot(shell, "skirmish-after-map-back");
            Click(shell, "SkirmishGameOptionsMenu.wnd:ButtonBack");
            Shot(shell, "main-after-skirmish");

            Click(shell, "MainMenu.wnd:ButtonLoadReplay");
            Shot(shell, "load-replay-menu");
            Click(shell, "MainMenu.wnd:ButtonLoadGame");
            Shot(shell, "load-game");
            Click(shell, "SaveLoad.wnd:ButtonBack");

            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Click(shell, "MainMenu.wnd:ButtonChallenge");
            Shot(shell, "challenge-difficulty");
            Click(shell, "MainMenu.wnd:ButtonMedium");
            Shot(shell, "challenge-menu");
            Click(shell, "ChallengeMenu.wnd:GeneralPosition2");
            Shot(shell, "challenge-menu-picked");

            shell = NewShell();
            Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            Click(shell, "MainMenu.wnd:ButtonUSA");
            Shot(shell, "campaign-difficulty");
            Click(shell, "MainMenu.wnd:ButtonHard");
            Shot(shell, "briefing-usa");
            shell.Update(0.5);
            Shot(shell, "briefing-usa-later");
        }
    }
}
