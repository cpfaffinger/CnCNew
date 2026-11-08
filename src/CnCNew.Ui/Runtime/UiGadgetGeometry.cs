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
    public const int TextInset = 5;

    /// <summary>Lines of a list or a drop-down; the modern skin gives them air (<c>FlatRowPadding</c> units above and below the text).</summary>
    public static int RowHeight(UiWindow window)
    {
        var line = Math.Max(1, window.Tree.Services.TextMeasure.LineHeight(window.Font));
        return window.Definition.Flat == WndFlatRole.None ? line : line + 2 * window.Transform.Y.Length(FlatRowPadding);
    }

    private const int FlatRowPadding = 2;

    public static int ScrollBarWidth(UiWindow window)
        => window.ListBox is { Data.ScrollBar: true } ? Math.Min(window.Rect.Width, window.Transform.X.Length(window.Definition.Flat == WndFlatRole.None ? GadgetSize : FlatGadgetSize)) : 0;

    private const int FlatGadgetSize = 14;

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

    /// <summary>The rows the list shows: the box's own count, fewer when the screen has no room for all of them on either side of the box (the list then scrolls).</summary>
    public static int ComboVisibleRows(UiWindow window) => ComboPlacement(window).Rows;

    private static (int Rows, int Y, int Height) ComboPlacement(UiWindow window)
    {
        var combo = window.ComboBox!;
        var flat = window.Definition.Flat != WndFlatRole.None;
        var max = combo.Data.MaxDisplay > 0 ? combo.Data.MaxDisplay : combo.Entries.Count;
        var rows = Math.Min(max, combo.Entries.Count);
        var rowHeight = RowHeight(window);
        var padding = flat ? 2 : 0;
        var height = rows * rowHeight + padding;
        var below = window.Tree.Viewport.Height - window.Rect.Bottom;
        var above = window.Rect.Y;
        if (height <= below) return (rows, window.Rect.Bottom, height);
        // Past the screen's bottom: above the box when the whole list fits there (the modern skin), else as many rows as the larger side holds.
        if (flat && height <= above) return (rows, window.Rect.Y - height, height);
        var room = flat && above > below ? above : below;
        var fitting = Math.Max(1, Math.Min(rows, (room - padding) / Math.Max(1, rowHeight)));
        if (fitting == rows) return (rows, window.Rect.Bottom, height);
        height = fitting * rowHeight + padding;
        return (fitting, flat && above > below ? window.Rect.Y - height : window.Rect.Bottom, height);
    }

    /// <summary>The open drop-down list below the combo box; in the modern skin above it when the screen ends first; shorter (and scrolling) when neither side has room for it.</summary>
    public static UiRect ComboDropDown(UiWindow window)
    {
        var (_, y, height) = ComboPlacement(window);
        return new UiRect(window.Rect.X, y, window.Rect.Width, height);
    }

    /// <summary>The row of an open drop-down at a height of the screen.</summary>
    public static int ComboRowAt(UiWindow window, int y)
    {
        var drop = ComboDropDown(window);
        var top = drop.Y + (window.Definition.Flat != WndFlatRole.None ? 1 : 0);
        return window.ComboBox!.TopIndex + (y - top) / RowHeight(window);
    }

    public static UiRect SliderThumb(UiWindow window)
    {
        var slider = window.Slider!;
        var rect = window.Rect;
        var range = Math.Max(1, slider.Maximum - slider.Minimum);
        if (window.Definition.Flat != WndFlatRole.None && window.Type == WndWindowType.HorizontalSlider)
        {
            // The modern slider's thumb stands on its track in the middle of the window: where it is drawn is where it answers.
            var thumbWidth = Math.Min(rect.Width, window.Transform.X.Length(FlatThumbWidth));
            var thumbHeight = Math.Min(rect.Height, window.Transform.Y.Length(FlatThumbHeight));
            return new UiRect(rect.X + (int)((long)(rect.Width - thumbWidth) * (slider.Value - slider.Minimum) / range), rect.Y + (rect.Height - thumbHeight) / 2, thumbWidth, thumbHeight);
        }
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

    private const int FlatThumbWidth = 8, FlatThumbHeight = 20;

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
