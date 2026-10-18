using System.Collections.Immutable;
using System.Text;
using CnCNew.Game.Replays;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>ReplayMenu.wnd</c>, the replay list (docs/REPLAY-BROWSER.md; <c>ReplayMenu.cpp</c>: <c>PopulateReplayFileListbox</c>, <c>ReplayMenuSystem</c>, rewritten): every replay
/// of the store in the list's four columns - name, date, state, map - the newest first and the first one chosen, a file this game cannot play in the original's grey. The
/// original's third column was the version of the game that recorded the file; here it is the state of the file against this game (<see cref="ReplayCompatibility"/>), and the
/// reason stands beside the list. "Load Replay" (or a double click) plays the chosen file, "Delete Replay" asks and deletes it, and the third button - the original copied a
/// file to the desktop - renames it (<see cref="ReplayRenameScreen"/>).
/// <para>
/// One window of this project's own is laid over the layout (<see cref="DetailsLayout"/>): the details of the chosen file - date, map, length, outcome, players and why it does
/// not play - in the free part of the button column. The original showed none of it; it is named in docs/REPLAY-BROWSER.md.
/// </para>
/// <para>
/// A fifth button, "Analysis", below "Back" (<see cref="AnalysisButton"/>, docs/MATCH-ANALYSIS.md) opens the analysis of the chosen file. It is a copy of the list's third
/// button, made from the shipped layout when the screen opens (<see cref="AnalysisLayoutOf"/>): its pictures and font are the original's, its place and text are this
/// project's.
/// </para>
/// </summary>
public sealed class ReplayMenuScreen : ShellScreen
{
    /// <summary>The name of the details window of <see cref="DetailsLayout"/>.</summary>
    public const string DetailsWindow = "ReplayDetails.wnd:StaticTextDetails";

    /// <summary>The name of the analysis button (<see cref="AnalysisLayoutOf"/>).</summary>
    public const string AnalysisButton = "ReplayAnalysis.wnd:ButtonAnalysis";

    /// <summary>The colours of <c>PopulateReplayFileListbox</c>: white for a file of this game, grey for one of another version.</summary>
    internal static readonly WndColor PlayableColor = new(255, 255, 255, 255);
    internal static readonly WndColor RefusedColor = new(128, 128, 128, 255);

    private ImmutableArray<ReplayEntry> _entries = [];
    private int _selected = -1;

    public ReplayMenuScreen(GameShell shell) : base(shell, ShellLayoutNames.ReplayMenu)
    {
        Tree.Register("ReplayMenuSystem", OnSystem);
        Tree.Register("ReplayMenuInput", OnInput);
        Tree.AddLayout(shell.Skin.Derive(DetailsLayout, shell.Layouts.HeaderTemplates));
        Tree.Register("ReplayAnalysisSystem", OnSystem);
        Tree.AddLayout(AnalysisLayoutOf(W("ButtonCopyReplay").Definition));
        Tree.FindByName(AnalysisButton)!.SetText(ReplayText.AnalysisButton);
        W("ButtonCopyReplay").SetText(ReplayText.RenameButton);
        Refresh(null);
        // ReplayMenuInit/Update: the gadgets fade in (ReplayMenuFade, reversed on shutdown).
        Transitions?.SetGroup("ReplayMenuFade");
    }

    protected override string? ExitTransitionGroup => "ReplayMenuFade";

    /// <summary>The files the list shows, in its order.</summary>
    public ImmutableArray<ReplayEntry> Entries => _entries;

    /// <summary>The chosen row, or -1.</summary>
    public int Selected => _selected;

    private UiWindow Details => Tree.FindByName(DetailsWindow) ?? throw new InvalidOperationException("The replay list has no details window.");

    /// <summary>Reads the store again and chooses the file named <paramref name="select"/> (or the first one, as the original does).</summary>
    internal void Refresh(string? select)
    {
        _entries = Shell.Replays!.List(Shell.ReplayMachine);
        var list = W("ListboxReplayFiles");
        list.ClearList();
        foreach (var entry in _entries) list.AddListRow(RowOf(Shell, entry));
        _selected = _entries.IsEmpty ? -1 : Math.Max(0, select is null ? 0 : _entries.IndexOf(_entries.FirstOrDefault(entry => entry.Name == select) ?? _entries[0]));
        if (_selected >= 0) list.SetListSelection(_selected);
        Update();
    }

