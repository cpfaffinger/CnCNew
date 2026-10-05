using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>Input routing and gadget behaviour (<c>GameWindowManager::winProcessMouseEvent</c>/<c>winProcessKey</c>, <c>Gadget*Input</c>).</summary>
public sealed partial class UiWindowTree
{
    private UiWindow? _hover;
    private UiWindow? _grab;
    private UiWindow? _openCombo;
    private long _hoverSince;
    private int _hoverX, _hoverY;
    private UiWindow? _lastClickWindow;
    private int _lastClickRow = -1;
    private long _lastClickTime = long.MinValue;

    /// <summary>The window under the pointer that receives mouse input (the mouse region of the original).</summary>
    public UiWindow? Hovered => _hover;
    /// <summary>The window that received the last left button down and gets the matching up (<c>m_grabWindow</c>).</summary>
    public UiWindow? Grabbed => _grab;
    public UiWindow? OpenComboBox => _openCombo;

    /// <summary>
    /// The window that takes input at a point: the open combo drop-down first (the "lone window"), then the hit window.
    /// A <c>NOINPUT</c> window swallows the event (no target) unless it belongs to a combo box.
    /// </summary>
    public UiWindow? InputTarget(int x, int y)
    {
        if (_openCombo is not null && UiGadgetGeometry.ComboDropDown(_openCombo).Contains(x, y)) return _openCombo;
        var window = HitTest(x, y);
        if (window is not null && (window.Status & WndStatus.NoInput) != 0)
            window = window.Parent?.Type == WndWindowType.ComboBox ? window.Parent : null;
        return window;
    }

    /// <summary>The nearest window from <paramref name="window"/> upward that has gadget input behaviour.</summary>
    private static UiWindow? Interactive(UiWindow? window)
    {
        for (; window is not null; window = window.Parent)
            if (window.IsInteractive) return window;
        return null;
    }

    /// <summary>
    /// Whether the pointer hilites the window: <c>MOUSETRACK</c> style on a gadget whose input function handles
    /// <c>GWM_MOUSE_ENTERING</c>. <c>GadgetStaticTextInput</c>, the progress bar and the tab control ignore the message, so
    /// their <c>HILITE*</c> draw data is never used however the style is set (shipped static texts carry <c>MOUSETRACK</c>
    /// and a bright hilite fill).
    /// </summary>
    private static bool Tracks(UiWindow window) => (window.Definition.Style & WndStyle.MouseTrack) != 0
        && window.Type is WndWindowType.PushButton or WndWindowType.CheckBox or WndWindowType.RadioButton
            or WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider or WndWindowType.ScrollListBox
            or WndWindowType.EntryField or WndWindowType.ComboBox;

    /// <summary>
    /// The gadgets that take the keyboard focus when clicked (<c>winSetFocus</c> in <c>GadgetComboBoxInput</c>,
    /// <c>GadgetListBoxInput</c>, <c>GadgetTextEntryInput</c>). Buttons, check boxes, radio buttons and sliders do not:
    /// a clicked button would otherwise keep its focus hilite after the pointer has left it.
    /// </summary>
    private static bool FocusesOnClick(UiWindow window)
        => window.Type is WndWindowType.ComboBox or WndWindowType.ScrollListBox or WndWindowType.EntryField;

    /// <summary>Moves the pointer; returns true when a window lies under it (the game should not handle the event).</summary>
    public bool MouseMove(int x, int y)
    {
        var target = InputTarget(x, y);
        if (_grab is not null)
        {
            if (_grab.Slider is { Dragging: true }) SetSliderFromPointer(_grab, x, y);
            return true;
        }
        if (target != _hover)
        {
            if (_hover is not null) Leave(_hover);
            _hover = target;
            _hoverSince = Now;
            if (target is not null) Enter(target);
        }
        // Mouse::createStreamMessages shows a tooltip once the pointer has been still for the delay: moving hides it again.
        if (x != _hoverX || y != _hoverY) _hoverSince = Now;
        _hoverX = x;
        _hoverY = y;
        return target is not null;
    }

