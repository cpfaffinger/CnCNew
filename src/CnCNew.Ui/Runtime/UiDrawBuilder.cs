using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// Produces the draw list of a tree. Order and state selection follow <c>GameWindowManager::drawWindow</c>/<c>winRepaint</c>
/// (top-level layers BELOW, normal, ABOVE; a window before its children; <c>SEE_THRU</c> skips only the window itself) and the
/// W3D gadget draw functions (<c>W3DGameWinDefaultDraw</c>, <c>W3DPushButton.cpp</c>, <c>W3DCheckBox.cpp</c>, <c>W3DRadioButton.cpp</c>,
/// <c>W3DStaticText.cpp</c>, <c>W3DListBox.cpp</c>, <c>W3DComboBox.cpp</c>, <c>W3DTextEntry.cpp</c>, <c>W3DProgressBar.cpp</c>,
/// <c>W3D*Slider.cpp</c>): the draw data set is chosen by state (disabled, hilited, enabled) and the gadget-specific entry index.
/// </summary>
public sealed partial class UiWindowTree
{
    public UiDrawList BuildDrawList() => new DrawBuilder(this).Build();

    private enum DrawState
    {
        Enabled,
        Disabled,
        Hilite,
    }

    private sealed class DrawBuilder(UiWindowTree tree)
    {
        private const int MaximumTiles = 256;
        private readonly ImmutableArray<UiDrawCommand>.Builder _commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        private readonly SortedDictionary<string, int> _custom = new(StringComparer.Ordinal);
        private int _windows, _unresolvedLabels, _unresolvedSizes, _border;

        public UiDrawList Build()
        {
            foreach (var root in tree.RootsBottomToTop()) Draw(root);
            if (tree._openCombo is { IsVisible: true } combo) DrawDropDown(combo);
            return new UiDrawList(_commands.ToImmutable(), new UiDrawStatistics(_windows, _unresolvedLabels, _unresolvedSizes, _border,
                _custom.ToImmutableSortedDictionary(StringComparer.Ordinal)));
        }

        private void Draw(UiWindow window)
        {
            if (window.IsHidden) return;
            if ((window.Status & WndStatus.SeeThru) == 0)
            {
                _windows++;
                if ((window.Status & WndStatus.Border) != 0) _border++;
                var callback = window.Definition.Callbacks.Draw;
                if (callback is null || callback == UiBuiltinCallbacks.PushButtonImageDraw) DrawGadget(window);
                else if (callback == UiBuiltinCallbacks.W3DDefaultDraw) DrawBackground(window, Set(window, WndDrawDataKind.Enabled), window.Rect);
                else if (callback is not (UiBuiltinCallbacks.NoDraw or UiBuiltinCallbacks.DefaultDraw))
                {
                    _commands.Add(new UiCustomDraw(callback, window.Name, window.Rect));
                    _custom[callback] = _custom.GetValueOrDefault(callback) + 1;
                }
            }
            foreach (var child in window.Children) Draw(child);
        }

        private static DrawState StateOf(UiWindow window)
            => !window.IsEnabled ? DrawState.Disabled : window.IsHilited ? DrawState.Hilite : DrawState.Enabled;

        /// <summary>
        /// The set of <paramref name="enabledKind"/>'s family for the window state; every family of
        /// <see cref="WndDrawDataKind"/> lists Enabled, Disabled, Hilite consecutively.
        /// </summary>
        private static WndDrawDataSet Set(UiWindow window, WndDrawDataKind enabledKind, DrawState? state = null)
            => window.Definition.GetDrawData(enabledKind + (int)(state ?? StateOf(window)));

        private void DrawGadget(UiWindow window)
        {
            switch (window.Type)
            {
                case WndWindowType.PushButton: DrawPushButton(window); break;
                case WndWindowType.CheckBox: DrawCheckBox(window); break;
                case WndWindowType.RadioButton: DrawRadioButton(window); break;
                case WndWindowType.StaticText: DrawStaticText(window); break;
                case WndWindowType.EntryField: DrawEntry(window); break;
                case WndWindowType.ScrollListBox: DrawListBox(window); break;
                case WndWindowType.ComboBox: DrawComboBox(window); break;
                case WndWindowType.HorizontalSlider or WndWindowType.VerticalSlider: DrawSlider(window); break;
                case WndWindowType.ProgressBar: DrawProgressBar(window); break;
                case WndWindowType.TabControl: DrawTabControl(window); break;
                default: DrawBackground(window, Set(window, WndDrawDataKind.Enabled), window.Rect); break;
            }
        }

