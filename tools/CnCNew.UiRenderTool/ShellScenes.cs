using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.UiRenderTool;

/// <summary>
/// Shell screens in the states a player actually sees them: driven through <see cref="GameShell"/> (the same entry point
/// the client uses for <c>--shell</c>) with real input events, so the pictures include the shell's own init code
/// (hidden sub-menus), hover/pressed gadget states, the options sliders and the quit dialog.
/// </summary>
internal static class ShellScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(1280, 720), (1920, 1080)];

    public static void Capture(GameData data, IUiTextMeasure textMeasure, string outputDirectory, Action<UiDrawList, int, int, string> capture)
    {
        foreach (var (width, height) in Sizes)
        {
            // Settings are only written on "Accept", which no scene clicks; the directory stays empty.
            var environment = new FixedShellEnvironment(Path.Combine(outputDirectory, "shell-settings"), width, height);
            var shell = GameShell.Create(data, environment, "English", new UiViewport(width, height), textMeasure);
            var time = 0L;
            void Shot(string name) => capture(shell.BuildDrawList(), width, height, $"shell-{name}-{width}x{height}");
            UiRect Rect(string name) => (shell.CurrentScreen.Tree.FindByName(name) ?? throw new InvalidDataException($"Window '{name}' is missing.")).Rect;
            void Move(int x, int y) => shell.HandleInput(new MouseMoveEvent(++time, new Vector2(x, y)));
            void Button(int x, int y, bool down) => shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, down, new Vector2(x, y)));
            (int X, int Y) Center(string name)
            {
                var rect = Rect(name);
                return (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            }
            void Click(string name)
            {
                var (x, y) = Center(name);
                Move(x, y);
                Button(x, y, true);
                Button(x, y, false);
            }

            Shot("main");
            var (singleX, singleY) = Center("MainMenu.wnd:ButtonSinglePlayer");
            Move(singleX, singleY);
            Shot("main-hover");
            Button(singleX, singleY, true);
            Shot("main-pressed");
            Move(0, 0);
            Button(0, 0, false);

            Click("MainMenu.wnd:ButtonOptions");
            Move(0, 0);
            Shot("options");
            var (labelX, labelY) = Center("OptionsMenu.wnd:MusicVolumeLabel");
            Move(labelX, labelY);
            Shot("options-label-hover");
            var slider = Rect("OptionsMenu.wnd:SliderMusicVolume");
            Move(slider.X + slider.Width / 4, slider.Y + slider.Height / 2);
            Shot("options-slider-hover");
            Button(slider.X + slider.Width / 4, slider.Y + slider.Height / 2, true);
            Move(slider.X + slider.Width * 3 / 4, slider.Y + slider.Height / 2);
            Shot("options-slider-drag");
            Move(slider.X + slider.Width * 3 / 4, slider.Bottom + 40);
            Shot("options-slider-drag-outside");
            Button(slider.X + slider.Width * 3 / 4, slider.Y + slider.Height / 2, false);
            var (acceptX, acceptY) = Center("OptionsMenu.wnd:ButtonAccept");
            Move(acceptX, acceptY);
            Shot("options-button-hover");
            Click("OptionsMenu.wnd:ButtonBack");
            Shot("main-after-options");

            Click("MainMenu.wnd:ButtonExit");
            Move(0, 0);
            Shot("quit");
            var (yesX, yesY) = Center("QuitMessageBox.wnd:ButtonYes");
            Move(yesX, yesY);
            Shot("quit-hover");
        }
    }
}