    private void Enter(UiWindow window)
    {
        if (!Tracks(window)) return;
        window.IsHilited = true;
        Emit(UiEventKind.ButtonMouseEntering, window);
    }

    private void Leave(UiWindow window)
    {
        if (Tracks(window))
        {
            // GWM_MOUSE_LEAVING clears the hilite unconditionally, also on the focused gadget.
            window.IsHilited = false;
            Emit(UiEventKind.ButtonMouseLeaving, window);
        }
        if (window.Type == WndWindowType.PushButton && (window.Status & WndStatus.CheckLike) == 0) window.IsSelected = false;
    }

    public bool MouseDown(int x, int y, UiMouseButton button)
    {
        MouseMove(x, y);
        var target = InputTarget(x, y);
        if (_openCombo is not null && target != _openCombo) CloseCombo();
        if (target is null) return false;
        var gadget = Interactive(target);
        if (button == UiMouseButton.Left) _grab = gadget ?? target;
        if (gadget is null || !gadget.IsEffectivelyEnabled) return true;
        if (button == UiMouseButton.Left && FocusesOnClick(gadget) && IsFocusable(gadget)) SetFocus(gadget);

        switch (gadget.Type)
        {
            case WndWindowType.PushButton when button == UiMouseButton.Left:
                if ((gadget.Status & WndStatus.CheckLike) != 0)
                {
                    gadget.IsSelected = !gadget.IsSelected;
                    Emit(UiEventKind.ButtonSelected, gadget, gadget.IsSelected ? 1 : 0);
                }
                else
                {
                    gadget.IsSelected = true;
                    if ((gadget.Status & WndStatus.OnMouseDown) != 0) Emit(UiEventKind.ButtonSelected, gadget);
                }
                break;
            case WndWindowType.PushButton when button == UiMouseButton.Right && (gadget.Status & WndStatus.RightClick) != 0:
                if ((gadget.Status & WndStatus.CheckLike) != 0)
                {
                    gadget.IsSelected = !gadget.IsSelected;
                    Emit(UiEventKind.ButtonSelectedRight, gadget, gadget.IsSelected ? 1 : 0);
                }
                else
                {
                    gadget.IsSelected = true;
                    _grab = gadget;
                }
                break;
            case WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider when button == UiMouseButton.Left:
                gadget.Slider!.Dragging = true;
                SetSliderFromPointer(gadget, x, y);
                break;
            case WndWindowType.ScrollListBox:
                ListMouseDown(gadget, x, y, button);
                break;
            case WndWindowType.ComboBox when button == UiMouseButton.Left:
                ComboMouseDown(gadget, x, y);
                break;
            case WndWindowType.EntryField when button == UiMouseButton.Left:
                EntryMouseDown(gadget, gadget.TextEntry!, UiGadgetGeometry.TextOriginX(gadget, gadget.Rect), x);
                break;
            case WndWindowType.TabControl when button == UiMouseButton.Left:
                var panes = gadget.Children.Count(child => child.Type == WndWindowType.TabPane);
                for (var index = 0; index < panes; index++)
                    if (UiGadgetGeometry.TabHeader(gadget, index).Contains(x, y)) gadget.SelectTab(index);
                break;
        }
        return true;
    }

