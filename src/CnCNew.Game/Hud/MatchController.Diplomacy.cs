using System.Collections.Immutable;
using CnCNew.Game.Live;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.World;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>One row of the diplomacy window (<c>PopulateInGameDiplomacyPopup</c>): the texts as they are drawn, and the connection a mute button acts on, or null where there is none.</summary>
public sealed record DiplomacyRow(PlayerId Player, string Name, string Side, string Team, string Status, WndColor Color, WndColor StatusColor, uint? MuteConnection, bool Muted);

/// <summary>
/// The window behind the command bar's communicator button and the diplomacy key (<c>Diplomacy.wnd</c>, <c>ToggleDiplomacy</c>). A mission has no players to show: there it is the
/// briefing history (<c>RecorderClass::isMultiplayer</c> is false, the button is <c>NonCommand_BriefingHistory</c>), every caption and popup of the scripts once. A skirmish counts as
/// multiplayer there, on a machine alone too: the players with side, team and state; only other humans in the match have a mute button. The window is not modal.
/// </summary>
public sealed partial class MatchController
{
    private const int DiplomacySlots = 8;
    private static readonly WndColor AliveColor = new(0, 255, 0, 255);
    private static readonly WndColor DeadColor = new(255, 0, 0, 255);
    private static readonly WndColor GoneColor = new(196, 0, 0, 255);

    private UiWindow _diplomacyRoot = null!;
    private UiWindow _diplomacyInGame = null!;
    private UiWindow _diplomacySolo = null!;
    private UiWindow _diplomacyBriefing = null!;
    private readonly UiWindow?[] _diplomacyPlayer = new UiWindow?[DiplomacySlots];
    private readonly UiWindow?[] _diplomacySide = new UiWindow?[DiplomacySlots];
    private readonly UiWindow?[] _diplomacyTeam = new UiWindow?[DiplomacySlots];
    private readonly UiWindow?[] _diplomacyStatus = new UiWindow?[DiplomacySlots];
    private readonly UiWindow?[] _diplomacyMute = new UiWindow?[DiplomacySlots];
    private readonly UiWindow?[] _diplomacyUnmute = new UiWindow?[DiplomacySlots];
    private int _briefingShown;

    public bool IsDiplomacyOpen => !_diplomacyRoot.IsHidden;

    /// <summary>What a mission's scripts said so far (<c>theBriefingList</c>); null in a skirmish, whose window shows the players instead.</summary>
    public IReadOnlyList<string>? BriefingHistory { get; set; }

    /// <summary>The players as the window shows them; empty outside a match of the session.</summary>
    public ImmutableArray<DiplomacyRow> DiplomacyRows { get; private set; } = [];

    private void AddDiplomacyWindow()
    {
        var roots = _tree.AddLayout(Skin.Derive(_assets.Diplomacy, _assets.HeaderTemplates));
        _diplomacyRoot = roots.Single(window => window.Name == "Diplomacy.wnd:Parent");
        _diplomacyRoot.SetHidden(true);
        _diplomacyInGame = Find("InGameParent");
        _diplomacySolo = Find("SoloParent");
        _diplomacyBriefing = Find("ListboxSolo");
        // The buddy list and its two tabs belong to GameSpy, which this game has not.
        _tree.FindByName("Diplomacy.wnd:RadioButtonInGame")?.SetHidden(true);
        _tree.FindByName("Diplomacy.wnd:RadioButtonBuddies")?.SetHidden(true);
        for (var row = 0; row < DiplomacySlots; row++)
        {
            _diplomacyPlayer[row] = _tree.FindByName($"Diplomacy.wnd:StaticTextPlayer{row}");
            _diplomacySide[row] = _tree.FindByName($"Diplomacy.wnd:StaticTextSide{row}");
            _diplomacyTeam[row] = _tree.FindByName($"Diplomacy.wnd:StaticTextTeam{row}");
            _diplomacyStatus[row] = _tree.FindByName($"Diplomacy.wnd:StaticTextStatus{row}");
            _diplomacyMute[row] = _tree.FindByName($"Diplomacy.wnd:ButtonMute{row}");
            _diplomacyUnmute[row] = _tree.FindByName($"Diplomacy.wnd:ButtonUnMute{row}");
        }
        _tree.Register("DiplomacySystem", OnDiplomacySystem);

        UiWindow Find(string name) => _tree.FindByName($"Diplomacy.wnd:{name}") ?? throw new InvalidOperationException($"Diplomacy.wnd has no {name} window.");
    }

    /// <summary><c>ToggleDiplomacy</c>: it hides the quit menu first, and <c>ShowDiplomacy</c> refuses while the scripts have taken the input.</summary>
    private void ToggleDiplomacy()
    {
        CloseMenu();
        if (IsDiplomacyOpen) HideDiplomacy();
        else if (WorldInputEnabled) ShowDiplomacy();
    }

    /// <summary><c>ControlBarSchemeManager::setControlBarSchemeByPlayer</c>: the same button is <c>NonCommand_Communicator</c> in a game with players and <c>NonCommand_BriefingHistory</c> in a mission.</summary>
    private string CommunicatorTooltip()
    {
        var name = BriefingHistory is null ? "CONTROLBAR:Communicator" : "CONTROLBAR:BriefingHistory";
        return $"{_assets.Text.Get(name)}\n{_assets.Text.Get(name + "Description")}";
    }

