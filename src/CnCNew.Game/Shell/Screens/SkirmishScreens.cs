using System.Collections.Immutable;
using CnCNew.Game.Live;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>SkirmishGameOptionsMenu.wnd</c>: map, human faction/name, up to 7 computer opponents (limited by the selected
/// map's start position count), and starting cash feed a <see cref="MatchSetup"/>. Everything else the layout shows
/// (game speed, superweapon limit, battle-honor list, per-slot color/team) has no counterpart in <c>LiveMatch</c> or
/// <see cref="Simulation.Skirmish.SkirmishMatchOptions"/> yet: the gadgets are populated and stay interactive so the
/// screen looks authentic, but their values are not read at Start time. AI difficulty is not exposed at all (the
/// skirmish AI in this build has no difficulty axis). All of this is listed as a request to <c>LiveMatch</c>/the AI in
/// <c>docs/GAME-SHELL.md</c>. Start does nothing (no inline validation message, just no output) until a map and at
/// least one computer opponent are chosen.
/// </summary>
public sealed class SkirmishSetupScreen : ShellScreen
{
    private const int Slots = 8;
    private static readonly ImmutableArray<int> MoneyChoices = [5_000, 10_000, 15_000, 20_000, 30_000, 40_000, 50_000];

    private readonly ImmutableArray<string> _factionChoices;
    private readonly bool[] _computer = new bool[Slots];
    private readonly int[] _factionIndex = new int[Slots];
    private int _startPositionCount = Slots;
    private string? _selectedMapPath;
    private int _startMoney;

    public SkirmishSetupScreen(GameShell shell) : base(shell, ShellLayoutNames.SkirmishGameOptionsMenu)
    {
        _factionChoices = [.. Shell.Data.Factions.Where(p => p.Faction.PlayableSide).Select(p => p.Faction.Name)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal), "Random"];
        _startMoney = MoneyChoices.Contains(Shell.Settings.Skirmish.LastStartMoney) ? Shell.Settings.Skirmish.LastStartMoney : MoneyChoices[1];
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        var colors = names.ColorNames();

        Tree.Register("SkirmishGameOptionsMenuSystem", OnSystem);
        Tree.Register("SkirmishGameOptionsMenuInput", OnInput);

        W("TextEntryPlayerName").SetEntryText("Player");
        for (var slot = 0; slot < Slots; slot++)
        {
            var template = W($"ComboBoxPlayerTemplate{slot}");
            foreach (var name in _factionChoices) template.AddComboEntry(name == "Random" ? names.Random : names.Faction(name));
            template.SetComboSelection(0);
            var color = W($"ComboBoxColor{slot}");
            foreach (var colorName in colors) color.AddComboEntry(colorName);
            color.SetComboSelection(Math.Min(slot, colors.Length - 1));
            var team = W($"ComboBoxTeam{slot}");
            for (var index = 0; index <= 4; index++) team.AddComboEntry(names.Team(index));
            team.SetComboSelection(0);
            if (slot == 0) continue;
            var player = W($"ComboBoxPlayer{slot}");
            player.AddComboEntry(names.Open);
            player.AddComboEntry("Computer");
            player.SetComboSelection(0);
        }
        _computer[1] = true;
        W("ComboBoxPlayer1").SetComboSelection(1);

        var money = W("ComboBoxStartingCash");
        foreach (var value in MoneyChoices) money.AddComboEntry(Shell.FormatMoney(value));
        money.SetComboSelection(MoneyChoices.IndexOf(_startMoney));

        RefreshMapDisplay();
        ApplySlotVisibility();
    }

    private void ApplySlotVisibility()
    {
        for (var slot = 1; slot < Slots; slot++)
        {
            var visible = slot < _startPositionCount;
            W($"ComboBoxPlayer{slot}").SetHidden(!visible);
            W($"ComboBoxPlayerTemplate{slot}").SetHidden(!visible);
            W($"ComboBoxColor{slot}").SetHidden(!visible);
            W($"ComboBoxTeam{slot}").SetHidden(!visible);
            if (!visible) _computer[slot] = false;
        }
    }

    private void RefreshMapDisplay()
        => W("TextEntryMapDisplay").SetText(_selectedMapPath is null ? "(no map selected)" : Shell.MapNames.DisplayName(_selectedMapPath, Shell.MapCatalog, Shell.TextSource));

    public void SetSelectedMap(string mapPath)
    {
        _selectedMapPath = mapPath;
        _startPositionCount = Shell.MapCatalog.TryGetValue(mapPath, out var entry) ? Math.Clamp(entry.StartPositionCount, 1, Slots) : Slots;
        RefreshMapDisplay();
        ApplySlotVisibility();
    }

    private string ResolveFaction(int slot)
    {
        var index = _factionIndex[slot];
        if (index < _factionChoices.Length - 1) return _factionChoices[index];
        var real = _factionChoices[..^1];
        return real[new Random(unchecked(0xC0FFEE + slot)).Next(real.Length)];
    }

    private void TryStart()
    {
        if (_selectedMapPath is null) return;
        var opponents = ImmutableArray.CreateBuilder<string>();
        for (var slot = 1; slot < _startPositionCount; slot++)
            if (_computer[slot]) opponents.Add(ResolveFaction(slot));
        if (opponents.Count == 0) return;
        var options = SkirmishMatchOptions.Standard with { StartMoney = _startMoney };
        var setup = new MatchSetup(_selectedMapPath, ResolveFaction(0), opponents.ToImmutable(), options);
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        Shell.ApplySettings(Shell.Settings with { Skirmish = Shell.Settings.Skirmish with { LastMap = _selectedMapPath, LastStartMoney = _startMoney } });
        Shell.BeginMatchLoading(setup);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonStart": TryStart(); break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishGameOptionsMenu.wnd:ButtonSelectMap":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenSkirmishMapSelect(this);
                break;
            case UiEventKind.ComboSelected: OnCombo(e); break;
        }
    }

    private void OnCombo(UiEvent e)
    {
        Shell.PlaySound(ShellUiSounds.ComboBoxClick);
        for (var slot = 0; slot < Slots; slot++)
        {
            if (e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxPlayerTemplate{slot}") { _factionIndex[slot] = e.Value; return; }
            if (slot > 0 && e.Source == $"SkirmishGameOptionsMenu.wnd:ComboBoxPlayer{slot}")
            {
                _computer[slot] = e.Value == 1;
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
        => ShellCustomDraw.ExpandMapPreview(Tree.BuildDrawList(),
            new ShellMapPreviewContext(_selectedMapPath is null ? null : Shell.MapNames.DisplayName(_selectedMapPath, Shell.MapCatalog, Shell.TextSource)),
            Tree.Services.TextMeasure);
}

/// <summary>
/// <c>SkirmishMapSelectMenu.wnd</c>. <c>RadioButtonUserMaps</c> has no backing directory in this build (no user-map
/// import yet), so switching to it simply empties the list; this is documented, not a silent failure. The list only
/// shows maps <c>Maps/MapCache.ini</c> flags <c>isMultiplayer = yes</c>.
/// </summary>
public sealed class SkirmishMapSelectScreen : ShellScreen
{
    private readonly SkirmishSetupScreen _owner;
    private ImmutableArray<string> _mapPaths = [];
    private int _selectedIndex = -1;

    public SkirmishMapSelectScreen(GameShell shell, SkirmishSetupScreen owner) : base(shell, ShellLayoutNames.SkirmishMapSelectMenu)
    {
        _owner = owner;
        Tree.Register("SkirmishMapSelectMenuSystem", OnSystem);
        Tree.Register("SkirmishMapSelectMenuInput", OnInput);
        W("RadioButtonSystemMaps").SetChecked(true);
        PopulateSystemMaps();
    }

    private void PopulateSystemMaps()
    {
        _mapPaths = [.. Shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer).Select(entry => entry.VirtualPath)
            .OrderBy(path => Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource), StringComparer.Ordinal)];
        var list = W("ListboxMap");
        list.ClearList();
        foreach (var path in _mapPaths) list.AddListRow(new UiListRow(Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource)));
        _selectedIndex = -1;
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "SkirmishMapSelectMenu.wnd:ListboxMap": _selectedIndex = e.Value; break;
            case UiEventKind.ListDoubleClicked when e.Source == "SkirmishMapSelectMenu.wnd:ListboxMap": _selectedIndex = e.Value; Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishMapSelectMenu.wnd:ButtonOK": Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishMapSelectMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishMapSelectMenu.wnd:RadioButtonSystemMaps": PopulateSystemMaps(); break;
            case UiEventKind.ButtonSelected when e.Source == "SkirmishMapSelectMenu.wnd:RadioButtonUserMaps":
                W("ListboxMap").ClearList();
                _mapPaths = [];
                _selectedIndex = -1;
                break;
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
            new ShellMapPreviewContext(_selectedIndex >= 0 && _selectedIndex < _mapPaths.Length ? Shell.MapNames.DisplayName(_mapPaths[_selectedIndex], Shell.MapCatalog, Shell.TextSource) : null),
            Tree.Services.TextMeasure);
}
