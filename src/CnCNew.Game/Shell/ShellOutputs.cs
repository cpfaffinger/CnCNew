using CnCNew.Game.Campaign;
using CnCNew.Game.Live;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Shell;

/// <summary>
/// One thing the shell wants the host to do. The host drains these with <see cref="GameShell.DrainOutputs"/> after every
/// <see cref="GameShell.Update"/>/<see cref="GameShell.HandleInput"/> call, the same pull pattern <c>LiveMatch.DrainSoundCues</c>
/// uses for sound cues, so the shell never calls back into Stride or any other host API directly.
/// </summary>
public abstract record ShellOutput;

/// <summary>The setup screen produced a valid match; the host should construct and run a <c>LiveMatch</c> from it.</summary>
public sealed record StartMatchOutput(MatchSetup Setup) : ShellOutput;

/// <summary>A skirmish savegame was chosen in the load list; the host should restore it (<c>LiveMatch.Restore</c>) and run it like a started match.</summary>
public sealed record ResumeMatchOutput(byte[] Save, string Slot) : ShellOutput;

/// <summary>
/// A campaign mission is ready to run (a fresh start past its briefing, or a save just loaded): the host should step
/// <see cref="MissionRun.Session"/> every tick (<see cref="CnCNew.Simulation.Mission.MissionSession.Step"/>), drain its
/// <c>ScriptEngine.DrainOutputs()</c> for presentation, and call <see cref="GameShell.MissionFinished"/> once
/// <c>Session.IsOver</c>. <see cref="Controller"/> is the campaign driving the mission (<see langword="null"/> only if a
/// future revision plays a single mission map outside any campaign).
/// </summary>
public sealed record StartMissionOutput(MissionRun Run, CampaignController? Controller) : ShellOutput;

/// <summary>The player confirmed Quit; the host should terminate the application.</summary>
public sealed record ExitRequestedOutput : ShellOutput;

/// <summary>Settings were accepted (Options screen) or reset to defaults and saved; the host may want to react immediately (e.g. audio mixer volumes).</summary>
public sealed record SettingsChangedOutput(GameSettings Settings) : ShellOutput;

/// <summary>A UI sound cue name the host's audio layer should play (button click, hover, etc.); the shell does not know sound file paths.</summary>
public sealed record PlayUiSoundOutput(string EventName) : ShellOutput;

/// <summary>The display mode/resolution changed and should be applied to the swap chain.</summary>
public sealed record ApplyResolutionOutput(DisplayMode Mode, int Width, int Height, bool VSync) : ShellOutput;

/// <summary>Names of the UI sound cues the shell emits (retail <c>ButtonClick</c>/<c>GUIComboBoxTypeIn</c> style event names; the host owns the actual sound catalog lookup).</summary>
public static class ShellUiSounds
{
    public const string ButtonClick = "GUIClick";
    public const string ComboBoxClick = "GUIComboBoxClick";
    public const string SliderMoved = "GUISliderMoved";
}
