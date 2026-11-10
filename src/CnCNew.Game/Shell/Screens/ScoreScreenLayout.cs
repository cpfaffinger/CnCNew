using System.Collections.Immutable;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ScoreScreen.wnd</c> as the skirmish's score screen needs it (issue 288): the panel "Tactical Analysis" (<c>ListboxWarschoolAdvice</c>) holds paragraphs of advice, and the shipped
/// layout gives it no scroll bar (<c>SCROLLBAR: 0</c>) and no input (<c>NOINPUT</c>), so what did not fit was cut off and could not be reached. The list is made scrollable - a bar and
/// the mouse wheel - before the skin derives its look, so both skins draw it with their own bar.
/// <para>
/// The original skin keeps the screen as it is otherwise: the list's picture (the blue frame, whose first lines the heading stands on) moves to a window of its own behind the list, and the
/// list - transparent, with the bar's art of the screen's chat list, which has the same rectangle and a bar - starts under the heading, so that scrolled text never runs under it.
/// </para>
/// </summary>
internal static class ScoreScreenLayout
{
    public const string Advice = "ScoreScreen.wnd:ListboxWarschoolAdvice";
    public const string AdviceFrame = "ScoreScreen.wnd:AdviceFrame";
    private const string Heading = "ScoreScreen.wnd:StaticTextWarSchool";
    private const string Chat = "ScoreScreen.wnd:ListboxChatWindowScoreScreen";

    public static WndLayout Prepare(WndLayout layout, bool modern)
    {
        var chat = layout.AllWindows().FirstOrDefault(window => window.Name == Chat);
        var heading = layout.AllWindows().FirstOrDefault(window => window.Name == Heading);
        return layout with { Windows = [.. Rewrite(layout.Windows, chat, heading, modern)] };
    }

    private static IEnumerable<WndWindow> Rewrite(ImmutableArray<WndWindow> windows, WndWindow? chat, WndWindow? heading, bool modern)
    {
        foreach (var window in windows)
        {
            if (window.Name != Advice)
            {
                yield return window.Children.IsDefaultOrEmpty ? window : window with { Children = [.. Rewrite(window.Children, chat, heading, modern)] };
                continue;
            }
            if (window.ListBox is not { } data) throw new InvalidDataException("ScoreScreen.wnd:ListboxWarschoolAdvice is not a list box.");
            var scrolling = window with { ListBox = data with { ScrollBar = true }, Status = window.Status & ~WndStatus.NoInput };
            if (modern || chat is null || heading is null)
            {
                yield return scrolling;
                continue;
            }
            // The frame keeps the list's picture and rectangle; the list under the heading is a clear window with the art of a bar.
            yield return window with
            {
                Type = WndWindowType.User, TypeName = "USER", Name = AdviceFrame, ListBox = null, Children = [], Style = WndStyle.User, Status = (window.Status | WndStatus.NoInput) & ~WndStatus.Hidden,
            };
            var clear = new WndDrawData(null, WndColor.Undefined, WndColor.Undefined);
            var sets = window.DrawData.Where(set => set.Kind <= WndDrawDataKind.Hilite).Select(set => new WndDrawDataSet(set.Kind, [clear, .. set.Entries.Skip(1)]))
                .Concat(chat.DrawData.Where(set => set.Kind > WndDrawDataKind.Hilite)).ToImmutableArray();
            yield return scrolling with
            {
                Rect = window.Rect with { Top = heading.Rect.Bottom },
                DrawData = sets,
            };
        }
    }
}
