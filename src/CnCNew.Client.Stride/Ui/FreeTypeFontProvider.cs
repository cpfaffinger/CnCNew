using System.Runtime.InteropServices;
using CnCNew.Ui.Rendering;
using Microsoft.Win32;
using SharpFont;

namespace CnCNew.Client.Stride.Ui;

/// <summary>
/// Rasterizes the fonts installed in Windows with FreeType (<c>Stride.SharpFont</c>, the library Stride itself ships). The
/// original draws GDI fonts (Arial; the name "Generals" is Arial as well, see <see cref="UiFontPolicy"/>) into 32-bit
/// bitmaps with the face installed on the machine, so the same system fonts are the faithful source; no font is bundled.
/// A requested family that is not installed is replaced by the first installed entry of
/// <see cref="UiFontPolicy.FallbackFamilies"/>; every such replacement is recorded in <see cref="Substitutions"/>
/// (and written once to stderr), a machine without any usable font fails loudly.
/// </summary>
internal sealed class FreeTypeFontProvider : IUiFontProvider, IDisposable
{
    private readonly Library _library = new();
    private readonly FontFileCatalog _catalog;
    private readonly SortedDictionary<string, string> _substitutions = new(StringComparer.OrdinalIgnoreCase);

    public FreeTypeFontProvider() : this(WindowsFontCatalog.Load()) { }

    public FreeTypeFontProvider(FontFileCatalog catalog) => _catalog = catalog;

    public FontFileCatalog Catalog => _catalog;

    /// <summary>Requested family (or "family bold") to the family or weight that was used instead.</summary>
    public IReadOnlyDictionary<string, string> Substitutions => _substitutions;

    public IUiFontFace Open(UiFontSpec spec)
    {
        var primary = OpenPrimary(spec);
        var fallbacks = UiFontPolicy.FallbackFamilies
            .Where(family => !family.Equals(primary.Family, StringComparison.OrdinalIgnoreCase))
            .Select(family => (Func<IUiFontFace?>)(() => TryOpenFace(family, spec, out _, out _)))
            .ToArray();
        return new UiFontChain(primary.Face, fallbacks, $"{spec} -> {primary.Face.Description}");
    }

    private (IUiFontFace Face, string Family) OpenPrimary(UiFontSpec spec)
    {
        var family = spec.Family;
        var face = TryOpenFace(family, spec, out var exact, out var entry);
        if (face is null)
        {
            foreach (var candidate in UiFontPolicy.FallbackFamilies)
            {
                family = candidate;
                face = TryOpenFace(candidate, spec, out exact, out entry);
                if (face is not null)
                {
                    Note(spec.Family, candidate);
                    break;
                }
            }
        }
        if (face is null || entry is null)
            throw new FileNotFoundException($"No usable font for '{spec.Family}': neither it nor any of {string.Join(", ", UiFontPolicy.FallbackFamilies)} is installed.");
        if (!exact) Note($"{spec.Family} bold", $"{entry.Family} regular");
        return (face, family);
    }

    private FreeTypeFace? TryOpenFace(string family, UiFontSpec spec, out bool exact, out FontFileEntry? entry)
    {
        entry = _catalog.Find(family, spec.Bold, out exact);
        if (entry is null || !File.Exists(entry.Path)) return null;
        var face = _library.NewFace(entry.Path, 0);
        var width = spec.WidthPercent == 100 ? 0u : (uint)Math.Max(1, (spec.PixelSize * spec.WidthPercent + 50) / 100);
        face.SetPixelSizes(width, (uint)spec.PixelSize);
        return new FreeTypeFace(face, $"{entry.Family}{(entry.Bold ? " Bold" : "")} {spec.PixelSize}px @{spec.WidthPercent}% [{Path.GetFileName(entry.Path)}]");
    }

    private void Note(string requested, string used)
    {
        if (_substitutions.TryAdd(requested, used)) Console.Error.WriteLine($"warning: UI font '{requested}' is not available, using '{used}'.");
    }

