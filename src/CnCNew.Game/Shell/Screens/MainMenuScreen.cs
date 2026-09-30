using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>MainMenu.wnd</c>: one stacked layout carrying every submenu group in a single file (the original hides all but
/// the entry group at <c>W3DMainMenuInit</c> time; the WND file itself marks none of them <c>Hidden</c>). There is no
/// callback registry entry for <c>init</c>/<c>update</c>/<c>shutdown</c> in <see cref="UiWindowTree"/> (they are not
/// routed messages, just lifecycle names in the WND file), so this constructor performs the equivalent of
/// <c>W3DMainMenuInit</c> directly: hide every submenu group except the entry one.
/// <para>
/// Out of scope for this build (see <c>docs/GAME-SHELL.md</c>): multiplayer, load/replay, and the three faction
/// campaign buttons plus Challenge. Their buttons stay visible (so the menu looks authentic) but disabled, and their
/// submenus (network options, load/replay, difficulty) are therefore unreachable and stay hidden.
/// </para>
/// </summary>
public sealed class MainMenuScreen : ShellScreen
{
    public MainMenuScreen(GameShell shell) : base(shell, ShellLayoutNames.MainMenu)
    {
        Tree.Register("MainMenuSystem", OnSystem);
        Tree.Register("MainMenuInput", OnInput);

        // Entry group visible, every submenu hidden (W3DMainMenuInit equivalent).
        W("MapBorder2").SetHidden(false);
        W("MapBorder").SetHidden(true);
        W("MapBorder1").SetHidden(true);
        W("MapBorder3").SetHidden(true);
        W("MapBorder4").SetHidden(true);

        // Out of scope for this package; kept visible but inert.
        W("ButtonMultiplayer").SetEnabled(false);
        W("ButtonLoadReplay").SetEnabled(false);
        W("ButtonUSA").SetEnabled(false);
        W("ButtonGLA").SetEnabled(false);
        W("ButtonChina").SetEnabled(false);
        W("ButtonChallenge").SetEnabled(false);
        // Campaign "recent save" shortcuts sit directly on the root (not inside any submenu group); no save system exists yet.
        W("ButtonUSARecentSave").SetEnabled(false);
        W("ButtonUSALoadGame").SetEnabled(false);
        W("ButtonGLARecentSave").SetEnabled(false);
        W("ButtonGLALoadGame").SetEnabled(false);
        W("ButtonChinaRecentSave").SetEnabled(false);
        W("ButtonChinaLoadGame").SetEnabled(false);
    }

    private bool SinglePlayerSubmenuOpen => !W("MapBorder").IsHidden;

    private void ShowMain()
    {
        W("MapBorder2").SetHidden(false);
        W("MapBorder").SetHidden(true);
    }

    private void ShowSinglePlayer()
    {
        W("MapBorder2").SetHidden(true);
        W("MapBorder").SetHidden(false);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "MainMenu.wnd:ButtonSinglePlayer": ShowSinglePlayer(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonSingleBack": ShowMain(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonSkirmish": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenSkirmishSetup(); break;
            case "MainMenu.wnd:ButtonOptions": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenOptions(); break;
            case "MainMenu.wnd:ButtonCredits": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenCredits(); break;
            case "MainMenu.wnd:ButtonExit": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenQuitConfirm(); break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.Key || e.Value != (int)UiKey.Escape) return;
        if (SinglePlayerSubmenuOpen) ShowMain();
        else Shell.OpenQuitConfirm();
    }
}
