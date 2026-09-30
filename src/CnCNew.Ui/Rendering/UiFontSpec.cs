using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// A font as the rasterizer needs it: a family, the pixel height of the em square, weight and a horizontal squeeze.
/// <see cref="WidthPercent"/> is 100 for the natural width of the face.
/// </summary>
public readonly record struct UiFontSpec(string Family, int PixelSize, bool Bold, int WidthPercent)
{
    public override string ToString() => $"{Family}/{PixelSize}px{(Bold ? "b" : "")}{(WidthPercent == 100 ? "" : $"@{WidthPercent}%")}";
}

/// <summary>
/// Turns a WND/header-template font (name, point size, bold) into pixels the way <c>FontCharsClass::Create_GDI_Font</c>
/// (render2dsentence.cpp) does: 96 DPI always, <c>height = MulDiv(points, 96, 72)</c> (rounded to nearest), and the
/// font name <c>Generals</c> is Arial drawn with a character width of 0.40 of the height (the original passes
/// <c>lfWidth</c> = height * 0.40 to <c>CreateFont</c>). Arial's average character width is 904/2048 = 0.4414 em, so that
/// request is a horizontal scale of about 91 %.
/// </summary>
public static class UiFontPolicy
{
    public const int DotsPerInch = 96;

    public const string GeneralsFamily = "Generals";

    /// <summary>0.40 / 0.4414, rounded; see the class remarks.</summary>
    public const int GeneralsWidthPercent = 91;

    /// <summary>
    /// Families tried after the requested one when a glyph is missing (and as substitute for a family that is not installed),
    /// in this order: broad Latin/Greek/Cyrillic first, then CJK and symbols.
    /// </summary>
    public static readonly string[] FallbackFamilies =
    [
        "Arial", "Segoe UI", "Arial Unicode MS", "Microsoft YaHei", "Malgun Gothic", "Yu Gothic", "MS Gothic", "Segoe UI Symbol",
    ];

    public static int PixelSize(int points)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(points, 1);
        return (points * DotsPerInch + 36) / 72;
    }

    public static UiFontSpec Resolve(UiFont font)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(font.Name);
        var generals = string.Equals(font.Name, GeneralsFamily, StringComparison.OrdinalIgnoreCase);
        return new UiFontSpec(generals ? "Arial" : font.Name, PixelSize(Math.Max(1, font.Size)), font.Bold, generals ? GeneralsWidthPercent : 100);
    }
}
