using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Time;

/// <summary>
/// Subdividing a step of the original (phase 23, package "physics"). A recursion that the original runs once per logic frame – a speed under a factor, an
/// angular velocity under an acceleration, a fall under gravity – cannot be scaled to another step size without changing its result. So the 30 Hz step stays
/// the definition: the recursion runs once per frame of the original, in units per frame, and only what it moves (a position, an angle) advances every tick
/// by its share of the frame's step. At every frame boundary the state is the 30 Hz state; in between it is the straight line between the two.
/// <para>
/// A frame here is the object's own: it starts with the update in which the motion began, not with the world's frame grid (<see cref="FramePhase"/>).
/// At 30 Hz a frame is one tick, every share is the whole step and the phase never leaves zero, so nothing here computes or stores anything new.
/// </para>
/// </summary>
public static class FramePhysics
{
    /// <summary>
    /// The share of the tick <paramref name="subTick"/> (1 … ticks per frame) of a step of one frame: <c>floor(step · k / n) − floor(step · (k − 1) / n)</c>
    /// on the raw value, so the shares of one frame add up to exactly <paramref name="frameStep"/>. 30 Hz: the step itself.
    /// </summary>
    public static Fixed FrameSlice(this SimulationTime time, Fixed frameStep, int subTick)
    {
        var ticks = time.TicksPerOriginalFrame;
        if (ticks == TickOrder.Next) return frameStep;
        // The supported factors are 2 and 4: the division is an arithmetic shift (rounds toward negative infinity for either sign).
        var shift = ticks >> 1;
        var raw = frameStep.Raw;
        return Fixed.FromRaw((checked(raw * subTick) >> shift) - (checked(raw * (subTick - TickOrder.Next)) >> shift));
    }

    /// <summary>
    /// The part of a step of one frame that the ticks 1 … <paramref name="subTick"/> have covered: <c>floor(step · k / n)</c>, the sum of their
    /// <see cref="FrameSlice"/>. 30 Hz: the step itself.
    /// </summary>
    public static Fixed FramePart(this SimulationTime time, Fixed frameStep, int subTick)
    {
        var ticks = time.TicksPerOriginalFrame;
        return ticks == TickOrder.Next ? frameStep : Fixed.FromRaw(checked(frameStep.Raw * subTick) >> (ticks >> 1));
    }

    /// <summary>A catalog value per tick (a speed the catalog scaled to the rate) back in units per frame of the original. 30 Hz: the value, untouched.</summary>
    public static Fixed PerTickAsPerOriginalFrame(this SimulationTime time, Fixed valuePerTick)
        => time.TicksPerOriginalFrame == TickOrder.Next ? valuePerTick : valuePerTick * time.TicksPerOriginalFrame;

    /// <summary>A catalog value per tick² (gravity) back in units per frame² of the original. 30 Hz: the value, untouched.</summary>
    public static Fixed PerTickSquaredAsPerOriginalFrameSquared(this SimulationTime time, Fixed valuePerTickSquared)
        => time.TicksPerOriginalFrame == TickOrder.Next ? valuePerTickSquared : valuePerTickSquared * ((long)time.TicksPerOriginalFrame * time.TicksPerOriginalFrame);
}

/// <summary>
/// Where an object is inside its own frame of the original: the ticks of the current frame that have already been taken, 0 … ticks per frame − 1. State of
/// the module that owns it; written only while it is not zero, which it always is at 30 Hz (snapshots and hashes of 30 Hz do not change).
/// </summary>
public struct FramePhase
{
    private int _taken;

    /// <summary>True between two frames: the next tick starts a frame. Always true at 30 Hz.</summary>
    public readonly bool AtBoundary => _taken == 0;

    /// <summary>Takes one tick and returns its number inside the frame, 1 … ticks per frame; the last one completes the frame. 30 Hz: always 1, which is both.</summary>
    public int Next(SimulationTime time)
    {
        var subTick = _taken + 1;
        _taken = subTick == time.TicksPerOriginalFrame ? 0 : subTick;
        return subTick;
    }

    /// <summary>Starts over: the next tick is the first of a frame.</summary>
    public void Reset() => _taken = 0;

    public readonly void Describe(IStateWriter writer, string name)
    {
        if (_taken != 0) writer.Int(name, _taken);
    }

    public void Load(IStateReader reader, string name) => _taken = reader.Has(name) ? checked((int)reader.Int(name)) : 0;
}
