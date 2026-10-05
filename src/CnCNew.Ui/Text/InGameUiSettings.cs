using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Text;

/// <summary>A pixel coordinate pair of the <c>InGameUI</c> block (<c>INI::parseICoord2D</c>: <c>X:10 Y:22</c>).</summary>
public readonly record struct InGameUiPixelPoint(int X, int Y);

/// <summary>A fractional screen position of the <c>InGameUI</c> block (<c>INI::parseCoord2D</c>: <c>X:0.90 Y:0.01</c>, shares of the display width and height).</summary>
public readonly record struct InGameUiFractionPoint(decimal X, decimal Y);

/// <summary>One font of the <c>InGameUI</c> block (<c>...Font</c>, <c>...PointSize</c>, <c>...Bold</c>); see <see cref="InGameUiSettings"/> for which of them a localized <c>Language.ini</c> overrides.</summary>
public sealed record InGameUiFont(string Name, int PointSize, bool Bold);

/// <summary>
/// The <c>InGameUI</c> block of <c>Data/INI/InGameUI.ini</c>, typed after <c>InGameUI::s_fieldParseTable</c> and the defaults of the <c>InGameUI</c> constructor of the retail source
/// (InGameUI.cpp). It reaches the client as a compatibility definition of the <see cref="CompiledDefinitionGraph"/>; unknown keys, malformed values and unknown sub-blocks are errors.
/// Keys the file does not set keep the constructor default and are listed in <see cref="DefaultedKeys"/>. The radius cursor sub-blocks (<c>...RadiusCursor</c>, decals of the
/// object layer) belong to another consumer and are only listed in <see cref="RadiusCursorSections"/>.
/// <para>
/// <b>Fonts.</b> <c>InGameUI::init</c> replaces every font it has a slot for in <c>Language.ini</c> (message, military caption title and body, superweapon and named timer, drawable caption)
/// by that slot's name, size and weight - for any <c>Language.ini</c>, because <c>FontDesc</c>'s default name is never empty. The fonts here are therefore what the <c>InGameUI.ini</c>
/// says, and the ones a screen draws with are <see cref="GlobalLanguageSettings.Font(string)"/> of the slot named by <see cref="LanguageSlots"/>; a test pins that the two agree in size and weight in the retail data.
/// </para>
/// </summary>
public sealed record InGameUiSettings(
    int MaxSelectionSize,
    WndColor MessageColor1,
    WndColor MessageColor2,
    InGameUiPixelPoint MessagePosition,
    InGameUiFont MessageFont,
    int MessageDelayMilliseconds,
    WndColor MilitaryCaptionColor,
    InGameUiPixelPoint MilitaryCaptionPosition,
    InGameUiFont MilitaryCaptionTitleFont,
    InGameUiFont MilitaryCaptionFont,
    bool MilitaryCaptionRandomizeTyping,
    int MilitaryCaptionSpeed,
    InGameUiFractionPoint SuperweaponPosition,
    decimal SuperweaponFlashFrames,
    WndColor SuperweaponFlashColor,
    InGameUiFont SuperweaponNormalFont,
    InGameUiFont SuperweaponReadyFont,
    InGameUiFractionPoint NamedTimerPosition,
    decimal NamedTimerFlashFrames,
    WndColor NamedTimerFlashColor,
    InGameUiFont NamedTimerNormalFont,
    WndColor NamedTimerNormalColor,
    InGameUiFont NamedTimerReadyFont,
    WndColor NamedTimerReadyColor,
    int FloatingTextTimeOutFrames,
    decimal FloatingTextMoveUpPerFrame,
    decimal FloatingTextVanishPerFrame,
    WndColor PopupMessageColor,
    InGameUiFont DrawableCaptionFont,
    WndColor DrawableCaptionColor,
    bool DrawRmbScrollAnchor,
    bool MoveRmbScrollAnchor,
    ImmutableArray<string> RadiusCursorSections,
    ImmutableArray<string> DefaultedKeys)
{
    public static readonly DefinitionId DefinitionId = new(DefinitionKind.Compatibility, "ingameui/ingameui");

    /// <summary>The logic frames per second the duration and velocity keys convert with (<c>LOGICFRAMES_PER_SECOND</c>).</summary>
    public const int LogicFramesPerSecond = 30;

    /// <summary>Which <c>Language.ini</c> font slot replaces which font of this block (<c>InGameUI::init</c>).</summary>
    public static ImmutableSortedDictionary<string, string> LanguageSlots { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MessageFont"] = "MessageFont",
        ["MilitaryCaptionTitleFont"] = "MilitaryCaptionTitleFont",
        ["MilitaryCaptionFont"] = "MilitaryCaptionFont",
        ["SuperweaponCountdownNormalFont"] = "SuperweaponCountdownNormalFont",
        ["SuperweaponCountdownReadyFont"] = "SuperweaponCountdownReadyFont",
        ["NamedTimerCountdownNormalFont"] = "NamedTimerCountdownNormalFont",
        ["NamedTimerCountdownReadyFont"] = "NamedTimerCountdownReadyFont",
        ["DrawableCaptionFont"] = "DrawableCaptionFont",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private static readonly ImmutableArray<string> Keys =
    [
        "MaxSelectionSize", "MessageColor1", "MessageColor2", "MessagePosition", "MessageFont", "MessagePointSize", "MessageBold", "MessageDelayMS",
        "MilitaryCaptionColor", "MilitaryCaptionPosition", "MilitaryCaptionTitleFont", "MilitaryCaptionTitlePointSize", "MilitaryCaptionTitleBold",
        "MilitaryCaptionFont", "MilitaryCaptionPointSize", "MilitaryCaptionBold", "MilitaryCaptionRandomizeTyping", "MilitaryCaptionSpeed",
        "SuperweaponCountdownPosition", "SuperweaponCountdownFlashDuration", "SuperweaponCountdownFlashColor",
        "SuperweaponCountdownNormalFont", "SuperweaponCountdownNormalPointSize", "SuperweaponCountdownNormalBold",
        "SuperweaponCountdownReadyFont", "SuperweaponCountdownReadyPointSize", "SuperweaponCountdownReadyBold",
        "NamedTimerCountdownPosition", "NamedTimerCountdownFlashDuration", "NamedTimerCountdownFlashColor",
        "NamedTimerCountdownNormalFont", "NamedTimerCountdownNormalPointSize", "NamedTimerCountdownNormalBold", "NamedTimerCountdownNormalColor",
        "NamedTimerCountdownReadyFont", "NamedTimerCountdownReadyPointSize", "NamedTimerCountdownReadyBold", "NamedTimerCountdownReadyColor",
        "FloatingTextTimeOut", "FloatingTextMoveUpSpeed", "FloatingTextVanishRate", "PopupMessageColor",
        "DrawableCaptionFont", "DrawableCaptionPointSize", "DrawableCaptionBold", "DrawableCaptionColor",
        "DrawRMBScrollAnchor", "MoveRMBScrollAnchor",
    ];

    public static InGameUiSettings FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var definition = graph.Definitions.SingleOrDefault(value => value.Id == DefinitionId)
            ?? throw new InvalidDataException("The definition graph has no 'InGameUI' block; Data/INI/InGameUI.ini was not loaded.");
        return FromDefinition(definition.Fields, definition.Sections);
    }

    public static InGameUiSettings FromDefinition(IEnumerable<DefinitionField> fields, IEnumerable<DefinitionSection> sections)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(sections);
        var values = new Dictionary<string, (string Text, string Where)>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var key = Keys.FirstOrDefault(name => name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"{field.Location.Source}({field.Location.Line}): '{field.Name}' is not a key of the InGameUI block (InGameUI.cpp s_fieldParseTable).");
            values[key] = (field.Value.OriginalText.Trim(), $"{field.Location.Source}({field.Location.Line})");
        }
        var cursors = ImmutableArray.CreateBuilder<string>();
        foreach (var section in sections)
        {
            var name = section.Declaration.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (!name.EndsWith("RadiusCursor", StringComparison.Ordinal))
                throw new FormatException($"{section.Location.Source}({section.Location.Line}): '{section.Declaration}' is not a sub-block of the InGameUI block (only the ...RadiusCursor decals are).");
            cursors.Add(name);
        }
        cursors.Sort(StringComparer.Ordinal);
        var defaulted = Keys.Where(key => !values.ContainsKey(key)).OrderBy(key => key, StringComparer.Ordinal).ToImmutableArray();

        string[] Tokens(string key)
        {
            var (text, where) = values[key];
            return IniTokens.Split(text, where);
        }

        string Single(string key)
        {
            var tokens = Tokens(key);
            return tokens.Length == 1 ? tokens[0] : throw new FormatException($"{values[key].Where}: {key} takes exactly one value, got {tokens.Length}.");
        }

        int Integer(string key, int fallback)
            => !values.ContainsKey(key) ? fallback
                : int.TryParse(Single(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value
                : throw new FormatException($"{values[key].Where}: {key} value '{Single(key)}' is not an integer.");

        decimal Real(string key, decimal fallback)
            => !values.ContainsKey(key) ? fallback
                : decimal.TryParse(Single(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value
                : throw new FormatException($"{values[key].Where}: {key} value '{Single(key)}' is not a real number.");

        bool Boolean(string key, bool fallback)
        {
            if (!values.ContainsKey(key)) return fallback;
            var token = Single(key);
            if (token.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (token.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
            throw new FormatException($"{values[key].Where}: {key} value '{token}' must be Yes or No.");
        }

        // INI::parseColorInt: R:n G:n B:n with an optional A:n (255 when omitted).
        WndColor Color(string key, WndColor fallback)
        {
            if (!values.ContainsKey(key)) return fallback;
            var tokens = Tokens(key);
            var where = values[key].Where;
            if (tokens.Length is not (3 or 4)) throw new FormatException($"{where}: {key} must be 'R:n G:n B:n [A:n]', got '{values[key].Text}'.");
            var channels = new byte[] { 0, 0, 0, 255 };
            for (var index = 0; index < tokens.Length; index++)
            {
                var prefix = "RGBA"[index] + ":";
                if (!tokens[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(tokens[index][prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) || channel is < 0 or > 255)
                    throw new FormatException($"{where}: {key} component '{tokens[index]}' is not {prefix}0..255.");
                channels[index] = (byte)channel;
            }
            return new WndColor(channels[0], channels[1], channels[2], channels[3]);
        }

        (string X, string Y) Pair(string key)
        {
            var tokens = Tokens(key);
            if (tokens.Length != 2 || !tokens[0].StartsWith("X:", StringComparison.OrdinalIgnoreCase) || !tokens[1].StartsWith("Y:", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"{values[key].Where}: {key} must be 'X:n Y:n', got '{values[key].Text}'.");
            return (tokens[0][2..], tokens[1][2..]);
        }

        InGameUiPixelPoint Pixels(string key, InGameUiPixelPoint fallback)
        {
            if (!values.ContainsKey(key)) return fallback;
            var (x, y) = Pair(key);
            if (!int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) || !int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var py))
                throw new FormatException($"{values[key].Where}: {key} '{values[key].Text}' is not a pair of integers.");
            return new InGameUiPixelPoint(px, py);
        }

        InGameUiFractionPoint Fractions(string key, InGameUiFractionPoint fallback)
        {
            if (!values.ContainsKey(key)) return fallback;
            var (x, y) = Pair(key);
            if (!decimal.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var fx) || !decimal.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var fy))
                throw new FormatException($"{values[key].Where}: {key} '{values[key].Text}' is not a pair of numbers.");
            return new InGameUiFractionPoint(fx, fy);
        }

        // INI::parseAsciiString takes the first token (or one quoted string) and ignores the rest of the line: "Courier New" unquoted is the font "Courier" (Language.ini replaces it anyway).
        string FontName(string key, string fallback)
        {
            if (!values.ContainsKey(key)) return fallback;
            var tokens = Tokens(key);
            return tokens.Length > 0 ? tokens[0] : throw new FormatException($"{values[key].Where}: {key} has no font name.");
        }

        InGameUiFont Font(string key, string pointSizeKey, string boldKey, InGameUiFont fallback)
            => new(FontName(key, fallback.Name), Integer(pointSizeKey, fallback.PointSize), Boolean(boldKey, fallback.Bold));

        // INI::parseDurationReal: milliseconds become (fractional) logic frames.
        decimal Frames(string key, decimal fallbackFrames)
            => values.ContainsKey(key) ? Real(key, 0m) * LogicFramesPerSecond / 1000m : fallbackFrames;

        // INI::parseDurationUnsignedInt: milliseconds become whole logic frames, rounded up.
        int WholeFrames(string key, int fallbackFrames)
            => values.ContainsKey(key) ? (int)Math.Ceiling(Real(key, 0m) * LogicFramesPerSecond / 1000m) : fallbackFrames;

        // INI::parseVelocityReal: units per second become units per logic frame.
        decimal PerFrame(string key, decimal fallback)
            => values.ContainsKey(key) ? Real(key, 0m) / LogicFramesPerSecond : fallback;

        // The defaults are the constructor of InGameUI (InGameUI.cpp); the retail file sets every key but the drawable caption ones.
        return new InGameUiSettings(
            Integer("MaxSelectionSize", -1),
            Color("MessageColor1", new WndColor(255, 255, 255, 255)),
            Color("MessageColor2", new WndColor(180, 180, 180, 255)),
            Pixels("MessagePosition", new InGameUiPixelPoint(10, 10)),
            Font("MessageFont", "MessagePointSize", "MessageBold", new InGameUiFont("Arial", 10, false)),
            Integer("MessageDelayMS", 5000),
            Color("MilitaryCaptionColor", new WndColor(200, 200, 30, 255)),
            Pixels("MilitaryCaptionPosition", new InGameUiPixelPoint(10, 380)),
            Font("MilitaryCaptionTitleFont", "MilitaryCaptionTitlePointSize", "MilitaryCaptionTitleBold", new InGameUiFont("Courier", 12, true)),
            Font("MilitaryCaptionFont", "MilitaryCaptionPointSize", "MilitaryCaptionBold", new InGameUiFont("Courier", 12, false)),
            Boolean("MilitaryCaptionRandomizeTyping", false),
            Integer("MilitaryCaptionSpeed", 1),
            Fractions("SuperweaponCountdownPosition", new InGameUiFractionPoint(0.7m, 0.7m)),
            Frames("SuperweaponCountdownFlashDuration", 1m),
            Color("SuperweaponCountdownFlashColor", new WndColor(255, 255, 255, 255)),
            Font("SuperweaponCountdownNormalFont", "SuperweaponCountdownNormalPointSize", "SuperweaponCountdownNormalBold", new InGameUiFont("Arial", 10, false)),
            Font("SuperweaponCountdownReadyFont", "SuperweaponCountdownReadyPointSize", "SuperweaponCountdownReadyBold", new InGameUiFont("Arial", 10, false)),
            Fractions("NamedTimerCountdownPosition", new InGameUiFractionPoint(0.05m, 0.7m)),
            Frames("NamedTimerCountdownFlashDuration", 1m),
            Color("NamedTimerCountdownFlashColor", new WndColor(0, 255, 255, 255)),
            Font("NamedTimerCountdownNormalFont", "NamedTimerCountdownNormalPointSize", "NamedTimerCountdownNormalBold", new InGameUiFont("Arial", 10, false)),
            Color("NamedTimerCountdownNormalColor", new WndColor(255, 255, 0, 255)),
            Font("NamedTimerCountdownReadyFont", "NamedTimerCountdownReadyPointSize", "NamedTimerCountdownReadyBold", new InGameUiFont("Arial", 10, false)),
            Color("NamedTimerCountdownReadyColor", new WndColor(255, 0, 255, 255)),
            WholeFrames("FloatingTextTimeOut", LogicFramesPerSecond / 3),
            PerFrame("FloatingTextMoveUpSpeed", 1m),
            PerFrame("FloatingTextVanishRate", 0.1m),
            Color("PopupMessageColor", new WndColor(255, 255, 255, 255)),
            Font("DrawableCaptionFont", "DrawableCaptionPointSize", "DrawableCaptionBold", new InGameUiFont("Arial", 10, false)),
            Color("DrawableCaptionColor", new WndColor(255, 255, 255, 255)),
            Boolean("DrawRMBScrollAnchor", false),
            Boolean("MoveRMBScrollAnchor", false),
            cursors.ToImmutable(), defaulted);
    }
}
