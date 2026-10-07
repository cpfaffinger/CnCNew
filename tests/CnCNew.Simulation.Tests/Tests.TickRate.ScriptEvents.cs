using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Time;

/// <summary>
/// Phase 23: what the world does on the ticks between two updates of the script VM (its clock is the frame of the original) still reaches the scripts, on the same
/// frame at 30, 60 and 120 Hz.
/// </summary>
static partial class Tests
{
    public static void TickRateScriptClockUnitEmptiedLooksOneFrameBack()
    {
        // evaluateUnitHasEmptied: true in the evaluation after the one that still saw passengers ("frameNum - 1"). The engine evaluates once per frame of the original.
        long entered30 = -1;
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, _, _, _) = RatedMissionWithParts(rate, [
                Scr.Script("In", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_ENTER_NAMED", Scr.Unit("Hiker"), Scr.Unit("Bunker"))], oneShot: true),
                Scr.Script("Watch", [Scr.And(Scr.If("UNIT_EMPTIED", Scr.Unit("Bunker")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("emptied"))]),
                Scr.Script("Out", [Scr.And(Scr.If("FLAG", Scr.Flag("leave"), Scr.Bool(true)))], [Scr.Do("NAMED_EXIT_ALL", Scr.Unit("Bunker"))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 130m, 100m, "teamA", "Hiker"), Placed("AlphaBunker", 150m, 100m, "teamA", "Bunker")]);
            var bunker = NamedIn(session, "Bunker")!;
            var hiker = NamedIn(session, "Hiker")!;
            long entered = -1;
            for (var step = 0; step < 600 * factor && entered < 0; step++)
            {
                session.Step();
                if (hiker.ContainedBy == bunker.Id) entered = session.World.Tick;
            }
            Check.That(entered > 0, $"{rate}: the soldier entered the bunker.");
            if (factor == 1) entered30 = entered;
            else Check.That(Math.Abs(entered - entered30 * factor) <= 2 * factor, $"{rate}: he entered at about the same game time as at 30 Hz (tick {entered} against {entered30}).");
            for (var step = 0; step < 20 * factor; step++) session.Step();
            Check.That(session.Engine.CounterValue("emptied") == 0, $"{rate}: an occupied bunker has not emptied.");
            session.Engine.SignalUiInteraction("leave");
            for (var step = 0; step < 200 * factor; step++) session.Step();
            Check.That(!hiker.ContainedBy.IsValid, $"{rate}: the soldier left the bunker.");
            Check.That(session.Engine.CounterValue("emptied") == 1, $"{rate}: UNIT_EMPTIED held in exactly one evaluation, the one after the last that saw the passenger (got {session.Engine.CounterValue("emptied")}).");
        }
    }

    public static void TickRateScriptClockSeesWhatHappenedBetweenFrames()
    {
        // The host puts a named soldier into the area and later kills him, both on a tick that begins no frame of the original (where the rate has one). The edge
        // condition and the destruction are seen by the next update of the engine, once, on the same frame at every rate.
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, fixture, _, _) = RatedMissionWithParts(rate, [
                Scr.Script("In", [Scr.And(Scr.If("NAMED_ENTERED_AREA", Scr.Unit("Late"), Scr.Area("LeftArea")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("entered"))]),
                RaiseWhen("Gone", "gone", Scr.If("NAMED_DESTROYED", Scr.Unit("Late"))),
            ]);
            var engine = session.Engine;
            var between = factor - 1;
            for (var step = 0; step < 10 * factor + between; step++) session.Step();
            Check.That(fixture.Templates.TryGet("AlphaSoldier", out var soldier), "The fixture has the soldier.");
            var id = session.World.RequestSpawn(soldier.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(200), Fixed.Zero, Fixed.Zero);
            engine.RegisterNamed("Late", id);
            // Requested in frame 10 (plus the ticks between), in the world after that step: the update of frame 11 is the first to see him inside.
            long seen = -1;
            for (var step = 0; step < 5 * factor && seen < 0; step++)
            {
                var tick = session.World.Tick;
                session.Step();
                if (engine.CounterValue("entered") > 0) seen = tick;
            }
            Check.That(seen == 11 * factor, $"{rate}: the update of frame 11 of the original sees the soldier who appeared in the area after frame 10 (got tick {seen}).");
            while (session.World.Tick < 20 * factor + between) session.Step();
            Check.That(engine.CounterValue("entered") == 1, $"{rate}: the edge is seen once (got {engine.CounterValue("entered")}).");
            Check.That(session.World.Tick == 20 * factor + between, $"{rate}: setup - the kill falls {between} ticks after frame 20 begins (tick {session.World.Tick}).");
            session.World.Kill(id);
            var gone = TickThatRaised(session, "gone", 300 * factor);
            // Killed before the step of that tick: the first update at or after it notices - frame 20 where the kill falls on the frame, frame 21 where it falls behind it.
            var expected = (20 + Math.Min(1, between)) * factor;
            Check.That(gone == expected, $"{rate}: the destruction is noticed by the first update of the engine after it (tick {gone}, expected {expected}).");
        }
    }

    public static void TickRateScriptClockRemembersAnObjectThatLivedBetweenFrames()
    {
        // didUnitExist: a named object that appears after one update and is gone before the next has still existed, so NAMED_DESTROYED holds for it. At 30 Hz every
        // tick is an update; at 120 Hz three ticks lie between two.
        foreach (var (rate, factor) in ScriptingRates)
        {
            var (session, fixture, _, _) = RatedMissionWithParts(rate, [RaiseWhen("Gone", "gone", Scr.If("NAMED_DESTROYED", Scr.Unit("Brief")))]);
            for (var step = 0; step < 10 * factor + Math.Min(1, factor - 1); step++) session.Step();
            Check.That(fixture.Templates.TryGet("AlphaSoldier", out var soldier), "The fixture has the soldier.");
            var id = session.World.RequestSpawn(soldier.Index, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(200), Fixed.Zero, Fixed.Zero);
            session.Engine.RegisterNamed("Brief", id);
            session.Step();
            Check.That(session.World.FindEntity(id) is not null, $"{rate}: the object is in the world after the step that followed the request.");
            session.Step();
            session.World.RequestDestroy(id);
            session.Step();
            Check.That(session.World.FindEntity(id) is null, $"{rate}: and gone one step after the request to destroy it (tick {session.World.Tick}).");
            for (var step = 0; step < 10 * factor; step++) session.Step();
            Check.That(session.Engine.FlagValue("gone"), $"{rate}: an object that lived for two ticks counts as destroyed, not as never created.");
        }
    }
}
