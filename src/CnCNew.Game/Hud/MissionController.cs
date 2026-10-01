using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Settings;
using CnCNew.Simulation.Scripting;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Hud;

/// <summary>
/// The playable HUD of a campaign mission: the skirmish HUD (<see cref="MatchController"/>: control bar, production, selection, orders, in-game menu, ticker) running over a
/// <see cref="LiveMission"/>, plus what only a mission has — the scripts' own on-screen text and radar events drawn over the HUD, and the human's selection reported to the
/// script engine (<c>NAMED_SELECTED</c>). The skirmish classes stay skirmish typed; <see cref="MatchController"/> only needs a <see cref="LiveGame"/>, so nothing of it was
/// copied.
/// </summary>
public sealed class MissionController
{
    private bool _endedFired;

    public MissionController(LiveMission mission, GameSettings settings, string language = "English", IUiTextMeasure? textMeasure = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        Mission = mission;
        Hud = new MatchController(mission, settings, language, textMeasure);
        Hud.Input.Selection.SelectionChanged += mission.Bridge.OnSelectionChanged;
        Hud.MatchEnded += OnEnded;
    }

    public LiveMission Mission { get; }

    /// <summary>The control bar, selection and order handling; feed it input events and draw its list as for a skirmish.</summary>
    public MatchController Hud { get; }

    public MissionPresentationDirector Director => Mission.Director;
    public MissionOutcome Outcome => Mission.Outcome;

    /// <summary>Raised once when the mission is over (victory or defeat plus the end-game delay): the host then calls <c>GameShell.MissionFinished</c>.</summary>
    public event Action<MissionOutcome>? MissionEnded;

    public void Process(InputEvent inputEvent) => Hud.Process(inputEvent);

    /// <summary>One rendered frame: advances the mission (<see cref="LiveGame.Advance"/>, which steps the simulation at 30 Hz), then the HUD's own clocks (ticker, quit menu).</summary>
    public void Update(double deltaSeconds)
    {
        Mission.Advance(deltaSeconds);
        Hud.Update(deltaSeconds);
    }

    /// <summary>
    /// The HUD with the mission's on-screen text (caption, subtitle, movie placeholder) appended; radar events are read from
    /// <see cref="MissionPresentationDirector.RadarBlips"/> by the host, which owns the minimap transform.
    /// </summary>
    public UiDrawList BuildDrawList(int width, int height)
    {
        var hud = Hud.BuildDrawList(width, height);
        return hud with { Commands = hud.Commands.AddRange(Director.BuildOverlayCommands(width, height)) };
    }

    private void OnEnded(MatchResult _)
    {
        if (_endedFired) return;
        _endedFired = true;
        MissionEnded?.Invoke(Outcome);
    }
}
