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
/// stay hidden until a general is picked (<c>setGeneralBio</c>); then the portrait shows and the four lines (<c>BioNameEntry</c>, <c>BioDOBEntry</c>, <c>BioBirthplaceEntry</c>,
/// <c>BioStrategyEntry</c>, repurposed by the original for name, rank, branch and strategy) are typed out two letters per update (<c>updateBio</c>; a shell without animations
/// shows them at once). A general under the pointer shows his bio; leaving him shows the chosen one's again. The buttons show the medallions of the general's player template
/// (<c>MedallionRegular</c>/<c>Hilite</c>/<c>Select</c>). <b>Nothing is unlocked:</b> the original has no such step, every general with <c>StartsEnabled</c> is open from the start, and what a person
/// has beaten shows only as a medal in the skirmish setup (<see cref="BattleHonors"/>).
/// </summary>
public sealed class ChallengeSelectScreen : ShellScreen
{
    private const int Slots = 12;
    private readonly ImmutableArray<CampaignDefinition?> _slots;
    private readonly ImmutableArray<ChallengePersona?> _personas;
    private readonly ScriptDifficulty _difficulty;
    private int _selected = -1;

    // ChallengeMenuUpdate runs once per frame of the shell, 30 a second.
    private const double UpdateSeconds = 1.0 / CnCNew.Data.OriginalFrame.PerSecond;
    private const int TeletypeSkip = 2;
    private const int IntroVoiceUpdates = 10;
    private double _updateClock;
    private int _introUpdates;
    private bool _introPlayed;
    private readonly string[] _bio = [string.Empty, string.Empty, string.Empty, string.Empty];
    private int _bioPosition;
    private static readonly string[] BioWindows = ["BioNameEntry", "BioDOBEntry", "BioBirthplaceEntry", "BioStrategyEntry"];

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

    /// <summary><c>setGeneralBio</c>: the portrait at once, the four lines cleared; <see cref="Update"/> types them out.</summary>
    private void ShowBio(int index)
    {
        if (index < 0) return;
        var persona = _personas[index]!;
        W("GeneralsBioParent").SetHidden(false);
        if (persona.BioPortraitSmall.Length > 0) W("BioPortrait").SetEnabledImage0(persona.BioPortraitSmall);
        _bio[0] = Text(persona.BioName);
        _bio[1] = Text(persona.BioRank);
        _bio[2] = Text(persona.BioBranch);
        _bio[3] = Text(persona.BioStrategy);
        _bioPosition = 0;
        foreach (var window in BioWindows) W(window).SetText(string.Empty);
        // A shell without animations shows the end of what is typed.
        if (Transitions is null) AdvanceBio(int.MaxValue);
    }