        /// <summary><c>W3DGameWinDefaultDraw</c>: image 0 with <c>IMAGE</c> status, else border and fill of entry 0.</summary>
        private void DrawBackground(UiWindow window, WndDrawDataSet set, UiRect rect, int index = 0)
        {
            if ((window.Status & WndStatus.Image) != 0)
            {
                if (set[index].Image is { } image) Image(image, Offset(window, rect));
            }
            else Box(rect, set[index]);
        }

        private static UiRect Offset(UiWindow window, UiRect rect)
            => rect with { X = rect.X + window.Definition.ImageOffsetX, Y = rect.Y + window.Definition.ImageOffsetY };

        private void DrawPushButton(UiWindow window)
        {
            var state = StateOf(window);
            var set = Set(window, WndDrawDataKind.Enabled);
            var selected = window.IsSelected;
            if ((window.Status & WndStatus.Image) != 0)
            {
                if (window.Definition.GetDrawData(WndDrawDataKind.Enabled)[5].Image is not null)
                    ThreePiece(Offset(window, window.Rect), set[selected ? 1 : 0].Image, set[selected ? 3 : 5].Image, set[selected ? 4 : 6].Image, true);
                else
                {
                    // W3DGadgetPushButtonImageDrawOne: enabled + selected uses the hilite-selected image.
                    var image = state switch
                    {
                        DrawState.Disabled => set[selected ? 1 : 0].Image,
                        DrawState.Hilite => set[selected ? 1 : 0].Image,
                        _ => selected ? Set(window, WndDrawDataKind.Enabled, DrawState.Hilite)[1].Image : set[0].Image,
                    };
                    if (image is not null) Image(image, Offset(window, window.Rect));
                }
            }
            else Box(window.Rect, set[selected ? 1 : 0]);
            Text(window, window.Rect, UiTextAlignment.Center, window.Rect.Width, true);
        }

        private void DrawCheckBox(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            var inset = window.Transform.Y.Length(3);
            var side = Math.Max(0, window.Rect.Height - 2 * inset);
            var box = new UiRect(window.Rect.X + window.Definition.ImageOffsetX, window.Rect.Y + inset, side, side);
            var entry = set[window.IsSelected ? 2 : 1];
            if ((window.Status & WndStatus.Image) != 0)
            {
                if (entry.Image is { } image) Image(image, box);
            }
            else
            {
                Box(box, entry);
                if (window.IsSelected) Fill(box.Inset(Math.Max(1, side / 4)), TextColors(window).Color);
            }
            var textArea = UiRect.FromEdges(Math.Min(window.Rect.Right, window.Rect.X + window.Rect.Height), window.Rect.Y, window.Rect.Right, window.Rect.Bottom);
            Text(window, textArea, UiTextAlignment.Left, 0, false);
        }

        private void DrawRadioButton(UiWindow window)
        {
            var selected = window.IsSelected;
            var set = selected ? Set(window, WndDrawDataKind.Enabled, DrawState.Hilite) : Set(window, WndDrawDataKind.Enabled);
            var first = selected ? 3 : 0;
            if ((window.Status & WndStatus.Image) != 0)
                ThreePiece(Offset(window, window.Rect), set[first].Image, set[first + 1].Image, set[first + 2].Image, true);
            else Box(window.Rect, set[first]);
            Text(window, window.Rect, UiTextAlignment.Center, window.Rect.Width, true);
        }

        /// <summary><c>drawStaticTextText</c>: wrap at width - 10, left margin 7 or centered, always vertically centered.</summary>
        private void DrawStaticText(UiWindow window)
        {
            DrawBackground(window, Set(window, WndDrawDataKind.Enabled), window.Rect);
            var centered = window.Definition.StaticText?.Centered ?? false;
            var margin = window.Transform.X.Length(7);
            var area = centered ? window.Rect : UiRect.FromEdges(Math.Min(window.Rect.Right, window.Rect.X + margin), window.Rect.Y, window.Rect.Right, window.Rect.Bottom);
            Text(window, area, centered ? UiTextAlignment.Center : UiTextAlignment.Left, window.Rect.Width - window.Transform.X.Length(10), true);
        }

