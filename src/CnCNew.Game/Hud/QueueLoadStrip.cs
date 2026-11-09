using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// The queue load of the buildings of a structure group (docs/PRESENTATION-INPUT.md 5b): one small cell per building - its number of orders as "n/9" over a row of pips, one pip
/// per place of its queue, a full queue tinted. The original selects one building at a time and has no such display, so this is ours: the modern skin puts the cells in the selection
/// plate under the kinds, the original skin and a window without a plate (4:3) put them in a tray over the bar's top edge, where nothing else is drawn. <see cref="Layout"/> is pure
/// geometry (cells that stay in their area and never overlap, a "+n" cell when the rows are full); <see cref="Draw"/> turns it into draw commands.
/// </summary>
internal static class QueueLoadStrip
{
    /// <summary>One cell: where it stands, the pips' box, where its text starts, and what it says (<see cref="Filled"/> of <see cref="PipCount"/> pips; none for the "+n" cell).</summary>
    internal readonly record struct Cell(UiRect Rect, UiRect Pips, int TextX, int TextY, string Text, int Filled, int PipCount, bool Full);

    internal sealed record Layout(ImmutableArray<Cell> Cells, int CellHeight, int Hidden, int PipWidth = 0, int PipGap = 0)
    {
        public static Layout Empty { get; } = new([], 0, 0);
    }

    /// <summary>The longest row of pips drawn; a queue of more places draws this many and fills them in proportion.</summary>
    private const int MaxPips = 12;

    /// <summary>The height of a cell for a line of text and the pips' height: what <see cref="Layout"/> will need per row.</summary>
    public static int RowHeight(int textHeight, int pipHeight, int pad) => pad + textHeight + pad / 2 + pipHeight + pad;

    /// <summary>
    /// Places the cells of <paramref name="loads"/> in <paramref name="area"/>, left to right, row after row - from the top of the area down, or, with <paramref name="fromBottom"/>, from
    /// its bottom up (the first row nearest the bar). A row that is not there is not made: the cells that do not fit are counted in the last cell, which says "+n".
    /// </summary>
    public static Layout Place(ImmutableArray<QueueLoad> loads, UiRect area, bool fromBottom, int pipWidth, int pipGap, int pipHeight, int pad, int textHeight, Func<string, int> measure)
    {
        if (loads.IsDefaultOrEmpty || area.Width < 1 || area.Height < 1) return Layout.Empty;
        var rowHeight = RowHeight(textHeight, pipHeight, pad);
        var rows = Math.Max(1, (area.Height + pad) / (rowHeight + pad));
        var widths = new int[loads.Length];
        for (var index = 0; index < loads.Length; index++)
        {
            var pips = Math.Min(Math.Max(loads[index].Limit, 1), MaxPips);
            widths[index] = Math.Max(measure(Text(loads[index])), pips * pipWidth + (pips - 1) * pipGap) + 2 * pad;
        }
        var cellWidth = widths.Max();
        var columns = Math.Max(1, (area.Width + pad) / (cellWidth + pad));
        var capacity = columns * rows;
        var shown = loads.Length <= capacity ? loads.Length : capacity - 1;
        var cells = ImmutableArray.CreateBuilder<Cell>(shown + 1);
        for (var index = 0; index <= shown && index < loads.Length; index++)
        {
            var more = index == shown && shown < loads.Length;
            var row = index / columns;
            var rowTop = fromBottom ? area.Bottom - (row + 1) * rowHeight - row * pad : area.Y + row * (rowHeight + pad);
            var rect = new UiRect(area.X + index % columns * (cellWidth + pad), rowTop, cellWidth, rowHeight);
            var text = more ? string.Create(CultureInfo.InvariantCulture, $"+{loads.Length - shown}") : Text(loads[index]);
            var textX = rect.X + (cellWidth - measure(text)) / 2;
            if (more)
            {
                cells.Add(new Cell(rect, default, textX, rect.Y + (rowHeight - textHeight) / 2, text, 0, 0, false));
                break;
            }
            var load = loads[index];
            var pipCount = Math.Min(Math.Max(load.Limit, 1), MaxPips);
            var pipsWidth = pipCount * pipWidth + (pipCount - 1) * pipGap;
            var filled = load.Limit <= MaxPips ? Math.Min(load.Count, pipCount) : (int)Math.Ceiling(load.Count * (double)pipCount / load.Limit);
            cells.Add(new Cell(rect, new UiRect(rect.X + (cellWidth - pipsWidth) / 2, rect.Bottom - pad - pipHeight, pipsWidth, pipHeight), textX, rect.Y + pad, text, load.Count > 0 ? Math.Max(filled, 1) : 0, pipCount, load.IsFull));
        }
        return new Layout(cells.ToImmutable(), rowHeight, Math.Max(0, loads.Length - shown), pipWidth, pipGap);
    }

    /// <summary>The text of a cell: "3/9". Tests and the golden pictures read it.</summary>
    public static string Text(QueueLoad load) => string.Create(CultureInfo.InvariantCulture, $"{load.Count}/{load.Limit}");

    /// <summary>The colours of a drawing: what a cell stands on, its rim and its empty pips, a filled pip, a full queue, the text.</summary>
    internal readonly record struct Colours(WndColor Backing, WndColor Border, WndColor Empty, WndColor Filled, WndColor Full, WndColor Text, WndColor TextShadow);

    public static ImmutableArray<UiDrawCommand> Draw(Layout layout, Colours colours, UiFont font, UiRect clip)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        foreach (var cell in layout.Cells)
        {
            commands.Add(new UiFillRect(cell.Rect, colours.Backing));
            commands.Add(new UiBorderRect(cell.Rect, cell.Full ? colours.Full : colours.Border));
            for (var pip = 0; pip < cell.PipCount; pip++)
                commands.Add(new UiFillRect(new UiRect(cell.Pips.X + pip * (layout.PipWidth + layout.PipGap), cell.Pips.Y, layout.PipWidth, cell.Pips.Height), pip < cell.Filled ? (cell.Full ? colours.Full : colours.Filled) : colours.Empty));
            commands.Add(new UiTextDraw(cell.Text, null, font, cell.Full ? colours.Full : colours.Text, colours.TextShadow, cell.TextX, cell.TextY, UiTextAlignment.Left, clip));
        }
        return commands.ToImmutable();
    }
}
