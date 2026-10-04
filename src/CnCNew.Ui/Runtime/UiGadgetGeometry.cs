using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// Sub-part geometry of gadgets, shared by input handling and drawing. Sizes that the original hard-codes in pixels
/// (<c>GADGET_SIZE</c> = 16, <c>HORIZONTAL_SLIDER_THUMB_WIDTH</c> = 13) are treated as creation-resolution units and
/// scaled with the window's transform.
/// </summary>
public static class UiGadgetGeometry
{
    public const int GadgetSize = 16;
    public const int HorizontalThumbWidth = 13;
    /// <summary><c>HORIZONTAL_SLIDER_THUMB_HEIGHT</c>.</summary>
    public const int HorizontalThumbHeight = 16;
    /// <summary><c>HORIZONTAL_SLIDER_THUMB_POSITION</c> = thumb height * 2 / 3.</summary>
    public const int HorizontalThumbOffsetY = HorizontalThumbHeight * 2 / 3;
    public const int TextInset = 4;

    public static int RowHeight(UiWindow window) => Math.Max(1, window.Tree.Services.TextMeasure.LineHeight(window.Font));

    public static int ScrollBarWidth(UiWindow window)
        => window.ListBox is { Data.ScrollBar: true } ? Math.Min(window.Rect.Width, window.Transform.X.Length(GadgetSize)) : 0;

    public static UiRect ListContent(UiWindow window)
        => UiRect.FromEdges(window.Rect.X, window.Rect.Y, window.Rect.Right - ScrollBarWidth(window), window.Rect.Bottom);

    public static int VisibleRows(UiWindow window) => Math.Max(1, ListContent(window).Height / RowHeight(window));

    public static int MaxTopIndex(UiWindow window) => Math.Max(0, (window.ListBox?.Rows.Count ?? 0) - VisibleRows(window));

    public static UiRect UpButton(UiWindow window)
    {
        var width = ScrollBarWidth(window);
        return new UiRect(window.Rect.Right - width, window.Rect.Y, width, Math.Min(width, window.Rect.Height / 2));
    }

    public static UiRect DownButton(UiWindow window)
    {
        var up = UpButton(window);
        return new UiRect(up.X, window.Rect.Bottom - up.Height, up.Width, up.Height);
    }

    public static UiRect ScrollTrack(UiWindow window)
    {
        var up = UpButton(window);
        return UiRect.FromEdges(up.X, up.Bottom, up.Right, DownButton(window).Y);
    }

    public static UiRect ScrollThumb(UiWindow window)
    {
        var track = ScrollTrack(window);
        var rows = window.ListBox?.Rows.Count ?? 0;
        var visible = VisibleRows(window);
        if (rows <= visible || track.Height <= 0) return track;
        var height = Math.Max(Math.Min(track.Width, track.Height), (int)((long)track.Height * visible / rows));
        var maxTop = MaxTopIndex(window);
        var top = track.Y + (int)((long)(track.Height - height) * window.ListBox!.TopIndex / Math.Max(1, maxTop));
        return new UiRect(track.X, top, track.Width, height);
    }

    /// <summary>Column rectangles of a row: <c>COLUMNSWIDTH</c> percentages of the content width, the last column takes the rest.</summary>
    public static UiRect[] Columns(UiWindow window, UiRect row)
    {
        var data = window.ListBox?.Data;
        var count = Math.Max(1, data?.Columns ?? 1);
        var percentages = data?.ColumnWidthPercentages ?? [];
        var result = new UiRect[count];
        var x = row.X;
        for (var i = 0; i < count; i++)
        {
            var width = i == count - 1 ? row.Right - x : (i < percentages.Length ? row.Width * percentages[i] / 100 : row.Width / count);
            result[i] = new UiRect(x, row.Y, Math.Max(0, Math.Min(width, row.Right - x)), row.Height);
            x += result[i].Width;
        }
        return result;
    }

    public static UiRect ComboButton(UiWindow window)
    {
        var size = Math.Min(window.Rect.Height, window.Rect.Width);
        return new UiRect(window.Rect.Right - size, window.Rect.Y, size, window.Rect.Height);
    }

    public static UiRect ComboEdit(UiWindow window) => UiRect.FromEdges(window.Rect.X, window.Rect.Y, ComboButton(window).X, window.Rect.Bottom);

    public static int ComboVisibleRows(UiWindow window)
    {
        var combo = window.ComboBox!;
        var max = combo.Data.MaxDisplay > 0 ? combo.Data.MaxDisplay : combo.Entries.Count;
        return Math.Min(max, combo.Entries.Count);
    }

    /// <summary>The open drop-down list below the combo box.</summary>
    public static UiRect ComboDropDown(UiWindow window)
        => new(window.Rect.X, window.Rect.Bottom, window.Rect.Width, ComboVisibleRows(window) * RowHeight(window));

    public static UiRect SliderThumb(UiWindow window)
    {
        var slider = window.Slider!;
        var rect = window.Rect;
        var range = Math.Max(1, slider.Maximum - slider.Minimum);
        if (window.Type == WndWindowType.HorizontalSlider)
        {
            var width = Math.Min(rect.Width, window.Transform.X.Length(HorizontalThumbWidth));
            var x = rect.X + (int)((long)(rect.Width - width) * (slider.Value - slider.Minimum) / range);
            // gogoGadgetSlider: the thumb button is 13x16 and hangs HORIZONTAL_SLIDER_THUMB_POSITION below the top edge.
            return new UiRect(x, rect.Y + window.Transform.Y.Length(HorizontalThumbOffsetY), width, window.Transform.Y.Length(HorizontalThumbHeight));
        }
        var height = Math.Min(rect.Height, window.Transform.Y.Length(GadgetSize));
        var y = rect.Y + (int)((long)(rect.Height - height) * (slider.Maximum - slider.Value) / range);
        return new UiRect(rect.X, y, rect.Width, height);
    }

    /// <summary>The slider value whose thumb is centered on the pointer (vertical sliders have the maximum at the top).</summary>
    public static int SliderValueAt(UiWindow window, int x, int y)
    {
        var slider = window.Slider!;
        var rect = window.Rect;
        var thumb = SliderThumb(window);
        var range = slider.Maximum - slider.Minimum;
        long travel, offset;
        if (window.Type == WndWindowType.HorizontalSlider)
        {
            travel = rect.Width - thumb.Width;
            offset = x - rect.X - thumb.Width / 2;
        }
        else
        {
            travel = rect.Height - thumb.Height;
            offset = rect.Bottom - thumb.Height / 2 - y;
        }
        if (travel <= 0) return slider.Minimum;
        offset = Math.Clamp(offset, 0, travel);
        var steps = (offset * range * 2 + travel) / (travel * 2);
        return slider.Clamp(slider.Minimum + (int)steps);
    }

    public static UiRect ProgressFill(UiWindow window)
        => new(window.Rect.X, window.Rect.Y, window.Rect.Width * window.ProgressPercent / 100, window.Rect.Height);

    public static UiRect TabHeader(UiWindow window, int index)
    {
        var data = window.TabControl!.Data;
        var width = window.Transform.X.Length(Math.Max(1, data.TabWidth));
        var height = window.Transform.Y.Length(Math.Max(1, data.TabHeight));
        return new UiRect(window.Rect.X + index * width, window.Rect.Y, width, height);
    }

    public static int TextOriginX(UiWindow window, UiRect area) => area.X + window.Transform.X.Length(TextInset);
}
