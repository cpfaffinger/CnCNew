using CnCNew.Data.Ini;
using CnCNew.Ui.Images;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Tests;

/// <summary>Tests of the Stride-free part of the 2D UI renderer (package S4): font policy, text metrics, atlas, clips, images, stats.</summary>
public static class UiRenderingTests
{
    private static int _failures;

    public static int Run(string[] args)
    {
        _ = args;
        _failures = 0;
        Test(FontPolicyFollowsTheOriginalFormula);
        Test(TextMetricsSumWholePixelAdvances);
        Test(FontChainFallsBackInOrderAndOpensLazily);
        Test(AtlasPacksDeterministicallyWithoutOverlap);
        Test(AtlasRejectsImagesLargerThanAPage);
        Test(GlyphCacheRasterizesOnceAndStoresCoverageAsAlpha);
        Test(LightTextGetsTheGammaSpaceAlphaCurve);
        Test(ClipStackIntersectsAndCountsUnderflow);
        Test(RotatedImagesAreTurnedUpright);
        Test(TextRunsPlaceGlyphsOnTheBaseline);
        Test(FontCatalogInterpretsRegistryNames);
        Test(CountersSnapshotIsSortedAndResettable);
        Console.WriteLine(_failures == 0 ? "UI rendering tests passed." : $"UI rendering tests: {_failures} failure(s).");
        return _failures;
    }

