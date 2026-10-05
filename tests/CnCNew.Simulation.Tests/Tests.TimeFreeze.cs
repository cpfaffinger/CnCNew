using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;

/// <summary><c>FREEZE_TIME</c>/<c>UNFREEZE_TIME</c> (<c>ScriptEngine::m_freezeByScript</c>, <c>GameLogic::update</c>): the scripts run, the world does not.</summary>
static partial class Tests
{
    public static void ScriptFreezeTimeStopsTheWorldNotTheScripts()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Freeze", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("FREEZE_TIME"), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("hold"), Scr.Real(1m))], oneShot: true),
            Scr.Script("Thaw", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("hold")))], [Scr.Do("UNFREEZE_TIME"), Scr.Do("SET_FLAG", Scr.Flag("thawed"), Scr.Bool(true))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Sleeper")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);

        session.Step();
        Check.That(session.Engine.TimeFrozen, "FREEZE_TIME freezes the game when its script runs.");
        var frozenAt = session.World.Tick;
        var hashBefore = session.World.StateHash;
        var steps = 0;
        while (session.Engine.TimeFrozen && steps < 200)
        {
            var result = session.Step();
            steps++;
            if (session.Engine.TimeFrozen) Check.That(result.Events.Count == 0 && session.World.Tick == frozenAt, $"A frozen step changes nothing in the world (tick {session.World.Tick}, events {result.Events.Count}).");
        }
        Check.That(!session.Engine.TimeFrozen && session.Engine.FlagValue("thawed"), $"The script timer ran out while the world stood still, and its script unfroze time (after {steps} steps).");
        Check.That(steps is >= 28 and <= 40, $"A one second timer lasts its 30 script updates although the frame counter did not move ({steps} steps).");
        Check.That(session.World.Tick == frozenAt + 1 && session.World.StateHash != hashBefore, "The step that unfroze time was the first to advance the world again.");

        session.Step();
        Check.That(session.World.Tick == frozenAt + 2, "Afterwards the world runs as usual.");
    }

    public static void ScriptFrozenTimeSurvivesSaveAndEnds()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Freeze", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("FREEZE_TIME")], oneShot: true),
            Scr.Script("Win", [Scr.And(Scr.If("FLAG", Scr.Flag("win"), Scr.Bool(true)))], [Scr.Do("VICTORY")], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Sleeper")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var step = 0; step < 5; step++) session.Step();
        Check.That(session.Engine.TimeFrozen, "The mission stays frozen until a script thaws it.");

        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.Engine.TimeFrozen && restored.StateHash() == session.StateHash(), "A saved frozen mission is frozen again when it is loaded (m_freezeByScript is part of the script state).");
        for (var step = 0; step < 20; step++)
        {
            session.Step();
            restored.Step();
        }
        Check.That(restored.StateHash() == session.StateHash() && restored.World.Tick == session.World.Tick, "The saved and the live mission stay identical while frozen.");

        session.Engine.SignalUiInteraction("win");
        session.Step();
        Check.That(session.Engine.Outcome == MissionOutcome.Victory && !session.Engine.TimeFrozen, "The end of the mission lifts the freeze (forceUnfreezeTime on MSG_CLEAR_GAME_DATA).");
        var tick = session.World.Tick;
        session.Step();
        Check.That(session.World.Tick == tick + 1 || session.Engine.IsEnded, "The ended mission is no longer frozen.");
    }
}