        private void DrawEntry(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            if ((window.Status & WndStatus.Image) != 0) ThreePiece(Offset(window, window.Rect), set[0].Image, set[2].Image, set[1].Image, true);
            else Box(window.Rect, set[0]);
            EditText(window, window.TextEntry!, window.Rect);
        }

        private void EditText(UiWindow window, UiTextEditState edit, UiRect area)
        {
            var (color, shadow) = TextColors(window);
            var measure = tree.Services.TextMeasure;
            var x = UiGadgetGeometry.TextOriginX(window, area);
            var height = measure.LineHeight(window.Font);
            var y = area.Y + area.Height / 2 - height / 2;
            var text = edit.DisplayText;
            _commands.Add(new UiPushClip(area));
            if (edit.HasSelection)
            {
                var start = x + measure.MeasureWidth(text[..edit.SelectionStart], window.Font);
                var end = x + measure.MeasureWidth(text[..edit.SelectionEnd], window.Font);
                Fill(new UiRect(start, y, end - start, height), window.Definition.TextColors.Hilite);
            }
            if (text.Length > 0 && color.IsVisible)
                _commands.Add(new UiTextDraw(text, null, window.Font, color, shadow, x, y, UiTextAlignment.Left, area));
            if (window.IsFocused)
                Fill(new UiRect(x + measure.MeasureWidth(text[..edit.Caret], window.Font), y, window.Transform.X.Length(1), height), color);
            _commands.Add(new UiPopClip());
        }

        private void DrawListBox(UiWindow window)
        {
            var list = window.ListBox!;
            var set = Set(window, WndDrawDataKind.Enabled);
            DrawBackground(window, set, window.Rect);
            var content = UiGadgetGeometry.ListContent(window);
            var rowHeight = UiGadgetGeometry.RowHeight(window);
            var visible = UiGadgetGeometry.VisibleRows(window);
            var (color, shadow) = TextColors(window);
            _commands.Add(new UiPushClip(content));
            for (var index = list.TopIndex; index < Math.Min(list.Rows.Count, list.TopIndex + visible + 1); index++)
            {
                var row = new UiRect(content.X, content.Y + (index - list.TopIndex) * rowHeight, content.Width, rowHeight);
                if (list.Selection.Contains(index)) Highlight(window, set, row);
                var columns = UiGadgetGeometry.Columns(window, row);
                var cells = list.Rows[index].Cells;
                var rowColor = list.Rows[index].Color.IsUndefined ? color : list.Rows[index].Color;
                for (var column = 0; column < Math.Min(columns.Length, cells.Length); column++)
                    if (cells[column].Length > 0 && rowColor.IsVisible)
                        _commands.Add(new UiTextDraw(cells[column], null, window.Font, rowColor, shadow,
                            UiGadgetGeometry.TextOriginX(window, columns[column]), row.Y, UiTextAlignment.Left, columns[column].Intersect(content)));
            }
            _commands.Add(new UiPopClip());
            if (!list.Data.ScrollBar) return;
            var state = StateOf(window);
            DrawBackground(window, Set(window, WndDrawDataKind.ListBoxEnabledUpButton, state), UiGadgetGeometry.UpButton(window));
            DrawBackground(window, Set(window, WndDrawDataKind.ListBoxEnabledDownButton, state), UiGadgetGeometry.DownButton(window));
            DrawBackground(window, Set(window, WndDrawDataKind.ListBoxEnabledSlider, state), UiGadgetGeometry.ScrollTrack(window));
            DrawBackground(window, Set(window, WndDrawDataKind.SliderThumbEnabled, state), UiGadgetGeometry.ScrollThumb(window));
        }

        /// <summary>Selected row: image entries 1 (left), 3 (center), 2 (right) or the color of entry 1.</summary>
        private void Highlight(UiWindow window, WndDrawDataSet set, UiRect row)
        {
            if ((window.Status & WndStatus.Image) != 0 && set[3].Image is not null) ThreePiece(row, set[1].Image, set[3].Image, set[2].Image, true);
            else Box(row, set[1]);
        }

