using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The display options of schema 8 that the HUD obeys (the owner's playtest of 8 November 2026): the opacity of the modern bar, the style of the selection markers and which objects show
/// a health bar and how thick. Each on the real HUD of a staged scene, in the draw list a window would show.
/// </summary>
internal static class HudDisplayOptionsTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(HudDisplayOptionsTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP HudDisplayOptionsTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The modern bar's band and wells are drawn at the opacity of the settings (80 % by default), the buttons and the radar stay opaque, and a change reaches a running match", () => BarOpacity(data));
        return harness.Failures;
    }

    internal sealed class Scene
    {
        public Scene(GameData data, GameSettings settings, ScenarioUnit[] units, int width = 1280, int height = 720)
            : this(LiveScenario.Create(data, new DemoScenario("hud-options", "hud-options", "hud-options", 6000, [.. units], [], 100f, 0f, CameraHeight: 330f) { StartMoney = 30000 }, new Vector2(width, height)), settings, width, height)
        {
        }

        public Scene(LiveScenario live, GameSettings settings, int width = 1280, int height = 720)
        {
            Width = width;
            Height = height;
            Live = live;
            Controller = new MatchController(Live, settings) { StarBlinks = false };
            Frame(3);
        }

        public int Width { get; }
        public int Height { get; }
        public LiveScenario Live { get; }
        public MatchController Controller { get; }

        public void Frame(int count = 1)
        {
            for (var frame = 0; frame < count; frame++)
            {
                Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public UiDrawList Draw() => Controller.BuildDrawList(Width, Height);
    }

    public static GameSettings Modern(Func<DisplaySettings, DisplaySettings>? display = null)
    {
        var settings = GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = UiSkinName.Modern } };
        return display is null ? settings : settings with { Display = display(settings.Display) };
    }

    private static void BarOpacity(GameData data)
    {
        var canvas = UiSkin.Modern.Style.Palette.Canvas;
        UiFillRect Band(UiDrawList list, int width) => list.Commands.OfType<UiFillRect>().First(fill => fill.Rect.Width == width && fill.Rect.Height > 40 && fill.Rect.Height < 400
            && fill.Color.R == canvas.R && fill.Color.G == canvas.G && fill.Color.B == canvas.B);
        var units = new[] { new ScenarioUnit("b1", "AmericaBarracks", 1, 0, 0, 0f) };

        var scene = new Scene(data, Modern(), units);
        var band = Band(scene.Draw(), scene.Width);
        TestHarness.Assert(band.Color.A == (byte)Math.Round(canvas.A * 0.8), $"the default is 80 % of the canvas' opacity ({band.Color.A} of {canvas.A})");
        // The pieces that are not the background stay as opaque as they were: the radar's black and the four small buttons' wells are drawn at their own alpha, and the text of the plate.
        TestHarness.Assert(scene.Draw().Commands.OfType<UiFillRect>().Any(fill => fill.Color is { R: 0, G: 0, B: 0, A: 255 }), "the radar stays black and opaque");

        var opaque = new Scene(data, Modern(display => display with { BarOpacityPercent = 100 }), units);
        TestHarness.Assert(Band(opaque.Draw(), opaque.Width).Color == canvas, "at 100 % the band is the canvas colour, as before");
        var faint = new Scene(data, Modern(display => display with { BarOpacityPercent = 20 }), units);
        TestHarness.Assert(Band(faint.Draw(), faint.Width).Color.A == (byte)Math.Round(canvas.A * 0.2), "at 20 % it is a fifth of it");
        // A match under way follows the settings the player saves, with the next frame.
        faint.Controller.ApplyDisplayOptions(Modern(display => display with { BarOpacityPercent = 60 }));
        TestHarness.Assert(Band(faint.Draw(), faint.Width).Color.A == (byte)Math.Round(canvas.A * 0.6), "a change of the setting reaches the running HUD");
        // The text and the cameos are not faded: the same commands in the same order, only the translucent fills differ.
        var plain = scene.Draw();
        var other = opaque.Draw();
        TestHarness.Assert(plain.Commands.OfType<UiTextDraw>().Select(text => (text.Text, text.Color)).SequenceEqual(other.Commands.OfType<UiTextDraw>().Select(text => (text.Text, text.Color)))
            && plain.Commands.OfType<UiImageDraw>().Select(image => (image.Image, image.Tint)).SequenceEqual(other.Commands.OfType<UiImageDraw>().Select(image => (image.Image, image.Tint))), "text and pictures are drawn alike at 80 % and at 100 %");
        // The original skin has no such band: nothing changes there.
        var original = new Scene(data, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = UiSkinName.Original, BarOpacityPercent = 20 } }, units);
        var originalAt100 = new Scene(data, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = UiSkinName.Original, BarOpacityPercent = 100 } }, units);
        TestHarness.Assert(original.Draw().Commands.Length == originalAt100.Draw().Commands.Length && original.Draw().Commands.SequenceEqual(originalAt100.Draw().Commands), "the original skin's HUD does not depend on the opacity");
    }
}
