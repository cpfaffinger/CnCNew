using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Live;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>Which engines the AI options screen offers.</summary>
public enum AiEngineMode
{
    /// <summary>Both: the local skirmish on a map without scripts of its own.</summary>
    Free,
    /// <summary>Classic only: the map has scripts of its own and only the original AI's session runs them.</summary>
    ClassicOnly,
    /// <summary>New only: a match on a server is played by the new AI (the session of a room has no script engine).</summary>
    NewOnly,
}

/// <summary>One computer slot of the skirmish setup as the AI options screen shows it: its slot, how the screen names it, and the AI it is played by.</summary>
public sealed record AiOptionsRow(int Slot, string Name, MatchAi Ai);

/// <summary>
/// The AI options of the skirmish setup, opened by its "AI options" button: for every computer slot the engine (New - this port's AI, Classic - the original game's AI), the
/// tactics (balanced, rush, turtle, eco; only the new AI has them) and the strength (easy, medium, hard). The slot's own box in the setup stays a shortcut for the same values
/// (<see cref="SkirmishSetupScreen"/>): Rush Army there sets the tactics rush, and a slot switched to Classic shows its strength as Easy, Medium or Hard Army. Docs/GAME-SHELL.md.
/// <para>
/// Like <see cref="DebugOptionsScreen"/> it has no layout of the original: it is made at run time from the shipped <c>OptionsMenu.wnd</c> under names and callbacks of this
/// project (<see cref="BuildLayout"/>) and drawn in the current skin. Accept hands the chosen values to whoever opened it; Back drops them.
/// </para>
/// </summary>
public sealed class AiOptionsScreen : ShellScreen
{
    public const string File = "AiOptions";

    /// <summary>The name of the button that opens the screen.</summary>
    public const string Button = "AiOptionsButton.wnd:ButtonAiOptions";

    public const string ButtonCaption = "AI options";

    /// <summary>The width and type of the two buttons that stand side by side in the gap between Start and Back of the setup and of the room (800 x 600).</summary>
    public const int ButtonWidth = 124, ButtonFontSize = 12;

    /// <summary>The most computer slots a setup has (the first slot is the person's).</summary>
    public const int MaxRows = 7;

    public static readonly ImmutableArray<(SkirmishAiKind Kind, string Text)> EngineChoices = [(SkirmishAiKind.Builtin, "New"), (SkirmishAiKind.Original, "Classic")];

    /// <summary>The tactics boxes' entries: balanced is the AI as it was (no profile; <see cref="MatchAi.Tactics"/> null).</summary>
    public static readonly ImmutableArray<(AiTactics? Tactics, string Text)> TacticsChoices =
        [(null, "Balanced"), (AiTactics.Rush, "Rush"), (AiTactics.Turtle, "Turtle"), (AiTactics.Eco, "Eco")];

    public static readonly ImmutableArray<(ScriptDifficulty Strength, string Text)> StrengthChoices =
        [(ScriptDifficulty.Easy, "Easy"), (ScriptDifficulty.Normal, "Medium"), (ScriptDifficulty.Hard, "Hard")];

    public override bool HidesScreensBelow => true;

    private readonly ImmutableArray<AiOptionsRow> _rows;
    private readonly MatchAi[] _working;
    private readonly AiEngineMode _mode;
    private readonly bool _readOnly;
    private readonly bool _debugOn;
    private readonly Action<ImmutableArray<MatchAi>> _accepted;

