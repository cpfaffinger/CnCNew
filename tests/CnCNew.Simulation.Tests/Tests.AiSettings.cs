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
    private static (bool Near, bool Last) SquadVictimOn(ScriptDifficulty difficulty, bool alwaysNormal)
    {
        MapScriptAction[] actions = alwaysNormal
            ? [Scr.Do("CHOOSE_VICTIM_ALWAYS_USES_NORMAL", Scr.Int(1)), Scr.Do("NAMED_ATTACK_TEAM", Scr.Unit("Hunter"), Scr.Team("teamB"))]
            : [Scr.Do("NAMED_ATTACK_TEAM", Scr.Unit("Hunter"), Scr.Team("teamB"))];
        var (map, fixture, terrain) = MissionMap([Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], actions, oneShot: true)],
            objects: [Placed("MoodGunner", 100m, 200m, "teamA", "Hunter"), Placed("Bystander", 100m, 330m, "teamB", "Near"), Placed("Bystander", 320m, 200m, "teamB", "Last")], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard with { Difficulty = difficulty }, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(450));
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
        return (Hurt(session, "Near"), Hurt(session, "Last"));
    }

    public static void SquadAttackersChooseByDifficulty()
    {
        Check.That(SquadVictimOn(ScriptDifficulty.Normal, false) is (true, false), "On normal the attacker takes the closest unit of the team.");
        Check.That(SquadVictimOn(ScriptDifficulty.Hard, false) is (false, true), "On hard every attacker takes the first of the team's list, the last that joined.");
        Check.That(SquadVictimOn(ScriptDifficulty.Hard, true) is (true, false), "CHOOSE_VICTIM_ALWAYS_USES_NORMAL makes it the closest on hard as well.");
        var easy = SquadVictimOn(ScriptDifficulty.Easy, false);
        Check.That(easy.Near != easy.Last, "On easy it is one random unit.");
        Check.That(SquadVictimOn(ScriptDifficulty.Easy, true) is (true, false), "And the closest with the switch.");
    }

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
