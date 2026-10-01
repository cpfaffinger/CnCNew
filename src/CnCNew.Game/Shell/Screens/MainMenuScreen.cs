using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>MainMenu.wnd</c>: one stacked layout carrying every submenu group in a single file (the original hides all but
/// the entry group at <c>W3DMainMenuInit</c> time; the WND file itself marks none of them <c>Hidden</c>). There is no
/// callback registry entry for <c>init</c>/<c>update</c>/<c>shutdown</c> in <see cref="UiWindowTree"/> (they are not
/// routed messages, just lifecycle names in the WND file), so this constructor performs the equivalent of
/// <c>W3DMainMenuInit</c> directly: hide every submenu group except the entry one.
/// <para>
/// USA/GLA/China/Challenge and Load Game are wired to the campaign/savegame flow (<c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>
/// §7). The three faction "recent save" shortcuts on the root have no equivalent in this build (there is no concept of
/// "most recent save per faction") and stay disabled; <c>ButtonMultiplayer</c> stays out of scope (see
/// <c>docs/GAME-SHELL.md</c>) and stays disabled too.
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

        // Multiplayer is out of scope for this build (see docs/GAME-SHELL.md); kept visible but inert.
        W("ButtonMultiplayer").SetEnabled(false);
        var hasCampaigns = Shell.Campaigns is not null;
        W("ButtonUSA").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("USA") is not null);
        W("ButtonGLA").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("GLA") is not null);
        W("ButtonChina").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("China") is not null);
        W("ButtonChallenge").SetEnabled(hasCampaigns && Shell.Campaigns!.Campaigns.Any(campaign => campaign.IsChallenge));
        W("ButtonLoadReplay").SetEnabled(true);
        // Campaign "recent save" shortcuts sit directly on the root (not inside any submenu group); this build has no
        // concept of a "most recent save per faction" (saves are not tagged by faction at all), so they stay inert.
        W("ButtonUSARecentSave").SetEnabled(false);
        W("ButtonUSALoadGame").SetEnabled(false);
        W("ButtonGLARecentSave").SetEnabled(false);
        W("ButtonGLALoadGame").SetEnabled(false);
        W("ButtonChinaRecentSave").SetEnabled(false);
        W("ButtonChinaLoadGame").SetEnabled(false);
    }

    private bool SinglePlayerSubmenuOpen => !W("MapBorder").IsHidden;
    private bool LoadReplaySubmenuOpen => !W("MapBorder3").IsHidden;

    private void ShowMain()
    {
        W("MapBorder2").SetHidden(false);
        W("MapBorder").SetHidden(true);
        W("MapBorder3").SetHidden(true);
    }

    private void ShowSinglePlayer()
    {
        W("MapBorder2").SetHidden(true);
        W("MapBorder").SetHidden(false);
    }

    private void ShowLoadReplay()
    {
        W("MapBorder2").SetHidden(true);
        W("MapBorder3").SetHidden(false);
    }

    private void OpenCampaign(string name)
    {
        if (Shell.Campaigns?.Find(name) is not { } campaign) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        Shell.OpenCampaignFaction(campaign);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "MainMenu.wnd:ButtonSinglePlayer": ShowSinglePlayer(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonSingleBack": ShowMain(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonSkirmish": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenSkirmishSetup(); break;
            case "MainMenu.wnd:ButtonUSA": OpenCampaign("USA"); break;
            case "MainMenu.wnd:ButtonGLA": OpenCampaign("GLA"); break;
            case "MainMenu.wnd:ButtonChina": OpenCampaign("China"); break;
            case "MainMenu.wnd:ButtonChallenge": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenChallengeSelect(); break;
            case "MainMenu.wnd:ButtonLoadReplay": ShowLoadReplay(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonLoadReplayBack": ShowMain(); Shell.PlaySound(ShellUiSounds.ButtonClick); break;
            case "MainMenu.wnd:ButtonLoadGame": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenLoadGame(); break;
            case "MainMenu.wnd:ButtonOptions": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenOptions(); break;
            case "MainMenu.wnd:ButtonCredits": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenCredits(); break;
            case "MainMenu.wnd:ButtonExit": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenQuitConfirm(); break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.Key || e.Value != (int)UiKey.Escape) return;
        if (SinglePlayerSubmenuOpen || LoadReplaySubmenuOpen) ShowMain();
        else Shell.OpenQuitConfirm();
    }
}