    public bool MouseUp(int x, int y, UiMouseButton button)
    {
        var grabbed = _grab;
        var target = InputTarget(x, y);
        if (button == UiMouseButton.Right)
        {
            if (grabbed is { Type: WndWindowType.PushButton } rightButton && (rightButton.Status & WndStatus.RightClick) != 0
                && (rightButton.Status & WndStatus.CheckLike) == 0 && rightButton.IsSelected)
            {
                rightButton.IsSelected = false;
                if (rightButton.Rect.Contains(x, y)) Emit(UiEventKind.ButtonSelectedRight, rightButton);
                _grab = null;
            }
            return target is not null;
        }
        if (button != UiMouseButton.Left) return target is not null;
        _grab = null;
        if (grabbed is null) return target is not null;
        // The grab window gets the up only when the pointer is still inside it (winProcessMouseEvent, GWM_LEFT_UP).
        var inside = grabbed.HitRect.Contains(x, y);
        switch (grabbed.Type)
        {
            case WndWindowType.PushButton when (grabbed.Status & WndStatus.CheckLike) == 0:
                if (inside && grabbed.IsSelected && (grabbed.Status & WndStatus.OnMouseDown) == 0) Emit(UiEventKind.ButtonSelected, grabbed);
                grabbed.IsSelected = false;
                break;
            case WndWindowType.CheckBox when inside && grabbed.IsEffectivelyEnabled:
                grabbed.IsSelected = !grabbed.IsSelected;
                Emit(UiEventKind.ButtonSelected, grabbed, grabbed.IsSelected ? 1 : 0);
                break;
            case WndWindowType.RadioButton when inside && grabbed.IsEffectivelyEnabled:
                SelectRadio(grabbed);
                break;
            case WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider when grabbed.Slider!.Dragging:
                grabbed.Slider.Dragging = false;
                Emit(UiEventKind.SliderDone, grabbed, grabbed.Slider.Value);
                break;
        }
        MouseMove(x, y);
        return target is not null || inside;
    }

    /// <summary>
    /// The pointer no longer belongs to this tree (another screen covers it): the hovered window gets its leaving
    /// message and loses hilite and pressed state, a drag ends without a value message. The original sends
    /// <c>GWM_MOUSE_LEAVING</c> to the old mouse region as soon as a modal window takes the pointer.
    /// </summary>
    public void PointerLeft()
    {
        if (_grab?.Slider is { } slider) slider.Dragging = false;
        _grab = null;
        if (_hover is null) return;
        Leave(_hover);
        _hover = null;
    }

    /// <summary>Wheel notches (positive = up) scroll the list box or open combo box under the pointer.</summary>
    public bool MouseWheel(int x, int y, int notches)
    {
        var target = InputTarget(x, y);
        if (target is null) return false;
        var gadget = Interactive(target);
        if (gadget?.ListBox is { } list) list.TopIndex = Math.Clamp(list.TopIndex - notches, 0, UiGadgetGeometry.MaxTopIndex(gadget));
        else if (gadget?.ComboBox is { IsOpen: true } combo)
            combo.TopIndex = Math.Clamp(combo.TopIndex - notches, 0, Math.Max(0, combo.Entries.Count - UiGadgetGeometry.ComboVisibleRows(gadget)));
        return true;
    }

    private void SetSliderFromPointer(UiWindow window, int x, int y)
    {
        var value = UiGadgetGeometry.SliderValueAt(window, x, y);
        if (value == window.Slider!.Value) return;
        window.Slider.Value = value;
        Emit(UiEventKind.SliderTrack, window, value);
    }

    private void SelectRadio(UiWindow radio)
    {
        if (radio.IsSelected) return;
        Emit(UiEventKind.ButtonSelected, radio, 1);
        radio.SetChecked(true);
    }

