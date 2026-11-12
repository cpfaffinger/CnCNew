using System.Collections.Immutable;

namespace CnCNew.Ui.Settings;

public sealed record AspectRatio(string Name, int Width, int Height)
{
    public double Value => Width / (double)Height;
}

public readonly record struct Resolution(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
    public override string ToString() => $"{Width}x{Height}";
}

/// <param name="Adjusted">True when <see cref="Result"/> differs from the request; <see cref="Reason"/> says why.</param>
public sealed record ResolutionFit(Resolution Requested, Resolution Result, bool Adjusted, string Reason);

/// <summary>Standard display resolutions and the windowed-mode fit.</summary>
public static class Resolutions
{
    /// <summary>The original game's UI is authored for 800x600 (<c>DEFAULT_DISPLAY_WIDTH/HEIGHT</c>); smaller is not supported.</summary>
    public static Resolution Minimum { get; } = new(800, 600);
    public static Resolution Maximum { get; } = new(16384, 16384);

    // 21:9 is the marketing name of 64:27 (2560x1080) and 43:18 (3440x1440); both classify as 21:9.
    public static ImmutableArray<AspectRatio> AspectRatios { get; } =
    [
        new("4:3", 4, 3),
        new("16:10", 16, 10),
        new("16:9", 16, 9),
        new("21:9", 64, 27),
        new("32:9", 32, 9),
    ];

