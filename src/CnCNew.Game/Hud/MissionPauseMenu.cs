using System.Collections.Immutable;
using CnCNew.Game.Campaign;
using CnCNew.Game.Save;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Hud;

/// <summary>
/// The in-mission pause menu for a campaign mission: <c>QuitMenu.wnd</c> (Resume/Options/Save-Load/Restart/Exit, the
/// same retail file <see cref="MatchController"/> uses for skirmish) plus <c>PopupSaveLoad.wnd</c> behind its
/// <c>ButtonSaveLoad</c>. Skirmish cannot be saved (the AI keeps state outside the world,
/// see <c>docs/CAMPAIGN-AND-SAVEGAMES.md</c>), so this is a mission-only, <see cref="MatchController"/>-independent
/// component: a host builds one alongside whatever drives the mission's camera/selection, feeds it input, and reacts to
/// <see cref="LoadRequested"/>/<see cref="RestartRequested"/>/<see cref="ExitRequested"/> by swapping the running
/// <see cref="MissionRun"/>. Saving is handled internally (writes straight to the <see cref="SaveGameStore"/> given at
/// construction) since it never needs to leave the mission.
/// </summary>
public sealed class MissionPauseMenu
{
    private readonly UiWindowTree _tree;
    private readonly UiWindow _quitMenuRoot;
    private readonly UiWindow _saveLoadRoot;
    private readonly SaveGameStore _store;
    private readonly Func<MissionRun> _currentRun;
    private readonly Func<CampaignProgress?> _currentProgress;
    private ImmutableArray<SaveSlot> _slots = [];
    private int _selectedSlot = -1;
    private string _pendingSlotName = string.Empty;

    public MissionPauseMenu(HudAssets assets, SaveGameStore store, Func<MissionRun> currentRun, Func<CampaignProgress?> currentProgress,
        UiViewport viewport, UiScaleOptions scaleOptions, IUiTextMeasure textMeasure)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _store = store;
        _currentRun = currentRun;
        _currentProgress = currentProgress;

        var services = assets.BuildTreeServices(textMeasure);
        _tree = new UiWindowTree(viewport, scaleOptions, services);
        var quitMenuRoots = _tree.AddLayout(assets.QuitMenu);
        _quitMenuRoot = quitMenuRoots.Single(window => window.Name == "QuitMenu.wnd:QuitMenuParent");
        _quitMenuRoot.SetHidden(true);
        _tree.Register("QuitMenuSystem", OnQuitMenuSystem);

