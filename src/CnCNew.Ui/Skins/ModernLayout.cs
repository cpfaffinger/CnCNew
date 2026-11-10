using System.Collections.Immutable;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Skins;

/// <summary>
/// The modern skin's derivation of a shipped layout (docs/UI-MODERN.md sections 2, 4 to 6). Every window keeps its name, place in the tree, rectangle, status
/// apart from the picture bit, callbacks and gadget data, so the screens drive it as they drive the original; what changes is how it is drawn:
/// <list type="bullet">
/// <item>the type: the family of the skin, the size of the step the original's size falls in, no header template;</item>
/// <item>the chrome of every gadget: drawn by <c>UiDrawBuilder.Flat.cs</c> from the palette by state instead of the original's images (a push button drawn as one picture
/// without a label - a start position marker, a ready dot - keeps it);</item>
/// <item>a plain window whose picture covers the whole creation area is a backdrop and becomes the translucent <see cref="Scrim"/>; a smaller picture (a logo, a cameo, a
/// portrait) stays; a plain window with a fill is a translucent panel, or a well inside a panel (docs/UI-MODERN.md section 16);</item>
/// <item>the text colours: the text colour (a title the accent), the disabled colour, no drop shadow.</item>
/// </list>
/// With <c>keepArt</c> only the type changes (the command bar, whose art the skin keeps).
/// </summary>
public static class ModernLayout
{
    /// <summary>What a full-screen backdrop becomes: the battle behind the menus shows through it, and without one it lies on the black of the cleared picture.</summary>
    public static readonly WndColor Scrim = new(15, 20, 27, 96);

    /// <summary>A group inside a panel.</summary>
    public static readonly WndColor Well = new(6, 9, 13, 110);

    /// <summary>Every second row of a table. Blending is linear, so a little of a light colour goes a long way.</summary>
    public static readonly WndColor Row = new(60, 74, 94, 40);

