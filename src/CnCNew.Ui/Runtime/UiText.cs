using System.Collections.Immutable;

namespace CnCNew.Ui.Runtime;

/// <summary>A resolved font: the window or header template font name, the scaled point size and the bold flag.</summary>
public readonly record struct UiFont(string Name, int Size, bool Bold)
{
    public override string ToString() => $"{Name}/{Size}{(Bold ? "b" : "")}";
}

/// <summary>Text metrics supplied by the renderer (tests use a fake monospace font). Results must be deterministic.</summary>
public interface IUiTextMeasure
{
    int MeasureWidth(string text, UiFont font);
    int LineHeight(UiFont font);
}

/// <summary>Mapped image sizes supplied by the renderer (resolved through the mapped image table).</summary>
public interface IUiImageLookup
{
    bool TryGetSize(string imageName, out int width, out int height);
}

/// <summary>Localized strings for <c>TEXT</c>/<c>TOOLTIPTEXT</c> labels such as <c>GUI:Options</c>.</summary>
public interface IUiTextSource
{
    bool TryResolve(string label, out string text);
}

/// <summary>A window text: either a plain string or a label key that is resolved through <see cref="IUiTextSource"/> when drawn.</summary>
public sealed record UiTextValue(string Value, bool IsLabel)
{
    public static UiTextValue Label(string label) => new(label, true);
    public static UiTextValue Plain(string text) => new(text, false);
}

public static class UiTextLayout
{
    /// <summary>
    /// Greedy word wrap at blanks; explicit <c>\n</c> starts a new line. A word wider than <paramref name="maxWidth"/> keeps
    /// its own line (it is not broken inside). <paramref name="maxWidth"/> &lt;= 0 disables wrapping.
    /// </summary>
    public static ImmutableArray<string> Wrap(string text, int maxWidth, UiFont font, IUiTextMeasure measure)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (maxWidth <= 0)
            {
                lines.Add(paragraph);
                continue;
            }
            var current = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && measure.MeasureWidth(candidate, font) > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = candidate;
            }
            lines.Add(current);
        }
        return lines.ToImmutable();
    }
}