    /// <summary><c>updateBio</c>: the next characters of the four lines, one line after the other.</summary>
    private void AdvanceBio(int characters)
    {
        var total = _bio.Sum(line => line.Length);
        _bioPosition = Math.Min(total, characters == int.MaxValue ? total : _bioPosition + characters);
        var left = _bioPosition;
        for (var line = 0; line < _bio.Length; line++)
        {
            var shown = Math.Min(left, _bio[line].Length);
            W(BioWindows[line]).SetText(_bio[line][..shown]);
            left -= shown;
        }
    }

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        _updateClock += dtSeconds;
        while (_updateClock >= UpdateSeconds)
        {
            _updateClock -= UpdateSeconds;
            AdvanceBio(TeletypeSkip);
            // "Choose your general", some updates after the transition is over.
            if (!_introPlayed && (Transitions?.IsSettled ?? true) && ++_introUpdates == IntroVoiceUpdates)
            {
                _introPlayed = true;
                Shell.PlaySound("Taunts_GCAnnouncer01");
            }
        }
    }

    private void Select(int index)
    {
        _selected = index;
        W("ButtonPlay").SetHidden(false);
    }

    private int PositionOf(string source)
    {
        for (var index = 0; index < Slots; index++)
            if (source == $"ChallengeMenu.wnd:GeneralPosition{index}") return index;
        return -1;
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        // GBM_MOUSE_ENTERING and GBM_MOUSE_LEAVING: a general under the pointer shows his bio; leaving him shows the chosen one's again.
        if (e.Kind == UiEventKind.ButtonMouseEntering && PositionOf(e.Source) is var entered and >= 0 && entered != _selected && _slots[entered] is not null)
        {
            ShowBio(entered);
            Shell.PlaySound("GUILogoMouseOver");
            return;
        }
        if (e.Kind == UiEventKind.ButtonMouseLeaving && PositionOf(e.Source) is var left and >= 0 && left != _selected)
        {
            ShowBio(_selected);
            return;
        }
        if (e.Kind != UiEventKind.ButtonSelected) return;
        if (PositionOf(e.Source) is var position and >= 0 && _slots[position] is not null)
        {
            // Exactly one button stays chosen; the choice plays the general's preview line.
            if (_selected >= 0 && _selected != position) W($"GeneralPosition{_selected}").SetChecked(false);
            if (_selected != position) ShowBio(position);
            Select(position);
            Shell.PlayNamedSound(_personas[position]!.PreviewSound);
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
/// doubled as the briefing while the mission's intro movie and voice-over played over it in the retail game). The movie
/// is drawn over the screen's background and under its children, as <c>W3DGameWinDefaultDraw</c> draws a window's video
/// buffer (<c>SinglePlayerLoadScreen::init</c>), and only Escape ends it early (<see cref="GameShell.HandleInput"/>).
/// Without a movie <see cref="CampaignMission.IntroMovie"/> shows as a named placeholder in the progress label and
/// <see cref="GameShell"/> holds this screen up for <see cref="CampaignMission.VoiceLengthSeconds"/> so the player has
/// time to read the objectives before the mission starts. The file declares no <c>SYSTEMCALLBACK</c>/<c>INPUTCALLBACK</c>
/// (confirmed against the shipped data, same as <see cref="LoadingScreen"/>), so there is deliberately no click-to-skip here.
/// </summary>
public sealed class MissionBriefingScreen : ShellScreen
{
    private readonly IUiVideoSurface? _movie;

    /// <summary>The movie is drawn into the root window, between its picture and its children; the objectives wait behind it (Zero Hour's load screen shows the movie and the bar alone).</summary>
    public override UiDrawList BuildDrawList()
    {
        var list = base.BuildDrawList();
        if (_movie is null) return list;
        var background = 0;
        while (background < list.Commands.Length && list.Commands[background] is not UiImageDraw) background++;
        if (background == list.Commands.Length) background = -1;
        return list with { Commands = list.Commands.Insert(background + 1, new UiVideoDraw(_movie, W("ParentSinglePlayerLoadScreen").Rect)) };
    }

    public MissionBriefingScreen(GameShell shell, CampaignMission mission, IUiVideoSurface? movie = null) : base(shell, ShellLayoutNames.SinglePlayerLoadScreen)
    {
        _movie = movie;
        if (movie is not null) HideWindow("ObjectivesWin");
        for (var line = 0; line < 5; line++)
            W($"StaticTextLine{line}").SetText(line < mission.ObjectiveLabels.Length ? Text(mission.ObjectiveLabels[line]) : string.Empty);
        W("StaticTextCameoText0").SetText(mission.LocationLabel.Length > 0 ? Text(mission.LocationLabel) : string.Empty);
        for (var unit = 0; unit < 3; unit++)
            W($"StaticTextCameoText{unit + 1}").SetText(unit < mission.UnitNames.Length ? Text(mission.UnitNames[unit]) : string.Empty);
        var placeholder = mission.IntroMovie.Length > 0 && movie is null ? $"(movie placeholder: {mission.IntroMovie})" : "Loading...";
        W("Percent").SetText(placeholder);
    }
}

/// <summary>
/// <c>ChallengeLoadScreen.wnd</c>, <c>ChallengeLoadScreen::init</c>: the player's general on the left and the opponent's on the right over the mission's background movie. The pieces come
/// at frames of that movie (<c>activatePieces</c>): the titles, the bio typed out, the portraits - movies that keep their last frame - with the player's name called, the reticle, the
/// "VS" movie with its call, the opponent's name; when the movie ends the opponent taunts and the mission starts. A machine without the movie gets every piece at once and holds for the
/// voice length (<c>activatePiecesMinSpec</c>), with the large portrait pictures.
/// </summary>
public sealed class ChallengeLoadScreen : ShellScreen
{
    // LoadScreen.cpp: the frames of the background movie.
    internal const int FrameTitlesStart = 20, FrameTeletypeStart = 24, FrameFudgeAdd = 30, FramePortraitsStart = 35, FrameOuterAlphaShow = 63, FrameInnerAlphaShow = 74,
        FrameBackdropShow = 80, FrameVsStart = 98, FrameRightVoice = 140, TeletypeUpdateFrames = 2;

    private static readonly string[] Titles = ["BioNameLeft", "BioBirthplaceLeft", "BioStrategyLeft", "BioNameRight", "BioBirthplaceRight", "BioStrategyRight"];
    private static readonly string[] Reticle = ["CircleAlphaOuter", "CircleAlphaInner", "VersusBackdrop", "OverlayVs"];
    private readonly ChallengePersona _player;
    private readonly ChallengePersona _opponent;
    private readonly CnCNew.Game.Movies.MovieClip? _background;
    private readonly List<CnCNew.Game.Movies.MovieClip> _opened = [];
    private readonly (string Window, string Text)[] _lines;
    private int _frame = -1;
    private int _typed;

    public ChallengeLoadScreen(GameShell shell, ChallengePersona player, ChallengePersona opponent, CnCNew.Game.Movies.MovieClip? background) : base(shell, ShellLayoutNames.ChallengeLoadScreen)
    {
        _player = player;
        _opponent = opponent;
        _background = background;
        _lines =
        [
            ("BioNameEntryLeft", Text(player.BioName)), ("BigNameEntryLeft", Text(player.BioName)), ("BioBirthplaceEntryLeft", Text(player.BioRank)), ("BioStrategyEntryLeft", Text(player.BioStrategy)),
            ("BioNameEntryRight", Text(opponent.BioName)), ("BigNameEntryRight", Text(opponent.BioName)), ("BioBirthplaceEntryRight", Text(opponent.BioRank)), ("BioStrategyEntryRight", Text(opponent.BioStrategy)),
        ];
        W("ProgressLoad").SetProgress(0);
        foreach (var window in Reticle) W(window).SetHidden(true);
        foreach (var name in new[] { "PortraitLeft", "PortraitRight", "PortraitMovieLeft", "PortraitMovieRight" }) W(name).SetHidden(true);
        foreach (var name in Titles) W(name).SetHidden(true);
        foreach (var (window, _) in _lines) W(window).SetHidden(true);
        if (background is not null)
        {
            W("ParentChallengeLoadScreen").Video = background;
            return;
        }
        // activatePiecesMinSpec.
        foreach (var name in Titles) W(name).SetHidden(false);
        foreach (var (window, text) in _lines)
        {
            W(window).SetHidden(false);
            W(window).SetText(text);
        }
        if (player.BioPortraitLarge.Length > 0) W("PortraitLeft").SetEnabledImage0(player.BioPortraitLarge);
        if (opponent.BioPortraitLarge.Length > 0) W("PortraitRight").SetEnabledImage0(opponent.BioPortraitLarge);
        W("PortraitLeft").SetHidden(false);
        W("PortraitRight").SetHidden(false);
        foreach (var window in Reticle) W(window).SetHidden(false);
        Play("OverlayVs", "VSSmall");
    }

    private void Play(string window, string movie)
    {
        if (movie.Length == 0 || Shell.Movies?.Open(movie) is not { } clip) return;
        _opened.Add(clip);
        W(window).Video = clip;
    }

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        if (_background is not { } movie) return;
        for (var frame = _frame + 1; frame <= movie.Frame; frame++) Activate(frame);
        _frame = Math.Max(_frame, movie.Frame);
        // Int percent = (shiftedPercent + FRAME_FUDGE_ADD)/1.3: the movie is the first 30 of the bar's 130 steps.
        if (movie.DurationSeconds > 0) W("ProgressLoad").SetProgress((int)(Math.Min(1, movie.Seconds / movie.DurationSeconds) * FrameFudgeAdd / 1.3));
    }

    private void Activate(int frame)
    {
        switch (frame)
        {
            case FrameTitlesStart:
                foreach (var name in Titles) W(name).SetHidden(false);
                break;
            case FrameTeletypeStart:
                _typed = 0;
                foreach (var (window, _) in _lines)
                {
                    W(window).SetHidden(false);
                    W(window).SetText(string.Empty);
                }
                break;
            case FramePortraitsStart:
                Play("PortraitMovieLeft", _player.PortraitMovieLeft);
                Play("PortraitMovieRight", _opponent.PortraitMovieRight);
                W("PortraitMovieLeft").SetHidden(false);
                W("PortraitMovieRight").SetHidden(false);
                Shell.PlayNamedSound(_player.NameSound);
                break;
            case FrameOuterAlphaShow: W("CircleAlphaOuter").SetHidden(false); break;
            case FrameInnerAlphaShow: W("CircleAlphaInner").SetHidden(false); break;
            case FrameBackdropShow: W("VersusBackdrop").SetHidden(false); break;
            case FrameVsStart:
                W("VersusBackdrop").SetHidden(false);
                W("OverlayVs").SetHidden(false);
                Play("OverlayVs", "VSSmall");
                Shell.PlaySound("Taunts_GCAnnouncer12");
                break;
            case FrameRightVoice: Shell.PlayNamedSound(_opponent.NameSound); break;
        }
        // The teletype readout: one character of every line, every second frame.
        if (frame <= FrameTeletypeStart || frame % TeletypeUpdateFrames != 0) return;
        _typed++;
        foreach (var (window, text) in _lines) W(window).SetText(text[..Math.Min(_typed, text.Length)]);
    }

    /// <summary>The end of <c>init</c>: the movie is over, and the opponent says something (<c>getRandomTauntSound</c>, which does not care about its distribution).</summary>
    internal void Finish()
    {
        if (_opponent.Taunts.Length == 3) Shell.PlayNamedSound(_opponent.Taunts[Random.Shared.Next(3)]);
    }

    public override void OnClosed()
    {
        foreach (var clip in _opened) clip.Stop();
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
/// is the only shipped, buttoned screen fit for "what next"). Shows the rows of <c>grabSinglePlayerInfo</c> as <see cref="MissionScore"/> counted them, or without a count one row
/// of <c>-</c> (the convention of <see cref="ScoreScreenScreen"/>), and repurposes <c>ButtonContinue</c>/<c>ButtonOk</c> for campaign
/// navigation instead of their retail skirmish labels.
/// </summary>
public sealed class MissionResultScreen : ShellScreen
{
    /// <summary>Shown over the victory/defeat banner (<see cref="MissionBannerScreen"/>), which is its backdrop here.</summary>
    public override bool HidesScreensBelow => false;

    public MissionResultScreen(GameShell shell, MissionRun run, CampaignController? controller, MissionConclusion? conclusion, ImmutableArray<MissionScoreRow> score = default) : base(shell, ShellLayoutNames.ScoreScreen)
    {
        Tree.Register("ScoreScreenSystem", OnSystem);
        Tree.Register("ScoreScreenInput", OnInput);

        W("ButtonBuddy").SetEnabled(false);
        W("ButtonSaveReplay").SetEnabled(false);
        W("TextEntryChat").SetHidden(true);
        W("ButtonEmote").SetHidden(true);
        W("ListboxChatWindowScoreScreen").SetHidden(true);

        var counted = !score.IsDefaultOrEmpty;
        for (var row = 0; row < 8; row++)
        {
            var shown = counted ? row < score.Length : row == 0;
            foreach (var prefix in RowFieldPrefixes) W($"{prefix}{row}").SetHidden(!shown);
            if (!shown) continue;
            W($"StaticTextObserver{row}").SetHidden(true);
            W($"GameWindowWinner{row}").SetHidden(row != 0 || conclusion != MissionConclusion.Victory);
            if (!counted)
            {
                W("StaticTextPlayer0").SetText(run.Mission?.Name ?? run.MapPath);
                foreach (var prefix in NumericPrefixes) W($"{prefix}0").SetText("-");
                continue;
            }
            W($"StaticTextPlayer{row}").SetText(Text(score[row].Label));
            var tally = score[row].Tally;
            W($"StaticTextUnitsBuilt{row}").SetText(tally.UnitsBuilt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextUnitsLost{row}").SetText(tally.UnitsLost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextUnitsDestroyed{row}").SetText(tally.UnitsDestroyed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextBuildingsBuilt{row}").SetText(tally.BuildingsBuilt.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextBuildingsLost{row}").SetText(tally.BuildingsLost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextBuildingsDestroyed{row}").SetText(tally.BuildingsDestroyed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            W($"StaticTextResources{row}").SetText(tally.Resources.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        if (controller is { Campaign.IsChallenge: true } && run.Mission is { } mission && Shell.Campaigns?.PersonaNamed(mission.GeneralName) is { } general) ShowChallenge(general, conclusion == MissionConclusion.Victory);
    }

    /// <summary>
    /// <c>displayChallengeWinLoss</c>: the score gives way to the opponent's picture and words on <c>GeneralsChallengeWinLoss</c>; the header names him with <c>GUI:ChallengeWinText</c> or
    /// <c>GUI:ChallengeLossText</c>, his own line is said, and a challenge has no replay.
    /// </summary>
    private void ShowChallenge(ChallengePersona general, bool won)
    {
        W("MainBackdrop").SetHidden(true);
        W("GadgetParent").SetHidden(true);
        W("ButtonSaveReplay").SetHidden(true);
        W("ButtonBuddy").SetHidden(true);
        W("ParentScoreScreen").SetEnabledImage0("GeneralsChallengeWinLoss");
        var portrait = W("BigPortrait");
        portrait.SetHidden(false);
        var image = won ? general.DefeatedImage : general.VictoriousImage;
        if (image.Length > 0) portrait.SetEnabledImage0(image);
        var header = Shell.RawText is { } text ? text.Format(won ? "GUI:ChallengeWinText" : "GUI:ChallengeLossText", Text(general.BioName)) : Text(general.BioName);
        W("ChallengeWinLossText").SetHidden(false);
        W("ChallengeWinLossText").SetText(header);
        W("GeneralRemarks").SetHidden(false);
        W("GeneralRemarks").SetText(Text(won ? general.DefeatedString : general.VictoriousString));
        Shell.PlayNamedSound(won ? general.WinSound : general.LossSound);
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
