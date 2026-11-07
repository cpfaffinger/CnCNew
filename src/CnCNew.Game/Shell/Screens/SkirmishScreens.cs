using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Game.Campaign;
using CnCNew.Game.Live;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>SkirmishGameOptionsMenu.wnd</c>: map, human faction/name, up to 7 computer opponents (limited by the selected
/// map's start position count), and starting cash feed a <see cref="MatchSetup"/>. Everything else the layout shows
/// (game speed, superweapon limit) has no counterpart in <c>LiveMatch</c> or
/// <see cref="Simulation.Skirmish.SkirmishMatchOptions"/> yet: the gadgets are populated and stay interactive so the
/// screen looks authentic, but their values are not read at Start time. The opponent slots offer <c>GUI:Open</c>, <c>GUI:Closed</c> and the original's
/// three armies (<c>GUI:EasyAI</c>, <c>GUI:MediumAI</c>, <c>GUI:HardAI</c>): the original AI plays them by its scripts, the built-in AI by its strength (<c>docs/SKIRMISH-AI.md</c>).
/// With the built-in AI three more entries follow - its tactics profiles rush, turtle and eco at medium strength (English names; the original has none). All of this
/// is listed in <c>docs/GAME-SHELL.md</c>. A map is always preselected as in the original; Start does
/// nothing (no inline validation message, just no output) until at least one computer opponent is chosen.
/// </summary>
public sealed class SkirmishSetupScreen : ShellScreen, IMapPickerOwner
{
    private const int Slots = 8;
    /// <summary>The starting cash the setup offers (<c>ComboBoxStartingCash</c>); the network room offers the same list. 75,000 and 100,000 were added on the owner's request.</summary>
    internal static readonly ImmutableArray<int> MoneyChoices = [5_000, 10_000, 15_000, 20_000, 30_000, 40_000, 50_000, 75_000, 100_000];

    private readonly ImmutableArray<string> _factionChoices;
    private readonly bool[] _computer = new bool[Slots];
    private readonly int[] _factionIndex = new int[Slots];
    private int _startPositionCount = Slots;

    /// <summary>
    /// The entries of an opponent slot: <c>GUI:Open</c>, <c>GUI:Closed</c> as <c>SkirmishGameOptionsMenuInit</c> fills them, then the original's three armies easy, medium, hard
    /// (<c>SLOT_EASY_AI</c>, <c>SLOT_MED_AI</c>, <c>SLOT_BRUTAL_AI</c>) - the medium army is the entry after the easy one -, and with the built-in AI its three tactics profiles.
    /// </summary>
    private const int ComputerArmyEntry = 2;
    private const int ProfileEntry = ComputerArmyEntry + 3;
    private static readonly ImmutableArray<CnCNew.Simulation.Skirmish.AiTactics> ProfileEntries =
        [CnCNew.Simulation.Skirmish.AiTactics.Rush, CnCNew.Simulation.Skirmish.AiTactics.Turtle, CnCNew.Simulation.Skirmish.AiTactics.Eco];
    /// <summary>Whether the slot boxes were made for the original AI (<see cref="GameShell.SkirmishAi"/>, the client's <c>--ai original</c>): they then list no tactics profiles.</summary>
    private readonly bool _originalAi;
    /// <summary>
    /// The AI of every slot (entry 0 is the person's and unused): engine, strength and tactics. The slot's box and the AI options screen are two views of this one value; an engine
    /// starts as the shell's <see cref="GameShell.SkirmishAi"/>, and a mix of the two engines is possible (docs/GAME-SHELL.md, "AI options").
    /// </summary>
    private readonly MatchAi[] _ai = new MatchAi[Slots];
    private const int DefaultArmyEntry = ComputerArmyEntry + 1;
    private string? _selectedMapPath;
    private int _startMoney;
    private CnCNew.Simulation.Skirmish.MatchDebugOptions _debug = CnCNew.Simulation.Skirmish.MatchDebugOptions.None;
    private int _gameSpeed = CnCNew.Ui.Settings.SkirmishMemory.NormalGameSpeed;

    /// <summary>The range of <c>SliderGameSpeed</c> (<c>SkirmishGameOptionsMenu.cpp</c>: <c>max(15, min(61, ...))</c>); above 60 there is no limit (<c>GREATER_NO_FPS_LIMIT</c>).</summary>
    private const int MinGameSpeed = 15, MaxGameSpeed = 61, NoLimitAbove = 60;

    /// <summary><c>setFPSTextBox</c>: the number of the slider, "--" above 60, the original's speed drawn disabled.</summary>
    private void ShowGameSpeed()
    {
        if (Tree.FindByName("SkirmishGameOptionsMenu.wnd:StaticTextGameSpeed") is not { } text) return;
        text.SetEnabled(_gameSpeed != CnCNew.Ui.Settings.SkirmishMemory.NormalGameSpeed);
        text.SetText(_gameSpeed > NoLimitAbove ? "--" : _gameSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary><c>reallyDoStart</c>: the slider is the frame rate of the match, at least 15, none above 60 (1,000 frames a second); this port runs the same game faster or slower.</summary>
    private double GameSpeedFactor => (_gameSpeed > NoLimitAbove ? 1000 : Math.Max(MinGameSpeed, _gameSpeed)) / (double)CnCNew.Ui.Settings.SkirmishMemory.NormalGameSpeed;
    // The room of the local game (docs/NETWORK-PROTOCOL.md section 4): what the team and colour boxes of a slot say travels into the match description.
    private readonly int[] _team = new int[Slots];
    private readonly int[] _start = [-1, -1, -1, -1, -1, -1, -1, -1];
    private readonly int[] _color = new int[Slots];

    public SkirmishSetupScreen(GameShell shell) : base(shell, ShellLayoutNames.SkirmishGameOptionsMenu)
    {
        _factionChoices = [.. Shell.Data.Factions.Where(p => p.Faction.PlayableSide).Select(p => p.Faction.Name)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal), "Random"];
        _startMoney = MoneyChoices.Contains(Shell.Settings.Skirmish.LastStartMoney) ? Shell.Settings.Skirmish.LastStartMoney : MoneyChoices[1];
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        var colors = names.ColorNames();

        Tree.Register("SkirmishGameOptionsMenuSystem", OnSystem);
        Tree.Register("SkirmishGameOptionsMenuInput", OnInput);
        Tree.TooltipOverride = window => window == TryW("ListboxInfo") ? HonorTooltip(window) : null;

        W("TextEntryPlayerName").SetEntryText(names.Player);
        var computerArmy = names.ComputerArmy;
        _originalAi = Shell.SkirmishAi == CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original;
        Array.Fill(_ai, new MatchAi(Shell.SkirmishAi));
        for (var slot = 0; slot < Slots; slot++)
        {
            var template = W($"ComboBoxPlayerTemplate{slot}");
            foreach (var name in _factionChoices) template.AddComboEntry(name == "Random" ? names.Random : names.Faction(name));
            template.SetComboSelection(0);
            var color = W($"ComboBoxColor{slot}");
            foreach (var colorName in colors) color.AddComboEntry(colorName);
            color.SetComboSelection(Math.Min(slot, colors.Length - 1));
            _color[slot] = colors.Length == 0 ? -1 : Math.Min(slot, colors.Length - 1);
            var team = W($"ComboBoxTeam{slot}");
            for (var index = 0; index <= 4; index++) team.AddComboEntry(names.Team(index));
            team.SetComboSelection(0);
            if (slot == 0) continue;
            var player = W($"ComboBoxPlayer{slot}");
            player.AddComboEntry(names.Open);
            player.AddComboEntry(names.Closed);
            player.AddComboEntry(names.EasyArmy);
            player.AddComboEntry(computerArmy);
            player.AddComboEntry(names.HardArmy);
            if (!_originalAi)
            {
                player.AddComboEntry(ShellSkirmishText.RushArmy);
                player.AddComboEntry(ShellSkirmishText.TurtleArmy);
                player.AddComboEntry(ShellSkirmishText.EcoArmy);
            }
            player.SetComboSelection(0);
        }
        // SkirmishGameOptionsMenuInit: the second slot starts as a computer army (the original picks easy, medium or hard by the battle honors; this build starts with medium).
        _computer[1] = true;
        W("ComboBoxPlayer1").SetComboSelection(DefaultArmyEntry);

        var money = W("ComboBoxStartingCash");
        foreach (var value in MoneyChoices) money.AddComboEntry(Shell.FormatMoney(value));
        money.SetComboSelection(MoneyChoices.IndexOf(_startMoney));

        // The debug options of the match (docs/GAME-SHELL.md): a button of this project between Start and Back opens their screen. The starting cash above is one of them for the tester.
        Tree.Register("SkirmishDebugOptionsSystem", OnSystem);
        Tree.AddLayout(DebugOptionsScreen.ButtonLayoutOf(W("ButtonStart").Definition, new WndScreenRect(DebugButtonLeft, 513, DebugButtonLeft + AiOptionsScreen.ButtonWidth, 549, 800, 600), "SkirmishDebugOptionsSystem",
            DebugOptionsScreen.Button, "DebugOptionsButton.wnd", AiOptionsScreen.ButtonFontSize));
        ShowDebugButton();
        // The AI options (docs/GAME-SHELL.md): a second button of this project beside it, for the engine, tactics and strength of every computer slot.
        Tree.Register("SkirmishAiOptionsSystem", OnSystem);
        Tree.AddLayout(DebugOptionsScreen.ButtonLayoutOf(W("ButtonStart").Definition, new WndScreenRect(AiButtonLeft, 513, AiButtonLeft + AiOptionsScreen.ButtonWidth, 549, 800, 600), "SkirmishAiOptionsSystem", AiOptionsScreen.Button, "AiOptionsButton.wnd", AiOptionsScreen.ButtonFontSize));
        Tree.FindByName(AiOptionsScreen.Button)!.SetText(AiOptionsScreen.ButtonCaption);

        // SkirmishGameOptionsMenuInit: the speed of the preferences (FPS), 15 to 61, beside it its number (setFPSTextBox).
        _gameSpeed = Math.Clamp(Shell.Settings.Skirmish.GameSpeed, MinGameSpeed, MaxGameSpeed);
        W("SliderGameSpeed").SetSliderValue(_gameSpeed);
        ShowGameSpeed();

        // SkirmishGameOptionsMenuInit: the map of the preferences (SkirmishPreferences::getPreferredMap), or the first multiplayer map of the cache (getDefaultMap) - the
        // setup never stands without a map unless there is none at all.
        if (PreferredMap() is { } map) SetSelectedMap(map);
        RefreshMapDisplay();
        ApplySlotVisibility();
        ShowStartSpots();
        ShowHonors();
        // SkirmishGameOptionsMenuUpdate: the screen fades in (FULLFADE of SubParent) and fades out again when it is closed.
        Transitions?.SetGroup("SkirmishGameOptionsMenuFade");
    }

    protected override string? ExitTransitionGroup => "SkirmishGameOptionsMenuFade";

    /// <summary>The two buttons of this project, "AI options" and "Debug options", stand side by side in the gap between Start (to 268) and Back (from 530) of the layout's 800 x 600, in a smaller type.</summary>
    private const int DebugButtonLeft = 402;
    private const int AiButtonLeft = 274;

    /// <summary>The AI of a computer slot as the setup will start it (engine, strength, tactics; <see cref="AiOptionsScreen"/>).</summary>
    public MatchAi AiOf(int slot) => _ai[slot];

    /// <summary>
    /// The entry of a slot's box that shows <paramref name="ai"/>: the strength as Easy, Medium or Hard Army - for either engine -, and with the new AI at medium strength its tactics
    /// profile as Rush, Turtle or Eco Army (the entries exist only when the boxes were made for the new AI). A combination the box has no entry for (a profile at another
    /// strength) shows its strength.
    /// </summary>
    private int EntryOf(MatchAi ai)
    {
        if (!_originalAi && ai.Kind == CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin && ai.Difficulty == CnCNew.Simulation.Scripting.ScriptDifficulty.Normal && ai.Tactics is { } tactics
            && ProfileEntries.IndexOf(tactics) is var profile and >= 0) return ProfileEntry + profile;
        return ComputerArmyEntry + (int)ai.Difficulty;
    }

    private void ShowSlot(int slot) => W($"ComboBoxPlayer{slot}").SetComboSelection(EntryOf(_ai[slot]));

    /// <summary>Whether the map brings scripts of its own: only the session of the original AI runs them, so every computer slot is played by that AI (<see cref="TryStart"/>).</summary>
    private bool MapHasScripts => _selectedMapPath is { } path && Shell.MapCatalog.TryGetValue(path, out var map) && map.HasScripts;

    private void OpenAiOptions()
    {
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        var rows = ImmutableArray.CreateBuilder<AiOptionsRow>();
        for (var slot = 1; slot < _startPositionCount; slot++)
        {
            if (!_computer[slot]) continue;
            var faction = _factionIndex[slot] < _factionChoices.Length - 1 ? names.Faction(_factionChoices[_factionIndex[slot]]) : names.Random;
            rows.Add(new AiOptionsRow(slot, $"Player {slot + 1}: {faction}", _ai[slot]));
        }
        var slots = rows.Select(row => row.Slot).ToImmutableArray();
        Shell.OpenAiOptions(rows.ToImmutable(), MapHasScripts ? AiEngineMode.ClassicOnly : AiEngineMode.Free, readOnly: false, !_debug.IsDefault, chosen =>
        {
            for (var index = 0; index < slots.Length; index++)
            {
                _ai[slots[index]] = chosen[index];
                ShowSlot(slots[index]);
            }
        });
    }

    private void ShowDebugButton() => Tree.FindByName(DebugOptionsScreen.Button)!.SetText(DebugOptionsScreen.ButtonText(_debug));

    /// <summary>The debug options chosen so far (the match's, when Start is pressed).</summary>
    public CnCNew.Simulation.Skirmish.MatchDebugOptions DebugOptions => _debug;

    private ImmutableArray<HonorEntry> _honors = [];

    /// <summary><c>populateSkirmishBattleHonors</c>: the numbers of the record and its two rows of pictures in <c>ListboxInfo</c>; the maps are the official multiplayer maps of the list.</summary>
    private void ShowHonors()
    {
        var record = Shell.Honors;
        W("StaticTextStreakValue").SetText(record.WinStreak.ToString(CultureInfo.InvariantCulture));
        W("StaticTextBestStreakValue").SetText(record.BestWinStreak.ToString(CultureInfo.InvariantCulture));
        W("StaticTextWinsValue").SetText(record.Wins.ToString(CultureInfo.InvariantCulture));
        W("StaticTextLossesValue").SetText(record.Losses.ToString(CultureInfo.InvariantCulture));
        _honors = record.List([..Shell.HonorMaps()]);
    }

    /// <summary>
    /// The pictures where <c>InsertBattleHonor</c> and <c>W3DGadgetListBoxDraw</c> put them: a spacer of 10 pixels above each of the two rows, the image 40 x 41 of the 800 x 600 screen,
    /// centred in its column of the window's <c>COLUMNSWIDTH</c>, and those not earned in the grey 80, 80, 80.
    /// </summary>
    private IEnumerable<(UiRect Cell, UiImageDraw Draw, HonorEntry Entry)> HonorCells(UiWindow window)
    {
        var width = 40 * Shell.Viewport.Width / 800;
        var height = 41 * Shell.Viewport.Height / 600;
        var spacer = 10 * Shell.Viewport.Height / 600;
        var content = UiGadgetGeometry.ListContent(window);
        for (var index = 0; index < _honors.Length; index++)
        {
            var entry = _honors[index];
            var row = index / 6;
            var top = content.Y + spacer + row * (height + spacer);
            var column = UiGadgetGeometry.Columns(window, new UiRect(content.X, top, content.Width, height))[index % 6];
            var cell = new UiRect(column.X + Math.Max(0, (column.Width - width) / 2), top, Math.Min(width, column.Width), height);
            yield return (cell, new UiImageDraw(entry.Image, cell, entry.Earned ? new WndColor(255, 255, 255, 255) : new WndColor(80, 80, 80, 255), false, false), entry);
        }
    }

    /// <summary><c>BattleHonorTooltip</c>: the picture under the pointer names itself, the window's own text stands for the rest.</summary>
    private string? HonorTooltip(UiWindow window)
    {
        foreach (var (cell, _, entry) in HonorCells(window))
            if (cell.Contains(Tree.HoverX, Tree.HoverY)) return Text(entry.Tooltip);
        return null;
    }

    private bool _mapPickerOpen;

    private void ShowStartSpots()
    {
        var entry = _selectedMapPath is { } path && Shell.MapCatalog.TryGetValue(path, out var known) ? known : null;
        StartSpots.Show(this, entry, _mapPickerOpen, [.._start.Select((start, slot) => slot == 0 || _computer[slot] ? start : -1)]);
    }

    /// <summary>A spot of the preview, as in the room: the human's seat and the computer armies take it or give it up.</summary>
    private void OnStartSpot(int spot, bool right)
    {
        var starts = StartSpots.Click([.._start.Select((start, slot) => slot == 0 || _computer[slot] ? start : -1)], spot, 0, true, slot => _computer[slot], right);
        Array.Copy(starts.ToArray(), _start, Slots);
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        ShowStartSpots();
    }

    /// <summary>The windows of the setup that <c>showSkirmishGameOptionsUnderlyingGUIElements</c> (<c>SkirmishMapSelectMenu.cpp</c>) hides while the map picker lies over it
    /// (the picker's own layout has no opaque background, so what it does not hide would show through its list): the map preview with its caption and name, the three
    /// column captions, the faction/colour/team combo boxes and the start button. The player name column and the lower panels stay, as in the original.</summary>
    private static readonly ImmutableArray<string> HiddenWhileMapPicker =
        ["MapWindow", "StaticTextTeam", "StaticTextFaction", "StaticTextColor", "TextEntryMapDisplay", "ButtonSelectMap", "ButtonStart", "StaticTextMapPreview"];

    /// <summary><c>showSkirmishGameOptionsUnderlyingGUIElements(!open)</c>; Reset and Back are disabled while the picker is open (<c>winEnable(show)</c>).</summary>
    public void SetMapPickerOpen(bool open)
    {
        _mapPickerOpen = open;
        foreach (var name in HiddenWhileMapPicker) W(name).SetHidden(open);
        ApplySlotVisibility();
        ShowStartSpots();
        W("ButtonReset").SetEnabled(!open);
        W("ButtonBack").SetEnabled(!open);
    }

    private void ApplySlotVisibility()
    {
        for (var slot = 0; slot < Slots; slot++)
        {
            var visible = slot < _startPositionCount || slot == 0;
            if (slot > 0) W($"ComboBoxPlayer{slot}").SetHidden(!visible);
            W($"ComboBoxPlayerTemplate{slot}").SetHidden(!visible || _mapPickerOpen);
            W($"ComboBoxColor{slot}").SetHidden(!visible || _mapPickerOpen);
            W($"ComboBoxTeam{slot}").SetHidden(!visible || _mapPickerOpen);
            if (!visible) _computer[slot] = false;
        }
    }

    /// <summary>
    /// <c>SkirmishPreferences::getPreferredMap</c>: the last map when it still is a multiplayer map of the catalog, otherwise <c>getDefaultMap(TRUE)</c> - the first multiplayer map
    /// of the map cache, which is ordered by the lower-case path. <see langword="null"/> only when the catalog has no multiplayer map.
    /// </summary>
    private string? PreferredMap()
    {
        if (Shell.Settings.Skirmish.LastMap is { } last && Shell.MapCatalog.TryGetValue(last, out var remembered) && remembered.IsMultiplayer) return remembered.VirtualPath;
        return Shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer).Select(entry => entry.VirtualPath)
            .OrderBy(path => path.Replace('/', '\\').ToLowerInvariant(), StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The map name window: the display name of the chosen map (<c>GadgetStaticTextSetText(textEntryMapDisplay, m_displayName)</c>); without any map the original leaves it as it is, here it is empty.</summary>
    private void RefreshMapDisplay()
        => W("TextEntryMapDisplay").SetText(_selectedMapPath is null ? string.Empty : Shell.MapNames.DisplayName(_selectedMapPath, Shell.MapCatalog, Shell.TextSource));

    public void SetSelectedMap(string mapPath)
    {
        _selectedMapPath = mapPath;
        _startPositionCount = Shell.MapCatalog.TryGetValue(mapPath, out var entry) ? Math.Clamp(entry.StartPositionCount, 1, Slots) : Slots;
        // A new map has other places: nobody keeps one.
        Array.Fill(_start, -1);
        RefreshMapDisplay();
        ApplySlotVisibility();
        ShowStartSpots();
    }

    private string ResolveFaction(int slot)
    {
        var index = _factionIndex[slot];
        // "Random" is not decided here: the match description carries it and its seed decides, the same on every machine (docs/SIMULATION-CORE.md section 45.2).
        return index < _factionChoices.Length - 1 ? _factionChoices[index] : MatchSetup.RandomFaction;
    }

    private void TryStart()
    {
        if (_selectedMapPath is null) return;
        var opponents = ImmutableArray.CreateBuilder<string>();
        // The AI of each slot in slot order; the first slot is the human's.
        var ai = ImmutableArray.CreateBuilder<MatchAi>();
        ai.Add(MatchAi.Builtin);
        // Team, colour and place of every slot that takes part, in the order of the match's slots; a slot without a place gets a random one by the seed of the description.
        // A map's own scripts run only in the session with the script engine, and that is the session of the original AI (SidesList::prepareForMP_or_Skirmish): a user's map brings them.
        // Without them each slot is played by the engine it was given; one Classic slot makes the match a scripted one, in which the New slots are played by the built-in AI.
        var scripted = MapHasScripts;
        var details = ImmutableArray.CreateBuilder<MatchSlotDetails>();
        details.Add(new MatchSlotDetails(_team[0], _color[0], StartPosition: _start[0]));
        for (var slot = 1; slot < _startPositionCount; slot++)
        {
            if (!_computer[slot]) continue;
            details.Add(new MatchSlotDetails(_team[slot], _color[slot], StartPosition: _start[slot]));
            opponents.Add(ResolveFaction(slot));
            var kind = scripted ? CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original : _ai[slot].Kind;
            ai.Add(new MatchAi(kind, _ai[slot].Difficulty, kind == CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin ? _ai[slot].Tactics : null));
        }
        if (opponents.Count == 0) return;
        if (ai.Any(slot => slot.Kind == CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Original) && !_debug.IsDefault)
        {
            Shell.ShowError("Debug options", "The debug options are not available in a match of the original skirmish AI or on a map with scripts of its own.");
            return;
        }
        // A person plays until the match is decided (D-8); a check's short match (the client's --skirmish-seconds) has its length in game time.
        var options = SkirmishMatchOptions.StandardAt(Shell.Data.Rate) with { StartMoney = _startMoney, MaxTicks = SkirmishMatchOptions.NoTimeLimit };
        if (Shell.SkirmishMatchSeconds > 0) options = options with { MaxTicks = Shell.Data.Rate.Time.WholeSecondsToTicks(Shell.SkirmishMatchSeconds) };
        var setup = new MatchSetup(_selectedMapPath, ResolveFaction(0), opponents.ToImmutable(), options) { Ai = ai.ToImmutable(), Details = details.ToImmutable(), Speed = GameSpeedFactor, Debug = _debug,
            PlayerName = W("TextEntryPlayerName").TextEntry?.Text is { Length: > 0 } typed ? typed : null };
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        _ = Shell.ApplySettings(Shell.Settings with { Skirmish = Shell.Settings.Skirmish with { LastMap = _selectedMapPath, LastStartMoney = _startMoney, GameSpeed = _gameSpeed } });
        Shell.BeginMatchLoading(setup);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected or UiEventKind.ButtonSelectedRight when e.Source.StartsWith("SkirmishGameOptionsMenu.wnd:", StringComparison.Ordinal) && StartSpots.SpotOf(e.Source["SkirmishGameOptionsMenu.wnd:".Length..]) is var spot and >= 0:
                OnStartSpot(spot, e.Kind == UiEventKind.ButtonSelectedRight);
                break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonStart": TryStart(); break;
            case UiEventKind.ButtonSelected when e.Source == DebugOptionsScreen.Button:
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenDebugOptions(_debug, readOnly: false, chosen => { _debug = chosen; ShowDebugButton(); });
                break;
            case UiEventKind.ButtonSelected when e.Source == AiOptionsScreen.Button:
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                OpenAiOptions();
                break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonSelectMap":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenSkirmishMapSelect(this);
                break;
            case UiEventKind.ComboSelected: OnCombo(e); break;
            case UiEventKind.SliderTrack or UiEventKind.SliderDone when e.Source == "SkirmishGameOptionsMenu.wnd:SliderGameSpeed":
                _gameSpeed = Math.Clamp(e.Value, MinGameSpeed, MaxGameSpeed);
                ShowGameSpeed();
                break;
        }
    }

    private void OnCombo(UiEvent e)
    {
        Shell.PlaySound(ShellUiSounds.ComboBoxClick);
        for (var slot = 0; slot < Slots; slot++)
        {
            if (e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxPlayerTemplate{slot}") { _factionIndex[slot] = e.Value; return; }
            if (e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxTeam{slot}") { _team[slot] = Math.Clamp(e.Value, 0, 4); return; }
            if (e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxColor{slot}") { _color[slot] = e.Value; return; }
            if (slot > 0 && e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxPlayer{slot}")
            {
                _computer[slot] = e.Value >= ComputerArmyEntry;
                // An army entry is a strength (the slot keeps its engine, its profile goes); a profile entry is the new AI with that profile at medium strength.
                if (e.Value >= ProfileEntry)
                    _ai[slot] = new MatchAi(CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin, CnCNew.Simulation.Scripting.ScriptDifficulty.Normal,
                        ProfileEntries[Math.Min(e.Value - ProfileEntry, ProfileEntries.Length - 1)]);
                else if (e.Value >= ComputerArmyEntry)
                    _ai[slot] = new MatchAi(_ai[slot].Kind, (CnCNew.Simulation.Scripting.ScriptDifficulty)Math.Clamp(e.Value - ComputerArmyEntry, 0, 2));
                W($"ComboBoxPlayerTemplate{slot}").SetEnabled(_computer[slot]);
                return;
            }
            if (e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxStartingCash") { _startMoney = MoneyChoices[Math.Clamp(e.Value, 0, MoneyChoices.Length - 1)]; return; }
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    public override UiDrawList BuildDrawList()
    {
        var list = ShellCustomDraw.ExpandMapPreview(Tree.BuildDrawList(),
            PreviewOf(_selectedMapPath, _selectedMapPath is null ? null : Shell.MapNames.DisplayName(_selectedMapPath, Shell.MapCatalog, Shell.TextSource)),
            Tree.Services.TextMeasure, Shell.Skin, Tree.Viewport);
        var window = W("ListboxInfo");
        return !window.IsVisible || _honors.IsDefaultOrEmpty ? list : list with { Commands = list.Commands.AddRange(HonorCells(window).Select(cell => (UiDrawCommand)cell.Draw)) };
    }
}

/// <summary>
/// <c>SkirmishMapSelectMenu.wnd</c>, and <c>LanMapSelectMenu.wnd</c> for the room of a server (the same windows under the other file's name). <c>RadioButtonUserMaps</c> shows the user's folder, read behind the menu (<see cref="GameShell.BeginLoadingUserMaps"/>). The list only
/// shows maps <c>Maps/MapCache.ini</c> flags <c>isMultiplayer = yes</c>.
/// </summary>
public sealed class SkirmishMapSelectScreen : ShellScreen
{
    /// <summary>The original creates the map picker as a layout of its own on top of the skirmish setup (<c>SkirmishGameOptionsMenu.cpp</c>: <c>winCreateLayout</c>, <c>hide(FALSE)</c>), not by <c>Shell::push</c>; the setup stays visible behind it.</summary>
    public override bool HidesScreensBelow => false;

    private readonly IMapPickerOwner _owner;
    private ImmutableArray<string> _mapPaths = [];
    private int _selectedIndex = -1;
    private bool _showingUserMaps;
    private (int Version, bool Loading) _shownUserMaps;
    private double _nextLookSeconds = LookSeconds;

    /// <summary>How often the open list looks at the user's folder: a map copied there while the list is open appears by itself.</summary>
    private const double LookSeconds = 5;

    public SkirmishMapSelectScreen(GameShell shell, IMapPickerOwner owner, string layoutName = ShellLayoutNames.SkirmishMapSelectMenu) : base(shell, layoutName)
    {
        _owner = owner;
        Tree.Register($"{layoutName}System", OnSystem);
        Tree.Register($"{layoutName}Input", OnInput);
        W("RadioButtonSystemMaps").SetChecked(true);
        PopulateSystemMaps();
        StartSpots.Hide(this);
        _owner.SetMapPickerOpen(true);
        Shell.RescanUserMaps();
    }

    /// <summary>OK and Back both give the setup its hidden elements back (<c>showSkirmishGameOptionsUnderlyingGUIElements(TRUE)</c>).</summary>
    public override void OnClosed() => _owner.SetMapPickerOpen(false);

    /// <summary>The user's side follows the folder that is read behind the menu.</summary>
    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        _nextLookSeconds -= dtSeconds;
        if (_nextLookSeconds <= 0)
        {
            _nextLookSeconds = LookSeconds;
            Shell.RescanUserMaps();
        }
        if (_showingUserMaps && _shownUserMaps != (Shell.UserMapsVersion, Shell.UserMapsLoading)) PopulateUserMaps();
    }

    private void PopulateSystemMaps()
    {
        _showingUserMaps = false;
        _mapPaths = [.. Shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer && !entry.VirtualPath.StartsWith(ContentFileSystem.UserMapPrefix, StringComparison.Ordinal)).Select(entry => entry.VirtualPath)
            .OrderBy(path => Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource), StringComparer.Ordinal)];
        var list = W("ListboxMap");
        list.ClearList();
        // Two columns of 5 % and 95 % (COLUMNSWIDTH of the file): populateMapListbox puts the difficulty-success image in the first and the name in the second. There is no
        // success record in this build, so the first column stays empty; the name used to land in it and was cut off after two letters.
        foreach (var path in _mapPaths) list.AddListRow(new UiListRow(string.Empty, Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource)));
        _selectedIndex = -1;
    }

    private void PopulateUserMaps()
    {
        _showingUserMaps = true;
        _shownUserMaps = (Shell.UserMapsVersion, Shell.UserMapsLoading);
        _mapPaths = [.. Shell.UserMaps.OrderBy(path => Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource), StringComparer.Ordinal)];
        var list = W("ListboxMap");
        list.ClearList();
        foreach (var path in _mapPaths) list.AddListRow(new UiListRow(string.Empty, Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource)));
        // A row behind the paths: Accept refuses it.
        if (Shell.UserMapsLoading) list.AddListRow(new UiListRow(string.Empty, "Reading your maps ..."));
        _selectedIndex = -1;
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == $"{LayoutName}.wnd:ListboxMap": _selectedIndex = e.Value; break;
            case UiEventKind.ListDoubleClicked when e.Source == $"{LayoutName}.wnd:ListboxMap": _selectedIndex = e.Value; Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == $"{LayoutName}.wnd:ButtonOK": Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == $"{LayoutName}.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when e.Source == $"{LayoutName}.wnd:RadioButtonSystemMaps": PopulateSystemMaps(); break;
            case UiEventKind.ButtonSelected when e.Source == $"{LayoutName}.wnd:RadioButtonUserMaps": PopulateUserMaps(); break;
        }
    }

    private void Accept()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _mapPaths.Length) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        _owner.SetSelectedMap(_mapPaths[_selectedIndex]);
        Shell.CloseTop();
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    public override UiDrawList BuildDrawList()
        => ShellCustomDraw.ExpandMapPreview(Tree.BuildDrawList(),
            PreviewOf(_selectedIndex >= 0 && _selectedIndex < _mapPaths.Length ? _mapPaths[_selectedIndex] : null, _selectedIndex >= 0 && _selectedIndex < _mapPaths.Length ? Shell.MapNames.DisplayName(_mapPaths[_selectedIndex], Shell.MapCatalog, Shell.TextSource) : null),
            Tree.Services.TextMeasure, Shell.Skin, Tree.Viewport);
}
