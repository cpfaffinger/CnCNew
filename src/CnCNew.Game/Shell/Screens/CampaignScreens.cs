using System.Collections.Immutable;
using CnCNew.Game.Campaign;
using CnCNew.Game.Save;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>SinglePlayerMenu.wnd</c>: New Game/Load Game/Back for one faction's campaign (reached from <c>MainMenu.wnd</c>'s
/// USA/GLA/China buttons, see <see cref="Screens.MainMenuScreen"/>). New Game asks the difficulty
/// (<see cref="DifficultySelectScreen"/>) and then starts <see cref="CampaignDefinition.FirstMission"/>; Load Game opens
/// the shared <see cref="LoadGameScreen"/> (not filtered by faction: the retail file does not either).
/// </summary>
public sealed class CampaignFactionScreen : ShellScreen
{
    private readonly CampaignDefinition _campaign;

    public CampaignFactionScreen(GameShell shell, CampaignDefinition campaign) : base(shell, ShellLayoutNames.SinglePlayerMenu)
    {
        _campaign = campaign;
        Tree.Register("SinglePlayerMenuSystem", OnSystem);
        Tree.Register("SinglePlayerMenuInput", OnInput);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "SinglePlayerMenu.wnd:ButtonNew":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenDifficultySelect(difficulty => Shell.BeginCampaign(_campaign, difficulty));
                break;
            case "SinglePlayerMenu.wnd:ButtonLoad":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenLoadGame();
                break;
            case "SinglePlayerMenu.wnd:ButtonBack":
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
/// <c>ChallengeMenu.wnd</c>: picks one of the <c>IsChallengeCampaign</c> campaigns of <c>Campaign.ini</c> (the nine
/// Generals' Challenge opponents) across its twelve <c>GeneralPosition</c> slots (the retail file has room for more
/// generals than Zero Hour ships; the unused slots stay disabled). Challenge mode fixes the difficulty to Hard (the
/// retail shell does not ask either: the bios already encode the intended difficulty curve), so there is no
/// <see cref="DifficultySelectScreen"/> step here. The bio panel shows the campaign's name label and its first
/// mission's <c>GeneralName</c>; the full biography text fields (strategy/birthplace/DOB) have no source data in this
/// build and are left at their shipped placeholder text.
/// </summary>
public sealed class ChallengeSelectScreen : ShellScreen
{
    private const int Slots = 12;
    private readonly ImmutableArray<CampaignDefinition> _challenges;
    private int _selected = -1;

    public ChallengeSelectScreen(GameShell shell) : base(shell, ShellLayoutNames.ChallengeMenu)
    {
        _challenges = [.. (shell.Campaigns?.Campaigns ?? []).Where(campaign => campaign.IsChallenge).OrderBy(campaign => campaign.Name, StringComparer.Ordinal)];
        Tree.Register("ChallengeMenuSystem", OnSystem);
        Tree.Register("ChallengeMenuInput", OnInput);
        for (var index = 0; index < Slots; index++) TryW($"GeneralPosition{index}")?.SetEnabled(index < _challenges.Length);
        RefreshBio();
    }

    private void RefreshBio()
    {
        var hasSelection = _selected >= 0 && _selected < _challenges.Length;
        W("ButtonPlay").SetEnabled(hasSelection);
        if (!hasSelection) return;
        var campaign = _challenges[_selected];
        TryW("BioNameEntry")?.SetText(Text(campaign.NameLabel));
        var general = campaign.Missions.Length > 0 ? campaign.Missions[0].GeneralName : string.Empty;
        if (general.Length > 0) TryW("BioBirthplaceEntry")?.SetText(general);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        for (var index = 0; index < _challenges.Length; index++)
        {
            if (e.Source != $"ChallengeMenu.wnd:GeneralPosition{index}") continue;
            Shell.PlaySound(ShellUiSounds.ButtonClick);
            _selected = index;
            RefreshBio();
            return;
        }
        switch (e.Source)
        {
            case "ChallengeMenu.wnd:ButtonPlay":
                if (_selected < 0 || _selected >= _challenges.Length) return;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.BeginCampaign(_challenges[_selected], ScriptDifficulty.Hard);
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

/// <summary><c>DifficultySelect.wnd</c>: Easy/Medium/Hard, defaulting to Medium like the retail dialog. Reports the
/// choice to <paramref name="onAccept"/> and closes; Cancel just closes.</summary>
public sealed class DifficultySelectScreen : ShellScreen
{
    private readonly Action<ScriptDifficulty> _onAccept;
    private ScriptDifficulty _difficulty = ScriptDifficulty.Normal;

    public DifficultySelectScreen(GameShell shell, Action<ScriptDifficulty> onAccept) : base(shell, ShellLayoutNames.DifficultySelect)
    {
        _onAccept = onAccept;
        Tree.Register("DifficultySelectSystem", OnSystem);
        Tree.Register("DifficultySelectInput", OnInput);
        W("RadioButtonMedium").SetChecked(true);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "DifficultySelect.wnd:RadioButtonEasy": _difficulty = ScriptDifficulty.Easy; break;
            case "DifficultySelect.wnd:RadioButtonMedium": _difficulty = ScriptDifficulty.Normal; break;
            case "DifficultySelect.wnd:RadioButtonHard": _difficulty = ScriptDifficulty.Hard; break;
            case "DifficultySelect.wnd:ButtonOk":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                var chosen = _difficulty;
                Shell.CloseTop();
                _onAccept(chosen);
                break;
            case "DifficultySelect.wnd:ButtonCancel":
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
        W("ButtonSave").SetEnabled(false);
        HideConfirmPanels();
        Refresh();
    }

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
            list.AddListRow(new UiListRow(slot.Header is { } header ? $"{header.Title} (tick {header.Tick})" : $"{slot.Name} ({slot.Problem})"));
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
