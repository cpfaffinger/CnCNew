using System.Collections.Immutable;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The debug options of a match (<see cref="MatchDebugOptions"/>), for testing: quick building and general points at the start. The screen is opened from
/// the skirmish setup and from the network room by their "Debug options" button; in a room only the host changes them, everybody else sees what the match will be played with.
/// The returning neutral buildings, which were a debug option once, are an advanced option of the game now (<see cref="AdvancedOptionsScreen"/>); both screens change one
/// <see cref="MatchDebugOptions"/> value and each leaves the other's fields as it found them.
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
        ("QuickBuild", "Quick build"), ("Recharge", "Power recharge"), ("QuickBuildFor", "Both apply to"), ("GeneralPoints", "General points at start"), ("MoneyButtons", "Money buttons"),
    ];

    /// <summary>Off, or everything built takes half, a quarter or a tenth of its time (<see cref="MatchDebugOptions.BuildTimePercent"/>).</summary>
    public static readonly ImmutableArray<(int Percent, string Text)> QuickBuildChoices =
    [
        (MatchDebugOptions.NormalBuildTime, "Off"), (MatchDebugOptions.HalfBuildPercent, "On: 50 % less build time"),
        (MatchDebugOptions.QuarterBuildPercent, "On: 75 % less build time"), (MatchDebugOptions.QuickBuildPercent, "On: 90 % less build time"),
    ];

    /// <summary>The recharge of every special power (a superweapon, the supply drop zone, a general's power): the same levels as quick build (<see cref="MatchDebugOptions.RechargePercent"/>).</summary>
    public static readonly ImmutableArray<(int Percent, string Text)> RechargeChoices =
    [
        (MatchDebugOptions.NormalBuildTime, "Off"), (MatchDebugOptions.HalfBuildPercent, "On: 50 % less recharge time"),
        (MatchDebugOptions.QuarterBuildPercent, "On: 75 % less recharge time"), (MatchDebugOptions.QuickBuildPercent, "On: 90 % less recharge time"),
    ];

    /// <summary>Who quick build and the faster recharge are for (<see cref="MatchDebugOptions.QuickBuildHumansOnly"/>): a second box rather than more levels, so that every level can be had for both (<see cref="MatchDebugOptions.QuickBuildHumansOnly"/>).</summary>
    public static readonly ImmutableArray<string> QuickBuildForChoices = ["Everybody", "Human players only"];

    /// <summary>Off, or two buttons in the in-game menu that give 10,000 credits to the player who presses them or to every player (<see cref="MatchDebugOptions.MoneyButtons"/>, #336).</summary>
    public static readonly ImmutableArray<string> MoneyButtonChoices = ["Off", "On: give 10,000 from the in-game menu"];

    /// <summary>
    /// The experience (skill points) every player starts with, which gives its general the rank those points reach (<c>Player::addSkillPoints</c>); 0 is the normal start at rank 1. The
    /// choices are the points the ranks of the shipped <c>Rank.ini</c> need (a test compares them with the data).
    /// </summary>
    public static readonly ImmutableArray<int> GeneralPointChoices = [0, 800, 1500, 2500, 5000];

    public override bool HidesScreensBelow => true;

    private readonly bool _readOnly;
    private readonly Action<MatchDebugOptions> _accepted;
    private MatchDebugOptions _working;

    public DebugOptionsScreen(GameShell shell, MatchDebugOptions current, bool readOnly, Action<MatchDebugOptions> accepted)
        : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu)), shell.Layouts.HeaderTemplates))
    {
        _working = current;
        _readOnly = readOnly;
        _accepted = accepted;
        Tree.Register("DebugOptionsSystem", OnSystem);
        Tree.Register("DebugOptionsInput", OnInput);
        W("Title").SetText("Debug options");
        W("NoteLabel").SetText(readOnly ? "For testing. The host chose these rules." : "For testing. Every player plays by these rules.");
        foreach (var (row, label) in Rows) W("Label" + row).SetText(label);
        Fill("QuickBuild", QuickBuildChoices.Select(choice => choice.Text));
        Fill("Recharge", RechargeChoices.Select(choice => choice.Text));
        Fill("QuickBuildFor", QuickBuildForChoices);
        Fill("MoneyButtons", MoneyButtonChoices);
        Fill("GeneralPoints", GeneralPointChoices.Select((points, index) => index == 0 ? "0 (normal start)" : $"{points.ToString(System.Globalization.CultureInfo.InvariantCulture)} (rank {index + 1})"));
        if (readOnly) W("ButtonAccept").SetHidden(true);
        Refresh();
    }

    /// <summary>Whether a debug option is on: the advanced option of the returning neutral buildings (<see cref="AdvancedOptionsScreen.IsOn"/>) is not one.</summary>
    public static bool IsOn(MatchDebugOptions options) => options.BuildTimePercent != MatchDebugOptions.NormalBuildTime || options.RechargePercent != MatchDebugOptions.NormalBuildTime || options.StartingGeneralPoints > 0 || options.MoneyButtons;

    /// <summary>The caption of the button: it says when something is on (short, because the button stands beside two others in a smaller type).</summary>
    public static string ButtonText(MatchDebugOptions options) => IsOn(options) ? "Debug (on)" : "Debug";

    private void Fill(string row, IEnumerable<string> entries)
    {
        var combo = W("Combo" + row);
        combo.ClearCombo();
        foreach (var entry in entries) combo.AddComboEntry(entry);
        combo.SetEnabled(!_readOnly);
    }

    private void Refresh()
    {
        W("ComboQuickBuild").SetComboSelection(NearestLevel(QuickBuildChoices, _working.BuildTimePercent));
        W("ComboRecharge").SetComboSelection(NearestLevel(RechargeChoices, _working.RechargePercent));
        W("ComboQuickBuildFor").SetComboSelection(_working.QuickBuildHumansOnly ? 1 : 0);
        W("ComboQuickBuildFor").SetEnabled(!_readOnly && AnyFasterRule(_working));
        W("ComboMoneyButtons").SetComboSelection(_working.MoneyButtons ? 1 : 0);
        W("ComboGeneralPoints").SetComboSelection(NearestPoints(_working.StartingGeneralPoints));
    }

    private static bool AnyFasterRule(MatchDebugOptions options) => options.BuildTimePercent != MatchDebugOptions.NormalBuildTime || options.RechargePercent != MatchDebugOptions.NormalBuildTime;

    private static int NearestLevel(ImmutableArray<(int Percent, string Text)> levels, int percent)
    {
        var best = 0;
        for (var index = 1; index < levels.Length; index++)
            if (Math.Abs(levels[index].Percent - percent) < Math.Abs(levels[best].Percent - percent)) best = index;
        return best;
    }

    private static int NearestPoints(int points)
    {
        var best = 0;
        for (var index = 1; index < GeneralPointChoices.Length; index++)
            if (Math.Abs(GeneralPointChoices[index] - points) < Math.Abs(GeneralPointChoices[best] - points)) best = index;
        return best;
    }

    /// <summary>What the boxes say now. A number of points the boxes do not offer is kept until the box is touched (a match from a save or another tool); the advanced option is kept as it was.</summary>
    public MatchDebugOptions Chosen => _working;

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
        if (source == Name("ComboQuickBuild") && index < QuickBuildChoices.Length)
        {
            _working = _working with { BuildTimePercent = QuickBuildChoices[index].Percent };
            _working = _working with { QuickBuildHumansOnly = AnyFasterRule(_working) && _working.QuickBuildHumansOnly };
        }
        else if (source == Name("ComboRecharge") && index < RechargeChoices.Length)
        {
            _working = _working with { RechargePercent = RechargeChoices[index].Percent };
            _working = _working with { QuickBuildHumansOnly = AnyFasterRule(_working) && _working.QuickBuildHumansOnly };
        }
        else if (source == Name("ComboQuickBuildFor") && index < QuickBuildForChoices.Length && AnyFasterRule(_working))
            _working = _working with { QuickBuildHumansOnly = index == 1 };
        else if (source == Name("ComboMoneyButtons") && index < MoneyButtonChoices.Length) _working = _working with { MoneyButtons = index == 1 };
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
    public static WndLayout BuildLayout(WndLayout options) => BuildLayout(options, File, Rows, "DebugOptionsSystem", "DebugOptionsInput");

    /// <summary>The same for any screen of this kind: its <paramref name="file"/> name, its <paramref name="rows"/> of a label and a combo box and its callbacks' names (<see cref="AdvancedOptionsScreen"/> is the other).</summary>
    internal static WndLayout BuildLayout(WndLayout options, string file, ImmutableArray<(string Row, string Label)> rows, string systemCallback, string inputCallback, int labelRight = 372, int comboLeft = 380)
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
        WndWindow Copy(WndWindow source, string name, WndScreenRect rect, bool keepText = false) => source with
        {
            Name = $"{file}.wnd:{name}",
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
        for (var index = 0; index < rows.Length; index++)
        {
            var top = first + index * step;
            children.Add(Copy(label, "Label" + rows[index].Row, At(152, top, labelRight, top + height)));
            children.Add(Copy(combo, "Combo" + rows[index].Row, At(comboLeft, top, 635, top + height)));
        }
        children.Add(Copy(label, "NoteLabel", At(152, first + rows.Length * step + 8, 635, first + rows.Length * step + 8 + height)));
        children.Add(Copy(accept, "ButtonAccept", accept.Rect, keepText: true));
        children.Add(Copy(back, "ButtonBack", back.Rect, keepText: true));

        var callbacks = parent.Callbacks with { System = systemCallback, Input = inputCallback };
        var newPanel = panel with { Name = $"{file}.wnd:Panel", Children = children.ToImmutable() };
        var root = parent with { Name = $"{file}.wnd:Parent", Callbacks = callbacks, Children = [newPanel] };
        return new WndLayout(file + ".wnd", options.FileVersion, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [root], [], ImmutableSortedDictionary<string, int>.Empty);
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
            // A header template's font wins over the window's own: the button that is set in a smaller type must not name the template of the big one.
            HeaderTemplate = fontSize is null ? template.HeaderTemplate : null,
            GroupCentered = true,
            Children = [],
        };
        return new WndLayout(file, 2, new WndLayoutCallbacks(null, null, null), WndDefaults.None, [button], [], ImmutableSortedDictionary<string, int>.Empty);
    }
}
