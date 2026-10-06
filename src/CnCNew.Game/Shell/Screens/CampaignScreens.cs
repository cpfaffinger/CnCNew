using System.Collections.Immutable;
using CnCNew.Game.Campaign;
using CnCNew.Game.Save;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ChallengeMenu.wnd</c> (<c>ChallengeMenu.cpp</c>): the nine Generals' Challenge opponents of <c>ChallengeMode.ini</c> on twelve <c>GeneralPositionN</c> buttons
/// (<see cref="CampaignCatalog.Personas"/>; a persona that does not start enabled or has no campaign keeps its button hidden, as <c>setEnabledButtons</c> does).
/// The difficulty was chosen on the main menu before this screen opened (<c>setupGameStart</c> with <c>launchChallengeMenu</c>:
/// <c>TheChallengeGenerals->setCurrentDifficulty</c>) and is what the campaign starts with. Like <c>ChallengeMenuInit</c> the biography panel and the Play button
/// stay hidden until a general is picked (<c>setGeneralBio</c>); then the four lines (<c>BioNameEntry</c>, <c>BioDOBEntry</c>, <c>BioBirthplaceEntry</c>,
/// <c>BioStrategyEntry</c>, repurposed by the original for name, rank, branch and strategy) show the persona's labels at once; the original types them out one
/// letter per frame (<c>updateBio</c>). The buttons show the medallions of the general's player template (<c>MedallionRegular</c>/<c>Hilite</c>/<c>Select</c>); the
/// biography portrait (<c>BioPortrait</c>) and the voice/preview sounds are not implemented.
/// </summary>
public sealed class ChallengeSelectScreen : ShellScreen
{
    private const int Slots = 12;
    private readonly ImmutableArray<CampaignDefinition?> _slots;
    private readonly ImmutableArray<ChallengePersona?> _personas;
    private readonly ScriptDifficulty _difficulty;
    private int _selected = -1;

    public ChallengeSelectScreen(GameShell shell, ScriptDifficulty difficulty) : base(shell, ShellLayoutNames.ChallengeMenu)
    {
        _difficulty = difficulty;
        var personas = ImmutableArray.CreateBuilder<ChallengePersona?>(Slots);
        var slots = ImmutableArray.CreateBuilder<CampaignDefinition?>(Slots);
        for (var index = 0; index < Slots; index++)
        {
            var persona = shell.Campaigns?.Personas.FirstOrDefault(candidate => candidate.Index == index);
            var campaign = persona is { StartsEnabled: true } ? shell.Campaigns!.Find(persona.Campaign) : null;
            personas.Add(persona);
            slots.Add(campaign);
        }
        _personas = personas.ToImmutable();
        _slots = slots.ToImmutable();
        Tree.Register("ChallengeMenuSystem", OnSystem);
        Tree.Register("ChallengeMenuInput", OnInput);
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        for (var index = 0; index < Slots; index++)
        {
            var button = TryW($"GeneralPosition{index}");
            button?.SetEnabled(_slots[index] is not null);
            button?.SetHidden(_slots[index] is null);
            if (button is null || _slots[index] is null || names.Medallions(_personas[index]!.PlayerTemplate) is not { } medallion) continue;
            // setEnabledButtons: the general's medallions replace the placeholder images, and "image size keeps changing, so it'll drive the window size directly".
            button.SetDrawImage(WndDrawDataKind.Enabled, 0, medallion.Regular);
            button.SetDrawImage(WndDrawDataKind.Hilite, 0, medallion.Hilite);
            button.SetDrawImage(WndDrawDataKind.Hilite, 1, medallion.Select);
            button.SetDrawImage(WndDrawDataKind.Disabled, 1, medallion.Select);
            if (Tree.Services.Images is { } images && images.TryGetSize(medallion.Regular, out var width, out _)) button.SetDesignSize(width, width);
        }
        W("GeneralsBioParent").SetHidden(true);
        W("ButtonPlay").SetHidden(true);
        // ChallengeMenuInit: the menu fades in (FULLFADE of GadgetParent); ChallengeMenuShutdown reverses it.
        Transitions?.SetGroup("ChallengeMenuFade");
    }

