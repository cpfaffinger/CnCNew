using System.Collections.Immutable;
using System.Text;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Rendering;

/// <summary>One laid-out line of a sentence: its text and the pen position of its first glyph relative to the block's left edge.</summary>
public readonly record struct UiSentenceLine(string Text, int X);

/// <summary>
/// A laid-out text block. <see cref="Width"/> and <see cref="Height"/> are the extents the window code centers with
/// (<c>DisplayString::getSize</c>): the widest line end plus the font's pixel overlap, and the line count times the line
/// height. The two can disagree with the drawn <see cref="Lines"/> exactly where the original's two builders disagree.
/// </summary>
public sealed record UiSentence(ImmutableArray<UiSentenceLine> Lines, int Width, int Height);

/// <summary>
/// Word wrap and line placement the way <c>Render2DSentenceClass</c> does it (render2dsentence.cpp), because the line
/// breaks and the block width decide where a window puts its text. A plain greedy "fits if width &lt;= limit" wrap differs from it
/// in four ways that show up on screen:
/// <list type="bullet">
/// <item><description>a word moves to the next line when <c>line + blank + word &gt;= wrap width</c> (<c>Build_Sentence_Not_Centered</c>), so a line may
/// only reach <c>wrap width - 1</c>;</description></item>
/// <item><description>the blank at which a line wraps is not dropped: the chunk of the next line starts at the blank, so a wrapped line is indented by one
/// blank width (left aligned text only; the centered builder advances the pen over a blank instead);</description></item>
/// <item><description>the block width is the widest line end plus the font's pixel overlap (<c>Get_Extra_Overlap</c>);</description></item>
/// <item><description>the centered builder (<c>Build_Sentence_Centered</c>, used for <c>WRAP_CENTERED</c> windows with a wrap width or a line break) counts the blank
/// before a word twice in its wrap test and centers each line on the block width of the not-centered builder, which also gives the block height.</description></item>
/// </list>
/// A character is a break character at U+0020 and U+000A; a word is a run of characters above U+0020. Hot key marks
/// (<c>&amp;</c> with <c>HOTKEY_TEXT</c>) and <c>UseHardWordWrap</c> (off in the retail English <c>Language.ini</c>) are not applied.
/// </summary>
public static class UiSentenceLayout
{
    public static UiSentence Layout(string text, UiFont font, IUiTextMeasure measure, int wrapWidth, bool wrapCentered)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        var runes = text.Replace("\r", string.Empty, StringComparison.Ordinal).EnumerateRunes().Select(rune => rune.Value).ToArray();
        var cache = new Dictionary<int, int>();
        int Spacing(int codepoint)
        {
            if (!cache.TryGetValue(codepoint, out var width))
                cache[codepoint] = width = measure.MeasureWidth(char.ConvertFromUtf32(codepoint), font);
            return width;
        }

        var lineHeight = measure.LineHeight(font);
        var overlap = measure.ExtraOverlap(font);
        var plain = NotCentered(runes, Spacing, wrapWidth);
        var extent = plain.MaxX + overlap;
        var height = plain.Lines.Length * lineHeight;
        if (!wrapCentered || (wrapWidth <= 0 && Array.IndexOf(runes, '\n') < 0))
            return new UiSentence([..plain.Lines.Select(line => new UiSentenceLine(line, 0))], extent, height);
        return new UiSentence(Centered(runes, Spacing, wrapWidth, extent), extent, height);
    }

    /// <summary><c>Build_Sentence_Not_Centered</c>: line texts (a wrapped line starts with its blank) and the widest line end.</summary>
    private static (ImmutableArray<string> Lines, int MaxX) NotCentered(int[] runes, Func<int, int> spacing, int wrapWidth)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        var current = new StringBuilder();
        int cursorX = 0, chunk = 0, maxX = 0;
        void NewLine()
        {
            lines.Add(current.ToString());
            current.Clear();
            cursorX = 0;
        }
        for (var index = 0; ; index++)
        {
            var ch = index < runes.Length ? runes[index] : 0;
            var advance = ch == 0 ? 0 : spacing(ch);
            if (ch is ' ' or '\n' or 0)
            {
                cursorX += chunk;
                maxX = Math.Max(maxX, cursorX);
                chunk = 0;
                if (ch == ' ')
                {
                    if (wrapWidth > 0)
                    {
                        var word = advance;
                        for (var next = index + 1; next < runes.Length && runes[next] > ' '; next++) word += spacing(runes[next]);
                        if (cursorX + word >= wrapWidth) NewLine();
                    }
                }
                else if (ch == '\n') NewLine();
                else break;
            }
            if (ch == '\n') continue;
            current.Append(char.ConvertFromUtf32(ch));
            chunk += advance;
        }
        lines.Add(current.ToString());
        return (lines.ToImmutable(), maxX);
    }

    /// <summary><c>Build_Sentence_Centered</c>.</summary>
    private static ImmutableArray<UiSentenceLine> Centered(int[] runes, Func<int, int> spacing, int wrapWidth, int extent)
    {
        var lines = ImmutableArray.CreateBuilder<UiSentenceLine>();
        var position = 0;
        var end = false;
        while (!end)
        {
            var word = position;
            int wordWidth = 0, lineWidth = 0, charCount = 0, wordCount = 0;
            while (true)
            {
                var charWidth = 0;
                while (word < runes.Length && runes[word] > ' ' && runes[word] != '\n')
                {
                    charWidth = spacing(runes[word++]);
                    wordWidth += charWidth;
                    wordCount++;
                }
                if (wrapWidth > 0 && lineWidth + wordWidth >= wrapWidth)
                {
                    if (charCount == 0)
                    {
                        // A word that does not fit even alone is taken whole.
                        charCount += wordCount - 1;
                        lineWidth += wordWidth - charWidth;
                        if (word >= runes.Length) end = true;
                        break;
                    }
                    charCount--;
                    break;
                }
                if (word >= runes.Length)
                {
                    charCount += wordCount;
                    lineWidth += wordWidth;
                    end = true;
                    break;
                }
                charCount += wordCount + 1;
                lineWidth += wordWidth;
                if (runes[word] != ' ') break;
                wordWidth = spacing(runes[word++]);
                wordCount = 0;
                lineWidth += wordWidth;
            }

            var consumed = new StringBuilder();
            var taken = 0;
            for (var i = 0; i <= charCount; i++)
            {
                if (position >= runes.Length) break;
                var ch = runes[position++];
                taken++;
                if (ch == '\n') break;
                consumed.Append(char.ConvertFromUtf32(ch));
            }
            if (taken == 0 && !end)
            {
                // The original would loop forever on this input (a blank wider than the wrap width); take one character.
                if (position < runes.Length) consumed.Append(char.ConvertFromUtf32(runes[position++]));
                else end = true;
            }
            lines.Add(new UiSentenceLine(consumed.ToString().TrimEnd(' '), Math.Max(0, (extent - lineWidth) / 2)));
        }
        return lines.ToImmutable();
    }
}
