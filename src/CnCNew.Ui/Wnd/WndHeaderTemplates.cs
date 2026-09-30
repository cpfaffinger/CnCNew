using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Ui.Wnd;

/// <summary>One <c>HeaderTemplate</c> of <c>Data/&lt;language&gt;/HeaderTemplate.ini</c> (<c>HeaderTemplate.cpp</c>).</summary>
public sealed record WndHeaderTemplate(string Name, string FontName, int Point, bool Bold);

/// <summary>
/// The header template table. A window whose <c>HEADERTEMPLATE</c> names a known template draws with the template
/// font instead of its own <c>FONT</c> (<c>parseWindow</c>, <c>getFontFromTemplate</c>); unknown names keep the window font.
/// Template lookup is case-sensitive like <c>HeaderTemplateManager::findHeaderTemplate</c>.
/// </summary>
public sealed record WndHeaderTemplateTable(ImmutableSortedDictionary<string, WndHeaderTemplate> Templates, ImmutableArray<WndDiagnostic> Diagnostics)
{
    public static readonly WndHeaderTemplateTable Empty = new(ImmutableSortedDictionary<string, WndHeaderTemplate>.Empty.WithComparers(StringComparer.Ordinal), []);

    public WndHeaderTemplate? Find(string? name) => name is not null && Templates.TryGetValue(name, out var value) ? value : null;

    public static WndHeaderTemplateTable Parse(string text, string fileName)
    {
        var templates = new SortedDictionary<string, WndHeaderTemplate>(StringComparer.Ordinal);
        var diagnostics = new List<WndDiagnostic>();
        void Report(string code, WndDiagnosticSeverity severity, int line, string message)
            => diagnostics.Add(new WndDiagnostic(code, severity, fileName, line, message));

        string? name = null;
        string? font = null;
        int? point = null;
        bool? bold = null;
        var blockLine = 0;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;
            if (name is null)
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Equals("HeaderTemplate", StringComparison.OrdinalIgnoreCase))
                {
                    name = parts[1];
                    blockLine = lineNumber;
                    font = null;
                    point = null;
                    bold = null;
                }
                else Report("unexpected-line", WndDiagnosticSeverity.Error, lineNumber, $"Expected 'HeaderTemplate <name>', found '{line}'.");
                continue;
            }
            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (font is null || point is null || bold is null)
                    Report("incomplete-template", WndDiagnosticSeverity.Error, blockLine,
                        $"HeaderTemplate {name} lacks {(font is null ? "Font" : point is null ? "Point" : "Bold")}.");
                else
                {
                    if (templates.ContainsKey(name))
                        Report("duplicate-template", WndDiagnosticSeverity.Unknown, blockLine,
                            $"HeaderTemplate {name} is defined twice; the original re-parses into the existing entry.");
                    templates[name] = new WndHeaderTemplate(name, font, point.Value, bold.Value);
                }
                name = null;
                continue;
            }
            var equals = line.IndexOf('=');
            var key = equals < 0 ? line : line[..equals].Trim();
            var value = equals < 0 ? string.Empty : line[(equals + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "font" when value.Length >= 2 && value[0] == '"' && value[^1] == '"':
                    font = value[1..^1];
                    break;
                case "point" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size):
                    point = size;
                    break;
                case "bold" when value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("No", StringComparison.OrdinalIgnoreCase):
                    bold = value.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                    break;
                case "font" or "point" or "bold":
                    Report("malformed-value", WndDiagnosticSeverity.Error, lineNumber, $"Invalid value '{value}' for {key}.");
                    break;
                default:
                    Report("unknown-key", WndDiagnosticSeverity.Unknown, lineNumber, $"Unknown HeaderTemplate key '{key}'.");
                    break;
            }
        }
        if (name is not null)
            Report("unterminated-template", WndDiagnosticSeverity.Error, blockLine, $"HeaderTemplate {name} is missing End.");
        return new WndHeaderTemplateTable(templates.ToImmutableSortedDictionary(StringComparer.Ordinal), [..diagnostics]);
    }
}