    protected override string? ExitTransitionGroup => "ChallengeMenuFade";

    private void Select(int index)
    {
        _selected = index;
        var persona = _personas[index]!;
        W("GeneralsBioParent").SetHidden(false);
        W("ButtonPlay").SetHidden(false);
        W("BioNameEntry").SetText(Text(persona.BioName));
        W("BioDOBEntry").SetText(Text(persona.BioRank));
        W("BioBirthplaceEntry").SetText(Text(persona.BioBranch));
        W("BioStrategyEntry").SetText(Text(persona.BioStrategy));
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        for (var index = 0; index < Slots; index++)
        {
            if (e.Source != $"ChallengeMenu.wnd:GeneralPosition{index}" || _slots[index] is null) continue;
            Shell.PlaySound(ShellUiSounds.ButtonClick);
            Select(index);
            return;
        }
        switch (e.Source)
        {
            case "ChallengeMenu.wnd:ButtonPlay":
                if (_selected < 0 || _slots[_selected] is not { } campaign) return;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.BeginCampaign(campaign, _difficulty);
                break;
            case "ChallengeMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

/// <summary>
/// <c>SaveLoad.wnd</c>: the main-menu load-game list, from <see cref="GameShell.Saves"/>
/// (<see cref="SaveGameStore.List"/>). A damaged file shows its <see cref="SaveSlot.Problem"/> instead of a title and
/// cannot be loaded. There is no running mission to save from the main menu, so <c>ButtonSave</c> stays disabled here
/// (the in-mission pause menu is where saving happens, see <c>docs/GAME-HUD.md</c>); delete is available either way.
/// </summary>
public sealed class LoadGameScreen : ShellScreen
{
    private ImmutableArray<SaveSlot> _slots = [];
    private int _selected = -1;

    public LoadGameScreen(GameShell shell) : base(shell, ShellLayoutNames.SaveLoad)
    {
        Tree.Register("SaveLoadMenuSystem", OnSystem);
        Tree.Register("SaveLoadMenuInput", OnInput);
        // SaveLoadMenuFullScreenInit: winSetFocus(SaveLoadMenu). That child carries the INPUTCALLBACK (the root of the file has none), so Escape only
        // reaches it from there; without a gadget focus the keys would otherwise start at the root and find nobody.
        Tree.KeyboardHome = W("SaveLoadMenu");
        W("ButtonSave").SetEnabled(false);
        HideConfirmPanels();
        Refresh();
        // PopupSaveLoad.cpp: SaveLoadMenuFade (FULLFADE of SaveLoadMenu) on entry, reversed on shutdown.
        Transitions?.SetGroup("SaveLoadMenuFade");
    }

    protected override string? ExitTransitionGroup => "SaveLoadMenuFade";

    private void HideConfirmPanels()
    {
        W("LoadConfirmParent").SetHidden(true);
        W("OverwriteConfirmParent").SetHidden(true);
        W("SaveDescParent").SetHidden(true);
        W("DeleteConfirmParent").SetHidden(true);
    }

    private void Refresh()
    {
        _slots = Shell.Saves!.List();
        _selected = -1;
        var list = W("ListboxGames");
        list.ClearList();
        foreach (var slot in _slots)
            list.AddListRow(new UiListRow(slot.Header is { } header ? $"{header.Title} ({header.GameTime})" : $"{slot.Name} ({slot.Problem})"));
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var hasSelection = _selected >= 0 && _selected < _slots.Length;
        W("ButtonLoad").SetEnabled(hasSelection && _slots[_selected].Header is not null);
        W("ButtonDelete").SetEnabled(hasSelection);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "SaveLoad.wnd:ListboxGames":
                _selected = e.Value;
                UpdateButtons();
                return;
            case UiEventKind.ListDoubleClicked when e.Source == "SaveLoad.wnd:ListboxGames":
                _selected = e.Value;
                UpdateButtons();
                if (_selected >= 0 && _selected < _slots.Length && _slots[_selected].Header is not null) ShowLoadConfirm();
                return;
            case UiEventKind.ButtonSelected:
                break;
            default:
                return;
        }
        switch (e.Source)
        {
            case "SaveLoad.wnd:ButtonLoad":
                if (_selected >= 0 && _selected < _slots.Length && _slots[_selected].Header is not null) ShowLoadConfirm();
                break;
            case "SaveLoad.wnd:ButtonLoadConfirm": AcceptLoad(); break;
            case "SaveLoad.wnd:ButtonLoadCancel": W("LoadConfirmParent").SetHidden(true); break;
            case "SaveLoad.wnd:ButtonDelete": if (_selected >= 0 && _selected < _slots.Length) W("DeleteConfirmParent").SetHidden(false); break;
            case "SaveLoad.wnd:ButtonDeleteConfirm":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                if (_selected >= 0 && _selected < _slots.Length) Shell.Saves!.Delete(_slots[_selected].Name);
                W("DeleteConfirmParent").SetHidden(true);
                Refresh();
                break;
            case "SaveLoad.wnd:ButtonDeleteCancel": W("DeleteConfirmParent").SetHidden(true); break;
            case "SaveLoad.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
        }
    }

    private void ShowLoadConfirm()
    {
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        W("LoadConfirmParent").SetHidden(false);
    }

    private void AcceptLoad()
    {
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        W("LoadConfirmParent").SetHidden(true);
        if (_selected < 0 || _selected >= _slots.Length) return;
        var name = _slots[_selected].Name;
        try
        {
            Shell.LoadSaveAndContinue(name);
        }
        catch (SaveGameException exception)
        {
            Shell.ShowError("Cannot Load", exception.Message);
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

/// <summary>
/// <c>SinglePlayerLoadScreen.wnd</c> reused as the mission briefing (the retail file already carries an
/// <c>ObjectivesWin</c> group with five <c>StaticTextLine</c> rows and three cameo+text slots, matching
/// <see cref="CampaignMission.ObjectiveLabels"/>/<see cref="CampaignMission.UnitNames"/> exactly: the loading screen
/// doubled as the briefing while the mission's intro movie and voice-over played over it in the retail game). There is
/// no Bink decoder in this build (documented permanent limit, see <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>), so
/// <see cref="CampaignMission.IntroMovie"/>/<see cref="CampaignMission.BriefingVoice"/> show as a named placeholder in
/// the progress label instead of actually playing; <see cref="GameShell"/> holds this screen up for
/// <see cref="CampaignMission.VoiceLengthSeconds"/> so the player has time to read the objectives before the mission
/// starts. The file declares no <c>SYSTEMCALLBACK</c>/<c>INPUTCALLBACK</c> (confirmed against the shipped data, same as
/// <see cref="LoadingScreen"/>), so there is deliberately no click-to-skip here either.
/// </summary>
public sealed class MissionBriefingScreen : ShellScreen
{
    public MissionBriefingScreen(GameShell shell, CampaignMission mission) : base(shell, ShellLayoutNames.SinglePlayerLoadScreen)
    {
        for (var line = 0; line < 5; line++)
            W($"StaticTextLine{line}").SetText(line < mission.ObjectiveLabels.Length ? Text(mission.ObjectiveLabels[line]) : string.Empty);
        W("StaticTextCameoText0").SetText(mission.LocationLabel.Length > 0 ? Text(mission.LocationLabel) : string.Empty);
        for (var unit = 0; unit < 3; unit++)
            W($"StaticTextCameoText{unit + 1}").SetText(unit < mission.UnitNames.Length ? Text(mission.UnitNames[unit]) : string.Empty);
        var placeholder = mission.IntroMovie.Length > 0 ? $"(movie placeholder: {mission.IntroMovie})" : "Loading...";
        W("Percent").SetText(placeholder);
    }
}

/// <summary>The victory/defeat banner (<c>Victorious.wnd</c>/<c>Defeat.wnd</c>) pushed underneath
/// <see cref="MissionResultScreen"/> by <see cref="GameShell.MissionFinished"/>. Neither shipped file declares a
/// callback or a button (confirmed against the shipped data): they are pure decoration, same as the retail game, which
/// shows them for a few seconds before the interactive result screen.</summary>
public sealed class MissionBannerScreen : ShellScreen
{
    public MissionBannerScreen(GameShell shell, MissionConclusion? conclusion)
        : base(shell, conclusion == MissionConclusion.Victory ? ShellLayoutNames.Victorious : ShellLayoutNames.Defeat)
    {
    }
}

/// <summary>
/// <c>ScoreScreen.wnd</c> reused as the campaign mission result screen (the retail file's own
/// <c>Victorious.wnd</c>/<c>Defeat.wnd</c> banners have no buttons at all, see <see cref="MissionBannerScreen"/>, so this
/// is the only shipped, buttoned screen fit for "what next"). Shows one row for the human player (no
/// <c>MatchStatistics</c> tracker exists for missions yet, so every numeric cell is <c>-</c>, same honest-fallback
/// convention as <see cref="ScoreScreenScreen"/>) and repurposes <c>ButtonContinue</c>/<c>ButtonOk</c> for campaign
/// navigation instead of their retail skirmish labels.
/// </summary>
public sealed class MissionResultScreen : ShellScreen
{
    /// <summary>Shown over the victory/defeat banner (<see cref="MissionBannerScreen"/>), which is its backdrop here.</summary>
    public override bool HidesScreensBelow => false;

    public MissionResultScreen(GameShell shell, MissionRun run, CampaignController? controller, MissionConclusion? conclusion) : base(shell, ShellLayoutNames.ScoreScreen)
    {
        Tree.Register("ScoreScreenSystem", OnSystem);
        Tree.Register("ScoreScreenInput", OnInput);

        W("ButtonBuddy").SetEnabled(false);
        W("ButtonSaveReplay").SetEnabled(false);
        W("TextEntryChat").SetHidden(true);
        W("ButtonEmote").SetHidden(true);
        W("ListboxChatWindowScoreScreen").SetHidden(true);

        for (var row = 0; row < 8; row++)
        {
            var isHumanRow = row == 0;
            foreach (var prefix in RowFieldPrefixes) W($"{prefix}{row}").SetHidden(!isHumanRow);
            if (!isHumanRow) continue;
            W("StaticTextPlayer0").SetText(run.Mission?.Name ?? run.MapPath);
            foreach (var prefix in NumericPrefixes) W($"{prefix}0").SetText("-");
            W("StaticTextObserver0").SetHidden(true);
            W("GameWindowWinner0").SetHidden(conclusion != MissionConclusion.Victory);
        }

        var finished = controller?.IsFinished ?? true;
        W("ButtonContinue").SetEnabled(controller is not null);
        W("ButtonContinue").SetText(conclusion switch
        {
            MissionConclusion.Victory when !finished => "Next Mission",
            MissionConclusion.Victory => "Campaign Complete",
            MissionConclusion.Defeat => "Retry Mission",
            _ => "Continue",
        });
        if (conclusion == MissionConclusion.Victory && finished) W("ButtonContinue").SetEnabled(false);
        W("ButtonOk").SetTextLabel("GUI:MainMenu");

        _run = run;
        _controller = controller;
    }

    private readonly MissionRun _run;
    private readonly CampaignController? _controller;

    private static readonly string[] RowFieldPrefixes =
    [
        "StaticTextPlayer", "StaticTextUnitsBuilt", "StaticTextUnitsLost", "StaticTextUnitsDestroyed",
        "StaticTextBuildingsBuilt", "StaticTextBuildingsLost", "StaticTextBuildingsDestroyed", "StaticTextResources",
        "StaticTextObserver", "GameWindowWinner",
    ];

    private static readonly string[] NumericPrefixes =
    [
        "StaticTextUnitsBuilt", "StaticTextUnitsLost", "StaticTextUnitsDestroyed",
        "StaticTextBuildingsBuilt", "StaticTextBuildingsLost", "StaticTextBuildingsDestroyed", "StaticTextResources",
    ];

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "ScoreScreen.wnd:ButtonContinue":
                if (_controller is null) return;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.ContinueOrRetryMission(_controller);
                break;
            case "ScoreScreen.wnd:ButtonOk":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.ReturnToMainMenuFromMissionResults();
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e) { }
}
