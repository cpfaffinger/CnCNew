using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;

namespace CnCNew.Game.Campaign;

/// <summary>How a mission ended for the campaign.</summary>
public enum MissionConclusion { Victory, Defeat }

/// <summary>
/// Drives one campaign: hands out the current mission as a <see cref="MissionRun"/> (counting the attempt) and turns the mission's script outcome into
/// progress. A mission still running has no conclusion and is refused; victory advances to the next mission, defeat keeps the current one for a retry
/// (<c>CampaignManager::gotoNextMission</c>, <c>ScriptActions::doVictory/doDefeat</c> calling <c>SetVictorious</c>).
/// </summary>
public sealed class CampaignController(MissionContext context, CampaignDefinition campaign, CampaignProgress progress, uint baseSeed = 0xC0FFEE09)
{
    public CampaignDefinition Campaign { get; } = campaign;
    public CampaignProgress Progress { get; private set; } = progress;
    public bool IsFinished => Progress.Finished;

    public CampaignMission CurrentMission
        => Campaign.FindMission(Progress.CurrentMission) ?? throw new InvalidOperationException($"Mission {Progress.CurrentMission} is not part of campaign {Campaign.Name}.");

    /// <summary>
    /// Starts the current mission: counts the attempt and builds the world. The seed depends on the mission and the attempt so a retry is not a
    /// replay of the last try. <paramref name="humanSideName"/> is the host's choice of human side for a map that does not mark one itself (the
    /// Generals' Challenge maps model the chosen general as the map's only human side but leave naming it to the host); empty keeps
    /// <see cref="Mission.MissionSession.LocalPlayerOf"/>'s own fallback; for a Generals' Challenge campaign it is chosen by <see cref="ChallengeSides"/> instead
    /// of left empty. <paramref name="presentationLatencyTicks"/> is for a host that has no <c>MissionPresentationDirector</c> to complete speech/movie/camera
    /// outputs itself: they then count as playing for that many ticks (0: finished at once, the headless default).
    /// </summary>
    public MissionRun StartCurrentMission(long maxTicks = 0, string humanSideName = "", int presentationLatencyTicks = 0)
    {
        if (Progress.Finished) throw new InvalidOperationException("The campaign is finished.");
        var mission = CurrentMission;
        if (humanSideName.Length == 0 && Campaign.IsChallenge) humanSideName = ChallengeSides.HumanSideFor(context.LoadMap(mission.MapPath));
        Progress = Progress.Started();
        var attempts = Progress.AttemptsOf(mission.Name);
        var seed = unchecked(baseSeed + (uint)attempts * 7919u + (uint)Campaign.Sequence.IndexOf(mission) * 104729u);
        var options = new MissionOptions(seed, Progress.Difficulty, maxTicks, PresentationLatencyTicks: presentationLatencyTicks, MissionAttempts: attempts, HumanSideName: humanSideName,
            HumanFaction: Campaign.IsChallenge ? Campaign.PlayerFaction : string.Empty);
        return MissionRun.Load(context, mission.MapPath, options, mission);
    }

    /// <summary>Rebuilds a saved run of the current mission (used by loading a savegame); the attempt count is not increased.</summary>
    public MissionRun ResumeCurrentMission(ReadOnlySpan<byte> state)
        => MissionRun.Restore(context, CurrentMission.MapPath, state, CurrentMission);

    /// <summary>Applies the outcome of a finished mission to the progress and returns it; a run that has no outcome yet is refused.</summary>
    public MissionConclusion Conclude(MissionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Mission is null || !string.Equals(run.Mission.Name, Progress.CurrentMission, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The run is not of the current mission.");
        switch (run.Session.Outcome)
        {
            case MissionOutcome.Victory:
                Progress = Progress.Won(Campaign);
                return MissionConclusion.Victory;
            case MissionOutcome.Defeat:
                Progress = Progress.Lost();
                return MissionConclusion.Defeat;
            default:
                throw new InvalidOperationException("The mission has no outcome yet.");
        }
    }
}
