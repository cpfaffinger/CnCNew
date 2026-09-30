using System.Collections.Immutable;

namespace CnCNew.Ui.Wnd;

/// <summary>Window types of <c>WINDOWTYPE</c> (<c>GameWindowManagerScript.cpp createWindow/createGadget</c>).</summary>
public enum WndWindowType
{
    /// <summary>A type name the original does not know; the original creates no window for it.</summary>
    Unknown,
    User,
    PushButton,
    RadioButton,
    CheckBox,
    VerticalSlider,
    HorizontalSlider,
    ScrollListBox,
    EntryField,
    StaticText,
    ProgressBar,
    TabControl,
    TabPane,
    ComboBox,
}

/// <summary>Bit values of <c>WIN_STATUS_*</c> (<c>GameWindow.h</c>); the name table is <c>WindowStatusNames</c>.</summary>
[Flags]
public enum WndStatus : uint
{
    None = 0,
    Active = 0x00000001,
    Toggle = 0x00000002,
    Draggable = 0x00000004,
    Enabled = 0x00000008,
    Hidden = 0x00000010,
    Above = 0x00000020,
    Below = 0x00000040,
    Image = 0x00000080,
    TabStop = 0x00000100,
    NoInput = 0x00000200,
    NoFocus = 0x00000400,
    Destroyed = 0x00000800,
    Border = 0x00001000,
    SmoothText = 0x00002000,
    OneLine = 0x00004000,
    NoFlush = 0x00008000,
    SeeThru = 0x00010000,
    RightClick = 0x00020000,
    WrapCentered = 0x00040000,
    CheckLike = 0x00080000,
    HotkeyText = 0x00100000,
    UseOverlayStates = 0x00200000,
    NotReady = 0x00400000,
    Flashing = 0x00800000,
    AlwaysColor = 0x01000000,
    OnMouseDown = 0x02000000,
}

/// <summary>Bit values of <c>GWS_*</c> (<c>Gadget.h</c>); the name table is <c>WindowStyleNames</c>.</summary>
[Flags]
public enum WndStyle : uint
{
    None = 0,
    PushButton = 0x00000001,
    RadioButton = 0x00000002,
    CheckBox = 0x00000004,
    VerticalSlider = 0x00000008,
    HorizontalSlider = 0x00000010,
    ScrollListBox = 0x00000020,
    EntryField = 0x00000040,
    StaticText = 0x00000080,
    ProgressBar = 0x00000100,
    User = 0x00000200,
    MouseTrack = 0x00000400,
    Animated = 0x00000800,
    TabStop = 0x00001000,
    TabControl = 0x00002000,
    TabPane = 0x00004000,
    ComboBox = 0x00008000,
}

/// <summary>
/// An RGBA color of a WND file. <c>255 255 255 0</c> is <c>GAME_COLOR_UNDEFINED</c> (<c>Color.h</c>), which the original
/// draw functions test before drawing.
/// </summary>
public readonly record struct WndColor(byte R, byte G, byte B, byte A)
{
    public static readonly WndColor Undefined = new(255, 255, 255, 0);
    public static readonly WndColor White = new(255, 255, 255, 255);
    public bool IsUndefined => this == Undefined;
    public bool IsVisible => A != 0;
    public override string ToString() => $"{R},{G},{B},{A}";
}

/// <summary>One <c>IMAGE: x, COLOR: r g b a, BORDERCOLOR: r g b a</c> triple; <see cref="Image"/> is null for <c>NoImage</c>.</summary>
public sealed record WndDrawData(string? Image, WndColor Color, WndColor BorderColor)
{
    public static readonly WndDrawData Empty = new(null, WndColor.Undefined, WndColor.Undefined);
}

/// <summary>The draw data keys of <c>gameWindowFieldList</c>; the gadget part keys fill the sub-windows of a gadget.</summary>
public enum WndDrawDataKind
{
    Enabled,
    Disabled,
    Hilite,
    ListBoxEnabledUpButton,
    ListBoxDisabledUpButton,
    ListBoxHiliteUpButton,
    ListBoxEnabledDownButton,
    ListBoxDisabledDownButton,
    ListBoxHiliteDownButton,
    ListBoxEnabledSlider,
    ListBoxDisabledSlider,
    ListBoxHiliteSlider,
    SliderThumbEnabled,
    SliderThumbDisabled,
    SliderThumbHilite,
    ComboBoxDropDownButtonEnabled,
    ComboBoxDropDownButtonDisabled,
    ComboBoxDropDownButtonHilite,
    ComboBoxEditBoxEnabled,
    ComboBoxEditBoxDisabled,
    ComboBoxEditBoxHilite,
    ComboBoxListBoxEnabled,
    ComboBoxListBoxDisabled,
    ComboBoxListBoxHilite,
}

public sealed record WndDrawDataSet(WndDrawDataKind Kind, ImmutableArray<WndDrawData> Entries)
{
    /// <summary><c>MAX_DRAW_DATA</c> of <c>WinInstanceData.h</c>.</summary>
    public const int MaxEntries = 9;

    public WndDrawData this[int index] => index < Entries.Length ? Entries[index] : WndDrawData.Empty;
}

/// <summary><c>TEXTCOLOR = ENABLED: .., ENABLEDBORDER: .., DISABLED: .., DISABLEDBORDER: .., HILITE: .., HILITEBORDER: ..</c>.</summary>
public sealed record WndTextColors(WndColor Enabled, WndColor EnabledBorder, WndColor Disabled, WndColor DisabledBorder, WndColor Hilite, WndColor HiliteBorder)
{
    public static readonly WndTextColors Undefined = new(WndColor.Undefined, WndColor.Undefined, WndColor.Undefined,
        WndColor.Undefined, WndColor.Undefined, WndColor.Undefined);
}

