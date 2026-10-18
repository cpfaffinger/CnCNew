using System.Collections.Immutable;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Skins;

/// <summary>
/// The modern skin's derivation of a shipped layout (docs/UI-MODERN.md sections 2, 4 to 6). Every window keeps its name, place in the tree, rectangle, status
/// apart from the picture bit, callbacks and gadget data, so the screens drive it as they drive the original; what changes is how it is drawn:
/// <list type="bullet">
/// <item>the type: the family of the skin, the size of the step the original's size falls in, no header template;</item>
/// <item>the chrome of every gadget: flat rectangles of the palette by state instead of the original's images (a push button drawn as one picture - a start
/// position marker, a ready dot - keeps it);</item>
/// <item>a plain window whose picture covers the whole creation area is a backdrop and becomes the canvas colour; a smaller picture (a logo, a cameo, a portrait)
/// stays; the fill and border of a plain window without a picture take the panel and border colours, keeping the original's opacity;</item>
/// <item>the text colours: the text colour (a title the accent), the disabled colour, no drop shadow.</item>
/// </list>
/// With <c>keepArt</c> only the type changes (the command bar, whose art the skin keeps).
/// </summary>
public static class ModernLayout
{
    public static readonly UiSkinPalette Palette = new(
        Canvas: new(15, 20, 27, 255),
        Panel: new(24, 32, 42, 255),
        Control: new(36, 48, 65, 255),
        Hover: new(47, 64, 88, 255),
        Selected: new(45, 79, 124, 255),
        Border: new(60, 74, 94, 255),
        Accent: new(242, 179, 61, 255),
        Text: new(238, 241, 245, 255),
        Muted: new(169, 180, 194, 255),
        Disabled: new(118, 130, 146, 255),
        Good: new(126, 220, 138, 255),
        Bad: new(255, 122, 102, 255),
        Shadow: WndColor.Undefined);

    /// <summary>
    /// Segoe UI, Windows' own interface family; the type scale caption 10, body 10, control 11, heading 12, title 17 points at 600 lines. The first scale of the design
    /// (10, 11, 12, 15, 20) overflowed the original's windows 33 times in the readability check: a line of Segoe UI is about 1.19 times as high as one of Arial at the same
    /// size, and the windows were made for Arial (docs/UI-MODERN.md section 10).
    /// </summary>
    public static readonly UiSkinStyle Style = new("Segoe UI", Palette, [10, 10, 11, 12, 17]);

    public static WndLayout Derive(WndLayout layout, WndHeaderTemplateTable templates, UiSkinStyle style, bool keepArt)
        => layout with { Windows = [.. layout.Windows.Select(window => Window(window, null, templates, style, keepArt))] };

    private static WndWindow Window(WndWindow window, WndWindow? parent, WndHeaderTemplateTable templates, UiSkinStyle style, bool keepArt)
    {
        var children = window.Children.Select(child => Window(child, window, templates, style, keepArt)).ToImmutableArray();
        var (_, size, bold) = OriginalFont(window, templates);
        var step = UiSkinStyle.StepOf(size);
        var font = new WndFont(style.FontFamily, style.Points(step), bold || UiSkinStyle.IsBold(step));
        var derived = window with { Font = font, HeaderTemplate = null, Children = children };
        if (keepArt) return derived;
        var palette = style.Palette;
        derived = derived with { TextColors = TextColors(window, step, palette) };
        return window.Type switch
        {
            WndWindowType.User => UserWindow(derived, palette),
            WndWindowType.PushButton when IsPicture(window) => derived,
            WndWindowType.PushButton or WndWindowType.CheckBox or WndWindowType.RadioButton or WndWindowType.EntryField or WndWindowType.ScrollListBox
                or WndWindowType.ComboBox or WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider or WndWindowType.ProgressBar or WndWindowType.TabControl
                => derived with { Status = derived.Status & ~WndStatus.Image, DrawData = Chrome(window.Type, palette), Rect = window.Type == WndWindowType.CheckBox ? CheckBoxRoom(window, parent) : window.Rect },
            WndWindowType.StaticText => derived with { Status = derived.Status & ~WndStatus.Image, DrawData = Plain(window, palette, canvas: false) },
            _ => derived,
        };
    }

