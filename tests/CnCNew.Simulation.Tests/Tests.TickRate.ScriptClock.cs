using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

/// <summary>
/// Phase 23: the clock of the script VM is the logic frame of the original (<c>ScriptEngine::update</c> runs once per frame). Each test runs the same small mission at
/// 30, 60 and 120 Hz and requires the same thing to happen on the same frame of the original - which is the same game time. Before the engine kept that clock, a script
/// was evaluated once per tick and a timer counted ticks, so everything below happened earlier or later at 60 and 120 Hz.
/// </summary>
static partial class Tests
{
    private static (MissionSession Session, SyntheticFixture Fixture, TerrainMap Terrain, MapData Map) RatedMissionWithParts(SimulationRate rate, MapScript[] scripts, MapObject[]? objects = null)
    {
        var (map, fixture, terrain) = MissionMap(scripts, objects: objects, rate: rate);
        return (MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map), fixture, terrain, map);
    }

    /// <summary>Steps until the flag is up and returns the tick whose step raised it (-1: never within the limit).</summary>
    private static long TickThatRaised(MissionSession session, string flag, long limit)
    {
        for (long step = 0; step < limit; step++)
        {
            var tick = session.World.Tick;
            session.Step();
            if (session.Engine.FlagValue(flag)) return tick;
        }
        return -1;
    }

    private static MapScript RaiseWhen(string name, string flag, params MapCondition[] conditions)
        => Scr.Script(name, [Scr.And(conditions)], [Scr.Do("SET_FLAG", Scr.Flag(flag), Scr.Bool(true))], oneShot: true);

    public static void TickRateScriptClockCountsEvaluationsPerFrame()
    {
        const int wanted = 90;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var session = RatedMission(rate, [
                Scr.Script("Count", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("n"))]),
                RaiseWhen("Done", "done", Scr.If("COUNTER", Scr.Counter("n"), Scr.Compare(Scr.GreaterEqual), Scr.Int(wanted))),
            ]);
            var tick = TickThatRaised(session, "done", 2_000);
            // 'Count' runs in the updates of the frames 0 .. 89; 'Done' follows it in the list and sees 90 in the update of frame 89.
            Check.That(tick == (wanted - 1) * factor, $"{rate}: a counter a script raises on every evaluation reaches {wanted} in frame {wanted - 1} of the original (got tick {tick}).");
            Check.That(session.Engine.CounterValue("n") == wanted, $"{rate}: the script was evaluated once per frame of the original, not once per tick (n = {session.Engine.CounterValue("n")}).");
            // The ticks between two frames change nothing in the script state but the tick it was last called for.
            var before = session.Engine.ExecutedScripts;
            for (var step = 0; step < 10 * factor; step++) session.Step();
            Check.That(session.Engine.ExecutedScripts - before == 10, $"{rate}: ten frames of the original are ten evaluations (got {session.Engine.ExecutedScripts - before}).");
        }
    }

    public static void TickRateScriptClockTimersCountFrames()
    {
        const int frames = 45;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var session = RatedMission(rate, [
                Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))],
                    [Scr.Do("SET_TIMER", Scr.Counter("inFrames"), Scr.Int(frames)), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("inSeconds"), Scr.Real(1.5m))], oneShot: true),
                RaiseWhen("FramesOver", "framesOver", Scr.If("TIMER_EXPIRED", Scr.Counter("inFrames"))),
                RaiseWhen("SecondsOver", "secondsOver", Scr.If("TIMER_EXPIRED", Scr.Counter("inSeconds"))),
            ]);
            session.Step();
            var engine = session.Engine;
            Check.That(engine.CounterValue("inFrames") == frames && engine.CounterValue("inSeconds") == frames,
                $"{rate}: a timer of {frames} frames and one of 1.5 s both hold {frames} frames of the original ({engine.CounterValue("inFrames")}, {engine.CounterValue("inSeconds")}).");
            for (var step = 1; step < 10 * factor; step++) session.Step();
            Check.That(engine.CounterValue("inFrames") == frames - 9, $"{rate}: after the updates of nine more frames the timer has counted nine down (got {engine.CounterValue("inFrames")}).");
            var framesOver = -1L;
            var secondsOver = -1L;
            for (var step = 0; step < 200 * factor && (framesOver < 0 || secondsOver < 0); step++)
            {
                var tick = session.World.Tick;
                session.Step();
                if (framesOver < 0 && engine.FlagValue("framesOver")) framesOver = tick;
                if (secondsOver < 0 && engine.FlagValue("secondsOver")) secondsOver = tick;
            }
            // Set to 45 in the update of frame 0, counted down in the updates of the frames 1 .. 45: below 1 in frame 45.
            Check.That(framesOver == frames * factor && secondsOver == frames * factor,
                $"{rate}: both timers run out in frame {frames} of the original, 1.5 s after they were set (ticks {framesOver} and {secondsOver}).");
        }
    }

    public static void TickRateScriptClockComparesTimersWithFrameLiterals()
    {
        // The pattern of MD_USA02 ('Global Timers' sets 'Convoy Trucks' to 16000, 'Truck01 Deploy' waits for COUNTER(Convoy Trucks < 15000)): a map compares a running
        // timer with a number of frames and does arithmetic on it. With a timer in ticks that wait was 283 s instead of 33 s at 60 Hz.
        foreach (var (rate, factor) in ScriptingRates)
        {
            var session = RatedMission(rate, [
                Scr.Script("Global Timers", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_TIMER", Scr.Counter("Convoy"), Scr.Int(16_000))], oneShot: true),
                RaiseWhen("Deploy", "deployed", Scr.If("COUNTER", Scr.Counter("Convoy"), Scr.Compare(Scr.Less), Scr.Int(15_900))),
                Scr.Script("Bump", [Scr.And(Scr.If("FLAG", Scr.Flag("deployed"), Scr.Bool(true)))],
                    [Scr.Do("INCREMENT_COUNTER", Scr.Int(60), Scr.Counter("Convoy")), Scr.Do("SET_FLAG", Scr.Flag("bumped"), Scr.Bool(true))], oneShot: true),
                RaiseWhen("Again", "again", Scr.If("FLAG", Scr.Flag("bumped"), Scr.Bool(true)), Scr.If("COUNTER", Scr.Counter("Convoy"), Scr.Compare(Scr.Less), Scr.Int(15_900))),
            ]);
            var deployed = TickThatRaised(session, "deployed", 1_000 * factor);
            // 16000 in frame 0, 16000 - k in frame k: below 15900 in frame 101.
            Check.That(deployed == 101 * factor, $"{rate}: the timer falls below the frame literal 101 frames of the original after it was set (got tick {deployed}).");
            var again = TickThatRaised(session, "again", 1_000 * factor);
            // 'Bump' adds 60 frames in frame 101 (15899 -> 15959): below 15900 again 60 frames later.
            Check.That(again == 161 * factor, $"{rate}: 60 frames added to the timer are 60 frames of the original (got tick {again}).");
        }
    }

    public static void TickRateScriptClockSequentialWait()
    {
        const int wait = 40;
        long begun30 = -1, waited30 = -1;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, _, _, _) = RatedMissionWithParts(rate, [
                Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("UNIT_EXECUTE_SEQUENTIAL_SCRIPT", Scr.Unit("Lone"), Scr.Script("Steps"))], oneShot: true),
                Scr.Script("Steps", [], [
                    Scr.Do("SET_FLAG", Scr.Flag("begun"), Scr.Bool(true)),
                    Scr.Do("UNIT_IDLE_FOR_FRAMECOUNT", Scr.Unit("Lone"), Scr.Int(wait)),
                    Scr.Do("SET_FLAG", Scr.Flag("waited"), Scr.Bool(true))], active: false),
            ], objects: [Placed("AlphaSoldier", 100m, 300m, "teamA", "Lone")]);
            var begun = TickThatRaised(session, "begun", 100 * factor);
            var waited = TickThatRaised(session, "waited", 400 * factor);
            Check.That(begun >= 0 && waited > begun && begun % factor == 0 && waited % factor == 0, $"{rate}: the sequence ran on frames of the original (begun {begun}, waited {waited}).");
            var frames = (waited - begun) / factor;
            // The wait is set in the update that ran the instruction and counted down in the next 40; the update after those finds it at 0 and goes on.
            Check.That(frames == wait + 1, $"{rate}: UNIT_IDLE_FOR_FRAMECOUNT {wait} holds the sequence for {wait} + 1 updates, one per frame of the original (got {frames}).");
            if (factor == 1) (begun30, waited30) = (begun, waited);
            else Check.That(begun == begun30 * factor && waited == waited30 * factor, $"{rate}: the same frames as at 30 Hz ({begun}/{waited} against {begun30}/{waited30}).");
        }
    }

    public static void TickRateScriptClockFrozenTimeKeepsTheFrame()
    {
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, fixture, terrain, map) = RatedMissionWithParts(rate, [
                Scr.Script("Freeze", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("FREEZE_TIME"), Scr.Do("SET_TIMER", Scr.Counter("hold"), Scr.Int(30))], oneShot: true),
                Scr.Script("Thaw", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("hold")))], [Scr.Do("UNFREEZE_TIME"), Scr.Do("SET_FLAG", Scr.Flag("thawed"), Scr.Bool(true))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Sleeper")]);
            session.Step();
            Check.That(session.Engine.TimeFrozen && session.World.Tick == 0, $"{rate}: the first update froze the world.");
            // Half way, between two frames of the original where the rate has such a place: save, restore, and let both run on.
            var half = 15 * factor + (factor - 1);
            for (var step = 0; step < half; step++) session.Step();
            var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(restored.StateHash() == session.StateHash(), $"{rate}: a mission frozen for {half} steps restores to the same state.");
            var steps = half;
            while (session.Engine.TimeFrozen && steps < 200 * factor)
            {
                session.Step();
                restored.Step();
                steps++;
                Check.That(restored.StateHash() == session.StateHash() && restored.Engine.TimeFrozen == session.Engine.TimeFrozen, $"{rate}: the restored frozen mission stays identical (step {steps}).");
            }
            // The timer is set in the first update and counted down in the 30 updates that follow, one per frame of the original although the world's tick stands still.
            Check.That(session.Engine.FlagValue("thawed") && steps == 30 * factor, $"{rate}: a timer of 30 frames lasts one second of steps while the world is frozen (got {steps} steps).");
            Check.That(session.World.Tick == 1, $"{rate}: the update that unfroze time let the world step again (tick {session.World.Tick}).");
        }
    }

    public static void TickRateScriptClockSnapshotBetweenFrames()
    {
        // Everything the engine counts is running when the snapshot is taken on a tick that begins no frame of the original: timers, an evaluation counter, a delayed
        // script, a sequential wait and a marching unit with an area edge ahead of it.
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, fixture, terrain, map) = RatedMissionWithParts(rate, [
                Scr.Script("Start", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                    Scr.Do("SET_TIMER", Scr.Counter("t"), Scr.Int(70)), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("s"), Scr.Real(3m)),
                    Scr.Do("UNIT_EXECUTE_SEQUENTIAL_SCRIPT", Scr.Unit("Lone"), Scr.Script("Steps")),
                    Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Runner"), Scr.Waypoint("Mid"))], oneShot: true),
                Scr.Script("Steps", [], [Scr.Do("UNIT_IDLE_FOR_FRAMECOUNT", Scr.Unit("Lone"), Scr.Int(50)), Scr.Do("SET_FLAG", Scr.Flag("waited"), Scr.Bool(true))], active: false),
                Scr.Script("Count", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("n"))]),
                Scr.Script("Slow", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("slow"))], delaySeconds: 1),
                RaiseWhen("Over", "over", Scr.If("TIMER_EXPIRED", Scr.Counter("t"))),
                RaiseWhen("Left", "left", Scr.If("NAMED_EXITED_AREA", Scr.Unit("Runner"), Scr.Area("LeftArea"))),
                Scr.Script("Win", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("s")))], [Scr.Do("VICTORY")], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 300m, "teamA", "Lone"), Placed("AlphaSoldier", 150m, 200m, "teamA", "Runner")]);
            var between = 20 * factor + (factor - 1);
            for (var step = 0; step < between; step++) session.Step();
            Check.That(factor == 1 || !session.World.Time.IsOriginalFrameBoundary(session.World.Tick), $"{rate}: the snapshot is taken between two frames of the original (tick {session.World.Tick}).");
            var bytes = MissionSnapshot.Write(session);
            var restored = MissionSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain, map);
            Check.That(restored.StateHash() == session.StateHash() && MissionSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), $"{rate}: the state written between two frames restores byte for byte.");
            var steps = 0;
            while (!session.IsOver && steps < 1_000 * factor)
            {
                session.Step();
                restored.Step();
                steps++;
                if (restored.StateHash() != session.StateHash()) Check.That(false, $"{rate}: the restored mission left the original at tick {session.World.Tick}.");
            }
            var engine = restored.Engine;
            Check.That(engine.IsEnded && engine.Outcome == MissionOutcome.Victory && engine.FlagValue("over") && engine.FlagValue("waited") && engine.FlagValue("left"),
                $"{rate}: timers, the sequential wait and the area edge all came about in the restored mission.");
            // VICTORY in frame 90 (3 s), the end 120 frames later; the engine's own tick of the outcome is a frame of the original.
            Check.That(engine.OutcomeTick == 90 * factor && restored.World.Tick == (90 + ScriptEngine.EndGameDelayFrames) * factor + TickOrder.Next,
                $"{rate}: victory in frame 90 of the original, the end {ScriptEngine.EndGameDelayFrames} frames later (outcome tick {engine.OutcomeTick}, end tick {restored.World.Tick}).");
        }
    }
}
