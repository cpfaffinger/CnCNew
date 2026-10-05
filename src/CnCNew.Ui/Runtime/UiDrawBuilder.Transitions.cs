using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// Draw helpers for the window transitions (<c>GameWindowTransitionsStyles.cpp</c>): a push button's images alone with a global alpha
/// (<c>PushButtonImageDrawThree</c>) and a text typed in part (<c>drawTypeText</c>). They produce the same commands the full draw
/// would, restricted to one window, so a stand-in looks like the window it replaces.
/// </summary>
public sealed partial class UiWindowTree
{
    /// <summary>The left/middle/right enabled images of a push button as <see cref="UiImageDraw"/>s tinted white with <paramref name="alpha"/> (0..255); empty when the window has no such images.</summary>
    public ImmutableArray<UiDrawCommand> BuildPushButtonImages(UiWindow window, int alpha)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new DrawBuilder(this).PushButtonImages(window, alpha);
    }

    /// <summary><paramref name="text"/> laid out like the text of the static text <paramref name="window"/> (window font and enabled colours, centered or left margin, wrapped at the width minus 10, vertically centered).</summary>
    public ImmutableArray<UiDrawCommand> BuildStaticTextLines(UiWindow window, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new DrawBuilder(this).StaticTextLines(window, text);
    }

    private sealed partial class DrawBuilder
    {
        private byte _tintAlpha = 255;

        public ImmutableArray<UiDrawCommand> PushButtonImages(UiWindow window, int alpha)
        {
            _tintAlpha = (byte)Math.Clamp(alpha, 0, 255);
            var set = window.Definition.GetDrawData(WndDrawDataKind.Enabled);
            ThreePiece(Offset(window, window.Rect), set[0].Image, set[5].Image, set[6].Image, true);
            return _commands.ToImmutable();
        }

        public ImmutableArray<UiDrawCommand> StaticTextLines(UiWindow window, string text)
        {
            var (color, shadow) = TextColors(window);
            if (!color.IsVisible || text.Length == 0) return [];
            var centered = window.Definition.StaticText?.Centered ?? false;
            var margin = window.Transform.X.Length(7);
            var area = centered ? window.Rect : UiRect.FromEdges(Math.Min(window.Rect.Right, window.Rect.X + margin), window.Rect.Y, window.Rect.Right, window.Rect.Bottom);
            var alignment = centered ? UiTextAlignment.Center : UiTextAlignment.Left;
            var measure = tree.Services.TextMeasure;
            var wrapCentered = (window.Status & WndStatus.WrapCentered) != 0;
            var sentence = UiSentenceLayout.Layout(text, window.Font, measure, window.Rect.Width - window.Transform.X.Length(10), wrapCentered);
            var lineHeight = measure.LineHeight(window.Font);
            var y = area.Y + area.Height / 2 - sentence.Height / 2;
            var left = alignment == UiTextAlignment.Left ? area.X : area.X + area.Width / 2 - sentence.Width / 2;
            for (var index = 0; index < sentence.Lines.Length; index++, y += lineHeight)
            {
                var line = sentence.Lines[index];
                if (line.Text.Length == 0) continue;
                _commands.Add(new UiTextDraw(line.Text, null, window.Font, color, shadow, left + line.X, y, alignment, window.Rect));
            }

            return _commands.ToImmutable();
        }
    }
}