    /// <summary>
    /// The room of a check box's label: the original draws it unclipped beyond the window when it is longer, and with the skin's wider type it is longer more often. The
    /// window reaches to the right up to the next window beside it (one that overlaps it vertically and begins right of it) or its parent's right edge, 4 units short of
    /// either; it never shrinks.
    /// </summary>
    private static WndScreenRect CheckBoxRoom(WndWindow window, WndWindow? parent)
    {
        var rect = window.Rect;
        if (parent is null) return rect;
        var right = parent.Rect.Right;
        foreach (var sibling in parent.Children)
            if (!ReferenceEquals(sibling, window) && (sibling.Status & WndStatus.Hidden) == 0 && sibling.Rect.Left >= rect.Right && sibling.Rect.Top < rect.Bottom && sibling.Rect.Bottom > rect.Top)
                right = Math.Min(right, sibling.Rect.Left);
        return rect with { Right = Math.Max(rect.Right, right - 4) };
    }

    /// <summary>The font the original draws a window with: a known header template replaces the window's <c>FONT</c>; a window with neither takes the tree's fallback.</summary>
    public static (string Name, int Size, bool Bold) OriginalFont(WndWindow window, WndHeaderTemplateTable templates)
    {
        if (templates.Find(window.HeaderTemplate) is { } template) return (template.FontName, template.Point, template.Bold);
        var font = window.Font ?? UiWindowTree.FallbackFont;
        return (font.Name, font.Size, font.Bold);
    }

    /// <summary>A push button the original draws as one picture (its enabled set has no middle piece): an icon, not chrome.</summary>
    private static bool IsPicture(WndWindow window)
        => (window.Status & WndStatus.Image) != 0 && window.GetDrawData(WndDrawDataKind.Enabled)[5].Image is null;

    private static WndWindow UserWindow(WndWindow window, UiSkinPalette palette)
    {
        var covers = window.Rect.CoversCreationArea || (window.Rect.Left <= 0 && window.Rect.Top <= 0
            && window.Rect.Right >= window.Rect.CreationWidth - 1 && window.Rect.Bottom >= window.Rect.CreationHeight - 1);
        if ((window.Status & WndStatus.Image) != 0)
        {
            if (!covers) return window;
            return window with { Status = window.Status & ~WndStatus.Image, DrawData = Families(_ => [new WndDrawData(null, palette.Canvas, WndColor.Undefined)]) };
        }
        return window with { DrawData = Plain(window, palette, covers) };
    }

    /// <summary>The fill and border of a window without a picture, in the palette's panel (or canvas) and border colours at the original's opacity.</summary>
    private static ImmutableArray<WndDrawDataSet> Plain(WndWindow window, UiSkinPalette palette, bool canvas)
    {
        var entry = window.GetDrawData(WndDrawDataKind.Enabled)[0];
        var fill = entry.Color.IsVisible ? (canvas ? palette.Canvas : palette.Panel) with { A = entry.Color.A } : WndColor.Undefined;
        var border = entry.BorderColor.IsVisible ? palette.Border with { A = entry.BorderColor.A } : WndColor.Undefined;
        return Families(_ => [new WndDrawData(null, fill, border)]);
    }

    private static WndTextColors TextColors(WndWindow window, UiTypeStep step, UiSkinPalette palette)
    {
        var original = window.TextColors;
        var text = step == UiTypeStep.Title ? palette.Accent : palette.Text;
        // An entry field and a combo box fill a selection with the hilite colour (W3DGadgetTextEntryDraw); every other gadget writes its hilited text in it.
        var hilite = window.Type is WndWindowType.EntryField or WndWindowType.ComboBox ? palette.Selected : text;
        static WndColor Keep(WndColor authored, WndColor replacement) => authored.IsVisible ? replacement : authored;
        return new WndTextColors(Keep(original.Enabled, text), WndColor.Undefined, Keep(original.Disabled, palette.Disabled), WndColor.Undefined,
            Keep(original.Hilite, hilite), WndColor.Undefined);
    }

