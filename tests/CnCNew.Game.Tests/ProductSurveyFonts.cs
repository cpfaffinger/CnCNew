using System.Runtime.InteropServices;
using System.Text;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// The text measure of the product survey: the widths and line heights of the font the client draws with (GDI, like the original - <c>GdiFontFace</c> of the Stride client),
/// so that "this label is wider than its window" is judged with the real glyph advances and not with the headless shell's synthetic width. Only the measurements are
/// taken (no glyph is rasterized). Windows only; elsewhere <see cref="Create"/> returns null and the survey falls back to the synthetic measure.
/// </summary>
internal sealed class ProductSurveyFonts : IUiFontProvider
{
    public static IUiTextMeasure? Create() => OperatingSystem.IsWindows() ? new UiTextMetrics(new ProductSurveyFonts()) : null;

    public IUiFontFace Open(UiFontSpec spec)
    {
        var face = new Face(spec.Family, spec);
        if (face.FaceName.Equals(spec.Family, StringComparison.OrdinalIgnoreCase)) return face;
        face.Dispose();
        foreach (var family in UiFontPolicy.FallbackFamilies)
        {
            var candidate = new Face(family, spec);
            if (candidate.FaceName.Equals(family, StringComparison.OrdinalIgnoreCase)) return candidate;
            candidate.Dispose();
        }
        return new Face(spec.Family, spec);
    }

    private sealed class Face : IUiFontFace
    {
        private readonly Dictionary<int, int> _advances = [];
        private readonly int _overhang;
        private IntPtr _dc, _font, _old;

        public Face(string family, UiFontSpec spec)
        {
            var generals = spec.WidthPercent == UiFontPolicy.GeneralsWidthPercent;
            var width = generals ? (int)(spec.PixelSize * 0.40f) : 0;
            _dc = Native.CreateCompatibleDC(IntPtr.Zero);
            _font = Native.CreateFontW(-spec.PixelSize, width, 0, 0, spec.Bold ? 700 : 400, 0, 0, 0, 1, 0, 0, 4, 2, family);
            _old = Native.SelectObject(_dc, _font);
            var name = new StringBuilder(64);
            Native.GetTextFaceW(_dc, name.Capacity, name);
            FaceName = name.ToString();
            Native.GetTextMetricsW(_dc, out var metrics);
            _overhang = generals ? 0 : metrics.Overhang;
            Metrics = new UiFontFaceMetrics(metrics.Ascent, metrics.Descent, metrics.Height, Math.Clamp(spec.PixelSize / 8, 0, 4));
            Description = $"GDI {FaceName} {spec.PixelSize}px";
        }

        public string FaceName { get; }
        public string Description { get; }
        public UiFontFaceMetrics Metrics { get; }
        public bool HasGlyph(int codepoint) => true;

        public int Advance(int codepoint)
        {
            if (_advances.TryGetValue(codepoint, out var cached)) return cached;
            var text = ((char)Math.Min(codepoint, 0xFFFF)).ToString();
            Native.GetTextExtentPoint32W(_dc, text, 1, out var size);
            return _advances[codepoint] = Math.Max(0, size.Cx + (codepoint == 'W' ? 1 : 0) - _overhang);
        }

        public GlyphBitmap Rasterize(int codepoint) => new(0, 0, 0, 0, Advance(codepoint), []);

        public void Dispose()
        {
            if (_dc == IntPtr.Zero) return;
            Native.SelectObject(_dc, _old);
            Native.DeleteObject(_font);
            Native.DeleteDC(_dc);
            _dc = _font = IntPtr.Zero;
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Size { public int Cx, Cy; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TextMetric
        {
            public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
            public char FirstChar, LastChar, DefaultChar, BreakChar;
            public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
        }

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision,
            uint clipPrecision, uint quality, uint pitchAndFamily, string face);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int GetTextFaceW(IntPtr dc, int count, StringBuilder name);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextMetricsW(IntPtr dc, out TextMetric metrics);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out Size size);
    }
}
