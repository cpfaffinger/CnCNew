namespace CnCNew.W3D;

/// <summary>RenderObjClass::AnimMode (ANIM_MODE_*).</summary>
public enum W3dAnimationMode
{
    Manual,
    Loop,
    Once,
    LoopPingPong,
    LoopBackwards,
    OnceBackwards,
}

/// <summary>
/// Stateless mapping from elapsed time to an animation frame. The original advances the frame incrementally
/// (Animatable3DObjClass::Compute_Current_Frame); for monotonic time without per-step overshoot of a whole period the
/// closed forms below give the same frame. As in the original, the playable range is 0..FrameCount-1 and a loop
/// wraps after FrameCount-1 frames.
/// </summary>
public static class W3dAnimationClock
{
    public static float GetFrame(
        W3dAnimationMode mode,
        int frameCount,
        int frameRate,
        double elapsedSeconds,
        double speedMultiplier = 1.0,
        float manualFrame = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), elapsedSeconds, "Elapsed time must be finite and non-negative.");
        }

        if (!double.IsFinite(speedMultiplier) || speedMultiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMultiplier), speedMultiplier, "Speed must be finite and non-negative.");
        }

        var last = frameCount - 1;
        if (mode == W3dAnimationMode.Manual)
        {
            if (!float.IsFinite(manualFrame) || manualFrame < 0 || manualFrame > last)
            {
                throw new ArgumentOutOfRangeException(nameof(manualFrame), manualFrame, $"Manual frame must be in [0, {last}].");
            }

            return manualFrame;
        }

        if (last == 0)
        {
            return 0;
        }

        var advanced = elapsedSeconds * frameRate * speedMultiplier;
        var frame = mode switch
        {
            W3dAnimationMode.Once => Math.Min(advanced, last),
            W3dAnimationMode.OnceBackwards => Math.Max(last - advanced, 0),
            W3dAnimationMode.Loop => Wrap(advanced, last),
            W3dAnimationMode.LoopBackwards => last - Wrap(advanced, last),
            W3dAnimationMode.LoopPingPong => PingPong(advanced, last),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown animation mode."),
        };
        return (float)frame;
    }

    /// <summary>Animatable3DObjClass::Is_Animation_Complete: a ONCE animation rests on its last frame, ONCE_BACKWARDS on 0.</summary>
    public static bool IsComplete(W3dAnimationMode mode, int frameCount, float frame) => mode switch
    {
        W3dAnimationMode.Once => frame >= frameCount - 1,
        W3dAnimationMode.OnceBackwards => frame <= 0,
        _ => false,
    };

    private static double Wrap(double value, int period)
    {
        var wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }

    private static double PingPong(double value, int last)
    {
        var cycle = Wrap(value, 2 * last);
        return cycle <= last ? cycle : 2 * last - cycle;
    }
}
