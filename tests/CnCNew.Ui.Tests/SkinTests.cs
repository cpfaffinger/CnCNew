using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;
using UiScaleMode = CnCNew.Ui.Layout.UiScaleMode;

namespace CnCNew.Ui.Tests;

/// <summary>
/// The skins of docs/UI-MODERN.md on synthetic layouts: the original skin changes nothing (R1), the modern derivation keeps every window's identity and changes how it
/// is drawn, the palette's contrast ratios (R3 c), the type scale's sizes (R3 b), the proportional band of the HUD, and the readability check itself.
/// </summary>
public static class SkinTests
{
    private static int _failures;

    private sealed class FixedMeasure : IUiTextMeasure
    {
        public int MeasureWidth(string text, UiFont font) => text.Length * Math.Max(1, font.Size / 2);
        public int LineHeight(UiFont font) => font.Size + 4;
    }

    public static int Run(string[] args)
    {
        _ = args;
        _failures = 0;
        Test(TheOriginalSkinReturnsTheShippedLayoutItself);
        Test(SkinsParseByNameAndRefuseOthers);
        Test(TheModernDerivationKeepsEveryWindowAndRedrawsItsChrome);
        Test(BackdropsBecomeCanvasPicturesStay);
        Test(ACheckBoxReachesToItsRightNeighbour);
        Test(KeepArtChangesOnlyTheType);
        Test(ThePaletteMeetsTheContrastThresholds);
        Test(TheTypeScaleMeetsTheSmallestSizes);
        Test(ABandKeepsItsProportionsOnlyWhenAsked);
        Test(TheReadabilityCheckFindsWideAndTallLabels);
        Console.WriteLine(_failures == 0 ? "Skin tests passed." : $"Skin tests: {_failures} failure(s).");
        return _failures;
    }

