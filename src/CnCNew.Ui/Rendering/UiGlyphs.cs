namespace CnCNew.Ui.Rendering;

/// <summary>
/// A rasterized glyph: 8-bit coverage, <see cref="Width"/> x <see cref="Height"/>, top row first. <see cref="BearingX"/> is the
/// offset of the left column from the pen, <see cref="BearingY"/> the distance from the baseline up to the top row.
/// <see cref="Advance"/> is the whole-pixel pen advance (no kerning, like <c>Get_Char_Spacing</c> of the original).
/// </summary>
public sealed record GlyphBitmap(int Width, int Height, int BearingX, int BearingY, int Advance, byte[] Coverage)
{
    public bool IsEmpty => Width == 0 || Height == 0;
}

/// <param name="Ascent">Pixels above the baseline (<c>tmAscent</c>).</param>
/// <param name="Descent">Pixels below the baseline.</param>
/// <param name="LineHeight">Cell height (<c>tmHeight</c> = ascent + descent); the line step without extra leading.</param>
/// <param name="Overlap">The font's <c>PixelOverlap</c> (<c>Get_Extra_Overlap</c>): glyph bitmaps are that many columns wider than their advance, and a text block is that much wider than its last pen position.</param>
public readonly record struct UiFontFaceMetrics(int Ascent, int Descent, int LineHeight, int Overlap = 0);

/// <summary>One sized face. Implementations rasterize on demand and must be deterministic for a given font file.</summary>
public interface IUiFontFace : IDisposable
{
    string Description { get; }
    UiFontFaceMetrics Metrics { get; }
    bool HasGlyph(int codepoint);

    /// <summary>Whole-pixel pen advance of a code point (the advance of the face that supplies the glyph); cheap, no rasterization.</summary>
    int Advance(int codepoint);

    /// <summary>The glyph of a code point; when <see cref="HasGlyph"/> is false the face's <c>.notdef</c> glyph (a visible box).</summary>
    GlyphBitmap Rasterize(int codepoint);
}

/// <summary>Opens the sized face (with its fallback chain) of a <see cref="UiFontSpec"/>.</summary>
public interface IUiFontProvider
{
    IUiFontFace Open(UiFontSpec spec);
}
