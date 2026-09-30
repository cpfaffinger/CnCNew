using System.Collections.Immutable;
using System.Text;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Runtime;

/// <summary>A renderer-neutral draw command in screen pixels; commands are drawn in list order (later on top).</summary>
public abstract record UiDrawCommand;

public sealed record UiFillRect(UiRect Rect, WndColor Color) : UiDrawCommand
{
    public override string ToString() => $"fill {Rect} {Color}";
}

/// <summary>A one-pixel outline along the inside of <see cref="Rect"/> (<c>winOpenRect</c>).</summary>
public sealed record UiBorderRect(UiRect Rect, WndColor Color) : UiDrawCommand
{
    public override string ToString() => $"border {Rect} {Color}";
}

/// <summary>A mapped image by name, stretched to <see cref="Destination"/>; the renderer resolves the name through the mapped image table.</summary>
public sealed record UiImageDraw(string Image, UiRect Destination, WndColor Tint, bool FlipHorizontal, bool FlipVertical) : UiDrawCommand
{
    public override string ToString()
        => $"image {Image} {Destination} tint {Tint}{(FlipHorizontal ? " flip-h" : "")}{(FlipVertical ? " flip-v" : "")}";
}

public enum UiTextAlignment
{
    Left,
    Center,
}

/// <summary>
/// One laid-out text line at (<see cref="X"/>, <see cref="Y"/>) (top-left of the line box). <see cref="LabelKey"/> is the
/// source label (null for plain text); when the label was not resolved, <see cref="Text"/> is the label itself.
/// <see cref="ShadowColor"/> is the border/drop color of <c>TEXTCOLOR</c>; undefined means no shadow.
/// </summary>
public sealed record UiTextDraw(string Text, string? LabelKey, UiFont Font, WndColor Color, WndColor ShadowColor, int X, int Y,
    UiTextAlignment Alignment, UiRect Clip) : UiDrawCommand
{
    public override string ToString()
        => $"text \"{Text}\"{(LabelKey is null ? "" : $" key={LabelKey}")} font={Font} color={Color} shadow={ShadowColor} at {X},{Y} {Alignment.ToString().ToLowerInvariant()} clip {Clip}";
}

public sealed record UiPushClip(UiRect Rect) : UiDrawCommand
{
    public override string ToString() => $"clip-push {Rect}";
}

public sealed record UiPopClip : UiDrawCommand
{
    public override string ToString() => "clip-pop";
}

/// <summary>A <c>DRAWCALLBACK</c> the runtime does not implement (for example <c>W3DMainMenuDraw</c>); the host draws it.</summary>
public sealed record UiCustomDraw(string Callback, string Window, UiRect Rect) : UiDrawCommand
{
    public override string ToString() => $"custom {Callback} {Window} {Rect}";
}

public sealed record UiDrawStatistics(
    int WindowsDrawn,
    int UnresolvedLabels,
    int UnresolvedImageSizes,
    int BorderStatusWindows,
    ImmutableSortedDictionary<string, int> CustomDraws);

public sealed record UiDrawList(ImmutableArray<UiDrawCommand> Commands, UiDrawStatistics Statistics)
{
    /// <summary>One line per command; deterministic, used for golden tests.</summary>
    public string Dump()
    {
        var builder = new StringBuilder();
        foreach (var command in Commands) builder.Append(command).Append('\n');
        return builder.ToString();
    }

    public ImmutableSortedDictionary<string, int> CountByKind()
        => Commands.GroupBy(command => command.GetType().Name)
            .ToImmutableSortedDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
