using System.Collections.Immutable;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// The two buttons of the debug option "money buttons" (#336), added to the in-game menu (<c>QuitMenu.wnd</c>, Escape) only in a match that turned the option on: "Give money to me" and
/// "Give money to all" (10,000 credits each, <see cref="CnCNew.Simulation.Commands.DebugGiveMoneyCommand"/>). They sit side by side under the menu's top edge, in the free band above its first
/// button, so they are out of the way during play (the menu is the pause screen) and a match without the option has exactly the menu of the original. Made at run time from the shipped
/// restart button, like the other screens of this project that the original has no layout for.
/// </summary>
public static class QuitMenuDebugButtons
{
    public const string GiveSelf = "QuitMenu.wnd:ButtonDebugGiveSelf", GiveAll = "QuitMenu.wnd:ButtonDebugGiveAll";

    public static WndLayout Extend(WndLayout menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var template = menu.AllWindows().FirstOrDefault(window => window.Name == "QuitMenu.wnd:ButtonRestart") ?? throw new InvalidDataException("QuitMenu.wnd has no ButtonRestart.");
        var parent = menu.Windows.FirstOrDefault(window => window.Name == "QuitMenu.wnd:QuitMenuParent") ?? throw new InvalidDataException("QuitMenu.wnd has no QuitMenuParent.");
        WndWindow Make(string name, int left, int right) => template with
        {
            Name = name,
            Rect = new WndScreenRect(left, 133, right, 173, 800, 600),
            Text = null,
            TooltipText = null,
            Font = template.Font is { } font ? font with { Size = 11 } : template.Font,
            HeaderTemplate = null,
            Children = [],
        };
        var extended = parent with { Children = parent.Children.Add(Make(GiveSelf, 288, 398)).Add(Make(GiveAll, 402, 512)) };
        return menu with { Windows = [..menu.Windows.Select(window => ReferenceEquals(window, parent) ? extended : window)] };
    }
}
