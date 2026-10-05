using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// The pointer tooltip of <c>Mouse::drawTooltip</c> (Mouse.cpp): a filled, outlined box 20 pixels right of the pointer (left of it
/// when it would leave the screen, above it at the bottom edge) with the text inside, word wrapped at <c>TooltipWidth</c> percent of the screen
/// width (<c>Mouse::setCursorTooltip</c>). The text is revealed from the left over <c>TooltipFillTime</c> milliseconds: a clip rectangle
/// that grows with the elapsed time shows the text in its normal color, the 15 pixel band at its leading edge shows it in the highlight
/// color. With <c>TooltipAnimateBackground</c> the box grows with the clip too.
/// </summary>
public static class UiTooltipLayout
{
    private const int HighlightWidth = 15;

    public static ImmutableArray<UiDrawCommand> Build(UiTooltip tooltip, MouseTooltipSettings style, UiFont font, UiViewport viewport, IUiTextMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(measure);
        if (tooltip.Text.Length == 0) return [];

        var wrap = (int)(viewport.Width * style.WidthFraction);
        if (wrap < 10) wrap = 120;
        else if (wrap > viewport.Width) wrap = viewport.Width;
        var sentence = UiSentenceLayout.Layout(tooltip.Text, font, measure, wrap, false);
        int width = sentence.Width, height = sentence.Height;

        var x = tooltip.X + 20;
        var y = tooltip.Y;
        if (x + width + 4 > viewport.Width) x -= 20 + width;
        if (y + height + 4 > viewport.Height) y -= height;

        // m_highlightPos starts at 0 when the tooltip appears and follows width * elapsed / fill time until it has passed the text and the band.
        long reveal = style.FillTimeMilliseconds <= 0 ? width + HighlightWidth : Math.Min(width + HighlightWidth, width * tooltip.AgeMs / style.FillTimeMilliseconds);
        var boxWidth = style.AnimateBackground ? (int)Math.Min(width, reveal) : width;

        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(new UiRect(x, y, boxWidth + 2, height + 2), style.BackgroundColor));
        commands.Add(new UiBorderRect(new UiRect(x, y, boxWidth + 2, height + 2), style.BorderColor));
        var lineHeight = measure.LineHeight(font);
        var shown = new UiRect(x + 2, y + 1, (int)reveal, height);
        var band = UiRect.FromEdges(x + 2 + (int)reveal - HighlightWidth, y + 1, x + 2 + (int)reveal, y + 1 + height);
        for (var pass = 0; pass < 2; pass++)
        {
            // Once the band has left the text it would draw nothing.
            if (pass == 1 && reveal - HighlightWidth >= width) break;
            var clip = pass == 0 ? shown : band;
            var color = pass == 0 ? style.TextColor : style.HighlightColor;
            for (var index = 0; index < sentence.Lines.Length; index++)
            {
                var line = sentence.Lines[index];
                if (line.Text.Length == 0) continue;
                commands.Add(new UiTextDraw(line.Text, null, font, color, style.ShadowColor, x + 2 + line.X, y + 1 + index * lineHeight, UiTextAlignment.Left, clip));
            }
        }
        return commands.ToImmutable();
    }
}