    /// <summary>One row of the list (and of the rename popup's list): name, date, state, map; grey when this game cannot play the file.</summary>
    internal static UiListRow RowOf(GameShell shell, ReplayEntry entry)
    {
        var info = entry.Info;
        return new UiListRow([entry.Name, info is null ? string.Empty : ReplayText.Date(info.Recorded), entry.Compatibility.Label, info is null ? string.Empty : MapName(shell, info.MapPath)],
            entry.Compatibility.IsPlayable ? PlayableColor : RefusedColor);
    }

    internal static string MapName(GameShell shell, string mapPath)
        => shell.MapCatalog.ContainsKey(mapPath) ? shell.MapNames.DisplayName(mapPath, shell.MapCatalog, shell.TextSource) : Path.GetFileNameWithoutExtension(mapPath.Replace('\\', '/'));

    private ReplayEntry? Chosen => _selected >= 0 && _selected < _entries.Length ? _entries[_selected] : null;

    private void Update()
    {
        var chosen = Chosen;
        Details.SetText(chosen is null ? (_entries.IsEmpty ? "No replays yet. Every skirmish is recorded when it ends." : string.Empty)
            : ReplayText.Details(chosen, chosen.Info is { } info ? MapName(Shell, info.MapPath) : string.Empty));
        W("ButtonLoadReplay").SetEnabled(true);
        W("ButtonDeleteReplay").SetEnabled(true);
        W("ButtonCopyReplay").SetEnabled(true);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "ReplayMenu.wnd:ListboxReplayFiles":
                _selected = e.Value;
                Update();
                return;
            case UiEventKind.ListDoubleClicked when e.Source == "ReplayMenu.wnd:ListboxReplayFiles":
                _selected = e.Value;
                Update();
                Play();
                return;
            case UiEventKind.ButtonSelected:
                break;
            default:
                return;
        }
        switch (e.Source)
        {
            case "ReplayMenu.wnd:ButtonLoadReplay":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Play();
                break;
            case "ReplayMenu.wnd:ButtonDeleteReplay":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                if (Chosen is not { } doomed) { NothingChosen(); break; }
                Shell.AskYesNoLabels("GUI:DeleteFile", "GUI:AreYouSureDelete", () =>
                {
                    Shell.CloseTop();
                    Shell.Replays!.Delete(doomed.Name);
                    Refresh(null);
                }, Shell.CloseTop);
                break;
            case "ReplayMenu.wnd:ButtonCopyReplay":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                if (Chosen is not { } renamed) { NothingChosen(); break; }
                Shell.OpenReplayRename(this, renamed.Name);
                break;
            case "ReplayMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case AnalysisButton:
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                if (Chosen is not { } analysed) { NothingChosen(); break; }
                Shell.OpenReplayAnalysis(analysed.Name);
                break;
        }
    }

    private void NothingChosen() => Shell.TellLabels("GUI:NoFileSelected", "GUI:PleaseSelectAFile");

    private void Play()
    {
        if (Chosen is not { } entry) { NothingChosen(); return; }
        Shell.BeginReplay(entry.Name);
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    /// <summary>
    /// The details window of the list as a layout of this project's own (no file of the original): a static text in the free part of the button column of
    /// <c>ReplayMenu.wnd</c>, between the third button and "Back", in the layout's 800 x 600 coordinates; it takes no input.
    /// </summary>
    /// <summary>
    /// The analysis button as a layout of its own: the shipped definition of the list's third button (<paramref name="copyButton"/>) with another name, the place below
    /// "Back" in the button column (563, 452 to 735, 488 of the layout's 800 x 600 - the same width and height, the gap between the buttons) and a system callback of its own,
    /// because it stands outside the list's parent window.
    /// </summary>
    public static WndLayout AnalysisLayoutOf(WndWindow copyButton)
    {
        ArgumentNullException.ThrowIfNull(copyButton);
        var rect = copyButton.Rect;
        var button = copyButton with
        {
            Name = AnalysisButton,
            Rect = rect with { Left = 563, Top = 452, Right = 563 + rect.Width, Bottom = 452 + rect.Height },
            Callbacks = copyButton.Callbacks with { System = "ReplayAnalysisSystem" },
        };
        return new WndLayout("ReplayAnalysis.wnd", 2, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [button], [], ImmutableSortedDictionary<string, int>.Empty);
    }

    public static WndLayout DetailsLayout { get; } = WndParser.Parse(new MemoryStream(Encoding.ASCII.GetBytes("""
        FILE_VERSION = 2;
        STARTLAYOUTBLOCK
          LAYOUTINIT = [None];
          LAYOUTUPDATE = [None];
          LAYOUTSHUTDOWN = [None];
        ENDLAYOUTBLOCK
        WINDOW
          WINDOWTYPE = STATICTEXT;
          SCREENRECT = UPPERLEFT: 563 291,
                       BOTTOMRIGHT: 735 388,
                       CREATIONRESOLUTION: 800 600;
          NAME = "ReplayDetails.wnd:StaticTextDetails";
          STATUS = ENABLED+NOINPUT+NOFOCUS;
          STYLE = STATICTEXT;
          SYSTEMCALLBACK = "[None]";
          INPUTCALLBACK = "[None]";
          TOOLTIPCALLBACK = "[None]";
          DRAWCALLBACK = "[None]";
          FONT = NAME: "Arial", SIZE: 9, BOLD: 0;
          HEADERTEMPLATE = "[NONE]";
          TOOLTIPDELAY = -1;
          TEXT = "";
          TEXTCOLOR = ENABLED:  255 255 255 255,
                      ENABLEDBORDER:  0 0 0 255,
                      DISABLED: 192 192 192 255, DISABLEDBORDER: 64 64 64 255,
                      HILITE:   255 255 255 255, HILITEBORDER:   0 0 0 255;
          STATICTEXTDATA = CENTERED: 0;
        END
        """)), "ReplayDetails.wnd");
}

