using System.Collections.Immutable;
using System.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>One list box row: one text per column and an optional text color (undefined = the window's text color).</summary>
public sealed record UiListRow(ImmutableArray<string> Cells, WndColor Color)
{
    public UiListRow(params string[] cells) : this([..cells], WndColor.Undefined) { }
}

public sealed class UiListBoxState(WndListBoxData data)
{
    private readonly List<UiListRow> _rows = [];
    private readonly SortedSet<int> _selection = [];

    public WndListBoxData Data { get; } = data;
    public IReadOnlyList<UiListRow> Rows => _rows;
    public IReadOnlyCollection<int> Selection => _selection;
    public int SelectedIndex => _selection.Count > 0 ? _selection.Min : -1;
    public int TopIndex { get; internal set; }

    /// <summary>
    /// Adds a row. A full list (<c>LENGTH</c>) drops its first row when <c>AUTOPURGE</c> is set and rejects the row
    /// otherwise (returns -1).
    /// </summary>
    internal int Add(UiListRow row)
    {
        if (Data.Length > 0 && _rows.Count >= Data.Length)
        {
            if (!Data.AutoPurge) return -1;
            _rows.RemoveAt(0);
            var shifted = _selection.Where(index => index > 0).Select(index => index - 1).ToArray();
            _selection.Clear();
            foreach (var index in shifted) _selection.Add(index);
            TopIndex = Math.Max(0, TopIndex - 1);
        }
        _rows.Add(row);
        return _rows.Count - 1;
    }

    internal void Clear()
    {
        _rows.Clear();
        _selection.Clear();
        TopIndex = 0;
    }

    internal void Select(int index, bool toggle)
    {
        if (!toggle) _selection.Clear();
        if (index < 0 || index >= _rows.Count) return;
        if (toggle && !_selection.Add(index)) _selection.Remove(index);
        else _selection.Add(index);
    }
}

public sealed class UiSliderState(WndSliderData data)
{
    public int Minimum { get; } = data.MinValue;
    /// <summary>A slider with <c>MAXVALUE == MINVALUE</c> gets <c>MIN + 1</c> like <c>gogoGadgetSlider</c>.</summary>
    public int Maximum { get; } = data.MaxValue == data.MinValue ? data.MinValue + 1 : data.MaxValue;
    public int Value { get; internal set; } = data.MinValue;
    public bool Dragging { get; internal set; }

    internal int Clamp(int value) => Math.Clamp(value, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum));
}

public sealed class UiComboBoxState(WndComboBoxData data)
{
    private readonly List<string> _entries = [];

    public WndComboBoxData Data { get; } = data;
    public IReadOnlyList<string> Entries => _entries;
    public int SelectedIndex { get; internal set; } = -1;
    public bool IsOpen { get; internal set; }
    public int TopIndex { get; internal set; }
    public UiTextEditState Edit { get; } = new(data.MaxChars, false, false, data.LettersAndNumbersOnly, data.AsciiOnly);

    internal int Add(string entry)
    {
        _entries.Add(entry);
        return _entries.Count - 1;
    }

    internal void Clear()
    {
        _entries.Clear();
        SelectedIndex = -1;
        TopIndex = 0;
    }
}

/// <summary>Editable text with caret and a single selection range (shared by text entries and editable combo boxes).</summary>
public sealed class UiTextEditState(int maxLength, bool secret, bool numericalOnly, bool alphaNumericalOnly, bool asciiOnly)
{
    private readonly StringBuilder _text = new();

    public int MaxLength { get; } = maxLength;
    public bool Secret { get; } = secret;
    public string Text => _text.ToString();
    public int Caret { get; private set; }
    /// <summary>The other end of the selection, or -1 when nothing is selected.</summary>
    public int SelectionAnchor { get; private set; } = -1;
    public bool HasSelection => SelectionAnchor >= 0 && SelectionAnchor != Caret;
    public int SelectionStart => HasSelection ? Math.Min(Caret, SelectionAnchor) : Caret;
    public int SelectionEnd => HasSelection ? Math.Max(Caret, SelectionAnchor) : Caret;
    public string DisplayText => Secret ? new string('*', _text.Length) : Text;

    /// <summary>Character filters of <c>TEXTENTRYDATA</c>/<c>COMBOBOXDATA</c>; control characters are never inserted.</summary>
    public bool Accepts(char c)
    {
        if (char.IsControl(c)) return false;
        if (numericalOnly && !char.IsAsciiDigit(c)) return false;
        if (alphaNumericalOnly && !char.IsAsciiLetterOrDigit(c)) return false;
        if (asciiOnly && c > 127) return false;
        return true;
    }

    internal void SetText(string text)
    {
        _text.Clear();
        _text.Append(MaxLength > 0 && text.Length > MaxLength ? text[..MaxLength] : text);
        Caret = _text.Length;
        SelectionAnchor = -1;
    }

    internal bool Insert(char c)
    {
        if (!Accepts(c)) return false;
        var removed = DeleteSelection();
        if (MaxLength > 0 && _text.Length >= MaxLength) return removed;
        _text.Insert(Caret, c);
        Caret++;
        return true;
    }

    internal bool Backspace()
    {
        if (DeleteSelection()) return true;
        if (Caret == 0) return false;
        _text.Remove(Caret - 1, 1);
        Caret--;
        return true;
    }

    internal bool Delete()
    {
        if (DeleteSelection()) return true;
        if (Caret >= _text.Length) return false;
        _text.Remove(Caret, 1);
        return true;
    }

    internal void MoveCaret(int position, bool extendSelection)
    {
        position = Math.Clamp(position, 0, _text.Length);
        if (extendSelection) { if (SelectionAnchor < 0) SelectionAnchor = Caret; }
        else SelectionAnchor = -1;
        Caret = position;
    }

    internal void SelectAll()
    {
        SelectionAnchor = 0;
        Caret = _text.Length;
    }

    private bool DeleteSelection()
    {
        if (!HasSelection)
        {
            SelectionAnchor = -1;
            return false;
        }
        var start = SelectionStart;
        _text.Remove(start, SelectionEnd - start);
        Caret = start;
        SelectionAnchor = -1;
        return true;
    }
}

public sealed class UiTabControlState(WndTabControlData data)
{
    public WndTabControlData Data { get; } = data;
    public int ActiveTab { get; internal set; }
}
