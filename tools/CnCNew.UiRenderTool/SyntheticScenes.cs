using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.UiRenderTool;

/// <summary>
/// Hand-built <see cref="UiDrawList"/>s that exercise renderer paths a shipped WND rarely reaches in one screen: umlauts and
/// mixed styles, nested clips, image tinting, and flips/rotation/missing assets. Built directly as commands (no
/// <c>UiWindowTree</c>) so each case is exact and independent of layout files.
/// </summary>
internal static class SyntheticScenes
{
    private static readonly UiDrawStatistics NoStatistics = new(0, 0, 0, 0, ImmutableSortedDictionary<string, int>.Empty);
    private static readonly UiFont Generals = new("Generals", 18, false);
    private static readonly UiFont GeneralsBold = new("Generals", 24, true);
    private static readonly UiFont GeneralsSmall = new("Generals", 12, false);

    public static UiDrawList Text(UiViewport viewport)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(viewport.Bounds, new WndColor(20, 24, 32, 255)));
        var y = 24;
        void Line(string text, UiFont font, WndColor color, WndColor shadow, UiTextAlignment alignment = UiTextAlignment.Left)
        {
            var height = font.Size * 2;
            var x = alignment == UiTextAlignment.Left ? 24 : viewport.Width / 2;
            commands.Add(new UiTextDraw(text, null, font, color, shadow, x, y, alignment, viewport.Bounds));
            y += height + 8;
        }
        Line("Fenster schließen? Ja/Nein — äöüÄÖÜß, naïve café résumé", Generals, WndColor.White, WndColor.Undefined);
        Line("BOLD HEADER: GRÖSSE, ANGRIFF, VERTEIDIGUNG", GeneralsBold, new WndColor(255, 220, 80, 255), new WndColor(0, 0, 0, 220));
        Line("Small print with a drop shadow (+1,+1)", GeneralsSmall, new WndColor(200, 200, 255, 255), new WndColor(20, 20, 20, 255));
        Line("Centered line", Generals, WndColor.White, WndColor.Undefined, UiTextAlignment.Center);
        // Unresolved label: Text equals LabelKey, must draw as the counted magenta placeholder with underline.
        commands.Add(new UiTextDraw("GUI:ThisLabelDoesNotExist", "GUI:ThisLabelDoesNotExist", Generals, WndColor.White, WndColor.Undefined, 24, y, UiTextAlignment.Left, viewport.Bounds));
        y += 40;
        // A private-use codepoint no installed font has: exercises the notdef/missing-glyph counter.
        commands.Add(new UiTextDraw("Missing glyph:  end", null, Generals, WndColor.White, WndColor.Undefined, 24, y, UiTextAlignment.Left, viewport.Bounds));
        return new UiDrawList(commands.ToImmutable(), NoStatistics);
    }

    public static UiDrawList Clipping(UiViewport viewport)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(viewport.Bounds, new WndColor(24, 28, 20, 255)));
        var outer = new UiRect(viewport.Width / 6, viewport.Height / 6, viewport.Width * 2 / 3, viewport.Height * 2 / 3);
        commands.Add(new UiBorderRect(outer, new WndColor(120, 220, 120, 255)));
        commands.Add(new UiPushClip(outer));
        // A fill larger than the clip: only the intersection may appear.
        commands.Add(new UiFillRect(new UiRect(outer.X - 80, outer.Y - 80, outer.Width + 160, outer.Height + 160), new WndColor(200, 80, 80, 180)));
        // Text that starts inside and runs past the right edge of the clip.
        commands.Add(new UiTextDraw("This line is long enough to run past the clip rectangle on the right edge >>>>>>>>", null, GeneralsBold,
            WndColor.White, new WndColor(0, 0, 0, 200), outer.X + 16, outer.CenterY - 40, UiTextAlignment.Left, outer));
        // Nested clip further inside.
        var inner = outer.Inset(Math.Max(20, outer.Width / 8));
        commands.Add(new UiPushClip(inner));
        commands.Add(new UiFillRect(new UiRect(inner.X - 40, inner.Y - 40, inner.Width + 80, inner.Height + 80), new WndColor(80, 80, 220, 200)));
        commands.Add(new UiBorderRect(inner, WndColor.White));
        commands.Add(new UiPopClip());
        commands.Add(new UiPopClip());
        // An extra pop with no matching push: must be counted, not throw.
        commands.Add(new UiPopClip());
        return new UiDrawList(commands.ToImmutable(), NoStatistics);
    }

    public static UiDrawList Images(UiViewport viewport, string plainImage)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(viewport.Bounds, new WndColor(30, 30, 30, 255)));
        var size = Math.Min(viewport.Width, viewport.Height) / 6;
        var gap = size + 16;
        var x = 24;
        var y = 24;
        void Cell(string label, WndColor tint, bool flipH = false, bool flipV = false)
        {
            var rect = new UiRect(x, y, size, size);
            commands.Add(new UiImageDraw(plainImage, rect, tint, flipH, flipV));
            commands.Add(new UiTextDraw(label, null, GeneralsSmall, WndColor.White, WndColor.Undefined, x, y + size + 4, UiTextAlignment.Left, viewport.Bounds));
            x += gap;
            if (x + size > viewport.Width - 24)
            {
                x = 24;
                y += gap + 24;
            }
        }
        Cell("normal", WndColor.White);
        Cell("tinted red", new WndColor(255, 80, 80, 255));
        Cell("half alpha", new WndColor(255, 255, 255, 128));
        Cell("disabled (gray)", new WndColor(140, 140, 140, 255));
        Cell("pushed (dark)", new WndColor(90, 90, 90, 255));
        Cell("flip-h", WndColor.White, flipH: true);
        Cell("flip-v", WndColor.White, flipV: true);
        return new UiDrawList(commands.ToImmutable(), NoStatistics);
    }

    public static UiDrawList Transform(UiViewport viewport, string rotatedImage)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        commands.Add(new UiFillRect(viewport.Bounds, new WndColor(16, 16, 24, 255)));
        var size = Math.Min(viewport.Width, viewport.Height) / 4;
        commands.Add(new UiImageDraw(rotatedImage, new UiRect(40, 40, size, size), WndColor.White, false, false));
        commands.Add(new UiTextDraw($"rotated image '{rotatedImage}'", null, GeneralsSmall, WndColor.White, WndColor.Undefined, 40, 40 + size + 4, UiTextAlignment.Left, viewport.Bounds));
        // A missing image name: must draw the counted magenta placeholder, never throw or vanish.
        var missingRect = new UiRect(40 + size + 40, 40, size, size);
        commands.Add(new UiImageDraw("DefinitelyMissingImage_S4Test", missingRect, WndColor.White, false, false));
        commands.Add(new UiTextDraw("unknown image name", null, GeneralsSmall, WndColor.White, WndColor.Undefined, missingRect.X, 40 + size + 4, UiTextAlignment.Left, viewport.Bounds));
        return new UiDrawList(commands.ToImmutable(), NoStatistics);
    }
}
