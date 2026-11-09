using System.Collections.Immutable;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The second options screen (docs/UI-MODERN.md section 7): what the original's <c>OptionsMenu.wnd</c> does not reach - the interface skin, the display mode, the 3D view in
/// a wide window, the text size, the sound device - and every hotkey of <c>CommandMap.ini</c>, which a person rebinds here: click a row, press the new key (with Ctrl, Alt
/// or Shift held when it should have them). A key another action of the same contexts already has is taken away from that action and the screen says so; "Defaults" restores the shipped
/// keys. The bindings are the working settings' <see cref="ControlSettings.Hotkeys"/> (only what differs from the shipped map) and are saved with Accept like the other choices;
/// the installation's <c>CommandMap.ini</c> is never written. It is opened from the options' "More options" button.
/// <para>
/// The original has no layout for it, and none is written down here: the screen is made at run time from the shipped <c>OptionsMenu.wnd</c> - its parent, its panel, its
/// title, its Accept and Back buttons, its line - and its rows from the shipped definitions of the options' resolution label and combo box and of the skirmish map list,
/// each under a name, a place and a callback of this project (<see cref="BuildLayout"/>). It is drawn in the current skin like every other screen.
/// </para>
/// Accept saves the settings; when the skin or the text size changed, the shell builds its screens again in the new look and the options stand over the main menu as before.
/// </summary>
public sealed class MoreOptionsScreen : ShellScreen
{
    public const string File = "MoreOptions";

    /// <summary>The rows of choices in their order (the suffix of the label's and the combo box's name, and the label's words).</summary>
    public static readonly ImmutableArray<(string Row, string Label)> Rows =
    [
        ("Skin", "Interface skin"), ("BarOpacity", "Control bar opacity"), ("DisplayMode", "Display mode"), ("Aspect", "Wide-screen view"), ("TextScale", "Text size"),
        ("MarkerStyle", "Selection markers"), ("HealthBarMode", "Health bars"), ("HealthBarHeight", "Health bar height"), ("AudioDevice", "Sound device"), ("TickRate", "Simulation rate"),
    ];

    /// <summary>The rows whose control is a slider instead of a combo box (<c>Slider</c> + row name), with the value written beside it (<c>Value</c> + row name).</summary>
    public static readonly ImmutableArray<string> SliderRows = ["BarOpacity"];

    /// <summary>The opacity slider's range in percent (<see cref="DisplaySettings.MinimumBarOpacityPercent"/> to 100).</summary>
    public const int OpacityMinimum = DisplaySettings.MinimumBarOpacityPercent, OpacityMaximum = 100;

    /// <summary>Which markers the world shows for a selection: what the original draws, what this port drew before (the default), a flat modern one.</summary>
    public static readonly ImmutableArray<(SelectionMarkerStyle Value, string Text)> MarkerStyleChoices =
        [(SelectionMarkerStyle.Original, "As the original (health box)"), (SelectionMarkerStyle.Extended, "Extended (ring, brackets)"), (SelectionMarkerStyle.Modern, "Modern (flat, thin)")];

    public static readonly ImmutableArray<(HealthBarMode Value, string Text)> HealthBarModeChoices =
        [(HealthBarMode.AlwaysAuto, "Always shown"), (HealthBarMode.AlwaysBelowFull, "Always, when not at full health"), (HealthBarMode.SelectedOnly, "Only when selected")];

    public static readonly ImmutableArray<(HealthBarHeight Value, string Text)> HealthBarHeightChoices =
        [(HealthBarHeight.Narrow, "Narrow"), (HealthBarHeight.Normal, "Normal"), (HealthBarHeight.Wide, "Wide")];

    public static readonly ImmutableArray<(UiSkinName Value, string Text)> SkinChoices = [(UiSkinName.Original, "Original"), (UiSkinName.Modern, "Modern")];

    public static readonly ImmutableArray<(DisplayMode Value, string Text)> DisplayModeChoices =
        [(DisplayMode.Windowed, "Window"), (DisplayMode.Borderless, "Window without frame"), (DisplayMode.BorderlessFullscreen, "Windowed fullscreen (borderless)"), (DisplayMode.Fullscreen, "Full screen")];

