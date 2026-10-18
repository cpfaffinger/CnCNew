using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Skins;

/// <summary>The five steps of a skin's type scale (docs/UI-MODERN.md section 4).</summary>
public enum UiTypeStep
{
    Caption,
    Body,
    Control,
    Heading,
    Title,
}

/// <summary>The colours of a skin (docs/UI-MODERN.md section 5). <see cref="Shadow"/> is the drop colour of text; undefined draws none.</summary>
public sealed record UiSkinPalette(
    WndColor Canvas,
    WndColor Panel,
    WndColor Control,
    WndColor Hover,
    WndColor Selected,
    WndColor Border,
    WndColor Accent,
    WndColor Text,
    WndColor Muted,
    WndColor Disabled,
    WndColor Good,
    WndColor Bad,
    WndColor Shadow);

/// <summary>
/// What a screen the game draws itself (the analysis pages, the observer HUD, the network indicators) takes from its skin: the palette, the font family and the
/// type scale in points at the 600 lines of the original's creation height. <see cref="Font"/> scales a step to a viewport like a window font of the original's
/// layouts in the anchored mode (the smaller of width / 800 and height / 600).
/// </summary>
public sealed record UiSkinStyle(string FontFamily, UiSkinPalette Palette, ImmutableArray<int> StepPoints)
{
    public int Points(UiTypeStep step) => StepPoints[(int)step];

    public static bool IsBold(UiTypeStep step) => step >= UiTypeStep.Heading;

    /// <summary>The font of a step for a viewport, multiplied by the accessibility text scale.</summary>
    public UiFont Font(UiTypeStep step, UiViewport viewport, decimal textScale = 1m)
    {
        var (numerator, denominator) = UiScaler.UniformScale(viewport, UiScaler.DefaultDisplayWidth, UiScaler.DefaultDisplayHeight);
        var size = Math.Max(1, (int)Math.Floor(Points(step) * (decimal)numerator / denominator * textScale));
        return new UiFont(FontFamily, size, IsBold(step));
    }

    /// <summary>The step a size of the original falls in (docs/UI-MODERN.md section 4): up to 8 caption, 9 and 10 body, 11 and 12 control, 13 to 16 heading, more title.</summary>
    public static UiTypeStep StepOf(int originalPoints) => originalPoints switch
    {
        <= 8 => UiTypeStep.Caption,
        <= 10 => UiTypeStep.Body,
        <= 12 => UiTypeStep.Control,
        <= 16 => UiTypeStep.Heading,
        _ => UiTypeStep.Title,
    };
}

/// <summary>
/// A skin of the user interface (docs/UI-MODERN.md): how the shell and the HUD lay out the original's layouts, what a layout looks like when it is drawn, and the
/// style of the screens the game draws itself. A skin is a choice of the presentation only. <see cref="Original"/> is the default and changes nothing: its
/// <see cref="Derive"/> returns the shipped layout itself and its scale options are the ones each caller used before skins existed.
/// </summary>
public abstract class UiSkin
{
    public static UiSkin Original { get; } = new OriginalSkin();
    public static UiSkin Modern { get; } = new ModernSkin();

    public static UiSkin For(UiSkinName name) => name == UiSkinName.Modern ? Modern : Original;

    /// <summary>Parses <c>original</c> or <c>modern</c> (any case); anything else is an error, never a quiet default.</summary>
    public static UiSkin Parse(string text) => text.ToLowerInvariant() switch
    {
        "original" => Original,
        "modern" => Modern,
        _ => throw new ArgumentException($"Unknown skin '{text}' (original or modern)."),
    };

    public abstract UiSkinName Name { get; }

    /// <summary>The lower-case name (<c>original</c>, <c>modern</c>): the client's <c>--skin</c> value and the suffix of the modern baselines.</summary>
    public string Id => Name == UiSkinName.Modern ? "modern" : "original";

    public bool IsModern => Name == UiSkinName.Modern;

    /// <summary>The scale options of the shell's menus; <paramref name="original"/> is what the shell used before skins (uniform with borders).</summary>
    public abstract UiScaleOptions ShellScale(UiScaleOptions original);

    /// <summary>The scale options of the in-game HUD; <paramref name="original"/> is what the HUD derived from the settings before skins.</summary>
    public abstract UiScaleOptions HudScale(UiScaleOptions original);

    /// <summary>
    /// A layout as this skin draws it. <paramref name="keepArt"/>: the layout's pictures stay (the command bar, whose art the modern skin keeps, section 3); only
    /// its type and text colours change.
    /// </summary>
    public abstract WndLayout Derive(WndLayout layout, WndHeaderTemplateTable templates, bool keepArt = false);

    /// <summary>The style of the screens the game draws itself.</summary>
    public abstract UiSkinStyle Style { get; }

    public override string ToString() => Id;

    private sealed class OriginalSkin : UiSkin
    {
        public override UiSkinName Name => UiSkinName.Original;
        public override UiScaleOptions ShellScale(UiScaleOptions original) => original;
        public override UiScaleOptions HudScale(UiScaleOptions original) => original;
        public override WndLayout Derive(WndLayout layout, WndHeaderTemplateTable templates, bool keepArt = false) => layout;

        /// <summary>Stands for the original look of the self-drawn screens; each such screen keeps its own constants for the original skin.</summary>
        public override UiSkinStyle Style { get; } = new("Arial", new UiSkinPalette(
            Canvas: new(0, 0, 0, 255), Panel: new(0, 0, 0, 170), Control: new(30, 36, 46, 235), Hover: new(70, 92, 130, 245), Selected: new(70, 92, 130, 245),
            Border: new(96, 108, 124, 255), Accent: new(255, 208, 84, 255), Text: new(236, 238, 242, 255), Muted: new(160, 168, 180, 255),
            Disabled: new(118, 130, 146, 255), Good: new(120, 230, 120, 255), Bad: new(255, 110, 90, 255), Shadow: new(0, 0, 0, 255)), [9, 10, 11, 12, 18]);
    }

    private sealed class ModernSkin : UiSkin
    {
        public override UiSkinName Name => UiSkinName.Modern;

        public override UiScaleOptions ShellScale(UiScaleOptions original) => original with { Mode = CnCNew.Ui.Layout.UiScaleMode.Anchor };

        public override UiScaleOptions HudScale(UiScaleOptions original) => original with { Mode = CnCNew.Ui.Layout.UiScaleMode.Anchor, KeepBandAspect = true };

        public override WndLayout Derive(WndLayout layout, WndHeaderTemplateTable templates, bool keepArt = false)
            => ModernLayout.Derive(layout, templates, Style, keepArt);

        public override UiSkinStyle Style { get; } = ModernLayout.Style;
    }
}
