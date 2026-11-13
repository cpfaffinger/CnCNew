using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// The gadgets of the modern skin (docs/UI-MODERN.md section 16): every window whose definition carries a <see cref="WndFlatRole"/> is drawn here, from the state families the derivation
/// wrote (entry 0 of a family: fill and border of that state), in the same order of the original's gadget functions, so input, tests and scripts see the same windows. Sizes are in
/// units of the window's own scale; nothing here touches a window of the original skin.
/// </summary>
public sealed partial class UiWindowTree
{
    private sealed partial class DrawBuilder
    {
        /// <summary>Soft edge around a panel: three outlines of falling strength, so a panel lifts off the battle behind it.</summary>
        private static readonly WndColor[] Shade = [new(0, 0, 0, 70), new(0, 0, 0, 42), new(0, 0, 0, 20)];

        private void DrawFlat(UiWindow window)
        {
            switch (window.Type)
            {
                case WndWindowType.PushButton: FlatButton(window); break;
                case WndWindowType.CheckBox: FlatCheckBox(window); break;
                case WndWindowType.RadioButton: FlatRadio(window); break;
                case WndWindowType.StaticText: DrawStaticText(window); break;
                case WndWindowType.EntryField: FlatEntry(window); break;
                case WndWindowType.ScrollListBox: FlatList(window); break;
                case WndWindowType.ComboBox: FlatCombo(window); break;
                case WndWindowType.HorizontalSlider: FlatSlider(window); break;
                case WndWindowType.VerticalSlider: FlatVerticalSlider(window); break;
                case WndWindowType.ProgressBar: FlatProgress(window); break;
                default: FlatPanel(window); break;
            }
        }

        /// <summary>A field draws a unit inside its window: fields that touch in the layout stand as cells with a gap between them.</summary>
        private static UiRect Cell(UiWindow window) => window.Rect.Inset(Math.Max(1, window.Transform.Y.Length(1)));

        private void FlatPanel(UiWindow window)
        {
            var entry = Set(window, WndDrawDataKind.Enabled)[0];
            if (window.Definition.Flat == WndFlatRole.Picture && entry.Image is { } picture)
            {
                var full = window.Transform.Map(window.Definition.Rect with { Left = 0, Top = 0, Right = window.Definition.Rect.CreationWidth, Bottom = window.Definition.Rect.CreationHeight });
                _commands.Add(new UiPushClip(window.Rect));
                _commands.Add(new UiImageDraw(picture, full, WndColor.White with { A = 215 }, false, false));
                _commands.Add(new UiPopClip());
                // The screen's title stands on a band of the panel colour: a picture is no ground for text.
                Fill(new UiRect(window.Rect.X, window.Rect.Y, window.Rect.Width, window.Transform.Y.Length(38)), entry.Color);
            }
            if (window.Definition.Flat is WndFlatRole.Panel or WndFlatRole.Picture && !window.Rect.IsEmpty)
            {
                for (var ring = 0; ring < Shade.Length; ring++)
                {
                    var grown = ring + 1;
                    _commands.Add(new UiBorderRect(new UiRect(window.Rect.X - grown, window.Rect.Y - grown, window.Rect.Width + 2 * grown, window.Rect.Height + 2 * grown), Shade[ring]));
                }
            }
            if (window.Definition.Flat == WndFlatRole.Picture) _commands.Add(new UiBorderRect(window.Rect, entry.BorderColor));
            else Box(window.Rect, entry);
        }

        private void FlatButton(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            if ((window.Status & WndStatus.UseOverlayStates) != 0)
            {
                OverlayButton(window, set);
                return;
            }
            Box(window.Rect, set[window.IsSelected ? 1 : 0]);
            // The hilited or pressed button carries the accent as a bar at its left edge.
            if (StateOf(window) == DrawState.Hilite || window.IsSelected)
                Fill(new UiRect(window.Rect.X, window.Rect.Y, window.Transform.X.Length(3), window.Rect.Height), set[1].BorderColor);
            Text(window, window.Rect, UiTextAlignment.Center, window.Rect.Width, true, tree.Viewport.Bounds);
        }