    public static readonly ImmutableArray<(AspectHandling Value, string Text)> AspectChoices =
        [(AspectHandling.KeepHorizontal, "As the original"), (AspectHandling.KeepVertical, "Wider (more to the sides)")];

    /// <summary>D-22: 60 Hz is the product's, 120 Hz is offered; the game loads its data for one rate, so a change counts from the next start.</summary>
    public static readonly ImmutableArray<(int Value, string Text)> TickRateChoices = [(60, "60 Hz"), (120, "120 Hz (from the next start)")];

    public const string DefaultDevice = "System default";

    /// <summary>
    /// The screen stands in the options' place, and its panel is as translucent as theirs: like a screen the original opens by <c>Shell::push</c> it hides the screens
    /// below, so the options do not show through it.
    /// </summary>
    public override bool HidesScreensBelow => true;

    private GameSettings _working;
    private readonly ImmutableArray<string> _devices;
    private readonly HotkeyMap _shipped;
    private HotkeyMap _hotkeys;
    private ImmutableArray<(MetaAction Action, string Name)> _rows = [];
    private MetaAction? _armed;
    private const string HotkeyHint = "Hotkeys: click, press a key";

    public MoreOptionsScreen(GameShell shell) : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu), shell.Layouts.Get(ShellLayoutNames.SkirmishMapSelectMenu)), shell.Layouts.HeaderTemplates))
    {
        _working = shell.Settings;
        _devices = [DefaultDevice, .. shell.Environment.AudioDevices];
        _shipped = HotkeyMap.FromGraph(shell.Data.Graph).WithEngineDefaults();
        _hotkeys = _shipped.ApplySettings(_working.Controls.Hotkeys);
        Tree.Register("MoreOptionsSystem", OnSystem);
        Tree.Register("MoreOptionsInput", OnInput);
        W("Title").SetText("More options");
        W("HotkeysLabel").SetText(HotkeyHint);
        W("HotkeysStatus").SetText("");
        foreach (var (row, label) in Rows) W("Label" + row).SetText(label);
        Fill("Skin", SkinChoices.Select(choice => choice.Text));
        Fill("DisplayMode", DisplayModeChoices.Select(choice => choice.Text));
        Fill("Aspect", AspectChoices.Select(choice => choice.Text));
        Fill("TextScale", SettingsValidator.TextScales.Select(percent => $"{percent} %"));
        Fill("AudioDevice", _devices);
        Fill("TickRate", TickRateChoices.Select(choice => choice.Text));
        Fill("MarkerStyle", MarkerStyleChoices.Select(choice => choice.Text));
        Fill("HealthBarMode", HealthBarModeChoices.Select(choice => choice.Text));
        Fill("HealthBarHeight", HealthBarHeightChoices.Select(choice => choice.Text));
        FillHotkeys();
        Refresh();
    }

    private void Fill(string row, IEnumerable<string> entries)
    {
        var combo = W("Combo" + row);
        combo.ClearCombo();
        foreach (var entry in entries) combo.AddComboEntry(entry);
    }

    private string ActionName(MetaAction action)
        => _rows.FirstOrDefault(row => row.Action == action).Name ?? Words(action.ToString());

    /// <summary>The rows are the shipped map's actions (an action a person unbinds stays listed), by name and then by the shipped key.</summary>
    private void FillHotkeys()
    {
        _rows = [.._shipped.Entries
            // An action without a name in the string table is listed by its own name in words, never left out.
            .Select(entry => (Entry: entry, Name: entry.DisplayNameLabel.Length > 0 ? Text(entry.DisplayNameLabel) : Words(entry.Action.ToString())))
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => KeyText(row.Entry.Binding), StringComparer.Ordinal)
            .Select(row => (row.Entry.Action, row.Name))];
        ShowHotkeys(selected: -1);
    }

    private void ShowHotkeys(int selected)
    {
        var list = W("ListHotkeys");
        var top = list.ListBox!.TopIndex;
        list.ClearList();
        foreach (var (action, name) in _rows)
            list.AddListRow(new UiListRow(name, _hotkeys.TryGetEntry(action, out var entry) ? KeyText(entry.Binding) : "Unbound"));
        list.SetListTop(top);
        if (selected >= 0) list.SetListSelection(selected);
    }

    public override bool CapturesKeys => _armed is not null;

    /// <summary>The key a person pressed for the armed row. Escape gives up; every other key (and the modifiers held with it) becomes the binding.</summary>
    public override void OnCapturedKey(KeyCode key, KeyModifiers modifiers)
    {
        if (_armed is not { } action) return;
        var row = _rows.ToList().FindIndex(item => item.Action == action);
        _armed = null;
        W("HotkeysLabel").SetText(HotkeyHint);
        if (key == KeyCode.Escape)
        {
            W("HotkeysStatus").SetText($"{ActionName(action)} keeps its key");
            return;
        }
        // Delete and Backspace take the binding away without giving it to anything else; they cannot be bound themselves.
        W("HotkeysStatus").SetText(key is KeyCode.Delete or KeyCode.Backspace ? Unbind(action) : Bind(action, key, modifiers));
        ShowHotkeys(row);
    }

    private void Commit(HotkeyMap map)
    {
        _hotkeys = map;
        _working = _working with { Controls = _working.Controls with { Hotkeys = _hotkeys.DifferencesFrom(_shipped) } };
    }

    /// <summary>
    /// Binds <paramref name="action"/> to <paramref name="key"/> with <paramref name="modifiers"/>. Only the key and the modifiers change: the binding keeps its transition (a
    /// key-up stays a key-up; an action that had no key gets the transition its kind fires on). The partner of a begin/end pair (<see cref="MetaActionNames.TryGetPartner"/>) moves
    /// to the same key with its own transition, so the pair keeps working. A key another action of overlapping contexts already has is taken from that action - a key does one
    /// thing at a time. Returns the line the screen shows.
    /// </summary>
    public string Bind(MetaAction action, KeyCode key, KeyModifiers modifiers)
    {
        var moved = new List<(MetaAction Action, KeyBinding Binding)>();
        var members = MetaActionNames.TryGetPartner(action, out var partner) ? new[] { action, partner } : [action];
        foreach (var member in members)
        {
            var transition = _hotkeys.TryGetEntry(member, out var current) ? current.Binding.Transition : MetaActionNames.IsEndOfPair(member) ? KeyTransition.Up : KeyTransition.Down;
            moved.Add((member, new KeyBinding(key, modifiers, transition)));
        }
        var map = _hotkeys;
        foreach (var (member, binding) in moved) map = map.Rebind(member, binding);
        var taken = new List<string>();
        foreach (var (member, binding) in moved)
        {
            if (!map.TryGetEntry(member, out var mine)) continue;
            foreach (var other in map.Entries.Where(entry => moved.All(item => item.Action != entry.Action) && entry.Binding == binding && (entry.UsableIn & mine.UsableIn) != 0).ToList())
            {
                map = map.Unbind(other.Action);
                taken.Add(ActionName(other.Action));
            }
        }
        Commit(map);
        var line = $"{ActionName(action)}: {KeyText(moved[0].Binding)}";
        if (members.Length > 1) line += $" (with {ActionName(partner)})";
        return taken.Count == 0 ? line : $"{line} (taken from {string.Join(", ", taken)})";
    }

    /// <summary>Takes the binding of <paramref name="action"/> away (and its partner's: half a pair does nothing). Returns the line the screen shows.</summary>
    public string Unbind(MetaAction action)
    {
        var map = _hotkeys.Unbind(action);
        var line = $"{ActionName(action)}: unbound";
        if (MetaActionNames.TryGetPartner(action, out var partner))
        {
            map = map.Unbind(partner);
            line += $" (and {ActionName(partner)})";
        }
        Commit(map);
        return line;
    }

    /// <summary>The shipped keys of <c>CommandMap.ini</c> again (not saved until Accept).</summary>
    public void RestoreHotkeyDefaults()
    {
        _armed = null;
        Commit(_shipped);
        W("HotkeysLabel").SetText(HotkeyHint);
        W("HotkeysStatus").SetText("The shipped keys are back");
        ShowHotkeys(selected: -1);
    }

    /// <summary>The binding the screen shows for an action, <c>null</c> when it is unbound.</summary>
    public KeyBinding? BindingOf(MetaAction action) => _hotkeys.TryGetEntry(action, out var entry) ? entry.Binding : null;

    /// <summary>The line under the list (what the last change did).</summary>
    public string HotkeyStatus => W("HotkeysStatus").Text?.Value ?? "";

    /// <summary>The action of a row, as the list shows it.</summary>
    public MetaAction? ActionAt(int row) => row >= 0 && row < _rows.Length ? _rows[row].Action : null;

    /// <summary>Arms a row as a click on it does: the next key pressed becomes its binding.</summary>
    public void SelectHotkey(MetaAction action)
    {
        var row = _rows.ToList().FindIndex(item => item.Action == action);
        if (row < 0) throw new ArgumentException($"{action} is not a listed hotkey.", nameof(action));
        Arm(row);
        W("ListHotkeys").SetListSelection(row);
    }

    private void Arm(int row)
    {
        if (ActionAt(row) is not { } action) return;
        _armed = action;
        W("HotkeysStatus").SetText($"Press a key for {ActionName(action)} (Esc: cancel, Del: unbind)");
    }

    /// <summary>A key as a person reads it: <c>Ctrl+Alt+F1</c>; a binding of the modifiers alone says so; a binding on the release of a key says that.</summary>
    public static string KeyText(KeyBinding binding)
    {
        var parts = new List<string>();
        if ((binding.Modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((binding.Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((binding.Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if (binding.Key != KeyCode.None)
        {
            var name = KeyNames.TryGetName(binding.Key, out var ini) ? ini : binding.Key.ToString();
            if (name.StartsWith("KEY_", StringComparison.Ordinal)) name = name[4..];
            name = name.StartsWith("KP", StringComparison.Ordinal) && name.Length > 2 ? "Keypad " + Words(name[2..]) : Words(name.Replace('_', ' '));
            parts.Add(name);
        }
        var text = parts.Count == 0 ? "-" : string.Join("+", parts) + (binding.Key == KeyCode.None ? " (held)" : string.Empty);
        return binding.Transition == KeyTransition.Up ? text + " (on release)" : text;
    }

    /// <summary><c>BeginCameraRotateLeft</c> as "Begin camera rotate left"; <c>LEFT</c> as "Left"; one letter or digit stays as it is.</summary>
    public static string Words(string name)
    {
        if (name.Length <= 1) return name;
        if (name.All(ch => !char.IsLower(ch))) return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.Length <= 1 ? word : word[0] + word[1..].ToLowerInvariant()));
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < name.Length; index++)
        {
            var ch = name[index];
            if (index > 0 && char.IsUpper(ch) && char.IsLower(name[index - 1])) builder.Append(' ').Append(char.ToLowerInvariant(ch));
            else if (index > 0 && char.IsDigit(ch) && !char.IsDigit(name[index - 1])) builder.Append(' ').Append(ch);
            else builder.Append(ch);
        }
        return builder.ToString();
    }

    /// <summary>The number of hotkeys the view lists.</summary>
    public int HotkeyRows => W("ListHotkeys").ListBox!.Rows.Count;

    private void Refresh()
    {
        W("ComboSkin").SetComboSelection(SkinChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.Skin));
        W("ComboDisplayMode").SetComboSelection(DisplayModeChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.Mode));
        W("ComboAspect").SetComboSelection(AspectChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.Aspect));
        W("ComboTextScale").SetComboSelection(SettingsValidator.TextScales.IndexOf(_working.Accessibility.UiTextScalePercent));
        // A device of the settings that is not there any more is shown as the default, which is what plays.
        W("ComboAudioDevice").SetComboSelection(Math.Max(0, _devices.IndexOf(_working.Audio.Device)));
        W("ComboTickRate").SetComboSelection(Math.Max(0, TickRateChoices.Select(choice => choice.Value).ToList().IndexOf(_working.General.TickRate)));
        W("ComboMarkerStyle").SetComboSelection(MarkerStyleChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.MarkerStyle));
        W("ComboHealthBarMode").SetComboSelection(HealthBarModeChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.HealthBars));
        W("ComboHealthBarHeight").SetComboSelection(HealthBarHeightChoices.Select(choice => choice.Value).ToList().IndexOf(_working.Display.HealthBarThickness));
        W("SliderBarOpacity").SetSliderValue(_working.Display.BarOpacityPercent);
        ShowOpacity();
    }

    /// <summary>The opacity as the slider stands, in percent, written beside it.</summary>
    private void ShowOpacity() => W("ValueBarOpacity").SetText($"{_working.Display.BarOpacityPercent} %");

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonAccept"): Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonDefaults"): Shell.PlaySound(ShellUiSounds.ButtonClick); RestoreHotkeyDefaults(); break;
            case UiEventKind.ListSelected when e.Source == Name("ListHotkeys"): Arm(e.Value); break;
            case UiEventKind.SliderTrack or UiEventKind.SliderDone when e.Source == Name("SliderBarOpacity"):
                _working = _working with { Display = _working.Display with { BarOpacityPercent = Math.Clamp(e.Value, OpacityMinimum, OpacityMaximum) } };
                ShowOpacity();
                break;
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonBack"): Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.CloseTop(); break;
            case UiEventKind.ComboSelected when e.Value >= 0:
                Choose(e.Source, e.Value);
                Shell.PlaySound(ShellUiSounds.ComboBoxClick);
                break;
        }
    }

    private void Choose(string source, int index)
    {
        if (source == Name("ComboSkin") && index < SkinChoices.Length) _working = _working with { Display = _working.Display with { Skin = SkinChoices[index].Value } };
        else if (source == Name("ComboDisplayMode") && index < DisplayModeChoices.Length) _working = _working with { Display = _working.Display with { Mode = DisplayModeChoices[index].Value } };
        else if (source == Name("ComboAspect") && index < AspectChoices.Length) _working = _working with { Display = _working.Display with { Aspect = AspectChoices[index].Value } };
        else if (source == Name("ComboTextScale") && index < SettingsValidator.TextScales.Length)
            _working = _working with { Accessibility = _working.Accessibility with { UiTextScalePercent = SettingsValidator.TextScales[index] } };
        else if (source == Name("ComboMarkerStyle") && index < MarkerStyleChoices.Length) _working = _working with { Display = _working.Display with { MarkerStyle = MarkerStyleChoices[index].Value } };
        else if (source == Name("ComboHealthBarMode") && index < HealthBarModeChoices.Length) _working = _working with { Display = _working.Display with { HealthBars = HealthBarModeChoices[index].Value } };
        else if (source == Name("ComboHealthBarHeight") && index < HealthBarHeightChoices.Length) _working = _working with { Display = _working.Display with { HealthBarThickness = HealthBarHeightChoices[index].Value } };
        else if (source == Name("ComboTickRate") && index < TickRateChoices.Length) _working = _working with { General = _working.General with { TickRate = TickRateChoices[index].Value } };
        else if (source == Name("ComboAudioDevice") && index < _devices.Length) _working = _working with { Audio = _working.Audio with { Device = index == 0 ? "" : _devices[index] } };
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    private void Accept()
    {
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        if (Shell.ApplySettings(_working)) Shell.RebuildScreens(withOptions: true);
        else Shell.CloseTop();
    }

    private static string Name(string window) => $"{File}.wnd:{window}";

    // ------------------------------------------------------------------------------------------------------------ the layout

    /// <summary>
    /// The screen's layout, made from the shipped <paramref name="options"/> (<c>OptionsMenu.wnd</c>) and <paramref name="mapSelect"/> (<c>SkirmishMapSelectMenu.wnd</c>, for its
    /// list box) in the layouts' 800 x 600 coordinates: the options' parent and panel with their own callbacks renamed, the title, the line under it, the rows of a label and a
    /// combo box (copies of the resolution label and its combo box), the hotkey list (a copy of the map list with two columns) and Accept and Back at their places.
    /// A window the shipped files do not have is an error.
    /// </summary>
    public static WndLayout BuildLayout(WndLayout options, WndLayout mapSelect)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mapSelect);
        static WndWindow Find(WndLayout layout, string name) => layout.AllWindows().FirstOrDefault(window => window.Name == name)
            ?? throw new InvalidDataException($"{layout.FileName} has no window '{name}'.");
        var parent = Find(options, "OptionsMenu.wnd:OptionsMenuParent");
        var panel = Find(options, "OptionsMenu.wnd:OptionsMenuParentOld");
        var label = Find(options, "OptionsMenu.wnd:ResolutionLabel");
        var combo = Find(options, "OptionsMenu.wnd:ComboBoxResolution");
        var accept = Find(options, "OptionsMenu.wnd:ButtonAccept");
        var defaults = Find(options, "OptionsMenu.wnd:ButtonDefaults");
        var back = Find(options, "OptionsMenu.wnd:ButtonBack");
        var line = Find(options, "OptionsMenu.wnd:Line");
        var list = Find(mapSelect, "SkirmishMapSelectMenu.wnd:ListboxMap");
        var slider = Find(options, "OptionsMenu.wnd:SliderMusicVolume");
        // The options' title is a static text without a name of its own at the panel's top left.
        var title = panel.Children.FirstOrDefault(child => child.Type == WndWindowType.StaticText && child.Rect.Left == 152 && child.Rect.Top == 21)
            ?? throw new InvalidDataException("OptionsMenu.wnd has no title static text at 152, 21.");

        static WndScreenRect At(int left, int top, int right, int bottom) => new(left, top, right, bottom, 800, 600);
        static WndWindow Copy(WndWindow source, string name, WndScreenRect rect, bool keepText = false) => source with
        {
            Name = Name(name),
            Rect = rect,
            Status = source.Status & ~WndStatus.Hidden,
            Text = keepText ? source.Text : null,
            TooltipText = null,
            Children = [],
        };

        var children = ImmutableArray.CreateBuilder<WndWindow>();
        children.Add(Copy(title, "Title", title.Rect));
        children.Add(Copy(line, "Line", line.Rect));
        const int first = 70, step = 28, height = 22;
        for (var index = 0; index < Rows.Length; index++)
        {
            var top = first + index * step;
            children.Add(Copy(label, "Label" + Rows[index].Row, At(152, top, 372, top + height)));
            if (SliderRows.Contains(Rows[index].Row))
            {
                // The opacity: a slider of the options' own kind (a copy of the music volume's) with its value beside it.
                children.Add(Copy(slider, "Slider" + Rows[index].Row, At(380, top, 560, top + height)) with { Slider = new WndSliderData(OpacityMinimum, OpacityMaximum) });
                children.Add(Copy(label, "Value" + Rows[index].Row, At(570, top, 635, top + height)));
            }
            else children.Add(Copy(combo, "Combo" + Rows[index].Row, At(380, top, 635, top + height)));
        }
        var hotkeysTop = first + Rows.Length * step + 6;
        children.Add(Copy(label, "HotkeysLabel", At(152, hotkeysTop, 470, hotkeysTop + height)));
        children.Add(Copy(defaults, "ButtonDefaults", At(480, hotkeysTop - 1, 635, hotkeysTop + height + 1), keepText: true));
        children.Add(Copy(label, "HotkeysStatus", At(152, 494, 635, 516)));
        var hotkeys = Copy(list, "ListHotkeys", At(152, hotkeysTop + height + 4, 635, 490));
        hotkeys = hotkeys with { ListBox = hotkeys.ListBox! with { Columns = 2, ColumnWidthPercentages = [68, 32], MultiSelect = false, ForceSelect = false } };
        children.Add(hotkeys);
        children.Add(Copy(accept, "ButtonAccept", accept.Rect, keepText: true));
        children.Add(Copy(back, "ButtonBack", back.Rect, keepText: true));

        var callbacks = parent.Callbacks with { System = "MoreOptionsSystem", Input = "MoreOptionsInput" };
        var newPanel = panel with { Name = Name("Panel"), Children = children.ToImmutable() };
        var root = parent with { Name = Name("Parent"), Callbacks = callbacks, Children = [newPanel] };
        return new WndLayout(File + ".wnd", options.FileVersion, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [root], [], ImmutableSortedDictionary<string, int>.Empty);
    }
}