        private void DrawComboBox(UiWindow window)
        {
            var combo = window.ComboBox!;
            var state = StateOf(window);
            DrawBackground(window, Set(window, WndDrawDataKind.Enabled), window.Rect);
            var edit = UiGadgetGeometry.ComboEdit(window);
            DrawBackground(window, Set(window, WndDrawDataKind.ComboBoxEditBoxEnabled, state), edit);
            if (combo.Data.IsEditable) EditText(window, combo.Edit, edit);
            else if (combo.SelectedIndex >= 0)
            {
                var (color, shadow) = TextColors(window);
                var height = tree.Services.TextMeasure.LineHeight(window.Font);
                if (color.IsVisible)
                    _commands.Add(new UiTextDraw(combo.Entries[combo.SelectedIndex], null, window.Font, color, shadow,
                        UiGadgetGeometry.TextOriginX(window, edit), edit.Y + edit.Height / 2 - height / 2, UiTextAlignment.Left, edit));
            }
            DrawBackground(window, Set(window, WndDrawDataKind.ComboBoxDropDownButtonEnabled, state), UiGadgetGeometry.ComboButton(window));
        }

        private void DrawDropDown(UiWindow window)
        {
            var combo = window.ComboBox!;
            var area = UiGadgetGeometry.ComboDropDown(window);
            var set = Set(window, WndDrawDataKind.ComboBoxListBoxEnabled, StateOf(window));
            DrawBackground(window, set, area);
            var rowHeight = UiGadgetGeometry.RowHeight(window);
            var (color, shadow) = TextColors(window);
            _commands.Add(new UiPushClip(area));
            for (var index = combo.TopIndex; index < Math.Min(combo.Entries.Count, combo.TopIndex + UiGadgetGeometry.ComboVisibleRows(window)); index++)
            {
                var row = new UiRect(area.X, area.Y + (index - combo.TopIndex) * rowHeight, area.Width, rowHeight);
                if (index == combo.SelectedIndex) Highlight(window, set, row);
                if (color.IsVisible)
                    _commands.Add(new UiTextDraw(combo.Entries[index], null, window.Font, color, shadow,
                        UiGadgetGeometry.TextOriginX(window, row), row.Y, UiTextAlignment.Left, row));
            }
            _commands.Add(new UiPopClip());
        }

        private void DrawSlider(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            var horizontal = window.Type == WndWindowType.HorizontalSlider;
            if ((window.Status & WndStatus.Image) != 0) ThreePiece(Offset(window, window.Rect), set[0].Image, set[2].Image, set[1].Image, horizontal);
            else Box(window.Rect, set[0]);
            DrawBackground(window, Set(window, WndDrawDataKind.SliderThumbEnabled), UiGadgetGeometry.SliderThumb(window));
        }

        private void DrawProgressBar(UiWindow window)
        {
            var set = Set(window, WndDrawDataKind.Enabled);
            var fill = UiGadgetGeometry.ProgressFill(window);
            if ((window.Status & WndStatus.Image) != 0)
            {
                ThreePiece(window.Rect, set[0].Image, set[2].Image, set[1].Image, true);
                if (fill.Width <= 0) return;
                _commands.Add(new UiPushClip(fill));
                ThreePiece(window.Rect, set[4].Image, set[6].Image, set[5].Image, true);
                _commands.Add(new UiPopClip());
            }
            else
            {
                Box(window.Rect, set[0]);
                if (fill.Width > 0) Box(fill, set[4]);
            }
        }

        private void DrawTabControl(UiWindow window)
        {
            DrawBackground(window, Set(window, WndDrawDataKind.Enabled), window.Rect);
            var panes = window.Children.Count(child => child.Type == WndWindowType.TabPane);
            for (var index = 0; index < panes; index++)
            {
                var active = index == window.TabControl!.ActiveTab;
                Box(UiGadgetGeometry.TabHeader(window, index), Set(window, WndDrawDataKind.Enabled, active ? DrawState.Hilite : StateOf(window))[0]);
            }
        }

