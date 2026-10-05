using System.Runtime.InteropServices;
using System.Text;
using CnCNew.Ui.Rendering;

namespace CnCNew.Client.Stride.Ui;

/// <summary>A font provider that reports which requested families it had to replace.</summary>
internal interface IUiFontSource : IUiFontProvider, IDisposable
{
    /// <summary>Requested family (or "family bold") to what stood in for it.</summary>
    IReadOnlyDictionary<string, string> Substitutions { get; }
}

/// <summary>Which rasterizer <see cref="UiRenderer"/> takes its glyphs from.</summary>
internal enum UiFontBackend
{
    /// <summary>GDI like the original (<see cref="GdiFontProvider"/>).</summary>
    Gdi,
    /// <summary>FreeType (<see cref="FreeTypeFontProvider"/>), kept for comparison.</summary>
    FreeType,
}

/// <summary>
/// The font pipeline of the original on Windows: a GDI font (<c>CreateFont</c>) selected into a memory DC, every glyph
/// drawn with <c>ExtTextOut</c> into a 24-bit DIB and read back, the advance taken from <c>GetTextExtentPoint32</c>
/// (<c>FontCharsClass::Create_GDI_Font</c>, <c>Store_GDI_Char</c>, <c>Get_Char_Spacing</c> in render2dsentence.cpp). The
/// original keeps the glyph in an A4R4G4B4 texture: the coverage is quantized to 16 steps (<c>pixel_value &gt;&gt; 4</c>) and
/// an all-zero low nibble that is not zero stays invisible; <see cref="GdiFontFace.Rasterize"/> reproduces both. This is
/// the face source of the client because the hinted whole-pixel advances and the cell metrics of GDI differ from any
/// outline renderer (see <c>docs/TEXT-AUDIT.md</c>); <see cref="FreeTypeFontProvider"/> remains for comparison.
/// A requested family that GDI does not have (it would silently substitute another) is detected through
/// <c>GetTextFace</c>, replaced by the first installed entry of <see cref="UiFontPolicy.FallbackFamilies"/> and recorded in
/// <see cref="Substitutions"/> and on stderr; only when none of those exists the GDI substitute is used, also recorded.
/// </summary>
internal sealed class GdiFontProvider : IUiFontSource
{
    private readonly SortedDictionary<string, string> _substitutions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _installed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GdiFontFace> _faces = [];

    /// <summary>Requested family (or "family bold") to what stood in for it.</summary>
    public IReadOnlyDictionary<string, string> Substitutions => _substitutions;

    public IUiFontFace Open(UiFontSpec spec)
    {
        var primary = OpenPrimary(spec);
        var fallbacks = UiFontPolicy.FallbackFamilies
            .Where(family => !family.Equals(primary.FaceName, StringComparison.OrdinalIgnoreCase))
            .Select(family => (Func<IUiFontFace?>)(() => TryOpen(family, spec)))
            .ToArray();
        return new UiFontChain(primary, fallbacks, $"{spec} -> {primary.Description}");
    }

    private GdiFontFace OpenPrimary(UiFontSpec spec)
    {
        var face = TryOpen(spec.Family, spec);
        if (face is not null) return face;
        foreach (var candidate in UiFontPolicy.FallbackFamilies)
        {
            face = TryOpen(candidate, spec);
            if (face is null) continue;
            Note(spec.Family, candidate);
            return face;
        }
        // Nothing of the list is installed: the original would show whatever GDI picks; do the same, but say so.
        face = Create(spec.Family, spec);
        Note(spec.Family, face.FaceName);
        return face;
    }

    /// <summary>The face when GDI really has the family, otherwise null (GDI would have substituted).</summary>
    private GdiFontFace? TryOpen(string family, UiFontSpec spec)
    {
        if (_installed.TryGetValue(family, out var known) && !known) return null;
        var face = Create(family, spec);
        var installed = face.FaceName.Equals(family, StringComparison.OrdinalIgnoreCase);
        _installed[family] = installed;
        if (installed) return face;
        face.Dispose();
        _faces.Remove(face);
        return null;
    }

