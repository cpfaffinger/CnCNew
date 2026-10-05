using System.Collections.Immutable;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>OptionsMenu.wnd</c>. Wires the controls that have a matching <see cref="GameSettings"/> field: resolution, the
/// three volume sliders, and scroll speed. The remaining gadgets (gamma, detail/anti-aliasing, alternate mouse,
/// double-click attack-move, retaliation, the whole <c>NetworkParent</c> group, the advanced display sub-panel) have no
/// counterpart in <see cref="GameSettings"/> yet; they stay visible (so the screen looks authentic and every callback
/// name is still exercised) but are inert placeholders, listed in <c>docs/GAME-SHELL.md</c>. Display mode, aspect
/// handling, language, high contrast and the accessibility text scale have no gadget of their own in the shipped
/// layout at all, so they are not reachable from this screen yet (also documented; a future revision would need a
/// custom WND section or reuse of an inert placeholder combo).
/// Accept persists the working copy via <see cref="SettingsStore"/> and emits <see cref="SettingsChangedOutput"/>;
/// Cancel discards it; Defaults resets the working copy (not the persisted file) to <see cref="GameSettings.Default"/>.
/// </summary>
public sealed class OptionsScreen : ShellScreen
{
    private static readonly ImmutableArray<(int Width, int Height)> ResolutionChoices =
        [.. Resolutions.Standard.Select(r => (r.Width, r.Height)).OrderBy(r => r.Width).ThenBy(r => r.Height)];

    /// <summary>The original layers the options menu over the main menu (<c>getOptionsLayout</c>, <c>runInit</c>, <c>hide(FALSE)</c>): no shell push, the main menu is not shut down.</summary>
    public override bool HidesScreensBelow => false;

    private GameSettings _working;
    private ImmutableArray<(int Width, int Height)> _resolutionEntries;

    public OptionsScreen(GameShell shell) : base(shell, ShellLayoutNames.OptionsMenu)
    {
        _working = shell.Settings;
        Tree.Register("OptionsMenuSystem", OnSystem);
        Tree.Register("OptionsMenuInput", OnInput);
        W("LabelVersion").SetText("CnCNew 0.1.0");
        // The layout's TEXT of an entry field is the word "Entry"; OptionsMenuInit overwrites it with the stored values. There are none here
        // (network play is out of scope), so the fields start empty instead of showing the placeholder.
        W("TextEntryHTTPProxy").SetEntryText(string.Empty);
        W("TextEntryFirewallPortOverride").SetEntryText(string.Empty);
        BuildResolutionList();
        RefreshWidgets();
    }

    private void BuildResolutionList()
    {
        var desktop = (Shell.Environment.DesktopWidth, Shell.Environment.DesktopHeight);
        var current = (_working.Display.Width, _working.Display.Height);
        var candidates = ResolutionChoices.Where(r => r.Width <= desktop.Item1 && r.Height <= desktop.Item2).ToList();
        if (!candidates.Contains(current)) candidates.Insert(0, current);
        _resolutionEntries = [.. candidates];
        var combo = W("ComboBoxResolution");
        combo.ClearCombo();
        foreach (var (w, h) in _resolutionEntries) combo.AddComboEntry($"{w} x {h}");
    }

    private void RefreshWidgets()
    {
        var index = _resolutionEntries.IndexOf((_working.Display.Width, _working.Display.Height));
        W("ComboBoxResolution").SetComboSelection(index);
        SetSlider("SliderScrollSpeed", _working.Controls.ScrollSpeed);
        SetSlider("SliderMusicVolume", _working.Audio.Music);
        SetSlider("SliderSFXVolume", _working.Audio.Effects);
        SetSlider("SliderVoiceVolume", _working.Audio.Voice);
    }

    private void SetSlider(string name, float fraction)
    {
        var slider = W(name).Slider!;
        W(name).SetSliderValue(slider.Minimum + (int)Math.Round(fraction * (slider.Maximum - slider.Minimum)));
    }

    private float SliderFraction(string name)
    {
        var slider = W(name).Slider!;
        return slider.Maximum == slider.Minimum ? 0f : (slider.Value - slider.Minimum) / (float)(slider.Maximum - slider.Minimum);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == "OptionsMenu.wnd:ButtonAccept": Accept(); break;
            case UiEventKind.ButtonSelected when e.Source == "OptionsMenu.wnd:ButtonBack": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.CloseTop(); break;
            case UiEventKind.ButtonSelected when e.Source == "OptionsMenu.wnd:ButtonDefaults": Defaults(); break;
            case UiEventKind.ComboSelected when e.Source == "OptionsMenu.wnd:ComboBoxResolution":
                if (e.Value >= 0 && e.Value < _resolutionEntries.Length)
                {
                    var (w, h) = _resolutionEntries[e.Value];
                    _working = _working with { Display = _working.Display with { Width = w, Height = h } };
                }
                Shell.PlaySound(ShellUiSounds.ComboBoxClick);
                break;
            case UiEventKind.SliderTrack or UiEventKind.SliderDone:
                ApplySlider(e.Source);
                break;
        }
    }

    private void ApplySlider(string source)
    {
        switch (source)
        {
            case "OptionsMenu.wnd:SliderScrollSpeed": _working = _working with { Controls = _working.Controls with { ScrollSpeed = SliderFraction("SliderScrollSpeed") } }; break;
            case "OptionsMenu.wnd:SliderMusicVolume": _working = _working with { Audio = _working.Audio with { Music = SliderFraction("SliderMusicVolume") } }; break;
            case "OptionsMenu.wnd:SliderSFXVolume": _working = _working with { Audio = _working.Audio with { Effects = SliderFraction("SliderSFXVolume") } }; break;
            case "OptionsMenu.wnd:SliderVoiceVolume": _working = _working with { Audio = _working.Audio with { Voice = SliderFraction("SliderVoiceVolume") } }; break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }

    private void Accept()
    {
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        Shell.ApplySettings(_working);
        Shell.CloseTop();
    }

    private void Defaults()
    {
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        var d = GameSettings.Default;
        _working = _working with { Display = d.Display, Audio = d.Audio, Controls = d.Controls };
        BuildResolutionList();
        RefreshWidgets();
    }
}
