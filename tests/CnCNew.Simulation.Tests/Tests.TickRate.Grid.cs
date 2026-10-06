using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Time;

/// <summary>
/// Phase 20, the one grid helper (<see cref="FrameGrid"/>). Stage 2 produced two helper files with the same purpose; they were merged into one name
/// per concept. These tests keep the replaced expressions as oracles and sweep them against the merged functions at 30, 60 and 120 Hz – the proof that
/// replacing one spelling by the other changed no call site – and pin the diagnostic probe as a pure observer.
/// </summary>
static partial class Tests
{
    private static readonly SimulationRate[] GridRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    /// <summary>
    /// <c>CountdownReload</c> (frames + 1, scaled, less one step) against the former <c>TickGrid.CountdownActingAfterZero</c> (ticks + ticks per frame − 1),
    /// and <c>DrawFrames</c> against the former <c>TickGrid.RandomTicks</c>: equal for every duration on the grid, which is what every call site passes
    /// (catalog durations, scaled frame counts, results of a draw). Off the grid the two countdown forms differ; the test pins that as the precondition.
    /// </summary>
    public static void TickRateGridMergedHelpersAreTheSameFunctions()
    {
        foreach (var rate in GridRates)
        {
            var time = rate.Time;
            var perFrame = time.TicksPerOriginalFrame;
            for (long frames = 0; frames <= 20_000; frames++)
            {
                var ticks = frames * perFrame;
                var former = ticks + (perFrame - TickOrder.Next);
                Check.That(time.CountdownReload(ticks) == former, $"{rate}: CountdownReload({ticks}) is {time.CountdownReload(ticks)}, the former start value was {former}.");
                // Period of a countdown "if (n > 0) { n--; return; } act; n = reload": reload + 1 updates = (frames + 1) logic frames at every rate.
                Check.That(time.CountdownReload(ticks) + 1 == (frames + 1) * perFrame, $"{rate}: a countdown over {frames} frames recurs every {frames + 1} frames.");
            }

            // "At least one frame": the method against both former spellings, on and off the grid, including zero and negative values.
            for (long ticks = -50; ticks <= 50_000; ticks++)
            {
                var value = time.AtLeastOneFrame(ticks);
                Check.That(value == Math.Max(ticks, time.TicksPerOriginalFrame) && value == Math.Max(ticks, time.OriginalFrames(1)), $"{rate}: AtLeastOneFrame({ticks}) is {value}.");
                var floor = time.FloorToFrame(ticks);
                var ceiling = time.CeilingToFrame(ticks);
                Check.That(floor % perFrame == 0 && floor <= ticks && ticks < floor + perFrame, $"{rate}: FloorToFrame({ticks}) is {floor}.");
                Check.That(ceiling % perFrame == 0 && ceiling >= ticks && ticks > ceiling - perFrame, $"{rate}: CeilingToFrame({ticks}) is {ceiling}.");
                Check.That(floor == time.OriginalFrames(time.ToOriginalFrames(ticks)), $"{rate}: FloorToFrame({ticks}) is the frame count scaled back.");
            }

            // The draw: same result and the same state of the stream afterwards as the former expression, over a sweep of ranges on the grid.
            var merged = new SageRandom(0x5EED_0001);
            var oracle = new SageRandom(0x5EED_0001);
            var left = new uint[6];
            var right = new uint[6];
            for (var low = 0; low <= 40; low++)
            {
                for (var span = 0; span <= 120; span += 3)
                {
                    long min = low * perFrame, max = (low + span) * perFrame;
                    var drawn = time.DrawFrames(merged, min, max);
                    var former = time.OriginalFrames(oracle.Range(checked((int)time.ToOriginalFrames(min)), checked((int)time.ToOriginalFrames(max))));
                    Check.That(drawn == former && drawn % perFrame == 0 && drawn >= min && drawn <= max, $"{rate}: DrawFrames({min}, {max}) gave {drawn}, the former expression {former}.");
                }
            }
            merged.CopyState(left);
            oracle.CopyState(right);
            Check.That(left.AsSpan().SequenceEqual(right), $"{rate}: the merged draw consumed the stream exactly like the former one.");
        }

        // The precondition, stated: off the grid the former countdown form kept the remainder, the merged one cuts it. No call site passes such a value
        // (the probe counts it as OffGrid if one ever does).
        var sixty = SimulationRate.Hz60.Time;
        Check.That(sixty.CountdownReload(7) == 7 && 7 + (sixty.TicksPerOriginalFrame - TickOrder.Next) == 8, "60 Hz: 7 ticks are 3 frames and a remainder; the merged countdown reloads 4 frames less a step.");

        // 30 Hz: every function is the bare former expression.
        var thirty = SimulationRate.Hz30.Time;
        var a = new SageRandom(77);
        var b = new SageRandom(77);
        for (long ticks = -20; ticks <= 20_000; ticks++)
        {
            Check.That(thirty.CountdownReload(ticks) == ticks && thirty.FloorToFrame(ticks) == ticks && thirty.CeilingToFrame(ticks) == ticks && thirty.AtLeastOneFrame(ticks) == Math.Max(ticks, 1),
                $"30 Hz: the grid functions are the identity (and max(ticks, 1)) at {ticks}.");
            if (ticks >= 0 && ticks % 7 == 0)
                Check.That(thirty.DrawFrames(a, ticks / 2, ticks) == b.Range((int)(ticks / 2), (int)ticks), $"30 Hz: DrawFrames is LogicRandom.Range at {ticks}.");
        }
    }

