using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Time;

/// <summary>
/// The frame grid of things that fly (<c>docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md</c>): a projectile takes its decisions once per logic frame of the original, on its own phase (counted
/// from its launch), exactly as it does at 30 Hz, and the step such a frame decides is flown in equal shares over the ticks of that frame. At 30 Hz a frame is one tick: the share is the
/// step and every function returns its argument, which keeps 30 Hz bit-identical.
/// </summary>
public static class ProjectileFrameGrid
{
    /// <summary>
    /// A speed per tick from the catalog as the speed per logic frame of the original <em>the 30 Hz catalog holds</em>. The catalog rounds "units per second / rate" once
    /// (<see cref="SimulationTime.PerSecondToPerTick(decimal)"/>), so the per-tick value times the ticks per frame can differ from the 30 Hz value by the last bits – enough to move a
    /// <c>ceil(distance / speed)</c> by a whole frame when the distance is a multiple of the speed. The authored value has few decimals: the per-second value with at most four of them
    /// that reproduces the given per-tick value is found and rounded the way the 30 Hz catalog rounds it. A value that is not reproduced (more decimals, or not a catalog speed) is
    /// scaled by the whole factor instead. 30 Hz: the argument, without any arithmetic. Not for a per-tick path: it runs when a shot is fired.
    /// </summary>
    public static Fixed PerTickToPerOriginalFrame(this SimulationTime time, Fixed perTick)
    {
        var factor = time.TicksPerOriginalFrame;
        if (factor == 1) return perTick;
        var perSecond = decimal.Round(perTick.ToDecimal() * time.TicksPerSecond, AuthoredDecimals);
        if (Fixed.FromDecimal(perSecond / time.TicksPerSecond) != perTick) return perTick * factor;
        return Fixed.FromDecimal(perSecond / SimulationRate.OriginalFramesPerSecond);
    }

    private const int AuthoredDecimals = 4;

    /// <summary>
    /// An acceleration per tick² as the acceleration per logic frame² of the original: times the ticks per frame, twice (exact; the catalog value itself is rounded once per rate).
    /// 30 Hz: the argument.
    /// </summary>
    public static Fixed PerTickSquaredToPerOriginalFrameSquared(this SimulationTime time, Fixed perTickSquared)
    {
        var factor = time.TicksPerOriginalFrame;
        if (factor == 1) return perTickSquared;
        var perTickAndFrame = perTickSquared * factor;
        return perTickAndFrame * factor;
    }

    /// <summary>
    /// The share of a frame's step that sub-step <paramref name="subStep"/> (0 … ticks per frame − 1) flies: the step divided by the ticks per frame, and in the last sub-step what is
    /// left, so that the shares of one frame add up to the step exactly. 30 Hz: the step.
    /// </summary>
    public static Fixed FrameShare(this SimulationTime time, Fixed frameStep, int subStep)
    {
        var factor = time.TicksPerOriginalFrame;
        if (factor == 1) return frameStep;
        var share = frameStep / Fixed.FromInt(factor);
        return subStep < factor - 1 ? share : frameStep - share * (factor - 1);
    }

    /// <summary>The sub-step after <paramref name="subStep"/>: 0 again when the frame is flown. 30 Hz: always 0.</summary>
    public static int NextSubStep(this SimulationTime time, int subStep) => subStep + 1 >= time.TicksPerOriginalFrame ? 0 : subStep + 1;
}
