using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Layout;

public enum UiScaleMode
{
    /// <summary>The original: both axes scale independently from the creation resolution to the display (<c>parseScreenRect</c>).</summary>
    Stretch,
    /// <summary>One scale factor for both axes; the creation area is centered (letterbox or pillarbox).</summary>
    Uniform,
    /// <summary>Uniform scale, but every top-level group is anchored to the nearest edge or the center; full-extent groups stretch.</summary>
    Anchor,
}

/// <summary>How one axis of a window maps from the creation resolution to the display.</summary>
public enum UiAxisAnchor
{
    Stretch,
    Start,
    Center,
    End,
}

/// <summary>
/// Scaling options. <see cref="TextScale"/> is an accessibility factor applied to every font after resolution scaling;
/// <see cref="MinimumHitTarget"/> (pixels, 0 = off) enlarges the hit area of small controls without moving them.
/// <see cref="ResolutionFontAdjustment"/> is <c>ResolutionFontAdjustment</c> of <c>Language.ini</c> (retail: 0.7).
/// </summary>
public sealed record UiScaleOptions
{
    public UiScaleMode Mode { get; init; } = UiScaleMode.Stretch;
    public decimal TextScale { get; init; } = 1m;
    public int MinimumHitTarget { get; init; }
    public decimal ResolutionFontAdjustment { get; init; } = 0.7m;

    public static readonly UiScaleOptions Original = new();
}

/// <summary><c>Offset + floor(value * Numerator / Denominator)</c>: exact rational integer mapping of one axis.</summary>
public readonly record struct UiAxisTransform(long Numerator, long Denominator, int Offset)
{
    public int Map(int value) => Offset + (int)FloorDivide(value * Numerator, Denominator);

    /// <summary>Scales a length (not a coordinate), at least one pixel for positive input.</summary>
    public int Length(int value) => value <= 0 ? 0 : Math.Max(1, (int)FloorDivide(value * Numerator, Denominator));

    public static long FloorDivide(long value, long divisor)
    {
        var quotient = value / divisor;
        return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
    }
}

public readonly record struct UiTransform(UiAxisTransform X, UiAxisTransform Y, UiAxisAnchor AnchorX, UiAxisAnchor AnchorY)
{
    public UiRect Map(WndScreenRect rect) => UiRect.FromEdges(X.Map(rect.Left), Y.Map(rect.Top), X.Map(rect.Right), Y.Map(rect.Bottom));
}

/// <summary>
/// Maps authored WND rectangles to display pixels. All modes use exact integer arithmetic
/// (<see cref="UiAxisTransform"/>); the original computes <c>(Int)(x * (Real)width / (Real)creationWidth)</c> in single precision,
/// which can be one pixel lower for some values (for example 100 * 2.4f = 239.99998 → 239 instead of 240).
/// Both edges of a window are mapped, the size is their difference, so edges shared at the creation resolution stay shared.
/// </summary>
public static class UiScaler
{
    /// <summary><c>DEFAULT_DISPLAY_WIDTH</c>/<c>HEIGHT</c> of the original, the base of the font size adjustment.</summary>
    public const int DefaultDisplayWidth = 800;
    public const int DefaultDisplayHeight = 600;

    /// <summary>The larger scale that fits the whole creation area into the viewport, as a fraction.</summary>
    public static (long Numerator, long Denominator) UniformScale(UiViewport viewport, int creationWidth, int creationHeight)
        => (long)viewport.Width * creationHeight <= (long)viewport.Height * creationWidth
            ? (viewport.Width, creationWidth)
            : (viewport.Height, creationHeight);

    public static UiTransform Create(UiViewport viewport, int creationWidth, int creationHeight, UiAxisAnchor anchorX, UiAxisAnchor anchorY)
    {
        var (numerator, denominator) = UniformScale(viewport, creationWidth, creationHeight);
        return new UiTransform(
            Axis(anchorX, viewport.Width, creationWidth, numerator, denominator),
            Axis(anchorY, viewport.Height, creationHeight, numerator, denominator),
            anchorX, anchorY);
    }

