using System.Collections.Immutable;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The second options screen (docs/UI-MODERN.md section 7): what the original's <c>OptionsMenu.wnd</c> does not reach - the interface skin, the display mode, the 3D view in
/// a wide window, the text size, the sound device - and a read-only view of every hotkey of <c>CommandMap.ini</c>. It is opened from the options' "More options" button.
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
        ("Skin", "Interface skin"), ("DisplayMode", "Display mode"), ("Aspect", "3D view in a wide window"), ("TextScale", "Text size"), ("AudioDevice", "Sound device"),
    ];

    public static readonly ImmutableArray<(UiSkinName Value, string Text)> SkinChoices = [(UiSkinName.Original, "Original"), (UiSkinName.Modern, "Modern")];

    public static readonly ImmutableArray<(DisplayMode Value, string Text)> DisplayModeChoices =
        [(DisplayMode.Windowed, "Window"), (DisplayMode.Borderless, "Window without frame"), (DisplayMode.Fullscreen, "Full screen")];

    public static readonly ImmutableArray<(AspectHandling Value, string Text)> AspectChoices =
        [(AspectHandling.KeepHorizontal, "As the original"), (AspectHandling.KeepVertical, "Wider (more to the sides)")];

    public const string DefaultDevice = "System default";

    /// <summary>
    /// The screen stands in the options' place, and its panel is as translucent as theirs: like a screen the original opens by <c>Shell::push</c> it hides the screens
    /// below, so the options do not show through it.
    /// </summary>
    public override bool HidesScreensBelow => true;

    private GameSettings _working;
    private readonly ImmutableArray<string> _devices;

    public MoreOptionsScreen(GameShell shell) : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu), shell.Layouts.Get(ShellLayoutNames.SkirmishMapSelectMenu)), shell.Layouts.HeaderTemplates))
    {
        _working = shell.Settings;
        _devices = [DefaultDevice, .. shell.Environment.AudioDevices];
        Tree.Register("MoreOptionsSystem", OnSystem);
        Tree.Register("MoreOptionsInput", OnInput);
        W("Title").SetText("More options");
        W("HotkeysLabel").SetText("Hotkeys (read only; the keys of CommandMap.ini)");
        foreach (var (row, label) in Rows) W("Label" + row).SetText(label);
        Fill("Skin", SkinChoices.Select(choice => choice.Text));
        Fill("DisplayMode", DisplayModeChoices.Select(choice => choice.Text));
        Fill("Aspect", AspectChoices.Select(choice => choice.Text));
        Fill("TextScale", SettingsValidator.TextScales.Select(percent => $"{percent} %"));
        Fill("AudioDevice", _devices);
        FillHotkeys();
        Refresh();
    }

    private void Fill(string row, IEnumerable<string> entries)
    {
        var combo = W("Combo" + row);
        combo.ClearCombo();
        foreach (var entry in entries) combo.AddComboEntry(entry);
    }

    private void FillHotkeys()
    {
        var list = W("ListHotkeys");
        var map = HotkeyMap.FromGraph(Shell.Data.Graph).WithEngineDefaults();
        var rows = map.Entries
            // An action without a name in the string table is listed by its own name in words, never left out.
            .Select(entry => (Name: entry.DisplayNameLabel.Length > 0 ? Text(entry.DisplayNameLabel) : Words(entry.Action.ToString()), Key: KeyText(entry.Binding)))
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Key, StringComparer.Ordinal);
        foreach (var (name, key) in rows) list.AddListRow(new UiListRow(name, key));
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
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonAccept"): Accept(); break;
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
        var back = Find(options, "OptionsMenu.wnd:ButtonBack");
        var line = Find(options, "OptionsMenu.wnd:Line");
        var list = Find(mapSelect, "SkirmishMapSelectMenu.wnd:ListboxMap");
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
        const int first = 72, step = 34, height = 24;
        for (var index = 0; index < Rows.Length; index++)
        {
            var top = first + index * step;
            children.Add(Copy(label, "Label" + Rows[index].Row, At(152, top, 372, top + height)));
            children.Add(Copy(combo, "Combo" + Rows[index].Row, At(380, top, 635, top + height)));
        }
        var hotkeysTop = first + Rows.Length * step + 8;
        children.Add(Copy(label, "HotkeysLabel", At(152, hotkeysTop, 635, hotkeysTop + height)));
        var hotkeys = Copy(list, "ListHotkeys", At(152, hotkeysTop + height + 4, 635, 516));
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
