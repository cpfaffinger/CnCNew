using System.Collections.Immutable;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell;

/// <summary>What the map preview custom draw needs: the display name of the currently selected map, if any.</summary>
public sealed record ShellMapPreviewContext(string? MapDisplayName);

/// <summary>What the credits custom draw needs: the parsed lines, a vertical scroll offset in pixels, and the text source for label lines.</summary>
/// <summary>The three credits fonts of <c>Language.ini</c> (<c>CreditsTitleFont</c>, <c>CreditsMinorTitleFont</c>, <c>CreditsNormalFont</c>, resolution adjusted).</summary>
public sealed record CreditsFonts(UiFont Title, UiFont MinorTitle, UiFont Normal);

public sealed record ShellCreditsContext(ImmutableArray<CreditsLine> Lines, int ScrollOffsetPixels, IUiTextSource? TextSource, CreditsFonts? Fonts = null);

/// <summary>
/// Expands the <see cref="UiCustomDraw"/> commands the shell's screens actually produce with real data
/// (<c>W3DDrawMapPreview</c>, <c>W3DCreditsMenuDraw</c>) into concrete fill/border/text commands. Every other custom draw
/// callback the shipped menus name (<c>W3DClockDraw</c>, <c>W3DShellMenuSchemeDraw</c>) belongs to a window that stays
/// hidden as authored (<c>Clock</c>, <c>ShellMenuScheme</c> in <c>MainMenu.wnd</c>): a hidden window never reaches the
/// draw list (<c>UiWindowTree.BuildDrawList</c> returns before evaluating its draw callback), so those two names never
/// appear here. This is a documented simplification (no analog clock readout, no animated 3D shell background), not a
/// missing registration: draw callbacks are not routed messages, so there is nothing to register for them.
/// </summary>
public static class ShellCustomDraw
{
    private static readonly WndColor PreviewBackground = new(16, 20, 28, 255);
    private static readonly WndColor PreviewBorder = new(90, 100, 120, 255);
    private static readonly WndColor PreviewText = WndColor.White;
    private static readonly WndColor CreditsTitle = new(161, 179, 255, 255);
    private static readonly WndColor CreditsNormal = new(209, 218, 255, 255);

    public static UiDrawList ExpandMapPreview(UiDrawList list, ShellMapPreviewContext context, IUiTextMeasure measure)
        => Rebuild(list, command => command.Callback == "W3DDrawMapPreview" ? MapPreview(command, context, measure) : null);

    public static UiDrawList ExpandCredits(UiDrawList list, ShellCreditsContext context, IUiTextMeasure measure)
        => Rebuild(list, command => command.Callback == "W3DCreditsMenuDraw" ? Credits(command, context, measure) : null);

    private static ImmutableArray<UiDrawCommand> MapPreview(UiCustomDraw command, ShellMapPreviewContext context, IUiTextMeasure measure)
    {
        var rect = command.Rect;
        var builder = ImmutableArray.CreateBuilder<UiDrawCommand>();
        builder.Add(new UiFillRect(rect, PreviewBackground));
        builder.Add(new UiBorderRect(rect, PreviewBorder));
        var font = new UiFont("Arial", Math.Max(8, rect.Height / 12), false);
        var text = context.MapDisplayName ?? "No map selected";
        var width = measure.MeasureWidth(text, font);
        var x = rect.X + Math.Max(0, (rect.Width - width) / 2);
        var y = rect.Y + rect.Height / 2 - measure.LineHeight(font) / 2;
        builder.Add(new UiTextDraw(text, null, font, PreviewText, WndColor.Undefined, x, y, UiTextAlignment.Center, rect));
        return builder.ToImmutable();
    }

    private static ImmutableArray<UiDrawCommand> Credits(UiCustomDraw command, ShellCreditsContext context, IUiTextMeasure measure)
    {
        var rect = command.Rect;
        var builder = ImmutableArray.CreateBuilder<UiDrawCommand>();
        builder.Add(new UiPushClip(rect));
        var y = rect.Y + rect.Height - context.ScrollOffsetPixels;
        foreach (var line in context.Lines)
        {
            var fonts = context.Fonts;
            var font = line.Style switch
            {
                CreditsStyle.Title => fonts?.Title ?? new UiFont("Arial", 24, true),
                CreditsStyle.MinorTitle => fonts?.MinorTitle ?? new UiFont("Arial", 16, true),
                _ => fonts?.Normal ?? new UiFont("Arial", 12, false),
            };
            var height = measure.LineHeight(font);
            if (y + height >= rect.Y && y <= rect.Bottom)
            {
                var text = Resolve(line, context.TextSource);
                if (text.Length > 0)
                {
                    var color = line.Style is CreditsStyle.Title or CreditsStyle.MinorTitle ? CreditsTitle : CreditsNormal;
                    var width = measure.MeasureWidth(text, font);
                    var x = rect.X + Math.Max(0, (rect.Width - width) / 2);
                    builder.Add(new UiTextDraw(text, null, font, color, WndColor.Undefined, x, y, UiTextAlignment.Center, rect));
                }
            }
            y += height;
        }
        builder.Add(new UiPopClip());
        return builder.ToImmutable();
    }

    private static string Resolve(CreditsLine line, IUiTextSource? textSource)
    {
        if (line.Text is null) return string.Empty;
        if (!line.IsLabel) return line.Text;
        return textSource is not null && textSource.TryResolve(line.Text, out var resolved) ? resolved : line.Text;
    }

    private static UiDrawList Rebuild(UiDrawList list, Func<UiCustomDraw, ImmutableArray<UiDrawCommand>?> expand)
    {
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        foreach (var command in list.Commands)
        {
            if (command is UiCustomDraw custom && expand(custom) is { } replacement) commands.AddRange(replacement);
            else commands.Add(command);
        }
        return list with { Commands = commands.ToImmutable() };
    }
}
