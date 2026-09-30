namespace CnCNew.Ui.Rendering;

/// <summary>A glyph placed in the atlas; <see cref="Region"/> is null for blank glyphs (space).</summary>
public sealed record CachedGlyph(AtlasRegion? Region, int BearingX, int BearingY, int Advance, bool IsMissing, bool IsSubstituted);

/// <summary>
/// Rasterizes each (face, code point) once and keeps it in the atlas as white RGBA with the coverage in alpha, so that the
/// vertex color tints it and alpha blending applies the coverage.
/// </summary>
public sealed class UiGlyphCache(UiGlyphAtlas atlas)
{
    private readonly Dictionary<(UiFontSpec, int), CachedGlyph> _glyphs = [];

    public int Count => _glyphs.Count;

    public CachedGlyph Get(UiFontSpec spec, IUiFontFace face, int codepoint)
    {
        var key = (spec, codepoint);
        if (_glyphs.TryGetValue(key, out var cached)) return cached;

        var missing = !face.HasGlyph(codepoint);
        var substituted = !missing && face is UiFontChain chain && chain.IsSubstituted(codepoint);
        var bitmap = face.Rasterize(codepoint);
        AtlasRegion? region = null;
        if (!bitmap.IsEmpty)
        {
            var rgba = new byte[bitmap.Width * bitmap.Height * 4];
            for (var i = 0; i < bitmap.Coverage.Length; i++)
            {
                rgba[i * 4] = 255;
                rgba[i * 4 + 1] = 255;
                rgba[i * 4 + 2] = 255;
                rgba[i * 4 + 3] = bitmap.Coverage[i];
            }
            region = atlas.Add(bitmap.Width, bitmap.Height, rgba);
        }
        var glyph = new CachedGlyph(region, bitmap.BearingX, bitmap.BearingY, bitmap.Advance, missing, substituted);
        _glyphs.Add(key, glyph);
        return glyph;
    }
}