    public void Dispose() => _library.Dispose();
}

internal sealed class FreeTypeFace : IUiFontFace
{
    private readonly Face _face;
    private readonly Dictionary<int, int> _advances = [];

    public FreeTypeFace(Face face, string description)
    {
        _face = face;
        Description = description;
        var metrics = face.Size.Metrics;
        var ascent = Ceiling(metrics.Ascender.Value);
        var descent = Ceiling(-metrics.Descender.Value);
        Metrics = new UiFontFaceMetrics(ascent, descent, ascent + descent);
    }

    public string Description { get; }
    public UiFontFaceMetrics Metrics { get; }

    public bool HasGlyph(int codepoint) => _face.GetCharIndex((uint)codepoint) != 0;

    public int Advance(int codepoint)
    {
        if (_advances.TryGetValue(codepoint, out var cached)) return cached;
        Load(codepoint);
        var advance = Round(_face.Glyph.Advance.X.Value);
        _advances.Add(codepoint, advance);
        return advance;
    }

    public GlyphBitmap Rasterize(int codepoint)
    {
        // FT_LOAD_RENDER in one call, not a separate Load()+RenderGlyph(): a two-step load-then-render left the glyph
        // slot's bitmap at PixelMode.None (an unrendered outline) here, even though the identical two-step sequence works
        // in a bare FreeType console program; combining the flag is the standard FreeType recipe and is reliable in both.
        var index = _face.GetCharIndex((uint)codepoint);
        _face.LoadGlyph(index, LoadFlags.Render, LoadTarget.Normal);
        var slot = _face.Glyph;
        // The advance must come from the slot that was just rendered: Advance(codepoint) would call Load again (a fresh,
        // unrendered glyph), invalidating the bitmap/pitch/buffer read below before it runs.
        var advance = Round(slot.Advance.X.Value);
        _advances[codepoint] = advance;
        var bitmap = slot.Bitmap;
        var width = bitmap.Width;
        var rows = bitmap.Rows;
        if (width == 0 || rows == 0) return new GlyphBitmap(0, 0, 0, 0, advance, []);
        if (bitmap.PixelMode != PixelMode.Gray)
            throw new InvalidDataException($"Glyph U+{codepoint:X4} of {Description} rendered as {bitmap.PixelMode}, expected 8-bit coverage.");
        var coverage = new byte[width * rows];
        var pitch = bitmap.Pitch;
        var row = new byte[Math.Abs(pitch)];
        for (var y = 0; y < rows; y++)
        {
            var source = pitch >= 0 ? bitmap.Buffer + y * pitch : bitmap.Buffer + (rows - 1 - y) * -pitch;
            Marshal.Copy(source, row, 0, width);
            Array.Copy(row, 0, coverage, y * width, width);
        }
        return new GlyphBitmap(width, rows, slot.BitmapLeft, slot.BitmapTop, advance, coverage);
    }

    private void Load(int codepoint) => _face.LoadGlyph(_face.GetCharIndex((uint)codepoint), LoadFlags.Default, LoadTarget.Normal);

    private static int Ceiling(int value26Dot6) => (value26Dot6 + 63) >> 6;

    private static int Round(int value26Dot6) => (value26Dot6 + 32) >> 6;

    public void Dispose() => _face.Dispose();
}

/// <summary>Reads the installed fonts from the Windows registry (machine and current user lists).</summary>
internal static class WindowsFontCatalog
{
    public static FontFileCatalog Load()
    {
        var entries = new List<(string Name, string Path)>();
        var fontDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        foreach (var (hive, relative) in new[] { (Registry.LocalMachine, true), (Registry.CurrentUser, false) })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key is null) continue;
            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is not string file || file.Length == 0) continue;
                entries.Add((name, Path.IsPathRooted(file) ? file : relative ? Path.Combine(fontDirectory, file) : file));
            }
        }
        return FontFileCatalog.FromRegistryNames(entries);
    }
}