        private void FlatCheckBox(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            var side = Math.Min(window.Rect.Height, window.Transform.Y.Length(16));
            var box = new UiRect(window.Rect.X, window.Rect.Y + (window.Rect.Height - side) / 2, side, side);
            Box(box, set[window.IsSelected ? 2 : 1]);
            if (window.IsSelected)
            {
                var color = TextColors(window).Color;
                var (left, top, size) = ((float)box.X, (float)box.Y, (float)side);
                var width = Math.Max(1.5f, size / 8f);
                _commands.Add(new UiLineDraw(left + size * 0.22f, top + size * 0.52f, left + size * 0.42f, top + size * 0.74f, width, color));
                _commands.Add(new UiLineDraw(left + size * 0.42f, top + size * 0.74f, left + size * 0.80f, top + size * 0.28f, width, color));
            }
            var textArea = UiRect.FromEdges(Math.Min(window.Rect.Right, box.Right + window.Transform.X.Length(6)), window.Rect.Y, window.Rect.Right, window.Rect.Bottom);
            Text(window, textArea, UiTextAlignment.Left, 0, false, tree.Viewport.Bounds);
        }

        /// <summary>A radio button of the shell is one of a row of tabs: the selected one is filled and underlined in the accent.</summary>
        private void FlatRadio(UiWindow window)
        {
            var selected = window.IsSelected;
            var set = selected ? Set(window, WndDrawDataKind.Enabled, DrawState.Hilite) : Set(window, WndDrawDataKind.Enabled);
            var entry = set[selected ? 3 : 0];
            Box(window.Rect, entry);
            if (selected) Fill(new UiRect(window.Rect.X, window.Rect.Bottom - window.Transform.Y.Length(2), window.Rect.Width, window.Transform.Y.Length(2)), entry.BorderColor);
            Text(window, window.Rect, UiTextAlignment.Center, 0, false, tree.Viewport.Bounds);
        }

        private void FlatEntry(UiWindow window)
        {
            Box(Cell(window), Set(window, WndDrawDataKind.Enabled)[0]);
            EditText(window, window.TextEntry!, window.Rect, false);
        }

        private void FlatCombo(UiWindow window)
        {
            var combo = window.ComboBox!;
            var state = StateOf(window);
            Box(Cell(window), Set(window, WndDrawDataKind.ComboBoxEditBoxEnabled, state)[0]);
            var edit = UiGadgetGeometry.ComboEdit(window);
            if (combo.Data.IsEditable) EditText(window, combo.Edit, edit, false);
            else if (combo.SelectedIndex >= 0)
            {
                var selected = new UiTextEditState(0, false, false, false, false);
                selected.SetText(combo.Entries[combo.SelectedIndex]);
                EditText(window, selected, edit, true, combo.ColorOf(combo.SelectedIndex));
            }
            var button = UiGadgetGeometry.ComboButton(window);
            var (color, _) = TextColors(window);
            Fill(new UiRect(button.X, button.Y + button.Height / 5, 1, button.Height - 2 * (button.Height / 5)), Set(window, WndDrawDataKind.ComboBoxDropDownButtonEnabled, state)[0].BorderColor);
            Chevron(button, color, combo.IsOpen);
        }

        /// <summary>A small arrow in the middle of a square: down, or up when <paramref name="up"/>.</summary>
        private void Chevron(UiRect area, WndColor color, bool up)
        {
            var half = Math.Max(3f, Math.Min(area.Width, area.Height) / 6f);
            var (x, y) = (area.X + area.Width / 2f, area.Y + area.Height / 2f);
            var rise = up ? half / 2f : -half / 2f;
            var width = Math.Max(1.5f, half / 2.5f);
            _commands.Add(new UiLineDraw(x - half, y + rise, x, y - rise, width, color));
            _commands.Add(new UiLineDraw(x, y - rise, x + half, y + rise, width, color));
        }

