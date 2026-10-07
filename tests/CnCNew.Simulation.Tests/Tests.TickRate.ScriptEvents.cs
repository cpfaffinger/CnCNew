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
}
