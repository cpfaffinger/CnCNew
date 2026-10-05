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
/// "most recent save per faction") and stay hidden like after <c>MainMenuInit</c>; <c>ButtonMultiplayer</c> stays out of scope (see
/// <c>docs/GAME-SHELL.md</c>) and stays disabled too.
/// </para>
/// <para>
/// With window transitions (<see cref="ShellScreen.Transitions"/>, the shell created with <c>animateTransitions</c>) the menu animates like
/// <c>MainMenu.cpp</c>: the entry plays <c>MainMenuDefaultMenuLogoFade</c> (the very first menu of a run after a fade from black,
/// <c>FadeWholeScreen</c>), a submenu change plays the old submenu's "Back" group reversed and the new submenu's group
/// (<c>remove</c>/<c>reverse</c>/<c>setGroup</c>), the faction buttons show their logo while hovered (<c>MainMenuFaction*</c>), and a button that opens
/// another screen first flashes the menu out and opens the screen when the groups have finished (<c>Shell::push</c> waits for the shutdown
/// animation). <c>dontAllowTransitions</c> of the original: while a transition runs, navigation clicks are ignored. Without transitions every
/// switch is a hard cut as before.
/// </para>
/// </summary>
public sealed class MainMenuScreen : ShellScreen
{
    private enum Submenu { Main, SinglePlayer, LoadReplay }

    private Submenu _submenu = Submenu.Main;

    // dontAllowTransitions / campaignSelected / showLogo+showSide of MainMenu.cpp.
    private bool _dontAllow;
    private bool _campaignSelected;
    private string? _pendingLogo;

    // Set when a button opens another screen: that screen opens when the flash-out has finished, and the entry replays when it is closed again.
    private Action? _open;
    private bool _leaving;