        /// <summary>
        /// A left/center/right (or top/center/bottom) bar: the end pieces at their image size, the center tiled between
        /// them under a clip (<c>W3DGadgetPushButtonImageDrawThree</c>, <c>W3DGadgetRadioButtonImageDraw</c>). Unknown image
        /// sizes are counted; ends then take half the height and the center is stretched.
        /// </summary>
        private void ThreePiece(UiRect rect, string? start, string? center, string? end, bool horizontal)
        {
            if (start is null || center is null || end is null) return;
            var length = horizontal ? rect.Width : rect.Height;
            var origin = horizontal ? rect.X : rect.Y;
            var startSize = PieceLength(start, rect, horizontal);
            var endSize = PieceLength(end, rect, horizontal);
            var centerStart = origin + startSize;
            var centerEnd = origin + length - endSize;
            UiRect Span(int from, int to) => horizontal ? UiRect.FromEdges(from, rect.Y, to, rect.Bottom) : UiRect.FromEdges(rect.X, from, rect.Right, to);
            if (centerEnd <= centerStart)
            {
                Image(start, Span(origin, origin + length / 2));
                Image(end, Span(origin + length / 2, origin + length));
                return;
            }
            var tile = tree.Services.Images is { } images && images.TryGetSize(center, out var width, out var height) ? (horizontal ? width : height) : 0;
            if (tile <= 0)
            {
                _unresolvedSizes++;
                Image(center, Span(centerStart, centerEnd));
            }
            else if ((centerEnd - centerStart + tile - 1) / tile > MaximumTiles) Image(center, Span(centerStart, centerEnd));
            else
            {
                _commands.Add(new UiPushClip(Span(centerStart, centerEnd)));
                for (var position = centerStart; position < centerEnd; position += tile) Image(center, Span(position, position + tile));
                _commands.Add(new UiPopClip());
            }
            Image(start, Span(origin, centerStart));
            Image(end, Span(centerEnd, origin + length));
        }

        private int PieceLength(string image, UiRect rect, bool horizontal)
        {
            if (tree.Services.Images is { } images && images.TryGetSize(image, out var width, out var height))
                return horizontal ? width : height;
            _unresolvedSizes++;
            return horizontal ? rect.Height / 2 : rect.Width / 2;
        }

        private (WndColor Color, WndColor Shadow) TextColors(UiWindow window)
        {
            var colors = window.Definition.TextColors;
            return StateOf(window) switch
            {
                DrawState.Disabled => (colors.Disabled, colors.DisabledBorder),
                DrawState.Hilite => (colors.Hilite, colors.HiliteBorder),
                _ => (colors.Enabled, colors.EnabledBorder),
            };
        }

        /// <summary>Lays out the window text (label resolved through the text source) as vertically centered lines.</summary>
        private void Text(UiWindow window, UiRect area, UiTextAlignment alignment, int wrapWidth, bool wrap)
        {
            if (window.Text is not { } value || value.Value.Length == 0) return;
            var (color, shadow) = TextColors(window);
            if (!color.IsVisible) return;
            string text;
            string? key = null;
            if (value.IsLabel)
            {
                key = value.Value;
                if (tree.Services.Text is { } source && source.TryResolve(value.Value, out var resolved)) text = resolved;
                else
                {
                    text = value.Value;
                    _unresolvedLabels++;
                }
            }
            else text = value.Value;
            var measure = tree.Services.TextMeasure;
            var lines = UiTextLayout.Wrap(text, wrap ? wrapWidth : 0, window.Font, measure);
            var lineHeight = measure.LineHeight(window.Font);
            var y = area.Y + area.Height / 2 - lines.Length * lineHeight / 2;
            var widths = lines.Select(line => measure.MeasureWidth(line, window.Font)).ToArray();
            var block = widths.Length == 0 ? 0 : widths.Max();
            var wrapCentered = (window.Status & WndStatus.WrapCentered) != 0;
            for (var index = 0; index < lines.Length; index++, y += lineHeight)
            {
                if (lines[index].Length == 0) continue;
                var x = alignment == UiTextAlignment.Left ? area.X
                    : wrapCentered ? area.X + area.Width / 2 - widths[index] / 2
                    : area.X + area.Width / 2 - block / 2;
                _commands.Add(new UiTextDraw(lines[index], key, window.Font, color, shadow, x, y, alignment, window.Rect));
            }
        }

        private void Box(UiRect rect, WndDrawData data)
        {
            if (rect.IsEmpty) return;
            if (data.BorderColor.IsVisible) _commands.Add(new UiBorderRect(rect, data.BorderColor));
            if (data.Color.IsVisible) Fill(rect.Inset(1), data.Color);
        }

        private void Fill(UiRect rect, WndColor color)
        {
            if (!rect.IsEmpty && color.IsVisible) _commands.Add(new UiFillRect(rect, color));
        }

        private void Image(string image, UiRect rect)
        {
            if (!rect.IsEmpty) _commands.Add(new UiImageDraw(image, rect, WndColor.White, false, false));
        }
    }
}
