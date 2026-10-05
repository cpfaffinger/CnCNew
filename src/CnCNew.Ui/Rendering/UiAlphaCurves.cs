using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// Glyph coverage curves for a device that blends in linear light. The original blends its text in the framebuffer's bytes (no sRGB write),
/// i.e. in gamma space: a pixel with 50 % coverage of white text on black ends up at byte 128. A linear-light blend (Stride with an sRGB render
/// target) gives encode(0,5) = 188 for the same pixel: edges come out brighter and the text looks bolder and blurrier. For light text over a dark
/// background the gamma-space result is reproduced exactly by using <c>decode(coverage)</c> as the alpha (the sRGB transfer function undoes itself on
/// a black background, and for a pure power law it is exact for every text color); backgrounds that are not black make it an approximation.
/// Dark text keeps the plain coverage (its usual background is light and the error there is smaller than with the opposite curve).
/// </summary>
public static class UiAlphaCurves
{
    /// <summary>Alpha table that maps a coverage byte to the alpha whose linear-light blend equals the gamma-space blend over black.</summary>
    public static byte[] LightOnDark { get; } = Build();

    /// <summary>True for colors that get <see cref="LightOnDark"/>: relative luminance of the color bytes at or above one half.</summary>
    public static bool IsLight(WndColor color) => 2126 * color.R + 7152 * color.G + 722 * color.B >= 127 * 10000;

    private static byte[] Build()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var encoded = i / 255.0;
            var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            table[i] = (byte)Math.Round(linear * 255.0);
        }
        return table;
    }
}
