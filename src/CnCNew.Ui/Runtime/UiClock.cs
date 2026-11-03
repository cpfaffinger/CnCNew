using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>
/// <c>W3DDisplay::drawRemainingRectClock</c> (<c>GadgetButtonDrawInverseClock</c>): over a button whose work is <c>percent</c> done, the share from <c>percent</c> to 100 % - swept clockwise from twelve
/// o'clock around the button's centre - is covered with the scheme's <c>BuildUpClockColor</c>, so the cameo appears as its clock runs. The original draws it as triangles; this draws the same
/// region as one span per pixel row (the renderer draws rectangles), each pixel taken by its centre.
/// </summary>
public static class UiClock
{
    public static ImmutableArray<UiFillRect> Remaining(UiRect rect, int percent, WndColor color)
    {
        // The original's sanity: nothing outside 0..99.
        if (percent < 0 || percent > 99 || rect.IsEmpty) return [];
        if (percent == 0) return [new UiFillRect(rect, color)];
        var spans = ImmutableArray.CreateBuilder<UiFillRect>();
        var start = percent / 100.0 * 2 * Math.PI;
        var midX = rect.X + rect.Width / 2;
        var midY = rect.Y + rect.Height / 2;
        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            var run = -1;
            for (var x = rect.X; x <= rect.Right; x++)
            {
                var covered = x < rect.Right && Covered(x + 0.5 - midX, y + 0.5 - midY, start);
                if (covered && run < 0) run = x;
                else if (!covered && run >= 0)
                {
                    spans.Add(new UiFillRect(new UiRect(run, y, x - run, 1), color));
                    run = -1;
                }
            }
        }
        return spans.ToImmutable();
    }

    /// <summary>Whether the point lies in the uncovered sweep's complement: its angle clockwise from twelve o'clock is at least <paramref name="start"/>.</summary>
    private static bool Covered(double dx, double dy, double start)
    {
        var angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;
        return angle >= start;
    }
}