    private static UiAxisTransform Axis(UiAxisAnchor anchor, int size, int creation, long numerator, long denominator)
    {
        if (anchor == UiAxisAnchor.Stretch) return new UiAxisTransform(size, creation, 0);
        var content = (int)UiAxisTransform.FloorDivide(creation * numerator, denominator);
        var offset = anchor switch
        {
            UiAxisAnchor.Start => 0,
            UiAxisAnchor.Center => (size - content) / 2,
            _ => size - content,
        };
        return new UiAxisTransform(numerator, denominator, offset);
    }

    /// <summary>
    /// Anchor of one axis of a top-level group: full extent stretches, otherwise the authored center decides
    /// (first third: start, last third: end, else center).
    /// </summary>
    public static UiAxisAnchor ClassifyAxis(int low, int high, int extent)
    {
        if (low <= 0 && high >= extent) return UiAxisAnchor.Stretch;
        var twiceCenter = (long)low + high;
        if (twiceCenter * 3 < 2L * extent) return UiAxisAnchor.Start;
        if (twiceCenter * 3 > 4L * extent) return UiAxisAnchor.End;
        return UiAxisAnchor.Center;
    }

    /// <summary>
    /// The transform of one window and the transform its children inherit (null: the children are group roots themselves).
    /// Stretch and Uniform use one transform for everything. Anchor: a window that covers the whole creation area is a
    /// stretched canvas whose children form their own groups; any other group root picks per axis an anchor
    /// (<see cref="ClassifyAxis"/>) that all its descendants share, so a dialog keeps its inner layout.
    /// </summary>
    public static (UiTransform Transform, UiTransform? Inherited) ForWindow(WndScreenRect rect, UiTransform? inherited, UiViewport viewport, UiScaleMode mode)
    {
        int cw = rect.CreationWidth, ch = rect.CreationHeight;
        switch (mode)
        {
            case UiScaleMode.Stretch:
                return (Create(viewport, cw, ch, UiAxisAnchor.Stretch, UiAxisAnchor.Stretch), null);
            case UiScaleMode.Uniform:
                return (Create(viewport, cw, ch, UiAxisAnchor.Center, UiAxisAnchor.Center), null);
        }
        if (inherited is { } group)
        {
            var own = Create(viewport, cw, ch, group.AnchorX, group.AnchorY);
            return (own, own);
        }
        if (rect.CoversCreationArea)
            return (Create(viewport, cw, ch, UiAxisAnchor.Stretch, UiAxisAnchor.Stretch), null);
        var transform = Create(viewport, cw, ch, ClassifyAxis(rect.Left, rect.Right, cw), ClassifyAxis(rect.Top, rect.Bottom, ch));
        return (transform, transform);
    }

    /// <summary>
    /// Point size of a window font. <see cref="UiScaleMode.Stretch"/> reproduces the original: header template fonts grow
    /// with <c>GlobalLanguage::adjustFontSize</c> (classic method: <c>1 + (width / 800 - 1) * adjustment</c>, clamped to
    /// [1, 2], floored), window <c>FONT</c> sizes stay as authored. Uniform and Anchor scale every font with the window's
    /// uniform scale. The accessibility <see cref="UiScaleOptions.TextScale"/> multiplies the result; the size is at least 1.
    /// </summary>
    public static int ScaleFontSize(int size, bool fromHeaderTemplate, UiViewport viewport, UiScaleOptions options, int creationWidth, int creationHeight)
    {
        decimal scaled;
        if (options.Mode == UiScaleMode.Stretch)
        {
            scaled = size;
            if (fromHeaderTemplate)
            {
                var factor = 1m + ((decimal)viewport.Width / DefaultDisplayWidth - 1m) * options.ResolutionFontAdjustment;
                factor = Math.Clamp(factor, 1m, 2m);
                scaled = Math.Floor(size * factor);
            }
        }
        else
        {
            var (numerator, denominator) = UniformScale(viewport, creationWidth, creationHeight);
            scaled = size * (decimal)numerator / denominator;
        }
        return Math.Max(1, (int)Math.Floor(scaled * options.TextScale));
    }
}
