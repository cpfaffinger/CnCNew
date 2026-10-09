using CnCNew.Game.Campaign;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Transitions;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>MainMenu.wnd</c>: one stacked layout carrying every submenu group in a single file (the original hides all but
/// the entry group at <c>W3DMainMenuInit</c> time; the WND file itself marks none of them <c>Hidden</c>). There is no
/// callback registry entry for <c>init</c>/<c>update</c>/<c>shutdown</c> in <see cref="UiWindowTree"/> (they are not
/// routed messages, just lifecycle names in the WND file), so this constructor performs the equivalent of
/// <c>W3DMainMenuInit</c> directly: hide every submenu group except the entry one.
/// <para>
/// USA/GLA/China open the difficulty submenu of this very layout (<c>MapBorder4</c>: <c>ButtonEasy/Medium/Hard/DiffBack</c>, groups
/// <c>MainMenuDifficultyMenu*</c>), as in <c>MainMenu.cpp</c>; choosing a difficulty starts the campaign (<c>FadeWholeScreen</c>, then the mission
/// briefing). Challenge asks the same question and then pushes <c>ChallengeMenu.wnd</c> (<c>setupGameStart</c> with <c>launchChallengeMenu</c>). There is no
/// intermediate New/Load page: <c>SinglePlayerMenu.wnd</c> is a leftover of the original Generals that nothing in Zero Hour opens
/// (<c>SinglePlayerMenu.cpp</c> exists, no <c>Shell::push</c> names it); loading a game is the Load button of the main menu (<c>MapBorder3</c>).
/// The three faction "recent save" shortcuts on the root have no equivalent in this build (there is no concept of
/// "most recent save per faction"; Zero Hour's <c>MainMenu.cpp</c> never shows them either) and stay hidden like after <c>MainMenuInit</c>;
/// <c>ButtonMultiplayer</c> stays out of scope (see <c>docs/GAME-SHELL.md</c>) and stays disabled too.
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
    private enum Submenu { Main, SinglePlayer, LoadReplay, Difficulty }

    /// <summary>
    /// One of the four buttons of the single-player submenu that lead to the difficulty question (<c>MainMenu.cpp</c>, <c>buttonUSAID</c>/<c>buttonGLAID</c>/
    /// <c>buttonChinaID</c>/<c>buttonChallengeID</c>) with the transition groups its branches play (<c>WindowTransitions.ini</c>). <see cref="Campaign"/> is the
    /// <c>Campaign.ini</c> name started by it; null for the Challenge button, which opens the challenge menu instead (<c>TheCampaignManager->setCampaign</c> is not called).
    /// </summary>
    private sealed record Faction(string Button, string? Campaign, string Logo, string HoverGroup, string BackFromSinglePlayer, string Difficulty, string DifficultyBack, string FromDifficulty);

    private static readonly Faction[] Factions =
    [
        new("ButtonUSA", "USA", "WinFactionUS", "MainMenuFactionUS", "MainMenuSinglePlayerMenuBackUS", "MainMenuDifficultyMenuUS", "MainMenuDifficultyMenuUSBack", "MainMenuSinglePlayerUSAMenuFromDiff"),
        new("ButtonGLA", "GLA", "WinFactionGLA", "MainMenuFactionGLA", "MainMenuSinglePlayerMenuBackGLA", "MainMenuDifficultyMenuGLA", "MainMenuDifficultyMenuGLABack", "MainMenuSinglePlayerGLAMenuFromDiff"),
        new("ButtonChina", "China", "WinFactionChina", "MainMenuFactionChina", "MainMenuSinglePlayerMenuBackChina", "MainMenuDifficultyMenuChina", "MainMenuDifficultyMenuChinaBack", "MainMenuSinglePlayerChinaMenuFromDiff"),
        new("ButtonChallenge", null, "WinFactionTraining", "MainMenuFactionTraining", "MainMenuSinglePlayerMenuBackTraining", "MainMenuDifficultyMenuTraining", "MainMenuDifficultyMenuTrainingBack", "MainMenuSinglePlayerTrainingMenuFromDiff"),
    ];

    /// <summary>The faction whose difficulty question is open (<c>showSide</c> of <c>MainMenu.cpp</c>).</summary>
    private Faction? _faction;

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
        // The retail MainMenu.wnd (PatchWindow.big) has a "custom mission" button between Skirmish and Back; this build has no such screen (the published source of the
        // original has no handler for it either). Kept visible but inert, like Multiplayer; a layout without the button has nothing to disable.
        Tree.FindByName("MainMenu.wnd:ButtonCustomMission")?.SetEnabled(false);
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
        _faction = null;
        _campaign = null;
        W("StaticTextSelectDifficulty").SetHidden(true);
        W("WinGrowMarker").SetHidden(true);
        W("Logo").SetHidden(false);
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
    /// Opens another screen: with transitions the menu first plays <paramref name="play"/> (the flash-out groups) and <paramref name="open"/> runs when the groups
    /// are done; without them it runs at once. <c>Shell::push</c> only starts the shutdown of the top screen, <c>MainMenuUpdate</c> finishes it
    /// (<c>isAnimFinished</c> and <c>TheTransitionHandler->isFinished</c>) and the new layout is created then.
    /// </summary>
    private void LeaveFor(Action open, Action<WindowTransitionHandler> play)
    {
        // The screen below is shut down for good: when it is closed the menu starts over (MainMenuInit runs again, here OnRevealed), with or without animation.
        _leaving = true;
        if (Transitions is not { } transitions)
        {
            open();
            return;
        }
        _campaignSelected = true;
        _open = open;
        play(transitions);
    }

    private void LeaveFor(Action open, string? removeGroup, string reverseGroup)
        => LeaveFor(open, transitions =>
        {
            if (removeGroup is not null) transitions.Remove(removeGroup);
            transitions.Reverse(reverseGroup);
        });

    private CampaignDefinition? _campaign;

    /// <summary>The USA/GLA/China/Challenge button: the single-player column gives way to the difficulty question (<c>MainMenu.cpp</c>, <c>GBM_SELECTED</c> of <c>buttonUSAID</c> and the like).</summary>
    private void OpenDifficulty(Faction faction)
    {
        CampaignDefinition? campaign = null;
        if (faction.Campaign is not null && (campaign = Shell.Campaigns?.Find(faction.Campaign)) is null) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        _campaign = campaign;
        _faction = faction;
        _submenu = Submenu.Difficulty;
        _campaignSelected = true;
        _pendingLogo = null;
        if (Transitions is not { } transitions)
        {
            // Resting state of the groups below: the single-player frame and its five logos gone, the difficulty frame with its text shown, and the faction's
            // logo grown into WinGrowMarker (MAINMENUSCALEUP ends by handing the disabled image of WinFaction<X>Medium to the marker and showing it).
            W("MapBorder").SetHidden(true);
            W("MapBorder4").SetHidden(false);
            W("StaticTextSelectDifficulty").SetHidden(false);
            foreach (var logo in FactionLogos) W(logo).SetHidden(true);
            W("WinGrowMarker").SetEnabledImage0(W(faction.Logo + "Medium").Definition.GetDrawData(WndDrawDataKind.Disabled)[0].Image);
            W("WinGrowMarker").SetHidden(false);
            // MainMenuSinglePlayerMenuBackTraining fades the title logo out (WINFADE, hidden at the end of its reversed run) and no later group of the Challenge
            // branch brings it back (MainMenuSinglePlayerTrainingMenuFromDiff does not name it): it returns with the entry group when the menu is rebuilt.
            if (campaign is null) W("Logo").SetHidden(true);
            return;
        }
        transitions.SetGroup(faction.HoverGroup);
        if (campaign is not null) transitions.Remove(faction.HoverGroup, skipPending: true);
        W(faction.Logo).SetHidden(true);
        transitions.Reverse(faction.BackFromSinglePlayer);
        transitions.SetGroup(faction.Difficulty);
    }

    /// <summary>Easy/Medium/Hard (<c>checkCDBeforeCampaign</c> without the CD check, <c>prepareCampaignGame</c>, <c>setupGameStart</c>): a campaign fades out and starts; Challenge keeps the choice for its menu.</summary>
    private void ChooseDifficulty(ScriptDifficulty difficulty)
    {
        if (_faction is not { } faction) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        if (faction.Campaign is null)
        {
            LeaveFor(() => Shell.OpenChallengeSelect(difficulty), transitions =>
            {
                _dontAllow = true;
                transitions.Reverse("MainMenuDifficultyMenuBack");
                transitions.Reverse(faction.Difficulty);
            });
            return;
        }
        var campaign = _campaign ?? throw new InvalidOperationException("The difficulty question of a campaign button is open without its campaign.");
        LeaveFor(() => Shell.BeginCampaign(campaign, difficulty), transitions =>
        {
            _dontAllow = true;
            transitions.Reverse("MainMenuDifficultyMenuBack");
            transitions.SetGroup("FadeWholeScreen");
        });
    }

    /// <summary><c>buttonDiffBackID</c> and <c>diffReverseSide</c>: back to the single-player column, with the faction's logo returning to its place.</summary>
    private void CloseDifficulty()
    {
        if (_faction is not { } faction) return;
        _campaign = null;
        _faction = null;
        _campaignSelected = false;
        _submenu = Submenu.SinglePlayer;
        if (Transitions is not { } transitions)
        {
            W("MapBorder4").SetHidden(true);
            W("StaticTextSelectDifficulty").SetHidden(true);
            W("WinGrowMarker").SetHidden(true);
            W("MapBorder").SetHidden(false);
            foreach (var logo in FactionLogos) W(logo).SetHidden(false);
            return;
        }
        _dontAllow = true;
        transitions.Reverse(faction.DifficultyBack);
        transitions.SetGroup(faction.FromDifficulty);
    }

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
            case "MainMenu.wnd:ButtonUSA" or "MainMenu.wnd:ButtonGLA" or "MainMenu.wnd:ButtonChina" or "MainMenu.wnd:ButtonChallenge":
                if (!_campaignSelected) OpenDifficulty(Factions.Single(faction => "MainMenu.wnd:" + faction.Button == e.Source));
                break;
            case "MainMenu.wnd:ButtonEasy": ChooseDifficulty(ScriptDifficulty.Easy); break;
            case "MainMenu.wnd:ButtonMedium": ChooseDifficulty(ScriptDifficulty.Normal); break;
            case "MainMenu.wnd:ButtonHard": ChooseDifficulty(ScriptDifficulty.Hard); break;
            case "MainMenu.wnd:ButtonDiffBack": Shell.PlaySound(ShellUiSounds.ButtonClick); CloseDifficulty(); break;
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
        if (_submenu == Submenu.Difficulty)
        {
            if (!_dontAllow) CloseDifficulty();
        }
        else if (SinglePlayerSubmenuOpen || LoadReplaySubmenuOpen)
        {
            if (!_dontAllow && !_campaignSelected) SwitchTo(Submenu.Main);
        }
        else Shell.OpenQuitConfirm();
    }
}
