using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Ui.Images;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Tests;

/// <summary>Package B1: CSF/STR string tables, localized text, Language.ini, mapped images and settings.</summary>
public static class TextAndSettingsTests
{
    public static int Run(string[] args)
    {
        var tests = new (string Name, Action Body)[]
        {
            ("CsfRoundTripUsesInvertedUtf16", CsfRoundTripUsesInvertedUtf16),
            ("CsfKeepsMultipleValuesButUsesTheFirst", CsfKeepsMultipleValuesButUsesTheFirst),
            ("CsfLookupIsCaseInsensitiveAndDuplicatesAreReported", CsfLookupIsCaseInsensitiveAndDuplicatesAreReported),
            ("CsfStructuralErrorsAreExplicit", CsfStructuralErrorsAreExplicit),
            ("StripSpacesFollowsTheOriginal", StripSpacesFollowsTheOriginal),
            ("StrParsesLabelsQuotesEscapesAndSpeech", StrParsesLabelsQuotesEscapesAndSpeech),
            ("StrErrorsAreExplicit", StrErrorsAreExplicit),
            ("LocalizedTextMarksAndCountsMissingLabels", LocalizedTextMarksAndCountsMissingLabels),
            ("FormatterHandlesTheGamesPrintfSubset", FormatterHandlesTheGamesPrintfSubset),
            ("LanguageCatalogResolvesWithoutFileSystem", LanguageCatalogResolvesWithoutFileSystem),
            ("LanguageIniIsTypedFromTheGraph", LanguageIniIsTypedFromTheGraph),
            ("MouseTooltipSettingsAreTypedFromTheGraph", MouseTooltipSettingsAreTypedFromTheGraph),
            ("InGameUiSettingsAreTypedFromTheGraph", InGameUiSettingsAreTypedFromTheGraph),
            ("MappedImagesAreTypedFromTheGraph", MappedImagesAreTypedFromTheGraph),
            ("MappedImageErrorsAreExplicit", MappedImageErrorsAreExplicit),
            ("MappedImageTexturesAreResolvedByDelegate", MappedImageTexturesAreResolvedByDelegate),
            ("SettingsRoundTripIsDeterministicAndAtomic", SettingsRoundTripIsDeterministicAndAtomic),
            ("SettingsDefaultsWhenAbsent", SettingsDefaultsWhenAbsent),
            ("SettingsClampingAndBindingIssuesAreReported", SettingsClampingAndBindingIssuesAreReported),
            ("CorruptSettingsAreKeptAside", CorruptSettingsAreKeptAside),
            ("NewerSettingsVersionIsAnErrorAndUntouched", NewerSettingsVersionIsAnErrorAndUntouched),
            ("OlderSettingsVersionsMigrateExplicitly", OlderSettingsVersionsMigrateExplicitly),
            ("ResolutionsClassifyAndFit", ResolutionsClassifyAndFit),
        };

        var failures = 0;
        foreach (var (name, body) in tests)
            failures += Execute(name, body);

        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null)
            failures += Execute("RealData", () => RealData(contentDirectory));