        private void FlatDropDown(UiWindow window)
        {
            var combo = window.ComboBox!;
            var area = UiGadgetGeometry.ComboDropDown(window);
            var set = Set(window, WndDrawDataKind.ComboBoxListBoxEnabled, StateOf(window));
            var hover = tree.DropDownRowUnderPointer(window);
            var rowHeight = UiGadgetGeometry.RowHeight(window);
            var rows = UiGadgetGeometry.ComboVisibleRows(window);
            for (var ring = 0; ring < Shade.Length; ring++)
            {
                var grown = ring + 1;
                _commands.Add(new UiBorderRect(new UiRect(area.X - grown, area.Y - grown, area.Width + 2 * grown, area.Height + 2 * grown), Shade[ring]));
            }
            Box(area, set[0]);
            // The combo box under the pointer is hilited, and its hilite colour is what a selection is filled with: the rows write in the text colour.
            var color = window.Definition.TextColors.Enabled;
            var inner = area.Inset(1);
            _commands.Add(new UiPushClip(inner));
            for (var index = combo.TopIndex; index < Math.Min(combo.Entries.Count, combo.TopIndex + rows); index++)
            {
                var row = new UiRect(inner.X, area.Y + 1 + (index - combo.TopIndex) * rowHeight, inner.Width, rowHeight);
                if (index == combo.SelectedIndex)
                {
                    Fill(row, set[1].Color);
                    Fill(new UiRect(row.X, row.Y, window.Transform.X.Length(3), row.Height), set[2].Color);
                }
                else if (index == hover) Fill(row, set[2].BorderColor);
                if (color.IsVisible)
                    _commands.Add(new UiTextDraw(combo.Entries[index], null, window.Font, combo.ColorOf(index) is { IsVisible: true } own ? own : color, WndColor.Undefined,
                        UiGadgetGeometry.TextOriginX(window, row) + window.Transform.X.Length(3), row.Y + (row.Height - tree.Services.TextMeasure.LineHeight(window.Font)) / 2, UiTextAlignment.Left, row));
            }
            // More entries than rows: a thin bar at the right edge says where the list stands.
            if (combo.Entries.Count > rows)
            {
                var track = UiRect.FromEdges(inner.Right - window.Transform.X.Length(3), inner.Y, inner.Right, inner.Bottom);
                var height = Math.Max(track.Width, track.Height * rows / combo.Entries.Count);
                var travel = track.Height - height;
                var top = track.Y + travel * combo.TopIndex / Math.Max(1, combo.Entries.Count - rows);
                Fill(new UiRect(track.X, top, track.Width, height), set[2].Color);
            }
            _commands.Add(new UiPopClip());
        }

        private void FlatSlider(UiWindow window)
        {
            var slider = window.Slider!;
            var state = StateOf(window);
            var rect = window.Rect;
            var thumb = UiGadgetGeometry.SliderThumb(window);
            var height = Math.Max(2, window.Transform.Y.Length(4));
            var top = rect.Y + (rect.Height - height) / 2;
            var body = Set(window, WndDrawDataKind.Enabled)[0];
            var knob = Set(window, WndDrawDataKind.SliderThumbEnabled, state)[slider.Dragging ? 1 : 0];
            var center = thumb.X + thumb.Width / 2;
            Fill(new UiRect(rect.X, top, rect.Width, height), body.BorderColor);
            if (center > rect.X) Fill(new UiRect(rect.X, top, center - rect.X, height), knob.Color);
            Box(thumb, knob);
        }

