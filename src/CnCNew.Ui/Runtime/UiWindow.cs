using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// A live window of a <see cref="UiWindowTree"/>: the parsed definition plus mutable state (hidden, enabled, hilited,
/// selected, text, gadget state) and its display rectangle. State changes that the original reports through messages go
/// through the tree (input methods); the setters here are silent like the original <c>Gadget*Set*</c> helpers.
/// </summary>
public sealed class UiWindow
{
    private readonly List<UiWindow> _children = [];

    internal UiWindow(UiWindowTree tree, WndWindow definition, UiWindow? parent, string layoutFile, int order)
    {
        Tree = tree;
        Definition = definition;
        Parent = parent;
        LayoutFile = layoutFile;
        Order = order;
        Status = definition.Status;
        if (definition.Text is { } text)
            Text = definition.Type == WndWindowType.EntryField ? UiTextValue.Plain(text) : UiTextValue.Label(text);
        ListBox = definition.ListBox is { } list ? new UiListBoxState(list) : null;
        Slider = definition.Slider is { } slider ? new UiSliderState(slider) : null;
        ComboBox = definition.ComboBox is { } combo ? new UiComboBoxState(combo) : null;
        TabControl = definition.TabControl is { } tabs ? new UiTabControlState(tabs) : null;
        if (definition.TextEntry is { } entry)
        {
            TextEntry = new UiTextEditState(entry.MaxLength, entry.SecretText, entry.NumericalOnly, entry.AlphaNumericalOnly, entry.AsciiOnly);
            // setWindowText passes the TEXT label of an entry field through untranslated (GadgetTextEntrySetText).
            if (definition.Text is { } initial) TextEntry.SetText(initial);
        }
    }

    public UiWindowTree Tree { get; }
    public WndWindow Definition { get; }
    public WndWindowType Type => Definition.Type;
    /// <summary>The decorated name (<c>File.wnd:Name</c>), empty when the definition has none.</summary>
    public string Name => Definition.Name ?? string.Empty;
    /// <summary>The path of the layout the window was loaded from.</summary>
    public string LayoutFile { get; }
    /// <summary>Creation order within the tree; stable identity for deterministic output.</summary>
    public int Order { get; }
    public UiWindow? Parent { get; }
    public IReadOnlyList<UiWindow> Children => _children;
    public UiRect Rect { get; internal set; }
    /// <summary><see cref="Rect"/> grown to <see cref="UiScaleOptions.MinimumHitTarget"/> for gadgets.</summary>
    public UiRect HitRect { get; internal set; }
    public UiTransform Transform { get; internal set; }
    public UiFont Font { get; internal set; }
    public WndStatus Status { get; private set; }
    public bool IsHidden => (Status & WndStatus.Hidden) != 0;
    public bool IsEnabled => (Status & WndStatus.Enabled) != 0;
    public bool IsVisible => !IsHidden && (Parent?.IsVisible ?? true);
    public bool IsEffectivelyEnabled => IsEnabled && (Parent?.IsEffectivelyEnabled ?? true);
    public bool IsHilited { get; internal set; }
    /// <summary>Pressed push button, checked check box/radio button/check-like button.</summary>
    public bool IsSelected { get; internal set; }
    public bool IsFocused => Tree.Focus == this;
    public UiTextValue? Text { get; private set; }
    public int ProgressPercent { get; private set; }
    public UiListBoxState? ListBox { get; }
    public UiSliderState? Slider { get; }
    public UiComboBoxState? ComboBox { get; }
    public UiTextEditState? TextEntry { get; }
    public UiTabControlState? TabControl { get; }

    public bool IsGadget => Type is not (WndWindowType.User or WndWindowType.TabPane or WndWindowType.Unknown);

    /// <summary>Gadgets with input behaviour (static text and progress bars only display).</summary>
    public bool IsInteractive => Type is WndWindowType.PushButton or WndWindowType.CheckBox or WndWindowType.RadioButton
        or WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider or WndWindowType.ScrollListBox
        or WndWindowType.EntryField or WndWindowType.ComboBox or WndWindowType.TabControl;

    internal void AddChild(UiWindow child) => _children.Add(child);

    public void SetHidden(bool hidden)
    {
        Status = hidden ? Status | WndStatus.Hidden : Status & ~WndStatus.Hidden;
        if (hidden) Tree.OnWindowHidden(this);
    }

    public void SetEnabled(bool enabled)
    {
        Status = enabled ? Status | WndStatus.Enabled : Status & ~WndStatus.Enabled;
        if (!enabled) Tree.OnWindowHidden(this);
    }

    private string? _enabledImage0;
    private bool _enabledImage0Replaced;

    /// <summary>The image of the first enabled draw entry: the file's own, or the one <see cref="SetEnabledImage0"/> replaced it with.</summary>
    public string? EnabledImage0 => _enabledImage0Replaced ? _enabledImage0 : Definition.GetDrawData(WndDrawDataKind.Enabled)[0].Image;

    /// <summary><c>winSetEnabledImage(0, image)</c>: replaces the first enabled draw entry's image (window transitions use it to hand an image to a grow marker).</summary>
    public void SetEnabledImage0(string? image)
    {
        _enabledImage0 = image;
        _enabledImage0Replaced = true;
    }

    public void SetText(string text) => Text = UiTextValue.Plain(text);

    public void SetTextLabel(string label) => Text = UiTextValue.Label(label);

    /// <summary>Checks or unchecks a check box, radio button (clearing its group) or check-like button without sending a message.</summary>
    public void SetChecked(bool value)
    {
        IsSelected = value;
        if (value && Type == WndWindowType.RadioButton) Tree.ClearRadioGroup(this);
    }

    public void SetProgress(int percent) => ProgressPercent = Math.Clamp(percent, 0, 100);

    public int AddListRow(UiListRow row) => (ListBox ?? throw NotA("list box")).Add(row);

    public void ClearList() => (ListBox ?? throw NotA("list box")).Clear();

    /// <summary>Selects a list row without a message (<c>GadgetListBoxSetSelected</c> does not notify either).</summary>
    public void SetListSelection(int row) => (ListBox ?? throw NotA("list box")).Select(row, false);

    public int AddComboEntry(string text) => (ComboBox ?? throw NotA("combo box")).Add(text);

    public void ClearCombo() => (ComboBox ?? throw NotA("combo box")).Clear();

    public void SetComboSelection(int index)
    {
        var combo = ComboBox ?? throw NotA("combo box");
        combo.SelectedIndex = index >= 0 && index < combo.Entries.Count ? index : -1;
        if (combo.SelectedIndex >= 0) combo.Edit.SetText(combo.Entries[combo.SelectedIndex]);
    }

    public void SetSliderValue(int value)
    {
        var slider = Slider ?? throw NotA("slider");
        slider.Value = slider.Clamp(value);
    }

    public void SetEntryText(string text) => (TextEntry ?? throw NotA("text entry")).SetText(text);

    public void SelectTab(int index)
    {
        var tabs = TabControl ?? throw NotA("tab control");
        var panes = Children.Where(child => child.Type == WndWindowType.TabPane).ToArray();
        tabs.ActiveTab = Math.Clamp(index, 0, Math.Max(0, panes.Length - 1));
        for (var i = 0; i < panes.Length; i++) panes[i].SetHidden(i != tabs.ActiveTab);
    }

    private InvalidOperationException NotA(string what) => new($"Window '{Name}' ({Definition.TypeName}) is not a {what}.");

    public override string ToString() => $"{Name} [{Definition.TypeName}] {Rect}";
}