    public MainMenuScreen(GameShell shell) : base(shell, ShellLayoutNames.MainMenu)
    {
        Tree.Register("MainMenuSystem", OnSystem);
        Tree.Register("MainMenuInput", OnInput);

        // Entry group visible, every submenu hidden (W3DMainMenuInit equivalent).
        ResetToMain();

        // Multiplayer is out of scope for this build (see docs/GAME-SHELL.md); kept visible but inert.
        W("ButtonMultiplayer").SetEnabled(false);
        var hasCampaigns = Shell.Campaigns is not null;
        W("ButtonUSA").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("USA") is not null);
        W("ButtonGLA").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("GLA") is not null);
        W("ButtonChina").SetEnabled(hasCampaigns && Shell.Campaigns!.Find("China") is not null);
        W("ButtonChallenge").SetEnabled(hasCampaigns && Shell.Campaigns!.Campaigns.Any(campaign => campaign.IsChallenge));
        W("ButtonLoadReplay").SetEnabled(true);
        // The campaign "recent save"/"load game" shortcuts sit directly on the root, left of the button column, and are
        // not Hidden in the WND file: MainMenuInit hides all six (showSelectiveButtons(SHOW_NONE)) and the original only
        // shows one pair while its faction button is hovered. This build has no "most recent save per faction" (saves are
        // not tagged by faction), so the pairs never come up; disabling them instead of hiding left six grey buttons
        // visible next to the main menu.
        foreach (var name in FactionSaveButtons)
        {
            W(name).SetEnabled(false);
            W(name).SetHidden(true);
        }

        if (Transitions is not null) StartEntry(first: Shell.TakeFirstMainMenu());
    }

    private static readonly string[] FactionLogos = ["WinFactionUS", "WinFactionGLA", "WinFactionChina", "WinFactionTraining", "WinFactionSkirmish"];

    private static readonly string[] FactionSaveButtons =
    [
        "ButtonUSARecentSave", "ButtonUSALoadGame", "ButtonGLARecentSave", "ButtonGLALoadGame", "ButtonChinaRecentSave", "ButtonChinaLoadGame",
    ];

    /// <summary>The group the faction logo of a hovered button plays (<c>MainMenuFactionTraining</c> serves Challenge as in the original).</summary>
    private static string? FactionGroup(string source) => source switch
    {
        "MainMenu.wnd:ButtonUSA" => "MainMenuFactionUS",
        "MainMenu.wnd:ButtonGLA" => "MainMenuFactionGLA",
        "MainMenu.wnd:ButtonChina" => "MainMenuFactionChina",
        "MainMenu.wnd:ButtonChallenge" => "MainMenuFactionTraining",
        "MainMenu.wnd:ButtonSkirmish" => "MainMenuFactionSkirmish",
        _ => null,
    };

    private void ResetToMain()
    {
        _submenu = Submenu.Main;
        _dontAllow = false;
        _campaignSelected = false;
        _pendingLogo = null;
        W("MapBorder2").SetHidden(false);
        W("MapBorder").SetHidden(true);
        W("MapBorder1").SetHidden(true);
        W("MapBorder3").SetHidden(true);
        W("MapBorder4").SetHidden(true);
        // initialHide(): every faction logo window in all three sizes starts hidden (the logos of the single-player submenu are shown by its transition group).
        foreach (var logo in FactionLogos)
            foreach (var size in LogoSizes) W(logo + size).SetHidden(true);
    }

    private static readonly string[] LogoSizes = ["", "Small", "Medium"];

    /// <summary>The init of <c>MainMenu.cpp</c>: the first menu of the run fades in from black, then the logo and the buttons flash in.</summary>
    private void StartEntry(bool first)
    {
        var transitions = Transitions!;
        if (first) transitions.Reverse("FadeWholeScreen");
        transitions.SetGroup("MainMenuDefaultMenuLogoFade");
    }

    public override void OnRevealed()
    {
        if (!_leaving) return;
        _leaving = false;
        ResetToMain();
        if (Transitions is not null) StartEntry(first: false);
    }

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        if (Transitions is not { } transitions) return;
        if (_dontAllow && transitions.IsFinished) _dontAllow = false;
        if (_pendingLogo is { } group && !_dontAllow)
        {
            transitions.SetGroup(group);
            _pendingLogo = null;
        }
        if (_open is { } open && transitions.IsSettled)
        {
            _open = null;
            open();
        }
    }

    public override bool IsAnimating => base.IsAnimating || _open is not null;

    private bool SinglePlayerSubmenuOpen => _submenu == Submenu.SinglePlayer;
    private bool LoadReplaySubmenuOpen => _submenu == Submenu.LoadReplay;

    /// <summary>Changes the submenu: the old one's "Back" group plays reversed, the new one's group plays (unless transitions are off: a hard cut).</summary>
    private void SwitchTo(Submenu target)
    {
        var from = _submenu;
        _submenu = target;
        if (Transitions is not { } transitions)
        {
            W("MapBorder2").SetHidden(target != Submenu.Main);
            W("MapBorder").SetHidden(target != Submenu.SinglePlayer);
            W("MapBorder3").SetHidden(target != Submenu.LoadReplay);
            // The single-player submenu shows the five faction logos along its bottom (WINSCALEUP leaves them shown in the original).
            foreach (var logo in FactionLogos) W(logo).SetHidden(target != Submenu.SinglePlayer);
            return;
        }
        _dontAllow = true;
        W(target switch { Submenu.SinglePlayer => "MapBorder", Submenu.LoadReplay => "MapBorder3", _ => "MapBorder2" }).SetHidden(false);
        var (leave, back) = from switch
        {
            Submenu.SinglePlayer => ("MainMenuSinglePlayerMenu", "MainMenuSinglePlayerMenuBack"),
            Submenu.LoadReplay => ("MainMenuLoadReplayMenu", "MainMenuLoadReplayMenuBack"),
            _ => ("MainMenuDefaultMenu", "MainMenuDefaultMenuBack"),
        };
        var enter = target switch
        {
            Submenu.SinglePlayer => "MainMenuSinglePlayerMenu",
            Submenu.LoadReplay => "MainMenuLoadReplayMenu",
            _ => "MainMenuDefaultMenu",
        };
        transitions.Remove(leave);
        transitions.Reverse(back);
        transitions.SetGroup(enter);
    }

    /// <summary>
    /// Opens another screen: with transitions the menu first flashes out (<paramref name="removeGroup"/> dropped, <paramref name="reverseGroup"/>
    /// reversed) and <paramref name="open"/> runs when the groups are done; without them it runs at once.
    /// </summary>
    private void LeaveFor(Action open, string? removeGroup, string reverseGroup)
    {
        if (Transitions is not { } transitions)
        {
            open();
            return;
        }
        _campaignSelected = true;
        _leaving = true;
        _open = open;
        if (removeGroup is not null) transitions.Remove(removeGroup);
        transitions.Reverse(reverseGroup);
    }

    private void OpenCampaign(string name, string backGroup)
    {
        if (Shell.Campaigns?.Find(name) is not { } campaign) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        LeaveFor(() => Shell.OpenCampaignFaction(campaign), FactionGroupOf(backGroup), backGroup);
    }

    private static string FactionGroupOf(string backGroup) => backGroup switch
    {
        "MainMenuSinglePlayerMenuBackUS" => "MainMenuFactionUS",
        "MainMenuSinglePlayerMenuBackGLA" => "MainMenuFactionGLA",
        "MainMenuSinglePlayerMenuBackChina" => "MainMenuFactionChina",
        "MainMenuSinglePlayerMenuBackTraining" => "MainMenuFactionTraining",
        _ => "MainMenuFactionSkirmish",
    };

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonMouseEntering: OnFactionHover(e.Source, entering: true); return;
            case UiEventKind.ButtonMouseLeaving: OnFactionHover(e.Source, entering: false); return;
            case UiEventKind.ButtonSelected: break;
            default: return;
        }
        // The submenu switches and the screens behind them are refused while a transition runs (dontAllowTransitions); Exit is not.
        if (_leaving || (_dontAllow && e.Source != "MainMenu.wnd:ButtonExit")) return;
        switch (e.Source)
        {
            case "MainMenu.wnd:ButtonSinglePlayer": Shell.PlaySound(ShellUiSounds.ButtonClick); SwitchTo(Submenu.SinglePlayer); break;
            case "MainMenu.wnd:ButtonSingleBack": if (_campaignSelected) break; Shell.PlaySound(ShellUiSounds.ButtonClick); SwitchTo(Submenu.Main); break;
            case "MainMenu.wnd:ButtonSkirmish":
                if (_campaignSelected) break;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                LeaveFor(Shell.OpenSkirmishSetup, "MainMenuFactionSkirmish", "MainMenuSinglePlayerMenuBackSkirmish");
                break;
            case "MainMenu.wnd:ButtonUSA": if (!_campaignSelected) OpenCampaign("USA", "MainMenuSinglePlayerMenuBackUS"); break;
            case "MainMenu.wnd:ButtonGLA": if (!_campaignSelected) OpenCampaign("GLA", "MainMenuSinglePlayerMenuBackGLA"); break;
            case "MainMenu.wnd:ButtonChina": if (!_campaignSelected) OpenCampaign("China", "MainMenuSinglePlayerMenuBackChina"); break;
            case "MainMenu.wnd:ButtonChallenge":
                if (_campaignSelected) break;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                LeaveFor(Shell.OpenChallengeSelect, "MainMenuFactionTraining", "MainMenuSinglePlayerMenuBackTraining");
                break;
            case "MainMenu.wnd:ButtonLoadReplay": Shell.PlaySound(ShellUiSounds.ButtonClick); SwitchTo(Submenu.LoadReplay); break;
            case "MainMenu.wnd:ButtonLoadReplayBack": Shell.PlaySound(ShellUiSounds.ButtonClick); SwitchTo(Submenu.Main); break;
            case "MainMenu.wnd:ButtonLoadGame":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                LeaveFor(Shell.OpenLoadGame, null, "MainMenuLoadReplayMenuBackTransition");
                break;
            case "MainMenu.wnd:ButtonOptions": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenOptions(); break;
            case "MainMenu.wnd:ButtonCredits":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                LeaveFor(Shell.OpenCredits, null, "MainMenuDefaultMenu");
                break;
            case "MainMenu.wnd:ButtonExit": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.OpenQuitConfirm(); break;
        }
    }

    /// <summary>
    /// <c>GBM_MOUSE_ENTERING</c>/<c>GBM_MOUSE_LEAVING</c> of the five buttons of the single-player submenu: the faction logo scales up behind them. While a
    /// transition runs the request is remembered and played afterwards (<c>showLogo</c>/<c>showSide</c>), once a screen was chosen it is ignored.
    /// </summary>
    private void OnFactionHover(string source, bool entering)
    {
        if (Transitions is not { } transitions || FactionGroup(source) is not { } group) return;
        if (entering)
        {
            if (_dontAllow && !_campaignSelected) _pendingLogo = group;
            if (_campaignSelected || _dontAllow) return;
            transitions.SetGroup(group);
        }
        else
        {
            if (_dontAllow && !_campaignSelected && _pendingLogo is not null) _pendingLogo = null;
            if (_campaignSelected || _dontAllow) return;
            transitions.Reverse(group);
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.Key || e.Value != (int)UiKey.Escape) return;
        if (_leaving) return;
        if (SinglePlayerSubmenuOpen || LoadReplaySubmenuOpen)
        {
            if (!_dontAllow && !_campaignSelected) SwitchTo(Submenu.Main);
        }
        else Shell.OpenQuitConfirm();
    }
}