        private void FlatVerticalSlider(UiWindow window)
        {
            var slider = window.Slider!;
            var state = StateOf(window);
            var rect = window.Rect;
            var thumb = UiGadgetGeometry.SliderThumb(window);
            var width = Math.Max(2, window.Transform.X.Length(4));
            var body = Set(window, WndDrawDataKind.Enabled)[0];
            var knob = Set(window, WndDrawDataKind.SliderThumbEnabled, state)[slider.Dragging ? 1 : 0];
            Fill(new UiRect(rect.X + (rect.Width - width) / 2, rect.Y, width, rect.Height), body.BorderColor);
            Box(thumb, knob);
        }

        private void FlatProgress(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            Box(window.Rect, set[0]);
            var fill = UiGadgetGeometry.ProgressFill(window);
            if (fill.Width > 0) Fill(fill.Inset(1), set[4].Color);
        }

        /// <summary>A list: rows with air around the text, every second row lighter, a thin rule between columns, the selected row filled with the accent at its left edge.</summary>
        private void FlatList(UiWindow window)
        {
            var list = window.ListBox!;
            var set = Set(window, WndDrawDataKind.Enabled);
            Box(window.Rect, set[0]);
            var content = UiGadgetGeometry.ListContent(window);
            var rowHeight = UiGadgetGeometry.RowHeight(window);
            var visible = UiGadgetGeometry.VisibleRows(window);
            var (color, _) = TextColors(window);
            var lineHeight = tree.Services.TextMeasure.LineHeight(window.Font);
            var inner = content.Inset(1);
            _commands.Add(new UiPushClip(inner));
            var rule = set[2].BorderColor;
            var columnsOfList = UiGadgetGeometry.Columns(window, content);
            if (rule.IsVisible)
                for (var column = 1; column < columnsOfList.Length; column++)
                    Fill(new UiRect(columnsOfList[column].X, inner.Y, 1, inner.Height), rule);
            for (var index = list.TopIndex; index < Math.Min(list.Rows.Count, list.TopIndex + visible + 1); index++)
            {
                var row = new UiRect(content.X, content.Y + 1 + (index - list.TopIndex) * rowHeight, content.Width, rowHeight);
                if ((index & 1) == 1) Fill(row, set[2].Color);
                if (list.Selection.Contains(index))
                {
                    Fill(row, set[1].Color);
                    Fill(new UiRect(row.X, row.Y, window.Transform.X.Length(3), row.Height), set[1].BorderColor);
                }
                var columns = UiGadgetGeometry.Columns(window, row);
                var cells = list.Rows[index].Cells;
                var rowColor = list.Rows[index].Color.IsUndefined ? color : list.Rows[index].Color;
                for (var column = 0; column < Math.Min(columns.Length, cells.Length); column++)
                    if (cells[column].Length > 0 && rowColor.IsVisible)
                        _commands.Add(new UiTextDraw(cells[column], null, window.Font, rowColor, WndColor.Undefined,
                            UiGadgetGeometry.TextOriginX(window, columns[column]), row.Y + (rowHeight - lineHeight) / 2, UiTextAlignment.Left, columns[column].Intersect(content)));
            }
            _commands.Add(new UiPopClip());
            if (!list.Data.ScrollBar) return;
            var state = StateOf(window);
            var up = UiGadgetGeometry.UpButton(window);
            var down = UiGadgetGeometry.DownButton(window);
            Chevron(up, TextColors(window).Color, true);
            Chevron(down, TextColors(window).Color, false);
            var track = UiGadgetGeometry.ScrollTrack(window);
            var well = Math.Max(2, track.Width / 4);
            Fill(new UiRect(track.X + (track.Width - well) / 2, track.Y, well, track.Height), Set(window, WndDrawDataKind.ListBoxEnabledSlider, state)[0].Color);
            if (list.Rows.Count > visible) Box(UiGadgetGeometry.ScrollThumb(window).Inset(Math.Max(1, track.Width / 5)), Set(window, WndDrawDataKind.SliderThumbEnabled, state)[0]);
        }
    }
}