    private static void Test(Action test)
    {
        try { test(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {test.Method.Name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // ------------------------------------------------------------------------------------------------------------------

    private static void FontPolicyFollowsTheOriginalFormula()
    {
        // FontCharsClass::Create_GDI_Font: height = MulDiv(points, 96, 72) (rounded to nearest).
        Check(UiFontPolicy.PixelSize(9) == 12, "9pt is 12px.");
        Check(UiFontPolicy.PixelSize(10) == 13, "10pt is 13.33px -> 13.");
        Check(UiFontPolicy.PixelSize(11) == 15, "11pt is 14.67px -> 15.");
        Check(UiFontPolicy.PixelSize(12) == 16, "12pt is 16px.");
        Check(UiFontPolicy.PixelSize(1) == 1, "1pt is 1.33px -> 1.");

        var generals = UiFontPolicy.Resolve(new UiFont("Generals", 12, true));
        Check(generals == new UiFontSpec("Arial", 16, true, 91), $"Generals is condensed Arial, got {generals}.");
        var named = UiFontPolicy.Resolve(new UiFont("Tahoma", 10, false));
        Check(named == new UiFontSpec("Tahoma", 13, false, 100), $"Other fonts keep their name, got {named}.");
        Check(UiFontPolicy.Resolve(new UiFont("generals", 10, false)).WidthPercent == 91, "The Generals name is case-insensitive.");
    }

    private static void TextMetricsSumWholePixelAdvances()
    {
        var provider = new FakeProvider();
        using var metrics = new UiTextMetrics(provider);
        var font = new UiFont("Arial", 12, false); // 16px -> advance 8, line height 20 in the fake face
        Check(metrics.MeasureWidth("abc", font) == 24, "Three glyphs of 8px.");
        Check(metrics.MeasureWidth("", font) == 0, "Empty text has no width.");
        Check(metrics.MeasureWidth("a\nb\t", font) == 16, "Control characters have neither glyph nor advance.");
        Check(metrics.MeasureWidth("\u00E4\u00F6\u00FC\u00DF", font) == 32, "Umlauts are four glyphs.");
        Check(metrics.MeasureWidth("a\U0001F600b", font) == 24, "A surrogate pair is one code point.");
        Check(metrics.LineHeight(font) == 20, "Line height is the cell height of the face.");
        metrics.MeasureWidth("abc", font);
        Check(provider.Opened == 1, "One face is opened per resolved spec.");
        metrics.MeasureWidth("abc", new UiFont("Arial", 12, true));
        Check(provider.Opened == 2, "Bold is a different face.");
        Check(metrics.MeasureWidth("ab", new UiFont("Generals", 12, false)) == 2 * 8, "Generals resolves to the Arial 91% spec of the fake (advance 8).");
    }

    private static void FontChainFallsBackInOrderAndOpensLazily()
    {
        var primary = new FakeFace("primary", 16, cp => cp < 0x100);
        var second = new FakeFace("second", 16, cp => cp is >= 0x400 and < 0x500);
        var third = new FakeFace("third", 16, cp => cp is >= 0x4E00 and < 0x4F00 or (>= 0x400 and < 0x410));
        var opened = new List<string>();
        using var chain = new UiFontChain(primary, [() => { opened.Add("second"); return second; }, () => { opened.Add("third"); return third; }], "test");
        Check(chain.HasGlyph('a') && opened.Count == 0, "Latin is served by the primary face without opening fallbacks.");
        Check(chain.IsSubstituted(0x0416) && opened.SequenceEqual(["second"]), "Cyrillic comes from the second face; the third stays closed.");
        Check(chain.Rasterize(0x0416).Coverage[0] == 2, "The second face rasterizes it (marker 2).");
        Check(chain.IsSubstituted(0x4E2D) && opened.SequenceEqual(["second", "third"]), "CJK falls through to the third face.");
        Check(!chain.HasGlyph(0x1F600) && !chain.IsSubstituted(0x1F600), "An unknown code point is missing and not a substitution.");
        Check(chain.Rasterize(0x1F600).Coverage[0] == 1 && chain.Advance(0x1F600) == primary.Advance(0x1F600), "A missing glyph is the notdef box of the primary face.");
        Check(chain.Metrics == primary.Metrics, "Line metrics come from the primary face.");
        Check(chain.OpenedFallbacks == 2, "Both fallbacks were opened once.");
    }

    private static void AtlasPacksDeterministicallyWithoutOverlap()
    {
        static List<AtlasRegion> Pack(MemorySink sink)
        {
            var atlas = new UiGlyphAtlas(128, sink);
            var random = new Random(1234);
            var regions = new List<AtlasRegion>();
            for (var i = 0; i < 300; i++)
            {
                var width = random.Next(1, 30);
                var height = random.Next(1, 30);
                regions.Add(atlas.Add(width, height, new byte[width * height * 4]));
            }
            return regions;
        }

        var sinkA = new MemorySink();
        var sinkB = new MemorySink();
        var a = Pack(sinkA);
        var b = Pack(sinkB);
        Check(a.SequenceEqual(b), "Two runs give identical placement.");
        Check(sinkA.Pages.Count > 1, "300 images need several 128x128 pages.");
        var occupied = new Dictionary<int, bool[,]>();
        foreach (var region in a)
        {
            if (!occupied.TryGetValue(region.Page, out var grid)) occupied[region.Page] = grid = new bool[128, 128];
            Check(region.X >= 0 && region.Y >= 0 && region.X + region.Width <= 128 && region.Y + region.Height <= 128, $"{region} lies inside the page.");
            for (var y = region.Y; y < region.Y + region.Height; y++)
                for (var x = region.X; x < region.X + region.Width; x++)
                {
                    Check(!grid[x, y], $"{region} overlaps another region at {x},{y}.");
                    grid[x, y] = true;
                }
        }
        foreach (var (page, grid) in occupied)
        {
            for (var y = 0; y < UiGlyphAtlas.WhiteSize; y++)
                for (var x = 0; x < UiGlyphAtlas.WhiteSize; x++)
                    Check(!grid[x, y], $"Page {page}: the white block at {x},{y} is reserved.");
            var pixels = sinkA.Pages[page];
            Check(pixels[(1 * 128 + 1) * 4 + 3] == 255 && pixels[(1 * 128 + 1) * 4] == 255, $"Page {page} has a solid white block.");
        }
        var atlas = new UiGlyphAtlas(128, new MemorySink());
        Check(atlas.White() == new AtlasRegion(0, 0, 0, UiGlyphAtlas.WhiteSize, UiGlyphAtlas.WhiteSize), "White block of page 0.");
    }

    private static void AtlasRejectsImagesLargerThanAPage()
    {
        var atlas = new UiGlyphAtlas(64, new MemorySink());
        Throws<ArgumentOutOfRangeException>(() => atlas.Add(64, 10, new byte[64 * 10 * 4]));
        Throws<ArgumentOutOfRangeException>(() => atlas.Add(10, 64, new byte[10 * 64 * 4]));
        Throws<ArgumentException>(() => atlas.Add(4, 4, new byte[3]));
        atlas.Add(63, 50, new byte[63 * 50 * 4]);
    }

    private static void LightTextGetsTheGammaSpaceAlphaCurve()
    {
        var curve = UiAlphaCurves.LightOnDark;
        Check(curve.Length == 256 && curve[0] == 0 && curve[255] == 255, "The curve keeps the ends.");
        for (var i = 1; i < 256; i++) Check(curve[i] >= curve[i - 1], "The curve is monotonic.");
        // 50 % coverage of white on black is byte 128 in a gamma-space blend; the linear-light device encodes alpha again, so the stored alpha is decode(128 / 255).
        Check(curve[128] is >= 53 and <= 56, $"decode(0.5) is 0.2140 (alpha {curve[128]}).");
        // A linear-light blend of that alpha over black encodes back to the coverage (within one step of rounding).
        for (var i = 59; i < 256; i += 17)
        {
            var linear = curve[i] / 255.0;
            var encoded = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
            Check(Math.Abs(encoded * 255.0 - i) <= 2.5, $"coverage {i}: re-encoded {encoded * 255.0:F1}");
        }
        Check(UiAlphaCurves.IsLight(new WndColor(255, 255, 255, 255)) && UiAlphaCurves.IsLight(new WndColor(209, 218, 255, 255)) && UiAlphaCurves.IsLight(new WndColor(220, 220, 220, 255)),
            "White and pale colors are light text.");
        Check(!UiAlphaCurves.IsLight(new WndColor(0, 0, 0, 255)) && !UiAlphaCurves.IsLight(new WndColor(0, 0, 128, 255)) && !UiAlphaCurves.IsLight(new WndColor(60, 60, 155, 255)),
            "Black and dark blue are dark text.");

        var sink = new MemorySink();
        var cache = new UiGlyphCache(new UiGlyphAtlas(256, sink), curve);
        var glyph = cache.Get(new UiFontSpec("Arial", 16, false, 100), new FakeFace("f", 16, _ => true), 'A');
        var region = glyph.Region!.Value;
        var offset = (region.Y * 256 + region.X) * 4;
        Check(sink.Pages[region.Page][offset + 3] == curve['A' % 251 + 1], "The cache stores the mapped alpha.");
    }

    private static void GlyphCacheRasterizesOnceAndStoresCoverageAsAlpha()
    {
        var sink = new MemorySink();
        var atlas = new UiGlyphAtlas(256, sink);
        var cache = new UiGlyphCache(atlas);
        var face = new FakeFace("f", 16, cp => cp != 'Q');
        var spec = new UiFontSpec("Arial", 16, false, 100);
        var glyph = cache.Get(spec, face, 'A');
        Check(glyph.Region is { } region && region.Width == 7 && region.Height == 16, "Glyph bitmap size comes from the face.");
        Check(ReferenceEquals(glyph, cache.Get(spec, face, 'A')) && face.Rasterized == 1, "A glyph is rasterized once.");
        var r = glyph.Region!.Value;
        var pixels = sink.Pages[r.Page];
        var offset = (r.Y * 256 + r.X) * 4;
        Check(pixels[offset] == 255 && pixels[offset + 1] == 255 && pixels[offset + 2] == 255 && pixels[offset + 3] == 'A' % 251 + 1,
            "Glyph pixels are white with the coverage in alpha.");
        var space = cache.Get(spec, face, ' ');
        Check(space.Region is null && space.Advance == 8, "A blank glyph has an advance and no region.");
        var missing = cache.Get(spec, face, 'Q');
        Check(missing.IsMissing && missing.Region is not null, "A missing glyph is flagged and keeps its visible box.");
        Check(cache.Get(new UiFontSpec("Arial", 16, true, 100), face, 'A') != glyph, "The cache is keyed by spec.");
        Check(cache.Count == 4, "Four cached glyphs.");
    }

    private static void ClipStackIntersectsAndCountsUnderflow()
    {
        var clips = new UiClipStack(new UiRect(0, 0, 800, 600));
        Check(clips.Current == new UiRect(0, 0, 800, 600) && clips.Depth == 0, "The viewport is the bottom clip.");
        clips.Push(new UiRect(100, 100, 300, 200));
        clips.Push(new UiRect(250, 50, 300, 100));
        Check(clips.Current == new UiRect(250, 100, 150, 50), $"Nested clips intersect, got {clips.Current}.");
        Check(clips.With(new UiRect(0, 0, 260, 400)) == new UiRect(250, 100, 10, 50), "An extra clip is intersected on top.");
        Check(clips.Pop() && clips.Current == new UiRect(100, 100, 300, 200), "Pop restores the previous clip.");
        clips.Push(new UiRect(700, 500, 50, 50));
        Check(clips.Current.IsEmpty, "Disjoint clips leave nothing visible.");
        clips.Pop();
        clips.Pop();
        Check(clips.Depth == 0 && clips.UnderflowCount == 0, "Balanced.");
        Check(!clips.Pop() && clips.UnderflowCount == 1, "An unbalanced pop is counted and not applied.");
        clips.Push(new UiRect(-50, -50, 100, 100));
        Check(clips.Current == new UiRect(0, 0, 50, 50), "Clips never exceed the viewport.");
        clips.Reset(new UiRect(0, 0, 10, 10));
        Check(clips.Depth == 0 && clips.UnderflowCount == 0 && clips.Current == new UiRect(0, 0, 10, 10), "Reset.");
    }

    private static void RotatedImagesAreTurnedUpright()
    {
        // A 5x4 page; the stored rectangle (x 1..4, y 1..3) is 3 wide and 2 high = a 2x3 image stored rotated 90 degrees clockwise.
        const int pageWidth = 5;
        const int pageHeight = 4;
        var page = new byte[pageWidth * pageHeight * 4];
        for (var y = 0; y < pageHeight; y++)
            for (var x = 0; x < pageWidth; x++)
            {
                var index = (y * pageWidth + x) * 4;
                page[index] = (byte)(x * 10 + y);
                page[index + 3] = 255;
            }
        var image = new MappedImage("Rotated", "page.tga", pageWidth, pageHeight, 1, 1, 4, 3, MappedImageStatus.Rotated90Clockwise, new IniSourceLocation("test", 1, 1));
        Check(image.Width == 2 && image.Height == 3, "Screen size is the transposed rectangle.");
        var upright = UiImagePixels.UprightRotated(image, pageWidth, pageHeight, page);
        Check(upright.Length == 2 * 3 * 4, "Output has the screen size.");
        // Screen pixel (x, y) <- stored pixel (storedWidth - 1 - y, x); checked against the original's triangle UVs:
        // screen top-left is the stored top-right corner, screen bottom-left the stored top-left corner.
        int Marker(int x, int y) => upright[(y * 2 + x) * 4];
        Check(Marker(0, 0) == 3 * 10 + 1, $"Screen top-left is the stored top-right pixel, got {Marker(0, 0)}.");
        Check(Marker(0, 2) == 1 * 10 + 1, $"Screen bottom-left is the stored top-left pixel, got {Marker(0, 2)}.");
        Check(Marker(1, 0) == 3 * 10 + 2, $"Screen top-right is the stored bottom-right pixel, got {Marker(1, 0)}.");
        Check(Marker(1, 2) == 1 * 10 + 2, $"Screen bottom-right is the stored bottom-left pixel, got {Marker(1, 2)}.");
        var plain = image with { Status = MappedImageStatus.None };
        Check(UiImagePixels.SourceRect(plain) == (1, 1, 3, 2), "Unrotated images use the stored rectangle.");
        Throws<ArgumentException>(() => UiImagePixels.UprightRotated(image, pageWidth, pageHeight, new byte[4]));
        Throws<ArgumentException>(() => UiImagePixels.UprightRotated(image with { Right = 9 }, pageWidth, pageHeight, page));
    }

    private static void TextRunsPlaceGlyphsOnTheBaseline()
    {
        var face = new FakeFace("f", 16, _ => true);
        var placements = UiTextRuns.Layout("ab\nc", face);
        Check(placements.Count == 3 && placements[0] == new UiGlyphPlacement('a', 0) && placements[1] == new UiGlyphPlacement('b', 8)
              && placements[2] == new UiGlyphPlacement('c', 16), "Pen positions are cumulative advances; control characters are skipped.");
        var draw = new UiTextDraw("ab", null, new UiFont("Arial", 12, false), WndColor.White, WndColor.Undefined, 40, 100, UiTextAlignment.Center, new UiRect(0, 0, 800, 600));
        var (x, baseline) = UiTextRuns.Origin(draw, face.Metrics);
        Check(x == 40 && baseline == 100 + face.Metrics.Ascent, "The draw X is the left edge; the baseline is one ascent below the line top.");
    }

    private static void FontCatalogInterpretsRegistryNames()
    {
        var catalog = FontFileCatalog.FromRegistryNames(
        [
            ("Arial (TrueType)", @"C:\Windows\Fonts\arial.ttf"),
            ("Arial Bold (TrueType)", @"C:\Windows\Fonts\arialbd.ttf"),
            ("Arial Bold Italic (TrueType)", @"C:\Windows\Fonts\arialbi.ttf"),
            ("Arial Italic (TrueType)", @"C:\Windows\Fonts\ariali.ttf"),
            ("Arial Narrow (TrueType)", @"C:\Windows\Fonts\arialn.ttf"),
            ("Microsoft YaHei & Microsoft YaHei UI (TrueType)", @"C:\Windows\Fonts\msyh.ttc"),
            ("Microsoft YaHei Bold & Microsoft YaHei UI Bold (TrueType)", @"C:\Windows\Fonts\msyhbd.ttc"),
            ("Segoe Fluent Icons (TrueType)", @"C:\Windows\Fonts\SegoeIcons.ttf"),
            ("Modern (All res)", @"C:\Windows\Fonts\modern.fon"),
        ]);
        Check(catalog.Find("arial", false, out var exact)?.Path.EndsWith("arial.ttf", StringComparison.Ordinal) == true && exact, "Regular Arial.");
        Check(catalog.Find("Arial", true, out exact)?.Path.EndsWith("arialbd.ttf", StringComparison.Ordinal) == true && exact, "Bold Arial; italics are never chosen.");
        Check(catalog.Find("Arial Narrow", false, out _)?.Path.EndsWith("arialn.ttf", StringComparison.Ordinal) == true, "Arial Narrow is its own family.");
        Check(catalog.Find("Microsoft YaHei UI", true, out exact)?.Path.EndsWith("msyhbd.ttc", StringComparison.Ordinal) == true && exact, "Combined TTC names list every family.");
        Check(catalog.Find("Segoe Fluent Icons", true, out exact)?.Path.EndsWith("SegoeIcons.ttf", StringComparison.Ordinal) == true && !exact,
            "Bold requested but only regular installed: regular, flagged as not exact.");
        Check(catalog.Find("Modern", false, out _) is null, "Bitmap fonts are not usable.");
        Check(catalog.Find("Nonexistent", false, out _) is null && !catalog.HasFamily("Nonexistent"), "Unknown family.");
        var split = FontFileCatalog.Split("Segoe UI Bold Italic");
        Check(split == ("Segoe UI", true, true), $"Style words are removed from the end, got {split}.");
    }

    private static void CountersSnapshotIsSortedAndResettable()
    {
        var counters = new UiRenderCounters();
        counters.MissingImage("Zeta");
        counters.MissingImage("Alpha");
        counters.MissingImage("Alpha");
        counters.MissingLabel("GUI:B");
        counters.MissingGlyph(0x4E2D);
        counters.MissingTexture("x.tga");
        counters.Sprites = 5;
        var snapshot = counters.Snapshot();
        Check(snapshot.MissingImages == 3 && snapshot.MissingImageNames.SequenceEqual(["Alpha", "Zeta"]), "Counted per occurrence, named once, sorted.");
        Check(snapshot.MissingGlyphCodes.SequenceEqual(["U+4E2D"]) && snapshot.MissingLabelKeys.SequenceEqual(["GUI:B"]) && snapshot.MissingTextureFiles.SequenceEqual(["x.tga"]), "Names.");
        var total = new UiRenderCounters();
        total.Add(counters);
        total.Add(counters);
        Check(total.Snapshot().MissingImages == 6 && total.Snapshot().Sprites == 10 && total.Snapshot().MissingImageNames.Length == 2, "Add sums counts and unions names.");
        counters.Reset();
        Check(counters.Snapshot() == UiRenderStats.Empty || counters.Snapshot().Format() == UiRenderStats.Empty.Format(), "Reset clears everything.");
        Check(snapshot.Format().Contains("missingImages=3", StringComparison.Ordinal), "Format names the counters.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    // ------------------------------------------------------------------------------------------------------------------

    private sealed class MemorySink : IAtlasPageSink
    {
        public Dictionary<int, byte[]> Pages { get; } = [];
        private readonly Dictionary<int, int> _widths = [];

        public void CreatePage(int page, int width, int height)
        {
            Pages[page] = new byte[width * height * 4];
            _widths[page] = width;
        }

        public void Upload(int page, int x, int y, int width, int height, byte[] rgba)
        {
            var pageWidth = _widths[page];
            for (var row = 0; row < height; row++)
                Array.Copy(rgba, row * width * 4, Pages[page], ((y + row) * pageWidth + x) * 4, width * 4);
        }
    }

    private sealed class FakeProvider : IUiFontProvider
    {
        public int Opened { get; private set; }

        public IUiFontFace Open(UiFontSpec spec)
        {
            Opened++;
            return new FakeFace(spec.ToString(), spec.PixelSize, _ => true);
        }
    }

    /// <summary>A face with advance = px / 2, ascent = px * 3 / 4 + 1, cell height = px + 4; the glyph coverage marker is the code point modulo 251 plus 1 (1 for notdef).</summary>
    private sealed class FakeFace(string name, int pixelSize, Func<int, bool> has) : IUiFontFace
    {
        private readonly int _marker = name switch { "second" => 2, "third" => 3, _ => 0 };

        public int Rasterized { get; private set; }
        public string Description => name;
        public UiFontFaceMetrics Metrics => new(pixelSize * 3 / 4 + 1, pixelSize - pixelSize * 3 / 4 + 3, pixelSize + 4);
        public bool HasGlyph(int codepoint) => has(codepoint);
        public int Advance(int codepoint) => pixelSize / 2;

        public GlyphBitmap Rasterize(int codepoint)
        {
            Rasterized++;
            if (codepoint == ' ') return new GlyphBitmap(0, 0, 0, 0, Advance(codepoint), []);
            var width = pixelSize / 2 - 1;
            var coverage = new byte[width * pixelSize];
            Array.Fill(coverage, (byte)(!has(codepoint) ? 1 : _marker != 0 ? _marker : codepoint % 251 + 1));
            return new GlyphBitmap(width, pixelSize, 0, pixelSize * 3 / 4, Advance(codepoint), coverage);
        }

        public void Dispose() { }
    }
}