    private static void Test(Action test)
    {
        try { test(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {test.Method.Name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static readonly WndTextColors White = new(WndColor.White, new WndColor(0, 0, 0, 255), new WndColor(90, 90, 90, 255), new WndColor(0, 0, 0, 255), new WndColor(255, 255, 0, 255), new WndColor(0, 0, 0, 255));

    private static ImmutableArray<WndDrawDataSet> Images(string image)
        => [new WndDrawDataSet(WndDrawDataKind.Enabled, [new WndDrawData(image, new WndColor(1, 2, 3, 255), new WndColor(4, 5, 6, 255)), .. Enumerable.Repeat(WndDrawData.Empty, 4), new WndDrawData(image + "Middle", WndColor.Undefined, WndColor.Undefined)])];

    private static WndWindow Window(WndWindowType type, string name, int left, int top, int right, int bottom, WndStatus status = WndStatus.Enabled,
        ImmutableArray<WndDrawDataSet>? drawData = null, WndFont? font = null, string? template = null, string? text = null, params WndWindow[] children)
        => new(type, type.ToString().ToUpperInvariant(), name, new WndScreenRect(left, top, right, bottom, 800, 600), status, WndStyle.None,
            new WndCallbacks(name.EndsWith("Parent", StringComparison.Ordinal) ? "TestSystem" : null, null, null, null), font ?? new WndFont("Arial", 10, false), template, -1, null, text,
            White, drawData ?? [new WndDrawDataSet(WndDrawDataKind.Enabled, [new WndDrawData(null, new WndColor(50, 50, 50, 128), new WndColor(200, 200, 200, 255))])], 0, 0,
            null, null, null, null, type == WndWindowType.StaticText ? new WndStaticTextData(false) : null, null, null, [.. children], 1);

    private static WndLayout Layout(params WndWindow[] windows)
        => new("Test.wnd", 2, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [.. windows], [], ImmutableSortedDictionary<string, int>.Empty);

    private static WndLayout Sample() => Layout(
        Window(WndWindowType.User, "Test.wnd:Parent", 0, 0, 800, 600, WndStatus.Enabled | WndStatus.Image, Images("Backdrop"), children:
        [
            Window(WndWindowType.PushButton, "Test.wnd:ButtonChrome", 100, 100, 260, 132, WndStatus.Enabled | WndStatus.Image, Images("Buttons-Left"), template: "Big", text: "GUI:Play"),
            Window(WndWindowType.PushButton, "Test.wnd:ButtonPicture", 300, 100, 316, 116, WndStatus.Enabled | WndStatus.Image,
                [new WndDrawDataSet(WndDrawDataKind.Enabled, [new WndDrawData("Dot", WndColor.Undefined, WndColor.Undefined)])]),
            Window(WndWindowType.User, "Test.wnd:Logo", 500, 20, 780, 110, WndStatus.Enabled | WndStatus.Image, Images("Logo")),
            Window(WndWindowType.StaticText, "Test.wnd:Title", 100, 20, 400, 56, font: new WndFont("Generals", 20, false), text: "Title"),
            Window(WndWindowType.CheckBox, "Test.wnd:Check", 100, 200, 250, 224, WndStatus.Enabled | WndStatus.Image, text: "A long check box label"),
            Window(WndWindowType.ComboBox, "Test.wnd:Neighbour", 400, 196, 500, 228),
        ]));

    private static readonly WndHeaderTemplateTable Templates = new(
        ImmutableSortedDictionary<string, WndHeaderTemplate>.Empty.WithComparers(StringComparer.Ordinal).Add("Big", new WndHeaderTemplate("Big", "Generals", 15, false)), []);

    private static void TheOriginalSkinReturnsTheShippedLayoutItself()
    {
        var layout = Sample();
        Check(ReferenceEquals(UiSkin.Original.Derive(layout, Templates), layout) && ReferenceEquals(UiSkin.Original.Derive(layout, Templates, keepArt: true), layout),
            "the original skin draws the shipped layout as it is");
        var options = UiScaleOptions.Original with { Mode = UiScaleMode.Uniform, TextScale = 1.25m };
        Check(UiSkin.Original.ShellScale(options) == options && UiSkin.Original.HudScale(options) == options, "and lays it out as before skins existed");
        Check(UiSkin.For(UiSkinName.Original) == UiSkin.Original && GameSettings.Default.Display.Skin == UiSkinName.Original, "the original is the default (D-21)");
    }

    private static void SkinsParseByNameAndRefuseOthers()
    {
        Check(UiSkin.Parse("modern") == UiSkin.Modern && UiSkin.Parse("Original") == UiSkin.Original && UiSkin.Modern.Id == "modern", "both names");
        try
        {
            UiSkin.Parse("dark");
            Check(false, "an unknown skin is refused");
        }
        catch (ArgumentException exception) { Check(exception.Message.Contains("dark", StringComparison.Ordinal), "with its name"); }
    }

    private static void TheModernDerivationKeepsEveryWindowAndRedrawsItsChrome()
    {
        var layout = Sample();
        var derived = UiSkin.Modern.Derive(layout, Templates);
        var before = layout.AllWindows().ToArray();
        var after = derived.AllWindows().ToArray();
        Check(before.Length == after.Length && before.Zip(after).All(pair => pair.First.Name == pair.Second.Name && pair.First.Type == pair.Second.Type
            && pair.First.Callbacks == pair.Second.Callbacks && pair.First.Children.Length == pair.Second.Children.Length), "every window keeps its name, type, callbacks and place in the tree");
        Check(after.All(window => window.HeaderTemplate is null && window.Font!.Name == "Segoe UI"), "every window has the skin's family and no header template");
        var button = after.Single(window => window.Name == "Test.wnd:ButtonChrome");
        Check((button.Status & WndStatus.Image) == 0 && button.Rect == before.Single(window => window.Name == "Test.wnd:ButtonChrome").Rect, "a three-piece button is drawn flat, in its place");
        Check(button.Font == new WndFont("Segoe UI", ModernLayout.Style.Points(UiTypeStep.Heading), true), $"its template size 15 is the heading step ({button.Font})");
        Check(button.GetDrawData(WndDrawDataKind.Enabled)[0] == new WndDrawData(null, ModernLayout.Palette.Control, ModernLayout.Palette.Border)
            && button.GetDrawData(WndDrawDataKind.Hilite)[0] == new WndDrawData(null, ModernLayout.Palette.Hover, ModernLayout.Palette.Accent)
            && button.GetDrawData(WndDrawDataKind.Enabled)[1] == new WndDrawData(null, ModernLayout.Palette.Selected, ModernLayout.Palette.Accent), "with the palette by state");
        Check(button.TextColors.Enabled == ModernLayout.Palette.Text && button.TextColors.EnabledBorder.IsUndefined && button.TextColors.Disabled == ModernLayout.Palette.Disabled,
            "its text in the text colour, without a drop shadow");
        var title = after.Single(window => window.Name == "Test.wnd:Title");
        Check(title.TextColors.Enabled == ModernLayout.Palette.Accent && title.Font!.Size == ModernLayout.Style.Points(UiTypeStep.Title), "a title is the accent at the title step");
        var picture = after.Single(window => window.Name == "Test.wnd:ButtonPicture");
        Check((picture.Status & WndStatus.Image) != 0 && picture.GetDrawData(WndDrawDataKind.Enabled)[0].Image == "Dot", "a button drawn as one picture keeps it");
    }

    private static void BackdropsBecomeCanvasPicturesStay()
    {
        var derived = UiSkin.Modern.Derive(Sample(), Templates).AllWindows().ToArray();
        var backdrop = derived.Single(window => window.Name == "Test.wnd:Parent");
        Check((backdrop.Status & WndStatus.Image) == 0 && backdrop.GetDrawData(WndDrawDataKind.Enabled)[0].Color == ModernLayout.Palette.Canvas, "a picture over the whole screen is the canvas");
        var logo = derived.Single(window => window.Name == "Test.wnd:Logo");
        Check((logo.Status & WndStatus.Image) != 0 && logo.GetDrawData(WndDrawDataKind.Enabled)[0].Image == "Logo", "a smaller picture stays the game's");
    }

    private static void ACheckBoxReachesToItsRightNeighbour()
    {
        var check = UiSkin.Modern.Derive(Sample(), Templates).AllWindows().Single(window => window.Name == "Test.wnd:Check");
        Check(check.Rect.Left == 100 && check.Rect.Right == 396 && check.Rect.Top == 200 && check.Rect.Bottom == 224, $"to 4 short of the combo box beside it ({check.Rect})");
        Check((check.Status & WndStatus.Image) == 0 && check.GetDrawData(WndDrawDataKind.Enabled)[2].BorderColor == ModernLayout.Palette.Accent, "with a drawn box, accented when checked");
    }

    private static void KeepArtChangesOnlyTheType()
    {
        var layout = Sample();
        var derived = UiSkin.Modern.Derive(layout, Templates, keepArt: true);
        Check(layout.AllWindows().Zip(derived.AllWindows()).All(pair => pair.First.Status == pair.Second.Status && pair.First.DrawData == pair.Second.DrawData
            && pair.First.TextColors == pair.Second.TextColors && pair.First.Rect == pair.Second.Rect), "pictures, colours and places stay");
        Check(derived.AllWindows().All(window => window.Font!.Name == "Segoe UI"), "the type is the skin's");
    }

    private static void ThePaletteMeetsTheContrastThresholds()
    {
        var p = ModernLayout.Palette;
        foreach (var (name, surface) in new[] { ("canvas", p.Canvas), ("panel", p.Panel), ("control", p.Control), ("hover", p.Hover), ("selected", p.Selected) })
            Check(ModernLayout.Contrast(p.Text, surface) >= 4.5, $"text on {name}: {ModernLayout.Contrast(p.Text, surface):F2}");
        foreach (var (name, surface) in new[] { ("canvas", p.Canvas), ("panel", p.Panel), ("control", p.Control) })
        {
            Check(ModernLayout.Contrast(p.Muted, surface) >= 4.5, $"muted on {name}: {ModernLayout.Contrast(p.Muted, surface):F2}");
            Check(ModernLayout.Contrast(p.Accent, surface) >= 4.5, $"accent on {name}: {ModernLayout.Contrast(p.Accent, surface):F2}");
            Check(ModernLayout.Contrast(p.Good, surface) >= 4.5 && ModernLayout.Contrast(p.Bad, surface) >= 4.5, $"good and bad on {name}");
        }
        Check(ModernLayout.Contrast(p.Disabled, p.Panel) >= 3 && ModernLayout.Contrast(p.Disabled, p.Control) >= 3, "disabled text at least 3:1");
        Check(Math.Abs(ModernLayout.Contrast(new WndColor(0, 0, 0, 255), WndColor.White) - 21) < 1e-9, "the measure runs from 1 to 21");
    }

    private static void TheTypeScaleMeetsTheSmallestSizes()
    {
        foreach (var (width, height, pixels) in new[] { (800, 600, 13), (1280, 720, 16), (1920, 1080, 24), (2560, 1080, 24) })
        {
            var caption = ModernLayout.Style.Font(UiTypeStep.Caption, new UiViewport(width, height));
            Check(UiFontPolicy.PixelSize(caption.Size) >= pixels, $"the caption at {width} x {height} is {UiFontPolicy.PixelSize(caption.Size)} px, at least {pixels}");
        }
        Check(ModernLayout.Style.Font(UiTypeStep.Body, new UiViewport(800, 600), 1.5m).Size == 15, "the text scale multiplies the size");
        Check(UiSkinStyle.StepOf(8) == UiTypeStep.Caption && UiSkinStyle.StepOf(10) == UiTypeStep.Body && UiSkinStyle.StepOf(12) == UiTypeStep.Control
            && UiSkinStyle.StepOf(14) == UiTypeStep.Heading && UiSkinStyle.StepOf(20) == UiTypeStep.Title, "the original's sizes fall in their steps");
        var steps = Enum.GetValues<UiTypeStep>().Select(ModernLayout.Style.Points).ToArray();
        Check(steps.Zip(steps.Skip(1)).All(pair => pair.First <= pair.Second), "the steps never get smaller");
    }

    private static void ABandKeepsItsProportionsOnlyWhenAsked()
    {
        var bar = new WndScreenRect(0, 416, 799, 599, 800, 600);
        var viewport = new UiViewport(1280, 720);
        var (stretched, _) = UiScaler.ForWindow(bar, null, viewport, UiScaleMode.Anchor);
        Check(stretched.Map(bar) is { X: 0, Width: >= 1278 }, $"the original stretches the band to the width ({stretched.Map(bar)})");
        var (kept, _) = UiScaler.ForWindow(bar, null, viewport, UiScaleMode.Anchor, keepBandAspect: true);
        var rect = kept.Map(bar);
        Check(rect.Width == 958 && rect.X == (1280 - 960) / 2 && rect.Bottom == 718, $"the modern HUD keeps it at the height's scale, centred at the bottom ({rect})");
        var canvas = new WndScreenRect(0, 0, 800, 600, 800, 600);
        Check(UiScaler.ForWindow(canvas, null, viewport, UiScaleMode.Anchor, keepBandAspect: true).Transform.Map(canvas) is { X: 0, Width: 1280, Height: 720 }, "a canvas still covers the screen");
    }

    private static void TheReadabilityCheckFindsWideAndTallLabels()
    {
        var layout = Layout(Window(WndWindowType.User, "Read.wnd:Parent", 0, 0, 800, 600, children:
        [
            Window(WndWindowType.PushButton, "Read.wnd:Wide", 10, 10, 60, 40, text: "Unbreakablewordthatislong"),
            Window(WndWindowType.StaticText, "Read.wnd:Tall", 100, 10, 160, 22, text: "one two three four five six"),
            Window(WndWindowType.PushButton, "Read.wnd:Fits", 200, 10, 400, 40, text: "Fine"),
        ]));
        var tree = new UiWindowTree(new UiViewport(800, 600), UiScaleOptions.Original, new UiTreeServices(new FixedMeasure()));
        tree.AddLayout(layout);
        var report = UiReadability.Audit(tree, tree.BuildDrawList());
        Check(report.TooWide == 1 && report.Overflows.Single(item => item.TooWide).Window == "Read.wnd:Wide", "a word wider than its button");
        Check(report.TooTall == 1 && report.Overflows.Single(item => !item.TooWide).Window == "Read.wnd:Tall", "a label wrapped taller than its window");
        Check(report.SmallestPixels == UiFontPolicy.PixelSize(10) && report.TextLines > 0, $"the smallest text in pixels ({report.SmallestPixels})");
    }
}
