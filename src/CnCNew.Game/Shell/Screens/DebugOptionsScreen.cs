using System.Collections.Immutable;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The debug options of a match (<see cref="MatchDebugOptions"/>), for testing: quick building, returning neutral buildings and general points at the start. The screen is opened from
/// the skirmish setup and from the network room by their "Debug options" button; in a room only the host changes them, everybody else sees what the match will be played with.
/// <para>
/// Like <see cref="MoreOptionsScreen"/> it has no layout of the original: it is made at run time from the shipped <c>OptionsMenu.wnd</c> (its parent, panel, title, line, label and combo
/// box, Accept and Back) under names and callbacks of this project (<see cref="BuildLayout"/>), and drawn in the current skin. Accept hands the chosen options to whoever opened it.
/// </para>
/// </summary>
public sealed class DebugOptionsScreen : ShellScreen
{
    public const string File = "DebugOptions";

    /// <summary>The name of the button that opens the screen (<see cref="ButtonLayoutOf"/>).</summary>
    public const string Button = "DebugOptionsButton.wnd:ButtonDebugOptions";

    /// <summary>The rows in their order (the suffix of the label's and the combo box's name, and the label's words).</summary>
    public static readonly ImmutableArray<(string Row, string Label)> Rows =
    [
        ("QuickBuild", "Quick build"), ("Respawn", "Respawn neutral buildings"), ("RespawnAfter", "Respawn after"), ("GeneralPoints", "General points at start"),
    ];

    /// <summary>Off, or everything built takes a tenth of its time (<see cref="MatchDebugOptions.QuickBuildPercent"/>).</summary>
    public static readonly ImmutableArray<(int Percent, string Text)> QuickBuildChoices = [(MatchDebugOptions.NormalBuildTime, "Off"), (MatchDebugOptions.QuickBuildPercent, "On: 90 % less build time")];

    public static readonly ImmutableArray<(bool On, string Text)> RespawnChoices = [(false, "Off"), (true, "On")];

    /// <summary>The times a destroyed neutral building takes to come back, in seconds of game time; <see cref="DefaultRespawnSeconds"/> is chosen when the option is switched on.</summary>
    public static readonly ImmutableArray<int> RespawnSeconds = [30, 60, 120, 300, 600];

    public const int DefaultRespawnSeconds = 120;

    /// <summary>The general points every player starts with; 0 is the normal start.</summary>
    public static readonly ImmutableArray<int> GeneralPointChoices = [0, 1, 3, 5, 10, 20, 50];

    public override bool HidesScreensBelow => true;

    private readonly bool _readOnly;
    private readonly Action<MatchDebugOptions> _accepted;
    private MatchDebugOptions _working;
    private bool _respawnOn;
    private int _respawnSeconds;