        var saveLoadRoots = _tree.AddLayout(assets.PopupSaveLoad);
        _saveLoadRoot = saveLoadRoots.Single(window => window.Name == "PopupSaveLoad.wnd:SaveLoadMenu");
        _saveLoadRoot.SetHidden(true);
        _tree.Register("SaveLoadMenuSystem", OnSaveLoadSystem);
        HideSaveLoadSubPanels();
    }

    public bool IsOpen { get; private set; }
    private bool SaveLoadOpen => !_saveLoadRoot.IsHidden;

    /// <summary>A button of the pause menu this component does not itself resolve (the host swaps the running mission or quits).</summary>
    public event Action? ResumeRequested;
    public event Action? OptionsRequested;
    public event Action? RestartRequested;
    public event Action? ExitRequested;
    /// <summary>The player confirmed loading this slot; the host restores it (<c>SaveGame.Load</c>) and replaces the running mission.</summary>
    public event Action<string>? LoadRequested;

    public void Open()
    {
        IsOpen = true;
        _quitMenuRoot.SetHidden(false);
    }

    public void Close()
    {
        IsOpen = false;
        _quitMenuRoot.SetHidden(true);
        _saveLoadRoot.SetHidden(true);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Process(InputEvent inputEvent)
    {
        if (!IsOpen) return;
        switch (inputEvent)
        {
            case MouseMoveEvent m: _tree.MouseMove((int)m.Position.X, (int)m.Position.Y); break;
            case MouseButtonEvent b:
                var button = b.Button switch { MouseButton.Left => UiMouseButton.Left, MouseButton.Right => UiMouseButton.Right, _ => UiMouseButton.Middle };
                if (b.IsDown) _tree.MouseDown((int)b.Position.X, (int)b.Position.Y, button);
                else _tree.MouseUp((int)b.Position.X, (int)b.Position.Y, button);
                break;
            case KeyEvent { IsDown: true, Key: KeyCode.Escape }:
                if (SaveLoadOpen) { _saveLoadRoot.SetHidden(true); HideSaveLoadSubPanels(); }
                else { Close(); ResumeRequested?.Invoke(); }
                break;
        }
        _tree.Dispatch();
    }

    public UiDrawList BuildDrawList() => _tree.BuildDrawList();

    // ------------------------------------------------------------------------------------------------- QuitMenu.wnd

    private void OnQuitMenuSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "QuitMenu.wnd:ButtonReturn": Close(); ResumeRequested?.Invoke(); break;
            case "QuitMenu.wnd:ButtonOptions": OptionsRequested?.Invoke(); break;
            case "QuitMenu.wnd:ButtonSaveLoad": OpenSaveLoad(); break;
            case "QuitMenu.wnd:ButtonRestart": Close(); RestartRequested?.Invoke(); break;
            case "QuitMenu.wnd:ButtonExit": Close(); ExitRequested?.Invoke(); break;
        }
    }

    // ------------------------------------------------------------------------------------------------- PopupSaveLoad.wnd

    private void HideSaveLoadSubPanels()
    {
        W("LoadConfirmParent").SetHidden(true);
        W("OverwriteConfirmParent").SetHidden(true);
        W("SaveDescParent").SetHidden(true);
        W("DeleteConfirmParent").SetHidden(true);
    }

    private UiWindow W(string name) => _tree.FindByName($"PopupSaveLoad.wnd:{name}") ?? throw new InvalidOperationException($"PopupSaveLoad.wnd has no window '{name}'.");

    private void OpenSaveLoad()
    {
        _saveLoadRoot.SetHidden(false);
        HideSaveLoadSubPanels();
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        _slots = _store.List();
        _selectedSlot = -1;
        var list = W("ListboxGames");
        list.ClearList();
        foreach (var slot in _slots)
            list.AddListRow(new UiListRow(slot.Header is { } header ? $"{header.Title} (tick {header.Tick})" : $"{slot.Name} ({slot.Problem})"));
    }

    private void OnSaveLoadSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "PopupSaveLoad.wnd:ListboxGames": _selectedSlot = e.Value; return;
            case UiEventKind.EntryUpdateText when e.Source == "PopupSaveLoad.wnd:EntryDesc": return;
            case UiEventKind.ButtonSelected: break;
            default: return;
        }
        switch (e.Source)
        {
            case "PopupSaveLoad.wnd:ButtonBack": _saveLoadRoot.SetHidden(true); break;
            case "PopupSaveLoad.wnd:ButtonSave": OpenSaveDescription(); break;
            case "PopupSaveLoad.wnd:ButtonLoad": if (HasLoadableSelection()) W("LoadConfirmParent").SetHidden(false); break;
            case "PopupSaveLoad.wnd:ButtonLoadConfirm": AcceptLoad(); break;
            case "PopupSaveLoad.wnd:ButtonLoadCancel": W("LoadConfirmParent").SetHidden(true); break;
            case "PopupSaveLoad.wnd:ButtonDelete": if (_selectedSlot >= 0 && _selectedSlot < _slots.Length) W("DeleteConfirmParent").SetHidden(false); break;
            case "PopupSaveLoad.wnd:ButtonDeleteConfirm":
                if (_selectedSlot >= 0 && _selectedSlot < _slots.Length) _store.Delete(_slots[_selectedSlot].Name);
                W("DeleteConfirmParent").SetHidden(true);
                RefreshSlots();
                break;
            case "PopupSaveLoad.wnd:ButtonDeleteCancel": W("DeleteConfirmParent").SetHidden(true); break;
            case "PopupSaveLoad.wnd:ButtonSaveDescConfirm": AcceptSaveDescription(); break;
            case "PopupSaveLoad.wnd:ButtonSaveDescCancel": W("SaveDescParent").SetHidden(true); break;
            case "PopupSaveLoad.wnd:ButtonOverwriteConfirm": WriteSave(_pendingSlotName); W("OverwriteConfirmParent").SetHidden(true); break;
            case "PopupSaveLoad.wnd:ButtonOverwriteCancel": W("OverwriteConfirmParent").SetHidden(true); break;
        }
    }

    private bool HasLoadableSelection() => _selectedSlot >= 0 && _selectedSlot < _slots.Length && _slots[_selectedSlot].Header is not null;

    private void AcceptLoad()
    {
        W("LoadConfirmParent").SetHidden(true);
        if (!HasLoadableSelection()) return;
        var name = _slots[_selectedSlot].Name;
        Close();
        LoadRequested?.Invoke(name);
    }

    private void OpenSaveDescription()
    {
        var run = _currentRun();
        var defaultTitle = run.Mission is { } mission ? $"{_currentProgress()?.Campaign ?? "Mission"} - {mission.Name}" : run.MapPath;
        W("EntryDesc").SetEntryText(defaultTitle);
        W("SaveDescParent").SetHidden(false);
    }

    /// <summary>Turns the free-text description into a filesystem-safe slot name
    /// (<see cref="SaveGameStore.PathOf"/> rejects anything else); falls back to a tick-based name for an empty or
    /// entirely-invalid description.</summary>
    private static string SlotNameFor(string description, long tick)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder();
        foreach (var ch in description.Trim()) builder.Append(invalid.Contains(ch) || ch == '.' ? '_' : ch);
        var candidate = builder.ToString().Trim('_', ' ');
        return candidate.Length > 0 ? candidate : $"save-{tick}";
    }

    private void AcceptSaveDescription()
    {
        W("SaveDescParent").SetHidden(true);
        var description = W("EntryDesc").TextEntry!.Text;
        var run = _currentRun();
        var slot = SlotNameFor(description, run.Session.World.Tick);
        _pendingSlotName = slot;
        RefreshSlots();
        if (_slots.Any(existing => string.Equals(existing.Name, slot, StringComparison.Ordinal))) W("OverwriteConfirmParent").SetHidden(false);
        else WriteSave(slot);
    }

    private void WriteSave(string slot)
    {
        var run = _currentRun();
        var progress = _currentProgress();
        var title = run.Mission is { } mission ? $"{progress?.Campaign ?? "Mission"} - {mission.Name}" : run.MapPath;
        var bytes = SaveGame.Write(run, progress, title);
        _store.Save(slot, bytes);
        RefreshSlots();
    }
}
