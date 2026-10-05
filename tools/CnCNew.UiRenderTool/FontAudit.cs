using System.Text;
using CnCNew.Client.Stride.Ui;
using CnCNew.Game.Data;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Rendering;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.UiRenderTool;

/// <summary>
/// Measures how far an outline renderer (FreeType) is from the GDI pipeline the original uses, on the data the game
/// actually shows: every window font of every retail layout at two screen sizes, every localized text those windows
/// carry, and every code point of the English string table. Output is plain text (<c>font-audit.txt</c>) and feeds
/// <c>docs/TEXT-AUDIT.md</c>.
/// </summary>
internal static class FontAudit
{
    /// <summary>
    /// Invariants of the GDI face that must hold on every Windows installation (the exact pixel metrics depend on the installed Arial, these do not):
    /// the face GDI selects is the requested one, glyph coverage is quantized to the 16 steps of the original's 4-bit alpha, the cell height is
    /// ascent + descent, the overlap follows <c>(height / 8)</c> within [0, 4], blank and letter advances are positive, the Generals font is clearly
    /// narrower than plain Arial and a bold face is wider than the regular one. Returns the failures.
    /// </summary>
    public static IReadOnlyList<string> SelfCheck()
    {
        var problems = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) problems.Add(message);
        }

        using var gdi = new GdiFontProvider();
        foreach (var (size, bold) in new[] { (13, false), (16, false), (16, true), (24, false), (36, false) })
        {
            var spec = new UiFontSpec("Arial", size, bold, 100);
            using var chain = (UiFontChain)gdi.Open(spec);
            var face = chain.Primary;
            Check(face is GdiFontFace { FaceName: "Arial" }, $"{spec}: GDI selected '{(face as GdiFontFace)?.FaceName}' instead of Arial.");
            Check(face.Metrics.LineHeight == face.Metrics.Ascent + face.Metrics.Descent, $"{spec}: cell height {face.Metrics.LineHeight} is not ascent {face.Metrics.Ascent} + descent {face.Metrics.Descent}.");
            Check(face.Metrics.Overlap == Math.Clamp(size / 8, 0, 4), $"{spec}: overlap {face.Metrics.Overlap}.");
            Check(face.Advance(' ') > 0 && face.Advance('i') > 0 && face.Advance('M') > face.Advance('i'), $"{spec}: implausible advances.");
            foreach (var character in "AEgjMQW%@")
            {
                var glyph = face.Rasterize(character);
                Check(!glyph.IsEmpty && glyph.Coverage.All(value => value % 17 == 0), $"{spec}: glyph '{character}' coverage is not a multiple of 17 (4-bit alpha) or empty.");
                Check(glyph.BearingY <= face.Metrics.Ascent && glyph.Width > 0 && glyph.Height <= face.Metrics.LineHeight, $"{spec}: glyph '{character}' does not fit the cell.");
            }
            Check(face.Advance('W') >= face.Advance('V'), $"{spec}: 'W' is narrower than 'V' although the original adds one pixel to it.");
        }
        var plain = gdi.Open(new UiFontSpec("Arial", 24, false, 100));
        var generals = gdi.Open(new UiFontSpec("Arial", 24, false, UiFontPolicy.GeneralsWidthPercent));
        const string sample = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var plainWidth = sample.Sum(character => plain.Advance(character));
        var generalsWidth = sample.Sum(character => generals.Advance(character));
        Check(generalsWidth < plainWidth * 0.92 && generalsWidth > plainWidth * 0.75, $"Generals is {generalsWidth} px wide for the capitals, Arial {plainWidth} px.");
        var regular = gdi.Open(new UiFontSpec("Arial", 16, false, 100));
        var heavy = gdi.Open(new UiFontSpec("Arial", 16, true, 100));
        Check("Command and Conquer".Sum(character => heavy.Advance(character)) > "Command and Conquer".Sum(character => regular.Advance(character)), "Arial Bold is not wider than Arial.");
        plain.Dispose();
        generals.Dispose();
        regular.Dispose();
        heavy.Dispose();
        return problems;
    }

    public static void Run(GameData data, LocalizedText text, WndHeaderTemplateTable templates, string outputDirectory)
    {
        var report = new StringBuilder();
        void Line(string value = "")
        {
            report.AppendLine(value);
            Console.WriteLine(value);
        }

        using var gdi = new GdiFontProvider();
        using var freeType = new FreeTypeFontProvider();
        using var gdiMeasure = new UiTextMetrics(gdi);
        using var ftMeasure = new UiTextMetrics(freeType);
        var source = new LocalizedTextSource(text);
        var services = new UiTreeServices(gdiMeasure) { Text = source, HeaderTemplates = templates };

        var layouts = data.Content.EnumerateFiles()
            .Where(path => path.StartsWith("Window/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".wnd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();

        // font -> texts (resolved, one entry per window text line)
        var fontTexts = new SortedDictionary<UiFont, List<string>>(Comparer<UiFont>.Create((a, b) => string.CompareOrdinal(a.ToString(), b.ToString())));
        var windowsWithoutFont = 0;
        var windowsWithText = 0;
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080) })
        {
            foreach (var file in layouts)
            {
                WndLayout layout;
                try
                {
                    layout = WndParser.Parse(data.Content.OpenRead(file), file);
                }
                catch (Exception exception)
                {
                    Line($"layout {file}: {exception.Message}");
                    continue;
                }
                var tree = new UiWindowTree(new UiViewport(width, height), UiScaleOptions.Original with { Mode = UiScaleMode.Uniform }, services);
                tree.AddLayout(layout);
                windowsWithoutFont += width == 1280 ? tree.Statistics.WindowsWithoutFont : 0;
                foreach (var window in tree.AllWindows)
                {
                    if (!fontTexts.TryGetValue(window.Font, out var list)) fontTexts[window.Font] = list = [];
                    if (window.Text is not { } value || value.Value.Length == 0) continue;
                    windowsWithText += width == 1280 ? 1 : 0;
                    var resolved = value.Value;
                    if (value.IsLabel && !source.TryResolve(value.Value, out resolved!))
                    {
                        unresolved.Add(value.Value);
                        continue;
                    }
                    foreach (var part in resolved.Split('\n')) if (part.Length > 0) list.Add(part);
                }
            }
        }

        Line("== A. Fonts used by the retail layouts (1280x720 and 1920x1080, Uniform) ==");
        Line($"layouts parsed: {layouts.Length}; windows without FONT and template (1280x720): {windowsWithoutFont}; windows with text: {windowsWithText}; unresolved labels: {unresolved.Count}");
        Line("font | spec | texts | GDI face / ascent+descent=height | FreeType ascent+descent=height | differing glyph advances ASCII+Latin-1 (max) | texts with other width (sum abs, max) px");
        var totalTexts = 0;
        var totalDifferent = 0;
        long totalAbsolute = 0;
        foreach (var (font, texts) in fontTexts)
        {
            var spec = UiFontPolicy.Resolve(font);
            var gdiFace = gdiMeasure.Face(spec);
            var ftFace = ftMeasure.Face(spec);
            var differing = 0;
            var maximum = 0;
            for (var cp = 0x20; cp < 0x100; cp++)
            {
                if (cp is >= 0x7F and < 0xA0) continue;
                var difference = Math.Abs(gdiFace.Advance(cp) - ftFace.Advance(cp));
                if (difference != 0) differing++;
                maximum = Math.Max(maximum, difference);
            }
            int textsDifferent = 0, textMax = 0;
            long textSum = 0;
            foreach (var line in texts)
            {
                var delta = Math.Abs(gdiMeasure.MeasureWidth(line, font) - ftMeasure.MeasureWidth(line, font));
                if (delta == 0) continue;
                textsDifferent++;
                textSum += delta;
                textMax = Math.Max(textMax, delta);
            }
            totalTexts += texts.Count;
            totalDifferent += textsDifferent;
            totalAbsolute += textSum;
            var g = gdiFace.Metrics;
            var f = ftFace.Metrics;
            Line($"{font} | {spec} | {texts.Count} | {gdiFace.Description} {g.Ascent}+{g.Descent}={g.LineHeight} | {f.Ascent}+{f.Descent}={f.LineHeight} | {differing} ({maximum}) | {textsDifferent} ({textSum}, {textMax})");
        }
        Line($"total window text lines: {totalTexts}; with a different width under FreeType: {totalDifferent}; sum of absolute differences: {totalAbsolute} px");
        Line($"GDI substitutions: {(gdi.Substitutions.Count == 0 ? "none" : string.Join(", ", gdi.Substitutions.Select(pair => $"{pair.Key} -> {pair.Value}")))}");
        Line($"FreeType substitutions: {(freeType.Substitutions.Count == 0 ? "none" : string.Join(", ", freeType.Substitutions.Select(pair => $"{pair.Key} -> {pair.Value}")))}");
        if (unresolved.Count > 0) Line($"unresolved labels in window text: {string.Join(", ", unresolved)}");

        Line();
        Line("== B. Glyph coverage of the English string table against Arial (GDI) ==");
        var codepoints = new SortedDictionary<int, int>();
        foreach (var entry in text.Table.Entries)
            foreach (var rune in entry.Text.EnumerateRunes())
                codepoints[rune.Value] = codepoints.GetValueOrDefault(rune.Value) + 1;
        var arial = (UiFontChain)gdiMeasure.Face(new UiFontSpec("Arial", 13, false, 100));
        var outside = codepoints.Where(pair => pair.Key >= 0x20 && !arial.Primary.HasGlyph(pair.Key)).ToArray();
        var unreachable = codepoints.Where(pair => pair.Key >= 0x20 && !arial.HasGlyph(pair.Key)).ToArray();
        Line($"strings: {text.Table.Count}; distinct code points: {codepoints.Count}; without a glyph in Arial: {outside.Length} ({string.Join(" ", outside.Select(pair => $"U+{pair.Key:X4}x{pair.Value}"))}); without a glyph in the whole chain: {unreachable.Length} ({string.Join(" ", unreachable.Select(pair => $"U+{pair.Key:X4}x{pair.Value}"))})");
        var controls = codepoints.Where(pair => pair.Key < 0x20 || (pair.Key >= 0x7F && pair.Key < 0xA0)).ToArray();
        Line($"control code points in strings: {string.Join(" ", controls.Select(pair => $"U+{pair.Key:X4}x{pair.Value}"))}");
        foreach (var family in UiFontPolicy.FallbackFamilies.Prepend("Times New Roman").Prepend("Courier New").Prepend("Courier").Prepend("FixedSys"))
        {
            var probe = gdi.Open(new UiFontSpec(family, 13, false, 100));
            var primary = ((UiFontChain)probe).Primary;
            Line($"family '{family}': GDI face '{primary.Description}'{(gdi.Substitutions.ContainsKey(family) ? " (SUBSTITUTED)" : "")}");
            probe.Dispose();
        }

        Line();
        Line("== C. C1 control code points in strings (examples) and what GDI does with them ==");
        foreach (var entry in text.Table.Entries.Where(e => e.Text.Any(ch => ch is '\u0085' or '\u0093' or '\u0094')).Take(8))
            Line($"{entry.Label}: {entry.Text.Replace('\u0085', '#')}   [U+0085 shown as #]");
        var probeFace = gdiMeasure.Face(new UiFontSpec("Arial", 16, false, 100));
        foreach (var cp in new[] { 0x85, 0x93, 0x94, 0x2026, 0x201C, 0x201D })
        {
            var bitmap = probeFace.Rasterize(cp);
            Line($"U+{cp:X4}: HasGlyph {probeFace.HasGlyph(cp)}, GDI advance {probeFace.Advance(cp)}, glyph {bitmap.Width}x{bitmap.Height}, drawable per UiTextRuns {UiTextRuns.IsDrawable(new Rune(cp))}");
        }

        Line();
        Line("== D. Advances of the Generals font (Arial with lfWidth = 0.40 height) against the 91 % squeeze ==");
        foreach (var px in new[] { 24, 32, 36, 48 })
        {
            var spec = new UiFontSpec("Arial", px, false, UiFontPolicy.GeneralsWidthPercent);
            var g = gdi.Open(spec);
            var f = freeType.Open(spec);
            var plain = freeType.Open(new UiFontSpec("Arial", px, false, 100));
            const string sample = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            Line($"{px}px GDI    : {string.Join(" ", sample.Select(ch => g.Advance(ch)))} sum {sample.Sum(ch => g.Advance(ch))}");
            Line($"{px}px FT 91% : {string.Join(" ", sample.Select(ch => f.Advance(ch)))} sum {sample.Sum(ch => f.Advance(ch))}");
            Line($"{px}px FT 100%: {string.Join(" ", sample.Select(ch => plain.Advance(ch)))} sum {sample.Sum(ch => plain.Advance(ch))}");
        }

        Line();
        Line("== E. Placeholders, hot key marks and line breaks in the English string table ==");
        var withAmpersand = text.Table.Entries.Where(e => e.Text.Contains('&')).ToArray();
        var withPercent = text.Table.Entries.Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.Text, "%[-+0 #]*[0-9]*(\\.[0-9]+)?(ll|l|h)?[a-zA-Z]")).ToArray();
        var withNewline = text.Table.Entries.Where(e => e.Text.Contains('\n')).ToArray();
        Line($"strings with '&': {withAmpersand.Length} (e.g. {string.Join(" | ", withAmpersand.Take(6).Select(e => $"{e.Label}={e.Text}"))})");
        Line($"strings with a printf conversion: {withPercent.Length} (e.g. {string.Join(" | ", withPercent.Take(6).Select(e => $"{e.Label}={e.Text}"))})");
        Line($"strings with a line break: {withNewline.Length}");

        File.WriteAllText(Path.Combine(outputDirectory, "font-audit.txt"), report.ToString());
    }
}
