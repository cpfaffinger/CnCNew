using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The main menu at a window size other than the one the shell was built for (GitHub issue 273): the client builds the shell at its start size and applies the size the settings keep
/// with its first frames, while the first menu is fading in. The window transitions (the buttons that flash in one after another, the logo's fade) read their windows' places once, so
/// after the resize they drew boxes at the old places - translucent white rectangles without label or art, where no button was. A resized shell must draw exactly what a shell built at
/// that size draws, frame by frame, and at the end of the entry every button shows its label and answers the pointer.
/// </summary>
internal static class ShellSizeTests
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1050, 825), (1280, 720), (1366, 768), (1920, 1200)];
    private static readonly string[] Buttons = ["ButtonSinglePlayer", "ButtonMultiplayer", "ButtonLoadReplay", "ButtonOptions", "ButtonCredits", "ButtonExit"];
    private const double Frame = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;
    private const int StartWidth = 1280, StartHeight = 720;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(ShellSizeTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP ShellSizeTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Issue 273: a shell resized while the main menu fades in draws, frame by frame, what a shell built at that size draws (both skins, five sizes, at every moment of the entry)", () => ResizedEntryEqualsBuiltEntry(data));
        harness.Run("Issue 273: a resize at any frame of the entry draws every flash exactly over its button or the menu's border", () => EveryResizeMoment(data));
        harness.Run("Issue 273: at the end of the entry every button shows its label, and the pointer over a button changes what is drawn there (both skins, five sizes, built and resized)", () => EntryEndsWithLabelsAndHighlight(data));
        harness.Run("Issue 273: the modern skin's flashing buttons stand in with their own fill and border, not with a bare white box", () => ModernStandInHasItsArt(data));
        return harness.Failures;
    }

    private static GameShell NewShell(GameData data, UiSkin skin, int width, int height)
        => GameShell.Create(data, new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-shell-size-" + Guid.NewGuid().ToString("N"))), viewport: new UiViewport(width, height), animateTransitions: true, skin: skin);

    /// <summary>The first command that differs between two draw lists, or null when they are the same.</summary>
    private static string? FirstDifference(UiDrawList built, UiDrawList resized)
    {
        var length = Math.Min(built.Commands.Length, resized.Commands.Length);
        for (var i = 0; i < length; i++)
            if (!built.Commands[i].Equals(resized.Commands[i])) return $"command {i}: built {built.Commands[i]} / resized {resized.Commands[i]}";
        return built.Commands.Length == resized.Commands.Length ? null : $"{built.Commands.Length} commands built, {resized.Commands.Length} resized";
    }

    private static void ResizedEntryEqualsBuiltEntry(GameData data)
    {
        foreach (var skin in new[] { UiSkin.Modern, UiSkin.Original })
            foreach (var (width, height) in Sizes)
            {
                var built = NewShell(data, skin, width, height);
                var resized = NewShell(data, skin, StartWidth, StartHeight);
                // What the client does: the first frame is drawn at the start size, the saved size is applied with the next ones.
                for (var frame = 0; frame < 70; frame++)
                {
                    if (frame == 3) resized.Resize(width, height);
                    // The first frames are the start size's: only what is drawn after the resize is compared.
                    if (frame < 3)
                    {
                        built.Update(Frame);
                        resized.Update(Frame);
                        continue;
                    }
                    var difference = FirstDifference(built.BuildDrawList(), resized.BuildDrawList());
                    TestHarness.Assert(difference is null, $"{skin.Name} {width}x{height}: frame {frame} of the entry differs after the resize at frame 3 - {difference}");
                    if (difference is not null) return;
                    built.Update(Frame);
                    resized.Update(Frame);
                }
            }
    }

    private static void EveryResizeMoment(GameData data)
    {
        // The flash of a button is drawn over the button's own window: a flash that lies elsewhere is a box in the wrong place.
        foreach (var skin in new[] { UiSkin.Modern, UiSkin.Original })
            for (var resizeAt = 0; resizeAt < 62; resizeAt += 2)
            {
                var shell = NewShell(data, skin, StartWidth, StartHeight);
                for (var frame = 0; frame < resizeAt; frame++) shell.Update(Frame);
                shell.Resize(1050, 825);
                var tree = shell.CurrentScreen.Tree;
                for (var frame = 0; frame < 62; frame++)
                {
                    var list = shell.BuildDrawList();
                    foreach (var command in list.Commands)
                    {
                        if (command is not UiFillRect { Color: { R: 255, G: 255, B: 255, A: < 255 } } flash || flash.Rect.Width >= 400) continue;
                        var border = tree.FindByName("MainMenu.wnd:MapBorder2")!.Rect;
                        var onAButton = Buttons.Any(name => tree.FindByName($"MainMenu.wnd:{name}")!.Rect == flash.Rect) || flash.Rect == new UiRect(border.X + 1, border.Y + 1, border.Width - 2, border.Height);
                        TestHarness.Assert(onAButton, $"{skin.Name}: resized at frame {resizeAt}, frame {frame}: a white flash {flash.Rect} is the place of no button and of not the menu's border");
                        if (!onAButton) return;
                    }
                    shell.Update(Frame);
                }
            }
    }

    private static bool Overlaps(UiRect a, UiRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private static void EntryEndsWithLabelsAndHighlight(GameData data)
    {
        foreach (var skin in new[] { UiSkin.Modern, UiSkin.Original })
            foreach (var (width, height) in Sizes)
                foreach (var resized in new[] { false, true })
                {
                    var shell = NewShell(data, skin, resized ? StartWidth : width, resized ? StartHeight : height);
                    for (var frame = 0; frame < 8; frame++) shell.Update(Frame);
                    if (resized) shell.Resize(width, height);
                    for (var steps = 0; steps < 600 && shell.IsAnimating; steps++) shell.Update(Frame);
                    var label = $"{skin.Name} {width}x{height}{(resized ? " (resized)" : string.Empty)}";
                    TestHarness.Assert(!shell.IsAnimating, $"{label}: the entry ends");
                    var tree = shell.CurrentScreen.Tree;
                    var idle = shell.BuildDrawList();
                    foreach (var name in Buttons)
                    {
                        var window = tree.FindByName($"MainMenu.wnd:{name}")!;
                        var rect = window.Rect;
                        TestHarness.Assert(!window.IsHidden, $"{label}: {name} is shown at the end of the entry");
                        TestHarness.Assert(rect.Width > 0 && rect.Height > 0 && rect.X >= 0 && rect.Right <= width && rect.Y >= 0 && rect.Bottom <= height, $"{label}: {name} {rect} lies inside the window");
                        TestHarness.Assert(idle.Commands.OfType<UiTextDraw>().Any(text => text.Text.Length > 0 && Touches(text, rect)), $"{label}: {name} draws its label");
                    }
                    // The pointer over the fourth button highlights that button and no other.
                    var options = tree.FindByName("MainMenu.wnd:ButtonOptions")!.Rect;
                    shell.HandleInput(new MouseMoveEvent(1, new Vector2(options.CenterX, options.CenterY)));
                    shell.Update(Frame);
                    var hovered = shell.BuildDrawList();
                    foreach (var name in Buttons)
                    {
                        var rect = tree.FindByName($"MainMenu.wnd:{name}")!.Rect;
                        var before = idle.Commands.Where(command => Touches(command, rect)).ToArray();
                        var after = hovered.Commands.Where(command => Touches(command, rect)).ToArray();
                        var changed = !before.SequenceEqual(after);
                        TestHarness.Assert(changed == (name == "ButtonOptions"), $"{label}: the pointer over Options {(name == "ButtonOptions" ? "highlights it" : $"leaves {name} as it was")}");
                    }
                }
    }

    private static bool Touches(UiDrawCommand command, UiRect rect) => command switch
    {
        UiFillRect fill => Overlaps(fill.Rect, rect),
        UiBorderRect border => Overlaps(border.Rect, rect),
        UiImageDraw image => Overlaps(image.Destination, rect),
        UiTextDraw text => text.X >= rect.X && text.X <= rect.Right && text.Y >= rect.Y - 4 && text.Y <= rect.Bottom,
        _ => false,
    };

    private static void ModernStandInHasItsArt(GameData data)
    {
        var shell = NewShell(data, UiSkin.Modern, 1050, 825);
        var first = shell.CurrentScreen.Tree.FindByName("MainMenu.wnd:ButtonSinglePlayer")!.Rect;
        var shown = false;
        for (var frame = 0; frame < 70; frame++)
        {
            var list = shell.BuildDrawList();
            var inside = list.Commands.OfType<UiFillRect>().Where(fill => fill.Rect.X >= first.X && fill.Rect.Y >= first.Y && fill.Rect.Right <= first.Right && fill.Rect.Bottom <= first.Bottom).ToArray();
            var flashing = inside.Any(fill => fill.Color is { R: 255, G: 255, B: 255, A: < 255 });
            // While the stand-in flashes at its fading stage (frames 4 to 7 of the effect) the button's own fill lies under the flash.
            if (flashing && inside.Any(fill => fill.Color.A >= 200 && !(fill.Color.R == 255 && fill.Color.G == 255 && fill.Color.B == 255))) shown = true;
            shell.Update(Frame);
        }
        TestHarness.Assert(shown, "a flashing button of the modern skin draws its own fill under the fading flash");
    }
}