/// <summary>
/// <c>PopupReplay.wnd</c> as the rename popup of the replay list (<c>PopupReplay.cpp</c>, rewritten): the original named the last replay after a match - its list shows
/// the replays that exist, a click on one copies its name into the field, Enter or the save button takes the name. Here the field starts with the chosen replay's name and
/// the button renames it; a name that is not one or is taken is refused over the popup with the reason. The popup lies over the list (the original creates the layout over
/// the score screen), Escape and "Back" close it.
/// </summary>
public sealed class ReplayRenameScreen : ShellScreen
{
    private readonly ReplayMenuScreen _owner;
    private readonly string _name;
    private readonly ImmutableArray<ReplayEntry> _entries;

    public ReplayRenameScreen(GameShell shell, ReplayMenuScreen owner, string name) : base(shell, ShellLayoutNames.PopupReplay)
    {
        _owner = owner;
        _name = name;
        Tree.Register("PopupReplaySystem", OnSystem);
        Tree.Register("PopupReplayInput", OnInput);
        W("StaticTextTitle").SetText(ReplayText.RenameTitle);
        W("ButtonSave").SetText(ReplayText.RenameButton);
        W("PopupReplaySaved").SetHidden(true);
        _entries = owner.Entries;
        var list = W("ListboxGames");
        list.ClearList();
        foreach (var entry in _entries) list.AddListRow(ReplayMenuScreen.RowOf(shell, entry));
        var field = W("TextEntryReplayName");
        field.SetEditLimit(64);
        field.SetEntryText(name);
        Tree.SetFocus(field);
    }

    public override bool HidesScreensBelow => false;

    /// <summary>The replay being renamed.</summary>
    public string Name => _name;

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "PopupReplay.wnd:ListboxGames" && e.Value >= 0 && e.Value < _entries.Length:
                W("TextEntryReplayName").SetEntryText(_entries[e.Value].Name);
                return;
            case UiEventKind.EntryEditDone when e.Source == "PopupReplay.wnd:TextEntryReplayName":
                Apply();
                return;
            case UiEventKind.ButtonSelected when e.Source == "PopupReplay.wnd:ButtonSave":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Apply();
                return;
            case UiEventKind.ButtonSelected when e.Source == "PopupReplay.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                return;
        }
    }

    private void Apply()
    {
        var newName = W("TextEntryReplayName").TextEntry!.Text;
        try
        {
            Shell.Replays!.Rename(_name, newName);
        }
        catch (ReplayRefusedException refusal)
        {
            Shell.ShowError(ReplayText.CannotRenameTitle, refusal.Message);
            return;
        }
        Shell.CloseTop();
        _owner.Refresh(newName);
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}