    private void ListMouseDown(UiWindow window, int x, int y, UiMouseButton button)
    {
        var list = window.ListBox!;
        if (button == UiMouseButton.Left && list.Data.ScrollBar && x >= UiGadgetGeometry.ListContent(window).Right)
        {
            var maxTop = UiGadgetGeometry.MaxTopIndex(window);
            if (UiGadgetGeometry.UpButton(window).Contains(x, y)) list.TopIndex = Math.Max(0, list.TopIndex - 1);
            else if (UiGadgetGeometry.DownButton(window).Contains(x, y)) list.TopIndex = Math.Min(maxTop, list.TopIndex + 1);
            else
            {
                var track = UiGadgetGeometry.ScrollTrack(window);
                if (track.Height > 0) list.TopIndex = Math.Clamp((int)((long)(y - track.Y) * (maxTop + 1) / track.Height), 0, maxTop);
            }
            return;
        }
        var content = UiGadgetGeometry.ListContent(window);
        var row = list.TopIndex + (y - content.Y) / UiGadgetGeometry.RowHeight(window);
        if (row >= list.Rows.Count) row = -1;
        if (button == UiMouseButton.Right)
        {
            Emit(UiEventKind.ListRightClicked, window, row);
            return;
        }
        if (button != UiMouseButton.Left || row < 0) return;
        var doubleClick = _lastClickWindow == window && _lastClickRow == row && Now - _lastClickTime <= Services.DoubleClickMs;
        list.Select(row, list.Data.MultiSelect);
        Emit(UiEventKind.ListSelected, window, list.Data.MultiSelect ? row : list.SelectedIndex);
        if (doubleClick)
        {
            Emit(UiEventKind.ListDoubleClicked, window, row);
            _lastClickWindow = null;
        }
        else
        {
            _lastClickWindow = window;
            _lastClickRow = row;
            _lastClickTime = Now;
        }
    }

    private void ComboMouseDown(UiWindow window, int x, int y)
    {
        var combo = window.ComboBox!;
        if (combo.IsOpen && UiGadgetGeometry.ComboDropDown(window).Contains(x, y))
        {
            var index = combo.TopIndex + (y - window.Rect.Bottom) / UiGadgetGeometry.RowHeight(window);
            if (index >= 0 && index < combo.Entries.Count) SelectComboEntry(window, index);
            CloseCombo();
            return;
        }
        if (combo.Data.IsEditable && UiGadgetGeometry.ComboEdit(window).Contains(x, y) && !combo.IsOpen)
        {
            EntryMouseDown(window, combo.Edit, UiGadgetGeometry.TextOriginX(window, UiGadgetGeometry.ComboEdit(window)), x);
            return;
        }
        if (combo.IsOpen) CloseCombo();
        else OpenCombo(window);
    }

    private void OpenCombo(UiWindow window)
    {
        if (_openCombo is not null && _openCombo != window) CloseCombo();
        var combo = window.ComboBox!;
        combo.IsOpen = true;
        var visible = UiGadgetGeometry.ComboVisibleRows(window);
        combo.TopIndex = Math.Clamp(combo.SelectedIndex - visible + 1, 0, Math.Max(0, combo.Entries.Count - visible));
        _openCombo = window;
    }

    private void CloseCombo()
    {
        if (_openCombo is null) return;
        _openCombo.ComboBox!.IsOpen = false;
        _openCombo = null;
    }

    private void SelectComboEntry(UiWindow window, int index)
    {
        var combo = window.ComboBox!;
        if (index == combo.SelectedIndex) return;
        window.SetComboSelection(index);
        Emit(UiEventKind.ComboSelected, window, index);
    }

