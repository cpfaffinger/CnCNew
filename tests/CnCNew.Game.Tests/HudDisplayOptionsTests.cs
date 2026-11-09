using System.Collections.Immutable;
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
        harness.Run("Which objects show a health bar: the rule, the colours of the original's box, the heights of every style and the draw commands of each style", MarkerGeometry);
        harness.Run("The health bars of every object in view or of every damaged one, beside what is selected, in a staged fight on real data; the default shows only what is selected or under the pointer", () => AmbientBars(data));
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

    /// <summary>A marker as the controller makes one: a unit of the viewer's at a place on a 1280 x 720 screen.</summary>
    private static UnitMarker Marker(float health = 1f, bool structure = false, bool barOnly = false, bool hovered = false, int damage = 0, bool blue = false)
        => new(new CnCNew.Simulation.Ids.EntityId(7), new Vector2(400, 300), 20f, health, true, false, structure) { BarOnly = barOnly, Hovered = hovered, Damage = damage, Blue = blue };

    private static ImmutableArray<UiDrawCommand> Draw(UnitMarker marker, SelectionMarkerStyle style = SelectionMarkerStyle.Extended, HealthBarHeight height = HealthBarHeight.Normal)
        => HudOverlayRenderer.Build(HudOverlay.Empty with { Selected = [marker] }, 0, new HudMarkerOptions(style, height));

    private static void MarkerGeometry()
    {
        // The rule: what is selected or under the pointer always shows; the others by the viewer's mode.
        foreach (var mode in Enum.GetValues<HealthBarMode>())
            foreach (var below in new[] { false, true })
            {
                TestHarness.Assert(HealthBarRule.Shows(mode, true, below), $"a selected object shows its bar ({mode}, below full {below})");
                var expected = mode == HealthBarMode.AlwaysAuto || (mode == HealthBarMode.AlwaysBelowFull && below);
                TestHarness.Assert(HealthBarRule.Shows(mode, false, below) == expected, $"an unselected object ({mode}, below full {below}) shows {expected}");
            }

        // The heights: the extended bar's fill and its outline, the original's box (3 pixels is the original's own), the modern bar.
        var heights = new[] { HealthBarHeight.Narrow, HealthBarHeight.Normal, HealthBarHeight.Wide };
        TestHarness.Assert(heights.Select(height => HudOverlayRenderer.BarFillHeight(SelectionMarkerStyle.Extended, height)).SequenceEqual([3, 5, 8]) && heights.Select(height => HudOverlayRenderer.BarOuterHeight(SelectionMarkerStyle.Extended, height)).SequenceEqual([5, 7, 10]), "extended: 3, 5, 8 pixels of fill inside an outline (the default 7 pixels is what was always drawn)");
        TestHarness.Assert(heights.Select(height => HudOverlayRenderer.BarOuterHeight(SelectionMarkerStyle.Original, height)).SequenceEqual([3, 5, 8]), "original: a box of 3, 5, 8 pixels, the narrow one the original's own");
        TestHarness.Assert(heights.Select(height => HudOverlayRenderer.BarOuterHeight(SelectionMarkerStyle.Modern, height)).SequenceEqual([3, 4, 6]), "modern: 3, 4, 6 pixels, flat");

        // Drawable::drawHealthBar's colours, worked by hand from the source: pristine and full is green; pristine and half is yellow-green (red halved, green averaged with full); really damaged and a fifth is
        // orange-red; under construction runs from blue to cyan and its outline is half of it.
        var (pristine, pristineOutline) = HudOverlayRenderer.OriginalHealthColors(1f, 0, false);
        TestHarness.Assert(pristine == new CnCNew.Ui.Wnd.WndColor(0, 255, 0, 255) && pristineOutline == new CnCNew.Ui.Wnd.WndColor(0, 127, 0, 255), $"full health, pristine: green with a dark green outline ({pristine}, {pristineOutline})");
        TestHarness.Assert(HudOverlayRenderer.OriginalHealthColors(0.5f, 0, false).Fill == new CnCNew.Ui.Wnd.WndColor(127, 255, 0, 255), "half health, pristine: 127, 255, 0");
        TestHarness.Assert(HudOverlayRenderer.OriginalHealthColors(0.5f, 1, false).Fill == new CnCNew.Ui.Wnd.WndColor(255, 255, 0, 255), "half health, damaged: yellow, as the plain ramp");
        TestHarness.Assert(HudOverlayRenderer.OriginalHealthColors(0.25f, 2, false).Fill == new CnCNew.Ui.Wnd.WndColor(255, 63, 0, 255), "a quarter, really damaged: 255, 63, 0");
        TestHarness.Assert(HudOverlayRenderer.OriginalHealthColors(0.5f, 0, true) is { Fill: { R: 0, G: 127, B: 255 }, Outline: { R: 0, G: 64, B: 128 } }, "under construction or disabled: blue to cyan");

        // Extended and Normal draw what every earlier version drew: a plate 7 high behind a fill 5 high, a ring of 28 lines under a unit and brackets round a building.
        var extended = Draw(Marker(0.5f));
        TestHarness.Assert(extended.OfType<UiLineDraw>().Count() == 28 && extended.OfType<UiFillRect>().Any(fill => fill.Rect.Height == 7) && extended.OfType<UiFillRect>().Any(fill => fill.Rect.Height == 5 && fill.Color == HudOverlayRenderer.HealthColor(0.5f)), "extended: ring, plate of 7, fill of 5 in the ramp's colour");
        TestHarness.Assert(Draw(Marker(1f, structure: true)).OfType<UiLineDraw>().Count() == 8, "extended: eight lines of brackets round a building");
        TestHarness.Assert(Draw(Marker(0.5f)).SequenceEqual(HudOverlayRenderer.Build(HudOverlay.Empty with { Selected = [Marker(0.5f)] }, 0)), "no options is extended and normal");
        foreach (var height in heights)
        {
            var fill = HudOverlayRenderer.BarFillHeight(SelectionMarkerStyle.Extended, height);
            TestHarness.Assert(Draw(Marker(0.5f), SelectionMarkerStyle.Extended, height).OfType<UiFillRect>().Any(item => item.Rect.Height == fill + 2) && Draw(Marker(0.5f), SelectionMarkerStyle.Extended, height).OfType<UiFillRect>().Any(item => item.Rect.Height == fill), $"extended {height}: plate {fill + 2}, fill {fill}");
        }

        // Original: nothing marks the selection but the box over it - no ring, no bracket - and the box is an outline with its fill inside.
        foreach (var height in heights)
        {
            var original = Draw(Marker(0.5f, damage: 1), SelectionMarkerStyle.Original, height);
            var outer = HudOverlayRenderer.BarOuterHeight(SelectionMarkerStyle.Original, height);
            TestHarness.Assert(original.OfType<UiLineDraw>().Count() == 0 && original.OfType<UiBorderRect>().Single().Rect.Height == outer, $"original {height}: no ring, one box of {outer} pixels");
            var inside = original.OfType<UiFillRect>().SingleOrDefault();
            TestHarness.Assert(outer == 3 ? inside is { Rect.Height: 1 } : inside is not null && inside.Rect.Height == outer - 2, $"original {height}: the fill is the box less its outline");
            var colours = HudOverlayRenderer.OriginalHealthColors(0.5f, 1, false);
            TestHarness.Assert(original.OfType<UiBorderRect>().Single().Color == colours.Outline && inside!.Color == colours.Fill, $"original {height}: the colours of the source");
        }
        TestHarness.Assert(Draw(Marker(0.5f, structure: true), SelectionMarkerStyle.Original).OfType<UiLineDraw>().Count() == 0, "original: no brackets round a building either");
        TestHarness.Assert(Draw(Marker(0f), SelectionMarkerStyle.Original).Length == 0, "original: an object without health has no box");

        // Modern: a thin ring (two of them, the outer faint) or short brackets, and a flat bar in the skin's colours.
        var modern = Draw(Marker(1f), SelectionMarkerStyle.Modern);
        TestHarness.Assert(modern.OfType<UiLineDraw>().Count() == 56 && modern.OfType<UiLineDraw>().Max(line => line.Thickness) <= 1.5f && modern.OfType<UiFillRect>().Any(fill => fill.Color == UiSkin.Modern.Style.Palette.Good), "modern: two thin rings and a bar in the skin's green");
        TestHarness.Assert(Draw(Marker(0.2f), SelectionMarkerStyle.Modern).OfType<UiFillRect>().Any(fill => fill.Color == UiSkin.Modern.Style.Palette.Bad) && Draw(Marker(0.4f), SelectionMarkerStyle.Modern).OfType<UiFillRect>().Any(fill => fill.Color == UiSkin.Modern.Style.Palette.Accent), "modern: amber below half, red below a quarter");
        TestHarness.Assert(Draw(Marker(1f, structure: true), SelectionMarkerStyle.Modern).OfType<UiLineDraw>().Count() == 8 && Draw(Marker(1f, structure: true), SelectionMarkerStyle.Modern).OfType<UiLineDraw>().Max(line => line.Thickness) <= 1.5f, "modern: short thin brackets round a building");

        // A marker that is only a bar (an object the viewer asked to see the health of) has no ring, no bracket and no pips in any style; one under the pointer has no ring either.
        foreach (var style in Enum.GetValues<SelectionMarkerStyle>())
        {
            TestHarness.Assert(Draw(Marker(0.5f, barOnly: true), style).OfType<UiLineDraw>().Count() == 0 && Draw(Marker(0.5f, barOnly: true), style).Length > 0, $"{style}: a bar alone");
            TestHarness.Assert(Draw(Marker(0.5f, hovered: true), style).OfType<UiLineDraw>().Count() == 0, $"{style}: under the pointer only the bar shows");
        }
    }

    private static void AmbientBars(GameData data)
    {
        // Two of the viewer's barracks and an enemy tank that shoots at the second until it is hurt; the first stays whole.
        var units = new[]
        {
            new ScenarioUnit("b1", "AmericaBarracks", 1, 0, 0, 0f), new ScenarioUnit("b2", "AmericaBarracks", 1, 90, 0, 0f), new ScenarioUnit("tank", "ChinaTankBattleMaster", 2, 90, 110, 0f),
        };
        var orders = new[] { new ScenarioOrder(1, "tank", ScenarioOrderKind.Attack, "b2") };
        var scenario = new DemoScenario("hud-bars", "hud-bars", "hud-bars", 6000, [.. units], [.. orders], 45f, 0f, CameraHeight: 330f) { StartMoney = 1000 };
        HashSet<string> Marked(Scene scene, out bool[] barOnly)
        {
            scene.Draw();
            barOnly = [.. scene.Controller.Overlay.Selected.Select(marker => marker.BarOnly)];
            return [.. scene.Controller.Overlay.Selected.Select(marker => scene.Live.Tags.First(tag => tag.Value == marker.Entity).Key)];
        }
        Scene Staged(GameSettings settings)
        {
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var scene = new Scene(live, settings);
            scene.Frame(240);
            return scene;
        }

        var shot = Staged(Modern());
        var b2 = shot.Live.World.FindEntity(shot.Live.Tags["b2"])!;
        var b1 = shot.Live.World.FindEntity(shot.Live.Tags["b1"])!;
        TestHarness.Assert(b2.Body!.Health < b2.Body.MaxHealth && b1.Body!.Health == b1.Body.MaxHealth, $"the staged fight hurt the second barracks and not the first ({b2.Body!.Health} of {b2.Body.MaxHealth}, {b1.Body!.Health} of {b1.Body.MaxHealth})");

        // The default (only what is selected or under the pointer): nothing is marked, whatever is hurt.
        TestHarness.Assert(Marked(shot, out _).Count == 0, "by default nothing unselected shows a bar");

        // Every damaged object: the hurt barracks only.
        var damaged = Staged(Modern(display => display with { HealthBars = HealthBarMode.AlwaysBelowFull }));
        var damagedMarks = Marked(damaged, out var damagedBarOnly);
        TestHarness.Assert(damagedMarks.SetEquals(["b2"]) && damagedBarOnly.All(only => only), $"always, when not at full health: the hurt barracks, as a bar alone ({string.Join(", ", damagedMarks)})");
        // ... and a selected one at full health shows as well, with its marker.
        damaged.Controller.Input.Selection.Replace([damaged.Live.Tags["b1"]]);
        damaged.Frame(2);
        var withSelection = Marked(damaged, out var selectionBarOnly);
        TestHarness.Assert(withSelection.SetEquals(["b1", "b2"]) && selectionBarOnly.Count(only => !only) == 1, $"the selected whole barracks shows beside the hurt one ({string.Join(", ", withSelection)})");

        // Every object in view: both barracks and the tank, each a bar alone.
        var everything = Staged(Modern(display => display with { HealthBars = HealthBarMode.AlwaysAuto }));
        var everyMarks = Marked(everything, out var everyBarOnly);
        TestHarness.Assert(everyMarks.SetEquals(["b1", "b2", "tank"]) && everyBarOnly.All(only => only), $"always: every barracks and the tank ({string.Join(", ", everyMarks)})");
        // The setting reaches the running HUD with the next frame.
        shot.Controller.ApplyDisplayOptions(Modern(display => display with { HealthBars = HealthBarMode.AlwaysAuto }));
        shot.Frame(2);
        TestHarness.Assert(Marked(shot, out _).SetEquals(["b1", "b2", "tank"]), "a change of the mode reaches a match under way");
        shot.Controller.ApplyDisplayOptions(Modern());
        shot.Frame(2);
        TestHarness.Assert(Marked(shot, out _).Count == 0, "and back");

        // The style and the height reach the draw list: in the original style no line is drawn for a selected unit, and the bar is as high as asked.
        var original = Staged(Modern(display => display with { MarkerStyle = SelectionMarkerStyle.Original, HealthBarThickness = HealthBarHeight.Narrow, HealthBars = HealthBarMode.AlwaysAuto }));
        var list = original.Draw();
        TestHarness.Assert(list.Commands.OfType<UiBorderRect>().Count(border => border.Rect.Height == 3 && border.Rect.Width >= 26) >= 3, "the original style draws a 3-pixel box for each of the three objects");
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