    private static WndDrawData D(WndColor fill, WndColor border) => new(null, fill, border);

    /// <summary>One entry list per state family (enabled, disabled, hilite) of the given kinds.</summary>
    private static ImmutableArray<WndDrawDataSet> Families(Func<int, WndDrawData[]> entries, WndDrawDataKind first = WndDrawDataKind.Enabled)
        => [new WndDrawDataSet(first, [.. entries(0)]), new WndDrawDataSet(first + 1, [.. entries(1)]), new WndDrawDataSet(first + 2, [.. entries(2)])];

    /// <summary>
    /// The chrome of a gadget by state: index 0 of a family is the enabled state, 1 the disabled, 2 the hilited (<see cref="WndDrawDataKind"/> lists every family
    /// in that order). The entries are those the gadget draw functions read without pictures (<c>UiWindowTree.DrawBuilder</c>).
    /// </summary>
    private static ImmutableArray<WndDrawDataSet> Chrome(WndWindowType type, UiSkinPalette p)
    {
        var none = WndColor.Undefined;
        WndDrawData Normal(int state) => state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Hover, p.Accent), _ => D(p.Control, p.Border) };
        WndDrawData Pressed(int state) => state == 1 ? D(p.Panel, p.Border) : D(p.Selected, p.Accent);
        var empty = WndDrawData.Empty;
        return type switch
        {
            WndWindowType.PushButton => Families(state => [Normal(state), Pressed(state)]),
            WndWindowType.CheckBox => Families(state => [empty, Normal(state), Pressed(state)]),
            WndWindowType.RadioButton => Families(state => [Normal(state), empty, empty, Pressed(state)]),
            WndWindowType.EntryField => Families(state => [state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Control, p.Accent), _ => D(p.Control, p.Border) }]),
            WndWindowType.ScrollListBox =>
            [
                .. Families(state => [D(p.Panel, state == 2 ? p.Accent : p.Border), D(p.Selected, none)]),
                .. Families(state => [Normal(state)], WndDrawDataKind.ListBoxEnabledUpButton),
                .. Families(state => [Normal(state)], WndDrawDataKind.ListBoxEnabledDownButton),
                .. Families(_ => [D(p.Canvas, none)], WndDrawDataKind.ListBoxEnabledSlider),
                .. Families(state => [state == 1 ? D(p.Control, p.Border) : D(p.Hover, state == 2 ? p.Accent : p.Border)], WndDrawDataKind.SliderThumbEnabled),
            ],
            WndWindowType.ComboBox =>
            [
                .. Families(_ => [empty]),
                .. Families(state => [state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Control, p.Accent), _ => D(p.Control, p.Border) }], WndDrawDataKind.ComboBoxEditBoxEnabled),
                .. Families(state => [state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Selected, p.Accent), _ => D(p.Hover, p.Border) }], WndDrawDataKind.ComboBoxDropDownButtonEnabled),
                .. Families(_ => [D(p.Panel, p.Border), D(p.Selected, none)], WndDrawDataKind.ComboBoxListBoxEnabled),
            ],
            WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider =>
            [
                .. Families(state => [Normal(state)]),
                .. Families(state => [state == 1 ? D(p.Disabled, p.Border) : D(p.Accent, p.Accent), D(p.Text, p.Accent)], WndDrawDataKind.SliderThumbEnabled),
            ],
            WndWindowType.ProgressBar => Families(state => [D(p.Control, p.Border), empty, empty, empty, D(state == 1 ? p.Disabled : p.Accent, none)]),
            WndWindowType.TabControl => Families(state => [Normal(state)]),
            _ => [],
        };
    }

    /// <summary>The contrast ratio of two opaque colours by WCAG 2 relative luminance (1 to 21).</summary>
    public static double Contrast(WndColor a, WndColor b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        static double Luminance(WndColor color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