    public DebugOptionsScreen(GameShell shell, MatchDebugOptions current, bool readOnly, Action<MatchDebugOptions> accepted)
        : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu)), shell.Layouts.HeaderTemplates))
    {
        _working = current;
        _readOnly = readOnly;
        _accepted = accepted;
        _respawnOn = current.NeutralRespawnSeconds > 0;
        _respawnSeconds = _respawnOn ? current.NeutralRespawnSeconds : DefaultRespawnSeconds;
        Tree.Register("DebugOptionsSystem", OnSystem);
        Tree.Register("DebugOptionsInput", OnInput);
        W("Title").SetText("Debug options");
        W("NoteLabel").SetText(readOnly ? "For testing. The host chose these; every player plays by them." : "For testing. Every player of the match plays by these rules.");
        foreach (var (row, label) in Rows) W("Label" + row).SetText(label);
        Fill("QuickBuild", QuickBuildChoices.Select(choice => choice.Text));
        Fill("Respawn", RespawnChoices.Select(choice => choice.Text));
        Fill("RespawnAfter", RespawnSeconds.Select(SecondsText));
        Fill("GeneralPoints", GeneralPointChoices.Select(points => points == 0 ? "0 (normal start)" : points.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (readOnly) W("ButtonAccept").SetHidden(true);
        Refresh();
    }

    /// <summary>"30 s", "2 minutes": how a time of the combo box reads.</summary>
    public static string SecondsText(int seconds)
        => seconds % 60 == 0 ? $"{seconds / 60} {(seconds == 60 ? "minute" : "minutes")}" : $"{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds";

    /// <summary>The options as a short line (the button's caption says "on" when any is): "quick build, respawn after 120 s, 5 general points".</summary>
    public static string Summary(MatchDebugOptions options)
    {
        var parts = new List<string>();
        if (options.BuildTimePercent != MatchDebugOptions.NormalBuildTime) parts.Add($"build time {options.BuildTimePercent.ToString(System.Globalization.CultureInfo.InvariantCulture)} %");
        if (options.NeutralRespawnSeconds > 0) parts.Add($"respawn after {options.NeutralRespawnSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} s");
        if (options.StartingGeneralPoints > 0) parts.Add($"{options.StartingGeneralPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)} general points");
        return parts.Count == 0 ? "off" : string.Join(", ", parts);
    }

    /// <summary>The caption of the button: it says when something is on (short, because the button stands beside the "AI options" button in a type of 12 points).</summary>
    public static string ButtonText(MatchDebugOptions options) => options.IsDefault ? "Debug options" : "Debug (on)";

    private void Fill(string row, IEnumerable<string> entries)
    {
        var combo = W("Combo" + row);
        combo.ClearCombo();
        foreach (var entry in entries) combo.AddComboEntry(entry);
        combo.SetEnabled(!_readOnly);
    }

    private void Refresh()
    {
        W("ComboQuickBuild").SetComboSelection(_working.BuildTimePercent == MatchDebugOptions.NormalBuildTime ? 0 : 1);
        W("ComboRespawn").SetComboSelection(_respawnOn ? 1 : 0);
        W("ComboRespawnAfter").SetComboSelection(NearestSeconds(_respawnSeconds));
        W("ComboRespawnAfter").SetEnabled(!_readOnly && _respawnOn);
        W("ComboGeneralPoints").SetComboSelection(NearestPoints(_working.StartingGeneralPoints));
    }

    private static int NearestSeconds(int seconds)
    {
        var best = 0;
        for (var index = 1; index < RespawnSeconds.Length; index++)
            if (Math.Abs(RespawnSeconds[index] - seconds) < Math.Abs(RespawnSeconds[best] - seconds)) best = index;
        return best;
    }

    private static int NearestPoints(int points)
    {
        var best = 0;
        for (var index = 1; index < GeneralPointChoices.Length; index++)
            if (Math.Abs(GeneralPointChoices[index] - points) < Math.Abs(GeneralPointChoices[best] - points)) best = index;
        return best;
    }

    /// <summary>What the boxes say now. A time or a number of points the boxes do not offer is kept until the box is touched (a match from a save or another tool).</summary>
    public MatchDebugOptions Chosen => _working with { NeutralRespawnSeconds = _respawnOn ? _respawnSeconds : 0 };

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == Name("ButtonAccept"):
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                _accepted(Chosen);
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

    private void Choose(string source, int index)
    {
        if (source == Name("ComboQuickBuild") && index < QuickBuildChoices.Length) _working = _working with { BuildTimePercent = QuickBuildChoices[index].Percent };
        else if (source == Name("ComboRespawn") && index < RespawnChoices.Length) _respawnOn = RespawnChoices[index].On;
        else if (source == Name("ComboRespawnAfter") && index < RespawnSeconds.Length) _respawnSeconds = RespawnSeconds[index];
        else if (source == Name("ComboGeneralPoints") && index < GeneralPointChoices.Length) _working = _working with { StartingGeneralPoints = GeneralPointChoices[index] };
        else return;
        Refresh();
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    private static string Name(string window) => $"{File}.wnd:{window}";

    // ------------------------------------------------------------------------------------------------------------ the layouts

    /// <summary>
    /// The screen's layout, made from the shipped <paramref name="options"/> (<c>OptionsMenu.wnd</c>) in the layout's 800 x 600 coordinates: its parent and panel with their own
    /// callbacks renamed, the title, the line under it, the rows of a label and a combo box (copies of the resolution label and its combo box), a note line and Accept and Back.
    /// A window the shipped file does not have is an error.
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
        const int first = 72, step = 34, height = 24;
        for (var index = 0; index < Rows.Length; index++)
        {
            var top = first + index * step;
            children.Add(Copy(label, "Label" + Rows[index].Row, At(152, top, 372, top + height)));
            children.Add(Copy(combo, "Combo" + Rows[index].Row, At(380, top, 635, top + height)));
        }
        children.Add(Copy(label, "NoteLabel", At(152, first + Rows.Length * step + 8, 635, first + Rows.Length * step + 8 + height)));
        children.Add(Copy(accept, "ButtonAccept", accept.Rect, keepText: true));
        children.Add(Copy(back, "ButtonBack", back.Rect, keepText: true));

        var callbacks = parent.Callbacks with { System = "DebugOptionsSystem", Input = "DebugOptionsInput" };
        var newPanel = panel with { Name = Name("Panel"), Children = children.ToImmutable() };
        var root = parent with { Name = Name("Parent"), Callbacks = callbacks, Children = [newPanel] };
        return new WndLayout(File + ".wnd", options.FileVersion, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [root], [], ImmutableSortedDictionary<string, int>.Empty);
    }

    /// <summary>
    /// The "Debug options" button as a layout of its own, standing on <paramref name="rect"/> (in the host screen's 800 x 600): a copy of the shipped <paramref name="template"/> button
    /// under another name with a system callback of its own (<paramref name="callback"/>), because the host screen's layout has no such button.
    /// </summary>
    public static WndLayout ButtonLayoutOf(WndWindow template, WndScreenRect rect, string callback) => ButtonLayoutOf(template, rect, callback, Button, "DebugOptionsButton.wnd");

    /// <summary>The same under another <paramref name="name"/> and layout file: the "AI options" button of the setup is made the same way (<see cref="AiOptionsScreen.Button"/>).</summary>
    public static WndLayout ButtonLayoutOf(WndWindow template, WndScreenRect rect, string callback, string name, string file, int? fontSize = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        var button = template with
        {
            Name = name,
            Rect = rect,
            Status = template.Status & ~WndStatus.Hidden,
            Text = null,
            TooltipText = null,
            Callbacks = template.Callbacks with { System = callback },
            Font = fontSize is { } size && template.Font is { } font ? font with { Size = size } : template.Font,
            GroupCentered = true,
            Children = [],
        };
        return new WndLayout(file, 2, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [button], [], ImmutableSortedDictionary<string, int>.Empty);
    }
}