    /// <summary>
    /// The run-time conversions that were written as bare divisions until phase 20 (<c>value / world.Time.TicksPerSecondFixed</c>, <c>total / ticks</c>)
    /// are those divisions, and the probe only watches: with a probe attached every function returns what it returns without one, and the counters name
    /// what was lost.
    /// </summary>
    public static void TickRateProbeOnlyObserves()
    {
        foreach (var rate in GridRates)
        {
            var time = rate.Time;
            Check.That(time.Probe is null, $"{rate}: no probe is attached by default.");
            long[] raws = [0, 1, 2, 3, 7, 29, 30, 59, 60, 119, 120, 121, 65_535, 65_536, 1_000_000, 123_456_789, -1, -29, -30, -65_536, -987_654_321];
            var plain = raws.Select(raw => Fixed.FromRaw(raw)).Select(value => (time.PerSecondToPerTick(value), time.PerOriginalFrameToPerTick(value), time.PerOriginalFrameSquaredToPerTickSquared(value),
                time.SharePerTick(value, Fixed.FromInt(45)), time.AtLeastOneFrame(value.Raw), time.CountdownReload(value.Raw), time.ToOriginalFrames(value.Raw))).ToArray();
            for (var index = 0; index < raws.Length; index++)
            {
                var value = Fixed.FromRaw(raws[index]);
                Check.That(plain[index].Item1 == value / time.TicksPerSecondFixed && plain[index].Item4 == value / Fixed.FromInt(45), $"{rate}: per second and share per tick are the plain divisions at raw {raws[index]}.");
            }

            var probe = new SimulationTimeProbe();
            time.Probe = probe;
            try
            {
                var watched = raws.Select(raw => Fixed.FromRaw(raw)).Select(value => (time.PerSecondToPerTick(value), time.PerOriginalFrameToPerTick(value), time.PerOriginalFrameSquaredToPerTickSquared(value),
                    time.SharePerTick(value, Fixed.FromInt(45)), time.AtLeastOneFrame(value.Raw), time.CountdownReload(value.Raw), time.ToOriginalFrames(value.Raw))).ToArray();
                Check.That(watched.SequenceEqual(plain), $"{rate}: a probe changes no result.");
                _ = time.PerSecondToPerTick(0.5m);
                _ = time.PerSecondSquaredToPerTickSquared(0.001m);
            }
            finally { time.Probe = null; }

            long Hits(TimeProbeKind kind, string function) => probe.Sites.Where(pair => pair.Key.Kind == kind && pair.Key.Function == function).Sum(pair => pair.Value);
            // raw / rate is zero for 0 < |raw| < rate / 2 (Fixed divides to the nearest value): the counter must say exactly how many of the inputs that was.
            var expectedZero = raws.Count(raw => raw != 0 && (Fixed.FromRaw(raw) / time.TicksPerSecondFixed) == Fixed.Zero);
            Check.That(Hits(TimeProbeKind.RunTimeRoundedToZero, nameof(SimulationTime.PerSecondToPerTick)) == expectedZero && expectedZero > 0,
                $"{rate}: {expectedZero} per-second values round to zero per tick and the probe counted {Hits(TimeProbeKind.RunTimeRoundedToZero, nameof(SimulationTime.PerSecondToPerTick))}.");
            Check.That(probe.Calls[(TimeProbeKind.RunTimeRoundedToZero, nameof(SimulationTime.PerSecondToPerTick))] == raws.Length, $"{rate}: every call is counted.");
            Check.That(Hits(TimeProbeKind.MinimumOneFrame, nameof(FrameGrid.AtLeastOneFrame)) == raws.Count(raw => raw < time.TicksPerOriginalFrame), $"{rate}: the one-frame minimum is counted when it engages.");
            Check.That(Hits(TimeProbeKind.OffGrid, nameof(FrameGrid.CountdownReload)) == raws.Count(raw => raw % time.TicksPerOriginalFrame != 0), $"{rate}: durations off the grid are counted.");
            Check.That(probe.Sites.Keys.All(site => site.Line > 0 && site.File.EndsWith("Tests.TickRate.Grid.cs", StringComparison.Ordinal)), $"{rate}: a counter names the calling line.");
            var perSecond = probe.Quantisation[nameof(SimulationTime.PerSecondToPerTick)];
            Check.That(perSecond is { Calls: 1, NonZero: 1, RoundedToZero: 0 } && probe.Quantisation[nameof(SimulationTime.PerSecondSquaredToPerTickSquared)].Calls == 1, $"{rate}: catalog conversions are counted per function.");
        }

        // A value per second² that is lost entirely at 120 Hz: 0.1 / 14400 is below half a Fixed step.
        var fast = SimulationRate.Hz120.Time;
        var lost = new SimulationTimeProbe();
        fast.Probe = lost;
        try { Check.That(fast.PerSecondSquaredToPerTickSquared(0.1m) == Fixed.Zero, "120 Hz: 0.1 per second squared is zero per tick squared."); }
        finally { fast.Probe = null; }
        Check.That(lost.Quantisation[nameof(SimulationTime.PerSecondSquaredToPerTickSquared)] is { RoundedToZero: 1, AboveTenPercent: 1, AboveOnePercent: 1 }, "The probe counts the value as rounded to zero.");
    }
}
