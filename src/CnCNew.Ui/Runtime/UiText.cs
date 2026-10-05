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

    /// <summary>The font's pixel overlap added to the width of a text block (<c>Render2DSentenceClass::Get_Formatted_Text_Extents</c>); 0 when a measure has no such notion.</summary>
    int ExtraOverlap(UiFont font) => 0;
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
