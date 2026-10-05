using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Text;

/// <summary>
/// The tooltip keys of the <c>Mouse</c> block of <c>Data/INI/Mouse.ini</c>, typed after <c>TheMouseFieldParseTable</c> of the retail source
/// (Mouse.cpp). The other keys of the block (camera, drag tolerance, the alternate player colors that only object tooltips use) belong to
/// other consumers and are not read here; the font comes from <c>Language.ini</c> (<c>TooltipFontName</c>, <see cref="GlobalLanguageSettings"/>)
/// as in <c>Mouse::mouseNotifyResolutionChange</c>, the font of this block is only its fallback.
/// </summary>
public sealed record MouseTooltipSettings(
    FontDescription Font,
    bool AnimateBackground,
    int FillTimeMilliseconds,
    int DelayMilliseconds,
    WndColor TextColor,
    WndColor HighlightColor,
    WndColor ShadowColor,
    WndColor BorderColor,
    WndColor BackgroundColor,
    decimal WidthFraction)
{
    public static readonly DefinitionId DefinitionId = new(DefinitionKind.Compatibility, "mouse/mouse");

    private static readonly string[] Keys =
    [
        "TooltipFontName", "TooltipFontSize", "TooltipFontIsBold", "TooltipAnimateBackground", "TooltipFillTime", "TooltipDelayTime",
        "TooltipTextColor", "TooltipHighlightColor", "TooltipShadowColor", "TooltipBorderColor", "TooltipBackgroundColor", "TooltipWidth",
    ];

    public static MouseTooltipSettings FromGraph(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var definition = graph.Definitions.SingleOrDefault(value => value.Id == DefinitionId)
            ?? throw new InvalidDataException("The definition graph has no 'Mouse' block; Data/INI/Mouse.ini was not loaded.");
        return FromFields(definition.Fields);
    }

    public static MouseTooltipSettings FromFields(IEnumerable<DefinitionField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var values = new Dictionary<string, (string Text, string Where)>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
            if (Keys.FirstOrDefault(key => key.Equals(field.Name, StringComparison.OrdinalIgnoreCase)) is { } key)
                values[key] = (field.Value.OriginalText.Trim(), $"{field.Location.Source}({field.Location.Line})");

        (string Text, string Where) Get(string key)
            => values.TryGetValue(key, out var value) ? value : throw new FormatException($"The Mouse block has no {key} (Mouse.cpp TheMouseFieldParseTable).");

        string Single(string key)
        {
            var (text, where) = Get(key);
            var tokens = IniTokens.Split(text, where);
            return tokens.Length == 1 ? tokens[0] : throw new FormatException($"{where}: {key} takes exactly one value, got {tokens.Length}.");
        }

        int Integer(string key) => int.TryParse(Single(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : throw new FormatException($"{Get(key).Where}: {key} value '{Single(key)}' is not an integer.");

        bool Boolean(string key) => Single(key).ToUpperInvariant() switch
        {
            "YES" => true,
            "NO" => false,
            _ => throw new FormatException($"{Get(key).Where}: {key} value '{Single(key)}' must be Yes or No."),
        };

        WndColor Color(string key)
        {
            var (text, where) = Get(key);
            var parts = IniTokens.Split(text, where);
            if (parts.Length != 4) throw new FormatException($"{where}: {key} must be 'R:n G:n B:n A:n', got '{text}'.");
            var channels = new int[4];
            for (var i = 0; i < 4; i++)
            {
                var prefix = "RGBA"[i] + ":";
                if (!parts[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(parts[i][prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i])
                    || channels[i] is < 0 or > 255)
                    throw new FormatException($"{where}: {key} component '{parts[i]}' is not {prefix}0..255.");
            }
            return new WndColor((byte)channels[0], (byte)channels[1], (byte)channels[2], (byte)channels[3]);
        }

        // INI::parsePercentToReal: a plain number is a percentage, the stored value is the fraction.
        var widthText = Single("TooltipWidth");
        if (!decimal.TryParse(widthText.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            throw new FormatException($"{Get("TooltipWidth").Where}: TooltipWidth value '{widthText}' is not a number.");

        return new MouseTooltipSettings(new FontDescription(Single("TooltipFontName"), Integer("TooltipFontSize"), Boolean("TooltipFontIsBold")),
            Boolean("TooltipAnimateBackground"), Integer("TooltipFillTime"), Integer("TooltipDelayTime"), Color("TooltipTextColor"),
            Color("TooltipHighlightColor"), Color("TooltipShadowColor"), Color("TooltipBorderColor"), Color("TooltipBackgroundColor"), percent / 100m);
    }
}
