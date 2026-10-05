using System.Text;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// Text measurement on real font faces for <see cref="UiTreeServices"/>: the width of a line is the sum of whole-pixel glyph
/// advances (no kerning, like <c>Render2DSentenceClass::Get_Text_Extents</c> in render2dsentence.cpp), the line height is
/// the face cell height. Faces are opened once per resolved <see cref="UiFontSpec"/> and shared with the renderer.
/// </summary>
public sealed class UiTextMetrics(IUiFontProvider provider) : IUiTextMeasure, IDisposable
{
    private readonly Dictionary<UiFontSpec, IUiFontFace> _faces = [];
    private readonly Dictionary<(UiFontSpec, int), int> _advances = [];

    public int OpenFaceCount => _faces.Count;

    public IUiFontFace Face(UiFont font) => Face(UiFontPolicy.Resolve(font));

    public IUiFontFace Face(UiFontSpec spec)
    {
        if (!_faces.TryGetValue(spec, out var face))
        {
            face = provider.Open(spec);
            _faces.Add(spec, face);
        }
        return face;
    }

    public int MeasureWidth(string text, UiFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spec = UiFontPolicy.Resolve(font);
        var face = Face(spec);
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!UiTextRuns.IsDrawable(rune)) continue;
            var key = (spec, rune.Value);
            if (!_advances.TryGetValue(key, out var advance))
            {
                advance = face.Advance(rune.Value);
                _advances.Add(key, advance);
            }
            width += advance;
        }
        return width;
    }

    public int LineHeight(UiFont font) => Face(font).Metrics.LineHeight;

    public int ExtraOverlap(UiFont font) => Face(font).Metrics.Overlap;

    public void Dispose()
    {
        foreach (var face in _faces.Values) face.Dispose();
        _faces.Clear();
        _advances.Clear();
    }
}

/// <summary>One glyph of a laid-out line: the code point and the pen position of its origin relative to the line start.</summary>
public readonly record struct UiGlyphPlacement(int Codepoint, int PenX);

public static class UiTextRuns
{
    /// <summary>Control characters have no glyph and no advance (lines are already wrapped by the tree).</summary>
    public static bool IsDrawable(Rune rune) => rune.Value >= 0x20 && rune.Value != 0x7F && !(rune.Value >= 0x80 && rune.Value < 0xA0);

    public static List<UiGlyphPlacement> Layout(string text, IUiFontFace face)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(face);
        var placements = new List<UiGlyphPlacement>(text.Length);
        var pen = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsDrawable(rune)) continue;
            placements.Add(new UiGlyphPlacement(rune.Value, pen));
            pen += face.Advance(rune.Value);
        }
        return placements;
    }

    /// <summary>Line origin: <see cref="UiTextDraw.X"/> is the final left edge, the baseline lies one ascent below the line top.</summary>
    public static (int X, int Baseline) Origin(UiTextDraw draw, UiFontFaceMetrics metrics) => (draw.X, draw.Y + metrics.Ascent);
}
