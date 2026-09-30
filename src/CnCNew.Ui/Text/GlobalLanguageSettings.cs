using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;

namespace CnCNew.Ui.Text;

public sealed record FontDescription(string Name, int Size, bool Bold)
{
    /// <summary><c>FontDesc::FontDesc</c> (GlobalLanguage.cpp): the value of a font slot that Language.ini does not set.</summary>
    public static FontDescription Default { get; } = new("Arial Unicode MS", 12, false);
}

/// <summary>
/// The <c>Language</c> block of <c>Data/&lt;Language&gt;/Language.ini</c>, typed after
/// <c>TheGlobalLanguageDataFieldParseTable</c> of the retail source (GlobalLanguage.cpp). The block reaches the UI as a
/// compatibility definition of the <see cref="CompiledDefinitionGraph"/> (the load plan loads the selected
/// language's file); unknown keys and malformed values are errors. Slots and keys that the file does not set keep the
/// constructor defaults of <c>GlobalLanguage</c>/<c>FontDesc</c> and are listed in <see cref="DefaultedKeys"/>.
/// </summary>
public sealed record GlobalLanguageSettings(
    string UnicodeFontName,
    ImmutableArray<string> LocalFontFiles,
    int MilitaryCaptionSpeed,
    bool UseHardWordWrap,
    decimal ResolutionFontAdjustment,
    int MilitaryCaptionDelayMilliseconds,
    ImmutableSortedDictionary<string, FontDescription> Fonts,
    ImmutableArray<string> DefaultedKeys)
{
    public static readonly DefinitionId DefinitionId = new(DefinitionKind.Compatibility, "language/language");

    /// <summary>Font slots of the retail parse table, in table order.</summary>
    public static ImmutableArray<string> FontSlots { get; } =
    [
        "CopyrightFont", "MessageFont", "MilitaryCaptionTitleFont", "MilitaryCaptionFont",
        "SuperweaponCountdownNormalFont", "SuperweaponCountdownReadyFont", "NamedTimerCountdownNormalFont",
        "NamedTimerCountdownReadyFont", "DrawableCaptionFont", "DefaultWindowFont", "DefaultDisplayStringFont",
        "TooltipFontName", "NativeDebugDisplay", "DrawGroupInfoFont", "CreditsTitleFont", "CreditsMinorTitleFont",
        "CreditsNormalFont",
    ];

    private static readonly ImmutableArray<string> ScalarKeys =
        ["UnicodeFontName", "LocalFontFile", "MilitaryCaptionSpeed", "UseHardWordWrap", "ResolutionFontAdjustment", "MilitaryCaptionDelayMS"];

    public FontDescription Font(string slot)
    {
        if (!FontSlots.Contains(slot, StringComparer.Ordinal))
            throw new ArgumentException($"'{slot}' is not a font slot of Language.ini.", nameof(slot));
        return Fonts.TryGetValue(slot, out var font) ? font : FontDescription.Default;
    }

    /// <summary>
    /// <c>GlobalLanguage::adjustFontSize</c> of the retail source: the factor <c>1 + (width / 800 - 1) * adjustment</c>
    /// is clamped to [1, 2] and the scaled point size is floored.
    /// </summary>
    public int AdjustFontSize(int pointSize, int displayWidth)
    {
        if (displayWidth <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth), displayWidth, "Display width must be positive.");
        var factor = 1m + (displayWidth / 800m - 1m) * ResolutionFontAdjustment;
        factor = Math.Clamp(factor, 1m, 2m);
        return (int)Math.Floor(pointSize * factor);
    }

    public static GlobalLanguageSettings FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var definition = graph.Definitions.SingleOrDefault(value => value.Id == DefinitionId)
            ?? throw new InvalidDataException("The definition graph has no 'Language' block; Data/<Language>/Language.ini was not loaded.");
        // The compiler keeps the last value of a repeated scalar key, but LocalFontFile accumulates in the original.
        if (graph.Audit.Compatibility.Any(entry => entry.Category == "duplicate-key" && entry.Name.Equals("Language/LocalFontFile", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Language.ini repeats LocalFontFile; the compiled graph keeps only the last one, the original loads all of them.");
        return FromFields(definition.Fields);
    }

    public static GlobalLanguageSettings FromFields(IEnumerable<DefinitionField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var unicodeFont = FontDescription.Default.Name;
        var localFonts = ImmutableArray.CreateBuilder<string>();
        var captionSpeed = 0;
        var hardWrap = false;
        var adjustment = 0.7m;
        var captionDelay = 750;
        var fonts = ImmutableSortedDictionary.CreateBuilder<string, FontDescription>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var text = field.Value.OriginalText.Trim();
            var where = $"{field.Location.Source}({field.Location.Line})";
            var slot = FontSlots.FirstOrDefault(name => name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
            if (slot is not null)
            {
                fonts[slot] = ParseFont(text, where, slot);
                seen.Add(slot);
                continue;
            }
            var key = ScalarKeys.FirstOrDefault(name => name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"{where}: '{field.Name}' is not a key of the Language block (GlobalLanguage.cpp).");
            seen.Add(key);
            var tokens = IniTokens.Split(text, where);
            switch (key)
            {
                case "UnicodeFontName": unicodeFont = Single(tokens, where, key); break;
                case "LocalFontFile": localFonts.Insert(0, Single(tokens, where, key)); break;
                case "MilitaryCaptionSpeed": captionSpeed = Integer(Single(tokens, where, key), where, key); break;
                case "MilitaryCaptionDelayMS": captionDelay = Integer(Single(tokens, where, key), where, key); break;
                case "UseHardWordWrap": hardWrap = Boolean(Single(tokens, where, key), where, key); break;
                case "ResolutionFontAdjustment":
                    var value = Single(tokens, where, key);
                    if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out adjustment))
                        throw new FormatException($"{where}: {key} = '{value}' is not a real number.");
                    break;
            }
        }
        var defaulted = ScalarKeys.Concat(FontSlots).Where(name => !seen.Contains(name)).OrderBy(name => name, StringComparer.Ordinal);
        return new GlobalLanguageSettings(unicodeFont, localFonts.ToImmutable(), captionSpeed, hardWrap, adjustment, captionDelay,
            fonts.ToImmutable(), [..defaulted]);
    }

    /// <summary><c>GlobalLanguage::parseFontDesc</c>: optionally quoted name, integer point size, Yes/No bold flag.</summary>
    private static FontDescription ParseFont(string text, string where, string slot)
    {
        var tokens = IniTokens.Split(text, where);
        if (tokens.Length != 3)
            throw new FormatException($"{where}: {slot} = '{text}' must be '<name> <size> <Yes|No>'.");
        return new FontDescription(tokens[0], Integer(tokens[1], where, slot), Boolean(tokens[2], where, slot));
    }

    private static string Single(string[] tokens, string where, string key)
        => tokens.Length == 1 ? tokens[0] : throw new FormatException($"{where}: {key} takes exactly one value, got {tokens.Length}.");

    private static int Integer(string token, string where, string key)
        => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{where}: {key} value '{token}' is not an integer.");

    private static bool Boolean(string token, string where, string key)
    {
        if (token.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (token.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        throw new FormatException($"{where}: {key} value '{token}' must be Yes or No.");
    }
}

/// <summary>INI value tokens with the quoting of <c>INI::getNextQuotedAsciiString</c>/<c>getNextAsciiString</c>.</summary>
internal static class IniTokens
{
    public static string[] Split(string text, string where)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] is ' ' or '\t' or '=') { index++; continue; }
            if (text[index] == '"')
            {
                var end = text.IndexOf('"', index + 1);
                if (end < 0) throw new FormatException($"{where}: unterminated quote in '{text}'.");
                tokens.Add(text[(index + 1)..end]);
                index = end + 1;
                continue;
            }
            var start = index;
            while (index < text.Length && text[index] is not (' ' or '\t' or '=')) index++;
            tokens.Add(text[start..index]);
        }
        return [..tokens];
    }
}
