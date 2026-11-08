using System.Text;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Rendering;

/// <summary>A label laid out to fit its window, with the font it is drawn in.</summary>
public readonly record struct UiFittedText(UiFont Font, UiSentence Sentence);

/// <summary>
/// A label that does not fit its window at the chosen text size (<see cref="UiScaleOptions.TextScale"/> above 100 %). The label steps its font down one point at a time, never
/// below the size it has at 100 % in the skin it is drawn in, until it fits; if even that does not fit, it wraps at the window's width where the window is high enough for the
/// lines, and otherwise is cut short with an ellipsis - it is never drawn outside its window. At 100 % (a font that is not above its base size) nothing here applies and the
/// sentence is exactly <see cref="UiSentenceLayout.Layout"/>'s, so the pictures of the shipped sizes stay as they were.
/// </summary>
public static class UiTextFit
{
    public static UiFittedText Fit(string text, UiFont font, int baseSize, IUiTextMeasure measure, int wrapWidth, bool wrapCentered, int roomWidth, int roomHeight)
    {
        var sentence = UiSentenceLayout.Layout(text, font, measure, wrapWidth, wrapCentered);
        if (font.Size <= baseSize || Fits(sentence, roomWidth, roomHeight)) return new UiFittedText(font, sentence);
        var current = font;
        for (var size = font.Size - 1; size >= baseSize; size--)
        {
            current = font with { Size = Math.Max(1, size) };
            sentence = UiSentenceLayout.Layout(text, current, measure, wrapWidth, wrapCentered);
            if (Fits(sentence, roomWidth, roomHeight)) return new UiFittedText(current, sentence);
        }
        // Even the 100 % size does not fit: wrap where the window is high enough, else cut short.
        if (roomWidth > 0 && wrapWidth != roomWidth)
        {
            var wrapped = UiSentenceLayout.Layout(text, current, measure, roomWidth, wrapCentered);
            if (Fits(wrapped, roomWidth, roomHeight)) return new UiFittedText(current, wrapped);
        }
        return new UiFittedText(current, Ellipsize(text, current, measure, roomWidth));
    }

    private static bool Fits(UiSentence sentence, int roomWidth, int roomHeight) => sentence.Width <= roomWidth && sentence.Height <= roomHeight;

    private static UiSentence Ellipsize(string text, UiFont font, IUiTextMeasure measure, int roomWidth)
    {
        var line = string.Join(' ', text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries)).Trim();
        var overlap = measure.ExtraOverlap(font);
        var runes = line.EnumerateRunes().ToList();
        var result = new StringBuilder();
        for (var keep = runes.Count; keep >= 0; keep--)
        {
            result.Clear();
            foreach (var rune in runes.Take(keep)) result.Append(rune.ToString());
            var candidate = result.ToString().TrimEnd() + "...";
            if (keep == 0 || measure.MeasureWidth(candidate, font) + overlap <= roomWidth)
                return new UiSentence([new UiSentenceLine(candidate, 0)], measure.MeasureWidth(candidate, font) + overlap, measure.LineHeight(font));
        }
        return new UiSentence([new UiSentenceLine("...", 0)], measure.MeasureWidth("...", font) + overlap, measure.LineHeight(font));
    }
}