    private GdiFontFace Create(string family, UiFontSpec spec)
    {
        var face = new GdiFontFace(family, spec);
        _faces.Add(face);
        return face;
    }

    private void Note(string requested, string used)
    {
        if (_substitutions.TryAdd(requested, used)) Console.Error.WriteLine($"warning: UI font '{requested}' is not available, using '{used}'.");
    }

    public void Dispose()
    {
        foreach (var face in _faces) face.Dispose();
        _faces.Clear();
    }
}

/// <summary>One GDI font at one pixel height in its own memory DC and DIB.</summary>
internal sealed class GdiFontFace : IUiFontFace
{
    private const int FwNormal = 400, FwBold = 700, DefaultCharset = 1, OutDefaultPrecis = 0, ClipDefaultPrecis = 0;
    private const int AntialiasedQuality = 4, VariablePitch = 2, EtoOpaque = 2, GgiMarkNonexistingGlyphs = 1;

    private readonly object _gate = new();
    private readonly Dictionary<int, int> _advances = [];
    private readonly Dictionary<int, bool> _glyphs = [];
    private readonly int _pixelOverlap, _overhang, _dibSize, _dibStride;
    private IntPtr _dc, _font, _bitmap, _bits, _oldFont, _oldBitmap;

    public GdiFontFace(string family, UiFontSpec spec)
    {
        // Create_GDI_Font: the height is in logical units at 96 dpi (UiFontSpec.PixelSize is MulDiv(points, 96, 72)), the
        // font "Generals" is Arial with a character width of 0.40 of the height, the overlap is height / 8 within [0, 4].
        var generals = spec.WidthPercent == UiFontPolicy.GeneralsWidthPercent;
        var width = generals ? (int)(spec.PixelSize * 0.40f) : 0;
        _pixelOverlap = Math.Clamp(spec.PixelSize / 8, 0, 4);
        _dibSize = Math.Max(16, spec.PixelSize * 2);
        _dibStride = (_dibSize * 3 + 3) & ~3;

        var screen = Native.GetDC(IntPtr.Zero);
        try
        {
            _dc = Native.CreateCompatibleDC(screen);
            var header = new Native.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<Native.BitmapInfoHeader>(), Width = _dibSize, Height = -_dibSize, Planes = 1, BitCount = 24,
            };
            _bitmap = Native.CreateDIBSection(screen, ref header, 0, out _bits, IntPtr.Zero, 0);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
        if (_dc == IntPtr.Zero || _bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
            throw new InvalidOperationException($"GDI could not create a memory DC and DIB for '{family}'.");

        _font = Native.CreateFontW(-spec.PixelSize, width, 0, 0, spec.Bold ? FwBold : FwNormal, 0, 0, 0, DefaultCharset, OutDefaultPrecis,
            ClipDefaultPrecis, AntialiasedQuality, VariablePitch, family);
        if (_font == IntPtr.Zero) throw new InvalidOperationException($"GDI CreateFont failed for '{family}'.");
        _oldBitmap = Native.SelectObject(_dc, _bitmap);
        _oldFont = Native.SelectObject(_dc, _font);
        Native.SetBkColor(_dc, 0x000000);
        Native.SetTextColor(_dc, 0xFFFFFF);

        var name = new StringBuilder(64);
        Native.GetTextFaceW(_dc, name.Capacity, name);
        FaceName = name.ToString();
        Native.GetTextMetricsW(_dc, out var metrics);
        _overhang = generals ? 0 : metrics.Overhang;
        Metrics = new UiFontFaceMetrics(metrics.Ascent, metrics.Descent, metrics.Height, _pixelOverlap);
        Description = $"GDI {FaceName}{(spec.Bold ? " Bold" : "")} {spec.PixelSize}px{(generals ? " @Generals" : "")}";
    }

    /// <summary>The face GDI selected (differs from the request when GDI substituted).</summary>
    public string FaceName { get; }

    public string Description { get; }

    public UiFontFaceMetrics Metrics { get; }

    public bool HasGlyph(int codepoint)
    {
        if (codepoint is < 0 or > 0xFFFF) return false;
        lock (_gate)
        {
            if (_glyphs.TryGetValue(codepoint, out var cached)) return cached;
            // 0xFFFF marks a code point without a glyph; a font for which the query fails (raster fonts) is taken as complete.
            var ch = ((char)codepoint).ToString();
            var index = new ushort[1];
            var result = Native.GetGlyphIndicesW(_dc, ch, 1, index, GgiMarkNonexistingGlyphs);
            var has = result == 0xFFFFFFFF || index[0] != 0xFFFF;
            _glyphs[codepoint] = has;
            return has;
        }
    }

    public int Advance(int codepoint)
    {
        lock (_gate) return AdvanceLocked(codepoint);
    }

    private int AdvanceLocked(int codepoint)
    {
        if (_advances.TryGetValue(codepoint, out var cached)) return cached;
        var ch = ((char)Math.Min(codepoint, 0xFFFF)).ToString();
        Native.GetTextExtentPoint32W(_dc, ch, 1, out var size);
        // Get_Char_Spacing: Width - PixelOverlap - CharOverhang where Width = cx + PixelOverlap + xOrigin ('W' is drawn one pixel in).
        var advance = Math.Max(0, size.Cx + (codepoint == 'W' ? 1 : 0) - _overhang);
        _advances[codepoint] = advance;
        return advance;
    }

    public GlyphBitmap Rasterize(int codepoint)
    {
        lock (_gate)
        {
            var advance = AdvanceLocked(codepoint);
            var ch = ((char)Math.Min(codepoint, 0xFFFF)).ToString();
            var origin = codepoint == 'W' ? 1 : 0;
            var rect = new Native.Rect { Left = 0, Top = 0, Right = _dibSize, Bottom = _dibSize };
            Native.ExtTextOutW(_dc, origin, 0, EtoOpaque, ref rect, ch, 1, IntPtr.Zero);
            Native.GetTextExtentPoint32W(_dc, ch, 1, out var size);
            Native.GdiFlush();

            // Store_GDI_Char copies cx + overlap + xOrigin columns and tmHeight rows of the first (blue) channel.
            var columns = Math.Min(_dibSize, size.Cx + _pixelOverlap + origin);
            var rows = Math.Min(_dibSize, size.Cy);
            var coverage = new byte[Math.Max(0, columns * rows)];
            var line = new byte[_dibStride];
            int minX = columns, maxX = -1, minY = rows, maxY = -1;
            for (var y = 0; y < rows; y++)
            {
                Marshal.Copy(_bits + y * _dibStride, line, 0, _dibStride);
                for (var x = 0; x < columns; x++)
                {
                    var pixel = line[x * 3];
                    // pixel_value >> 4 into the 4-bit alpha of A4R4G4B4, expanded back to 8 bits by the texture sampler.
                    var alpha = (byte)((pixel >> 4) * 17);
                    coverage[y * columns + x] = alpha;
                    if (alpha == 0) continue;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (maxX < 0) return new GlyphBitmap(0, 0, 0, 0, advance, []);

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var trimmed = new byte[width * height];
            for (var y = 0; y < height; y++) Array.Copy(coverage, (minY + y) * columns + minX, trimmed, y * width, width);
            return new GlyphBitmap(width, height, minX, Metrics.Ascent - minY, advance, trimmed);
        }
    }

    public void Dispose()
    {
        if (_dc == IntPtr.Zero) return;
        Native.SelectObject(_dc, _oldFont);
        Native.SelectObject(_dc, _oldBitmap);
        Native.DeleteObject(_font);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_dc);
        _dc = _font = _bitmap = IntPtr.Zero;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public int Size, Width, Height;
            public short Planes, BitCount;
            public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Size
        {
            public int Cx, Cy;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TextMetric
        {
            public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang,
                DigitizedAspectX, DigitizedAspectY;
            public char FirstChar, LastChar, DefaultChar, BreakChar;
            public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern uint SetBkColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int GetTextFaceW(IntPtr dc, int count, StringBuilder name);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextMetricsW(IntPtr dc, out TextMetric metrics);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out Size size);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool ExtTextOutW(IntPtr dc, int x, int y, uint options, ref Rect clip, string text, uint count, IntPtr dx);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, [Out] ushort[] indices, uint flags);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    }
}
