using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Skirmish;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// The advanced options of a match: rules a normal player may want that the original game does not have. The first is the returning neutral buildings - a destroyed neutral building of
/// the map (an oil derrick, a hospital, a tech building) comes back at its place after a time. It was a debug option once and is part of the game now (the owner's decision of 8 November
/// 2026). The screen is opened from the skirmish setup and from the network room by their "Advanced options" button; in a room only the host changes them, everybody else sees what the
/// match will be played with.
/// <para>
/// The second is building in the fog of war: the original refuses a building site on ground the player has not explored or does not look at (<c>SHROUD_REVEALED</c>, the message
/// "can't build in the shroud"); with the option on the site check asks no shroud question (the owner's request of 9 November 2026).
/// <para>
/// The options travel in <see cref="MatchDebugOptions"/> of the match description: <see cref="MatchDebugOptions.NeutralRespawnSeconds"/> and <see cref="MatchDebugOptions.PlaceBuildingsInFog"/>
/// (the fields' names are an internal detail). This screen changes those two fields of the value it is given and keeps the debug fields (<see cref="DebugOptionsScreen"/>) as it found them.
/// </para>
/// Like <see cref="DebugOptionsScreen"/> it has no layout of the original: it is made at run time from the shipped <c>OptionsMenu.wnd</c> (the way <c>DebugOptionsScreen.BuildLayout</c> makes its own)
/// and drawn in the current skin. Accept hands the chosen options to whoever opened it.
/// </summary>
public sealed class AdvancedOptionsScreen : ShellScreen
{
    public const string File = "AdvancedOptions";

    /// <summary>The name of the button that opens the screen.</summary>
    public const string Button = "AdvancedOptionsButton.wnd:ButtonAdvancedOptions";

    /// <summary>The rows in their order (the suffix of the label's and the combo box's name, and the label's words).</summary>
    public static readonly ImmutableArray<(string Row, string Label)> Rows = [("Respawn", "Respawn neutral buildings"), ("RespawnAfter", "Respawn after"), ("FogBuild", "Build in the fog of war")];

    public static readonly ImmutableArray<(bool On, string Text)> RespawnChoices = [(false, "Off"), (true, "On")];

    /// <summary>Off, or a builder may be ordered to put a structure on ground that is not explored or not in sight (<see cref="MatchDebugOptions.PlaceBuildingsInFog"/>).</summary>
    public static readonly ImmutableArray<(bool On, string Text)> FogChoices = [(false, "Off"), (true, "On")];

    /// <summary>The times a destroyed neutral building takes to come back, in seconds of game time; <see cref="DefaultRespawnSeconds"/> is chosen when the option is switched on.</summary>
    public static readonly ImmutableArray<int> RespawnSeconds = [30, 60, 120, 300, 600];

    public const int DefaultRespawnSeconds = 120;

    /// <summary>
    /// The three buttons of this project - "AI options", "Advanced options" and "Debug options" - side by side in the gap between Start (to 268) and Back (from 530) of the setup's
    /// 800 x 600 layout (the room's Start and Back stand a little differently; its places are the same with <paramref name="top"/> and <paramref name="bottom"/> of its own), in a type of
    /// <see cref="ButtonFontSize"/> points. The widths follow the captions: the longest, "Advanced (on)", is the widest.
    /// </summary>
    public static (WndScreenRect Ai, WndScreenRect Advanced, WndScreenRect Debug) SetupButtons(int top, int bottom)
        => (new WndScreenRect(AiLeft, top, AiLeft + AiWidth, bottom, 800, 600), new WndScreenRect(AdvancedLeft, top, AdvancedLeft + AdvancedWidth, bottom, 800, 600),
            new WndScreenRect(DebugLeft, top, DebugLeft + DebugWidth, bottom, 800, 600));

    /// <summary>The type of the three buttons: the original skin's text is large for the window it stands in (the layout's buttons are made for one line of a big font), so it is small there; the modern skin keeps its body step.</summary>
    public static int ButtonFontSize(CnCNew.Ui.Skins.UiSkin skin) => skin.IsModern ? 10 : 9;
    private const int AiLeft = 272, AiWidth = 84, AdvancedLeft = 360, AdvancedWidth = 98, DebugLeft = 462, DebugWidth = 64;

    public override bool HidesScreensBelow => true;

    private readonly bool _readOnly;
    private readonly Action<MatchDebugOptions> _accepted;
    private readonly MatchDebugOptions _current;
    private bool _respawnOn;
    private int _respawnSeconds;
    private bool _fogOn;