    private void EntryMouseDown(UiWindow window, UiTextEditState edit, int originX, int x)
    {
        var measure = Services.TextMeasure;
        var text = edit.DisplayText;
        var caret = text.Length;
        for (var i = 0; i < text.Length; i++)
        {
            var left = measure.MeasureWidth(text[..i], window.Font);
            var right = measure.MeasureWidth(text[..(i + 1)], window.Font);
            if (x < originX + (left + right) / 2)
            {
                caret = i;
                break;
            }
        }
        edit.MoveCaret(caret, false);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Keyboard

    public static bool IsFocusable(UiWindow window)
        => window.Type is WndWindowType.PushButton or WndWindowType.CheckBox or WndWindowType.RadioButton
               or WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider or WndWindowType.ScrollListBox
               or WndWindowType.EntryField or WndWindowType.ComboBox
           && (window.Status & (WndStatus.NoFocus | WndStatus.NoInput)) == 0
           && window.IsVisible && window.IsEffectivelyEnabled;

    /// <summary>
    /// Focusable controls in reading order: top edge, then left edge, then creation order. Tab and Shift+Tab cycle
    /// through this list, which makes every enabled control reachable without a mouse.
    /// </summary>
    public IReadOnlyList<UiWindow> FocusOrder()
        => _all.Where(IsFocusable).OrderBy(window => window.Rect.Y).ThenBy(window => window.Rect.X).ThenBy(window => window.Order).ToArray();

    public void SetFocus(UiWindow? window)
    {
        if (window == Focus) return;
        if (Focus is { } old)
        {
            if (old.Type == WndWindowType.PushButton && old != _hover) old.IsHilited = false;
            Emit(UiEventKind.FocusChange, old, 0);
        }
        Focus = window;
        if (window is null) return;
        // GWM_INPUT_FOCUS hilites buttons, which is what keyboard users see as the focus mark.
        if (window.Type == WndWindowType.PushButton) window.IsHilited = true;
        Emit(UiEventKind.FocusChange, window, 1);
    }

    public bool MoveFocus(bool backwards)
    {
        var order = FocusOrder();
        if (order.Count == 0) return false;
        var index = Focus is null ? -1 : IndexOf(order, Focus);
        var next = index < 0
            ? (backwards ? order.Count - 1 : 0)
            : (index + (backwards ? order.Count - 1 : 1)) % order.Count;
        SetFocus(order[next]);
        return true;
    }

    private static int IndexOf(IReadOnlyList<UiWindow> list, UiWindow window)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == window) return i;
        return -1;
    }

    /// <summary>
    /// A key press. The focused gadget handles it first; Tab/Shift+Tab move the focus; other keys travel from the focus
    /// (or, without focus, from the topmost visible top-level window) up to the nearest <c>INPUTCALLBACK</c> as a
    /// <see cref="UiEventKind.Key"/> event. Returns true when the UI consumed the key.
    /// </summary>
    public bool KeyDown(UiKey key, UiModifiers modifiers = UiModifiers.None)
    {
        var shift = (modifiers & UiModifiers.Shift) != 0;
        if (Focus is { } focus && !IsFocusable(focus)) SetFocus(null);
        if (Focus is { } gadget && GadgetKey(gadget, key, shift)) return true;
        if (key == UiKey.Tab) return MoveFocus(shift);
        if (key == UiKey.Escape && _openCombo is not null)
        {
            CloseCombo();
            return true;
        }
        var source = Focus ?? RootsBottomToTop().LastOrDefault(root => root.IsVisible);
        if (source is null) return false;
        var received = KeyWouldBeReceived(source);
        Emit(UiEventKind.Key, source, (int)key);
        return received;
    }

    /// <summary>A typed character for the focused text entry or editable combo box.</summary>
    public bool Character(char c)
    {
        if (Focus is not { } focus) return false;
        var edit = focus.TextEntry ?? (focus.ComboBox is { Data.IsEditable: true } combo ? combo.Edit : null);
        if (edit is null) return false;
        if (edit.Insert(c)) TextChanged(focus, edit);
        return true;
    }

    private void TextChanged(UiWindow window, UiTextEditState edit)
        => Emit(window.Type == WndWindowType.ComboBox ? UiEventKind.ComboUpdateText : UiEventKind.EntryUpdateText, window, 0, edit.Text);

    private bool GadgetKey(UiWindow window, UiKey key, bool shift)
    {
        switch (window.Type)
        {
            case WndWindowType.PushButton:
                if (key is UiKey.Enter or UiKey.Space)
                {
                    if ((window.Status & WndStatus.CheckLike) != 0)
                    {
                        window.IsSelected = !window.IsSelected;
                        Emit(UiEventKind.ButtonSelected, window, window.IsSelected ? 1 : 0);
                    }
                    else Emit(UiEventKind.ButtonSelected, window);
                    return true;
                }
                return ArrowFocus(key);
            case WndWindowType.CheckBox:
                if (key is UiKey.Enter or UiKey.Space)
                {
                    window.IsSelected = !window.IsSelected;
                    Emit(UiEventKind.ButtonSelected, window, window.IsSelected ? 1 : 0);
                    return true;
                }
                return ArrowFocus(key);
            case WndWindowType.RadioButton:
                if (key is UiKey.Enter or UiKey.Space)
                {
                    SelectRadio(window);
                    return true;
                }
                return ArrowFocus(key);
            case WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider:
            {
                var delta = key switch { UiKey.Left or UiKey.Down => -1, UiKey.Right or UiKey.Up => 1, _ => 0 };
                if (delta == 0) return false;
                var slider = window.Slider!;
                var value = slider.Clamp(slider.Value + delta);
                if (value != slider.Value)
                {
                    slider.Value = value;
                    Emit(UiEventKind.SliderTrack, window, value);
                    Emit(UiEventKind.SliderDone, window, value);
                }
                return true;
            }
            case WndWindowType.ScrollListBox:
            {
                var list = window.ListBox!;
                if (list.Rows.Count == 0) return false;
                var visible = UiGadgetGeometry.VisibleRows(window);
                var current = list.SelectedIndex;
                var target = key switch
                {
                    UiKey.Up => current - 1,
                    UiKey.Down => current + 1,
                    UiKey.PageUp => current - visible,
                    UiKey.PageDown => current + visible,
                    UiKey.Home => 0,
                    UiKey.End => list.Rows.Count - 1,
                    _ => int.MinValue,
                };
                if (target == int.MinValue) return false;
                target = Math.Clamp(target, 0, list.Rows.Count - 1);
                if (target != current)
                {
                    list.Select(target, false);
                    if (target < list.TopIndex) list.TopIndex = target;
                    else if (target >= list.TopIndex + visible) list.TopIndex = target - visible + 1;
                    Emit(UiEventKind.ListSelected, window, target);
                }
                return true;
            }
            case WndWindowType.ComboBox:
            {
                var combo = window.ComboBox!;
                if (key is UiKey.Enter or UiKey.Space && !(combo.Data.IsEditable && key == UiKey.Space))
                {
                    if (combo.IsOpen) CloseCombo();
                    else OpenCombo(window);
                    return true;
                }
                if (key is UiKey.Up or UiKey.Down && combo.Entries.Count > 0)
                {
                    var index = Math.Clamp(combo.SelectedIndex + (key == UiKey.Up ? -1 : 1), 0, combo.Entries.Count - 1);
                    SelectComboEntry(window, index);
                    return true;
                }
                return combo.Data.IsEditable && EditKey(window, combo.Edit, key, shift);
            }
            case WndWindowType.EntryField:
            {
                var edit = window.TextEntry!;
                if (key == UiKey.Enter)
                {
                    Emit(UiEventKind.EntryEditDone, window, 0, edit.Text);
                    return true;
                }
                return EditKey(window, edit, key, shift);
            }
        }
        return false;
    }

    private bool EditKey(UiWindow window, UiTextEditState edit, UiKey key, bool shift)
    {
        switch (key)
        {
            case UiKey.Left: edit.MoveCaret(edit.Caret - 1, shift); return true;
            case UiKey.Right: edit.MoveCaret(edit.Caret + 1, shift); return true;
            case UiKey.Home: edit.MoveCaret(0, shift); return true;
            case UiKey.End: edit.MoveCaret(edit.Text.Length, shift); return true;
            case UiKey.Backspace:
                if (edit.Backspace()) TextChanged(window, edit);
                return true;
            case UiKey.Delete:
                if (edit.Delete()) TextChanged(window, edit);
                return true;
        }
        return false;
    }

    /// <summary>Arrow keys on buttons move the focus like <c>winNextTab</c>/<c>winPrevTab</c> in the gadget input functions.</summary>
    private bool ArrowFocus(UiKey key) => key switch
    {
        UiKey.Down or UiKey.Right => MoveFocus(false),
        UiKey.Up or UiKey.Left => MoveFocus(true),
        _ => false,
    };
}