/// <summary><c>FONT = NAME: "Arial", SIZE: 8, BOLD: 1</c>.</summary>
public sealed record WndFont(string Name, int Size, bool Bold);

/// <summary>
/// <c>SCREENRECT</c> in absolute screen coordinates of the creation resolution. The original converts it to a position
/// relative to the parent only after scaling (<c>parseScreenRect</c>); this record keeps the authored values.
/// </summary>
public readonly record struct WndScreenRect(int Left, int Top, int Right, int Bottom, int CreationWidth, int CreationHeight)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsInsideCreationArea => Left >= 0 && Top >= 0 && Right <= CreationWidth && Bottom <= CreationHeight;
    public bool CoversCreationArea => Left <= 0 && Top <= 0 && Right >= CreationWidth && Bottom >= CreationHeight;
}

public sealed record WndListBoxData(int Length, bool AutoScroll, bool ScrollIfAtEnd, bool AutoPurge, bool ScrollBar,
    bool MultiSelect, int Columns, ImmutableArray<int> ColumnWidthPercentages, bool ForceSelect);

public sealed record WndComboBoxData(bool IsEditable, int MaxChars, int MaxDisplay, bool AsciiOnly, bool LettersAndNumbersOnly);

public sealed record WndSliderData(int MinValue, int MaxValue);

public sealed record WndRadioButtonData(int Group);

public sealed record WndStaticTextData(bool Centered);

public sealed record WndTextEntryData(int MaxLength, bool SecretText, bool NumericalOnly, bool AlphaNumericalOnly, bool AsciiOnly);

public sealed record WndTabControlData(int TabOrientation, int TabEdge, int TabWidth, int TabHeight, int TabCount, int PaneBorder,
    ImmutableArray<bool> PaneDisabled);

/// <summary>Callback names of one window; <c>null</c> stands for the <c>[None]</c> sentinel (any case).</summary>
public sealed record WndCallbacks(string? System, string? Input, string? Tooltip, string? Draw)
{
    public static readonly WndCallbacks None = new(null, null, null, null);
}

/// <summary>One <c>WINDOW ... END</c> block with its <c>CHILD</c> windows in file order (file order is bottom-to-top z-order).</summary>
public sealed record WndWindow(
    WndWindowType Type,
    string TypeName,
    string? Name,
    WndScreenRect Rect,
    WndStatus Status,
    WndStyle Style,
    WndCallbacks Callbacks,
    WndFont? Font,
    string? HeaderTemplate,
    int TooltipDelay,
    string? TooltipText,
    string? Text,
    WndTextColors TextColors,
    ImmutableArray<WndDrawDataSet> DrawData,
    int ImageOffsetX,
    int ImageOffsetY,
    WndListBoxData? ListBox,
    WndComboBoxData? ComboBox,
    WndSliderData? Slider,
    WndRadioButtonData? RadioButton,
    WndStaticTextData? StaticText,
    WndTextEntryData? TextEntry,
    WndTabControlData? TabControl,
    ImmutableArray<WndWindow> Children,
    int Line)
{
    public WndDrawDataSet GetDrawData(WndDrawDataKind kind)
    {
        foreach (var set in DrawData)
            if (set.Kind == kind) return set;
        return new WndDrawDataSet(kind, []);
    }

    public IEnumerable<WndWindow> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var window in child.DescendantsAndSelf())
                yield return window;
    }
}

/// <summary><c>STARTLAYOUTBLOCK</c> callbacks; <c>null</c> for <c>[None]</c>.</summary>
public sealed record WndLayoutCallbacks(string? Init, string? Update, string? Shutdown);

/// <summary>File-level default colors (<c>ENABLEDCOLOR</c>, ...) and <c>FONT</c> of <c>winCreateFromScript</c>.</summary>
public sealed record WndDefaults(
    WndColor? EnabledColor, WndColor? DisabledColor, WndColor? HiliteColor, WndColor? SelectedColor,
    WndColor? TextColor, WndColor? BackgroundColor, string? Font)
{
    public static readonly WndDefaults None = new(null, null, null, null, null, null, null);
}

public enum WndDiagnosticSeverity
{
    /// <summary>A documented quirk of the shipped files; the parser reproduces the original behaviour.</summary>
    Compatibility,
    /// <summary>A key, type or flag the original does not know.</summary>
    Unknown,
    /// <summary>Malformed input.</summary>
    Error,
}

public sealed record WndDiagnostic(string Code, WndDiagnosticSeverity Severity, string File, int Line, string Message)
{
    public bool IsExplained => Severity == WndDiagnosticSeverity.Compatibility;
    public override string ToString() => $"{File}({Line}): {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

/// <summary>A parsed <c>.wnd</c> file.</summary>
public sealed record WndLayout(
    string FileName,
    int FileVersion,
    WndLayoutCallbacks Callbacks,
    WndDefaults Defaults,
    ImmutableArray<WndWindow> Windows,
    ImmutableArray<WndDiagnostic> Diagnostics,
    ImmutableSortedDictionary<string, int> KeyCounts)
{
    public IEnumerable<WndWindow> AllWindows() => Windows.SelectMany(window => window.DescendantsAndSelf());
}
