namespace CnCNew.Simulation.Time;

/// <summary>
/// Further grid helpers (<see cref="FrameGrid"/>) for the "one tick where the original means one frame" cases of phase 23
/// (<c>docs/RATE-INVARIANCE-TIMING.md</c>). Like those of <see cref="FrameGrid"/> each one is the former expression at 30 Hz, and none is for a per-tick path.
/// </summary>
public static class FrameGridTiming
{
    /// <summary>
    /// The wait behind a deadline that is tested once per tick with <c>tick &gt;= deadline</c> by something that has already had its turn in this tick (a weapon that
    /// just fired, a state machine that just changed state). A wait of zero then means "at the next try", and the next try of the original is one logic frame
    /// later. At 30 Hz the next tick is that frame and zero stays zero, so stored deadlines keep their value; above 30 Hz zero becomes one logic frame of the
    /// original. A wait that is not zero is returned unchanged.
    /// </summary>
    public static long ZeroIsNextFrame(this SimulationTime time, long ticks)
        => ticks != 0 || time.TicksPerOriginalFrame == TickOrder.Next ? ticks : time.TicksPerOriginalFrame;
}
