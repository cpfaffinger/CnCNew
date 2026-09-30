using System.Collections.Immutable;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// A requested face followed by fallback faces (<see cref="UiFontPolicy.FallbackFamilies"/>). A code point is taken from the
/// first face that has it; when no face has it, the primary face's <c>.notdef</c> glyph is used, so a missing glyph is
/// always a visible box and never an empty gap. Fallback faces open lazily, on the first code point the earlier faces lack.
/// </summary>
public sealed class UiFontChain : IUiFontFace
{
    private readonly ImmutableArray<Func<IUiFontFace?>> _factories;
    private readonly IUiFontFace?[] _opened;
    private readonly bool[] _attempted;

    /// <param name="primary">The requested face (already open); it supplies the line metrics.</param>
    /// <param name="fallbacks">Factories of the fallback faces in order; one may return null when its family is not installed.</param>
    public UiFontChain(IUiFontFace primary, IEnumerable<Func<IUiFontFace?>> fallbacks, string description)
    {
        ArgumentNullException.ThrowIfNull(primary);
        Primary = primary;
        Description = description;
        _factories = [..fallbacks];
        _opened = new IUiFontFace?[_factories.Length];
        _attempted = new bool[_factories.Length];
    }

    public IUiFontFace Primary { get; }
    public string Description { get; }
    public UiFontFaceMetrics Metrics => Primary.Metrics;

    /// <summary>Number of fallback faces that had to be opened so far (for diagnostics and tests).</summary>
    public int OpenedFallbacks => _opened.Count(face => face is not null);

    public bool HasGlyph(int codepoint) => Supplier(codepoint) is not null;

    public int Advance(int codepoint) => (Supplier(codepoint) ?? Primary).Advance(codepoint);

    public GlyphBitmap Rasterize(int codepoint) => (Supplier(codepoint) ?? Primary).Rasterize(codepoint);

    /// <summary>True when a fallback face (not the primary one) supplies the code point.</summary>
    public bool IsSubstituted(int codepoint)
    {
        var supplier = Supplier(codepoint);
        return supplier is not null && !ReferenceEquals(supplier, Primary);
    }

    private IUiFontFace? Supplier(int codepoint)
    {
        if (Primary.HasGlyph(codepoint)) return Primary;
        for (var i = 0; i < _factories.Length; i++)
        {
            if (!_attempted[i])
            {
                _attempted[i] = true;
                _opened[i] = _factories[i]();
            }
            if (_opened[i] is { } face && face.HasGlyph(codepoint)) return face;
        }
        return null;
    }

    public void Dispose()
    {
        Primary.Dispose();
        for (var i = 0; i < _opened.Length; i++)
        {
            _opened[i]?.Dispose();
            _opened[i] = null;
        }
    }
}
