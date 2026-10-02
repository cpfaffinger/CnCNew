using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Testing;

/// <summary>Phase 12: the goal-directed objective driver (plans from the ending scripts backwards, avoids the opposite ending's scripts, moves counters the right way).</summary>
static partial class Tests
{
    public static void ScriptObjectiveDriverPlansBackwards()
    {
        // The win and lose sequences share one timer ("endT"). "LoseStart" comes first in script order and is just as cheap as "Gate", so a planner that only
        // counts leaves would route the victory through the lose sequence; the driver must see that "LoseFinal" ends the mission the wrong way.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Opener", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("ENABLE_SCRIPT", Scr.Script("Gate"))], oneShot: true),
            Scr.Script("LoseStart", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Decoy")))], [Scr.Do("SET_TIMER", Scr.Counter("endT"), Scr.Int(1)), Scr.Do("ENABLE_SCRIPT", Scr.Script("LoseFinal"))], oneShot: true),
            Scr.Script("Gate", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Target")))], [Scr.Do("SET_TIMER", Scr.Counter("endT"), Scr.Int(2)), Scr.Do("ENABLE_SCRIPT", Scr.Script("Win"))], active: false, oneShot: true),
            Scr.Script("Win", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("endT")))], [Scr.Do("VICTORY")], active: false, oneShot: true),
            Scr.Script("LoseFinal", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("endT")))], [Scr.Do("DEFEAT")], active: false, oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("BetaSoldier", 520m, 380m, "teamB", "Target"), Placed("BetaSoldier", 540m, 380m, "teamB", "Decoy")]);
        var win = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 3000);
        Check.That(win.ReachedByScript && win.OutcomeScript == "Win", $"The victory comes through 'Win' (outcome {win.Outcome} by '{win.OutcomeScript}', log: {string.Join("; ", win.Log)}).");
        Check.That(win.Log.Any(line => line.Contains("destroy named 'Target'", StringComparison.Ordinal)) && !win.Log.Any(line => line.Contains("Decoy", StringComparison.Ordinal)),
            "It destroyed the target of the win path and left the decoy of the lose path alone.");
        var lose = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, false, maxTicks: 3000);
        Check.That(lose.ReachedByScript && lose.OutcomeScript == "LoseFinal" && lose.Log.Any(line => line.Contains("Decoy", StringComparison.Ordinal)) && !lose.Log.Any(line => line.Contains("'Target'", StringComparison.Ordinal)),
            $"The defeat comes through the lose sequence ('{lose.OutcomeScript}', log: {string.Join("; ", lose.Log)}).");
    }

    public static void ScriptObjectiveDriverMovesCountersTheRightWay()
    {
        // "End" wants Score <= 0: only the script that decrements it helps; the one that increments it must not be chosen.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Init", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_COUNTER", Scr.Counter("Score"), Scr.Int(10))], oneShot: true),
            Scr.Script("Up", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Up")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(5), Scr.Counter("Score"))], oneShot: true),
            Scr.Script("Down", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Down")))], [Scr.Do("DECREMENT_COUNTER", Scr.Int(20), Scr.Counter("Score"))], oneShot: true),
            Scr.Script("End", [Scr.And(Scr.If("COUNTER", Scr.Counter("Score"), Scr.Compare(Scr.LessEqual), Scr.Int(0)))], [Scr.Do("DEFEAT")], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("AlphaSoldier", 120m, 100m, "teamA", "Up"), Placed("AlphaSoldier", 140m, 100m, "teamA", "Down")]);
        var result = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, false, maxTicks: 1500);
        Check.That(result.ReachedByScript && result.OutcomeScript == "End", $"The counter ending fires through the script ('{result.OutcomeScript}', log: {string.Join("; ", result.Log)}).");
        Check.That(result.Log.Any(line => line.Contains("'Down'", StringComparison.Ordinal)) && !result.Log.Any(line => line.Contains("'Up'", StringComparison.Ordinal)), "Only the decrementing script's trigger was used.");
        var again = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, false, maxTicks: 1500);
        Check.That(again.OutcomeTick == result.OutcomeTick && again.Log.SequenceEqual(result.Log), "A second run is identical.");
    }

    public static void ScriptExplainLeavesTheEngineUntouched()
    {
        // Explaining a script must not change what the engine later does: NAMED_ENTERED_AREA keeps its edge memory, counters it reads are not created.
        MapScript[] Scripts() => [
            Scr.Script("Enter", [Scr.And(Scr.If("NAMED_ENTERED_AREA", Scr.Unit("Walker"), Scr.Area("LeftArea")))], [Scr.Do("SET_FLAG", Scr.Flag("entered"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Count", [Scr.And(Scr.If("COUNTER", Scr.Counter("fresh"), Scr.Compare(Scr.Equal), Scr.Int(1)))], [Scr.Do("SET_FLAG", Scr.Flag("never"), Scr.Bool(true))]),
        ];
        MapObject[] Objects() => [Placed("AlphaSoldier", 100m, 200m, "teamA", "Walker")];
        var plain = StartMission(Scripts(), objects: Objects());
        var explained = StartMission(Scripts(), objects: Objects());
        for (var tick = 0; tick < 3; tick++)
        {
            explained.Engine.ExplainScript("Enter");
            explained.Engine.ExplainScript("Count");
            plain.Step();
            explained.Step();
        }
        Check.That(plain.StateHash() == explained.StateHash(), "A session whose scripts were explained every tick hashes exactly like one that was not.");
        Check.That(explained.Engine.FlagValue("entered") == plain.Engine.FlagValue("entered") && plain.Engine.FlagValue("entered"), "The entered-area edge was not swallowed by the explanation.");
    }
}