    public AdvancedOptionsScreen(GameShell shell, MatchDebugOptions current, bool readOnly, Action<MatchDebugOptions> accepted)
        : base(shell, File, shell.Skin.Derive(BuildLayout(shell.Layouts.Get(ShellLayoutNames.OptionsMenu)), shell.Layouts.HeaderTemplates))
    {
        _current = current;
        _readOnly = readOnly;
        _accepted = accepted;
        _respawnOn = current.NeutralRespawnSeconds > 0;
        _respawnSeconds = _respawnOn ? current.NeutralRespawnSeconds : DefaultRespawnSeconds;
        _fogOn = current.PlaceBuildingsInFog;
        Tree.Register("AdvancedOptionsSystem", OnSystem);
        Tree.Register("AdvancedOptionsInput", OnInput);
        W("Title").SetText("Advanced options");
        W("NoteLabel").SetText(readOnly ? "The host chose these rules." : "Every player plays by these rules.");
        foreach (var (row, label) in Rows) W("Label" + row).SetText(label);
        Fill("Respawn", RespawnChoices.Select(choice => choice.Text));
        Fill("RespawnAfter", RespawnSeconds.Select(SecondsText));
        Fill("FogBuild", FogChoices.Select(choice => choice.Text));
        if (readOnly) W("ButtonAccept").SetHidden(true);
        Refresh();
    }

    /// <summary>"30 seconds", "2 minutes": how a time of the combo box reads.</summary>
    public static string SecondsText(int seconds)
        => seconds % 60 == 0 ? $"{seconds / 60} {(seconds == 60 ? "minute" : "minutes")}" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";

    /// <summary>Whether an advanced option is on; the debug options (<see cref="DebugOptionsScreen.IsOn"/>) are not counted.</summary>
    public static bool IsOn(MatchDebugOptions options) => options.NeutralRespawnSeconds > 0 || options.PlaceBuildingsInFog;

    /// <summary>The caption of the button: it says when something is on (short, because the button stands beside two others in a smaller type).</summary>
    public static string ButtonText(MatchDebugOptions options) => IsOn(options) ? "Advanced (on)" : "Advanced";

    private void Fill(string row, IEnumerable<string> entries)
    {
        var combo = W("Combo" + row);
        combo.ClearCombo();
        foreach (var entry in entries) combo.AddComboEntry(entry);
        combo.SetEnabled(!_readOnly);
    }

    private void Refresh()
    {
        W("ComboRespawn").SetComboSelection(_respawnOn ? 1 : 0);
        W("ComboRespawnAfter").SetComboSelection(NearestSeconds(_respawnSeconds));
        W("ComboRespawnAfter").SetEnabled(!_readOnly && _respawnOn);
        W("ComboFogBuild").SetComboSelection(_fogOn ? 1 : 0);
    }

    private static int NearestSeconds(int seconds)
    {
        var best = 0;
        for (var index = 1; index < RespawnSeconds.Length; index++)
            if (Math.Abs(RespawnSeconds[index] - seconds) < Math.Abs(RespawnSeconds[best] - seconds)) best = index;
        return best;
    }

    /// <summary>What the boxes say now, on top of the value the screen was given. A time the box does not offer is kept until the box is touched (a match from a save or another tool).</summary>
    public MatchDebugOptions Chosen => _current with { NeutralRespawnSeconds = _respawnOn ? _respawnSeconds : 0, PlaceBuildingsInFog = _fogOn };

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
        if (source == Name("ComboRespawn") && index < RespawnChoices.Length) _respawnOn = RespawnChoices[index].On;
        else if (source == Name("ComboRespawnAfter") && index < RespawnSeconds.Length) _respawnSeconds = RespawnSeconds[index];
        else if (source == Name("ComboFogBuild") && index < FogChoices.Length) _fogOn = FogChoices[index].On;
        else return;
        Refresh();
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    private static string Name(string window) => $"{File}.wnd:{window}";

    /// <summary>The screen's layout, made from the shipped <paramref name="options"/> (<c>OptionsMenu.wnd</c>) the way the debug screen's is.</summary>
    public static WndLayout BuildLayout(WndLayout options) => DebugOptionsScreen.BuildLayout(options, File, Rows, "AdvancedOptionsSystem", "AdvancedOptionsInput", labelRight: 430, comboLeft: 438);
}