    public static readonly UiSkinPalette Palette = new(
        Canvas: new(15, 20, 27, 255),
        Panel: new(24, 32, 42, 214),
        Control: new(36, 48, 65, 235),
        Hover: new(47, 64, 88, 245),
        Selected: new(45, 79, 124, 245),
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
        => layout with { Windows = [.. layout.Windows.Select(window => Window(window, null, false, true, templates, style, keepArt))] };

    /// <summary>The groups of every screen but the main menu stay together in the middle of a wide window; the main menu has its column at the edge, and only its row of medals in the middle.</summary>
    private static bool StaysCentered(WndWindow window)
        => window.Name is not { } name || !name.StartsWith("MainMenu.wnd:", StringComparison.Ordinal) || name.StartsWith("MainMenu.wnd:WinFaction", StringComparison.Ordinal) || name == "MainMenu.wnd:WinGrowMarker";

    private static WndWindow Window(WndWindow window, WndWindow? parent, bool inPanel, bool groupRoot, WndHeaderTemplateTable templates, UiSkinStyle style, bool keepArt)
    {
        var drawn = (window.Status & (WndStatus.Image | WndStatus.SeeThru)) == 0;
        var isPanel = !keepArt && window.Type == WndWindowType.User && drawn && !Covers(window) && window.GetDrawData(WndDrawDataKind.Enabled)[0].Color.IsVisible;
        var children = window.Children.Select(child => Window(child, window, inPanel || isPanel, groupRoot && Covers(window), templates, style, keepArt)).ToImmutableArray();
        var (_, size, bold) = OriginalFont(window, templates);
        var step = UiSkinStyle.StepOf(size);
        var font = new WndFont(style.FontFamily, style.Points(step), bold || UiSkinStyle.IsBold(step));
        var derived = window with { Font = font, HeaderTemplate = null, Children = children };
        if (keepArt) return derived;
        derived = derived with { Rect = window.KeepRect ? window.Rect : ModernScreens.Place(window), GroupCentered = groupRoot && !Covers(window) && StaysCentered(window) };
        var palette = style.Palette;
        derived = derived with { TextColors = TextColors(window, step, palette) };
        return window.Type switch
        {
            WndWindowType.User => UserWindow(derived, parent, palette, inPanel),
            WndWindowType.PushButton when IsPicture(window) => derived,
            WndWindowType.PushButton or WndWindowType.CheckBox or WndWindowType.RadioButton or WndWindowType.EntryField or WndWindowType.ScrollListBox
                or WndWindowType.ComboBox or WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider or WndWindowType.ProgressBar
                => derived with { Flat = WndFlatRole.Gadget, Status = derived.Status & ~WndStatus.Image, DrawData = Chrome(window, palette), Rect = window.Type == WndWindowType.CheckBox ? CheckBoxRoom(window, parent) : derived.Rect },
            WndWindowType.TabControl => derived with { Status = derived.Status & ~WndStatus.Image, DrawData = Chrome(window, palette) },
            WndWindowType.StaticText => derived with
            {
                Flat = WndFlatRole.Gadget,
                Status = derived.Status & ~(WndStatus.Image | (ModernScreens.StartsLeft(window.Name) ? WndStatus.WrapCentered : 0)),
                StaticText = ModernScreens.StartsLeft(window.Name) ? new WndStaticTextData(false) : derived.StaticText,
                DrawData = Label(window),
            },
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

    /// <summary>A push button the original draws as one picture (its enabled set has no middle piece) and no label: an icon, not chrome.</summary>
    private static bool IsPicture(WndWindow window)
        => (window.Status & WndStatus.Image) != 0 && window.GetDrawData(WndDrawDataKind.Enabled)[5].Image is null && string.IsNullOrEmpty(window.Text);

    private static bool Covers(WndWindow window) => window.Rect.CoversCreationArea || (window.Rect.Left <= 0 && window.Rect.Top <= 0
        && window.Rect.Right >= window.Rect.CreationWidth - 1 && window.Rect.Bottom >= window.Rect.CreationHeight - 1);

    private static WndWindow UserWindow(WndWindow window, WndWindow? parent, UiSkinPalette palette, bool inPanel)
    {
        // The marker of a winning row, placed by the score screen itself: a bar in the skin's good colour (the shipped window has no picture to draw).
        if (window.KeepRect && window.Name is { } marker && marker.StartsWith("ScoreScreen.wnd:GameWindowWinner", StringComparison.Ordinal))
            return window with { Flat = WndFlatRole.Gadget, Status = window.Status & ~WndStatus.Image, DrawData = Families(_ => [new WndDrawData(null, palette.Good, WndColor.Undefined)]) };
        if ((window.Status & WndStatus.Image) != 0)
        {
            // The general's window: one picture is all its chrome, frame and all. The skin has a panel for that.
            if (window.Name == "GeneralsExpPoints.wnd:GenExpParent")
                return window with { Flat = WndFlatRole.Panel, Status = window.Status & ~WndStatus.Image, DrawData = Families(_ => [new WndDrawData(null, palette.Panel with { A = 245 }, palette.Border)]) };
            if (!Covers(window)) return window;
            return window with { Flat = WndFlatRole.Gadget, Status = window.Status & ~WndStatus.Image, DrawData = Families(_ => [new WndDrawData(null, Scrim, WndColor.Undefined)]) };
        }
        var entry = window.GetDrawData(WndDrawDataKind.Enabled)[0];
        var border = entry.BorderColor.IsVisible ? palette.Border : WndColor.Undefined;
        // The world map of the Generals' Challenge is what the medals stand on: the backdrop's picture moves into the frame of the screen, translucent.
        if (window.Name == "ChallengeMenu.wnd:Frame" && parent?.GetDrawData(WndDrawDataKind.Enabled)[0].Image is { } map)
            return window with { Flat = WndFlatRole.Picture, DrawData = Families(_ => [new WndDrawData(map, palette.Panel with { A = 150 }, palette.Border)]) };
        if (Covers(window)) return window with { Flat = WndFlatRole.Gadget, DrawData = Families(_ => [new WndDrawData(null, entry.Color.IsVisible ? Scrim : WndColor.Undefined, WndColor.Undefined)]) };
        // A line of a layout is a window of no height with a fill or a border: it stays a line.
        if (window.Rect.Height <= 2 || window.Rect.Width <= 2) return window with { Flat = WndFlatRole.Gadget, DrawData = Families(_ => [new WndDrawData(null, palette.Border, WndColor.Undefined)]) };
        if (!entry.Color.IsVisible) return window with { Flat = WndFlatRole.Gadget, DrawData = Families(_ => [new WndDrawData(null, WndColor.Undefined, inPanel ? WndColor.Undefined : border)]) };
        return inPanel
            ? window with { Flat = WndFlatRole.Well, DrawData = Families(_ => [new WndDrawData(null, Well, WndColor.Undefined)]) }
            : window with { Flat = WndFlatRole.Panel, DrawData = Families(_ => [new WndDrawData(null, ModernScreens.IsDialog(window.Name) ? palette.Panel with { A = 245 } : palette.Panel, border)]) };
    }

    /// <summary>A label has no plate of its own in the modern skin: the panel behind it is the plate. The cells of a table are the exception: every second row of them is a shade lighter.</summary>
    private static ImmutableArray<WndDrawDataSet> Label(WndWindow window)
    {
        var fill = ModernScreens.ScoreRow(window.Name) is >= 0 and var row && row % 2 == 0 ? Row : WndColor.Undefined;
        return Families(_ => [new WndDrawData(null, fill, WndColor.Undefined)]);
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
    private static ImmutableArray<WndDrawDataSet> Chrome(WndWindow window, UiSkinPalette p)
    {
        var type = window.Type;
        var none = WndColor.Undefined;
        WndDrawData Normal(int state) => state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Hover, p.Accent), _ => D(p.Control, p.Border) };
        WndDrawData Pressed(int state) => state == 1 ? D(p.Panel, p.Border) : D(p.Selected, p.Accent);
        var empty = WndDrawData.Empty;
        return type switch
        {
            WndWindowType.PushButton => Families(state => [Normal(state), Pressed(state)]),
            WndWindowType.CheckBox => Families(state => [empty, Normal(state), state == 1 ? D(p.Panel, p.Border) : D(p.Selected, p.Accent)]),
            WndWindowType.RadioButton => Families(state => [Normal(state), empty, empty, Pressed(state)]),
            WndWindowType.EntryField => Families(state => [state switch { 1 => D(p.Panel, p.Border), 2 => D(p.Control, p.Accent), _ => D(p.Control, p.Border) }]),
            WndWindowType.ScrollListBox =>
            [
                // Entry 0: the list; 1: a selected row (fill) and its bar (border); 2: every second row (fill) and the rule between columns (border).
                .. Families(state => [D(new(6, 9, 13, 150), state == 2 ? p.Accent : p.Border), D(p.Selected, p.Accent), D(TextList(window) ? WndColor.Undefined : Row, p.Border)]),
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
                // Entry 0: the list; 1: the selected row; 2: its bar (fill) and a row under the pointer (border).
                .. Families(_ => [D(p.Panel with { A = 255 }, p.Border), D(p.Selected, none), D(p.Accent, p.Hover)], WndDrawDataKind.ComboBoxListBoxEnabled),
            ],
            WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider =>
            [
                .. Families(state => [Normal(state)]),
                .. Families(state => [state switch { 1 => D(p.Disabled, p.Border), 2 => D(p.Accent, p.Text), _ => D(p.Accent, p.Accent) }, D(p.Text, p.Accent)], WndDrawDataKind.SliderThumbEnabled),
            ],
            WndWindowType.ProgressBar => Families(state => [D(p.Control, p.Border), empty, empty, empty, D(state == 1 ? p.Disabled : p.Accent, none)]),
            WndWindowType.TabControl => Families(state => [Normal(state)]),
            _ => [],
        };
    }

    /// <summary>A colour of the game (a player's) as text on the skin's dark surfaces: lifted towards white until it reads, keeping its hue; a light colour stays as it is.</summary>
    public static WndColor Readable(WndColor color)
    {
        var surface = Palette.Panel with { A = 255 };
        for (var step = 0; step < 10 && Contrast(color, surface) < 4.5; step++)
            color = new WndColor((byte)(color.R + (255 - color.R) / 3), (byte)(color.G + (255 - color.G) / 3), (byte)(color.B + (255 - color.B) / 3), color.A);
        return color;
    }

    /// <summary>A list of running text, whose wrapped lines are rows: it has no stripes.</summary>
    private static bool TextList(WndWindow window) => window.Name is "Diplomacy.wnd:ListboxSolo" or "ScoreScreen.wnd:ListboxWarschoolAdvice";

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