        Console.WriteLine(failures == 0 ? $"TextAndSettingsTests: {tests.Length} synthetic tests passed." : $"TextAndSettingsTests: {failures} failure(s).");
        return failures;
    }

    private static int Execute(string name, Action body)
    {
        try
        {
            body();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------------------------------------------------ CSF

    private static void CsfRoundTripUsesInvertedUtf16()
    {
        var bytes = CsfTestWriter.Write(3, CsfLanguage.German,
        [
            ("GUI:Ok", [("OK", null)]),
            ("GUI:Umlaut", [("  Größe  \n  zwei ", "speech_01")]),
            ("GUI:Empty", []),
        ]);
        Assert(Encoding.ASCII.GetString(bytes, 0, 4) == " FSC", "The CSF tag is the little-endian integer of 'CSF '.");
        // 'O' (0x004F) is stored as ~0x004F = 0xFFB0.
        var firstText = 24 + 4 + 4 + 4 + "GUI:Ok".Length + 4 + 4;
        Assert(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(firstText)) == 0xFFB0, "Text code units are stored bitwise inverted.");

        var file = CsfReader.Read(bytes);
        Assert(file.Version == 3 && file.Language == CsfLanguage.German && file.Labels.Length == 3, "Header fields round trip.");
        Assert(file.Labels[1].Values[0].Text == "  Größe  \n  zwei " && file.Labels[1].Values[0].Speech == "speech_01", "Raw value text and STRW speech round trip.");
        var table = StringTable.FromCsf(file, "fixture.csf");
        Assert(table.TryGet("GUI:Umlaut", out var entry) && entry.Text == "Größe\nzwei" && entry.Speech == "speech_01", "The entry is whitespace-normalized.");
        Assert(table.TryGet("GUI:Empty", out var empty) && empty.Text.Length == 0 && empty.Speech is null, "A label without values has an empty text.");

        var version1 = CsfReader.Read(CsfTestWriter.Write(1, CsfLanguage.Korean, [("A", [("x", null)])]));
        Assert(version1.Language == CsfLanguage.Us, "Version 1 carries no language id; the original assumes US.");
        var truncatedAtNul = CsfReader.Read(CsfTestWriter.Write(3, CsfLanguage.Us, [("A", [("ab\0cd", null)])]));
        Assert(truncatedAtNul.Labels[0].Values[0].Text == "ab", "A decoded NUL ends the text like the original's null-terminated copy.");
    }

    private static void CsfKeepsMultipleValuesButUsesTheFirst()
    {
        var file = CsfReader.Read(CsfTestWriter.Write(3, CsfLanguage.Us,
        [
            ("GUI:Multi", [("first", null), ("second", "wave2"), ("third", null)]),
            ("GUI:SpeechLater", [("text", ""), ("other", "ignored")]),
        ]));
        Assert(file.Labels[0].Values.Length == 3 && file.ValueCount == 5, "All values are read.");
        var table = StringTable.FromCsf(file, "fixture.csf");
        Assert(table.TryGet("GUI:Multi", out var multi) && multi.Text == "first" && multi.Speech is null, "Only the first value is used.");
        Assert(table.TryGet("GUI:SpeechLater", out var later) && later.Speech is null, "An empty speech of the first value is no speech, later speech is ignored.");
    }

    private static void CsfLookupIsCaseInsensitiveAndDuplicatesAreReported()
    {
        var table = StringTable.FromCsf(CsfReader.Read(CsfTestWriter.Write(3, CsfLanguage.Us,
        [
            ("GUI:ExitGame", [("Exit", null)]),
            ("gui:exitgame", [("Second", null)]),
            ("GUI:Other", [("Other", null)]),
        ])), "fixture.csf");
        Assert(table.TryGet("gui:EXITGAME", out var entry) && entry.Text == "Exit", "Lookup is case-insensitive and the first duplicate wins.");
        Assert(table.Duplicates.Length == 1 && table.Duplicates[0].Occurrences == 2 && table.Duplicates[0].Label == "GUI:ExitGame", "Duplicates are listed.");
        Assert(table.Count == 2 && table.Entries.Length == 3, "Count is distinct labels, entries keep file order.");
        Assert(table.LabelsWithPrefix("GUI:").SequenceEqual(["GUI:ExitGame", "GUI:Other"]), "Prefix search is canonical.");
    }

    private static void CsfStructuralErrorsAreExplicit()
    {
        var good = CsfTestWriter.Write(3, CsfLanguage.Us, [("A", [("x", "w")]), ("B", [("y", null)])]);
        Throws<CsfFormatException>(() => CsfReader.Read(good.AsSpan(0, 10)), "header alone");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 0, 0x12345678)), "header id");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 4, 4)), "version 4");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 20, 42)), "language id 42");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 8, 3)), "declares 3 labels");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 24, 0x11111111)), "Expected 'LBL '");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 24 + 4 + 4 + 4 + 1, 0x11111111)), "Expected 'STR ' or 'STRW'");
        Throws<CsfFormatException>(() => CsfReader.Read(good.AsSpan(0, good.Length - 1)), "truncated");
        Throws<CsfFormatException>(() => CsfReader.Read(Patch(good, 24 + 4, -1)), "negative");
    }

    private static void StripSpacesFollowsTheOriginal()
    {
        Assert(GameTextWhitespace.StripSpaces("  a   b  ") == "a b", "Leading, repeated and trailing blanks.");
        Assert(GameTextWhitespace.StripSpaces("a  \n  b") == "a\nb", "Blanks around a line break.");
        Assert(GameTextWhitespace.StripSpaces("a \t b") == "a\tb", "Blanks around a tab.");
        Assert(GameTextWhitespace.StripSpaces("a\u00A0\u00A0b") == "a\u00A0\u00A0b", "Only U+0020 is a blank.");
        Assert(GameTextWhitespace.StripSpaces("") == "", "Empty stays empty.");
    }

    // ------------------------------------------------------------------------------------------------------ STR

    private static void StrParsesLabelsQuotesEscapesAndSpeech()
    {
        const string text = "// header comment\r\n\r\nGUI:Simple\r\n\"Hello   world\"\r\nEND\r\n\r\n"
            + "MAP:Multi\n  \"first line\n   second \\\"quoted\\\" line\\n next\\\\\"  = Speech_07 trailing\n  end\n"
            + "MAP:NoText\n// comment inside\nEND\n"
            + "MAP:Latin\n\"caf\xE9\"\nEND\n";
        var table = StrReader.Read(Encoding.Latin1.GetBytes(text), "fixture.str");
        Assert(table.Format == StringTableFormat.Str && table.Count == 4, "Four labels.");
        Assert(table.TryGet("gui:simple", out var simple) && simple.Text == "Hello world" && simple.Speech is null, "Blanks collapse, lookup ignores case.");
        Assert(table.TryGet("MAP:Multi", out var multi) && multi.Text == "first line second \"quoted\" line\nnext\\", $"Line breaks become blanks, escapes translate: '{multi.Text}'.");
        Assert(multi.Speech == "Speech_07e", "A speech name ending in a digit gets the suffix 'e'.");
        Assert(table.TryGet("MAP:NoText", out var none) && none.Text.Length == 0, "A label without a quote has an empty text.");
        Assert(table.TryGet("MAP:Latin", out var latin) && latin.Text == "café", "Bytes are Latin-1.");
        Assert(StrReader.TranslateEscapes(@"a\tb\?c\'d\qe\") == "a\tb?c'dqe", "translateCopy escapes; a trailing backslash is dropped.");
    }

    private static void StrErrorsAreExplicit()
    {
        static void Read(string text) => StrReader.Read(Encoding.Latin1.GetBytes(text), "bad.str");
        Throws<StrFormatException>(() => Read("A\n\"x\"\nEND\na\n\"y\"\nEND\n"), "defined again");
        Throws<StrFormatException>(() => Read("A\n\"x\"\n\"y\"\nEND\n"), "more than one string");
        Throws<StrFormatException>(() => Read("A\n\"x\"\n"), "END is missing");
        Throws<StrFormatException>(() => Read("A\n\"never closed\nEND\n"), "Unterminated");
        Throws<StrFormatException>(() => Read("A\nstray\nEND\n"), "Unexpected line 'stray'");
    }

    // ------------------------------------------------------------------------------------------------------ facade

    private static void LocalizedTextMarksAndCountsMissingLabels()
    {
        var main = new StringTable("main", StringTableFormat.Csf,
            [new StringEntry("GUI:Players", "%d players on %s", null), new StringEntry("GUI:Shared", "main", null)]);
        var map = new StringTable("map.str", StringTableFormat.Str,
            [new StringEntry("MAP:Intro", "intro", "intro01e"), new StringEntry("GUI:Shared", "map", null)]);
        var text = new LocalizedText(main, map);
        Assert(text.TryGet("gui:players", out var players) && players == "%d players on %s", "TryGet finds the main table.");
        Assert(text.Get("MAP:Intro") == "intro", "The map table is searched after the main table.");
        Assert(text.Get("GUI:Shared") == "main", "The main table wins over the map table.");
        Assert(!text.TryGet("GUI:Nope", out var nothing) && nothing.Length == 0, "TryGet does not count.");
        Assert(text.MissCount == 0, "Only Get/Format count misses.");
        Assert(text.Get("GUI:Nope") == "??GUI:Nope??" && text.Get("GUI:Nope") == "??GUI:Nope??", "A missing label is a visible marker.");
        Assert(text.Format("GUI:Gone", 1) == "??GUI:Gone??", "A missing format label is not formatted.");
        Assert(text.MissingLabels.SequenceEqual(["GUI:Gone", "GUI:Nope"]) && text.MissCount == 3, "Misses are recorded and counted.");
        Assert(text.Format("GUI:Players", 4, "Tournament Desert") == "4 players on Tournament Desert", "Format fills the arguments.");
        Assert(text.WithMapStrings(null).Get("MAP:Intro") == "??MAP:Intro??", "Without a map table the map label is missing.");
    }

    private static void FormatterHandlesTheGamesPrintfSubset()
    {
        Assert(GameTextFormatter.Format("%d%% %s %ls %hs %S", 50, "a", "b", "c", "d") == "50% a b c d", "Basic conversions.");
        Assert(GameTextFormatter.Format("[%5d|%-5d|%05d|%+d|%.3d]", 42, 42, -42, 7, 5) == "[   42|42   |-0042|+7|005]", "Width, flags, precision.");
        Assert(GameTextFormatter.Format("%.2f|%6.1f|%f|%+.0f", 1.004, 2.26f, 0.5m, 3.0) == "1.00|   2.3|0.500000|+3", "Reals use the invariant culture.");
        Assert(GameTextFormatter.Format("%x %X %#x %c %.2s", 255, 255, 255, 'Z', "abc") == "ff FF 0xff Z ab", "Hex, char, string precision.");
        Assert(GameTextFormatter.Format("%I64d %lld %lu", 5L, 6L, 7u) == "5 6 7", "Length modifiers are accepted.");
        Throws<FormatException>(() => GameTextFormatter.Format("%d", "text"), "expects an integer");
        Throws<FormatException>(() => GameTextFormatter.Format("%s", 5), "expects a string");
        Throws<FormatException>(() => GameTextFormatter.Format("%s %s", "one"), "no argument");
        Throws<FormatException>(() => GameTextFormatter.Format("%s", "one", "two"), "2 were supplied");
        Throws<FormatException>(() => GameTextFormatter.Format("%q", 1), "Unsupported conversion");
        Throws<FormatException>(() => GameTextFormatter.Format("100%"), "Dangling");
        Throws<FormatException>(() => GameTextFormatter.Format("%*d", 1, 2), "not supported");
    }

    private static void LanguageCatalogResolvesWithoutFileSystem()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Data/English/generals.csf", "Data/English/Language.ini", "Data/German/Language.ini" };
        var english = LanguageCatalog.Resolve("English", files.Contains);
        Assert(english.StringFile == "Data/English/Generals.csf" && english.StringFormat == StringTableFormat.Csf && english.LanguageIni is not null, "English resolves to its CSF.");
        var german = LanguageCatalog.Resolve("German", files.Contains);
        Assert(!german.HasStringTable && german.LanguageIni == "Data/German/Language.ini", "German without CSF has no string table.");
        files.Add("Data/Generals.str");
        Assert(LanguageCatalog.Resolve("English", files.Contains).StringFormat == StringTableFormat.Str, "Data/Generals.str takes precedence like in release builds.");
        var all = LanguageCatalog.Discover(["German", "English", "english"], files.Contains);
        Assert(all.Select(value => value.Language).SequenceEqual(["English", "German"]), "Discovery is distinct and ordinal.");
        Throws<ArgumentException>(() => LanguageCatalog.CsfFile("../English"), "ASCII letters");
    }

    private static void LanguageIniIsTypedFromTheGraph()
    {
        const string ini = "Language\n  UnicodeFontName = \"Arial Unicode MS\"\n  LocalFontFile = FONT.TTF\n  MilitaryCaptionSpeed = 2;\n"
            + "  ResolutionFontAdjustment = 0.5\n  MessageFont = Arial 10 Yes ; comment\n  DefaultWindowFont = \"Times New Roman\" 14 No\nEnd\n";
        var graph = Compile(new Dictionary<string, string> { ["Data/English/Language.ini"] = ini });
        var language = GlobalLanguageSettings.FromGraph(graph);
        Assert(language.UnicodeFontName == "Arial Unicode MS" && language.LocalFontFiles.SequenceEqual(["FONT.TTF"]), "Scalar keys are typed, quotes removed.");
        Assert(language.MilitaryCaptionSpeed == 2 && language.ResolutionFontAdjustment == 0.5m && language.MilitaryCaptionDelayMilliseconds == 750, "Numbers and the delay default.");
        Assert(language.Font("MessageFont") == new FontDescription("Arial", 10, true), "Unquoted font.");
        Assert(language.Font("DefaultWindowFont") == new FontDescription("Times New Roman", 14, false), "Quoted font name.");
        Assert(language.Font("TooltipFontName") == FontDescription.Default && language.DefaultedKeys.Contains("TooltipFontName"), "Unset slots are default and listed.");
        Assert(language.AdjustFontSize(10, 800) == 10 && language.AdjustFontSize(10, 1600) == 15 && language.AdjustFontSize(10, 8000) == 20, "adjustFontSize clamps to 1..2.");
        Throws<FormatException>(() => GlobalLanguageSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/English/Language.ini"] = "Language\n  Bogus = 1\nEnd\n" })), "not a key");
        Throws<FormatException>(() => GlobalLanguageSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/English/Language.ini"] = "Language\n  MessageFont = Arial Yes\nEnd\n" })), "must be");
        Throws<InvalidDataException>(() => GlobalLanguageSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/Mouse.ini"] = "Mouse\nEnd\n" })), "no 'Language' block");
    }

    private static void MouseTooltipSettingsAreTypedFromTheGraph()
    {
        const string ini = "Mouse\n  TooltipFontName = Arial\n  TooltipFontSize = 8\n  TooltipFontIsBold = No\n  TooltipAnimateBackground = No ; scroll out\n"
            + "  TooltipFillTime = 250\n  TooltipDelayTime = 800\n  TooltipTextColor = R:220 G:220 B:220 A:255\n  TooltipHighlightColor = R:255 G:255 B:0 A:255\n"
            + "  TooltipShadowColor = R:0 G:0 B:0 A:255\n  TooltipBorderColor = R:60 G:60 B:155 A:255\n  TooltipBackgroundColor = R:20 G:20 B:20 A:128\n"
            + "  TooltipWidth = 20\n  OrthoCamera = Yes\n  DragTolerance = 25\nEnd\n";
        var settings = MouseTooltipSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/Mouse.ini"] = ini }));
        Assert(settings.Font == new FontDescription("Arial", 8, false) && !settings.AnimateBackground, "Font and animation flag.");
        Assert(settings.FillTimeMilliseconds == 250 && settings.DelayMilliseconds == 800 && settings.WidthFraction == 0.2m, "Times and the width percentage become a fraction.");
        Assert(settings.TextColor.R == 220 && settings.HighlightColor.G == 255 && settings.BorderColor.B == 155 && settings.BackgroundColor.A == 128, "Colors are R:n G:n B:n A:n.");
        Throws<FormatException>(() => MouseTooltipSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/Mouse.ini"] = ini.Replace("TooltipWidth = 20\n", string.Empty, StringComparison.Ordinal) })), "no TooltipWidth");
        Throws<FormatException>(() => MouseTooltipSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/Mouse.ini"] = ini.Replace("R:20 G:20 B:20", "R:20 G:20", StringComparison.Ordinal) })), "Malformed color");
        Throws<InvalidDataException>(() => MouseTooltipSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/English/Language.ini"] = "Language\nEnd\n" })), "no 'Mouse' block");
    }

    private static void InGameUiSettingsAreTypedFromTheGraph()
    {
        const string ini = "InGameUI\n  MessageColor1 = R:255 G:250 B:245\n  MessagePosition = X:10 Y:22\n  MessageFont = Arial\n  MessagePointSize = 10\n  MessageBold = Yes\n  MessageDelayMS = 75000\n"
            + "  MilitaryCaptionColor = R:255 G:255 B:255 A:128\n  MilitaryCaptionPosition = X:10 Y:340\n  MilitaryCaptionTitleFont = Courier New\n  MilitaryCaptionTitlePointSize = 12\n  MilitaryCaptionTitleBold = Yes\n"
            + "  SuperweaponCountdownPosition = X:0.90 Y:0.01\n  SuperweaponCountdownFlashDuration = 500 ; in milliseconds\n  NamedTimerCountdownPosition = X:0.90 Y:0.666\n"
            + "  FloatingTextTimeOut = 333\n  FloatingTextMoveUpSpeed = 30\n  FloatingTextVanishRate = 3\n  DrawRMBScrollAnchor = No\n"
            + "  SpyDroneRadiusCursor\n    Texture = SccSpyDrone_USA\n    Style = SHADOW_ALPHA_DECAL\n  End\nEnd\n";
        var settings = InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = ini }));
        Assert(settings.MessagePosition == new InGameUiPixelPoint(10, 22) && settings.MessageColor1 == new WndColor(255, 250, 245, 255), "Pixel pair and a color without alpha (alpha 255).");
        Assert(settings.MessageFont == new InGameUiFont("Arial", 10, true) && settings.MessageDelayMilliseconds == 75000, "Font keys and the delay.");
        Assert(settings.MilitaryCaptionColor == new WndColor(255, 255, 255, 128) && settings.MilitaryCaptionPosition == new InGameUiPixelPoint(10, 340), "RGBA color and position.");
        Assert(settings.MilitaryCaptionTitleFont.Name == "Courier", "parseAsciiString takes the first token of an unquoted name (Language.ini replaces the font anyway).");
        Assert(settings.SuperweaponPosition == new InGameUiFractionPoint(0.90m, 0.01m) && settings.NamedTimerPosition == new InGameUiFractionPoint(0.90m, 0.666m), "Fractional positions.");
        Assert(settings.SuperweaponFlashFrames == 15m, "500 ms are 15 logic frames.");
        Assert(settings.FloatingTextTimeOutFrames == 10 && settings.FloatingTextMoveUpPerFrame == 1m && settings.FloatingTextVanishPerFrame == 0.1m, "Durations round up, velocities become per frame.");
        Assert(settings.RadiusCursorSections.SequenceEqual(["SpyDroneRadiusCursor"]), "The radius cursor sub-block is listed.");
        Assert(settings.MessageColor2 == new WndColor(180, 180, 180, 255) && settings.DefaultedKeys.Contains("MessageColor2") && !settings.DefaultedKeys.Contains("MessageColor1"), "A key the file lacks keeps the constructor default and is listed.");
        Assert(settings.NamedTimerNormalColor == new WndColor(255, 255, 0, 255) && settings.DrawableCaptionFont == new InGameUiFont("Arial", 10, false), "More constructor defaults.");
        Throws<FormatException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = "InGameUI\n  Bogus = 1\nEnd\n" })), "not a key");
        Throws<FormatException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = "InGameUI\n  MessageColor1 = R:1 G:2\nEnd\n" })), "Malformed color");
        Throws<FormatException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = "InGameUI\n  MessagePosition = X:1.5 Y:2\nEnd\n" })), "pair of integers");
        Throws<FormatException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = "InGameUI\n  MessageBold = Maybe\nEnd\n" })), "Yes or No");
        Throws<FormatException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/InGameUI.ini"] = "InGameUI\n  Surprise\n    Texture = X\n  End\nEnd\n" })), "not a sub-block");
        Throws<InvalidDataException>(() => InGameUiSettings.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/Mouse.ini"] = "Mouse\nEnd\n" })), "no 'InGameUI' block");
    }

    // ------------------------------------------------------------------------------------------------------ images

    private const string ImagesIni = """
        MappedImage FirstImage
          Texture = PageA.tga
          TextureWidth = 512
          TextureHeight = 256
          Coords = Left:0 Top:0 Right:128 Bottom:64
          Status = NONE
        End

        MappedImage RotatedImage
          Texture = PageA.tga
          TextureWidth = 512
          TextureHeight = 256
          Coords = Left:128 Top:0 Right:160 Bottom:100
          Status = ROTATED_90_CLOCKWISE
        End

        MappedImage OtherPage
          Texture = PageB.tga
          TextureWidth = 64
          TextureHeight = 64
          Coords = Left:1 Top:2 Right:3 Bottom:4
          Status = NONE
        End
        """;

    private static void MappedImagesAreTypedFromTheGraph()
    {
        var graph = Compile(new Dictionary<string, string>
        {
            ["Data/INI/MappedImages/TextureSize_512/Packed.ini"] = ImagesIni,
            ["Data/INI/MappedImages/HandCreated/Override.ini"] = "MappedImage FirstImage\n  Coords = Left:2 Top:4 Right:130 Bottom:68\nEnd\n",
        });
        var table = MappedImageTable.FromGraph(graph);
        Assert(table.Count == 3, $"Three images, got {table.Count}.");
        var first = table.Get("FIRSTIMAGE");
        Assert(first is { Left: 2, Top: 4, Right: 130, Bottom: 68, TextureFile: "PageA.tga", Width: 128, Height: 64 }, "A later block updates the image (HandCreated loads after TextureSize_512).");
        Assert(first.Uv == new UvRect(2 / 512f, 4 / 256f, 130 / 512f, 68 / 256f), "UVs are normalized by the texture size.");
        var rotated = table.Get("RotatedImage");
        Assert(rotated.IsRotated && rotated.Width == 100 && rotated.Height == 32, "Rotated images swap width and height.");
        Assert(table.ImagesPerTexture().SequenceEqual([("PageA.tga", 2), ("PageB.tga", 1)]), "Images per texture file.");
        Assert(!table.TryGet("Missing", out _), "Unknown names are not found.");
        Throws<KeyNotFoundException>(() => table.Get("Missing"), "does not exist");
        Assert(table.Images.Select(image => image.Name).SequenceEqual(["FirstImage", "OtherPage", "RotatedImage"]), "Canonical order.");
    }

    private static void MappedImageErrorsAreExplicit()
    {
        static void Build(string body) => MappedImageTable.FromGraph(Compile(new Dictionary<string, string>
        {
            ["Data/INI/MappedImages/HandCreated/Bad.ini"] = "MappedImage Bad\n" + body + "End\n",
        }));
        const string texture = "  Texture = A.tga\n  TextureWidth = 64\n  TextureHeight = 64\n";
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:0 Top:0 Right:8 Bottom:8\n"), "'Status' is missing");
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:0 Top:0 Right:8\n  Status = NONE\n"), "must be 'Left:n");
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:0 Top:0 Right:80 Bottom:8\n  Status = NONE\n"), "inside the 64x64 page");
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:8 Top:0 Right:8 Bottom:8\n  Status = NONE\n"), "non-empty rectangle");
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:0 Top:0 Right:8 Bottom:8\n  Status = SPINNING\n"), "not an image status");
        Throws<MappedImageFormatException>(() => Build(texture + "  Coords = Left:0 Top:0 Right:8 Bottom:8\n  Status = NONE\n  Tint = 1\n"), "not a MappedImage key");
        Throws<MappedImageFormatException>(() => Build("  Texture = A.tga\n  TextureWidth = 0\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:8 Bottom:8\n  Status = NONE\n"), "positive integer");
        Throws<MappedImageFormatException>(() => Build("  Texture = A.tga B.tga\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:8 Bottom:8\n  Status = NONE\n"), "one file name");
    }

    private static void MappedImageTexturesAreResolvedByDelegate()
    {
        var table = MappedImageTable.FromGraph(Compile(new Dictionary<string, string> { ["Data/INI/MappedImages/TextureSize_512/Packed.ini"] = ImagesIni }));
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Data/German/Art/Textures/pagea.tga", "Art/Textures/PageA.tga" };
        var report = MappedImageTextures.Resolve(table, "German", files.Contains);
        Assert(report.Textures.Length == 2 && report.Textures[0] is { Found: true, Localized: true, Images: 2 }, "The localized folder wins.");
        Assert(report.Missing.Single().TextureFile == "PageB.tga" && report.ImagesWithMissingTexture == 1, "Missing textures are reported with their image count.");
        files.Add("Art/Textures/PageB.dds");
        Assert(MappedImageTextures.Resolve(table, "English", files.Contains) is { Missing.Length: 0 } english
            && english.Textures[0].VirtualPath == "Art/Textures/PageA.tga" && english.Textures[1].VirtualPath == "Art/Textures/PageB.dds", "A .dds stem counts.");
        Throws<ArgumentException>(() => MappedImageTextures.Candidates("../x.tga", "English"), "plain file name");
    }

    // ------------------------------------------------------------------------------------------------------ settings

    private static void SettingsRoundTripIsDeterministicAndAtomic()
    {
        var directory = Scratch();
        try
        {
            var store = new SettingsStore(directory);
            var settings = GameSettings.Default with
            {
                Display = GameSettings.Default.Display with { Mode = DisplayMode.Borderless, Width = 3440, Height = 1440, UiScale = UiScaleMode.IntegerMultiple },
                Audio = GameSettings.Default.Audio with { Master = 0.25f, Voice = 0f },
                Accessibility = new AccessibilitySettings(true, 150, true),
                General = GameSettings.Default.General with { Language = "German", ShowFps = true, DefaultAiDifficulty = AiDifficulty.Hard },
                Skirmish = new SkirmishMemory("Maps/Alpine Assault/Alpine Assault.map", ["FactionChina", "FactionGLA"], 50000),
            };
            Assert(store.Save(settings).IsEmpty, "Valid settings save without issues.");
            Assert(!File.Exists(store.FilePath + ".tmp"), "No temporary file is left behind.");
            var first = File.ReadAllBytes(store.FilePath);
            var loaded = store.Load();
            Assert(loaded.Status == SettingsLoadStatus.Loaded && loaded.Issues.IsEmpty && loaded.FileVersion == GameSettings.CurrentVersion, "Loads cleanly.");
            Assert(loaded.Settings == settings, "Round trip preserves every value (including the faction list).");
            store.Save(loaded.Settings);
            Assert(first.AsSpan().SequenceEqual(File.ReadAllBytes(store.FilePath)), "Serialization is byte-identical.");
            Assert(first.AsSpan().SequenceEqual(SettingsJson.Write(settings)), "The file is exactly the serializer output.");
            var json = Encoding.UTF8.GetString(first);
            Assert(!json.Contains('\r') && json.IndexOf("\"version\"", StringComparison.Ordinal) < json.IndexOf("\"display\"", StringComparison.Ordinal)
                && json.IndexOf("\"general\"", StringComparison.Ordinal) < json.IndexOf("\"skirmish\"", StringComparison.Ordinal), "Fixed property order and \\n line ends.");
            Assert(json.Contains("\"mode\": \"borderless\"") && json.Contains("\"uiScale\": \"integerMultiple\""), "Enums are camelCase names.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void SettingsDefaultsWhenAbsent()
    {
        var directory = Path.Combine(Scratch(), "not-yet-created");
        try
        {
            var result = new SettingsStore(directory).Load();
            Assert(result.Status == SettingsLoadStatus.Defaults && result.Settings == GameSettings.Default && result.Issues.IsEmpty, "Absent file means defaults.");
            Assert(!Directory.Exists(directory), "Loading does not create the directory.");
            new SettingsStore(directory).Save(GameSettings.Default);
            File.WriteAllText(Path.Combine(directory, SettingsStore.FileName + ".tmp"), "partial");
            var stale = new SettingsStore(directory).Load();
            Assert(stale.Issues.Single().Code == "stale-temp-file", "A left-over temporary file is reported.");
            File.WriteAllBytes(Path.Combine(directory, SettingsStore.FileName), [0xEF, 0xBB, 0xBF, ..SettingsJson.Write(GameSettings.Default)]);
            Assert(new SettingsStore(directory).Load().Status == SettingsLoadStatus.Loaded, "A UTF-8 byte order mark is accepted.");
        }
        finally { Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true); }
    }

    private static void SettingsClampingAndBindingIssuesAreReported()
    {
        var (normalized, issues) = SettingsValidator.Normalize(GameSettings.Default with
        {
            Display = GameSettings.Default.Display with { Width = 320, Mode = (DisplayMode)9 },
            Audio = GameSettings.Default.Audio with { Master = 1.5f, Music = float.NaN },
            Accessibility = GameSettings.Default.Accessibility with { UiTextScalePercent = 130 },
            General = GameSettings.Default.General with { Language = "Engl1sh" },
            Skirmish = new SkirmishMemory("C:\\evil.map", ["FactionUSA", "bad name", "A", "B", "C", "D", "E", "F", "G", "H"], -5),
        });
        Assert(normalized.Display.Width == 800 && normalized.Display.Mode == DisplayMode.Windowed, "Width clamps, unknown enum falls back visibly.");
        Assert(normalized.Audio.Master == 1f && normalized.Audio.Music == GameSettings.Default.Audio.Music, "Volume clamps, NaN is replaced.");
        Assert(normalized.Accessibility.UiTextScalePercent == 125 && normalized.General.Language == "English", "Text scale snaps, bad language is replaced.");
        Assert(normalized.Skirmish.LastMap is null && normalized.Skirmish.LastFactions.Length == 8 && normalized.Skirmish.LastStartMoney == 0, "Skirmish memory is sanitized.");
        var codes = issues.Select(issue => $"{issue.Path}:{issue.Code}").ToArray();
        string[] expected =
        [
            "display.mode:invalid-value", "display.width:clamped", "audio.master:clamped", "audio.music:invalid-value",
            "accessibility.uiTextScalePercent:snapped", "general.language:invalid-value", "skirmish.lastMap:invalid-value",
            "skirmish.lastFactions:invalid-value", "skirmish.lastFactions:clamped", "skirmish.lastStartMoney:clamped",
        ];
        Assert(codes.SequenceEqual(expected), $"Every correction is reported: {string.Join(", ", codes)}.");

        var directory = Scratch();
        try
        {
            var store = new SettingsStore(directory);
            File.WriteAllText(store.FilePath, """
                {
                  "version": 1,
                  "display": { "mode": "fullscreen", "width": "wide", "height": 1080, "vsync": false, "uiScale": "stretch", "aspect": "pillarbox", "hdr": true },
                  "audio": { "master": 0.5, "music": 0.5, "effects": 0.5, "voice": 0.5, "ambient": 7 },
                  "controls": { "scrollSpeed": 0.9, "edgeScrolling": false },
                  "general": { "language": "French", "showFps": true, "pauseOnFocusLoss": false, "defaultAiDifficulty": "brutal" },
                  "skirmish": { "lastMap": null, "lastFactions": ["FactionGLA", 3], "lastStartMoney": 20000 },
                  "extra": 1
                }
                """);
            var result = store.Load();
            var found = result.Issues.Select(issue => $"{issue.Path}:{issue.Code}").ToArray();
            string[] wanted =
            [
                "extra:unknown-property", "display.hdr:unknown-property", "accessibility:missing", "display.width:invalid-type",
                "controls.cameraRotateSpeed:missing", "controls.alternateMouse:missing", "general.defaultAiDifficulty:invalid-type", "skirmish.lastFactions:invalid-type",
                "audio.ambient:clamped",
            ];
            Assert(found.SequenceEqual(wanted), $"Binding issues: {string.Join(", ", found)}.");
            Assert(result.Status == SettingsLoadStatus.Loaded && result.Settings.Display.Mode == DisplayMode.Fullscreen && result.Settings.Display.Width == 1280
                && result.Settings.Audio.Ambient == 1f && result.Settings.General.Language == "French" && result.Settings.Skirmish.LastFactions.IsEmpty,
                "Valid values are kept, invalid ones use the defaults.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CorruptSettingsAreKeptAside()
    {
        foreach (var content in new[] { "{ \"version\": 1, ", "[1, 2]", "{ \"display\": {} }", "{ \"version\": \"one\" }" })
        {
            var directory = Scratch();
            try
            {
                var store = new SettingsStore(directory);
                File.WriteAllText(store.FilePath, content);
                var result = store.Load();
                Assert(result.Status == SettingsLoadStatus.RecoveredFromCorruptFile && result.Settings == GameSettings.Default, $"'{content}' is corrupt.");
                Assert(result.BrokenFile == store.BrokenFilePath && File.ReadAllText(store.BrokenFilePath) == content && !File.Exists(store.FilePath), "The broken file is kept aside.");
                Assert(result.Issues.Single().Code == "corrupt", "Corruption is reported.");
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    private static void NewerSettingsVersionIsAnErrorAndUntouched()
    {
        var directory = Scratch();
        try
        {
            var store = new SettingsStore(directory);
            var content = $"{{ \"version\": {GameSettings.CurrentVersion + 1} }}";
            File.WriteAllText(store.FilePath, content);
            Throws<SettingsVersionException>(() => store.Load(), "newer than version");
            Assert(File.ReadAllText(store.FilePath) == content && !File.Exists(store.BrokenFilePath), "A newer file is left unchanged.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void OlderSettingsVersionsMigrateExplicitly()
    {
        var directory = Scratch();
        try
        {
            File.WriteAllText(Path.Combine(directory, SettingsStore.FileName), """{ "version": 0, "fullscreen": true }""");
            Throws<SettingsVersionException>(() => new SettingsStore(directory).Load(), "no migration from settings schema version 0");

            var migration = new SettingsMigration(0, "fullscreen flag becomes display.mode", root =>
            {
                var fullscreen = root["fullscreen"]!.GetValue<bool>();
                root.Remove("fullscreen");
                var migrated = JsonNode.Parse(Encoding.UTF8.GetString(SettingsJson.Write(GameSettings.Default)))!.AsObject();
                migrated["display"]!["mode"] = fullscreen ? "fullscreen" : "windowed";
                return migrated;
            });
            var result = new SettingsStore(directory, [migration]).Load();
            Assert(result.Status == SettingsLoadStatus.Migrated && result.FileVersion == 0, "The file is migrated.");
            Assert(result.Settings.Display.Mode == DisplayMode.Fullscreen, "The migration result is bound.");
            Assert(result.Issues.Single().Code == "migrated", "The migration is reported.");
            Throws<ArgumentException>(() => new SettingsStore(directory, [migration, migration]), "same schema version");
            Throws<ArgumentException>(() => new SettingsStore(directory, [migration with { FromVersion = GameSettings.CurrentVersion }]), "must start below");
            Assert(SettingsStore.Migrations.IsEmpty, "Schema version 1 ships without migrations.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void ResolutionsClassifyAndFit()
    {
        Assert(Resolutions.Classify(new Resolution(1366, 768))?.Name == "16:9", "1366x768 is 16:9.");
        Assert(Resolutions.Classify(new Resolution(3440, 1440))?.Name == "21:9" && Resolutions.Classify(new Resolution(2560, 1080))?.Name == "21:9", "Both 21:9 shapes.");
        Assert(Resolutions.Classify(new Resolution(5120, 1440))?.Name == "32:9" && Resolutions.Classify(new Resolution(1024, 768))?.Name == "4:3", "32:9 and 4:3.");
        Assert(Resolutions.Classify(new Resolution(1280, 1024)) is null && Resolutions.Classify(new Resolution(1080, 1920)) is null, "5:4 and portrait are custom.");
        Assert(Resolutions.Standard.All(value => Resolutions.Classify(value) is not null), "Every standard resolution classifies.");
        Assert(Resolutions.Standard.Distinct().Count() == Resolutions.Standard.Length, "No duplicate standard resolutions.");

        Assert(Resolutions.BestFit(1600, 900, 1920, 1080) is { Adjusted: false, Result: { Width: 1600, Height: 900 } }, "A fitting request is kept.");
        Assert(Resolutions.BestFit(3840, 2160, 1920, 1040) is { Adjusted: true, Result: { Width: 1600, Height: 900 } }, "Largest 16:9 inside the work area.");
        Assert(Resolutions.BestFit(5120, 1440, 2560, 1400) is { Adjusted: true, Result: { Width: 2560, Height: 1080 } }, "No 32:9 fits, largest of any class.");
        Assert(Resolutions.BestFit(640, 480, 1920, 1080) is { Adjusted: true, Result: { Width: 800, Height: 600 } }, "Raised to the minimum.");
        Assert(Resolutions.BestFit(1024, 768, 640, 480) is { Adjusted: true, Result: { Width: 800, Height: 600 } } fit && fit.Reason.Contains("smaller than the minimum"), "Tiny desktop.");
        Throws<ArgumentOutOfRangeException>(() => Resolutions.BestFit(800, 600, 0, 600), "must be positive");
    }

    // ------------------------------------------------------------------------------------------------------ real data

    private static readonly string[] MenuWindows =
    [
        "Window/Menus/MainMenu.wnd", "Window/Menus/OptionsMenu.wnd", "Window/Menus/SkirmishGameOptionsMenu.wnd",
        "Window/Menus/SkirmishMapSelectMenu.wnd", "Window/ControlBar.wnd", "Window/Menus/ScoreScreen.wnd",
    ];

    private static void RealData(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);

        // String table.
        var language = LanguageCatalog.Resolve("English", content.Exists);
        Assert(language is { StringFormat: StringTableFormat.Csf, StringFile: not null }, "English resolves to the CSF.");
        CsfFile csf;
        using (var stream = content.OpenRead(language.StringFile!)) csf = CsfReader.Read(stream);
        var table = StringTable.FromCsf(csf, language.StringFile!);
        var withSpeech = csf.Labels.Count(label => label.Values.Any(value => !string.IsNullOrEmpty(value.Speech)));
        var multiValue = csf.Labels.Count(label => label.Values.Length > 1);
        var emptyLabels = csf.Labels.Count(label => label.Values.IsEmpty);
        Console.WriteLine($"  generals.csf: version {csf.Version}, language {csf.Language}, {csf.Labels.Length} labels, {csf.ValueCount} values "
            + $"(header num_strings {csf.DeclaredStringCount}), {table.Count} distinct labels, {table.Duplicates.Length} duplicate labels, "
            + $"{withSpeech} with speech, {multiValue} multi-value, {emptyLabels} without value");
        foreach (var duplicate in table.Duplicates) Console.WriteLine($"    duplicate: {duplicate.Label} x{duplicate.Occurrences}");
        Assert(csf.Labels.Length > 6000, "The retail CSF has more than 6000 labels.");
        var categories = csf.Labels.Select(label => label.Label.Split(':')[0]).GroupBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Take(8);
        Console.WriteLine($"  label prefixes (top 8): {string.Join(", ", categories.Select(group => $"{group.Key} {group.Count()}"))}");

        // Every printf conversion used by the texts must be one the formatter understands. "% " followed by a letter is a
        // literal percent sign in prose ("50% armor"), not a space-flag conversion; those are counted separately.
        var conversions = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var specPattern = new Regex(@"%(?:%|[-+0#]*\d*(?:\.\d+)?(?:I64|ll|h|l|L)?[a-zA-Z])", RegexOptions.CultureInvariant);
        var literalPercent = table.Entries.Sum(entry => Regex.Matches(entry.Text, "% [A-Za-z]").Count);
        foreach (var entry in table.Entries)
            foreach (Match match in specPattern.Matches(entry.Text))
                conversions[match.Value] = conversions.GetValueOrDefault(match.Value) + 1;
        Console.WriteLine($"  printf conversions: {string.Join(", ", conversions.Select(pair => $"{pair.Key} {pair.Value}"))}; {literalPercent} literal \"% x\" percent signs");
        foreach (var spec in conversions.Keys.Where(spec => spec != "%%"))
            GameTextFormatter.Format(spec, SampleArgument(spec));

        // Textual string files shipped with the game (map strings and the patch file).
        var strFiles = content.EnumerateFiles().Where(path => path.EndsWith(".str", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        var strLabels = 0;
        var rejected = new List<string>();
        foreach (var path in strFiles)
        {
            using var stream = content.OpenRead(path);
            try { strLabels += StrReader.Read(stream, path).Count; }
            catch (StrFormatException exception)
            {
                rejected.Add(path);
                Console.WriteLine($"    rejected: {exception.Message}");
            }
        }
        Console.WriteLine($"  .str files: {strFiles.Length - rejected.Count}/{strFiles.Length} parsed, {strLabels} labels");
        // MD_GLA05/map.str is commented out line by line with ';', which the string reader does not know: the original
        // takes the first line as a label, skips the rest and fails at the end of the file (no END), so it loads nothing.
        Assert(rejected.SequenceEqual(["Maps/MD_GLA05/map.str"]), $"Only the known commented-out map.str is rejected: {string.Join(", ", rejected)}.");

        // WND label coverage of the screens the first menus need.
        var windows = MenuWindows.Concat(content.EnumerateFiles("Window/Menus")
                .Where(path => Path.GetFileName(path).Contains("Load", StringComparison.Ordinal) && path.EndsWith(".wnd", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var text = new LocalizedText(table);
        var totalLabels = 0;
        var unresolvedTotal = 0;
        var unresolvedAll = new List<string>();
        foreach (var path in windows)
        {
            Assert(content.Exists(path), $"{path} exists.");
            using var stream = content.OpenRead(path);
            using var reader = new StreamReader(stream, Encoding.Latin1);
            var labels = WndTextLabels(reader.ReadToEnd());
            var unresolved = labels.Where(label => !text.TryGet(label, out _)).ToArray();
            totalLabels += labels.Count;
            unresolvedTotal += unresolved.Length;
            unresolvedAll.AddRange(unresolved.Select(label => $"{Path.GetFileName(path)}|{label}"));
            Console.WriteLine($"  {path}: {labels.Count - unresolved.Length}/{labels.Count} labels resolve"
                + (unresolved.Length == 0 ? string.Empty : $"; unresolved: {string.Join(", ", unresolved)}"));
        }
        var placeholders = unresolvedAll.Count(entry => !entry.Split('|')[1].Contains(':'));
        Console.WriteLine($"  WND coverage: {totalLabels - unresolvedTotal}/{totalLabels} distinct labels per file resolve in {windows.Length} files; "
            + $"of the {unresolvedTotal} unresolved, {placeholders} are placeholder texts without a label prefix, {unresolvedTotal - placeholders} are labels missing from the retail CSF");

        // Definition graph: Language.ini and mapped images.
        var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), options: DefinitionCompilerOptions.Audit with { MapOverlays = MapOverlayMode.Exclude });
        var languageSettings = GlobalLanguageSettings.FromGraph(graph);
        Console.WriteLine($"  Language.ini: unicode font '{languageSettings.UnicodeFontName}', {languageSettings.Fonts.Count} font slots set, "
            + $"defaulted: {string.Join(", ", languageSettings.DefaultedKeys)}; DefaultWindowFont {languageSettings.Font("DefaultWindowFont")}");
        RealInGameUi(graph, languageSettings);
        var images = MappedImageTable.FromGraph(graph);
        var report = MappedImageTextures.Resolve(images, "English", content.Exists);
        var rotated = images.Images.Count(image => image.IsRotated);
        var redefined = graph.MultiFileDefinitions.Count(value => value.Id.Kind == DefinitionKind.Compatibility && value.Id.Name.StartsWith(MappedImageTable.DefinitionPrefix, StringComparison.Ordinal));
        Console.WriteLine($"  mapped images: {images.Count} images ({rotated} rotated) on {report.Textures.Length} texture files, "
            + $"{report.Textures.Count(texture => texture.Localized)} resolved in Data/English, {report.Missing.Length} missing "
            + $"({report.ImagesWithMissingTexture} images); {redefined} images are defined in more than one file (the later file updates them)");
        foreach (var missing in report.Missing) Console.WriteLine($"    missing texture: {missing.TextureFile} ({missing.Images} images)");

        var languages = LanguageCatalog.Discover(LanguageCatalog.ShippedLanguages, content.Exists);
        Console.WriteLine($"  languages: {string.Join(", ", languages.Select(value => $"{value.Language}({(value.HasStringTable ? "text" : "no text")}{(value.LanguageIni is null ? "" : ", ini")})"))}");

        var unexpected = unresolvedAll.Except(KnownUnresolvedWndLabels, StringComparer.Ordinal).ToArray();
        var fixedNow = KnownUnresolvedWndLabels.Except(unresolvedAll, StringComparer.Ordinal).ToArray();
        Assert(unexpected.Length == 0 && fixedNow.Length == 0,
            $"WND label baseline drifted; new: {string.Join(", ", unexpected)}; no longer unresolved: {string.Join(", ", fixedNow)}.");
        Assert(images.Count > 1000, "The retail graph has more than 1000 mapped images.");
        Assert(report.Missing.Select(texture => texture.TextureFile).SequenceEqual(KnownMissingTextures, StringComparer.Ordinal),
            $"Missing texture baseline drifted: {string.Join(", ", report.Missing.Select(texture => texture.TextureFile))}.");
    }

    /// <summary>The retail <c>InGameUI.ini</c> typed: the values the mission texts follow, and the fonts <c>Language.ini</c> replaces.</summary>
    private static void RealInGameUi(CompiledDefinitionGraph graph, GlobalLanguageSettings language)
    {
        var settings = InGameUiSettings.FromGraph(graph);
        Console.WriteLine($"  InGameUI.ini: message {settings.MessagePosition} delay {settings.MessageDelayMilliseconds} ms, military caption {settings.MilitaryCaptionPosition}, superweapons {settings.SuperweaponPosition}, "
            + $"named timers {settings.NamedTimerPosition}, {settings.RadiusCursorSections.Length} radius cursor blocks, defaulted: {string.Join(", ", settings.DefaultedKeys)}");
        Assert(settings.MessagePosition == new InGameUiPixelPoint(10, 22) && settings.MessageColor1 == new WndColor(255, 255, 255, 255) && settings.MessageColor2 == new WndColor(180, 180, 255, 255)
            && settings.MessageDelayMilliseconds == 75000, "The retail message values.");
        Assert(settings.MilitaryCaptionPosition == new InGameUiPixelPoint(10, 340) && settings.MilitaryCaptionColor == new WndColor(255, 255, 255, 255) && settings.MilitaryCaptionRandomizeTyping, "The retail military caption values.");
        Assert(settings.SuperweaponPosition == new InGameUiFractionPoint(0.90m, 0.01m) && settings.SuperweaponFlashFrames == 15m && settings.SuperweaponFlashColor == new WndColor(255, 255, 255, 255), "The retail superweapon values.");
        Assert(settings.NamedTimerPosition == new InGameUiFractionPoint(0.90m, 0.666m) && settings.NamedTimerFlashFrames == 15m && settings.NamedTimerNormalColor == new WndColor(255, 255, 255, 255), "The retail named timer values.");
        Assert(settings.RadiusCursorSections.Length == 29, $"The retail file has 29 radius cursor blocks ({settings.RadiusCursorSections.Length}).");
        Assert(settings.DefaultedKeys.SequenceEqual(["DrawableCaptionBold", "DrawableCaptionColor", "DrawableCaptionFont", "DrawableCaptionPointSize", "MilitaryCaptionSpeed"]), $"Only the drawable caption keys and the commented-out caption speed are left to the defaults ({string.Join(", ", settings.DefaultedKeys)}).");

        // InGameUI::init replaces each font of the block by the Language.ini slot: size and weight are the same in the retail data; the name differs only where the INI writes the
        // unquoted "Courier New" (parseAsciiString keeps "Courier") and Language.ini has it quoted.
        (string Slot, InGameUiFont Ini)[] fonts =
        [
            ("MessageFont", settings.MessageFont), ("MilitaryCaptionTitleFont", settings.MilitaryCaptionTitleFont), ("MilitaryCaptionFont", settings.MilitaryCaptionFont),
            ("SuperweaponCountdownNormalFont", settings.SuperweaponNormalFont), ("SuperweaponCountdownReadyFont", settings.SuperweaponReadyFont),
            ("NamedTimerCountdownNormalFont", settings.NamedTimerNormalFont), ("NamedTimerCountdownReadyFont", settings.NamedTimerReadyFont),
        ];
        foreach (var (slot, ini) in fonts)
        {
            var replaced = language.Font(InGameUiSettings.LanguageSlots[slot]);
            Assert(replaced.Size == ini.PointSize && replaced.Bold == ini.Bold, $"{slot}: Language.ini {replaced} and InGameUI.ini {ini} agree in size and weight.");
            Assert(replaced.Name == ini.Name || (replaced.Name == "Courier New" && ini.Name == "Courier"), $"{slot}: the font name '{replaced.Name}' agrees with the INI's '{ini.Name}'.");
        }
        Assert(language.MilitaryCaptionSpeed == 1 && language.MilitaryCaptionDelayMilliseconds == 750, "The typing speed and the start delay come from Language.ini.");
    }
    /// <summary>
    /// WND text entries of the menu screens that do not resolve in the retail <c>generals.csf</c>. Placeholders without a
    /// label prefix are overwritten by the menu code at run time (score values, player names, version); the prefixed ones
    /// are genuinely absent from the retail CSF, where the original shows <c>MISSING: '...'</c> unless code replaces them.
    /// </summary>
    private static readonly string[] KnownUnresolvedWndLabels =
    [
        "ControlBar.wnd|[None]",
        "ChallengeLoadScreen.wnd|GUI:BioBirthplaceEntry", "ChallengeLoadScreen.wnd|GUI:BioNameEntry", "ChallengeLoadScreen.wnd|GUI:BioStrategyEntry",
        "ChallengeLoadScreen.wnd|Static Text", "ChallengeLoadScreen.wnd|Tooltip:LoadProgress",
        "GameSpyLoadScreen.wnd|Static Text", "GameSpyLoadScreen.wnd|Tooltip:LoadProgress",
        "MultiplayerLoadScreen.wnd|Static Text",
        "OptionsMenu.wnd|GUI:LanguageFilter", "OptionsMenu.wnd|Version",
        "ScoreScreen.wnd|##", "ScoreScreen.wnd|$$$", "ScoreScreen.wnd|Buidings Built", "ScoreScreen.wnd|Buildings Killed",
        "ScoreScreen.wnd|Buildings Lost", "ScoreScreen.wnd|PlayerName", "ScoreScreen.wnd|Static Text", "ScoreScreen.wnd|Units Built",
        "ScoreScreen.wnd|Units Destroyed", "ScoreScreen.wnd|Units Lost",
        "ShellGameLoadScreen.wnd|Tooltip:LoadProgress",
        "SinglePlayerLoadScreen.wnd|Static Text", "SinglePlayerLoadScreen.wnd|Tooltip:LoadProgress",
        "SkirmishGameOptionsMenu.wnd|Static Text",
    ];

    /// <summary>Texture pages named by retail mapped images that no retail archive ships (checked against the original archives).</summary>
    private static readonly string[] KnownMissingTextures =
    [
        "China2_BackgroundSM.tga", "China2_Frame.tga", "China2_FrameSM.tga", "COB2_BackgroundSM.tga", "COB2_FrameSM.tga",
        "GeneralsChallengeMapInterface.tga", "GLA2_BackgroundSM.tga", "GLA2_Frame.tga", "GLA2_FrameSM.tga",
        "MainMenuBlackBorder.tga", "Skirmish_Load.tga", "USAMissionDemoLoadScreen.tga",
    ];

    /// <summary>
    /// Distinct <c>TEXT</c>/<c>TOOLTIPTEXT</c> labels of a WND file that the game looks up with <c>TheGameText->fetch</c>
    /// and shows (<c>GameWindowManagerScript.cpp setWindowText</c>): entry fields show their label literally, so their
    /// <c>TEXT</c> is not a label. The window type is the last <c>WINDOWTYPE</c> before the entry.
    /// </summary>
    private static List<string> WndTextLabels(string wnd)
    {
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        var windowType = string.Empty;
        foreach (var raw in wnd.Split('\n'))
        {
            var line = raw.Trim();
            var type = Regex.Match(line, @"^WINDOWTYPE\s*=\s*([A-Z]+)");
            if (type.Success) { windowType = type.Groups[1].Value; continue; }
            var match = Regex.Match(line, "^(TEXT|TOOLTIPTEXT)\\s*=\\s*\"([^\"]*)\"");
            if (!match.Success || match.Groups[2].Value.Length == 0) continue;
            if (match.Groups[1].Value == "TEXT" && windowType == "ENTRYFIELD") continue;
            labels.Add(match.Groups[2].Value);
        }
        return [..labels];
    }

    private static object SampleArgument(string spec) => spec[^1] switch
    {
        'd' or 'i' => 1,
        'u' or 'x' or 'X' => 1u,
        'c' => 'c',
        's' or 'S' => "s",
        'f' or 'F' => 1.5,
        _ => throw new InvalidOperationException($"The real CSF uses the conversion '{spec}', which the formatter does not support."),
    };

    // ------------------------------------------------------------------------------------------------------ helpers

    private static CompiledDefinitionGraph Compile(Dictionary<string, string> files)
        => DefinitionCompiler.Compile(new MemoryDefinitionSource(files));

    private static byte[] Patch(byte[] data, int offset, int value)
    {
        var copy = (byte[])data.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }

    private static string Scratch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}

/// <summary>Builds synthetic CSF fixtures (the inverse of <see cref="CsfReader"/>); test-only.</summary>
internal static class CsfTestWriter
{
    public static byte[] Write(int version, CsfLanguage language, IReadOnlyList<(string Label, (string Text, string? Speech)[] Values)> labels)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(CsfFile.FileId);
        writer.Write(version);
        writer.Write(labels.Count);
        writer.Write(labels.Sum(label => label.Values.Length));
        writer.Write(0);
        writer.Write((int)language);
        foreach (var (label, values) in labels)
        {
            writer.Write(CsfFile.LabelId);
            writer.Write(values.Length);
            writer.Write(label.Length);
            writer.Write(Encoding.Latin1.GetBytes(label));
            foreach (var (text, speech) in values)
            {
                writer.Write(speech is null ? CsfFile.StringId : CsfFile.StringWithSpeechId);
                writer.Write(text.Length);
                foreach (var ch in text) writer.Write((ushort)~ch);
                if (speech is null) continue;
                writer.Write(speech.Length);
                writer.Write(Encoding.Latin1.GetBytes(speech));
            }
        }
        writer.Flush();
        return buffer.ToArray();
    }
}

file sealed class MemoryDefinitionSource(IReadOnlyDictionary<string, string> files) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => files.Keys;
    public Stream OpenRead(string virtualPath) => new MemoryStream(Encoding.Latin1.GetBytes(files[virtualPath]));
    public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
}

file sealed class ContentDefinitionSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
