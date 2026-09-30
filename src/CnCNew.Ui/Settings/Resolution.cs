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

    private static bool Fits(Resolution value, Resolution desktop) => value.Width <= desktop.Width && value.Height <= desktop.Height;
}
