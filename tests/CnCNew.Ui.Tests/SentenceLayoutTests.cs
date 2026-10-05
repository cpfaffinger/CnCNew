using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Tests;

/// <summary>
/// <see cref="UiSentenceLayout"/> against hand-traced runs of <c>Render2DSentenceClass::Build_Sentence_Not_Centered</c> and
/// <c>Build_Sentence_Centered</c> (render2dsentence.cpp). The fake font is fixed width (5 px per character, a blank included)
/// with a 12 px line, so every expected number below can be followed on paper.
/// </summary>
public static class SentenceLayoutTests
{
    private static int _failures;
    private static readonly UiFont Font = new("Arial", 10, false);

    private sealed class FixedMeasure(int overlap = 0) : IUiTextMeasure
    {
        public int MeasureWidth(string text, UiFont font) => text.Length * 5;
        public int LineHeight(UiFont font) => 12;
        public int ExtraOverlap(UiFont font) => overlap;
    }

    public static int Run(string[] args)
    {
        _ = args;
        _failures = 0;
        Test(SingleLineWidthIsTheTextPlusTheOverlap);
        Test(AWordMovesDownWhenItReachesTheWrapWidth);
        Test(AWrappedLineKeepsTheBlankAtItsStart);
        Test(ExplicitBreaksAndTheTrailingEmptyLineCount);
        Test(AWordWiderThanTheWrapWidthStaysWhole);
        Test(CenteredBuilderCountsTheBlankTwiceAndCentersEachLine);
        Test(CenteredWithoutWrapOrBreakIsTheSingleLineCase);
        Test(CenteredHeightComesFromTheNotCenteredBuilder);
        Test(TinyWrapWidthTerminates);
        Test(TooltipSitsRightOfThePointerAndFlipsAtTheEdges);
        Test(TooltipTextIsRevealedOverTheFillTime);
        Console.WriteLine(_failures == 0 ? "Sentence layout tests passed." : $"Sentence layout tests: {_failures} failure(s).");
        return _failures;
    }