    /// <param name="rows">The computer slots in slot order, at most <see cref="MaxRows"/>.</param>
    /// <param name="mode">Which engines can be chosen; with only one the engine boxes show it and cannot be changed.</param>
    /// <param name="readOnly">The values are shown and nothing can be changed (a member of a room sees what its host chose).</param>
    /// <param name="debugOn">The setup's debug options are on (they do not work with the Classic AI; the screen says so).</param>
    /// <param name="accepted">Told the AI of every row, in the order of <paramref name="rows"/>.</param>
    public AiOptionsScreen(GameShell shell, ImmutableArray<AiOptionsRow> rows, AiEngineMode mode, bool readOnly, bool debugOn, Action<ImmutableArray<MatchAi>> accepted)
        : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu)), shell.Layouts.HeaderTemplates))
    {
        if (rows.Length > MaxRows) throw new ArgumentException($"At most {MaxRows} computer slots.", nameof(rows));
        _rows = rows;
        _working = [.. rows.Select(row => row.Ai)];
        _mode = mode;
        _readOnly = readOnly;
        _debugOn = debugOn;
        _accepted = accepted;
        Tree.Register("AiOptionsSystem", OnSystem);
        Tree.Register("AiOptionsInput", OnInput);
        W("Title").SetText("AI options");
        W("HeadEngine").SetText("AI");
        W("HeadTactics").SetText("Tactics");
        W("HeadStrength").SetText("Strength");
        for (var index = 0; index < MaxRows; index++)
        {
            var shown = index < rows.Length;
            foreach (var part in RowParts) W(part + index).SetHidden(!shown);
            if (!shown) continue;
            W("Label" + index).SetText(rows[index].Name);
            Fill("ComboEngine" + index, EngineChoices.Select(choice => choice.Text));
            Fill("ComboTactics" + index, TacticsChoices.Select(choice => choice.Text));
            Fill("ComboStrength" + index, StrengthChoices.Select(choice => choice.Text));
        }
        W("HeadEngine").SetHidden(rows.Length == 0);
        W("HeadTactics").SetHidden(rows.Length == 0);
        W("HeadStrength").SetHidden(rows.Length == 0);
        if (readOnly) W("ButtonAccept").SetHidden(true);
        Refresh();
    }

    private static readonly string[] RowParts = ["Label", "ComboEngine", "ComboTactics", "ComboStrength"];

    private void Fill(string combo, IEnumerable<string> entries)
    {
        var box = W(combo);
        box.ClearCombo();
        foreach (var entry in entries) box.AddComboEntry(entry);
    }

    /// <summary>The kind the screen shows for a row: the only one on offer, whatever the row says.</summary>
    private SkirmishAiKind ShownKind(int index) => _mode switch { AiEngineMode.ClassicOnly => SkirmishAiKind.Original, AiEngineMode.NewOnly => SkirmishAiKind.Builtin, _ => _working[index].Kind };

    private void Refresh()
    {
        for (var index = 0; index < _rows.Length; index++)
        {
            var ai = _working[index];
            var classic = ShownKind(index) == SkirmishAiKind.Original;
            W("ComboEngine" + index).SetComboSelection(classic ? 1 : 0);
            W("ComboEngine" + index).SetEnabled(!_readOnly && _mode == AiEngineMode.Free);
            W("ComboTactics" + index).SetComboSelection(TacticsIndex(ai.Tactics));
            W("ComboTactics" + index).SetEnabled(!_readOnly && !classic);
            W("ComboStrength" + index).SetComboSelection((int)ai.Difficulty);
            W("ComboStrength" + index).SetEnabled(!_readOnly);
        }
        var notes = Notes([.. Enumerable.Range(0, _rows.Length).Select(ShownKind)], _mode, _readOnly, _debugOn);
        for (var line = 0; line < NoteLines; line++) W("Note" + line).SetText(line < notes.Length ? notes[line] : string.Empty);
    }

    private static int TacticsIndex(AiTactics? tactics)
    {
        for (var index = 0; index < TacticsChoices.Length; index++)
            if (TacticsChoices[index].Tactics == tactics || (tactics == AiTactics.Balanced && TacticsChoices[index].Tactics is null)) return index;
        return 0;
    }

    private const int NoteLines = 3;

    /// <summary>
    /// The lines under the table: what the two AIs are, why a box is greyed, what the combination costs. Only what applies to the rows now.
    /// </summary>
    public static ImmutableArray<string> Notes(IReadOnlyList<SkirmishAiKind> kinds, AiEngineMode mode, bool readOnly, bool debugOn)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        if (kinds.Count == 0)
        {
            lines.Add("No computer opponent: choose an army in the setup.");
            return lines.ToImmutable();
        }
        lines.Add("New: this game's AI. Classic: the original game's AI.");
        var anyClassic = kinds.Any(kind => kind == SkirmishAiKind.Original);
        var anyNew = kinds.Any(kind => kind == SkirmishAiKind.Builtin);
        if (mode == AiEngineMode.ClassicOnly) lines.Add("This map has scripts: only the Classic AI plays it.");
        else if (mode == AiEngineMode.NewOnly) lines.Add("A match on a server is played by the new AI only.");
        else if (anyClassic) lines.Add("Tactics are greyed: the Classic AI has none.");
        if (readOnly) lines.Add("The host chose these; all players play by them.");
        else if (anyClassic && debugOn) lines.Add("Debug options are on and need the new AI.");
        else if (anyClassic && anyNew) lines.Add("Both AIs in one match: no saving, no replay.");
        else if (anyClassic) lines.Add("A match with the Classic AI records no replay.");
        return lines.ToImmutable();
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonAccept"):
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                _accepted([.. _working]);
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonBack"):
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseTop();
                break;
            case UiEventKind.ComboSelected when e.Value >= 0 && !_readOnly:
                Choose(e.Source, e.Value);
                Shell.PlaySound(ShellUiSounds.ComboBoxClick);
                break;
        }
    }

    private void Choose(string source, int value)
    {
        for (var index = 0; index < _rows.Length; index++)
        {
            var ai = _working[index];
            if (source == Name("ComboEngine" + index) && _mode == AiEngineMode.Free && value < EngineChoices.Length) _working[index] = ai with { Kind = EngineChoices[value].Kind };
            else if (source == Name("ComboTactics" + index) && value < TacticsChoices.Length) _working[index] = ai with { Tactics = TacticsChoices[value].Tactics };
            else if (source == Name("ComboStrength" + index) && value < StrengthChoices.Length) _working[index] = ai with { Difficulty = StrengthChoices[value].Strength };
            else continue;
            Refresh();
            return;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    private static string Name(string window) => $"{File}.wnd:{window}";

    // ------------------------------------------------------------------------------------------------------------ the layouts

    /// <summary>The columns of the table in the layout's 800 x 600: the slot's name, then the three boxes.</summary>
    private const int LabelLeft = 146, LabelRight = 296, EngineLeft = 300, EngineRight = 390, TacticsLeft = 396, TacticsRight = 502, StrengthLeft = 508, StrengthRight = 604;

    /// <summary>
    /// The screen's layout, made from the shipped <paramref name="options"/> (<c>OptionsMenu.wnd</c>) the way <see cref="DebugOptionsScreen.BuildLayout"/> makes its own: the parent
    /// and panel with their callbacks renamed, the title and the line under it, the headings of the columns, <see cref="MaxRows"/> rows of a label and three combo boxes (copies of
    /// the resolution label and its box), three note lines, Accept and Back. A window the shipped file does not have is an error.
    /// </summary>
    public static WndLayout BuildLayout(WndLayout options)
    {
        ArgumentNullException.ThrowIfNull(options);
        static WndWindow Find(WndLayout layout, string name) => layout.AllWindows().FirstOrDefault(window => window.Name == name)
            ?? throw new InvalidDataException($"{layout.FileName} has no window '{name}'.");
        var parent = Find(options, "OptionsMenu.wnd:OptionsMenuParent");
        var panel = Find(options, "OptionsMenu.wnd:OptionsMenuParentOld");
        var label = Find(options, "OptionsMenu.wnd:ResolutionLabel");
        var combo = Find(options, "OptionsMenu.wnd:ComboBoxResolution");
        var accept = Find(options, "OptionsMenu.wnd:ButtonAccept");
        var back = Find(options, "OptionsMenu.wnd:ButtonBack");
        var line = Find(options, "OptionsMenu.wnd:Line");
        var title = panel.Children.FirstOrDefault(child => child.Type == WndWindowType.StaticText && child.Rect.Left == 152 && child.Rect.Top == 21)
            ?? throw new InvalidDataException("OptionsMenu.wnd has no title static text at 152, 21.");

        static WndScreenRect At(int left, int top, int right, int bottom) => new(left, top, right, bottom, 800, 600);
        static WndWindow Copy(WndWindow source, string name, WndScreenRect rect, bool keepText = false) => source with
        {
            Name = Name(name),
            Rect = rect,
            Status = source.Status & ~WndStatus.Hidden,
            Text = keepText ? source.Text : null,
            TooltipText = null,
            Children = [],
        };

        var children = ImmutableArray.CreateBuilder<WndWindow>();
        children.Add(Copy(title, "Title", title.Rect));
        children.Add(Copy(line, "Line", line.Rect));
        const int headingTop = 66, first = 94, step = 32, height = 24;
        children.Add(Copy(label, "HeadEngine", At(EngineLeft, headingTop, EngineRight, headingTop + height)));
        children.Add(Copy(label, "HeadTactics", At(TacticsLeft, headingTop, TacticsRight, headingTop + height)));
        children.Add(Copy(label, "HeadStrength", At(StrengthLeft, headingTop, StrengthRight, headingTop + height)));
        for (var index = 0; index < MaxRows; index++)
        {
            var top = first + index * step;
            children.Add(Copy(label, "Label" + index.ToString(CultureInfo.InvariantCulture), At(LabelLeft, top, LabelRight, top + height)));
            children.Add(Copy(combo, "ComboEngine" + index.ToString(CultureInfo.InvariantCulture), At(EngineLeft, top, EngineRight, top + height)));
            children.Add(Copy(combo, "ComboTactics" + index.ToString(CultureInfo.InvariantCulture), At(TacticsLeft, top, TacticsRight, top + height)));
            children.Add(Copy(combo, "ComboStrength" + index.ToString(CultureInfo.InvariantCulture), At(StrengthLeft, top, StrengthRight, top + height)));
        }
        var notesTop = first + MaxRows * step + 12;
        for (var note = 0; note < NoteLines; note++)
            children.Add(Copy(label, "Note" + note.ToString(CultureInfo.InvariantCulture), At(LabelLeft, notesTop + note * (height + 2), 640, notesTop + note * (height + 2) + height)));
        children.Add(Copy(accept, "ButtonAccept", accept.Rect, keepText: true));
        children.Add(Copy(back, "ButtonBack", back.Rect, keepText: true));

        var callbacks = parent.Callbacks with { System = "AiOptionsSystem", Input = "AiOptionsInput" };
        var newPanel = panel with { Name = Name("Panel"), Children = children.ToImmutable() };
        var root = parent with { Name = Name("Parent"), Callbacks = callbacks, Children = [newPanel] };
        return new WndLayout(File + ".wnd", options.FileVersion, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [root], [], ImmutableSortedDictionary<string, int>.Empty);
    }
}
