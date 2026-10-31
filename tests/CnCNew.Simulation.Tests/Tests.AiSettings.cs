using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;

/// <summary>
/// <c>CHOOSE_VICTIM_ALWAYS_USES_NORMAL</c> and <c>AIAttackSquadState::chooseVictim</c>; <c>PLAYER_SELECT_SKILLSET</c> and <c>SET_BASE_CONSTRUCTION_SPEED</c> as the original AI reads them
/// (<c>AIPlayer::doUpgradesAndSkills</c>, <c>AIPlayer::setTeamDelaySeconds</c>). F-22.
/// </summary>
static partial class Tests
{
    public static void OriginalAiObeysItsScriptedSettings()
    {
        MapScript[] settings = [Scr.Script("A Settings", [Scr.And(Scr.If("CONDITION_TRUE"))],
            [Scr.Do("PLAYER_SELECT_SKILLSET", Scr.Side("<This Player>"), Scr.Int(2)), Scr.Do("SET_BASE_CONSTRUCTION_SPEED", Scr.Side("<This Player>"), Scr.Int(1))], oneShot: true)];
        var (session, _, _, _, _, _) = StartOriginalSkirmish(alphaLibraryScripts: settings);
        var (other, _, _, _, _, _) = StartOriginalSkirmish();
        var alpha = new PlayerId(1);
        foreach (var match in new[] { session, other })
        {
            StepFrames(match, 3);
            Check.That(match.World.Submit(new GiveSciencePointsCommand(PlayerId.System, 1_000_000, match.World.Tick, alpha, 2)).Accepted, "The points are given.");
        }
        long Delay(CnCNew.Simulation.Skirmish.Original.ScriptedSkirmishSession match)
        {
            var start = match.Engine.AiTeamReadyTick(alpha);
            for (var frame = 0; frame < 900; frame++)
            {
                StepFrames(match, 1);
                var ready = match.Engine.AiTeamReadyTick(alpha);
                if (ready != start) return ready - match.World.Tick;
            }
            return -1;
        }
        var scripted = Delay(session);
        var plain = Delay(other);
        Check.That(scripted >= 0 && plain >= 0 && plain - scripted >= session.World.Frames(20), $"The team timer starts from the script's second, not from the data's two ({session.World.InFrames(scripted)} and {session.World.InFrames(plain)} frames).");
        Check.That(session.World.CanPurchaseScienceNow(alpha, "SCIENCE_One") && !session.World.CanPurchaseScienceNow(alpha, "SCIENCE_Three"), "The AI bought from the second skill set.");
        Check.That(!session.Engine.Diagnostics.Keys.Any(key => key.Contains("recorded", StringComparison.Ordinal)) && session.Engine.RecordedActions == 0, "Nothing was only recorded.");
        var restored = session.Save();
        Check.That(System.Text.Encoding.UTF8.GetString(restored).Contains("\"teamSeconds\":1", StringComparison.Ordinal), "The setting is saved.");
    }
}