    private static void Test(Action test)
    {
        try { test(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {test.Method.Name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string Dump(UiSentence sentence)
        => $"{sentence.Width}x{sentence.Height} " + string.Join("|", sentence.Lines.Select(line => $"{line.X}:{line.Text}"));

    private static void SingleLineWidthIsTheTextPlusTheOverlap()
    {
        var plain = UiSentenceLayout.Layout("Hello", Font, new FixedMeasure(), 0, false);
        Check(Dump(plain) == "25x12 0:Hello", Dump(plain));
        // Get_Formatted_Text_Extents: extent.X = maxX + Get_Extra_Overlap().
        var overlapped = UiSentenceLayout.Layout("Hello", Font, new FixedMeasure(2), 0, false);
        Check(Dump(overlapped) == "27x12 0:Hello", Dump(overlapped));
    }

    private static void AWordMovesDownWhenItReachesTheWrapWidth()
    {
        // "aaaa bbbb" is 45 wide. The next word wraps when the line so far plus blank plus word is >= the wrap width.
        Check(Dump(UiSentenceLayout.Layout("aaaa bbbb", Font, new FixedMeasure(), 46, false)) == "45x12 0:aaaa bbbb", "46 keeps both on one line.");
        Check(Dump(UiSentenceLayout.Layout("aaaa bbbb", Font, new FixedMeasure(), 45, false)).EndsWith("0:aaaa|0: bbbb", StringComparison.Ordinal),
            "45 already wraps (>=, not >).");
    }

    private static void AWrappedLineKeepsTheBlankAtItsStart()
    {
        // Build_Sentence_Not_Centered records the chunk of the next line from before the blank: the blank indents it by one blank width,
        // and the block width counts the indent (" bbbb" is 25 wide).
        var sentence = UiSentenceLayout.Layout("aaaa bbbb", Font, new FixedMeasure(), 30, false);
        Check(Dump(sentence) == "25x24 0:aaaa|0: bbbb", Dump(sentence));
    }

    private static void ExplicitBreaksAndTheTrailingEmptyLineCount()
    {
        Check(Dump(UiSentenceLayout.Layout("ab\ncd", Font, new FixedMeasure(), 0, false)) == "10x24 0:ab|0:cd", "A line break starts a line without a blank.");
        Check(Dump(UiSentenceLayout.Layout("ab\n", Font, new FixedMeasure(), 0, false)) == "10x24 0:ab|0:", "A trailing line break leaves an empty second line that still counts in the height.");
        Check(Dump(UiSentenceLayout.Layout("ab\r\ncd", Font, new FixedMeasure(), 0, false)) == "10x24 0:ab|0:cd", "A carriage return is dropped.");
    }

    private static void AWordWiderThanTheWrapWidthStaysWhole()
    {
        var sentence = UiSentenceLayout.Layout("a bbbbbbbbbbbb c", Font, new FixedMeasure(), 20, false);
        Check(Dump(sentence) == "65x36 0:a|0: bbbbbbbbbbbb|0: c", Dump(sentence));
    }

    private static void CenteredBuilderCountsTheBlankTwiceAndCentersEachLine()
    {
        // Wrap width 50 on "Hello wide world": the not-centered pass gives the lines "Hello", " wide", " world" (block 30 x 36);
        // the centered pass wraps where line width (with the blank) + blank + word >= 50, so it breaks the same way here and
        // centers on the block: x = (30 - lineWidth) / 2 with the blank included in the line width (30, 25, 25).
        var sentence = UiSentenceLayout.Layout("Hello wide world", Font, new FixedMeasure(), 50, true);
        Check(Dump(sentence) == "30x36 0:Hello|2:wide|2:world", Dump(sentence));
    }

    private static void CenteredWithoutWrapOrBreakIsTheSingleLineCase()
    {
        var sentence = UiSentenceLayout.Layout("Hello", Font, new FixedMeasure(), 0, true);
        Check(Dump(sentence) == "25x12 0:Hello", Dump(sentence));
        var broken = UiSentenceLayout.Layout("ab\ncdef", Font, new FixedMeasure(), 0, true);
        Check(Dump(broken) == "20x24 5:ab|0:cdef", Dump(broken));
    }

    private static void CenteredHeightComesFromTheNotCenteredBuilder()
    {
        // 46 wide: not centered "aaaa bbbb" fits one line (45 < 46), height 12; the centered pass wraps it (line "aaaa " plus the doubled blank
        // and word reaches 46) into two lines. The original draws two lines inside a block that is one line tall.
        var sentence = UiSentenceLayout.Layout("aaaa bbbb", Font, new FixedMeasure(), 46, true);
        Check(sentence.Height == 12 && sentence.Lines.Length == 2, Dump(sentence));
    }

    private static readonly MouseTooltipSettings TooltipStyle = new(new FontDescription("Arial", 8, false), false, 250, 800,
        new WndColor(220, 220, 220, 255), new WndColor(255, 255, 0, 255), new WndColor(0, 0, 0, 255), new WndColor(60, 60, 155, 255), new WndColor(20, 20, 20, 255), 0.2m);

    private static void TooltipSitsRightOfThePointerAndFlipsAtTheEdges()
    {
        var font = new UiFont("Arial", 10, false);
        var viewport = new UiViewport(1000, 600);
        // 20 % of 1000 = 200 px wrap width; "Hello world" is 55 px wide, one line of 12 px.
        var commands = UiTooltipLayout.Build(new UiTooltip("W", "Hello world", null, 100, 100, 1000), TooltipStyle, font, viewport, new FixedMeasure());
        var box = (UiFillRect)commands[0];
        Check(box.Rect == new UiRect(120, 100, 57, 14) && box.Color == new WndColor(20, 20, 20, 255), $"Box 20 px right of the pointer, text width + 2: {commands[0]}");
        Check(commands[1] is UiBorderRect outline && outline.Rect == box.Rect && outline.Color == TooltipStyle.BorderColor, "The outline has the box rectangle.");
        Check(commands[2] is UiTextDraw { Text: "Hello world", X: 122, Y: 101 } text && text.Color == new WndColor(220, 220, 220, 255) && text.ShadowColor == new WndColor(0, 0, 0, 255), $"Text 2 px in and 1 px down: {commands[2]}");
        Check(commands.Length == 3, $"A finished reveal has no highlight band left: {commands.Length} commands");

        var right = UiTooltipLayout.Build(new UiTooltip("W", "Hello world", null, 980, 100, 1000), TooltipStyle, font, viewport, new FixedMeasure());
        Check(right[0] is UiFillRect { Rect.X: 925 }, $"Near the right edge the box moves left of the pointer (pointer - width): {right[0]}");
        var bottom = UiTooltipLayout.Build(new UiTooltip("W", "Hello world", null, 100, 595, 1000), TooltipStyle, font, viewport, new FixedMeasure());
        Check(bottom[0] is UiFillRect { Rect.Y: 583 }, $"Near the bottom edge the box moves above the pointer: {bottom[0]}");
        // The wrap width is 20 % of the screen: 100 px of a 500 px screen wraps "Hello world" (55 px) never, but 50 px does.
        var narrow = UiTooltipLayout.Build(new UiTooltip("W", "Hello world", null, 10, 10, 1000), TooltipStyle with { WidthFraction = 0.1m }, font, new UiViewport(500, 300), new FixedMeasure());
        Check(narrow.OfType<UiTextDraw>().Select(draw => draw.Text).SequenceEqual(["Hello", " world"]), "Text wraps at TooltipWidth percent of the screen width.");
    }

    private static void TooltipTextIsRevealedOverTheFillTime()
    {
        var font = new UiFont("Arial", 10, false);
        var viewport = new UiViewport(1000, 600);
        var tooltip = new UiTooltip("W", "Hello world", null, 100, 100);
        // 250 ms fill time, 55 px of text: after 125 ms the clip is 27 px wide and the highlight band covers its last 15 pixels.
        var half = UiTooltipLayout.Build(tooltip with { AgeMs = 125 }, TooltipStyle, font, viewport, new FixedMeasure());
        var draws = half.OfType<UiTextDraw>().ToArray();
        Check(draws.Length == 2 && draws[0].Clip == new UiRect(122, 101, 27, 12) && draws[0].Color == TooltipStyle.TextColor, $"Normal color text clipped to the revealed part: {string.Join(" | ", draws.Select(d => d.ToString()))}");
        Check(draws[1].Clip == new UiRect(134, 101, 15, 12) && draws[1].Color == TooltipStyle.HighlightColor, "Highlight color on the leading 15 pixels.");
        var start = UiTooltipLayout.Build(tooltip with { AgeMs = 0 }, TooltipStyle, font, viewport, new FixedMeasure());
        Check(start.OfType<UiTextDraw>().First().Clip.IsEmpty || start.OfType<UiTextDraw>().First().Clip.Width == 0, "Nothing of the text is shown at the first moment.");
        var animated = UiTooltipLayout.Build(tooltip with { AgeMs = 125 }, TooltipStyle with { AnimateBackground = true }, font, viewport, new FixedMeasure());
        Check(animated[0] is UiFillRect { Rect.Width: 29 }, $"With TooltipAnimateBackground the box grows with the reveal: {animated[0]}");
    }

    private static void TinyWrapWidthTerminates()
    {
        var sentence = UiSentenceLayout.Layout("a b c d", Font, new FixedMeasure(), 3, true);
        Check(sentence.Lines.Length > 0 && sentence.Lines.Length < 20, Dump(sentence));
        var plain = UiSentenceLayout.Layout("a b c d", Font, new FixedMeasure(), 3, false);
        Check(plain.Lines.Length == 4, Dump(plain));
    }
}
