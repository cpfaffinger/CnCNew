using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Skins;

/// <summary>
/// One label that does not fit its window: its decorated window name, its text, what was measured against what (pixels), and whether it is too wide (the threshold R3 (a))
/// or only too tall (a wrapped label whose lines reach past the window's top and bottom; reported).
/// </summary>
public sealed record UiOverflow(string Window, string Text, string Measure, bool TooWide);

/// <summary>
/// What the readability check of a screen measured (docs/UI-MODERN.md section 9, R3): how many text lines it draws, the pixel height of its smallest font, and every label
/// that does not fit the window it belongs to.
/// </summary>
public sealed record UiReadabilityReport(int TextLines, int SmallestPixels, ImmutableArray<UiOverflow> Overflows)
{
    public int TooWide => Overflows.Count(overflow => overflow.TooWide);
    public int TooTall => Overflows.Count(overflow => !overflow.TooWide);
}

/// <summary>
/// The readability check of a drawn screen, with the text measure it is given - the renderer's GDI font for the threshold, the synthetic one in headless tests.
/// <list type="bullet">
/// <item>The smallest text: the pixel height (<see cref="UiFontPolicy.PixelSize"/>) of the smallest font of any text the draw list draws.</item>
/// <item>An overflow: a visible push button, static text, check box or radio button whose label, laid out as the draw functions lay it out
/// (<see cref="UiSentenceLayout"/> with the wrap width they use), is wider or taller than the area they give it. A word longer than a button is wider than it; a label
/// wrapped to more lines than the window is high is taller.</item>
/// </list>
/// </summary>
public static class UiReadability
{
    public static UiReadabilityReport Audit(UiWindowTree tree, UiDrawList list)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(list);
        var texts = list.Commands.OfType<UiTextDraw>().ToArray();
        var smallest = texts.Length == 0 ? 0 : texts.Min(text => UiFontPolicy.PixelSize(Math.Max(1, text.Font.Size)));
        var overflows = ImmutableArray.CreateBuilder<UiOverflow>();
        var measure = tree.Services.TextMeasure;
        foreach (var window in tree.AllWindows)
        {
            if (!window.IsVisible || window.Text is not { } value || value.Value.Length == 0 || (window.Status & WndStatus.SeeThru) != 0) continue;
            var text = value.IsLabel && tree.Services.Text is { } source && source.TryResolve(value.Value, out var resolved) ? resolved : value.Value;
            var rect = window.Rect;
            switch (window.Type)
            {
                case WndWindowType.PushButton:
                    Check(window, text, rect.Width, rect.Width, rect.Height, (window.Status & WndStatus.WrapCentered) != 0);
                    break;
                case WndWindowType.StaticText:
                    var centered = window.Definition.StaticText?.Centered ?? false;
                    var wrap = rect.Width - window.Transform.X.Length(10);
                    var room = centered ? rect.Width : rect.Width - Math.Min(rect.Width, window.Transform.X.Length(7));
                    Check(window, text, wrap, room, rect.Height, (window.Status & WndStatus.WrapCentered) != 0);
                    break;
                case WndWindowType.CheckBox:
                    Check(window, text, 0, Math.Max(0, rect.Width - rect.Height), rect.Height, false);
                    break;
                case WndWindowType.RadioButton:
                    Check(window, text, 0, rect.Width, rect.Height, false);
                    break;
            }
        }
        return new UiReadabilityReport(texts.Length, smallest, overflows.ToImmutable());

        void Check(UiWindow window, string text, int wrapWidth, int roomWidth, int roomHeight, bool wrapCentered)
        {
            var sentence = UiSentenceLayout.Layout(text, window.Font, measure, Math.Max(0, wrapWidth), wrapCentered);
            if (sentence.Width > roomWidth) overflows.Add(new UiOverflow(window.Name, text, $"width {sentence.Width} > {roomWidth}", true));
            else if (sentence.Height > roomHeight) overflows.Add(new UiOverflow(window.Name, text, $"height {sentence.Height} ({sentence.Lines.Length} lines) > {roomHeight}", false));
        }
    }
}
