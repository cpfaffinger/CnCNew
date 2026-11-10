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
/// <para>
/// The modern skin (issue 289) gets an arrangement of its own, made from the shipped windows before it derives its look (<see cref="Arrange"/>): one panel with a headline and a line about the match
/// over the table, whose rows are as high as the number of players allows, then the advice panel, which takes the room the rows leave, and the buttons in a fixed line at the bottom.
/// </para>
/// </summary>
internal static class ScoreScreenLayout
{
    public const string Advice = "ScoreScreen.wnd:ListboxWarschoolAdvice";
    public const string AdviceFrame = "ScoreScreen.wnd:AdviceFrame";
    private const string Heading = "ScoreScreen.wnd:StaticTextWarSchool";
    private const string Chat = "ScoreScreen.wnd:ListboxChatWindowScoreScreen";

    /// <summary>The layout for a screen with <paramref name="players"/> rows (only the modern skin's arrangement depends on it).</summary>
    public static WndLayout Prepare(WndLayout layout, bool modern, int players)
    {
        var chat = layout.AllWindows().FirstOrDefault(window => window.Name == Chat);
        var heading = layout.AllWindows().FirstOrDefault(window => window.Name == Heading);
        var scrolling = layout with { Windows = [.. Rewrite(layout.Windows, chat, heading, modern)] };
        return modern ? Arrange(scrolling, players) : scrolling;
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

    // ---- the modern skin's arrangement (issue 289) ----
    // The layout's 800 x 600: one panel; under it a headline and a line about the match, the table (headings, pictures, rows), the panel "Tactical Analysis" and the buttons.

    public const string Headline = "ScoreScreen.wnd:Headline";
    public const string MatchLine = "ScoreScreen.wnd:MatchLine";
    private const int Left = 46, Right = 754, NameRight = 236, FirstRowTop = 150, TableHeight = 228, MaxRowHeight = 40, ListBottom = 512, HeadingTop = 80, HeadingBottom = 118, CameoBottom = 146;

    /// <summary>The widths of the seven number columns: "Constructed" and "Collected" are the longest words of the headings.</summary>
    private static readonly int[] ColumnWidths = [66, 62, 76, 94, 72, 76, 72];
    private static readonly string[] Cells =
    [
        "StaticTextPlayer", "StaticTextUnitsBuilt", "StaticTextUnitsLost", "StaticTextUnitsDestroyed", "StaticTextBuildingsBuilt", "StaticTextBuildingsLost", "StaticTextBuildingsDestroyed", "StaticTextResources",
    ];
    private static readonly string[] Headings =
    [
        "StaticTextPlayerName", "StaticTextUnitsBuilt", "StaticTextUnitsLost", "StaticTextUnitsDestroyed", "StaticTextBuildingsConstructed", "StaticTextBuildingsLost", "StaticTextBuildingsDestroyed", "StaticTextSuppliesCollected",
    ];
    private static readonly string[] Cameos =
    [
        "", "CameoUnitsCreated", "CameoUnitsLost", "CameoUnitsDestroyed", "CameoBuildingCreated", "CameoBuildingLost", "CameoBuildingDestroyed", "CameoSuppliesCollected",
    ];

    /// <summary>The height of a row of the table for <paramref name="rows"/> players: as high as the table's room allows, up to <see cref="MaxRowHeight"/>.</summary>
    public static int RowHeight(int rows) => Math.Min(MaxRowHeight, TableHeight / Math.Max(1, rows));

    private static int ColumnLeft(int column) => column == 0 ? Left : NameRight + ColumnWidths.Take(column - 1).Sum();

    private static int ColumnRight(int column) => column == 0 ? NameRight : ColumnLeft(column) + ColumnWidths[column - 1];

    private static WndWindow At(WndWindow window, int left, int top, int right, int bottom)
        => window with { Rect = window.Rect with { Left = left, Top = top, Right = right, Bottom = bottom }, KeepRect = true };

    private static WndLayout Arrange(WndLayout layout, int players)
    {
        var rows = Math.Max(1, Math.Min(8, players));
        var rowHeight = RowHeight(rows);
        var rowsBottom = FirstRowTop + rows * rowHeight;
        var all = layout.AllWindows().ToDictionary(window => window.Name ?? string.Empty, window => window, StringComparer.Ordinal);
        WndWindow Source(string name) => all.TryGetValue("ScoreScreen.wnd:" + name, out var found) ? found : throw new InvalidDataException($"ScoreScreen.wnd has no window {name}.");
        var headline = At(Source("ChallengeWinLossText") with { Name = Headline, Status = (Source("ChallengeWinLossText").Status & ~WndStatus.Hidden) | WndStatus.NoInput | WndStatus.NoFocus, Text = string.Empty, Children = [] }, Left, 20, Right, 56);
        var matchLine = At(Source("StaticTextGameSaveComplete") with { Name = MatchLine, Status = (Source("StaticTextGameSaveComplete").Status & ~WndStatus.Hidden) | WndStatus.NoInput | WndStatus.NoFocus, Text = string.Empty, Children = [] }, Left, 56, Right, 78);

        WndWindow Place(WndWindow window)
        {
            var name = window.Name ?? string.Empty;
            if (!name.StartsWith("ScoreScreen.wnd:", StringComparison.Ordinal)) return window;
            var local = name["ScoreScreen.wnd:".Length..];
            WndWindow Done(WndWindow placed) => placed with { Children = [.. placed.Children.Select(Place)] };
            switch (local)
            {
                case "MainBackdrop": return Done(At(window, 30, 18, 770, 568));
                case "ParentScoreScreen": return window with { Children = [.. window.Children.Select(Place), headline, matchLine] };
                case "": return Done(At(window, Left, HeadingTop, Right, CameoBottom));
                case "Line": return Done(At(window, Left, CameoBottom + 1, Right, CameoBottom + 2));
                case "GadgetParent": return Done(At(window, Left, FirstRowTop, Right, rowsBottom));
                case "StaticTextWarSchool": return Done(At(window, Left, rowsBottom + 6, Right, rowsBottom + 32));
                case "ListboxWarschoolAdvice": return Done(At(window, Left, rowsBottom + 34, Right, ListBottom));
                case "ButtonSaveReplay": return Done(At(window, Left, 524, Left + 190, 558));
                case "ButtonContinue": return Done(At(window, Right - 330, 524, Right - 170, 558));
                case "ButtonOk": return Done(At(window, Right - 160, 524, Right, 558));
            }
            for (var column = 0; column < Headings.Length; column++)
            {
                if (local == Headings[column]) return Done(At(window, ColumnLeft(column) + 2, HeadingTop, ColumnRight(column) - 2, HeadingBottom));
                if (local == Cameos[column]) return Done(At(window, (ColumnLeft(column) + ColumnRight(column)) / 2 - 18, HeadingBottom + 2, (ColumnLeft(column) + ColumnRight(column)) / 2 + 18, CameoBottom));
            }
            if (local.Length > 1 && char.IsAsciiDigit(local[^1]) && int.Parse(local[^1..], System.Globalization.CultureInfo.InvariantCulture) is var row)
            {
                var top = FirstRowTop + row * rowHeight;
                var prefix = local[..^1];
                if (prefix == "GameWindowWinner") return Done(At(window, Left + 2, top + 3, Left + 8, top + rowHeight - 3));
                if (prefix == "StaticTextObserver") return Done(At(window, Left + 12, top, Right, top + rowHeight));
                for (var column = 0; column < Cells.Length; column++)
                    if (prefix == Cells[column])
                        return Done(At(window, column == 0 ? Left + 12 : ColumnLeft(column), top, ColumnRight(column), top + rowHeight));
            }
            return Done(window);
        }
        return layout with { Windows = [.. layout.Windows.Select(Place)] };
    }
}