    /// <summary>Ascending by aspect ratio, then by pixel count.</summary>
    public static ImmutableArray<Resolution> Standard { get; } =
    [
        new(800, 600), new(1024, 768), new(1152, 864), new(1280, 960), new(1400, 1050), new(1600, 1200), new(2048, 1536),
        new(1280, 800), new(1440, 900), new(1680, 1050), new(1920, 1200), new(2560, 1600),
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3200, 1800), new(3840, 2160),
        new(2560, 1080), new(3440, 1440), new(3840, 1600), new(5120, 2160),
        new(3840, 1080), new(5120, 1440),
    ];

    /// <summary>The aspect ratio within 3 % of <paramref name="resolution"/>, or <c>null</c> for a custom shape.</summary>
    public static AspectRatio? Classify(Resolution resolution)
    {
        if (resolution.Width <= 0 || resolution.Height <= 0) return null;
        var value = resolution.Width / (double)resolution.Height;
        return AspectRatios
            .Select(aspect => (aspect, distance: Math.Abs(value / aspect.Value - 1)))
            .Where(pair => pair.distance <= 0.03)
            .OrderBy(pair => pair.distance)
            .Select(pair => pair.aspect)
            .FirstOrDefault();
    }

    /// <summary>
    /// Fits a windowed-mode request into the usable desktop area. A request that fits is kept. Otherwise the largest
    /// standard resolution of the same aspect class that fits is taken, then the largest standard resolution of any
    /// class. Requests outside <see cref="Minimum"/>..<see cref="Maximum"/> are clamped first; a desktop smaller than
    /// the minimum yields the minimum and says so.
    /// </summary>
    public static ResolutionFit BestFit(int width, int height, int desktopWidth, int desktopHeight)
    {
        if (desktopWidth <= 0 || desktopHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(desktopWidth), $"Desktop size {desktopWidth}x{desktopHeight} must be positive.");
        var requested = new Resolution(width, height);
        var desktop = new Resolution(desktopWidth, desktopHeight);
        var candidate = new Resolution(Math.Clamp(width, Minimum.Width, Maximum.Width), Math.Clamp(height, Minimum.Height, Maximum.Height));
        var raised = candidate != requested;

        if (desktop.Width < Minimum.Width || desktop.Height < Minimum.Height)
            return new ResolutionFit(requested, Minimum, requested != Minimum, $"desktop {desktop} is smaller than the minimum {Minimum}");
        if (Fits(candidate, desktop))
            return new ResolutionFit(requested, candidate, raised, raised ? $"clamped to the supported range {Minimum}..{Maximum}" : "fits");

        var aspect = Classify(candidate);
        var sameAspect = Standard.Where(value => Fits(value, desktop) && aspect is not null && Classify(value) == aspect)
            .OrderByDescending(value => value.Pixels).ThenByDescending(value => value.Width).ToArray();
        if (sameAspect.Length > 0)
            return new ResolutionFit(requested, sameAspect[0], true, $"largest {aspect!.Name} standard resolution inside desktop {desktop}");
        // The desktop is at least the minimum here, so 800x600 always qualifies.
        var any = Standard.Where(value => Fits(value, desktop))
            .OrderByDescending(value => value.Pixels).ThenByDescending(value => value.Width).First();
        return new ResolutionFit(requested, any, true, $"largest standard resolution inside desktop {desktop}");
    }

    /// <summary>
    /// The sizes the options offer: every standard resolution up to the screen's size, and every other landscape mode of the display that reaches the minimum (5:4 and the like) and
    /// is not larger than the screen in width or in height - a size the screen cannot show is never offered, whatever a downscaling driver lists. Ascending by width, then height.
    /// </summary>
    /// <param name="screen">The bound: the whole virtual desktop for a window (all monitors together), the display's own desktop for a full screen; an unknown screen (zero) offers the modes alone, or the standard sizes when there are none either.</param>
    /// <param name="modes">The display modes of the adapter (sizes only; empty when the host cannot enumerate them).</param>
    public static ImmutableArray<Resolution> Offered(Resolution screen, IEnumerable<Resolution> modes)
    {
        var knownScreen = screen.Width > 0 && screen.Height > 0;
        var supported = new HashSet<Resolution>(modes.Where(mode => mode.Width >= Minimum.Width && mode.Height >= Minimum.Height && (!knownScreen || Fits(mode, screen))));
        var offered = new HashSet<Resolution>();
        foreach (var size in Standard)
            if ((knownScreen && Fits(size, screen)) || (!knownScreen && (supported.Contains(size) || supported.Count == 0))) offered.Add(size);
        foreach (var mode in supported)
            if (mode.Width >= mode.Height) offered.Add(mode);
        return [.. offered.OrderBy(size => size.Width).ThenBy(size => size.Height)];
    }

    /// <summary>The box around every monitor (desktop coordinates, right and bottom exclusive): side by side, 1920x1080 and 1280x1024 are 3200x1080; none is a zero size.</summary>
    public static Resolution AllMonitors(IReadOnlyList<(int Left, int Top, int Right, int Bottom)> monitors) =>
        monitors.Count == 0 ? default : new Resolution(monitors.Max(m => m.Right) - monitors.Min(m => m.Left), monitors.Max(m => m.Bottom) - monitors.Min(m => m.Top));

    /// <summary>The monitor a window is on: the one that holds the window's centre, the first when none does (a window dragged off every screen); -1 without monitors.</summary>
    public static int MonitorUnder(int centreX, int centreY, IReadOnlyList<(int Left, int Top, int Right, int Bottom)> monitors)
    {
        for (var i = 0; i < monitors.Count; i++)
            if (centreX >= monitors[i].Left && centreX < monitors[i].Right && centreY >= monitors[i].Top && centreY < monitors[i].Bottom) return i;
        return monitors.Count == 0 ? -1 : 0;
    }

    /// <summary>The bound of the sizes the options offer and a stored size is kept to: a window may reach over all monitors, a full screen stays on the display it is on.</summary>
    public static Resolution ScreenFor(DisplayMode mode, Resolution desktop, Resolution virtualDesktop) =>
        mode is DisplayMode.Windowed or DisplayMode.Borderless && virtualDesktop.Width > 0 && virtualDesktop.Height > 0 ? new Resolution(Math.Max(desktop.Width, virtualDesktop.Width), Math.Max(desktop.Height, virtualDesktop.Height)) : desktop;

    /// <summary>
    /// What a stored size and display mode become on this display: a window or a frameless window of a size that no longer fits the desktop is brought to the largest standard size that does
    /// (<see cref="BestFit"/>); a full screen of a size the display's modes do not list takes the largest listed mode that is not larger, or the smallest listed one, or the desktop's size
    /// when no modes are known. A request the display offers is kept as it is.
    /// </summary>
    public static Resolution FitToDisplay(Resolution requested, DisplayMode mode, Resolution desktop, IReadOnlyCollection<Resolution> modes, Resolution virtualDesktop = default)
    {
        if (mode == DisplayMode.BorderlessFullscreen && desktop.Width > 0 && desktop.Height > 0) return desktop;
        if (mode == DisplayMode.Fullscreen)
        {
            var listed = modes.Where(size => size.Width >= Minimum.Width && size.Height >= Minimum.Height).ToArray();
            if (listed.Length > 0)
            {
                if (listed.Contains(requested)) return requested;
                var below = listed.Where(size => size.Width <= requested.Width && size.Height <= requested.Height).OrderByDescending(size => size.Pixels).ThenByDescending(size => size.Width).ToArray();
                return below.Length > 0 ? below[0] : listed.OrderBy(size => size.Pixels).First();
            }
        }
        var bound = ScreenFor(mode, desktop, virtualDesktop);
        return bound.Width > 0 && bound.Height > 0 ? BestFit(requested.Width, requested.Height, bound.Width, bound.Height).Result : requested;
    }

    private static bool Fits(Resolution value, Resolution desktop) => value.Width <= desktop.Width && value.Height <= desktop.Height;
}
