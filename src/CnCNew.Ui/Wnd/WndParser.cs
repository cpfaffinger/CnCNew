using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace CnCNew.Ui.Wnd;

/// <summary>
/// Parser for <c>.wnd</c> window layout files. The grammar follows <c>GameWindowManagerScript.cpp</c>
/// (<c>winCreateFromScript</c>, <c>parseLayoutBlock</c>, <c>parseWindow</c>, <c>parseChildWindows</c> and the
/// <c>gameWindowFieldList</c> parse functions): keys are whitespace-separated tokens, a value runs from the token
/// after the <c>=</c> up to the next <c>;</c>. Everything the original ignores or misreads is reported as a
/// <see cref="WndDiagnostic"/>; nothing is dropped silently.
/// </summary>
public static class WndParser
{
    /// <summary><c>WIN_STACK_DEPTH</c>: the original's parent stack overflows at this nesting depth.</summary>
    public const int MaximumNestingDepth = 9;

    private static readonly ImmutableDictionary<string, WndWindowType> TypeNames =
        new Dictionary<string, WndWindowType>(StringComparer.Ordinal)
        {
            ["USER"] = WndWindowType.User,
            ["PUSHBUTTON"] = WndWindowType.PushButton,
            ["RADIOBUTTON"] = WndWindowType.RadioButton,
            ["CHECKBOX"] = WndWindowType.CheckBox,
            ["VERTSLIDER"] = WndWindowType.VerticalSlider,
            ["HORZSLIDER"] = WndWindowType.HorizontalSlider,
            ["SCROLLLISTBOX"] = WndWindowType.ScrollListBox,
            ["ENTRYFIELD"] = WndWindowType.EntryField,
            ["STATICTEXT"] = WndWindowType.StaticText,
            ["PROGRESSBAR"] = WndWindowType.ProgressBar,
            ["TABCONTROL"] = WndWindowType.TabControl,
            ["TABPANE"] = WndWindowType.TabPane,
            ["COMBOBOX"] = WndWindowType.ComboBox,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    // Order of WindowStatusNames / WindowStyleNames in GameWindowManagerScript.cpp; the bit is 1 << index.
    private static readonly string[] StatusNames =
    [
        "ACTIVE", "TOGGLE", "DRAGABLE", "ENABLED", "HIDDEN", "ABOVE", "BELOW", "IMAGE", "TABSTOP", "NOINPUT",
        "NOFOCUS", "DESTROYED", "BORDER", "SMOOTH_TEXT", "ONE_LINE", "NO_FLUSH", "SEE_THRU", "RIGHT_CLICK",
        "WRAP_CENTERED", "CHECK_LIKE", "HOTKEY_TEXT", "USE_OVERLAY_STATES", "NOT_READY", "FLASHING", "ALWAYS_COLOR",
        "ON_MOUSE_DOWN",
    ];

    private static readonly string[] StyleNames =
    [
        "PUSHBUTTON", "RADIOBUTTON", "CHECKBOX", "VERTSLIDER", "HORZSLIDER", "SCROLLLISTBOX", "ENTRYFIELD",
        "STATICTEXT", "PROGRESSBAR", "USER", "MOUSETRACK", "ANIMATED", "TABSTOP", "TABCONTROL", "TABPANE", "COMBOBOX",
    ];

    private static readonly ImmutableDictionary<string, WndDrawDataKind> DrawDataKeys =
        new Dictionary<string, WndDrawDataKind>(StringComparer.Ordinal)
        {
            ["ENABLEDDRAWDATA"] = WndDrawDataKind.Enabled,
            ["DISABLEDDRAWDATA"] = WndDrawDataKind.Disabled,
            ["HILITEDRAWDATA"] = WndDrawDataKind.Hilite,
            ["LISTBOXENABLEDUPBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxEnabledUpButton,
            ["LISTBOXDISABLEDUPBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxDisabledUpButton,
            ["LISTBOXHILITEUPBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxHiliteUpButton,
            ["LISTBOXENABLEDDOWNBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxEnabledDownButton,
            ["LISTBOXDISABLEDDOWNBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxDisabledDownButton,
            ["LISTBOXHILITEDOWNBUTTONDRAWDATA"] = WndDrawDataKind.ListBoxHiliteDownButton,
            ["LISTBOXENABLEDSLIDERDRAWDATA"] = WndDrawDataKind.ListBoxEnabledSlider,
            ["LISTBOXDISABLEDSLIDERDRAWDATA"] = WndDrawDataKind.ListBoxDisabledSlider,
            ["LISTBOXHILITESLIDERDRAWDATA"] = WndDrawDataKind.ListBoxHiliteSlider,
            ["SLIDERTHUMBENABLEDDRAWDATA"] = WndDrawDataKind.SliderThumbEnabled,
            ["SLIDERTHUMBDISABLEDDRAWDATA"] = WndDrawDataKind.SliderThumbDisabled,
            ["SLIDERTHUMBHILITEDRAWDATA"] = WndDrawDataKind.SliderThumbHilite,
            ["COMBOBOXDROPDOWNBUTTONENABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxDropDownButtonEnabled,
            ["COMBOBOXDROPDOWNBUTTONDISABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxDropDownButtonDisabled,
            ["COMBOBOXDROPDOWNBUTTONHILITEDRAWDATA"] = WndDrawDataKind.ComboBoxDropDownButtonHilite,
            ["COMBOBOXEDITBOXENABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxEditBoxEnabled,
            ["COMBOBOXEDITBOXDISABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxEditBoxDisabled,
            ["COMBOBOXEDITBOXHILITEDRAWDATA"] = WndDrawDataKind.ComboBoxEditBoxHilite,
            ["COMBOBOXLISTBOXENABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxListBoxEnabled,
            ["COMBOBOXLISTBOXDISABLEDDRAWDATA"] = WndDrawDataKind.ComboBoxListBoxDisabled,
            ["COMBOBOXLISTBOXHILITEDRAWDATA"] = WndDrawDataKind.ComboBoxListBoxHilite,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<string> FieldKeys = ImmutableHashSet.Create(StringComparer.Ordinal,
        "NAME", "STATUS", "STYLE", "SYSTEMCALLBACK", "INPUTCALLBACK", "TOOLTIPCALLBACK", "DRAWCALLBACK", "FONT",
        "HEADERTEMPLATE", "LISTBOXDATA", "COMBOBOXDATA", "SLIDERDATA", "RADIOBUTTONDATA", "TOOLTIPTEXT", "TOOLTIPDELAY",
        "TEXT", "TEXTCOLOR", "STATICTEXTDATA", "TEXTENTRYDATA", "TABCONTROLDATA", "IMAGEOFFSET", "TOOLTIP")
        .Union(DrawDataKeys.Keys);

    private static readonly ImmutableHashSet<string> DefaultColorKeys = ImmutableHashSet.Create(StringComparer.Ordinal,
        "ENABLEDCOLOR", "DISABLEDCOLOR", "HILITECOLOR", "SELECTEDCOLOR", "TEXTCOLOR", "BACKGROUNDCOLOR");

    public static WndLayout Parse(ReadOnlySpan<byte> bytes, string fileName)
        => Parse(Encoding.Latin1.GetString(bytes), fileName);

    public static WndLayout Parse(Stream stream, string fileName)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), fileName);
    }

    public static WndLayout Parse(string text, string fileName)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return new FileParser(text, fileName).Run();
    }

    /// <summary><c>"[None]"</c> in any case is the original's empty callback/template sentinel.</summary>
    public static bool IsNoneSentinel(string? value)
        => value is null || value.Length == 0 || value.Equals("[None]", StringComparison.OrdinalIgnoreCase);

    public static WndStyle StyleOf(WndWindowType type) => type switch
    {
        WndWindowType.User => WndStyle.User,
        WndWindowType.PushButton => WndStyle.PushButton,
        WndWindowType.RadioButton => WndStyle.RadioButton,
        WndWindowType.CheckBox => WndStyle.CheckBox,
        WndWindowType.VerticalSlider => WndStyle.VerticalSlider,
        WndWindowType.HorizontalSlider => WndStyle.HorizontalSlider,
        WndWindowType.ScrollListBox => WndStyle.ScrollListBox,
        WndWindowType.EntryField => WndStyle.EntryField,
        WndWindowType.StaticText => WndStyle.StaticText,
        WndWindowType.ProgressBar => WndStyle.ProgressBar,
        WndWindowType.TabControl => WndStyle.TabControl,
        WndWindowType.TabPane => WndStyle.TabPane,
        WndWindowType.ComboBox => WndStyle.ComboBox,
        _ => WndStyle.None,
    };

    private sealed class FileParser(string text, string fileName)
    {
        private readonly List<WndDiagnostic> _diagnostics = [];
        private readonly SortedDictionary<string, int> _keyCounts = new(StringComparer.Ordinal);
        private int _position;
        private int _line = 1;

        public WndLayout Run()
        {
            var version = ParseFileVersion();
            var callbacks = new WndLayoutCallbacks(null, null, null);
            if (version >= 2) callbacks = ParseLayoutBlock();
            var defaults = WndDefaults.None;
            var windows = ImmutableArray.CreateBuilder<WndWindow>();
            while (TryReadToken(out var token, out var line))
            {
                Count(token);
                if (token == "END") continue; // winCreateFromScript skips stray END tokens
                if (token == "WINDOW")
                {
                    var window = ParseWindow(line, 0);
                    if (window is not null) windows.Add(window);
                }
                else if (DefaultColorKeys.Contains(token)) defaults = ParseDefaultColor(token, defaults, line);
                else if (token == "FONT")
                {
                    ExpectEquals(token, line);
                    defaults = defaults with { Font = ReadUntilSemicolon(line, token).Trim() };
                }
                else Report("top-level-token-ignored", WndDiagnosticSeverity.Unknown, line,
                    $"Token '{token}' outside a window; winCreateFromScript ignores it.");
            }
            return new WndLayout(fileName, version, callbacks, defaults, windows.ToImmutable(), [.._diagnostics],
                _keyCounts.ToImmutableSortedDictionary(StringComparer.Ordinal));
        }

        private int ParseFileVersion()
        {
            if (!TryReadToken(out var token, out var line))
            {
                Report("empty-file", WndDiagnosticSeverity.Error, 1, "File is empty.");
                return 0;
            }
            Count(token);
            var rest = token.StartsWith("FILE_VERSION=", StringComparison.Ordinal) ? token["FILE_VERSION=".Length..] : null;
            if (rest is null && token != "FILE_VERSION")
            {
                Report("missing-file-version", WndDiagnosticSeverity.Error, line, $"Expected FILE_VERSION, found '{token}'.");
                return 0;
            }
            var value = (rest ?? string.Empty) + ReadUntilSemicolon(line, token);
            var tokens = Split(value, " =");
            if (tokens.Length != 1 || !int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
            {
                Report("malformed-file-version", WndDiagnosticSeverity.Error, line, $"FILE_VERSION value '{value.Trim()}' is not an integer.");
                return 0;
            }
            return version;
        }

        private WndLayoutCallbacks ParseLayoutBlock()
        {
            if (!TryReadToken(out var token, out var line) || token != "STARTLAYOUTBLOCK")
            {
                Report("missing-layout-block", WndDiagnosticSeverity.Error, line,
                    "FILE_VERSION 2+ requires STARTLAYOUTBLOCK; winCreateFromScript rejects the file.");
                return new WndLayoutCallbacks(null, null, null);
            }
            Count(token);
            string? init = null, update = null, shutdown = null;
            while (true)
            {
                if (!TryReadToken(out token, out line))
                {
                    Report("unterminated-layout-block", WndDiagnosticSeverity.Error, line, "Missing ENDLAYOUTBLOCK.");
                    break;
                }
                Count(token);
                if (token == "ENDLAYOUTBLOCK") break;
                if (token is "LAYOUTINIT" or "LAYOUTUPDATE" or "LAYOUTSHUTDOWN")
                {
                    var tokens = Split(ReadUntilSemicolon(line, token), " =");
                    var name = tokens.Length > 0 && !IsNoneSentinel(tokens[0]) ? tokens[0] : null;
                    if (tokens.Length != 1)
                        Report("malformed-layout-callback", WndDiagnosticSeverity.Error, line, $"{token} expects exactly one name.");
                    if (token == "LAYOUTINIT") init = name;
                    else if (token == "LAYOUTUPDATE") update = name;
                    else shutdown = name;
                }
                else Report("layout-block-token-ignored", WndDiagnosticSeverity.Unknown, line,
                    $"Token '{token}' in the layout block; parseLayoutBlock ignores it.");
            }
            return new WndLayoutCallbacks(init, update, shutdown);
        }

        private WndDefaults ParseDefaultColor(string key, WndDefaults defaults, int line)
        {
            ExpectEquals(key, line);
            var value = ReadUntilSemicolon(line, key).Trim();
            WndColor color;
            if (value == "TRANSPARENT") color = WndColor.Undefined;
            else
            {
                var tokens = Split(value, " ");
                if (tokens.Length != 3 || !TryByte(tokens[0], out var r) || !TryByte(tokens[1], out var g) || !TryByte(tokens[2], out var b))
                {
                    Report("malformed-default-color", WndDiagnosticSeverity.Error, line, $"{key} value '{value}' is not 'r g b' or TRANSPARENT.");
                    return defaults;
                }
                color = new WndColor(r, g, b, 255);
            }
            return key switch
            {
                "ENABLEDCOLOR" => defaults with { EnabledColor = color },
                "DISABLEDCOLOR" => defaults with { DisabledColor = color },
                "HILITECOLOR" => defaults with { HiliteColor = color },
                "SELECTEDCOLOR" => defaults with { SelectedColor = color },
                "TEXTCOLOR" => defaults with { TextColor = color },
                _ => defaults with { BackgroundColor = color },
            };
        }

        private WndWindow? ParseWindow(int windowLine, int depth)
        {
            if (depth > MaximumNestingDepth)
                Report("nesting-too-deep", WndDiagnosticSeverity.Error, windowLine,
                    $"Window nesting depth {depth} exceeds WIN_STACK_DEPTH; the original parent stack overflows.");

            var typeStatement = ReadUntilSemicolon(windowLine, "WINDOWTYPE");
            var typeTokens = Split(typeStatement, " =");
            if (typeTokens.Length != 2 || typeTokens[0] != "WINDOWTYPE")
            {
                Report("missing-window-type", WndDiagnosticSeverity.Error, windowLine, $"Expected 'WINDOWTYPE = <type>', found '{typeStatement.Trim()}'.");
                SkipWindowBody();
                return null;
            }
            Count("WINDOWTYPE");
            var typeName = typeTokens[1];
            if (!TypeNames.TryGetValue(typeName, out var type))
            {
                type = WndWindowType.Unknown;
                Report("unknown-window-type", WndDiagnosticSeverity.Unknown, windowLine,
                    $"WINDOWTYPE '{typeName}' is unknown; createWindow creates no window for it.");
            }

            var rectLine = _line;
            var rectStatement = ReadUntilSemicolon(windowLine, "SCREENRECT");
            Count("SCREENRECT");
            var rect = ParseScreenRect(rectStatement, rectLine);

            var state = new WindowState(type, typeName, rect, windowLine);
            var childrenParsed = false;
            while (true)
            {
                if (!TryReadToken(out var token, out var line))
                {
                    Report("unterminated-window", WndDiagnosticSeverity.Error, windowLine, "Window block is missing END.");
                    break;
                }
                Count(token);
                if (token == "END") break;
                if (token == "CHILD")
                {
                    if (childrenParsed)
                        Report("repeated-child-block", WndDiagnosticSeverity.Error, line,
                            "Second CHILD block in one window; createWindow would create the window twice.");
                    ParseChildren(state, depth);
                    childrenParsed = true;
                    continue;
                }
                if (token == "DATA")
                {
                    ExpectEquals(token, line);
                    ReadUntilSemicolon(line, token);
                    Report("obsolete-data-key", WndDiagnosticSeverity.Compatibility, line,
                        "DATA is the obsolete gadget data key (parseData, 'THIS FUNCTION IS NEVER REACHED'); value ignored.");
                    continue;
                }
                if (!FieldKeys.Contains(token))
                {
                    ReadUntilSemicolon(line, token);
                    Report("unknown-window-key", WndDiagnosticSeverity.Unknown, line, $"Unknown window key '{token}'; parseWindow skips its value.");
                    continue;
                }
                ExpectEquals(token, line);
                var value = ReadUntilSemicolon(line, token);
                if (childrenParsed)
                {
                    Report("field-after-children", WndDiagnosticSeverity.Compatibility, line,
                        $"'{token}' follows the CHILD block; the original created the window at CHILD, so the value is ignored.");
                    continue;
                }
                ParseField(state, token, value, line);
            }
            return state.Build(this);
        }

        private void ParseChildren(WindowState state, int depth)
        {
            while (true)
            {
                if (!TryReadToken(out var token, out var line))
                {
                    Report("unterminated-children", WndDiagnosticSeverity.Error, state.Line, "CHILD block is missing ENDALLCHILDREN.");
                    return;
                }
                Count(token);
                if (token == "ENDALLCHILDREN") return;
                if (token == "END")
                {
                    Report("children-closed-by-end", WndDiagnosticSeverity.Compatibility, line,
                        "CHILD block closed by END instead of ENDALLCHILDREN; parseChildWindows accepts both.");
                    return;
                }
                if (token == "CHILD") continue; // separator in front of every child WINDOW
                if (token == "WINDOW")
                {
                    var child = ParseWindow(line, depth + 1);
                    if (child is not null) state.Children.Add(child);
                }
                else if (DefaultColorKeys.Contains(token))
                {
                    ExpectEquals(token, line);
                    ReadUntilSemicolon(line, token);
                    Report("child-default-color", WndDiagnosticSeverity.Compatibility, line,
                        $"{token} inside a CHILD block sets a file default for later windows only; value not applied.");
                }
                else Report("child-token-ignored", WndDiagnosticSeverity.Unknown, line,
                    $"Token '{token}' in a CHILD block; parseChildWindows ignores it.");
            }
        }

        private void SkipWindowBody()
        {
            var open = 0;
            while (TryReadToken(out var token, out var line))
            {
                if (token == "WINDOW") open++;
                else if (token == "END")
                {
                    if (open == 0) return;
                    open--;
                }
                else if (token is not ("CHILD" or "ENDALLCHILDREN")) ReadUntilSemicolon(line, token);
            }
        }

        private WndScreenRect ParseScreenRect(string value, int line)
        {
            var tokens = Split(value, " ,:=");
            string[] labels = ["SCREENRECT", "UPPERLEFT", "", "", "BOTTOMRIGHT", "", "", "CREATIONRESOLUTION", "", ""];
            var numbers = new int[6];
            var ok = tokens.Length == labels.Length;
            for (int i = 0, n = 0; ok && i < labels.Length; i++)
            {
                if (labels[i].Length > 0) ok = tokens[i] == labels[i];
                else ok = int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[n++]);
            }
            if (!ok)
            {
                Report("malformed-screen-rect", WndDiagnosticSeverity.Error, line,
                    $"Expected 'SCREENRECT = UPPERLEFT: x y, BOTTOMRIGHT: x y, CREATIONRESOLUTION: w h', found '{value.Trim()}'.");
                return new WndScreenRect(0, 0, 0, 0, 800, 600);
            }
            if (numbers[4] <= 0 || numbers[5] <= 0)
            {
                Report("invalid-creation-resolution", WndDiagnosticSeverity.Error, line,
                    $"CREATIONRESOLUTION {numbers[4]}x{numbers[5]} is not positive; parseScreenRect would divide by zero.");
                return new WndScreenRect(numbers[0], numbers[1], numbers[2], numbers[3], 800, 600);
            }
            if (numbers[2] < numbers[0] || numbers[3] < numbers[1])
                Report("inverted-screen-rect", WndDiagnosticSeverity.Error, line, "BOTTOMRIGHT lies above or left of UPPERLEFT.");
            return new WndScreenRect(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
        }

        private void ParseField(WindowState state, string key, string value, int line)
        {
            if (DrawDataKeys.TryGetValue(key, out var kind))
            {
                if (state.DrawData.ContainsKey(kind))
                    Report("duplicate-key", WndDiagnosticSeverity.Unknown, line, $"{key} appears twice; the later value wins.");
                state.DrawData[kind] = ParseDrawData(key, value, line);
                return;
            }
            if (!state.SeenKeys.Add(key))
                Report("duplicate-key", WndDiagnosticSeverity.Unknown, line, $"{key} appears twice; the later value wins.");
            switch (key)
            {
                case "NAME": state.Name = Quoted(value, "\"", key, line); break;
                case "STATUS": state.Status = (WndStatus)ParseFlags(value, StatusNames, key, line); break;
                case "STYLE": state.Style = (WndStyle)ParseFlags(value, StyleNames, key, line); break;
                case "SYSTEMCALLBACK": state.System = CallbackName(value, key, line); break;
                case "INPUTCALLBACK": state.Input = CallbackName(value, key, line); break;
                case "TOOLTIPCALLBACK": state.Tooltip = CallbackName(value, key, line); break;
                case "DRAWCALLBACK": state.Draw = CallbackName(value, key, line); break;
                case "HEADERTEMPLATE": state.HeaderTemplate = CallbackName(value, key, line); break;
                case "FONT": state.Font = ParseFont(value, line); break;
                case "TOOLTIPDELAY":
                {
                    var tokens = Split(value, " :,");
                    if (tokens.Length != 1 || !TryInt(tokens[0], out var delay))
                        Report("malformed-tooltip-delay", WndDiagnosticSeverity.Error, line, $"TOOLTIPDELAY '{value.Trim()}' is not an integer.");
                    else state.TooltipDelay = delay;
                    break;
                }
                case "TOOLTIPTEXT": state.TooltipText = NullIfEmpty(Quoted(value, "\"", key, line)); break;
                case "TEXT": state.Text = NullIfEmpty(Quoted(value, "\"", key, line)); break;
                case "TEXTCOLOR": state.TextColors = ParseTextColors(value, line); break;
                case "IMAGEOFFSET":
                {
                    var tokens = Split(value, " ");
                    if (tokens.Length != 2 || !TryInt(tokens[0], out var x) || !TryInt(tokens[1], out var y))
                        Report("malformed-image-offset", WndDiagnosticSeverity.Error, line, $"IMAGEOFFSET '{value.Trim()}' is not 'x y'.");
                    else { state.ImageOffsetX = x; state.ImageOffsetY = y; }
                    break;
                }
                case "TOOLTIP":
                    Report("obsolete-tooltip-key", WndDiagnosticSeverity.Compatibility, line,
                        "TOOLTIP is obsolete (parseTooltip stores a fixed placeholder); value ignored.");
                    break;
                case "LISTBOXDATA": state.ListBox = ParseListBoxData(new Labeled(value, key, line, this)); RequireType(state, WndWindowType.ScrollListBox, key, line); break;
                case "COMBOBOXDATA": state.ComboBox = ParseComboBoxData(new Labeled(value, key, line, this)); RequireType(state, WndWindowType.ComboBox, key, line); break;
                case "SLIDERDATA":
                {
                    var labeled = new Labeled(value, key, line, this);
                    state.Slider = new WndSliderData(labeled.Int("MINVALUE"), labeled.Int("MAXVALUE"));
                    labeled.End();
                    if (state.Type is not (WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider))
                        Report("gadget-data-type-mismatch", WndDiagnosticSeverity.Unknown, line, $"{key} on a {state.TypeName} window.");
                    break;
                }
                case "RADIOBUTTONDATA":
                {
                    var labeled = new Labeled(value, key, line, this);
                    state.RadioButton = new WndRadioButtonData(labeled.Int("GROUP"));
                    labeled.End();
                    RequireType(state, WndWindowType.RadioButton, key, line);
                    break;
                }
                case "STATICTEXTDATA":
                {
                    var labeled = new Labeled(value, key, line, this);
                    state.StaticText = new WndStaticTextData(labeled.Bool("CENTERED"));
                    labeled.End();
                    RequireType(state, WndWindowType.StaticText, key, line);
                    break;
                }
                case "TEXTENTRYDATA":
                {
                    var labeled = new Labeled(value, key, line, this);
                    state.TextEntry = new WndTextEntryData(labeled.Int("MAXLEN"), labeled.Bool("SECRETTEXT"), labeled.Bool("NUMERICALONLY"),
                        labeled.Bool("ALPHANUMERICALONLY"), labeled.Bool("ASCIIONLY"));
                    labeled.End();
                    RequireType(state, WndWindowType.EntryField, key, line);
                    break;
                }
                case "TABCONTROLDATA": state.TabControl = ParseTabControlData(new Labeled(value, key, line, this)); RequireType(state, WndWindowType.TabControl, key, line); break;
            }
        }

        private void RequireType(WindowState state, WndWindowType type, string key, int line)
        {
            if (state.Type != type)
                Report("gadget-data-type-mismatch", WndDiagnosticSeverity.Unknown, line, $"{key} on a {state.TypeName} window.");
        }

        private static WndListBoxData ParseListBoxData(Labeled labeled)
        {
            var length = labeled.Int("LENGTH");
            var autoScroll = labeled.Bool("AUTOSCROLL");
            var scrollIfAtEnd = labeled.OptionalBool("SCROLLIFATEND");
            var autoPurge = labeled.Bool("AUTOPURGE");
            var scrollBar = labeled.Bool("SCROLLBAR");
            var multiSelect = labeled.Bool("MULTISELECT");
            var columns = labeled.Int("COLUMNS");
            var widths = ImmutableArray.CreateBuilder<int>();
            // parseListboxData reads one COLUMNSWIDTH per column only when there is more than one column.
            if (columns > 1)
                for (var i = 0; i < columns; i++) widths.Add(labeled.Int("COLUMNSWIDTH"));
            var forceSelect = labeled.Bool("FORCESELECT");
            labeled.End();
            return new WndListBoxData(length, autoScroll, scrollIfAtEnd, autoPurge, scrollBar, multiSelect, columns, widths.ToImmutable(), forceSelect);
        }

        private static WndComboBoxData ParseComboBoxData(Labeled labeled)
        {
            var data = new WndComboBoxData(labeled.Bool("ISEDITABLE"), labeled.Int("MAXCHARS"), labeled.Int("MAXDISPLAY"),
                labeled.Bool("ASCIIONLY"), labeled.Bool("LETTERSANDNUMBERS"));
            labeled.End();
            return data;
        }

        private static WndTabControlData ParseTabControlData(Labeled labeled)
        {
            var orientation = labeled.Int("TABORIENTATION");
            var edge = labeled.Int("TABEDGE");
            var width = labeled.Int("TABWIDTH");
            var height = labeled.Int("TABHEIGHT");
            var count = labeled.Int("TABCOUNT");
            var border = labeled.Int("PANEBORDER");
            var entries = labeled.Int("PANEDISABLED");
            var disabled = ImmutableArray.CreateBuilder<bool>();
            for (var i = 0; i < entries; i++) disabled.Add(labeled.BareBool());
            labeled.End();
            return new WndTabControlData(orientation, edge, width, height, count, border, disabled.ToImmutable());
        }

        private WndFont? ParseFont(string value, int line)
        {
            var quote = value.IndexOf('"');
            var tokens = Split(value[..Math.Max(quote, 0)], " ,:");
            if (quote < 0 || tokens.Length != 1 || tokens[0] != "NAME")
            {
                Report("malformed-font", WndDiagnosticSeverity.Error, line, $"FONT '{value.Trim()}' does not start with NAME: \"...\".");
                return null;
            }
            var end = value.IndexOfAny([':', ',', '"'], quote + 1);
            if (end < 0) end = value.Length;
            var name = value[(quote + 1)..end];
            var rest = end < value.Length && value[end] == '"' ? value[(end + 1)..] : value[end..];
            var labeled = new Labeled(rest, "FONT", line, this);
            var size = labeled.Int("SIZE");
            var bold = labeled.Bool("BOLD");
            labeled.End();
            return new WndFont(name, size, bold);
        }

        private WndTextColors ParseTextColors(string value, int line)
        {
            var labeled = new Labeled(value, "TEXTCOLOR", line, this);
            var colors = new WndColor[6];
            string[] labels = ["ENABLED", "ENABLEDBORDER", "DISABLED", "DISABLEDBORDER", "HILITE", "HILITEBORDER"];
            for (var i = 0; i < labels.Length; i++) colors[i] = labeled.Color(labels[i]);
            labeled.End();
            return new WndTextColors(colors[0], colors[1], colors[2], colors[3], colors[4], colors[5]);
        }

        private WndDrawDataSet ParseDrawData(string key, string value, int line)
        {
            var labeled = new Labeled(value, key, line, this);
            var entries = ImmutableArray.CreateBuilder<WndDrawData>();
            while (!labeled.AtEnd && entries.Count < WndDrawDataSet.MaxEntries)
            {
                var image = labeled.Word("IMAGE");
                var color = labeled.Color("COLOR");
                var border = labeled.Color("BORDERCOLOR");
                if (labeled.Failed) break;
                entries.Add(new WndDrawData(image == "NoImage" ? null : image, color, border));
            }
            if (!labeled.Failed && entries.Count != WndDrawDataSet.MaxEntries)
                Report("draw-data-count", WndDiagnosticSeverity.Unknown, line,
                    $"{key} has {entries.Count} entries; parseDrawData reads exactly {WndDrawDataSet.MaxEntries}.");
            var rest = labeled.Rest;
            if (rest.Length >= 2 && (FieldKeys.Contains(rest[0]) || DrawDataKeys.ContainsKey(rest[0])) && rest[1] == "=")
            {
                Report("statement-swallows-next-key", WndDiagnosticSeverity.Compatibility, line,
                    $"{key} lacks its ';', so the value runs into '{rest[0]}'; parseDrawData reads {WndDrawDataSet.MaxEntries} entries and drops the rest, so {rest[0]} is never applied.");
                labeled.Consume();
            }
            labeled.End();
            return new WndDrawDataSet(DrawDataKeys[key], entries.ToImmutable());
        }

        private uint ParseFlags(string value, string[] names, string key, int line)
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("NULL", StringComparison.Ordinal)) return 0;
            uint bits = 0;
            foreach (var raw in trimmed.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                var flag = raw.Trim();
                var index = Array.FindIndex(names, name => name.Equals(flag, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) bits |= 1u << index;
                else if (key == "STATUS" && flag == "DISABLED")
                    Report("status-disabled-flag", WndDiagnosticSeverity.Compatibility, line,
                        "STATUS flag DISABLED does not exist (parseBitString logs and ignores it); the window is disabled because ENABLED is absent.");
                else Report("unknown-flag", WndDiagnosticSeverity.Unknown, line, $"{key} flag '{flag}' is unknown; parseBitString ignores it.");
            }
            return bits;
        }

        private string? CallbackName(string value, string key, int line)
        {
            var name = Quoted(value, "\"", key, line);
            return IsNoneSentinel(name) ? null : name;
        }

        private string? Quoted(string value, string terminators, string key, int line)
        {
            var start = value.IndexOf('"');
            if (start < 0)
            {
                Report("missing-quote", WndDiagnosticSeverity.Error, line, $"{key} value '{value.Trim()}' is not quoted.");
                return null;
            }
            var end = value.IndexOfAny(terminators.ToCharArray(), start + 1);
            if (end < 0)
            {
                Report("unterminated-quote", WndDiagnosticSeverity.Error, line, $"{key} value '{value.Trim()}' has no closing quote.");
                end = value.Length;
            }
            else if (value[(end + 1)..].Trim().Length > 0)
                Report("trailing-text", WndDiagnosticSeverity.Error, line, $"{key} has text after the closing quote: '{value[(end + 1)..].Trim()}'.");
            return value[(start + 1)..end];
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private void ExpectEquals(string key, int line)
        {
            SkipWhitespace();
            if (_position < text.Length && text[_position] == '=')
            {
                _position++;
                return;
            }
            Report("missing-equals", WndDiagnosticSeverity.Error, line, $"Key '{key}' is not followed by '='.");
        }

        private string ReadUntilSemicolon(int line, string key)
        {
            SkipWhitespace();
            var builder = new StringBuilder();
            while (_position < text.Length)
            {
                var c = text[_position++];
                if (c == '\n') _line++;
                if (c == ';') return builder.ToString();
                builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
            }
            Report("unterminated-statement", WndDiagnosticSeverity.Error, line, $"Value of '{key}' is not terminated by ';'.");
            return builder.ToString();
        }

        private bool TryReadToken(out string token, out int line)
        {
            SkipWhitespace();
            line = _line;
            var start = _position;
            while (_position < text.Length && !char.IsWhiteSpace(text[_position])) _position++;
            token = text[start.._position];
            return token.Length > 0;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                if (text[_position] == '\n') _line++;
                _position++;
            }
        }

        private void Count(string key) => _keyCounts[key] = _keyCounts.GetValueOrDefault(key) + 1;

        public void Report(string code, WndDiagnosticSeverity severity, int line, string message)
            => _diagnostics.Add(new WndDiagnostic(code, severity, fileName, line, message));

        private static string[] Split(string value, string separators)
            => value.Split(separators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

        private static bool TryInt(string token, out int value)
            => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryByte(string token, out byte value)
            => byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>Positional <c>LABEL: value</c> reader with the separators of the original (<c>" :,\n\r\t"</c>).</summary>
        private sealed class Labeled(string value, string key, int line, FileParser owner)
        {
            private readonly string[] _tokens = Split(value, " :,");
            private int _index;

            public bool Failed { get; private set; }
            public bool AtEnd => _index >= _tokens.Length;

            public int Int(string label) => Label(label) && Next(out var token) && TryInt(token, out var value) ? value : Fail(label);

            public bool Bool(string label) => Int(label) != 0;

            public bool BareBool() => Next(out var token) && TryInt(token, out var value) ? value != 0 : Fail("value") != 0;

            public bool OptionalBool(string label)
            {
                if (_index < _tokens.Length && _tokens[_index].Equals(label, StringComparison.OrdinalIgnoreCase)) return Bool(label);
                return false;
            }

            public string? Word(string label) => Label(label) && Next(out var token) ? token : FailText(label);

            public WndColor Color(string label)
            {
                if (!Label(label)) return WndColor.Undefined;
                var bytes = new byte[4];
                for (var i = 0; i < 4; i++)
                    if (!Next(out var token) || !TryByte(token, out bytes[i]))
                    {
                        Fail(label);
                        return WndColor.Undefined;
                    }
                return new WndColor(bytes[0], bytes[1], bytes[2], bytes[3]);
            }

            public void End()
            {
                if (!Failed && !AtEnd)
                    owner.Report("trailing-text", WndDiagnosticSeverity.Error, line,
                        $"{key} has unexpected trailing tokens '{string.Join(' ', _tokens[_index..])}'.");
            }

            private bool Label(string label)
            {
                if (Failed) return false;
                if (_index < _tokens.Length && _tokens[_index] == label)
                {
                    _index++;
                    return true;
                }
                // The original reads label tokens with strtok and discards them unchecked, so a wrong label word is
                // harmless there; the value is taken positionally.
                if (_index < _tokens.Length && _tokens[_index].All(char.IsAsciiLetterUpper))
                {
                    owner.Report("label-mismatch", WndDiagnosticSeverity.Compatibility, line,
                        $"{key}: label '{_tokens[_index]}' where '{label}' belongs; the original ignores label words and reads the value positionally.");
                    _index++;
                    return true;
                }
                Fail(label);
                return false;
            }

            /// <summary>The remaining tokens, used to recognise a statement that swallowed the next key.</summary>
            public string[] Rest => Failed || AtEnd ? [] : _tokens[_index..];

            public void Consume() => _index = _tokens.Length;

            private bool Next(out string token)
            {
                token = _index < _tokens.Length ? _tokens[_index++] : string.Empty;
                return token.Length > 0;
            }

            private int Fail(string label)
            {
                if (!Failed)
                    owner.Report($"malformed-{key.ToLowerInvariant()}", WndDiagnosticSeverity.Error, line,
                        $"{key}: expected '{label}' at token {_index} of '{value.Trim()}'.");
                Failed = true;
                return 0;
            }

            private string? FailText(string label)
            {
                Fail(label);
                return null;
            }
        }

        private sealed class WindowState(WndWindowType type, string typeName, WndScreenRect rect, int line)
        {
            public WndWindowType Type { get; } = type;
            public string TypeName { get; } = typeName;
            public int Line { get; } = line;
            public HashSet<string> SeenKeys { get; } = new(StringComparer.Ordinal);
            public SortedDictionary<WndDrawDataKind, WndDrawDataSet> DrawData { get; } = [];
            public List<WndWindow> Children { get; } = [];
            public string? Name;
            public WndStatus Status;
            public WndStyle Style;
            public string? System, Input, Tooltip, Draw, HeaderTemplate, TooltipText, Text;
            public WndFont? Font;
            public int TooltipDelay = -1;
            public WndTextColors TextColors = WndTextColors.Undefined;
            public int ImageOffsetX, ImageOffsetY;
            public WndListBoxData? ListBox;
            public WndComboBoxData? ComboBox;
            public WndSliderData? Slider;
            public WndRadioButtonData? RadioButton;
            public WndStaticTextData? StaticText;
            public WndTextEntryData? TextEntry;
            public WndTabControlData? TabControl;

            public WndWindow Build(FileParser owner)
            {
                var typeStyle = StyleOf(Type);
                const WndStyle typeBits = WndStyle.PushButton | WndStyle.RadioButton | WndStyle.CheckBox | WndStyle.VerticalSlider
                    | WndStyle.HorizontalSlider | WndStyle.ScrollListBox | WndStyle.EntryField | WndStyle.StaticText | WndStyle.ProgressBar
                    | WndStyle.User | WndStyle.TabControl | WndStyle.TabPane | WndStyle.ComboBox;
                if (Type != WndWindowType.Unknown && (Style & typeBits & ~typeStyle) != 0)
                    owner.Report("style-type-mismatch", WndDiagnosticSeverity.Unknown, Line,
                        $"STYLE '{Style}' names another window type than WINDOWTYPE {TypeName}.");
                var missing = Type switch
                {
                    WndWindowType.ScrollListBox when ListBox is null => "LISTBOXDATA",
                    WndWindowType.ComboBox when ComboBox is null => "COMBOBOXDATA",
                    WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider when Slider is null => "SLIDERDATA",
                    WndWindowType.RadioButton when RadioButton is null => "RADIOBUTTONDATA",
                    WndWindowType.StaticText when StaticText is null => "STATICTEXTDATA",
                    WndWindowType.EntryField when TextEntry is null => "TEXTENTRYDATA",
                    WndWindowType.TabControl when TabControl is null => "TABCONTROLDATA",
                    _ => null,
                };
                if (missing is not null)
                    owner.Report("gadget-data-missing", WndDiagnosticSeverity.Unknown, Line,
                        $"{TypeName} window without {missing}; the original uses zeroed gadget data.");
                if (Name is null)
                    owner.Report("missing-name", WndDiagnosticSeverity.Unknown, Line, "Window has no NAME; it cannot be looked up.");
                return new WndWindow(Type, TypeName, Name, rect, Status, Style | typeStyle,
                    new WndCallbacks(System, Input, Tooltip, Draw), Font, HeaderTemplate, TooltipDelay, TooltipText, Text, TextColors,
                    [..DrawData.Values], ImageOffsetX, ImageOffsetY, ListBox, ComboBox, Slider, RadioButton, StaticText, TextEntry,
                    TabControl, [..Children], Line);
            }
        }
    }
}