    private void ShowDiplomacy()
    {
        var solo = BriefingHistory is not null;
        _diplomacyInGame.SetHidden(solo);
        _diplomacySolo.SetHidden(!solo);
        _diplomacyRoot.SetHidden(false);
        RefreshDiplomacy();
    }

    private void HideDiplomacy() => _diplomacyRoot.SetHidden(true);

    private void OnDiplomacySystem(UiWindowTree tree, UiEvent uiEvent)
    {
        if (uiEvent.Kind != UiEventKind.ButtonSelected) return;
        ButtonClicked?.Invoke();
        if (uiEvent.Source == "Diplomacy.wnd:ButtonHide")
        {
            HideDiplomacy();
            return;
        }
        for (var row = 0; row < DiplomacyRows.Length; row++)
        {
            var mute = uiEvent.Source == $"Diplomacy.wnd:ButtonMute{row}";
            if (!mute && uiEvent.Source != $"Diplomacy.wnd:ButtonUnMute{row}") continue;
            if (DiplomacyRows[row].MuteConnection is { } connection) Net?.SetMuted(connection, mute);
            RefreshDiplomacy();
            return;
        }
    }

    private void RefreshDiplomacy()
    {
        if (BriefingHistory is { } history)
        {
            // Each new line in the next of the two message colours (getMessageColor(numEntries % 2)).
            // GadgetListBoxAddEntryText wraps an entry at the column's width: a caption is a sentence, far wider than the list.
            var list = _diplomacyBriefing;
            var width = UiGadgetGeometry.ListContent(list).Width - 2 * list.Transform.X.Length(UiGadgetGeometry.TextInset);
            for (; _briefingShown < history.Count; _briefingShown++)
            {
                var color = _briefingShown % 2 == 0 ? TextStyle.Settings.MessageColor1 : TextStyle.Settings.MessageColor2;
                foreach (var line in CnCNew.Ui.Rendering.UiSentenceLayout.Layout(history[_briefingShown], list.Font, _textMeasure, width, false).Lines) list.AddListRow(new UiListRow([line.Text], color));
            }
            return;
        }
        DiplomacyRows = BuildDiplomacyRows();
        for (var row = 0; row < DiplomacySlots; row++)
        {
            var line = row < DiplomacyRows.Length ? DiplomacyRows[row] : null;
            Text(_diplomacyPlayer[row], line?.Name, line?.Color);
            Text(_diplomacySide[row], line?.Side, line?.Color);
            Text(_diplomacyTeam[row], line?.Team, line?.Color);
            Text(_diplomacyStatus[row], line?.Status, line?.StatusColor);
            _diplomacyMute[row]?.SetHidden(line?.MuteConnection is null || line.Muted);
            _diplomacyUnmute[row]?.SetHidden(line?.MuteConnection is null || !line.Muted);
        }

        static void Text(UiWindow? window, string? text, WndColor? color)
        {
            if (window is null) return;
            window.SetHidden(text is null);
            if (text is null) return;
            window.SetText(text);
            window.SetTextColor(color);
        }
    }

    private ImmutableArray<DiplomacyRow> BuildDiplomacyRows()
    {
        if (Match is not LiveMatch live) return [];
        var world = Match.World;
        var rows = ImmutableArray.CreateBuilder<DiplomacyRow>();
        foreach (var slot in live.Match.Slots)
        {
            var human = slot.Description.Kind == MatchSlotKind.Human;
            var gone = human && Net is { } hud && hud.HasLeft(slot.Player.Value);
            var connection = human && !gone ? Net?.ConnectionOfOther(slot.Player.Value) : null;
            var name = Net?.NameOfSeat(slot.Player.Value) ?? (human ? _assets.Text.Get("GUI:Player") : _assets.Text.Get(StrengthLabel(slot.Description.AiStrength)));
            var side = slot.Description.Faction.Length == 0 && slot.Player != Match.Human && !world.AreAllies(Match.Human, slot.Player)
                ? _assets.Text.Get("GUI:Random") : Messages.FactionText(slot.Participant.Faction.Name);
            var team = !human && slot.Description.Team == 0 ? "Team:AI" : $"Team:{slot.Description.Team}";
            var alive = !world.IsPlayerDefeated(slot.Player);
            var color = Match.Colors.Of(slot.Player);
            rows.Add(new DiplomacyRow(slot.Player, name, side, _assets.Text.Get(team), _assets.Text.Get(gone ? "GUI:PlayerGone" : alive ? "GUI:PlayerAlive" : "GUI:PlayerDead"),
                Readable(new WndColor(color.Red, color.Green, color.Blue, 255)), gone ? GoneColor : alive ? AliveColor : DeadColor, connection, connection is { } id && Net!.IsMuted(id)));
        }
        return rows.ToImmutable();
    }

    /// <summary>A player's colour as text: the modern skin lifts a dark one until it reads on its panel.</summary>
    private WndColor Readable(WndColor color) => Skin.IsModern ? CnCNew.Ui.Skins.ModernLayout.Readable(color) : color;

    private static string StrengthLabel(int strength) => strength switch
    {
        (int)AiStrength.Easy => "GUI:EasyAI",
        (int)AiStrength.Hard => "GUI:HardAI",
        _ => "GUI:MediumAI",
    };
}
