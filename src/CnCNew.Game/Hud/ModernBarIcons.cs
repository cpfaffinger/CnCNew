using System.Collections.Immutable;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>The icons the modern skin draws on the command bar's own buttons, in place of the scheme's pictures.</summary>
internal enum ModernBarIcon
{
    /// <summary>The game's menu: three bars.</summary>
    Options,

    /// <summary>The next idle worker: a hammer.</summary>
    IdleWorker,

    /// <summary>A beacon on the map: a flag on its pole.</summary>
    Beacon,

    /// <summary>The communicator (the players, the briefing history): a speech bubble.</summary>
    Chat,
}

/// <summary>
/// Icons of lines and discs (docs/UI-MODERN.md section 20): no picture file and nothing of the game's art. An icon is drawn in a square of <c>side</c> pixels at
/// (<c>left</c>, <c>top</c>), its lines a ninth of the square thick, so it reads the same at every size of the window.
/// </summary>
internal static class ModernBarIcons
{
    public static void Draw(ImmutableArray<UiDrawCommand>.Builder commands, ModernBarIcon icon, float left, float top, float side, WndColor color)
    {
        var stroke = MathF.Max(1.5f, side / 9f);
        void Line(float x0, float y0, float x1, float y1, float scale = 1f)
            => commands.Add(new UiLineDraw(left + x0 * side, top + y0 * side, left + x1 * side, top + y1 * side, stroke * scale, color));
        void Dot(float x, float y)
        {
            var size = Math.Max(2, (int)MathF.Round(stroke * 1.2f));
            commands.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect((int)MathF.Round(left + x * side - size / 2f), (int)MathF.Round(top + y * side - size / 2f), size, size), color));
        }
        switch (icon)
        {
            case ModernBarIcon.Options:
                Line(0.12f, 0.24f, 0.88f, 0.24f);
                Line(0.12f, 0.50f, 0.88f, 0.50f);
                Line(0.12f, 0.76f, 0.88f, 0.76f);
                break;
            case ModernBarIcon.IdleWorker:
                // The head across the top right, the handle down to the bottom left.
                Line(0.42f, 0.14f, 0.86f, 0.58f, 2.4f);
                Line(0.62f, 0.38f, 0.16f, 0.84f);
                break;
            case ModernBarIcon.Beacon:
                Line(0.30f, 0.10f, 0.30f, 0.90f);
                Line(0.30f, 0.14f, 0.84f, 0.32f);
                Line(0.84f, 0.32f, 0.30f, 0.50f);
                Line(0.30f, 0.32f, 0.62f, 0.32f);
                Line(0.14f, 0.90f, 0.48f, 0.90f);
                break;
            case ModernBarIcon.Chat:
                Line(0.10f, 0.16f, 0.90f, 0.16f);
                Line(0.90f, 0.16f, 0.90f, 0.66f);
                Line(0.90f, 0.66f, 0.48f, 0.66f);
                Line(0.48f, 0.66f, 0.26f, 0.88f);
                Line(0.26f, 0.88f, 0.30f, 0.66f);
                Line(0.30f, 0.66f, 0.10f, 0.66f);
                Line(0.10f, 0.66f, 0.10f, 0.16f);
                Dot(0.32f, 0.41f);
                Dot(0.50f, 0.41f);
                Dot(0.68f, 0.41f);
                break;
        }
    }

    /// <summary>A filled disc of <paramref name="diameter"/> pixels about (<paramref name="cx"/>, <paramref name="cy"/>), one rectangle per pixel row.</summary>
    public static void Disc(ImmutableArray<UiDrawCommand>.Builder commands, int cx, int cy, int diameter, WndColor color)
    {
        if (diameter <= 0) return;
        var radius = diameter / 2f;
        var top = cy - diameter / 2;
        for (var row = 0; row < diameter; row++)
        {
            var dy = row + 0.5f - radius;
            var half = MathF.Sqrt(MathF.Max(0f, radius * radius - dy * dy));
            var width = (int)MathF.Round(half * 2f);
            if (width <= 0) continue;
            commands.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(cx - width / 2, top + row, width, 1), color));
        }
    }
}
